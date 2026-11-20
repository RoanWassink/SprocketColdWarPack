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
    internal float MinScale=-1,MaxScale=-1;
    internal bool HasSnap;internal long SnapRoot;internal int SnapVuid;internal Vector3 SnapHitPos,SnapDir;
}
internal static class BallisticsEffectProbe
{
    private static Func<string>? readKind;
    private static Func<bool>? readVisualOnly;
    private static long nextSearch;
    internal static void Read(out string kind,out bool visualOnly)
    {
        kind="";visualOnly=false;
        try
        {
            if(readKind==null&&Environment.TickCount64>=nextSearch)
            {
                nextSearch=Environment.TickCount64+2000;
                foreach(var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t=asm.GetType("SprocketExpandedBallistics.BallisticsEffectApi",false);
                    if(t==null)continue;
                    readKind=t.GetProperty("CurrentExplosionKind")?.GetGetMethod()?.CreateDelegate<Func<string>>();
                    readVisualOnly=t.GetProperty("CurrentExplosionVisualOnly")?.GetGetMethod()?.CreateDelegate<Func<bool>>();
                    break;
                }
            }
            if(readKind!=null)kind=readKind()??"";
            if(readVisualOnly!=null)visualOnly=readVisualOnly();
        }
        catch{}
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
    private static bool uiLogged;
    [HarmonyPostfix,HarmonyPatch(typeof(PlateStructureEditor),nameof(PlateStructureEditor.OnGUI))]
    private static void DrawDestructable(PlateStructureEditor __instance,IGUILayout layout)
    {
        try
        {
            var ui=layout.TryCast<IGUIElementDrawer>();
            var targets=ui==null?new List<StructureBlueprint>():Targets(__instance);
            if(!uiLogged)
            {
                uiLogged=true;
                Plugin.Info($"[UI] OnGUI postfix reached; drawer={(ui==null?"null":"ok")}; targets={targets.Count}");
                if(ui==null)Plugin.Info("[UI] reason: layout.TryCast<IGUIElementDrawer>() returned null; no row drawn.");
                else if(targets.Count==0)Plugin.Info("[UI] reason: no target structure blueprint (properties.Target and Component.Blueprint both null); no row drawn.");
            }
            if(ui==null||targets.Count==0)return;
            bool on=DestructibleFlag.Has(targets[0].Description);
            var editor=__instance;
            // Own dropdown section (EB pattern, RuntimePlateStackUi): close whatever section the other mods left open, open ours, always close it again.
            layout.EndAllDropdowns();
            layout.BeginDropdown("Destruction",DestructionOpen,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>DestructionOpen=v))!);
            try
            {
                ui.ToggleField("Destructable",on,DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>((Action<bool>)(v=>SetFlag(editor,v)))!,"Plate breaks away after enough damage (Plate Destruction mod). A mirrored pair (left and right skirt) breaks one side at a time.");
            }
            finally{layout.EndAllDropdowns();}
        }
        catch(Exception e){if(!uiWarned){uiWarned=true;Plugin.Warn("Destructable checkbox: "+e);}}
    }
    // Open/closed state of the "Destruction" section: open by default, remembered while the game runs.
    internal static bool DestructionOpen=true;
    private static void SetFlag(PlateStructureEditor ed,bool value)
    {
        try
        {
            foreach(var bp in Targets(ed))
            {
                var before=bp.Description;var after=DestructibleFlag.Set(before,value);
                if(string.Equals(before,after,StringComparison.Ordinal))continue;
                bp.Description=after;
                bp.MarkModified();
                Plugin.Info($"[FLAG] editor: Destructable={value} on structure blueprint {bp.Pointer.ToInt64():X}; description '{before}' -> '{after}'");
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
        BallisticsEffectProbe.Read(out string kind,out bool visualOnly);
        var e=new BlastEvent{Pos=info.Position,Normal=info.HitNormal,Velocity=info.HitVelocity,Calibre=info.Calibre,Type=info.Type,Frame=UnityEngine.Time.frameCount,Kind=kind,VisualOnly=visualOnly};
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
