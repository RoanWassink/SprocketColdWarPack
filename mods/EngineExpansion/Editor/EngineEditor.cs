using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Engines.Editor;
using Sprocket.VehicleDesigner.Access.Editors;
using Sprocket.Engines.Editor;
using SprocketEngineExpansion.Core;
using SprocketEngineExpansion.Native;
using UnityEngine;
using UnityEngine.Events;
namespace SprocketEngineExpansion.Editor;

[HarmonyPatch]
internal static class EngineEditor
{
    private sealed record Controls(CombustionEngineComponentEditor Editor,EngineBlueprint Blueprint,PropertyGroup Group,StringProperty Choice)
    {internal EngineKind? DisplayKind;internal bool Refreshing;}
    private static readonly Dictionary<IntPtr,Controls> states=new();
    private static bool expanded=true;
    private static int syncReports;
    private static readonly int[] profileReports=new int[3];
    private static readonly HashSet<IntPtr> seededPreview=new();
    [ThreadStatic]private static bool repairingShift;
    internal static bool ShiftRepairEnabled=true;
    internal static void RepairShift(EngineBlueprint bp)
    {
        if(!ShiftRepairEnabled||repairingShift||Environment.CurrentManagedThreadId!=Plugin.MainThreadId)return;
        var state=states.Values.FirstOrDefault(s=>s.Blueprint.Pointer==bp.Pointer);
        if(state==null||state.Editor.Component==null||state.Editor.Component.VehicleObject.Released||state.Editor.Component.Blueprint.Pointer!=bp.Pointer||!Unique(state.Editor.Component)||!Selections.Document(bp).Editable)return;
        repairingShift=true;
        try
        {
            int nativeMax;
            Plans.NativeRead++;try{nativeMax=bp.MaxRPM;}finally{Plans.NativeRead--;}
            var fixedShift=DrivetrainRules.Shift(bp.Downshift,bp.Upshift,bp.MaxRPM,(int)(nativeMax*.2),EngineProperties.MinShiftRange,bp.RevLimitOverride?bp.CustomRevLimit:-1);
            // Native property setters retain limiter/order coupling and the registered blueprint edit.
            if(bp.Upshift!=fixedShift.Up)state.Editor.properties.upshift.Value=fixedShift.Up;
            if(bp.Downshift!=fixedShift.Down)state.Editor.properties.downshift.Value=fixedShift.Down;
        }
        catch(Exception ex){Plugin.Warn("engine shift range",ex);}
        finally{repairingShift=false;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CombustionEngine),nameof(CombustionEngine.Build))]
    private static void BeforeEngineBuild(CombustionEngine __instance)
    {
        EngineDateRegistry.Remember(__instance.Blueprint, __instance);
        RepairShift(__instance.Blueprint);
    }
    internal static CombustionEngine? ForProperties(EngineProperties properties)=>states.Values.FirstOrDefault(s=>s.Editor.properties.Pointer==properties.Pointer)?.Editor.Component;
    private static Il2CppSystem.Action<bool> Bool(Action<bool> callback)=>DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(callback)!;
    private static UnityAction<int> Index(Action<int> callback)=>DelegateSupport.ConvertDelegate<UnityAction<int>>(callback)!;
    private static bool Unique(CombustionEngine engine)
    {
        var items=engine.Vehicle.ObjectReader.Items;int count=0;
        int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleObject>>().Count;
        for(int i=0;i<n;i++)
        {
            var components=items[i].Components;int m=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.Vehicles.VehicleComponent>>().Count;
            for(int j=0;j<m;j++)if(components[j].TryCast<CombustionEngine>() is {} other&&other.Blueprint.Pointer==engine.Blueprint.Pointer)count++;
        }
        return count==1;
    }
    private static void Remove(IntPtr editor)
    {if(states.Remove(editor,out var state)){seededPreview.Remove(state.Editor.properties.Pointer);state.Group.Release();}}
    private static void RefreshDisplay(Controls state)
    {
        if(state.Refreshing||state.Editor.Component==null||state.Editor.Component.VehicleObject.Released||state.Editor.Component.Blueprint.Pointer!=state.Blueprint.Pointer)return;
        var kind=Selections.Get(state.Blueprint).Kind;
        if(state.DisplayKind==kind)return;
        state.Refreshing=true;
        try{state.Editor.OnComponentRebuilt();state.DisplayKind=kind;}
        finally{state.Refreshing=false;}
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CombustionEngineComponentEditor),nameof(CombustionEngineComponentEditor.OnGUI))]
    private static void RefreshBeforeDraw(CombustionEngineComponentEditor __instance)
    {try{if(states.TryGetValue(__instance.Pointer,out var state))RefreshDisplay(state);}catch(Exception ex){Plugin.Warn("engine display refresh",ex);}}
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleComponentEditor<CombustionEngine>),nameof(VehicleComponentEditor<CombustionEngine>.Release))]
    private static void Release(Il2CppSystem.Object __instance)
    {try{Remove(__instance.Pointer);}catch(Exception ex){Plugin.Warn("editor release",ex);}}
    [HarmonyPostfix,HarmonyPatch(typeof(CombustionEngineComponentEditor),nameof(CombustionEngineComponentEditor.OnGUI))]
    private static void Draw(CombustionEngineComponentEditor __instance,IGUILayout __0)
    {
        try
        {
            var engine=__instance.Component;var ui=__0.TryCast<IGUIElementDrawer>();if(engine==null||ui==null)return;
            __0.EndAllDropdowns();__0.BeginDropdown("Engine expansion",expanded,Bool(v=>expanded=v));
            try
            {
                if(!Unique(engine)){Remove(__instance.Pointer);ui.InfoField("Make this engine design independent to edit its type.",2);return;}
                var bp=engine.Blueprint;
                var vehicleDate=EngineDateRegistry.Date(engine);
                EngineDateRegistry.Remember(bp,vehicleDate);
                if(!Selections.Document(bp).Editable)
                {Remove(__instance.Pointer);ui.InfoField("Saved engine settings require a compatible plugin version. They are preserved; this engine uses its native profile.",2);return;}
                if(states.TryGetValue(__instance.Pointer,out var old)&&old.Blueprint.Pointer!=bp.Pointer)Remove(__instance.Pointer);
                if(!states.TryGetValue(__instance.Pointer,out var state))
                {
                    var group=new PropertyGroup(__instance.NewPropertyRegister(bp,true));
                    var choice=new StringProperty("Engine setup",DelegateSupport.ConvertDelegate<Il2CppSystem.Func<string>>(new Func<string>(()=>SelectionCodec.Encode(Selections.Get(bp)))),
                        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(new Action<string>(json=>
                        {var next=SelectionCodec.Decode(json);Plans.Capture(bp,next);Selections.Set(bp,next);RepairShift(bp);})),"Engine type.");
                    group.Register(choice);state=new(__instance,bp,group,choice);states.Add(__instance.Pointer,state);RefreshDisplay(state);
                }
                var savedKind=Selections.Get(bp).Kind;
                var labels=new Il2CppSystem.Collections.Generic.List<string>();
                for(int i=0;i<3;i++)
                {
                    var kind=(EngineKind)i;
                    var name=kind==EngineKind.GasTurbine?"Gas turbine":kind.ToString();
                    labels.Add(EngineDates.Allows(vehicleDate,kind)?name:$"{name} (vanaf {EngineDates.For(kind):yyyy-MM-dd})");
                }
                ui.Dropdown("Engine type",labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),(int)savedKind,Index(i=>
                {
                    if(i<0||i>2||engine.VehicleObject.Released||engine.Blueprint.Pointer!=bp.Pointer||!Unique(engine))return;
                    var kind=(EngineKind)i;
                    if(!EngineDates.Allows(vehicleDate,kind))return;
                    try
                    {
                        // Keep dormant saved turbo settings for petrol/diesel; choosing turbine explicitly disables its unsupported flag.
                        var next=Selections.Get(bp) with {Kind=kind,Turbo=i==2?false:Selections.Get(bp).Turbo};
                        next.Validate();Plans.Capture(bp,next);state.Choice.StartEdit();
                        try{state.Choice.Value=SelectionCodec.Encode(next);}finally{state.Choice.EndEdit();}
                        RefreshDisplay(state);__instance.OnRedrawRequested(null!,Il2CppSystem.EventArgs.Empty);
                    }
                    catch(Exception ex){Plugin.Warn("engine type edit",ex);}
                }),"Diesel: lower revs and earlier torque. Gas turbine: more power and fuel use. Too-early profiles are labelled with their start date.");
                foreach(var kind in new[]{EngineKind.Petrol,EngineKind.Diesel,EngineKind.GasTurbine})
                    if(!EngineDates.Allows(vehicleDate,kind))
                        ui.InfoField($"vanaf {EngineDates.For(kind):yyyy-MM-dd}: {kind}",2,new Color(.6f,.6f,.6f));
                if(!EngineDates.Allows(vehicleDate,savedKind))
                    ui.InfoField($"Saved {savedKind} needs {EngineDates.For(savedKind):yyyy-MM-dd}; native petrol is used until then. The saved profile is kept.",3,new Color(.6f,.6f,.6f));
                DrawPowerpack(__instance,engine,bp,vehicleDate,ui);
                if(!Plans.Modified(bp))return;
                var plan=Plans.Capture(bp).Plan;var rpm=__instance.properties.powerDisplayRPM.Value;
                ui.InfoField($"At {rpm:0} RPM: {plan.Torque(rpm,false):0} Nm | {plan.Power(rpm,false):0.#} kW",2);
            }
            finally{__0.EndAllDropdowns();}
        }
        catch(Exception ex){Plugin.Warn("editor",ex);}
    }
    private static void DrawPowerpack(CombustionEngineComponentEditor editor,CombustionEngine engine,EngineBlueprint bp,DateTime? vehicleDate,IGUIElementDrawer ui)
    {
        var grey=new Color(.6f,.6f,.6f);
        bool saved=Selections.Get(bp).Powerpack,allowed=EngineDates.AllowsPowerpack(vehicleDate);
        var context=PowerpackLayout.Installed?PowerpackLayout.Resolve(engine):null;
        var status=context?.Status??PowerpackStatus.NoDirectTransmission;
        bool twin=context?.IsTwin==true;
        string label=allowed?"Powerpack":$"Powerpack (vanaf {EngineDates.PowerpackDate:yyyy-MM-dd})";
        string tooltip=!PowerpackLayout.Installed?"Powerpack hooks failed to install; see the BepInEx log."
            :status switch
            {
                PowerpackStatus.ViaJunction=>"Twin transmission: the engine drives a splitter junction, but it does not feed exactly two mirrored gearboxes. Powerpack is not available for this layout.",
                PowerpackStatus.Conflict=>twin
                    ?"This twin layout already uses auto-position or abstracted flags that Powerpack cannot restore exactly; Powerpack leaves it unchanged."
                    :"This transmission or its engine connection already uses native auto-position or abstracted flags; Powerpack leaves it unchanged.",
                PowerpackStatus.NoDirectTransmission=>"Connect the engine output directly to a transmission (or to a splitter feeding two mirrored transmissions) to use a powerpack.",
                _ when twin=>$"Twin transmission: both gearboxes sit 1 mm clear of the engine's obstruction collider, one on each side, at the same depth, and the splitter stays between them while following the engine. The housing adds {PowerpackLayout.MassFraction()*100:0.#}% of the engine mass and {PowerpackLayout.CostFraction()*100:0.#}% of its material cost.",
                _=>$"Engine and transmission as one unit: the transmission sits on the engine output without a driveshaft and follows the engine. The housing adds {PowerpackLayout.MassFraction()*100:0.#}% of the engine mass and {PowerpackLayout.CostFraction()*100:0.#}% of its material cost."
            };
        if(context?.Collapse is {} collapse&&collapse.Status!=PowerpackStatus.Ready)tooltip=collapse.Reason;
        else if(PowerpackLayout.Refusal(engine,context) is {} refusal)tooltip=refusal;
        if(!allowed)tooltip=$"Date: powerpack requires {EngineDates.PowerpackDate:yyyy-MM-dd}; current date {vehicleDate:yyyy-MM-dd}. "+tooltip;
        // Removing is always possible; adding needs the date and a directly fed transmission or twin pair.
        bool interactable=Selections.Document(bp).Editable&&(saved||(allowed&&PowerpackLayout.Installed&&status==PowerpackStatus.Ready));
        ui.ToggleField(label,saved,interactable,Bool(on=>
        {
            if(on==Selections.Get(bp).Powerpack||engine.VehicleObject.Released||engine.Blueprint.Pointer!=bp.Pointer||!Unique(engine))return;
            if(on&&!EngineDates.AllowsPowerpack(EngineDateRegistry.Date(engine)))return;
            try{PowerpackLayout.Toggle(editor,engine,bp,on);editor.OnRedrawRequested(null!,Il2CppSystem.EventArgs.Empty);}
            catch(Exception ex){Plugin.Warn("powerpack toggle",ex);}
        }),tooltip);
        if(saved&&!allowed)
            ui.InfoField($"Saved powerpack needs {EngineDates.PowerpackDate:yyyy-MM-dd}; the layout is kept, housing mass and cost are not applied.",2,grey);
        if(saved&&context!=null&&!context.Marked)
            ui.InfoField(twin?"The gearboxes are not mounted as a twin powerpack. Untick and tick again to re-apply.":"The transmission is not mounted as a powerpack. Untick and tick again to re-apply.",2,grey);
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineProperties),nameof(EngineProperties.SyncDependentValues))]
    private static void Synced(EngineProperties __instance,EngineBlueprint __0)
    {
        if(!Plans.Modified(__0))return;
        try
        {
            // Native Sync refreshes main torque, safe RPM and rev-limit/shift ranges after Rebuilt.
            // Native Sync has updated preview bounds; clamp only stale out-of-range preview selection.
            if(__instance.powerDisplayRPM.Value>__instance.powerDisplayRPM.Max)
                __instance.powerDisplayRPM.Value=__instance.powerDisplayRPM.Max;
            // get_RPM creates a zero-valued preview; seed it once with the native shift midpoint.
            // Later deliberate zero RPM selections remain valid and cannot overwrite cylinder torque.
            if(seededPreview.Add(__instance.Pointer)&&__instance.powerDisplayRPM.Value==0)
                __instance.powerDisplayRPM.Value=Math.Clamp((__0.Downshift+__0.Upshift)*.5f,__instance.powerDisplayRPM.Min,__instance.powerDisplayRPM.Max);
            __instance.UpdatePowerDisplay(__0);
            if(Plugin.Detailed&&syncReports++<32)Plugin.ModLog.LogInfo($"[Engine Expansion] SYNC blueprint={__0.Pointer} kind={Selections.Get(__0).Kind} maxRPM={__0.MaxRPM} safe={__instance.maxSafeRPM.Value} limiter={__instance.revLimit.Value} shiftRange={__instance.downshift.Min}..{__instance.upshift.Max} storedShift={__0.Downshift}/{__0.Upshift} torque={__instance.torque.Value} previewRPM={__instance.powerDisplayRPM.Value}");
        }
        catch(Exception ex){Plugin.Warn("dependent engine values",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(EngineProperties),nameof(EngineProperties.UpdatePowerDisplay))]
    private static void Preview(EngineProperties __instance,EngineBlueprint __0)
    {
        if(!Plans.Modified(__0))return;
        try
        {
            var snapshot=Plans.Capture(__0);var plan=snapshot.Plan;float rpm=__instance.powerDisplayRPM.Value;bool valid=false;
            // Main cylinder torque belongs to SyncDependentValues (MaxTorque), never preview RPM.
            float displayed=Sprocket.UnitConversions.ConvertPower((float)plan.Power(rpm,valid),UnitsNet.Units.PowerUnit.Kilowatt);
                        __instance.powerOutput.Value=displayed;__instance.UpdateSpecificPower(displayed);
            if(Plugin.Detailed&&profileReports[(int)plan.Selection.Kind]++<4)
                Plugin.ModLog.LogInfo($"[Engine Expansion] PROFILE blueprint={__0.Pointer} kind={plan.Selection.Kind} cylinders={__0.CylinderCount} displacement={__0.CylinderVolume} turbo={plan.Selection.Turbo} valid={valid} coefficient={snapshot.OutputCoefficient} nativePeakKW={plan.Native.MaxProduct(plan.IdleRPM/plan.NativeMaxRPM,1)*plan.NativeMaxRPM/EnginePlan.NmRpmPerKW} profilePeakKW={plan.Profile.Power*plan.Native.MaxProduct(plan.IdleRPM/plan.NativeMaxRPM,1)*plan.NativeMaxRPM/EnginePlan.NmRpmPerKW} shiftMidRPM={(__0.Downshift+__0.Upshift)*.5} selectedRPM={rpm} selectedKW={plan.Power(rpm,valid)} displayedPower={displayed}");
        }
        catch(Exception ex){Plugin.Warn("preview",ex);}
    }
}
