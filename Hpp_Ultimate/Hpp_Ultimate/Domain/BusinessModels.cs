namespace Hpp_Ultimate.Domain;

public enum ProductStatus
{
    Active,
    Inactive
}

public enum MaterialStatus
{
    Active,
    Inactive
}

public enum StockMovementType
{
    OpeningBalance,
    StockIn,
    StockOut,
    Adjustment,
    ProductionUsage
}

public enum ProductionRunStatus
{
    Completed = 0,
    Running = 1,
    Queued = 2
}

public enum PurchaseChannel
{
    Offline,
    Online
}

public enum PurchaseOrderStatus
{
    Ordered,
    Received
}

public enum LedgerEntryDirection
{
    Income,
    Expense
}

public sealed record Product(
    Guid Id,
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal SellingPrice,
    string Description,
    ProductStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public bool IsActive => Status == ProductStatus.Active;
}

public sealed record MaterialUnitConversion(
    string UnitName,
    decimal ConversionQuantity);

public sealed record RawMaterial(
    Guid Id,
    string Code,
    string Name,
    string? Brand,
    string BaseUnit,
    decimal NetQuantity,
    string NetUnit,
    decimal PricePerPack,
    IReadOnlyList<MaterialUnitConversion> UnitConversions,
    string? Description,
    MaterialStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    decimal MinimumStock = 0m)
{
    public bool IsActive => Status == MaterialStatus.Active;

    public string Unit => BaseUnit;

    public string? SupplierName => Brand;

    public decimal NetQuantityInBaseUnit => MaterialUnitCatalog.Convert(NetQuantity, NetUnit, BaseUnit);

    public decimal CostPerBaseUnit => NetQuantityInBaseUnit <= 0 ? 0m : PricePerPack / NetQuantityInBaseUnit;

    public decimal PurchasePrice => CostPerBaseUnit;
}

public sealed record MaterialPriceEntry(
    Guid Id,
    Guid MaterialId,
    DateTime EffectiveAt,
    decimal PricePerPack,
    decimal NetQuantity,
    string NetUnit,
    string BaseUnit,
    DateTime UpdatedAt,
    decimal? PreviousPricePerPack,
    string Note)
{
    public decimal NetQuantityInBaseUnit => MaterialUnitCatalog.Convert(NetQuantity, NetUnit, BaseUnit);

    public decimal PricePerUnit => NetQuantityInBaseUnit <= 0 ? 0m : PricePerPack / NetQuantityInBaseUnit;
}

public sealed record StockMovementEntry(
    Guid Id,
    Guid MaterialId,
    StockMovementType Type,
    decimal Quantity,
    DateTime OccurredAt,
    string Note,
    Guid? RelatedBatchId = null);

public sealed record ProductRecipe(
    Guid ProductId,
    decimal BatchOutputQuantity,
    decimal YieldPercentage,
    string? Notes,
    DateTime UpdatedAt,
    Guid RecipeId = default);

public sealed record BomItem(Guid ProductId, Guid MaterialId, decimal QuantityPerUnit);

public sealed record ProductionBatch(
    Guid Id,
    Guid ProductId,
    string BatchCode,
    DateTime ProducedAt,
    int QuantityProduced,
    DateTime? QueuedAt = null,
    Guid RecipeId = default,
    string? Notes = null,
    decimal MaterialCost = 0m,
    decimal LaborCost = 0m,
    decimal OverheadCost = 0m,
    int BatchCount = 1,
    decimal PortionYieldPerBatch = 0m,
    int TargetDurationMinutes = 0,
    ProductionRunStatus Status = ProductionRunStatus.Completed,
    DateTime? CompletedAt = null);

public sealed record LaborCostEntry(Guid Id, Guid BatchId, decimal Amount, string Note, DateTime UpdatedAt);

public sealed record OverheadCostEntry(Guid Id, Guid BatchId, decimal Amount, string Note, DateTime UpdatedAt);

public sealed record PurchaseOrder(
    Guid Id,
    string PurchaseNumber,
    DateTime OrderedAt,
    string SupplierName,
    PurchaseChannel Channel,
    string? EcommercePlatform,
    int LineCount,
    int TotalPackCount,
    decimal Subtotal,
    decimal ShippingCost,
    decimal GrandTotal,
    PurchaseOrderStatus Status,
    string? Notes,
    DateTime? ReceivedAt = null,
    string? ReceiptFileName = null,
    string? ReceiptContentType = null,
    string? ReceiptBase64 = null,
    DateTime? ReceiptUploadedAt = null);

public sealed record PurchaseOrderLine(
    Guid Id,
    Guid PurchaseOrderId,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string? Brand,
    string BaseUnit,
    decimal NetQuantityPerPack,
    string NetUnit,
    decimal BaseQuantityPerPack,
    int PackCount,
    decimal PricePerPack,
    decimal LineSubtotal);

public sealed record ManualLedgerEntry(
    Guid Id,
    DateTime OccurredAt,
    string Title,
    LedgerEntryDirection Direction,
    decimal Amount,
    string? Counterparty,
    string? Notes);
