namespace Hpp_Ultimate.Domain;

public sealed record MaterialAiAuditRequest(
    IReadOnlyList<RawMaterialListItem> Materials);

public sealed record MaterialAiDuplicateReference(
    Guid MaterialId,
    string Code,
    string Name,
    string? Brand);

public sealed record MaterialAiNormalizationSuggestion(
    Guid TargetMaterialId,
    string TargetCode,
    string CanonicalName,
    string? CanonicalBrand,
    string Confidence,
    string Reason,
    IReadOnlyList<MaterialAiDuplicateReference> SimilarMaterials);

public sealed record MaterialAiAuditResult(
    bool Success,
    string Message,
    IReadOnlyList<MaterialAiNormalizationSuggestion> Suggestions);

