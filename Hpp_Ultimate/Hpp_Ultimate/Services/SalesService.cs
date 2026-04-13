using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class SalesService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail,
    HppCalculatorService hppCalculator)
{
    public async Task<PosSnapshot> GetPosSnapshotAsync(string? search = null, Guid? selectedProductId = null, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var cacheKey = $"pos-sales:{store.Version}:{normalizedSearch}:{selectedProductId}";
        if (cache.TryGetValue(cacheKey, out PosSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(40, cancellationToken);

        var menus = await BuildSellableProductsAsync(normalizedSearch, cancellationToken);
        var selected = selectedProductId is Guid requestedId
            ? await BuildProductDetailAsync(requestedId, cancellationToken)
            : null;
        var resolvedSelectedId = selected?.Recipe.Id
            ?? menus.FirstOrDefault(item => item.CanSell)?.ProductId
            ?? menus.FirstOrDefault()?.ProductId;

        if (selected is null && resolvedSelectedId is Guid recipeId)
        {
            selected = await BuildProductDetailAsync(recipeId, cancellationToken);
        }

        var recentSales = store.Sales
            .OrderByDescending(item => item.SoldAt)
            .Take(50)
            .Select(MapSalesHistoryItem)
            .ToArray();

        snapshot = new PosSnapshot(menus, resolvedSelectedId, selected, recentSales);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public async Task<PosCheckoutResult> CheckoutAsync(PosCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return new PosCheckoutResult(false, accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Lines.Count == 0)
        {
            return new PosCheckoutResult(false, "Pesanan masih kosong.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return new PosCheckoutResult(false, "Nama pembeli wajib diisi.");
        }

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        var onHandMap = InventoryMath.GetRecipeMenuOnHandMap(store).ToDictionary(item => item.Key, item => item.Value);
        var validatedLines = new List<SaleLine>();

        foreach (var line in request.Lines)
        {
            if (line.RecipeId is not Guid recipeId)
            {
                return new PosCheckoutResult(false, "Menu pesanan tidak valid.");
            }

            var recipe = store.FindRecipeBook(recipeId);
            if (recipe is null || recipe.Status != RecipeStatus.Active)
            {
                return new PosCheckoutResult(false, "Menu yang dipilih tidak ditemukan atau tidak aktif.");
            }

            var available = onHandMap.GetValueOrDefault(recipeId);
            if (available < line.Quantity)
            {
                return new PosCheckoutResult(false, $"Stok menu {recipe.Name} tidak cukup. Tersedia {available:0.##} {recipe.PortionUnit}.");
            }

            var financials = InventoryMath.CalculateRecipeFinancials(recipe, store.RawMaterials);
            var hppPerUnit = line.HppPerUnit > 0 ? line.HppPerUnit : financials.HppPerPortion;
            var unitPrice = line.UnitPrice > 0
                ? line.UnitPrice
                : recipe.SuggestedSellingPrice > 0
                    ? recipe.SuggestedSellingPrice
                    : RoundToIncrement(hppPerUnit * (1m + ((recipe.TargetMarginPercent > 0 ? recipe.TargetMarginPercent : 35m) / 100m)));

            validatedLines.Add(new SaleLine(
                Guid.NewGuid(),
                Guid.Empty,
                recipeId,
                recipeId,
                recipe.Code,
                recipe.Name,
                recipe.PortionUnit,
                line.Quantity,
                unitPrice,
                hppPerUnit));

            onHandMap[recipeId] = available - line.Quantity;
        }

        var totalRevenue = validatedLines.Sum(item => item.UnitPrice * item.Quantity);
        var totalHpp = validatedLines.Sum(item => item.HppPerUnit * item.Quantity);
        var totalQuantity = validatedLines.Sum(item => item.Quantity);
        if (totalRevenue <= 0)
        {
            return new PosCheckoutResult(false, "Total transaksi harus lebih besar dari 0.");
        }

        if (paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase) && request.AmountReceived > 0 && request.AmountReceived < totalRevenue)
        {
            return new PosCheckoutResult(false, "Pembayaran tunai belum cukup.");
        }

        var now = request.SoldAt == default ? DateTime.Now : request.SoldAt;
        var isPaid = paymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase) || request.MarkAsPaid;
        var amountReceived = isPaid
            ? Math.Max(totalRevenue, request.AmountReceived)
            : 0m;
        var saleId = Guid.NewGuid();
        var sale = new SaleTransaction(
            saleId,
            GenerateNextReceiptNumber(),
            now,
            actor.Id,
            actor.FullName,
            paymentMethod,
            validatedLines.Count,
            totalQuantity,
            totalRevenue,
            totalHpp,
            totalRevenue - totalHpp,
            amountReceived,
            isPaid ? Math.Max(0m, amountReceived - totalRevenue) : 0m,
            SaleStatus.Completed,
            NormalizeOptional(request.Notes),
            NormalizeOptional(request.CustomerName),
            isPaid,
            isPaid ? now : null);

        var persistedLines = validatedLines
            .Select(item => item with { SaleId = saleId })
            .ToArray();

        store.AddSale(sale, persistedLines);
        auditTrail.Record(actor, "Sales", "Checkout POS", sale.ReceiptNumber, sale.Id, $"Pesanan {sale.ReceiptNumber} untuk {sale.CustomerName ?? "tanpa nama"} selesai dengan total {sale.GrossRevenue:N0} via {sale.PaymentMethod}.");
        return new PosCheckoutResult(true, $"Pesanan {sale.ReceiptNumber} berhasil disimpan.", sale);
    }

    public Task<SalePaymentResult> MarkSalePaidAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new SalePaymentResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        var sale = store.FindSale(saleId);
        if (sale is null)
        {
            return Task.FromResult(new SalePaymentResult(false, "Pesanan tidak ditemukan."));
        }

        if (sale.Status == SaleStatus.Voided)
        {
            return Task.FromResult(new SalePaymentResult(false, "Pesanan void tidak bisa ditandai lunas."));
        }

        if (sale.IsPaid)
        {
            return Task.FromResult(new SalePaymentResult(false, "Pesanan ini sudah berstatus lunas."));
        }

        var updated = sale with
        {
            IsPaid = true,
            PaidAt = DateTime.Now,
            AmountReceived = sale.GrossRevenue,
            ChangeAmount = 0m
        };

        store.UpdateSale(updated);
        auditTrail.Record(actor, "Sales", "Tandai lunas", updated.ReceiptNumber, updated.Id, $"Pesanan {updated.ReceiptNumber} ditandai lunas.");
        return Task.FromResult(new SalePaymentResult(true, "Status pembayaran diperbarui menjadi lunas.", updated));
    }

    public Task<SalesHistorySnapshot> GetHistorySnapshotAsync(
        string? search = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        SaleStatus? status = null,
        Guid? selectedSaleId = null,
        CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var items = FilterSales(search, dateFrom, dateTo, status)
            .OrderByDescending(item => item.SoldAt)
            .ToArray();
        var resolvedSelectedId = selectedSaleId is Guid requested && items.Any(item => item.Id == requested)
            ? requested
            : items.FirstOrDefault()?.Id;
        var detail = resolvedSelectedId is Guid id ? BuildSaleDetail(id) : null;

        return Task.FromResult(new SalesHistorySnapshot(
            items.Select(MapSalesHistoryItem).ToArray(),
            items.Length,
            items.Sum(item => item.GrossRevenue),
            items.Sum(item => item.GrossProfit),
            resolvedSelectedId,
            detail));
    }

    public Task<PosCheckoutResult> VoidSaleAsync(VoidSaleRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new PosCheckoutResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.SaleId is not Guid saleId)
        {
            return Task.FromResult(new PosCheckoutResult(false, "Transaksi yang akan di-void tidak valid."));
        }

        var sale = store.FindSale(saleId);
        if (sale is null)
        {
            return Task.FromResult(new PosCheckoutResult(false, "Transaksi tidak ditemukan."));
        }

        if (sale.Status == SaleStatus.Voided)
        {
            return Task.FromResult(new PosCheckoutResult(false, "Transaksi ini sudah berstatus void."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult(new PosCheckoutResult(false, "Alasan void wajib diisi."));
        }

        var updated = sale with
        {
            Status = SaleStatus.Voided,
            VoidReason = request.Reason.Trim(),
            VoidedAt = DateTime.Now
        };

        store.UpdateSale(updated);
        auditTrail.Record(actor, "Sales", "Void transaksi", sale.ReceiptNumber, sale.Id, $"Transaksi {sale.ReceiptNumber} di-void. Alasan: {updated.VoidReason}");
        return Task.FromResult(new PosCheckoutResult(true, $"Transaksi {sale.ReceiptNumber} berhasil di-void.", updated));
    }

    public SaleDetail? GetSaleDetail(Guid saleId)
        => BuildSaleDetail(saleId);

    private async Task<IReadOnlyList<PosProductOption>> BuildSellableProductsAsync(string? search, CancellationToken cancellationToken)
    {
        var menuOnHandMap = InventoryMath.GetRecipeMenuOnHandMap(store);
        var recipes = store.Recipes
            .Where(item => item.Status == RecipeStatus.Active)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            recipes = recipes.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var result = new List<PosProductOption>();
        foreach (var recipe in recipes.OrderBy(item => item.Name))
        {
            var breakdown = await hppCalculator.GetBreakdownAsync(recipe.Id, cancellationToken: cancellationToken);
            if (breakdown is null)
            {
                continue;
            }

            var onHand = menuOnHandMap.GetValueOrDefault(recipe.Id);
            var marginPercent = recipe.TargetMarginPercent > 0 ? recipe.TargetMarginPercent : 35m;
            var suggested = recipe.SuggestedSellingPrice > 0
                ? recipe.SuggestedSellingPrice
                : RoundToIncrement(breakdown.Summary.HppPerPortion * (1m + (marginPercent / 100m)));

            result.Add(new PosProductOption(
                recipe.Id,
                recipe.Id,
                recipe.Code,
                recipe.Name,
                recipe.PortionUnit,
                "Menu",
                onHand,
                breakdown.Summary.HppPerPortion,
                suggested,
                suggested,
                suggested - breakdown.Summary.HppPerPortion,
                recipe.Code,
                recipe.Name,
                onHand > 0,
                onHand > 0 ? $"Stok siap dijual {onHand:0.##} {recipe.PortionUnit}" : "Stok menu kosong. Selesaikan produksi dulu.",
                recipe.UpdatedAt));
        }

        return result;
    }

    private async Task<PosProductDetail?> BuildProductDetailAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var recipe = store.FindRecipeBook(recipeId);
        if (recipe is null || recipe.Status != RecipeStatus.Active)
        {
            return null;
        }

        var breakdown = await hppCalculator.GetBreakdownAsync(recipe.Id, cancellationToken: cancellationToken);
        if (breakdown is null)
        {
            return null;
        }

        var marginPercent = recipe.TargetMarginPercent > 0 ? recipe.TargetMarginPercent : 35m;
        var suggested = recipe.SuggestedSellingPrice > 0
            ? recipe.SuggestedSellingPrice
            : RoundToIncrement(breakdown.Summary.HppPerPortion * (1m + (marginPercent / 100m)));

        return new PosProductDetail(
            recipe,
            InventoryMath.GetRecipeMenuOnHand(store, recipeId),
            breakdown.Summary.HppPerPortion,
            suggested,
            breakdown);
    }

    private IEnumerable<SaleTransaction> FilterSales(string? search, DateOnly? dateFrom, DateOnly? dateTo, SaleStatus? status)
    {
        var rows = store.Sales.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Trim();
            rows = rows.Where(item =>
                item.ReceiptNumber.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.CashierName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (item.CustomerName?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (dateFrom is DateOnly from)
        {
            rows = rows.Where(item => DateOnly.FromDateTime(item.SoldAt) >= from);
        }

        if (dateTo is DateOnly to)
        {
            rows = rows.Where(item => DateOnly.FromDateTime(item.SoldAt) <= to);
        }

        if (status is not null)
        {
            rows = rows.Where(item => item.Status == status);
        }

        return rows;
    }

    private SaleDetail? BuildSaleDetail(Guid saleId)
    {
        var sale = store.FindSale(saleId);
        if (sale is null)
        {
            return null;
        }

        var lines = store.FindSaleLines(saleId)
            .Select(item => new SalesHistoryLineItem(
                item.Id,
                item.ProductCode,
                item.ProductName,
                item.UnitLabel,
                item.Quantity,
                item.UnitPrice,
                item.HppPerUnit,
                item.UnitPrice * item.Quantity,
                (item.UnitPrice - item.HppPerUnit) * item.Quantity))
            .ToArray();

        return new SaleDetail(sale, lines);
    }

    private PosSalesHistoryItem MapSalesHistoryItem(SaleTransaction sale)
    {
        var lines = store.FindSaleLines(sale.Id);

        return new PosSalesHistoryItem(
            sale.Id,
            sale.ReceiptNumber,
            sale.SoldAt,
            sale.CustomerName ?? "-",
            BuildMenuSummary(lines),
            sale.CashierName,
            sale.PaymentMethod,
            sale.TotalQuantity,
            sale.GrossRevenue,
            sale.GrossProfit,
            sale.IsPaid,
            sale.Status);
    }

    private static string BuildMenuSummary(IReadOnlyList<SaleLine> lines)
    {
        if (lines.Count == 0)
        {
            return "-";
        }

        if (lines.Count == 1)
        {
            return lines[0].ProductName;
        }

        return $"{lines[0].ProductName} +{lines.Count - 1} menu";
    }

    private string GenerateNextReceiptNumber()
    {
        var next = store.Sales
            .Select(item => item.ReceiptNumber)
            .Where(code => code.StartsWith("TRX-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code[4..])
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"TRX-{next:000000}";
    }

    private decimal RoundToIncrement(decimal value)
    {
        var settings = store.GetBusinessSettings();
        if (value <= 0 || settings.DefaultPriceRounding <= 1)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        return Math.Round(value / settings.DefaultPriceRounding, MidpointRounding.AwayFromZero) * settings.DefaultPriceRounding;
    }

    private static string NormalizePaymentMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Cash";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "cash" => "Cash",
            "transfer" => "Transfer Bank",
            "transfer bank" => "Transfer Bank",
            _ => value.Trim()
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
