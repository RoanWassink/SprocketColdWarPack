using System.Reflection;
using SprocketStabilization;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using Il2CppSystem.Runtime.Serialization;

// Regression probes for the real runtime/state/persistence source.
// All game/Unity/Harmony APIs are explicit controlled doubles (NativeDoubles.cs);
// nothing here loads game code or proves Harmony callability or native driving.

const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
int checks = 0;

object? InvokeResult(Type type, string name, params object?[] args)
    => type.GetMethod(name, Hidden)!.Invoke(null, args);

void Require(bool value, string message)
{
    checks++;
    if (!value) throw new Exception("FAILED: " + message);
}

object? Prefix(TurretBehaviour behaviour)
{
    var args = new object?[] { behaviour, null };
    InvokeResult(typeof(StabilizerRuntime), "BeforeMoveToTarget", args);
    return args[1];
}

void Postfix(TurretBehaviour behaviour, object? state)
    => InvokeResult(typeof(StabilizerRuntime), "AfterMoveToTarget", behaviour, state);

object? Finish(TurretBehaviour behaviour, object? state, Exception? exception)
    => InvokeResult(typeof(StabilizerRuntime), "Finalizer", behaviour, state, exception);

void Run(TurretBehaviour behaviour)
{
    object? state = Prefix(behaviour);
    Postfix(behaviour, state);
}

void Save(VehicleComponent component, SerializationInfo info)
    => InvokeResult(typeof(StabilizerPersistence), "SaveDataPostfix", component, info);

void Load(VehicleComponent component, SerializationInfo info)
    => InvokeResult(typeof(StabilizerPersistence), "LoadDataPostfix", component, info);

void Release(VehicleComponent component)
    => InvokeResult(typeof(StabilizerPersistence), "ReleaseInternalPrefix", component);

void SaveMotorBlueprint(Sprocket.Vehicles.MotorBlueprint blueprint, Sprocket.Blueprints.IBlueprintSerializer serializer)
    => InvokeResult(typeof(StabilizerBlueprintPersistence), "MotorSavePostfix", blueprint, serializer);

void LoadMotorBlueprint(Sprocket.Vehicles.MotorBlueprint blueprint, Sprocket.Blueprints.IBlueprintDeserializer deserializer)
    => InvokeResult(typeof(StabilizerBlueprintPersistence), "MotorLoadPostfix", blueprint, deserializer);

void CloneMotorBlueprint(Sprocket.Blueprints.Blueprint source, Il2CppSystem.Object result)
    => InvokeResult(typeof(StabilizerBlueprintPersistence), "BlueprintClonePostfix", source, result);

void Reset()
{
    StabilizerRuntime.ResetForUnload();
    StabilizerState.ClearAll();
    StabilizerState.ClearBlueprintFlags();
    Plugin.StabilizerEnabled.Value = true;
    Plugin.OverrideRateLimits.Value = true;
    Plugin.RealisticLimits.Value = true;
    Plugin.HorizontalMaxTraverseRate.Value = 24f;
    Plugin.ElevationMaxTraverseRate.Value = 6f;
    Plugin.ResponseTimeSeconds.Value = 0.75f;
    Plugin.BoostTorqueMultiplier.Value = 100f;
    Plugin.BoostMaxTraverseRate.Value = 360f;
    Plugin.BoostMaxAcceleration.Value = 3600f;
    Plugin.BoostMaxBrakeAcceleration.Value = 3600f;
}

(TraverseMotor Motor, TurretBehaviour Behaviour) NewDrive(
    int id = 7,
    int componentPointer = 70,
    int vehiclePointer = 700,
    bool elevationTag = false)
{
    var motor = new TraverseMotor { VUID = new Sprocket.Id(id), Pointer = (IntPtr)componentPointer };
    motor.MotorBlueprintSlot.Blueprint = new Sprocket.Vehicles.MotorBlueprint
    {
        Pointer = (IntPtr)(componentPointer + 2)
    };
    motor.Vehicle.Pointer = (IntPtr)vehiclePointer;
    var ring = new TurretRing { motor = motor };
    // Production gating requires the ring and motor to belong to the same live
    // vehicle; mirror that invariant in the controlled double.
    ring.Vehicle.Pointer = (IntPtr)vehiclePointer;
    if (elevationTag) ring.VehicleObject.Tags.Add("oscillatingElevationDrive");
    var behaviour = new TurretBehaviour
    {
        Pointer = (IntPtr)(componentPointer + 1),
        MotorTorqueMultiplier = 2f,
        MaxTraverseRate = 20f,
        MaxAcceleration = 3f,
        MaxBrakeAcceleration = 4f
    };
    behaviour.transform.Components[typeof(TurretRing)] = ring;
    return (motor, behaviour);
}

bool AtBase(TurretBehaviour behaviour)
    => behaviour.MotorTorqueMultiplier == 2f &&
       behaviour.MaxTraverseRate == 20f &&
       behaviour.MaxAcceleration == 3f &&
       behaviour.MaxBrakeAcceleration == 4f;

bool AtDefaults(TurretBehaviour behaviour)
    => behaviour.MotorTorqueMultiplier == 100f &&
       behaviour.MaxTraverseRate == 360f &&
       behaviour.MaxAcceleration == 3600f &&
       behaviour.MaxBrakeAcceleration == 3600f;

// 1. Normal scoped boost: values are boosted for the native call and restored after it.
Reset();
var normal = NewDrive();
StabilizerState.SetBlueprintFlag(normal.Motor.MotorBlueprintSlot.Blueprint, true);
object? normalState = Prefix(normal.Behaviour);
Require(AtDefaults(normal.Behaviour), "normal call: boost uses validated defaults while MoveToTarget runs");
Postfix(normal.Behaviour, normalState);
Require(AtBase(normal.Behaviour), "normal call: all four values restored after MoveToTarget");
Require(normalState != null, "normal call: per-call __state exists");
Require(!StabilizerState.GetFlag(default), "default key is never true");

// 1b. Native-only mode: no limit writes at all; the native gearing caps the
// speed, so a fast mouse swing can outrun the drive.
Reset();
Plugin.OverrideRateLimits.Value = false;
Plugin.RealisticLimits.Value = false;
var realistic = NewDrive();
StabilizerState.SetBlueprintFlag(realistic.Motor.MotorBlueprintSlot.Blueprint, true);
object? realisticState = Prefix(realistic.Behaviour);
Require(realistic.Behaviour.MotorTorqueMultiplier == 100f, "realistic mode boosts torque");
Require(
    realistic.Behaviour.MaxTraverseRate == 20f &&
    realistic.Behaviour.MaxAcceleration == 3f &&
    realistic.Behaviour.MaxBrakeAcceleration == 4f,
    "realistic mode leaves the native speed and acceleration limits untouched");
Postfix(realistic.Behaviour, realisticState);
Require(AtBase(realistic.Behaviour), "realistic mode restores the boosted torque");

// 1c. Realistic-limits mode: the drive is clamped to the historical reference
// (24 deg/s traverse, 0.75 s response -> 32 deg/s^2 acceleration/braking).
Reset();
Plugin.OverrideRateLimits.Value = false;
Plugin.RealisticLimits.Value = true;
var limited = NewDrive();
limited.Behaviour.MaxTraverseRate = 100f;
limited.Behaviour.MaxAcceleration = 1000f;
limited.Behaviour.MaxBrakeAcceleration = 1000f;
StabilizerState.SetBlueprintFlag(limited.Motor.MotorBlueprintSlot.Blueprint, true);
object? limitedState = Prefix(limited.Behaviour);
Require(limited.Behaviour.MotorTorqueMultiplier == 100f, "realistic limits still boost torque");
Require(limited.Behaviour.MaxTraverseRate == 24f, "horizontal drive is clamped to the historical 24 deg/s");
Require(limited.Behaviour.MaxAcceleration == 32f, "acceleration is rate/response = 32 deg/s^2");
Require(limited.Behaviour.MaxBrakeAcceleration == 32f, "braking matches the derived acceleration");
Postfix(limited.Behaviour, limitedState);
Require(
    limited.Behaviour.MaxTraverseRate == 100f &&
    limited.Behaviour.MaxAcceleration == 1000f &&
    limited.Behaviour.MaxBrakeAcceleration == 1000f,
    "realistic limits restore the native values");

// 1d. The tagged oscillating elevation ring uses the elevation reference (6 deg/s,
// 0.75 s -> 8 deg/s^2), never the faster horizontal cap.
Reset();
Plugin.OverrideRateLimits.Value = false;
Plugin.RealisticLimits.Value = true;
var elevation = NewDrive(elevationTag: true);
elevation.Behaviour.MaxTraverseRate = 100f;
elevation.Behaviour.MaxAcceleration = 1000f;
elevation.Behaviour.MaxBrakeAcceleration = 1000f;
StabilizerState.SetBlueprintFlag(elevation.Motor.MotorBlueprintSlot.Blueprint, true);
object? elevationState = Prefix(elevation.Behaviour);
Require(elevation.Behaviour.MaxTraverseRate == 6f, "elevation drive is clamped to the historical 6 deg/s");
Require(elevation.Behaviour.MaxAcceleration == 8f, "elevation acceleration is 6/0.75 = 8 deg/s^2");
Postfix(elevation.Behaviour, elevationState);

// 1e. Realistic mode never raises a drive that the native design already limits
// more tightly than the historical ceiling.
Reset();
Plugin.OverrideRateLimits.Value = false;
Plugin.RealisticLimits.Value = true;
var slow = NewDrive();
slow.Behaviour.MaxTraverseRate = 9f;
slow.Behaviour.MaxAcceleration = 2f;
slow.Behaviour.MaxBrakeAcceleration = 2f;
StabilizerState.SetBlueprintFlag(slow.Motor.MotorBlueprintSlot.Blueprint, true);
object? slowState = Prefix(slow.Behaviour);
Require(slow.Behaviour.MaxTraverseRate == 9f, "realistic mode keeps a slower native traverse limit");
Require(slow.Behaviour.MaxAcceleration == 2f, "realistic mode keeps a slower native acceleration limit");
Postfix(slow.Behaviour, slowState);

// 2. Two consecutive calls with a changed native baseline between them.
Reset();
var consecutive = NewDrive();
StabilizerState.SetBlueprintFlag(consecutive.Motor.MotorBlueprintSlot.Blueprint, true);
Run(consecutive.Behaviour);
consecutive.Behaviour.MotorTorqueMultiplier = 5f;
object? secondState = Prefix(consecutive.Behaviour);
Require(consecutive.Behaviour.MotorTorqueMultiplier == 100f, "second call boosts again");
Postfix(consecutive.Behaviour, secondState);
Require(consecutive.Behaviour.MotorTorqueMultiplier == 5f, "second call restores the current baseline, not the first");

// 3. An external/native update during the call is not clobbered on restore.
Reset();
var external = NewDrive();
StabilizerState.SetBlueprintFlag(external.Motor.MotorBlueprintSlot.Blueprint, true);
object? externalState = Prefix(external.Behaviour);
external.Behaviour.MaxTraverseRate = 7f; // another patch/native write
Postfix(external.Behaviour, externalState);
Require(external.Behaviour.MotorTorqueMultiplier == 2f, "guard: untouched fields are restored");
Require(external.Behaviour.MaxTraverseRate == 7f, "guard: concurrently changed field is left alone");
Require(external.Behaviour.MaxAcceleration == 3f && external.Behaviour.MaxBrakeAcceleration == 4f, "guard: remaining fields restored");

// 4. Finalizer restores and preserves the original native exception.
// Harmony identifies the original exception by the reserved parameter name
// "__exception"; "__ex" is not injectable and is rejected by the real runtime.
// This metadata check is necessary but not sufficient: it cannot prove that
// Harmony binds the patch, so the coordinator still runs the real binding smoke.
var finalizerParameters = typeof(StabilizerRuntime).GetMethod("Finalizer", Hidden)!.GetParameters();
Require(
    finalizerParameters.Any(parameter =>
        parameter.Name == "__exception" && parameter.ParameterType == typeof(Exception)),
    "finalizer uses Harmony's __exception injection name");
Require(
    !finalizerParameters.Any(parameter => parameter.Name == "__ex"),
    "finalizer does not use the rejected __ex parameter name");

Reset();
var failing = NewDrive();
StabilizerState.SetBlueprintFlag(failing.Motor.MotorBlueprintSlot.Blueprint, true);
object? failingState = Prefix(failing.Behaviour);
var nativeException = new InvalidOperationException("controlled native failure");
object? returnedException = Finish(failing.Behaviour, failingState, nativeException);
Require(ReferenceEquals(returnedException, nativeException), "finalizer returns the original native exception unchanged");
Require(AtBase(failing.Behaviour), "finalizer restores the four values");
Postfix(failing.Behaviour, failingState);
Finish(failing.Behaviour, failingState, null);
Require(AtBase(failing.Behaviour), "postfix/finalizer cleanup is idempotent");

// 5. Partial prefix failure: fields written before the throw are restored.
Reset();
var partial = NewDrive();
StabilizerState.SetBlueprintFlag(partial.Motor.MotorBlueprintSlot.Blueprint, true);
partial.Behaviour.ThrowOnAccelerationSet = true;
object? partialState = Prefix(partial.Behaviour);
Require(partialState != null, "partial prefix failure keeps a cleanup scope");
Require(partial.Behaviour.MotorTorqueMultiplier == 100f && partial.Behaviour.MaxTraverseRate == 360f, "partial prefix wrote earlier fields before failing");
Require(partial.Behaviour.MaxAcceleration == 3f && partial.Behaviour.MaxBrakeAcceleration == 4f, "partial prefix did not complete later fields");
Postfix(partial.Behaviour, partialState);
Require(AtBase(partial.Behaviour), "partial prefix failure restores every field it changed");
partial.Behaviour.ThrowOnAccelerationSet = false;

// 6. Nested native calls: the inner call neither re-captures nor restores early.
Reset();
var nested = NewDrive();
StabilizerState.SetBlueprintFlag(nested.Motor.MotorBlueprintSlot.Blueprint, true);
object? outerState = Prefix(nested.Behaviour);
object? innerState = Prefix(nested.Behaviour);
Require(innerState != null && innerState != outerState, "nested call gets its own passive scope");
Require(AtDefaults(nested.Behaviour), "nested prefix does not re-capture boosted values as a baseline");
Postfix(nested.Behaviour, innerState);
Require(AtDefaults(nested.Behaviour), "nested postfix does not restore while the outer call is active");
Postfix(nested.Behaviour, outerState);
Require(AtBase(nested.Behaviour), "outer postfix restores the original baseline");
Run(nested.Behaviour);
Require(AtBase(nested.Behaviour), "nesting guard is released after the outer call");

// 7. Invalid configured floats never reach the native setters.
Reset();
var invalidConfig = NewDrive();
StabilizerState.SetBlueprintFlag(invalidConfig.Motor.MotorBlueprintSlot.Blueprint, true);
Plugin.BoostTorqueMultiplier.Value = float.NaN;
Plugin.BoostMaxTraverseRate.Value = float.PositiveInfinity;
Plugin.BoostMaxAcceleration.Value = 0f;
Plugin.BoostMaxBrakeAcceleration.Value = -5f;
object? invalidState = Prefix(invalidConfig.Behaviour);
Require(AtDefaults(invalidConfig.Behaviour), "invalid config falls back to finite positive defaults");
Require(
    float.IsFinite(invalidConfig.Behaviour.MotorTorqueMultiplier) &&
    float.IsFinite(invalidConfig.Behaviour.MaxTraverseRate) &&
    invalidConfig.Behaviour.MaxAcceleration > 0f &&
    invalidConfig.Behaviour.MaxBrakeAcceleration > 0f,
    "no NaN, infinity or nonpositive control parameter is written");
Postfix(invalidConfig.Behaviour, invalidState);
Require(AtBase(invalidConfig.Behaviour), "invalid-config call still restores");

// 8. Valid custom values are used exactly.
Reset();
var customConfig = NewDrive();
StabilizerState.SetBlueprintFlag(customConfig.Motor.MotorBlueprintSlot.Blueprint, true);
Plugin.BoostTorqueMultiplier.Value = 5f;
Plugin.BoostMaxTraverseRate.Value = 90f;
Plugin.BoostMaxAcceleration.Value = 1800f;
Plugin.BoostMaxBrakeAcceleration.Value = 2700f;
object? customState = Prefix(customConfig.Behaviour);
Require(
    customConfig.Behaviour.MotorTorqueMultiplier == 5f &&
    customConfig.Behaviour.MaxTraverseRate == 90f &&
    customConfig.Behaviour.MaxAcceleration == 1800f &&
    customConfig.Behaviour.MaxBrakeAcceleration == 2700f,
    "valid configured values are written unchanged");
Postfix(customConfig.Behaviour, customState);
Require(AtBase(customConfig.Behaviour), "custom-config call restores");

// 9. Master switch and per-drive flag both gate the boost.
Reset();
var disabled = NewDrive();
StabilizerState.SetBlueprintFlag(disabled.Motor.MotorBlueprintSlot.Blueprint, true);
Plugin.StabilizerEnabled.Value = false;
object? masterState = Prefix(disabled.Behaviour);
Require(disabled.Behaviour.MotorTorqueMultiplier == 2f, "master switch off means no boost");
Postfix(disabled.Behaviour, masterState);
Require(AtBase(disabled.Behaviour), "master-switch-off call leaves values untouched");

Reset();
var uncheckedDrive = NewDrive();
Plugin.StabilizerEnabled.Value = true;
object? uncheckedState = Prefix(uncheckedDrive.Behaviour);
Require(AtBase(uncheckedDrive.Behaviour), "unchecked drive is not boosted");
Postfix(uncheckedDrive.Behaviour, uncheckedState);
Require(AtBase(uncheckedDrive.Behaviour), "unchecked drive stays at its baseline");

// 9b. Unchecking leaves nothing boosted: no later runtime call is needed to clean up.
Reset();
var uncheckedAfterBoost = NewDrive();
Sprocket.Vehicles.MotorBlueprint uncheckedAfterBlueprint =
    uncheckedAfterBoost.Motor.MotorBlueprintSlot.Blueprint;
StabilizerState.SetBlueprintFlag(uncheckedAfterBlueprint, true);
Run(uncheckedAfterBoost.Behaviour);
Require(AtBase(uncheckedAfterBoost.Behaviour), "values are already restored before unchecking");
StabilizerState.SetBlueprintFlag(uncheckedAfterBlueprint, false);
Run(uncheckedAfterBoost.Behaviour);
Require(AtBase(uncheckedAfterBoost.Behaviour), "unchecked drive needs no deferred restore and stays at baseline");

// 10. Dated technology is read live; no stale per-pointer cache.
Reset();
var tech = NewDrive();
StabilizerState.SetBlueprintFlag(tech.Motor.MotorBlueprintSlot.Blueprint, true);
Require(StabilizerState.TechEnabled(tech.Motor), "technology initially enabled");
tech.Motor.Vehicle.Tech.Enabled = false;
Require(!StabilizerState.TechEnabled(tech.Motor), "technology loss is observed immediately");
object? noTechState = Prefix(tech.Behaviour);
Require(tech.Behaviour.MotorTorqueMultiplier == 2f, "lost technology means no boost");
Postfix(tech.Behaviour, noTechState);
tech.Motor.Vehicle.Tech.Enabled = true;
Require(StabilizerState.TechEnabled(tech.Motor), "technology gain is observed immediately");
Run(tech.Behaviour);
Require(AtBase(tech.Behaviour), "technology re-enabled call restores");

// 11. Missing/malformed saved value explicitly defaults false.
Reset();
var missing = NewDrive();
StabilizerKey missingKey = StabilizerState.KeyOf(missing.Motor);
StabilizerState.SetFlag(missingKey, true);
Load(missing.Motor, new SerializationInfo());
Require(!StabilizerState.GetFlag(missingKey), "missing save key resets an existing true flag to false");

StabilizerState.SetFlag(missingKey, true);
var malformedInfo = new SerializationInfo();
malformedInfo.Values[StabilizerState.SaveKey] = "not-a-bool";
Load(missing.Motor, malformedInfo);
Require(!StabilizerState.GetFlag(missingKey), "malformed save value defaults false");

StabilizerState.SetFlag(missingKey, true);
var falseInfo = new SerializationInfo();
falseInfo.AddValue(StabilizerState.SaveKey, false);
Load(missing.Motor, falseInfo);
Require(!StabilizerState.GetFlag(missingKey), "explicit saved false loads false");

// 12. Save/load round trip keeps the key and applies it to the loaded identity.
Reset();
var saved = NewDrive();
StabilizerState.SetFlag(StabilizerState.KeyOf(saved.Motor), true);
var saveInfo = new SerializationInfo();
Save(saved.Motor, saveInfo);
Require(saveInfo.GetBoolean(StabilizerState.SaveKey), "save writes the existing key name and true value");

var loaded = NewDrive(7, 71, 701);
Load(loaded.Motor, saveInfo);
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(loaded.Motor)), "load applies the value to the loaded component identity");
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(saved.Motor)), "load does not disturb another component");

StabilizerState.SetFlag(StabilizerState.KeyOf(saved.Motor), false);
var falseSave = new SerializationInfo();
Save(saved.Motor, falseSave);
Require(!falseSave.GetBoolean(StabilizerState.SaveKey), "save writes false when unchecked");

// 13. A changed VUID on the same live component replaces, not duplicates, state.
Reset();
var reloaded = NewDrive();
StabilizerState.SetFlag(StabilizerState.KeyOf(reloaded.Motor), true);
reloaded.Motor.VUID = new Sprocket.Id(99);
Load(reloaded.Motor, saveInfo);
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(reloaded.Motor)), "postfix load uses the completed identity");
Require(StabilizerState.TrackedFlagCount == 1, "stale pre-load identity is removed for the same component");

Reset();
var movedVehicle = NewDrive();
StabilizerState.SetFlag(StabilizerState.KeyOf(movedVehicle.Motor), true);
movedVehicle.Motor.Vehicle.Pointer = (IntPtr)777;
Load(movedVehicle.Motor, saveInfo);
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(movedVehicle.Motor)), "postfix load follows a changed vehicle identity");
Require(StabilizerState.TrackedFlagCount == 1, "changed vehicle identity replaces the same component's state");

// 14. Vehicle/component identity isolation and exact release cleanup.
Reset();
var vehicleA = NewDrive(7, 70, 700);
var vehicleB = NewDrive(7, 80, 800); // same VUID text, different vehicle/component
StabilizerState.SetFlag(StabilizerState.KeyOf(vehicleA.Motor), true);
Require(!StabilizerState.GetFlag(StabilizerState.KeyOf(vehicleB.Motor)), "same VUID on another vehicle is isolated");
StabilizerState.SetFlag(StabilizerState.KeyOf(vehicleB.Motor), true);
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(vehicleA.Motor)), "vehicle A flag survives vehicle B edit");
Release(vehicleA.Motor);
Require(!StabilizerState.GetFlag(StabilizerState.KeyOf(vehicleA.Motor)), "release clears the exact component");
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(vehicleB.Motor)), "release does not clear another vehicle");
Require(StabilizerState.TrackedFlagCount == 1, "release leaves exactly the unrelated entry");

var sameVehicleA = NewDrive(9, 90, 900);
var sameVehicleB = NewDrive(9, 91, 900); // same vehicle and VUID, different component
StabilizerState.SetFlag(StabilizerState.KeyOf(sameVehicleA.Motor), true);
Require(!StabilizerState.GetFlag(StabilizerState.KeyOf(sameVehicleB.Motor)), "same VUID in one vehicle is component-scoped");
StabilizerState.SetFlag(StabilizerState.KeyOf(sameVehicleB.Motor), true);
Release(sameVehicleA.Motor);
Require(!StabilizerState.GetFlag(StabilizerState.KeyOf(sameVehicleA.Motor)), "release clears the exact component in a vehicle");
Require(StabilizerState.GetFlag(StabilizerState.KeyOf(sameVehicleB.Motor)), "release keeps the sibling component");

// 15. Plugin unload clears all flags and the nesting guard safely.
Reset();
var unloaded = NewDrive();
StabilizerState.SetFlag(StabilizerState.KeyOf(unloaded.Motor), true);
StabilizerRuntime.ResetForUnload();
StabilizerState.ClearAll();
Require(StabilizerState.TrackedFlagCount == 0, "unload clears every stored flag");
Require(!StabilizerState.GetFlag(StabilizerState.KeyOf(unloaded.Motor)), "unloaded component reports false");
Postfix(unloaded.Behaviour, null);
Finish(unloaded.Behaviour, null, null);
Require(AtBase(unloaded.Behaviour), "null __state cleanup is safe");

// 16. Defensive identity helpers and a behaviour without a turret ring.
Reset();
Require(!StabilizerState.KeyOf(null).IsValid, "null component has no identity");
StabilizerState.SetFlag(default, true);
Require(StabilizerState.TrackedFlagCount == 0, "invalid identity is not stored");
var orphan = new TurretBehaviour
{
    Pointer = (IntPtr)500,
    MotorTorqueMultiplier = 2f,
    MaxTraverseRate = 20f,
    MaxAcceleration = 3f,
    MaxBrakeAcceleration = 4f
};
object? orphanState = Prefix(orphan);
Require(AtBase(orphan), "behaviour without a turret ring is untouched");
Postfix(orphan, orphanState);

// 17. Blueprint object-state persistence: the actual design save/load route.
// The legacy component SaveData route is not called by the native outer
// ToBlueprint path; the motor blueprint's Save/Load string entry is.
Reset();
var blueprintDrive = NewDrive();
Sprocket.Vehicles.MotorBlueprint motorBlueprint = blueprintDrive.Motor.MotorBlueprintSlot.Blueprint;
StabilizerState.SetBlueprintFlag(motorBlueprint, true);

var blueprintSerializer = new FakeBlueprintSerializer();
SaveMotorBlueprint(motorBlueprint, blueprintSerializer);
Require(
    blueprintSerializer.Values.TryGetValue(StabilizerState.BlueprintSaveKey, out string? savedBlueprintValue) &&
    savedBlueprintValue == "1",
    "blueprint save writes the namespaced string entry when stabilized");

var reloadedMotorBlueprint = new Sprocket.Vehicles.MotorBlueprint { Pointer = (IntPtr)901 };
LoadMotorBlueprint(reloadedMotorBlueprint, new FakeBlueprintDeserializer(blueprintSerializer.Values));
Require(StabilizerState.GetBlueprintFlag(reloadedMotorBlueprint), "blueprint load applies the saved value");

var copiedMotorBlueprint = new Sprocket.Vehicles.MotorBlueprint { Pointer = (IntPtr)902 };
StabilizerState.CopyBlueprintFlag(reloadedMotorBlueprint, copiedMotorBlueprint);
Require(StabilizerState.GetBlueprintFlag(copiedMotorBlueprint), "structured blueprint copy carries the flag");

// Clone is virtual and its interop result is Il2CppSystem.Object; the 0.7.0
// postfix declared Blueprint and made the whole PatchAll fail. This regression
// invokes the corrected postfix with the boxed result type.
var clonedMotorBlueprint = new Sprocket.Vehicles.MotorBlueprint { Pointer = (IntPtr)903 };
CloneMotorBlueprint(reloadedMotorBlueprint, clonedMotorBlueprint);
Require(StabilizerState.GetBlueprintFlag(clonedMotorBlueprint), "clone postfix accepts the boxed Il2CppSystem.Object result");

StabilizerState.SetBlueprintFlag(motorBlueprint, false);
var uncheckedSerializer = new FakeBlueprintSerializer();
SaveMotorBlueprint(motorBlueprint, uncheckedSerializer);
Require(
    !uncheckedSerializer.Values.ContainsKey(StabilizerState.BlueprintSaveKey),
    "unchecked drive writes no blueprint entry");

LoadMotorBlueprint(reloadedMotorBlueprint, new FakeBlueprintDeserializer(new Dictionary<string, string>()));
Require(
    !StabilizerState.GetBlueprintFlag(reloadedMotorBlueprint),
    "missing blueprint entry loads false");

// 18. The blueprint flag is kept separate from the legacy component identity.
Reset();
var isolated = NewDrive();
StabilizerState.SetFlag(StabilizerState.KeyOf(isolated.Motor), true);
Require(
    !StabilizerState.GetBlueprintFlag(isolated.Motor.MotorBlueprintSlot.Blueprint),
    "legacy component flag does not enable the blueprint checkbox");
StabilizerState.SetBlueprintFlag(isolated.Motor.MotorBlueprintSlot.Blueprint, true);
Require(
    StabilizerState.GetBlueprintFlag(isolated.Motor.MotorBlueprintSlot.Blueprint),
    "blueprint flag round-trips through the state store");
Require(
    StabilizerState.TrackedBlueprintFlagCount == 1,
    "only the blueprint identity is tracked");
Require(
    StabilizerState.GetFlag(StabilizerState.KeyOf(isolated.Motor)),
    "blueprint and legacy component state stay independent");

Console.WriteLine(
    $"{checks} stabilizer regression checks passed using the real runtime/state/persistence source. " +
    "All native/Unity/Harmony APIs are controlled doubles; no in-game acceptance is claimed.");

sealed class FakeBlueprintSerializer : Sprocket.Blueprints.IBlueprintSerializer
{
    public Dictionary<string, string> Values = new();
    public void AddValue(string key, string value) => Values[key] = value;
}

sealed class FakeBlueprintDeserializer : Sprocket.Blueprints.IBlueprintDeserializer
{
    private readonly Dictionary<string, string> _values;
    public FakeBlueprintDeserializer(Dictionary<string, string> values) => _values = values;
    public string GetString(string key)
        => _values.TryGetValue(key, out string? value)
            ? value
            : throw new InvalidOperationException("missing blueprint key");
}
