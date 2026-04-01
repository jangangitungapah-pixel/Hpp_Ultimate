namespace Hpp_Ultimate.Domain;

public enum DashboardPeriodPreset
{
    Today,
    ThisWeek,
    ThisMonth,
    Custom
}

public enum TrendDirection
{
    Neutral,
    Up,
    Down
}

public enum InsightSeverity
{
    Success,
    Warning,
    Danger
}

public sealed record DashboardFilter(
    DashboardPeriodPreset Preset,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? ProductId = null);

public sealed record DashboardRange(DateTime Start, DateTime EndExclusive, string Label);

public sealed record DashboardMetric(
    string Title,
    string Value,
    string Subtitle,
    string DeltaText,
    TrendDirection Trend,
    string Tone);

public sealed record CostBreakdownItem(string Label, decimal Amount, decimal Percentage, string Tone);

public sealed record TimeSeriesPoint(string Label, decimal Value);

public sealed record TopProductItem(string ProductName, decimal Value, string ValueLabel);

public sealed record ProductSummaryRow(
    Guid ProductId,
    string ProductName,
    int TotalProduction,
    decimal HppPerUnit,
    decimal SellingPrice,
    decimal ProfitPerUnit,
    decimal TotalProfit,
    decimal MarginPercentage);

public sealed record ActivityItem(DateTime Timestamp, string Type, string Title, string Detail);

public sealed record InsightItem(InsightSeverity Severity, string Title, string Detail);

public sealed record ProductFilterOption(Guid Id, string Name);

public sealed record DashboardSnapshot(
    DashboardRange Range,
    IReadOnlyList<DashboardMetric> Metrics,
    IReadOnlyList<CostBreakdownItem> CostBreakdown,
    IReadOnlyList<TimeSeriesPoint> ProductionSeries,
    IReadOnlyList<TopProductItem> TopProductsByVolume,
    IReadOnlyList<TopProductItem> TopProductsByProfit,
    IReadOnlyList<ProductSummaryRow> ProductRows,
    IReadOnlyList<ActivityItem> Activities,
    IReadOnlyList<InsightItem> Insights,
    IReadOnlyList<ProductFilterOption> AvailableProducts,
    DateTime LastUpdated);
