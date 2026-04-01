using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ProductionHistoryService(IMemoryCache cache, IBusinessDataStore store)
{
    private static readonly CultureInfo IdCulture = new("id-ID");

    public async Task<ProductionHistorySnapshot> QueryAsync(ProductionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(query);
        var range = DashboardService.ResolveRange(new DashboardFilter(normalized.Preset, normalized.From, normalized.To, normalized.ProductId), DateTime.Now);
        var cacheKey = $"production-history:{store.Version}:{normalized.Search}:{normalized.ProductId}:{normalized.Preset}:{normalized.From}:{normalized.To}:{normalized.SortBy}:{normalized.Descending}";

        if (cache.TryGetValue(cacheKey, out ProductionHistorySnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(100, cancellationToken);

        snapshot = BuildSnapshot(normalized, range);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<ProductionHistoryDetail?> GetDetailAsync(Guid batchId, Guid? compareBatchId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(BuildDetail(batchId, compareBatchId));

    private ProductionHistorySnapshot BuildSnapshot(ProductionHistoryQuery query, DashboardRange range)
    {
        var rows = store.ProductionBatches
            .Where(batch => batch.ProducedAt >= range.Start && batch.ProducedAt < range.EndExclusive)
            .Where(batch => query.ProductId is null || batch.ProductId == query.ProductId)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            rows = rows.Where(batch =>
            {
                var product = store.Products.FirstOrDefault(item => item.Id == batch.ProductId);
                return batch.BatchCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                    || (product?.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (product?.Code.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ?? false);
            });
        }

        var mapped = rows.Select(MapRow);
        mapped = query.SortBy.ToLowerInvariant() switch
        {
            "batch" => query.Descending ? mapped.OrderByDescending(item => item.BatchCode) : mapped.OrderBy(item => item.BatchCode),
            "product" => query.Descending ? mapped.OrderByDescending(item => item.ProductName) : mapped.OrderBy(item => item.ProductName),
            "qty" => query.Descending ? mapped.OrderByDescending(item => item.QuantityProduced) : mapped.OrderBy(item => item.QuantityProduced),
            "total" => query.Descending ? mapped.OrderByDescending(item => item.TotalCost) : mapped.OrderBy(item => item.TotalCost),
            "hpp" => query.Descending ? mapped.OrderByDescending(item => item.HppPerUnit) : mapped.OrderBy(item => item.HppPerUnit),
            "profit" => query.Descending ? mapped.OrderByDescending(item => item.Profit) : mapped.OrderBy(item => item.Profit),
            _ => query.Descending ? mapped.OrderByDescending(item => item.ProducedAt) : mapped.OrderBy(item => item.ProducedAt)
        };

        var items = mapped.ToArray();
        var summary = new ProductionCostSummary(
            items.Sum(item => item.MaterialCost),
            items.Sum(item => item.LaborCost),
            items.Sum(item => item.OverheadCost),
            items.Sum(item => item.TotalCost),
            items.Sum(item => item.QuantityProduced),
            items.Length,
            items.Sum(item => item.QuantityProduced) == 0 ? 0m : items.Sum(item => item.TotalCost) / items.Sum(item => item.QuantityProduced));

        var products = store.Products
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ProductFilterOption(item.Id, item.Name))
            .ToArray();

        return new ProductionHistorySnapshot(
            range,
            products,
            items,
            summary,
            BuildInsights(items),
            $"riwayat-produksi-{DateTime.Now:yyyyMMdd-HHmm}",
            BuildCsv(items),
            DateTime.Now);
    }

    private ProductionHistoryDetail? BuildDetail(Guid batchId, Guid? compareBatchId)
    {
        var currentBatch = store.ProductionBatches.FirstOrDefault(item => item.Id == batchId);
        if (currentBatch is null)
        {
            return null;
        }

        var current = MapRow(currentBatch);
        var materials = store.BomItems
            .Where(item => item.ProductId == currentBatch.ProductId)
            .Select(item =>
            {
                var material = store.RawMaterials.First(raw => raw.Id == item.MaterialId);
                var unitPrice = ResolveMaterialPrice(item.MaterialId, currentBatch.ProducedAt);
                return new ProductionHistoryMaterialItem(
                    material.Code,
                    material.Name,
                    material.Unit,
                    item.QuantityPerUnit,
                    item.QuantityPerUnit * currentBatch.QuantityProduced,
                    unitPrice,
                    item.QuantityPerUnit * currentBatch.QuantityProduced * unitPrice);
            })
            .OrderBy(item => item.MaterialName)
            .ToArray();

        var notes = BuildCurrentNotes(current);
        var comparison = BuildComparison(current, compareBatchId);

        return new ProductionHistoryDetail(current, materials, notes, comparison);
    }

    private ProductionHistoryComparison? BuildComparison(ProductionHistoryRow current, Guid? compareBatchId)
    {
        if (compareBatchId is null)
        {
            return null;
        }

        var baselineBatch = store.ProductionBatches.FirstOrDefault(item => item.Id == compareBatchId.Value);
        if (baselineBatch is null || baselineBatch.Id == current.BatchId)
        {
            return null;
        }

        var baseline = MapRow(baselineBatch);
        var notes = new List<string>();

        if (current.HppPerUnit > baseline.HppPerUnit)
        {
            notes.Add($"HPP batch saat ini naik {FormatCurrency(current.HppPerUnit - baseline.HppPerUnit)} per unit.");
        }
        else if (current.HppPerUnit < baseline.HppPerUnit)
        {
            notes.Add($"HPP batch saat ini lebih efisien {FormatCurrency(baseline.HppPerUnit - current.HppPerUnit)} per unit.");
        }

        if (current.QuantityProduced != baseline.QuantityProduced)
        {
            notes.Add($"Output batch berubah {(current.QuantityProduced - baseline.QuantityProduced):N0} unit.");
        }

        if (current.OverheadCost > baseline.OverheadCost)
        {
            notes.Add("Overhead batch saat ini lebih tinggi dibanding batch pembanding.");
        }

        return new ProductionHistoryComparison(
            baseline,
            current,
            current.QuantityProduced - baseline.QuantityProduced,
            current.TotalCost - baseline.TotalCost,
            current.HppPerUnit - baseline.HppPerUnit,
            current.MarginPercentage - baseline.MarginPercentage,
            notes);
    }

    private ProductionHistoryRow MapRow(ProductionBatch batch)
    {
        var product = store.Products.First(item => item.Id == batch.ProductId);
        var materialCost = store.BomItems
            .Where(item => item.ProductId == batch.ProductId)
            .Sum(item => item.QuantityPerUnit * batch.QuantityProduced * ResolveMaterialPrice(item.MaterialId, batch.ProducedAt));
        var laborCost = store.LaborCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var overheadCost = store.OverheadCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var totalCost = materialCost + laborCost + overheadCost;
        var revenue = product.SellingPrice * batch.QuantityProduced;
        var profit = revenue - totalCost;
        var margin = revenue == 0 ? 0m : (profit / revenue) * 100m;

        return new ProductionHistoryRow(
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
            revenue,
            profit,
            margin);
    }

    private IReadOnlyList<string> BuildInsights(IReadOnlyList<ProductionHistoryRow> rows)
    {
        var insights = new List<string>();
        var expensive = rows.OrderByDescending(item => item.HppPerUnit).FirstOrDefault();
        if (expensive is not null)
        {
            insights.Add($"Batch {expensive.BatchCode} memiliki HPP tertinggi {FormatCurrency(expensive.HppPerUnit)} per unit.");
        }

        var mostProfitable = rows.OrderByDescending(item => item.Profit).FirstOrDefault();
        if (mostProfitable is not null)
        {
            insights.Add($"Batch {mostProfitable.BatchCode} memberi profit tertinggi {FormatCurrency(mostProfitable.Profit)}.");
        }

        var lowMargin = rows.Where(item => item.MarginPercentage < 10m).Take(2).ToArray();
        foreach (var row in lowMargin)
        {
            insights.Add($"Margin batch {row.BatchCode} hanya {row.MarginPercentage:0.0}%.");
        }

        if (insights.Count == 0)
        {
            insights.Add("Belum ada anomali batch pada filter ini.");
        }

        return insights.Take(4).ToArray();
    }

    private IReadOnlyList<string> BuildCurrentNotes(ProductionHistoryRow row)
    {
        var notes = new List<string>();

        if (row.MarginPercentage < 10m)
        {
            notes.Add("Margin batch ini tipis dan perlu review harga atau biaya.");
        }

        if (row.LaborCost <= 0)
        {
            notes.Add("Biaya tenaga kerja belum tercatat pada batch ini.");
        }

        if (row.OverheadCost <= 0)
        {
            notes.Add("Biaya overhead belum tercatat pada batch ini.");
        }

        if (row.MaterialCost > row.TotalCost * 0.6m)
        {
            notes.Add("Porsi biaya bahan baku mendominasi struktur biaya batch ini.");
        }

        if (notes.Count == 0)
        {
            notes.Add("Struktur biaya batch terlihat stabil.");
        }

        return notes;
    }

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

    private static string BuildCsv(IReadOnlyList<ProductionHistoryRow> rows)
    {
        var lines = new List<string>
        {
            "Batch,Tanggal,Kode Produk,Nama Produk,Qty,Bahan,Labor,Overhead,Total,HPP/Unit,Profit,Margin"
        };

        lines.AddRange(rows.Select(item =>
            Csv(
                item.BatchCode,
                item.ProducedAt.ToString("yyyy-MM-dd"),
                item.ProductCode,
                item.ProductName,
                item.QuantityProduced.ToString(CultureInfo.InvariantCulture),
                item.MaterialCost,
                item.LaborCost,
                item.OverheadCost,
                item.TotalCost,
                item.HppPerUnit,
                item.Profit,
                $"{item.MarginPercentage:0.0}%")));

        return string.Join(Environment.NewLine, lines);
    }

    private static string Csv(params object[] values)
        => string.Join(",", values.Select(value =>
        {
            var text = value switch
            {
                decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty
            };

            return $"\"{text.Replace("\"", "\"\"")}\"";
        }));

    private static ProductionHistoryQuery Normalize(ProductionHistoryQuery query)
        => query with
        {
            Search = query.Search?.Trim(),
            SortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "date" : query.SortBy.Trim()
        };

    private static string FormatCurrency(decimal value)
        => string.Create(IdCulture, $"Rp {value:N0}");
}
