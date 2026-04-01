using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public enum BomCoverageFilter
{
    All,
    Configured,
    Missing
}

public sealed record BomProductListItem(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Category,
    bool HasRecipe,
    int MaterialCount,
    decimal DirectCostPerUnit,
    decimal BatchOutputQuantity,
    decimal YieldPercentage,
    int WarningCount,
    DateTime UpdatedAt);

public sealed record BomQuery(
    string? Search = null,
    BomCoverageFilter Coverage = BomCoverageFilter.All,
    string SortBy = "updated",
    bool Descending = true);

public sealed record BomItemView(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    string? Brand,
    decimal QuantityPerUnit,
    decimal UnitPrice,
    decimal LineCostPerUnit);

public sealed record BomDetail(
    Product Product,
    ProductRecipe Recipe,
    IReadOnlyList<BomItemView> Items,
    decimal DirectMaterialCostPerUnit,
    decimal DirectMaterialCostPerBatch,
    bool HasProduction,
    IReadOnlyList<string> Alerts);

public sealed record BomQueryResult(
    IReadOnlyList<BomProductListItem> Items,
    int ConfiguredCount,
    int MissingCount,
    int CoveragePercentage);

public sealed record BomMutationResult(bool Success, string Message);

public sealed class RecipeMetaRequest
{
    [Range(0.0001, double.MaxValue, ErrorMessage = "Output batch harus lebih besar dari 0.")]
    public decimal BatchOutputQuantity { get; set; } = 1;

    [Range(0.1, 100, ErrorMessage = "Yield harus di antara 0.1 sampai 100.")]
    public decimal YieldPercentage { get; set; } = 100;

    public string? Notes { get; set; }
}

public sealed class BomItemRequest
{
    [Required(ErrorMessage = "Bahan baku wajib dipilih.")]
    public Guid? MaterialId { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "Jumlah pemakaian harus lebih besar dari 0.")]
    public decimal QuantityPerUnit { get; set; }
}
