using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.PlateStructures.Blueprints;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures.Design;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
namespace PlateDestructionP0;

internal static class EffectBoost
{
    internal static float Factor=1f;
    /// <summary>True while our own code plays an effect (break explosion, decal): such calls are never counted as a blast.</summary>
    internal static bool Own;
}

/// <summary>One explosion seen in ProjectileImpactEffect.PlayEffect (not played by us). Queued there, handled in Runtime.Update.</summary>
internal sealed class BlastEvent
{
    internal Vector3 Pos,Normal,Velocity;internal int Calibre,Frame;internal ProjectileEffectType Type;internal string Parent="",Kind="";
    internal bool VisualOnly;
    internal bool EbHasData,EbPerforated,EbSpaced;
    internal double EbCalibreMm,EbPower,EbBreachMm;
    internal long EbPerforatedRegister;
    internal int EbSeq;
    internal float MinScale=-1,MaxScale=-1;
    internal bool HasSnap;internal long SnapRoot;internal int SnapVuid;internal Vector3 SnapHitPos,SnapDir;
}
internal static class BallisticsEffectProbe
{
    private static Func<string>? readKind;
    private static Func<bool>? readVisualOnly,readHasData,readPerforated,readSpaced;
    private static Func<double>? readCalibreMm,readPower,readBreachMm;
    private static Func<long>? readPerfReg;
    private static Func<int>? readSeq;
    private static Action<int,int,double,double,bool,double,bool,bool>? reportPlate;
    private static Action<int,int,double>? reportEra;
    private static Action<int,int,int,string,double,double,bool,int,int>? reportBreach;
    private static long nextSearch;
    private static void EnsureBound()
    {
        if(readKind!=null||Environment.TickCount64<nextSearch)return;
        nextSearch=Environment.TickCount64+2000;
        foreach(var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t=asm.GetType("SprocketExpandedBallistics.BallisticsEffectApi",false);
            if(t==null)continue;
            readKind=t.GetProperty("CurrentExplosionKind")?.GetGetMethod()?.CreateDelegate<Func<string>>();
            readVisualOnly=t.GetProperty("CurrentExplosionVisualOnly")?.GetGetMethod()?.CreateDelegate<Func<bool>>();
            readHasData=t.GetProperty("CurrentExplosionHasData")?.GetGetMethod()?.CreateDelegate<Func<bool>>();
            readCalibreMm=t.GetProperty("CurrentExplosionCalibreMm")?.GetGetMethod()?.CreateDelegate<Func<double>>();
            readPower=t.GetProperty("CurrentExplosionPower")?.GetGetMethod()?.CreateDelegate<Func<double>>();
            readBreachMm=t.GetProperty("CurrentExplosionBreachMm")?.GetGetMethod()?.CreateDelegate<Func<double>>();
            readPerforated=t.GetProperty("CurrentExplosionPerforated")?.GetGetMethod()?.CreateDelegate<Func<bool>>();
            readSpaced=t.GetProperty("CurrentExplosionSpaced")?.GetGetMethod()?.CreateDelegate<Func<bool>>();
            readPerfReg=t.GetProperty("CurrentExplosionPerforatedRegister")?.GetGetMethod()?.CreateDelegate<Func<long>>();
            readSeq=t.GetProperty("CurrentExplosionSeq")?.GetGetMethod()?.CreateDelegate<Func<int>>();
            reportPlate=t.GetMethod("ReportBlastPlate",new[]{typeof(int),typeof(int),typeof(double),typeof(double),typeof(bool),typeof(double),typeof(bool),typeof(bool)})?.CreateDelegate<Action<int,int,double,double,bool,double,bool,bool>>();
            reportEra=t.GetMethod("ReportBlastEraStripped",new[]{typeof(int),typeof(int),typeof(double)})?.CreateDelegate<Action<int,int,double>>();
            reportBreach=t.GetMethod("ReportBlastBreachDamage",new[]{typeof(int),typeof(int),typeof(int),typeof(string),typeof(double),typeof(double),typeof(bool),typeof(int),typeof(int)})?.CreateDelegate<Action<int,int,int,string,double,double,bool,int,int>>();
            break;
        }
    }
    internal static void Read(BlastEvent e)
    {
        e.Kind="";e.VisualOnly=false;e.EbHasData=false;e.EbPerforated=false;e.EbSpaced=false;
        e.EbCalibreMm=0;e.EbPower=0;e.EbBreachMm=0;e.EbPerforatedRegister=0;e.EbSeq=0;
        try
        {
            EnsureBound();
            if(readKind!=null)e.Kind=readKind()??"";
            if(readVisualOnly!=null)e.VisualOnly=readVisualOnly();
            if(readHasData!=null&&readHasData())
            {
                e.EbHasData=true;
                if(readCalibreMm!=null)e.EbCalibreMm=readCalibreMm();
                if(readPower!=null)e.EbPower=readPower();
                if(readBreachMm!=null)e.EbBreachMm=readBreachMm();
                if(readPerforated!=null)e.EbPerforated=readPerforated();
                if(readSpaced!=null)e.EbSpaced=readSpaced();
                if(readPerfReg!=null)e.EbPerforatedRegister=readPerfReg();
                if(readSeq!=null)e.EbSeq=readSeq();
            }
        }
        catch{}
    }
    internal static void ReportPlate(int seq,int plateVuid,double localThicknessMm,double breachCapacityMm,bool breached,double holeRadiusM,bool isSolidBlock,bool isDirect)
    {
        if(seq<=0)return;
        try{EnsureBound();reportPlate?.Invoke(seq,plateVuid,localThicknessMm,breachCapacityMm,breached,holeRadiusM,isSolidBlock,isDirect);}catch{}
    }
    internal static void ReportEraStripped(int seq,int count,double radiusM)
    {
        if(seq<=0||count<=0)return;
        try{EnsureBound();reportEra?.Invoke(seq,count,radiusM);}catch{}
    }
    internal static void ReportBreachDamage(int seq,int plateVuid,int entityVuid,string componentType,double distM,double damageFraction,bool alive,int crewAliveAfter,int crewTotal)
    {
        if(seq<=0)return;
        try{EnsureBound();reportBreach?.Invoke(seq,plateVuid,entityVuid,componentType,distM,damageFraction,alive,crewAliveAfter,crewTotal);}catch{}
    }
}
internal static class Blasts
{
    internal static readonly Queue<BlastEvent> Queue=new();
    internal static int Seen,Dropped;
    private const int MaxQueue=64;
    internal static void Enqueue(BlastEvent e){lock(Queue){Seen++;if(Queue.Count>=MaxQueue){Queue.Dequeue();Dropped++;}Queue.Enqueue(e);}}
    internal static List<BlastEvent> Drain(int max){var r=new List<BlastEvent>();lock(Queue){while(Queue.Count>0&&r.Count<max)r.Add(Queue.Dequeue());}return r;}
    internal static void Clear(){lock(Queue)Queue.Clear();}
}

// Route A and the editor checkbox. Two prefixes, no ref-struct parameters, no state written: they only return false for a plate the
// test key excluded. They run only while the game rebuilds the damage model.
internal static class Patches
{
    internal static readonly PlateState State=new();
    internal static int PlateSkipped,ModelSkipped,PlateBuilt,ModelBuilt;

    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.BuildDamageModelColliders))]
    private static bool PlateBuild(PlateStructure __instance)
    {
        try
        {
            if(Skip(__instance)){PlateSkipped++;return false;}
            PlateBuilt++;
            BuilderPatches.Enter(__instance);
        }
        catch(Exception e){Plugin.Warn("plate prefix: "+e.Message);}
        return true;
    }
    // Ends the plate context the builder prefixes read (also runs when the prefix skipped the original).
    [HarmonyFinalizer,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.BuildDamageModelColliders))]
    private static void PlateBuildEnd(){BuilderPatches.Ctx=null;}
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.BuildDamageModelColliders))]
    private static bool ModelBuild(VehicleObjectModel __instance)
    {
        try
        {
            if(Skip(__instance)){ModelSkipped++;return false;}
            ModelBuilt++;
        }
        catch(Exception e){Plugin.Warn("model prefix: "+e.Message);}
        return true;
    }

    // ---- Destructable checkbox (editor) ------------------------------------------------------------------------
    // Drawn at the end of PlateStructureEditor.OnGUI (postfix, same pattern as EB RuntimeIndependentStructureUi). Storage: the token [destructible] in the native, already saved string
    // StructureBlueprint.Description (Blueprint.Description). No serialisation, save or load method is patched.
    private static bool uiWarned;
    private static List<StructureBlueprint> Targets(PlateStructureEditor ed)
    {
        var list=new List<StructureBlueprint>();
        var a=ed.properties?.Target;if(a!=null)list.Add(a);
        var b=ed.Component?.Blueprint;if(b!=null&&!list.Any(x=>x.Pointer==b.Pointer))list.Add(b);
        return list;
    }
    private static List<StructureBlueprint> TankRootTargets(PlateStructureEditor ed)
    {
        var list=new List<StructureBlueprint>();
        try
        {
            var rootObj=ed.Component?.VehicleTransform?.Root?.VehicleObject;
            var comps=rootObj?.behaviours;
            if(comps!=null)
            {
                for(int i=0;i<comps.Count;i++)
                {
                    var pl=comps[i]?.TryCast<PlateStructure>();
                    var bp=pl?.Blueprint;
                    if(bp!=null&&!list.Any(x=>x.Pointer==bp.Pointer))list.Add(bp);
                }
            }
            var reader=ed.Component?.Vehicle?.ObjectReader;
            if(list.Count==0&&reader!=null)
            {
                var items=reader.Items;
                for(int i=0;i<reader.Count&&list.Count==0;i++)
                {
                    var bList=items[i]?.behaviours;if(bList==null)continue;
                    for(int j=0;j<bList.Count;j++)
                    {
                        var pl=bList[j]?.TryCast<PlateStructure>();
                        var bp=pl?.Blueprint;
                        if(bp!=null){list.Add(bp);break;}
                    }
                }
            }
        }catch{}
        if(list.Count==0)return Targets(ed);
        return list;
    }
    private static bool uiLogged;
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureEditor),nameof(PlateStructureEditor.OnGUI))]
    private static void DrawDestructable(PlateStructureEditor __instance,IGUILayout layout)
    {
        try
        {
            var ui=layout.TryCast<IGUIElementDrawer>();
            bool wholeTank=Plugin.Instance.WholeTankDestructible.Value;
            var targets=ui==null?new List<StructureBlueprint>():(wholeTank?TankRootTargets(__instance):Targets(__instance));
            if(!uiLogged)
            {
                uiLogged=true;
                Plugin.Info($"[UI] OnGUI postfix reached; drawer={(ui==null?"null":"ok")}; targets={targets.Count}; wholeTank={wholeTank}");
                if(ui==null)Plugin.Info("[UI] reason: layout.TryCast<IGUIElementDrawer>() returned null; no row drawn.");
                else if(targets.Count==0)Plugin.Info("[UI] reason: no target structure blueprint (properties.Target and Component.Blueprint both null); no row drawn.");
            }
            if(ui==null||targets.Count==0)return;
            bool on=wholeTank?!TankDestructionFlag.IsDisabled(targets[0].Description):DestructibleFlag.Has(targets[0].Description);
            var editor=__instance;
            // Own dropdown section (EB pattern, RuntimePlateStackUi): close whatever section the other mods left open, open ours, always close it again.
            layout.EndAllDropdowns();
            layout.BeginDropdown("Destruction",DestructionOpen,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>DestructionOpen=v))!);
            try
            {
                if(wholeTank)
                    ui.ToggleField("Vehicle destructible",on,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>SetFlag(editor,v,true)))!,"Whole vehicle can take localized blast breaches, dents, and behind-armor crew damage based on real local thickness (Plate Destruction 1.0).");
                else
                    ui.ToggleField("Destructable",on,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>SetFlag(editor,v,false)))!,"Plate breaks away after enough damage (Plate Destruction mod). A mirrored pair (left and right skirt) breaks one side at a time.");
            }
            finally{layout.EndAllDropdowns();}
        }
        catch(Exception e){if(!uiWarned){uiWarned=true;Plugin.Warn("Destructable checkbox: "+e);}}
    }
    // Open/closed state of the "Destruction" section: open by default, remembered while the game runs.
    internal static bool DestructionOpen=true;
    private static void SetFlag(PlateStructureEditor ed,bool value,bool wholeTank)
    {
        try
        {
            var targets=wholeTank?TankRootTargets(ed):Targets(ed);
            foreach(var bp in targets)
            {
                var before=bp.Description;
                var after=wholeTank?TankDestructionFlag.SetEnabled(before,value):DestructibleFlag.Set(before,value);
                if(string.Equals(before,after,StringComparison.Ordinal))continue;
                bp.Description=after;
                bp.MarkModified();
                Plugin.Info($"[FLAG] editor: {(wholeTank?"VehicleDestructible":"Destructable")}={value} on structure blueprint {bp.Pointer.ToInt64():X}; description '{before}' -> '{after}'");
            }
            ed.RequestRedraw();
        }
        catch(Exception e){Plugin.Warn("Destructable toggle: "+e);}
    }

    // Test-explosion scale: same proven pattern as EB RuntimeShellEffects (assets Min/MaxScale multiplied only while our flag is set, restored in the finalizer).
    internal static int BoostApplied,BoostRestored;
    private sealed class ScaleState{internal ProjectileImpactEffect Owner=null!;internal ProjectileImpactEffect.ShellImpactEffectAssets Assets=null!;internal float Min,Max;}
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileImpactEffect),nameof(ProjectileImpactEffect.PlayEffect))]
    private static void Scale(ProjectileImpactEffect __instance,ProjectileEffectInfo __0,out ScaleState? __state)
    {
        __state=null;
        try
        {
            // Blast detection (0.3.0): every explosion not played by our own code is only queued here; Runtime.Update handles it (never inside the native call).
            if((__0.Type&ProjectileEffectType.Explosion)!=0&&!EffectBoost.Own)Capture(__instance,__0);
        }
        catch(Exception e){Plugin.Warn("blast capture: "+e.Message);}
        try
        {
            if(EffectBoost.Factor<=1f||(__0.Type&ProjectileEffectType.Explosion)==0)return;
            var assets=__instance.explosionAssets;if(assets==null)return;
            __state=new ScaleState{Owner=__instance,Assets=assets,Min=assets.MinScale,Max=assets.MaxScale};
            assets.MinScale*=EffectBoost.Factor;assets.MaxScale*=EffectBoost.Factor;
            __instance.explosionAssets=assets;BoostApplied++;
        }
        catch(Exception e){Plugin.Warn("scale prefix: "+e.Message);}
    }
    // Reads ProjectileEffectInfo.Type/Position/HitNormal/Parent/Calibre and the explosion asset scale as it is at this moment (EB's boost is already in it when EB's prefix ran first).
    private static void Capture(ProjectileImpactEffect inst,ProjectileEffectInfo info)
    {
        var e=new BlastEvent{Pos=info.Position,Normal=info.HitNormal,Velocity=info.HitVelocity,Calibre=info.Calibre,Type=info.Type,Frame=UnityEngine.Time.frameCount};
        BallisticsEffectProbe.Read(e);
        try{var par=info.Parent;e.Parent=par==null?"none":par.name;}catch{e.Parent="?";}
        try{var assets=inst.explosionAssets;if(assets!=null){e.MinScale=assets.MinScale;e.MaxScale=assets.MaxScale;}}catch{}
        Blasts.Enqueue(e);
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(ProjectileImpactEffect),nameof(ProjectileImpactEffect.PlayEffect))]
    private static void RestoreScale(ScaleState? __state)
    {
        if(__state==null)return;
        try{__state.Assets.MinScale=__state.Min;__state.Assets.MaxScale=__state.Max;__state.Owner.explosionAssets=__state.Assets;BoostRestored++;}
        catch(Exception e){Plugin.Warn("scale restore: "+e.Message);}
    }
    private static bool Skip(VehicleComponent c)
    {
        if(State.ExcludedCount==0||!Plugin.Instance.EnableRouteA.Value)return false;
        var root=c.VehicleRoot;
        return root!=null&&State.IsExcluded(root.Pointer.ToInt64(),c.VUID.Value);
    }
}
