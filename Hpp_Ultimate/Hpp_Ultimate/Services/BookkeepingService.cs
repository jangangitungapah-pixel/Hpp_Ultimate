using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class BookkeepingService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    public async Task<BookkeepingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var cacheKey = $"bookkeeping:{store.Version}";
        if (cache.TryGetValue(cacheKey, out BookkeepingSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(35, cancellationToken);

        var timeline = BuildTimeline()
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.ReferenceNumber)
            .ToArray();

        var runningBalance = 0m;
        var projected = new List<BookkeepingListItem>(timeline.Length);
        foreach (var item in timeline)
        {
            runningBalance += item.Direction == LedgerEntryDirection.Income ? item.AmountIn : -item.AmountOut;
            projected.Add(item with { RunningBalance = runningBalance });
        }

        var items = projected
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.ReferenceNumber)
            .ToArray();

        snapshot = new BookkeepingSnapshot(
            items,
            items.Sum(item => item.AmountIn),
            items.Sum(item => item.AmountOut),
            projected.LastOrDefault()?.RunningBalance ?? 0m,
            items.Length);

        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<BookkeepingMutationResult> AddManualEntryAsync(ManualLedgerEntryRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new BookkeepingMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Task.FromResult(new BookkeepingMutationResult(false, "Nama list pembukuan wajib diisi."));
        }

        if (request.Amount <= 0)
        {
            return Task.FromResult(new BookkeepingMutationResult(false, "Nominal harus lebih besar dari 0."));
        }

        var entry = new ManualLedgerEntry(
            Guid.NewGuid(),
            request.OccurredAt == default ? DateTime.Now : request.OccurredAt,
            request.Title.Trim(),
            request.Direction,
            decimal.Round(request.Amount, 2),
            NormalizeOptional(request.Counterparty),
            NormalizeOptional(request.Notes));

        store.AddManualLedgerEntry(entry);
        auditTrail.Record(actor, "Bookkeeping", "Tambah manual entry", entry.Title, entry.Id, $"Pembukuan manual {entry.Title} ditambahkan dengan nominal {entry.Amount:N0}.");
        return Task.FromResult(new BookkeepingMutationResult(true, "List pembukuan berhasil ditambahkan.", entry));
    }

    private IEnumerable<BookkeepingListItem> BuildTimeline()
    {
        foreach (var sale in store.Sales.Where(item => item.Status == SaleStatus.Completed))
        {
            yield return new BookkeepingListItem(
                sale.Id,
                sale.SoldAt,
                "POS",
                sale.ReceiptNumber,
                sale.CustomerName is { Length: > 0 } ? $"Penjualan ke {sale.CustomerName}" : "Penjualan POS",
                LedgerEntryDirection.Income,
                sale.IsPaid ? "Lunas" : "Belum lunas",
                sale.CustomerName,
                sale.GrossRevenue,
                0m,
                0m,
                $"{sale.TotalQuantity} item - {sale.PaymentMethod}");
        }

        foreach (var purchase in store.PurchaseOrders.Where(item => item.Status == PurchaseOrderStatus.Received))
        {
            yield return new BookkeepingListItem(
                purchase.Id,
                purchase.ReceivedAt ?? purchase.OrderedAt,
                "Belanja",
                purchase.PurchaseNumber,
                $"Belanja material {purchase.SupplierName}",
                LedgerEntryDirection.Expense,
                "Selesai",
                purchase.SupplierName,
                0m,
                purchase.GrandTotal,
                0m,
                $"{purchase.LineCount} item - {purchase.TotalPackCount} pack");
        }

        foreach (var entry in store.ManualLedgerEntries)
        {
            yield return new BookkeepingListItem(
                entry.Id,
                entry.OccurredAt,
                "Manual",
                $"MNL-{entry.Id.ToString()[..8].ToUpperInvariant()}",
                entry.Title,
                entry.Direction,
                "Manual",
                entry.Counterparty,
                entry.Direction == LedgerEntryDirection.Income ? entry.Amount : 0m,
                entry.Direction == LedgerEntryDirection.Expense ? entry.Amount : 0m,
                0m,
                entry.Notes ?? "Entry pembukuan manual");
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
