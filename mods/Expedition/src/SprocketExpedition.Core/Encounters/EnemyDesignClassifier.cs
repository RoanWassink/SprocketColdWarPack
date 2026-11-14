using System.Text.RegularExpressions;

namespace SprocketExpedition.Core.Encounters;

// Pure, deterministic design classification shared by new-catalogue indexing and
// policy-5 planning. Recognisable real designs are classified by NAME first;
// fictional/unknown designs use measured native mass (kg) and era thresholds.
// Price is never a class signal. Unknown name plus unknown mass is neutral Line.
public static class EnemyDesignClassifier
{
    // Word boundaries prevent M41 from matching fictional M410 content.
    private static readonly Regex ReconName = new(
        @"\b(light\s+tank|scout|recon(?:naissance)?|armou?red\s+car|aec|dingo|humber|ferret|saladin|panhard|sdkfz\s*22[124])\b"
        + @"|\b(pt[\s-]?76|amx[\s-]?13|m41|m24|stuart|bt-\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HeavyName = new(
        @"\b(heavy\s+tank|king\s+tiger|tiger|churchill|pershing|conqueror|maus|jagdtiger|tortoise)\b"
        + @"|\b(is|kv|t)[\s-]?(?:1|2|3|4|10|35)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MediumName = new(
        @"\b(medium\s+tank|mbt|main\s+battle\s+tank|leopard|abrams|challenger|centurion|sherman|panther|cromwell|comet|matilda|valentine|pershing|chieftain)\b"
        + @"|\b(m1\s+abrams|m4|m26|m46|m47|m48|m60)\b"
        + @"|\b(t[\s-]?(?:34|44|54|55|62|64|72|80|90))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Historical light/medium boundaries in kilograms. Authored gameplay
    // classification thresholds, not native era definitions or prices.
    private static readonly (string Era, int LightBelow, int HeavyAtLeast)[] MassThresholds =
    {
        ("WWI", 12_000, 28_000),
        ("Interwar", 12_000, 30_000),
        ("Earlywar", 14_000, 32_000),
        ("Midwar", 16_000, 35_000),
        ("Latewar", 18_000, 42_000),
        ("Coldwar", 18_000, 45_000)
    };

    // Historical compatibility path used by pinned v4 catalogue identity. It has
    // no measured mass and therefore never invents a weight class.
    public static EnemyRole Role(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("light tank") || n.Contains("scout") || n.Contains("recon")
            || n.Contains("pt-76") || n.Contains("pt76") || n.Contains("amx-13") || n.Contains("amx 13")
            || n.Contains("m41") || n.Contains("m24") || n.Contains("stuart") || n.StartsWith("bt-")
            ? EnemyRole.Recon : EnemyRole.Line;
    }

    public static EnemyRole Role(string name, double? massKg, string? era) =>
        TryClassify(name, massKg, era, out var role) ? role : EnemyRole.Line;

    // True when a known name token or a valid measured mass supplied the class.
    // This lets a policy-5 planner refine pinned metadata without discarding an
    // explicit authored role for genuinely unknown content.
    public static bool TryClassify(string? name, double? massKg, string? era,
        out EnemyRole role)
    {
        role = EnemyRole.Line;
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (ReconName.IsMatch(name)) { role = EnemyRole.Recon; return true; }
            if (HeavyName.IsMatch(name)) { role = EnemyRole.Heavy; return true; }
            if (MediumName.IsMatch(name)) { role = EnemyRole.Line; return true; }
        }

        if (massKg is not { } mass || !double.IsFinite(mass) || mass <= 0d)
            return false;
        var (lightBelow, heavyAtLeast) = ThresholdFor(era);
        role = mass < lightBelow ? EnemyRole.Recon
            : mass >= heavyAtLeast ? EnemyRole.Heavy
            : EnemyRole.Line;
        return true;
    }

    // Policy-5 planning view of a pinned entry. Existing catalogue Role/Family/
    // Version bytes are never rewritten; only the generated policy-5 roster uses
    // the more specific name/mass evidence when it is available.
    public static EnemyRole PlannedRole(EnemyDesignEntry design)
    {
        if (design is null) return EnemyRole.Line;
        var identity = design.FormationFamily;
        if (string.IsNullOrWhiteSpace(identity)) identity = design.Blueprint?.LocalId;
        return TryClassify(identity, design.MassKg, design.Blueprint?.Era?.NativeId,
            out var refined) ? refined : design.Role;
    }

    public static (int LightBelow, int HeavyAtLeast) ThresholdFor(string? era)
    {
        foreach (var threshold in MassThresholds)
            if (string.Equals(threshold.Era, era, StringComparison.Ordinal))
                return (threshold.LightBelow, threshold.HeavyAtLeast);
        // A later era of the same family ("Coldwar2") inherits that family's mass limits.
        var family = Run.EraSchedulePolicy.FamilyName(era);
        if (family.Length > 0 && !string.Equals(family, era, StringComparison.Ordinal))
            foreach (var threshold in MassThresholds)
                if (string.Equals(threshold.Era, family, StringComparison.Ordinal))
                    return (threshold.LightBelow, threshold.HeavyAtLeast);
        return (15_000, 35_000);
    }
}
