using BepInEx;
using BepInEx.Configuration;
using SprocketKeybinds;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using PlayerController = Sprocket.Gameplay.VehicleControl.VehicleController;
using NativeScope = Sprocket.Vehicles.Weapons.Scope;

namespace SprocketThermalSight;

[BepInPlugin("sprocket.thermalsight", "Sprocket Thermal Sight", "0.5.6")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.6 <0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Instance = null!;
    private Harmony? harmony;
    private Harmony? iconHarmony;
    private Harmony? visualHarmony;
    private Harmony? rangeHarmony;
    private Harmony? menuHarmony;
    private Harmony? consoleModelHarmony, irHarmony;
    public override void Load()
    {
        Instance = this;
        try
        {
            Keybinds.RegisterModMetadata("sprocket.thermalsight", "Nero", "Thermal");
            Runtime.Configure(Config);
            harmony = new Harmony("sprocket.thermalsight");
            harmony.PatchAll(typeof(Hooks));
            harmony.PatchAll(typeof(ProfileStateHooks));
            harmony.PatchAll(typeof(ConsoleLinks));
            harmony.PatchAll(typeof(ConsolePersistence));
            AddComponent<ThermalDriver>();
            Log.LogInfo("Thermal sight v0.5.6 loaded; remote FCS laying/traverse drives, Driver/Commander consoles, Commander Panoramic Sight, OES-OP targeting sight, 2S38 OES-OP HUD and full-screen thermal displays supported.");
        }
        catch (Exception ex) { Runtime.Shutdown(); Runtime.ReleaseBindings(); harmony?.UnpatchSelf(); Log.LogError("Thermal disabled: " + ex); return; }
        try
        {
            iconHarmony = new Harmony("sprocket.thermalsight.icon");
            iconHarmony.PatchAll(typeof(ElectronicsIcons));
        }
        catch(Exception ex){iconHarmony?.UnpatchSelf();Log.LogWarning("Optional electronics icons disabled: "+ex.Message);}
        try
        {
            visualHarmony=new Harmony("sprocket.thermalsight.visual");
            visualHarmony.PatchAll(typeof(ProfileInspector));
            visualHarmony.PatchAll(typeof(LensAssets));
            visualHarmony.PatchAll(typeof(ThermalModelAssets));
        }
        catch (Exception ex) { visualHarmony?.UnpatchSelf(); Log.LogWarning("Optional thermal visual hooks disabled: " + ex.Message); }
        try
        {
            consoleModelHarmony=new Harmony("sprocket.thermalsight.fcs-model");
            consoleModelHarmony.PatchAll(typeof(ConsoleAssets));
            consoleModelHarmony.PatchAll(typeof(T72SightAssets));
        }
        catch(Exception ex){consoleModelHarmony?.UnpatchSelf();Log.LogWarning("Optional FCS model hooks disabled: "+ex.Message);}
        try
        {
            irHarmony = new Harmony("sprocket.thermalsight.ir-searchlight");
            irHarmony.PatchAll(typeof(IrSearchlight));
        }
        catch (Exception ex) { irHarmony?.UnpatchSelf(); Log.LogWarning("Optional IR searchlight disabled: " + ex.Message); }
        try
        {
            menuHarmony = new Harmony("sprocket.thermalsight.menu");
            menuHarmony.PatchAll(typeof(SightsMenu));
        }
        catch (Exception ex) { menuHarmony?.UnpatchSelf(); Log.LogWarning("Optional sights menu disabled; native selection retained: " + ex.Message); }
        try
        {
            Rangefinder.Configure(Config);
            rangeHarmony = new Harmony("sprocket.thermalsight.rangefinder");
            rangeHarmony.PatchAll(typeof(RangefinderHooks));
            rangeHarmony.PatchAll(typeof(RangefinderAssets));
            rangeHarmony.PatchAll(typeof(RangefinderBallistics));
            Log.LogInfo("[Rangefinder] Fitted Laser rangefinder device support ready; assign Thermal / Measure range in Settings.");
        }
        catch (Exception ex) { rangeHarmony?.UnpatchSelf(); Rangefinder.Shutdown(); Log.LogWarning("Optional rangefinder disabled: " + ex.Message); }
    }
    public override bool Unload()
    { ConsoleLinks.Clear(); NightHud.Clear(); ThermalModelAssets.Cleanup(); ConsoleAssets.Cleanup(); T72SightAssets.Cleanup(); IrSearchlight.Cleanup(); irHarmony?.UnpatchSelf(); ThermalEraAccess.Clear(); consoleModelHarmony?.UnpatchSelf(); ElectronicsIcons.Cleanup(); visualHarmony?.UnpatchSelf(); menuHarmony?.UnpatchSelf(); Rangefinder.Shutdown(); rangeHarmony?.UnpatchSelf(); RangefinderAssets.Shutdown(); Runtime.Shutdown(); Runtime.ReleaseBindings(); iconHarmony?.UnpatchSelf(); IconHooks.Cleanup(); harmony?.UnpatchSelf(); return true; }
}

public sealed class ThermalDriver : MonoBehaviour
{
    public ThermalDriver(IntPtr ptr) : base(ptr) { }
    public void LateUpdate()
    {
        try { Runtime.Tick(); } catch (Exception ex) { Runtime.Fail(ex); }
        NightHud.Sync();
        try { Rangefinder.Tick(); } catch (Exception ex) { Rangefinder.Fail(ex); }
        RangefinderAssets.Tick();
        LensAssets.Tick();
        ThermalModelAssets.Tick();
        try { ConsoleLinks.Tick(); ConsoleAssets.Tick(); T72SightAssets.Tick(); IrSearchlight.Tick(); } catch(Exception ex) { Runtime.Warn("Console update: "+ex.Message); }
    }
    public void OnGUI() { Runtime.DrawCooling(); try { Rangefinder.Draw(); } catch (Exception ex) { Rangefinder.Fail(ex, true); } Runtime.DrawHint(); }
    public void OnDestroy() { Rangefinder.Clear(); Runtime.Shutdown(); }
}

internal static class Runtime
{
    private static ConfigEntry<Key> toggle = null!, reload = null!, polarityKey = null!;
    private static ModKeybind? toggleBinding, reloadBinding, polarityBinding;
    private static bool legacyKeysImported;
    private static ConfigEntry<bool> enabled = null!;
    private static ConfigEntry<bool>? detailedLogging, removeThermalVignette;
    internal static bool DetailedLogging => detailedLogging?.Value ?? true;
    private static Dictionary<string, ThermalProfile> models = new(StringComparer.Ordinal);
    // 0.5.4: legacy part GUID / profile id -> modern profile, read from the catalogue's alias table.
    private static ThermalCatalog catalog = new();
    private static string defaultProfileId = "tiGen1";
    internal const string VerticalId = "thermalSightVertical";
    internal const string VerticalGuid = "937bf2e5-17c6-4bae-a7b4-489bf5bc2e68";
    internal const string ConfigurableId = "thermalSight";
    internal const string LensId = "thermalSightLens";
    internal const string LensGuid = "c5319088-49d1-46b4-9112-07a9b4e040fe";
    internal const string ConfigurableGuid = "c9f96b83-cd58-47d1-999b-83c5e71a981f";
    internal const string OesOpId = "oesOpSight";
    internal const string OesOpGuid = "f4c82a19-7d3e-4b61-9a85-3e1c6f0d8b42";
    private static readonly HashSet<string> legacyIds = new(StringComparer.Ordinal) { "thermalSightModel1", "thermalSightModel2", "thermalSightModel3", "thermalSightMk3WhiteHot", "thermalSightMk3BlackHot" };
    internal static IReadOnlyList<ThermalProfile> Profiles => models.Values.ToArray();
    internal static bool IsThermal(VehicleComponent component) => component != null && component.TryCast<GunnerSight>() != null && (component.ComponentID == ConfigurableId || component.ComponentID == VerticalId || component.ComponentID == OesOpId || legacyIds.Contains(component.ComponentID));
    internal static bool IsLegacyGuid(string guid) => ThermalAlias.LegacyPartGuids.Contains(guid);
    private static string catalogPath = "";
    private static bool active, inputAllowed, stopped;
    private static int playerFrame = -100, scopeFrame = -100, inputFrame = -1;
    private static IntPtr sightId;
    // 0.5.4: white-hot/black-hot per sight, session only (no save hook, no save-format change).
    internal static bool BlackHot(long sight) => PolarityState.Get(sight);
    internal static bool EffectiveBlackHot(long sight, ThermalProfile profile, string? sourceId = null) =>
        profile.PolaritySwitch && PolarityState.Effective(sight, profile.BlackHotDefault || catalog.AliasBlackHot(sourceId));
    internal static bool ActiveBlackHot(ThermalProfile profile) => EffectiveBlackHot(ActiveSightKey, profile, activeSight?.ComponentID);
    internal static void SetBlackHot(long sight, bool blackHot) => PolarityState.Set(sight, blackHot);
    internal static void ClearPolarity() => PolarityState.Clear();
    internal static bool AliasBlackHot(string? id) => catalog.AliasBlackHot(id);
    // The sight component itself, so the switch also works before the first scope frame.
    internal static long ActiveSightKey => activeSight?.Pointer.ToInt64() ?? 0;
    // The palette of the last processed frame; the HUD reads it instead of guessing from the profile.
    internal static string ShownPalette = "whiteHot";
    private static GunnerSight? activeSight;
    private static Camera? camera;
    private static PlayerController? player;
    private static GameTime? gameTime;
    private static bool driverReported, playerReported;
    private static float playerLookupAt;
    private static string scopeDescription = "No active scope observed";
    private static ThermalProfile? selected;
    private static DrawRenderersCustomPass? pass;
    private static Material? heatMaterial;
    private static int heatPass;
    private static SensorCapture? sensor;
    private static readonly List<SensorCapture> retired = new();
    private static readonly List<Renderer> targets = new();
    private static float refreshTargetsAt, captureAt;
    private static readonly HashSet<string> warnings = new();
    // 0.5.0: cooled detectors and the HUD inputs owned by this runtime.
    private static ConfigEntry<float> cooldownSeconds = null!;
    // 0.5.1: cooling runs once per sight per session (per vehicle, reset on a new mission); not paused by switching off.
    private static readonly CoolingLedger cooling = new();
    private static float coolingRemaining;
    private static bool irLamp;
    private static long SightKey => sightId.ToInt64();

    internal static bool HasVehicleContext
    {
        get
        {
            if (player != null && player.isActiveAndEnabled && player.ControlledVehicle != null) return true;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name == "VehicleDesignerUI") return true;
            return false;
        }
    }
    internal static void Configure(ConfigFile config)
    {
        detailedLogging = config.Bind("Diagnostics", "DetailedLogging", true, "Log sensor layouts and detailed development diagnostics.");
        enabled = config.Bind("General", "Enabled", true, "Enable thermal only on recognized thermal sight parts.");
        removeThermalVignette = config.Bind("Hud", "RemoveThermalVignette", true, "Remove the native round black optical tube mask on electronic thermal displays (Gen 1..Gen 3 HD, 2S38 OES-OP). Active IR and Image Intensifier tubes keep their round eyepiece.");
        toggle = config.Bind("Keys", "Toggle", Key.N, "Legacy import only; edit Thermal / Toggle in Settings / keybinds.");
        reload = config.Bind("Keys", "ReloadProfiles", Key.F8, "Legacy import only; edit Thermal / Reload profiles in Settings / keybinds. Invalid profiles keep the last accepted catalog.");
        polarityKey = config.Bind("Keys", "Polarity", Key.None, "Legacy import only; edit Thermal / Polarity in Settings / keybinds. White-hot/black-hot switch of the active thermal sight (session only).");
        HeatTracker.Configure(config);
        cooldownSeconds = config.Bind("Sensor", "CooldownSeconds", 20f,
            new ConfigDescription("Start-up time of cooled thermal profiles (cooled: true) after switching on, in seconds: grey, noisy image with a COOLING line, nothing else. 0 = instant.", new AcceptableValueRange<float>(0, 120)));
        NightHud.Configure(config);
        catalogPath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "thermal-models.json");
        Reload(); stopped = false;
        toggleBinding = Keybinds.RegisterButton("sprocket.thermalsight", "Thermal", "toggle", "Toggle", "<Keyboard>/n");
        reloadBinding = Keybinds.RegisterButton("sprocket.thermalsight", "Thermal", "reload-profiles", "Reload profiles", "<Keyboard>/f8");
        // 0.5.4: starts unbound; N and F8 are taken by Toggle and Reload profiles.
        polarityBinding = Keybinds.RegisterButton("sprocket.thermalsight", "Thermal", "polarity", "Polarity", "");
        legacyKeysImported = false;
        TryImportLegacyKeys();
    }
    private static void TryImportLegacyKeys()
    {
        if (legacyKeysImported || Keybinds.IsConfiguring) return;
        var keyboard = Keyboard.current;
        if (keyboard == null && (toggle.Value != Key.None || reload.Value != Key.None || polarityKey.Value != Key.None)) return;
        Keybinds.TryImportLegacyBinding(toggleBinding!, toggle.Value == Key.None ? "" : "<Keyboard>/" + keyboard![toggle.Value].name);
        Keybinds.TryImportLegacyBinding(reloadBinding!, reload.Value == Key.None ? "" : "<Keyboard>/" + keyboard![reload.Value].name);
        Keybinds.TryImportLegacyBinding(polarityBinding!, polarityKey.Value == Key.None ? "" : "<Keyboard>/" + keyboard![polarityKey.Value].name);
        legacyKeysImported = true;
        Plugin.Instance.Log.LogInfo("[Thermal] Shared keybinds ready; existing API choices preserved, legacy CFG keys imported only on first use.");
    }
    private static void Reload()
    {
        var loaded = ThermalCatalog.Read(catalogPath, message => Plugin.Instance.Log.LogInfo(message));
        var next = loaded.Models.ToDictionary(p => p.ComponentId, StringComparer.Ordinal);
        catalog = loaded; models = next; defaultProfileId = loaded.DefaultProfileId; warnings.Clear(); ThermalEraAccess.Clear(); Deactivate(); selected = null;
        Plugin.Instance.Log.LogInfo($"[Thermal] Accepted {models.Count} part profiles; {loaded.Aliases.Count} legacy part aliases.");
    }
    // 0.5.4: a legacy part GUID or an old profile id to the id of its modern replacement (null when unknown).
    internal static string? MapProfile(string? id) => catalog.Map(id);
    internal static bool FindProfile(VehicleComponent component, out ThermalProfile p)
    {
        p = null!;
        // Component IDs are deliberately unique fileIDs on the supplied standalone parts.
        // VehicleObject.GUID identifies a placed object and is NOT the asset GUID.
        if (!IsThermal(component)) return false;
        var requested = ProfileStateHooks.Selection(component);
        if (requested == null && (component.ComponentID == ConfigurableId || component.ComponentID == VerticalId || component.ComponentID == OesOpId))
        {
            string preferred = component.ComponentID == OesOpId && models.ContainsKey("ti2s38OesOp") ? "ti2s38OesOp" : defaultProfileId;
            var available = ProfileSelectionPolicy.Default(models.Values, preferred, profile => ThermalEraAccess.Allowed(component, profile));
            // Keep a profile for electronics accounting even when no sensor is usable.
            p = available ?? models[defaultProfileId];
            return true;
        }
        var id = requested ?? component.ComponentID;
        if (models.TryGetValue(id, out p!)) return true;
        // 0.5.4: a removed legacy part or an old saved profile id resolves to its modern replacement.
        if (catalog.Map(id) is { } mapped && models.TryGetValue(mapped, out p!))
        {
            if (warnings.Add("alias:" + id)) Plugin.Instance.Log.LogInfo($"[Thermal] Legacy profile '{id}' maps to '{mapped}'.");
            return true;
        }
        Warn($"Saved profile '{id}' is missing; using '{defaultProfileId}' until restored or changed in the inspector.", "missing-profile");
        return models.TryGetValue(defaultProfileId, out p!);
    }

    internal static bool OwnsPartGuid(string guid) => string.Equals(guid, ConfigurableGuid, StringComparison.OrdinalIgnoreCase) || string.Equals(guid, VerticalGuid, StringComparison.OrdinalIgnoreCase) || string.Equals(guid, OesOpGuid, StringComparison.OrdinalIgnoreCase) || IsLegacyGuid(guid);
    internal static void PlayerInput(PlayerController controller, GameTime time, Camera view)
    {
        if (stopped) return;
        player = controller; gameTime = time;
        if (!playerReported) { playerReported = true; Plugin.Instance.Log.LogInfo("[Thermal] Player control hook reached."); }
        playerFrame = Time.frameCount; camera = view;
        inputAllowed = Application.isFocused && time.PauseState == PauseState.Unpaused && time.TimeScale > 0 && time.DeltaTime > 0;
        var go = EventSystem.current?.currentSelectedGameObject;
        if (go != null && ((go.GetComponent<TMP_InputField>()?.isFocused ?? false) || (go.GetComponent<UnityEngine.UI.InputField>()?.isFocused ?? false))) inputAllowed = false;
    }
    internal static void ScopeUpdate(ScopeController controller)
    {
        if (stopped) return;
        var sight = controller.HasActive && controller.Scoped ? controller.ControlledScope?.TryCast<GunnerSight>() : null;
        ThermalProfile? next = null;
        // The native controller already decides whether a sight is usable. Non-damageable
        // optical components need not have a positive HealthFraction.
        activeSight = sight;
        if (sight != null && ConsoleLinks.Profile(sight, out var p))
        {
            if (ThermalEraAccess.Allowed(sight, p)) next = p;
            else Warn($"Saved sight profile {p.ComponentId} works from {ThermalEraAccess.WorksFrom(sight, p)}; normal sight retained, identity/mass/cost preserved.", "profile-date:" + p.ComponentId);
        }
        scopeDescription = $"scoped={controller.Scoped}, hasActive={controller.HasActive}, component={sight?.ComponentID ?? "none"}, health={sight?.HealthFraction.ToString() ?? "n/a"}, profile={next?.DisplayName ?? "none"}";
        IntPtr identity = sight?.Pointer ?? IntPtr.Zero;
        if (identity != sightId || !ReferenceEquals(next, selected))
        {
            Deactivate(); sightId = identity; selected = next; Plugin.Instance.Log.LogInfo("[Thermal] Sight: " + scopeDescription);
            // 0.5.4: a removed black-hot part or a black-hot default keeps its polarity on first sight.
            if (sight != null && !PolarityState.Seen(identity.ToInt64()) && ((next?.BlackHotDefault ?? false) || catalog.AliasBlackHot(sight.ComponentID)))
                SetBlackHot(identity.ToInt64(), true);
        }
        scopeFrame = Time.frameCount;
    }
    internal static void Tick()
    {
        for (int i = retired.Count - 1; i >= 0; i--)
            if (!retired[i].Pending) { retired[i].Dispose(); retired.RemoveAt(i); }
        if (stopped) return;
        if (!driverReported) { driverReported = true; Plugin.Instance.Log.LogInfo("[Thermal] Sensor driver ticking."); }
        TryImportLegacyKeys();
        bool inCombat = false;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name == "VehicleControlUI") { inCombat = true; break; }
        if (inCombat && (player == null || !player.isActiveAndEnabled) && Time.unscaledTime >= playerLookupAt)
        {
            playerLookupAt = Time.unscaledTime + 1;
            player = UnityEngine.Object.FindObjectsOfType<PlayerController>().FirstOrDefault(p => p != null && p.isActiveAndEnabled && p.ControlledVehicle != null);
        }
        // A new mission (combat scene left) or another vehicle: every cooled sight cools again.
        if (!inCombat) { if (cooling.Vehicle != 0 || cooling.Count > 0) cooling.Reset(); }
        else if (player != null && player.isActiveAndEnabled && player.ControlledVehicle != null)
            cooling.SetVehicle(player.ControlledVehicle.Pointer.ToInt64());
        if (cooling.Finished(Time.unscaledTime) is { } cooledName)
            Plugin.Instance.Log.LogInfo("[Thermal] Cooled; sensor image live: " + cooledName);
        if (inCombat && player != null && player.isActiveAndEnabled && player.ControlledVehicle != null)
        {
            camera = player.ActiveCamera;
            var controller = player.ScopeControl?.TryCast<ScopeController>();
            if (controller != null) ScopeUpdate(controller);
        }
        inputAllowed = inCombat && Application.isFocused && gameTime != null && gameTime.PauseState == PauseState.Unpaused && gameTime.TimeScale > 0 && gameTime.DeltaTime > 0;
        var focused = EventSystem.current?.currentSelectedGameObject;
        bool textFocused = focused != null && ((focused.GetComponent<TMP_InputField>()?.isFocused ?? false) || (focused.GetComponent<UnityEngine.UI.InputField>()?.isFocused ?? false));
        if (textFocused) inputAllowed = false;
        bool pressed = toggleBinding!.WasPressedThisFrame && inputFrame != Time.frameCount;
        if (pressed)
            Plugin.Instance.Log.LogInfo($"[Thermal] Toggle pressed: enabled={enabled.Value}, combat={inCombat}, inputAllowed={inputAllowed}, gameClock={gameTime != null}, playerHookAge={Time.frameCount-playerFrame}, scopeAge={Time.frameCount-scopeFrame}, camera={camera?.name ?? "none"}; {scopeDescription}");
        if (Application.isFocused && !textFocused && !Keybinds.IsConfiguring && reloadBinding!.WasPressedThisFrame && inputFrame != Time.frameCount)
        {
            inputFrame = Time.frameCount;
            try { ThermalEraAccess.Clear(); Reload(); } catch (Exception ex) { Warn("Profile reload rejected; previous catalog retained: " + ex.Message, "reload"); }
            return;
        }
        // 0.5.4: the polarity switch works on the active sight known to the runtime, including before the first scope frame.
        if (inputAllowed && inputFrame != Time.frameCount && polarityBinding!.WasPressedThisFrame)
        {
            inputFrame = Time.frameCount;
            TogglePolarity();
        }
        if (!enabled.Value || !Application.isFocused || !inCombat || Time.frameCount - scopeFrame > 1 || selected == null || camera == null)
        { Deactivate(); return; }
        if (inputAllowed && inputFrame != Time.frameCount)
        {
            inputFrame = Time.frameCount;
            if (toggleBinding!.WasPressedThisFrame)
            {
                if (active) Deactivate(); else Activate();
            }
        }
        if (active && selected.SensorType == "thermal") HeatTracker.Poll(camera);
        if (active && sensor != null)
        {
            float remaining = selected.Cooled ? cooling.Remaining(SightKey, Time.unscaledTime) : 0;
            coolingRemaining = remaining;
            sensor.Cooling = remaining > 0;
            sensor.UploadIfReady();
            // Same condition as the scope image replacement: only over a visible night/thermal image.
            if (inputAllowed && sensor.HasImage) NightHud.Collect(selected, activeSight, camera, player, sensor.Gain, irLamp, remaining, ShownPalette);
            else NightHud.Hide();
        }
    }
    private static GunnerSight? KnownActiveSight() =>
        activeSight ?? (player?.ScopeControl?.TryCast<ScopeController>() is { HasActive: true } sc ? sc.ControlledScope?.TryCast<GunnerSight>() : null);
    // White-hot/black-hot for this sight, session only: no save hook and no change to the save format.
    private static void TogglePolarity()
    {
        var sight = KnownActiveSight();
        if (sight == null)
        {
            if (warnings.Add("polarity-no-sight")) Plugin.Instance.Log.LogInfo("[Thermal] Polarity key pressed without an active sight; ignored.");
            return;
        }
        ThermalProfile? profile = sight == activeSight && selected != null ? selected
            : (ConsoleLinks.Profile(sight, out var p) && ThermalEraAccess.Allowed(sight, p) ? p : null);
        if (profile == null)
        {
            if (warnings.Add("polarity-no-profile")) Plugin.Instance.Log.LogInfo("[Thermal] Polarity key pressed on a sight without an available thermal profile; ignored.");
            return;
        }
        if (!profile.PolaritySwitch)
        {
            if (warnings.Add("polarity-non-thermal:" + profile.ComponentId)) Plugin.Instance.Log.LogInfo($"[Thermal] Polarity switch ignored on {profile.DisplayName} ({profile.SensorType} stays green).");
            return;
        }
        bool defaultBlackHot = profile.BlackHotDefault || catalog.AliasBlackHot(sight.ComponentID);
        bool blackHot = PolarityState.Toggle(sight.Pointer.ToInt64(), defaultBlackHot);
        Plugin.Instance.Log.LogInfo($"[Thermal] Polarity {profile.DisplayName}: {(blackHot ? "black-hot" : "white-hot")} (session only).");
    }
    private static void Activate()
    {
        if (selected == null || camera == null || !ThermalEraAccess.Allowed(activeSight, selected)) return;
        if (!SystemInfo.supportsAsyncGPUReadback) throw new NotSupportedException("GPU does not support asynchronous sensor capture.");
        if (heatMaterial == null)
        {
            var shader = Shader.Find("HDRP/Unlit");
            if (shader == null) throw new NotSupportedException("HDRP/Unlit shader missing; native graphics retained.");
            heatMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            heatMaterial.SetColor("_UnlitColor", Color.white);
            heatMaterial.SetColor("_EmissiveColor", Color.black);
            heatMaterial.SetFloat("_ZWrite", 0);
            heatMaterial.SetFloat("_ZTestDepthEqualForOpaque", (float)CompareFunction.Equal);
            heatMaterial.SetFloat("_CullMode", (float)CullMode.Back);
            heatMaterial.SetFloat("_CullModeForward", (float)CullMode.Back);
            heatPass = heatMaterial.FindPass("ForwardOnly");
            if (heatPass < 0) throw new NotSupportedException("HDRP/Unlit ForwardOnly pass missing.");
        }
        pass ??= new DrawRenderersCustomPass
        {
            name = "Thermal sensor", layerMask = 0,
            overrideMaterial = heatMaterial, overrideMaterialPassName = "ForwardOnly", overrideMaterialPassIndex = heatPass,
            targetColorBuffer = CustomPass.TargetBuffer.Camera,
            targetDepthBuffer = CustomPass.TargetBuffer.Camera,
            clearFlags = ClearFlag.None
        };
        sensor = new SensorCapture(selected);
        // Use a native pass as the lifecycle host; its zero layerMask draws no stock geometry.
        // Only our instance's Execute postfix appends the heat-mask commands.
        CustomPassVolume.RegisterGlobalCustomPass(CustomPassInjectionPoint.BeforePostProcess, pass, 0);
        active = true; refreshTargetsAt = captureAt = 0; irLamp = false;
        Plugin.Instance.Log.LogInfo("[Thermal] Active: " + selected.DisplayName + $" ({selected.Width}x{selected.Height}, {selected.RefreshHz} Hz)");
        coolingRemaining = 0;
        if (selected.Cooled)
        {
            coolingRemaining = cooling.SwitchOn(SightKey, selected.DisplayName, Time.unscaledTime, cooldownSeconds.Value, out bool started);
            if (started) Plugin.Instance.Log.LogInfo($"[Thermal] Cooling {cooldownSeconds.Value:0.#} s: {selected.DisplayName}");
        }
    }
    internal static void Deactivate()
    {
        bool wasActive = active; active = false; coolingRemaining = 0; irLamp = false; NightHud.Release();
        if (pass != null) CustomPassVolume.UnregisterGlobalCustomPass(CustomPassInjectionPoint.BeforePostProcess, pass);
        if (sensor != null) { retired.Add(sensor); sensor = null; }
        targets.Clear();
        if (wasActive) Plugin.Instance.Log.LogInfo("[Thermal] Normal sight restored.");
    }
    internal static void Render(DrawRenderersCustomPass instance, CustomPassContext ctx)
    {
        if (!ThermalEraAccess.Allowed(activeSight, selected)) { Deactivate(); return; }
        if (!active || pass == null || instance.Pointer != pass.Pointer || sensor == null || selected == null || camera == null || !inputAllowed) return;
        if (ctx.hdCamera.camera.GetInstanceID() != camera.GetInstanceID()) return;
        if (ctx.hdCamera.viewCount != 1 || ctx.hdCamera.msaaEnabled)
            throw new NotSupportedException("Prototype sensor supports mono, non-MSAA cameras only.");
        if (sensor.Pending || sensor.AwaitingUpload || Time.unscaledTime < captureAt) return;
        if (selected.SensorType == "thermal" && Time.unscaledTime >= refreshTargetsAt)
        {
            targets.Clear(); var ids = new HashSet<int>(); HeatTracker.BeginTrack();
            foreach (var v in UnityEngine.Object.FindObjectsOfType<Vehicle>())
            {
                if (v == null || v.Released || !v.BehaviourEnabled) continue;
                HeatTracker.Track(v);
                var register = v.rendererRegister?.renderers;
                if (register == null) continue;
                for (int i = 0; i < register.Count; i++)
                { var r = register[i]?.Renderer; if (r != null && ids.Add(r.GetInstanceID())) targets.Add(r); }
            }
            HeatTracker.EndTrack();
            refreshTargetsAt = Time.unscaledTime + 1;
        }
        // Without a healthy lamp the tube only shows its own green noise; the HUD says why.
        if (selected.SensorType == "activeIR" && activeSight != null)
        {
            irLamp = sensor.PrepareIr(camera, activeSight, player?.ControlledVehicleGameObject);
            if (!irLamp) NoSearchlight();
        }
        // Hot spots are projected with the camera of this custom-pass context: the camera and matrices that draw the mask.
        if (selected.SensorType == "thermal") sensor.SetHeatSpots(HeatTracker.Project(ctx.hdCamera.camera, sensor.HeatSpots, selected.Width, selected.Height), HeatTracker.Settings);
        sensor.Capture(ctx, targets, heatMaterial!, heatPass);
        captureAt = Time.unscaledTime + 1 / selected.RefreshHz;
    }
    internal static bool ReplaceScopeSource(CommandBuffer cmd, HDCamera hdCamera, ref RTHandle source, RTHandle destination)
    {
        if (!ThermalEraAccess.Allowed(activeSight, selected)) { Deactivate(); return true; }
        if (active && inputAllowed && sensor?.HasImage == true && camera != null && hdCamera.camera.GetInstanceID() == camera.GetInstanceID())
        {
            // Electronic thermal monitors (Gen 1..Gen 3 HD, 2S38 OES-OP) are full rectangular displays, not optical glass tubes.
            // Blit directly to destination and skip NativeScope.Render's black circular vignette & stock reticle.
            if ((removeThermalVignette?.Value ?? true) && selected != null && selected.SensorType == "thermal" && destination?.rt != null)
            {
                sensor.ScopeImage(cmd, hdCamera, destination);
                return false;
            }
            source = sensor.ScopeImage(cmd, hdCamera, source);
        }
        return true;
    }
    private static float hintUntil;
    private static void NoSearchlight()
    {
        // Refreshed every capture, so the hint stays while the noisy image is shown.
        hintUntil = Time.unscaledTime + .5f;
        Warn("Active IR needs a healthy IR searchlight on the same vehicle; green noise shown with a HUD hint.", "ir-searchlight-missing");
    }
    internal static void DrawHint()
    {
        if (!active || Time.unscaledTime > hintUntil || activeSight == null || !Application.isFocused) return;
        GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 95, 380, 35), "ACTIVE IR  IR-zoeklicht nodig");
    }
    // 0.5.1: the COOLING line also without the canvas HUD (disabled, failed or not ready), so a grey image is explained.
    internal static void DrawCooling()
    {
        if (!active || coolingRemaining <= 0 || !Application.isFocused || NightHud.CoolingShown) return;
        GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f + 130, 380, 35), "COOLING " + CooldownTimer.Format(coolingRemaining));
    }
    internal static void Fail(Exception ex) { Deactivate(); Warn(ex.GetType().Name + ": " + ex.Message, "runtime-error"); }
    internal static void Warn(string message, string? key = null,
        [System.Runtime.CompilerServices.CallerFilePath] string file = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (warnings.Add(key ?? file + ":" + line)) Plugin.Instance.Log.LogWarning("[Thermal] " + message); }
    internal static void ReleaseBindings()
    {
        if (toggleBinding != null) { Keybinds.Unregister("sprocket.thermalsight", "toggle"); toggleBinding = null; }
        if (reloadBinding != null) { Keybinds.Unregister("sprocket.thermalsight", "reload-profiles"); reloadBinding = null; }
        if (polarityBinding != null) { Keybinds.Unregister("sprocket.thermalsight", "polarity"); polarityBinding = null; }
    }
    internal static void Shutdown()
    {
        if (stopped) return;
        Deactivate(); stopped = true; HeatTracker.Clear(); ClearPolarity();
        // Callback-owned buffers remain alive until both GPU requests complete.
        foreach (var s in retired) s.RetireForShutdown();
        retired.Clear();
        if (heatMaterial != null) { UnityEngine.Object.Destroy(heatMaterial); heatMaterial = null; }
    }
}

internal static class Hooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(PlayerController), nameof(PlayerController.UpdateControl))]
    private static void Input(PlayerController __instance, GameTime __0, Camera __3) { try { Runtime.PlayerInput(__instance, __0, __3); } catch (Exception ex) { Runtime.Fail(ex); } }
    // ScopeController.Update is inlined on the native gameplay route. Runtime.Tick already
    // observes player.ScopeControl each frame; keep that existing fallback without a detour.
    [HarmonyPrefix, HarmonyPatch(typeof(ScopeController), nameof(ScopeController.Dispose))]
    private static void Release() => Runtime.Deactivate();
    [HarmonyPostfix, HarmonyPatch(typeof(DrawRenderersCustomPass), nameof(DrawRenderersCustomPass.Execute), new[] { typeof(CustomPassContext) })]
    private static void Sensor(DrawRenderersCustomPass __instance, CustomPassContext __0) { try { Runtime.Render(__instance, __0); } catch (Exception ex) { Runtime.Fail(ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(NativeScope), nameof(NativeScope.Render))]
    private static bool ScopeImage(CommandBuffer __0, HDCamera __1, ref RTHandle __2, RTHandle __3)
    { try { return Runtime.ReplaceScopeSource(__0, __1, ref __2, __3); } catch (Exception ex) { Runtime.Fail(ex); return true; } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.GetMass))]
    private static void Mass(VehicleComponent __instance, MassType __0, ref float __result)
    { if ((__0 & MassType.Mechanisms) != 0 && Runtime.FindProfile(__instance, out var p)) __result += p.ExtraMassKg; }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.GetCost))]
    private static void Cost(VehicleComponent __instance, MassType __0, CostType __1, ref float __result)
    { if ((__0 & MassType.Mechanisms) != 0 && (__1 & CostType.Assembly) != 0 && Runtime.FindProfile(__instance, out var p)) __result += p.ExtraAssemblyCost; }
}




























