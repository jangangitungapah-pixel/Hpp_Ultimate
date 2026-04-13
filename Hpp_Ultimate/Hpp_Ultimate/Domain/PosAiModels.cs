namespace Hpp_Ultimate.Domain;

public sealed record PosAiOrderRequest(
    string Prompt,
    DateTime DraftSoldAt,
    IReadOnlyList<PosProductOption> Products);

public sealed record PosAiOrderSuggestion(
    string? ProductCode,
    int Quantity,
    string? CustomerName,
    string PaymentMethod,
    DateTime? SoldAt,
    string Reason,
    IReadOnlyList<string> Warnings);

public sealed record PosAiOrderResult(
    bool Success,
    string Message,
    PosAiOrderSuggestion? Suggestion = null);
