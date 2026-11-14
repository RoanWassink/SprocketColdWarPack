using System;

namespace SprocketExpandedBallistics;

/// <summary>
/// Stable identities and historically representative fixed magnification for the
/// two additive sight parts. The values are deliberately data, not inferred from
/// whichever sight happens to exist on the vehicle.
/// </summary>
internal readonly record struct SightSpec(
    string Guid, string Key, string DisplayName, float Magnification,
    string Era, string Date, float MassKg, string Source);

internal static class SightCatalog
{
    // Random, stable v4 UUIDs. They are the part-definition GUIDs in native-data/Parts.
    internal const string Ww1Guid = "4f6f0d8e-2b8a-4e1f-9c3a-1d2e3f4a5b60";
    internal const string Ww2Guid = "9a7c1b2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d";

    internal static readonly SightSpec Ww1 = new(
        Ww1Guid, "ww1", "WW1 sight", 2.0f, "WWI", "1917.05.01", 4f,
        "British WW1 Male Tank telescope sight TS No.15 Mk1, x2 (physical artifact)");

    internal static readonly SightSpec Ww2 = new(
        Ww2Guid, "ww2", "WW2 sight", 5.0f, "WW2", "1943.11.01", 12f,
        "German TZF 12a / TZF 9d high-power 5x setting (primary manuals D 655/2, D 2061/1)");

    internal static readonly SightSpec[] All = { Ww1, Ww2 };

    internal static bool TryResolveIdentifier(string? identifier, out SightSpec spec)
    {
        if (TryResolve(identifier, out spec)) return true;
        if (!string.IsNullOrWhiteSpace(identifier))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.DisplayName, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Key, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.Key + "Sight", identifier, StringComparison.OrdinalIgnoreCase))
                {
                    spec = candidate;
                    return true;
                }
            }
        }
        spec = default;
        return false;
    }

    internal static bool TryResolve(string? guid, out SightSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(guid))
        {
            foreach (var candidate in All)
            {
                if (string.Equals(candidate.Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    spec = candidate;
                    return true;
                }
            }
        }
        spec = default;
        return false;
    }

    /// <summary>
    /// Correct first-order magnification mapping for a vertical camera FOV.
    /// The ratio is tan(reference/2) / magnification, not reference / magnification.
    /// </summary>
    internal static float VerticalFieldOfView(float referenceVerticalFovDegrees, float magnification)
    {
        if (!float.IsFinite(referenceVerticalFovDegrees) || referenceVerticalFovDegrees is < 1f or > 170f)
            throw new ArgumentOutOfRangeException(nameof(referenceVerticalFovDegrees));
        if (!float.IsFinite(magnification) || magnification is < 1f or > 20f)
            throw new ArgumentOutOfRangeException(nameof(magnification));
        var half = referenceVerticalFovDegrees * MathF.PI / 360f;
        var result = 2f * MathF.Atan(MathF.Tan(half) / magnification) * 180f / MathF.PI;
        return Math.Clamp(result, .01f, 170f);
    }

    internal static float SafeVerticalFieldOfView(float referenceVerticalFovDegrees, float magnification)
    {
        referenceVerticalFovDegrees = float.IsFinite(referenceVerticalFovDegrees)
            ? Math.Clamp(referenceVerticalFovDegrees, 1f, 170f) : 60f;
        magnification = float.IsFinite(magnification) ? Math.Clamp(magnification, 1f, 20f) : 1f;
        return VerticalFieldOfView(referenceVerticalFovDegrees, magnification);
    }
}
