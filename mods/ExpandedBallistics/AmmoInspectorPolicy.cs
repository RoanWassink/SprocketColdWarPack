namespace SprocketExpandedBallistics;

/// <summary>
/// Pure presentation rules for the merged shell-profile and ammunition inspector.
/// The saved design shell selection and the carried mix's starting type are the
/// same value, so the unified section never keeps a second independent selector.
/// </summary>
internal static class AmmoInspectorPolicy
{
    internal const string SectionTitle = "Shell profile and ammunition";

    /// <summary>
    /// Which parts of the merged inspector are available. The section is shown when
    /// either subsystem can contribute controls; shell and ammunition controls keep
    /// their own independent enable/ready gates.
    /// </summary>
    internal readonly record struct InspectorVisibility(bool ShowSection, bool ShellControls, bool AmmoControls);

    internal static InspectorVisibility Visibility(bool shellEnabled, bool ammoEnabled, bool ammoReady)
    {
        var ammo = ammoEnabled && ammoReady;
        return new(shellEnabled || ammo, shellEnabled, ammo);
    }

    /// <summary>Carried rows that can actually supply a round, in user order.</summary>
    internal static IReadOnlyList<AmmoLoadoutEntry> Active(IReadOnlyList<AmmoLoadoutEntry> entries) =>
        entries.Where(e => e.Count > 0).ToArray();

    internal static bool HasActiveMix(IReadOnlyList<AmmoLoadoutEntry> entries) =>
        entries.Any(e => e.Count > 0);

    /// <summary>
    /// The carried type the design prefers. A saved choice that is still carried
    /// wins; otherwise the first non-empty row is the starting type. An empty mix
    /// has no starting type and leaves the ordinary saved shell selection alone.
    /// </summary>
    internal static string? StartingProfile(IReadOnlyList<AmmoLoadoutEntry> entries, string? stored) =>
        entries.FirstOrDefault(e => e.Count > 0 && e.ProfileId == stored)?.ProfileId
        ?? entries.FirstOrDefault(e => e.Count > 0)?.ProfileId;

    /// <summary>Index in <see cref="Active"/>, or -1 when the mix is empty.</summary>
    internal static int StartingIndex(IReadOnlyList<AmmoLoadoutEntry> active, string? starting)
    {
        for (var i = 0; i < active.Count; i++)
            if (active[i].ProfileId == starting) return i;
        return active.Count == 0 ? -1 : 0;
    }

    /// <summary>
    /// "Add shell type" (0.22.3). The first click on a design without a mix turns the native
    /// selection into the first row with every round it has now, and adds the new type with
    /// 0 rounds: one click gives two types. Later clicks append the new type with 0 rounds.
    /// Adding the type the native selection already uses gives that single row.
    /// </summary>
    internal static IReadOnlyList<AmmoLoadoutEntry> Add(IReadOnlyList<AmmoLoadoutEntry> current, string? nativeSelection, string added, int capacity)
    {
        if (current.Any(e => e.ProfileId == added) || current.Count >= AmmoLoadoutRules.MaximumTypes) return current;
        var next = current.ToList();
        if (next.Count == 0)
        {
            var native = string.IsNullOrEmpty(nativeSelection) ? ShellProfiles.Vanilla : nativeSelection!;
            next.Add(new AmmoLoadoutEntry(native, Math.Max(0, capacity)));
            if (native == added) return next;
        }
        next.Add(new AmmoLoadoutEntry(added, 0));
        return next;
    }

    /// <summary>
    /// Keeps the native shell selection and the carried mix from contradicting each other.
    /// A mix with rounds rules: the saved selection becomes its starting type. A mix without
    /// rounds is inactive (the native type loads): a single row follows the native type.
    /// </summary>
    internal static (IReadOnlyList<AmmoLoadoutEntry> Entries, string? Selection) Reconcile(IReadOnlyList<AmmoLoadoutEntry> entries, string? nativeSelection)
    {
        if (entries.Count == 0) return (entries, nativeSelection);
        if (AmmoLoadoutRules.Total(entries) == 0)
        {
            if (entries.Count == 1 && !string.IsNullOrEmpty(nativeSelection) && entries[0].ProfileId != nativeSelection)
                return (new[] { entries[0] with { ProfileId = nativeSelection! } }, nativeSelection);
            return (entries, nativeSelection);
        }
        return (entries, StartingProfile(entries, nativeSelection) ?? nativeSelection);
    }

    /// <summary>Carried types the vehicle date does not allow: they are skipped in battle and named in the inspector and HUD.</summary>
    internal static IReadOnlyList<string> Disallowed(IReadOnlyList<AmmoLoadoutEntry> entries, Func<string, bool> allowed) =>
        entries.Where(e => e.Count > 0 && e.ProfileId != ShellProfiles.Vanilla && !allowed(e.ProfileId)).Select(e => e.ProfileId).ToArray();
}

/// <summary>
/// Stable identity for the global damage-feed actions. Kept free of Unity and
/// Keybinds types so registration metadata and saved action IDs can be checked
/// without loading either optional runtime dependency.
/// </summary>
internal static class DamageFeedKeyPolicy
{
    internal const string ToggleAction = "damage-feed-toggle";
    internal const string CornerAction = "damage-feed-cycle-corner";
    internal static readonly string[] Actions = { ToggleAction, CornerAction };
    internal static readonly string[] Labels = { "Toggle damage feed in Play", "Cycle damage feed corner" };
}
