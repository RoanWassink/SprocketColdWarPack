using System.Numerics;

namespace SprocketExpedition.Game;

// Pure combined-arms doctrine shared by native spawning and the runtime hunt AI.
// Authored roles come from pinned catalogue metadata; an unknown or missing
// authored role is neutral (Center), never inferred from cost. A friendly force
// may still receive a tactical flank lane for cohesion, which is not a claim
// about its design class.
internal static class CampaignGroupDoctrine
{
    internal const string Center = "Center";
    internal const string Flank = "Flank";
    internal const string Support = "Support";
    internal const float MinimumSpacing = 24.5f;
    internal const float EngagementLegSeconds = 6f;
    internal const float EngagementLegCooldown = 6f;

    internal static string Normalize(string? role) => role switch
    {
        Flank => Flank,
        Support => Support,
        _ => Center
    };

    internal static int Side(int slot) => slot % 2 == 0 ? -1 : 1;

    // No authored role: unmeasured enemies remain neutral Centre/Support from
    // their native combat profile. A friendly force gets a stable third hull in
    // the flank lane so the player's group also advances as combined arms; this
    // is a tactical lane, never a claim that the design is a light tank.
    internal static string DefaultRole(bool friendly, int slot, bool fireSupport)
    {
        if (fireSupport) return Support;
        return friendly && slot % 4 == 2 ? Flank : Center;
    }

    // Local offsets around the native group centre. The spawn planner subtracts
    // offset.Y from the forward radius, so negative Y increases forward reach.
    // Support uses negative depth to sit farther from the player, behind the
    // centre; flankers remain side-lane offsets.
    internal static Vector2[] Layout(IReadOnlyList<string> roles, float compression = 1f)
    {
        var result = new Vector2[roles.Count];
        compression = float.IsFinite(compression)
            ? Math.Clamp(compression, .55f, 1.5f) : 1f;
        var spacing = Math.Max(MinimumSpacing, 36f * compression);
        var centres = roles.Count(role => Normalize(role) == Center);
        var supports = roles.Count(role => Normalize(role) == Support);
        var centre = 0;
        var flank = 0;
        var support = 0;
        for (var i = 0; i < roles.Count; i++)
        {
            result[i] = Normalize(roles[i]) switch
            {
                Flank => new Vector2(
                    (flank % 2 == 0 ? -1 : 1) * (Math.Max(100f, 100f * compression)
                        + flank / 2 * 36f * compression),
                    30f + flank++ / 2 * 28f),
                Support => new Vector2(
                    (support - (supports - 1) * .5f) * spacing,
                    -(Math.Max(36f, 50f * compression)) - support++ * 12f),
                _ => new Vector2(
                    (centre++ - (centres - 1) * .5f) * spacing,
                    0f)
            };
        }
        return result;
    }

    // Player-relative ambush placement. Coordinates are local to the player's
    // initial spawn heading: X is right of the player, Y is ahead. Recon pairs
    // sit exactly at 3 and 9 o'clock (Y = 0), with repeated pairs stepping
    // farther along the same side axis so they cannot share one marker.
    internal static Vector2[] AmbushOffsets(IReadOnlyList<string>? roles, float radius)
    {
        if (roles is null || roles.Count == 0) return Array.Empty<Vector2>();
        var count = roles.Count;
        var normalized = new string[count];
        for (var index = 0; index < count; index++)
            normalized[index] = Normalize(roles[index]);
        var result = new Vector2[count];
        radius = float.IsFinite(radius) && radius > 1f ? radius : 350f;
        // Keep the planned radius. A 10% closer side lane reduces clearance to
        // offset friendly anchors and can make an exact side pair unsatisfiable;
        // the planner's era minimum is already satisfied by the sampled radius.
        var flankReach = radius;
        var centres = normalized.Count(role => role == Center);
        var supports = normalized.Count(role => role == Support);
        var flanks = normalized.Count(role => role == Flank);
        // Policy-7 ambush without authored flankers: put half of the centre
        // hulls on the side lanes. Groups with real flank roles keep the
        // original 3/9 o'clock geometry unchanged.
        var splitCentres = flanks == 0 && centres >= 2;
        var sideCentres = splitCentres ? centres / 2 : 0;
        var centre = 0;
        var flank = 0;
        var support = 0;
        for (var index = 0; index < count; index++)
        {
            switch (normalized[index])
            {
                case Flank:
                {
                    var side = flank % 2 == 0 ? -1f : 1f;
                    var pair = flank / 2;
                    result[index] = new Vector2(
                        side * (flankReach + pair * MinimumSpacing),
                        0f);
                    flank++;
                    break;
                }
                case Support:
                {
                    // Absolute depth floor keeps the support element behind the
                    // centre without collapsing below the planner's 24 m spacing
                    // gate at compact/map-fitted radii. Scaling only adds depth.
                    var depth = MathF.Max(25f, radius * .08f);
                    result[index] = new Vector2(
                        (support - (supports - 1) * .5f) * 40f,
                        radius - depth);
                    support++;
                    break;
                }
                default:
                {
                    if (splitCentres && centre < sideCentres)
                    {
                        var side = centre % 2 == 0 ? -1f : 1f;
                        var pair = centre / 2;
                        // Exact side bearings stay on the player's half-plane
                        // (dot = 0) and repeat outward without sharing a marker.
                        result[index] = new Vector2(
                            side * (radius + pair * MinimumSpacing),
                            0f);
                    }
                    else
                    {
                        var front = centre - sideCentres;
                        var frontCount = centres - sideCentres;
                        result[index] = new Vector2(
                            (front - (frontCount - 1) * .5f) * 40f,
                            radius);
                    }
                    centre++;
                    break;
                }
            }
        }
        return result;
    }

    // Runtime approach lanes. Center slots spread across the frontage, flankers
    // use a stable side lane, support holds a depth behind the contact.
    internal static Vector2 ApproachWaypoint(
        Vector2 contact,
        Vector2 origin,
        string role,
        int side,
        int slot,
        float lateralSpacing = 32f)
    {
        var forward = Direction(contact - origin);
        var right = new Vector2(forward.Y, -forward.X);
        var spacing = float.IsFinite(lateralSpacing)
            ? Math.Clamp(lateralSpacing, 6f, 80f) : 32f;
        return Normalize(role) switch
        {
            Flank => contact - forward * (140f + (slot % 3) * 18f)
                + right * (side == 0 ? 1 : side) * 120f,
            Support => contact - forward * 300f
                + right * ((slot % 3) - 1) * spacing,
            _ => contact - forward * 200f
                + right * ((slot % 5) - 2) * spacing
        };
    }

    // Clear-contact bound endpoint. Lateral/standoff geometry keeps the moving
    // element inside a deliberate envelope instead of driving through the target.
    internal static Vector2 EngagementWaypoint(
        Vector2 contact,
        Vector2 origin,
        string role,
        int side,
        int slot)
    {
        var forward = Direction(contact - origin);
        var right = new Vector2(forward.Y, -forward.X);
        return Normalize(role) switch
        {
            Flank => contact - forward * 190f + right * (side == 0 ? 1 : side) * 170f,
            _ => contact - forward * 240f + right * ((slot % 5) - 2) * 48f
        };
    }

    // A clear-shot contact still advances in short alternating bounds. The lead
    // hull and every authored Support hull cover; alternating parity groups move
    // in separate windows. The contact epoch is the first-sight time, never the
    // last-seen timestamp (which resets on every visible scan).
    internal static bool EngagementMoveAllowed(
        string role,
        int slot,
        int teamSize,
        int leadSlot,
        bool coverAvailable,
        float now,
        float contactSince,
        float contactDistance)
    {
        if (teamSize <= 1) return false;
        if (!coverAvailable) return false;
        if (!float.IsFinite(now) || !float.IsFinite(contactSince)
            || !float.IsFinite(contactDistance)) return false;
        if (contactSince < 0f) return false;
        role = Normalize(role);
        if (role == Support || slot == leadSlot) return false;
        if (contactDistance <= (role == Flank ? 220f : 320f)) return false;
        var phase = PositiveModulo(now - contactSince, 20f);
        return slot % 2 == 0 ? phase < 8f : phase >= 10f && phase < 18f;
    }

    internal static bool EngagementNoCoverLeadAllowed(
        string role,
        int slot,
        int teamSize,
        int leadSlot,
        float contactDistance)
    {
        _ = role;
        return teamSize > 1 && slot == leadSlot && float.IsFinite(contactDistance)
            && contactDistance > 320f;
    }

    internal static bool IsCoverSlot(string role, int slot, int teamSize, int leadSlot) =>
        teamSize > 1 && (Normalize(role) == Support || slot == leadSlot);

    // Keep a started clear-contact leg until it makes meaningful progress for its
    // bounded duration, rather than replacing it with an attack order every scan.
    internal static bool KeepEngagementLeg(
        float now,
        float until,
        float started,
        float startDistance,
        float currentDistance,
        bool stalled)
    {
        if (!float.IsFinite(now) || !float.IsFinite(until)
            || !float.IsFinite(started) || !float.IsFinite(startDistance)
            || !float.IsFinite(currentDistance)) return false;
        if (stalled || now < started || now >= until) return false;
        if (currentDistance <= 12f) return false;
        if (currentDistance <= startDistance - 4f) return true;
        // Give the native driver a short turn/acceleration window, then require
        // measurable closure before spending more time on this leg.
        return now - started < 3.5f;
    }

    internal static bool IsStationarySpeed(float speed) =>
        float.IsFinite(speed) && Math.Abs(speed) <= 1.5f;

    // Pick a mobile lead for the no-cover repositioning exception. An immobile
    // lowest slot must not block every remaining hull forever.
    internal static int SelectLeadSlot(
        IReadOnlyList<(int Slot, bool Mobile, bool Usable)> team,
        int fallback)
    {
        var lead = int.MaxValue;
        foreach (var member in team)
            if (member.Usable && member.Mobile && member.Slot >= 0 && member.Slot < lead)
                lead = member.Slot;
        return lead == int.MaxValue ? fallback : lead;
    }

    private static float PositiveModulo(float value, float modulus)
    {
        var result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    // A new movement leg may start only when another same-team hull is actually
    // stationary with a visible firing solution, or when no hull can cover the
    // contact. The latter still permits exactly one lead hull to reposition, so
    // terrain blocking every line of sight cannot deadlock the whole group.
    // An existing leg is never aborted by this check; casualties are represented
    // by live team size and stable slots, and a lone survivor always advances.
    internal static bool CanStartLeg(
        string role,
        int slot,
        int teamSize,
        int leadSlot,
        bool contactVisible,
        bool coverAvailable,
        float now,
        float contactAt,
        float contactDistance,
        bool currentlyMoving = false)
    {
        if (currentlyMoving) return true;
        if (teamSize <= 1) return true;
        if (!contactVisible) return true;
        if (!float.IsFinite(now) || !float.IsFinite(contactAt)
            || !float.IsFinite(contactDistance)) return true;
        if (!coverAvailable) return slot == leadSlot;

        role = Normalize(role);
        if (role == Support)
            return contactDistance > 380f;

        if (contactDistance <= (role == Flank ? 170f : 270f)) return false;
        // Share the same alternating phase clock as EngagementMoveAllowed.
        var phase = PositiveModulo(now - contactAt, 20f);
        return slot % 2 == 0 ? phase < 8f : phase >= 10f && phase < 18f;
    }

    // Pure role expansion in the native UnitDefinition dictionary order. Each
    // design name is matched by identity, never by the managed offer list index.
    // Diagnostics distinguish: a native design with no configured role, a
    // configured mapping for a design that never appears, and an invalid role
    // value. Only a valid configured role counts as matched.
    internal static string[] ExpandDeploymentRoles(
        IReadOnlyList<(string Name, int Count)> nativeOrder,
        IReadOnlyDictionary<string, string>? rolesByName,
        out int matchedNames,
        out int unmatchedNames) =>
        ExpandDeploymentRoles(nativeOrder, rolesByName, out matchedNames,
            out unmatchedNames, out _, out _);

    internal static string[] ExpandDeploymentRoles(
        IReadOnlyList<(string Name, int Count)> nativeOrder,
        IReadOnlyDictionary<string, string>? rolesByName,
        out int matchedNames,
        out int unmatchedNativeNames,
        out int unusedConfiguredNames,
        out int invalidRoleValues)
    {
        matchedNames = 0;
        unmatchedNativeNames = 0;
        unusedConfiguredNames = 0;
        invalidRoleValues = 0;
        var seenNative = new HashSet<string>(StringComparer.Ordinal);
        var usedConfigured = new HashSet<string>(StringComparer.Ordinal);
        var resolvedRoles = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var (name, count) in nativeOrder)
        {
            if (count < 1) continue;
            var nativeName = name ?? string.Empty;
            if (!resolvedRoles.TryGetValue(nativeName, out var role))
            {
                role = Center;
                if (rolesByName != null
                    && !string.IsNullOrWhiteSpace(nativeName)
                    && rolesByName.TryGetValue(nativeName, out var supplied))
                {
                    usedConfigured.Add(nativeName);
                    if (string.Equals(supplied, Center, StringComparison.Ordinal)
                        || string.Equals(supplied, Flank, StringComparison.Ordinal)
                        || string.Equals(supplied, Support, StringComparison.Ordinal))
                    {
                        role = supplied;
                        matchedNames++;
                    }
                    else
                    {
                        invalidRoleValues++;
                    }
                }
                else
                {
                    unmatchedNativeNames++;
                }
                resolvedRoles[nativeName] = role;
            }
            for (var i = 0; i < count; i++) result.Add(role);
        }
        if (rolesByName != null)
            foreach (var name in rolesByName.Keys)
                if (!usedConfigured.Contains(name)) unusedConfiguredNames++;
        return result.ToArray();
    }

    private static Vector2 Direction(Vector2 value) =>
        Finite(value) && value.LengthSquared() > .01f
            ? Vector2.Normalize(value) : Vector2.UnitY;

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
