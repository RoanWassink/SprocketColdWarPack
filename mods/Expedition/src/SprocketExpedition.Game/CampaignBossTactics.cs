using System.Numerics;

namespace SprocketExpedition.Game;

// Pure boss identity/intent policy. The adapter owns native name lookup,
// current visibility, live orders and terrain/bounds qualification.
internal static class CampaignBossTactics
{
    internal enum Character { None, Fortress, Hunter, Battery, Veteran }

    internal const float MaximumSidestep = 20f;
    internal const float FortressHoldSeconds = 90f;
    internal const float FortressSlowStepMeters = 40f;
    internal const float BatteryRetreatFraction = .5f;

    internal static string NativeTankName { get; private set; } = string.Empty;
    internal static Character PreparedCharacter { get; private set; }
    internal static bool Enabled => PreparedCharacter != Character.None && NativeTankName.Length > 0;

    internal static void Prepare(string nativeTankName, string character)
    {
        Release();
        if (string.IsNullOrWhiteSpace(nativeTankName) || nativeTankName.Length > 256
            || !Enum.TryParse<Character>(character, true, out var parsed)
            || !Enum.IsDefined(typeof(Character), parsed) || parsed == Character.None) return;
        // Preserve the actual native name exactly; never fuzzy-match a label,
        // GameObject name, suffix or unrelated tank when content is unavailable.
        NativeTankName = nativeTankName;
        PreparedCharacter = parsed;
    }

    internal static void Release()
    {
        NativeTankName = string.Empty;
        PreparedCharacter = Character.None;
    }

    internal static bool ExactEnemyName(string? observedNativeName, bool enemy) => Enabled && enemy
        && string.Equals(NativeTankName, observedNativeName, StringComparison.Ordinal);

    internal static Character Match(string? observedNativeName, bool enemy, int matchingEnemies) =>
        matchingEnemies == 1 && ExactEnemyName(observedNativeName, enemy)
            ? PreparedCharacter : Character.None;

    // Only the Hunter privately pursues the player hull. Fortress and Battery
    // escorts use ordinary sighting memory.
    internal static bool TryPlayerPursuit(Character character, bool enemy, bool activePlayer,
        Vector2 playerPosition, out Vector2 destination)
    {
        destination = default;
        if (!Enabled || character == Character.None || character != PreparedCharacter
            || character != Character.Hunter || !enemy || !activePlayer
            || !Finite(playerPosition)) return false;
        destination = playerPosition;
        return true;
    }

    internal static bool HoldsPosition(Character character) => character == Character.Fortress;

    internal static bool SidestepsOnly(Character character) =>
        character is Character.Fortress or Character.Battery;

    internal static string BehaviorName(Character character) => character switch
    {
        Character.Fortress => "holds a dug-in position and fires",
        Character.Hunter => "hunts the player with flanking escorts",
        Character.Battery => "keeps long range and withdraws when closed on",
        Character.Veteran => "veteran combined-arms attack",
        _ => "no boss behavior"
    };

    internal static bool StopToFire(Character character, float distance, bool previouslyStopped) => character switch
    {
        Character.Fortress or Character.Battery => true,
        Character.Hunter => distance <= (previouslyStopped ? 200f : 140f),
        _ => distance <= (previouslyStopped ? 340f : 280f)
    };

    // Battery withdraws only after the player closes inside half the first
    // contact range. The step is a short bounded move straight away.
    internal static bool TryBatteryRetreat(Character character, Vector2 position, Vector2 visibleTarget,
        float initialDistance, out Vector2 waypoint)
    {
        waypoint = position;
        if (character != Character.Battery || !Finite(position) || !Finite(visibleTarget)
            || !float.IsFinite(initialDistance) || initialDistance <= 0f) return false;
        var away = position - visibleTarget;
        var distance = away.Length();
        if (!float.IsFinite(distance) || distance < 1f
            || distance >= initialDistance * BatteryRetreatFraction) return false;
        var step = Math.Clamp(initialDistance * BatteryRetreatFraction - distance, 20f, 50f);
        waypoint = position + away / distance * step;
        return Finite(waypoint);
    }

    // 0.3.5 fallback for Fortress/Battery: one lateral step, no advance.
    internal static bool TrySidestep(Character character, Vector2 position, Vector2 visibleTarget,
        int side, out Vector2 waypoint)
    {
        waypoint = position;
        if (!SidestepsOnly(character) || !Finite(position) || !Finite(visibleTarget)) return false;
        var ahead = visibleTarget - position;
        var length = ahead.Length();
        if (!float.IsFinite(length) || length < 1f) return false;
        ahead /= length;
        var right = new Vector2(ahead.Y, -ahead.X);
        waypoint = position + right * (side >= 0 ? 1f : -1f) * MaximumSidestep;
        return Finite(waypoint);
    }

    // After 90 s without contact a Fortress may make one slow step toward the
    // last known position; never a full pursuit.
    internal static bool MaySlowAdvance(Character character, float now, float lastContactAt) =>
        character == Character.Fortress && float.IsFinite(now) && float.IsFinite(lastContactAt)
        && now >= lastContactAt && now - lastContactAt >= FortressHoldSeconds;

    internal static bool TrySlowAdvancePoint(Vector2 position, Vector2 lastKnown, out Vector2 waypoint)
    {
        waypoint = position;
        if (!Finite(position) || !Finite(lastKnown)) return false;
        var delta = lastKnown - position;
        var distance = delta.Length();
        if (!float.IsFinite(distance) || distance < 1f) return false;
        var step = Math.Min(distance, FortressSlowStepMeters);
        waypoint = position + delta / distance * step;
        return Finite(waypoint);
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
