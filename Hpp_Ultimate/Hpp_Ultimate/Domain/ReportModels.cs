namespace Hpp_Ultimate.Domain;

public enum ReportKind
{
    Hpp,
    Production,
    ProfitLoss
}

public sealed record ReportsQuery(
    Guid? ProductId = null,
    DashboardPeriodPreset Preset = DashboardPeriodPreset.ThisMonth,
    DateOnly? From = null,
    DateOnly? To = null,
    ReportKind Kind = ReportKind.Hpp);

public sealed record ReportMetric(
    string Label,
    string Value,
    string Caption,
    string Tone);

public sealed record HppReportRow(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Category,
    int TotalProduction,
    decimal HppPerUnit,
    decimal SellingPrice,
    decimal ProfitPerUnit,
    decimal TotalProfit,
    decimal MarginPercentage);

public sealed record ProductionReportRow(
    Guid BatchId,
    string BatchCode,
    DateTime ProducedAt,
    string ProductCode,
    string ProductName,
    int QuantityProduced,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    decimal CostPerUnit);

public sealed record ProfitLossReportRow(
    Guid ProductId,
    string ProductName,
    int TotalProduction,
    decimal Revenue,
    decimal TotalCost,
    decimal GrossProfit,
    decimal MarginPercentage);

public sealed record ReportSnapshot(
    DashboardRange Range,
    ReportKind Kind,
    IReadOnlyList<ProductFilterOption> Products,
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<HppReportRow> HppRows,
    IReadOnlyList<ProductionReportRow> ProductionRows,
    IReadOnlyList<ProfitLossReportRow> ProfitLossRows,
    IReadOnlyList<string> Insights,
    string ExportFileName,
    string ExportCsv,
    string ExportHtml,
    DateTime LastUpdated);
