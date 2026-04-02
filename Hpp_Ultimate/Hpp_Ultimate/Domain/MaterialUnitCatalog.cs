namespace Hpp_Ultimate.Domain;

public sealed record MaterialUnitOption(string Value, string Label, string Family, decimal RatioToCanonical);

public static class MaterialUnitCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["gram"] = "gr",
        ["grams"] = "gr",
        ["kilogram"] = "kg",
        ["kilograms"] = "kg",
        ["milliliter"] = "ml",
        ["milliliters"] = "ml",
        ["liter"] = "l",
        ["liters"] = "l",
        ["piece"] = "pcs",
        ["pieces"] = "pcs",
        ["pc"] = "pcs"
    };

    public static readonly IReadOnlyList<MaterialUnitOption> Units =
    [
        new("gr", "Gram (gr)", "mass", 1m),
        new("kg", "Kilogram (kg)", "mass", 1000m),
        new("ml", "Mililiter (ml)", "volume", 1m),
        new("l", "Liter (l)", "volume", 1000m),
        new("pcs", "Pieces (pcs)", "count", 1m),
        new("lusin", "Lusin", "count", 12m),
        new("box", "Box", "count", 1m)
    ];

    public static IReadOnlyList<MaterialUnitOption> BaseUnits =>
        Units.Where(item => item.Value is "gr" or "ml" or "pcs").ToArray();

    public static IReadOnlyList<MaterialUnitOption> GetCompatibleUnits(string baseUnit)
    {
        var family = GetFamily(baseUnit);
        return family is null
            ? []
            : Units.Where(item => item.Family.Equals(family, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static bool AreCompatible(string leftUnit, string rightUnit)
        => string.Equals(GetFamily(leftUnit), GetFamily(rightUnit), StringComparison.OrdinalIgnoreCase);

    public static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        var normalized = unit.Trim().ToLowerInvariant();
        return Aliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }

    public static string? GetFamily(string unit)
        => TryGetOption(unit)?.Family;

    public static decimal Convert(decimal value, string fromUnit, string toUnit)
    {
        if (value == 0)
        {
            return 0m;
        }

        var normalizedFrom = NormalizeUnit(fromUnit);
        var normalizedTo = NormalizeUnit(toUnit);
        if (string.IsNullOrWhiteSpace(normalizedFrom) || string.IsNullOrWhiteSpace(normalizedTo))
        {
            return 0m;
        }

        if (normalizedFrom.Equals(normalizedTo, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (!AreCompatible(fromUnit, toUnit))
        {
            return 0m;
        }

        var from = TryGetOption(normalizedFrom);
        var to = TryGetOption(normalizedTo);
        if (from is null || to is null)
        {
            return 0m;
        }

        return value * from.RatioToCanonical / to.RatioToCanonical;
    }

    private static MaterialUnitOption? TryGetOption(string? unit)
    {
        var normalized = NormalizeUnit(unit);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : Units.FirstOrDefault(item => item.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
