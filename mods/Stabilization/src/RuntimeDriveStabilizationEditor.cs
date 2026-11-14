using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Turrets;
using Sprocket.VehicleDesigner.Access.Editors;
using VehicleDesigner.Compartments.Design;
namespace SprocketStabilization;

// Editor-only native drive controls. No controller, physics or sight patches.
[HarmonyPatch]
internal static class RuntimeDriveStabilizationEditor
{
    private sealed record State(LayingDriveEditor Editor,LayingDrive Drive,TraverseMotor Motor,
        IntPtr DriveBlueprint,IntPtr MotorBlueprint,IntPtr MotorTech, StabilizationSettings Settings, LayingDriveProperties.AxisProperties AxisGroup,
        FloatProperty Strength,MotorProperties MotorGroup)
    {
        internal Il2CppSystem.EventHandler? MotorRebuilt;
    }
    private static readonly Dictionary<IntPtr,State> states=new();
    private static readonly HashSet<string> warnings=new();
    private static bool expanded=true;
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list)
        =>list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;

    private static bool Resolve(LayingDrive drive,out TraverseMotor? motor,out string reason)
    {
        motor=null;reason="No uniquely connected turret motor; two-axis controls unavailable.";
        if(drive.VehicleObject==null||drive.VehicleObject.Released)return false;
        var ring=drive.GetVehicleComponentInParent<TurretRing>();
        if(ring?.motor==null||!ring.motor.Connected||ring.motor.Vehicle.Pointer!=drive.Vehicle.Pointer)return false;
        var candidate=ring.motor;
        var d=drive.BlueprintSlot?.Blueprint;var m=candidate.MotorBlueprintSlot?.Blueprint;
        if(d==null||m==null)return false;
        int driveRefs=0,motorRefs=0,ringRefs=0;
        var items=drive.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(items);i++)
        {
            var parts=items[i].Components;
            for(int j=0;j<Count(parts);j++)
            {
                var component=parts[j];
                if(component.TryCast<LayingDrive>() is {} ld&&ld.BlueprintSlot?.Blueprint?.Pointer==d.Pointer)driveRefs++;
                if(component.TryCast<TraverseMotor>() is {} tm&&tm.MotorBlueprintSlot?.Blueprint?.Pointer==m.Pointer)motorRefs++;
                if(component.TryCast<TurretRing>() is {} tr&&tr.motor?.Pointer==candidate.Pointer)ringRefs++;
            }
        }
        if(!DriveSetupRules.UniqueOwnership(driveRefs,motorRefs,ringRefs))
        {reason="Shared or ambiguous drive/motor design: make the native design independent before editing here.";return false;}
        var value=d.Elevation.TorqueMultiplier;
        if(!float.IsFinite(value)||value<=0){reason="Existing elevation strength is not a positive finite value; unchanged.";return false;}
        motor=candidate;return true;
    }

    internal static void ReleaseAll(){foreach(var key in states.Keys.ToArray())Remove(key);}
    private static void Remove(IntPtr key)
    {
        if(!states.Remove(key,out var state))return;
        // Release only our additions, before the owning native editor detaches.
        if(state.MotorRebuilt!=null)state.Motor.remove_Rebuilt(state.MotorRebuilt);
        state.AxisGroup.Deregister(state.Strength);
        // Follow native PropertyGroup.Release: deregister, retain property setters
        // for existing native undo operations. Do not destroy replay targets.
        state.MotorGroup.Release();
    }

    [HarmonyPrefix,HarmonyPatch(typeof(VehicleComponentEditor<LayingDrive>),nameof(VehicleComponentEditor<LayingDrive>.Release))]
    private static void Released(Il2CppSystem.Object __instance)
    {
        // The native generic Release body is shared: no changes for other editors.
        if(!states.ContainsKey(__instance.Pointer))return;
        try{Remove(__instance.Pointer);}catch(Exception ex){Warn("Release",ex);}
    }
    private static void Warn(string area,Exception ex)
    {if(warnings.Add(area+ex.Message))Plugin.ModLog.LogWarning("[Drive setup] "+area+": "+ex.Message);}

    [HarmonyPostfix,HarmonyPatch(typeof(LayingDriveEditor),nameof(LayingDriveEditor.OnGUI))]
    private static void Draw(LayingDriveEditor __instance,IGUILayout __0)
    {
        try
        {
            var ui=__0.TryCast<IGUIElementDrawer>();var drive=__instance.Component;
            if(ui==null||drive==null)return;
            if(!StabilizationTechnology.TryRead(drive.Vehicle.Tech,out var settings)){Remove(__instance.Pointer);return;}
            __0.EndAllDropdowns();
            __0.BeginDropdown("Stabilizer — elevation drive strength",expanded,Ui.BoolCallback(v=>expanded=v));
            try
            {
                if(!Resolve(drive,out var motor,out var reason)){Remove(__instance.Pointer);ui.InfoField(reason,2);return;}
                var driveBlueprint=drive.BlueprintSlot.Blueprint;var motorBlueprint=motor!.MotorBlueprintSlot.Blueprint;
                if(states.TryGetValue(__instance.Pointer,out var existing)&&
                    (existing.DriveBlueprint!=driveBlueprint.Pointer||existing.MotorBlueprint!=motorBlueprint.Pointer||existing.Motor.Pointer!=motor.Pointer||existing.MotorTech!=motor.MotorTech.Pointer||existing.AxisGroup.Pointer!=__instance.props.Elevation.Pointer||existing.Settings!=settings))
                    Remove(__instance.Pointer);
                if(!states.TryGetValue(__instance.Pointer,out var state))
                {
                    var axis=driveBlueprint.Elevation;
                    var limits=DriveSetupRules.StrengthLimits(axis.TorqueMultiplier,settings.Scale,settings.MaxStrength);
                    var get=DelegateSupport.ConvertDelegate<Il2CppSystem.Func<float>>(new Func<float>(()=>axis.TorqueMultiplier/settings.Scale));
                    var set=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(new Action<float>(value=>
                    {
                        var accepted=DriveSetupRules.ValidStrengthEdit(value,limits.Min,limits.Max);
                        if(accepted)axis.TorqueMultiplier=value*settings.Scale;
                    }));
                    // Existing native register supplies edit operations/dirty notification.
                    // Extend bounds for imported values; selection never writes/clamps them.
                    var strength=new FloatProperty("Stabilizer — elevation drive strength",get,set,limits.Min,limits.Max,.01f,"0.##",null,
                        "Stabilizer elevation drive strength: increases native torque and moved mass; the stabilizer rate caps still limit top speed.");
                    var motorRegister=__instance.NewPropertyRegister(motorBlueprint,true);
                    var motorGroup=new MotorProperties(motorBlueprint,motorRegister,MotorDesignText.Default,motor.MotorTech);
                    __instance.props.Elevation.Register(strength);
                    state=new(__instance,drive,motor,driveBlueprint.Pointer,motorBlueprint.Pointer,motor.MotorTech.Pointer,settings,__instance.props.Elevation,strength,motorGroup);
                    states.Add(__instance.Pointer,state);
                    // The selected editor listens to the laying drive, not this motor.
                    // Mirror TraverseMotorEditor.OnComponentRebuilt for native readouts,
                    // then use native editor redraw. No forced component/physics rebuild.
                    var observed=state;
                    state.MotorRebuilt=DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler>(
                        new Action<Il2CppSystem.Object,Il2CppSystem.EventArgs>((sender,args)=>
                        {
                            try
                            {
                                if(!states.TryGetValue(__instance.Pointer,out var active)||!ReferenceEquals(active,observed))return;
                                if(motor.MotorBlueprintSlot.Blueprint.Pointer!=active.MotorBlueprint)return;
                                active.MotorGroup.OnRebuild(motor.GetMovedMass());
                                __instance.OnRedrawRequested(sender,args);
                            }
                            catch(Exception ex){Warn("Motor refresh",ex);}
                        }));
                    motor.add_Rebuilt(state.MotorRebuilt);
                }
                ui.Float(state.Strength);
                if(driveBlueprint.Elevation.TorqueMultiplier>settings.MaxStrength||driveBlueprint.Elevation.TorqueMultiplier<1)
                    ui.InfoField($"Existing strength {driveBlueprint.Elevation.TorqueMultiplier:0.##} is preserved until edited.",2);
                ui.InfoField("Stabilizer elevation drive strength; the turret motor Torque/Ratio live on the traverse motor inspector.",2);
            }
            finally{__0.EndAllDropdowns();}
        }
        catch(Exception ex){Warn("Editor",ex);}
    }
}


