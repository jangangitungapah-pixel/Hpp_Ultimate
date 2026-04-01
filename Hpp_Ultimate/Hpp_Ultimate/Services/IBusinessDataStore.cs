using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public interface IBusinessDataStore
{
    event Action? Changed;

    long Version { get; }

    IReadOnlyList<Product> Products { get; }
    IReadOnlyList<RawMaterial> RawMaterials { get; }
    IReadOnlyList<MaterialPriceEntry> MaterialPrices { get; }
    IReadOnlyList<StockMovementEntry> StockMovements { get; }
    IReadOnlyList<ProductRecipe> ProductRecipes { get; }
    IReadOnlyList<BomItem> BomItems { get; }
    IReadOnlyList<ProductionBatch> ProductionBatches { get; }
    IReadOnlyList<LaborCostEntry> LaborCosts { get; }
    IReadOnlyList<OverheadCostEntry> OverheadCosts { get; }
}
