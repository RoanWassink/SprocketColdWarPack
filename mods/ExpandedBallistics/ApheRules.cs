using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprocketExpandedBallistics;

internal sealed record ApheSettings(
    double FuseSensitivityRhaMm = 15,
    double FuseDelayMeters = 0.60,
    double MaxGapFraction = 0.65,
    double MinWallClearanceMeters = 0.05,
    double OpenFallbackMeters = 0.20,
    double ConeHalfAngleDegrees = 180,
    double ForwardMomentumBlend = 0.25,
    int FragmentCountAt100Mm = 72,
    double FragmentMassKgAt100Mm = 4.5,
    double FragmentSpeedMetersPerSecond = 1050,
    double ExplosionVisualScale = 0.85,
    bool DetonateOnStop = true)
{
    internal static readonly ApheSettings Default = new();
}

internal sealed record ApheProfileOverrides(
    double? FuseSensitivityRhaMm = null,
    double? FuseDelayMeters = null,
    double? MaxGapFraction = null,
    double? MinWallClearanceMeters = null,
    double? OpenFallbackMeters = null,
    double? ConeHalfAngleDegrees = null,
    double? ForwardMomentumBlend = null,
    int? FragmentCountAt100Mm = null,
    double? FragmentMassKgAt100Mm = null,
    double? FragmentSpeedMetersPerSecond = null,
    double? ExplosionVisualScale = null,
    bool? DetonateOnStop = null);

internal readonly record struct ApheBurstPlan(
    int TotalCount,
    int BurstCalls,
    double PerBurstCount,
    double TotalVolumeM3,
    double PerBurstVolumeM3,
    double TotalMassKg,
    double FragmentSpeedMetersPerSecond);

internal enum ApheAirAction
{
    Continue,
    Detonate,
    Overpenetrate
}

internal readonly record struct ApheAirDecision(
    ApheAirAction Action,
    double OffsetMeters,
    string Reason);

internal sealed class ApheShotState
{
    internal double AccumulatedRhaMm { get; set; }
    internal bool FuseArmed { get; set; }
    internal double RemainingDelayM { get; set; }
    internal double AirTravelSinceArmedM { get; set; }
    internal bool JustArmed { get; set; }
    internal bool Detonated { get; set; }
    internal int DetonationFragments { get; set; }
    internal double DetonationOffsetM { get; set; }
}

/// <summary>
/// Pure post-penetration APHE detonation model (0.22.104–0.22.107):
/// fuse sensitivity (RHA-eq threshold), real post-penetration air travel along the shell's trajectory
/// for the fuse delay (with safe interior detonation on narrow exit walls or internal stops),
/// uniform 360-degree Fibonacci fragmentation sphere with physical forward shell-velocity vector addition,
/// and true Air-space fragment spawning (materialIndex = -1, depthLookup = null, PenetrationPrecomputed, AP profile).
/// </summary>
internal static class ApheRules
{
    internal const double SteelDensity = 7850;
    internal const int NativeBurstCap = 32;
    internal const int MinTotalFragments = 8;
    internal const int MaxTotalFragments = 160;
    internal const double GoldenAngleRadians = 2.39996322972865332; // pi * (3 - sqrt(5))
    internal const double FarWallThicknessToleranceM = 0.30;
    internal const double EdgeClearanceM = 0.02;

    /// <summary>Merges optional per-shell APHE overrides from shells.json over the global fallback (spall.json / defaults).</summary>
    internal static ApheSettings Effective(ShellProfile? profile, ApheSettings fallback)
    {
        if (profile?.Aphe is not { } o) return fallback;
        return new ApheSettings(
            FuseSensitivityRhaMm: o.FuseSensitivityRhaMm ?? fallback.FuseSensitivityRhaMm,
            FuseDelayMeters: o.FuseDelayMeters ?? fallback.FuseDelayMeters,
            MaxGapFraction: o.MaxGapFraction ?? fallback.MaxGapFraction,
            MinWallClearanceMeters: o.MinWallClearanceMeters ?? fallback.MinWallClearanceMeters,
            OpenFallbackMeters: o.OpenFallbackMeters ?? fallback.OpenFallbackMeters,
            ConeHalfAngleDegrees: o.ConeHalfAngleDegrees ?? fallback.ConeHalfAngleDegrees,
            ForwardMomentumBlend: o.ForwardMomentumBlend ?? fallback.ForwardMomentumBlend,
            FragmentCountAt100Mm: o.FragmentCountAt100Mm ?? fallback.FragmentCountAt100Mm,
            FragmentMassKgAt100Mm: o.FragmentMassKgAt100Mm ?? fallback.FragmentMassKgAt100Mm,
            FragmentSpeedMetersPerSecond: o.FragmentSpeedMetersPerSecond ?? fallback.FragmentSpeedMetersPerSecond,
            ExplosionVisualScale: o.ExplosionVisualScale ?? fallback.ExplosionVisualScale,
            DetonateOnStop: o.DetonateOnStop ?? fallback.DetonateOnStop);
    }

    internal static void ValidateOverrides(string id, ApheProfileOverrides o)
    {
        void Check(string field, double? value, double min, double max)
        {
            if (value is { } n && (!double.IsFinite(n) || n < min || n > max))
                throw new FormatException(ShellBallistics.RangeError(id, field, n, min, max));
        }
        Check("apheFuseSensitivityRhaMm", o.FuseSensitivityRhaMm, 1, 200);
        Check("apheFuseDelayMeters", o.FuseDelayMeters, 0, 3.0);
        Check("apheMaxGapFraction", o.MaxGapFraction, 0.15, 0.90);
        Check("apheMinWallClearanceMeters", o.MinWallClearanceMeters, 0.01, 0.25);
        Check("apheOpenFallbackMeters", o.OpenFallbackMeters, 0.02, 0.50);
        Check("apheConeHalfAngleDegrees", o.ConeHalfAngleDegrees, 15, 180);
        Check("apheForwardMomentumBlend", o.ForwardMomentumBlend, 0, 0.60);
        Check("apheFragmentCountAt100Mm", o.FragmentCountAt100Mm, 16, 160);
        Check("apheFragmentMassKgAt100Mm", o.FragmentMassKgAt100Mm, 0.2, 30.0);
        Check("apheFragmentSpeedMetersPerSecond", o.FragmentSpeedMetersPerSecond, 250, 1800);
        Check("apheExplosionVisualScale", o.ExplosionVisualScale, 0.1, 2.0);
    }

    /// <summary>Exact Sprocket native AP-profile 0-degree RHA penetration (mm) for an APHE fragment burst.</summary>
    internal static double EstimateFragmentPenetrationMm(ApheBurstPlan plan, double penetratorConstant = 1658.0)
    {
        if (plan.TotalCount <= 0 || plan.TotalMassKg <= 0 || plan.TotalVolumeM3 <= 0 || !double.IsFinite(penetratorConstant) || penetratorConstant <= 0)
            return 0;
        double m = plan.TotalMassKg / plan.TotalCount;
        double v = plan.FragmentSpeedMetersPerSecond;
        double dMm = 2000.0 * Math.Cbrt((plan.TotalVolumeM3 / plan.TotalCount) * 3.0 / (4.0 * Math.PI));
        if (dMm <= 0) return 0;
        return 100.0 * Math.Pow((Math.Sqrt(m) * v) / (penetratorConstant * Math.Pow(dMm * 0.01, 0.75)), 1.43);
    }

    /// <summary>True when accumulated RHA-equivalent thickness has armed the APHE fuse.</summary>
    internal static bool IsFuseArmed(double accumulatedRhaMm, ApheSettings s)
        => double.IsFinite(accumulatedRhaMm) && accumulatedRhaMm >= s.FuseSensitivityRhaMm;

    /// <summary>
    /// Decides what an armed APHE shell currently flying in empty Air (materialIndex == -1) should do:
    /// detonate cleanly in open air when the fuse expires, clamp inside a narrow far exterior wall,
    /// continue natively through internal modules / seams / spaced gaps, or overpenetrate into open sky.
    /// Guarantees every returned Detonate offset lies strictly inside (0, nextEnterM) so fragments
    /// always spawn in empty Air and never inside a solid object.
    /// </summary>
    internal static ApheAirDecision DecideAirStep(
        double remainingDelayM,
        double nextEnterM,
        bool nextIsArmour,
        double maxExitM,
        double minGapMm,
        ApheSettings s,
        bool justArmed)
    {
        var delay = double.IsFinite(remainingDelayM) ? remainingDelayM : 0;
        var openFallback = Math.Clamp(double.IsFinite(s.OpenFallbackMeters) ? s.OpenFallbackMeters : ApheSettings.Default.OpenFallbackMeters, 0.02, 0.50);

        // No solid object ahead on the ray (open-top compartment, corner grazing exit, or exited rear wall).
        if (!double.IsFinite(nextEnterM) || double.IsPositiveInfinity(nextEnterM))
        {
            if (justArmed || delay <= openFallback)
            {
                var offset = Math.Clamp(delay > 0 ? Math.Min(delay, openFallback) : 0.02, 0.02, openFallback);
                return new(ApheAirAction.Detonate, offset, "open-space");
            }
            return new(ApheAirAction.Overpenetrate, 0, "overpenetrated");
        }

        if (nextEnterM < 0) nextEnterM = 0;

        // Fuse already expired while traversing an interior module/plate: detonate immediately in open air.
        if (delay <= 0.005)
        {
            if (nextEnterM >= 0.01)
                return new(ApheAirAction.Detonate, Math.Min(0.02, nextEnterM * 0.5), "delay-expired");
            return new(ApheAirAction.Continue, 0, "touching-surface");
        }

        // Fuse expires in open air with safe clearance before the next object.
        if (delay <= nextEnterM - EdgeClearanceM)
            return new(ApheAirAction.Detonate, Math.Max(0.01, delay), "fuse-delay");

        // Fuse would expire within 2 cm of the next surface: detonate 2 cm in front in guaranteed clean air.
        if (delay <= nextEnterM + EdgeClearanceM && nextEnterM >= 0.05)
            return new(ApheAirAction.Detonate, nextEnterM - EdgeClearanceM, "near-surface");

        // Narrow turret / corner where the very next armour plate is the far exterior exit wall of the vehicle.
        var minSeamM = Math.Max(0.06, (double.IsFinite(minGapMm) && minGapMm > 0 ? minGapMm : 0) * 0.001);
        var isFarExitWall = nextIsArmour && double.IsFinite(maxExitM) && maxExitM >= nextEnterM && (maxExitM - nextEnterM) < FarWallThicknessToleranceM;
        if (isFarExitWall && nextEnterM >= minSeamM)
        {
            var clearance = Math.Clamp(s.MinWallClearanceMeters, 0.02, 0.25);
            var frac = Math.Clamp(s.MaxGapFraction, 0.15, 0.90);
            var maxSafe = Math.Max(0.02, nextEnterM - Math.Min(clearance, nextEnterM * 0.35));
            var offset = Math.Clamp(Math.Min(delay, nextEnterM * frac), 0.02, maxSafe);
            return new(ApheAirAction.Detonate, offset, "narrow-compartment");
        }

        // Next object is closer than fuse delay (seam, spaced inner plate, crewman, fuel tank, ammo rack, engine):
        // let the intact APHE shell travel natively into it.
        return new(ApheAirAction.Continue, 0, "traverse-interior");
    }

    /// <summary>
    /// Safe Air-space offset right in front of an internal obstacle (e.g. engine block or thick rear wall)
    /// when an already-armed APHE shell stops inside the vehicle before its fuse delay expires.
    /// Returns 0 when there is no safe air gap in front of the obstacle.
    /// </summary>
    internal static double StopDetonationOffset(double travelDistanceM, double nextEnterM)
    {
        var hasTravel = double.IsFinite(travelDistanceM) && travelDistanceM > 0;
        var hasEnter = double.IsFinite(nextEnterM) && nextEnterM > 0;
        var limit = hasTravel && hasEnter ? Math.Min(travelDistanceM, nextEnterM) : hasEnter ? nextEnterM : hasTravel ? travelDistanceM : 0;
        if (limit <= 0.002) return 0;
        if (limit < 0.02) return limit * 0.5;
        return Math.Max(0.01, limit - Math.Min(0.04, limit * 0.25));
    }

    /// <summary>
    /// Legacy single-gap detonation offset helper kept for compatibility and pure tests.
    /// </summary>
    internal static double DetonationOffsetMeters(ApheSettings s, double? forwardGapMm)
    {
        var desired = Math.Clamp(double.IsFinite(s.FuseDelayMeters) ? s.FuseDelayMeters : ApheSettings.Default.FuseDelayMeters, 0, 3.0);
        if (desired <= 0) return 0;
        if (forwardGapMm is not { } gapMm || !double.IsFinite(gapMm) || double.IsPositiveInfinity(gapMm))
        {
            var openFallback = Math.Clamp(double.IsFinite(s.OpenFallbackMeters) ? s.OpenFallbackMeters : ApheSettings.Default.OpenFallbackMeters, 0, 0.5);
            return Math.Min(desired, openFallback);
        }
        if (gapMm <= 0) return 0;
        var gapM = gapMm * 0.001;
        var clearance = Math.Clamp(s.MinWallClearanceMeters, 0.01, 0.25);
        if (gapM <= clearance * 2) return gapM * 0.5;
        var maxAllowed = Math.Max(0.05, gapM - clearance);
        var proportional = gapM * Math.Clamp(s.MaxGapFraction, 0.1, 0.95);
        return Math.Clamp(Math.Min(desired, proportional), 0.05, maxAllowed);
    }

    /// <summary>
    /// Fragment count, burst split (each call <= 32), volume, mass, and blast speed for a calibre and tuning factor
    /// (tuning = ApheSpallMultiplier / 4, so default tuning is 1.0).
    /// </summary>
    internal static ApheBurstPlan Plan(double calibreMm, double tuning, ApheSettings s)
    {
        var ratio = Math.Clamp((double.IsFinite(calibreMm) && calibreMm > 0 ? calibreMm : 100) / 100.0, 0.2, 3.0);
        var scale = Math.Clamp(double.IsFinite(tuning) && tuning > 0 ? tuning : 1.0, 0.25, 3.0);
        var rawCount = s.FragmentCountAt100Mm * Math.Pow(ratio, 1.0) * scale;
        var totalCount = (int)Math.Clamp(Math.Round(rawCount), MinTotalFragments, MaxTotalFragments);
        var burstCalls = Math.Clamp((int)Math.Ceiling(totalCount / (double)NativeBurstCap), 1, 5);
        var perBurstCount = totalCount / (double)burstCalls;
        var totalMass = s.FragmentMassKgAt100Mm * Math.Pow(ratio, 2.5) * scale;
        var totalVolume = totalMass / SteelDensity;
        var perBurstVolume = totalVolume / burstCalls;
        var speed = Math.Clamp(s.FragmentSpeedMetersPerSecond * Math.Pow(ratio, 0.1), 250, 1800);
        return new(totalCount, burstCalls, perBurstCount, totalVolume, perBurstVolume, totalMass, speed);
    }

    /// <summary>
    /// Exact integer batch sizes (each 1..32) summing to totalCount, so every batch spawns
    /// identical per-fragment mass and diameter when volume is proportional to batchCount.
    /// </summary>
    internal static int[] BatchCounts(int totalCount)
    {
        var remaining = Math.Clamp(totalCount, 1, MaxTotalFragments);
        var list = new List<int>(4);
        while (remaining > 0)
        {
            var batch = Math.Min(NativeBurstCap, remaining);
            list.Add(batch);
            remaining -= batch;
        }
        return list.ToArray();
    }

    /// <summary>
    /// Uniform golden-angle Fibonacci sphere direction in local frame (+Z = shell travel axis).
    /// Eliminates random clumping and blind spots across the 360-degree interior burst.
    /// </summary>
    internal static (float X, float Y, float Z) FibonacciSphereDirection(int index, int count, double halfAngleDegrees, double azimuthOffsetRadians = 0)
    {
        var n = Math.Max(1, count);
        var i = Math.Clamp(index, 0, n - 1);
        var angle = Math.Clamp(double.IsFinite(halfAngleDegrees) ? halfAngleDegrees : 180, 1, 180);
        var v = (i + 0.5) / n;
        var z = 1.0 - v * (1.0 - Math.Cos(angle * Math.PI / 180.0));
        var r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
        var phi = i * GoldenAngleRadians + (double.IsFinite(azimuthOffsetRadians) ? azimuthOffsetRadians : 0);
        return ((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)z);
    }

    /// <summary>
    /// Real vector addition of isotropic explosive blast velocity and residual shell forward velocity:
    /// v_frag = v_blast * u_sphere + min(v_shell * forwardCarry, 0.45 * v_blast) * +Z.
    /// Forward fragments gain speed and penetration; rear fragments still travel backward cleanly.
    /// </summary>
    internal static ((float X, float Y, float Z) Direction, float Speed) CombineVelocity(
        (float X, float Y, float Z) localUnitDir,
        double blastSpeedMps,
        double shellSpeedMps,
        double forwardCarry)
    {
        var vBlast = Math.Clamp(double.IsFinite(blastSpeedMps) ? blastSpeedMps : ApheSettings.Default.FragmentSpeedMetersPerSecond, 100, 2500);
        var carry = Math.Clamp(double.IsFinite(forwardCarry) ? forwardCarry : ApheSettings.Default.ForwardMomentumBlend, 0, 0.6);
        var vShell = double.IsFinite(shellSpeedMps) && shellSpeedMps > 0 ? shellSpeedMps : 0;
        var vForward = Math.Min(vShell * carry, vBlast * 0.45);
        var vx = localUnitDir.X * vBlast;
        var vy = localUnitDir.Y * vBlast;
        var vz = localUnitDir.Z * vBlast + vForward;
        var speed = Math.Sqrt(vx * vx + vy * vy + vz * vz);
        if (!double.IsFinite(speed) || speed < 1e-6)
            return ((0f, 0f, 1f), (float)vBlast);
        return (((float)(vx / speed), (float)(vy / speed), (float)(vz / speed)), (float)speed);
    }

    /// <summary>
    /// Unit direction in local frame (+Z = shell travel axis). Supports full 180-degree half-angle (360-degree sphere)
    /// plus an optional forward momentum blend along +Z.
    /// </summary>
    internal static (float X, float Y, float Z) Direction(double azimuthUnit, double polarUnit, double halfAngleDegrees, double forwardMomentumBlend)
    {
        var angle = Math.Clamp(double.IsFinite(halfAngleDegrees) ? halfAngleDegrees : 180, 1, 180);
        var u = Math.Clamp(double.IsFinite(azimuthUnit) ? azimuthUnit : 0, 0, 1);
        var v = Math.Clamp(double.IsFinite(polarUnit) ? polarUnit : 0, 0, 1);
        var z = 1.0 - v * (1.0 - Math.Cos(angle * Math.PI / 180.0));
        var r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
        var phi = u * Math.PI * 2.0;
        var x = r * Math.Cos(phi);
        var y = r * Math.Sin(phi);
        var blend = Math.Clamp(double.IsFinite(forwardMomentumBlend) ? forwardMomentumBlend : 0, 0, 0.6);
        if (blend > 0)
        {
            z += blend;
            var len = Math.Sqrt(x * x + y * y + z * z);
            if (len > 1e-9) { x /= len; y /= len; z /= len; }
        }
        return ((float)x, (float)y, (float)z);
    }

    /// <summary>
    /// Reads the optional "aphe" object from sprocket.expandedballistics.spall.json and returns
    /// the JSON without it for the strict SpallBalance.Parse parser (same pattern as HeshRules / HeRules).
    /// </summary>
    internal static ApheSettings Read(string json, Action<string> log, out string spallJson)
    {
        spallJson = json;
        try
        {
            var root = JsonNode.Parse(json, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject();
            if (root == null || !root.ContainsKey("aphe")) return ApheSettings.Default;
            var node = root["aphe"];
            root.Remove("aphe");
            spallJson = root.ToJsonString();
            if (node is not JsonObject obj)
            {
                log("[APHE] Ignoring non-object 'aphe' section in spall.json; using defaults.");
                return ApheSettings.Default;
            }
            var issues = new List<string>();
            double Num(string key, double def, double min, double max)
            {
                if (!obj.TryGetPropertyValue(key, out var val) || val == null) return def;
                if (val is JsonValue jv && jv.TryGetValue<double>(out var d) && double.IsFinite(d))
                {
                    if (d < min || d > max) { issues.Add($"{key}={d} clamped to {min}..{max}"); return Math.Clamp(d, min, max); }
                    return d;
                }
                issues.Add($"{key} invalid; default {def}");
                return def;
            }
            bool Bool(string key, bool defVal)
            {
                if (!obj.TryGetPropertyValue(key, out var val) || val == null) return defVal;
                if (val is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
                issues.Add($"{key} invalid; default {defVal}");
                return defVal;
            }
            var def = ApheSettings.Default;
            var result = new ApheSettings(
                FuseSensitivityRhaMm: Num("fuseSensitivityRhaMm", def.FuseSensitivityRhaMm, 1, 200),
                FuseDelayMeters: Num("fuseDelayMeters", def.FuseDelayMeters, 0, 3.0),
                MaxGapFraction: Num("maxGapFraction", def.MaxGapFraction, 0.15, 0.90),
                MinWallClearanceMeters: Num("minWallClearanceMeters", def.MinWallClearanceMeters, 0.01, 0.25),
                OpenFallbackMeters: Num("openFallbackMeters", def.OpenFallbackMeters, 0.02, 0.50),
                ConeHalfAngleDegrees: Num("coneHalfAngleDegrees", def.ConeHalfAngleDegrees, 15, 180),
                ForwardMomentumBlend: Num("forwardMomentumBlend", def.ForwardMomentumBlend, 0, 0.6),
                FragmentCountAt100Mm: (int)Math.Round(Num("fragmentCountAt100Mm", def.FragmentCountAt100Mm, 16, 160)),
                FragmentMassKgAt100Mm: Num("fragmentMassKgAt100Mm", def.FragmentMassKgAt100Mm, 0.2, 30.0),
                FragmentSpeedMetersPerSecond: Num("fragmentSpeedMetersPerSecond", def.FragmentSpeedMetersPerSecond, 250, 1800),
                ExplosionVisualScale: Num("explosionVisualScale", def.ExplosionVisualScale, 0.1, 2.0),
                DetonateOnStop: Bool("detonateOnStop", def.DetonateOnStop));
            if (issues.Count > 0) log("[APHE] spall.json adjustments: " + string.Join("; ", issues));
            return result;
        }
        catch (Exception ex)
        {
            log("[APHE] Could not read optional 'aphe' section; using defaults: " + ex.Message);
            return ApheSettings.Default;
        }
    }

    internal static string DetonationReportText(string reason, double accumulatedRhaMm, double offsetM, int spawned)
        => string.Create(CultureInfo.InvariantCulture,
            $"APHE detonation ({reason}): fuse armed after {accumulatedRhaMm:0.#} mm RHA, burst {offsetM * 1000:0} mm inside ({spawned} fragments).");

    internal static string Describe(ApheSettings s)
        => string.Create(CultureInfo.InvariantCulture,
            $"[APHE] fuse>={s.FuseSensitivityRhaMm:0.#}mm RHA delay={s.FuseDelayMeters:0.##}m cone={s.ConeHalfAngleDegrees:0.#}deg count@100mm={s.FragmentCountAt100Mm} mass@100mm={s.FragmentMassKgAt100Mm:0.##}kg speed={s.FragmentSpeedMetersPerSecond:0}m/s");
}
