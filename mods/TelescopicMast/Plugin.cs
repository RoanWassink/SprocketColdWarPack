using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket;
using Sprocket.Gameplay.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Sprocket.Vehicles.Colliders;
using SprocketKeybinds;
using SprocketUiKit;

namespace SprocketTelescopicMast;

[BepInPlugin(ModGuid, "Sprocket Telescopic Mast", "0.9.9")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.3 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.12 <0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal const string ModGuid = "sprocket.telescopicmast";
    internal const string Version = "0.9.9";
    internal const string RaiseActionId = "raise", LowerActionId = "lower";
    internal static Plugin Instance = null!;
    internal static ModKeybind RaiseBinding = null!, LowerBinding = null!;
    private static ConfigEntry<Key> legacyRaise = null!, legacyLower = null!;
    private static bool legacyMigrationComplete;
    internal static ConfigEntry<float> Stroke = null!, Speed = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<bool>? DetailedLogging;
    /// <summary>Diagnostics.DetailedLogging: gate every trace line with "if (Plugin.Detailed)".</summary>
    internal static bool Detailed => DetailedLogging?.Value ?? true;
    internal static void Trace(string message) => Instance.Log.LogInfo(message);
    internal const string PanelActionId = "hinge-panel";
    internal static ModKeybind? PanelBinding;
    internal static ModKeybind?[]? ActionBindings;
    private Harmony? harmony;
    private Harmony? iconHarmony;
    private readonly List<Harmony> hingeHarmonies = new();
    public override void Load()
    {
        Instance = this;
        Io.IoApiCore.Log = message => Log.LogWarning(message);
        try { Io.IoApiCore.ConfigurePluginRoot(Paths.PluginPath); } catch (Exception ex) { Log.LogWarning("[IO API] Icon root unavailable: " + ex.Message); }
        Enabled = Config.Bind("General", "Enabled", true, "Enable mast movement; disabling retracts the masts.");
        DetailedLogging = Config.Bind("Diagnostics", "DetailedLogging", true, "Verbose trace lines for testing. Warnings and errors are always logged.");
        legacyRaise = Config.Bind("Keys", "Raise", Key.Insert, "Legacy migration only. Change Mast / Raise in the vanilla Settings keybind menu.");
        legacyLower = Config.Bind("Keys", "Lower", Key.Delete, "Legacy migration only. Change Mast / Lower in the vanilla Settings keybind menu.");
        RegisterBindings();
        Stroke = Config.Bind("Mechanism", "StrokeMetres", 1f, new ConfigDescription("Maximum extension at height scale 1; actual extension follows the mast height scale.", new AcceptableValueRange<float>(.05f, 1f)));
        Speed = Config.Bind("Mechanism", "SpeedMetresPerSecond", .2f, new ConfigDescription("Speed at height scale 1; scaled masts retain the same full-stroke time.", new AcceptableValueRange<float>(.01f, 1f)));
        bool passive = Config.Bind("Diagnostics", "PassiveAtStartup", false,
            "Diagnostic baseline. Restart required: do not install gameplay hooks or mast driver; native part assets remain loaded.").Value;
        if (!passive)
        {
            harmony = new Harmony("sprocket.telescopicmast");
            try { harmony.PatchAll(typeof(Hooks)); }
            catch { harmony.UnpatchSelf(); UnregisterBindings(); throw; }
            AddComponent<MastDriver>();
            LoadHinge();
        }
        iconHarmony=new Harmony("sprocket.telescopicmast.icons");
        try { iconHarmony.PatchAll(typeof(IconHooks)); }
        catch(Exception ex) { iconHarmony.UnpatchSelf(); Log.LogWarning($"Optional mast icons unavailable: {ex.Message}"); }
        AddComponent<KeybindMigrationDriver>();
        if (passive) Log.LogInfo("[Mast] PASSIVE DIAGNOSTIC: gameplay hooks and mast driver not installed; no mast mesh, collision, mass, fitting or movement changes. Restart with PassiveAtStartup=false for normal operation.");
        else Log.LogInfo($"[Mast] v{Version} mast mass cache synchronized; native collision mesh retained; custom cylinder only for rendering and placement. Telescopic mast ready: hold Mast / Raise or Mast / Lower; configure these actions in the vanilla Settings keybind menu; stroke 1m, speed 0.2m/s by default.");
    }
    // ---------- Hinge + I/O (0.4.0). Every group is optional: a failure never disables the mast. ----------
    private void LoadHinge()
    {
        try
        {
            // Each input is one class + one registration; engine, panel and storage are not touched.
            foreach (var type in new Io.IInputType[] { new Io.CannonFiredInput(), new Io.CannonReloadingInput(), new Io.CannonReloadedInput(),
                new Io.CannonLoadedInput(), new Io.HingeStateInput(), new Io.MastStateInput(), new Io.VehicleBrakingInput(), new Io.KeyInput(), new Io.RotatorStateInput() })
                try { if (Io.IoRegistry.FindInput(type.Id) == null) Io.IoRegistry.RegisterInput(type); }
                catch (Exception ex) { Log.LogWarning($"[Hinge] Input type {type.Id} registration failed: {ex.Message}"); }
            if (Io.IoRegistry.FindOutput("hinge") == null) Io.IoRegistry.RegisterOutput(new HingeOutput());
            if (Io.IoRegistry.FindOutput("mast") == null) Io.IoRegistry.RegisterOutput(new Io.FlowMastOutput());
            if (Io.IoRegistry.FindOutput("rotator") == null) Io.IoRegistry.RegisterOutput(new Io.RotatorOutput());
            // 0.6.0: how each built-in input reads in the panel's rule sentence (types without one get a fallback).
            Io.BuiltinRules.Register();
        }
        catch (Exception ex) { Log.LogWarning($"[Hinge] I/O type registration failed: {ex.Message}"); }
        RegisterHingeBindings();
        bool core = PatchGroup("hinge", typeof(HingeCoreHooks), "hinge parts, rotation and I/O storage");
        if (!core) { Log.LogWarning("[Hinge] Hinge disabled; the mast is unaffected."); return; }
        PatchGroup("hinge.loadbackup", typeof(HingeLoadBackupHooks), "backup I/O config reader");
        PatchGroup("hinge.fire", typeof(HingeFireHooks), "cannon fired/reloading inputs");
        // 0.6.0: the I/O Flow panel is a Sprocket UI Kit panel; cursor, input block, click-through and Esc are the kit's.
        bool panel = false;
        if (PanelBinding != null)
            try { HingePanel.Create(PanelBinding); panel = true; }
            catch (Exception ex) { Log.LogWarning($"[Hinge] I/O Flow panel unavailable (UI Kit): {ex.Message}"); }
        AddComponent<HingeHost>();
        Log.LogInfo($"[Hinge] v{Version} hinge + I/O ready: inputs {string.Join(", ", Io.IoRegistry.InputTypes.Select(t => t.Id))}; outputs {string.Join(", ", Io.IoRegistry.OutputTypes.Select(t => t.Id))}; API v{Io.IoApi.ApiVersion} rev {Io.IoApi.ApiRevision}; panel {(panel ? "UI Kit " + UiKit.Version : "unavailable")}; detailed logging {(Detailed ? "on" : "off")}. Open the panel with I/O Flow / Open panel (Settings keybind menu).");
    }
    private bool PatchGroup(string suffix, Type hooks, string what)
    {
        var h = new Harmony(ModGuid + "." + suffix);
        try { h.PatchAll(hooks); hingeHarmonies.Add(h); return true; }
        catch (Exception ex) { try { h.UnpatchSelf(); } catch { } Log.LogWarning($"[Hinge] Optional hooks for {what} unavailable: {ex.Message}"); return false; }
    }
    private static void RegisterHingeBindings()
    {
        try
        {
            PanelBinding = Keybinds.RegisterButton(ModGuid, "Telescopic Mast", PanelActionId, "I/O Flow / Open panel", "<Keyboard>/f8");
            ActionBindings = new ModKeybind?[Io.KeyInput.Actions];
            for (int i = 1; i <= Io.KeyInput.Actions; i++)
                ActionBindings[i - 1] = Keybinds.RegisterButton(ModGuid, "Telescopic Mast", "hinge-action-" + i, "I/O Flow / Action " + i, "<Keyboard>/numpad" + i);
        }
        catch (Exception ex)
        {
            Instance.Log.LogWarning($"[Hinge] Keybind registration failed; panel and action keys unavailable: {ex.Message}");
            UnregisterHingeBindings();
        }
    }
    private static void UnregisterHingeBindings()
    {
        if (PanelBinding != null) { Keybinds.Unregister(ModGuid, PanelActionId); PanelBinding = null; }
        if (ActionBindings != null)
            for (int i = 1; i <= ActionBindings.Length; i++)
                if (ActionBindings[i - 1] != null) Keybinds.Unregister(ModGuid, "hinge-action-" + i);
        ActionBindings = null;
    }
    private static void RegisterBindings()
    {
        legacyMigrationComplete = false;
        // Register factory defaults, never a customized legacy value: Reset must
        // restore Insert/Delete even after importing a different primary binding.
        RaiseBinding = Keybinds.RegisterButton(ModGuid, "Telescopic Mast", RaiseActionId,
            "Mast / Raise", "<Keyboard>/insert");
        try
        {
            LowerBinding = Keybinds.RegisterButton(ModGuid, "Telescopic Mast", LowerActionId,
                "Mast / Lower", "<Keyboard>/delete");
        }
        catch { Keybinds.Unregister(ModGuid, RaiseActionId); RaiseBinding = null!; throw; }
        try { TryMigrateLegacyBindings(); }
        catch (Exception ex) { MastRuntime.Warn("Legacy key migration", ex); }
    }
    internal static void TryMigrateLegacyBindings()
    {
        if (legacyMigrationComplete || RaiseBinding == null || LowerBinding == null ||
            Keyboard.current == null || Keybinds.IsConfiguring) return;
        string PathFor(Key key) => key == Key.None ? "" : "<Keyboard>/" + Keyboard.current[key].name;
        // Existing API choices win, including Reset and explicitly unbound keys.
        Keybinds.TryImportLegacyBinding(RaiseBinding, PathFor(legacyRaise.Value));
        Keybinds.TryImportLegacyBinding(LowerBinding, PathFor(legacyLower.Value));
        legacyMigrationComplete = true;
        Instance.Log.LogInfo("[Mast Keys] Legacy key migration checked; saved API bindings preserved. Configure Mast / Raise and Mast / Lower in Settings.");
    }
    private static void UnregisterBindings()
    {
        if (RaiseBinding != null) { Keybinds.Unregister(ModGuid, RaiseActionId); RaiseBinding = null!; }
        if (LowerBinding != null) { Keybinds.Unregister(ModGuid, LowerActionId); LowerBinding = null!; }
    }
    public override bool Unload()
    {
        try { MastRuntime.ResetAll(); HingeRuntime.ResetAll(); RotatorRuntime.ResetAll(); }
        finally
        {
            PartPaint.Cleanup();
            harmony?.UnpatchSelf(); iconHarmony?.UnpatchSelf();
            foreach (var h in hingeHarmonies) h.UnpatchSelf();
            HingePanel.Destroy();
            UnregisterBindings(); UnregisterHingeBindings();
        }
        return true;
    }
}

internal static class GameState
{
    private static Sprocket.GameControl.GameController? controller;
    private static float nextResolve;
    /// <summary>Read-only pause state from the current GameController, with Time.timeScale as fallback.</summary>
    internal static bool Paused()
    {
        var current = Current();
        if (current != null)
            try { return current.PauseState != PauseState.Unpaused || Time.timeScale <= 0; }
            catch { }
        return Time.timeScale <= 0;
    }
    private static Sprocket.GameControl.GameController? Current()
    {
        if (controller != null && !controller.WasCollected) return controller;
        if (Time.unscaledTime < nextResolve) return null;
        nextResolve = Time.unscaledTime + 2f;
        try
        {
            // The active gameplay scene root is the GameController; fall back to a scene search
            // when the scene has not exposed its root yet.
            if (SprocketApplication.Instance != null)
                controller = SprocketApplication.Instance.scenes?.ActiveScene?.LoadedSceneRoot?.TryCast<Sprocket.GameControl.GameController>();
        }
        catch { controller = null; }
        if (controller == null)
            try { controller = UnityEngine.Object.FindObjectOfType<Sprocket.GameControl.GameController>(); }
            catch { controller = null; }
        return controller;
    }
    internal static void Reset() { controller = null; nextResolve = 0; }
}

internal static class MastRuntime
{
    internal const string Tag = "sprocketTelescopicMast";
    internal const string Piston = "sprocketMastPiston", Sleeve = "sprocketMastSleeve";
    internal const string BaseTag = "sprocketTelescopeBase", PartTag = "sprocketTelescopePart";
    internal const string BaseModel = "sprocketTelescopeBaseModel", PartModel = "sprocketTelescopePartModel";
    private sealed class State
    {
        internal VehicleObject Object;
        internal VehicleObjectModel? Housing;
        internal VehicleObjectModel? MovingModel;
        internal readonly Io.FlowMastControl Control = new();
        internal bool ManualHeld;
        internal float Height;
        internal float AppliedDistance;
        internal int LastFrame = -1;
        internal int SnapshotCount;
        internal float NextSnapshot;
        internal bool Separate, Fitted, Fitting, TiltLogged;
        internal IntPtr Vehicle;          // 0.5.0: read-only lookup for the mast.state input
        internal float NextVehicleResolve;
        internal State(VehicleObject obj) { Object = obj; }
    }
    private static readonly Dictionary<int, State> States = new();
    private static readonly HashSet<string> Warnings = new();
    private static Mesh? pistonMesh, sleeveMesh, baseMesh, partMesh;
    internal static bool Own(VehicleObjectModel model) =>
        ((model.ComponentID == Piston || model.ComponentID == Sleeve) && model.VehicleObject?.HasTag(Tag) == true) ||
        (model.ComponentID == BaseModel && model.VehicleObject?.HasTag(BaseTag) == true) ||
        (model.ComponentID == PartModel && model.VehicleObject?.HasTag(PartTag) == true);
    internal static void Warn(string area, Exception ex)
    { if (Warnings.Add(area)) Plugin.Instance.Log.LogWarning($"[Mast] {area}: {ex}"); }
    private static bool DateAllowed(VehicleObject obj)
    {
        try
        {
            var value = obj.Vehicle?.DesignInfo?.Date;
            if (value is not { Valid: true } d) return false;
            var date = d.Year == 0 ? DateTime.MinValue : new DateTime(d.Year, d.Month, d.Day);
            if (MastDatePolicy.Allows(date, MastDatePolicy.MastDate)) return true;
            if (Warnings.Add("mast-date " + obj.GetInstanceID()))
                Plugin.Instance.Log.LogWarning(
                    $"[Mast] Telescopic mast needs {MastDatePolicy.MastDate:yyyy-MM-dd}; this vehicle is {date:yyyy-MM-dd}. " +
                    "Saved part retained, no extension.");
            return false;
        }
        catch (Exception ex) { Warn("Mast date", ex); return false; }
    }

    internal static void Prepare(VehicleObjectModel component)
    {
        if (!Own(component) || !component.HasModel) return;
        HingeRuntime.RegisterFlowPart(component.VehicleObject);
        bool fixedBase = component.ComponentID == BaseModel;
        bool separatePart = component.ComponentID == PartModel;
        bool sleeve = component.ComponentID == Sleeve;
        var mesh = fixedBase ? baseMesh ??= CylinderMesh.Create(true,true) :
            separatePart ? partMesh ??= CylinderMesh.Create(false,true) :
            sleeve ? sleeveMesh ??= CylinderMesh.Create(true) : pistonMesh ??= CylinderMesh.Create(false);
        var model = component.Model;
        model.SetMesh(mesh); model.SetShadowProxy(mesh); model.SetInteractionMesh(mesh);
        // Keep the loaded native collision mesh. The mast tube/sleeve geometry is
        // for rendering and placement raycasts, not physical mesh generation.
        // In particular a convex physical hull cannot preserve the sleeve bore.
        component.MarkDynamic(); // Prevent render baking from freezing the mechanism in place.
        var obj = component.VehicleObject;
        var placement = obj.VehicleTransform;
        // Re-enable scaling on pre-0.2.0 instances as well as newly placed parts.
        placement.Options |= Sprocket.Placements.PlacementOptions.AllowScale;
        placement.MinScale = new Vector3(.1f, .1f, .1f);
        placement.MaxScale = new Vector3(5f, 5f, 5f);
        if (fixedBase) return; // This object is never registered for movement or counter-motion.
        int id = obj.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
        {
            States.Add(id, state = new State(obj));
            if (Plugin.Detailed) Plugin.Trace($"[Mast] Registered cylinder on object {id}.");
        }
        state.Separate = separatePart;
        if (sleeve) { state.Housing = component; ApplySleeve(state); }
        else state.MovingModel = component;
        // Loading and spawn builds run before PhysicsEnabled becomes true.
        // Never infer that they are editable instances from this flag alone.
        PhysicsSafety(component);
    }
    internal static void Balance(VehicleObjectModel component)
    {
        if (!Own(component)) return;
        PartPaint.Apply(component); // Existing post-Build path, shared by all visible mast parts.
        // Preserve reference balance, then scale structural material by volume.
        // Fixed control hardware keeps tiny masts from becoming effectively free.
        var scale = component.Scale;
        float volume = Travel.VolumeFactor(scale.x, scale.y, scale.z);
        bool sleeve = component.ComponentID == Sleeve || component.ComponentID == BaseModel;
        float kg = sleeve ? 10 + 40 * volume : 20 * volume;
        float previousCachedMass=component.CachedMass;
        component.SetMass(kg, MassType.Mechanisms);
        // SetMass updates resource amounts, but not the cached mass consumed by
        // GetCenterOfMass_WorldSpace. Native compartment group mass uses GetMass
        // instead. Keep numerator and denominator consistent for our own models.
        float resourceMass=component.GetMass(MassType.Everything);
        component.cachedMass=resourceMass;
        component.RecalculateCenterOfMass();
        string massKey=$"Mass cache {component.GetInstanceID()}";
        if(Plugin.Detailed && Warnings.Add(massKey))
            Plugin.Trace($"[Mast MASS] component={component.ComponentID} oldCached={previousCachedMass:F3} resourceMass={resourceMass:F3} cached={component.CachedMass:F3}; centre-of-mass cache synchronized.");
        component.SetCost(kg * 1.2f, MassType.Mechanisms, CostType.Material);
        component.SetCost((sleeve ? 40 : 0) + (sleeve ? 60 : 40) * MathF.Sqrt(volume), MassType.Mechanisms, CostType.Assembly);
    }
    internal static void Update()
    {
        if (!Plugin.Enabled.Value) { ResetAll(); return; }
        if (!HingeRuntime.InControl() || GameState.Paused()) return;
        if (!Application.isFocused || Time.timeScale <= 0 || Time.deltaTime <= 0 || HingePanel.IsOpen) { ReleaseManualControls(); return; }
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && ((selected.GetComponent<TMP_InputField>()?.isFocused ?? false) ||
            (selected.GetComponent<UnityEngine.UI.InputField>()?.isFocused ?? false))) { ReleaseManualControls(); return; }
        var vehicle = PlayerVehicle();
        if (vehicle == null || vehicle.Rigidbody == null) { ReleaseManualControls(); return; }
        int body = vehicle.Rigidbody.GetInstanceID();
        int input = (Plugin.RaiseBinding.IsHeld ? 1 : 0) - (Plugin.LowerBinding.IsHeld ? 1 : 0);
        foreach (var state in States.Values.ToArray())
        {
            var obj = state.Object;
            if (obj == null || obj.Released) continue;
            var owner = obj.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>();
            if (!obj.PhysicsEnabled || owner?.Rigidbody == null || owner.Rigidbody.GetInstanceID() != body)
            { if (state.ManualHeld) { state.Control.ReleaseManual(); state.ManualHeld = false; } continue; }
            if (input != 0) { state.Control.Manual(input); state.ManualHeld = true; }
            else if (state.ManualHeld) { state.Control.ReleaseManual(); state.ManualHeld = false; }
        }
    }
    private static void ReleaseManualControls()
    {
        foreach (var state in States.Values)
            if (state.ManualHeld) { state.Control.ReleaseManual(); state.ManualHeld = false; }
    }
    internal static bool IsFlowOutput(VehicleObject obj) => obj != null && States.ContainsKey(obj.GetInstanceID());
    internal static double FlowPosition(VehicleObject obj) => obj != null && States.TryGetValue(obj.GetInstanceID(), out var s)
        ? Math.Clamp(s.Height / Math.Max(.05f, Plugin.Stroke?.Value ?? 1f), 0, 1) : 0;
    internal static void FlowCommand(VehicleObject obj, bool on)
    { if (States.TryGetValue(obj.GetInstanceID(), out var state)) state.Control.Flow(on); }
    internal static void UnbindFlow(VehicleObject obj)
    { if (States.TryGetValue(obj.GetInstanceID(), out var state)) state.Control.Unbind(); }
    internal static void TickFlow(IntPtr vehicle)
    {
        if (!Plugin.Enabled.Value || GameState.Paused() || Time.deltaTime <= 0) return;
        foreach (var state in States.Values.OrderBy(s => HingeRuntime.TwinLeader(s.Object) == null ? 0 : 1).ToArray())
        {
            var obj = state.Object;
            if (obj == null || obj.Released || !obj.PhysicsEnabled || !DateAllowed(obj)) continue;
            if (obj.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer != vehicle || state.LastFrame == Time.frameCount) continue;
            if (state.Separate)
            {
                if (!state.Fitted) { if (FindBase(obj.VehicleTransform) == null) continue; state.Fitted = true; }
            }
            else if (state.Housing == null || !state.Housing.HasModel) continue;
            state.LastFrame = Time.frameCount;
            var twinLeader = HingeRuntime.TwinLeader(obj);
            float next = twinLeader != null && States.TryGetValue(twinLeader.GetInstanceID(),out var primaryState)
                ? primaryState.Height : Travel.Step(state.Height, state.Control.Direction, Plugin.Speed.Value, Time.deltaTime, Plugin.Stroke.Value);
            float scaled = Travel.WorldExtension(next, obj.VehicleTransform.Scale.y);
            bool twinOffset = twinLeader != null && Vector3.Distance(obj.VehicleTransform.Position,twinLeader.VehicleTransform.Position) > .00001f;
            if (MathF.Abs(next - state.Height) > .000001f || MathF.Abs(scaled - state.AppliedDistance) > .000001f || twinOffset) Move(state, next);
        }
    }

    // Read-only replacement for the VehicleController.UpdateControl instance: the cached local
    // controller's ControlledVehicle. Re-resolved when the controller or vehicle goes away.
    private static VehicleController? playerController;
    private static float nextControllerResolve;
    private static VehicleBehaviour? PlayerVehicle()
    {
        if (playerController != null && !playerController.WasCollected)
        {
            var current = playerController.ControlledVehicle?.TryCast<VehicleBehaviour>();
            if (current != null && current.ControlType == ControlType.Player) return current;
        }
        if (Time.unscaledTime < nextControllerResolve) return null;
        nextControllerResolve = Time.unscaledTime + 1f;
        try
        {
            playerController = UnityEngine.Object.FindObjectsOfType<VehicleController>()
                .FirstOrDefault(c => c != null && !c.WasCollected &&
                    c.ControlledVehicle?.TryCast<VehicleBehaviour>()?.ControlType == ControlType.Player);
        }
        catch { playerController = null; }
        return playerController?.ControlledVehicle?.TryCast<VehicleBehaviour>();
    }
    private static VehicleTransform? FindBase(VehicleTransform moving)
    {
        // Physical attachment identity, never nearest-object matching.
        var parent = moving.Parent ?? moving.PlaceTarget;
        for (int i=0; parent != null && i<32; i++, parent=parent.Parent)
            if (parent.VehicleObject?.HasTag(BaseTag) == true) return parent;
        return null;
    }
    private static bool Fit(State state)
    {
        var moving = state.Object.VehicleTransform;
        var basis = FindBase(moving);
        if (basis == null)
        {
            if (Warnings.Add("Unattached telescope part"))
                Plugin.Instance.Log.LogWarning("[Mast] Attach Telescope Part to Telescope Base; unattached inserts do not move.");
            return false;
        }
        // Native vehicle Scale is an independent part scale, as confirmed by the working 0.2.0 controls.
        if (state.Fitting) return false;
        state.Fitting=true;
        try
        {
            var position=basis.Position + basis.Rotation * new Vector3(0,
                Travel.InsertPosition(state.Object.PhysicsEnabled ? state.Height : 0, basis.Scale.y),0);
            bool changed=false;
            if (moving.Scale != basis.Scale) { moving.Scale = basis.Scale; changed=true; }
            if (Quaternion.Angle(moving.Rotation,basis.Rotation) > .001f) { moving.Rotation=basis.Rotation; changed=true; }
            if (Vector3.Distance(moving.Position,position) > .00001f) { moving.Position=position; changed=true; }
            if (changed) moving.NotifyTransformChanged();
            if (!state.Fitted && Plugin.Detailed)
                Plugin.Trace($"[Mast] Separate insert {state.Object.GetInstanceID()} fitted to fixed base {basis.VehicleObject.GetInstanceID()}; scaleY={moving.Scale.y:F3}.");
            state.Fitted=true;
            return true;
        }
        finally { state.Fitting=false; }
    }
    private static void Move(State state, float height)
    {
        var transform = state.Object.VehicleTransform;
        bool tilt = Plugin.Detailed && !state.TiltLogged && height != 0;
        if (tilt) LogTilt(transform, "before");
        float distance = Travel.WorldExtension(height, transform.Scale.y);
        float delta = distance - state.AppliedDistance;
        if (state.Separate)
        {
            var basis = FindBase(transform);
            if (basis == null) return;
            // Absolute position relative to the fixed base. Native save/editor
            // transitions can reset a transform without resetting our old offset.
            // Never subtract that cached offset from a reset transform.
            transform.Position = basis.Position + basis.Rotation * new Vector3(0,Travel.InsertPosition(height,basis.Scale.y),0);
        }
        // Native setter moves logical descendants, their renderers and interaction transforms.
        // Move along the installed cylinder's local up axis, including turret/hull rotation.
        else transform.Position += transform.Rotation * new Vector3(0, delta, 0);
        state.Height = height;
        state.AppliedDistance = distance;
        transform.NotifyTransformChanged();
        if (tilt) { LogTilt(transform, "after"); state.TiltLogged = true; }
        // Apply AFTER native transform processing. Use world-space ModelPosition:
        // its inspected native setter converts to renderer-local coordinates,
        // including scaling and the separate sleeve component transform.
        ApplySleeve(state);
        if (Plugin.Detailed && (height == 0 || height == Plugin.Stroke.Value))
        {
            float sleeveError = state.Housing != null && state.Housing.HasModel
                ? Vector3.Distance(state.Housing.ModelPosition, transform.Position - transform.Rotation * new Vector3(0, distance, 0)) : -1;
            Plugin.Trace($"[Mast] Stop height={height:F3} worldExtension={distance:F3}m scaleY={transform.Scale.y:F3} sleevePositionError={sleeveError:F5}m.");
        }
    }
    /// <summary>Read-only: sight-tilt diagnosis on the mast (HINGE-MAP measuring point 5).</summary>
    private static void LogTilt(VehicleTransform insert, string when)
    {
        try
        {
            var children = insert.Children;
            int n = children == null ? 0 : children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count;
            Plugin.Trace($"[Mast TILT] {when} insert={insert.VehicleObject?.GetInstanceID()} rot={insert.Rotation.eulerAngles} localRot={insert.LocalRotation.eulerAngles} mode={insert.OrientationMode} autoReorient={insert.AutoReorientationEnabled} manual={insert.ManualRotation:F1} lossyScale={insert.transform.lossyScale} children={n}");
            for (int i = 0; i < n && i < 8; i++)
            {
                var c = children![i];
                if (c == null) continue;
                Plugin.Trace($"[Mast TILT] {when} child={c.VehicleObject?.GetInstanceID()} guid={c.VehicleObject?.GUID} rot={c.Rotation.eulerAngles} localRot={c.LocalRotation.eulerAngles} mode={c.OrientationMode} autoReorient={c.AutoReorientationEnabled} manual={c.ManualRotation:F1} lossyScale={c.transform.lossyScale} unityParent={c.transform.parent?.name}");
            }
        }
        catch (Exception ex) { Warn("Tilt diagnostics", ex); }
    }
    private static void ApplySleeve(State state)
    {
        var housing = state.Housing;
        if (housing == null || !housing.HasModel || state.Object == null || state.Object.Released) return;
        var transform = state.Object.VehicleTransform;
        housing.ModelPosition = transform.Position - transform.Rotation * new Vector3(0, state.AppliedDistance, 0);
    }
    internal static void RefreshAll()
    {
        foreach (var state in States.Values.ToArray())
        {
            if (state.Height == 0 || state.Object == null || state.Object.Released) continue;
            ApplySleeve(state);
        }
    }
    internal static void ResetAll()
    {
        foreach (var state in States.Values.ToArray())
            try { if (state.Object != null && !state.Object.Released && state.Height != 0) Move(state, 0); }
            catch (Exception ex) { Warn("Retract", ex); }
    }
    internal static void Retract(VehicleObject obj)
    {
        if (obj != null && States.TryGetValue(obj.GetInstanceID(), out var state) && state.Height != 0) Move(state, 0);
    }
    internal static void BeforeSave(VehicleObject obj)
    {
        if (obj == null || !States.TryGetValue(obj.GetInstanceID(),out var state)) return;
        // Saving the separate assembly is read-only with respect to its live position.
        // On load, Maintain reconstructs its canonical seated position from the base.
        if (!state.Separate) Retract(obj);
    }
    private static string ParentPath(Transform transform)
    {
        var names = new List<string>();
        for (int i=0; transform != null && i<6; i++,transform=transform.parent)
            names.Add($"{transform.name}#{transform.GetInstanceID()}");
        return string.Join(" <- ",names);
    }
    internal static void ObserveSpawn()
    {
        if (!Plugin.Detailed) return;
        if (!SceneManager.GetSceneByName("VehicleControlUI").isLoaded) return;
        foreach(var state in States.Values.ToArray())
        {
            if(state.SnapshotCount >= 5 || Time.unscaledTime < state.NextSnapshot ||
               state.Object == null || state.Object.Released) continue;
            state.SnapshotCount++; state.NextSnapshot=Time.unscaledTime+.5f;
            var moving=state.Object.VehicleTransform;
            var basis=FindBase(moving);
            var body=state.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Rigidbody;
            if(body == null) continue;
            string map=SceneManager.GetSceneByName("Sandbox (Low performance)").isLoaded ? "flat" : "other";
            var model=state.MovingModel?.Model;
            var rendered=model?.transform;
            var rodUnity=moving.transform;
            var baseUnity=basis?.transform;
            Plugin.Trace($"[Mast SPAWN] map={map} sample={state.SnapshotCount} object={state.Object.GetInstanceID()} physics={state.Object.PhysicsEnabled} height={state.Height:F3} bodyWorld={body.position} bodyVelocity={body.linearVelocity} mass={body.mass:F1} centreOfMass={body.centerOfMass} rodWorld={moving.Position} rodInBody={body.transform.InverseTransformPoint(moving.Position)} baseWorld={basis?.Position} baseInBody={(basis != null ? body.transform.InverseTransformPoint(basis.Position).ToString() : "missing")} renderWorld={rendered?.position} rodParents={ParentPath(rodUnity)} baseParents={(baseUnity != null ? ParentPath(baseUnity) : "missing")} renderParents={(rendered != null ? ParentPath(rendered) : "missing")}");
            var children=moving.Children;
            if(children != null)
                for(int i=0;i<children.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>().Count && i<8;i++)
                {
                    var child=children[i];
                    Plugin.Trace($"[Mast CHILD] sample={state.SnapshotCount} object={child.VehicleObject.GetInstanceID()} world={child.Position} inBody={body.transform.InverseTransformPoint(child.Position)} parents={ParentPath(child.transform)}");
                }
        }
    }
    internal static void Maintain()
    {
        // Explicit editor scene gate: a spawning clone can have PhysicsEnabled=false.
        if(!SceneManager.GetSceneByName("VehicleDesignerUI").isLoaded || SceneManager.GetSceneByName("VehicleControlUI").isLoaded) return;
        foreach(var pair in States.ToArray())
        {
            var state=pair.Value;
            if(state.Object == null || state.Object.Released) { States.Remove(pair.Key); continue; }
            if(!state.Separate || state.Fitting || state.Object.PhysicsEnabled) continue;
            // Runs after native loading/editor updates; references can become
            // available after either model's Build/OnModelLoaded callback.
            state.Height=0; state.AppliedDistance=0;
            Fit(state);
        }
    }
    internal static void PhysicsSafety(VehicleObjectModel component)
    {
        if(!Own(component)) return;
        component.generateDamageColliders=false;
        component.interactionColliderType &= ~ColliderType.Physics;
        var colliders=component.Colliders;
        if(colliders == null) return;
        int count=colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
        for(int i=0;i<count;i++)
        {
            var collider=colliders[i];
            if(collider == null || collider.Owner?.Pointer != component.Pointer) continue;
            var original=collider.Type;
            collider.Type=original & ~ColliderType.Physics;
            if(original != collider.Type && Plugin.Detailed)
                Plugin.Trace($"[Mast] Removed own physical collider bit: component={component.ComponentID} {original} -> {collider.Type} (selection/placement retained).");
        }
    }
    internal static void BeforePhysics(VehicleObject obj)
    {
        var components=obj.Components;
        if(components == null) return;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for(int i=0;i<count;i++)
        {
            var model=components[i]?.TryCast<VehicleObjectModel>();
            if(model == null || !Own(model)) continue;
            PhysicsSafety(model);
            if (Plugin.Detailed) Plugin.Trace($"[Mast] Physics enable component={model.ComponentID} object={obj.GetInstanceID()} position={obj.VehicleTransform.Position} scale={obj.VehicleTransform.Scale}; colliderPhysics excluded.");
        }
    }
    internal static void Designer(VehicleObject obj)
    {
        Retract(obj);
        if (obj != null && States.TryGetValue(obj.GetInstanceID(), out var state)) state.Fitted = false;
    }
    internal static void Remove(VehicleObject obj) { PartPaint.Release(obj); if (obj != null) States.Remove(obj.GetInstanceID()); }

    /// <summary>0.5.0, read-only: extension fraction of each mast on this vehicle (for the mast.state input).</summary>
    internal static void Collect(IntPtr vehicle, List<Io.MastState> into)
    {
        float stroke = Plugin.Stroke?.Value ?? 1f;
        if (!(stroke > 0)) stroke = 1f;
        foreach (var state in States.Values.ToArray())
        {
            try
            {
                if (state.Object == null || state.Object.Released) continue;
                if (Time.unscaledTime >= state.NextVehicleResolve)
                {
                    state.NextVehicleResolve = Time.unscaledTime + 2f;
                    state.Vehicle = state.Object.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Pointer ?? IntPtr.Zero;
                }
                if (state.Vehicle == vehicle) into.Add(new Io.MastState(Math.Clamp(state.Height / stroke, 0f, 1f)));
            }
            catch (Exception ex) { Warn("Mast state input", ex); }
        }
    }
}

[HarmonyPatch]
internal static class Hooks
{
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void BeforeBuild(VehicleObjectModel __instance)
    { try { MastRuntime.Prepare(__instance); } catch (Exception ex) { MastRuntime.Warn("Mesh preparation", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void AfterBuild(VehicleObjectModel __instance)
    { try { MastRuntime.Prepare(__instance); MastRuntime.Balance(__instance); } catch (Exception ex) { MastRuntime.Warn("Mass/cost", ex); } }
    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    { try { if (MastRuntime.Own(__instance)) { MastRuntime.Prepare(__instance); __instance.ForceRebuild(); } } catch (Exception ex) { MastRuntime.Warn("Model loaded", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.DisablePhysics))]
    private static void Designer(VehicleObject __instance)
    { try { MastRuntime.Designer(__instance); } catch (Exception ex) { MastRuntime.Warn("Return to designer", ex); } }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObject),nameof(VehicleObject.EnablePhysics))]
    private static void Physics(VehicleObject __instance)
    { try { MastRuntime.BeforePhysics(__instance); } catch(Exception ex) { MastRuntime.Warn("Physics safety",ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleTransform), nameof(VehicleTransform.SaveState))]
    private static void Save(VehicleTransform __instance)
    { try { MastRuntime.BeforeSave(__instance.VehicleObject); } catch (Exception ex) { MastRuntime.Warn("Save position", ex); } }
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Release))]
    private static void Release(VehicleObject __instance) => MastRuntime.Remove(__instance);
}

public sealed class MastDriver : MonoBehaviour
{
    public MastDriver(IntPtr pointer) : base(pointer) { }
    public void Update()
    { try { MastRuntime.Update(); } catch(Exception ex) { MastRuntime.Warn("Movement",ex); } }
    public void LateUpdate()
    { try { MastRuntime.ObserveSpawn(); MastRuntime.Maintain(); MastRuntime.RefreshAll(); } catch(Exception ex) { MastRuntime.Warn("Editor seating",ex); } }
}

// Plugin-owned main-thread retry, independent of designer/control scene lifetimes.
// Never unregister actions when a vehicle or temporary scene object is released.
public sealed class KeybindMigrationDriver : MonoBehaviour
{
    private float nextAttempt;
    public KeybindMigrationDriver(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        if (Time.unscaledTime < nextAttempt) return;
        nextAttempt = Time.unscaledTime + .5f;
        try { Plugin.TryMigrateLegacyBindings(); }
        catch (Exception ex) { MastRuntime.Warn("Legacy key migration", ex); }
    }
}

