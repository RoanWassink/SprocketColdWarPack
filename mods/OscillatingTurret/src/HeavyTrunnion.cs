using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace SprocketOscillatingTurret;

/// <summary>
/// Heavy Trunnion Mount (0.3.0): a mantlet on the native trunnions with its own laying drive.
/// Every hook returns at once unless the drive carries the heavyTrunnionDrive tag or the mantlet
/// names the heavy drive GUID, so native mantlets, drives and the oscillating ring see nothing.
/// Own Harmony owner: a failure here never disables the oscillating turret patches.
/// </summary>
internal static class HeavyTrunnion
{
    internal const string Owner = "sprocket.oscillatingturret.heavy";
    internal static HeavyTrunnionPolicy.Settings Settings = HeavyTrunnionPolicy.Defaults;

    private static Harmony? _harmony;
    // Native elevation TorqueMultiplier per axis pointer while LayingDrive.EnableBehaviour runs.
    private static readonly Dictionary<IntPtr, float> PendingTorque = new();
    // Native trunnion and drive models whose renderer stays hidden (colliders untouched).
    private static readonly HashSet<IntPtr> HiddenModels = new();
    private static readonly Dictionary<IntPtr, string> LastBuildLine = new();
    private static readonly HashSet<string> SeenEnable = new();
    private static readonly HashSet<string> Warnings = new();
    private static Sprite? _icon;
    private static bool _iconFailed, _iconLogged;

    internal static void Load(ConfigFile config)
    {
        const string section = "Heavy Trunnion";
        var torque = config.Bind(section, "TorqueFactor", HeavyTrunnionPolicy.DefaultTorqueFactor,
            "Elevation torque of the Heavy Trunnion Mount as a multiple of the native drive (recommended 1-4). Top speed stays native.");
        var mass = config.Bind(section, "ExtraMassKg", HeavyTrunnionPolicy.DefaultExtraMassKg,
            "Mass the Heavy Trunnion Mount adds, in kg. Never below 0.");
        var cost = config.Bind(section, "CostFactor", HeavyTrunnionPolicy.DefaultCostFactor,
            "Cost multiple on the native mechanism price of the added mass. Never below 1.");
        var distance = config.Bind(section, "MaxDistanceFactor", HeavyTrunnionPolicy.DefaultMaxDistanceFactor,
            "Allowed distance between the laying drive and the trunnions, as a multiple of the native 1 m.");
        Settings = HeavyTrunnionPolicy.Sanitize(torque.Value, mass.Value, cost.Value, distance.Value);
        Plugin.Info(
            I($"[Heavy trunnion] TorqueFactor={Settings.TorqueFactor:0.###} ExtraMassKg={Settings.ExtraMassKg:0.#} ") +
            I($"CostFactor={Settings.CostFactor:0.###} (cost {HeavyTrunnionPolicy.Cost(Settings):0.#}) ") +
            I($"MaxDistanceFactor={Settings.MaxDistanceFactor:0.###} (limit {HeavyTrunnionPolicy.MaxDistance(Settings):0.##} m)."));
    }

    internal static void Install()
    {
        _harmony = new Harmony(Owner);
        var required = new List<string>();
        var optional = new List<string>();
        Patch(typeof(LayingDrive), nameof(LayingDrive.EnableBehaviour), nameof(EnableBehaviourPrefix), nameof(EnableBehaviourPostfix), required);
        Patch(typeof(LayingDrive), nameof(LayingDrive.Build), null, nameof(DriveBuildPostfix), required);
        Patch(typeof(Mantlet), nameof(Mantlet.Build), null, nameof(MantletBuildPostfix), required);
        if (required.Count > 0)
        {
            foreach (string failure in required) Plugin.Error("[Heavy trunnion] patch failure: " + failure);
            _harmony.UnpatchSelf();
            _harmony = null;
            Plugin.Error("[Heavy trunnion] Heavy Trunnion Mount hooks disabled; the oscillating turret is unaffected.");
            return;
        }

        Patch(typeof(LayingDrive), "ValidateTrunnionDistance", null, nameof(ValidateDistancePostfix), optional);
        Patch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.SetVisible), nameof(KeepHiddenPrefix), null, optional);
        Patch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build), null, nameof(ModelBuildPostfix), optional);
        Patch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard), null, nameof(CreateCardPostfix), optional);
        foreach (string failure in optional) Plugin.Warn("[Heavy trunnion] optional hook unavailable: " + failure);
        Plugin.Info("[Heavy trunnion] Heavy Trunnion Mount hooks installed (mantlet " + HeavyTrunnionPolicy.MantletGuid +
                    ", drive " + HeavyTrunnionPolicy.DriveGuid + ").");
    }

    private static void Patch(Type type, string method, string? prefix, string? postfix, List<string> failures)
    {
        try
        {
            // Declared on the type itself, never an inherited (possibly shared, empty) base stub.
            MethodInfo? original = AccessTools.DeclaredMethod(type, method);
            if (original == null) { failures.Add(type.Name + "." + method + " was not found."); return; }
            _harmony!.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(HeavyTrunnion), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(HeavyTrunnion), postfix));
        }
        catch (Exception ex)
        {
            failures.Add(type.Name + "." + method + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal static void Uninstall()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        HeavyTrunnionVisuals.Clear();
        PendingTorque.Clear();
        HiddenModels.Clear();
        LastBuildLine.Clear();
        SeenEnable.Clear();
        Warnings.Clear();
    }

    internal static void Release(IntPtr component)
    {
        HeavyTrunnionVisuals.Release(component);
        HiddenModels.Remove(component);
        LastBuildLine.Remove(component);
    }

    // ---------- scope ----------

    internal static bool IsHeavyDrive(LayingDrive? drive)
    {
        if (drive == null) return false;
        var vehicleObject = drive.VehicleObject;
        return vehicleObject != null && HeavyTrunnionPolicy.IsHeavyDrive(vehicleObject.HasTag);
    }

    internal static bool IsHeavyMantlet(Mantlet? mantlet) =>
        mantlet != null && HeavyTrunnionPolicy.IsHeavyMantlet(mantlet.LayingDrivePartGuid);

    private static LayingDriveBlueprint.Axis? ElevationAxis(LayingDrive drive)
    {
        var slot = drive.BlueprintSlot;
        if (slot == null || !slot.HasBlueprint) return null;
        return slot.Blueprint?.Elevation;
    }

    // ---------- hook 1+2: stronger, not faster ----------

    // Native EnableBehaviour builds the elevation RotationAxisInfo inline from the blueprint axis
    // (MotorTorque = Ratio * 125 * TorqueMultiplier, MaxSpeed = 180 / Ratio) and hands it to
    // LayingDrivesMain.Create. Scaling the multiplier for the length of the call scales only the
    // torque; the blueprint value is restored before anything can save or show it.
    private static void EnableBehaviourPrefix(LayingDrive __instance)
    {
        try
        {
            if (!IsHeavyDrive(__instance)) return;
            var axis = ElevationAxis(__instance);
            if (axis == null) return;
            // A call that never reached its postfix left the scaled value behind: start from the native one.
            float native = PendingTorque.TryGetValue(axis.Pointer, out float stale) ? stale : axis.TorqueMultiplier;
            PendingTorque[axis.Pointer] = native;
            axis.TorqueMultiplier = HeavyTrunnionPolicy.ScaledTorqueMultiplier(native, Settings);
        }
        catch (Exception ex) { WarnOnce("enable prefix", ex); }
    }

    private static void EnableBehaviourPostfix(LayingDrive __instance)
    {
        try
        {
            if (!IsHeavyDrive(__instance)) return;
            var axis = ElevationAxis(__instance);
            if (axis == null || !PendingTorque.Remove(axis.Pointer, out float native)) return;
            axis.TorqueMultiplier = native;
            if (Diagnostics.Detailed) LogEnable(__instance, axis, native);
        }
        catch (Exception ex) { WarnOnce("enable postfix", ex); }
    }

    private static void LogEnable(LayingDrive drive, LayingDriveBlueprint.Axis axis, float native)
    {
        float ratio = axis.Ratio;
        string actual = "behaviour=none";
        bool slower = true;
        var behaviour = drive.Behaviour is Il2CppObjectBase wrapper ? wrapper.TryCast<LayingDriveBehaviour>() : null;
        if (behaviour != null)
        {
            var info = behaviour.elevationInfo;
            slower = info.MaxSpeed <= HeavyTrunnionPolicy.MaxSpeed(ratio) + 1e-4f;
            actual = FormattableString.Invariant($"elevationInfo torque={info.MotorTorque:0.#} maxSpeed={info.MaxSpeed:0.####}");
        }

        string line =
            I($"[Heavy trunnion] enable drive={Id(drive)} ratio={ratio:0.###} torque {HeavyTrunnionPolicy.MotorTorque(ratio, native):0.#} -> ") +
            I($"{HeavyTrunnionPolicy.MotorTorque(ratio, HeavyTrunnionPolicy.ScaledTorqueMultiplier(native, Settings)):0.#} ") +
            I($"maxSpeed {HeavyTrunnionPolicy.MaxSpeed(ratio):0.####} -> {HeavyTrunnionPolicy.MaxSpeed(ratio):0.####}; {actual} ") +
            I($"notFaster={slower} blueprintTorqueMultiplier={axis.TorqueMultiplier:0.###}");
        if (SeenEnable.Add(drive.Pointer.ToInt64().ToString("X") + line)) Plugin.Info(line);
        if (!slower) Plugin.Warn("[Heavy trunnion] native MaxSpeed above 180/ratio; report this log.");
    }

    // ---------- hook 3: mass, cost and reach ----------

    // Native LayingDrive and Mantlet never call SetMass/SetCost (only the drive model would, and its
    // JSON has no mass), so the native drive weighs and costs 0. Set absolute values like
    // TraverseMotor.Build does, then refresh the cached mass and centre as native Build code does.
    private static void ApplyMassCost(LayingDrive drive)
    {
        drive.SetMass(HeavyTrunnionPolicy.Mass(Settings), MassType.Mechanisms);
        drive.SetCost(HeavyTrunnionPolicy.Cost(Settings), MassType.Mechanisms, CostType.Assembly);
        drive.cachedMass = drive.GetMass(MassType.Everything);
        drive.RecalculateCenterOfMass();
    }

    private static void DriveBuildPostfix(LayingDrive __instance)
    {
        try
        {
            if (!IsHeavyDrive(__instance)) return;
            ApplyMassCost(__instance);
            HideModel(__instance.model);
        }
        catch (Exception ex) { WarnOnce("drive build", ex); }
    }

    // Native validity: distance(drive, trunnions) < 1 m (a literal in ValidateTrunnionDistance).
    private static void ValidateDistancePostfix(LayingDrive __instance)
    {
        try
        {
            if (!IsHeavyDrive(__instance)) return;
            var area = __instance.Trunnions;
            var a = __instance.VehicleTransform;
            var b = area?.VehicleTransform;
            if (a == null || b == null) return;
            float distance = Vector3.Distance(a.Position, b.Position);
            VehicleComponentMarking.SetValid(__instance, distance < HeavyTrunnionPolicy.MaxDistance(Settings));
        }
        catch (Exception ex) { WarnOnce("distance", ex); }
    }

    // ---------- hook 4: model ----------

    private static void MantletBuildPostfix(Mantlet __instance)
    {
        try
        {
            if (!IsHeavyMantlet(__instance)) return;
            var drive = __instance.layingDrive;
            bool heavyDrive = IsHeavyDrive(drive);
            if (heavyDrive) ApplyMassCost(drive!);

            var trunnionModel = __instance.trunnionModel?.TryCast<VehicleObjectModel>();
            int layer = 0;
            try { layer = trunnionModel?.Model?.Layer ?? __instance.gameObject.layer; } catch { }
            string meshes = HeavyTrunnionVisuals.Attach(__instance, layer);
            bool trunnionHidden = HideModel(__instance.trunnionModel);
            bool driveHidden = heavyDrive && HideModel(drive!.model);
            if (Diagnostics.Detailed) LogBuild(__instance, drive, heavyDrive, meshes, trunnionHidden, driveHidden);
        }
        catch (Exception ex) { WarnOnce("mantlet build", ex); }
    }

    private static void LogBuild(Mantlet mantlet, LayingDrive? drive, bool heavyDrive, string meshes, bool trunnionHidden, bool driveHidden)
    {
        string torque = "drive=missing";
        string massCost = "mass=? cost=?";
        if (drive != null)
        {
            var axis = ElevationAxis(drive);
            if (axis != null)
            {
                float ratio = axis.Ratio, native = axis.TorqueMultiplier;
                torque =
                    I($"torque {HeavyTrunnionPolicy.MotorTorque(ratio, native):0.#} -> ") +
                    I($"{HeavyTrunnionPolicy.MotorTorque(ratio, HeavyTrunnionPolicy.ScaledTorqueMultiplier(native, Settings)):0.#} ") +
                    I($"(x{Settings.TorqueFactor:0.##}) maxSpeed {HeavyTrunnionPolicy.MaxSpeed(ratio):0.####} -> {HeavyTrunnionPolicy.MaxSpeed(ratio):0.####}");
            }

            massCost = FormattableString.Invariant(
                $"mass={drive.GetMass(MassType.Mechanisms):0.#}kg cached={drive.cachedMass:0.#} cost={drive.GetCost(MassType.Mechanisms, CostType.Everything):0.#}");
        }

        Vector3 scale = mantlet.VehicleTransform != null ? mantlet.VehicleTransform.Scale : Vector3.one;
        string line =
            I($"[Heavy trunnion] build mantlet={Id(mantlet)} part={HeavyTrunnionPolicy.MantletGuid} trunnion={mantlet.TrunnionPartGuid} ") +
            I($"trunnionsScale={mantlet.TrunnionsScale:0.###} partScale=({scale.x:0.###},{scale.y:0.###},{scale.z:0.###}) ") +
            I($"drive={mantlet.LayingDrivePartGuid} heavyTag={heavyDrive} {torque} {massCost} ") +
            I($"meshes={meshes} hidden(trunnion={trunnionHidden},drive={driveHidden})");
        if (LastBuildLine.TryGetValue(mantlet.Pointer, out string? last) && last == line) return;
        LastBuildLine[mantlet.Pointer] = line;
        Plugin.Info(line);
    }

    // Hides only the renderer (VehicleObjectModel.SetVisible stores the flag and forwards it to the
    // renderer group); colliders and the placement surface stay.
    private static bool HideModel(IVehicleModelComponent? component)
    {
        var model = component?.TryCast<VehicleObjectModel>();
        if (model == null) return false;
        HiddenModels.Add(model.Pointer);
        model.SetVisible(false);
        return true;
    }

    private static void KeepHiddenPrefix(VehicleObjectModel __instance, ref bool __0)
    {
        if (__0 && HiddenModels.Count > 0 && HiddenModels.Contains(__instance.Pointer)) __0 = false;
    }

    private static void ModelBuildPostfix(VehicleObjectModel __instance)
    {
        try
        {
            if (HiddenModels.Count > 0 && HiddenModels.Contains(__instance.Pointer)) __instance.SetVisible(false);
        }
        catch (Exception ex) { WarnOnce("model build", ex); }
    }

    // ---------- hook 5: icon ----------

    // PartDefinitionCardFactory.CreateCard sets Icon = GameIcons.GetOrDefault(part name); the
    // picker never renders the model itself, so a new part would get the default icon.
    private static void CreateCardPostfix(PartDefinition __0, PartDisplayCard __result)
    {
        try
        {
            if (__0 == null || __result == null ||
                !string.Equals(__0.guid, HeavyTrunnionPolicy.MantletGuid, StringComparison.OrdinalIgnoreCase)) return;
            var icon = Icon();
            if (icon != null) __result.Icon = icon;
            if (!_iconLogged)
            {
                _iconLogged = true;
                Plugin.Info("[Heavy trunnion] picker icon " + (icon != null ? "set from the embedded PNG" : "unavailable; native default kept") + ".");
            }
        }
        catch (Exception ex) { WarnOnce("icon", ex); }
    }

    private static Sprite? Icon()
    {
        if (_icon != null || _iconFailed) return _icon;
        try
        {
            using var stream = typeof(HeavyTrunnion).Assembly.GetManifestResourceStream("heavy-trunnion-icon.png");
            if (stream == null) throw new InvalidOperationException("embedded icon missing");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var texture = new Texture2D(2, 2) { name = "Heavy trunnion icon", hideFlags = HideFlags.HideAndDontSave };
            if (!ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)bytes, false)) throw new InvalidOperationException("PNG decode failed");
            _icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            _icon.hideFlags = HideFlags.HideAndDontSave;
        }
        catch (Exception ex)
        {
            _iconFailed = true;
            WarnOnce("icon load", ex);
        }

        return _icon;
    }

    // ---------- helpers ----------

    private static string I(FormattableString text) => FormattableString.Invariant(text);

    private static string Id(VehicleComponent component)
    {
        try { return component.VUID.Value.ToString(CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }

    private static void WarnOnce(string where, Exception ex)
    {
        if (Warnings.Add(where + ":" + ex.Message)) Plugin.Warn("[Heavy trunnion] " + where + ": " + ex.GetType().Name + ": " + ex.Message);
    }
}
