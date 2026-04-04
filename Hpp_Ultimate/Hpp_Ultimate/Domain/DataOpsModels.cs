namespace Hpp_Ultimate.Domain;

public sealed record AppDataSnapshot(
    IReadOnlyList<Product> Products,
    IReadOnlyList<RawMaterial> RawMaterials,
    IReadOnlyList<MaterialPriceEntry> MaterialPrices,
    IReadOnlyList<RecipeBook> Recipes,
    IReadOnlyList<StockMovementEntry> StockMovements,
    IReadOnlyList<ProductRecipe> ProductRecipes,
    IReadOnlyList<BomItem> BomItems,
    IReadOnlyList<ProductionBatch> ProductionBatches,
    IReadOnlyList<LaborCostEntry> LaborCosts,
    IReadOnlyList<OverheadCostEntry> OverheadCosts,
    IReadOnlyList<PurchaseOrder> PurchaseOrders,
    IReadOnlyList<PurchaseOrderLine> PurchaseOrderLines,
    IReadOnlyList<ManualLedgerEntry> ManualLedgerEntries,
    IReadOnlyList<SaleTransaction> Sales,
    IReadOnlyList<SaleLine> SaleLines,
    IReadOnlyList<BusinessUser> Users,
    IReadOnlyList<AuditLogEntry> AuditLogs,
    BusinessSettings BusinessSettings,
    AuthSession? AuthSession);

public sealed record BackupFileItem(
    string FileName,
    string AbsolutePath,
    long SizeBytes,
    DateTime UpdatedAt,
    string Kind);

public sealed record DataOpsSnapshot(
    IReadOnlyList<BackupFileItem> Backups,
    IReadOnlyList<BackupFileItem> Exports,
    int BackupCount,
    int ExportCount);

public sealed record DataOperationResult(
    bool Success,
    string Message,
    string? AbsolutePath = null);

public sealed record SalesDataBundle(
    IReadOnlyList<SaleTransaction> Sales,
    IReadOnlyList<SaleLine> Lines);
