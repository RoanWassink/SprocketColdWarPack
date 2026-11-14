using UnityEngine;
namespace SprocketTelescopicMast;

// Procedural hinge meshes (metres, scale 1). Pin along local X.
// Base origin: mounting surface directly below the pin. Leaf origin: on the pin axis.
internal static class HingeMesh
{
    private sealed class Builder
    {
        internal readonly List<Vector3> V = new(), N = new();
        internal readonly List<int> T = new();
        internal readonly List<Vector2> UV = new();
        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int s = V.Count; V.AddRange(new[] { a, b, c, d }); N.AddRange(new[] { n, n, n, n });
            UV.AddRange(new[] { NativeUv.Plane(a,n), NativeUv.Plane(b,n), NativeUv.Plane(c,n), NativeUv.Plane(d,n) });
            T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
        internal void Box(Vector3 min, Vector3 max)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.up);
            Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), Vector3.down);
            Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), Vector3.forward);
            Quad(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0), Vector3.back);
            Quad(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), Vector3.right);
            Quad(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), Vector3.left);
        }
        /// <summary>Cylinder (or tube when inner &gt; 0) along X, centred at (0, cy, cz).</summary>
        internal void CylinderX(float outer, float inner, float x0, float x1, float cy, float cz, int segments = 48)
        {
            Vector3 P(float r, float x, float a) => new(x, cy + r * MathF.Cos(a), cz + r * MathF.Sin(a));
            Vector3 Radial(float a) => new(0, MathF.Cos(a), MathF.Sin(a));
            for (int i = 0; i < segments; i++)
            {
                float a = i * MathF.PI * 2 / segments, b = (i + 1) * MathF.PI * 2 / segments;
                int s = V.Count;
                V.AddRange(new[] { P(outer, x0, a), P(outer, x0, b), P(outer, x1, b), P(outer, x1, a) });
                N.AddRange(new[] { Radial(a), Radial(b), Radial(b), Radial(a) });
                // Planar per facet, with chart translations joined along the band.
                // Chord length is the exact width of this flat face, not a normalized chart.
                float outerStep=2*outer*MathF.Sin(MathF.PI/segments)*NativeUv.PerMetre;
                UV.AddRange(new[] { new Vector2(x0*NativeUv.PerMetre,i*outerStep), new Vector2(x0*NativeUv.PerMetre,(i+1)*outerStep), new Vector2(x1*NativeUv.PerMetre,(i+1)*outerStep), new Vector2(x1*NativeUv.PerMetre,i*outerStep) });
                T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                if (inner > 0)
                {
                    s = V.Count;
                    V.AddRange(new[] { P(inner, x0, a), P(inner, x1, a), P(inner, x1, b), P(inner, x0, b) });
                    N.AddRange(new[] { -Radial(a), -Radial(a), -Radial(b), -Radial(b) });
                    float innerStep=2*inner*MathF.Sin(MathF.PI/segments)*NativeUv.PerMetre;
                    UV.AddRange(new[] { new Vector2(x0*NativeUv.PerMetre,i*innerStep), new Vector2(x1*NativeUv.PerMetre,i*innerStep), new Vector2(x1*NativeUv.PerMetre,(i+1)*innerStep), new Vector2(x0*NativeUv.PerMetre,(i+1)*innerStep) });
                    T.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    Quad(P(inner, x0, a), P(inner, x0, b), P(outer, x0, b), P(outer, x0, a), Vector3.left);
                    Quad(P(inner, x1, a), P(outer, x1, a), P(outer, x1, b), P(inner, x1, b), Vector3.right);
                }
                else
                {
                    s = V.Count;
                    V.AddRange(new[] { new Vector3(x0, cy, cz), P(outer, x0, b), P(outer, x0, a) });
                    N.AddRange(new[] { Vector3.left, Vector3.left, Vector3.left });
                    UV.AddRange(new[] { NativeUv.Plane(V[s],Vector3.left), NativeUv.Plane(V[s+1],Vector3.left), NativeUv.Plane(V[s+2],Vector3.left) });
                    T.AddRange(new[] { s, s + 1, s + 2 });
                    s = V.Count;
                    V.AddRange(new[] { new Vector3(x1, cy, cz), P(outer, x1, a), P(outer, x1, b) });
                    N.AddRange(new[] { Vector3.right, Vector3.right, Vector3.right });
                    UV.AddRange(new[] { NativeUv.Plane(V[s],Vector3.right), NativeUv.Plane(V[s+1],Vector3.right), NativeUv.Plane(V[s+2],Vector3.right) });
                    T.AddRange(new[] { s, s + 1, s + 2 });
                }
            }
        }
        internal Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = V.ToArray(); mesh.triangles = T.ToArray(); mesh.normals = N.ToArray();
            if (name != "Hinge mount")
                mesh.uv = UV.ToArray();
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
    }

    internal static Mesh CreateBase()
    {
        var b = new Builder();
        const float h = HingeMath.PinHeight, half = HingeMath.PinLength / 2, lugInner = HingeMath.LeafWidth / 2;
        // Strap plate behind the pin, on the mounting surface.
        b.Box(new Vector3(-half, 0, -.03f - HingeMath.StrapDepth), new Vector3(half, HingeMath.StrapThickness, -.03f));
        // End lugs carrying the pin.
        b.Box(new Vector3(-half, 0, -.03f), new Vector3(-lugInner, HingeMath.LugHeight, .025f));
        b.Box(new Vector3(lugInner, 0, -.03f), new Vector3(half, HingeMath.LugHeight, .025f));
        // Pin, slightly proud of the lugs so it reads as a pin.
        b.CylinderX(HingeMath.PinRadius, 0, -half - .006f, half + .006f, h, 0);
        return b.Build("Hinge base");
    }

    internal static Mesh CreateLeaf()
    {
        var b = new Builder();
        const float r = HingeMath.KnuckleRadius, w = HingeMath.LeafWidth / 2;
        // Knuckle tube around the pin (origin on the pin axis).
        b.CylinderX(r, HingeMath.PinRadius, -w, w, 0, 0);
        // Plate: bottom flush with the knuckle bottom, so the closed leaf lies on the surface.
        b.Box(new Vector3(-w, -r, r * .6f), new Vector3(w, -r + HingeMath.LeafThickness, HingeMath.LeafDepth));
        return b.Build("Hinge leaf");
    }

    /// <summary>Hinge Mount: 0.10 x 0.10 m plate, 10 mm thick, on a 20 mm pin; origin on the pin axis.</summary>
    internal static Mesh CreateMount()
    {
        var b = new Builder();
        const float r = HingeMath.MountPinRadius, w = HingeMath.MountWidth / 2;
        b.CylinderX(r, 0, -w, w, 0, 0, 24);
        // Plate: bottom flush with the pin bottom, top through the pin axis (closed pose lies flat).
        b.Box(new Vector3(-w, -r, r), new Vector3(w, -r + HingeMath.MountThickness, HingeMath.MountDepth));
        return b.Build("Hinge mount");
    }
}
