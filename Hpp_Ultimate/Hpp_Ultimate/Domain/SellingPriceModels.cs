namespace Hpp_Ultimate.Domain;

public sealed record SellingPriceQuery(
    Guid? ProductId = null,
    Guid? BatchId = null,
    DashboardPeriodPreset Preset = DashboardPeriodPreset.ThisMonth,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record SellingPriceScenario(
    string Label,
    decimal Price,
    decimal ProfitPerUnit,
    decimal MarginPercentage,
    decimal ExpectedProfit,
    string Caption,
    string Tone);

public sealed record SellingPriceSummary(
    string BasisLabel,
    decimal QuantityBasis,
    decimal HppPerUnit,
    decimal CurrentPrice,
    decimal BreakEvenPrice,
    decimal MinimumSafePrice,
    decimal HealthyPrice,
    decimal PremiumPrice,
    decimal CurrentProfitPerUnit,
    decimal CurrentMarginPercentage,
    decimal CurrentExpectedProfit);

public sealed record SellingPriceSnapshot(
    DashboardRange Range,
    Product? Product,
    ProductRecipe? Recipe,
    HppSummary HppSummary,
    IReadOnlyList<ProductFilterOption> Products,
    IReadOnlyList<HppBatchOption> Batches,
    SellingPriceSummary Summary,
    IReadOnlyList<SellingPriceScenario> DefaultScenarios,
    IReadOnlyList<string> Alerts,
    DateTime LastUpdated);
