using Il2CppInterop.Runtime;
using Sprocket.Vehicles.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SprocketGunSights;

/// <summary>
/// "Sight stays static" (per sight, 0.3.0): shows the scope picture along the sight part's
/// own optical axis instead of the native view that follows the gun. Only sights whose
/// "Sight follows gun" box is off (or the [View] default) get this. The mouse still commands the native aim:
/// only the camera's render view matrix is overridden; its transform, which game
/// systems read for aiming, is never changed. The native dynamic crosshair
/// (where the gun points relative to the mouse aim) is hidden while this is active.
/// The independent ATGM guidance sight keeps its free view.
/// </summary>
internal static class TurretView
{
    private static Camera? overridden;
    private static CrosshairController? crosshairs;
    private static float nextLookup;
    private static bool hidden;
    private static (bool Main, bool Background) crosshairWas;
    private static int warnings;

    internal static void Install()
    {
        Application.add_onBeforeRender(DelegateSupport.ConvertDelegate<UnityAction>((Action)BeforeRender));
        UnityEngine.Rendering.RenderPipelineManager.add_endCameraRendering(
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UnityEngine.Rendering.ScriptableRenderContext, Camera>>(
                (Action<UnityEngine.Rendering.ScriptableRenderContext, Camera>)AfterCamera));
    }

    // The override only exists while that camera renders; aiming code that reads the
    // camera (transform or matrices) between frames always sees the native view.
    private static void AfterCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
    {
        try
        {
            if (overridden == null || camera == null || camera.Pointer != overridden.Pointer) return;
            overridden.ResetWorldToCameraMatrix();
            overridden = null;
        }
        catch (Exception ex) { Warn("Turret view reset", ex); }
    }

    private static IntPtr loggedSight;

    // Decided per active sight every frame, so switching sights or clicking the box applies at once.
    private static bool Active
    {
        get
        {
            if (!ScopeRuntime.Scoped || ScopeRuntime.ActiveSight is not { } sight) { loggedSight = IntPtr.Zero; return false; }
            if (sight.Pointer != loggedSight) { loggedSight = sight.Pointer; SightChoices.LogActivation(sight); }
            return SightChoices.Static(sight);
        }
    }

    private static void BeforeRender()
    {
        try { SightPersistence.Flush(); } catch (Exception ex) { Warn("Save log", ex); }
        try
        {
            var active = Active;
            Crosshair(active);
            var camera = Plugin.ScopeCamera();
            if (!active || camera == null)
            {
                if (overridden != null) { if (!overridden.WasCollected) overridden.ResetWorldToCameraMatrix(); overridden = null; }
                return;
            }
            var optic = ScopeRuntime.ActiveSight!.SightParent;
            if (optic == null) return;
            if (overridden != null && overridden.Pointer != camera.Pointer && !overridden.WasCollected) overridden.ResetWorldToCameraMatrix();
            // Unity view matrix: camera looks down -Z in view space.
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
                Matrix4x4.TRS(camera.transform.position, Quaternion.LookRotation(optic.forward, optic.up), Vector3.one).inverse;
            camera.worldToCameraMatrix = view;
            overridden = camera;
        }
        catch (Exception ex) { Warn("Turret view", ex); }
    }

    private static void Crosshair(bool hide)
    {
        if (hide && (crosshairs == null || crosshairs.WasCollected) && Time.unscaledTime >= nextLookup)
        {
            nextLookup = Time.unscaledTime + 2f;
            crosshairs = Object.FindObjectOfType<CrosshairController>();
        }
        if (crosshairs == null || crosshairs.WasCollected) { hidden = false; return; }
        Image? main = crosshairs.crosshair, background = crosshairs.crosshairBackground;
        if (hide)
        {
            if (!hidden) { crosshairWas = (main != null && main.enabled, background != null && background.enabled); hidden = true; }
            if (main != null) main.enabled = false;
            if (background != null) background.enabled = false;
        }
        else if (hidden)
        {
            if (main != null) main.enabled = crosshairWas.Main;
            if (background != null) background.enabled = crosshairWas.Background;
            hidden = false;
        }
    }

    private static void Warn(string area, Exception ex)
    { if (warnings++ < 4) Plugin.Log.LogWarning($"[Gun sights] {area}: {ex.Message}"); }
}
