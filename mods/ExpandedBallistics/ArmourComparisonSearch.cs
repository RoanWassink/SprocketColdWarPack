using System;
using System.Collections.Generic;

namespace SprocketExpandedBallistics;

internal enum ComparisonProbeOutcome { Stopped, Perforated, Indeterminate }
internal enum ComparisonSearchStatus
{
    Pending, Bracketed, BelowMinimum, AboveMaximum, Indeterminate, NonMonotonic, ProbeLimit, Grazing
}

internal sealed record ComparisonSearchResult(ComparisonSearchStatus Status, float? LowerMm, float? UpperMm, string Detail);

/// <summary>
/// Incremental, bounded search over the native simulation. A bracket is conditional on the
/// sampled monotonic response; it is never a universal RHA-equivalent armour value.
/// Each Observe consumes the currently requested probe, so callers can budget work per frame.
/// </summary>
internal sealed class ArmourComparisonSearch
{
    private readonly float minimum, maximum, tolerance;
    private readonly int maximumProbes;
    private readonly Queue<float> guards = new();
    private readonly Dictionary<float, ComparisonProbeOutcome> observations = new();
    private float? lower, upper;
    private int replay;

    internal ArmourComparisonSearch(float minimumMm = 10, float maximumMm = 2500,
        float toleranceMm = 5, int maxProbes = 20, bool grazing = false)
    {
        minimum = minimumMm; maximum = maximumMm; tolerance = toleranceMm;
        maximumProbes = Math.Clamp(maxProbes, 1, 64);
        Result = new(ComparisonSearchStatus.Pending, null, null, "Sampling native stop/perforation response.");
        if (grazing)
        {
            Finish(ComparisonSearchStatus.Grazing, "Exactly grazing: no finite protection threshold is reported.");
            return;
        }
        if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || !float.IsFinite(tolerance) ||
            minimum <= 0 || maximum <= minimum || tolerance <= 0 || !float.IsFinite(maximum - minimum))
        {
            Finish(ComparisonSearchStatus.Indeterminate, "Invalid search bounds or tolerance.");
            return;
        }
        // Guard samples catch visible reversals before bisection assumes monotonicity.
        for (int i = 0; i <= 4; i++) guards.Enqueue(minimum + (maximum - minimum) * (i / 4f));
        NextPenetrationMm = guards.Dequeue();
    }

    internal float? NextPenetrationMm { get; private set; }
    internal int ProbeCount { get; private set; }
    internal ComparisonSearchResult Result { get; private set; }

    internal void Observe(ComparisonProbeOutcome outcome)
    {
        if (Result.Status != ComparisonSearchStatus.Pending || !NextPenetrationMm.HasValue) return;
        float penetration = NextPenetrationMm.Value;
        NextPenetrationMm = null;
        ProbeCount++;
        if (outcome != ComparisonProbeOutcome.Stopped && outcome != ComparisonProbeOutcome.Perforated)
        {
            Finish(ComparisonSearchStatus.Indeterminate, "A native probe had no reliable stop/perforation outcome.");
            return;
        }
        if (observations.TryGetValue(penetration, out var previous) && previous != outcome)
        {
            Finish(ComparisonSearchStatus.NonMonotonic, "Replaying the same probe changed its outcome; no reliable threshold.");
            return;
        }
        foreach (var sample in observations)
        {
            if ((outcome == ComparisonProbeOutcome.Stopped && sample.Key < penetration && sample.Value == ComparisonProbeOutcome.Perforated) ||
                (outcome == ComparisonProbeOutcome.Perforated && sample.Key > penetration && sample.Value == ComparisonProbeOutcome.Stopped))
            {
                Finish(ComparisonSearchStatus.NonMonotonic, "Sampled response is not monotonic; no single protection threshold.");
                return;
            }
        }
        observations[penetration] = outcome;
        if (outcome == ComparisonProbeOutcome.Stopped) lower = lower.HasValue ? Math.Max(lower.Value, penetration) : penetration;
        else upper = upper.HasValue ? Math.Min(upper.Value, penetration) : penetration;

        if (guards.Count > 0) { Request(guards.Dequeue()); return; }
        if (replay == 0 && lower.HasValue && upper.HasValue && upper.Value - lower.Value > tolerance)
        {
            float midpoint = lower.Value + (upper.Value - lower.Value) * .5f;
            if (midpoint > lower.Value && midpoint < upper.Value) { Request(midpoint); return; }
            Finish(ComparisonSearchStatus.Indeterminate, "Floating-point resolution cannot resolve this bracket.");
            return;
        }
        // Replay both observed endpoints, including censored cases, before reporting a result.
        if (replay < 2)
        {
            bool bracket = lower.HasValue && upper.HasValue;
            float endpoint = replay++ == 0 ? (bracket ? lower!.Value : minimum) : (bracket ? upper!.Value : maximum);
            Request(endpoint);
            return;
        }
        if (!lower.HasValue) Finish(ComparisonSearchStatus.BelowMinimum, "Perforates at the minimum tested penetration; threshold is below the test range.");
        else if (!upper.HasValue) Finish(ComparisonSearchStatus.AboveMaximum, "Stops at the maximum tested penetration; threshold is at least the test limit.");
        else Finish(ComparisonSearchStatus.Bracketed, "Observed stop/perforation bracket; sampled guard and endpoint replay checks passed.");
    }

    private void Request(float penetration)
    {
        if (ProbeCount >= maximumProbes) Finish(ComparisonSearchStatus.ProbeLimit, "Probe budget reached before a reliable threshold was completed.");
        else NextPenetrationMm = penetration;
    }

    private void Finish(ComparisonSearchStatus status, string detail)
    {
        NextPenetrationMm = null;
        Result = new(status, lower, upper, detail);
    }
}
