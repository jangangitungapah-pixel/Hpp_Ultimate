using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ProductionCostService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public async Task<ProductionCostQueryResult> QueryAsync(ProductionCostQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var range = DashboardService.ResolveRange(new DashboardFilter(normalized.Preset, normalized.From, normalized.To, normalized.ProductId), DateTime.Now);
        var cacheKey = $"production-costs:{store.Version}:{normalized.Search}:{normalized.ProductId}:{normalized.Preset}:{normalized.From}:{normalized.To}:{normalized.SortBy}:{normalized.Descending}";

        if (cache.TryGetValue(cacheKey, out ProductionCostQueryResult? result))
        {
            return result!;
        }

        await Task.Delay(100, cancellationToken);

        result = BuildQueryResult(normalized, range);
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));
        return result;
    }

    public Task<ProductionCostDetail?> GetDetailAsync(Guid batchId, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildDetail(batchId));

    public Task<ProductionCostMutationResult> SaveEntryAsync(ProductionCostEntryType type, ProductionCostEntryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.BatchId is null)
        {
            return Task.FromResult(new ProductionCostMutationResult(false, "Batch produksi wajib dipilih."));
        }

        var batch = store.FindBatch(request.BatchId.Value);
        if (batch is null)
        {
            return Task.FromResult(new ProductionCostMutationResult(false, "Batch produksi tidak ditemukan."));
        }

        if (request.Amount <= 0)
        {
            return Task.FromResult(new ProductionCostMutationResult(false, "Nominal biaya harus lebih besar dari 0."));
        }

        var note = string.IsNullOrWhiteSpace(request.Note)
            ? type == ProductionCostEntryType.Labor ? "Biaya tenaga kerja" : "Biaya overhead"
            : request.Note.Trim();

        var now = DateTime.Now;

        if (type == ProductionCostEntryType.Labor)
        {
            if (request.Id is null)
            {
                var created = new LaborCostEntry(Guid.NewGuid(), batch.Id, request.Amount, note, now);
                store.AddLaborCost(created);
                return Task.FromResult(new ProductionCostMutationResult(true, "Biaya tenaga kerja berhasil ditambahkan.", type, created.Id, batch.Id));
            }

            var existing = store.FindLaborCost(request.Id.Value);
            if (existing is null)
            {
                return Task.FromResult(new ProductionCostMutationResult(false, "Biaya tenaga kerja tidak ditemukan."));
            }

            var updated = existing with
            {
                BatchId = batch.Id,
                Amount = request.Amount,
                Note = note,
                UpdatedAt = now
            };

            store.UpdateLaborCost(updated);
            return Task.FromResult(new ProductionCostMutationResult(true, "Biaya tenaga kerja berhasil diperbarui.", type, updated.Id, batch.Id));
        }

        if (request.Id is null)
        {
            var created = new OverheadCostEntry(Guid.NewGuid(), batch.Id, request.Amount, note, now);
            store.AddOverheadCost(created);
            return Task.FromResult(new ProductionCostMutationResult(true, "Biaya overhead berhasil ditambahkan.", type, created.Id, batch.Id));
        }

        var current = store.FindOverheadCost(request.Id.Value);
        if (current is null)
        {
            return Task.FromResult(new ProductionCostMutationResult(false, "Biaya overhead tidak ditemukan."));
        }

        var edited = current with
        {
            BatchId = batch.Id,
            Amount = request.Amount,
            Note = note,
            UpdatedAt = now
        };

        store.UpdateOverheadCost(edited);
        return Task.FromResult(new ProductionCostMutationResult(true, "Biaya overhead berhasil diperbarui.", type, edited.Id, batch.Id));
    }

    public Task<ProductionCostMutationResult> DeleteEntryAsync(ProductionCostEntryType type, Guid entryId, CancellationToken cancellationToken = default)
    {
        if (type == ProductionCostEntryType.Labor)
        {
            var removed = store.RemoveLaborCost(entryId);
            return Task.FromResult(new ProductionCostMutationResult(removed, removed ? "Biaya tenaga kerja dihapus." : "Biaya tenaga kerja tidak ditemukan.", type, entryId));
        }

        var overheadRemoved = store.RemoveOverheadCost(entryId);
        return Task.FromResult(new ProductionCostMutationResult(overheadRemoved, overheadRemoved ? "Biaya overhead dihapus." : "Biaya overhead tidak ditemukan.", type, entryId));
    }

    private ProductionCostQueryResult BuildQueryResult(ProductionCostQuery query, DashboardRange range)
    {
        var rows = store.ProductionBatches
            .Where(batch => batch.ProducedAt >= range.Start && batch.ProducedAt < range.EndExclusive)
            .Where(batch => query.ProductId is null || batch.ProductId == query.ProductId)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            rows = rows.Where(batch =>
            {
                var product = store.FindProduct(batch.ProductId);
                return batch.BatchCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                    || (product?.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (product?.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }

        var mapped = rows.Select(MapBatchRow);
        mapped = query.SortBy.ToLowerInvariant() switch
        {
            "batch" => query.Descending ? mapped.OrderByDescending(item => item.BatchCode) : mapped.OrderBy(item => item.BatchCode),
            "product" => query.Descending ? mapped.OrderByDescending(item => item.ProductName) : mapped.OrderBy(item => item.ProductName),
            "qty" => query.Descending ? mapped.OrderByDescending(item => item.QuantityProduced) : mapped.OrderBy(item => item.QuantityProduced),
            "material" => query.Descending ? mapped.OrderByDescending(item => item.MaterialCost) : mapped.OrderBy(item => item.MaterialCost),
            "labor" => query.Descending ? mapped.OrderByDescending(item => item.LaborCost) : mapped.OrderBy(item => item.LaborCost),
            "overhead" => query.Descending ? mapped.OrderByDescending(item => item.OverheadCost) : mapped.OrderBy(item => item.OverheadCost),
            "total" => query.Descending ? mapped.OrderByDescending(item => item.TotalCost) : mapped.OrderBy(item => item.TotalCost),
            "unit" => query.Descending ? mapped.OrderByDescending(item => item.CostPerUnit) : mapped.OrderBy(item => item.CostPerUnit),
            _ => query.Descending ? mapped.OrderByDescending(item => item.ProducedAt) : mapped.OrderBy(item => item.ProducedAt)
        };

        var items = mapped.ToArray();
        var totalUnits = items.Sum(item => item.QuantityProduced);
        var totalCost = items.Sum(item => item.TotalCost);
        var summary = new ProductionCostSummary(
            items.Sum(item => item.MaterialCost),
            items.Sum(item => item.LaborCost),
            items.Sum(item => item.OverheadCost),
            totalCost,
            totalUnits,
            items.Length,
            totalUnits == 0 ? 0m : totalCost / totalUnits);

        var products = store.Products
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new ProductFilterOption(product.Id, product.Name))
            .ToArray();

        return new ProductionCostQueryResult(
            range,
            summary,
            items,
            products,
            BuildInsights(items));
    }

    private ProductionCostBatchListItem MapBatchRow(ProductionBatch batch)
    {
        var product = store.FindProduct(batch.ProductId)!;
        var materialCost = CalculateMaterialCost(batch);
        var laborCost = store.LaborCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var overheadCost = store.OverheadCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var totalCost = materialCost + laborCost + overheadCost;

        return new ProductionCostBatchListItem(
            batch.Id,
            batch.BatchCode,
            product.Id,
            product.Code,
            product.Name,
            batch.ProducedAt,
            batch.QuantityProduced,
            materialCost,
            laborCost,
            overheadCost,
            totalCost,
            batch.QuantityProduced == 0 ? 0m : totalCost / batch.QuantityProduced,
            laborCost <= 0,
            overheadCost <= 0);
    }

    private ProductionCostDetail? BuildDetail(Guid batchId)
    {
        var batch = store.FindBatch(batchId);
        if (batch is null)
        {
            return null;
        }

        var product = store.FindProduct(batch.ProductId)!;
        var materialCost = CalculateMaterialCost(batch);
        var laborEntries = store.LaborCosts
            .Where(item => item.BatchId == batchId)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new ProductionCostEntryView(item.Id, item.BatchId, item.Amount, item.Note, item.UpdatedAt))
            .ToArray();
        var overheadEntries = store.OverheadCosts
            .Where(item => item.BatchId == batchId)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new ProductionCostEntryView(item.Id, item.BatchId, item.Amount, item.Note, item.UpdatedAt))
            .ToArray();

        var laborCost = laborEntries.Sum(item => item.Amount);
        var overheadCost = overheadEntries.Sum(item => item.Amount);
        var totalCost = materialCost + laborCost + overheadCost;
        var revenue = product.SellingPrice * batch.QuantityProduced;
        var profit = revenue - totalCost;
        var margin = revenue <= 0 ? 0m : (profit / revenue) * 100m;

        return new ProductionCostDetail(
            batch,
            product,
            materialCost,
            laborCost,
            overheadCost,
            totalCost,
            batch.QuantityProduced == 0 ? 0m : totalCost / batch.QuantityProduced,
            revenue,
            profit,
            margin,
            laborEntries,
            overheadEntries,
            BuildAlerts(batch, product, materialCost, laborEntries, overheadEntries, profit, margin));
    }

    private IReadOnlyList<ProductionCostInsight> BuildInsights(IReadOnlyList<ProductionCostBatchListItem> items)
    {
        var insights = new List<ProductionCostInsight>();

        var missingEntries = items.Count(item => item.MissingLabor || item.MissingOverhead);
        if (missingEntries > 0)
        {
            insights.Add(new ProductionCostInsight("warning", "Ada batch dengan biaya belum lengkap", $"{missingEntries} batch belum punya input tenaga kerja atau overhead lengkap."));
        }

        var mostExpensive = items.OrderByDescending(item => item.CostPerUnit).FirstOrDefault();
        if (mostExpensive is not null)
        {
            insights.Add(new ProductionCostInsight("danger", "Biaya per unit tertinggi", $"{mostExpensive.ProductName} batch {mostExpensive.BatchCode} mencapai {FormatCurrency(mostExpensive.CostPerUnit)} per unit."));
        }

        var mostEfficient = items
            .Where(item => !item.MissingLabor && !item.MissingOverhead)
            .OrderBy(item => item.CostPerUnit)
            .FirstOrDefault();
        if (mostEfficient is not null)
        {
            insights.Add(new ProductionCostInsight("success", "Batch paling efisien", $"{mostEfficient.BatchCode} berjalan di {FormatCurrency(mostEfficient.CostPerUnit)} per unit."));
        }

        if (insights.Count == 0)
        {
            insights.Add(new ProductionCostInsight("success", "Belum ada warning biaya", "Semua batch dalam filter ini sudah memiliki struktur biaya yang stabil."));
        }

        return insights;
    }

    private IReadOnlyList<string> BuildAlerts(
        ProductionBatch batch,
        Product product,
        decimal materialCost,
        IReadOnlyList<ProductionCostEntryView> laborEntries,
        IReadOnlyList<ProductionCostEntryView> overheadEntries,
        decimal profit,
        decimal margin)
    {
        var alerts = new List<string>();

        if (materialCost <= 0)
        {
            alerts.Add("Batch ini belum memiliki komponen bahan baku dari BOM.");
        }

        if (laborEntries.Count == 0)
        {
            alerts.Add("Biaya tenaga kerja untuk batch ini belum diinput.");
        }

        if (overheadEntries.Count == 0)
        {
            alerts.Add("Biaya overhead untuk batch ini belum diinput.");
        }

        if (profit < 0)
        {
            alerts.Add($"Estimasi batch ini rugi {FormatCurrency(Math.Abs(profit))}.");
        }
        else if (margin < 10m)
        {
            alerts.Add($"Margin batch ini hanya {margin:0.0}% dan perlu evaluasi harga atau biaya.");
        }

        if (!store.HasBom(product.Id))
        {
            alerts.Add("Produk ini belum memiliki BOM lengkap sehingga biaya bahan bisa tidak akurat.");
        }

        return alerts.Take(4).ToArray();
    }

    private decimal CalculateMaterialCost(ProductionBatch batch)
        => store.BomItems
            .Where(item => item.ProductId == batch.ProductId)
            .Sum(item => item.QuantityPerUnit * batch.QuantityProduced * ResolveMaterialPrice(item.MaterialId, batch.ProducedAt));

    private decimal ResolveMaterialPrice(Guid materialId, DateTime at)
    {
        var price = store.MaterialPrices
            .Where(item => item.MaterialId == materialId && item.EffectiveAt <= at)
            .OrderByDescending(item => item.EffectiveAt)
            .FirstOrDefault();

        if (price is not null)
        {
            return price.PricePerUnit;
        }

        return store.MaterialPrices
            .Where(item => item.MaterialId == materialId)
            .OrderBy(item => item.EffectiveAt)
            .First()
            .PricePerUnit;
    }

    private static ProductionCostQuery Normalize(ProductionCostQuery query)
        => query with
        {
            Search = query.Search?.Trim(),
            SortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "date" : query.SortBy.Trim()
        };

    private static string FormatCurrency(decimal value)
        => string.Create(new System.Globalization.CultureInfo("id-ID"), $"Rp {value:N0}");
}
