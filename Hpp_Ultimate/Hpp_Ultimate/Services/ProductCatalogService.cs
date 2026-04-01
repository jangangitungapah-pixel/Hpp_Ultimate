using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ProductCatalogService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public static readonly string[] Units = ["pcs", "pack", "botol", "pouch", "kg", "gram", "liter", "ml"];

    public async Task<ProductQueryResult> QueryAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var cacheKey = $"products:{store.Version}:{normalized.Search}:{normalized.Category}:{normalized.Status}:{normalized.SortBy}:{normalized.Descending}:{normalized.Page}:{normalized.PageSize}";

        if (cache.TryGetValue(cacheKey, out ProductQueryResult? result))
        {
            return result!;
        }

        await Task.Delay(100, cancellationToken);

        result = BuildQueryResult(normalized);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));

        return result;
    }

    public Task<ProductDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildDetail(id));

    public Task<string> GenerateNextCodeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(store.GenerateNextProductCode());

    public Task<ProductMutationResult> CreateAsync(ProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request, null);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var now = DateTime.Now;
        var product = new Product(
            Guid.NewGuid(),
            request.Code.Trim().ToUpperInvariant(),
            request.Name.Trim(),
            request.Category.Trim(),
            request.Unit.Trim(),
            request.SellingPrice,
            request.Description.Trim(),
            request.Status,
            now,
            now);

        store.AddProduct(product);
        return Task.FromResult(new ProductMutationResult(true, "Produk berhasil ditambahkan.", product));
    }

    public Task<ProductMutationResult> UpdateAsync(Guid id, ProductUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var current = store.FindProduct(id);
        if (current is null)
        {
            return Task.FromResult(new ProductMutationResult(false, "Produk tidak ditemukan."));
        }

        var validation = ValidateRequest(request, id);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        var updated = current with
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Category = request.Category.Trim(),
            Unit = request.Unit.Trim(),
            SellingPrice = request.SellingPrice,
            Description = request.Description.Trim(),
            Status = request.Status,
            UpdatedAt = DateTime.Now
        };

        store.UpdateProduct(updated);
        return Task.FromResult(new ProductMutationResult(true, "Produk berhasil diperbarui.", updated));
    }

    public Task<ProductMutationResult> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = store.FindProduct(id);
        if (current is null)
        {
            return Task.FromResult(new ProductMutationResult(false, "Produk tidak ditemukan."));
        }

        var updated = current with
        {
            Status = ProductStatus.Inactive,
            UpdatedAt = DateTime.Now
        };

        store.UpdateProduct(updated);
        return Task.FromResult(new ProductMutationResult(true, "Produk dinonaktifkan.", updated, store.HasProduction(id)));
    }

    public async Task<ProductMutationResult> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var current = store.FindProduct(id);
        if (current is null)
        {
            return new ProductMutationResult(false, "Produk tidak ditemukan.");
        }

        var nextCode = await GenerateNextCodeAsync(cancellationToken);
        var now = DateTime.Now;
        var duplicate = current with
        {
            Id = Guid.NewGuid(),
            Code = nextCode,
            Name = $"{current.Name} Copy",
            CreatedAt = now,
            UpdatedAt = now,
            Status = ProductStatus.Active
        };

        store.AddProduct(duplicate);
        return new ProductMutationResult(true, "Produk berhasil diduplikasi.", duplicate);
    }

    private ProductQueryResult BuildQueryResult(ProductQuery query)
    {
        var rows = store.Products.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            rows = rows.Where(product =>
                product.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                product.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            rows = rows.Where(product => product.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Status is not null)
        {
            rows = rows.Where(product => product.Status == query.Status);
        }

        var mapped = rows.Select(MapToListItem);
        mapped = query.SortBy.ToLowerInvariant() switch
        {
            "code" => query.Descending ? mapped.OrderByDescending(item => item.Code) : mapped.OrderBy(item => item.Code),
            "name" => query.Descending ? mapped.OrderByDescending(item => item.Name) : mapped.OrderBy(item => item.Name),
            "category" => query.Descending ? mapped.OrderByDescending(item => item.Category) : mapped.OrderBy(item => item.Category),
            "unit" => query.Descending ? mapped.OrderByDescending(item => item.Unit) : mapped.OrderBy(item => item.Unit),
            "price" => query.Descending ? mapped.OrderByDescending(item => item.SellingPrice) : mapped.OrderBy(item => item.SellingPrice),
            "status" => query.Descending ? mapped.OrderByDescending(item => item.Status) : mapped.OrderBy(item => item.Status),
            _ => query.Descending ? mapped.OrderByDescending(item => item.UpdatedAt) : mapped.OrderBy(item => item.UpdatedAt)
        };

        var total = mapped.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 5, 25);
        var items = mapped.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return new ProductQueryResult(
            items,
            store.Products.Select(product => product.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).ToArray(),
            total,
            page,
            pageSize,
            store.GenerateNextProductCode());
    }

    private ProductDetail? BuildDetail(Guid id)
    {
        var product = store.FindProduct(id);
        if (product is null)
        {
            return null;
        }

        var bomItems = store.BomItems.Where(item => item.ProductId == id).ToArray();
        var materialMap = store.RawMaterials.ToDictionary(item => item.Id, item => item.Name);
        var batches = store.ProductionBatches.Where(item => item.ProductId == id).OrderByDescending(item => item.ProducedAt).ToArray();

        return new ProductDetail(
            product,
            bomItems.Length > 0,
            batches.Length > 0,
            CalculateLastHpp(id),
            batches.Length,
            bomItems.Length,
            bomItems.Select(item => materialMap.GetValueOrDefault(item.MaterialId, "Unknown")).Distinct().Take(6).ToArray(),
            batches.Take(5).Select(item => item.BatchCode).ToArray());
    }

    private ProductListItem MapToListItem(Product product)
        => new(
            product.Id,
            product.Code,
            product.Name,
            product.Category,
            product.Unit,
            product.SellingPrice,
            product.Status,
            product.UpdatedAt,
            store.HasBom(product.Id),
            store.HasProduction(product.Id),
            CalculateLastHpp(product.Id));

    private decimal? CalculateLastHpp(Guid productId)
    {
        var latestBatch = store.ProductionBatches
            .Where(item => item.ProductId == productId)
            .OrderByDescending(item => item.ProducedAt)
            .FirstOrDefault();

        if (latestBatch is null)
        {
            return null;
        }

        var bomItems = store.BomItems.Where(item => item.ProductId == productId).ToArray();
        if (bomItems.Length == 0 || latestBatch.QuantityProduced <= 0)
        {
            return null;
        }

        var materialCost = bomItems.Sum(item =>
        {
            var price = store.MaterialPrices
                .Where(entry => entry.MaterialId == item.MaterialId && entry.EffectiveAt <= latestBatch.ProducedAt)
                .OrderByDescending(entry => entry.EffectiveAt)
                .FirstOrDefault()
                ?? store.MaterialPrices.Where(entry => entry.MaterialId == item.MaterialId).OrderBy(entry => entry.EffectiveAt).First();

            return item.QuantityPerUnit * latestBatch.QuantityProduced * price.PricePerUnit;
        });

        var labor = store.LaborCosts.Where(item => item.BatchId == latestBatch.Id).Sum(item => item.Amount);
        var overhead = store.OverheadCosts.Where(item => item.BatchId == latestBatch.Id).Sum(item => item.Amount);

        return (materialCost + labor + overhead) / latestBatch.QuantityProduced;
    }

    private ProductMutationResult? ValidateRequest(ProductUpsertRequest request, Guid? existingId)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ProductMutationResult(false, "Nama produk wajib diisi.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return new ProductMutationResult(false, "Kode produk wajib diisi.");
        }

        if (store.ProductCodeExists(request.Code.Trim(), existingId))
        {
            return new ProductMutationResult(false, "Kode produk sudah dipakai.");
        }

        if (request.SellingPrice < 0)
        {
            return new ProductMutationResult(false, "Harga jual tidak boleh negatif.");
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            return new ProductMutationResult(false, "Kategori wajib diisi.");
        }

        if (string.IsNullOrWhiteSpace(request.Unit))
        {
            return new ProductMutationResult(false, "Satuan wajib diisi.");
        }

        return null;
    }

    private static ProductQuery Normalize(ProductQuery query)
        => query with
        {
            Search = query.Search?.Trim(),
            Category = query.Category?.Trim(),
            SortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "updated" : query.SortBy.Trim(),
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 5, 25)
        };
}
