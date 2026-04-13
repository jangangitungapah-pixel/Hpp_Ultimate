namespace Hpp_Ultimate.Domain;

public sealed record RecipeDoughWeightLineEstimate(
    Guid GroupId,
    Guid LineId,
    Guid? MaterialId,
    string MaterialName,
    decimal WeightGr,
    bool IsResolved,
    bool UsedAi,
    string Message);

public sealed record RecipeDoughWeightGroupEstimate(
    Guid GroupId,
    string GroupName,
    decimal TotalWeightGr,
    bool IsComplete,
    bool UsedAi,
    int ResolvedLineCount,
    int AiLineCount,
    int UnresolvedLineCount,
    string Message);

public sealed record RecipeDoughWeightEstimateResult(
    decimal TotalWeightGr,
    bool IsComplete,
    bool UsedAi,
    int DirectLineCount,
    int AiLineCount,
    int UnresolvedLineCount,
    string Message,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<RecipeDoughWeightGroupEstimate> Groups,
    IReadOnlyList<RecipeDoughWeightLineEstimate> Lines)
{
    public static RecipeDoughWeightEstimateResult Empty { get; } = new(
        0m,
        true,
        false,
        0,
        0,
        0,
        "Belum ada bahan yang dihitung.",
        [],
        [],
        []);
}
