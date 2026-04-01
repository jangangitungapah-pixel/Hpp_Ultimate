using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public enum ProductionCostEntryType
{
    Labor,
    Overhead
}

public sealed record ProductionCostQuery(
    string? Search = null,
    Guid? ProductId = null,
    DashboardPeriodPreset Preset = DashboardPeriodPreset.ThisMonth,
    DateOnly? From = null,
    DateOnly? To = null,
    string SortBy = "date",
    bool Descending = true);

public sealed record ProductionCostSummary(
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    int TotalUnits,
    int BatchCount,
    decimal AverageCostPerUnit);

public sealed record ProductionCostBatchListItem(
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
    decimal CostPerUnit,
    bool MissingLabor,
    bool MissingOverhead);

public sealed record ProductionCostEntryView(
    Guid Id,
    Guid BatchId,
    decimal Amount,
    string Note,
    DateTime UpdatedAt);

public sealed record ProductionCostDetail(
    ProductionBatch Batch,
    Product Product,
    decimal MaterialCost,
    decimal LaborCost,
    decimal OverheadCost,
    decimal TotalCost,
    decimal CostPerUnit,
    decimal EstimatedRevenue,
    decimal EstimatedProfit,
    decimal EstimatedMargin,
    IReadOnlyList<ProductionCostEntryView> LaborEntries,
    IReadOnlyList<ProductionCostEntryView> OverheadEntries,
    IReadOnlyList<string> Alerts);

public sealed record ProductionCostInsight(string Tone, string Title, string Detail);

public sealed record ProductionCostQueryResult(
    DashboardRange Range,
    ProductionCostSummary Summary,
    IReadOnlyList<ProductionCostBatchListItem> Items,
    IReadOnlyList<ProductFilterOption> Products,
    IReadOnlyList<ProductionCostInsight> Insights);

public sealed record ProductionCostMutationResult(
    bool Success,
    string Message,
    ProductionCostEntryType? EntryType = null,
    Guid? EntryId = null,
    Guid? BatchId = null);

public sealed class ProductionCostEntryRequest
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Batch produksi wajib dipilih.")]
    public Guid? BatchId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Nominal biaya harus lebih besar dari 0.")]
    public decimal Amount { get; set; }

    public string Note { get; set; } = string.Empty;
}
