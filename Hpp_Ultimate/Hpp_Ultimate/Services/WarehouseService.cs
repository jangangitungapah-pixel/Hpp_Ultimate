using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class WarehouseService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    public async Task<WarehouseSnapshot> GetSnapshotAsync(string? search = null, Guid? selectedMaterialId = null, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var cacheKey = $"warehouse:{store.Version}:{normalizedSearch}:{selectedMaterialId}";
        if (cache.TryGetValue(cacheKey, out WarehouseSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(60, cancellationToken);
        snapshot = BuildSnapshot(normalizedSearch, selectedMaterialId);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<WarehouseMutationResult> RecordMovementAsync(StockMovementRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new WarehouseMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        if (request.MaterialId is not Guid materialId)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Material wajib dipilih."));
        }

        var material = store.FindRawMaterial(materialId);
        if (material is null)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Material tidak ditemukan."));
        }

        if (request.Quantity == 0)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Kuantitas tidak boleh 0."));
        }

        var signedQuantity = NormalizeQuantity(request.Type, request.Quantity);
        if (signedQuantity == 0)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Kuantitas tidak valid untuk tipe mutasi tersebut."));
        }

        var currentOnHand = GetOnHandQuantity(materialId);
        var projected = currentOnHand + signedQuantity;
        if (projected < 0)
        {
            return Task.FromResult(new WarehouseMutationResult(false, $"Stok {material.Name} tidak cukup. Saldo saat ini {currentOnHand:0.####} {material.BaseUnit}."));
        }

        var note = string.IsNullOrWhiteSpace(request.Note)
            ? GetDefaultMovementNote(request.Type)
            : request.Note.Trim();

        store.AddStockMovement(new StockMovementEntry(
            Guid.NewGuid(),
            materialId,
            request.Type,
            signedQuantity,
            request.OccurredAt,
            note));

        auditTrail.Record(actor, "Warehouse", "Mutasi stok", material.Name, material.Id, $"Mutasi {request.Type} sebesar {signedQuantity:0.####} {material.BaseUnit} untuk {material.Name}.");
        return Task.FromResult(new WarehouseMutationResult(true, "Mutasi stok berhasil dicatat.", BuildDetail(materialId)));
    }

    public Task<WarehouseMutationResult> SaveMinimumStockAsync(MaterialStockPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            return Task.FromResult(new WarehouseMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        if (request.MaterialId is not Guid materialId)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Material wajib dipilih."));
        }

        var material = store.FindRawMaterial(materialId);
        if (material is null)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Material tidak ditemukan."));
        }

        if (request.MinimumStock < 0)
        {
            return Task.FromResult(new WarehouseMutationResult(false, "Minimum stock tidak boleh negatif."));
        }

        var updated = material with
        {
            MinimumStock = request.MinimumStock,
            UpdatedAt = DateTime.Now
        };

        store.UpdateRawMaterial(updated, material.PricePerPack, "Update minimum stock gudang");
        auditTrail.Record(actor, "Warehouse", "Update minimum stock", material.Name, material.Id, $"Minimum stock {material.Name} disetel ke {request.MinimumStock:0.####} {material.BaseUnit}.");
        return Task.FromResult(new WarehouseMutationResult(true, "Minimum stock berhasil diperbarui.", BuildDetail(materialId)));
    }

    private WarehouseSnapshot BuildSnapshot(string? search, Guid? selectedMaterialId)
    {
        var onHandMap = store.StockMovements
            .GroupBy(item => item.MaterialId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var lastMovementMap = store.StockMovements
            .GroupBy(item => item.MaterialId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.OccurredAt));

        var materials = store.RawMaterials.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            materials = materials.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (item.Brand?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var items = materials
            .Select(material =>
            {
                var onHand = onHandMap.TryGetValue(material.Id, out var quantity) ? quantity : 0m;
                var minimum = Math.Max(0m, material.MinimumStock);
                return new WarehouseMaterialItem(
                    material.Id,
                    material.Code,
                    material.Name,
                    material.Brand,
                    material.BaseUnit,
                    material.CostPerBaseUnit,
                    onHand,
                    minimum,
                    minimum - onHand,
                    onHand < minimum,
                    material.Status,
                    lastMovementMap.TryGetValue(material.Id, out var lastMovementAt) ? lastMovementAt : null);
            })
            .OrderByDescending(item => item.IsBelowMinimumStock)
            .ThenByDescending(item => item.LastMovementAt.HasValue)
            .ThenByDescending(item => item.LastMovementAt)
            .ThenBy(item => item.Name)
            .ToArray();

        var resolvedSelectedId = selectedMaterialId is Guid requested && items.Any(item => item.MaterialId == requested)
            ? requested
            : items.FirstOrDefault()?.MaterialId;

        return new WarehouseSnapshot(
            items,
            items.Length,
            items.Count(item => item.IsBelowMinimumStock),
            items.Sum(item => item.OnHandQuantity * item.CostPerUnit),
            resolvedSelectedId,
            resolvedSelectedId is Guid materialId ? BuildDetail(materialId) : null);
    }

    private WarehouseMaterialDetail? BuildDetail(Guid materialId)
    {
        var material = store.FindRawMaterial(materialId);
        if (material is null)
        {
            return null;
        }

        var onHand = GetOnHandQuantity(materialId);
        var minimum = Math.Max(0m, material.MinimumStock);
        var summary = new WarehouseMaterialItem(
            material.Id,
            material.Code,
            material.Name,
            material.Brand,
            material.BaseUnit,
            material.CostPerBaseUnit,
            onHand,
            minimum,
            minimum - onHand,
            onHand < minimum,
            material.Status,
            store.StockMovements.Where(item => item.MaterialId == materialId).OrderByDescending(item => item.OccurredAt).Select(item => item.OccurredAt).FirstOrDefault());

        var recentMovements = store.StockMovements
            .Where(item => item.MaterialId == materialId)
            .OrderByDescending(item => item.OccurredAt)
            .Take(12)
            .Select(item => new WarehouseStockMovementItem(item.Id, item.Type, item.Quantity, item.OccurredAt, item.Note, item.RelatedBatchId))
            .ToArray();

        return new WarehouseMaterialDetail(summary, recentMovements);
    }

    private decimal GetOnHandQuantity(Guid materialId)
        => store.StockMovements
            .Where(item => item.MaterialId == materialId)
            .Sum(item => item.Quantity);

    private static decimal NormalizeQuantity(StockMovementType type, decimal quantity)
    {
        return type switch
        {
            StockMovementType.OpeningBalance => Math.Abs(quantity),
            StockMovementType.StockIn => Math.Abs(quantity),
            StockMovementType.StockOut => -Math.Abs(quantity),
            StockMovementType.ProductionUsage => -Math.Abs(quantity),
            StockMovementType.Adjustment => quantity,
            _ => quantity
        };
    }

    private static string GetDefaultMovementNote(StockMovementType type)
        => type switch
        {
            StockMovementType.OpeningBalance => "Saldo awal gudang",
            StockMovementType.StockIn => "Stok masuk gudang",
            StockMovementType.StockOut => "Stok keluar gudang",
            StockMovementType.ProductionUsage => "Pemakaian produksi",
            StockMovementType.Adjustment => "Penyesuaian stok",
            _ => "Mutasi stok"
        };
}
