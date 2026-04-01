using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class BomCatalogService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public async Task<BomQueryResult> QueryAsync(BomQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var cacheKey = $"bom:{store.Version}:{normalized.Search}:{normalized.Coverage}:{normalized.SortBy}:{normalized.Descending}";

        if (cache.TryGetValue(cacheKey, out BomQueryResult? result))
        {
            return result!;
        }

        await Task.Delay(90, cancellationToken);

        result = BuildQueryResult(normalized);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));
        return result;
    }

    public Task<BomDetail?> GetDetailAsync(Guid productId, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildDetail(productId));

    public Task<BomMutationResult> SaveRecipeMetaAsync(Guid productId, RecipeMetaRequest request, CancellationToken cancellationToken = default)
    {
        var product = store.FindProduct(productId);
        if (product is null)
        {
            return Task.FromResult(new BomMutationResult(false, "Produk tidak ditemukan."));
        }

        if (request.BatchOutputQuantity <= 0)
        {
            return Task.FromResult(new BomMutationResult(false, "Output batch harus lebih besar dari 0."));
        }

        if (request.YieldPercentage <= 0 || request.YieldPercentage > 100)
        {
            return Task.FromResult(new BomMutationResult(false, "Yield harus di antara 0.1 sampai 100."));
        }

        var recipe = new ProductRecipe(productId, request.BatchOutputQuantity, request.YieldPercentage, request.Notes?.Trim(), DateTime.Now);
        store.UpsertRecipe(recipe);
        return Task.FromResult(new BomMutationResult(true, "Metadata resep berhasil disimpan."));
    }

    public Task<BomMutationResult> UpsertItemAsync(Guid productId, BomItemRequest request, CancellationToken cancellationToken = default)
    {
        var product = store.FindProduct(productId);
        if (product is null)
        {
            return Task.FromResult(new BomMutationResult(false, "Produk tidak ditemukan."));
        }

        if (request.MaterialId is null)
        {
            return Task.FromResult(new BomMutationResult(false, "Bahan baku wajib dipilih."));
        }

        var material = store.FindRawMaterial(request.MaterialId.Value);
        if (material is null)
        {
            return Task.FromResult(new BomMutationResult(false, "Bahan baku tidak ditemukan."));
        }

        if (request.QuantityPerUnit <= 0)
        {
            return Task.FromResult(new BomMutationResult(false, "Jumlah pemakaian harus lebih besar dari 0."));
        }

        store.UpsertBomItem(new BomItem(productId, material.Id, request.QuantityPerUnit));
        EnsureRecipeExists(productId);
        return Task.FromResult(new BomMutationResult(true, "Item BOM berhasil disimpan."));
    }

    public Task<BomMutationResult> RemoveItemAsync(Guid productId, Guid materialId, CancellationToken cancellationToken = default)
    {
        var removed = store.RemoveBomItem(productId, materialId);
        return Task.FromResult(new BomMutationResult(removed, removed ? "Item BOM dihapus." : "Item BOM tidak ditemukan."));
    }

    private void EnsureRecipeExists(Guid productId)
    {
        var recipe = store.FindRecipe(productId);
        if (recipe is null)
        {
            store.UpsertRecipe(new ProductRecipe(productId, 100m, 100m, "Resep dibuat otomatis saat item pertama ditambahkan.", DateTime.Now));
        }
    }

    private BomQueryResult BuildQueryResult(BomQuery query)
    {
        var rows = store.Products.Where(product => product.IsActive).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            rows = rows.Where(product =>
                product.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                product.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.Select(MapProductRow);

        list = query.Coverage switch
        {
            BomCoverageFilter.Configured => list.Where(item => item.HasRecipe),
            BomCoverageFilter.Missing => list.Where(item => !item.HasRecipe),
            _ => list
        };

        list = query.SortBy.ToLowerInvariant() switch
        {
            "code" => query.Descending ? list.OrderByDescending(item => item.ProductCode) : list.OrderBy(item => item.ProductCode),
            "name" => query.Descending ? list.OrderByDescending(item => item.ProductName) : list.OrderBy(item => item.ProductName),
            "category" => query.Descending ? list.OrderByDescending(item => item.Category) : list.OrderBy(item => item.Category),
            "materials" => query.Descending ? list.OrderByDescending(item => item.MaterialCount) : list.OrderBy(item => item.MaterialCount),
            "cost" => query.Descending ? list.OrderByDescending(item => item.DirectCostPerUnit) : list.OrderBy(item => item.DirectCostPerUnit),
            _ => query.Descending ? list.OrderByDescending(item => item.UpdatedAt) : list.OrderBy(item => item.UpdatedAt)
        };

        var items = list.ToArray();
        var configuredCount = items.Count(item => item.HasRecipe);
        var totalProducts = store.Products.Count(product => product.IsActive);
        var missingCount = Math.Max(0, totalProducts - configuredCount);
        var coverage = totalProducts == 0 ? 0 : (int)Math.Round((configuredCount / (decimal)totalProducts) * 100m);

        return new BomQueryResult(items, configuredCount, missingCount, coverage);
    }

    private BomProductListItem MapProductRow(Product product)
    {
        var recipe = store.FindRecipe(product.Id);
        var items = store.BomItems.Where(item => item.ProductId == product.Id).ToArray();
        var warnings = 0;

        if (items.Length == 0)
        {
            warnings++;
        }

        warnings += items.Count(item =>
        {
            var material = store.FindRawMaterial(item.MaterialId);
            return material is not null && material.Status != MaterialStatus.Active;
        });

        return new BomProductListItem(
            product.Id,
            product.Code,
            product.Name,
            product.Category,
            items.Length > 0,
            items.Length,
            CalculateDirectCostPerUnit(product.Id),
            recipe?.BatchOutputQuantity ?? 0m,
            recipe?.YieldPercentage ?? 0m,
            warnings,
            recipe?.UpdatedAt ?? product.UpdatedAt);
    }

    private BomDetail? BuildDetail(Guid productId)
    {
        var product = store.FindProduct(productId);
        if (product is null)
        {
            return null;
        }

        var recipe = store.FindRecipe(productId) ?? new ProductRecipe(productId, 100m, 100m, null, product.UpdatedAt);
        var items = store.BomItems
            .Where(item => item.ProductId == productId)
            .Select(item =>
            {
                var material = store.FindRawMaterial(item.MaterialId)!;
                var unitPrice = ResolveMaterialPrice(item.MaterialId);
                var lineCost = item.QuantityPerUnit * unitPrice;
                return new BomItemView(
                    material.Id,
                    material.Code,
                    material.Name,
                    material.BaseUnit,
                    material.Brand,
                    item.QuantityPerUnit,
                    unitPrice,
                    lineCost);
            })
            .OrderBy(item => item.MaterialName)
            .ToArray();

        var costPerUnit = items.Sum(item => item.LineCostPerUnit);
        var costPerBatch = recipe.BatchOutputQuantity <= 0 ? 0m : costPerUnit * recipe.BatchOutputQuantity;
        var alerts = BuildAlerts(productId, recipe, items);

        return new BomDetail(
            product,
            recipe,
            items,
            costPerUnit,
            costPerBatch,
            store.HasProduction(productId),
            alerts);
    }

    private string[] BuildAlerts(Guid productId, ProductRecipe recipe, IReadOnlyList<BomItemView> items)
    {
        var alerts = new List<string>();

        if (items.Count == 0)
        {
            alerts.Add("Produk ini belum memiliki item BOM.");
        }

        if (recipe.YieldPercentage < 90m)
        {
            alerts.Add($"Yield produk {recipe.YieldPercentage:0.#}% cukup rendah dan berpotensi menaikkan HPP.");
        }

        if (!store.HasProduction(productId))
        {
            alerts.Add("Belum ada riwayat produksi untuk memvalidasi resep ini.");
        }

        return alerts.Take(4).ToArray();
    }

    private decimal CalculateDirectCostPerUnit(Guid productId)
        => store.BomItems
            .Where(item => item.ProductId == productId)
            .Sum(item => item.QuantityPerUnit * ResolveMaterialPrice(item.MaterialId));

    private decimal ResolveMaterialPrice(Guid materialId)
        => store.MaterialPrices
            .Where(entry => entry.MaterialId == materialId)
            .OrderByDescending(entry => entry.EffectiveAt)
            .First()
            .PricePerUnit;

    private static BomQuery Normalize(BomQuery query)
        => query with
        {
            Search = query.Search?.Trim(),
            SortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "updated" : query.SortBy.Trim()
        };
}
