using System.Numerics;
namespace SprocketVisualEffects;

// 0.5.0 HDR flash coronas and supersonic shockwave rings (Unity-free pure math).
internal enum BlastWaveKind : byte
{
    FlashCorona = 0,
    ShockwaveRing = 1
}

internal struct BlastWave
{
    internal BlastWaveKind Kind;
    internal Vector3 Origin, AxisRight, AxisUp;
    internal float Born, Life, R0, R1, Tau, Param, Intensity, R, G, B;
}

internal static class BlastWaveMath
{
    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        var l = v.LengthSquared();
        return l > 1e-6f && float.IsFinite(l) ? v / MathF.Sqrt(l) : fallback;
    }

    private static void RingPlane(Vector3 normal, out Vector3 right, out Vector3 up)
    {
        var n = SafeNorm(normal, Vector3.UnitY);
        var refVec = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        right = SafeNorm(Vector3.Cross(refVec, n), Vector3.UnitX);
        up = SafeNorm(Vector3.Cross(n, right), Vector3.UnitZ);
    }

    internal static bool Evaluate(in BlastWave w, float now, out float radius, out float alpha)
    {
        radius = 0f;
        alpha = 0f;
        if (w.Life <= 0f) return false;
        var age = now - w.Born;
        if (!float.IsFinite(age) || age < 0f || age >= w.Life) return false;
        var tau = Math.Max(w.Tau, 1e-3f);
        radius = w.R0 + (w.R1 - w.R0) * (1f - MathF.Exp(-age / tau));
        if (w.Kind == BlastWaveKind.FlashCorona)
        {
            var fade = 1f - Smooth(0.08f * w.Life, w.Life, age);
            alpha = Math.Clamp(w.Intensity * Smooth(0f, 0.008f, age) * fade, 0f, 4f);
        }
        else
        {
            var fade = 1f - Smooth(0.18f * w.Life, w.Life, age);
            alpha = Math.Clamp(w.Intensity * Smooth(0f, 0.015f, age) * fade, 0f, 3f);
        }
        return alpha > 1e-4f && radius > 1e-3f;
    }

    internal static void SpawnMuzzleWaves(Vector3 muzzle, Vector3 forward, float scale, float now, float fireIntensity, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.35f, 3.5f);
        var fi = Math.Clamp(float.IsFinite(fireIntensity) ? fireIntensity : 1.3f, 0.3f, 3f);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = muzzle + SafeNorm(forward, Vector3.UnitZ) * (0.28f * s),
                Born = now,
                Life = 0.09f,
                R0 = 0.35f * s,
                R1 = 1.65f * s,
                Tau = 0.028f,
                Param = 0.4f,
                Intensity = 0.95f * fi,
                R = 1f, G = 0.68f, B = 0.24f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(forward, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = muzzle + SafeNorm(forward, Vector3.UnitZ) * (0.40f * s),
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.22f,
                R0 = 0.35f * s,
                R1 = 3.2f * s,
                Tau = 0.065f,
                Param = 0.068f,
                Intensity = 0.78f,
                R = 1f, G = 0.85f, B = 0.66f
            };
        }
    }

    internal static void SpawnArmourImpactFlash(Vector3 pos, Vector3 normal, float scale, float now, bool ricochet, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(normal, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * 0.14f,
                Born = now,
                Life = ricochet ? 0.09f : 0.11f,
                R0 = 0.22f * s,
                R1 = (ricochet ? 1.25f : 1.55f) * s,
                Tau = 0.028f,
                Param = 1.1f,
                Intensity = ricochet ? 0.95f : 1.15f,
                R = 1f, G = 0.72f, B = 0.25f
            };
        }
    }

    internal static void SpawnExplosionWaves(Vector3 pos, Vector3 normal, float scale, float now, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(normal, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * (0.48f * s),
                Born = now,
                Life = 0.085f,
                R0 = 0.50f * s,
                R1 = 2.8f * s,
                Tau = 0.025f,
                Param = 0.7f,
                Intensity = 1.25f,
                R = 1f, G = 0.80f, B = 0.42f
            };
        }
        if (shockwave && into.Length > count)
        {
            var ringAxis = SafeNorm(n * 0.4f + Vector3.UnitY * 0.6f, Vector3.UnitY);
            RingPlane(ringAxis, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + ringAxis * (0.18f * s),
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.26f,
                R0 = 0.5f * s,
                R1 = 7.4f * s,
                Tau = 0.065f,
                Param = 0.065f,
                Intensity = 0.90f,
                R = 1f, G = 0.84f, B = 0.66f
            };
        }
    }

    internal static void SpawnCookoffWaves(Vector3 pos, float scale, float now, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.5f, 3.5f);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + Vector3.UnitY * (0.65f * s),
                Born = now,
                Life = 0.18f,
                R0 = 0.85f * s,
                R1 = 4.6f * s,
                Tau = 0.055f,
                Param = 0.2f,
                Intensity = 1.35f,
                R = 1f, G = 0.58f, B = 0.15f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(Vector3.UnitY, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + Vector3.UnitY * 0.25f,
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.48f,
                R0 = 0.8f * s,
                R1 = 12.0f * s,
                Tau = 0.12f,
                Param = 0.085f,
                Intensity = 0.95f,
                R = 1f, G = 0.82f, B = 0.62f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(SafeNorm(new Vector3(0.35f, 0.9f, 0.25f), Vector3.UnitY), out var rx2, out var ry2);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + Vector3.UnitY * 0.45f,
                AxisRight = rx2,
                AxisUp = ry2,
                Born = now + 0.05f,
                Life = 0.42f,
                R0 = 0.6f * s,
                R1 = 9.5f * s,
                Tau = 0.11f,
                Param = 0.068f,
                Intensity = 0.72f,
                R = 1f, G = 0.66f, B = 0.28f
            };
        }
    }

    internal static void SpawnEraWaves(Vector3 pos, Vector3 outward, float scale, float now, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(outward, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * (0.14f * s),
                Born = now,
                Life = 0.055f,
                R0 = 0.18f * s,
                R1 = 0.85f * s,
                Tau = 0.018f,
                Param = 0.8f,
                Intensity = 0.90f,
                R = 1f, G = 0.86f, B = 0.52f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(n, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + n * (0.10f * s),
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.14f,
                R0 = 0.18f * s,
                R1 = 1.9f * s,
                Tau = 0.038f,
                Param = 0.050f,
                Intensity = 0.65f,
                R = 1f, G = 0.88f, B = 0.72f
            };
        }
    }

    internal static void SpawnHeatWaves(Vector3 pos, Vector3 outward, float scale, float now, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(outward, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * (0.20f * s),
                Born = now,
                Life = 0.065f,
                R0 = 0.25f * s,
                R1 = 1.35f * s,
                Tau = 0.020f,
                Param = 0.9f,
                Intensity = 1.05f,
                R = 1f, G = 0.88f, B = 0.56f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(n, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + n * (0.14f * s),
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.17f,
                R0 = 0.25f * s,
                R1 = 2.8f * s,
                Tau = 0.045f,
                Param = 0.055f,
                Intensity = 0.72f,
                R = 1f, G = 0.88f, B = 0.70f
            };
        }
    }

    internal static void SpawnHeshWaves(Vector3 pos, Vector3 outward, float scale, float now, bool shockwave, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(outward, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * (0.25f * s),
                Born = now,
                Life = 0.075f,
                R0 = 0.32f * s,
                R1 = 1.75f * s,
                Tau = 0.022f,
                Param = 0.7f,
                Intensity = 1.10f,
                R = 1f, G = 0.82f, B = 0.45f
            };
        }
        if (shockwave && into.Length > count)
        {
            RingPlane(n, out var rx, out var ry);
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.ShockwaveRing,
                Origin = pos + n * (0.16f * s),
                AxisRight = rx,
                AxisUp = ry,
                Born = now,
                Life = 0.21f,
                R0 = 0.35f * s,
                R1 = 4.2f * s,
                Tau = 0.052f,
                Param = 0.062f,
                Intensity = 0.80f,
                R = 1f, G = 0.86f, B = 0.68f
            };
        }
    }

    internal static void SpawnApheWaves(Vector3 pos, Vector3 outward, float scale, float now, Span<BlastWave> into, out int count)
    {
        count = 0;
        var s = Math.Clamp(scale, 0.25f, 3.5f);
        var n = SafeNorm(outward, Vector3.UnitY);
        if (into.Length > count)
        {
            into[count++] = new BlastWave
            {
                Kind = BlastWaveKind.FlashCorona,
                Origin = pos + n * (0.16f * s),
                Born = now,
                Life = 0.070f,
                R0 = 0.25f * s,
                R1 = 1.4f * s,
                Tau = 0.022f,
                Param = 0.6f,
                Intensity = 0.95f,
                R = 1f, G = 0.78f, B = 0.36f
            };
        }
    }
}
