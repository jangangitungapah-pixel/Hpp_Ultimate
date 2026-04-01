using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class SellingPriceService(IMemoryCache cache, IBusinessDataStore store, HppCalculatorService hppCalculatorService)
{
    public async Task<SellingPriceSnapshot> GetSnapshotAsync(SellingPriceQuery query, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"selling-price:{store.Version}:{query.ProductId}:{query.BatchId}:{query.Preset}:{query.From}:{query.To}";

        if (cache.TryGetValue(cacheKey, out SellingPriceSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(80, cancellationToken);

        var hppSnapshot = await hppCalculatorService.GetSnapshotAsync(
            new HppCalculatorQuery(query.ProductId, query.BatchId, query.Preset, query.From, query.To),
            cancellationToken);

        snapshot = BuildSnapshot(hppSnapshot);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    private static SellingPriceSnapshot BuildSnapshot(HppCalculatorSnapshot hppSnapshot)
    {
        var hpp = hppSnapshot.Summary.HppPerUnit;
        var currentPrice = hppSnapshot.Product?.SellingPrice ?? 0m;
        var quantityBasis = hppSnapshot.Summary.OutputQuantity;
        var breakEven = hpp;
        var minimumSafe = ComputePriceByMargin(hpp, 10m);
        var healthy = ComputePriceByMargin(hpp, 25m);
        var premium = ComputePriceByMargin(hpp, 35m);
        var currentProfitUnit = currentPrice - hpp;
        var currentMargin = currentPrice <= 0 ? 0m : (currentProfitUnit / currentPrice) * 100m;
        var currentExpectedProfit = currentProfitUnit * quantityBasis;

        var summary = new SellingPriceSummary(
            hppSnapshot.Summary.BasisLabel,
            quantityBasis,
            hpp,
            currentPrice,
            breakEven,
            minimumSafe,
            healthy,
            premium,
            currentProfitUnit,
            currentMargin,
            currentExpectedProfit);

        var scenarios = new[]
        {
            CreateScenario("Harga saat ini", currentPrice, hpp, quantityBasis, "Posisi harga yang sedang dipakai.", currentProfitUnit >= 0 ? "neutral" : "danger"),
            CreateScenario("Harga aman", minimumSafe, hpp, quantityBasis, "Menjaga margin minimum 10%.", "warning"),
            CreateScenario("Harga sehat", healthy, hpp, quantityBasis, "Target margin 25% untuk operasi yang lebih longgar.", "success"),
            CreateScenario("Harga premium", premium, hpp, quantityBasis, "Ruang lebih besar untuk promo dan fee channel.", "accent")
        };

        var alerts = BuildAlerts(hppSnapshot, summary);

        return new SellingPriceSnapshot(
            hppSnapshot.Range,
            hppSnapshot.Product,
            hppSnapshot.Recipe,
            hppSnapshot.Summary,
            hppSnapshot.Products,
            hppSnapshot.Batches,
            summary,
            scenarios,
            alerts,
            DateTime.Now);
    }

    private static SellingPriceScenario CreateScenario(string label, decimal price, decimal hpp, decimal quantityBasis, string caption, string tone)
    {
        var profitPerUnit = price - hpp;
        var margin = price <= 0 ? 0m : (profitPerUnit / price) * 100m;
        return new SellingPriceScenario(label, price, profitPerUnit, margin, profitPerUnit * quantityBasis, caption, tone);
    }

    public static decimal ComputePriceByMargin(decimal hppPerUnit, decimal targetMarginPercent)
    {
        var marginFactor = 1m - (targetMarginPercent / 100m);
        if (hppPerUnit <= 0 || marginFactor <= 0)
        {
            return 0m;
        }

        return hppPerUnit / marginFactor;
    }

    private static IReadOnlyList<string> BuildAlerts(HppCalculatorSnapshot hppSnapshot, SellingPriceSummary summary)
    {
        var alerts = new List<string>();

        alerts.AddRange(hppSnapshot.Alerts.Take(3));

        if (summary.CurrentPrice <= 0)
        {
            alerts.Add("Produk ini belum memiliki harga jual aktif.");
        }
        else if (summary.CurrentPrice < summary.BreakEvenPrice)
        {
            alerts.Add($"Harga jual saat ini masih di bawah titik impas sebesar {FormatCurrency(summary.BreakEvenPrice - summary.CurrentPrice)} per unit.");
        }
        else if (summary.CurrentMarginPercentage < 10m)
        {
            alerts.Add($"Margin harga jual saat ini hanya {summary.CurrentMarginPercentage:0.0}% dan terlalu tipis.");
        }

        if (summary.HppPerUnit <= 0)
        {
            alerts.Add("HPP belum terbentuk penuh sehingga pricing masih indikatif.");
        }

        return alerts.Distinct().Take(5).ToArray();
    }

    private static string FormatCurrency(decimal value)
        => string.Create(new System.Globalization.CultureInfo("id-ID"), $"Rp {value:N0}");
}
