namespace Hpp_Ultimate.Domain;

public enum HppCalculationBasis
{
    AggregatedPeriod,
    BatchActual,
    SimulatedRecipe
}

public sealed record HppCalculatorQuery(
    Guid? ProductId = null,
    Guid? BatchId = null,
    DashboardPeriodPreset Preset = DashboardPeriodPreset.ThisMonth,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record HppBatchOption(Guid Id, string BatchCode, DateTime ProducedAt, int QuantityProduced);

public sealed record HppMaterialBreakdownItem(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal QuantityPerUnit,
    decimal TotalQuantity,
    decimal UnitPrice,
    decimal LineCost);

public sealed record HppCostShareItem(string Label, decimal Amount, decimal Percentage, string Tone);

public sealed record HppBatchComparisonItem(
    Guid BatchId,
    string BatchCode,
    DateTime ProducedAt,
    int QuantityProduced,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    decimal HppPerUnit);

public sealed record HppSummary(
    HppCalculationBasis Basis,
    string BasisLabel,
    decimal OutputQuantity,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    decimal HppPerUnit,
    decimal SellingPrice,
    decimal ProfitPerUnit,
    decimal MarginPercentage);

public sealed record HppCalculatorSnapshot(
    DashboardRange Range,
    Product? Product,
    ProductRecipe? Recipe,
    HppSummary Summary,
    IReadOnlyList<ProductFilterOption> Products,
    IReadOnlyList<HppBatchOption> Batches,
    IReadOnlyList<HppMaterialBreakdownItem> Materials,
    IReadOnlyList<HppCostShareItem> CostShares,
    IReadOnlyList<HppBatchComparisonItem> RecentBatches,
    IReadOnlyList<string> Alerts,
    DateTime LastUpdated);
