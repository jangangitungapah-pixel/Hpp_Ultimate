using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record ProductionRecipeOption(
    Guid RecipeId,
    string Code,
    string Name,
    RecipeStatus RecipeStatus,
    decimal BatchOutputQuantity,
    string OutputUnit,
    decimal PortionYieldPerBatch,
    string PortionUnit,
    int MaterialCount,
    bool HasRunningBatch,
    bool CanStart,
    string ReadinessMessage);

public sealed record ProductionRequirementLine(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string? Brand,
    string BaseUnit,
    decimal QuantityPerBatch,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal ShortageQuantity,
    decimal EstimatedCost,
    bool IsSufficient);

public sealed record ProductionDraft(
    Guid RecipeId,
    string RecipeCode,
    string RecipeName,
    decimal BatchOutputQuantity,
    string OutputUnit,
    decimal PortionYieldPerBatch,
    string PortionUnit,
    int BatchCount,
    decimal TotalOutputQuantity,
    decimal TotalPortions,
    decimal EstimatedMaterialCost,
    bool CanQueue,
    bool CanStart,
    IReadOnlyList<string> ValidationMessages,
    IReadOnlyList<ProductionRequirementLine> Requirements);

public sealed record ProductionBatchListItem(
    Guid Id,
    string BatchCode,
    Guid RecipeId,
    string RecipeCode,
    string RecipeName,
    int BatchCount,
    decimal TotalOutputQuantity,
    string OutputUnit,
    decimal TotalPortions,
    string PortionUnit,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int TargetDurationMinutes,
    ProductionRunStatus Status,
    decimal MaterialCost,
    string? Notes,
    bool CanStart = true,
    string? StartWarning = null);

public sealed record ProductionSnapshot(
    IReadOnlyList<ProductionRecipeOption> Recipes,
    int TotalRecipeCount,
    int QueueCount,
    int RunningCount,
    int CompletedCount,
    Guid? SelectedRecipeId,
    ProductionDraft? Draft,
    IReadOnlyList<ProductionBatchListItem> Queue,
    IReadOnlyList<ProductionBatchListItem> History);

public sealed record ProductionMutationResult(
    bool Success,
    string Message,
    ProductionBatchListItem? Batch = null);

public sealed class ProductionBatchCreateRequest
{
    public Guid? BatchId { get; set; }

    [Required(ErrorMessage = "Resep wajib dipilih.")]
    public Guid? RecipeId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah batch minimal 1.")]
    public int BatchCount { get; set; } = 1;

    [Range(1, 1440, ErrorMessage = "Target waktu produksi minimal 1 menit.")]
    public int TargetDurationMinutes { get; set; } = 30;

    public DateTime StartedAt { get; set; } = DateTime.Now;

    public string? Notes { get; set; }
}
