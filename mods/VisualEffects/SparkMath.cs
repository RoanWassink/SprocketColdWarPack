using System.Numerics;
namespace SprocketVisualEffects;

// 0.5.0 HDR velocity-stretched spark particles (Unity-free pure math, shared with Unity Editor Proof).
internal enum SparkKind : byte
{
    Muzzle = 0,
    Ricochet = 1,
    Penetration = 2,
    NonPenetration = 3,
    Explosion = 4,
    Cookoff = 5,
    Ground = 6,
    Era = 7,
    Heat = 8,
    Hesh = 9,
    Aphe = 10
}

internal struct Spark
{
    internal Vector3 Pos, Vel;
    internal float Age, Life, Drag, Gravity, Width, StreakSeconds, Brightness, Hotness, GroundY, Bounce, Seed;
    internal SparkKind Kind;
}

internal readonly struct SparkTuning
{
    internal readonly float Intensity, Scale;
    internal SparkTuning(float intensity, float scale)
    {
        Intensity = float.IsFinite(intensity) ? Math.Clamp(intensity, 0f, 3f) : 1.25f;
        Scale = float.IsFinite(scale) ? Math.Clamp(scale, 0.4f, 2.5f) : 1f;
    }
    internal static SparkTuning Default => new(1.25f, 1f);
}

internal static class SparkMath
{
    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Range(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    private static Vector3 SafeNorm(Vector3 v, Vector3 fallback)
    {
        var l = v.LengthSquared();
        return l > 1e-6f && float.IsFinite(l) ? v / MathF.Sqrt(l) : fallback;
    }

    private static void Basis(Vector3 axis, out Vector3 t1, out Vector3 t2)
    {
        axis = SafeNorm(axis, Vector3.UnitY);
        var refVec = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        t1 = SafeNorm(Vector3.Cross(refVec, axis), Vector3.UnitX);
        t2 = SafeNorm(Vector3.Cross(axis, t1), Vector3.UnitZ);
    }

    private static Vector3 ConeDir(Random r, Vector3 axis, Vector3 t1, Vector3 t2, float maxAngleDeg)
    {
        var maxSin = MathF.Sin(Math.Clamp(maxAngleDeg, 0.1f, 89.5f) * MathF.PI / 180f);
        var sinA = MathF.Sqrt((float)r.NextDouble()) * maxSin;
        var cosA = MathF.Sqrt(Math.Max(0f, 1f - sinA * sinA));
        var phi = Range(r, 0f, MathF.PI * 2f);
        return SafeNorm(axis * cosA + (t1 * MathF.Cos(phi) + t2 * MathF.Sin(phi)) * sinA, axis);
    }

    private static Vector3 AnnularDir(Random r, Vector3 axis, Vector3 t1, Vector3 t2, float minDeg, float maxDeg)
    {
        var ang = Range(r, Math.Clamp(minDeg, 0.1f, 89f), Math.Clamp(maxDeg, 0.2f, 89.5f)) * MathF.PI / 180f;
        var sinA = MathF.Sin(ang);
        var cosA = MathF.Cos(ang);
        var phi = Range(r, 0f, MathF.PI * 2f);
        return SafeNorm(axis * cosA + (t1 * MathF.Cos(phi) + t2 * MathF.Sin(phi)) * sinA, axis);
    }

    private static int ScaledCount(int minCount, int maxCount, float scale, float intensity, int capacity)
    {
        if (intensity <= 0f || capacity <= 0) return 0;
        var sq = MathF.Sqrt(Math.Clamp(scale, 0.25f, 3.5f));
        var baseCount = minCount + (maxCount - minCount) * Math.Clamp((sq - 0.5f) / 1.3f, 0f, 1f);
        return Math.Clamp((int)MathF.Round(baseCount * intensity), 1, capacity);
    }

    internal static void Step(ref Spark s, float dt)
    {
        if (s.Life <= 0f || !float.IsFinite(dt) || dt <= 0f) return;
        s.Age += dt;
        if (s.Age >= s.Life || !float.IsFinite(s.Age)) { s.Life = 0f; return; }
        var decay = MathF.Exp(-s.Drag * dt);
        s.Vel *= decay;
        s.Vel.Y -= 9.81f * s.Gravity * dt;
        s.Pos += s.Vel * dt;
        if (s.Pos.Y < s.GroundY && s.Vel.Y < 0f)
        {
            s.Pos.Y = s.GroundY + (s.GroundY - s.Pos.Y) * 0.25f;
            s.Vel.Y = -s.Vel.Y * s.Bounce;
            s.Vel.X *= 0.72f;
            s.Vel.Z *= 0.72f;
            var ang = s.Seed * 1.37f + s.Age * 19.1f;
            var kick = MathF.Abs(s.Vel.Y) * 0.14f;
            s.Vel.X += MathF.Cos(ang) * kick;
            s.Vel.Z += MathF.Sin(ang) * kick;
        }
    }

    internal static void ComputeQuad(
        in Spark s, Vector3 camPos,
        out Vector3 v0, out Vector3 v1, out Vector3 v2, out Vector3 v3,
        out float alpha, out float brightness, out float hotness,
        out float r, out float g, out float b)
    {
        var u = Math.Clamp(s.Age / Math.Max(s.Life, 1e-4f), 0f, 1f);
        var flicker = 0.82f + 0.18f * MathF.Sin(s.Age * 58f + s.Seed * 3.7f);
        alpha = Smooth(0f, 0.012f, s.Age) * (1f - Smooth(0.45f, 1f, u)) * flicker;
        brightness = s.Brightness * (1f - 0.55f * Smooth(0.55f, 1f, u));
        hotness = Math.Clamp(s.Hotness * (1f - Smooth(0.08f, 0.72f, u)), 0f, 1f);

        // Blackbody cooling ramp: white-gold -> bright gold -> fiery orange -> deep cherry ember.
        var temp = Math.Clamp(1.05f * (1f - u * 0.85f) * (0.65f + 0.35f * s.Hotness), 0f, 1f);
        if (temp >= 0.72f)
        {
            var t = (temp - 0.72f) / 0.28f;
            r = 1f; g = 0.68f + 0.26f * t; b = 0.18f + 0.60f * t;
        }
        else if (temp >= 0.38f)
        {
            var t = (temp - 0.38f) / 0.34f;
            r = 1f; g = 0.32f + 0.36f * t; b = 0.05f + 0.13f * t;
        }
        else
        {
            var t = temp / 0.38f;
            r = 0.75f + 0.25f * t; g = 0.08f + 0.24f * t; b = 0.01f + 0.04f * t;
        }

        var speed = s.Vel.Length();
        var dir = speed > 1e-4f ? s.Vel / speed : Vector3.UnitY;
        var maxStreak = Math.Clamp(s.Width * 24f, 0.25f, 0.90f);
        var streakLen = Math.Clamp(speed * s.StreakSeconds, s.Width * 2.2f, maxStreak);
        var head = s.Pos;
        var tail = head - dir * streakLen;
        var mid = (head + tail) * 0.5f;
        var toCam = camPos - mid;
        var side = Vector3.Cross(dir, toCam);
        if (side.LengthSquared() < 1e-6f)
            side = Vector3.Cross(dir, MathF.Abs(dir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        side = SafeNorm(side, Vector3.UnitX) * (s.Width * 0.5f);

        v0 = head - side;
        v1 = head + side;
        v2 = tail + side;
        v3 = tail - side;
    }

    internal static void SpawnMuzzleSparks(Random rng, Vector3 muzzle, Vector3 forward, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.35f, 3.5f) * tuning.Scale;
        count = ScaledCount(26, 44, s, tuning.Intensity, into.Length);
        var sq = MathF.Sqrt(s);
        Basis(forward, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var dir = ConeDir(rng, forward, t1, t2, i < count * 3 / 4 ? 14f : 32f);
            into[i] = new Spark
            {
                Kind = SparkKind.Muzzle,
                Pos = muzzle + forward * Range(rng, 0.05f, 0.45f * sq),
                Vel = dir * (Range(rng, 30f, 80f) * sq),
                Age = 0f,
                Life = Range(rng, 0.25f, 0.75f) * MathF.Sqrt(sq),
                Drag = Range(rng, 2.3f, 3.3f),
                Gravity = Range(rng, 0.35f, 0.6f),
                Width = Range(rng, 0.022f, 0.044f) * sq,
                StreakSeconds = Range(rng, 0.026f, 0.038f),
                Brightness = Range(rng, 1.25f, 1.90f),
                Hotness = Range(rng, 0.85f, 1.0f),
                GroundY = groundY,
                Bounce = 0.32f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnRicochetSparks(Random rng, Vector3 pos, Vector3 normal, Vector3 inVelocity, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var n = SafeNorm(normal, Vector3.UnitY);
        Vector3 refl;
        if (inVelocity.LengthSquared() > 1f)
        {
            var inDir = Vector3.Normalize(inVelocity);
            refl = SafeNorm(inDir - 2f * Vector3.Dot(inDir, n) * n + n * 0.18f, n);
        }
        else refl = SafeNorm(n + Vector3.UnitY * 0.25f, n);

        var fastCount = ScaledCount(45, 80, s, tuning.Intensity, into.Length);
        var slagCount = Math.Min(into.Length - fastCount, Math.Max(0, (int)MathF.Round(16f * sq * tuning.Intensity)));
        count = fastCount + slagCount;

        Basis(refl, out var t1, out var t2);
        for (var i = 0; i < fastCount; i++)
        {
            var dir = ConeDir(rng, refl, t1, t2, i < fastCount * 4 / 5 ? 16f : 32f);
            into[i] = new Spark
            {
                Kind = SparkKind.Ricochet,
                Pos = pos + n * 0.05f,
                Vel = dir * (Range(rng, 25f, 75f) * sq),
                Age = 0f,
                Life = Range(rng, 0.45f, 1.35f) * (0.7f + 0.3f * sq),
                Drag = Range(rng, 1.5f, 2.3f),
                Gravity = Range(rng, 0.7f, 0.95f),
                Width = Range(rng, 0.026f, 0.052f) * sq,
                StreakSeconds = Range(rng, 0.034f, 0.048f),
                Brightness = Range(rng, 1.45f, 2.25f),
                Hotness = Range(rng, 0.92f, 1.0f),
                GroundY = groundY,
                Bounce = 0.44f,
                Seed = Range(rng, 0f, 97f)
            };
        }

        Basis(n, out var nt1, out var nt2);
        for (var i = 0; i < slagCount; i++)
        {
            var dir = ConeDir(rng, SafeNorm(refl * 0.55f + n * 0.45f, n), nt1, nt2, 40f);
            into[fastCount + i] = new Spark
            {
                Kind = SparkKind.Ricochet,
                Pos = pos + n * 0.04f,
                Vel = dir * (Range(rng, 10f, 26f) * sq),
                Age = 0f,
                Life = Range(rng, 0.9f, 1.9f),
                Drag = Range(rng, 1.1f, 1.7f),
                Gravity = 1f,
                Width = Range(rng, 0.030f, 0.058f) * sq,
                StreakSeconds = Range(rng, 0.028f, 0.040f),
                Brightness = Range(rng, 1.1f, 1.7f),
                Hotness = Range(rng, 0.7f, 0.92f),
                GroundY = groundY,
                Bounce = 0.48f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnPenetrationSparks(Random rng, Vector3 pos, Vector3 normal, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var n = SafeNorm(normal, Vector3.UnitY);
        count = ScaledCount(50, 90, s, tuning.Intensity, into.Length);
        Basis(n, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var ember = i >= count * 4 / 5;
            var dir = ConeDir(rng, n, t1, t2, ember ? 58f : 32f);
            into[i] = new Spark
            {
                Kind = SparkKind.Penetration,
                Pos = pos + n * 0.05f,
                Vel = dir * (Range(rng, ember ? 8f : 16f, ember ? 24f : 58f) * sq),
                Age = 0f,
                Life = Range(rng, ember ? 0.9f : 0.5f, ember ? 1.85f : 1.55f),
                Drag = ember ? 1.35f : Range(rng, 1.7f, 2.5f),
                Gravity = Range(rng, 0.8f, 1.05f),
                Width = Range(rng, 0.026f, 0.054f) * sq,
                StreakSeconds = Range(rng, 0.032f, 0.044f),
                Brightness = Range(rng, 1.35f, 2.15f),
                Hotness = ember ? 0.78f : Range(rng, 0.9f, 1.0f),
                GroundY = groundY,
                Bounce = 0.38f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnNonPenetrationSparks(Random rng, Vector3 pos, Vector3 normal, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var n = SafeNorm(normal, Vector3.UnitY);
        count = ScaledCount(55, 100, s, tuning.Intensity, into.Length);
        Basis(n, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            Vector3 dir;
            var radial = i < count * 7 / 10;
            if (radial)
            {
                var phi = Range(rng, 0f, MathF.PI * 2f);
                var elev = Range(rng, 0.08f, 0.48f);
                dir = SafeNorm(t1 * MathF.Cos(phi) + t2 * MathF.Sin(phi) + n * elev, n);
            }
            else dir = ConeDir(rng, n, t1, t2, 55f);

            into[i] = new Spark
            {
                Kind = SparkKind.NonPenetration,
                Pos = pos + n * 0.05f,
                Vel = dir * (Range(rng, 18f, 62f) * sq),
                Age = 0f,
                Life = Range(rng, 0.5f, 1.6f),
                Drag = Range(rng, 1.8f, 2.6f),
                Gravity = Range(rng, 0.75f, 1f),
                Width = Range(rng, 0.025f, 0.052f) * sq,
                StreakSeconds = Range(rng, 0.032f, 0.044f),
                Brightness = Range(rng, 1.45f, 2.25f),
                Hotness = Range(rng, 0.88f, 1.0f),
                GroundY = groundY,
                Bounce = 0.40f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnExplosionSparks(Random rng, Vector3 pos, Vector3 normal, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var axis = SafeNorm(normal * 0.55f + Vector3.UnitY * 0.75f, Vector3.UnitY);
        count = ScaledCount(50, 110, s, tuning.Intensity, into.Length);
        Basis(axis, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var dir = ConeDir(rng, axis, t1, t2, i < count * 3 / 4 ? 78f : 42f);
            var slowEmber = i % 7 == 0;
            into[i] = new Spark
            {
                Kind = SparkKind.Explosion,
                Pos = pos + axis * 0.12f,
                Vel = dir * (Range(rng, slowEmber ? 12f : 24f, slowEmber ? 26f : 78f) * sq),
                Age = 0f,
                Life = Range(rng, slowEmber ? 0.55f : 0.30f, slowEmber ? 1.25f : 1.05f) * (0.75f + 0.25f * sq),
                Drag = slowEmber ? 1.4f : Range(rng, 1.8f, 2.6f),
                Gravity = Range(rng, 0.75f, 1f),
                Width = Range(rng, 0.030f, 0.064f) * sq,
                StreakSeconds = Range(rng, 0.034f, 0.046f),
                Brightness = Range(rng, 1.35f, 2.15f),
                Hotness = Range(rng, 0.90f, 1.0f),
                GroundY = groundY,
                Bounce = 0.42f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnCookoffSparks(Random rng, Vector3 pos, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.5f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        count = ScaledCount(110, 180, s, tuning.Intensity, into.Length);
        Basis(Vector3.UnitY, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var fountain = i < count * 2 / 3;
            var dir = ConeDir(rng, Vector3.UnitY, t1, t2, fountain ? 30f : 72f);
            into[i] = new Spark
            {
                Kind = SparkKind.Cookoff,
                Pos = pos + Vector3.UnitY * 0.2f,
                Vel = dir * (Range(rng, fountain ? 20f : 14f, fountain ? 55f : 42f) * sq),
                Age = 0f,
                Life = Range(rng, 0.9f, 2.6f),
                Drag = Range(rng, 1.1f, 1.6f),
                Gravity = Range(rng, 0.85f, 1.05f),
                Width = Range(rng, 0.034f, 0.072f) * sq,
                StreakSeconds = Range(rng, 0.036f, 0.050f),
                Brightness = Range(rng, 1.50f, 2.35f),
                Hotness = Range(rng, 0.85f, 1.0f),
                GroundY = groundY,
                Bounce = 0.48f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    internal static void SpawnGroundSparks(Random rng, Vector3 pos, Vector3 normal, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var n = SafeNorm(normal, Vector3.UnitY);
        count = ScaledCount(16, 32, s, tuning.Intensity, into.Length);
        Basis(n, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var dir = ConeDir(rng, n, t1, t2, 42f);
            into[i] = new Spark
            {
                Kind = SparkKind.Ground,
                Pos = pos + n * 0.08f,
                Vel = dir * (Range(rng, 12f, 38f) * sq),
                Age = 0f,
                Life = Range(rng, 0.35f, 1.15f),
                Drag = Range(rng, 1.9f, 2.8f),
                Gravity = 1f,
                Width = Range(rng, 0.024f, 0.048f) * sq,
                StreakSeconds = Range(rng, 0.026f, 0.038f),
                Brightness = Range(rng, 1.05f, 1.65f),
                Hotness = Range(rng, 0.72f, 0.95f),
                GroundY = groundY,
                Bounce = 0.35f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    // 0.5.2: Directional ERA brick jet (~75% outward flyer cone + ~25% flat cassette edge shear).
    internal static void SpawnEraSparks(Random rng, Vector3 pos, Vector3 outward, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var axis = SafeNorm(outward, Vector3.UnitY);
        count = ScaledCount(32, 52, s, tuning.Intensity, into.Length);
        Basis(axis, out var t1, out var t2);
        var fwdSplit = count * 3 / 4;
        for (var i = 0; i < count; i++)
        {
            var isFwd = i < fwdSplit;
            var dir = isFwd ? ConeDir(rng, axis, t1, t2, i < fwdSplit / 2 ? 24f : 40f) : AnnularDir(rng, axis, t1, t2, 76f, 88f);
            into[i] = new Spark
            {
                Kind = SparkKind.Era,
                Pos = pos + axis * 0.06f,
                Vel = dir * (Range(rng, isFwd ? 30f : 22f, isFwd ? 82f : 52f) * sq),
                Age = 0f,
                Life = Range(rng, isFwd ? 0.24f : 0.20f, isFwd ? 0.78f : 0.55f),
                Drag = Range(rng, 1.8f, 2.6f),
                Gravity = Range(rng, 0.65f, 0.90f),
                Width = Range(rng, 0.016f, 0.032f) * sq,
                StreakSeconds = Range(rng, 0.022f, 0.032f),
                Brightness = Range(rng, 1.35f, 1.95f),
                Hotness = Range(rng, 0.92f, 1.0f),
                GroundY = groundY,
                Bounce = 0.42f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    // 0.5.3: HEAT backsplash (~70% fierce narrow outward stream + ~30% armor-skimming splash crown).
    internal static void SpawnHeatSparks(Random rng, Vector3 pos, Vector3 outward, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var axis = SafeNorm(outward, Vector3.UnitY);
        count = ScaledCount(45, 75, s, tuning.Intensity, into.Length);
        Basis(axis, out var t1, out var t2);
        var fwdSplit = count * 7 / 10;
        for (var i = 0; i < count; i++)
        {
            var isFwd = i < fwdSplit;
            var dir = isFwd ? ConeDir(rng, axis, t1, t2, i < fwdSplit / 2 ? 18f : 34f) : AnnularDir(rng, axis, t1, t2, 68f, 84f);
            into[i] = new Spark
            {
                Kind = SparkKind.Heat,
                Pos = pos + axis * 0.08f,
                Vel = dir * (Range(rng, isFwd ? 36f : 26f, isFwd ? 95f : 62f) * sq),
                Age = 0f,
                Life = Range(rng, isFwd ? 0.28f : 0.25f, isFwd ? 0.90f : 0.72f),
                Drag = Range(rng, 1.7f, 2.5f),
                Gravity = Range(rng, 0.70f, 0.95f),
                Width = Range(rng, 0.018f, 0.036f) * sq,
                StreakSeconds = Range(rng, 0.025f, 0.036f),
                Brightness = Range(rng, 1.45f, 2.10f),
                Hotness = Range(rng, 0.94f, 1.0f),
                GroundY = groundY,
                Bounce = 0.44f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    // 0.5.3: HESH squash blast (~55% broad outward spall/jet cone + ~45% fierce plate-flush radial spray).
    internal static void SpawnHeshSparks(Random rng, Vector3 pos, Vector3 outward, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var axis = SafeNorm(outward, Vector3.UnitY);
        count = ScaledCount(60, 95, s, tuning.Intensity, into.Length);
        Basis(axis, out var t1, out var t2);
        var fwdSplit = count * 55 / 100;
        for (var i = 0; i < count; i++)
        {
            var isFwd = i < fwdSplit;
            var dir = isFwd ? ConeDir(rng, axis, t1, t2, i < fwdSplit / 2 ? 38f : 62f) : AnnularDir(rng, axis, t1, t2, 70f, 88f);
            into[i] = new Spark
            {
                Kind = SparkKind.Hesh,
                Pos = pos + axis * 0.08f,
                Vel = dir * (Range(rng, isFwd ? 26f : 28f, isFwd ? 74f : 68f) * sq),
                Age = 0f,
                Life = Range(rng, isFwd ? 0.32f : 0.28f, isFwd ? 1.00f : 0.85f),
                Drag = Range(rng, 1.6f, 2.4f),
                Gravity = Range(rng, 0.75f, 1.0f),
                Width = Range(rng, 0.020f, 0.042f) * sq,
                StreakSeconds = Range(rng, 0.026f, 0.036f),
                Brightness = Range(rng, 1.35f, 1.95f),
                Hotness = Range(rng, 0.88f, 1.0f),
                GroundY = groundY,
                Bounce = 0.42f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }

    // 0.5.2: Compact internal APHE breach sparks.
    internal static void SpawnApheSparks(Random rng, Vector3 pos, Vector3 outward, float scale, float groundY, in SparkTuning tuning, Span<Spark> into, out int count)
    {
        var s = Math.Clamp(scale, 0.25f, 3.5f) * tuning.Scale;
        var sq = MathF.Sqrt(s);
        var axis = SafeNorm(outward, Vector3.UnitY);
        count = ScaledCount(24, 44, s, tuning.Intensity, into.Length);
        Basis(axis, out var t1, out var t2);
        for (var i = 0; i < count; i++)
        {
            var dir = ConeDir(rng, axis, t1, t2, i < count / 2 ? 30f : 55f);
            into[i] = new Spark
            {
                Kind = SparkKind.Aphe,
                Pos = pos + axis * 0.05f,
                Vel = dir * (Range(rng, 16f, 48f) * sq),
                Age = 0f,
                Life = Range(rng, 0.25f, 0.80f),
                Drag = Range(rng, 1.8f, 2.6f),
                Gravity = Range(rng, 0.75f, 1.0f),
                Width = Range(rng, 0.022f, 0.044f) * sq,
                StreakSeconds = Range(rng, 0.028f, 0.040f),
                Brightness = Range(rng, 1.35f, 2.10f),
                Hotness = Range(rng, 0.88f, 1.0f),
                GroundY = groundY,
                Bounce = 0.40f,
                Seed = Range(rng, 0f, 97f)
            };
        }
    }
}
