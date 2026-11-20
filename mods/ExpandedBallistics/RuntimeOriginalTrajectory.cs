using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;
namespace SprocketExpandedBallistics;

// Separate optional owner. No byref-return detour and no incoming direction rewrite.
[HarmonyPatch]
internal static class RuntimeOriginalTrajectory
{
    internal static bool Ready {get;private set;}
    private static int diagnostics,warnings;
    private sealed record Capture(IntPtr Simulation,short Index,int Count,Vector3 Direction,ShellImpactContext Context);
    internal static bool Install(Harmony owner)
    {
        Ready=false;
        if(!GameBuildGate.AllowHotPath)return false; // 0.22.95 game-build gate: shot/hit-path hooks only on an audited GameAssembly
        owner.CreateClassProcessor(typeof(RuntimeOriginalTrajectory)).Patch();
        var method=AccessTools.Method(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment),new[]{typeof(PenetrationSimulation),typeof(short)});
        var info=Harmony.GetPatchInfo(method);
        if(info==null||!info.Prefixes.Any(p=>p.owner==owner.Id&&p.PatchMethod.DeclaringType==typeof(RuntimeOriginalTrajectory))||
            !info.Postfixes.Any(p=>p.owner==owner.Id&&p.PatchMethod.DeclaringType==typeof(RuntimeOriginalTrajectory)))
            throw new InvalidOperationException("Native original-trajectory hooks not registered.");
        Ready=true;return true;
    }
    internal static void Clear(){Ready=false;diagnostics=warnings=0;}
    private static System.Numerics.Vector3 Pure(Vector3 v)=>new(v.x,v.y,v.z);

    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void Begin(PenetrationSimulation sim,short index,out Capture? __state)
    {
        __state=null;
        if(!Ready||RuntimeSpall.Impact is not {} context||!OriginalTrajectoryPolicy.Behavior(context.Behavior))return;
        try
        {
            if(index<0||index>=sim.FragmentCount)return;
            var fragment=sim.fragments[index];
            if((fragment.flags&FragmentFlag.OriginalPenetrator)==0)return;
            __state=new(sim.Pointer,index,sim.FragmentCount,fragment.direction,context);
        }
        catch(Exception ex){Warn(ex);}
    }

    [HarmonyPostfix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void End(PenetrationSimulation sim,short index,Capture? __state)
    {
        if(!Ready||__state is not {} capture||capture.Simulation!=sim.Pointer||capture.Index!=index||
            !ReferenceEquals(capture.Context,RuntimeSpall.Impact))return;
        try
        {
            var count=sim.FragmentCount;
            if(index<0||index>=count||capture.Count<0||count<capture.Count||count>sim.fragments.Length||count-capture.Count>4096)return;
            var parent=sim.fragments[index];int match=-1;
            for(int i=capture.Count;i<count;i++)
            {
                var child=sim.fragments[i];
                if(nonFinite<5&&!(OriginalTrajectoryPolicy.Finite(Pure(child.spawnPoint))&&OriginalTrajectoryPolicy.Finite(Pure(child.direction))))NonFinite(sim.Pointer,index,i,child.spawnPoint,child.direction); // 0.22.90: diagnostics only (FEAT-0.22.90 C), reads
                if(!OriginalTrajectoryPolicy.Eligible((ushort)parent.flags,(ushort)child.flags,child.parentIndex,index,
                    parent.projectileProfileIndex,child.projectileProfileIndex,parent.mass,child.mass,parent.diameter,child.diameter,
                    child.speed,Pure(capture.Direction),Pure(child.direction),Pure(child.spawnPoint)))continue;
                if(match>=0)return; // Ambiguous native descendants: leave all native directions unchanged.
                match=i;
            }
            if(match<0)return;
            var selected=sim.fragments[match];var native=selected.direction;
            selected.direction=capture.Direction;sim.fragments[match]=selected;
            if(ReleaseDiagnostics.Enabled&&diagnostics++<16)
                Plugin.ModLog.LogInfo($"[Original trajectory] {capture.Context.Behavior} successful continuation kept incoming axis; native bend={Vector3.Angle(capture.Direction,native):0.0}deg; speed/exit/flags preserved.");
        }
        catch(Exception ex){Warn(ex);}
    }
    // 0.22.90 begin
    // FEAT-0.22.90 C, diagnostics only (no hook, no write): a new native child with a non-finite spawn point or direction is the NaN link of the
    // 16-11 vanishing (a block at rhaFactor 0: budget +inf, the overmatch blend NaN, a ricochet child at a NaN position; VANISH-MAP section 2).
    // At most 5 lines per session. Seen in this hook's own calls only: an original penetrator of a HEAT or APFSDS shot (OriginalTrajectoryPolicy.Behavior).
    private static int nonFinite;
    private static void NonFinite(IntPtr sim,short index,int child,Vector3 spawn,Vector3 direction)
    {
        nonFinite++;
        try
        {
            var c=System.Globalization.CultureInfo.InvariantCulture;
            string V(Vector3 v)=>string.Create(c,$"({v.x:g4}, {v.y:g4}, {v.z:g4})");
            Plugin.ModLog.LogWarning("[Original trajectory] non-finite child spawn/direction after block "+(NativeMaterialReactions.OpenWriteText(sim,index)??"(no EB rhaFactor write in this call)")+
                string.Create(c,$"; fragment {index}, child {child}, spawn {V(spawn)}, direction {V(direction)} (logged at most 5 times per session)"));
        }
        catch(Exception){/* diagnostics never change the impact */}
    }
    // 0.22.90 end
    private static void Warn(Exception error)
    {if(warnings++<4)Plugin.ModLog.LogWarning("[Original trajectory] Native continuation retained: "+error.Message);}
}
