using DesignerFramework.AssetManagement;
using HarmonyLib;
using Sprocket.Selection;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using Sprocket.Vehicles.ProceduralMeshGeneration.ThreeSplitTurret;
using Sprocket.Vehicles.Turrets;
using UnityEngine;

namespace SprocketExpandedBallistics;

// One selector option creates two native structures/drives and a real sight.
// All objects, transforms, meshes, motors and undo entries remain native.
[HarmonyPatch]
internal static class RuntimeAtgmGimbalPreset
{
    internal const string Owner="sprocket.expandedballistics.atgm.gimbal-preset";
    private const string Name="ATGM sight mount (azimuth + elevation)";
    internal const float YawTorque=2000,PitchTorque=2000;
    // Native ValidateMotorPosition: connected = distance(motor, ring) < ring.motorMaxDistanceFromRing.
    // Rings carrying the dedicated guidance sight let the motor sit at the gunner.
    internal const float RemoteMotorDistance=8;
    private static readonly Dictionary<IntPtr,OptionGroup> groups=new();
    private static bool Available=>BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.ContainsKey("sprocket.oscillatingturret")&&Harmony.HasAnyPatches("sprocket.oscillatingturret");
    internal static void Clear(){groups.Clear();warnings.Clear();motorDiameters.Clear();compactModels.Clear();}
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.GetSelection))]
    private static void Selection(PlateStructureDesigner __instance,ISelectionBuffer __0)
    {
        if(!Available||__instance.selection==null)return;
        try
        {
            if(!groups.TryGetValue(__instance.Pointer,out var group))
            {
                PlateStructureDesigner.GeneratedDesignOption? source=null;
                foreach(var g in __instance.selection)
                    foreach(var o in g?.TryCast<OptionGroup>()?.items??new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Option>(0))
                        if(o?.Data?.TryCast<PlateStructureDesigner.GeneratedDesignOption>() is {} p&&p.Initial?.TryCast<ThreeSplitTurretShapeContainer>()!=null){source=p;break;}
                if(source==null)return;
                var designer=__instance;var preset=source;
                var option=new Option{Name=Name,Description="Combined native yaw foot and oscillating elevation stage with a modern ATGM sight. Mount the launcher on the LOWER foot and assign its sight. Assign crew to both motors and the launcher. Upper stage elevation defaults to -20 / +60 degrees.",Icon=AtgmGuidanceSightVisuals.Icon(),
                    OnSelect=(Option.PerformSelect)(Action<Il2CppSystem.Object>)(_=>Create(designer,preset))};
                group=new OptionGroup("ATGM mounts",new[]{option});groups[__instance.Pointer]=group;
            }
            __0.Add(group.Cast<ISelectionGroup>());
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[ATGM mount] Selector: "+ex.Message);}
    }
    private static PlateStructureDesigner.GeneratedDesignOption Shape(PlateStructureDesigner.GeneratedDesignOption source,float width,float height,float length)
    {
        var original=source.Initial.TryCast<ThreeSplitTurretShapeContainer>()??throw new InvalidOperationException("Expected native three-split turret preset.");
        var data=original.Data; // Copy all ancillary native shape features first.
        var shape=new ThreeSplitTurretBlueprint.Shape{
            Width=width,Height=height,FrontLength=length*.25f,MidLength=length*.5f,RearLength=length*.25f,
            FrontCheekAngle=0,RearCheekAngle=0,FrontGlacisAngle=0,RearGlacisAngle=0,SideGlacisAngle=0,
            FrontHeightOffset=0,RearHeightOffset=0,RingRadius=width*.28f,ShapeFeatures=data.ShapeFeatures};
        var container=ScriptableObject.CreateInstance<ThreeSplitTurretShapeContainer>();container.Data=shape;
        // NewBlueprintFromDesignOption reads Initial.DataObjCopy synchronously.
        return new PlateStructureDesigner.GeneratedDesignOption{Name=Name,Initial=container,Min=source.Min,Max=source.Max};
    }
    private static void Compact(PlateStructureDesigner d,PlateStructure p,TurretRing ring,PlateStructureDesigner.GeneratedDesignOption source,bool pitch)
    {
        var shape=Shape(source,pitch ? .30f : .60f,pitch ? .10f : .07f,pitch ? .28f : .44f);
        try
        {
            var bp=d.NewBlueprintFromDesignOption(shape);
            if(bp.BaseBlueprint.TryCast<ThreeSplitTurretBlueprint>() is {} generated)
            {
                generated.thickness=new ThreeSplitTurretBlueprint.Thickness{FrontArmour=5,RearArmour=5,SideArmour=5,FrontCheekArmour=5,RearCheekArmour=5,FrontUpperArmour=5,RearUpperArmour=5,RoofArmour=5,BottomArmourThickness=5};
                generated.MarkModified();
            }
            p.Mesh.GenerationBlueprint=bp;
            var b=ring.Blueprint;b.RingDiameter=(ushort)(pitch?180:300);b.RingHeight=20;b.RingThickness=8;b.MarkModified();
            if(ring.motor?.TraverseBlueprintSlot?.Blueprint is {} drive)
            {drive.MinAngle=(short)(pitch?-20:-180);drive.MaxAngle=(short)(pitch?60:180);drive.MarkModified();}
            if(ring.motor?.MotorBlueprintSlot?.Blueprint is {} motor)
            // High-torque defaults (native motor mass is 0.1 kg per torque unit);
            // players can still retune both motors in the native editor.
            {motor.Type=MotorType.Electric;motor.Torque=pitch?PitchTorque:YawTorque;motor.Ratio=20;motor.MarkModified();}
            if(ring.basket?.TryCast<TurretBasket>()?.BlueprintSlot?.Blueprint is {} basket)
            {basket.Segments=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Sprocket.TurretBaskets.BasketSegment>(0);basket.MarkModified();}
            p.RequestRebuild();ring.RequestRebuild();
        }
        finally{UnityEngine.Object.Destroy(shape.Initial);}
    }
    private static void Create(PlateStructureDesigner designer,PlateStructureDesigner.GeneratedDesignOption source)
    {
        int group=designer.VehicleOperations.GetNewGroupID();
        var target=designer.Target;
        var before=Objects(target).Select(o=>o.Pointer).ToHashSet();
        try
        {
            var lower=designer.NewStructureWithFoundation(markerPartGuid:"99281776-6b29-4ffb-9d8b-04139ca7b6a2",structureGuid:designer.plateStructureGuid,opGroupID:group,structureFoundation:out var lowFoundation);
            if(lower==null||lowFoundation?.VehicleObject?.GetBehaviour<TurretRing>() is not {} yaw)throw new InvalidOperationException("Native yaw foundation missing.");
            var upper=designer.NewStructureWithFoundation(markerPartGuid:RuntimeAtgmSightMount.ElevationGuid,structureGuid:designer.plateStructureGuid,opGroupID:group,structureFoundation:out var highFoundation);
            if(upper==null||highFoundation?.VehicleObject?.GetBehaviour<TurretRing>() is not {} pitch)throw new InvalidOperationException("Native elevation foundation missing.");
            highFoundation.VehicleObject.VehicleTransform.SetParent(lower.VehicleTransform,false);
            highFoundation.VehicleObject.VehicleTransform.LocalPosition=new Vector3(0,.13f,0);
            highFoundation.VehicleObject.VehicleTransform.LocalRotation=Quaternion.identity;
            var sightOp=designer.VehicleOperations.Instantiate(RuntimeAtgmGuidanceSight.Guid,group);
            var sight=sightOp.Instances[0].Object??throw new InvalidOperationException("Native guidance sight missing.");
            sight.VehicleTransform.SetParent(upper.VehicleTransform,false);
            sight.VehicleTransform.LocalPosition=new Vector3(0,.14f,.12f);sight.VehicleTransform.LocalRotation=Quaternion.identity;
            // Start placement with the complete child tree, then change the
            // geometry as native LoadGeneratedDesign does. The placement op
            // sees both stages and the sight when it snapshots the assembly.
            designer.VehicleOperations.BeginPlacement(lowFoundation.VehicleObject.GetReference(),group);
            Compact(designer,lower,yaw,source,false);
            Compact(designer,upper,pitch,source,true);
            designer.Target.RaiseDirtyFlags(VehicleDirtyFlags.PartConfiguration|VehicleDirtyFlags.PartTransform|VehicleDirtyFlags.CrewRoles);
            Plugin.ModLog.LogInfo("[ATGM mount] Created native yaw/elevation assembly. Attach launcher to lower platform; assign sight and both motor crew roles. Save/reload still requires in-game verification.");
        }
        catch(Exception ex)
        {
            // The native helper can register a foundation/motor and then throw
            // before returning. Snapshot the same vehicle around this synchronous
            // invocation, rather than tracking only successfully returned parts.
            try
            {
                var after=Objects(target).Where(o=>!o.Released).ToArray();
                var roots=AtgmCreationOwnership.NewRoots(before,after.Select(o=>new AtgmCreatedObject(o.Pointer,o.VehicleTransform.Parent?.VehicleObject?.Pointer??IntPtr.Zero)));
                var references=after.Where(o=>roots.Contains(o.Pointer)).Select(o=>o.GetReference()).ToArray();
                if(references.Length>0)designer.VehicleOperations.Destroy(references,group);
            }
            catch(Exception cleanup){Plugin.ModLog.LogError("[ATGM mount] Partial construction cleanup failed: "+cleanup.Message);}
            Plugin.ModLog.LogError("[ATGM mount] Construction failed: "+ex);
        }
    }
    private static IEnumerable<VehicleObject> Objects(IVehicleGateway vehicle)
    {
        var objects=vehicle.ObjectReader.Items;
        int count=objects.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<count;i++)yield return objects[i];
    }
    private static readonly HashSet<string> warnings=new();
    [HarmonyPrefix,HarmonyPatch(typeof(TurretRing),nameof(TurretRing.ValidateMotorPosition))]
    private static void RemoteMotor(TurretRing __instance)
    {
        try
        {
            if(__instance.motorMaxDistanceFromRing>=RemoteMotorDistance||!RuntimeAtgmSightMount.IsMountRing(__instance))return;
            __instance.motorMaxDistanceFromRing=RemoteMotorDistance;
            if(warnings.Add("remote:"+__instance.Pointer))Plugin.ModLog.LogInfo($"[ATGM mount] Remote traverse motor allowed for ring={__instance.VUID.Value} ({(__instance.VehicleObject.GUID==RuntimeAtgmSightMount.ElevationGuid?"elevation":"azimuth")}): up to {RemoteMotorDistance} m from the ring; native crew reach, motor mass and cost retained.");
        }
        catch(Exception ex){if(warnings.Add(ex.Message))Plugin.ModLog.LogWarning("[ATGM mount] Remote motor check: "+ex.Message);}
    }
    // InitiateMotor validates (and builds the motor model) before a loaded sight
    // may be parented; re-check both once the ring is built.
    [HarmonyPostfix,HarmonyPatch(typeof(TurretRing),nameof(TurretRing.Build))]
    private static void Revalidate(TurretRing __instance)
    {
        try
        {
            if(__instance.motor==null||!RuntimeAtgmSightMount.IsMountRing(__instance))return;
            if(__instance.motorMaxDistanceFromRing<RemoteMotorDistance)__instance.ValidateMotorPosition(__instance.motor);
            var model=__instance.motor.model?.TryCast<TraverseMotorModelComponent>();
            if(model!=null&&!compactModels.Contains(model.Pointer))__instance.motor.Build();
        }
        catch(Exception ex){if(warnings.Add(ex.Message))Plugin.ModLog.LogWarning("[ATGM mount] Remote motor revalidation: "+ex.Message);}
    }
    // Native electric motor scale = diameter(torque) / ModelMotorDiameter, with the
    // diameter growing up to 2000 torque. Doubling the reference diameter halves
    // the drawn motor of both mount drives; torque, mass, cost and crew stay native.
    internal const float MotorModelScale=.5f;
    private static readonly Dictionary<IntPtr,float> motorDiameters=new();
    private static readonly HashSet<IntPtr> compactModels=new();
    [HarmonyPrefix,HarmonyPatch(typeof(TraverseMotorModelComponent),nameof(TraverseMotorModelComponent.Build))]
    private static void CompactMotor(TraverseMotorModelComponent __instance)
    {
        try
        {
            if(!motorDiameters.TryGetValue(__instance.Pointer,out var original))motorDiameters[__instance.Pointer]=original=__instance.ModelMotorDiameter;
            var motor=__instance.AssociatedComponent?.TryCast<TraverseMotor>();
            var ring=motor==null?null:RuntimeAtgmSightMount.RingOf(motor);
            bool compact=ring!=null&&RuntimeAtgmSightMount.IsMountRing(ring);
            __instance.ModelMotorDiameter=compact?original/MotorModelScale:original;
            if(compact){if(compactModels.Add(__instance.Pointer))Plugin.ModLog.LogInfo($"[ATGM mount] Compact motor model for ring={ring!.VUID.Value}: reference diameter {original:0.###} -> {__instance.ModelMotorDiameter:0.###} m (half size).");}
            else compactModels.Remove(__instance.Pointer);
        }
        catch(Exception ex){if(warnings.Add(ex.Message))Plugin.ModLog.LogWarning("[ATGM mount] Motor model size: "+ex.Message);}
    }
    // Release is a 3-byte ret shared with 787 other methods. The unique caller still owns this instance.
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.ReleaseInternal))]
    private static void ReleaseMotorModel(VehicleComponent __instance){if(__instance==null)return;motorDiameters.Remove(__instance.Pointer);compactModels.Remove(__instance.Pointer);}
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructureDesigner),nameof(PlateStructureDesigner.Release))]
    private static void Release(PlateStructureDesigner __instance)=>groups.Remove(__instance.Pointer);
}

