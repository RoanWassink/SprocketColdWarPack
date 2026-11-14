using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using UnityEngine;
using NativeScope = Sprocket.Vehicles.Weapons.Scope;

namespace SprocketGunSights;

/// <summary>
/// While the native scope view is active: drives the scope camera's focal length to
/// the chosen sight's optical powers (wheel steps between them), hides the stock
/// reticle and shows the period reticle and gunner indications.
/// Only hooks already proven safe in this game build are used.
/// </summary>
[HarmonyPatch]
internal static class ScopeRuntime
{
    private sealed class State
    {
        internal IntPtr Sight;
        internal string VariantId = "";
        internal int Level;
        internal float NextStep;
    }

    private static readonly Dictionary<IntPtr, ScopeController> scopes = new();   // camera -> scope
    private static readonly Dictionary<IntPtr, int> levels = new();               // sight -> optic index
    private static readonly State state = new();
    private static readonly HashSet<string> warned = new();
    private static float focalPerTan; // focal length * tan(fov/2) of the scope camera
    private static float previousApplied;
    private static bool loggedZoom;
    private static int scopedFrame = -10;
    internal static GunnerSight? ActiveSight;
    internal static SightVariant? ActiveVariant;
    internal static ScopeController? ActiveScope;
    internal static float ActiveMagnification;

    internal static bool Scoped => Time.frameCount - scopedFrame <= 2 && ActiveSight != null && ActiveVariant != null;

    private static void Warn(string area, Exception ex)
    { if (warned.Add(area)) Plugin.Log.LogWarning($"[Gun sights] {area}: {ex.Message}"); }

    internal static void SightChanged(IntPtr sight) => levels.Remove(sight);

    internal static void Clear()
    {
        scopes.Clear(); levels.Clear(); warned.Clear();
        ActiveSight = null; ActiveVariant = null; ActiveScope = null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ScopeController), nameof(ScopeController.ActivateSight))]
    private static void Activated(ScopeController __instance)
    {
        try { if (__instance.controller != null) scopes[__instance.controller.Pointer] = __instance; }
        catch (Exception ex) { Warn("Activate", ex); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ScopeController), nameof(ScopeController.Dispose))]
    private static void Disposed(ScopeController __instance)
    {
        foreach (var key in scopes.Where(p => p.Value.Pointer == __instance.Pointer).Select(p => p.Key).ToArray()) scopes.Remove(key);
        if (ActiveScope?.Pointer == __instance.Pointer) { ActiveScope = null; ActiveSight = null; ActiveVariant = null; }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(ScopeCameraController), nameof(ScopeCameraController.ProcessInputs))]
    private static void Inputs(ScopeCameraController __instance)
    {
        if (!Plugin.Enabled) return;
        try
        {
            if (!scopes.TryGetValue(__instance.Pointer, out var scope) || !scope.Scoped) return;
            var sight = scope.ControlledScope?.TryCast<GunnerSight>();
            if (sight == null) return;
            var variant = SightChoices.Variant(sight);
            if (!SightChoices.Available(sight, variant))
            {
                // Saved identity remains; this vehicle keeps the game's own sight.
                SightChoices.WarnUnavailable(sight, variant);
                ActiveScope = null; ActiveSight = null; ActiveVariant = null;
                return;
            }
            ActiveScope = scope; ActiveSight = sight; ActiveVariant = variant; scopedFrame = Time.frameCount;

            if (state.Sight != sight.Pointer || state.VariantId != variant.Id)
            {
                state.Sight = sight.Pointer; state.VariantId = variant.Id;
                state.Level = levels.TryGetValue(sight.Pointer, out var saved) ? saved : 0;
            }
            var powers = variant.Magnifications;
            state.Level = Math.Clamp(state.Level, 0, powers.Length - 1);

            // Wheel steps between the sight's optics instead of the stock smooth zoom.
            var scroll = __instance.scrollValue;
            if (scroll != 0)
            {
                if (powers.Length > 1 && Time.unscaledTime >= state.NextStep)
                {
                    state.Level = Math.Clamp(state.Level + (scroll > 0 ? 1 : -1), 0, powers.Length - 1);
                    levels[sight.Pointer] = state.Level;
                    state.NextStep = Time.unscaledTime + .15f;
                }
                __instance.scrollValue = 0;
            }
            ActiveMagnification = powers[state.Level];
            Zoom(__instance, ActiveMagnification);
        }
        catch (Exception ex) { Warn("Scope input", ex); }
    }

    // The scope camera's view angle follows its focal length: tan(fov/2) = k / f.
    // Measure k from the live camera, then set the focal length for the power.
    private static void Zoom(ScopeCameraController camera, float magnification)
    {
        var applied = camera.AppliedFocalLength;
        var view = Plugin.ScopeCamera();
        // Calibrate once, on the untouched native view (sensor-size constant).
        if (focalPerTan <= 0 && view != null && previousApplied > 0 && Math.Abs(applied - previousApplied) < previousApplied * .002f)
        {
            var k = previousApplied * MathF.Tan(view.fieldOfView * MathF.PI / 360f);
            if (float.IsFinite(k) && k > 0)
            {
                Plugin.Log.LogInfo($"[Gun sights] Scope calibrated: focal {applied:0.###} at {view.fieldOfView:0.##} deg (k={k:0.####}).");
                focalPerTan = k;
            }
        }
        previousApplied = applied;
        if (focalPerTan <= 0) return;
        var fov = SightCatalogue.FieldOfView(Plugin.ReferenceFov, magnification);
        var focal = focalPerTan / MathF.Tan(fov * MathF.PI / 360f);
        if (!float.IsFinite(focal) || focal <= 0) return;
        camera.minZoom = focal; camera.maxZoom = focal; camera.targetFocalLength = focal;
        // Native ApplyInputs only glides the applied focal length while a scroll
        // value is pending, and the wheel is consumed above: glide it here.
        var step = Math.Clamp(Time.unscaledDeltaTime * 12f, 0f, 1f);
        var next = applied + (focal - applied) * step;
        if (Math.Abs(focal - next) < focal * .002f) next = focal;
        camera.appliedFocalLength = next;
        if (!loggedZoom) { loggedZoom = true; Plugin.Log.LogInfo($"[Gun sights] Optics driven: {magnification:0.##}x -> focal {focal:0.##} (view {(view != null ? view.fieldOfView : 0f):0.##} deg before)."); }
    }

    // ------------------------------------------------------------- stock reticle
    private static Texture2D? clear;
    private static readonly Dictionary<IntPtr, Texture> stockOverlay = new();

    [HarmonyPrefix, HarmonyPatch(typeof(NativeScope), nameof(NativeScope.Render))]
    private static void StockReticle(NativeScope __instance)
    {
        try
        {
            if (!Plugin.HideStockReticle) return;
            var parameter = __instance.overlay;
            if (parameter == null) return;
            if (Scoped)
            {
                if (clear == null)
                {
                    clear = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    clear.SetPixel(0, 0, new Color(0, 0, 0, 0)); clear.Apply();
                }
                var current = parameter.value;
                if (current != null && current.Pointer != clear.Pointer && !stockOverlay.ContainsKey(__instance.Pointer))
                {
                    stockOverlay[__instance.Pointer] = current;
                    Plugin.Log.LogInfo($"[Gun sights] Stock scope overlay '{current.name}' hidden while a period sight is active.");
                }
                parameter.value = clear;
            }
            else if (stockOverlay.Remove(__instance.Pointer, out var original)) parameter.value = original;
        }
        catch (Exception ex) { Warn("Stock reticle", ex); }
    }

    // ------------------------------------------------------------ indications
    private static Cannon? cannon;
    private static IntPtr cannonFor;
    private static float nextCannonLookup;

    internal static Cannon? LinkedCannon(GunnerSight sight)
    {
        if (cannonFor == sight.Pointer && Time.unscaledTime < nextCannonLookup && cannon != null) return cannon;
        cannonFor = sight.Pointer; nextCannonLookup = Time.unscaledTime + 1; cannon = null;
        var objects = sight.Vehicle?.ObjectReader?.Items;
        if (objects == null) return null;
        int count = objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for (int i = 0; i < count && cannon == null; i++)
        {
            var parts = objects[i].Components;
            int n = parts.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for (int j = 0; j < n; j++)
                if (parts[j].TryCast<Cannon>() is { } c && c.TryGetAssignedSight(out var assigned) && assigned?.Pointer == sight.Pointer)
                { cannon = c; break; }
        }
        return cannon;
    }

    /// <summary>Turret bearing relative to the hull, 0-360 clockwise, or null.</summary>
    internal static float? Bearing(GunnerSight sight, Cannon? gun)
    {
        var hull = gun?.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.transform;
        var optic = sight.SightParent;
        if (hull == null || optic == null) return null;
        var forward = Vector3.ProjectOnPlane(optic.forward, hull.up);
        if (forward.sqrMagnitude < 1e-6f) return null;
        var angle = Vector3.SignedAngle(hull.forward, forward, hull.up);
        return (angle + 360f) % 360f;
    }
}
