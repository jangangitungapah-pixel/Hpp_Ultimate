using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record WarehouseMaterialItem(
    Guid MaterialId,
    string Code,
    string Name,
    string? Brand,
    string BaseUnit,
    decimal CostPerUnit,
    decimal OnHandQuantity,
    decimal MinimumStock,
    decimal DeltaToMinimumStock,
    bool IsBelowMinimumStock,
    MaterialStatus Status,
    DateTime? LastMovementAt);

public sealed record WarehouseStockMovementItem(
    Guid Id,
    StockMovementType Type,
    decimal Quantity,
    DateTime OccurredAt,
    string Note,
    Guid? RelatedBatchId);

public sealed record WarehouseMaterialDetail(
    WarehouseMaterialItem Summary,
    IReadOnlyList<WarehouseStockMovementItem> RecentMovements);

public sealed record WarehouseSnapshot(
    IReadOnlyList<WarehouseMaterialItem> Materials,
    int TotalMaterials,
    int BelowMinimumStockCount,
    decimal TotalStockValue,
    Guid? SelectedMaterialId,
    WarehouseMaterialDetail? Detail);

public sealed record WarehouseMutationResult(
    bool Success,
    string Message,
    WarehouseMaterialDetail? Detail = null);

public sealed class StockMovementRequest
{
    [Required(ErrorMessage = "Material wajib dipilih.")]
    public Guid? MaterialId { get; set; }

    public StockMovementType Type { get; set; } = StockMovementType.StockIn;

    [Range(typeof(decimal), "-999999999", "999999999", ErrorMessage = "Kuantitas harus diisi.")]
    public decimal Quantity { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.Now;

    public string Note { get; set; } = string.Empty;
}

public sealed class MaterialStockPolicyRequest
{
    [Required(ErrorMessage = "Material wajib dipilih.")]
    public Guid? MaterialId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Minimum stock tidak boleh negatif.")]
    public decimal MinimumStock { get; set; }
}
