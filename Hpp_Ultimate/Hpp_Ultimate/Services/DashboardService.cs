using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class DashboardService(IMemoryCache cache, IBusinessDataStore store)
{
    private static readonly CultureInfo IdCulture = new("id-ID");

    public async Task<DashboardSnapshot> GetSnapshotAsync(DashboardFilter filter, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(filter, DateTime.Now);
        var cacheKey = $"dashboard:{store.Version}:{range.Start:yyyyMMdd}:{range.EndExclusive:yyyyMMdd}:{filter.ProductId}";

        if (cache.TryGetValue(cacheKey, out DashboardSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(140, cancellationToken);

        snapshot = BuildSnapshot(filter, range);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(25));

        return snapshot;
    }

    public DashboardSnapshot GetSnapshot(DashboardFilter filter)
        => BuildSnapshot(filter, ResolveRange(filter, DateTime.Now));

    private DashboardSnapshot BuildSnapshot(DashboardFilter filter, DashboardRange range)
    {
        var activeProducts = store.Products.Where(product => product.IsActive).OrderBy(product => product.Name).ToArray();

        var filteredBatches = store.ProductionBatches
            .Where(batch => batch.ProducedAt >= range.Start && batch.ProducedAt < range.EndExclusive)
            .Where(batch => filter.ProductId is null || batch.ProductId == filter.ProductId)
            .OrderBy(batch => batch.ProducedAt)
            .ToArray();

        var batchFinancials = filteredBatches
            .Select(batch => BuildBatchFinancial(batch))
            .ToArray();

        var current = Aggregate(batchFinancials);
        var previousRange = new DashboardRange(range.Start - (range.EndExclusive - range.Start), range.Start, $"Prev {range.Label}");
        var previous = Aggregate(
            store.ProductionBatches
                .Where(batch => batch.ProducedAt >= previousRange.Start && batch.ProducedAt < previousRange.EndExclusive)
                .Where(batch => filter.ProductId is null || batch.ProductId == filter.ProductId)
                .Select(BuildBatchFinancial)
                .ToArray());

        var productRows = batchFinancials
            .GroupBy(item => item.Product.Id)
            .Select(group =>
            {
                var totalUnits = group.Sum(item => item.Batch.QuantityProduced);
                var totalCost = group.Sum(item => item.TotalCost);
                var revenue = group.Sum(item => item.Revenue);
                var profit = revenue - totalCost;
                var hppPerUnit = totalUnits == 0 ? 0m : totalCost / totalUnits;
                var profitPerUnit = totalUnits == 0 ? 0m : profit / totalUnits;
                var margin = revenue == 0 ? 0m : (profit / revenue) * 100m;

                return new ProductSummaryRow(
                    group.First().Product.Id,
                    group.First().Product.Name,
                    totalUnits,
                    hppPerUnit,
                    group.First().Product.SellingPrice,
                    profitPerUnit,
                    profit,
                    margin);
            })
            .OrderByDescending(row => row.TotalProfit)
            .ToArray();

        var metrics = BuildMetrics(current, previous, productRows);
        var costBreakdown = BuildCostBreakdown(current);
        var productionSeries = BuildProductionSeries(batchFinancials, range);
        var topByVolume = productRows
            .OrderByDescending(row => row.TotalProduction)
            .Take(5)
            .Select(row => new TopProductItem(row.ProductName, row.TotalProduction, $"{row.TotalProduction:N0} unit"))
            .ToArray();
        var topByProfit = productRows
            .OrderByDescending(row => row.TotalProfit)
            .Take(5)
            .Select(row => new TopProductItem(row.ProductName, row.TotalProfit, FormatCurrency(row.TotalProfit)))
            .ToArray();

        var activities = BuildActivities(range);
        var insights = BuildInsights(range, productRows);

        return new DashboardSnapshot(
            range,
            metrics,
            costBreakdown,
            productionSeries,
            topByVolume,
            topByProfit,
            productRows,
            activities,
            insights,
            activeProducts.Select(product => new ProductFilterOption(product.Id, product.Name)).ToArray(),
            DateTime.Now);
    }

    private BatchFinancial BuildBatchFinancial(ProductionBatch batch)
    {
        var product = store.Products.First(item => item.Id == batch.ProductId);
        var bomItems = store.BomItems.Where(item => item.ProductId == batch.ProductId).ToArray();

        var materialCost = bomItems.Sum(item =>
        {
            var unitPrice = ResolveMaterialPrice(item.MaterialId, batch.ProducedAt);
            return item.QuantityPerUnit * batch.QuantityProduced * unitPrice;
        });

        var laborCost = store.LaborCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var overheadCost = store.OverheadCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var totalCost = materialCost + laborCost + overheadCost;
        var revenue = product.SellingPrice * batch.QuantityProduced;

        return new BatchFinancial(batch, product, materialCost, laborCost, overheadCost, totalCost, revenue);
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

    private static DashboardMetric[] BuildMetrics(AggregateMetrics current, AggregateMetrics previous, IReadOnlyList<ProductSummaryRow> productRows)
    {
        var avgHpp = productRows.Count == 0 ? 0m : productRows.Average(row => row.HppPerUnit);
        var prevAvgHpp = previous.ProductHpps.Count == 0 ? 0m : previous.ProductHpps.Average();
        var marginValue = current.Revenue == 0 ? 0m : (current.Profit / current.Revenue) * 100m;
        var previousMargin = previous.Revenue == 0 ? 0m : (previous.Profit / previous.Revenue) * 100m;

        return
        [
            CreateMetric("Total Biaya Produksi", current.TotalCost, previous.TotalCost, false, "Periode aktif", "cost"),
            CreateMetric("Total Produksi", current.TotalUnits, previous.TotalUnits, true, "Total unit batch", "neutral"),
            CreateMetric("Rata-rata HPP", avgHpp, prevAvgHpp, false, "Rata-rata semua produk", "warning"),
            CreateMetric("Estimasi Revenue", current.Revenue, previous.Revenue, true, "Harga jual x produksi", "neutral"),
            CreateMetric("Estimasi Profit", current.Profit, previous.Profit, true, "Revenue dikurangi biaya", current.Profit >= 0 ? "success" : "danger"),
            CreateMetric("Margin", marginValue, previousMargin, true, "Profit / revenue", marginValue >= 10m ? "success" : "warning", isPercentage: true)
        ];
    }

    private static DashboardMetric CreateMetric(string title, decimal current, decimal previous, bool higherIsBetter, string subtitle, string tone, bool isPercentage = false)
    {
        var value = isPercentage ? $"{current:0.0}%" : title.Contains("Produksi", StringComparison.OrdinalIgnoreCase) ? $"{current:N0} unit" : FormatValue(current);
        var (deltaText, trend) = BuildDeltaText(current, previous, higherIsBetter, isPercentage);
        return new DashboardMetric(title, value, subtitle, deltaText, trend, tone);
    }

    private static string FormatValue(decimal value)
        => value switch
        {
            _ when Math.Abs(value) >= 1000m => string.Create(IdCulture, $"Rp {value:N0}"),
            _ => string.Create(IdCulture, $"Rp {value:N2}")
        };

    private static (string Text, TrendDirection Trend) BuildDeltaText(decimal current, decimal previous, bool higherIsBetter, bool isPercentage)
    {
        if (previous == 0)
        {
            return ("vs periode sebelumnya", TrendDirection.Neutral);
        }

        var delta = ((current - previous) / previous) * 100m;
        var trend = delta == 0
            ? TrendDirection.Neutral
            : delta > 0
                ? (higherIsBetter ? TrendDirection.Up : TrendDirection.Down)
                : (higherIsBetter ? TrendDirection.Down : TrendDirection.Up);

        var prefix = delta >= 0 ? "+" : string.Empty;
        var suffix = isPercentage ? " pts" : "%";
        return ($"{prefix}{delta:0.0}{suffix} vs periode lalu", trend);
    }

    private static CostBreakdownItem[] BuildCostBreakdown(AggregateMetrics metrics)
    {
        var total = metrics.TotalCost == 0 ? 1m : metrics.TotalCost;

        return
        [
            new CostBreakdownItem("Bahan Baku", metrics.MaterialCost, (metrics.MaterialCost / total) * 100m, "accent"),
            new CostBreakdownItem("Tenaga Kerja", metrics.LaborCost, (metrics.LaborCost / total) * 100m, "ocean"),
            new CostBreakdownItem("Overhead", metrics.OverheadCost, (metrics.OverheadCost / total) * 100m, "warning")
        ];
    }

    private static TimeSeriesPoint[] BuildProductionSeries(IReadOnlyList<BatchFinancial> financials, DashboardRange range)
    {
        var totalDays = (range.EndExclusive - range.Start).TotalDays;

        if (totalDays <= 10)
        {
            return Enumerable.Range(0, (int)totalDays)
                .Select(index =>
                {
                    var day = range.Start.Date.AddDays(index);
                    var value = financials
                        .Where(item => item.Batch.ProducedAt.Date == day)
                        .Sum(item => item.Batch.QuantityProduced);
                    return new TimeSeriesPoint(day.ToString("dd MMM"), value);
                })
                .ToArray();
        }

        if (totalDays <= 45)
        {
            return financials
                .GroupBy(item => item.Batch.ProducedAt.Date)
                .OrderBy(group => group.Key)
                .Select(group => new TimeSeriesPoint(group.Key.ToString("dd MMM"), group.Sum(item => item.Batch.QuantityProduced)))
                .ToArray();
        }

        return financials
            .GroupBy(item => new DateTime(item.Batch.ProducedAt.Year, item.Batch.ProducedAt.Month, 1))
            .OrderBy(group => group.Key)
            .Select(group => new TimeSeriesPoint(group.Key.ToString("MMM yy"), group.Sum(item => item.Batch.QuantityProduced)))
            .ToArray();
    }

    private IReadOnlyList<ActivityItem> BuildActivities(DashboardRange range)
    {
        var productionActivities = store.ProductionBatches
            .Where(batch => batch.ProducedAt >= range.Start.AddDays(-7))
            .OrderByDescending(batch => batch.ProducedAt)
            .Take(3)
            .Select(batch =>
            {
                var product = store.Products.First(item => item.Id == batch.ProductId);
                return new ActivityItem(batch.ProducedAt, "Produksi", batch.BatchCode, $"{product.Name} menghasilkan {batch.QuantityProduced:N0} unit.");
            });

        var priceActivities = store.MaterialPrices
            .OrderByDescending(item => item.UpdatedAt)
            .Take(2)
            .Select(item =>
            {
                var material = store.RawMaterials.First(raw => raw.Id == item.MaterialId);
                return new ActivityItem(item.UpdatedAt, "Harga Bahan", material.Name, $"{FormatCurrency(item.PricePerUnit)} / {material.Unit}.");
            });

        var costActivities = store.OverheadCosts
            .OrderByDescending(item => item.UpdatedAt)
            .Take(2)
            .Select(item => new ActivityItem(item.UpdatedAt, "Biaya", item.Note, $"{FormatCurrency(item.Amount)} tercatat pada batch terkait."));

        return productionActivities
            .Concat(priceActivities)
            .Concat(costActivities)
            .OrderByDescending(item => item.Timestamp)
            .Take(6)
            .ToArray();
    }

    private IReadOnlyList<InsightItem> BuildInsights(DashboardRange range, IReadOnlyList<ProductSummaryRow> productRows)
    {
        var insights = new List<InsightItem>();

        var weekStart = range.EndExclusive.Date.AddDays(-7);
        var materialTrend = store.RawMaterials
            .Select(material =>
            {
                var current = store.MaterialPrices
                    .Where(item => item.MaterialId == material.Id && item.EffectiveAt <= range.EndExclusive)
                    .OrderByDescending(item => item.EffectiveAt)
                    .FirstOrDefault();
                var previous = store.MaterialPrices
                    .Where(item => item.MaterialId == material.Id && item.EffectiveAt < weekStart)
                    .OrderByDescending(item => item.EffectiveAt)
                    .FirstOrDefault();

                if (current is null || previous is null || previous.PricePerUnit == 0)
                {
                    return (Name: material.Name, Change: 0m);
                }

                return (Name: material.Name, Change: ((current.PricePerUnit - previous.PricePerUnit) / previous.PricePerUnit) * 100m);
            })
            .OrderByDescending(item => item.Change)
            .FirstOrDefault();

        if (materialTrend.Change > 10m)
        {
            insights.Add(new InsightItem(
                InsightSeverity.Danger,
                $"Biaya bahan {materialTrend.Name} naik {materialTrend.Change:0.0}%",
                "Periksa supplier atau revisi HPP periode aktif."));
        }

        foreach (var lowMargin in productRows.Where(row => row.MarginPercentage < 10m).Take(2))
        {
            insights.Add(new InsightItem(
                InsightSeverity.Warning,
                $"{lowMargin.ProductName} punya margin rendah",
                $"Margin saat ini {lowMargin.MarginPercentage:0.0}% dan perlu evaluasi harga jual."));
        }

        var best = productRows.OrderByDescending(row => row.TotalProfit).FirstOrDefault();
        if (best is not null)
        {
            insights.Add(new InsightItem(
                InsightSeverity.Success,
                $"{best.ProductName} paling menguntungkan",
                $"Total profit periode ini mencapai {FormatCurrency(best.TotalProfit)}."));
        }

        if (insights.Count == 0)
        {
            insights.Add(new InsightItem(InsightSeverity.Success, "Semua indikator stabil", "Belum ada warning signifikan pada periode terpilih."));
        }

        return insights.Take(4).ToArray();
    }

    private static AggregateMetrics Aggregate(IReadOnlyList<BatchFinancial> financials)
    {
        var totalUnits = financials.Sum(item => item.Batch.QuantityProduced);
        var totalCost = financials.Sum(item => item.TotalCost);
        var revenue = financials.Sum(item => item.Revenue);

        return new AggregateMetrics(
            financials.Sum(item => item.MaterialCost),
            financials.Sum(item => item.LaborCost),
            financials.Sum(item => item.OverheadCost),
            totalCost,
            totalUnits,
            revenue,
            revenue - totalCost,
            financials
                .GroupBy(item => item.Product.Id)
                .Select(group =>
                {
                    var units = group.Sum(item => item.Batch.QuantityProduced);
                    return units == 0 ? 0m : group.Sum(item => item.TotalCost) / units;
                })
                .ToArray());
    }

    public static DashboardRange ResolveRange(DashboardFilter filter, DateTime now)
    {
        var today = now.Date;

        return filter.Preset switch
        {
            DashboardPeriodPreset.Today => new DashboardRange(today, today.AddDays(1), "Hari ini"),
            DashboardPeriodPreset.ThisWeek => new DashboardRange(StartOfWeek(today), StartOfWeek(today).AddDays(7), "Minggu ini"),
            DashboardPeriodPreset.ThisMonth => new DashboardRange(new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1), "Bulan ini"),
            DashboardPeriodPreset.Custom => ResolveCustomRange(filter, today),
            _ => new DashboardRange(new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1), "Bulan ini")
        };
    }

    private static DashboardRange ResolveCustomRange(DashboardFilter filter, DateTime today)
    {
        var from = filter.From?.ToDateTime(TimeOnly.MinValue) ?? today.AddDays(-29);
        var to = filter.To?.ToDateTime(TimeOnly.MinValue) ?? today;

        if (to < from)
        {
            (from, to) = (to, from);
        }

        return new DashboardRange(from.Date, to.Date.AddDays(1), $"{from:dd MMM} - {to:dd MMM}");
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static string FormatCurrency(decimal value)
        => string.Create(IdCulture, $"Rp {value:N0}");

    private sealed record BatchFinancial(
        ProductionBatch Batch,
        Product Product,
        decimal MaterialCost,
        decimal LaborCost,
        decimal OverheadCost,
        decimal TotalCost,
        decimal Revenue);

    private sealed record AggregateMetrics(
        decimal MaterialCost,
        decimal LaborCost,
        decimal OverheadCost,
        decimal TotalCost,
        int TotalUnits,
        decimal Revenue,
        decimal Profit,
        IReadOnlyList<decimal> ProductHpps);
}
