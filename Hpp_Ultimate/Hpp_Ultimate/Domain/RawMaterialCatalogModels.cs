using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record RawMaterialQuery(
    string? Search = null,
    MaterialStatus? Status = null,
    string SortBy = "updated",
    bool Descending = true,
    int Page = 1,
    int PageSize = 10);

public sealed record RawMaterialListItem(
    Guid Id,
    string Code,
    string Name,
    string? Brand,
    string BaseUnit,
    decimal NetQuantity,
    string NetUnit,
    decimal PricePerPack,
    decimal CostPerUnit,
    int ConversionCount,
    MaterialStatus Status,
    DateTime UpdatedAt,
    bool UsedInBom,
    bool UsedInProduction);

public sealed record RawMaterialPriceHistoryItem(
    DateTime ChangedAt,
    decimal PricePerPack,
    decimal CostPerUnit,
    decimal NetQuantity,
    string NetUnit,
    string Note);

public sealed record RawMaterialDetail(
    RawMaterial Material,
    bool UsedInBom,
    bool UsedInProduction,
    IReadOnlyList<string> BomProducts,
    IReadOnlyList<RawMaterialPriceHistoryItem> PriceHistory);

public sealed record RawMaterialQueryResult(
    IReadOnlyList<RawMaterialListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    string SuggestedNextCode);

public sealed record RawMaterialMutationResult(
    bool Success,
    string Message,
    RawMaterial? Material = null,
    bool UsedInBom = false,
    bool UsedInProduction = false,
    bool WasDeleted = false);

public sealed class RawMaterialUnitConversionInput
{
    [Required(ErrorMessage = "Nama unit wajib diisi.")]
    public string UnitName { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "Nilai konversi harus lebih besar dari 0.")]
    public decimal ConversionQuantity { get; set; }
}

public sealed class RawMaterialUpsertRequest
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nama material wajib diisi.")]
    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }

    [Required(ErrorMessage = "Base unit wajib dipilih.")]
    public string BaseUnit { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "Netto qty harus lebih besar dari 0.")]
    public decimal NetQuantity { get; set; }

    [Required(ErrorMessage = "Netto unit wajib dipilih.")]
    public string NetUnit { get; set; } = string.Empty;

    [Range(0.0001, double.MaxValue, ErrorMessage = "Harga per pack harus lebih besar dari 0.")]
    public decimal PricePerPack { get; set; }

    public List<RawMaterialUnitConversionInput> UnitConversions { get; set; } = [];

    public string? Description { get; set; }

    public MaterialStatus Status { get; set; } = MaterialStatus.Active;
}

public sealed record RawMaterialImportPreviewRow(
    int RowNumber,
    string Name,
    string? Brand,
    string BaseUnit,
    decimal? NetQuantity,
    string NetUnit,
    decimal? PricePerPack,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public decimal CostPerUnit
    {
        get
        {
            if (NetQuantity is null || NetQuantity <= 0 || PricePerPack is null)
            {
                return 0m;
            }

            var normalized = MaterialUnitCatalog.Convert(NetQuantity.Value, NetUnit, BaseUnit);
            return normalized <= 0 ? 0m : PricePerPack.Value / normalized;
        }
    }
}

public sealed record RawMaterialImportPreviewResult(
    string FileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<RawMaterialImportPreviewRow> Rows,
    int ValidRowCount,
    int ErrorRowCount);

public sealed record RawMaterialImportResult(
    bool Success,
    string Message,
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> Errors);

public sealed class RawMaterialImportCommitRequest
{
    public List<RawMaterialImportPreviewRow> Rows { get; set; } = [];

    public bool SkipInvalidRows { get; set; } = true;
}
