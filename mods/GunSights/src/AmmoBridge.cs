using System.Reflection;
using Sprocket.Vehicles.Cannons;

namespace SprocketGunSights;

/// <summary>
/// Optional read-only link to Sprocket Expanded Ballistics' public
/// BallisticsAmmoApi: the round in the gun, the next selected type and the
/// remaining count per type. Without that mod this shows nothing.
/// </summary>
internal static class AmmoBridge
{
    private static MethodInfo? tryGetState;
    private static bool searched;
    private static int warnings;

    internal static IEnumerable<string> Lines(Cannon? gun)
    {
        if (gun == null || !Find()) yield break;
        object?[] args = { gun, null };
        object? state;
        try { if (tryGetState!.Invoke(null, args) is not true) yield break; state = args[1]; }
        catch (Exception ex) { if (warnings++ < 3) Plugin.Log.LogWarning("[Gun sights] Ammo link: " + ex.Message); yield break; }
        if (state == null) yield break;
        if (Get<bool>(state, "Frozen")) { yield return "Ammo  unavailable"; yield break; }

        var loaded = Get<string>(state, "ChamberedLabel");
        var next = Get<string>(state, "NextLabel");
        yield return "Loaded  " + (Get<bool>(state, "ChamberedConfirmed") && loaded != null ? loaded : "-");
        if (next != null && next != loaded) yield return "Next  " + next;
        if (Get<System.Collections.IEnumerable>(state, "Rounds") is { } rounds)
        {
            var parts = new List<string>();
            foreach (var round in rounds)
            {
                var code = Get<string>(round, "Code") ?? Get<string>(round, "Label") ?? "?";
                var marker = Get<bool>(round, "Chambered") ? "*" : Get<bool>(round, "Requested") ? ">" : "";
                parts.Add($"{marker}{code} {Get<int>(round, "Rounds")}");
            }
            if (parts.Count > 1) yield return string.Join("   ", parts);
        }
    }

    private static bool Find()
    {
        if (!searched)
        {
            searched = true;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("SprocketExpandedBallistics.BallisticsAmmoApi", false)).FirstOrDefault(t => t != null);
            tryGetState = type?.GetMethod("TryGetState", BindingFlags.Public | BindingFlags.Static);
            Plugin.Log.LogInfo(tryGetState != null
                ? "[Gun sights] Expanded Ballistics ammunition shown in the sight."
                : "[Gun sights] Expanded Ballistics not found; no ammunition line.");
        }
        return tryGetState != null;
    }

    private static T? Get<T>(object source, string property)
    {
        var value = source.GetType().GetProperty(property)?.GetValue(source);
        return value is T typed ? typed : default;
    }
}
