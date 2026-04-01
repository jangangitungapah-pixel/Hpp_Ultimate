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
    DateTime UpdatedAt)
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
    string Note);

public sealed record ProductRecipe(
    Guid ProductId,
    decimal BatchOutputQuantity,
    decimal YieldPercentage,
    string? Notes,
    DateTime UpdatedAt);

public sealed record BomItem(Guid ProductId, Guid MaterialId, decimal QuantityPerUnit);

public sealed record ProductionBatch(Guid Id, Guid ProductId, string BatchCode, DateTime ProducedAt, int QuantityProduced);

public sealed record LaborCostEntry(Guid Id, Guid BatchId, decimal Amount, string Note, DateTime UpdatedAt);

public sealed record OverheadCostEntry(Guid Id, Guid BatchId, decimal Amount, string Note, DateTime UpdatedAt);
