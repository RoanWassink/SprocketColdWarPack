using System.Globalization;
using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;
namespace SprocketExpandedBallistics;

// Separate owner is necessary: tandem itself is ordered after the simulator owner.
// Putting an after-tandem prefix into that simulator owner would create a cycle.
[HarmonyPatch]
internal static class RuntimeArmourComparisonFinalInput
{
    private const string Owner="sprocket.expandedballistics.comparison.finalinput";
    internal static void Install()
    {
        try{new Harmony(Owner).PatchAll(typeof(RuntimeArmourComparisonFinalInput));}
        catch(Exception ex){new Harmony(Owner).UnpatchSelf();Plugin.ModLog.LogWarning("[Array comparison] Selected-input check unavailable: "+ex.Message);}
    }
    internal static void Clear()=>new Harmony(Owner).UnpatchSelf();
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    [HarmonyPriority(Priority.Last-200),HarmonyAfter("sprocket.expandedballistics","sprocket.expandedballistics.simulator","sprocket.expandedballistics.tandemprobe")]
    private static void Capture(PenetratorInfo penetrator)=>RuntimeArmourComparisonCapture.CaptureInput(penetrator);
}

// Numeric copy of the actual final simulator input, not a profile-name reconstruction.
internal sealed record ArmourComparisonSelectedInput(float Mass,float Diameter,float Density,float MaxNormalization,
    ushort Constant,Vector3 Position,Vector3 Velocity,ShellProfile Profile,ImpactGeometry Geometry,
    string ThreatClass,float GunDiameter,double ChemicalBudget,bool ChemicalInitialized)
{
    internal double InitialBudgetMm=>Math.Pow(Math.Sqrt(Mass)*Math.Sqrt(Velocity.sqrMagnitude)/
        (Math.Pow(Diameter*10,.75)*Constant),1.43)*100;
    internal PenetratorInfo Input()=>new(Mass,Diameter,Density,MaxNormalization,Constant,Position,Velocity);
    internal bool MatchesInput(PenetratorInfo p)=>p.Mass==Mass&&p.Diameter==Diameter&&p.Density==Density&&
        p.MaxNormalizationAngle==MaxNormalization&&p.PenetratorConstant==Constant&&p.Position==Position&&p.Velocity==Velocity;
    internal ShellImpactContext Context(ArmourComparisonTarget target)=>new(){ProfileId=Profile.Id,Profile=Profile,
        Geometry=Geometry,ThreatClass=ThreatClass,GunDiameter=GunDiameter,ChemicalBudget=ChemicalBudget,
        ChemicalInitialized=ChemicalInitialized,LiveImpact=false,ArmourSnapshot=target.Snapshot,
        Experimental=target.Snapshot.Experimental,FiringEra=target.Era,SharedPreviewCells=new EraCells()};
    internal static ArmourComparisonSelectedInput? Capture(PenetratorInfo p,ShellImpactContext? context)
    {
        if(context is not {LiveImpact:false,Profile:{} profile,Geometry:{} geometry}||
            profile.TandemPrecursorPenetrationMm!=0||context.Behavior is not ("apfsds" or "heat"))return null;
        if(!float.IsFinite(p.Mass)||p.Mass<=0||!float.IsFinite(p.Diameter)||p.Diameter<=0||
            !float.IsFinite(p.Density)||p.Density<=0||!float.IsFinite(p.MaxNormalizationAngle)||
            p.PenetratorConstant==0||!RuntimeArmourComparisonCapture.Finite(p.Position)||
            !RuntimeArmourComparisonCapture.Finite(p.Velocity)||!float.IsFinite(p.Velocity.sqrMagnitude)||p.Velocity.sqrMagnitude<=0)return null;
        return new(p.Mass,p.Diameter,p.Density,p.MaxNormalizationAngle,p.PenetratorConstant,p.Position,p.Velocity,
            profile,geometry,context.ThreatClass,context.GunDiameter,context.ChemicalBudget,context.ChemicalInitialized);
    }
    internal bool MatchesInitial(PenetrationSimulation sim,short index)
    {
        if(index!=0||sim.FragmentCount<=0)return false;
        var f=sim.fragments[index];
        if((f.flags&FragmentFlag.OriginalPenetrator)==0||f.projectileProfileIndex>=sim.projectileProfiles.Length)return false;
        static bool Near(float a,float b)=>float.IsFinite(a)&&float.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(1e-6,Math.Abs(b)*1e-5);
        return Near(f.mass,Mass)&&Near(f.diameter,Diameter)&&Near(f.speed,MathF.Sqrt(Velocity.sqrMagnitude))&&
            sim.projectileProfiles[f.projectileProfileIndex].penetratorConstant==Constant&&
            (sim.SimToWorldSpaceDirection(f.direction).normalized-Velocity.normalized).sqrMagnitude<1e-8f&&
            (sim.SimToWorldSpacePoint(f.spawnPoint)-Position).sqrMagnitude<1e-8f;
    }
}

internal static class RuntimeArmourComparisonParity
{
    private static ArmourComparisonTarget? lastTarget;
    private static ArmourComparisonProbeResult? lastReplay;
    private static readonly HashSet<string> sampleDiagnostics=new(StringComparer.Ordinal);
    internal static void Clear(){lastTarget=null;lastReplay=null;sampleDiagnostics.Clear();}
    internal static void ObserveOrdinary(ArmourComparisonTarget target,PenetrationSimulation simulation)
    {
        try{if(OrdinaryDiagnostic(target,simulation) is {} line)Plugin.ModLog.LogInfo("[Armour simulator sample] "+line);}
        catch(Exception){/* Optional bounded evidence never changes the ordinary simulation. */}
    }
    internal static string? OrdinaryDiagnostic(ArmourComparisonTarget target,PenetrationSimulation simulation)
    {
        if(target.OrdinaryWholeModel is not {} outcome||outcome.Outcome==ComparisonProbeOutcome.Perforated||sampleDiagnostics.Count>=8||
            simulation.FragmentCount<1||simulation.FragmentCount>16384)return null;
        var input=target.SelectedInput;
        var key=FormattableString.Invariant($"{target.Entity}|{Math.Round(target.ActualAngleDegrees/5)}|{input?.GunDiameter}|{input?.Diameter}|{input?.Constant}|{(input==null?0:Math.Round(input.InitialBudgetMm))}");
        if(!sampleDiagnostics.Add(key))return null;
        var state=input==null?"verifiedSelectedInput=unavailable":string.Format(CultureInfo.InvariantCulture,
            "gun={0:0.###}mm inputP={1:0.###}mm coreD={2:0.###}mm coreL={3:0.###}mm mass={4:0.######}kg density={5:0.###} K={6} speed={7:0.###}m/s maxNorm={8:0.######}",
            input.GunDiameter*1000,input.InitialBudgetMm,input.Diameter*1000,input.Geometry.LengthM*1000,input.Mass,
            input.Density,input.Constant,Math.Sqrt(input.Velocity.sqrMagnitude),input.MaxNormalization);
        var tails=new List<string>();
        for(int i=simulation.FragmentCount-1;i>=0&&tails.Count<4;i--)
        {
            var f=simulation.fragments[i];if((f.flags&FragmentFlag.OriginalPenetrator)==0)continue;
            double budget=f.projectileProfileIndex<simulation.projectileProfiles.Length?
                f.GetBasePenetration(simulation.projectileProfiles[f.projectileProfileIndex].penetratorConstant)*1000:double.NaN;
            tails.Add(FormattableString.Invariant($"i={i},parent={f.parentIndex},flags=0x{(ushort)f.flags:X},v={f.speed:0.###},P={budget:0.###}"));
        }
        return state+FormattableString.Invariant($"; entryEntity={target.Entity} angle={target.ActualAngleDegrees:0.###}deg; wholeModel={outcome.Outcome} ({outcome.Reason}); lastOriginals=[")+
            string.Join(";",tails)+"]; bounded8 distinct stopped/unknown samples per plugin lifecycle; whole-model stop does not identify first stopping plate";
    }
    internal static string Test(ArmourComparisonTarget target)
    {
        lastTarget=target;lastReplay=null;
        string summary;
        if(target.SelectedInput is not {} selected)
            summary="Selected-shell check unavailable: sample a supported single-charge HEAT or APFSDS profile again.";
        else
        {
            var replay=RuntimeArmourComparisonRunner.ProbeSelected(target,selected);
            lastReplay=replay;
            summary=Describe(selected,target.OrdinaryWholeModel,replay);
        }
        Plugin.ModLog.LogInfo("[Array comparison selected check] "+summary.Replace('\n',' '));
        return summary;
    }
    internal static string Describe(ArmourComparisonSelectedInput selected,ArmourComparisonProbeResult? ordinary,ArmourComparisonProbeResult replay)
    {
        var geometry=string.Format(CultureInfo.InvariantCulture,"Selected {0:0.#} mm / {1:0.#} mm input: ",
            selected.GunDiameter*1000,selected.InitialBudgetMm);
        var originalExit=ordinary?.Outcome==ComparisonProbeOutcome.Perforated;
        var repeat=replay.Outcome switch{ComparisonProbeOutcome.Perforated=>"replay passes front armour.",
            ComparisonProbeOutcome.Stopped=>"replay stops or deflects.",_=>"replay outcome unknown."};
        var verdict=originalExit&&replay.Outcome==ComparisonProbeOutcome.Stopped?"MISMATCH with original model exit; inspect exact path.":
            originalExit&&replay.Outcome==ComparisonProbeOutcome.Perforated?"Original model exit agrees.":
            replay.Outcome==ComparisonProbeOutcome.Indeterminate||ordinary?.Outcome==ComparisonProbeOutcome.Indeterminate||ordinary==null?
            "Not enough evidence to compare.":"Original stopped somewhere in model; front-armour outcome is not proven.";
        return geometry+repeat+"\n"+verdict;
    }
    internal static string Details(ArmourComparisonTarget target)
    {
        if(target.SelectedInput is not {} s)return "No verified post-prefix selected input. Tandem and vanilla profiles are not supported by this check.";
        return string.Format(CultureInfo.InvariantCulture,
            "Exact selected-input replay: gun {0:0.###} mm; core diameter {1:0.###} mm; length {2:0.###} mm; mass {3:0.######} kg; native density {4:0.###} kg/m³; K {5}; max normalization {6:0.######}; input speed {7:0.###} m/s; native nominal budget {8:0.###} mm.\nOrdinary whole-model outcome: {9}. The replay uses the same world position/velocity, profile, geometry, era and armour snapshot, with fresh preview state. Front-armour pass and whole-model stop are different endpoints. No real-gun performance prediction is made.",
            s.GunDiameter*1000,s.Diameter*1000,s.Geometry.LengthM*1000,s.Mass,s.Density,s.Constant,s.MaxNormalization,
            Math.Sqrt(s.Velocity.sqrMagnitude),s.InitialBudgetMm,target.OrdinaryWholeModel?.Reason??"unavailable")+
            (ReferenceEquals(lastTarget,target)?"\nSelected replay outcome: "+(lastReplay?.Reason??"unavailable"):"");
    }
}
