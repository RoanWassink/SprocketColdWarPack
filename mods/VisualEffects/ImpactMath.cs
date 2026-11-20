using System.Numerics;
namespace SprocketVisualEffects;

// 0.5.0 Impact/explosion constants, scaling, duplicate guard and light curves (Unity-free pure math).
internal enum ImpactKind : byte
{
    Explosion = 0,
    EnvironmentImpact = 1,
    Penetration = 2,
    NonPenetration = 3,
    Ricochet = 4,
    Cookoff = 5,
    Era = 6,
    Heat = 7,
    Hesh = 8,
    Aphe = 9
}

internal readonly struct ImpactTuning
{
    internal readonly bool Enabled, Sparks, Shockwaves, ImpactLights, CookoffEffects;
    internal readonly float ScaleMultiplier, SparkIntensity, ExplosionFireballIntensity, MaxDistance;

    internal ImpactTuning(bool enabled, bool sparks, bool shockwaves, bool impactLights, bool cookoffEffects,
        float scaleMultiplier, float sparkIntensity, float explosionFireballIntensity, float maxDistance)
    {
        static float ClampSafe(float v, float fallback, float lo, float hi) => float.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;
        Enabled = enabled;
        Sparks = sparks;
        Shockwaves = shockwaves;
        ImpactLights = impactLights;
        CookoffEffects = cookoffEffects;
        ScaleMultiplier = ClampSafe(scaleMultiplier, 1.2f, 0.4f, 2.5f);
        SparkIntensity = ClampSafe(sparkIntensity, 1.25f, 0f, 3f);
        ExplosionFireballIntensity = ClampSafe(explosionFireballIntensity, 1.3f, 0f, 3f);
        MaxDistance = ClampSafe(maxDistance, 1800f, 100f, 5000f);
    }

    internal static ImpactTuning Default => new(true, true, true, true, true, 1.2f, 1.25f, 1.3f, 1800f);
}

internal static class ImpactMath
{
    internal static readonly Vector3 ExplosionProxyMin = new(-4.5f, -4.0f, -4.5f);
    internal static readonly Vector3 ExplosionProxyMax = new(4.5f, 8.0f, 5.2f);
    internal static readonly Vector3 ExplosionBoxMin = new(-4.5f, -1.5f, -4.5f);
    internal static readonly Vector3 ExplosionBoxMax = new(4.5f, 8.0f, 4.5f);
    internal static readonly Vector3 DirectionalBoxMin = new(-4.0f, -4.0f, -1.6f);
    internal static readonly Vector3 DirectionalBoxMax = new(4.0f, 4.0f, 5.2f);

    internal const float ExplosionVolumeLifetime = 0.85f;
    internal const float CookoffVolumeLifetime = 2.8f;
    internal const float EraVolumeLifetime = 0.42f;
    internal const float HeatVolumeLifetime = 0.52f;
    internal const float HeshVolumeLifetime = 0.68f;
    internal const float ApheVolumeLifetime = 0.48f;

    internal const float EraDiskRatio = 0.55f;
    internal const float HeatDiskRatio = 0.78f;
    internal const float HeshDiskRatio = 1.30f;

    internal const float ExplosionFireLuminance = 3600000f;
    internal const float ExplosionFireStrength = 30f;

    internal const float ImpactFlashCandela = 450000f, ImpactFlashRange = 16f, ImpactFlashDuration = 0.14f;
    internal const float ExplosionFlashCandela = 1800000f, ExplosionFlashRange = 42f, ExplosionFlashDuration = 0.22f;
    internal const float CookoffFlashCandela = 2600000f, CookoffFlashRange = 55f, CookoffFlashDuration = 1.15f;
    internal const float EraFlashCandela = 280000f, EraFlashRange = 14f, EraFlashDuration = 0.09f;
    internal const float HeatFlashCandela = 550000f, HeatFlashRange = 22f, HeatFlashDuration = 0.11f;
    internal const float HeshFlashCandela = 850000f, HeshFlashRange = 30f, HeshFlashDuration = 0.15f;
    internal const float ApheFlashCandela = 260000f, ApheFlashRange = 12f, ApheFlashDuration = 0.09f;

    internal const float ExplosionSootR = 0.08f, ExplosionSootG = 0.075f, ExplosionSootB = 0.07f;
    internal const float CookoffSootR = 0.05f, CookoffSootG = 0.045f, CookoffSootB = 0.045f;

    private static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float ClampMult(float multiplier) => float.IsFinite(multiplier) ? Math.Clamp(multiplier, 0.4f, 2.5f) : 1.2f;

    internal static float ImpactScale(float calibreMm, float multiplier)
    {
        var cal = float.IsFinite(calibreMm) && calibreMm > 0f ? Math.Clamp(calibreMm, 12f, 300f) : 75f;
        var m = ClampMult(multiplier);
        return Math.Clamp(MathF.Pow(cal / 100f, 0.65f) * m, 0.25f, 3.5f);
    }

    // 0.5.3: Realistic compact sizing for ERA cassette tiles, HEAT backsplash, HESH squash charge and internal APHE.
    internal static float EraScale(float multiplier)
    {
        var m = ClampMult(multiplier);
        return Math.Clamp(0.30f * (0.70f + 0.30f * m), 0.22f, 0.44f);
    }

    internal static float HeatScale(float calibreMm, float multiplier)
        => Math.Clamp(ImpactScale(calibreMm, multiplier) * 0.46f, 0.26f, 1.05f);

    internal static float HeshScale(float calibreMm, float multiplier)
        => Math.Clamp(ImpactScale(calibreMm, multiplier) * 0.68f, 0.36f, 1.50f);

    internal static float ApheScale(float calibreMm, float multiplier)
        => Math.Clamp(ImpactScale(calibreMm, multiplier) * 0.38f, 0.24f, 0.80f);

    internal static float CookoffScale(float mass, float impulse, float multiplier)
    {
        var mf = float.IsFinite(mass) && mass > 0f ? Math.Clamp(mass / 100f, 0.4f, 2.5f) : 1f;
        var imf = float.IsFinite(impulse) && impulse > 0f ? Math.Clamp(impulse / 10000f, 0.4f, 2.5f) : 1f;
        var raw = 1.35f * MathF.Sqrt(MathF.Max(mf, imf));
        var m = ClampMult(multiplier);
        return Math.Clamp(raw * (0.7f + 0.3f * m), 1.1f, 2.6f);
    }

    internal static ImpactKind ResolveExplosionKind(string? apiKind) => apiKind switch
    {
        "era" => ImpactKind.Era,
        "heat" => ImpactKind.Heat,
        "hesh" => ImpactKind.Hesh,
        "aphe" => ImpactKind.Aphe,
        _ => ImpactKind.Explosion
    };

    // Guarantees outward blast direction faces away from armor/hull rather than into it.
    internal static Vector3 ResolveOutwardAxis(Vector3 hitNormal, Vector3 hitVelocity)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        var hasVel = Finite(hitVelocity) && hitVelocity.LengthSquared() > 1e-2f;
        var backDir = hasVel ? -Vector3.Normalize(hitVelocity) : Vector3.Zero;
        var hasNorm = Finite(hitNormal) && hitNormal.LengthSquared() > 1e-6f;
        if (hasNorm)
        {
            var n = Vector3.Normalize(hitNormal);
            if (hasVel && Vector3.Dot(n, backDir) < -0.05f) n = -n;
            return n;
        }
        return hasVel ? backDir : Vector3.UnitY;
    }

    internal static float ImpactLightCurve(float age)
    {
        if (!float.IsFinite(age) || age <= 0f || age >= ImpactFlashDuration) return 0f;
        return Smooth(0f, 0.01f, age) * (1f - Smooth(0.02f, ImpactFlashDuration, age));
    }

    internal static float ExplosionLightCurve(float age) => TimedLightCurve(age, ExplosionFlashDuration);

    internal static float TimedLightCurve(float age, float duration)
    {
        if (!float.IsFinite(age) || !float.IsFinite(duration) || duration <= 0.02f || age <= 0f || age >= duration) return 0f;
        var peakEnd = Math.Min(duration * 0.55f, 0.12f);
        var peak = Smooth(0f, 0.008f, age) * (1f - Smooth(0.022f, peakEnd, age));
        var afterglow = 0.16f * Smooth(0.012f, 0.035f, age) * (1f - Smooth(0.05f, duration, age));
        return Math.Clamp(MathF.Max(peak, afterglow), 0f, 1f);
    }

    internal static float CookoffLightCurve(float age)
    {
        if (!float.IsFinite(age) || age <= 0f || age >= CookoffFlashDuration) return 0f;
        var pulse = 0.82f + 0.18f * MathF.Sin(age * 28f);
        return Smooth(0f, 0.025f, age) * (1f - Smooth(0.25f, CookoffFlashDuration, age)) * pulse;
    }

    // Ring buffer rejecting exact duplicate same-kind triggers at the same position & frame window.
    internal sealed class DuplicateFilter
    {
        private struct Entry
        {
            internal ImpactKind Kind;
            internal Vector3 Pos;
            internal float Time;
            internal bool Used;
        }
        private readonly Entry[] entries = new Entry[16];
        private int next;

        internal bool Accept(ImpactKind kind, Vector3 pos, float now)
        {
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z) || !float.IsFinite(now)) return false;
            const float distSqLimit = 0.08f * 0.08f;
            const float dtLimit = 0.015f;
            for (var i = 0; i < entries.Length; i++)
            {
                ref readonly var e = ref entries[i];
                if (!e.Used || e.Kind != kind) continue;
                if (MathF.Abs(now - e.Time) < dtLimit && Vector3.DistanceSquared(e.Pos, pos) < distSqLimit)
                    return false;
            }
            entries[next] = new Entry { Kind = kind, Pos = pos, Time = now, Used = true };
            next = (next + 1) % entries.Length;
            return true;
        }

        internal void Clear()
        {
            for (var i = 0; i < entries.Length; i++) entries[i].Used = false;
            next = 0;
        }
    }

    internal readonly struct ClusterDecision
    {
        internal readonly bool SpawnVolume, SpawnWave, SpawnLight, ReplacePrevious;
        internal readonly float SparkMul, PuffMul;
        internal readonly int EntryIndex, ReplacedVolSlot, ReplacedLightSlot;
        internal readonly string Tag;

        internal ClusterDecision(bool vol, bool wave, bool light, float sparkMul, float puffMul, bool replace, int entryIndex, int oldVol, int oldLight, string tag)
        {
            SpawnVolume = vol; SpawnWave = wave; SpawnLight = light;
            SparkMul = sparkMul; PuffMul = puffMul;
            ReplacePrevious = replace; EntryIndex = entryIndex;
            ReplacedVolSlot = oldVol; ReplacedLightSlot = oldLight; Tag = tag;
        }
    }

    // 0.5.3: Coalesces multi-hit clusters at the same impact point (e.g. Duplet ERA + HEAT + Penetration)
    // so only ONE primary fireball volume, flash corona and point light render per point.
    internal sealed class ClusterLimiter
    {
        private struct Entry
        {
            internal Vector3 Pos;
            internal float Time;
            internal int MaxRank;
            internal int VolSlot;
            internal int LightSlot;
            internal bool Used;
        }

        private readonly Entry[] entries = new Entry[16];
        private int next;

        internal static int Rank(ImpactKind kind) => kind switch
        {
            ImpactKind.Cookoff => 5,
            ImpactKind.Explosion => 4,
            ImpactKind.Hesh => 3,
            ImpactKind.Heat => 2,
            ImpactKind.Era => 1,
            ImpactKind.Aphe => 1,
            _ => 0
        };

        internal ClusterDecision Evaluate(ImpactKind kind, Vector3 pos, float now)
        {
            const float radiusSq = 0.90f * 0.90f;
            const float dtLimit = 0.10f;
            var rank = Rank(kind);
            var wantVol = rank > 0;
            var wantWave = kind != ImpactKind.EnvironmentImpact;
            var wantLight = kind != ImpactKind.EnvironmentImpact;
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z) || !float.IsFinite(now))
                return new ClusterDecision(wantVol, wantWave, wantLight, 1f, 1f, false, -1, -1, -1, "ok");

            var best = -1;
            var bestAge = float.MaxValue;
            for (var i = 0; i < entries.Length; i++)
            {
                if (!entries[i].Used) continue;
                var age = MathF.Abs(now - entries[i].Time);
                if (age <= dtLimit && Vector3.DistanceSquared(entries[i].Pos, pos) <= radiusSq)
                {
                    if (best < 0 || entries[i].MaxRank > entries[best].MaxRank || (entries[i].MaxRank == entries[best].MaxRank && age < bestAge))
                    {
                        best = i;
                        bestAge = age;
                    }
                }
            }

            if (best < 0)
            {
                var idx = next;
                next = (next + 1) % entries.Length;
                entries[idx] = new Entry { Pos = pos, Time = now, MaxRank = rank, VolSlot = -1, LightSlot = -1, Used = true };
                return new ClusterDecision(wantVol, wantWave, wantLight, 1f, 1f, false, idx, -1, -1, "ok");
            }

            ref var cur = ref entries[best];
            if (rank <= cur.MaxRank)
            {
                var mul = rank == 0 && cur.MaxRank >= 1 ? 0.35f : rank == 0 ? 0.60f : 0.45f;
                return new ClusterDecision(false, false, false, mul, mul, false, best, -1, -1, "coalesced");
            }

            var oldVol = cur.VolSlot;
            var oldLight = cur.LightSlot;
            cur.Pos = pos;
            cur.Time = now;
            cur.MaxRank = rank;
            cur.VolSlot = -1;
            cur.LightSlot = -1;
            return new ClusterDecision(wantVol, wantWave, wantLight, 0.75f, 0.75f, true, best, oldVol, oldLight, "upgrade");
        }

        internal void SetSlots(int entryIndex, int volSlot, int lightSlot)
        {
            if (entryIndex < 0 || entryIndex >= entries.Length || !entries[entryIndex].Used) return;
            if (volSlot >= 0) entries[entryIndex].VolSlot = volSlot;
            if (lightSlot >= 0) entries[entryIndex].LightSlot = lightSlot;
        }

        internal void Clear()
        {
            for (var i = 0; i < entries.Length; i++) entries[i].Used = false;
            next = 0;
        }
    }
}
