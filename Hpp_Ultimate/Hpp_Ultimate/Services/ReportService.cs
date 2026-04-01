using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ReportService(IMemoryCache cache, IBusinessDataStore store)
{
    private static readonly CultureInfo IdCulture = new("id-ID");

    public async Task<ReportSnapshot> GetSnapshotAsync(ReportsQuery query, CancellationToken cancellationToken = default)
    {
        var range = DashboardService.ResolveRange(new DashboardFilter(query.Preset, query.From, query.To, query.ProductId), DateTime.Now);
        var cacheKey = $"reports:{store.Version}:{query.ProductId}:{query.Preset}:{query.From}:{query.To}:{query.Kind}";

        if (cache.TryGetValue(cacheKey, out ReportSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(110, cancellationToken);

        snapshot = BuildSnapshot(query, range);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    private ReportSnapshot BuildSnapshot(ReportsQuery query, DashboardRange range)
    {
        var products = store.Products
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ProductFilterOption(item.Id, item.Name))
            .ToArray();

        var batches = store.ProductionBatches
            .Where(batch => batch.ProducedAt >= range.Start && batch.ProducedAt < range.EndExclusive)
            .Where(batch => query.ProductId is null || batch.ProductId == query.ProductId)
            .OrderByDescending(batch => batch.ProducedAt)
            .ToArray();

        var financials = batches.Select(BuildBatchFinancial).ToArray();

        var hppRows = financials
            .GroupBy(item => item.Product.Id)
            .Select(group =>
            {
                var units = group.Sum(item => item.Batch.QuantityProduced);
                var totalCost = group.Sum(item => item.TotalCost);
                var revenue = group.Sum(item => item.Revenue);
                var profit = revenue - totalCost;
                var hppPerUnit = units == 0 ? 0m : totalCost / units;
                var profitPerUnit = units == 0 ? 0m : profit / units;
                var margin = revenue == 0 ? 0m : (profit / revenue) * 100m;
                var product = group.First().Product;

                return new HppReportRow(
                    product.Id,
                    product.Code,
                    product.Name,
                    product.Category,
                    units,
                    hppPerUnit,
                    product.SellingPrice,
                    profitPerUnit,
                    profit,
                    margin);
            })
            .OrderByDescending(item => item.TotalProfit)
            .ToArray();

        var productionRows = financials
            .Select(item => new ProductionReportRow(
                item.Batch.Id,
                item.Batch.BatchCode,
                item.Batch.ProducedAt,
                item.Product.Code,
                item.Product.Name,
                item.Batch.QuantityProduced,
                item.MaterialCost,
                item.LaborCost,
                item.OverheadCost,
                item.TotalCost,
                item.Batch.QuantityProduced <= 0 ? 0m : item.TotalCost / item.Batch.QuantityProduced))
            .ToArray();

        var profitRows = hppRows
            .Select(item => new ProfitLossReportRow(
                item.ProductId,
                item.ProductName,
                item.TotalProduction,
                item.SellingPrice * item.TotalProduction,
                item.HppPerUnit * item.TotalProduction,
                item.TotalProfit,
                item.MarginPercentage))
            .OrderByDescending(item => item.GrossProfit)
            .ToArray();

        var metrics = BuildMetrics(financials, hppRows);
        var insights = BuildInsights(hppRows, productionRows);
        var exportCsv = BuildCsv(query.Kind, hppRows, productionRows, profitRows);
        var exportHtml = BuildHtml(query.Kind, range, hppRows, productionRows, profitRows);
        var exportFileName = $"laporan-{query.Kind.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmm}";

        return new ReportSnapshot(
            range,
            query.Kind,
            products,
            metrics,
            hppRows,
            productionRows,
            profitRows,
            insights,
            exportFileName,
            exportCsv,
            exportHtml,
            DateTime.Now);
    }

    private BatchFinancial BuildBatchFinancial(ProductionBatch batch)
    {
        var product = store.Products.First(item => item.Id == batch.ProductId);
        var materialCost = store.BomItems
            .Where(item => item.ProductId == batch.ProductId)
            .Sum(item => item.QuantityPerUnit * batch.QuantityProduced * ResolveMaterialPrice(item.MaterialId, batch.ProducedAt));
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

    private static IReadOnlyList<ReportMetric> BuildMetrics(IReadOnlyList<BatchFinancial> financials, IReadOnlyList<HppReportRow> hppRows)
    {
        var units = financials.Sum(item => item.Batch.QuantityProduced);
        var totalCost = financials.Sum(item => item.TotalCost);
        var revenue = financials.Sum(item => item.Revenue);
        var profit = revenue - totalCost;
        var avgHpp = hppRows.Count == 0 ? 0m : hppRows.Average(item => item.HppPerUnit);

        return
        [
            new ReportMetric("Total produksi", $"{units:N0} unit", "Akumulasi semua batch pada periode ini.", "neutral"),
            new ReportMetric("Total biaya", FormatCurrency(totalCost), "Bahan baku, tenaga kerja, dan overhead.", "warning"),
            new ReportMetric("Rata-rata HPP", FormatCurrency(avgHpp), "Rata-rata HPP seluruh produk aktif.", "warning"),
            new ReportMetric("Estimasi revenue", FormatCurrency(revenue), "Harga jual dikali output produksi.", "neutral"),
            new ReportMetric("Laba kotor", FormatCurrency(profit), "Revenue dikurangi total biaya produksi.", profit >= 0 ? "success" : "danger")
        ];
    }

    private static IReadOnlyList<string> BuildInsights(IReadOnlyList<HppReportRow> hppRows, IReadOnlyList<ProductionReportRow> productionRows)
    {
        var insights = new List<string>();

        var lowMargin = hppRows.Where(item => item.MarginPercentage < 10m).Take(2).ToArray();
        foreach (var row in lowMargin)
        {
            insights.Add($"{row.ProductName} memiliki margin rendah {row.MarginPercentage:0.0}%.");
        }

        var best = hppRows.OrderByDescending(item => item.TotalProfit).FirstOrDefault();
        if (best is not null)
        {
            insights.Add($"{best.ProductName} memberi total profit tertinggi sebesar {FormatCurrency(best.TotalProfit)}.");
        }

        var expensiveBatch = productionRows.OrderByDescending(item => item.CostPerUnit).FirstOrDefault();
        if (expensiveBatch is not null)
        {
            insights.Add($"Batch {expensiveBatch.BatchCode} memiliki HPP tertinggi {FormatCurrency(expensiveBatch.CostPerUnit)} per unit.");
        }

        if (insights.Count == 0)
        {
            insights.Add("Belum ada insight signifikan untuk periode ini.");
        }

        return insights.Take(4).ToArray();
    }

    private static string BuildCsv(
        ReportKind kind,
        IReadOnlyList<HppReportRow> hppRows,
        IReadOnlyList<ProductionReportRow> productionRows,
        IReadOnlyList<ProfitLossReportRow> profitRows)
    {
        var lines = new List<string>();

        switch (kind)
        {
            case ReportKind.Production:
                lines.Add("Batch,Tanggal,Kode Produk,Nama Produk,Qty,Material,Labor,Overhead,Total,HPP/Unit");
                lines.AddRange(productionRows.Select(item =>
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
                        item.CostPerUnit)));
                break;
            case ReportKind.ProfitLoss:
                lines.Add("Produk,Total Produksi,Revenue,Total Biaya,Laba Kotor,Margin");
                lines.AddRange(profitRows.Select(item =>
                    Csv(
                        item.ProductName,
                        item.TotalProduction.ToString(CultureInfo.InvariantCulture),
                        item.Revenue,
                        item.TotalCost,
                        item.GrossProfit,
                        $"{item.MarginPercentage:0.0}%")));
                break;
            default:
                lines.Add("Kode Produk,Nama Produk,Kategori,Produksi,HPP/Unit,Harga Jual,Profit/Unit,Total Profit,Margin");
                lines.AddRange(hppRows.Select(item =>
                    Csv(
                        item.ProductCode,
                        item.ProductName,
                        item.Category,
                        item.TotalProduction.ToString(CultureInfo.InvariantCulture),
                        item.HppPerUnit,
                        item.SellingPrice,
                        item.ProfitPerUnit,
                        item.TotalProfit,
                        $"{item.MarginPercentage:0.0}%")));
                break;
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildHtml(
        ReportKind kind,
        DashboardRange range,
        IReadOnlyList<HppReportRow> hppRows,
        IReadOnlyList<ProductionReportRow> productionRows,
        IReadOnlyList<ProfitLossReportRow> profitRows)
    {
        var title = kind switch
        {
            ReportKind.Production => "Laporan Produksi",
            ReportKind.ProfitLoss => "Laporan Laba Rugi",
            _ => "Laporan HPP"
        };

        var rows = kind switch
        {
            ReportKind.Production => string.Join("", productionRows.Select(item =>
                $"<tr><td>{item.BatchCode}</td><td>{item.ProducedAt:dd MMM yyyy}</td><td>{item.ProductName}</td><td>{item.QuantityProduced:N0}</td><td>{FormatCurrency(item.TotalCost)}</td><td>{FormatCurrency(item.CostPerUnit)}</td></tr>")),
            ReportKind.ProfitLoss => string.Join("", profitRows.Select(item =>
                $"<tr><td>{item.ProductName}</td><td>{item.TotalProduction:N0}</td><td>{FormatCurrency(item.Revenue)}</td><td>{FormatCurrency(item.TotalCost)}</td><td>{FormatCurrency(item.GrossProfit)}</td><td>{item.MarginPercentage:0.0}%</td></tr>")),
            _ => string.Join("", hppRows.Select(item =>
                $"<tr><td>{item.ProductCode}</td><td>{item.ProductName}</td><td>{item.TotalProduction:N0}</td><td>{FormatCurrency(item.HppPerUnit)}</td><td>{FormatCurrency(item.TotalProfit)}</td><td>{item.MarginPercentage:0.0}%</td></tr>"))
        };

        return $$"""
<!doctype html>
<html lang="id">
<head>
<meta charset="utf-8" />
<title>{{title}}</title>
<style>
body { font-family: Arial, sans-serif; padding: 24px; color: #0f172a; }
h1 { margin-bottom: 6px; }
p { color: #475569; }
table { width: 100%; border-collapse: collapse; margin-top: 18px; }
th, td { border: 1px solid #cbd5e1; padding: 10px; text-align: left; }
th { background: #f8fafc; }
</style>
</head>
<body>
<h1>{{title}}</h1>
<p>Periode: {{range.Label}}</p>
<table>
<tbody>
{{rows}}
</tbody>
</table>
</body>
</html>
""";
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
}
