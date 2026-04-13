namespace Hpp_Ultimate.Domain;

public sealed record ProductionAiRecipeContext(
    Guid RecipeId,
    string Code,
    string Name,
    decimal PortionYieldPerBatch,
    string PortionUnit,
    int MaterialCount,
    bool HasRunningBatch,
    bool CanStart,
    string ReadinessMessage);

public sealed record ProductionAiHistoryContext(
    Guid RecipeId,
    string RecipeCode,
    string RecipeName,
    int BatchCount,
    decimal TotalPortions,
    DateTime QueuedAt,
    ProductionRunStatus Status);

public sealed record ProductionAiSuggestion(
    Guid RecipeId,
    string RecipeCode,
    string RecipeName,
    int SuggestedBatchCount,
    int SuggestedTargetDurationMinutes,
    string Confidence,
    string Reason);

public sealed record ProductionAiRecommendationRequest(
    IReadOnlyList<ProductionAiRecipeContext> Recipes,
    IReadOnlyList<ProductionAiHistoryContext> Queue,
    IReadOnlyList<ProductionAiHistoryContext> History);

public sealed record ProductionAiRecommendationResult(
    bool Success,
    string Message,
    IReadOnlyList<ProductionAiSuggestion> Suggestions);

