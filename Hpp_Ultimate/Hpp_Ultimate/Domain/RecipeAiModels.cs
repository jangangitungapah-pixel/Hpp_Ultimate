namespace Hpp_Ultimate.Domain;

public sealed record RecipeAiMaterialContext(
    string Code,
    string Name,
    string? Brand,
    string BaseUnit,
    string[] AvailableUnits,
    bool ExistsInWarehouse);

public sealed record RecipeAiDraftRequest(
    string Prompt,
    IReadOnlyList<RecipeAiMaterialContext> Materials);

public sealed record RecipeAiMaterialSuggestion(
    string? MaterialCode,
    decimal Quantity,
    string Unit,
    decimal WastePercent,
    string? Notes);

public sealed record RecipeAiGroupSuggestion(
    string Name,
    string? Notes,
    IReadOnlyList<RecipeAiMaterialSuggestion> Materials);

public sealed record RecipeAiCostSuggestion(
    RecipeCostType Type,
    string Name,
    decimal Amount,
    string? Notes);

public sealed record RecipeAiDraft(
    string Name,
    string? Description,
    decimal PortionYield,
    string PortionUnit,
    decimal TargetMarginPercent,
    IReadOnlyList<RecipeAiGroupSuggestion> Groups,
    IReadOnlyList<RecipeAiCostSuggestion> Costs,
    IReadOnlyList<string> Warnings);

public sealed record RecipeAiDraftResult(
    bool Success,
    string Message,
    RecipeAiDraft? Draft = null);

