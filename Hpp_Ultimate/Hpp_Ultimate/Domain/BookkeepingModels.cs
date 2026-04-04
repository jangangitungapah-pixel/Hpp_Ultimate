using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record BookkeepingListItem(
    Guid Id,
    DateTime OccurredAt,
    string SourceType,
    string ReferenceNumber,
    string Title,
    LedgerEntryDirection Direction,
    string StatusLabel,
    string? Counterparty,
    decimal AmountIn,
    decimal AmountOut,
    decimal RunningBalance,
    string Detail);

public sealed record BookkeepingSnapshot(
    IReadOnlyList<BookkeepingListItem> Items,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal ClosingBalance,
    int TotalEntries);

public sealed record BookkeepingMutationResult(
    bool Success,
    string Message,
    ManualLedgerEntry? Entry = null);

public sealed class ManualLedgerEntryRequest
{
    public DateTime OccurredAt { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "Nama list pembukuan wajib diisi.")]
    public string Title { get; set; } = string.Empty;

    public LedgerEntryDirection Direction { get; set; } = LedgerEntryDirection.Expense;

    [Range(0.01, double.MaxValue, ErrorMessage = "Nominal harus lebih besar dari 0.")]
    public decimal Amount { get; set; }

    public string? Counterparty { get; set; }

    public string? Notes { get; set; }
}
