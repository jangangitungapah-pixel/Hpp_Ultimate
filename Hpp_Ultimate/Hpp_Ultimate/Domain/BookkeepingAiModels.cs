namespace Hpp_Ultimate.Domain;

public sealed record BookkeepingAiDraftRequest(
    string Title,
    LedgerEntryDirection Direction,
    decimal Amount,
    string? Counterparty,
    string? Notes);

public sealed record BookkeepingAiDraftSuggestion(
    string SuggestedCategory,
    LedgerEntryDirection SuggestedDirection,
    string SuggestedTitle,
    string? SuggestedCounterparty,
    string? SuggestedNotes,
    string Reason);

public sealed record BookkeepingAiDraftResult(
    bool Success,
    string Message,
    BookkeepingAiDraftSuggestion? Suggestion = null);

public sealed record BookkeepingAiSummaryRequest(
    decimal TotalIncome,
    decimal TotalExpense,
    decimal ClosingBalance,
    IReadOnlyList<BookkeepingListItem> Items);

public sealed record BookkeepingAiSummary(
    string Headline,
    IReadOnlyList<string> Highlights);

public sealed record BookkeepingAiSummaryResult(
    bool Success,
    string Message,
    BookkeepingAiSummary? Summary = null);

