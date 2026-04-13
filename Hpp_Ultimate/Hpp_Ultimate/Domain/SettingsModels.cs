using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record BusinessSettings(
    string BusinessName,
    string LegalName,
    string Phone,
    string Email,
    string Address,
    string CurrencyCode,
    string DefaultProductUnit,
    string DefaultMaterialUnit,
    int DefaultPriceRounding,
    decimal TaxPercent,
    bool TaxIncluded,
    string Timezone,
    DateTime UpdatedAt,
    string GeminiApiKey = "");

public sealed record BusinessSettingsSnapshot(
    BusinessSettings Settings,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<string> ProductUnits,
    IReadOnlyList<string> MaterialUnits,
    IReadOnlyList<string> Notes,
    bool CanManageSettings,
    bool CanViewAuditTrail,
    IReadOnlyList<AuditLogEntry> RecentAuditEntries);

public sealed record BusinessSettingsMutationResult(bool Success, string Message, BusinessSettings? Settings = null);

public sealed record BusinessDataResetResult(
    bool Success,
    string Message,
    int ClearedProducts,
    int ClearedMaterials,
    int ClearedStocks,
    int ClearedRecipes,
    int ClearedProductionBatches);

public sealed class BusinessSettingsRequest
{
    [Required(ErrorMessage = "Nama usaha wajib diisi.")]
    public string BusinessName { get; set; } = string.Empty;

    public string LegalName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    public string Email { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mata uang wajib dipilih.")]
    public string CurrencyCode { get; set; } = "IDR";

    [Required(ErrorMessage = "Satuan default produk wajib dipilih.")]
    public string DefaultProductUnit { get; set; } = "pcs";

    [Required(ErrorMessage = "Satuan default bahan wajib dipilih.")]
    public string DefaultMaterialUnit { get; set; } = "kg";

    [Range(1, 10000, ErrorMessage = "Pembulatan harga harus minimal 1.")]
    public int DefaultPriceRounding { get; set; } = 100;

    [Range(0, 100, ErrorMessage = "Pajak harus di antara 0 sampai 100%.")]
    public decimal TaxPercent { get; set; }

    public bool TaxIncluded { get; set; }
    public string Timezone { get; set; } = "Asia/Jakarta";
    public string GeminiApiKey { get; set; } = string.Empty;
}
