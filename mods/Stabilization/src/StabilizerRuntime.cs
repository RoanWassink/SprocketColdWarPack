using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;

namespace SprocketStabilization;

/// <summary>
/// Everything the stabilizer changed for one native MoveToTarget invocation.
/// The object lives only in Harmony's per-call __state; it is never stored in a
/// long-lived behaviour-pointer dictionary.
/// </summary>
internal sealed class BoostScope
{
    internal IntPtr Pointer;
    internal bool TracksCall;
    internal bool CallExited;
    internal bool Boosted;
    internal bool Restored;
    internal float BaseTorqueMultiplier;
    internal float BaseMaxTraverseRate;
    internal float BaseMaxAcceleration;
    internal float BaseMaxBrakeAcceleration;
    internal BoostSettings Applied;
    internal float AppliedMaxTraverseRate;
    internal float AppliedMaxAcceleration;
    internal float AppliedMaxBrakeAcceleration;
}

/// <summary>
/// In-game stabilizer approximation: per drive an auto torque boost.
///
/// The native controllers already track a world-space target direction; the
/// limit is how fast the drive can follow it while the hull moves. The most
/// faithful approximation without rewriting the aim target is therefore to
/// raise the drive's available torque (and, where the behaviour accepts it, its
/// rate/acceleration limits) for that axis only. The native controller, crew,
/// mass and aiming logic stay authoritative.
///
/// Lifetime: the Harmony prefix captures the current four values immediately
/// before the native call, the native method observes the boosted values, and
/// the postfix/finalizer restores them as soon as that same call returns. A
/// later native or third-party reconfiguration is therefore never overwritten by
/// an old snapshot.
/// </summary>
[HarmonyPatch]
internal static class StabilizerRuntime
{
    // Per-thread, per-call nesting guard. Entries exist only while a native
    // MoveToTarget call is on the stack; the finalizer removes them.
    [ThreadStatic]
    private static Dictionary<IntPtr, bool>? InFlight;

    private static int _warnings;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
    private static void BeforeMoveToTarget(TurretBehaviour __instance, out BoostScope? __state)
    {
        __state = null;
        if (__instance == null) return;

        BoostScope scope = new BoostScope();
        try
        {
            scope.Pointer = __instance.Pointer;
        }
        catch (Exception ex)
        {
            Warn("move", ex);
            return;
        }

        // Publish the scope before doing anything else so the finalizer can
        // clean up even a partially applied prefix.
        __state = scope;

        try
        {
            if (!TryEnter(scope.Pointer))
            {
                // A nested native call on this thread already owns the boost;
                // do not capture the boosted values as a new baseline.
                scope.Restored = true;
                return;
            }

            scope.TracksCall = true;

            if (!TryResolve(__instance, out StabilizerAxis axis))
            {
                scope.Restored = true;
                return;
            }

            if (!TryCapture(
                    __instance,
                    out float torque,
                    out float rate,
                    out float acceleration,
                    out float brake))
            {
                scope.Restored = true;
                return;
            }

            scope.BaseTorqueMultiplier = torque;
            scope.BaseMaxTraverseRate = rate;
            scope.BaseMaxAcceleration = acceleration;
            scope.BaseMaxBrakeAcceleration = brake;

            BoostSettings configured = CurrentSettings();
            scope.Applied = configured;

            bool explicitLimits = Plugin.OverrideRateLimits?.Value == true;
            bool realisticLimits = !explicitLimits && Plugin.RealisticLimits?.Value == true;

            float appliedRate = 0f;
            float appliedAcceleration = 0f;
            float appliedBrake = 0f;
            if (explicitLimits)
            {
                appliedRate = configured.MaxTraverseRate;
                appliedAcceleration = configured.MaxAcceleration;
                appliedBrake = configured.MaxBrakeAcceleration;
            }
            else if (realisticLimits)
            {
                RealisticLimits limits = StabilizerBoost.RealisticFromConfig(
                    Plugin.HorizontalMaxTraverseRate?.Value,
                    Plugin.ElevationMaxTraverseRate?.Value,
                    Plugin.ResponseTimeSeconds?.Value);
                bool elevation = axis == StabilizerAxis.Elevation;
                float capRate = limits.RateFor(elevation);
                float capAcceleration = limits.AccelerationFor(elevation);

                // Never raise a native limit above the historical ceiling, and
                // never speed up a drive that the native gearing already limits
                // more tightly; the ceiling only applies when the native value is
                // missing or unusable. The result: a drive at most as fast as the
                // real stabilizer-class reference, usually exactly as slow as the
                // native design.
                appliedRate = rate > 0f ? Math.Min(rate, capRate) : capRate;
                appliedAcceleration = acceleration > 0f ? Math.Min(acceleration, capAcceleration) : capAcceleration;
                appliedBrake = brake > 0f ? Math.Min(brake, capAcceleration) : capAcceleration;
            }

            // Mark the scope as boosted before the first write: a setter that
            // throws halfway must still be restored by the finalizer.
            scope.AppliedMaxTraverseRate = appliedRate;
            scope.AppliedMaxAcceleration = appliedAcceleration;
            scope.AppliedMaxBrakeAcceleration = appliedBrake;
            scope.Boosted = true;
            __instance.MotorTorqueMultiplier = configured.TorqueMultiplier;

            if (explicitLimits || realisticLimits)
            {
                __instance.MaxTraverseRate = appliedRate;
                __instance.MaxAcceleration = appliedAcceleration;
                __instance.MaxBrakeAcceleration = appliedBrake;
            }
        }
        catch (Exception ex)
        {
            // Do not rethrow: the original native call and its exception must
            // stay untouched. The finalizer restores any fields already written.
            Warn("move", ex);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
    private static void AfterMoveToTarget(TurretBehaviour __instance, BoostScope? __state)
    {
        Restore(__instance, __state);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
    private static Exception? Finalizer(
        TurretBehaviour __instance,
        BoostScope? __state,
        Exception? __exception)
    {
        // Harmony reserves the injection name "__exception" for the exception
        // thrown by the original method; a different name (for example "__ex")
        // is not injectable and makes the actual runtime reject the patch.
        // Restore happens here too, so an exception in the native call (or a
        // skipped postfix) still cleans up. Returning __exception unchanged
        // preserves the original native exception instead of swallowing it.
        Restore(__instance, __state);
        return __exception;
    }

    private static bool TryResolve(TurretBehaviour behaviour, out StabilizerAxis axis)
    {        axis = StabilizerAxis.None;
        TurretRing? ring = StabilizerState.FindInAncestry<TurretRing>(behaviour.transform);
        TraverseMotor? motor = null;
        try { motor = ring != null ? ring.motor : null; }
        catch { }

        if (motor == null) return false;

        // Mirror the editor's association requirement: the motor must be
        // connected and the ring/motor must belong to the same live vehicle, so
        // a shared or cross-vehicle ancestor ring cannot gate an unrelated drive.
        try
        {
            if (!motor.Connected) return false;
            var ringObject = ring != null ? ring.VehicleObject : null;
            if (ringObject == null || ringObject.Released) return false;
            var motorVehicle = motor.Vehicle;
            var ringVehicle = ring != null ? ring.Vehicle : null;
            if (motorVehicle == null || ringVehicle == null) return false;
            if (motorVehicle.Pointer != ringVehicle.Pointer) return false;
        }
        catch
        {
            return false;
        }

        if (!StabilizerMath.AcceptsEdit(
                StabilizerState.MasterEnabled,
                StabilizerState.TechEnabled(motor)))
        {
            return false;
        }

        Il2CppObjectBase? blueprint = StabilizerState.BlueprintOf(motor);
        if (blueprint == null) return false;
        if (!StabilizerState.GetBlueprintFlag(blueprint)) return false;

        axis = StabilizerState.AxisOf(ring);
        if (axis == StabilizerAxis.None) axis = StabilizerAxis.Horizontal;
        return true;
    }

    private static BoostSettings CurrentSettings()
        => StabilizerBoost.FromConfig(
            Plugin.BoostTorqueMultiplier?.Value,
            Plugin.BoostMaxTraverseRate?.Value,
            Plugin.BoostMaxAcceleration?.Value,
            Plugin.BoostMaxBrakeAcceleration?.Value);

    private static bool TryCapture(
        TurretBehaviour behaviour,
        out float torque,
        out float rate,
        out float acceleration,
        out float brake)
    {
        torque = 0f;
        rate = 0f;
        acceleration = 0f;
        brake = 0f;
        try
        {
            torque = behaviour.MotorTorqueMultiplier;
            rate = behaviour.MaxTraverseRate;
            acceleration = behaviour.MaxAcceleration;
            brake = behaviour.MaxBrakeAcceleration;
            return true;
        }
        catch
        {
            // No complete baseline: never write a partial boost.
            return false;
        }
    }

    private static void Restore(TurretBehaviour? behaviour, BoostScope? scope)
    {
        if (scope == null) return;

        try
        {
            if (!scope.Restored)
            {
                scope.Restored = true;
                if (scope.Boosted && behaviour != null)
                {
                    RestoreFields(behaviour, scope);
                }
            }
        }
        catch (Exception ex)
        {
            Warn("restore", ex);
        }
        finally
        {
            if (scope.TracksCall && !scope.CallExited)
            {
                scope.CallExited = true;
                TryExit(scope.Pointer);
            }
        }
    }

    private static void RestoreFields(TurretBehaviour behaviour, BoostScope scope)
    {
        // Per-field guard: only write the snapshot back while the field still
        // holds the value this call wrote. If another patch/native path changed
        // it during the call, that newer value is left alone.
        RestoreTorque(behaviour, scope.Applied.TorqueMultiplier, scope.BaseTorqueMultiplier);
        RestoreRate(behaviour, scope.AppliedMaxTraverseRate, scope.BaseMaxTraverseRate);
        RestoreAcceleration(behaviour, scope.AppliedMaxAcceleration, scope.BaseMaxAcceleration);
        RestoreBrake(behaviour, scope.AppliedMaxBrakeAcceleration, scope.BaseMaxBrakeAcceleration);
    }

    private static void RestoreTorque(TurretBehaviour behaviour, float applied, float original)
    {
        try
        {
            if (behaviour.MotorTorqueMultiplier == applied)
            {
                behaviour.MotorTorqueMultiplier = original;
            }
        }
        catch
        {
            // Behaviour may already be disposed; nothing to restore.
        }
    }

    private static void RestoreRate(TurretBehaviour behaviour, float applied, float original)
    {
        try
        {
            if (behaviour.MaxTraverseRate == applied)
            {
                behaviour.MaxTraverseRate = original;
            }
        }
        catch
        {
        }
    }

    private static void RestoreAcceleration(TurretBehaviour behaviour, float applied, float original)
    {
        try
        {
            if (behaviour.MaxAcceleration == applied)
            {
                behaviour.MaxAcceleration = original;
            }
        }
        catch
        {
        }
    }

    private static void RestoreBrake(TurretBehaviour behaviour, float applied, float original)
    {
        try
        {
            if (behaviour.MaxBrakeAcceleration == applied)
            {
                behaviour.MaxBrakeAcceleration = original;
            }
        }
        catch
        {
        }
    }

    private static bool TryEnter(IntPtr pointer)
    {
        var inFlight = InFlight ??= new Dictionary<IntPtr, bool>();
        if (inFlight.ContainsKey(pointer)) return false;
        inFlight[pointer] = true;
        return true;
    }

    private static void TryExit(IntPtr pointer)
    {
        try { InFlight?.Remove(pointer); }
        catch { }
    }

    /// <summary>
    /// Called from Plugin.Unload. Scopes are per call and normally already gone;
    /// this only drops the current thread's nesting guard so an unloaded plugin
    /// cannot leave a blocker behind.
    /// </summary>
    internal static void ResetForUnload()
    {
        try { InFlight?.Clear(); }
        catch { }
        InFlight = null;
    }

    private static void Warn(string area, Exception ex)
    {
        if (_warnings >= 20) return;
        _warnings++;
        Plugin.ModLog.LogWarning("[Stabilizer] " + area + ": " + ex.GetType().Name + ": " + ex.Message);
    }
}
