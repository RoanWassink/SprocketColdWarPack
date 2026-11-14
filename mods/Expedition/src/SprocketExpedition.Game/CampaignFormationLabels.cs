using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Game;

// Conservative named-content labels. Classification itself is the shared pure
// Core classifier so new catalogue identity and policy-5 planning cannot drift.
internal static class CampaignFormationLabels
{
    private static readonly string[] KnownFamilies =
    {
        "Leopard", "Abrams", "Challenger", "Centurion", "Sherman", "Panther",
        "Tiger", "Churchill", "Comet", "Cromwell", "Matilda", "Valentine",
        "Pershing", "Chieftain", "Conqueror", "AEC", "Panhard", "SdKfz"
    };

    // Legacy two-argument-free path retained for existing pinned catalogue
    // identity and tests. It deliberately has no invented mass classification.
    internal static EnemyRole Role(string name) => EnemyDesignClassifier.Role(name);

    internal static EnemyRole Role(string name, double? massKg, string? era) =>
        EnemyDesignClassifier.Role(name, massKg, era);

    internal static (int LightBelow, int HeavyAtLeast) ThresholdFor(string? era) =>
        EnemyDesignClassifier.ThresholdFor(era);

    internal static string Family(string name)
    {
        foreach (var family in KnownFamilies)
            if (name.Contains(family, StringComparison.OrdinalIgnoreCase)) return family;

        return SafeLabel(name);
    }

    internal static string Family(string name, double? massKg, string? era)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var role = EnemyDesignClassifier.Role(name, massKg, era);
            return role switch
            {
                EnemyRole.Recon => "Recon",
                EnemyRole.Heavy => "Heavy",
                _ => "Armoured"
            };
        }

        return Family(name);
    }

    private static string SafeLabel(string name)
    {
        // Unknown content remains its own exact design family, but the catalogue
        // validator requires a short control-free local label.
        var safe = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe)) return "Armoured";
        return safe.Length > ContentSafety.MaxReferenceLength
            ? safe[..ContentSafety.MaxReferenceLength].TrimEnd()
            : safe;
    }
}
