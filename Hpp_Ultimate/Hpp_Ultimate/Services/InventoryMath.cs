using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public static class InventoryMath
{
    public static IReadOnlyDictionary<Guid, decimal> GetFinishedGoodsOnHandMap(SeededBusinessDataStore store)
    {
        var produced = store.ProductionBatches
            .Where(item => item.ProductId != Guid.Empty && item.Status == ProductionRunStatus.Completed)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (decimal)item.QuantityProduced));

        var sold = store.SaleLines
            .Join(
                store.Sales.Where(item => item.Status == SaleStatus.Completed),
                line => line.SaleId,
                sale => sale.Id,
                (line, _) => line)
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (decimal)item.Quantity));

        return produced.Keys
            .Concat(sold.Keys)
            .Distinct()
            .ToDictionary(
                key => key,
                key => produced.GetValueOrDefault(key) - sold.GetValueOrDefault(key));
    }

    public static decimal GetFinishedGoodsOnHand(SeededBusinessDataStore store, Guid productId)
        => GetFinishedGoodsOnHandMap(store).GetValueOrDefault(productId);

    public static IReadOnlyDictionary<Guid, decimal> GetRecipeMenuOnHandMap(SeededBusinessDataStore store)
    {
        var produced = store.ProductionBatches
            .Where(item => item.ProductId == Guid.Empty && item.RecipeId != Guid.Empty && item.Status == ProductionRunStatus.Completed)
            .GroupBy(item => item.RecipeId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item =>
                {
                    var portionYieldPerBatch = item.PortionYieldPerBatch > 0
                        ? item.PortionYieldPerBatch
                        : store.FindRecipeBook(item.RecipeId)?.PortionYield ?? 0m;
                    var batchCount = item.BatchCount > 0 ? item.BatchCount : Math.Max(1, item.QuantityProduced);
                    return portionYieldPerBatch * batchCount;
                }));

        var sold = store.SaleLines
            .Join(
                store.Sales.Where(item => item.Status == SaleStatus.Completed),
                line => line.SaleId,
                sale => sale.Id,
                (line, _) => line)
            .Where(item => item.RecipeId != Guid.Empty)
            .GroupBy(item => item.RecipeId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (decimal)item.Quantity));

        return produced.Keys
            .Concat(sold.Keys)
            .Distinct()
            .ToDictionary(
                key => key,
                key => produced.GetValueOrDefault(key) - sold.GetValueOrDefault(key));
    }

    public static decimal GetRecipeMenuOnHand(SeededBusinessDataStore store, Guid recipeId)
        => GetRecipeMenuOnHandMap(store).GetValueOrDefault(recipeId);

    public static RecipeFinancialSummary CalculateRecipeFinancials(RecipeBook recipe, IReadOnlyList<RawMaterial> materials)
    {
        var materialMap = materials.ToDictionary(item => item.Id);
        var materialCost = 0m;

        foreach (var group in recipe.Groups)
        {
            foreach (var line in group.Materials)
            {
                if (!materialMap.TryGetValue(line.MaterialId, out var material))
                {
                    continue;
                }

                materialCost += RecipeCatalogMath.CalculateLineCost(line, material);
            }
        }

        var overheadCost = recipe.Costs.Where(item => item.Type == RecipeCostType.Overhead).Sum(item => item.Amount);
        var productionCost = recipe.Costs.Where(item => item.Type == RecipeCostType.Production).Sum(item => item.Amount);
        var totalCost = materialCost + overheadCost + productionCost;
        var hppPerOutput = recipe.OutputQuantity <= 0 ? 0m : totalCost / recipe.OutputQuantity;
        var hppPerPortion = recipe.PortionYield <= 0 ? 0m : totalCost / recipe.PortionYield;

        return new RecipeFinancialSummary(materialCost, overheadCost, productionCost, totalCost, hppPerOutput, hppPerPortion);
    }
}

public sealed record RecipeFinancialSummary(
    decimal MaterialCost,
    decimal OverheadCost,
    decimal ProductionCost,
    decimal TotalCost,
    decimal HppPerOutputUnit,
    decimal HppPerPortion);
