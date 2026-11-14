using HarmonyLib;
using Sprocket.DamageModelling;
using Il2CppInterop.Runtime;
using UnityEngine;
namespace SprocketExpandedBallistics;
// Live same-tick, one-target native probe. NOT a release tandem/timing model.
[HarmonyPatch]
internal static class RuntimeTandem
{
 internal static string DamageStage=>current is {} attempt?(attempt.InsidePrecursor?"Precursor":"Main"):"";
 internal static bool InPrecursor=>PreviewInsidePrecursor||current is {InsidePrecursor:true};
 internal static bool Ready {get;private set;}
 internal const string Owner="sprocket.expandedballistics.tandemprobe";
 internal static void VerifyOrdering(bool requireSimulator=false)
 {
  foreach(var method in new[]{AccessTools.Method(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel)),AccessTools.Method(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))})
  {
   var info=Harmony.GetPatchInfo(method)??throw new InvalidOperationException("Missing tandem patch info");
   var sorted=PatchProcessor.GetSortedPatchMethods(method,info.Prefixes.ToArray());
   var tandem=sorted.FindIndex(m=>m.DeclaringType==typeof(RuntimeTandem));
   var core=sorted.Select((m,i)=>(m,i)).Where(x=>x.m.DeclaringType==typeof(RuntimeSpall)||x.m.DeclaringType==typeof(RuntimeShellSelection)||x.m.DeclaringType==typeof(RuntimeArmourSimulator)).Select(x=>x.i).ToArray();
   if(requireSimulator&&method.DeclaringType==typeof(CompoundStructure))
   {
    if(!sorted.Any(m=>m.DeclaringType==typeof(RuntimeArmourSimulator)))throw new InvalidOperationException("Missing tandem simulator prefix.");
    if(!info.Finalizers.Any(p=>p.PatchMethod.DeclaringType==typeof(RuntimeTandem)&&p.PatchMethod.Name==nameof(PreviewEnd)))throw new InvalidOperationException("Missing tandem preview scope finalizer.");
   }
   if(tandem<0||core.Length==0||core.Any(i=>i>=tandem))throw new InvalidOperationException("Tandem prefix ordering not after core: "+method.Name);
   if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("sorted prefixes "+method.DeclaringType!.Name+"."+method.Name+": "+string.Join(" -> ",sorted.Select(m=>m.DeclaringType!.Name+"."+m.Name)));
  }
  if(requireSimulator)Ready=true;
 }
 private readonly record struct Key(string Definition,int Id,float Spawn,IntPtr Transform);
 private sealed class Carrier
 {
  internal readonly IntPtr Register;internal readonly ShellProfile Profile;internal readonly TandemStage Precursor=new();
  internal Carrier(IntPtr register,ShellProfile profile){Register=register;Profile=profile;}
  internal bool ImpactStarted;
 }
 private sealed class Attempt
 {
  internal readonly Carrier Carrier;internal readonly ShellImpactContext Main;internal readonly IntPtr Target;
  internal bool InsidePrecursor,ReachedNative;
  internal Attempt(Carrier carrier,ShellImpactContext main,IntPtr target){Carrier=carrier;Main=main;Target=target;}
 }
 [ThreadStatic] private static Attempt? current;
 [ThreadStatic] internal static bool PreviewInsidePrecursor;
 private sealed record PreviewScope(ShellImpactContext Main,EraCells? Previous);
 private static readonly Dictionary<Key,Carrier> carriers=new();
 private static int traceBudget=100;
 private static int mainThread;
 internal static void Configure(){Ready=false;mainThread=Environment.CurrentManagedThreadId;}
 private static Key Identity(ProjectileInstance p)=>new(p.Definition.Guid.ToString(),p.ID,p.spawnTime,p.transform?.Pointer??IntPtr.Zero);
 internal static void Remember(ProjectileRegister register,ProjectileInstance p,ShellProfile profile)
 {
  if(profile.TandemPrecursorPenetrationMm<=0||Environment.CurrentManagedThreadId!=mainThread)return;
  if(carriers.Count>=256){if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("launch latch capacity exceeded; probe shot will be withheld");return;}
  carriers[Identity(p)]=new(register.Pointer,profile);
 }
 internal static void Clear(){Ready=false;carriers.Clear();current=null;}
 private static void Trace(string text){if(traceBudget--<=0)return;if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Tandem probe] "+text);}
 [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Release))]
 private static void Released(ProjectileRegister __instance,int __0)
 {foreach(var key in carriers.Where(x=>x.Value.Register==__instance.Pointer&&x.Key.Id==__0).Select(x=>x.Key).ToArray())carriers.Remove(key);}
 [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.DestroyAll))]
 private static void Destroyed(ProjectileRegister __instance)
 {foreach(var key in carriers.Where(x=>x.Value.Register==__instance.Pointer).Select(x=>x.Key).ToArray())carriers.Remove(key);}
 private sealed record ImpactState(Attempt? Previous,Attempt? Active);
 [HarmonyPrefix,HarmonyPriority(Priority.Last),HarmonyAfter("sprocket.expandedballistics","sprocket.expandedballistics.simulator")]
 [HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
 private static bool Begin(ref ProjectileInstance __0,ref ProjectileDamageModelHit __1,ref DamageModelHitResult __result,out ImpactState __state)
 {
  __state=new(current,null);var main=RuntimeSpall.Impact;
  if(main is not{LiveImpact:true,Profile:{TandemPrecursorPenetrationMm:>0}})return true;
  if(Environment.CurrentManagedThreadId!=mainThread||!carriers.TryGetValue(Identity(__0),out var carrier)||carrier.ImpactStarted)
  {
   __result=DamageModelHitResult.Absorbed;__0.velocity=Vector3.zero;
   if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("carrier not latched or second target: withheld (single-target probe, no main-budget refill)");return false;
  }
  carrier.ImpactStarted=true;
  var model=__1.ModelHit.Model?.TryCast<CompoundStructure>();
  if(model==null){if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("unsupported target before commit; stock main only");return true;}
  var attempt=new Attempt(carrier,main,model.Pointer);current=attempt;__state=__state with{Active=attempt};
  if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace($"armed profile={carrier.Profile.Id} id={__0.ID} precursorReference={carrier.Profile.TandemPrecursorPenetrationMm:0.0} mainBudget={main.ChemicalBudget:0.0}; native ray Position retained");
  return true;
 }
 [HarmonyPrefix,HarmonyPriority(Priority.Last),HarmonyAfter("sprocket.expandedballistics","sprocket.expandedballistics.simulator")]
 [HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
 private static void Precursor(CompoundStructure __instance,ref PenetratorInfo penetrator,out PreviewScope? __state)
 {
  __state=null;
  if(!PreviewInsidePrecursor&&RuntimeSpall.Impact is {LiveImpact:false,Profile:{TandemPrecursorPenetrationMm:>0}} preview)
  {
   __state=new(preview,preview.SharedPreviewCells);
   preview.SharedPreviewCells=new EraCells();
   Preview(__instance,penetrator,preview);
   return;
  }
  var attempt=current;
  if(attempt==null||attempt.InsidePrecursor||attempt.ReachedNative||RuntimeSpall.Impact!=attempt.Main||__instance.Pointer!=attempt.Target)return;
  attempt.ReachedNative=true;var stage=attempt.Carrier.Precursor;var main=attempt.Main;
  var budget=TandemStage.Budget(attempt.Carrier.Profile.TandemPrecursorPenetrationMm,main.GunDiameter*1000,attempt.Carrier.Profile.ReferenceCalibreMm);
  var velocity=penetrator.Velocity;var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(penetrator.Diameter*1000,penetrator.Mass,(float)budget,penetrator.PenetratorConstant);
  if(!main.ChemicalInitialized||budget<=0||!float.IsFinite(speed)||speed<=0||!float.IsFinite(velocity.x)||!float.IsFinite(velocity.y)||!float.IsFinite(velocity.z)||velocity.sqrMagnitude<=0)
  {stage.Fail();if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("invalid precursor before commit; stock main only");return;}
  // Copy native world-space input. Only independent speed/budget changes. Never use HitPoint or move behind armour.
  var precursor=new PenetratorInfo(penetrator.Mass,penetrator.Diameter,penetrator.Density,0,penetrator.PenetratorConstant,penetrator.Position,velocity.normalized*speed);
  var context=new ShellImpactContext{ProfileId=main.ProfileId,Profile=attempt.Carrier.Profile,Experimental=main.Experimental,ArmourSnapshot=main.ArmourSnapshot,Geometry=main.Geometry,
   ThreatClass="unknown",GunDiameter=main.GunDiameter,FiringEra=main.FiringEra,LiveImpact=true,ChemicalBudget=budget,ChemicalInitialized=true};
  attempt.InsidePrecursor=true;RuntimeSpall.Impact=context;
  try
  {
   if(!stage.BeginSimulation())throw new InvalidOperationException("Precursor already started.");
   if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace($"precursor simulate-enter budget={budget:0.0} target={attempt.Target}; ERA commit boundary");
   var simulation=__instance.Penetrate(ref precursor,PenetrationSimulationOptions.Default);
   if(simulation==null)throw new InvalidOperationException("No precursor simulation.");
   var concrete=simulation.TryCast<PenetrationSimulation>();
   if(concrete?.Transform==null)throw new InvalidOperationException("Precursor target transform unavailable.");
   if(simulation.Hit)
   {
    if(!stage.BeginApply())throw new InvalidOperationException("Precursor ApplyFragmentHits already entered.");
    if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace($"precursor apply-enter sim={simulation.Pointer}; exactly once");
    simulation.ApplyFragmentHits();
    if(concrete.Transform==null)throw new InvalidOperationException("Target destroyed by precursor; main withheld.");
   }
   stage.Complete();if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace($"precursor complete sim={simulation.Pointer}; stock main next with own budget={main.ChemicalBudget:0.0}");
  }
  catch(Exception ex)
  {
   stage.Fail();if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("precursor uncertain/partial commit; main withheld: "+ex.Message);
   throw; // Outer AP finalizer absorbs after commit. Never duplicate through stock fallback.
  }
  finally
  {
   main.PlacedActivations.AddRange(context.PlacedActivations);main.ArmourExplosions.AddRange(context.ArmourExplosions);
   RuntimeSpall.Impact=main;attempt.InsidePrecursor=false;
  }
 }
 private static void Preview(CompoundStructure target,PenetratorInfo mainInput,ShellImpactContext main)
 {
  var profile=main.Profile!;
  var budget=TandemStage.Budget(profile.TandemPrecursorPenetrationMm,main.GunDiameter*1000,profile.ReferenceCalibreMm);
  var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(mainInput.Diameter*1000,mainInput.Mass,(float)budget,mainInput.PenetratorConstant);
  var velocity=mainInput.Velocity;
  if(!main.ChemicalInitialized||budget<=0||!float.IsFinite(speed)||speed<=0||!float.IsFinite(velocity.x)||!float.IsFinite(velocity.y)||!float.IsFinite(velocity.z)||velocity.sqrMagnitude<=0)
   throw new InvalidOperationException("Invalid tandem preview precursor; main preview withheld.");
  var precursor=new PenetratorInfo(mainInput.Mass,mainInput.Diameter,mainInput.Density,0,mainInput.PenetratorConstant,mainInput.Position,velocity.normalized*speed);
  var context=new ShellImpactContext{ProfileId=main.ProfileId,Profile=profile,Experimental=main.Experimental,ArmourSnapshot=main.ArmourSnapshot,Geometry=main.Geometry,
   ThreatClass="unknown",GunDiameter=main.GunDiameter,FiringEra=main.FiringEra,LiveImpact=false,ChemicalBudget=budget,ChemicalInitialized=true,SharedPreviewCells=main.SharedPreviewCells};
  var stage=new TandemStage();PreviewInsidePrecursor=true;RuntimeSpall.Impact=context;
  try
  {
   if(!stage.BeginSimulation())throw new InvalidOperationException("Tandem preview precursor already started.");
   var simulation=target.Penetrate(ref precursor,PenetrationSimulationOptions.Default);
   if(simulation==null||simulation.TryCast<PenetrationSimulation>()?.Transform==null)throw new InvalidOperationException("Tandem preview precursor unavailable; main withheld.");
   // Native simulation consumes only the shared transient ledger; never apply fragments or live effects.
   stage.Complete();
  }
  catch{stage.Fail();throw;}
  finally{RuntimeSpall.Impact=main;PreviewInsidePrecursor=false;}
 }
 [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
 private static void PreviewEnd(PreviewScope? __state)
 {if(__state!=null)__state.Main.SharedPreviewCells=__state.Previous;}
 [HarmonyFinalizer,HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
 private static Exception? End(Exception? __exception,ref ProjectileInstance __0,ref DamageModelHitResult __result,ImpactState __state)
 {
  try
  {
   if(__state.Active is{} attempt)
   {
    if(__exception!=null&&attempt.Carrier.Precursor.Committed)
    {
     __result=DamageModelHitResult.Absorbed;__0.velocity=Vector3.zero;
     if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace("native failure after stage commit; carrier absorbed, no fallback: "+__exception.Message);return null;
    }
    if(ReleaseDiagnostics.Enabled&&traceBudget>0)Trace($"main stock-result={__result} reachedPrecursor={attempt.ReachedNative} phase={attempt.Carrier.Precursor.Phase}; single carrier impulse/effects owner");
   }
   return __exception;
  }
  finally{current=__state.Previous;}
 }
}
