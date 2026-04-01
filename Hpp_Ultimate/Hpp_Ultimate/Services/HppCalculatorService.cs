using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class HppCalculatorService(IMemoryCache cache, IBusinessDataStore store)
{
    public async Task<HppCalculatorSnapshot> GetSnapshotAsync(HppCalculatorQuery query, CancellationToken cancellationToken = default)
    {
        var range = DashboardService.ResolveRange(new DashboardFilter(query.Preset, query.From, query.To, query.ProductId), DateTime.Now);
        var cacheKey = $"hpp-calculator:{store.Version}:{query.ProductId}:{query.BatchId}:{query.Preset}:{query.From}:{query.To}";

        if (cache.TryGetValue(cacheKey, out HppCalculatorSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(100, cancellationToken);

        snapshot = BuildSnapshot(query, range);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    private HppCalculatorSnapshot BuildSnapshot(HppCalculatorQuery query, DashboardRange range)
    {
        var products = store.Products
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new ProductFilterOption(product.Id, product.Name))
            .ToArray();

        var selectedProduct = ResolveProduct(query.ProductId, products);
        if (selectedProduct is null)
        {
            return new HppCalculatorSnapshot(
                range,
                null,
                null,
                new HppSummary(HppCalculationBasis.SimulatedRecipe, "Belum ada produk", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m),
                products,
                Array.Empty<HppBatchOption>(),
                Array.Empty<HppMaterialBreakdownItem>(),
                Array.Empty<HppCostShareItem>(),
                Array.Empty<HppBatchComparisonItem>(),
                ["Tambahkan produk aktif untuk mulai menghitung HPP."],
                DateTime.Now);
        }

        var recipe = store.ProductRecipes.FirstOrDefault(item => item.ProductId == selectedProduct.Id);
        var rangeBatches = store.ProductionBatches
            .Where(batch => batch.ProductId == selectedProduct.Id)
            .Where(batch => batch.ProducedAt >= range.Start && batch.ProducedAt < range.EndExclusive)
            .OrderByDescending(batch => batch.ProducedAt)
            .ToArray();
        var batchOptions = rangeBatches
            .Select(batch => new HppBatchOption(batch.Id, batch.BatchCode, batch.ProducedAt, batch.QuantityProduced))
            .ToArray();

        var selectedBatch = rangeBatches.FirstOrDefault(batch => batch.Id == query.BatchId);
        var basis = ResolveBasis(selectedProduct, recipe, range, rangeBatches, selectedBatch);

        var materials = store.BomItems
            .Where(item => item.ProductId == selectedProduct.Id)
            .Select(item =>
            {
                var material = store.RawMaterials.First(raw => raw.Id == item.MaterialId);
                var unitPrice = ResolveMaterialPrice(item.MaterialId, basis.ReferenceDate);
                var totalQuantity = item.QuantityPerUnit * basis.OutputQuantity;
                return new HppMaterialBreakdownItem(
                    material.Id,
                    material.Code,
                    material.Name,
                    material.BaseUnit,
                    item.QuantityPerUnit,
                    totalQuantity,
                    unitPrice,
                    totalQuantity * unitPrice);
            })
            .OrderBy(item => item.MaterialName)
            .ToArray();

        var materialCost = materials.Sum(item => item.LineCost);
        var totalCost = materialCost + basis.LaborCost + basis.OverheadCost;
        var hppPerUnit = basis.OutputQuantity <= 0 ? 0m : totalCost / basis.OutputQuantity;
        var profitPerUnit = selectedProduct.SellingPrice - hppPerUnit;
        var margin = selectedProduct.SellingPrice <= 0 ? 0m : (profitPerUnit / selectedProduct.SellingPrice) * 100m;

        var summary = new HppSummary(
            basis.Basis,
            basis.Label,
            basis.OutputQuantity,
            materialCost,
            basis.LaborCost,
            basis.OverheadCost,
            totalCost,
            hppPerUnit,
            selectedProduct.SellingPrice,
            profitPerUnit,
            margin);

        var costShares = BuildCostShares(materialCost, basis.LaborCost, basis.OverheadCost);
        var recentBatches = rangeBatches
            .OrderByDescending(batch => batch.ProducedAt)
            .Take(6)
            .Select(BuildComparisonItem)
            .ToArray();
        var alerts = BuildAlerts(selectedProduct, recipe, rangeBatches, selectedBatch, summary, materials);

        return new HppCalculatorSnapshot(
            range,
            selectedProduct,
            recipe,
            summary,
            products,
            batchOptions,
            materials,
            costShares,
            recentBatches,
            alerts,
            DateTime.Now);
    }

    private Product? ResolveProduct(Guid? requestedProductId, IReadOnlyList<ProductFilterOption> products)
    {
        var productId = requestedProductId ?? products.FirstOrDefault()?.Id;
        return productId is null ? null : store.Products.FirstOrDefault(item => item.Id == productId.Value && item.IsActive);
    }

    private BasisContext ResolveBasis(Product product, ProductRecipe? recipe, DashboardRange range, IReadOnlyList<ProductionBatch> rangeBatches, ProductionBatch? selectedBatch)
    {
        if (selectedBatch is not null)
        {
            return new BasisContext(
                HppCalculationBasis.BatchActual,
                $"Batch {selectedBatch.BatchCode}",
                selectedBatch.QuantityProduced,
                selectedBatch.ProducedAt,
                store.LaborCosts.Where(item => item.BatchId == selectedBatch.Id).Sum(item => item.Amount),
                store.OverheadCosts.Where(item => item.BatchId == selectedBatch.Id).Sum(item => item.Amount));
        }

        if (rangeBatches.Count > 0)
        {
            return new BasisContext(
                HppCalculationBasis.AggregatedPeriod,
                $"Agregasi {range.Label.ToLowerInvariant()}",
                rangeBatches.Sum(item => item.QuantityProduced),
                range.EndExclusive.AddTicks(-1),
                store.LaborCosts.Where(item => rangeBatches.Select(batch => batch.Id).Contains(item.BatchId)).Sum(item => item.Amount),
                store.OverheadCosts.Where(item => rangeBatches.Select(batch => batch.Id).Contains(item.BatchId)).Sum(item => item.Amount));
        }

        var outputFromRecipe = recipe is null ? 0m : recipe.BatchOutputQuantity * (recipe.YieldPercentage <= 0 ? 1m : recipe.YieldPercentage / 100m);
        var historicalBatches = store.ProductionBatches.Where(batch => batch.ProductId == product.Id).ToArray();
        var historicalUnits = historicalBatches.Sum(batch => batch.QuantityProduced);
        var historicalLabor = historicalBatches.Sum(batch => store.LaborCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount));
        var historicalOverhead = historicalBatches.Sum(batch => store.OverheadCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount));
        var laborPerUnit = historicalUnits == 0 ? 0m : historicalLabor / historicalUnits;
        var overheadPerUnit = historicalUnits == 0 ? 0m : historicalOverhead / historicalUnits;

        return new BasisContext(
            HppCalculationBasis.SimulatedRecipe,
            "Simulasi dari resep standar",
            outputFromRecipe,
            DateTime.Now,
            laborPerUnit * outputFromRecipe,
            overheadPerUnit * outputFromRecipe);
    }

    private HppBatchComparisonItem BuildComparisonItem(ProductionBatch batch)
    {
        var materialCost = store.BomItems
            .Where(item => item.ProductId == batch.ProductId)
            .Sum(item => item.QuantityPerUnit * batch.QuantityProduced * ResolveMaterialPrice(item.MaterialId, batch.ProducedAt));
        var laborCost = store.LaborCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var overheadCost = store.OverheadCosts.Where(item => item.BatchId == batch.Id).Sum(item => item.Amount);
        var totalCost = materialCost + laborCost + overheadCost;

        return new HppBatchComparisonItem(
            batch.Id,
            batch.BatchCode,
            batch.ProducedAt,
            batch.QuantityProduced,
            materialCost,
            laborCost,
            overheadCost,
            totalCost,
            batch.QuantityProduced <= 0 ? 0m : totalCost / batch.QuantityProduced);
    }

    private IReadOnlyList<string> BuildAlerts(
        Product product,
        ProductRecipe? recipe,
        IReadOnlyList<ProductionBatch> rangeBatches,
        ProductionBatch? selectedBatch,
        HppSummary summary,
        IReadOnlyList<HppMaterialBreakdownItem> materials)
    {
        var alerts = new List<string>();

        if (recipe is null)
        {
            alerts.Add("Produk ini belum memiliki metadata resep lengkap.");
        }

        if (materials.Count == 0)
        {
            alerts.Add("Produk ini belum punya item BOM sehingga biaya bahan belum akurat.");
        }

        if (selectedBatch is null && rangeBatches.Count == 0)
        {
            alerts.Add("Tidak ada batch produksi di periode ini, kalkulasi memakai simulasi resep standar.");
        }

        if (summary.ProfitPerUnit < 0)
        {
            alerts.Add($"Harga jual {product.Name} masih di bawah HPP sebesar {FormatCurrency(Math.Abs(summary.ProfitPerUnit))} per unit.");
        }
        else if (summary.MarginPercentage < 10m)
        {
            alerts.Add($"Margin {product.Name} hanya {summary.MarginPercentage:0.0}% dan rawan tergerus biaya.");
        }

        if (summary.LaborCost <= 0)
        {
            alerts.Add("Komponen tenaga kerja masih kosong pada basis kalkulasi saat ini.");
        }

        if (summary.OverheadCost <= 0)
        {
            alerts.Add("Komponen overhead masih kosong pada basis kalkulasi saat ini.");
        }

        return alerts.Take(5).ToArray();
    }

    private IReadOnlyList<HppCostShareItem> BuildCostShares(decimal materialCost, decimal laborCost, decimal overheadCost)
    {
        var total = materialCost + laborCost + overheadCost;
        var safeTotal = total <= 0 ? 1m : total;

        return
        [
            new HppCostShareItem("Bahan baku", materialCost, (materialCost / safeTotal) * 100m, "accent"),
            new HppCostShareItem("Tenaga kerja", laborCost, (laborCost / safeTotal) * 100m, "ocean"),
            new HppCostShareItem("Overhead", overheadCost, (overheadCost / safeTotal) * 100m, "warning")
        ];
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

    private static string FormatCurrency(decimal value)
        => string.Create(new System.Globalization.CultureInfo("id-ID"), $"Rp {value:N0}");

    private sealed record BasisContext(
        HppCalculationBasis Basis,
        string Label,
        decimal OutputQuantity,
        DateTime ReferenceDate,
        decimal LaborCost,
        decimal OverheadCost);
}
