using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using NQuaternion = System.Numerics.Quaternion;
using Object = UnityEngine.Object;

namespace SprocketOscillatingTurret;

// Render-only meshes of the Heavy Trunnion Mount. Static frame on the mantlet transform, moving
// trunnion block on the trunnion transform (the one LayingDriveBehaviour turns), pinion under the
// static frame. No colliders, bodies or mass: the native trunnions stay the placement surface and
// only their renderer is hidden (HeavyTrunnion.HideModel).
internal static class HeavyTrunnionVisuals
{
    private sealed class Visual
    {
        public IntPtr Mantlet;
        public IntPtr FramePointer;
        public IntPtr TrunnionPointer;
        public Transform Frame = null!;
        public Transform Trunnion = null!;
        public GameObject Static = null!;
        public GameObject Moving = null!;
        public Transform Pinion = null!;
        public float LastAngle = float.NaN;
    }

    // Pinion axis in the static mesh frame: OpenSCAD (y -131, z -110) mm -> Unity (x, z, y) m.
    private static readonly Vector3 PinionAxis = new(0f, -0.110f, -0.131f);
    private static readonly Dictionary<IntPtr, Visual> ByMantlet = new();
    private static readonly List<Visual> Active = new();
    private static readonly HashSet<string> Warnings = new();
    private static Mesh? staticMesh, movingMesh, pinionMesh;
    private static Material? cast, metal;

    internal static int Count => Active.Count;

    private static Material Paint(string name, Color colour, float smoothness, float metallic)
    {
        var material = new Material(Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit unavailable"))
        { name = name, hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_BaseColor", colour);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        HDMaterial.ValidateMaterial(material);
        return material;
    }

    private static Mesh Build(string name, float[] vertices, float[] normals, int[][] triangles)
    {
        var points = new Vector3[vertices.Length / 3];
        var directions = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3(vertices[3 * i], vertices[3 * i + 1], vertices[3 * i + 2]);
            directions[i] = new Vector3(normals[3 * i], normals[3 * i + 1], normals[3 * i + 2]);
        }

        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = points;
        mesh.normals = directions;
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles((Il2CppStructArray<int>)triangles[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Assets()
    {
        if (cast == null) cast = Paint("Heavy trunnion cast steel", new Color(.105f, .115f, .11f, 1f), .32f, .15f);
        if (metal == null) metal = Paint("Heavy trunnion machined steel", new Color(.52f, .54f, .55f, 1f), .62f, .85f);
        if (staticMesh == null)
            staticMesh = Build("Heavy trunnion frame", HeavyTrunnionMeshData.StaticVertices,
                HeavyTrunnionMeshData.StaticNormals, HeavyTrunnionMeshData.StaticTriangles);
        if (movingMesh == null)
            movingMesh = Build("Heavy trunnion block", HeavyTrunnionMeshData.MovingVertices,
                HeavyTrunnionMeshData.MovingNormals, HeavyTrunnionMeshData.MovingTriangles);
        if (pinionMesh == null)
            pinionMesh = Build("Heavy trunnion pinion", HeavyTrunnionMeshData.PinionVertices,
                HeavyTrunnionMeshData.PinionNormals, HeavyTrunnionMeshData.PinionTriangles);
    }

    // Materials follow the per-material STL files: cast -> dark, steel and bronze -> metal.
    private static Il2CppReferenceArray<Material> MaterialsFor(string[] names)
    {
        var result = new Material[names.Length];
        for (int i = 0; i < names.Length; i++) result[i] = names[i].EndsWith("-cast", StringComparison.Ordinal) ? cast! : metal!;
        return new Il2CppReferenceArray<Material>(result);
    }

    private static GameObject Part(Transform parent, string name, Mesh mesh, string[] materials, int layer)
    {
        var root = new GameObject(name) { layer = layer };
        root.transform.SetParent(parent, false);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = MaterialsFor(materials);
        return root; // No collider, rigid body, crew task or mass.
    }

    /// <summary>Hangs (or refreshes) the three meshes on a heavy mantlet. Returns a short state for the log.</summary>
    internal static string Attach(Mantlet mantlet, int layer)
    {
        try
        {
            var frame = mantlet.VehicleTransform?.transform;
            var trunnion = mantlet.trunnionRotationArea?.transform;
            if (frame == null || trunnion == null) return "waiting(frame=" + (frame != null) + ",trunnion=" + (trunnion != null) + ")";

            IntPtr key = mantlet.Pointer;
            if (!ByMantlet.TryGetValue(key, out var v) || v.FramePointer != frame.Pointer ||
                v.TrunnionPointer != trunnion.Pointer || v.Static == null || v.Moving == null || v.Pinion == null)
            {
                Release(key);
                Assets();
                var staticPart = Part(frame, "Heavy trunnion frame", staticMesh!, HeavyTrunnionMeshData.StaticMaterials, layer);
                var movingPart = Part(trunnion, "Heavy trunnion block", movingMesh!, HeavyTrunnionMeshData.MovingMaterials, layer);
                var pinionPart = Part(staticPart.transform, "Heavy trunnion pinion", pinionMesh!, HeavyTrunnionMeshData.PinionMaterials, layer);
                pinionPart.transform.localPosition = PinionAxis;
                v = new Visual
                {
                    Mantlet = key, FramePointer = frame.Pointer, TrunnionPointer = trunnion.Pointer,
                    Frame = frame, Trunnion = trunnion, Static = staticPart, Moving = movingPart, Pinion = pinionPart.transform
                };
                ByMantlet[key] = v;
                Active.Add(v);
            }

            // Mantlet.Build gives the trunnion transform a position only; part scale x trunnionsScale
            // goes to the native model. Apply the same here, net of any Unity scale already inherited.
            Vector3 part = mantlet.VehicleTransform!.Scale;
            float k = HeavyTrunnionPolicy.MeshScale(mantlet.TrunnionsScale);
            var wanted = new Vector3(part.x * k, part.y * k, part.z * k);
            v.Static.transform.localScale = Divide(wanted, frame.lossyScale);
            v.Moving.transform.localScale = Divide(wanted, trunnion.lossyScale);
            v.Static.SetActive(true);
            v.Moving.SetActive(true);
            v.LastAngle = float.NaN; // force one pinion update
            return FormattableString.Invariant($"attached(static,moving,pinion) meshScale=({wanted.x:F3},{wanted.y:F3},{wanted.z:F3})");
        }
        catch (Exception ex)
        {
            if (Warnings.Add(ex.Message)) Plugin.Warn("[Heavy trunnion] mesh attach: " + ex.Message);
            return "failed(" + ex.GetType().Name + ")";
        }
    }

    private static Vector3 Divide(Vector3 wanted, Vector3 inherited) => new(
        wanted.x / Safe(inherited.x), wanted.y / Safe(inherited.y), wanted.z / Safe(inherited.z));

    private static float Safe(float value)
    {
        float a = Math.Abs(value);
        return a < 1e-4f ? 1f : a;
    }

    /// <summary>
    /// Before rendering: turn each pinion by -3.75 x the trunnion angle relative to the static frame.
    /// Writes only when that angle changed; managed maths, no allocations.
    /// </summary>
    internal static void Frame()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var v = Active[i];
            try
            {
                if (v.Frame == null || v.Trunnion == null || v.Pinion == null)
                {
                    // A transform went away without a component release (rebuild or scene change):
                    // drop the leftovers; the next Mantlet.Build attaches fresh meshes.
                    Release(v.Mantlet);
                    continue;
                }
                var f = v.Frame.rotation;
                var t = v.Trunnion.rotation;
                NQuaternion relative = NQuaternion.Inverse(new NQuaternion(f.x, f.y, f.z, f.w)) * new NQuaternion(t.x, t.y, t.z, t.w);
                float angle = HeavyTrunnionPolicy.AngleAboutX(relative.X, relative.W);
                if (!HeavyTrunnionPolicy.AngleChanged(v.LastAngle, angle)) continue;
                v.LastAngle = angle;
                var (x, w) = HeavyTrunnionPolicy.AboutX(HeavyTrunnionPolicy.PinionAngle(angle));
                v.Pinion.localRotation = new Quaternion(x, 0f, 0f, w);
            }
            catch (Exception ex)
            {
                Active.RemoveAt(i);
                ByMantlet.Remove(v.Mantlet);
                if (Warnings.Add("frame:" + ex.Message)) Plugin.Warn("[Heavy trunnion] pinion update stopped: " + ex.Message);
            }
        }
    }

    internal static bool Has(IntPtr mantlet) => ByMantlet.ContainsKey(mantlet);

    internal static void Release(IntPtr mantlet)
    {
        if (!ByMantlet.Remove(mantlet, out var v)) return;
        Active.Remove(v);
        try
        {
            if (v.Static != null) { v.Static.SetActive(false); Object.Destroy(v.Static); }
            if (v.Moving != null) { v.Moving.SetActive(false); Object.Destroy(v.Moving); }
        }
        catch { }
    }

    internal static void Clear()
    {
        foreach (var key in ByMantlet.Keys.ToArray()) Release(key);
        if (staticMesh != null) Object.Destroy(staticMesh);
        if (movingMesh != null) Object.Destroy(movingMesh);
        if (pinionMesh != null) Object.Destroy(pinionMesh);
        if (cast != null) Object.Destroy(cast);
        if (metal != null) Object.Destroy(metal);
        staticMesh = movingMesh = pinionMesh = null;
        cast = metal = null;
        Warnings.Clear();
    }
}
