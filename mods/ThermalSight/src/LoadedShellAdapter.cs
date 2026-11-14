using System.Collections;
using System.Reflection;

namespace SprocketThermalSight;

// Reflects the installed optional API without a load-order dependency.
public sealed class LoadedShellAdapter
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly MethodInfo? singleProfile, ammoState, hasMix;
    private readonly PropertyInfo? profiles;
    private Type? speedOwner;
    private PropertyInfo? speed;
    // EB 0.22.17+: muzzle speed (m/s) of the confirmed chambered round from the
    // last SuspendReason call; null means the caller keeps Cannon.muzzleVelocity.
    public double? ChamberedSpeed { get; private set; }
    public LoadedShellAdapter(Type selection, Type? ammoApi)
    {
        singleProfile = selection.GetMethod("CannonProfile", Flags);
        profiles = selection.GetProperty("Profiles", Flags);
        ammoState = ammoApi?.GetMethod("TryGetState", Flags);
        hasMix = ammoApi?.GetMethod("HasCarriedMix", Flags);
    }
    public bool MustSuspend(object cannon) => SuspendReason(cannon) != null;

    // Null when the round in (or about to enter) the gun is provably unguided;
    // otherwise a short diagnostic reason for suspending automatic correction.
    public string? SuspendReason(object cannon)
    {
        ChamberedSpeed = null;
        if (ammoState == null || hasMix == null) return "ammo-api-missing";
        object? profile = null;
        object?[] args = { cannon, null };
        if (ammoState.Invoke(null, args) is true && args[1] is {} state)
        {
            var type = state.GetType();
            if (type.GetProperty("Frozen")?.GetValue(state) is not false) return "mix-frozen";
            var id = type.GetProperty("ChamberedProfileId")?.GetValue(state) as string;
            if (type.GetProperty("ChamberedConfirmed")?.GetValue(state) is not true)
                // Empty, reloading or just switched: nothing guided can be loaded
                // when every carried type is unguided.
                return CarriedUnguided(type, state) ? null : "chamber-unconfirmed";
            ChamberedSpeed = Speed(type, state);
            if (id == "vanilla") return null;
            if (string.IsNullOrWhiteSpace(id)) return "chamber-unknown";
            profile = Find(id);
            if (profile == null) return "chamber-unknown:" + id;
        }
        else
        {
            // A configured mix with no confirmed snapshot must never use InitialProfile.
            if (hasMix.Invoke(null, new[] { cannon }) is not false) return "mix-without-snapshot";
            if (singleProfile == null) return "profile-api-missing";
            profile = singleProfile.Invoke(null, new[] { cannon });
        }
        if (profile == null) return null; // Ordinary vanilla cannon, no carried mix.
        return Guided(profile) ? "guided:" + (profile.GetType().GetProperty("Id")?.GetValue(profile) as string) : null;
    }
    // Older EB has no such property; absent, null, non-finite or < 50 m/s values
    // fall back to the cannon. The lookup is cached per state type.
    private double? Speed(Type type, object state)
    {
        if (type != speedOwner) { speedOwner = type; speed = type.GetProperty("ChamberedMuzzleVelocityMps"); }
        return speed?.GetValue(state) is double v && double.IsFinite(v) && v >= 50 ? v : null;
    }
    private object? Find(string id)
    {
        if (profiles?.GetValue(null) is IEnumerable catalog)
            foreach (var item in catalog)
                if (item?.GetType().GetProperty("Id")?.GetValue(item) as string == id) return item;
        return null;
    }
    private static bool Guided(object profile)
    {
        var behavior = profile.GetType().GetProperty("Behavior")?.GetValue(profile) as string;
        return behavior == null || behavior.StartsWith("atgm", StringComparison.OrdinalIgnoreCase);
    }
    // Every carried and requested type must be known and unguided; an empty or
    // unreadable list fails closed.
    private bool CarriedUnguided(Type type, object state)
    {
        if (type.GetProperty("Rounds")?.GetValue(state) is not IEnumerable rounds) return false;
        var any = false;
        foreach (var round in rounds)
        {
            any = true;
            if (!Unguided(round?.GetType().GetProperty("ProfileId")?.GetValue(round) as string)) return false;
        }
        var next = type.GetProperty("NextProfileId")?.GetValue(state) as string;
        return any && (next == null || Unguided(next));
    }
    private bool Unguided(string? id)
        => id == "vanilla" || (!string.IsNullOrWhiteSpace(id) && Find(id) is {} profile && !Guided(profile));
}
