namespace Hpp_Ultimate.Domain;

public sealed record HppRecipeOption(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    RecipeStatus Status,
    decimal OutputQuantity,
    string OutputUnit,
    decimal PortionYield,
    int MaterialCount,
    decimal TotalBatchCost,
    decimal HppPerOutput,
    decimal HppPerPortion,
    DateTime UpdatedAt);

public sealed record HppMaterialBreakdown(
    Guid LineId,
    string MaterialCode,
    string MaterialName,
    string? Brand,
    string NetLabel,
    decimal Quantity,
    string Unit,
    decimal BaseQuantity,
    string BaseUnit,
    decimal WastePercent,
    decimal CostPerBaseUnit,
    decimal LineCost,
    string? Notes);

public sealed record HppGroupBreakdown(
    Guid Id,
    string Name,
    string? Notes,
    int MaterialCount,
    decimal Subtotal,
    IReadOnlyList<HppMaterialBreakdown> Materials);

public sealed record HppCostBreakdown(
    Guid Id,
    RecipeCostType Type,
    string Name,
    decimal Amount,
    string? Notes);

public sealed record HppCalculatorSummary(
    decimal PlannedOutputQuantity,
    decimal RealizedOutputQuantity,
    string OutputUnit,
    decimal PortionYield,
    decimal MaterialCost,
    decimal OverheadCost,
    decimal ProductionCost,
    decimal OperationalCost,
    decimal TotalBatchCost,
    decimal HppPerPlannedOutput,
    decimal HppPerRealizedOutput,
    decimal HppPerPortion,
    decimal RoundedHpp,
    decimal HppAfterTax,
    int RoundingIncrement,
    decimal TaxPercent,
    bool TaxIncluded);

public sealed record HppRecipeBreakdown(
    Guid RecipeId,
    string Code,
    string Name,
    string? Description,
    RecipeStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<HppGroupBreakdown> Groups,
    IReadOnlyList<HppCostBreakdown> Costs,
    HppCalculatorSummary Summary);

public sealed record HppCalculatorSnapshot(
    IReadOnlyList<HppRecipeOption> Recipes,
    string SearchText,
    Guid? SelectedRecipeId,
    HppRecipeBreakdown? Breakdown);
