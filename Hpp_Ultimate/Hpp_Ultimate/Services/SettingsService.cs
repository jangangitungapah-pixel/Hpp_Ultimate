using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class SettingsService(
    IMemoryCache cache,
    SeededBusinessDataStore store,
    WorkspaceAccessService access,
    AuditTrailService auditTrail)
{
    public static readonly string[] ProductUnits = ["pcs", "pack", "botol", "pouch", "kg", "gram", "liter", "ml"];
    public static readonly string[] MaterialUnits = ["kg", "gram", "liter", "ml", "pcs", "pak", "box", "botol", "pouch"];
    public static readonly string[] Currencies = ["IDR", "USD", "SGD", "MYR"];

    public async Task<BusinessSettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAuthenticated();
        if (!accessDecision.Allowed)
        {
            throw new InvalidOperationException(accessDecision.Message);
        }

        var actor = accessDecision.Actor!;
        var cacheKey = $"settings:{store.Version}:{actor.Id}:{actor.Role}";

        if (cache.TryGetValue(cacheKey, out BusinessSettingsSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(80, cancellationToken);

        snapshot = BuildSnapshot(actor);
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<BusinessSettingsMutationResult> SaveAsync(BusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            if (accessDecision.Actor is not null)
            {
                auditTrail.Record(accessDecision.Actor, "Security", "Akses ditolak", "Pengaturan bisnis", null, accessDecision.Message);
            }

            return Task.FromResult(new BusinessSettingsMutationResult(false, accessDecision.Message));
        }

        var actor = accessDecision.Actor!;
        if (string.IsNullOrWhiteSpace(request.BusinessName))
        {
            return Task.FromResult(new BusinessSettingsMutationResult(false, "Nama usaha wajib diisi."));
        }

        if (request.TaxPercent < 0 || request.TaxPercent > 100)
        {
            return Task.FromResult(new BusinessSettingsMutationResult(false, "Pajak harus di antara 0 sampai 100%."));
        }

        if (request.DefaultPriceRounding <= 0)
        {
            return Task.FromResult(new BusinessSettingsMutationResult(false, "Pembulatan harga harus lebih besar dari 0."));
        }

        var current = store.GetBusinessSettings();
        var updated = current with
        {
            BusinessName = request.BusinessName.Trim(),
            LegalName = request.LegalName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            Address = request.Address.Trim(),
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            DefaultProductUnit = request.DefaultProductUnit.Trim(),
            DefaultMaterialUnit = request.DefaultMaterialUnit.Trim(),
            DefaultPriceRounding = request.DefaultPriceRounding,
            TaxPercent = request.TaxPercent,
            TaxIncluded = request.TaxIncluded,
            Timezone = request.Timezone.Trim(),
            UpdatedAt = DateTime.Now
        };

        store.UpdateBusinessSettings(updated);
        auditTrail.Record(actor, "Settings", "Simpan pengaturan", updated.BusinessName, null, "Pengaturan bisnis dasar diperbarui.");
        return Task.FromResult(new BusinessSettingsMutationResult(true, "Pengaturan berhasil disimpan.", updated));
    }

    public Task<BusinessDataResetResult> ClearOperationalDataAsync(CancellationToken cancellationToken = default)
    {
        var accessDecision = access.RequireAdmin();
        if (!accessDecision.Allowed)
        {
            if (accessDecision.Actor is not null)
            {
                auditTrail.Record(accessDecision.Actor, "Security", "Akses ditolak", "Clear data", null, accessDecision.Message);
            }

            return Task.FromResult(new BusinessDataResetResult(false, accessDecision.Message, 0, 0, 0, 0, 0));
        }

        var actor = accessDecision.Actor!;
        cancellationToken.ThrowIfCancellationRequested();

        var cleared = store.ClearOperationalData();
        var message = $"Semua data operasional berhasil dibersihkan. Produk: {cleared.Products}, material: {cleared.Materials}, stok: {cleared.StockMovements}, resep: {cleared.Recipes}, produksi: {cleared.ProductionBatches}.";
        auditTrail.Record(actor, "Settings", "Clear data", "Data operasional", null, message);

        return Task.FromResult(new BusinessDataResetResult(
            true,
            message,
            cleared.Products,
            cleared.Materials,
            cleared.StockMovements,
            cleared.Recipes,
            cleared.ProductionBatches));
    }

    private BusinessSettingsSnapshot BuildSnapshot(BusinessUser actor)
    {
        var settings = store.GetBusinessSettings();
        var notes = new List<string>
        {
            $"Mata uang utama saat ini {settings.CurrencyCode} untuk seluruh tampilan biaya.",
            $"Pembulatan harga jual default memakai kelipatan {settings.DefaultPriceRounding:N0}.",
            settings.TaxIncluded
                ? $"Pajak {settings.TaxPercent:0.#}% dihitung sebagai include tax."
                : $"Pajak {settings.TaxPercent:0.#}% masih di mode exclude tax."
        };
        var canManageSettings = actor.Role == UserRole.Admin;
        if (!canManageSettings)
        {
            notes.Add("Mode staff bersifat read-only untuk pengaturan usaha dan clear data.");
        }

        var canViewAuditTrail = canManageSettings;
        var recentAuditEntries = canViewAuditTrail
            ? auditTrail.GetRecent(10)
            : Array.Empty<AuditLogEntry>();

        return new BusinessSettingsSnapshot(
            settings,
            Currencies,
            ProductUnits,
            MaterialUnits,
            notes,
            canManageSettings,
            canViewAuditTrail,
            recentAuditEntries);
    }
}
