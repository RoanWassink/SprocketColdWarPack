namespace SprocketThermalSight;

internal static class SavedProfileKeys
{
    internal const string Canonical = "sprocketThermalProfile";
    internal static string? Resolve(IEnumerable<string> names) => names.Contains(Canonical) ? Canonical : null;
}
