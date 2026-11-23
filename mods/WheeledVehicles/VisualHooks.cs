using HarmonyLib;
using Sprocket.ContinuousTracks;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Tracks.Belts;
using UnityEngine;

namespace SprocketWheeledVehicles;

[HarmonyPatch]
internal static class VisualHooks
{
    // Tracks the exact TrackAssembly pointers whose belt / end wheels were actively hidden by this mod,
    // so normal tracked vehicles are NEVER touched and Play-mode clones never have editor segments re-enabled.
    private static readonly HashSet<IntPtr> HiddenBeltAssemblies = new();
    private static readonly HashSet<IntPtr> HiddenEndWheelAssemblies = new();

    internal static void ClearTrackedState()
    {
        HiddenBeltAssemblies.Clear();
        HiddenEndWheelAssemblies.Clear();
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TrackBelt), nameof(TrackBelt.CreateBeltRenderer))]
    private static bool CreateBeltRendererPrefix(TrackBelt __instance, ref ITrackBeltRenderer __result)
    {
        try
        {
            if (!Plugin.Enabled.Value) return true;
            if (WheeledProfiles.TryGetFor(__instance.ParentAssembly, out var settings) && settings.HideTrackBelt)
            {
                __result = new NullBeltRenderer().Cast<ITrackBeltRenderer>();
                Plugin.Diag(() => "Substituted NullBeltRenderer for wheeled vehicle track belt.");
                return false;
            }
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("CreateBeltRenderer", ex);
        }
        return true;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackBelt), nameof(TrackBelt.UpdateVisibility))]
    private static void BeltUpdateVisibilityPostfix(TrackBelt __instance)
    {
        try
        {
            ApplyBeltVisibility(__instance);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("TrackBelt.UpdateVisibility", ex);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackBelt), nameof(TrackBelt.TrackBuild))]
    private static void BeltTrackBuildPostfix(TrackBelt __instance)
    {
        try
        {
            ApplyBeltVisibility(__instance);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("TrackBelt.TrackBuild", ex);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackAssembly), nameof(TrackAssembly.Build))]
    private static void TrackBuildPostfix(TrackAssembly __instance)
    {
        try
        {
            ApplyTrackAssemblyVisibility(__instance, allowRestore: true);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("TrackAssembly.Build visibility", ex);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(TrackAssembly), nameof(TrackAssembly.EnableBehaviour))]
    private static void TrackEnableBehaviourPostfix(TrackAssembly __instance)
    {
        try
        {
            // Never restore editor belt segments during Play spawn (Sprocket manages tracked belts itself).
            ApplyTrackAssemblyVisibility(__instance, allowRestore: false);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("TrackAssembly.EnableBehaviour visibility", ex);
        }
    }

    internal static void ApplyTrackAssemblyVisibility(TrackAssembly? track, bool allowRestore = false)
    {
        if (track == null) return;
        WheeledSettings? settings = null;
        bool wheeled = Plugin.Enabled.Value && WheeledProfiles.TryGetFor(track, out settings);
        bool hideBelt = wheeled && settings!.HideTrackBelt;
        bool hideEndWheels = wheeled && settings!.HideEndWheels;

        IntPtr ptr = track.Pointer;

        if (track.Belt != null)
        {
            if (hideBelt)
            {
                SetBeltHidden(track.Belt, true);
                HiddenBeltAssemblies.Add(ptr);
            }
            else if (allowRestore && HiddenBeltAssemblies.Remove(ptr))
            {
                SetBeltHidden(track.Belt, false);
            }
            else if (!allowRestore)
            {
                HiddenBeltAssemblies.Remove(ptr);
            }
        }

        // Hide sprocket, idler, front/rear end mounts and return rollers completely on wheeled vehicles.
        // Never touch mounts/renderers on normal tracked vehicles unless restoring a previously hidden designer assembly.
        if (hideEndWheels)
        {
            SetAllEndWheelsHidden(track, true);
            HiddenEndWheelAssemblies.Add(ptr);
        }
        else if (allowRestore && HiddenEndWheelAssemblies.Remove(ptr))
        {
            SetAllEndWheelsHidden(track, false);
        }
        else if (!allowRestore)
        {
            HiddenEndWheelAssemblies.Remove(ptr);
        }
    }

    private static void SetAllEndWheelsHidden(TrackAssembly track, bool hidden)
    {
        SetWheelMountHidden(track.SprocketAssembly, hidden);
        SetWheelMountHidden(track.IdlerAssembly, hidden);
        SetWheelMountHidden(track.FrontWheelAssembly, hidden);
        SetWheelMountHidden(track.RearWheelAssembly, hidden);
        SetWheelMountArrayHidden(track.Rollers, hidden);
    }

    internal static void SetTransformRenderersHidden(Transform? root, bool hidden)
    {
        if (root == null) return;
        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    ApplyRendererHidden(renderers[i], hidden);
            }
            var lods = root.GetComponentsInChildren<LODGroup>(true);
            if (lods != null)
            {
                for (int i = 0; i < lods.Length; i++)
                {
                    if (lods[i] != null) lods[i].enabled = !hidden;
                }
            }
        }
        catch { }
    }

    private static void SetWheelMountArrayHidden(WheelMountArray? array, bool hidden)
    {
        if (array == null) return;
        try
        {
            SetTransformRenderersHidden(array.transform, hidden);
            var assemblies = array.Assemblies;
            if (assemblies != null)
            {
                int count = assemblies.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<WheelMount>>().Count;
                for (int i = 0; i < count; i++)
                    SetWheelMountHidden(assemblies[i], hidden);
            }
        }
        catch { }
    }

    private static void SetWheelMountHidden(WheelMount? mount, bool hidden)
    {
        if (mount == null) return;
        try
        {
            // 1. Mount housing model (sprocket gearbox housing / idler mount) via native SetVisible + forceRenderingOff
            SetVehicleModelHidden(mount.model, hidden);

            // 2. Any suspension arm models belonging to the end wheel mount (e.g. idler tension arm)
            var arms = mount.Suspension?.arms;
            if (arms != null)
            {
                for (int i = 0; i < arms.Length; i++)
                {
                    var arm = arms[i];
                    if (arm == null) continue;
                    SetVehicleModelHidden(arm.model, hidden);
                    SetTransformRenderersHidden(arm.transform, hidden);
                }
            }

            // 3. All axles on this end mount: axle rod model + WheelArray (wheels + bakedModels + shadow proxy)
            var axles = mount.axles;
            if (axles != null)
            {
                for (int i = 0; i < axles.Length; i++)
                {
                    var axle = axles[i]?.TryCast<Axle>();
                    if (axle == null) continue;

                    var rod = axle.Rod?.TryCast<AxleRod>();
                    if (rod != null)
                    {
                        SetVehicleModelHidden(rod.model, hidden);
                        SetTransformRenderersHidden(rod.transform, hidden);
                    }

                    var wheelArray = axle.WheelArray?.TryCast<WheelArray>();
                    if (wheelArray != null)
                    {
                        SetWheelArrayHidden(wheelArray, hidden);
                    }

                    SetTransformRenderersHidden(axle.transform, hidden);
                }
            }

            // 4. Fallback sweep on the mount hierarchy itself
            SetTransformRenderersHidden(mount.transform, hidden);
        }
        catch { }
    }

    private static void SetWheelArrayHidden(WheelArray wheelArray, bool hidden)
    {
        try
        {
            SetTransformRenderersHidden(wheelArray.transform, hidden);

            var baked = wheelArray.bakedModels;
            if (baked != null)
            {
                for (int i = 0; i < baked.Length; i++)
                {
                    var bm = baked[i];
                    if (bm != null) ApplyRendererHidden(bm.Renderer, hidden);
                }
            }

            var shadow = wheelArray.bakedShadowProxyRenderer;
            if (shadow != null)
                ApplyRendererHidden(shadow.Renderer, hidden);
        }
        catch { }
    }

    private static void SetVehicleModelHidden(IVehicleModelComponent? modelComp, bool hidden)
    {
        if (modelComp == null) return;
        try
        {
            var vom = modelComp.TryCast<VehicleObjectModel>();
            if (vom != null)
            {
                // Native visibility toggle prevents RendererMergerAssemblyStage from baking it into the hull mesh
                // and keeps LOD switching from re-enabling it.
                vom.SetVisible(!hidden);
                vom.ShadowsEnabled = !hidden;
                SetTransformRenderersHidden(vom.transform, hidden);
            }
        }
        catch { }
    }

    private static void ApplyRendererHidden(Renderer? r, bool hidden)
    {
        if (r == null) return;
        r.forceRenderingOff = hidden;
        r.enabled = !hidden;
    }

    private static void ApplyBeltVisibility(TrackBelt? belt)
    {
        if (belt == null) return;
        bool hideBelt = Plugin.Enabled.Value &&
                        WheeledProfiles.TryGetFor(belt.ParentAssembly, out var settings) &&
                        settings.HideTrackBelt;
        if (hideBelt)
            SetBeltHidden(belt, true);
    }

    private static void SetBeltHidden(TrackBelt belt, bool hidden)
    {
        try
        {
            if (hidden)
            {
                belt.SetEditorSegmentModelsActive(false);
                belt.segmentsVisible = false;
            }
            else
            {
                belt.segmentsVisible = true;
                belt.SetEditorSegmentModelsActive(true);
            }

            if (belt.beltParent != null)
                SetTransformRenderersHidden(belt.beltParent, hidden);
        }
        catch (Exception ex)
        {
            RuntimeWheeled.Warn("SetBeltHidden", ex);
        }
    }
}
