using System.Globalization;

namespace SprocketExpandedBallistics.Materials;

internal sealed record MaterialEditorRow(string Id, string Label, string Group, string Behavior, int Index);

internal readonly record struct MaterialEditorCheck(bool Ok, string? Error, double Value);

/// <summary>
/// Pure presentation rules for the UI Kit material editor: the five groups the player sees and the
/// validation used before a field reaches <see cref="MaterialEditorDraft"/>. Kept free of Unity and
/// JSON Editor types so the tests can exercise it without the game.
/// </summary>
internal static class MaterialEditorView
{
    internal static readonly string[] GroupOrder = { "Steel", "Ceramic", "Composite", "ERA", "Cassettes" };
    internal static readonly (string Id, string Label)[] Groups =
    {
        ("Steel", "Steel"), ("Ceramic", "Ceramic"), ("Composite", "Composite"), ("ERA", "ERA"), ("Cassettes", "Cassettes")
    };

    internal static string GroupOf(string id, string label, string behavior)
    {
        if (id.StartsWith("ebCassette_", StringComparison.Ordinal)) return "Cassettes";
        var text = (id + " " + label).ToLowerInvariant();
        static bool Has(string value, params string[] parts) => parts.Any(p => value.Contains(p, StringComparison.Ordinal));
        if (behavior is "lightEra" or "heavyEra") return "ERA";
        if (Has(text, "ceramic", "boron")) return "Ceramic";
        if (Has(text, "composite", "textolite", "aramid", "elastomer", "nera", "dumesh", "glass", "quartz", "varma"))
            return "Composite";
        if (Has(text, "active", "cassette", "reactive", "kontakt", "relikt", "nizh", "duplet", "brat", "arat", "romor", "dyna"))
            return "ERA";
        return "Steel";
    }

    internal static int GroupIndex(string group)
    {
        var index = Array.IndexOf(GroupOrder, group);
        return index < 0 ? GroupOrder.Length : index;
    }

    internal static IReadOnlyList<MaterialEditorRow> Sorted(IEnumerable<MaterialEditorRow> rows) =>
        rows.OrderBy(r => GroupIndex(r.Group))
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();

    internal static MaterialEditorCheck CheckField(string field, string? text)
    {
        text ??= "";
        switch (field)
        {
            case "label":
                if (string.IsNullOrWhiteSpace(text)) return new(false, "Enter a material name.", 0);
                if (text.Length > 80 || text.Any(char.IsControl)) return new(false, "Use 1-80 printable characters.", 0);
                return new(true, null, 0);
            case "date":
                return TryDate(text, out _) ? new(true, null, 0) : new(false, "Use a real date as yyyy.MM.dd.", 0);
            case "rhaFactor":
            case "density":
            case "spallFactor":
            case "costMultiplier":
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    !double.IsFinite(value))
                    return new(false, "Enter a finite number.", 0);
                return field switch
                {
                    "rhaFactor" => value > 0 ? new(true, null, value) : new(false, "RHA factor must be above zero.", value),
                    "density" => value > 0 ? new(true, null, value) : new(false, "Density must be above zero.", value),
                    "spallFactor" => value >= 0 ? new(true, null, value) : new(false, "Spall factor cannot be negative.", value),
                    _ => value >= 0 ? new(true, null, value) : new(false, "Cost multiplier cannot be negative.", value)
                };
            default:
                return new(false, "Unknown field.", 0);
        }
    }

    /// <summary>Cross-field check matching what Save will accept (including the MaterialBalance floor).</summary>
    internal static string? CheckMaterial(string label, string date, string rha, string density, string spall, string cost)
    {
        foreach (var (field, text) in new[] { ("label", label), ("date", date), ("rhaFactor", rha), ("density", density), ("spallFactor", spall), ("costMultiplier", cost) })
            if (CheckField(field, text) is { Ok: false } bad) return bad.Error;
        var r = CheckField("rhaFactor", rha).Value;
        var d = CheckField("density", density).Value;
        var c = CheckField("costMultiplier", cost).Value;
        return MaterialBalance.Calculate((float)r, (float)d, (float)c).IsValid
            ? null : "The passive material values are outside the supported range.";
    }

    internal static bool TryDate(string text, out DateTime date)
    {
        date = default;
        return DateTime.TryParseExact(text, "yyyy.MM.dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    /// <summary>Cost of one square metre at 100 mm actual thickness, in the game's cost-per-kg units.</summary>
    internal static double CostPerSquareMetre(double density, double costMultiplier) =>
        density * 0.1 * costMultiplier;
}
