using Microsoft.Extensions.Caching.Memory;
using Hpp_Ultimate.Domain;

namespace Hpp_Ultimate.Services;

public sealed class SettingsService(IMemoryCache cache, SeededBusinessDataStore store)
{
    public static readonly string[] ProductUnits = ["pcs", "pack", "botol", "pouch", "kg", "gram", "liter", "ml"];
    public static readonly string[] MaterialUnits = ["kg", "gram", "liter", "ml", "pcs", "pak", "box", "botol", "pouch"];
    public static readonly string[] Currencies = ["IDR", "USD", "SGD", "MYR"];

    public async Task<BusinessSettingsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = $"settings:{store.Version}";

        if (cache.TryGetValue(cacheKey, out BusinessSettingsSnapshot? snapshot))
        {
            return snapshot!;
        }

        await Task.Delay(80, cancellationToken);

        snapshot = BuildSnapshot();
        cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(20));
        return snapshot;
    }

    public Task<BusinessSettingsMutationResult> SaveAsync(BusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
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
        return Task.FromResult(new BusinessSettingsMutationResult(true, "Pengaturan berhasil disimpan.", updated));
    }

    private BusinessSettingsSnapshot BuildSnapshot()
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

        return new BusinessSettingsSnapshot(settings, Currencies, ProductUnits, MaterialUnits, notes);
    }
}
