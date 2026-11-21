using System.Globalization;
using HarmonyLib;
using Sprocket;
using Sprocket.PlayerControl;
using Sprocket.PlateMesh;
using Sprocket.TransformOptimizations;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using Object = UnityEngine.Object;

namespace SprocketFloppyParts;

[HarmonyPatch]
internal static class FloppyOverlayRuntime
{
    private sealed record View(
        GameObject Root,
        Mesh Mesh,
        MeshRenderer Renderer,
        NVector[] CoverVertices,
        int CoverTriCount,
        int MinRequiredNativeTris,
        string GeometrySignature,
        string ConfigGeoSignature,
        string MaterialSignature,
        LookPreset Preset,
        MotionProfile Motion,
        int MaterialGeneration,
        bool HasMaskMap,
        Vector3 Scale,
        int Scene);

    private sealed class NativeRenderer : IOverlayNativeRenderer
    {
        private readonly PlateStructure plate;
        private readonly int slot;
        private readonly int instanceId;
        private readonly Renderer renderer;

        internal NativeRenderer(PlateStructure plate, int slot, Renderer renderer)
        {
            this.plate = plate;
            this.slot = slot;
            this.renderer = renderer;
            instanceId = renderer.GetInstanceID();
        }

        public long Identity => renderer.Pointer.ToInt64();

        public bool Current
        {
            get
            {
                if (plate == null || renderer == null) return false;
                var wrapper = slot == 0
                    ? plate.highDetailRenderer
                    : (slot == 1 ? plate.lowDetailRenderer : plate.shadowProxyRenderer);
                var current = wrapper?.Renderer;
                return current != null &&
                       current.Pointer == renderer.Pointer &&
                       renderer.GetInstanceID() == instanceId;
            }
        }

        public bool Hidden
        {
            get => renderer.forceRenderingOff;
            set => renderer.forceRenderingOff = value;
        }
    }

    private sealed record Snapshot(
        OverlaySurface Front,
        OverlaySurface Back,
        OverlaySurface Sides,
        string Signature,
        NMatrix Mirror);

    private const string DesignerScene = "VehicleDesignerUI";
    private const string ControlScene = "VehicleControlUI";
    private const string PhotoModeScene = "PhotomodeOverlay";

    private static readonly Dictionary<long, PlateStructure> plates = new();
    private static readonly Dictionary<long, View> views = new();
    private static readonly OverlayBuildQueue<PlateStructure> pending = new();
    private static readonly OverlayVisibility hidden = new(ex => Warn("restore", ex.Message));
    private static readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private static readonly List<long> removed = new();

    private static bool diagnosticOverlay;
    private static bool wasDesigner;
    private static int lastFrame = -1;
    private static float nextPoll;
    private static float nextPaintSync;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlateStructure), nameof(PlateStructure.Build))]
    private static void OnPlateBuild(PlateStructure __instance) => Note(__instance, changed: true);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlateStructure), nameof(PlateStructure.Release))]
    private static void OnPlateRelease(PlateStructure __instance) => Release(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void OnBeforeModelBuild(VehicleObjectModel __instance) => EnsureDynamicIfUnderHingedPlate(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void OnModelBuild(VehicleObjectModel __instance) => NoteModel(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.ApplyOverlay))]
    private static void OnApplyOverlay(VehicleOverlay __0) => SetDiagnosticOverlay(__0 != null);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ArmourOverlayConfig), nameof(ArmourOverlayConfig.Draw))]
    private static void OnArmourOverlayDraw() => SetDiagnosticOverlay(true);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.GameLateUpdate))]
    private static void OnLateUpdate(GameTime __0)
    {
        bool paused = __0 != null && __0.PauseState != PauseState.Unpaused;
        float dt = __0 != null ? __0.DeltaTime : Time.deltaTime;
        Tick(paused, dt);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.DisablePhysics))]
    private static void OnDisablePhysics()
    {
        try
        {
            FloppyMotionController.RestoreAllAttachedChildrenToRest();
        }
        catch
        {
        }
    }

    internal static void ProtectTransformHierarchy(GameObject? go)
    {
        if (go == null) return;
        try
        {
            var conn = go.GetComponent<TransformConnection>() ?? go.AddComponent<TransformConnection>();
            if (conn != null)
            {
                conn.AllowReparenting = false;
                conn.ChildReparentOptions = ChildReparentType.Ignore;
            }
        }
        catch
        {
        }
    }

    internal static void MarkModelDynamic(VehicleObjectModel? model)
    {
        if (model == null || model.WasCollected) return;
        if (!model.HasModel || model.Model == null)
        {
            model.MarkDynamic();
        }
        else if ((model.Model.Flags & VehicleRendererFlags.Static) != 0)
        {
            model.MarkDynamic();
            model.Model.Flags &= ~VehicleRendererFlags.Static;
        }
    }

    internal static void EnsureDynamicIfUnderHingedPlate(VehicleObjectModel? model)
    {
        if (model == null || model.WasCollected) return;
        var vt = model.VehicleObject?.VehicleTransform;
        vt = vt?.Parent ?? vt?.PlaceTarget;
        for (int depth = 0; vt != null && !vt.WasCollected && depth < 8; depth++, vt = vt.Parent ?? vt.PlaceTarget)
        {
            if (HasHingedPlate(vt.VehicleObject) || HasHingedPlate(vt.Mirror?.VehicleObject))
            {
                ProtectTransformHierarchy(model.VehicleObject?.VehicleTransform?.gameObject);
                MarkModelDynamic(model);
                return;
            }
        }
    }

    private static bool HasHingedPlate(VehicleObject? vo)
    {
        var comps = vo?.Components;
        if (comps == null) return false;
        int count = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for (int j = 0; j < count; j++)
        {
            if (comps[j]?.TryCast<PlateStructure>() is { } pPlate &&
                PlateConfigStore.Get(pPlate).Motion == MotionProfile.Hinged)
                return true;
        }
        return false;
    }

    internal static void NoteModel(VehicleObjectModel? model)
    {
        try
        {
            if (model == null) return;

            EnsureDynamicIfUnderHingedPlate(model);

            var components = model.VehicleObject?.Components;
            if (components == null) return;
            int count = components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int i = 0; i < count; i++)
            {
                if (components[i]?.TryCast<PlateStructure>() is { } plate)
                    Note(plate, changed: false);
            }
        }
        catch (Exception ex)
        {
            Warn("note-model", ex.Message);
        }
    }

    internal static void Note(PlateStructure? plate, bool changed = false)
    {
        try
        {
            if (plate == null) return;
            long key = plate.Pointer.ToInt64();
            var cfg = PlateConfigStore.Get(plate);
            if (cfg.IsDefault)
            {
                if (plates.ContainsKey(key) || views.ContainsKey(key))
                    Release(plate);
                return;
            }

            plates[key] = plate;
            if (cfg.Motion == MotionProfile.Hinged)
                ProtectTransformHierarchy(plate.gameObject);
            if (changed)
                Suspend(key);
            if (changed || !views.ContainsKey(key))
                pending.Schedule(key, plate, Time.unscaledTime, 0.08f, replace: changed);
        }
        catch (Exception ex)
        {
            Warn("note", ex.Message);
        }
    }

    internal static void NotifyConfigChanged(PlateStructure plate, bool geometryChanged)
    {
        if (plate == null) return;
        long key = plate.Pointer.ToInt64();
        var cfg = PlateConfigStore.Get(plate);
        if (cfg.IsDefault)
        {
            Release(plate);
            return;
        }

        plates[key] = plate;
        if (cfg.Motion == MotionProfile.Hinged)
            ProtectTransformHierarchy(plate.gameObject);
        if (!geometryChanged && views.TryGetValue(key, out var view) &&
            view.Root != null &&
            view.Preset == cfg.Preset &&
            view.Motion == cfg.Motion &&
            string.Equals(view.ConfigGeoSignature, cfg.GeometrySignature(), StringComparison.Ordinal))
        {
            var mat = ResolveOverlayMaterial(plate, cfg, out bool hasMask);
            if (mat != null)
            {
                if (!FloppyMotionController.PinPreviewEnabled || cfg.Motion == MotionProfile.Static || !wasDesigner)
                {
                    view.Renderer.sharedMaterial = mat;
                    ApplyMaterialProperties(view.Renderer, plate, cfg, hasMask);
                }
                if (cfg.Motion != MotionProfile.Static)
                    FloppyMotionController.UpdateMaterialReference(key, mat);

                var updatedView = view with
                {
                    MaterialSignature = cfg.MaterialSignature(),
                    MaterialGeneration = MaterialLibrary.Generation,
                    HasMaskMap = hasMask
                };
                views[key] = updatedView;

                if (!updatedView.Root.activeSelf || !hidden.Intact(key))
                {
                    try
                    {
                        Activate(key, plate, updatedView);
                    }
                    catch (Exception ex)
                    {
                        Suspend(key);
                        Warn($"activate:{key}", ex.Message);
                    }
                }
                return;
            }
        }

        Suspend(key);
        pending.Schedule(key, plate, Time.unscaledTime, 0.04f, replace: true);
    }

    internal static void RefreshMaterials()
    {
        foreach (var pair in plates)
            pending.Schedule(pair.Key, pair.Value, Time.unscaledTime, 0f, replace: true);
    }

    internal static void Release(PlateStructure? plate)
    {
        if (plate == null) return;
        long key = plate.Pointer.ToInt64();
        FloppyMotionController.Unregister(key);
        hidden.Restore(key);
        DestroyView(key);
        pending.Remove(key);
        plates.Remove(key);
    }

    internal static void Clear()
    {
        SuspendAll();
        FloppyMotionController.Clear();
        TankPaintReader.Clear();
        foreach (var key in views.Keys.ToArray())
            DestroyView(key);
        hidden.RestoreAll();
        pending.Clear();
        plates.Clear();
        warnings.Clear();
        removed.Clear();
        diagnosticOverlay = false;
        wasDesigner = false;
        lastFrame = -1;
        nextPoll = 0f;
        nextPaintSync = 0f;
    }

    private static void SetDiagnosticOverlay(bool active)
    {
        if (diagnosticOverlay == active) return;
        diagnosticOverlay = active;
        SuspendAll();
        if (!active)
        {
            foreach (var p in plates)
                pending.Schedule(p.Key, p.Value, Time.unscaledTime, 0.1f);
        }
    }

    private static void Tick(bool paused, float dt)
    {
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            TickCore(paused, dt);
        }
        catch (Exception ex)
        {
            SuspendAll();
            Warn("tick", ex.Message);
        }
    }

    private static void TickCore(bool paused, float dt)
    {
        bool designer = IsDesignerActive();
        if (designer != wasDesigner)
        {
            SuspendAll();
            diagnosticOverlay = false;
            wasDesigner = designer;
            nextPoll = 0f;
            foreach (var p in plates)
                pending.Schedule(p.Key, p.Value, Time.unscaledTime, 0.15f);
        }

        hidden.RestoreOrphans(key => views.ContainsKey(key));

        bool busyMouse = designer && MouseHeld();
        bool syncPaint = Time.unscaledTime >= nextPaintSync;
        if (syncPaint) nextPaintSync = Time.unscaledTime + 0.20f;

        removed.Clear();
        foreach (var pair in plates)
        {
            long key = pair.Key;
            var plate = pair.Value;
            try
            {
                if (plate == null || plate.VehicleObject == null || plate.VehicleObject.Released)
                {
                    removed.Add(key);
                    continue;
                }

                var cfg = PlateConfigStore.Get(plate);
                if (cfg.IsDefault)
                {
                    removed.Add(key);
                    continue;
                }

                if (!views.TryGetValue(key, out var view))
                    continue;

                if (view.Root == null || view.Scene != plate.gameObject.scene.handle)
                {
                    Suspend(key);
                    DestroyView(key);
                    pending.Schedule(key, plate, Time.unscaledTime, 0.15f);
                    continue;
                }

                if (view.Preset != cfg.Preset ||
                    view.Motion != cfg.Motion ||
                    (view.Scale - plate.transform.lossyScale).sqrMagnitude > 1e-6f ||
                    view.MaterialGeneration != MaterialLibrary.Generation ||
                    !string.Equals(view.ConfigGeoSignature, cfg.GeometrySignature(), StringComparison.Ordinal))
                {
                    Suspend(key);
                    pending.Schedule(key, plate, Time.unscaledTime, 0.10f);
                    continue;
                }

                if ((designer && diagnosticOverlay) || TankPaintReader.IsXRayMaterial(plate))
                {
                    Suspend(key);
                    continue;
                }

                if (!plate.gameObject.activeInHierarchy)
                {
                    Suspend(key);
                    continue;
                }

                if (!plate.HasMesh || plate.meshRefresh || plate.Mesh.RebuildRequired)
                {
                    Suspend(key);
                    pending.Schedule(key, plate, Time.unscaledTime, 0.15f);
                    continue;
                }

                if (!view.Root.activeSelf)
                {
                    pending.Schedule(key, plate, Time.unscaledTime, 0.10f);
                    continue;
                }

                if (!hidden.Intact(key))
                {
                    Suspend(key);
                    pending.Schedule(key, plate, Time.unscaledTime, 0.10f);
                    continue;
                }

                Place(view.Root.transform, plate.transform);

                if (syncPaint &&
                    cfg.Preset != LookPreset.Native &&
                    cfg.ColorMode == ColorMode.FollowTankPaint &&
                    !(designer && FloppyMotionController.PinPreviewEnabled && cfg.Motion != MotionProfile.Static))
                {
                    var paint = TankPaintReader.Read(plate);
                    MaterialLibrary.ApplyPropertyBlock(view.Renderer, cfg, paint, view.HasMaskMap);
                }
            }
            catch (Exception ex)
            {
                Suspend(key);
                removed.Add(key);
                Warn("follow", ex.Message);
            }
        }

        foreach (long key in removed)
        {
            FloppyMotionController.Unregister(key);
            hidden.Restore(key);
            DestroyView(key);
            pending.Remove(key);
            plates.Remove(key);
        }

        if (designer && diagnosticOverlay)
        {
            FloppyMotionController.Tick(designer: true, photoMode: false, paused: true, dt: 0f);
            return;
        }

        bool photoMode = IsPhotoModeActive();
        FloppyMotionController.Tick(designer, photoMode, paused, dt);

        if (busyMouse) return;

        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + 1.0f;
            foreach (var p in plates)
            {
                if (designer || !views.ContainsKey(p.Key))
                    pending.Schedule(p.Key, p.Value, Time.unscaledTime, 0f);
            }
        }

        if (pending.Count == 0) return;

        foreach (var job in pending.TakeDue(Time.unscaledTime, 4))
        {
            try
            {
                Build(job.Plate, designer);
            }
            catch (Exception ex)
            {
                Suspend(job.Key);
                Warn($"build:{job.Key}", ex.Message);
                pending.Schedule(job.Key, job.Plate, Time.unscaledTime, 2.0f);
            }
        }
    }

    private static void Build(PlateStructure plate, bool designer)
    {
        if (plate == null) return;
        long key = plate.Pointer.ToInt64();
        var cfg = PlateConfigStore.Get(plate);
        if (cfg.IsDefault || plate.VehicleObject == null || plate.VehicleObject.Released)
        {
            Release(plate);
            return;
        }

        if (!plate.gameObject.activeInHierarchy)
        {
            Suspend(key);
            pending.Schedule(key, plate, Time.unscaledTime, 1.0f);
            return;
        }

        if ((designer && diagnosticOverlay) || TankPaintReader.IsXRayMaterial(plate))
        {
            Suspend(key);
            return;
        }

        var mat = ResolveOverlayMaterial(plate, cfg, out bool hasMask);
        if (mat == null)
        {
            Suspend(key);
            if (cfg.Preset == LookPreset.Native)
                pending.Schedule(key, plate, Time.unscaledTime, 0.25f);
            return;
        }

        var snap = ReadSnapshot(plate, designer);
        if (snap == null)
        {
            Suspend(key);
            pending.Schedule(key, plate, Time.unscaledTime, 0.25f);
            return;
        }

        var scale = plate.transform.lossyScale;
        string configGeoSig = cfg.GeometrySignature();
        string geoSig = $"{snap.Signature}|{QuantizeScale(scale)}|{configGeoSig}";

        if (views.TryGetValue(key, out var old) &&
            old.Root != null &&
            old.Scene == plate.gameObject.scene.handle &&
            string.Equals(old.GeometrySignature, geoSig, StringComparison.Ordinal))
        {
            if (!(designer && FloppyMotionController.PinPreviewEnabled && cfg.Motion != MotionProfile.Static))
            {
                old.Renderer.sharedMaterial = mat;
                ApplyMaterialProperties(old.Renderer, plate, cfg, hasMask);
            }
            ConfigureRendererShadows(old.Renderer, cfg);
            if (cfg.Motion != MotionProfile.Static)
                FloppyMotionController.UpdateMaterialReference(key, mat);
            views[key] = old with
            {
                ConfigGeoSignature = configGeoSig,
                MaterialSignature = cfg.MaterialSignature(),
                Preset = cfg.Preset,
                Motion = cfg.Motion,
                MaterialGeneration = MaterialLibrary.Generation,
                HasMaskMap = hasMask
            };
            if (cfg.Motion == MotionProfile.Hinged)
                ProtectTransformHierarchy(plate.gameObject);
            if (designer && cfg.Motion == MotionProfile.Hinged)
                FloppyMotionController.RefreshHingedChildren(key);
            Activate(key, plate, views[key]);
            return;
        }

        var nScale = new NVector(scale.x, scale.y, scale.z);
        Vector3[] uVerts;
        Vector3[] uNorms;
        Vector2[] uUvs;
        Vector4[] uTangents;
        int[] triangles;
        NVector[] coverVertices;
        int coverTriCount;
        int minNativeTris = -1; // -1 means enforce triCount >= nativeTriCount; >= 1 overrides for thin sheets.

        SubdividedSheet? sheet = null;
        ThickShellMesh? shell = null;

        if (cfg.Motion == MotionProfile.Static)
        {
            var data = WeldedPlateGeometry.Build(
                nScale,
                cfg.TileSizeM,
                cfg.UvRotationDeg,
                snap.Front,
                snap.Back,
                snap.Sides);

            if (plate.Mirrored)
                data = WeldedPlateGeometry.Mirror(data, snap.Mirror);

            uVerts = ToUnity(data.Vertices);
            uNorms = ToUnity(data.Normals);
            uUvs = ToUnity(data.Uv);
            uTangents = ToUnity(data.Tangents);
            triangles = data.Triangles;
            coverVertices = data.Vertices;
            coverTriCount = data.Triangles.Length / 3;
        }
        else
        {
            sheet = MeshSubdivider.Build(
                snap.Front,
                snap.Back,
                nScale,
                cfg.TileSizeM,
                cfg.UvRotationDeg,
                snap.Mirror,
                plate.Mirrored,
                cfg.Motion);
            shell = ThickShellMesh.Build(sheet, cfg.Preset, NVector.Abs(nScale));

            uVerts = ToUnity(shell.RestVertices);
            uNorms = ToUnity(shell.RestNormals);
            uUvs = ToUnity(shell.Uvs);
            uTangents = ToUnity(shell.Tangents);
            triangles = shell.Triangles;
            coverTriCount = shell.Triangles.Length / 3;

            if (shell.IsThick)
            {
                coverVertices = shell.RestVertices;
            }
            else
            {
                // Include front + back offset envelope so bounding-box proof covers the thin native mesh.
                int nSim = sheet.Positions.Length;
                coverVertices = new NVector[nSim * 2];
                for (int i = 0; i < nSim; i++)
                {
                    coverVertices[i] = sheet.Positions[i];
                    coverVertices[nSim + i] = sheet.Positions[i] + sheet.Normals[i] * sheet.BackOffset[i];
                }
                minNativeTris = 1;
            }
        }

        GameObject? root = null;
        Mesh? mesh = null;
        try
        {
            root = new GameObject($"FloppyParts {cfg.Preset}")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = plate.gameObject.layer
            };
            root.SetActive(false);
            Place(root.transform, plate.transform);

            mesh = new Mesh
            {
                name = "floppy-parts-overlay",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32
            };
            mesh.MarkDynamic();

            mesh.vertices = uVerts;
            mesh.normals = uNorms;
            mesh.uv = uUvs;
            mesh.tangents = uTangents;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.receiveShadows = true;
            ConfigureRendererShadows(renderer, cfg);
            ApplyMaterialProperties(renderer, plate, cfg, hasMask);

            FloppyMotionController.Unregister(key);
            hidden.Restore(key);
            DestroyView(key);

            var view = new View(
                root,
                mesh,
                renderer,
                coverVertices,
                coverTriCount,
                minNativeTris,
                geoSig,
                configGeoSig,
                cfg.MaterialSignature(),
                cfg.Preset,
                cfg.Motion,
                MaterialLibrary.Generation,
                hasMask,
                scale,
                plate.gameObject.scene.handle);
            views[key] = view;

            if (sheet != null && shell != null)
            {
                if (cfg.Motion == MotionProfile.Hinged)
                    MarkHingedChildrenDynamic(plate);
                FloppyMotionController.Register(key, plate, sheet, shell, cfg, mesh, renderer, mat, designer);
            }

            root = null;
            mesh = null;

            Activate(key, plate, view);

            if (Plugin.Detailed)
                Plugin.ModLog.LogInfo(
                    $"[Overlay] Plate #{plate.VUID.Value} ({cfg.Preset}/{cfg.Motion}): {uVerts.Length} verts, {triangles.Length / 3} tris.");
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }
    }

    private static Material? ResolveOverlayMaterial(PlateStructure plate, PlateLookConfig cfg, out bool hasMask)
    {
        if (cfg.Preset == LookPreset.Native)
        {
            hasMask = true;
            return plate.exteriorMaterial?.Material ?? plate.defaultExteriorMaterial?.Material;
        }
        return MaterialLibrary.GetMaterial(cfg.Preset, cfg.CustomFolder, out hasMask);
    }

    private static void ApplyMaterialProperties(
        MeshRenderer renderer,
        PlateStructure plate,
        PlateLookConfig cfg,
        bool hasMask)
    {
        if (cfg.Preset == LookPreset.Native)
        {
            // Never apply MPB tint override onto native VehicleLit materials.
            renderer.SetPropertyBlock(null);
            return;
        }
        var paint = cfg.ColorMode == ColorMode.FollowTankPaint ? TankPaintReader.Read(plate) : null;
        MaterialLibrary.ApplyPropertyBlock(renderer, cfg, paint, hasMask);
    }

    private static void ConfigureRendererShadows(MeshRenderer renderer, PlateLookConfig cfg)
    {
        if (cfg.Preset == LookPreset.Glass)
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        else if (cfg.Motion != MotionProfile.Static || cfg.Preset == LookPreset.CamoNet)
            renderer.shadowCastingMode = ShadowCastingMode.On;
        else
            renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static void Activate(long key, PlateStructure plate, View view)
    {
        if (wasDesigner && diagnosticOverlay)
        {
            Suspend(key);
            return;
        }

        Place(view.Root.transform, plate.transform);
        try
        {
            if (!hidden.Intact(key))
            {
                hidden.Restore(key);
                int slotCount = (view.Motion != MotionProfile.Static || view.Preset is LookPreset.Glass or LookPreset.CamoNet)
                    ? 3
                    : 2;
                var renderers = new List<(int Slot, Renderer Renderer)>(slotCount);
                for (int slot = 0; slot < slotCount; slot++)
                {
                    var wrapper = slot == 0
                        ? plate.highDetailRenderer
                        : (slot == 1 ? plate.lowDetailRenderer : plate.shadowProxyRenderer);
                    var renderer = wrapper?.Renderer;
                    if (renderer == null) continue;
                    var nativeMesh = wrapper?.SharedMesh;
                    if (nativeMesh == null || !Covers(plate, renderer, nativeMesh, view))
                        throw new InvalidOperationException("Native LOD coverage not proven; native plate retained.");
                    renderers.Add((slot, renderer));
                }

                if (renderers.Count == 0)
                    throw new InvalidOperationException("No native renderer ready.");

                foreach (var r in renderers)
                    hidden.Hide(key, new NativeRenderer(plate, r.Slot, r.Renderer));
            }

            view.Root.SetActive(true);
        }
        catch
        {
            Suspend(key);
            throw;
        }
    }

    private static Snapshot? ReadSnapshot(PlateStructure plate, bool designer)
    {
        if (designer && MouseHeld()) return null;
        if (!plate.HasMesh || plate.meshRefresh) return null;
        var native = plate.Mesh;
        if (native == null || native.Released || native.RebuildRequired) return null;

        var front = CopySurface(native.frontMesh);
        var back = CopySurface(native.thickenFaceMesh);
        var sides = CopySurface(native.thickenEdgeMesh);
        if (front == null || back == null || sides == null ||
            front.Vertices.Length < 3 || back.Vertices.Length < 3)
            return null;

        if (native.Released || plate.meshRefresh || native.RebuildRequired || plate.Mesh.Pointer != native.Pointer)
            return null;

        var mirror = ReadMirror(plate);
        string sig = WeldedPlateGeometry.Fingerprint(front, back, sides) + "/" + QuantizeMatrix(mirror);
        return new Snapshot(front, back, sides, sig, mirror);
    }

    private static OverlaySurface? CopySurface(TessellatedMesh? mesh)
    {
        if (mesh == null) return null;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var triangles = mesh.triangles;
        if (vertices == null || normals == null || triangles == null) return null;

        var v = new NVector[vertices.Length];
        var n = new NVector[normals.Length];
        var t = new int[triangles.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var p = vertices[i];
            v[i] = new NVector(p.x, p.y, p.z);
        }
        for (int i = 0; i < n.Length; i++)
        {
            var p = normals[i];
            n[i] = new NVector(p.x, p.y, p.z);
        }
        for (int i = 0; i < t.Length; i++)
            t[i] = triangles[i];

        return new OverlaySurface(v, t, n);
    }

    private static NMatrix ReadMirror(PlateStructure plate)
    {
        if (!plate.Mirrored) return NMatrix.Identity;
        plate.GetMirrorMatrix(out var m);
        return new NMatrix(
            m.m00, m.m10, m.m20, m.m30,
            m.m01, m.m11, m.m21, m.m31,
            m.m02, m.m12, m.m22, m.m32,
            m.m03, m.m13, m.m23, m.m33);
    }

    private static bool Covers(PlateStructure plate, Renderer renderer, Mesh mesh, View view)
    {
        var bounds = mesh.bounds;
        var corners = new NVector[8];
        for (int i = 0; i < 8; i++)
        {
            var p = bounds.center + Vector3.Scale(
                bounds.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            p = plate.transform.worldToLocalMatrix.MultiplyPoint3x4(
                renderer.transform.localToWorldMatrix.MultiplyPoint3x4(p));
            corners[i] = new NVector(p.x, p.y, p.z);
        }

        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            count += mesh.GetIndexCount(i) / 3;

        if (count > int.MaxValue || count <= 0) return false;
        int requiredTris = view.MinRequiredNativeTris > 0 ? view.MinRequiredNativeTris : (int)count;
        return WeldedPlateGeometry.Covers(view.CoverVertices, view.CoverTriCount, corners, requiredTris);
    }

    private static void MarkHingedChildrenDynamic(PlateStructure plate)
    {
        try
        {
            ProtectTransformHierarchy(plate.gameObject);
            var selfVt = plate.VehicleObject?.VehicleTransform;
            if (selfVt == null || selfVt.WasCollected) return;

            var visited = new HashSet<IntPtr> { selfVt.Pointer };
            if (plate.Mirrored && selfVt.Mirror != null && !selfVt.Mirror.WasCollected)
                visited.Add(selfVt.Mirror.Pointer);

            void MarkChildren(VehicleTransform? root, int depth)
            {
                var children = root?.Children;
                if (children == null || depth > 8) return;
                int count = children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count;
                for (int i = 0; i < count; i++)
                {
                    var childVt = children[i];
                    if (childVt == null || childVt.WasCollected || !visited.Add(childVt.Pointer)) continue;
                    var childVo = childVt.VehicleObject;
                    if (childVo == null || childVo.WasCollected || childVo.Released) continue;
                    if (FloppyMotionController.IsCoreVehiclePart(childVo)) continue;

                    ProtectTransformHierarchy(childVt.gameObject);
                    var comps = childVo.Components;
                    if (comps != null)
                    {
                        int cCount = comps.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                        for (int j = 0; j < cCount; j++)
                        {
                            if (comps[j]?.TryCast<VehicleObjectModel>() is { } model)
                                MarkModelDynamic(model);
                        }
                    }
                    MarkChildren(childVt, depth + 1);
                }
            }

            MarkChildren(selfVt, 1);
            if (plate.Mirrored && selfVt.Mirror != null && !selfVt.Mirror.WasCollected)
                MarkChildren(selfVt.Mirror, 1);
        }
        catch
        {
        }
    }

    private static string QuantizeScale(Vector3 s) =>
        FormattableString.Invariant(
            $"{(int)MathF.Round(s.x * 1000f)}x{(int)MathF.Round(s.y * 1000f)}x{(int)MathF.Round(s.z * 1000f)}");

    private static string QuantizeMatrix(NMatrix m)
    {
        static int Q(float v) => (int)MathF.Round(v * 1000f);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Q(m.M11)},{Q(m.M12)},{Q(m.M13)},{Q(m.M14)},{Q(m.M21)},{Q(m.M22)},{Q(m.M23)},{Q(m.M24)},{Q(m.M31)},{Q(m.M32)},{Q(m.M33)},{Q(m.M34)},{Q(m.M41)},{Q(m.M42)},{Q(m.M43)},{Q(m.M44)}");
    }

    private static void Place(Transform visual, Transform plate)
    {
        visual.SetPositionAndRotation(plate.position, plate.rotation);
        visual.localScale = plate.lossyScale;
    }

    private static void Suspend(long key)
    {
        hidden.Restore(key);
        if (views.TryGetValue(key, out var view) && view.Root != null)
            view.Root.SetActive(false);
    }

    private static void SuspendAll()
    {
        foreach (long key in views.Keys)
            Suspend(key);
    }

    private static void DestroyView(long key)
    {
        if (!views.Remove(key, out var view)) return;
        if (view.Root != null)
        {
            view.Root.SetActive(false);
            Object.DestroyImmediate(view.Root);
        }
        if (view.Mesh != null)
            Object.DestroyImmediate(view.Mesh);
    }

    private static bool IsPhotoModeActive() => SceneSurfaceActive(PhotoModeScene);

    private static bool IsDesignerActive() =>
        SceneSurfaceActive(DesignerScene) && !SceneSurfaceActive(ControlScene) && !IsPhotoModeActive();

    private static bool SceneSurfaceActive(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.isLoaded) return false;
        var roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (root != null && root.activeInHierarchy) return true;
        }
        return false;
    }

    private static bool MouseHeld()
    {
        try
        {
            var mouse = Mouse.current;
            return mouse != null &&
                   (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed);
        }
        catch
        {
            return false;
        }
    }

    private static void Warn(string key, string message)
    {
        if (warnings.Add(key))
            Plugin.ModLog.LogWarning($"[Overlay] {message}");
    }

    private static Vector3[] ToUnity(NVector[] src)
    {
        var dst = new Vector3[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new Vector3(src[i].X, src[i].Y, src[i].Z);
        return dst;
    }

    private static Vector2[] ToUnity(System.Numerics.Vector2[] src)
    {
        var dst = new Vector2[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new Vector2(src[i].X, src[i].Y);
        return dst;
    }

    private static Vector4[] ToUnity(System.Numerics.Vector4[] src)
    {
        var dst = new Vector4[src.Length];
        for (int i = 0; i < src.Length; i++)
            dst[i] = new Vector4(src[i].X, src[i].Y, src[i].Z, src[i].W);
        return dst;
    }
}
