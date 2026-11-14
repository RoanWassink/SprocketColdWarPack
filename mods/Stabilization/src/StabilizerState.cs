using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using UnityEngine;

namespace SprocketStabilization;

internal enum StabilizerAxis
{
    None,
    Elevation,
    Horizontal
}

/// <summary>
/// Identity of one live native vehicle component.
///
/// The VUID text alone is not an identity: two vehicles (or a reloaded design)
/// can carry the same VUID. The native pointers are used instead, with the VUID
/// kept only as an extra discriminator. Pointers are native, so two managed
/// wrappers of the same native object map to the same key. A recycled pointer is
/// still possible; release/unload cleanup limits that exposure but cannot prove
/// universal immunity without a native generation id (the game supplies none).
/// </summary>
internal readonly struct StabilizerKey : IEquatable<StabilizerKey>
{
    internal StabilizerKey(IntPtr vehicle, IntPtr component, string? vuid)
    {
        Vehicle = vehicle;
        Component = component;
        Vuid = vuid ?? string.Empty;
    }

    internal IntPtr Vehicle { get; }

    internal IntPtr Component { get; }

    internal string Vuid { get; }

    internal bool IsValid => Component != IntPtr.Zero;

    public bool Equals(StabilizerKey other)
        => Vehicle == other.Vehicle &&
           Component == other.Component &&
           string.Equals(Vuid, other.Vuid, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StabilizerKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Vehicle, Component, Vuid);

    public override string ToString()
        => "vehicle=" + Vehicle + ";component=" + Component + ";vuid=" + Vuid;
}

/// <summary>
/// Runtime state for the per-drive combat stabilizer:
/// - which axis a drive belongs to (tagged oscillating ring = elevation,
///   ordinary turret ring = horizontal, LayingDrive = elevation),
/// - the editor checkbox value per live component, persisted through the native
///   VehicleComponent save/load path,
/// - dated-technology gating and hull reference helpers.
///
/// The dated Technology is read live on every query; there is deliberately no
/// permanent per-pointer eligibility cache, because an era/date change must take
/// effect immediately.
/// </summary>
internal static class StabilizerState
{
    internal const string ElevationTag = "oscillatingElevationDrive";
    internal const string SaveKey = "sprocketStabilization.axisStabilized";

    /// <summary>
    /// Namespaced string key written into the motor blueprint's object-state
    /// entry. The native outer blueprint ToBlueprint path does not call
    /// VehicleComponent.SaveData, so the checkbox is persisted per blueprint via
    /// IBlueprintSerializer/IBlueprintDeserializer (the path used by other mods
    /// on this game version). Native string entries are the supported shape.
    /// </summary>
    internal const string BlueprintSaveKey = "sprocket.stabilization.v1";

    private static readonly Dictionary<StabilizerKey, bool> Flags = new();

    private sealed record BlueprintFlagEntry(Il2CppObjectBase Blueprint, bool Stabilized);

    // Keyed by the live blueprint pointer. The wrapper is retained deliberately:
    // pointer-keyed weak wrappers can vanish while the game still owns the
    // blueprint. Entries are overwritten on load, copied on structured
    // duplication, and dropped on unload.
    private static readonly Dictionary<IntPtr, BlueprintFlagEntry> BlueprintFlags = new();

    internal static bool MasterEnabled => Plugin.StabilizerEnabled is null || Plugin.StabilizerEnabled.Value;

    internal static int TrackedFlagCount => Flags.Count;

    internal static StabilizerKey KeyOf(VehicleComponent? component)
    {
        if (component == null) return default;
        try
        {
            IntPtr componentPointer = component.Pointer;
            IntPtr vehiclePointer = IntPtr.Zero;
            try
            {
                var vehicle = component.Vehicle;
                if (vehicle != null) vehiclePointer = vehicle.Pointer;
            }
            catch
            {
                // Identity falls back to the component pointer only.
            }

            string vuid = string.Empty;
            try { vuid = component.VUID.Value.ToString() ?? string.Empty; }
            catch { }

            return new StabilizerKey(vehiclePointer, componentPointer, vuid);
        }
        catch
        {
            return default;
        }
    }

    internal static bool GetFlag(StabilizerKey key)
    {
        // Absent state means false: a save record without the key must never
        // inherit an older true value.
        return key.IsValid && Flags.TryGetValue(key, out bool value) && value;
    }

    internal static void SetFlag(StabilizerKey key, bool value)
    {
        if (!key.IsValid) return;
        Flags[key] = value;
    }

    /// <summary>
    /// Removes every stored value for this exact live component (all VUID or
    /// vehicle-wrapper variants of the same component pointer). Unrelated
    /// components have different native pointers and are untouched.
    /// </summary>
    internal static bool ClearComponent(StabilizerKey key)
    {
        if (key.Component == IntPtr.Zero) return false;

        List<StabilizerKey>? stale = null;
        foreach (StabilizerKey candidate in Flags.Keys)
        {
            if (candidate.Component == key.Component)
            {
                (stale ??= new List<StabilizerKey>()).Add(candidate);
            }
        }

        if (stale == null) return false;
        foreach (StabilizerKey candidate in stale) Flags.Remove(candidate);
        return true;
    }

    internal static void ClearAll() => Flags.Clear();

    internal static int TrackedBlueprintFlagCount => BlueprintFlags.Count;

    /// <summary>The motor blueprint that owns this drive's persisted checkbox.</summary>
    internal static Il2CppObjectBase? BlueprintOf(TraverseMotor? motor)
    {
        if (motor == null) return null;
        try { return motor.MotorBlueprintSlot?.Blueprint; }
        catch { return null; }
    }

    internal static bool GetBlueprintFlag(Il2CppObjectBase? blueprint)
    {
        if (blueprint == null) return false;
        try
        {
            return BlueprintFlags.TryGetValue(blueprint.Pointer, out BlueprintFlagEntry? entry) &&
                   entry.Stabilized;
        }
        catch
        {
            return false;
        }
    }

    internal static void SetBlueprintFlag(Il2CppObjectBase? blueprint, bool value)
    {
        if (blueprint == null) return;
        try
        {
            IntPtr pointer = blueprint.Pointer;
            if (pointer == IntPtr.Zero) return;
            if (!value)
            {
                BlueprintFlags.Remove(pointer);
                return;
            }

            if (BlueprintFlags.Count > 8192) BlueprintFlags.Clear();
            BlueprintFlags[pointer] = new BlueprintFlagEntry(blueprint, true);
        }
        catch
        {
            // A stale blueprint wrapper must never break the editor callback.
        }
    }

    internal static void CopyBlueprintFlag(Il2CppObjectBase? source, Il2CppObjectBase? target)
    {
        if (source == null || target == null) return;
        try { SetBlueprintFlag(target, GetBlueprintFlag(source)); }
        catch { }
    }

    internal static bool ClearBlueprintFlag(Il2CppObjectBase? blueprint)
    {
        if (blueprint == null) return false;
        try { return BlueprintFlags.Remove(blueprint.Pointer); }
        catch { return false; }
    }

    internal static void ClearBlueprintFlags() => BlueprintFlags.Clear();

    internal static StabilizerAxis AxisOf(TurretRing? ring)
    {
        if (ring == null) return StabilizerAxis.None;
        try
        {
            var vehicleObject = ring.VehicleObject;
            return vehicleObject != null && vehicleObject.HasTag(ElevationTag)
                ? StabilizerAxis.Elevation
                : StabilizerAxis.Horizontal;
        }
        catch
        {
            return StabilizerAxis.None;
        }
    }

    internal static bool TechEnabled(VehicleComponent? component)
    {
        if (component == null) return false;

        // Always consult the current dated technology inputs. Caching the first
        // answer per pointer made an era/date change invisible.
        try
        {
            var tech = component.Vehicle != null ? component.Vehicle.Tech : null;
            return StabilizationTechnology.TryRead(tech, out _);
        }
        catch
        {
            return false;
        }
    }

    internal static T? FindInAncestry<T>(Transform? start) where T : Il2CppObjectBase
    {
        var current = start;
        for (int depth = 0; depth < 10 && current != null; depth++)
        {
            try
            {
                var component = current.GetComponent(Il2CppType.Of<T>());
                if (component != null)
                {
                    var found = component.TryCast<T>();
                    if (found != null) return found;
                }
            }
            catch
            {
                // Stale wrapper during a rebuild; try the next ancestor.
            }

            try { current = current.parent; }
            catch { break; }
        }

        return null;
    }

    internal static Quaternion HullWorldRotation(VehicleComponent component)
    {
        try
        {
            var root = component.VehicleRoot;
            var vehicleTransform = root != null ? root.VehicleTransform : null;
            if (vehicleTransform != null) return vehicleTransform.Rotation;
        }
        catch
        {
            // Fall through to the scene-root transform below.
        }

        try { return component.transform.root.rotation; }
        catch { return Quaternion.identity; }
    }
}
