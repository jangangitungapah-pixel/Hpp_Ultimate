namespace Hpp_Ultimate.Domain;

public sealed record ProductionHistoryQuery(
    string? Search = null,
    Guid? ProductId = null,
    DashboardPeriodPreset Preset = DashboardPeriodPreset.ThisMonth,
    DateOnly? From = null,
    DateOnly? To = null,
    string SortBy = "date",
    bool Descending = true);

public sealed record ProductionHistoryRow(
    Guid BatchId,
    string BatchCode,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    DateTime ProducedAt,
    int QuantityProduced,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    decimal HppPerUnit,
    decimal Revenue,
    decimal Profit,
    decimal MarginPercentage);

public sealed record ProductionHistoryMaterialItem(
    string MaterialCode,
    string MaterialName,
    string Unit,
    decimal QuantityPerUnit,
    decimal TotalQuantity,
    decimal UnitPrice,
    decimal LineCost);

public sealed record ProductionHistoryComparison(
    ProductionHistoryRow Baseline,
    ProductionHistoryRow Current,
    decimal DeltaQuantity,
    decimal DeltaTotalCost,
    decimal DeltaHppPerUnit,
    decimal DeltaMarginPercentage,
    IReadOnlyList<string> Notes);

public sealed record ProductionHistoryDetail(
    ProductionHistoryRow Current,
    IReadOnlyList<ProductionHistoryMaterialItem> Materials,
    IReadOnlyList<string> Notes,
    ProductionHistoryComparison? Comparison);

public sealed record ProductionHistorySnapshot(
    DashboardRange Range,
    IReadOnlyList<ProductFilterOption> Products,
    IReadOnlyList<ProductionHistoryRow> Rows,
    ProductionCostSummary Summary,
    IReadOnlyList<string> Insights,
    string ExportFileName,
    string ExportCsv,
    DateTime LastUpdated);
