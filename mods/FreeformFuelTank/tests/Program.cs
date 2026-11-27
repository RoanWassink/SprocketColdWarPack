using System;
using System.Collections.Generic;
using SprocketFreeformFuelTank;

int passed = 0;
int failed = 0;

void Assert(bool cond, string msg)
{
    if (cond) { passed++; Console.WriteLine($"  PASS: {msg}"); }
    else { failed++; Console.WriteLine($"  FAIL: {msg}"); }
}

void AssertNear(double actual, double expected, double tol, string msg)
{
    bool ok = Math.Abs(actual - expected) <= tol;
    Assert(ok, $"{msg} (actual={actual:F6}, expected={expected:F6}, tol={tol})");
}

// Helper: build a flat-shaded (duplicated vertices per face, like Sprocket frontMesh) box [x0..x1, y0..y1, z0..z1]
(FreeformMeshMath.Vec3[] verts, int[] tris) MakeFlatShadedBox(
    double x0, double x1, double y0, double y1, double z0, double z1,
    bool omitTopFace = false, bool flipWinding = false)
{
    var v = new List<FreeformMeshMath.Vec3>();
    var t = new List<int>();

    void AddQuad(FreeformMeshMath.Vec3 a, FreeformMeshMath.Vec3 b, FreeformMeshMath.Vec3 c, FreeformMeshMath.Vec3 d)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        if (!flipWinding)
        {
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }
        else
        {
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }
    }

    var p000 = new FreeformMeshMath.Vec3(x0, y0, z0);
    var p100 = new FreeformMeshMath.Vec3(x1, y0, z0);
    var p110 = new FreeformMeshMath.Vec3(x1, y1, z0);
    var p010 = new FreeformMeshMath.Vec3(x0, y1, z0);
    var p001 = new FreeformMeshMath.Vec3(x0, y0, z1);
    var p101 = new FreeformMeshMath.Vec3(x1, y0, z1);
    var p111 = new FreeformMeshMath.Vec3(x1, y1, z1);
    var p011 = new FreeformMeshMath.Vec3(x0, y1, z1);

    AddQuad(p000, p100, p101, p001); // bottom
    if (!omitTopFace) AddQuad(p010, p011, p111, p110); // top
    AddQuad(p000, p010, p110, p100); // front (-z)
    AddQuad(p001, p101, p111, p011); // back (+z)
    AddQuad(p000, p001, p011, p010); // left (-x)
    AddQuad(p100, p110, p111, p101); // right (+x)

    return (v.ToArray(), t.ToArray());
}

Console.WriteLine("=== FreeformMeshMath Tests ===");

// 1. Default 0.5 x 0.5 x 0.5 cube -> 0.125 m^3 = 125 L, centroid (0, 0.25, 0)
{
    var (v, t) = MakeFlatShadedBox(-0.25, 0.25, 0.0, 0.5, -0.25, 0.25);
    var res = FreeformMeshMath.Compute(v, t, new FreeformMeshMath.Vec3(1, 1, 1));
    Assert(res.IsClosed, "Default 0.5m flat-shaded cube is watertight after 0.1mm vertex weld");
    Assert(res.OpenEdgeCount == 0, "Default cube has 0 open edges");
    AssertNear(res.VolumeCubicMeters, 0.125, 1e-6, "Default cube volume = 0.125 m^3");
    AssertNear(res.Centroid.X, 0.0, 1e-6, "Default cube centroid X = 0");
    AssertNear(res.Centroid.Y, 0.25, 1e-6, "Default cube centroid Y = 0.25");
    AssertNear(res.Centroid.Z, 0.0, 1e-6, "Default cube centroid Z = 0");
    float cap = FreeformMeshMath.ComputeCapacityLitres(res.VolumeCubicMeters, 1.0f);
    float mass = FreeformMeshMath.ComputeFuelMassKg(cap);
    float cost = FreeformMeshMath.ComputeFuelCost(mass);
    AssertNear(cap, 125.0, 1e-3, "Default cube capacity = 125 L");
    AssertNear(mass, 104.0, 1e-3, "Default cube diesel mass = 104.0 kg");
    AssertNear(cost, 104.0 * 0.20249656, 1e-3, "Default cube cost matches vanilla formula");
}

// 2. Vanilla Box Fuel Tank parity (1.0 x 0.5 x 1.0 = 500 L) via non-uniform scale (2, 1, 2)
{
    var (v, t) = MakeFlatShadedBox(-0.25, 0.25, 0.0, 0.5, -0.25, 0.25);
    var res = FreeformMeshMath.Compute(v, t, new FreeformMeshMath.Vec3(2.0, 1.0, 2.0));
    Assert(res.IsClosed, "Scaled cube is closed");
    AssertNear(res.VolumeCubicMeters, 0.5, 1e-6, "Scaled 1.0x0.5x1.0 volume = 0.5 m^3");
    AssertNear(FreeformMeshMath.ComputeCapacityLitres(res.VolumeCubicMeters, 1.0f), 500.0, 1e-3, "Scaled capacity = 500 L");
}

// 3. Flipped winding still produces positive volume and correct centroid
{
    var (v, t) = MakeFlatShadedBox(1.0, 2.0, 2.0, 4.0, -1.0, 0.0, flipWinding: true);
    var res = FreeformMeshMath.Compute(v, t, new FreeformMeshMath.Vec3(1, 1, 1));
    Assert(res.IsClosed, "Flipped-winding box is closed");
    AssertNear(res.VolumeCubicMeters, 2.0, 1e-6, "Flipped-winding 1x2x1 box volume = 2.0 m^3");
    AssertNear(res.Centroid.X, 1.5, 1e-6, "Off-origin centroid X = 1.5");
    AssertNear(res.Centroid.Y, 3.0, 1e-6, "Off-origin centroid Y = 3.0");
    AssertNear(res.Centroid.Z, -0.5, 1e-6, "Off-origin centroid Z = -0.5");
}

// 4. Open mesh (top face deleted) detected as NOT closed
{
    var (v, t) = MakeFlatShadedBox(-0.25, 0.25, 0.0, 0.5, -0.25, 0.25, omitTopFace: true);
    var res = FreeformMeshMath.Compute(v, t, new FreeformMeshMath.Vec3(1, 1, 1));
    Assert(!res.IsClosed, "Open box (missing top face) is detected as open");
    Assert(res.OpenEdgeCount == 4, $"Open box has 4 boundary edges (got {res.OpenEdgeCount})");
}

// 5. Triangular prism / sloped wedge (sponson tank shape): base 1.0 x 0.5, height 0.6 -> V = 0.5 * 1.0 * 0.6 * 0.5 = 0.15 m^3 = 150 L
{
    var verts = new[]
    {
        new FreeformMeshMath.Vec3(0, 0, 0),
        new FreeformMeshMath.Vec3(1, 0, 0),
        new FreeformMeshMath.Vec3(0, 0.6, 0),
        new FreeformMeshMath.Vec3(0, 0, 0.5),
        new FreeformMeshMath.Vec3(1, 0, 0.5),
        new FreeformMeshMath.Vec3(0, 0.6, 0.5),
    };
    var tris = new[]
    {
        0, 2, 1,       // front tri
        3, 4, 5,       // back tri
        0, 1, 4, 0, 4, 3, // bottom
        0, 3, 5, 0, 5, 2, // vertical back
        1, 2, 5, 1, 5, 4  // sloped face
    };
    var res = FreeformMeshMath.Compute(verts, tris, new FreeformMeshMath.Vec3(1, 1, 1));
    Assert(res.IsClosed, "Sloped wedge prism is closed");
    AssertNear(res.VolumeCubicMeters, 0.15, 1e-6, "Sloped wedge volume = 0.15 m^3 (150 L)");
    AssertNear(res.Centroid.X, 1.0 / 3.0, 1e-6, "Sloped wedge centroid X = 1/3");
    AssertNear(res.Centroid.Y, 0.2, 1e-6, "Sloped wedge centroid Y = 0.2");
    AssertNear(res.Centroid.Z, 0.25, 1e-6, "Sloped wedge centroid Z = 0.25");
}

Console.WriteLine($"Result: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
