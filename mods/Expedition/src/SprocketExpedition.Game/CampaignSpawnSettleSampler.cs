using System.Numerics;

namespace SprocketExpedition.Game;

internal readonly record struct SpawnSettleSample(
    long HullId,
    string Design,
    float AgeSeconds,
    Vector3 Position,
    float UpDot,
    float Speed);

// Narrow, bounded observations of freshly spawned owned hulls. These samples
// are diagnostic only; they never alter physics, AI orders or target memory.
internal static class CampaignSpawnSettleSampler
{
    internal const float WindowSeconds = 3f;
    internal const int MaxSamplesPerHull = 4;
    internal const float MinimumSpacing = .75f;

    private sealed class HullState
    {
        internal float Started;
        internal float Next;
        internal int Count;
    }

    private static readonly Dictionary<long, HullState> states = new();
    private static readonly List<SpawnSettleSample> samples = new();

    internal static IReadOnlyList<SpawnSettleSample> Samples => samples;

    internal static void Reset()
    {
        states.Clear();
        samples.Clear();
    }

    internal static bool Observe(
        float now,
        long hullId,
        string design,
        Vector3 position,
        Vector3 bodyUp,
        float speed,
        out SpawnSettleSample sample)
    {
        sample = default;
        if (!float.IsFinite(now)
            || !Finite(position) || !Finite(bodyUp) || !float.IsFinite(speed)
            || speed < 0f || bodyUp.LengthSquared() < .01f)
            return false;

        if (!states.TryGetValue(hullId, out var state))
        {
            state = new HullState { Started = now, Next = now };
            states.Add(hullId, state);
        }
        if (now < state.Started || now - state.Started > WindowSeconds
            || state.Count >= MaxSamplesPerHull || now < state.Next)
            return false;

        var up = Vector3.Normalize(bodyUp);
        sample = new SpawnSettleSample(hullId, design ?? string.Empty,
            now - state.Started, position, Vector3.Dot(up, Vector3.UnitY), speed);
        samples.Add(sample);
        state.Count++;
        state.Next = now + MinimumSpacing;
        return true;
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
