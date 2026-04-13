using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class ShoppingService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    private static readonly string[] SupportedPlatforms = ["Shopee", "Tokopedia", "TikTok", "WhatsApp"];

    public async Task<ShoppingSnapshot> GetSnapshotAsync(Guid? selectedMaterialId = null, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var cacheKey = $"shopping:{store.Version}:{selectedMaterialId}";
        if (cache.TryGetValue(cacheKey, out ShoppingSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(40, cancellationToken);

        var onHandMap = store.StockMovements
            .GroupBy(item => item.MaterialId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        var materials = store.RawMaterials
            .Where(item => item.Status == MaterialStatus.Active)
            .OrderBy(item => item.Name)
            .Select(item => new ShoppingMaterialOption(
                item.Id,
                item.Code,
                item.Name,
                item.Brand,
                item.BaseUnit,
                item.NetQuantity,
                item.NetUnit,
                item.NetQuantityInBaseUnit,
                item.PricePerPack,
                item.CostPerBaseUnit,
                onHandMap.GetValueOrDefault(item.Id),
                BuildMaterialLookupLabel(item)))
            .ToArray();

        var resolvedSelectedId = selectedMaterialId is Guid requested && materials.Any(item => item.MaterialId == requested)
            ? requested
            : materials.FirstOrDefault()?.MaterialId;

        var selected = resolvedSelectedId is Guid materialId
            ? BuildDetail(materialId, onHandMap)
            : null;

        var history = store.PurchaseOrders
            .OrderByDescending(item => item.OrderedAt)
            .Take(50)
            .Select(MapHistoryItem)
            .ToArray();

        snapshot = new ShoppingSnapshot(
            materials,
            resolvedSelectedId,
            selected,
            history,
            history.Count(item => item.Status == PurchaseOrderStatus.Ordered),
            history.Length,
            history.Sum(item => item.GrandTotal));

        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<ShoppingMutationResult> CheckoutAsync(ShoppingCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ShoppingMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Lines.Count == 0)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "List belanja masih kosong."));
        }

        if (string.IsNullOrWhiteSpace(request.SupplierName))
        {
            return Task.FromResult(new ShoppingMutationResult(false, "Nama toko / supplier wajib diisi."));
        }

        if (request.ShippingCost < 0)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "Ongkir tidak boleh negatif."));
        }

        var platform = request.Channel == PurchaseChannel.Online
            ? NormalizePlatform(request.EcommercePlatform)
            : null;

        if (request.Channel == PurchaseChannel.Online && string.IsNullOrWhiteSpace(platform))
        {
            return Task.FromResult(new ShoppingMutationResult(false, "Pilih ecommerce untuk belanja online."));
        }

        var groupedLines = request.Lines
            .Where(item => item.MaterialId is not null && item.PackCount > 0)
            .GroupBy(item => item.MaterialId!.Value)
            .Select(group => new { MaterialId = group.Key, PackCount = group.Sum(item => item.PackCount) })
            .ToArray();

        if (groupedLines.Length == 0)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "List belanja masih kosong."));
        }

        var purchaseId = Guid.NewGuid();
        var now = request.OrderedAt == default ? DateTime.Now : request.OrderedAt;
        var orderLines = new List<PurchaseOrderLine>();

        foreach (var line in groupedLines)
        {
            var material = store.FindRawMaterial(line.MaterialId);
            if (material is null)
            {
                return Task.FromResult(new ShoppingMutationResult(false, "Salah satu material belanja tidak ditemukan."));
            }

            if (material.NetQuantityInBaseUnit <= 0)
            {
                return Task.FromResult(new ShoppingMutationResult(false, $"Pack material {material.Name} belum punya netto yang valid."));
            }

            orderLines.Add(new PurchaseOrderLine(
                Guid.NewGuid(),
                purchaseId,
                material.Id,
                material.Code,
                material.Name,
                material.Brand,
                material.BaseUnit,
                material.NetQuantity,
                material.NetUnit,
                material.NetQuantityInBaseUnit,
                line.PackCount,
                material.PricePerPack,
                decimal.Round(material.PricePerPack * line.PackCount, 2)));
        }

        var subtotal = orderLines.Sum(item => item.LineSubtotal);
        var status = request.Channel == PurchaseChannel.Offline ? PurchaseOrderStatus.Received : PurchaseOrderStatus.Ordered;
        DateTime? receivedAt = status == PurchaseOrderStatus.Received ? now : null;
        var order = new PurchaseOrder(
            purchaseId,
            GenerateNextPurchaseNumber(),
            now,
            request.SupplierName.Trim(),
            request.Channel,
            platform,
            orderLines.Count,
            orderLines.Sum(item => item.PackCount),
            subtotal,
            request.ShippingCost,
            subtotal + request.ShippingCost,
            status,
            NormalizeOptional(request.Notes),
            receivedAt);

        var stockMovements = status == PurchaseOrderStatus.Received
            ? BuildStockMovements(order, orderLines, now)
            : Array.Empty<StockMovementEntry>();

        store.AddPurchaseOrder(order, orderLines, stockMovements);
        auditTrail.Record(
            actor,
            "Shopping",
            "Checkout belanja",
            order.PurchaseNumber,
            order.Id,
            $"Belanja {order.PurchaseNumber} dibuat untuk {order.LineCount} item dari {order.SupplierName}.");

        return Task.FromResult(new ShoppingMutationResult(
            true,
            status == PurchaseOrderStatus.Received
                ? "Belanja berhasil dicatat dan stok gudang langsung ditambahkan."
                : "Belanja online berhasil dicatat. Stok akan masuk saat barang ditandai sampai.",
            order));
    }

    public Task<ShoppingMutationResult> MarkReceivedAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ShoppingMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        var order = store.FindPurchaseOrder(purchaseOrderId);
        if (order is null)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "History belanja tidak ditemukan."));
        }

        if (order.Status == PurchaseOrderStatus.Received)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "Belanja ini sudah diterima dan stok sudah masuk."));
        }

        var lines = store.FindPurchaseOrderLines(purchaseOrderId);
        if (lines.Count == 0)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "Detail item belanja tidak ditemukan."));
        }

        foreach (var line in lines)
        {
            if (store.FindRawMaterial(line.MaterialId) is null)
            {
                return Task.FromResult(new ShoppingMutationResult(false, $"Material {line.MaterialName} sudah tidak tersedia di katalog."));
            }
        }

        var receivedAt = DateTime.Now;
        var updated = order with
        {
            Status = PurchaseOrderStatus.Received,
            ReceivedAt = receivedAt
        };

        var stockMovements = BuildStockMovements(updated, lines, receivedAt);
        store.UpdatePurchaseOrder(updated, stockMovements);
        auditTrail.Record(actor, "Shopping", "Barang sampai", updated.PurchaseNumber, updated.Id, $"Belanja {updated.PurchaseNumber} ditandai sampai dan stok gudang ditambahkan.");
        return Task.FromResult(new ShoppingMutationResult(true, "Barang ditandai sampai. Stok gudang berhasil ditambahkan.", updated));
    }

    public Task<ShoppingMutationResult> UploadReceiptAsync(ShoppingReceiptUploadRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new ShoppingMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.PurchaseOrderId is not Guid purchaseOrderId)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "History belanja tidak valid."));
        }

        var order = store.FindPurchaseOrder(purchaseOrderId);
        if (order is null)
        {
            return Task.FromResult(new ShoppingMutationResult(false, "History belanja tidak ditemukan."));
        }

        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.Base64Content))
        {
            return Task.FromResult(new ShoppingMutationResult(false, "File struk belum valid."));
        }

        var updated = order with
        {
            ReceiptFileName = request.FileName.Trim(),
            ReceiptContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType.Trim(),
            ReceiptBase64 = request.Base64Content,
            ReceiptUploadedAt = DateTime.Now
        };

        store.UpdatePurchaseOrder(updated);
        auditTrail.Record(actor, "Shopping", "Upload struk", updated.PurchaseNumber, updated.Id, $"Struk belanja diunggah untuk {updated.PurchaseNumber}.");
        return Task.FromResult(new ShoppingMutationResult(true, "Struk belanja berhasil diunggah.", updated));
    }

    private ShoppingMaterialDetail? BuildDetail(Guid materialId, IReadOnlyDictionary<Guid, decimal> onHandMap)
    {
        var material = store.FindRawMaterial(materialId);
        if (material is null)
        {
            return null;
        }

        return new ShoppingMaterialDetail(
            material,
            onHandMap.GetValueOrDefault(materialId),
            BuildMaterialLookupLabel(material));
    }

    private ShoppingHistoryItem MapHistoryItem(PurchaseOrder order)
    {
        var lines = store.FindPurchaseOrderLines(order.Id);
        return new ShoppingHistoryItem(
            order.Id,
            order.PurchaseNumber,
            order.OrderedAt,
            order.SupplierName,
            order.Channel,
            order.EcommercePlatform,
            BuildItemSummary(lines),
            order.LineCount,
            order.TotalPackCount,
            order.Subtotal,
            order.ShippingCost,
            order.GrandTotal,
            order.Status,
            order.ReceivedAt,
            !string.IsNullOrWhiteSpace(order.ReceiptFileName),
            order.ReceiptFileName,
            order.Channel == PurchaseChannel.Online && order.Status == PurchaseOrderStatus.Ordered);
    }

    private static string BuildItemSummary(IReadOnlyList<PurchaseOrderLine> lines)
    {
        if (lines.Count == 0)
        {
            return "-";
        }

        if (lines.Count == 1)
        {
            return lines[0].MaterialName;
        }

        return $"{lines[0].MaterialName} +{lines.Count - 1} material";
    }

    private static IReadOnlyList<StockMovementEntry> BuildStockMovements(PurchaseOrder order, IEnumerable<PurchaseOrderLine> lines, DateTime occurredAt)
        => lines
            .Select(line => new StockMovementEntry(
                Guid.NewGuid(),
                line.MaterialId,
                StockMovementType.StockIn,
                decimal.Round(line.BaseQuantityPerPack * line.PackCount, 4),
                occurredAt,
                $"Belanja {order.PurchaseNumber} dari {order.SupplierName}",
                order.Id))
            .ToArray();

    private string GenerateNextPurchaseNumber()
    {
        var next = store.PurchaseOrders
            .Select(item => item.PurchaseNumber)
            .Where(code => code.StartsWith("BLJ-", StringComparison.OrdinalIgnoreCase))
            .Select(code => code[4..])
            .Select(part => int.TryParse(part, out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        return $"BLJ-{next:000000}";
    }

    private static string BuildMaterialLookupLabel(RawMaterial material)
        => $"{material.Code} - {material.Name}{(string.IsNullOrWhiteSpace(material.Brand) ? string.Empty : $" - {material.Brand}")}";

    private static string? NormalizePlatform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "shopee" => SupportedPlatforms[0],
            "tokopedia" => SupportedPlatforms[1],
            "tiktok" => SupportedPlatforms[2],
            "tiktok shop" => SupportedPlatforms[2],
            "whatsapp" => SupportedPlatforms[3],
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
