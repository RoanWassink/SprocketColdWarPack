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
    private static void Warn(Exception error)
    {if(warnings++<4)Plugin.ModLog.LogWarning("[Original trajectory] Native continuation retained: "+error.Message);}
}
