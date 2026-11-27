using System.Globalization;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketThermalSight;

internal static class ConsoleAssets
{
    private static Mesh? mesh;
    private static Mesh? displayMesh;
    private static Mesh? commanderSightMesh;
    private static Mesh? oesOpSightMesh;
    private static VehicleMaterial? glass;
    private static VehicleMaterial? housing;
    private static float scanAt;
    private static bool failed;
    private static int appliedLogs;
    private static string Asset(string file) => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "assets", file);
    private static bool IsHandled(VehicleComponent c) => ConsoleLinks.IsAnyConsole(c) || ConsoleLinks.IsCommanderSightModel(c) || ConsoleLinks.IsOesOpModel(c);

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Initiate))]
    private static void Initiating(VehicleObjectModel __instance)
    {
        if (ConsoleLinks.IsAnyConsole(__instance))
        {
            // Create the native interaction collider on the internal crew layer,
            // instead of the generic exterior-equipment selection layer.
            __instance.partType = VehiclePartType.Crew;
            __instance.staticRenderer = false;
            __instance.rendererFlags |= VehicleRendererFlags.ApplyMainOverlay;
        }
        else if (ConsoleLinks.IsCommanderSightModel(__instance) || ConsoleLinks.IsOesOpModel(__instance))
        {
            __instance.staticRenderer = false;
            __instance.rendererFlags |= VehicleRendererFlags.ApplyMainOverlay;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void Building(VehicleObjectModel __instance)
    { if (IsHandled(__instance)) __instance.staticRenderer = false; }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    { try { if (IsHandled(__instance)) Apply(__instance); } catch (Exception ex) { Runtime.Warn("FCS model load: " + ex.Message); } }

    private static Mesh LoadTwoSurfaceObj(string file, string meshName, int expectedPositions, int expectedVertices, string logLine)
    {
        var positions = new List<Vector3>(); var texcoords = new List<Vector2>(); var sourceNormals = new List<Vector3>();
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var normals = new List<Vector3>();
        var faces = new[] { new List<int>(), new List<int>() }; int material = 0;
        float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);
        foreach (var line in File.ReadLines(Asset(file)))
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (f.Length == 0) continue;
            if (f[0] == "v" && f.Length == 4) positions.Add(new Vector3(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "vt" && f.Length >= 3) texcoords.Add(new Vector2(F(f[1]), F(f[2])));
            else if (f[0] == "vn" && f.Length == 4) sourceNormals.Add(new Vector3(F(f[1]), F(f[2]), F(f[3])));
            else if (f[0] == "usemtl" && f.Length == 2) material = f[1] switch { "Housing" => 0, "Glass" => 1, _ => throw new InvalidDataException("Unknown console surface: " + f[1]) };
            else if (f[0] == "f")
            {
                if (f.Length != 4) throw new InvalidDataException(file + " must be triangulated");
                for (int i = 1; i < 4; i++)
                {
                    var p = f[i].Split('/'); if (p.Length != 3) throw new InvalidDataException(file + " UV/normal tuple missing");
                    int a = int.Parse(p[0], CultureInfo.InvariantCulture) - 1, b = int.Parse(p[1], CultureInfo.InvariantCulture) - 1, c = int.Parse(p[2], CultureInfo.InvariantCulture) - 1;
                    if (a < 0 || a >= positions.Count || b < 0 || b >= texcoords.Count || c < 0 || c >= sourceNormals.Count) throw new InvalidDataException(file + " tuple out of bounds");
                    faces[material].Add(vertices.Count); vertices.Add(positions[a]); uv.Add(texcoords[b]); normals.Add(sourceNormals[c]);
                }
            }
        }
        if (positions.Count != expectedPositions || vertices.Count != expectedVertices || faces[0].Count == 0 || vertices.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)))
            throw new InvalidDataException($"Unexpected {file} geometry: positions={positions.Count}, vertices={vertices.Count}");
        var built = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
        built.vertices = vertices.ToArray(); built.uv = uv.ToArray(); built.normals = normals.ToArray(); built.subMeshCount = 2;
        for (int i = 0; i < 2; i++) built.SetTriangles(faces[i].ToArray(), i);
        built.RecalculateBounds();
        Plugin.Instance.Log.LogInfo(logLine);
        return built;
    }

    private static void EnsureSharedMaterials()
    {
        if (glass != null) return;
        var shader = Shader.Find("HDRP/Lit"); if (shader == null) throw new InvalidOperationException("FCS shader unavailable");
        var surface = new Material(shader) { name = "FCS eyepiece glass", hideFlags = HideFlags.HideAndDontSave };
        surface.SetColor("_BaseColor", new Color(.035f, .065f, .075f, 1)); surface.SetFloat("_Smoothness", .88f);
        // Render the optical surface from either side.
        surface.SetFloat("_DoubleSidedEnable", 1); surface.SetFloat("_CullMode", 0); surface.SetFloat("_CullModeForward", 0); HDMaterial.ValidateMaterial(surface);
        glass = new VehicleMaterial(surface) { Flags = VehicleMaterialFlags.Unpainted | VehicleMaterialFlags.DisableGrime | VehicleMaterialFlags.CustomShader };
    }

    private static Mesh EnsureMesh(VehicleObjectModel model)
    {
        EnsureSharedMaterials();
        if (ConsoleLinks.IsOesOpModel(model))
            return oesOpSightMesh ??= LoadTwoSurfaceObj("oes-op-sight.obj", "OES-OP targeting sight", 5456, 33042,
                "[OES-OP sight] Targeting sensor model loaded: 11014 triangles; metric housing UVs (1 UV/m); exact recessed glass verified.");
        if (ConsoleLinks.IsCommanderSightModel(model))
            return commanderSightMesh ??= LoadTwoSurfaceObj("commander-sight.obj", "Commander panoramic sight", 2164, 12948,
                "[Commander sight] Panoramic sight model loaded: 4316 triangles; metric housing UVs (1 UV/m); OpenSCAD export verified.");
        if (ConsoleLinks.IsDisplayModel(model))
            return displayMesh ??= LoadTwoSurfaceObj("driver-console.obj", "Display console", 1240, 7284,
                "[Console] Display console model loaded: 2428 triangles; metric housing UVs (1 UV/m); OpenSCAD export verified.");
        return mesh ??= LoadTwoSurfaceObj("fcs-console.obj", "Gunner FCS console", 1623, 7476,
            "[FCS] User model loaded: 2492 triangles; metric housing UVs (1 UV/m, since 0.4.1); original normals and origin retained.");
    }

    private static void Apply(VehicleObjectModel model)
    {
        var native = model.Model; if (native == null) return;
        var targetMesh = EnsureMesh(model);
        model.MarkDynamic();
        if (native.InteractionMesh != targetMesh)
        { native.SetMesh(targetMesh); native.SetShadowProxy(targetMesh); native.SetInteractionMesh(targetMesh); native.SetCollisionMesh(targetMesh); }
        var materials = native.Materials; var paint = materials != null && materials.Length > 0 ? materials[0] : native.Material;
        if (housing == null)
        {
            var shader = Shader.Find("HDRP/Lit"); if (shader == null) throw new InvalidOperationException("FCS housing shader unavailable");
            var surface = new Material(shader) { name = "FCS clean housing", hideFlags = HideFlags.HideAndDontSave };
            surface.SetColor("_BaseColor", new Color(.24f, .27f, .18f, 1));
            surface.SetFloat("_Smoothness", .25f); surface.SetFloat("_Metallic", 0);
            HDMaterial.SetSurfaceType(surface, false);
            surface.SetFloat("_DoubleSidedEnable", 1); surface.SetFloat("_DoubleSidedNormalMode", 1);
            surface.SetFloat("_CullMode", 0); surface.SetFloat("_CullModeForward", 0); HDMaterial.ValidateMaterial(surface);
            housing = new VehicleMaterial(surface) { Flags = VehicleMaterialFlags.Unpainted | VehicleMaterialFlags.DisableGrime | VehicleMaterialFlags.CustomShader };
        }
        paint = ExteriorPaint.Get(model, housing);
        if (materials == null || materials.Length != 2 || materials[0]?.Pointer != paint!.Pointer || materials[1]?.Pointer != glass!.Pointer)
        {
            native.SetMaterials(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleMaterial>(new[] { paint!, glass! }));
            ExteriorPaint.Process(model, paint!);
        }
        if (appliedLogs < 8) { appliedLogs++; Plugin.Instance.Log.LogInfo($"[Console] Native model applied ({model.ComponentID}); selection={model.PartType}; layer={model.InteractionCollider?.Layer}; meshMatch={native.InteractionMesh == targetMesh}"); }
    }

    internal static void Cleanup()
    {
        if (mesh != null) UnityEngine.Object.Destroy(mesh); mesh = null;
        if (displayMesh != null) UnityEngine.Object.Destroy(displayMesh); displayMesh = null;
        if (commanderSightMesh != null) UnityEngine.Object.Destroy(commanderSightMesh); commanderSightMesh = null;
        if (oesOpSightMesh != null) UnityEngine.Object.Destroy(oesOpSightMesh); oesOpSightMesh = null;
        if (glass?.Material != null) UnityEngine.Object.Destroy(glass.Material); glass = null;
        if (housing?.Material != null) UnityEngine.Object.Destroy(housing.Material); housing = null;
        failed = false; scanAt = 0; appliedLogs = 0;
    }

    internal static void Tick()
    {
        if (!Runtime.HasVehicleContext || failed || Time.unscaledTime < scanAt) return; scanAt = Time.unscaledTime + .5f;
        try
        {
            foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if (vehicle == null || vehicle.Released) continue;
                var list = vehicle.ObjectReader.Items; int n = list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
                for (int i = 0; i < n; i++)
                {
                    var components = list[i]?.Components; if (components == null) continue;
                    int m = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                    for (int j = 0; j < m; j++)
                    { var model = components[j]?.TryCast<VehicleObjectModel>(); if (model != null && IsHandled(model)) Apply(model); }
                }
            }
        }
        catch (Exception ex) { failed = true; Runtime.Warn("FCS visual unavailable: " + ex.Message); }
    }
}
