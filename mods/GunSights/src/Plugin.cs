using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace SprocketGunSights;

[BepInPlugin(Guid, "Sprocket Gun Sights", Version)]
public sealed class Plugin : BasePlugin
{
    internal const string Guid = "sprocket.gunsights";
    internal const string Version = "0.2.4";
    internal static new ManualLogSource Log = null!;
    internal static bool Enabled;
    internal static bool HideStockReticle;
    internal static float ReferenceFov;
    internal static float MinimumLinePixels;
    internal static bool FollowTurret;
    internal static bool ShowAmmunition;
    private readonly List<Harmony> owners = new();

    public override void Load()
    {
        Log = base.Log;
        Enabled = Config.Bind("General", "Enabled", true, "Period gunner's sights: chosen per placed sight (right-click > Gun sight). Restart to change.").Value;
        HideStockReticle = Config.Bind("General", "HideStockReticle", true, "Hide the game's own scope reticle while a period sight is shown.").Value;
        ReferenceFov = Math.Clamp(Config.Bind("Optics", "ReferenceFieldOfView", 60f, "Vertical field of view (degrees) that counts as 1x. Magnification m gives tan(fov/2) = tan(reference/2) / m.").Value, 20f, 100f);
        MinimumLinePixels = Math.Clamp(Config.Bind("Optics", "MinimumLinePixels", 1.6f, "Thinnest reticle stroke on screen, so low-power sights stay readable.").Value, .8f, 6f);
        FollowTurret = Config.Bind("View", "ScopeFollowsTurret", true, "Show the scope picture along the sight's real axis, so it follows the turret and gun; the mouse still commands the aim. Hides the moving aim circle. The ATGM guidance sight keeps its free view.").Value;
        ShowAmmunition = Config.Bind("View", "ShowAmmunition", true, "Show the loaded and next round from Sprocket Expanded Ballistics in the sight.").Value;
        if (!Enabled) { Log.LogInfo("Sprocket Gun Sights disabled by configuration."); return; }

        Install("sprocket.gunsights.choices", typeof(SightChoices), "Per-sight country and variant menu with saving", required: true);
        Install("sprocket.gunsights.scope", typeof(ScopeRuntime), "Optics, wheel switching and stock reticle hiding", required: true);
        AddComponent<SightHud>();
        if (FollowTurret)
        {
            try { TurretView.Install(); }
            catch (Exception ex) { FollowTurret = false; Log.LogWarning("[Gun sights] Turret-following view unavailable; native view kept: " + ex.Message); }
        }
        Log.LogInfo($"Sprocket Gun Sights {Version} loaded: {SightCatalogue.All.Length} sights for {SightCatalogue.Countries.Length} countries.");
    }

    private void Install(string owner, Type patches, string what, bool required)
    {
        var harmony = new Harmony(owner);
        try { harmony.PatchAll(patches); owners.Add(harmony); }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            if (required) Enabled = false;
            Log.LogError($"[Gun sights] {what} unavailable: {ex.Message}" + (required ? " Mod disabled; native sights retained." : ""));
        }
    }

    public override bool Unload()
    {
        foreach (var h in owners) h.UnpatchSelf();
        SightHud.Clear(); ScopeRuntime.Clear(); SightChoices.Clear();
        return true;
    }

    // The scope view renders through the main camera while the sight is shown.
    internal static Camera? ScopeCamera()
    {
        var main = Camera.main;
        if (main != null && main.isActiveAndEnabled) return main;
        foreach (var camera in Camera.allCameras) if (camera != null && camera.isActiveAndEnabled) return camera;
        return null;
    }
}
