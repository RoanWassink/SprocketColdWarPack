using HarmonyLib;
using Il2CppInterop.Runtime;
using Operations;
using Sprocket.PowertrainLayouts;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Colliders;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Engines.Editor;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Vehicles.Transmissions;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

/// <summary>
/// Powerpack layout: native PositionBetweenInputs on the transmission plus Abstracted connections.
/// Native ExecuteAutoposition (run by VehiclePowertrain.BuildLayout on dirty layouts) does the
/// following; these hooks only make the input port, not the module origin, land on the engine
/// output (single) or put both twin gearboxes against the engine output (twin), and give
/// downstream auto-positioned junctions a second native pass.
/// </summary>
[HarmonyPatch]
internal static partial class PowerpackLayout
{
    internal static Func<double> MassFraction=()=>.10,CostFraction=()=>.15;
    internal static bool Installed;
    internal static bool CollapseJunctions=true;
    internal static double MaxCollapseDistance=10.0;
    internal static double MonoblockOverlap=PowerpackRules.DefaultMonoblockOverlap;
    private static readonly Dictionary<IntPtr,(IntPtr Layout,string Reason)> refusals=new();
    internal static string? Refusal(CombustionEngine engine,Context? context)=>context!=null&&refusals.TryGetValue(engine.Blueprint.Pointer,out var refusal)&&refusal.Layout==context.Powertrain.Layout.Pointer?refusal.Reason:null;
    [ThreadStatic]private static bool secondPass;
    [ThreadStatic]private static VehiclePowertrain? building;
    [ThreadStatic]private static bool inBuildLayout;
    [ThreadStatic]private static bool healedFlags;
    private static readonly TransmissionExtentCache extents=new();
    private static TransmissionExtent FallbackExtent=>new(0,.4*DrivetrainRules.LengthScale(DrivetrainAdapter.Volume())*.5);
    // One safety-net warning per toggle (re-armed by every Execute), and one before the first toggle.
    private static bool safetyNetArmed=true;
    internal sealed record Context(VehiclePowertrain Powertrain,PowerpackPlan Plan,TwinPowerpackPlan? Twin,CollapsePlan? Collapse=null,
        int[]? ChainModules=null,int ChainSplitter=-1)
    {
        internal PowerpackStatus Status=>Collapse?.Status??Twin?.Status??Plan.Status;
        // Ready exact Twin wins; otherwise a ready collapsed two-way chain supplies the two final
        // transmission modules. This keeps render-only consumers valid if a chain is observed
        // before its copy is swapped in.
        internal int[] ModuleIds=>Twin is {Status:PowerpackStatus.Ready} twin?twin.ModuleIds
            :ChainModules??(Plan.TransmissionId>=0?new[]{Plan.TransmissionId}:Array.Empty<int>());
        internal int SplitterId=>Twin is {Status:PowerpackStatus.Ready} twin?twin.JunctionId:ChainSplitter;
        internal bool IsTwin=>ModuleIds.Length>1;
        internal bool Marked=>Twin?.Marked??Plan.Marked;
        internal bool Partial=>Twin?.Partial??Plan.Partial;
    }

    internal static Context? Resolve(CombustionEngine engine)
    {
        try{return Read(engine);}
        catch(Exception ex){Plugin.Warn("powerpack layout read",ex);return null;}
    }
    private static Context? Read(CombustionEngine engine)
    {
        var powertrain=engine.PowertrainNode?.TryCast<PowertrainComponent>()?.layout?.TryCast<VehiclePowertrain>();
        var layout=powertrain?.Layout;
        var engineModule=layout?.GetPort(engine.OutputPortID)?.module;
        if(powertrain==null||layout==null||engineModule==null)return null;
        var modules=new List<LayoutModule>();var connections=new List<LayoutConnection>();
        var nativeModules=layout.powertrainModules;
        for(int i=0;i<nativeModules.Count;i++)
        {
            var m=nativeModules[i];
            var mirror=0;
            try{mirror=m.mirror?.id??-1;}catch{mirror=-1;}
            modules.Add(new(m.id,(ModuleKind)(byte)m.moduleType,(short)m.moduleFlags,mirror));
        }
        var nativeConnections=layout.connections;
        for(int i=0;i<nativeConnections.Count;i++)
        {
            var c=nativeConnections[i];var provider=c.provider?.module;var receiver=c.receiver?.module;
            if(provider!=null&&receiver!=null)connections.Add(new(c.id,provider.id,receiver.id,(byte)c.connectionFlags));
        }
        var plan=PowerpackRules.Find(modules,connections,engineModule.id);
        TwinPowerpackPlan? twin=null;
        if(plan.Status==PowerpackStatus.ViaJunction)twin=PowerpackRules.FindTwin(modules,connections,engineModule.id);
        CollapsePlan? collapse=null;
        if(CollapseJunctions&&plan.Status!=PowerpackStatus.Ready&&twin?.Status!=PowerpackStatus.Ready)
            collapse=LayoutCollapse.Find(modules,connections,engineModule.id,
                id=>ToVec(layout.GetConnection(id).receiver.position),ToVec(layout.GetPort(engine.OutputPortID).position),
                ToVec(layout.GetPort(engine.OutputPortID).direction),MaxCollapseDistance,
                ToVec(Quaternion.Inverse(powertrain.transform.rotation)*powertrain.Vehicle.transform.forward));
        int[]? chainModules=null;int chainSplitter=-1;
        if(collapse is {Status:PowerpackStatus.Ready,Splitter:>=0})
        {
            chainSplitter=collapse.Splitter;
            chainModules=collapse.Links.Select(l=>connections.Single(c=>c.Id==l.LastConnection).ReceiverModule)
                .Distinct().ToArray();
        }
        return new(powertrain,plan,twin,collapse,chainModules,chainSplitter);
    }

    /// <summary>
    /// One native history step: a copy of the layout with the powerpack flags set or cleared is swapped
    /// in (the same copy-swap as native PowertrainTopologyEditOperation), together with the saved
    /// selection. Revert swaps the original layout and selection back.
    /// </summary>
    internal static bool Toggle(CombustionEngineComponentEditor editor,CombustionEngine engine,EngineBlueprint bp,bool on)
    {
        var host=editor.Gateway?.Editor;
        var controller=host?.TryCast<Sprocket.VehicleDesigner.VehicleEditor>()?.ops
            ??host?.TryCast<Sprocket.VehicleDesigner.Access.IVehicleEditorOperations>()?.OpControl?.TryCast<OperationController>();
        if(controller==null){Plugin.Warn("powerpack",new InvalidOperationException("No active vehicle editor history; powerpack unchanged."));return false;}
        var context=Resolve(engine);
        bool withLayout=context?.Status==PowerpackStatus.Ready&&(context.Marked!=on||context.Partial);
        if(on&&context?.Status!=PowerpackStatus.Ready)return false;
        var before=Selections.Capture(bp);
        var next=Selections.Get(bp) with {Powerpack=on};next.Validate();
        PowertrainLayout? initial=null,final=null;Selections.Snapshot? after=null;
        Context? applied=context;
        if(withLayout)
        {
            initial=context!.Powertrain.Layout;final=new PowertrainLayout(initial);
            try
            {
                if(on&&context.Collapse is {} collapse)
                {
                    CollapseCopy(final,collapse);
                    applied=ReadLayout(context.Powertrain,final,engine.OutputPortID);
                }
                if(applied!.Twin is {} twin)ApplyTwin(final,twin,on);
                else ApplyDirect(final,applied.Plan,on);
                if(on)
                {
                    var previousBuilding=building;
                    try { building=context.Powertrain;extents.BeginBuild();PowertrainTransformations.ExecuteAutoposition(final); }
                    finally { building=previousBuilding;extents.BeginBuild(); }
                    string? problem=ValidateCopy(context.Powertrain,initial,final);
                    if(problem!=null)
                    {
                        refusals[bp.Pointer]=(initial.Pointer,"Powerpack copy refused: "+problem);
                        if(Plugin.Detailed)Plugin.ModLog.LogInfo("POWERPACK refused: "+problem);
                        return false;
                    }
                }
            }
            catch(Exception ex)
            {
                refusals[bp.Pointer]=(initial.Pointer,"Powerpack copy refused: "+ex.Message);
                if(Plugin.Detailed)Plugin.ModLog.LogInfo("POWERPACK refused: "+ex.Message);
                return false;
            }
        }
        refusals.Remove(bp.Pointer);
        var op=new Operation(on?"Powerpack":"Remove powerpack",editor);
        // Same flags as native VehiclePowertrainEditor.ExecuteOperation for topology edits.
        op.Flags=(OperationFlags)0x37;
        op.Execute=DelegateSupport.ConvertDelegate<OperationExecute>(new Func<Operation,OperatorContext,ExecutionResult>((_,_)=>
        {
            try
            {
                if(engine.VehicleObject.Released)return ExecutionResult.Cancelled;
                safetyNetArmed=true;
                if(after==null)
                {
                    Selections.Set(bp,next);after=Selections.Capture(bp);
                }
                else Selections.Restore(bp,after);
                try{if(final!=null)context!.Powertrain.Layout=final;}
                catch{Selections.Restore(bp,before);throw;}
                Changed(engine,bp);
                PowerpackCollision.Refresh(context!.Powertrain);
                PowerpackCollision.MarkPowertrainCollidersModified(context.Powertrain);
                if(Plugin.Detailed)
                {
                    Plugin.ModLog.LogInfo(FormattableString.Invariant($"POWERPACK collapse junctions={(on?context?.Collapse?.Removed.Length??0:0)} dist={context?.Collapse?.Distance??0:0.###}"));
                    Plugin.ModLog.LogInfo($"POWERPACK collision mode={(PowerpackCollision.Active?"monoblock-filter":"native-1mm")} shafts={PowerpackCollision.DriveshaftMode}");
                }
                return ExecutionResult.Executed;
            }
            catch(Exception ex)
            {
                Selections.Restore(bp,before);
                if(initial!=null)context!.Powertrain.Layout=initial;
                after=null;
                Plugin.Warn("powerpack edit",ex);return ExecutionResult.Cancelled;
            }
        }));
        op.Revert=DelegateSupport.ConvertDelegate<OperationRevert>(new Action<Operation,OperatorContext>((_,_)=>
        {
            try
            {
                if(engine.VehicleObject.Released)return;
                Selections.Restore(bp,before);
                if(initial!=null)context!.Powertrain.Layout=initial;
                Changed(engine,bp);
                PowerpackCollision.Refresh(context!.Powertrain);
                PowerpackCollision.MarkPowertrainCollidersModified(context.Powertrain);
                if(Plugin.Detailed)Plugin.ModLog.LogInfo($"[Engine Expansion] POWERPACK revert engine={bp.Pointer} restored={(before.Document.Selection.Powerpack?"on":"off")}");
            }
            catch(Exception ex){Plugin.Warn("powerpack undo",ex);}
        }));
        controller.Execute(op,-1,ExecuteMode.Synchronous);
        return true;
    }
    private static string Ids(Context? context)=>context is null?"-":string.Join("/",context.ModuleIds);
    private static string ConnectionIds(Context? context)=>context?.Twin is {} twin?string.Join("/",twin.ConnectionIds):context?.Plan.ConnectionId.ToString()??"-";

    private static void ApplyDirect(PowertrainLayout final,PowerpackPlan plan,bool on)
    {
        var module=final.GetModule(plan.TransmissionId);var connection=final.GetConnection(plan.ConnectionId);
        var flags=on?PowerpackRules.Apply((short)module.moduleFlags,(byte)connection.connectionFlags)
                    :PowerpackRules.Restore((short)module.moduleFlags,(byte)connection.connectionFlags);
        PowertrainLayoutOps.SetModuleFlags(final,module.id,(PowertrainModuleFlags)flags.Module);
        PowertrainLayoutOps.SetConnectionFlags(final,connection.id,(PortConnectionFlags)flags.Connection);
        // Unticked, the transmission stays in line and steps back by the native minimum shaft length,
        // so the restored physical shaft is never zero length.
        if(!on)PowertrainTransformations.SetModulePosition(final,module.id,module.position+connection.provider.direction*DriveShaft.MinDriveShaftLength);
    }

    private static void ApplyTwin(PowertrainLayout final,TwinPowerpackPlan twin,bool on)
    {
        foreach(var id in twin.ModuleIds)
        {
            var module=final.GetModule(id);
            var flags=on?PowerpackRules.Apply((short)module.moduleFlags,0).Module
                        :PowerpackRules.Restore((short)module.moduleFlags,0).Module;
            PowertrainLayoutOps.SetModuleFlags(final,module.id,(PowertrainModuleFlags)flags);
        }
        foreach(var id in twin.ConnectionIds)
        {
            var connection=final.GetConnection(id);
            var flags=on?PowerpackRules.Apply(0,(byte)connection.connectionFlags).Connection
                        :PowerpackRules.Restore(0,(byte)connection.connectionFlags).Connection;
            PowertrainLayoutOps.SetConnectionFlags(final,connection.id,(PortConnectionFlags)flags);
        }
        if(on)return;
        // The splitter (native 0x70) settles on the midpoint of the two transmission inputs after the
        // rebuild. Widen the pair symmetrically, and move both along the engine shaft if needed, so
        // every restored shaft is at least DriveShaft.MinDriveShaftLength.
        var engineInput=final.GetConnection(twin.EngineJunctionConnectionId);
        var splitter=final.GetModule(twin.JunctionId);
        var left=final.GetModule(twin.LeftTransmissionId);var right=final.GetModule(twin.RightTransmissionId);
        var leftConnection=final.GetConnection(twin.LeftConnectionId);var rightConnection=final.GetConnection(twin.RightConnectionId);
        var positions=PowerpackRules.RestoredTwinPositions(
            ToVec(left.position),ToQuat(left.rotation),ToVec(leftConnection.receiver.localPosition),
            ToVec(right.position),ToQuat(right.rotation),ToVec(rightConnection.receiver.localPosition),
            ToQuat(splitter.rotation),ToVec(engineInput.receiver.localPosition),
            ToVec(engineInput.provider.position),ToVec(engineInput.provider.direction),DriveShaft.MinDriveShaftLength);
        PowertrainTransformations.SetModulePosition(final,left.id,ToVector(positions.Left));
        PowertrainTransformations.SetModulePosition(final,right.id,ToVector(positions.Right));
    }
    private static Vec3 ToVec(Vector3 value)=>new(value.x,value.y,value.z);
    private static Quat ToQuat(Quaternion value)=>new(value.x,value.y,value.z,value.w);
    private static Vector3 ToVector(Vec3 value)=>new((float)value.X,(float)value.Y,(float)value.Z);
    private static Quaternion ToUnityQuat(Quat value)=>new((float)value.X,(float)value.Y,(float)value.Z,(float)value.W);
    private static double SignedClearance()=>PowerpackCollision.Active?-Math.Clamp(MonoblockOverlap,0,.25):PowerpackRules.ObstructionClearance;

    private static void Changed(CombustionEngine engine,EngineBlueprint bp){bp.MarkModified();engine.RequestRebuild();}

    [HarmonyPrefix,HarmonyPatch(typeof(PowertrainTransformations),nameof(PowertrainTransformations.ExecuteAutoposition))]
    private static void SetRotations(PowertrainLayout __0)
    {
        if(secondPass||__0==null)return;
        try
        {
            if(inBuildLayout&&building!=null)SyncPowerpackFlags(building,__0);
            var modules=__0.powertrainModules;
            for(int i=0;i<modules.Count;i++)
            {
                var module=modules[i];
                if(!TryInput(module,out _,out var connection))continue;
                var providerModule=connection.provider?.module;
                if(providerModule==null)continue;
                if(providerModule.moduleType==PowertrainModuleType.Engine)
                {
                    module.rotation=providerModule.rotation;
                }
                else if(TwinEngineOutput(providerModule,connection,out var engineOutput)&&
                    engineOutput.module is {} engineMod&&
                    FindTwinPartner(providerModule,module) is {} partner)
                {
                    int side=TwinSideFor(module,partner,engineMod,ToVec(engineOutput.direction),preferExistingRotation:false);
                    module.rotation=ToUnityQuat(PowerpackRules.TwinRotation(ToQuat(engineMod.rotation),side));
                    providerModule.rotation=engineMod.rotation;
                }
            }
        }
        catch(Exception ex){Plugin.Warn("powerpack rotation",ex);}
    }

    // The native caller uses the result as the module position. Single & Twin both use
    // engine-local 3D rigid placement (and SetRotations aligns rotation), tucking flush when
    // internal collision filtering is active or holding 1 mm positive clearance otherwise.
    [HarmonyPostfix,HarmonyPatch(typeof(PowertrainTransformations),nameof(PowertrainTransformations.GetModuleAutoPosition))]
    private static void Flush(PowertrainModule __0,ref Vector3 __result)
    {
        try
        {
            if(!TryInput(__0,out var input,out var connection))return;
            var provider=connection.provider;
            if(provider?.module is not {} providerModule)return;
            Vec3 origin;
            double clearance=SignedClearance();
            if(providerModule.moduleType==PowertrainModuleType.Engine)
            {
                OrientedBox? engineBox=building!=null&&EngineObstruction(building,providerModule,out var eb)?eb:null;
                var placed=PowerpackRules.MonoblockOrigin(
                    ToVec(provider.position),ToQuat(providerModule.rotation),ToVec(provider.direction),
                    ToVec(input.localPosition),ExtentFor(__0),engineBox,clearance);
                if(placed.FellBack&&engineBox is {} bad)WarnSafetyNet(__0.id,placed.Depth,bad);
                origin=placed.Origin;
            }
            else if(TwinEngineOutput(providerModule,connection,out var engineOutput)&&engineOutput.module is {} engineMod)
            {
                var partner=FindTwinPartner(providerModule,__0);
                var engineDirection=ToVec(engineOutput.direction);
                int side=partner!=null
                    ?TwinSideFor(__0,partner,engineMod,engineDirection,preferExistingRotation:true)
                    :((short)__0.moduleFlags&2)!=0?-1:1;
                OrientedBox? engineBox=building!=null&&EngineObstruction(building,engineMod,out var eb)?eb:null;
                var placed=PowerpackRules.TwinMonoblockOrigin(
                    ToVec(engineOutput.position),ToQuat(engineMod.rotation),engineDirection,
                    ToQuat(__0.rotation),ToVec(input.localPosition),ExtentFor(__0),side,engineBox,clearance);
                if(placed.FellBack&&engineBox is {} bad)WarnSafetyNet(__0.id,placed.Depth,bad);
                origin=placed.Origin;
            }
            else return;
            __result=ToVector(origin);
        }
        catch(Exception ex){Plugin.Warn("powerpack placement",ex);}
    }
    // ExecuteAutoposition walks modules once; junctions placed from these transmissions' outputs
    // need their new position. The transmissions (IDs 2/3) precede the splitter (ID 1) in Leopard's
    // layout, so the first pass already places it; this pass keeps downstream junctions in step too.
    [HarmonyPostfix,HarmonyPatch(typeof(PowertrainTransformations),nameof(PowertrainTransformations.ExecuteAutoposition))]
    private static void SecondPass(PowertrainLayout __0)
    {
        if(secondPass||__0==null)return;
        try
        {
            if(!HasPowerpack(__0))return;
            secondPass=true;
            PowertrainTransformations.ExecuteAutoposition(__0);
        }
        catch(Exception ex){Plugin.Warn("powerpack autoposition",ex);}
        finally{secondPass=false;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehiclePowertrain),nameof(VehiclePowertrain.BuildLayout),new[]{typeof(PowertrainLayout)})]
    private static void BeforeBuild(VehiclePowertrain __instance){building=__instance;inBuildLayout=true;healedFlags=false;extents.BeginBuild();}
    [HarmonyFinalizer,HarmonyPatch(typeof(VehiclePowertrain),nameof(VehiclePowertrain.BuildLayout),new[]{typeof(PowertrainLayout)})]
    private static Exception? AfterBuild(VehiclePowertrain __instance,Exception? __exception)
    {
        bool healed=healedFlags;
        building=null;inBuildLayout=false;healedFlags=false;extents.BeginBuild();
        if(__exception==null)
        {
            PowerpackCollision.Refresh(__instance);
            if(healed)PowerpackCollision.MarkPowertrainCollidersModified(__instance);
        }
        return __exception;
    }
    // A transmission parameter change can alter the box width without changing the min/max ratios
    // that VehiclePowertrain.SetTransmissionInfo compares. That path then never dirties the layout,
    // so the gearbox would keep its old origin. Geometry is the native dirty flag for a rebuild that
    // re-runs autoposition without re-instantiating modules (unlike RequestReload, which sets a
    // module's component slot to -1). This is per Build, never per frame.
    [HarmonyPostfix,HarmonyPatch(typeof(TransmissionBlock),nameof(TransmissionBlock.Build))]
    private static void AfterTransmissionBuild(TransmissionBlock __instance)
    {
        try
        {
            if(__instance.VehicleObject.Released)return;
            if(__instance.powertrainNode?.TryCast<PowertrainComponent>() is not {} node)return;
            if(node.layout?.TryCast<VehiclePowertrain>() is not {} powertrain)return;
            var module=powertrain.Layout?.GetModule(node.powertrainModuleID);
            if(module==null||((short)module.moduleFlags&PowerpackRules.PositionBetweenInputs)!=PowerpackRules.PositionBetweenInputs)return;
            powertrain.RaiseDirtyFlags(VehiclePowertrainLayoutDirtyFlags.Geometry);
        }
        catch(Exception ex){Plugin.Warn("powerpack width rebuild",ex);}
    }

    private static void SyncPowerpackFlags(VehiclePowertrain powertrain,PowertrainLayout layout)
    {
        try
        {
            var reader=powertrain.Vehicle?.ObjectReader;
            if(reader?.Items is not {} items)return;
            int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<n;i++)
            {
                var components=items[i].Components;
                int m=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<m;j++)
                {
                    if(components[j].TryCast<CombustionEngine>() is not {} engine)continue;
                    if(engine.VehicleObject==null||engine.VehicleObject.Released||engine.Blueprint is not {} bp||!Selections.Get(bp).Powerpack)continue;
                    var node=engine.PowertrainNode?.TryCast<PowertrainComponent>();
                    if(node==null||node.layout?.Pointer!=powertrain.Pointer)continue;
                    var engineMod=layout.GetModule(node.powertrainModuleID);
                    if(engineMod==null)continue;
                    ScanModulePorts(engineMod,out _,out var engOut);
                    if(engOut.Count!=1||engOut[0].receiver?.module is not {} recv)continue;
                    var c=engOut[0];
                    if(recv.moduleType==PowertrainModuleType.Transmission)
                    {
                        ScanModulePorts(recv,out var tIn,out _);
                        if(tIn.Count!=1||tIn[0].id!=c.id||((short)recv.moduleFlags&PowerpackRules.SplitterBetweenOutputs)!=0)continue;
                        healedFlags|=EnsurePowerpackModuleFlags(layout,recv);
                        healedFlags|=EnsurePowerpackConnectionFlags(layout,c);
                    }
                    else if(recv.moduleType==PowertrainModuleType.Junction)
                    {
                        if(((short)recv.moduleFlags&PowerpackRules.SplitterBetweenOutputs)!=PowerpackRules.SplitterBetweenOutputs
                            ||((short)recv.moduleFlags&PowerpackRules.PositionBetweenInputs)!=0)continue;
                        ScanModulePorts(recv,out var jIn,out var jOut);
                        if(jIn.Count!=1||jIn[0].id!=c.id||jOut.Count!=2)continue;
                        var cL=jOut[0];var cR=jOut[1];
                        if(cL.receiver?.module is not {moduleType:PowertrainModuleType.Transmission} tL
                            ||cR.receiver?.module is not {moduleType:PowertrainModuleType.Transmission} tR
                            ||tL.id==tR.id)continue;
                        ScanModulePorts(tL,out var lIn,out _);ScanModulePorts(tR,out var rIn,out _);
                        if(lIn.Count!=1||lIn[0].id!=cL.id||rIn.Count!=1||rIn[0].id!=cR.id)continue;
                        if(((short)tL.moduleFlags&PowerpackRules.SplitterBetweenOutputs)!=0
                            ||((short)tR.moduleFlags&PowerpackRules.SplitterBetweenOutputs)!=0)continue;
                        healedFlags|=EnsurePowerpackModuleFlags(layout,tL);
                        healedFlags|=EnsurePowerpackModuleFlags(layout,tR);
                        healedFlags|=EnsurePowerpackConnectionFlags(layout,c);
                        healedFlags|=EnsurePowerpackConnectionFlags(layout,cL);
                        healedFlags|=EnsurePowerpackConnectionFlags(layout,cR);
                    }
                }
            }
        }
        catch(Exception ex){Plugin.Warn("powerpack sync",ex);}
    }
    private static void ScanModulePorts(PowertrainModule module,out List<PortConnection> incoming,out List<PortConnection> outgoing)
    {
        incoming=new();outgoing=new();
        var first=module.firstPort;if(first==null)return;
        int guard=0;var port=first;
        do
        {
            var c=port.connection;
            if(c!=null)
            {
                if(c.receiver?.Pointer==port.Pointer)incoming.Add(c);
                else if(c.provider?.Pointer==port.Pointer)outgoing.Add(c);
            }
            port=port.next;
        }
        while(port!=null&&port.Pointer!=first.Pointer&&++guard<64);
    }
    private static bool EnsurePowerpackModuleFlags(PowertrainLayout layout,PowertrainModule module)
    {
        short flags=(short)module.moduleFlags;
        if((flags&PowerpackRules.PositionBetweenInputs)==PowerpackRules.PositionBetweenInputs)return false;
        PowertrainLayoutOps.SetModuleFlags(layout,module.id,(PowertrainModuleFlags)(flags|PowerpackRules.PositionBetweenInputs));
        return true;
    }
    private static bool EnsurePowerpackConnectionFlags(PowertrainLayout layout,PortConnection connection)
    {
        byte flags=(byte)connection.connectionFlags;
        if((flags&PowerpackRules.Abstracted)!=0)return false;
        PowertrainLayoutOps.SetConnectionFlags(layout,connection.id,(PortConnectionFlags)(flags|PowerpackRules.Abstracted));
        return true;
    }

    private static bool HasPowerpack(PowertrainLayout layout)
    {
        var modules=layout.powertrainModules;
        for(int i=0;i<modules.Count;i++)if(TryInput(modules[i],out _,out _))return true;
        return false;
    }
    /// <summary>A transmission with PositionBetweenInputs and exactly one Abstracted input.</summary>
    private static bool TryInput(PowertrainModule? module,out Port input,out PortConnection connection)
    {
        input=null!;connection=null!;
        if(module==null||module.moduleType!=PowertrainModuleType.Transmission||((short)module.moduleFlags&PowerpackRules.PositionBetweenInputs)!=PowerpackRules.PositionBetweenInputs)return false;
        var first=module.firstPort;if(first==null)return false;
        int inputs=0,guard=0;var port=first;
        do
        {
            var candidate=port.connection;
            if(candidate!=null&&candidate.receiver?.Pointer==port.Pointer)
            {
                if(++inputs>1||((byte)candidate.connectionFlags&PowerpackRules.Abstracted)==0)return false;
                input=port;connection=candidate;
            }
            port=port.next;
        }
        while(port!=null&&port.Pointer!=first.Pointer&&++guard<64);
        return inputs==1;
    }
    /// <summary>
    /// A twin transmission is fed by a splitter junction whose only input is an Abstracted engine
    /// connection. The splitter itself must keep its native 0x70 and feed exactly two transmissions.
    /// </summary>
    private static bool TwinEngineOutput(PowertrainModule junction,PortConnection transmissionConnection,out Port engineOutput)
    {
        engineOutput=null!;
        if(junction.moduleType!=PowertrainModuleType.Junction||((short)junction.moduleFlags&PowerpackRules.SplitterBetweenOutputs)!=PowerpackRules.SplitterBetweenOutputs)return false;
        var first=junction.firstPort;if(first==null)return false;
        int inputs=0,outputs=0,guard=0;Port? incoming=null;var port=first;
        do
        {
            var candidate=port.connection;
            if(candidate!=null)
            {
                if(candidate.receiver?.Pointer==port.Pointer)
                {
                    if(++inputs>1||((byte)candidate.connectionFlags&PowerpackRules.Abstracted)==0||candidate.provider?.module?.moduleType!=PowertrainModuleType.Engine)return false;
                    incoming=port;
                }
                else if(candidate.provider?.Pointer==port.Pointer&&candidate.receiver?.module?.moduleType==PowertrainModuleType.Transmission)
                {
                    if(++outputs>2)return false;
                }
            }
            port=port.next;
        }
        while(port!=null&&port.Pointer!=first.Pointer&&++guard<64);
        if(inputs!=1||outputs!=2||incoming?.connection?.provider is not {} provider)return false;
        engineOutput=provider;
        return true;
    }

    private static TransmissionExtent ExtentFor(PowertrainModule module)
    {
        // GetModuleAutoPosition also runs outside BuildLayout. There is no build to anchor a width to
        // then, so answer with a fresh guess and never store it.
        if(building==null)return FallbackExtent;
        if(extents.TryGet(module.Pointer,out var cached))return cached;
        var extent=Measure(building,module,out bool exact);
        if(extent is not {} found)return FallbackExtent;
        // Only the live collider is a measurement. A width derived from the reference shroud is an
        // estimate and must not survive this call.
        return exact?extents.Remember(module.Pointer,found,FallbackExtent):found;
    }

    /// <summary>
    /// The transmission's obstruction box. Centre and size are the native box collider values on all
    /// three axes, in module-local space; the old CentreX/HalfWidth remain the local-X face.
    /// </summary>
    internal static TransmissionExtent? Measure(VehiclePowertrain powertrain,PowertrainModule module,out bool exact)
    {
        exact=false;
        try
        {
            var block=FindTransmission(powertrain,module);
            if(block==null)return null;
            var collider=(block.obstructionCollider??block.selectionCollider)?.TryCast<Sprocket.Vehicles.Colliders.VehicleBoxCollider>();
            if(collider!=null&&collider.size.x>0)
            {
                exact=true;
                // centre/size are in the collider Transform's space. The module frame is the
                // transmission's own Transform (native ModuleTransformUpdate writes module.position and
                // rotation there); convert only when the collider sits on another Transform.
                var box=new OrientedBox(ToVec(collider.centre),ToVec(collider.size),new Quat(0,0,0,1));
                var colliderTransform=collider.Transform;var moduleTransform=block.transform;
                if(colliderTransform!=null&&moduleTransform!=null&&colliderTransform.Pointer!=moduleTransform.Pointer)
                    box=FrameOf(moduleTransform).ToLocal(FrameOf(colliderTransform).ToWorld(box));
                return TransmissionExtent.FromModuleBox(box);
            }
            var width=block.referenceShroudSize.x;
            if(width>0)return new(0,width*DrivetrainRules.LengthScale(DrivetrainAdapter.Volume())*.5);
        }
        catch(Exception ex){Plugin.Warn("powerpack extent",ex);}
        return null;
    }

    internal static TransmissionBlock? FindTransmission(VehiclePowertrain powertrain,PowertrainModule module)
    {
        var reader=powertrain.Vehicle?.ObjectReader;
        if(reader==null)return null;
        var items=reader.Items;
        int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<n;i++)
        {
            var components=items[i].Components;
            int m=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<m;j++)
            {
                if(components[j].TryCast<TransmissionBlock>() is not {} block)continue;
                var node=block.powertrainNode?.TryCast<PowertrainComponent>();
                // PowertrainComponent.layout is the VehiclePowertrain controller, not its layout data.
                if(node==null||node.powertrainModuleID!=module.id||node.layout?.Pointer!=powertrain.Pointer)continue;
                return block;
            }
        }
        return null;
    }

    /// <summary>
    /// The engine's real required-space collider, in layout space. Native 0.2.56.2 creates it as a
    /// mesh collider (CombustionEngine.Initiate, type Selection|RequiredSpace), not a box; its local
    /// bounds are the tight oriented box used for the motor-side clearance. LocalBounds is in the
    /// collider's local space: the collider Transform takes it to world space (including the engine
    /// yaw), and the VehiclePowertrain's Transform takes world back to layout space, exactly as native
    /// SetModuleTransform does. Module and port positions are layout space; the editor vehicle is not
    /// at the world origin.
    /// </summary>
    private static bool EngineObstruction(VehiclePowertrain powertrain,PowertrainModule module,out OrientedBox box)
    {
        box=default;
        var reader=powertrain.Vehicle?.ObjectReader;
        if(reader==null)return false;
        var items=reader.Items;
        int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<n;i++)
        {
            var components=items[i].Components;
            int m=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<m;j++)
            {
                if(components[j].TryCast<CombustionEngine>() is not {} engine)continue;
                var node=engine.PowertrainNode?.TryCast<PowertrainComponent>();
                if(node==null||node.powertrainModuleID!=module.id||node.layout?.Pointer!=powertrain.Pointer)continue;
                var colliders=engine.Colliders;
                int count=colliders.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleCollider>>().Count;
                for(int k=0;k<count;k++)
                {
                    var candidate=colliders[k];
                    if(candidate==null||((int)candidate.Type&(int)ColliderType.RequiredSpace)==0)continue;
                    var collider=candidate.TryCast<VehicleCollider>();
                    if(collider?.Transform==null)continue;
                    var local=collider.LocalBounds;
                    if(ToVec(local.size).Length<1e-9)continue;
                    var world=FrameOf(collider.Transform).ToWorld(new OrientedBox(ToVec(local.center),ToVec(local.size),new Quat(0,0,0,1)));
                    box=FrameOf(powertrain.transform).ToLocal(world);
                    return true;
                }
            }
        }
        return false;
    }
    private static Frame FrameOf(Transform transform)=>new(ToVec(transform.position),ToQuat(transform.rotation),ToVec(transform.lossyScale));

    private static void WarnSafetyNet(int module,double depth,OrientedBox engineBox)
    {
        if(!safetyNetArmed)return;
        safetyNetArmed=false;
        Plugin.ModLog.LogWarning(FormattableString.Invariant(
            $"[Engine Expansion] POWERPACK safety net: module={module} depth={depth:0.###} m against the engine box is more than {PowerpackRules.PlacementSafetyNet} m off the engine output plane; used the output-plane placement plus {PowerpackRules.ObstructionClearance} m. engine=[{Box(engineBox)}]"));
    }

    private static PowertrainModule? FindTwinPartner(PowertrainModule splitter,PowertrainModule module)
    {
        var first=splitter.firstPort;if(first==null)return null;
        int guard=0;var port=first;
        do
        {
            var candidate=port.connection;
            if(candidate!=null&&candidate.provider?.Pointer==port.Pointer&&
                candidate.receiver?.module is {moduleType:PowertrainModuleType.Transmission} other&&
                other.id!=module.id)
                return other;
            port=port.next;
        }
        while(port!=null&&port.Pointer!=first.Pointer&&++guard<64);
        return null;
    }
    private static (Vec3 Target,bool HasDownstream) DownstreamTarget(PowertrainModule start)
    {
        var current=start;
        var visited=new HashSet<int>{start.id};
        Vec3? best=null;
        for(int hop=0;hop<8;hop++)
        {
            PortConnection? outgoing=null;
            var first=current.firstPort;if(first==null)break;
            int guard=0;var port=first;
            do
            {
                var c=port.connection;
                if(c!=null&&c.provider?.Pointer==port.Pointer&&c.receiver?.module!=null)
                {outgoing=c;break;}
                port=port.next;
            }
            while(port!=null&&port.Pointer!=first.Pointer&&++guard<64);
            if(outgoing?.receiver?.module is not {} next||!visited.Add(next.id))break;
            best=ToVec(next.position);
            if(((short)next.moduleFlags&PowerpackRules.PositionBetweenInputs)==0)return (best.Value,true);
            current=next;
        }
        return best is {} b?(b,true):(ToVec(start.position),false);
    }
    private static int TwinSideFor(PowertrainModule module,PowertrainModule partner,PowertrainModule engineModule,
        Vec3 engineDirection,bool preferExistingRotation)
    {
        var engineRight=PowerpackRules.EngineRight(ToQuat(engineModule.rotation),engineDirection);
        var a=DownstreamTarget(module);var b=DownstreamTarget(partner);
        if(a.HasDownstream&&b.HasDownstream&&Math.Abs(Vec3.Dot(a.Target-b.Target,engineRight))>1e-4)
            return PowerpackRules.ChooseTwinSide(engineRight,a.Target,b.Target,(short)module.moduleFlags,(short)partner.moduleFlags,module.id,partner.id);
        if(preferExistingRotation)
        {
            double posProj=Vec3.Dot(ToVec(module.position)-ToVec(partner.position),engineRight);
            if(Math.Abs(posProj)>1e-4)return posProj<0?-1:1;
        }
        return PowerpackRules.ChooseTwinSide(engineRight,a.Target,b.Target,(short)module.moduleFlags,(short)partner.moduleFlags,module.id,partner.id);
    }
    private static string Box(OrientedBox box)=>FormattableString.Invariant(
        $"c=({box.Centre.X:0.###},{box.Centre.Y:0.###},{box.Centre.Z:0.###}) s=({box.Size.X:0.###},{box.Size.Y:0.###},{box.Size.Z:0.###}) q=({box.Rotation.X:0.####},{box.Rotation.Y:0.####},{box.Rotation.Z:0.####},{box.Rotation.W:0.####})");
}
