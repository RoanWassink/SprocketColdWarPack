using System.Globalization;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketThermalSight;

internal static class IrSearchlight
{
    internal const string Id = "irSearchlight";
    internal const string Guid = "bc3bd5c7-d1a6-4e89-b4e7-175ef19f9842";
    private static Mesh? mesh;
    private static VehicleMaterial? lens;
    private static bool failed;
    private static float scanAt;
    internal static bool Own(VehicleComponent? c) => c?.ComponentID == Id && c.TryCast<VehicleObjectModel>() != null;

    internal static bool TryPose(VehicleComponent owner, out Vector3 origin, out Vector3 forward)
    {
        origin = forward = Vector3.zero;
        foreach (var c in ConsoleLinks.Components(owner))
        {
            if (!Own(c) || !c.IsInstalled || c.VehicleObject.Released || !float.IsFinite(c.HealthFraction)
                || c.HealthFraction <= 0 || !ThermalEraAccess.Allowed(c)) continue;
            // Original asset: +Z forward, lens centre 0.25 m above the mounting foot.
            var position = c.VehicleTransform.LocalToWorldSpace(new Vector3(0, .25f, .128f));
            var direction = c.VehicleTransform.LocalToWorldSpace(new Vector3(0, .25f, 1.128f)) - position;
            if (direction.sqrMagnitude < .000001f) continue;
            origin = position; forward = direction.normalized; return true;
        }
        return false;
    }

    private static void EnsureAssets()
    {
        if (mesh != null) return;
        var positions = new List<Vector3>(); var texcoords = new List<Vector2>(); var sourceNormals = new List<Vector3>();
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var normals = new List<Vector3>();
        var corners = new Dictionary<(int V, int Vt, int Vn), int>();
        var faces = new[] { new List<int>(), new List<int>() }; int surface = 0; bool hasUv = true, hasNormals = true;
        float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);
        var path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "assets", "ir-searchlight.obj");
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 0) continue;
            if (f[0] == "v" && f.Length == 4) positions.Add(new Vector3(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "vn" && f.Length == 4) sourceNormals.Add(new Vector3(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "vt" && f.Length >= 3) texcoords.Add(new Vector2(F(f[1]), F(f[2])));
            else if (f.Length == 2 && f[0] is "o" or "g" or "usemtl")
                surface = f[1].Equals("lens", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            else if (f[0] == "f")
            {
                if (f.Length != 4) throw new InvalidDataException("IR searchlight must be triangulated.");
                for (int i = 1; i < 4; i++)
                {
                    var tuple = f[i].Split('/');
                    int vi = int.Parse(tuple[0], CultureInfo.InvariantCulture) - 1;
                    int ti = tuple.Length > 1 && tuple[1].Length > 0 ? int.Parse(tuple[1], CultureInfo.InvariantCulture) - 1 : -1;
                    int ni = tuple.Length > 2 && tuple[2].Length > 0 ? int.Parse(tuple[2], CultureInfo.InvariantCulture) - 1 : -1;
                    if (vi < 0 || vi >= positions.Count) throw new InvalidDataException("IR searchlight vertex out of range.");
                    if (ti < -1 || ti >= texcoords.Count) throw new InvalidDataException("IR UV out of range.");
                    if (ni < -1 || ni >= sourceNormals.Count) throw new InvalidDataException("IR normal out of range.");
                    if (!corners.TryGetValue((vi, ti, ni), out int vertex))
                    {
                        // One vertex per unique v/vt/vn combination preserves hard edges and UV seams.
                        vertex = vertices.Count; corners.Add((vi, ti, ni), vertex);
                        vertices.Add(positions[vi]);
                        if (ti >= 0) uv.Add(texcoords[ti]); else hasUv = false;
                        if (ni >= 0) normals.Add(sourceNormals[ni]); else hasNormals = false;
                    }
                    faces[surface].Add(vertex);
                }
            }
        }
        if (vertices.Count < 3 || vertices.Count > 60000 || faces[0].Count == 0 || faces[1].Count == 0
            || faces.Any(f => f.Count % 3 != 0)
            || vertices.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z))
            || (hasUv && uv.Count != vertices.Count) || (hasNormals && normals.Count != vertices.Count))
            throw new InvalidDataException("Invalid IR searchlight geometry.");
        var filter = LensAssets.IrFilterGlass() ?? throw new InvalidOperationException("IR searchlight glass unavailable.");
        lens = new VehicleMaterial(filter) { Flags = VehicleMaterialFlags.Unpainted | VehicleMaterialFlags.DisableGrime | VehicleMaterialFlags.CustomShader };
        mesh = new Mesh { name = "IR searchlight", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices.ToArray(); mesh.subMeshCount = 2;
        for (int i = 0; i < 2; i++) mesh.SetTriangles(faces[i].ToArray(), i);
        if (hasUv) mesh.uv = uv.ToArray();
        if (hasNormals) mesh.normals = normals.ToArray(); else mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Plugin.Instance.Log.LogInfo($"[IR searchlight] Model ready: {vertices.Count} unique vertices, {faces[0].Count / 3 + faces[1].Count / 3} triangles, vn/vt retained; housing native paint, lens coated filter glass.");
    }

    private static void Apply(VehicleObjectModel model)
    {
        if (model.Model == null) return;
        EnsureAssets(); model.MarkDynamic(); var native = model.Model;
        if (native.InteractionMesh != mesh)
        { native.SetMesh(mesh!); native.SetShadowProxy(mesh!); native.SetInteractionMesh(mesh!); native.SetCollisionMesh(mesh!); }
        var current=native.Materials;
        var housing = ExteriorPaint.Get(model,current!=null && current.Length>0 ? current[0] : native.Material);
        if (housing == null) return;
        var materials = native.Materials;
        if (materials == null || materials.Length != 2 || materials[0]?.Pointer != housing.Pointer || materials[1]?.Pointer != lens!.Pointer)
        {
            native.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(new[] { housing, lens! }));
            ExteriorPaint.Process(model,housing);
        }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void Prepare(VehicleObjectModel __instance)
    {
        if (!Own(__instance)) return;
        __instance.staticRenderer = false; __instance.generateDamageColliders = true;
        __instance.autoApplyScale = true; __instance.autoFlipModel = true;
    }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void Resources(VehicleObjectModel __instance)
    {
        if (!Own(__instance)) return;
        try
        {
            __instance.SetMass(0, MassType.Armour); __instance.SetCost(0, MassType.Armour, CostType.Material);
            __instance.SetMass(80, MassType.Mechanisms); __instance.SetCost(0, MassType.Mechanisms, CostType.Material);
            __instance.SetCost(300, MassType.Mechanisms, CostType.Assembly);
            __instance.cachedMass = __instance.GetMass(MassType.Everything); __instance.RecalculateCenterOfMass();
        }
        catch (Exception ex) { Runtime.Warn("IR searchlight resources: " + ex.Message); }
    }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    { try { if (Own(__instance)) Apply(__instance); } catch (Exception ex) { Runtime.Warn("IR searchlight model: " + ex.Message); } }
    internal static void Tick()
    {
        if (!Runtime.HasVehicleContext || failed || Time.unscaledTime < scanAt) return;
        scanAt = Time.unscaledTime + .5f;
        try
        {
            foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if (vehicle == null || vehicle.Released) continue;
                var objects = vehicle.ObjectReader.Items;
                int count = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for (int i = 0; i < count; i++)
                {
                    var components = objects[i]?.Components; if (components == null) continue;
                    int n = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for (int j = 0; j < n; j++)
                        if (Own(components[j]) && components[j].TryCast<VehicleObjectModel>() is {} model) Apply(model);
                }
            }
        }
        catch (Exception ex) { failed = true; Runtime.Warn("IR searchlight visuals: " + ex.Message); }
    }
    internal static void Cleanup()
    {
        ExteriorPaint.Cleanup();
        if (mesh != null) UnityEngine.Object.Destroy(mesh); mesh = null;
        if (lens?.Material != null) UnityEngine.Object.Destroy(lens.Material); lens = null;
        failed = false; scanAt = 0;
    }
}
