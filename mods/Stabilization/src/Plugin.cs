using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace SprocketStabilization;

[BepInPlugin("sprocket.stabilization", "Sprocket Stabilization", "0.7.3")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource ModLog = null!;

    internal static ConfigEntry<bool>? StabilizerEnabled;

    internal static ConfigEntry<bool>? OverrideRateLimits;

    internal static ConfigEntry<bool>? RealisticLimits;

    internal static ConfigEntry<float>? HorizontalMaxTraverseRate;

    internal static ConfigEntry<float>? ElevationMaxTraverseRate;

    internal static ConfigEntry<float>? ResponseTimeSeconds;

    internal static ConfigEntry<float>? BoostTorqueMultiplier;

    internal static ConfigEntry<float>? BoostMaxTraverseRate;

    internal static ConfigEntry<float>? BoostMaxAcceleration;

    internal static ConfigEntry<float>? BoostMaxBrakeAcceleration;

    private Harmony? editor;
    private Harmony? stabilizerEditor;
    private Harmony? stabilizerRuntime;
    private Harmony? stabilizerPersistence;
    private Harmony? stabilizerBlueprintPersistence;

    public override void Load()
    {
        ModLog = Log;

        StabilizerEnabled = Config.Bind(
            "Stabilizer",
            "Enabled",
            true,
            "Master switch for the per-drive stabilizer boost. The traverse motor checkboxes decide " +
            "which drive is boosted and require the dated sprocketStabilization Technology.");

        OverrideRateLimits = Config.Bind(
            "Stabilizer",
            "OverrideRateLimits",
            false,
            "When true this takes precedence over RealisticLimits: the three explicit limits below are " +
            "written for this drive. When both switches are false the native drive limits stay authoritative.");

        BoostTorqueMultiplier = Config.Bind(
            "Stabilizer",
            "TorqueMultiplier",
            StabilizerBoost.DefaultTorqueMultiplier,
            "Native turret torque multiplier applied while a drive is marked stabilized. " +
            "Higher values make the drive hold and follow the aim harder. Must be finite and in " +
            "[" + StabilizerBoost.MinTorqueMultiplier + ", " + StabilizerBoost.MaxTorqueMultiplier +
            "]; an invalid value falls back to " + StabilizerBoost.DefaultTorqueMultiplier + ".");

        RealisticLimits = Config.Bind(
            "Stabilizer",
            "RealisticLimits",
            true,
            "Default realistic mode. When true (and OverrideRateLimits is false) each stabilized drive is " +
            "clamped to the historical rates below and its acceleration/braking is derived from the response " +
            "time, so a fast mouse swing cannot be followed instantly. Cold War reference: AFV Database M60 " +
            "class 22.5-24 deg/s traverse and 4 deg/s elevation; M60A2 40/10.");

        HorizontalMaxTraverseRate = Config.Bind(
            "Stabilizer",
            "HorizontalMaxTraverseRate",
            StabilizerBoost.DefaultHorizontalMaxTraverseRate,
            "Realistic traverse ceiling in degrees per second for the horizontal turret drive. " +
            "Must be finite and in [" + StabilizerBoost.MinMaxTraverseRate + ", " +
            StabilizerBoost.MaxMaxTraverseRate + "].");

        ElevationMaxTraverseRate = Config.Bind(
            "Stabilizer",
            "ElevationMaxTraverseRate",
            StabilizerBoost.DefaultElevationMaxTraverseRate,
            "Realistic elevation ceiling in degrees per second for the elevation ring or laying drive. " +
            "Must be finite and in [" + StabilizerBoost.MinMaxTraverseRate + ", " +
            StabilizerBoost.MaxMaxTraverseRate + "].");

        ResponseTimeSeconds = Config.Bind(
            "Stabilizer",
            "ResponseTimeSeconds",
            StabilizerBoost.DefaultResponseTimeSeconds,
            "Seconds the drive may take to reach its maximum rate. Acceleration and braking are derived as " +
            "rate / response time (24 deg/s over 0.75 s means 32 deg/s^2). Must be finite and in [" +
            StabilizerBoost.MinResponseTimeSeconds + ", " + StabilizerBoost.MaxResponseTimeSeconds + "].");

        BoostMaxTraverseRate = Config.Bind(
            "Stabilizer",
            "MaxTraverseRate",
            StabilizerBoost.DefaultMaxTraverseRate,
            "Only used when OverrideRateLimits=true. Degrees per second cap requested from the " +
            "behaviour while stabilized. The native limit is never raised: the written value is the " +
            "drive ceiling for that call. Must be finite and in " +
            "[" + StabilizerBoost.MinMaxTraverseRate + ", " + StabilizerBoost.MaxMaxTraverseRate +
            "]; an invalid value falls back to " + StabilizerBoost.DefaultMaxTraverseRate + ".");

        BoostMaxAcceleration = Config.Bind(
            "Stabilizer",
            "MaxAcceleration",
            StabilizerBoost.DefaultMaxAcceleration,
            "Only used when OverrideRateLimits=true. Acceleration limit requested from the behaviour " +
            "while stabilized. Must be finite and in " +
            "[" + StabilizerBoost.MinMaxAcceleration + ", " + StabilizerBoost.MaxMaxAcceleration +
            "]; an invalid value falls back to " + StabilizerBoost.DefaultMaxAcceleration + ".");

        BoostMaxBrakeAcceleration = Config.Bind(
            "Stabilizer",
            "MaxBrakeAcceleration",
            StabilizerBoost.DefaultMaxBrakeAcceleration,
            "Only used when OverrideRateLimits=true. Braking limit requested from the behaviour while " +
            "stabilized. Must be finite and in " +
            "[" + StabilizerBoost.MinMaxBrakeAcceleration + ", " + StabilizerBoost.MaxMaxBrakeAcceleration +
            "]; an invalid value falls back to " + StabilizerBoost.DefaultMaxBrakeAcceleration + ".");

        editor = new Harmony("sprocket.stabilization.editor");
        try { editor.PatchAll(typeof(RuntimeDriveStabilizationEditor)); }
        catch { editor.UnpatchSelf(); throw; }

        // Independent patch groups: a failure in one owner (for example a
        // string-target release hook that no longer binds) must not silently
        // disable the other owners. Only the failing feature is lost.
        stabilizerEditor = new Harmony("sprocket.stabilization.stabilizer.editor");
        try { stabilizerEditor.PatchAll(typeof(StabilizerEditor)); }
        catch (Exception ex)
        {
            stabilizerEditor.UnpatchSelf();
            ModLog.LogError(
                "[Stabilizer] checkbox UI patches unavailable: " +
                ex.GetType().Name + ": " + ex.Message);
        }

        stabilizerRuntime = new Harmony("sprocket.stabilization.stabilizer.runtime");
        try { stabilizerRuntime.PatchAll(typeof(StabilizerRuntime)); }
        catch (Exception ex)
        {
            stabilizerRuntime.UnpatchSelf();
            ModLog.LogError(
                "[Stabilizer] stabilizer boost patches unavailable: " +
                ex.GetType().Name + ": " + ex.Message);
        }

        stabilizerPersistence = new Harmony("sprocket.stabilization.stabilizer.persistence");
        try { stabilizerPersistence.PatchAll(typeof(StabilizerPersistence)); }
        catch (Exception ex)
        {
            stabilizerPersistence.UnpatchSelf();
            ModLog.LogError(
                "[Stabilizer] stabilizer persistence patches unavailable: " +
                ex.GetType().Name + ": " + ex.Message);
        }

        // Separate owner for the blueprint design route. A mis-signature here
        // (as in 0.7.0's Clone postfix) must not disable the boost, editor or
        // legacy component-state owners.
        stabilizerBlueprintPersistence = new Harmony("sprocket.stabilization.stabilizer.blueprint");
        try { stabilizerBlueprintPersistence.PatchAll(typeof(StabilizerBlueprintPersistence)); }
        catch (Exception ex)
        {
            stabilizerBlueprintPersistence.UnpatchSelf();
            ModLog.LogError(
                "[Stabilizer] blueprint persistence patches unavailable: " +
                ex.GetType().Name + ": " + ex.Message);
        }

        Log.LogInfo(
            "Sprocket Stabilization 0.7.3 loaded: per-drive stabilizer boost, " +
            "realistic limits and blueprint persistence.");
    }

    public override bool Unload()
    {
        // Scoped boosts are already restored by the native-call finalizer; drop
        // any remaining runtime state so an unloaded plugin leaves no flags and
        // no per-call nesting guard behind.
        StabilizerRuntime.ResetForUnload();
        StabilizerEditor.ResetForUnload();
        RuntimeDriveStabilizationEditor.ReleaseAll();
        stabilizerBlueprintPersistence?.UnpatchSelf();
        stabilizerPersistence?.UnpatchSelf();
        stabilizerRuntime?.UnpatchSelf();
        stabilizerEditor?.UnpatchSelf();
        editor?.UnpatchSelf();
        StabilizerState.ClearAll();
        StabilizerState.ClearBlueprintFlags();
        return true;
    }
}

internal static class Ui
{
    internal static Il2CppSystem.Action<bool> BoolCallback(Action<bool> action)
        => DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(action)!;
}
