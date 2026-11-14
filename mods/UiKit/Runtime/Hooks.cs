using System.Reflection;
using HarmonyLib;
using Sprocket;
using Sprocket.Inputsystem;
using Sprocket.VehicleControl;
namespace SprocketUiKit;

// Same proven targets and patterns as Telescopic Mast 0.4.1 (user-confirmed in game, 2026-10-31):
// class-only/no-struct Harmony parameters; the input block uses the managed __result out value.

/// <summary>Designer click-through guard: report the pointer as over GUI while it is over a kit panel.</summary>
[HarmonyPatch]
internal static class PointerHooks
{
    [HarmonyPostfix, HarmonyPatch(typeof(Inputs), nameof(Inputs.Update))]
    private static void AfterInputs()
    {
        try
        {
            if (!Kit.PointerCaptured()) return;
            Inputs.pointerOverGUI = true;
            Inputs.pointer0 = Mark(Inputs.pointer0);
            Inputs.pointer1 = Mark(Inputs.pointer1);
            Inputs.pointer2 = Mark(Inputs.pointer2);
        }
        catch (Exception ex) { Guard.Report("Designer pointer guard", ex); }
    }
    private static ButtonInfo Mark(ButtonInfo b) =>
        b.WasPressedThisFrame || b.Held ? new ButtonInfo(b.WasPressedThisFrame, b.WasReleasedThisFrame, b.Held, true) : b;
}

/// <summary>Esc that the kit used (panel, popup, text field) does not also open the pause menu.</summary>
[HarmonyPatch]
internal static class PauseHooks
{
    private static int traces;
    [HarmonyPrefix, HarmonyPatch(typeof(GameTime), nameof(GameTime.RequestPause))]
    private static bool BeforePause()
    {
        try
        {
            bool block = Kit.EscapeBelongsToKit();
            if (Plugin.Detailed && traces++ < 40) Plugin.Trace($"[UI Kit] Pause requested (frame {UnityEngine.Time.frameCount}, kit Esc frame {Kit.EscConsumedFrame}): {(block ? "blocked, Esc belonged to a kit panel" : "allowed")}.");
            return !block;
        }
        catch (Exception ex) { Guard.Report("Pause guard", ex); return true; }
    }
    /// <summary>0.1.2: remember the GameTime instance; Kit reads its PauseState to recognise the native pause menu.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(GameTime), nameof(GameTime.RequestPause))]
    private static void AfterPause(GameTime __instance)
    {
        try { Kit.GameTimeRef = __instance; }
        catch (Exception ex) { Guard.Report("Pause state hook", ex); }
    }
}

/// <summary>
/// In game, while a blocking panel is open: mouse buttons do not fire/scope/order and mouse motion
/// does not turn the camera. Each of the 9 targets is patched separately and may be skipped by
/// config (InputBlock.SkipTargets); a missing target only loses that one block.
/// </summary>
internal static class InputBlock
{
    internal static readonly string[] VehicleButtons = { "Fire", "Scope", "LockAim", "CommandTeam", "CommandTarget", "CancelOrder", "ShowDetailedInfo" };
    internal static int Applied, Expected = VehicleButtons.Length + 2;
    internal static readonly List<string> Missing = new();

    internal static void Apply(Harmony harmony, ISet<string> skip)
    {
        var button = new HarmonyMethod(typeof(InputBlock).GetMethod(nameof(BlockButton), BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var name in VehicleButtons) TryPatch(harmony, typeof(VehicleInputSystemInputs), name, button, skip);
        var camera = new HarmonyMethod(typeof(InputBlock).GetMethod(nameof(BlockCamera), BindingFlags.Static | BindingFlags.NonPublic));
        TryPatch(harmony, typeof(OrbitalMovementController), "ProcessInputs", camera, skip);
        TryPatch(harmony, typeof(ScopeCameraController), "ProcessInputs", camera, skip);
    }
    private static void TryPatch(Harmony harmony, Type type, string name, HarmonyMethod prefix, ISet<string> skip)
    {
        string id = type.Name + "." + name;
        if (skip.Contains(id) || skip.Contains(name)) { Missing.Add(id + " (skipped by config)"); return; }
        try
        {
            var target = AccessTools.GetDeclaredMethods(type).SingleOrDefault(m => m.Name == name);
            if (target == null) { Missing.Add(id); Plugin.Instance.Log.LogWarning($"[UI Kit] Input block: {id} not found."); return; }
            harmony.Patch(target, prefix: prefix);
            Applied++;
        }
        catch (Exception ex) { Missing.Add(id); Plugin.Instance.Log.LogWarning($"[UI Kit] Input block: {id} unavailable: {ex.Message}"); }
    }
    private static bool BlockButton(ref bool __result)
    {
        if (!Kit.OwnsGameplayInput) return true;
        __result = false; return false;
    }
    private static bool BlockCamera(ref CameraInputProccessingFlags __result)
    {
        if (!Kit.OwnsGameplayInput) return true;
        __result = default; return false;
    }
}
