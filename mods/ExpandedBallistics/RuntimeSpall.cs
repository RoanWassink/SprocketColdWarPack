using BepInEx;
using HarmonyLib;
using Sprocket.DamageModelling;
namespace SprocketExpandedBallistics;
internal sealed class ShellImpactContext
{
    internal string ProfileId = "";
    internal string ThreatClass {get;init;} = "unknown";
    internal ShellProfile? Profile;
    internal ImpactGeometry? Geometry;
    internal ArmourCatalogueSnapshot? ArmourSnapshot;
    internal SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmourCatalogue? Experimental;
    internal string Behavior => Profile is {} profile ? ShellBalance.ImpactBehavior(profile) : ProfileId;
    internal bool ChemicalInitialized;
    internal bool ChemicalVisualPlayed;
    internal readonly HashSet<IntPtr> PayloadBursts = new();
    internal readonly SpacedKineticGate SpacedKineticShots = new();
    internal readonly HashSet<IntPtr> CapStripped = new();
    internal readonly Dictionary<IntPtr,ChemicalLayers> Layers = new();
    internal readonly ChemicalDiagnosticLedger ChemicalDiagnostics = new();
    internal float GunDiameter;
    internal string? FiringEra;
    internal double ChemicalBudget;
    internal bool LiveImpact;
    internal EraCells? SharedPreviewCells;
    internal bool ExplosionPending;
    internal UnityEngine.Vector3 ExplosionPosition;
    internal readonly List<UnityEngine.Vector3> ArmourExplosions=new();
    internal readonly List<PlacedEraActivation> PlacedActivations=new();
    internal bool ArmourEffectPlaying;
}
[HarmonyPatch]
internal static class RuntimeSpall
{
    internal static SpallSettings Settings = new();
    [ThreadStatic] internal static ShellImpactContext? Impact;
    [ThreadStatic] private static UnityEngine.Vector3? apheAxis;
    [ThreadStatic] private static PenetrationSimulation? layerSimulation;
    [ThreadStatic] private static short layerFragment;
    [ThreadStatic] private static CompoundStructure? layerStructure;
    [ThreadStatic] private static PenetrationSimulation? obliquityArmedSimulation;
    private static int chemicalDiagnosticBudget=80;
    // 0.22.22: the simulation of the fragment now being simulated (set for every impact with a context, unlike layerSimulation),
    // the gap ledger of the current Penetrate call, and the ricochet bookkeeping (chain weight per fragment, children that still owe a thin-gap check).
    [ThreadStatic] private static PenetrationSimulation? activeSimulation;
    [ThreadStatic] private static SpallGapLedger? gapLedger;
    [ThreadStatic] private static Dictionary<(IntPtr,int),int>? ricochetCounts;
    [ThreadStatic] private static HashSet<(IntPtr,int)>? thinGapChildren;
    private static SpallGapLedger GapLedger=>gapLedger??=new();
    private static Dictionary<(IntPtr,int),int> RicochetCounts=>ricochetCounts??=new();
    private static HashSet<(IntPtr,int)> ThinGapChildren=>thinGapChildren??=new();
    private static void ResetShotState(){gapLedger?.Clear();ricochetCounts?.Clear();thinGapChildren?.Clear();}
    private sealed record LayerScope(PenetrationSimulation? Simulation,short Index,CompoundStructure? Structure,
        PenetrationSimulation? PreviousArmedSimulation=null,PenetrationSimulation? ChemicalSimulation=null,float? OriginalObliquity=null,
        int RicochetBefore=-1,FragmentFlag RicochetPreFlags=FragmentFlag.None,
        PenetrationSimulation? PreviousActive=null,UnityEngine.Vector3 RicochetPreDirection=default);
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void BeginLayer(CompoundStructure __instance,PenetrationSimulation sim,short index,out LayerScope __state)
    {
        var previousArmed=obliquityArmedSimulation;
        __state=new(layerSimulation,layerFragment,layerStructure,previousArmed,PreviousActive:activeSimulation);
        activeSimulation=sim;
        if(BeginObliquity(sim,index,out var originalObliquity))
        {
            __state=__state with {ChemicalSimulation=sim,OriginalObliquity=originalObliquity};
            obliquityArmedSimulation=sim;
        }
        layerStructure=__instance;
        layerSimulation=SpallBalance.FollowsLayers(Impact?.Profile) ? sim : null;
        layerFragment=index;
        try
        {
            var rules=Settings.FragmentRicochet??FragmentRicochetSettings.Default;
            if(Impact is {} context&&FragmentRicochetModel.Applies(rules,true,context.Behavior,context.Profile?.EffectiveKineticKind)&&index>=0&&index<sim.FragmentCount)
            {
                var fragment=sim.fragments[index];
                if((rules.AffectsOriginal||(fragment.flags&FragmentFlag.OriginalPenetrator)==0)&&(fragment.flags&FragmentFlag.Killed)==0)
                    __state=__state with {RicochetBefore=sim.FragmentCount,RicochetPreFlags=fragment.flags,RicochetPreDirection=fragment.direction};
            }
        }
        catch(Exception){/* Fragment ricochet is optional and never changes the native call. */}
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void EndLayer(LayerScope __state,PenetrationSimulation sim,short index,Exception? __exception)
    {
        try { if(__state.ChemicalSimulation is {} chemical&&__state.OriginalObliquity is float original)RestoreObliquity(chemical,original); }
        finally { obliquityArmedSimulation=__state.PreviousArmedSimulation;layerSimulation=__state.Simulation;layerFragment=__state.Index;layerStructure=__state.Structure;activeSimulation=__state.PreviousActive; }
        if(__state.RicochetBefore>=0&&__exception==null)
        {
            try{FragmentRicochet(sim,index,__state.RicochetBefore,__state.RicochetPreFlags,__state.RicochetPreDirection);}
            catch(Exception ex){if(ricochetWarnings++<3)Plugin.ModLog.LogWarning("[Spall] Fragment ricochet unavailable: "+ex.Message);}
        }
    }
    private static int ricochetWarnings;
    private static bool probeChild,probeInPlace,probeEmbedded,probeGapKill,probeGapSuppress;
    // 0.22.17 point 7 + 0.22.22: children that a ricocheting non-original fragment spawned in this very call carry
    // sqrt(1-loss) of its speed; beyond the maximum number of ricochets they are killed. A ricochet at a grazing incidence
    // counts double (FragmentRicochetModel.Weight). A ricochet that made no child (in place, Deflected and not Embedded) is scaled and counted the
    // same way, through the per-shot side table RicochetCounts (the native Fragment has no spare field for a counter).
    private static void FragmentRicochet(PenetrationSimulation sim,short index,int countBefore,FragmentFlag preFlags,UnityEngine.Vector3 preDirection)
    {
        var rules=Settings.FragmentRicochet??FragmentRicochetSettings.Default;
        if(index<0||index>=sim.FragmentCount)return;
        var parent=sim.fragments[index];
        const FragmentFlag bounce=FragmentFlag.Deflected|FragmentFlag.GougeDeflect;
        if((parent.flags&bounce)==0||(parent.flags&FragmentFlag.Killed)!=0)return;
        // A flag copied from an earlier ricochet must not turn a penetration into one.
        if((preFlags&bounce)!=0&&(parent.flags&FragmentFlag.Penetrated)!=0)return;
        var chain=FragmentRicochetModel.CountChain(j=>sim.fragments[j].parentIndex,j=>(sim.fragments[j].flags&bounce)!=0,index,sim.FragmentCount);
        var prior=RicochetCounts.TryGetValue((sim.Pointer,(int)index),out var known)?known:chain-1;
        var factor=(float)FragmentRicochetModel.SpeedFactor(rules.EnergyLoss);
        int children=0,killed=0,count=chain;
        for(int i=countBefore;i<sim.FragmentCount;i++)
        {
            var child=sim.fragments[i];
            if(child.parentIndex!=index||(child.flags&FragmentFlag.Killed)!=0||(child.flags&FragmentFlag.Embedded)!=0)continue;
            if(!rules.AffectsOriginal&&(child.flags&FragmentFlag.OriginalPenetrator)!=0)continue;
            children++;
            var weight=FragmentRicochetModel.Weight(rules,preDirection.x,preDirection.y,preDirection.z,child.direction.x,child.direction.y,child.direction.z);
            count=prior+weight;
            if(FragmentRicochetModel.Next(count,rules.MaxRicochets)==RicochetOutcome.Kill){child.speed=0;child.flags|=FragmentFlag.Killed;killed++;}
            else
            {
                child.speed*=factor;
                RicochetCounts[(sim.Pointer,i)]=count;
                if(rules.ThinGapKillMm>0)ThinGapChildren.Add((sim.Pointer,i));
            }
            sim.fragments[i]=child;
        }
        var kind=InPlaceKind.None;
        if(children==0&&sim.FragmentCount==countBefore&&(preFlags&bounce)==0)
        {
            kind=FragmentRicochetModel.ClassifyInPlace((parent.flags&FragmentFlag.Deflected)!=0,(parent.flags&FragmentFlag.GougeDeflect)!=0,(parent.flags&FragmentFlag.Embedded)!=0);
            if(kind==InPlaceKind.Ricochet&&(parent.flags&FragmentFlag.Penetrated)==0)
            {
                var weight=FragmentRicochetModel.Weight(rules,preDirection.x,preDirection.y,preDirection.z,parent.direction.x,parent.direction.y,parent.direction.z);
                count=prior+weight;
                if(FragmentRicochetModel.Next(count,rules.MaxRicochets)==RicochetOutcome.Kill){parent.speed=0;parent.flags|=FragmentFlag.Killed;killed++;}
                else parent.speed*=factor;
                RicochetCounts[(sim.Pointer,(int)index)]=count;
                sim.fragments[index]=parent;
                RuntimeArmourResponses.NotifyParentSpeedChange(sim,index);
            }
        }
        if(!ReleaseDiagnostics.Enabled)return;
        // One probe per kind of outcome: does a ricochet spawn a child, or does the fragment change in place?
        if(children>0&&!probeChild)
        {
            probeChild=true;
            var first=sim.fragments[countBefore];
            Plugin.ModLog.LogInfo($"[Spall probe] ricochet SPAWNS a child: parent {index} flags {preFlags}->{parent.flags} speed {parent.speed:0.0}; children {children} (killed {killed}) from {countBefore} to {sim.FragmentCount}; first child flags {first.flags} parent {first.parentIndex} speed {first.speed:0.0}; chain {count}/{rules.MaxRicochets}; factor {factor:0.000}");
        }
        else if(kind==InPlaceKind.Ricochet&&!probeInPlace){probeInPlace=true;Plugin.ModLog.LogInfo(FragmentRicochetModel.InPlaceProbeText(kind,index,preFlags.ToString(),parent.flags.ToString(),parent.speed,count,rules.MaxRicochets,factor));}
        else if(kind==InPlaceKind.Embedded&&!probeEmbedded){probeEmbedded=true;Plugin.ModLog.LogInfo(FragmentRicochetModel.InPlaceProbeText(kind,index,preFlags.ToString(),parent.flags.ToString(),parent.speed,count,rules.MaxRicochets,factor));}
    }
    // 0.22.22: records, for every solid block a fragment of a chemical or APFSDS shot leaves, the gap to the next solid block along its path, and gives a
    // fragment that is the child of a ricochet its one thin-gap check at the first solid block it meets (the distance from where it bounced to that block).
    [HarmonyPostfix,HarmonyPatch(typeof(StructureIntersection),nameof(StructureIntersection.GetNextMaterialBlock))]
    private static void GapLookahead(MaterialBlock __result,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<StructureIntersection> __0,int __1)
    {
        try
        {
            var sim=activeSimulation;var context=Impact;
            if(sim==null||context==null||!__result.Valid||__result.MaterialIndex<0||layerFragment<0||layerFragment>=sim.FragmentCount)return;
            var entry=__result.EnterIntersectionIndex;var exit=__result.ExitIntersectionIndex;
            if(entry<0||exit<0||entry>=__1||exit>=__1||entry>=__0.Length||exit>=__0.Length)return;
            var key=(sim.Pointer,(int)layerFragment);
            var rules=Settings.FragmentRicochet??FragmentRicochetSettings.Default;
            if(ThinGapChildren.Remove(key)&&rules.ThinGapKillMm>0)
            {
                var gapMm=Math.Max(0,__0[entry].distance*1000.0);
                if(SpallGapModel.KillThinGap(rules.ThinGapKillMm,gapMm))
                {
                    var fragment=sim.fragments[layerFragment];
                    fragment.speed=0;fragment.flags|=FragmentFlag.Killed;
                    sim.fragments[layerFragment]=fragment;
                    RuntimeArmourResponses.NotifyParentSpeedChange(sim,layerFragment);
                    if(ReleaseDiagnostics.Enabled&&!probeGapKill){probeGapKill=true;Plugin.ModLog.LogInfo($"[Spall probe] ricochet child {layerFragment} killed in a thin gap: next solid block {gapMm:0.0} mm ahead < {rules.ThinGapKillMm:0.#} mm.");}
                }
            }
            if(!SpallGapModel.Applies(context.Behavior)||Settings.MinSpallGapMm<=0)return;
            var n=Math.Min(__1,__0.Length);
            var distance=new float[n];var enter=new bool[n];var solid=new bool[n];
            for(int k=exit;k<n;k++)
            {
                var hit=__0[k];distance[k]=hit.distance;
                if((hit.flags&IntersectionFlags.Enter)==0)continue;
                enter[k]=true;
                solid[k]=hit.objectIndex>=0&&hit.objectIndex<sim.objects.Length&&sim.objects[hit.objectIndex].materialIndex>=0;
            }
            GapLedger.Record(sim.Pointer,layerFragment,__0[exit].distance,SpallGapModel.NextSolidGapMm(distance,enter,solid,exit));
        }
        catch(Exception ex){if(ricochetWarnings++<3)Plugin.ModLog.LogWarning("[Spall] Gap lookahead unavailable: "+ex.Message);}
    }
    // The gap in front of a burst that is about to spawn: null when this fragment's blocks were not recorded.
    private static double? BurstGapMm(PenetrationSimulation simulation,CompoundStructure.SpallSpawnBurst o,out bool byDistance)
    {
        byDistance=false;
        if(o.parentIndex<0||o.parentIndex>=simulation.FragmentCount)return null;
        var parent=simulation.fragments[o.parentIndex];
        double? spawn=null;
        var direction=parent.direction;
        if(direction.sqrMagnitude>.5f){var d=UnityEngine.Vector3.Dot(o.spawnPoint-parent.spawnPoint,direction.normalized);if(float.IsFinite(d))spawn=d;}
        return GapLedger.Match(simulation.Pointer,o.parentIndex,spawn,out byDistance);
    }
    // Native Penetrate reserves profile zero for the original core (source=-1, AP).
    // Its default AP exponent1.33 is wrong for fast sub-calibre and chemical kinds.
    // Change only that profile during an identified custom original call, then
    // restore it before the native outer loop evaluates any following original or spall.
    private static bool BeginObliquity(PenetrationSimulation sim,short index,out float original)
    {
        original=0;
        var context=Impact;
        if(context?.Profile is not {} profile)return false;
        if(context.Behavior=="heat"&&(!context.ChemicalInitialized||!double.IsFinite(context.ChemicalBudget)||context.ChemicalBudget<=0))return false;
        if(index<0||index>=sim.FragmentCount||index>=sim.fragments.Length||sim.projectileProfiles.Length==0)return false;
        var fragment=sim.fragments[index];
        if((fragment.flags&FragmentFlag.OriginalPenetrator)==0||fragment.projectileProfileIndex!=0)return false;
        var projectile=sim.projectileProfiles[0];
        if(projectile.sourceMaterialIndex!=-1||(byte)projectile.flags!=1)return false; // Native AP=1; avoid duplicate interop enum types.
        if(!float.IsFinite(projectile.obliquityExponent)||projectile.obliquityExponent<=0)return false;
        if(!ShellObliquity.Applies(profile))return false;
        var target=SpacedKineticModel.KineticObliquityExponent(profile,Settings,context.CapStripped.Contains(sim.Pointer));
        if(!double.IsFinite(target)||target<0||target>2||Math.Abs(target-projectile.obliquityExponent)<=1e-6)return false;
        original=projectile.obliquityExponent;
        projectile.obliquityExponent=(float)target;
        sim.projectileProfiles[0]=projectile; // Il2Cpp value-array requires explicit writeback and read-back.
        if(sim.projectileProfiles[0].obliquityExponent!=(float)target)
        {
            RestoreObliquity(sim,original);
            if(obliquityWarnings++<4)Plugin.ModLog.LogWarning("[Spall] ProjectileProfile exponent write-back mismatch.");
            return false;
        }
        return true;
    }
    private static int obliquityWarnings;
    private static void RestoreObliquity(PenetrationSimulation sim,float original)
    {
        if(sim.projectileProfiles.Length==0)return;
        var profile=sim.projectileProfiles[0];
        if(profile.sourceMaterialIndex!=-1||(byte)profile.flags!=1)return;
        profile.obliquityExponent=original;
        sim.projectileProfiles[0]=profile;
        if(sim.projectileProfiles[0].obliquityExponent!=original&&obliquityWarnings++<4)
            Plugin.ModLog.LogWarning("[Spall] ProjectileProfile exponent restore read-back mismatch.");
    }
    [HarmonyPostfix,HarmonyPatch(typeof(StructureIntersection),nameof(StructureIntersection.GetNextMaterialBlock))]
    private static void MaterialLayer(MaterialBlock __result,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<StructureIntersection> __0,int __1,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<MaterialInfo> __5)
    {
        var sim=layerSimulation;var context=Impact;
        if(sim==null || context?.Profile is not {} profile || !__result.Valid || layerFragment<0 || layerFragment>=sim.FragmentCount)return;
        var fragment=sim.fragments[layerFragment];
        if((fragment.flags & FragmentFlag.OriginalPenetrator)==0)return;
        var entry=__result.EnterIntersectionIndex;var exit=__result.ExitIntersectionIndex;
        if(entry<0||exit<0||entry>=__1||exit>=__1||entry>=__0.Length||exit>=__0.Length)return;
        var length=Math.Max(0,(__0[exit].distance-__0[entry].distance)*1000);
        var solid=__result.MaterialIndex>=0;
        if(solid){if(__result.MaterialIndex>=__5.Length)return;length*=Math.Max(0,__5[__result.MaterialIndex].rhaFactor);}
        var chemical=SpallBalance.IsChemicalLayerBehavior(context.Behavior);
        if(chemical&&ReleaseDiagnostics.Enabled&&solid&&layerStructure is {} structure&&chemicalDiagnosticBudget>0)
        {
            try
            {
            var first=__0[entry];
            var normal=structure.GetIntersectionNormal(ref first).normalized;
            var cosine=Math.Clamp(Math.Abs(UnityEngine.Vector3.Dot(fragment.direction.normalized,normal)),0,1);
            var losMm=Math.Max(0,(__0[exit].distance-__0[entry].distance)*1000);
            var diagnosticConstant=sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant;
            var inputMm=fragment.GetBasePenetration(diagnosticConstant)*1000;
            chemicalDiagnosticBudget--;
            Plugin.ModLog.LogInfo($"[Chemical plate] profile={profile.Id} live={context.LiveImpact} angleFromNormal={Math.Acos(cosine)*180/Math.PI:0.0}deg normalThickness={losMm*cosine:0.0}mm pathThickness={losMm:0.0}mm pathRHA={length:0.0}mm incomingBaseBudget={inputMm:0.0}mm; native armour consumption follows (session trace capped80).");
            }
            catch(Exception ex){chemicalDiagnosticBudget=0;Plugin.ModLog.LogWarning("[Chemical plate] Optional trace disabled: "+ex.Message);}
        }
        var diagnosticKey=new ChemicalDiagnosticKey(sim.Pointer,layerFragment,entry,exit);
        if(chemical&&context.LiveImpact&&solid&&context.ChemicalDiagnostics.Geometry(diagnosticKey))
        ChemicalDiagnosticLedger.Observe(()=>
        {
            var first=__0[entry];var last=__0[exit];var passive=__5[__result.MaterialIndex];
            var grouped=first.objectIndex!=last.objectIndex||__result.IntersectedObjectIndices?.Length>1;
            double? incoming=fragment.projectileProfileIndex<sim.projectileProfiles.Length?fragment.GetBasePenetration(sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant)*1000:null;
            var entryId=first.objectIndex>=0&&first.objectIndex<sim.objects.Length?sim.objects[first.objectIndex].ID:-1;
            var exitId=last.objectIndex>=0&&last.objectIndex<sim.objects.Length?sim.objects[last.objectIndex].ID:-1;
            double? nativeAngle=null;
            try
            {
                if(layerStructure is {} nativeStructure)
                {
                    var nativeNormal=nativeStructure.GetIntersectionNormal(ref first).normalized;
                    var axis=fragment.direction.normalized;
                    var dot=UnityEngine.Vector3.Dot(axis,nativeNormal);
                    if(float.IsFinite(dot)&&axis.sqrMagnitude>.5f&&nativeNormal.sqrMagnitude>.5f)
                        nativeAngle=Math.Acos(Math.Clamp(Math.Abs(dot),0,1))*180/Math.PI;
                }
            }
            catch(Exception){/* Entry/exit diagnostics remain useful when the optional normal is unavailable. */}
            RuntimeDamageFeed.RecordArmourInteraction(sim,layerFragment,"chemicalGeometry",
                ChemicalDiagnosticLedger.GeometryText($"{sim.Pointer}:{layerFragment}:{entry}:{exit}",__result.MaterialIndex,
                    entryId,first.flags.ToString(),exitId,last.flags.ToString(),first.distance*1000,last.distance*1000,
                    passive.rhaFactor,passive.density,length,nativeAngle,grouped),incoming,hitId:ChemicalDiagnosticLedger.HitKey(diagnosticKey));
        });
        if(!context.Layers.TryGetValue(sim.Pointer,out var layers))context.Layers[sim.Pointer]=layers=new();
        var transition=layers.ObserveBlock(solid,length);
        if(transition is not {} gap)return;
        var constant=sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant;
        var remaining=fragment.GetBasePenetration(constant)*1000;
        if(!float.IsFinite(remaining)||remaining<=0)return;
        var retention=1d;string? spacedReport=null;
        if(chemical)
        {
            retention=ShellBalance.SpacedRetention(profile,context.GunDiameter*1000,gap.PlateRhaMm,gap.GapMm);
        }
        else
        {
            var spaced=Settings.SpacedKinetic??SpacedKineticSettings.Default;
            var calibre=context.GunDiameter*1000;
            var effect=SpacedKineticModel.Evaluate(spaced,profile.Behavior,profile.EffectiveKineticKind,calibre,gap.PlateRhaMm,gap.GapMm);
            // A seam below the minimum scale has no effect and must not consume
            // the once-per-shot gate; TryClaim refuses such an effect.
            if(!SpacedKineticModel.TryClaim(context.SpacedKineticShots,sim.Pointer,effect))return;
            var kindText=profile.Behavior=="aphe"?"APHE":profile.Behavior=="apfsds"?"APFSDS":profile.EffectiveKineticKind switch
            {"apc"=>"APC","apcbc"=>"APCBC","apcr"=>"APCR","apds"=>"APDS",_=>"AP"};
            if(effect.CapStrip)
            {
                var applied=false;
                if(layerFragment<sim.FragmentCount&&sim.projectileProfiles.Length>0)
                {
                    var kineticFragment=sim.fragments[layerFragment];var index=kineticFragment.projectileProfileIndex;
                    var projectile=sim.projectileProfiles[0];
                    if(index==0&&projectile.sourceMaterialIndex==-1&&(byte)projectile.flags==1)
                    {
                        var armedHere=obliquityArmedSimulation is {} armed&&armed.Pointer==sim.Pointer;
                        var apExponent=(float)Settings.ApObliquityExponent;
                        var alreadyAp=float.IsFinite(projectile.obliquityExponent)&&Math.Abs(projectile.obliquityExponent-apExponent)<=1e-6;
                        if(armedHere||alreadyAp)
                        {
                            if(!alreadyAp){projectile.obliquityExponent=apExponent;sim.projectileProfiles[0]=projectile;}
                        if(sim.projectileProfiles[0].obliquityExponent!=apExponent)return;
                            context.CapStripped.Add(sim.Pointer);
                            applied=true;
                        }
                    }
                }
                if(applied)spacedReport=$"Spaced plate: cap stripped ({kindText} \u2192 AP)";
                else
                {
                    var scale=SpacedKineticModel.Scale(calibre,gap.PlateRhaMm,gap.GapMm);
                    retention=SpacedKineticModel.Flat(spaced,profile.EffectiveKineticKind,scale);
                    spacedReport=$"Spaced plate: \u2212{(1-retention)*100:0.#} % (cap fallback)";
                }
            }
            else
            {
                retention=effect.Retention;
                spacedReport=$"Spaced plate: \u2212{(1-retention)*100:0.#} % (yaw)";
            }
        }
        var budget=remaining*retention;
        var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(fragment.diameter*1000,fragment.mass,(float)budget,constant);
        if(!float.IsFinite(speed)||speed<=0)return;
        fragment.speed=Math.Min(fragment.speed,speed);
        sim.fragments[layerFragment]=fragment;
        RuntimeArmourResponses.NotifyParentSpeedChange(sim,layerFragment);
        if(spacedReport!=null&&(context.LiveImpact||RuntimeArmourComparisonRunner.IsProbing))
            RuntimeDamageFeed.RecordArmourInteraction(sim,layerFragment,"materialReaction",spacedReport,remaining,budget);
        if(chemical&&context.LiveImpact&&context.ChemicalDiagnostics.Gap(diagnosticKey))
        ChemicalDiagnosticLedger.Observe(()=>
        {
            var actualAfter=fragment.GetBasePenetration(constant)*1000;
            RuntimeDamageFeed.RecordArmourInteraction(sim,layerFragment,"chemicalGap",
                $"Observed solid-air-solid disruption: previous solid path-RHA proxy {gap.PlateRhaMm:0.###} mm; air gap {gap.GapMm:0.###} mm; retention {retention:0.######}. Separate from native passive resistance and ERA; not added again to the block total.",remaining,actualAfter,hitId:ChemicalDiagnosticLedger.HitKey(diagnosticKey));
        });
        if(ReleaseDiagnostics.Enabled)
        {
            if(chemical)Plugin.ModLog.LogInfo($"[Chemical layers] {context.Behavior} plate={gap.PlateRhaMm:0.0}mm RHA gap={gap.GapMm:0.0}mm retention={retention:0.000}: {remaining:0.0} -> {budget:0.0}mm");
            else if(spacedReport!=null)Plugin.ModLog.LogInfo($"[Spaced kinetic] {context.Behavior} plate={gap.PlateRhaMm:0.0}mm RHA gap={gap.GapMm:0.0}mm report=\"{spacedReport}\": {remaining:0.0} -> {budget:0.0}mm");
        }
    }
    internal static void Configure()
    {
        var path=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.spall.json");
        if(!File.Exists(path)) File.WriteAllText(path,SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=FragmentRicochetSettings.Default}));
        else
        {
            // 0.22.17/0.22.22: add only the keys that are missing (fragmentRicochet, minSpallGapMm, thinGapKillMm ...), after one backup of the user's file.
            var text=File.ReadAllText(path);var merged=FragmentRicochetModel.MergeMissing(text);
            if(merged!=text)
            {
                var backup=path+".bak-0.22.25";
                if(!File.Exists(backup))File.WriteAllText(backup,text);
                File.WriteAllText(path,merged);
                Plugin.ModLog.LogInfo("[Spall] Added the missing spall settings; backup "+backup);
            }
        }
        Settings=SpallBalance.Parse(File.ReadAllText(path));
        Plugin.ModLog.LogInfo("[Spall] APFSDS remaining-penetration/cone and APHE amplified native AP spall enabled. "+path);
    }
    private sealed record BurstState(int CountBefore,bool Custom,bool Aphe,UnityEngine.Vector3 ExplosionPosition=default,UnityEngine.Vector3? PreviousAxis=null,short ParentIndex=-1);
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.Penetrate))]
    [HarmonyPriority(Priority.Last)]
    private static void ExperimentalNormalization(ref PenetratorInfo penetrator)
    {
        ResetShotState();
        var context=Impact;
        if(context is {LiveImpact:true,ChemicalInitialized:false} && context.Behavior is "heat" or "hesh")
        {
            var p=context.Profile!;
            var diameter=context.Behavior=="heat" ? context.GunDiameter*.05f : penetrator.Diameter;
            var mass=context.Behavior=="heat" ? penetrator.Mass*.05f : penetrator.Mass;
            var budget=context.ChemicalBudget;
            var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(diameter*1000,mass,(float)budget,penetrator.PenetratorConstant);
            if(!float.IsFinite(speed)||speed<=0)return;
            penetrator=new PenetratorInfo(mass,diameter,penetrator.Density,0,penetrator.PenetratorConstant,penetrator.Position,penetrator.Velocity.normalized*speed);
            context.ChemicalInitialized=true;
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Chemical] profile={p.Id} firingEra={context.FiringEra} equivalent penetrator {budget:0}mm RHA; impact-speed-independent proxy");
            return;
        }
        if(context?.Behavior is "heat" or "hesh")
        {
            penetrator=new PenetratorInfo(penetrator.Mass,penetrator.Diameter,penetrator.Density,0,penetrator.PenetratorConstant,penetrator.Position,penetrator.Velocity);
            return;
        }
        // APCR/HVAP flies as its light full-bore carrier. At the plate the native
        // penetrator becomes the dense sub-calibre core, with speed and direction
        // unchanged; native drag already acted on the carrier during flight.
        if(context?.Profile is {Behavior:"ap",Settings:{HasCarrier:true}} carrier&&context.GunDiameter>0)
        {
            var core=ShellBallistics.CoreGeometry(context.GunDiameter*1000,carrier.Settings);
            var carrierDiameter=penetrator.Diameter;var carrierMass=penetrator.Mass;
            penetrator=new PenetratorInfo((float)core.Mass,(float)core.Diameter,(float)carrier.Settings.Density,
                penetrator.MaxNormalizationAngle,penetrator.PenetratorConstant,penetrator.Position,penetrator.Velocity);
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Carrier] profile={carrier.Id} carrier {carrierDiameter*1000:0.0}mm/{carrierMass:0.000}kg -> core {core.Diameter*1000:0.0}mm/{core.Mass:0.000}kg; speed and direction retained");
            return;
        }
        penetrator=ApfsdsInput(penetrator,context,Settings.ApfsdsDisableClassicNormalization);
    }
    // Native AP constructs density from a shared native default, even for a custom
    // definition with explicitly calculated rod mass/geometry. Keep both adapters
    // on the actual selected rod material without changing its energy or RHA budget.
    internal static PenetratorInfo ApfsdsInput(PenetratorInfo input,ShellImpactContext? context,bool disableNormalization)
    {
        if(context?.Behavior!="apfsds")return input;
        var density=input.Density;
        if(context.Profile is {} profile&&double.IsFinite(profile.Settings.Density)&&profile.Settings.Density>0&&
            float.IsFinite((float)profile.Settings.Density))density=(float)profile.Settings.Density;
        return new PenetratorInfo(input.Mass,input.Diameter,density,disableNormalization?0:input.MaxNormalizationAngle,
            input.PenetratorConstant,input.Position,input.Velocity);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.CreateSpallBurst))]
    private static bool Burst(PenetrationSimulation simulation,ref CompoundStructure.SpallSpawnBurst o,out BurstState __state)
    {
        __state=new(simulation.FragmentCount,false,false,PreviousAxis:apheAxis);
        try
        {
            var context=Impact;
            if(context==null || context.Behavior is not ("apfsds" or "aphe" or "heat" or "hesh")) return true;
            if(o.parentIndex<0 || o.parentIndex>=simulation.FragmentCount) return true;
            var parent=simulation.fragments[o.parentIndex];
            // 0.22.22: no HEAT/HESH/APFSDS fragments from a plate exit that is followed by solid armour within minSpallGapMm (native bursts included).
            if(SpallGapModel.Applies(context.Behavior)&&Settings.MinSpallGapMm>0&&BurstGapMm(simulation,o,out var byDistance) is {} gap&&SpallGapModel.SuppressBurst(Settings.MinSpallGapMm,gap))
            {
                if(ReleaseDiagnostics.Enabled&&!probeGapSuppress){probeGapSuppress=true;Plugin.ModLog.LogInfo($"[Spall probe] {context.Behavior} burst suppressed: next solid armour {gap:0.0} mm ahead < {Settings.MinSpallGapMm:0.#} mm (gap matched {(byDistance?"by exit distance":"by latest block")}; parent {o.parentIndex}).");}
                return false;
            }
            if(context.Behavior is "aphe" or "heat" or "hesh" && ShellBalance.SuppressPayloadBurst(context.Behavior,
                (parent.flags & FragmentFlag.OriginalPenetrator)!=0,context.PayloadBursts.Contains(simulation.Pointer)))return false;
            if((parent.flags & FragmentFlag.OriginalPenetrator)==0 || parent.materialIndex<0) return true;
            var s=Settings;
            var aphe=context.Behavior is "aphe" or "heat" or "hesh";
            if(aphe)
            {
                // Keep native AP origin, material occupancy and fragment profile.
                // The same native loop evaluates both the original projectile and its fragments.
                var calibre=context.GunDiameter*1000;
                if(!float.IsFinite(o.spallFactor)||o.spallFactor<=0)return true;
                var tuning=context.Behavior=="aphe" ? s.ApheSpallMultiplier/4 : context.Profile!.SpallMultiplier/(context.Behavior=="hesh"?8:1.5);
                o.volume=(float)(ShellBalance.FragmentVolume(context.Behavior,calibre)*tuning);
                var count=Math.Clamp(ShellBalance.FragmentTarget(context.Behavior,calibre)*tuning,4,32);
                o.crossectionalArea=(float)(o.spallFactor*count);
                o.speed=(float)ShellBalance.PayloadFragmentSpeed(context.Behavior,calibre,o.speed);
                if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Payload burst] {context.Behavior} targetCount={count:0} fragmentSpeed={o.speed:0}m/s volume={o.volume:0.000000}m3");
                apheAxis=context.Behavior=="heat" ? o.direction.normalized : o.planeNormal.sqrMagnitude>.5f?o.planeNormal.normalized:o.direction.normalized;
                __state=__state with {Custom=true,Aphe=true,ParentIndex=o.parentIndex,
                    ExplosionPosition=context.LiveImpact?simulation.SimToWorldSpacePoint(o.spawnPoint):default};
            }
            else
            {
                var original=simulation.Penetrator;
                var initialPen=PenetrationUtils.ComputePenetration(original.Diameter*1000,original.Mass,original.Velocity.magnitude,original.PenetratorConstant);
                var remainingPen=parent.GetBasePenetration(simulation.projectileProfiles[parent.projectileProfileIndex].penetratorConstant)*1000;
                if(!float.IsFinite(initialPen) || initialPen<=0 || !float.IsFinite(remainingPen)) return true;
                var remainingFraction=Math.Clamp(remainingPen/initialPen,0,1);
                var ratio=SpallBalance.RemainingPenetrationRatio(remainingFraction,s);
                o.volume=(float)(SpallBalance.ReferenceApVolume(context.GunDiameter,context.GunDiameter)*ratio);
                o.crossectionalArea=(float)(Math.PI*context.GunDiameter*context.GunDiameter*.25*ratio);
                var energyFraction=original.Mass>0 && original.Velocity.sqrMagnitude>0
                    ? parent.mass*parent.speed*parent.speed/(original.Mass*original.Velocity.sqrMagnitude):0;
                var cone=SpallBalance.ConeFactor(energyFraction,s);
                o.spread*=(float)(cone*ShellBalance.RodCone(context.Profile!.Settings));
                __state=__state with {Custom=true};
                if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Spall] APFSDS remaining={remainingPen:0.0}/{initialPen:0.0}mm fraction={remainingFraction:0.000} massAndCountRatioToAP={ratio:0.000} coneFactor={cone:0.000}");
            }
        }
        catch(Exception ex){Plugin.ModLog.LogError("[Spall] Burst tuning failed: "+ex);}
        return true;
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.CreateSpallBurst))]
    private static void EndBurst(BurstState __state) => apheAxis=__state.PreviousAxis;
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.GetFragmentDirection))]
    private static bool ConeDirection(ref UnityEngine.Vector3 __result)
    {
        if(apheAxis is not {} axis)return true;
        var helper=Math.Abs(axis.y)<.9f?UnityEngine.Vector3.up:UnityEngine.Vector3.right;
        var tangent=UnityEngine.Vector3.Cross(helper,axis).normalized;
        var bitangent=UnityEngine.Vector3.Cross(axis,tangent);
        var angle=Impact?.Behavior=="aphe" ? Settings.ApheConeHalfAngleDegrees : Impact?.Profile?.ConeHalfAngleDegrees ?? Settings.ApheConeHalfAngleDegrees;
        var sample=SpallBalance.Cone(System.Random.Shared.NextDouble(),System.Random.Shared.NextDouble(),angle);
        __result=axis*sample.Z+tangent*sample.X+bitangent*sample.Y;
        return false;
    }
    [HarmonyPostfix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.CreateSpallBurst))]
    private static void BurstDone(PenetrationSimulation simulation,BurstState __state)
    {
        if(!__state.Custom)return;
        var count=simulation.FragmentCount-__state.CountBefore;
        if(count>0 && __state.Aphe && Impact is {} impact) impact.PayloadBursts.Add(simulation.Pointer);
        if(count>0 && Impact?.Behavior is "aphe" && __state.ParentIndex>=0)
        {
            var parent=simulation.fragments[__state.ParentIndex];
            parent.flags |= FragmentFlag.Killed;
            simulation.fragments[__state.ParentIndex]=parent;
        }
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Spall] {Impact?.Behavior} spawned={count}; calibre={(Impact?.GunDiameter??0)*1000:0}mm");
        if(__state.Aphe && count>0 && Impact is {LiveImpact:true,ExplosionPending:false} context)
        {
            context.ExplosionPosition=__state.ExplosionPosition;
            context.ExplosionPending=true;
        }
    }
}
