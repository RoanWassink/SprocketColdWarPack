using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.UI;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace SprocketOscillatingTurret;

/// <summary>
/// 0.2.11 X-ray diagnosis. One line per overlay switch and one line per
/// unclaimed Apply correction; never per frame. Behaviour is unchanged except
/// for the guarded native-axis restore in Patches.ApplyPrefix.
/// </summary>
internal static class Diagnostics
{
    private static ConfigEntry<bool>? _detailed;
    private static readonly Dictionary<IntPtr, PendingApply> Pending = new();
    private static readonly HashSet<string> SeenApply = new();
    private static readonly HashSet<string> SeenReturn = new();
    private static string _before = "";
    private static bool _activeBefore;
    private static bool _captured;

    internal static bool Detailed => _detailed != null && _detailed.Value;
    internal static bool XrayActive { get; private set; }

    internal static void Load(ConfigFile config)
    {
        _detailed = config.Bind(
            "Diagnostics",
            "DetailedLogging",
            false,
            "Write one X-ray/pose line per overlay switch or correction. No per-frame spam.");
        Plugin.Info("[Xray diag] DetailedLogging=" + Detailed);
    }

    internal static void Install(Harmony harmony)
    {
        try
        {
            var toggle = AccessTools.Method(typeof(VehicleBattleOverlayController), nameof(VehicleBattleOverlayController.ToggleOverlay));
            var setActive = AccessTools.Method(typeof(VehicleBattleOverlayController), nameof(VehicleBattleOverlayController.SetOverlayActive));
            if (toggle == null || setActive == null)
            {
                Plugin.Warn("[Xray diag] overlay hooks unavailable; X-ray state will stay unknown.");
                return;
            }

            harmony.Patch(toggle,
                prefix: new HarmonyMethod(typeof(Diagnostics), nameof(TogglePrefix)),
                postfix: new HarmonyMethod(typeof(Diagnostics), nameof(TogglePostfix)));
            harmony.Patch(setActive,
                prefix: new HarmonyMethod(typeof(Diagnostics), nameof(SetActivePrefix)),
                postfix: new HarmonyMethod(typeof(Diagnostics), nameof(SetActivePostfix)));
            Plugin.Info("[Xray diag] Overlay hooks installed (ToggleOverlay, SetOverlayActive).");
        }
        catch (Exception ex)
        {
            Plugin.Warn("[Xray diag] overlay hook failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void TogglePrefix() => Capture();
    private static void SetActivePrefix() => Capture();

    private static void TogglePostfix(VehicleBattleOverlayController __instance)
    {
        XrayActive = __instance != null && __instance.OverlayActive;
        LogSwitch("toggle");
    }

    private static void SetActivePostfix(VehicleBattleOverlayController __instance, bool __0)
    {
        XrayActive = __instance != null && __instance.OverlayActive;
        LogSwitch("setActive(" + __0 + ")");
    }

    private static void Capture()
    {
        if (!Detailed) return;
        _captured = true;
        _activeBefore = XrayActive;
        _before = Snapshot();
    }

    private static void LogSwitch(string tag)
    {
        if (!Detailed) return;
        string before = _captured ? _before : "(not captured)";
        string after = Snapshot();
        Plugin.Info(
            "[Xray diag] " + tag + " active=" + _activeBefore + "->" + XrayActive +
            " entries=" + CountEntries() + " before=" + before + " after=" + after);
        _captured = false;
    }

    private static int CountEntries()
    {
        try { return ElevationRegistry.SnapshotForVisualTuning().Count; }
        catch { return -1; }
    }

    private static string Snapshot()
    {
        try
        {
            List<OwnedTurret> entries = ElevationRegistry.SnapshotForVisualTuning();
            if (entries.Count == 0) return "(none)";
            var parts = new List<string>(entries.Count);
            foreach (OwnedTurret entry in entries)
            {
                try
                {
                    string ring = RingId(entry);
                    var behaviour = BehaviourFor(entry);
                    string local = "?";
                    string rest = "?";
                    string axis = "?";
                    string upY = "?";
                    string model = "?";
                    if (behaviour != null)
                    {
                        var transform = behaviour.transform;
                        if (transform != null)
                        {
                            local = Q(transform.localRotation);
                            upY = F(transform.up.y);
                        }
                        rest = Q(behaviour.restRotation);
                        axis = V(behaviour.axis);
                    }

                    TurretRing? ringRef = entry.RingRef;
                    var modelRef = ringRef?.ringModel;
                    if (modelRef != null && modelRef.HasModel)
                    {
                        model = Q(modelRef.ModelLocalRotation);
                    }

                    parts.Add("ring=" + ring + " local=" + local + " upY=" + upY +
                              " rest=" + rest + " axis=" + axis + " model=" + model);
                }
                catch (Exception ex)
                {
                    parts.Add("ring=? snapshot-error=" + ex.GetType().Name);
                }
            }

            return string.Join("; ", parts);
        }
        catch (Exception ex)
        {
            return "snapshot-error=" + ex.GetType().Name;
        }
    }

    internal static void ApplyUnclaimedPrefix(TurretBehaviour behaviour, bool nativeAxisReset, Func<string> ringId)
    {
        if (!Detailed || behaviour == null) return;

        UnityEngine.Transform? transform;
        try { transform = behaviour.transform; }
        catch { return; }
        IntPtr transformPointer = transform != null ? transform.Pointer : IntPtr.Zero;
        string key = behaviour.Pointer.ToInt64().ToString("X") + ":" +
                     transformPointer.ToInt64().ToString("X") + ":" +
                     (XrayActive ? "1" : "0") + ":" +
                     (nativeAxisReset ? "1" : "0");

        if (!SeenApply.Add(key)) return;

        string ring = "?";
        try { ring = ringId(); } catch { }

        Pending[behaviour.Pointer] = new PendingApply
        {
            Ring = ring,
            NativeAxisReset = nativeAxisReset,
            Before = Pose(behaviour)
        };
    }

    internal static void ApplyUnclaimedPostfix(TurretBehaviour behaviour)
    {
        if (!Detailed || behaviour == null) return;
        if (!Pending.Remove(behaviour.Pointer, out PendingApply? pending)) return;

        Plugin.Info(
            "[Xray diag] apply ring=" + pending.Ring + " xray=" + XrayActive +
            " owner=none nativeAxisReset=" + pending.NativeAxisReset +
            " before=" + pending.Before + " after=" + Pose(behaviour));
    }

    internal static void ReturnToNative(string reason, IntPtr entryPointer, string ring, Vector3 before, Vector3 after)
    {
        if (!Detailed) return;
        string key = entryPointer.ToInt64().ToString("X") + ":" + reason;
        if (!SeenReturn.Add(key)) return;
        Plugin.Info("[Xray diag] returnToNative ring=" + ring + " reason=" + reason +
                    " axis=" + V(before) + "->" + V(after));
    }

    internal static void VisualRoll(string ring, Quaternion before, Quaternion after)
    {
        if (!Detailed) return;
        Plugin.Info("[Xray diag] visualRoll ring=" + ring + " xray=" + XrayActive +
                    " model=" + Q(before) + "->" + Q(after));
    }

    internal static void Shutdown()
    {
        Pending.Clear();
        SeenApply.Clear();
        SeenReturn.Clear();
        _captured = false;
        _before = "";
    }

    internal static string RingId(OwnedTurret entry)
    {
        try
        {
            TurretRing? ring = entry.RingRef;
            if (ring != null && !ring.WasCollected)
            {
                return ring.VUID.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return "?";
    }

    internal static TurretRing? FindTaggedRing(TurretBehaviour behaviour)
    {
        try { return ElevationRegistry.FindTaggedRingForDiagnostics(behaviour); }
        catch { return null; }
    }

    private static TurretBehaviour? BehaviourFor(OwnedTurret entry)
    {
        try
        {
            TurretRing? ring = entry.RingRef;
            var motor = ring?.motor;
            if (motor?.Behaviour is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase wrapper)
            {
                return wrapper.TryCast<TurretBehaviour>();
            }
        }
        catch { }
        return null;
    }

    private static string Pose(TurretBehaviour behaviour)
    {
        try
        {
            var transform = behaviour.transform;
            if (transform == null) return "local=? rest=? axis=? upY=?";
            return "local=" + Q(transform.localRotation) +
                   " upY=" + F(transform.up.y) +
                   " rest=" + Q(behaviour.restRotation) +
                   " axis=" + V(behaviour.axis);
        }
        catch (Exception ex)
        {
            return "pose-error=" + ex.GetType().Name;
        }
    }

    private static string Q(Quaternion value) =>
        FormattableString.Invariant($"({value.x:F3},{value.y:F3},{value.z:F3},{value.w:F3})");

    private static string V(Vector3 value) =>
        FormattableString.Invariant($"({value.x:F3},{value.y:F3},{value.z:F3})");

    private static string F(float value) =>
        FormattableString.Invariant($"{value:F3}");

    private sealed class PendingApply
    {
        public string Ring = "?";
        public bool NativeAxisReset;
        public string Before = "";
    }
}
