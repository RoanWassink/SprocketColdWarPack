using System.Numerics;

namespace SprocketExpedition.Game;

// Pure actual-spawn verification shared by the native adapter and managed tests.
// It reads no scene state; callers supply the planned slots and observed owned
// hull spawn positions/design names.
internal readonly record struct SpawnPlanSlot(Vector2 Position, string Name, string Role);

internal readonly record struct SpawnObservation(Vector2 Position, string Name);

internal static class CampaignSpawnVerification
{
    internal const float PositionTolerance = 32f;

    internal static bool Verify(
        IReadOnlyList<SpawnPlanSlot> expected,
        IReadOnlyList<SpawnObservation> observed,
        IReadOnlyDictionary<string, string>? rolesByName,
        out string error)
    {
        error = string.Empty;
        if (expected.Count == 0)
        {
            error = "no planned marker/design/role order exists";
            return false;
        }
        if (observed.Count != expected.Count)
        {
            error = $"expected {expected.Count} spawned enemies, observed {observed.Count}";
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            var planned = expected[index];
            var actual = observed[index];
            if (!Finite(planned.Position) || !Finite(actual.Position))
            {
                error = $"slot {index + 1} has a non-finite position";
                return false;
            }
            var delta = actual.Position - planned.Position;
            if (delta.Length() > PositionTolerance)
            {
                error = $"slot {index + 1} spawned {delta.Length():N1} m from the planned marker";
                return false;
            }

            var plannedName = Name(planned.Name);
            var actualName = Name(actual.Name);
            if (IsUnknown(plannedName))
            {
                error = $"slot {index + 1} has no planned design identity";
                return false;
            }
            if (IsUnknown(actualName))
            {
                error = $"slot {index + 1} has no readable spawned design identity";
                return false;
            }
            if (!string.Equals(plannedName, actualName, StringComparison.Ordinal))
            {
                error = $"slot {index + 1} expected design '{plannedName}' but observed '{actualName}'";
                return false;
            }

            var actualRole = rolesByName != null
                && rolesByName.TryGetValue(actualName, out var configured)
                ? CampaignGroupDoctrine.Normalize(configured)
                : CampaignGroupDoctrine.Center;
            var plannedRole = CampaignGroupDoctrine.Normalize(planned.Role);
            if (!string.Equals(plannedRole, actualRole, StringComparison.Ordinal))
            {
                error = $"slot {index + 1} expected role {plannedRole} but resolved {actualRole}";
                return false;
            }
        }

        return true;
    }

    private static string Name(string? value) => value ?? string.Empty;

    private static bool IsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);

    private static bool Finite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
