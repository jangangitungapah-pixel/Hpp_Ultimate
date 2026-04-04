using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class HppCalculatorService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access)
{
    public async Task<HppCalculatorSnapshot> GetSnapshotAsync(
        string? search = null,
        Guid? selectedRecipeId = null,
        decimal? realizedOutput = null,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim();
        var normalizedOutput = realizedOutput is > 0 ? decimal.Round(realizedOutput.Value, 4) : 0m;
        var cacheKey = $"hpp:{store.Version}:{normalizedSearch}:{selectedRecipeId}:{normalizedOutput}";

        if (cache.TryGetValue(cacheKey, out HppCalculatorSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(40, cancellationToken);

        var recipes = store.Recipes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            recipes = recipes.Where(item =>
                item.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var options = recipes
            .OrderByDescending(item => item.UpdatedAt)
            .Select(MapOption)
            .ToArray();

        var resolvedRecipeId = selectedRecipeId is Guid recipeId && options.Any(item => item.Id == recipeId)
            ? recipeId
            : options.FirstOrDefault()?.Id;

        var breakdown = resolvedRecipeId is Guid chosenId
            ? BuildBreakdown(chosenId, realizedOutput)
            : null;

        snapshot = new HppCalculatorSnapshot(options, normalizedSearch, resolvedRecipeId, breakdown);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<HppRecipeBreakdown?> GetBreakdownAsync(
        Guid recipeId,
        decimal? realizedOutput = null,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildBreakdown(recipeId, realizedOutput));
    }

    private HppRecipeOption MapOption(RecipeBook recipe)
    {
        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var materialCount = 0;
        var materialCost = 0m;

        foreach (var group in recipe.Groups)
        {
            foreach (var item in group.Materials)
            {
                if (!materialMap.TryGetValue(item.MaterialId, out var material))
                {
                    continue;
                }

                materialCount++;
                materialCost += RecipeCatalogMath.CalculateLineCost(item, material);
            }
        }

        var operationalCost = recipe.Costs.Sum(item => item.Amount);
        var totalBatchCost = materialCost + operationalCost;
        var hppPerOutput = recipe.OutputQuantity <= 0 ? 0m : totalBatchCost / recipe.OutputQuantity;
        var hppPerPortion = recipe.PortionYield <= 0 ? 0m : totalBatchCost / recipe.PortionYield;

        return new HppRecipeOption(
            recipe.Id,
            recipe.Code,
            recipe.Name,
            recipe.Description,
            recipe.Status,
            recipe.OutputQuantity,
            recipe.OutputUnit,
            recipe.PortionYield,
            materialCount,
            totalBatchCost,
            hppPerOutput,
            hppPerPortion,
            recipe.UpdatedAt);
    }

    private HppRecipeBreakdown? BuildBreakdown(Guid recipeId, decimal? realizedOutput)
    {
        var recipe = store.FindRecipeBook(recipeId);
        if (recipe is null)
        {
            return null;
        }

        var materialMap = store.RawMaterials.ToDictionary(item => item.Id);
        var groups = recipe.Groups.Select(group =>
        {
            var materials = group.Materials.Select(line =>
            {
                if (!materialMap.TryGetValue(line.MaterialId, out var material))
                {
                    return new HppMaterialBreakdown(
                        line.Id,
                        "-",
                        "Material tidak ditemukan",
                        null,
                        "-",
                        line.Quantity,
                        line.Unit,
                        0m,
                        "-",
                        line.WastePercent,
                        0m,
                        0m,
                        line.Notes);
                }

                var baseQuantity = RecipeCatalogMath.ConvertToBaseQuantity(line.Quantity, line.Unit, material);
                return new HppMaterialBreakdown(
                    line.Id,
                    material.Code,
                    material.Name,
                    material.Brand,
                    $"{material.NetQuantity.ToString("0.####")} {material.NetUnit}",
                    line.Quantity,
                    line.Unit,
                    baseQuantity,
                    material.BaseUnit,
                    line.WastePercent,
                    material.CostPerBaseUnit,
                    RecipeCatalogMath.CalculateLineCost(line, material),
                    line.Notes);
            }).ToArray();

            return new HppGroupBreakdown(
                group.Id,
                group.Name,
                group.Notes,
                materials.Length,
                materials.Sum(item => item.LineCost),
                materials);
        }).ToArray();

        var costs = recipe.Costs
            .OrderBy(item => item.Type)
            .ThenBy(item => item.Name)
            .Select(item => new HppCostBreakdown(item.Id, item.Type, item.Name, item.Amount, item.Notes))
            .ToArray();

        var materialCost = groups.Sum(item => item.Subtotal);
        var overheadCost = costs.Where(item => item.Type == RecipeCostType.Overhead).Sum(item => item.Amount);
        var productionCost = costs.Where(item => item.Type == RecipeCostType.Production).Sum(item => item.Amount);
        var operationalCost = overheadCost + productionCost;
        var totalBatchCost = materialCost + operationalCost;
        var plannedOutput = recipe.OutputQuantity <= 0 ? 1m : recipe.OutputQuantity;
        var actualOutput = realizedOutput is > 0 ? realizedOutput.Value : plannedOutput;
        var portionYield = recipe.PortionYield <= 0 ? 1m : recipe.PortionYield;
        var hppPerPlanned = totalBatchCost / plannedOutput;
        var hppPerRealized = actualOutput <= 0 ? 0m : totalBatchCost / actualOutput;
        var hppPerPortion = totalBatchCost / portionYield;

        var settings = store.GetBusinessSettings();
        var roundedHpp = RoundToIncrement(hppPerPortion, settings.DefaultPriceRounding);
        var hppAfterTax = settings.TaxIncluded
            ? roundedHpp
            : roundedHpp * (1m + Math.Max(0m, settings.TaxPercent) / 100m);

        var summary = new HppCalculatorSummary(
            plannedOutput,
            actualOutput,
            recipe.OutputUnit,
            portionYield,
            materialCost,
            overheadCost,
            productionCost,
            operationalCost,
            totalBatchCost,
            hppPerPlanned,
            hppPerRealized,
            hppPerPortion,
            roundedHpp,
            hppAfterTax,
            settings.DefaultPriceRounding,
            settings.TaxPercent,
            settings.TaxIncluded);

        return new HppRecipeBreakdown(
            recipe.Id,
            recipe.Code,
            recipe.Name,
            recipe.Description,
            recipe.Status,
            recipe.UpdatedAt,
            groups,
            costs,
            summary);
    }

    private static decimal RoundToIncrement(decimal value, int increment)
    {
        if (value <= 0 || increment <= 1)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return Math.Round(value / increment, MidpointRounding.AwayFromZero) * increment;
    }
}
