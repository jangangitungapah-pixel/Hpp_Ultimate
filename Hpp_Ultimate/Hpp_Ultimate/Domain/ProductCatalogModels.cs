using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public sealed record ProductQuery(
    string? Search = null,
    string? Category = null,
    ProductStatus? Status = null,
    string SortBy = "updated",
    bool Descending = true,
    int Page = 1,
    int PageSize = 10);

public sealed record ProductListItem(
    Guid Id,
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal SellingPrice,
    ProductStatus Status,
    DateTime UpdatedAt,
    bool HasBom,
    bool HasProduction,
    decimal? LastHpp);

public sealed record ProductDetail(
    Product Product,
    bool HasBom,
    bool HasProduction,
    decimal? LastHpp,
    int ProductionCount,
    int BomItemCount,
    IReadOnlyList<string> BomMaterials,
    IReadOnlyList<string> RecentBatchCodes);

public sealed record ProductQueryResult(
    IReadOnlyList<ProductListItem> Items,
    IReadOnlyList<string> Categories,
    int TotalCount,
    int Page,
    int PageSize,
    string SuggestedNextCode);

public sealed record ProductMutationResult(bool Success, string Message, Product? Product = null, bool HasProduction = false);

public sealed class ProductUpsertRequest
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Nama produk wajib diisi.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kode produk wajib diisi.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kategori wajib dipilih.")]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "Satuan wajib dipilih.")]
    public string Unit { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Harga jual tidak boleh negatif.")]
    public decimal SellingPrice { get; set; }

    public string Description { get; set; } = string.Empty;

    public ProductStatus Status { get; set; } = ProductStatus.Active;
}
