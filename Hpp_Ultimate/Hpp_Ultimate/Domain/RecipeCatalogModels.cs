using System.ComponentModel.DataAnnotations;

namespace Hpp_Ultimate.Domain;

public enum RecipeStatus
{
    Draft,
    Active
}

public enum RecipeCostType
{
    Overhead,
    Production
}

public static class RecipePortionUnitCatalog
{
    private static readonly string[] _options =
    [
        "pcs",
        "pouch",
        "box",
        "pack",
        "cup",
        "bottle",
        "tray"
    ];

    public static IReadOnlyList<string> Options => _options;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "pcs";
        }

        var normalized = value.Trim().ToLowerInvariant();
        var matched = _options.FirstOrDefault(item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(matched) ? normalized : matched;
    }
}

public sealed record RecipeBook(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal OutputQuantity,
    string OutputUnit,
    RecipeStatus Status,
    IReadOnlyList<RecipeMaterialGroup> Groups,
    IReadOnlyList<RecipeCostComponent> Costs,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    decimal PortionYield = 1m,
    string PortionUnit = "pcs",
    decimal TargetMarginPercent = 0m,
    decimal SuggestedSellingPrice = 0m);

public sealed record RecipeMaterialGroup(
    Guid Id,
    string Name,
    string? Notes,
    IReadOnlyList<RecipeMaterialLine> Materials);

public sealed record RecipeMaterialLine(
    Guid Id,
    Guid MaterialId,
    decimal Quantity,
    string Unit,
    decimal WastePercent,
    string? Notes);

public sealed record RecipeCostComponent(
    Guid Id,
    RecipeCostType Type,
    string Name,
    decimal Amount,
    string? Notes);

public sealed record RecipeListItem(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal OutputQuantity,
    string OutputUnit,
    RecipeStatus Status,
    int GroupCount,
    int MaterialCount,
    int CostItemCount,
    decimal MaterialCost,
    decimal OperationalCost,
    decimal TotalCost,
    decimal CostPerOutputUnit,
    decimal PortionYield,
    string PortionUnit,
    decimal CostPerPortion,
    IReadOnlyList<string> GroupNames,
    DateTime UpdatedAt,
    decimal TargetMarginPercent,
    decimal SuggestedSellingPrice);

public sealed record RecipeQueryResult(
    IReadOnlyList<RecipeListItem> Items,
    int TotalCount,
    string SuggestedNextCode);

public sealed record RecipeMutationResult(
    bool Success,
    string Message,
    RecipeBook? Recipe = null);

public sealed class RecipeUpsertRequest
{
    public Guid? Id { get; set; }

    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nama resep wajib diisi.")]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(0.0001, double.MaxValue, ErrorMessage = "Output batch harus lebih besar dari 0.")]
    public decimal OutputQuantity { get; set; } = 1m;

    [Required(ErrorMessage = "Satuan output wajib diisi.")]
    public string OutputUnit { get; set; } = "pcs";

    [Range(0.0001, double.MaxValue, ErrorMessage = "Jumlah porsi harus lebih besar dari 0.")]
    public decimal PortionYield { get; set; } = 1m;

    [Required(ErrorMessage = "Satuan porsi wajib dipilih.")]
    public string PortionUnit { get; set; } = "pcs";

    [Range(0, 500, ErrorMessage = "Margin harus di antara 0 sampai 500.")]
    public decimal TargetMarginPercent { get; set; }

    public decimal SuggestedSellingPrice { get; set; }

    public RecipeStatus Status { get; set; } = RecipeStatus.Active;

    public List<RecipeGroupInput> Groups { get; set; } = [];

    public List<RecipeCostInput> Costs { get; set; } = [];
}

public sealed class RecipeGroupInput
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public List<RecipeMaterialInput> Materials { get; set; } = [];
}

public sealed class RecipeMaterialInput
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? MaterialId { get; set; }

    public decimal Quantity { get; set; } = 1m;

    public string Unit { get; set; } = string.Empty;

    public decimal WastePercent { get; set; }

    public string? Notes { get; set; }
}

public sealed class RecipeCostInput
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public RecipeCostType Type { get; set; } = RecipeCostType.Overhead;

    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Notes { get; set; }
}

public sealed record RecipeSummaryTotals(
    int GroupCount,
    int MaterialCount,
    int CostItemCount,
    decimal PortionYield,
    string PortionUnit,
    decimal MaterialCost,
    decimal OperationalCost,
    decimal TotalCost,
    decimal CostPerOutputUnit,
    decimal CostPerPortion,
    decimal TargetMarginPercent,
    decimal SuggestedSellingPrice);

public static class RecipeCatalogMath
{
    public static IReadOnlyList<string> GetAvailableUnits(RawMaterial material)
    {
        var units = MaterialUnitCatalog.GetCompatibleUnits(material.BaseUnit)
            .Select(item => item.Value)
            .Concat(material.UnitConversions.Select(item => item.UnitName.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return units;
    }

    public static decimal ConvertToBaseQuantity(decimal quantity, string unit, RawMaterial material)
    {
        if (quantity <= 0 || string.IsNullOrWhiteSpace(unit))
        {
            return 0m;
        }

        var normalizedUnit = MaterialUnitCatalog.NormalizeUnit(unit);
        if (normalizedUnit.Equals(material.BaseUnit, StringComparison.OrdinalIgnoreCase))
        {
            return quantity;
        }

        if (MaterialUnitCatalog.AreCompatible(normalizedUnit, material.BaseUnit))
        {
            return MaterialUnitCatalog.Convert(quantity, normalizedUnit, material.BaseUnit);
        }

        var custom = material.UnitConversions.FirstOrDefault(item =>
            item.UnitName.Equals(unit.Trim(), StringComparison.OrdinalIgnoreCase));

        return custom is null ? 0m : quantity * custom.ConversionQuantity;
    }

    public static decimal CalculateLineCost(RecipeMaterialLine line, RawMaterial material)
    {
        var baseQuantity = ConvertToBaseQuantity(line.Quantity, line.Unit, material);
        if (baseQuantity <= 0)
        {
            return 0m;
        }

        return material.CostPerBaseUnit * baseQuantity * (1m + Math.Max(0m, line.WastePercent) / 100m);
    }

    public static decimal CalculateLineCost(RecipeMaterialInput line, RawMaterial material)
    {
        var baseQuantity = ConvertToBaseQuantity(line.Quantity, line.Unit, material);
        if (baseQuantity <= 0)
        {
            return 0m;
        }

        return material.CostPerBaseUnit * baseQuantity * (1m + Math.Max(0m, line.WastePercent) / 100m);
    }
}
