#if !EB_PURE_SOLID_MARK
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.Weapons;
#endif
namespace SprocketExpandedBallistics;

public static class BallisticsEffectApi
{
    public static string CurrentExplosionKind { get; internal set; } = "";
    public static bool CurrentExplosionVisualOnly { get; internal set; }
    public static bool CurrentExplosionHasData { get; internal set; }
    public static double CurrentExplosionCalibreMm { get; internal set; }
    public static double CurrentExplosionPower { get; internal set; }
    public static double CurrentExplosionBreachMm { get; internal set; }
    public static bool CurrentExplosionPerforated { get; internal set; }
    public static bool CurrentExplosionSpaced { get; internal set; }
    public static long CurrentExplosionPerforatedRegister { get; internal set; }
    public static int CurrentExplosionSeq { get; internal set; }
    internal static Action<int, CombatReportEvent, int?, int?>? ReportSink { get; set; }

    internal static void Set(BlastTelemetry telemetry)
    {
        CurrentExplosionKind = telemetry.Kind;
        CurrentExplosionVisualOnly = telemetry.VisualOnly;
        CurrentExplosionHasData = true;
        CurrentExplosionCalibreMm = telemetry.CalibreMm;
        CurrentExplosionPower = telemetry.Power;
        CurrentExplosionBreachMm = telemetry.BreachMm;
        CurrentExplosionPerforated = telemetry.Perforated;
        CurrentExplosionSpaced = telemetry.Spaced;
        CurrentExplosionPerforatedRegister = telemetry.PerforatedRegister;
        CurrentExplosionSeq++;
    }

    internal static void Clear()
    {
        CurrentExplosionKind = "";
        CurrentExplosionVisualOnly = false;
        CurrentExplosionHasData = false;
        CurrentExplosionCalibreMm = 0;
        CurrentExplosionPower = 0;
        CurrentExplosionBreachMm = 0;
        CurrentExplosionPerforated = false;
        CurrentExplosionSpaced = false;
        CurrentExplosionPerforatedRegister = 0;
    }

    private static void DispatchReport(int seq, CombatReportEvent evt, int? crewAliveAfter = null, int? crewTotal = null)
    {
        var sink = ReportSink;
        if (sink != null) { sink(seq, evt, crewAliveAfter, crewTotal); return; }
#if !EB_PURE_SOLID_MARK
        RuntimeDamageFeed.AcceptBlastEvent(seq, evt, crewAliveAfter, crewTotal);
#endif
    }

    public static void ReportBlastPlate(int seq, int plateVuid, double localThicknessMm, double breachCapacityMm, bool breached, double holeRadiusM, bool isSolidBlock, bool isDirect)
    {
        try
        {
            if (seq <= 0) return;
            var evt = CombatBlastReport.PlateEvent(plateVuid, localThicknessMm, breachCapacityMm, breached, holeRadiusM, isSolidBlock, isDirect);
            DispatchReport(seq, evt);
        }
        catch (Exception) { }
    }

    public static void ReportBlastEraStripped(int seq, int count, double radiusM)
    {
        try
        {
            if (seq <= 0 || count <= 0) return;
            var evt = CombatBlastReport.EraStrippedEvent(count, radiusM);
            if (evt != null) DispatchReport(seq, evt);
        }
        catch (Exception) { }
    }

    public static void ReportBlastBreachDamage(int seq, int plateVuid, int entityVuid, string componentType, double distM, double damageFraction, bool alive, int crewAliveAfter, int crewTotal)
    {
        try
        {
            if (seq <= 0) return;
            var evt = CombatBlastReport.BreachDamageEvent(entityVuid, componentType, alive);
            DispatchReport(seq, evt, crewTotal > 0 ? crewAliveAfter : null, crewTotal > 0 ? crewTotal : null);
        }
        catch (Exception) { }
    }
}

#if !EB_PURE_SOLID_MARK
[HarmonyPatch]
internal static class RuntimeShellEffects
{
    private sealed record ScaleState(ProjectileImpactEffect Owner,ProjectileImpactEffect.ShellImpactEffectAssets? Assets,float Minimum,float Maximum,bool ClearTelemetry);
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileImpactEffect),nameof(ProjectileImpactEffect.PlayEffect))]
    private static void Scale(ProjectileImpactEffect __instance,ProjectileEffectInfo __0,out ScaleState? __state)
    {
        __state=null;
        var context=RuntimeSpall.Impact;
        if(context?.ArmourEffectPlaying==true)return;
        if(context is not {LiveImpact:true} || context.Behavior is not ("aphe" or "heat" or "hesh" or "he") || (__0.Type & ProjectileEffectType.Explosion)==0)return;
        var clearTelemetry=false;
        if(context.Behavior=="he" && !BallisticsEffectApi.CurrentExplosionHasData)
        {
            var telemetry=BlastTelemetryMath.ForExplosion("he",context.Profile,context.GunDiameter*1000.0,RuntimeSpall.He);
            BallisticsEffectApi.Set(telemetry);
            RuntimeDamageFeed.RecordBlastTelemetry(BallisticsEffectApi.CurrentExplosionSeq,telemetry);
            clearTelemetry=true;
        }
        var assets=__instance.explosionAssets;
        if(assets==null)
        {
            if(clearTelemetry)__state=new(__instance,null,0,0,true);
            return;
        }
        __state=new(__instance,assets,assets.MinScale,assets.MaxScale,clearTelemetry);
        var scale=ShellBalance.Visual(context.Behavior=="aphe" ? ApheRules.Effective(context.Profile,RuntimeSpall.Aphe).ExplosionVisualScale : context.Profile!.ExplosionScale,context.GunDiameter*1000);
        assets.MinScale*=(float)scale;
        assets.MaxScale*=(float)scale;
        __instance.explosionAssets=assets;
        Plugin.ModLog.LogInfo($"[APHE Effect] {context.Behavior} asset scale bounds multiplied by {scale:0.00}");
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(ProjectileImpactEffect),nameof(ProjectileImpactEffect.PlayEffect))]
    private static void RestoreScale(ScaleState? __state)
    {
        if(__state==null)return;
        if(__state.Assets!=null)
        {
            __state.Assets.MinScale=__state.Minimum;__state.Assets.MaxScale=__state.Maximum;
            __state.Owner.explosionAssets=__state.Assets;
        }
        if(__state.ClearTelemetry)BallisticsEffectApi.Clear();
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileEffectConfig),nameof(ProjectileEffectConfig.PlayEffect))]
    private static void Play(ProjectileEffectConfig __instance,ProjectileEffectInfo __0)
    {
        var context=RuntimeSpall.Impact;
        if(context?.ArmourEffectPlaying==true)return;
        if(context is {LiveImpact:true} && context.ArmourExplosions.Count>0)
        {
            var positions=context.ArmourExplosions.ToArray();context.ArmourExplosions.Clear();
            context.ArmourEffectPlaying=true;
            try
            {
                BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion("era",null,35));
                foreach(var position in positions)
                    __instance.PlayEffect(new ProjectileEffectInfo {Type=ProjectileEffectType.Explosion,Position=position,
                        HitNormal=__0.HitNormal,HitVelocity=__0.HitVelocity,Parent=__0.Parent,Calibre=35});
            }
            catch(Exception ex){Plugin.ModLog.LogWarning("[ERA] Visual unavailable: "+ex.Message);}
            finally{BallisticsEffectApi.Clear();context.ArmourEffectPlaying=false;}
        }
        if(context is {LiveImpact:true,ChemicalVisualPlayed:false} && context.Behavior is "heat" or "hesh" && (__0.Type & ProjectileEffectType.Explosion)==0)
        { context.ChemicalVisualPlayed=true; context.ExplosionPending=true; context.ExplosionPosition=__0.Position; }
        if(context is not {LiveImpact:true,ExplosionPending:true} || context.Behavior is not ("aphe" or "heat" or "hesh"))return;
        context.ExplosionPending=false; // Also guards the nested visual-only PlayEffect call.
        if(context.Behavior=="aphe" && !RuntimeSpall.Settings.ApheExplosionEffect)return;
        try
        {
            var calibreMm=context.GunDiameter*1000.0;
            var telemetry=BlastTelemetryMath.ForExplosion(context.Behavior,context.Profile,calibreMm,RuntimeSpall.He,perforated:context.Behavior=="aphe");
            BallisticsEffectApi.Set(telemetry);
            if(context.Behavior=="hesh")RuntimeDamageFeed.RecordBlastTelemetry(BallisticsEffectApi.CurrentExplosionSeq,telemetry);
            __instance.PlayEffect(new ProjectileEffectInfo {
                Type=ProjectileEffectType.Explosion,Position=context.ExplosionPosition,
                HitNormal=__0.HitNormal,HitVelocity=__0.HitVelocity,Parent=__0.Parent,
                Calibre=(ushort)Math.Clamp(Math.Round(calibreMm),1,ushort.MaxValue)
            });
            Plugin.ModLog.LogInfo("[APHE Effect] Native explosion visual requested; no added blast damage.");
        }
        catch(Exception ex){Plugin.ModLog.LogError("[APHE Effect] Visual unavailable: "+ex);}
        finally{BallisticsEffectApi.Clear();}
    }
}
#endif
