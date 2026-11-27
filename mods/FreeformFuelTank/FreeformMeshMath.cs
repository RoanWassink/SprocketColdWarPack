using System;
using System.Collections.Generic;

namespace SprocketFreeformFuelTank;

/// <summary>
/// Pure math helpers for computing enclosed volume, centroid, watertightness,
/// and vanilla-parity fuel mass/cost from a triangulated freeform mesh.
/// Kept free of Unity/IL2CPP types so it can be unit-tested directly.
/// </summary>
public static class FreeformMeshMath
{
    public const float VanillaDieselDensityKgPerLitre = 0.832f;
    public const float VanillaFuelCostPerKg = 0.20249656f;
    public const float LitresPerCubicMeter = 1000f;

    public readonly struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public readonly struct MeshVolumeResult
    {
        public readonly double VolumeCubicMeters;
        public readonly double SignedVolumeCubicMeters;
        public readonly Vec3 Centroid;
        public readonly bool IsClosed;
        public readonly bool IsInsideOut;
        public readonly int OpenEdgeCount;
        public readonly int TriangleCount;

        public MeshVolumeResult(
            double volumeCubicMeters,
            double signedVolumeCubicMeters,
            Vec3 centroid,
            bool isClosed,
            int openEdgeCount,
            int triangleCount)
        {
            VolumeCubicMeters = volumeCubicMeters;
            SignedVolumeCubicMeters = signedVolumeCubicMeters;
            Centroid = centroid;
            IsClosed = isClosed;
            IsInsideOut = signedVolumeCubicMeters < -1e-12;
            OpenEdgeCount = openEdgeCount;
            TriangleCount = triangleCount;
        }
    }

    /// <summary>
    /// Computes the enclosed volume (m^3), volume-weighted centroid, and watertightness
    /// of a triangulated surface mesh, applying per-axis scale.
    /// </summary>
    public static MeshVolumeResult Compute(
        ReadOnlySpan<Vec3> vertices,
        ReadOnlySpan<int> triangles,
        Vec3 scale)
    {
        if (vertices.Length == 0 || triangles.Length < 3)
        {
            return new MeshVolumeResult(0.0, 0.0, new Vec3(0, 0, 0), isClosed: false, openEdgeCount: 0, triangleCount: 0);
        }

        double sx = Math.Abs(scale.X) > 1e-6 ? scale.X : 1.0;
        double sy = Math.Abs(scale.Y) > 1e-6 ? scale.Y : 1.0;
        double sz = Math.Abs(scale.Z) > 1e-6 ? scale.Z : 1.0;

        // Weld vertices on a 0.1 mm grid so flat-shaded per-face duplicated vertices share edges.
        const double weldScale = 10000.0; // 0.1 mm resolution
        var weldMap = new Dictionary<(long, long, long), int>(vertices.Length);
        var weldedIndex = new int[vertices.Length];
        var scaledVerts = new Vec3[vertices.Length];

        double sumX = 0, sumY = 0, sumZ = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            double x = vertices[i].X * sx;
            double y = vertices[i].Y * sy;
            double z = vertices[i].Z * sz;
            scaledVerts[i] = new Vec3(x, y, z);
            sumX += x;
            sumY += y;
            sumZ += z;

            var key = (
                (long)Math.Round(x * weldScale),
                (long)Math.Round(y * weldScale),
                (long)Math.Round(z * weldScale));
            if (!weldMap.TryGetValue(key, out int wid))
            {
                wid = weldMap.Count;
                weldMap[key] = wid;
            }
            weldedIndex[i] = wid;
        }

        var edgeCounts = new Dictionary<(int, int), int>(triangles.Length);
        double signedVol6 = 0.0;
        double cxAccum = 0.0, cyAccum = 0.0, czAccum = 0.0;
        int validTris = 0;

        int triLen = (triangles.Length / 3) * 3;
        for (int i = 0; i < triLen; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];
            if ((uint)i0 >= (uint)vertices.Length ||
                (uint)i1 >= (uint)vertices.Length ||
                (uint)i2 >= (uint)vertices.Length)
            {
                continue;
            }

            int w0 = weldedIndex[i0];
            int w1 = weldedIndex[i1];
            int w2 = weldedIndex[i2];
            if (w0 == w1 || w1 == w2 || w2 == w0)
            {
                continue; // Degenerate triangle
            }

            validTris++;
            CountEdge(edgeCounts, w0, w1);
            CountEdge(edgeCounts, w1, w2);
            CountEdge(edgeCounts, w2, w0);

            Vec3 a = scaledVerts[i0];
            Vec3 b = scaledVerts[i1];
            Vec3 c = scaledVerts[i2];

            // 6 * signed tetrahedron volume = a . (b x c)
            double crossX = b.Y * c.Z - b.Z * c.Y;
            double crossY = b.Z * c.X - b.X * c.Z;
            double crossZ = b.X * c.Y - b.Y * c.X;
            double v6 = a.X * crossX + a.Y * crossY + a.Z * crossZ;

            signedVol6 += v6;
            cxAccum += v6 * (a.X + b.X + c.X);
            cyAccum += v6 * (a.Y + b.Y + c.Y);
            czAccum += v6 * (a.Z + b.Z + c.Z);
        }

        int openEdges = 0;
        foreach (var kv in edgeCounts)
        {
            if (kv.Value % 2 != 0)
            {
                openEdges++;
            }
        }

        bool isClosed = validTris >= 4 && openEdges == 0;
        double signedVol = signedVol6 / 6.0;
        double volume = Math.Abs(signedVol);

        Vec3 centroid;
        if (Math.Abs(signedVol6) > 1e-12)
        {
            double inv = 1.0 / (4.0 * signedVol6);
            centroid = new Vec3(cxAccum * inv, cyAccum * inv, czAccum * inv);
        }
        else
        {
            double invN = 1.0 / vertices.Length;
            centroid = new Vec3(sumX * invN, sumY * invN, sumZ * invN);
        }

        if (!double.IsFinite(volume) || volume < 0.0) { volume = 0.0; signedVol = 0.0; }
        if (!double.IsFinite(centroid.X) || !double.IsFinite(centroid.Y) || !double.IsFinite(centroid.Z))
        {
            centroid = new Vec3(0, 0, 0);
        }

        return new MeshVolumeResult(volume, signedVol, centroid, isClosed, openEdges, validTris);
    }

    public static float ComputeCapacityLitres(double volumeCubicMeters, float fillEfficiency)
    {
        if (volumeCubicMeters <= 0.0 || fillEfficiency <= 0f) return 0f;
        double litres = volumeCubicMeters * LitresPerCubicMeter * fillEfficiency;
        if (!double.IsFinite(litres) || litres < 0.0) return 0f;
        return (float)litres;
    }

    public static float ComputeFuelMassKg(float capacityLitres, float densityKgPerLitre = VanillaDieselDensityKgPerLitre)
    {
        if (capacityLitres <= 0f || densityKgPerLitre <= 0f) return 0f;
        return capacityLitres * densityKgPerLitre;
    }

    public static float ComputeFuelCost(float fuelMassKg)
    {
        if (fuelMassKg <= 0f) return 0f;
        return fuelMassKg * VanillaFuelCostPerKg;
    }

    private static void CountEdge(Dictionary<(int, int), int> counts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        counts.TryGetValue(key, out int c);
        counts[key] = c + 1;
    }
}
