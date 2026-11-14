using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using UQuaternion = UnityEngine.Quaternion;
using UVector3 = UnityEngine.Vector3;

namespace SprocketOscillatingTurret;

internal static class PluginInfo
{
    public const string Guid = "sprocket.oscillatingturret";
    public const string Name = "Sprocket Oscillating Turret";
    public const string Version = "0.3.1";
    public const string ElevationTag = "oscillatingElevationDrive";
}

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public sealed class Plugin : BasePlugin
{
    internal static Plugin? Instance;

    internal static ConfigEntry<bool>? EnabledSetting;

    internal static ConfigEntry<bool>? VisualRotateSetting;

    internal static ConfigEntry<float>? VisualRollSetting;

    private Harmony? _harmony;
    private Harmony? _visualHarmony;

    public override void Load()
    {
        Instance = this;

        EnabledSetting = Config.Bind(
            "General",
            "Enabled",
            true,
            "Install the Oscillating Turret runtime patches. Set false before startup to skip all patches.");

        VisualRotateSetting = Config.Bind(
            "Visual",
            "RotateRingModel",
            true,
            "Roll the visible turret-ring model so its hole faces the elevation axis. Visual only: " +
            "functional transform, mirroring, attachments, mass and cost are untouched.");

        VisualRollSetting = Config.Bind(
            "Visual",
            "RingModelRollDegrees",
            90f,
            "Roll of the visible ring model around the part's local forward axis. 0 leaves the model untouched.");

        if (!EnabledSetting.Value)
        {
            Log.LogInfo("Oscillating Turret patches disabled by configuration.");
            return;
        }
        PivotHousingVisuals.Enabled=Config.Bind("Visual","CompactPivotHousing",true,
            "Use an original horizontal enclosed pivot and actuator housing instead of the exposed elevation ring and visible basket. Native motor, axis, collision clearance, mass and cost remain. Restart to change.").Value;
        Diagnostics.Load(Config);

        _harmony = new Harmony(PluginInfo.Guid);
        var failures = new List<string>();
        PatchInstaller.Install(_harmony, failures);
        try { OscillatingSelector.SelectorIntegration.Install(_harmony, Log); }
        catch (Exception ex) { failures.Add("Turret selector: " + ex.Message); }

        if (failures.Count > 0)
        {
            foreach (string failure in failures)
            {
                Log.LogError("Oscillating Turret patch failure: " + failure);
            }

            _harmony.UnpatchSelf();
            _harmony = null;
            Log.LogError("Oscillating Turret did not activate because required patches failed.");
            return;
        }

        Log.LogInfo(
            "Sprocket Oscillating Turret " + PluginInfo.Version +
            " loaded: local -X elevation axis, X-ray native-axis guard and visual ring-model maintenance.");
        Diagnostics.Install(_harmony);
        try
        {
            HeavyTrunnion.Load(Config);
            HeavyTrunnion.Install();
            UnityEngine.Application.add_onBeforeRender(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)HeavyTrunnionVisuals.Frame));
        }
        catch (Exception ex) { HeavyTrunnion.Uninstall(); Log.LogWarning("[Heavy trunnion] Heavy Trunnion Mount unavailable: " + ex.Message); }
        try
        {
            _visualHarmony=new Harmony(PivotHousingVisuals.Owner);
            _visualHarmony.PatchAll(typeof(PivotHousingVisuals));
            UnityEngine.Application.add_onBeforeRender(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)PivotHousingVisuals.Frame));
            Log.LogInfo("[Pivot housing] Horizontal enclosed pivot and actuator model enabled="+PivotHousingVisuals.Enabled+"; native elevation and motor retained.");
        }
        catch(Exception ex){_visualHarmony?.UnpatchSelf();_visualHarmony=null;Log.LogWarning("[Pivot housing] Optional basket visibility hook unavailable: "+ex.Message);}

        try
        {
            AddComponent<VisualTuningDriver>();
        }
        catch (Exception ex)
        {
            Log.LogWarning(
                "Oscillating Turret visual ring-model maintenance could not start: " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    public override bool Unload()
    {
        _visualHarmony?.UnpatchSelf();_visualHarmony=null;
        PivotHousingVisuals.Clear();
        HeavyTrunnion.Uninstall();
        Diagnostics.Shutdown();
        _harmony?.UnpatchSelf();
        _harmony = null;
        ElevationRegistry.Clear();
        OscillatingSelector.SelectorIntegration.Clear();
        Instance = null;
        return true;
    }

    internal static void Info(string message)
    {
        Instance?.Log.LogInfo(message);
    }

    internal static void Warn(string message)
    {
        Instance?.Log.LogWarning(message);
    }

    internal static void Error(string message)
    {
        Instance?.Log.LogError(message);
    }


}

internal static class PatchInstaller
{
    public static void Install(Harmony harmony, List<string> failures)
    {
        // Release() is an ICF-folded empty stub. Its unique lifecycle caller runs
        // before virtual Release and resource disposal; keep this cleanup a prefix.
        Patch(harmony, typeof(VehicleComponent), "ReleaseInternal", Type.EmptyTypes,
            nameof(Patches.ComponentReleasePrefix), null, failures);
        Patch(
            harmony,
            typeof(TurretRing),
            "CreateNewBlueprint",
            null,
            null,
            nameof(Patches.CreateNewBlueprintPostfix),
            failures);

        Patch(
            harmony,
            typeof(TurretRing),
            "InitiateMotor",
            new[] { typeof(TraverseMotor) },
            nameof(Patches.InitiateMotorPrefix),
            nameof(Patches.InitiateMotorPostfix),
            failures);

        Patch(
            harmony,
            typeof(TurretRing),
            "EnableBehaviour",
            null,
            nameof(Patches.RingEnableBehaviourPrefix),
            null,
            failures);

        Patch(
            harmony,
            typeof(TraverseMotor),
            "EnableBehaviour",
            null,
            nameof(Patches.MotorEnableBehaviourPrefix),
            nameof(Patches.MotorEnableBehaviourPostfix),
            failures);

        Patch(
            harmony,
            typeof(TraverseMotor),
            "DisableBehaviour",
            null,
            nameof(Patches.MotorDisableBehaviourPrefix),
            null,
            failures);

        Patch(
            harmony,
            typeof(TurretBehaviour),
            "MoveToTarget",
            new[] { typeof(float), typeof(float), typeof(AimFlags) },
            nameof(Patches.MoveToTargetPrefix),
            nameof(Patches.MoveToTargetPostfix),
            failures);

        Patch(
            harmony,
            typeof(TurretBehaviour),
            "Apply",
            null,
            nameof(Patches.ApplyPrefix),
            nameof(Patches.ApplyPostfix),
            failures);

        Patch(
            harmony,
            typeof(TurretBehaviour),
            "ResetRotation",
            null,
            null,
            nameof(Patches.ResetRotationPostfix),
            failures);

        Patch(
            harmony,
            typeof(TurretBehaviour),
            "Dispose",
            null,
            nameof(Patches.DisposePrefix),
            null,
            failures);
    }

    private static void Patch(
        Harmony harmony,
        Type declaringType,
        string methodName,
        Type[]? parameterTypes,
        string? prefixName,
        string? postfixName,
        List<string> failures)
    {
        MethodInfo? original;
        try
        {
            original = parameterTypes == null
                ? AccessTools.Method(declaringType, methodName)
                : AccessTools.Method(declaringType, methodName, parameterTypes);
        }
        catch (Exception ex)
        {
            failures.Add(
                declaringType.FullName + "." + methodName + ": " +
                ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (original == null)
        {
            failures.Add(declaringType.FullName + "." + methodName + " was not found.");
            return;
        }

        HarmonyMethod? prefix = prefixName == null
            ? null
            : new HarmonyMethod(typeof(Patches), prefixName);

        HarmonyMethod? postfix = postfixName == null
            ? null
            : new HarmonyMethod(typeof(Patches), postfixName);

        try
        {
            harmony.Patch(original, prefix: prefix, postfix: postfix);
        }
        catch (Exception ex)
        {
            failures.Add(
                declaringType.FullName + "." + methodName + ": " +
                ex.GetType().Name + ": " + ex.Message);
        }
    }
}

internal static class UnityConversion
{
    public static NQuaternion ToNumerics(UQuaternion value)
    {
        return new NQuaternion(value.x, value.y, value.z, value.w);
    }

    public static UQuaternion ToUnity(NQuaternion value)
    {
        return new UQuaternion(value.X, value.Y, value.Z, value.W);
    }

    public static UVector3 ToUnity(NVector3 value) => new(value.X, value.Y, value.Z);

    public static NVector3 ToNumerics(UVector3 value)
    {
        return new NVector3(value.x, value.y, value.z);
    }
}

internal sealed class OwnedTurret
{
    public IntPtr RingPointer;
    public TurretRing? RingRef;
    public IntPtr MotorPointer;
    public IntPtr BehaviourPointer;
    public IntPtr FoundationPointer;
    public IntPtr TransformPointer;
    public bool SuppressAutoClaim;

    public int FoundationChanges;

    public bool DynamicReparentWarningIssued;
    public bool PhysicsWarningIssued;
    public bool InvalidAngleWarningIssued;
    public bool MissingTransformWarningIssued;
    public bool MissingBehaviourWarningIssued;

}

/// <summary>
/// Owned state is keyed by IL2CPP pointer. An entry is created only when an owned
/// turret ring (tagged <c>oscillatingElevationDrive</c>) initiates a motor, and the
/// motor is accepted only when its pointer matches the motor recorded for that ring.
/// </summary>
internal static class ElevationRegistry
{
    private static readonly Dictionary<IntPtr, OwnedTurret> ByRing = new();
    private static readonly Dictionary<IntPtr, OwnedTurret> ByMotor = new();
    private static readonly Dictionary<IntPtr, OwnedTurret> ByBehaviour = new();
    private static readonly Dictionary<(IntPtr Behaviour, IntPtr Transform), int> RecoverAttempts = new();
    private static readonly Dictionary<IntPtr, bool> TaggedTransformCache = new();

    public static void Clear()
    {
        ByRing.Clear();
        ByMotor.Clear();
        ByBehaviour.Clear();
        RecoverAttempts.Clear();
        TaggedTransformCache.Clear();
    }

    public static void ReleaseComponent(VehicleComponent component)
    {
        if (ByRing.TryGetValue(component.Pointer, out var ring))
        {
            Remove(ring);
        }

        if (ByMotor.TryGetValue(component.Pointer, out var motor))
        {
            Remove(motor);
        }
    }

    public static void SyncRing(TurretRing ring, TraverseMotor? motor)
    {
        if (ring == null)
        {
            return;
        }

        IntPtr ringPointer = ring.Pointer;
        var vehicleObject = ring.VehicleObject;

        if (vehicleObject == null)
        {
            // VehicleObject can be temporarily absent during a lifecycle
            // transition. Do not discard ownership on a transient null; Release()
            // is the authoritative cleanup path.
            return;
        }

        if (!vehicleObject.HasTag(PluginInfo.ElevationTag))
        {
            if (ByRing.TryGetValue(ringPointer, out OwnedTurret? stale))
            {
                Remove(stale);
            }

            return;
        }

        if (motor == null)
        {
            return;
        }

        IntPtr motorPointer = motor.Pointer;

        if (ByRing.TryGetValue(ringPointer, out OwnedTurret? existing))
        {
            if (existing.MotorPointer == motorPointer)
            {
                existing.RingRef = ring;
                RememberControlledTransform(existing, motor);
                return;
            }

            Remove(existing);
        }

        if (ByMotor.TryGetValue(motorPointer, out OwnedTurret? otherMotor))
        {
            if (otherMotor.RingPointer != ringPointer)
            {
                Remove(otherMotor);
            }
        }

        OwnedTurret entry = GetOrCreate(motorPointer);
        entry.RingPointer = ringPointer;
        entry.RingRef = ring;
        entry.MotorPointer = motorPointer;
        RememberControlledTransform(entry, motor);
        ByRing[ringPointer] = entry;
        ByMotor[motorPointer] = entry;
    }

    private static void RememberControlledTransform(OwnedTurret entry, TraverseMotor motor)
    {
        if (motor == null)
        {
            return;
        }

        var controlledTransform = motor.ControlledTransform;
        if (controlledTransform != null)
        {
            entry.TransformPointer = controlledTransform.Pointer;
        }
    }

    public static OwnedTurret? FindByMotor(TraverseMotor motor)
    {
        if (motor == null)
        {
            return null;
        }

        return ByMotor.TryGetValue(motor.Pointer, out OwnedTurret? entry)
            ? entry
            : null;
    }

    public static OwnedTurret? FindByBehaviour(TurretBehaviour behaviour)
    {
        if (behaviour == null)
        {
            return null;
        }

        return ByBehaviour.TryGetValue(behaviour.Pointer, out OwnedTurret? entry)
            ? entry
            : null;
    }

    /// <summary>
    /// Snapshot for the visual ring-model maintenance driver. Returns the current
    /// owned entries without changing ownership or touching native state.
    /// </summary>
    public static List<OwnedTurret> SnapshotForVisualTuning()
    {
        return new List<OwnedTurret>(ByMotor.Values);
    }

    public static void Prepare(TraverseMotor motor, TurretBehaviour behaviour)
    {
        OwnedTurret? entry = Attach(motor, behaviour);
        if (entry == null)
        {
            return;
        }

        if (behaviour.physicsDriven)
        {
            ReportPhysics(entry);
            ReturnToNative(behaviour, entry, suppressAutoClaim: true);
            return;
        }

        entry.FoundationPointer = FoundationPointer(behaviour);
        if (behaviour.transform != null)
        {
            entry.TransformPointer = behaviour.transform.Pointer;
        }

        behaviour.axis = UnityConversion.ToUnity(
            ElevationMath.ComputeElevationAxis(
                UnityConversion.ToNumerics(behaviour.restRotation)));

        // restForwards is deliberately left untouched.
    }

    public static OwnedTurret? Attach(TraverseMotor motor, TurretBehaviour behaviour)
    {
        if (motor == null || behaviour == null)
        {
            return null;
        }

        if (!ByMotor.TryGetValue(motor.Pointer, out OwnedTurret? entry))
        {
            return null;
        }

        entry.SuppressAutoClaim = false;
        IntPtr behaviourPointer = behaviour.Pointer;

        if (entry.BehaviourPointer != behaviourPointer)
        {
            if (entry.BehaviourPointer != IntPtr.Zero)
            {
                ByBehaviour.Remove(entry.BehaviourPointer);
            }

            if (ByBehaviour.TryGetValue(behaviourPointer, out OwnedTurret? previous))
            {
                if (!ReferenceEquals(previous, entry))
                {
                    previous.BehaviourPointer = IntPtr.Zero;
                }
            }

            entry.BehaviourPointer = behaviourPointer;
            ResetWarnings(entry);
        }

        entry.FoundationPointer = FoundationPointer(behaviour);
        if (behaviour.transform != null)
        {
            entry.TransformPointer = behaviour.transform.Pointer;
        }

        ByBehaviour[behaviourPointer] = entry;
        return entry;
    }

    /// <summary>
    /// Recovers ownership when the game replaces or re-enables the native
    /// TurretBehaviour instance after registration. The match is only by the exact
    /// controlled-transform pointer recorded for this owned motor; a duplicated
    /// match is rejected rather than converting an ambiguous or unrelated turret.
    /// </summary>
    public static OwnedTurret? TryClaimByTransform(TurretBehaviour behaviour)
    {
        if (behaviour == null || behaviour.physicsDriven)
        {
            return null;
        }

        var transform = behaviour.transform;
        if (transform == null)
        {
            return null;
        }

        IntPtr target = transform.Pointer;
        if (target == IntPtr.Zero)
        {
            return null;
        }

        // Cheap rejection first: an unrelated turret must not cause per-frame
        // candidate-list allocations or ever be considered for conversion.
        bool hasExactCandidate = false;
        foreach (OwnedTurret candidate in ByMotor.Values)
        {
            if (candidate.TransformPointer == target)
            {
                hasExactCandidate = true;
                break;
            }
        }

        if (!hasExactCandidate)
        {
            return null;
        }

        var candidates = new List<OwnedTurret>(ByMotor.Count);
        var transforms = new List<IntPtr>(ByMotor.Count);
        foreach (OwnedTurret candidate in ByMotor.Values)
        {
            candidates.Add(candidate);
            transforms.Add(candidate.TransformPointer);
        }

        int index = OwnerResolution.FindUniqueTransformMatch(target, transforms);
        if (index < 0)
        {
            return null;
        }

        OwnedTurret entry = candidates[index];
        if (entry.SuppressAutoClaim)
        {
            return null;
        }

        IntPtr behaviourPointer = behaviour.Pointer;
        bool changed = entry.BehaviourPointer != behaviourPointer;

        if (changed && entry.BehaviourPointer != IntPtr.Zero)
        {
            ByBehaviour.Remove(entry.BehaviourPointer);
        }

        entry.BehaviourPointer = behaviourPointer;
        entry.FoundationPointer = FoundationPointer(behaviour);
        entry.TransformPointer = target;
        ByBehaviour[behaviourPointer] = entry;

        behaviour.axis = UnityConversion.ToUnity(
            ElevationMath.ComputeElevationAxis(
                UnityConversion.ToNumerics(behaviour.restRotation)));

        return entry;
    }

    /// <summary>
    /// Recovers ownership from the behaviour's own native ancestry after a
    /// designer -> play rebuild emptied the registry. Only a TurretRing whose
    /// VehicleObject carries the oscillatingElevationDrive tag is accepted, so an
    /// unrelated turret can never be converted. The walk is bounded and cached
    /// per behaviour pointer.
    /// </summary>
    public static OwnedTurret? TryRecoverFromBehaviour(TurretBehaviour behaviour)
    {
        if (behaviour == null)
        {
            return null;
        }

        IntPtr behaviourPointer = behaviour.Pointer;
        UnityEngine.Transform? start;
        try
        {
            start = behaviour.transform;
        }
        catch
        {
            return null;
        }

        // The 3-attempt cap is per behaviour AND per transform. A model/ring
        // rebuild can create a new transform, so it must get its own attempts
        // instead of inheriting the exhausted count of the old hierarchy.
        IntPtr transformPointer = start != null ? start.Pointer : IntPtr.Zero;
        var key = (behaviourPointer, transformPointer);
        RecoverAttempts.TryGetValue(key, out int attempts);
        if (attempts >= 3)
        {
            return null;
        }

        RecoverAttempts[key] = attempts + 1;

        TurretRing? ring = FindTaggedRing(start);
        if (ring == null)
        {
            ring = FindTaggedRing(FoundationTransform(behaviour));
        }

        if (ring == null)
        {
            return null;
        }

        TraverseMotor? motor;
        try
        {
            motor = ring.motor;
        }
        catch
        {
            return null;
        }

        if (motor == null)
        {
            return null;
        }

        SyncRing(ring, motor);
        Prepare(motor, behaviour);
        return FindByBehaviour(behaviour);
    }

    private static UnityEngine.Transform? FoundationTransform(TurretBehaviour behaviour)
    {
        try
        {
            return behaviour.foundation;
        }
        catch
        {
            return null;
        }
    }

    private static TurretRing? FindTaggedRing(UnityEngine.Transform? start)
    {        var current = start;
        for (int depth = 0; depth < 12 && current != null; depth++)
        {
            try
            {
                var component = current.GetComponent(Il2CppType.Of<TurretRing>());
                if (component != null)
                {
                    var ring = component.TryCast<TurretRing>();
                    if (ring != null && Patches.IsOwnedRing(ring))
                    {
                        return ring;
                    }
                }
            }
            catch
            {
                // A wrapper can be stale during a transition; try the next ancestor.
            }

            try
            {
                current = current.parent;
            }
            catch
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// 0.2.11 safety net. Native TurretBehaviour.Apply computes
    /// LookRotation(delta * restForwards, axis) and rolls the upper structure by
    /// 90 degrees when a stale local -X elevation axis is left on the behaviour.
    /// This runs only when an owned entry cannot be matched and the behaviour
    /// still has the exact transform/foundation of a registered motor, or still
    /// sits under an owned tagged ring, so an unrelated or other-mod turret is
    /// never touched.
    /// </summary>
    internal static bool ResetNativeAxisIfFormerlyOwned(TurretBehaviour? behaviour)
    {
        if (behaviour == null)
        {
            return false;
        }

        UnityEngine.Transform? transform;
        try { transform = behaviour.transform; }
        catch { return false; }
        if (transform == null)
        {
            return false;
        }

        IntPtr transformPointer = transform.Pointer;
        if (transformPointer == IntPtr.Zero)
        {
            return false;
        }

        IntPtr foundationPointer;
        try { foundationPointer = FoundationPointer(behaviour); }
        catch { foundationPointer = IntPtr.Zero; }

        foreach (OwnedTurret entry in ByMotor.Values)
        {
            if (entry.TransformPointer == transformPointer ||
                (foundationPointer != IntPtr.Zero && entry.FoundationPointer == foundationPointer))
            {
                behaviour.axis = UnityConversion.ToUnity(
                    ElevationMath.ComputeNativeAxis(
                        UnityConversion.ToNumerics(behaviour.restRotation)));
                return true;
            }
        }

        // A rebuilt behaviour/transform can lose the recorded pointer. Only a
        // behaviour whose ancestry still contains an owned, tagged ring is
        // eligible, so unrelated and other-mod turrets keep their own axis.
        // The ancestry result is cached per transform pointer: this path runs
        // for every unowned turret behaviour, every frame.
        if (!TaggedTransformCache.TryGetValue(transformPointer, out bool tagged))
        {
            tagged = FindTaggedRingForDiagnostics(behaviour) != null;
            if (TaggedTransformCache.Count > 256)
            {
                TaggedTransformCache.Clear();
            }
            TaggedTransformCache[transformPointer] = tagged;
        }

        if (tagged)
        {
            behaviour.axis = UnityConversion.ToUnity(
                ElevationMath.ComputeNativeAxis(
                    UnityConversion.ToNumerics(behaviour.restRotation)));
            return true;
        }

        return false;
    }

    internal static TurretRing? FindTaggedRingForDiagnostics(TurretBehaviour? behaviour)
    {
        if (behaviour == null)
        {
            return null;
        }

        try
        {
            return FindTaggedRing(behaviour.transform) ??
                   FindTaggedRing(FoundationTransform(behaviour));
        }
        catch
        {
            return null;
        }
    }

    internal static string TaggedRingIdForDiagnostics(TurretBehaviour? behaviour)
    {
        TurretRing? ring = FindTaggedRingForDiagnostics(behaviour);
        if (ring == null)
        {
            return "?";
        }

        try { return ring.VUID.Value.ToString(); }
        catch { return "?"; }
    }

    private static void ResetWarnings(OwnedTurret entry)
    {
        entry.DynamicReparentWarningIssued = false;
        entry.PhysicsWarningIssued = false;
        entry.InvalidAngleWarningIssued = false;
        entry.MissingTransformWarningIssued = false;
        entry.MissingBehaviourWarningIssued = false;
    }

    public static void DetachForReenable(TraverseMotor motor)
    {
        OwnedTurret? entry = FindByMotor(motor);
        if (entry == null)
        {
            return;
        }

        entry.SuppressAutoClaim = false;
        if (entry.BehaviourPointer != IntPtr.Zero)
        {
            ByBehaviour.Remove(entry.BehaviourPointer);
            entry.BehaviourPointer = IntPtr.Zero;
        }
    }

    public static void ReturnToNative(
        TurretBehaviour behaviour,
        OwnedTurret entry,
        bool suppressAutoClaim = false)
    {
        entry.SuppressAutoClaim = suppressAutoClaim;
        if (behaviour != null)
        {
            if (entry.BehaviourPointer == behaviour.Pointer)
            {
                ByBehaviour.Remove(entry.BehaviourPointer);
                entry.BehaviourPointer = IntPtr.Zero;
            }

            behaviour.axis = UnityConversion.ToUnity(
                ElevationMath.ComputeNativeAxis(
                    UnityConversion.ToNumerics(behaviour.restRotation)));
            Diagnostics.ReturnToNative(
                suppressAutoClaim ? "physics/suppressed" : "native",
                entry.MotorPointer,
                Diagnostics.RingId(entry),
                UnityConversion.ToUnity(ElevationMath.ComputeElevationAxis(
                    UnityConversion.ToNumerics(behaviour.restRotation))),
                behaviour.axis);
        }
        else
        {
            Remove(entry);
        }
    }

    public static void Forget(TurretBehaviour behaviour)
    {
        if (behaviour == null)
        {
            return;
        }

        IntPtr pointer = behaviour.Pointer;
        if (ByBehaviour.TryGetValue(pointer, out OwnedTurret? entry))
        {
            ByBehaviour.Remove(pointer);
            if (entry.BehaviourPointer == pointer)
            {
                entry.BehaviourPointer = IntPtr.Zero;
            }
        }
    }

    public static void ForgetByMotor(TraverseMotor motor)
    {
        OwnedTurret? entry = FindByMotor(motor);
        if (entry == null)
        {
            return;
        }

        if (entry.BehaviourPointer != IntPtr.Zero)
        {
            ByBehaviour.Remove(entry.BehaviourPointer);
            entry.BehaviourPointer = IntPtr.Zero;
        }
    }

    public static bool FoundationMatches(TurretBehaviour behaviour, OwnedTurret entry)
    {
        var transform = behaviour.transform;
        var actualParent = transform != null ? transform.parent : null;
        IntPtr current = actualParent != null ? actualParent.Pointer : IntPtr.Zero;

        // The designer -> play rebuild legitimately reparents the behaviour once.
        // Native MoveToTarget then recaptures rest/foundation and resets its axis,
        // which is why the axis is re-asserted around native calls. Track the new
        // foundation instead of handing the behaviour back to the native up axis.
        if (current != entry.FoundationPointer)
        {
            if (entry.FoundationChanges++ >= 12)
            {
                if (!entry.DynamicReparentWarningIssued)
                {
                    entry.DynamicReparentWarningIssued = true;
                    Plugin.Warn(
                        "Oscillating elevation drive: foundation kept changing repeatedly " +
                        "(0x" + entry.FoundationPointer.ToInt64().ToString("X") +
                        " -> 0x" + current.ToInt64().ToString("X") +
                        "). Returning it to the native axis.");
                }

                ReturnToNative(behaviour, entry, suppressAutoClaim: true);
                return false;
            }

            entry.FoundationPointer = current;
            if (transform != null)
            {
                entry.TransformPointer = transform.Pointer;
            }
        }

        return true;
    }

    public static void ReportPhysics(OwnedTurret entry)
    {
        if (entry.PhysicsWarningIssued)
        {
            return;
        }

        entry.PhysicsWarningIssued = true;
        Plugin.Warn(
            "Oscillating elevation drive: physicsDriven behaviour is unsupported. " +
            "Returning it to native control; the plugin will not disable or move a Rigidbody.");
    }

    public static void ReportInvalidAngle(OwnedTurret entry, float angle)
    {
        if (entry.InvalidAngleWarningIssued)
        {
            return;
        }

        entry.InvalidAngleWarningIssued = true;
        Plugin.Warn(
            "Oscillating elevation drive: registered simulation angle was non-finite (" +
            angle.ToString("R") + "); using zero pitch for the pose.");
    }

    public static void ReportMissingTransform(OwnedTurret entry)
    {
        if (entry.MissingTransformWarningIssued)
        {
            return;
        }

        entry.MissingTransformWarningIssued = true;
        Plugin.Warn(
            "Oscillating elevation drive: behaviour transform was unavailable; returning it to native control.");
    }

    public static void ReportMissingBehaviour(OwnedTurret entry)
    {
        if (entry.MissingBehaviourWarningIssued)
        {
            return;
        }

        entry.MissingBehaviourWarningIssued = true;
        Plugin.Warn(
            "Oscillating elevation drive: motor.Behaviour could not be cast to TurretBehaviour.");
    }

    private static OwnedTurret GetOrCreate(IntPtr motorPointer)
    {
        if (ByMotor.TryGetValue(motorPointer, out OwnedTurret? entry))
        {
            return entry;
        }

        entry = new OwnedTurret
        {
            MotorPointer = motorPointer
        };

        ByMotor.Add(motorPointer, entry);
        return entry;
    }

    private static void Remove(OwnedTurret entry)
    {
        if (entry.RingPointer != IntPtr.Zero &&
            ByRing.TryGetValue(entry.RingPointer, out OwnedTurret? ringEntry) &&
            ReferenceEquals(ringEntry, entry))
        {
            ByRing.Remove(entry.RingPointer);
        }

        if (entry.MotorPointer != IntPtr.Zero &&
            ByMotor.TryGetValue(entry.MotorPointer, out OwnedTurret? motorEntry) &&
            ReferenceEquals(motorEntry, entry))
        {
            ByMotor.Remove(entry.MotorPointer);
        }

        if (entry.BehaviourPointer != IntPtr.Zero &&
            ByBehaviour.TryGetValue(entry.BehaviourPointer, out OwnedTurret? behaviourEntry) &&
            ReferenceEquals(behaviourEntry, entry))
        {
            ByBehaviour.Remove(entry.BehaviourPointer);
        }

        entry.BehaviourPointer = IntPtr.Zero;
    }

    private static IntPtr FoundationPointer(TurretBehaviour behaviour)
    {
        var foundation = behaviour.foundation;
        return foundation != null ? foundation.Pointer : IntPtr.Zero;
    }
}

internal static class Patches
{
    internal static void ComponentReleasePrefix(VehicleComponent __instance)
    {
        PivotHousingVisuals.Release(__instance.Pointer);
        HeavyTrunnion.Release(__instance.Pointer);
        ElevationRegistry.ReleaseComponent(__instance);
    }
    internal static void CreateNewBlueprintPostfix(
        TurretRing __instance,
        TurretRingBlueprint __result)
    {
        if (__instance == null || __result == null)
        {
            return;
        }

        if (!IsOwnedRing(__instance))
        {
            return;
        }

        // This hook creates new defaults; serialized/user-edited blueprints load separately.
        __result.RingDiameter = 350;
        __result.RingHeight = 25;
        __result.RingThickness = 10;
    }

    internal static void InitiateMotorPrefix(TurretRing __instance, TraverseMotor __0)
    {
        if (__instance == null || __0 == null)
        {
            return;
        }

        ElevationRegistry.SyncRing(__instance, __0);
    }

    internal static void InitiateMotorPostfix(TurretRing __instance, TraverseMotor __0)
    {
        if (__instance == null || __0 == null)
        {
            return;
        }

        ElevationRegistry.SyncRing(__instance, __0);
        if (IsOwnedRing(__instance) && OscillatingSelector.SelectorIntegration.Creating &&
            __0.TraverseBlueprintSlot != null && __0.TraverseBlueprintSlot.HasBlueprint)
        {
            var blueprint = __0.TraverseBlueprintSlot.Blueprint;
            blueprint.MinAngle = -15;
            blueprint.MaxAngle = 35;
        }

        TurretBehaviour? behaviour = ResolveBehaviour(__0);
        if (behaviour != null)
        {
            ElevationRegistry.Prepare(__0, behaviour);
        }
    }

    internal static void RingEnableBehaviourPrefix(TurretRing __instance)
    {
        if (__instance == null)
        {
            return;
        }

        ElevationRegistry.SyncRing(__instance, __instance.motor);
    }

    internal static void MotorEnableBehaviourPrefix(TraverseMotor __instance)
    {
        if (__instance == null)
        {
            return;
        }

        ElevationRegistry.DetachForReenable(__instance);
    }

    internal static void MotorEnableBehaviourPostfix(TraverseMotor __instance)
    {
        if (__instance == null)
        {
            return;
        }

        OwnedTurret? entry = ElevationRegistry.FindByMotor(__instance);
        if (entry == null)
        {
            return;
        }

        TurretBehaviour? behaviour = ResolveBehaviour(__instance);
        if (behaviour == null)
        {
            ElevationRegistry.ReportMissingBehaviour(entry);
            return;
        }

        ElevationRegistry.Prepare(__instance, behaviour);
    }

    internal static void MotorDisableBehaviourPrefix(TraverseMotor __instance)
    {
        if (__instance == null)
        {
            return;
        }

        OwnedTurret? entry = ElevationRegistry.FindByMotor(__instance);
        if (entry == null)
        {
            return;
        }

        TurretBehaviour? behaviour = ResolveBehaviour(__instance);
        if (behaviour != null)
        {
            ElevationRegistry.ReturnToNative(behaviour, entry);
        }
        else
        {
            ElevationRegistry.ForgetByMotor(__instance);
        }
    }

    internal static void DisposePrefix(TurretBehaviour __instance)
    {
        if (__instance != null)
        {
            ElevationRegistry.Forget(__instance);
        }
    }

    internal static void MoveToTargetPrefix(
        TurretBehaviour __instance,
        ref AimFlags __2)
    {
        if (__instance == null)
        {
            return;
        }

        OwnedTurret? entry = ElevationRegistry.FindByBehaviour(__instance) ??
            ElevationRegistry.TryClaimByTransform(__instance) ??
            ElevationRegistry.TryRecoverFromBehaviour(__instance);
        if (entry == null)
        {
            return;
        }

        if (__instance.physicsDriven)
        {
            ElevationRegistry.ReportPhysics(entry);
            ElevationRegistry.ReturnToNative(__instance, entry, suppressAutoClaim: true);
            return;
        }

        if (!ElevationRegistry.FoundationMatches(__instance, entry))
        {
            return;
        }

        // Keep native lock/simultaneous aiming, but elevation must not wrap.
        __2 &= ~AimFlags.Wrapping;

        __instance.axis = UnityConversion.ToUnity(
            ElevationMath.ComputeElevationAxis(
                UnityConversion.ToNumerics(__instance.restRotation)));
    }

    internal static void MoveToTargetPostfix(TurretBehaviour __instance)
    {
        if (__instance == null)
        {
            return;
        }

        OwnedTurret? entry = ElevationRegistry.FindByBehaviour(__instance) ??
            ElevationRegistry.TryClaimByTransform(__instance) ??
            ElevationRegistry.TryRecoverFromBehaviour(__instance);
        if (entry == null)
        {
            return;
        }

        if (__instance.physicsDriven)
        {
            ElevationRegistry.ReportPhysics(entry);
            return;
        }

        if (!ElevationRegistry.FoundationMatches(__instance, entry))
        {
            return;
        }

        // Native MoveToTarget may reinitialize the rest frame if the parent changed.
        // In the normal nested-turret case the parent pointer is stable, so only the
        // axis has to be restored after native execution.
        __instance.axis = UnityConversion.ToUnity(
            ElevationMath.ComputeElevationAxis(
                UnityConversion.ToNumerics(__instance.restRotation)));
    }

    internal static bool ApplyPrefix(TurretBehaviour __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        OwnedTurret? entry = ElevationRegistry.FindByBehaviour(__instance) ??
            ElevationRegistry.TryClaimByTransform(__instance) ??
            ElevationRegistry.TryRecoverFromBehaviour(__instance);
        if (entry == null)
        {
            // Native Apply is about to run. If this behaviour still belongs to a
            // registered motor, its axis may still be the plugin's local -X; the
            // native LookRotation(forward, -X) then rolls the upper structure 90
            // degrees. Restore the native up axis first; never touch unrelated
            // turrets. The diagnostics line records the rotation before/after.
            bool reset = ElevationRegistry.ResetNativeAxisIfFormerlyOwned(__instance);
            Diagnostics.ApplyUnclaimedPrefix(
                __instance,
                reset,
                () => ElevationRegistry.TaggedRingIdForDiagnostics(__instance));
            return true;
        }
        if (__instance.physicsDriven)
        {
            ElevationRegistry.ReportPhysics(entry);
            ElevationRegistry.ReturnToNative(__instance, entry, suppressAutoClaim: true);
            return true;
        }

        if (!ElevationRegistry.FoundationMatches(__instance, entry))
        {
            return true;
        }

        if (__instance.transform == null)
        {
            ElevationRegistry.ReportMissingTransform(entry);
            ElevationRegistry.ReturnToNative(__instance, entry, suppressAutoClaim: true);
            return true;
        }

        ApplyOwnedPose(__instance, entry);

        // Replaces native Apply only for an owned, non-physicsDriven behaviour.
        // Native Apply uses AngleAxis(...) * restForwards and then LookRotation with
        // the axis as the up vector, which produces a rolled upper structure here.
        return false;
    }

    internal static void ApplyPostfix(TurretBehaviour __instance)
    {
        if (__instance == null)
        {
            return;
        }

        Diagnostics.ApplyUnclaimedPostfix(__instance);

        OwnedTurret? entry = ElevationRegistry.FindByBehaviour(__instance) ??
            ElevationRegistry.TryClaimByTransform(__instance) ??
            ElevationRegistry.TryRecoverFromBehaviour(__instance);
        if (entry == null)
        {
            return;
        }
        if (__instance.physicsDriven)
        {
            return;
        }

        if (!ElevationRegistry.FoundationMatches(__instance, entry))
        {
            return;
        }

        if (__instance.transform == null)
        {
            return;
        }

        // Safety net: if the native backend still executed Apply despite the
        // prefix's false return, write the owned elevation pose last.
        ApplyOwnedPose(__instance, entry);
    }

    private static void ApplyOwnedPose(TurretBehaviour behaviour, OwnedTurret entry)
    {
        float rawAngle = ReadAppliedAngle(behaviour);
        float angle = ElevationMath.SanitizeAngleDegrees(rawAngle);
        if (!float.IsFinite(rawAngle))
        {
            ElevationRegistry.ReportInvalidAngle(entry, rawAngle);
        }

        NQuaternion rest = UnityConversion.ToNumerics(behaviour.restRotation);
        NQuaternion pose = ElevationMath.ComputeElevationRotation(rest, angle);

        var transform = behaviour.transform;
        if (transform == null)
        {
            return;
        }

        transform.localRotation = UnityConversion.ToUnity(pose);
        behaviour.hasAppliedTarget = (behaviour.aimSetFlags & SetAimFlags.IsTarget) != 0;
    }

    internal static void ResetRotationPostfix(TurretBehaviour __instance)
    {
        if (__instance == null)
        {
            return;
        }

        OwnedTurret? entry = ElevationRegistry.FindByBehaviour(__instance) ??
            ElevationRegistry.TryClaimByTransform(__instance) ??
            ElevationRegistry.TryRecoverFromBehaviour(__instance);
        if (entry == null)
        {
            return;
        }

        if (__instance.physicsDriven)
        {
            ElevationRegistry.ReportPhysics(entry);
            ElevationRegistry.ReturnToNative(__instance, entry, suppressAutoClaim: true);
            return;
        }

        if (!ElevationRegistry.FoundationMatches(__instance, entry))
        {
            return;
        }

        // Native ResetRotation restores restRotation and its scalar simulation
        // baseline. Reassert the elevation axis in case that lifecycle reset also
        // rebuilt the native up axis, which would otherwise yaw the upper turret.
        if (__instance.transform != null)
        {
            entry.TransformPointer = __instance.transform.Pointer;
        }

        __instance.axis = UnityConversion.ToUnity(
            ElevationMath.ComputeElevationAxis(
                UnityConversion.ToNumerics(__instance.restRotation)));
    }

    private static TurretBehaviour? ResolveBehaviour(TraverseMotor motor)
    {
        if (motor == null)
        {
            return null;
        }

        ITurretBehaviour behaviour = motor.Behaviour;
        if (behaviour == null)
        {
            return null;
        }

        if (behaviour is Il2CppObjectBase wrapper)
        {
            return wrapper.TryCast<TurretBehaviour>();
        }

        return null;
    }

    internal static bool IsOwnedRing(TurretRing ring)
    {
        var vehicleObject = ring.VehicleObject;
        if (vehicleObject == null || !vehicleObject.HasTag(PluginInfo.ElevationTag)) return false;
        return DateAllowed(ring);
    }

    private static readonly HashSet<IntPtr> DateWarned = new();
    private static bool DateAllowed(TurretRing ring)
    {
        try
        {
            var value = ring.Vehicle?.DesignInfo?.Date;
            if (value is not { Valid: true } d) return true;
            var date = d.Year == 0 ? DateTime.MinValue : new DateTime(d.Year, d.Month, d.Day);
            if (OscillatingDatePolicy.Allows(date)) return true;
            if (DateWarned.Add(ring.Pointer))
                Plugin.Warn(
                    $"[Oscillating] The oscillating drive needs {OscillatingDatePolicy.MinimumDate:yyyy-MM-dd}; " +
                    $"this vehicle is {date:yyyy-MM-dd}. Saved part retained, native turret used.");
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static float ReadAppliedAngle(TurretBehaviour behaviour)
    {
        var axes = RotationAxisPhysicsRegister.axes;
        if (axes == null)
        {
            return float.NaN;
        }

        int index = behaviour.physicsHandle;
        if (index < 0 || index >= axes.Length)
        {
            return float.NaN;
        }

        var physics = axes[index];
        if (physics == null)
        {
            return float.NaN;
        }

        // Read-only simulation angle. Do not mutate the boxed physics wrapper.
        return physics.GetAppliedAngle();
    }

}
