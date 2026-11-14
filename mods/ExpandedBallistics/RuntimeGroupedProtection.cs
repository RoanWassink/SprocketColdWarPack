using BepInEx.Configuration;
using System.Runtime.CompilerServices;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace SprocketExpandedBallistics;
internal static class RuntimeGroupedProtection
{
    internal static void Configure(ConfigFile config)
    {
        var mode=config.Bind("Protection","GroupedBlockMode",GroupedBlockMode.PerInterval,"PerInterval charges confirmed own material intervals once per penetrator line. Legacy retains 0.22.46 grouped-block behaviour.");
        var scale=config.Bind("Protection","CompositeProtectionScale",1d,"Global non-negative factor multiplier for cassette/composer materials only. 1 preserves the authored calibration.");
        void Refresh(){GroupedProtection.Mode=mode.Value;GroupedProtection.CompositeProtectionScale=scale.Value;}
        mode.SettingChanged+=(_,_)=>Refresh();scale.SettingChanged+=(_,_)=>Refresh();Refresh();
        // 0.22.71: the line per hit on a plate in layers is a troubleshooting aid; it was one line per hit in the log whenever detailed logging was on.
        var hitLog=config.Bind("Composite fill","Layer hit log",false,"Writes one line per hit on a plate in layers. For troubleshooting.");
        hitLog.SettingChanged+=(_,_)=>LayerHitLog=hitLog.Value;LayerHitLog=hitLog.Value;
    }
    /// <summary>The setting Composite fill / Layer hit log (default off).</summary>
    internal static bool LayerHitLog{get;private set;}
}

// 0.22.60 "lagen als vlakken": the hit per layer. NativeMaterialReactions.cs, NativeMaterialIntervals.cs and MaterialIntervalLedger.cs stay byte-exact
// (the existing suite pins them, including the body of Reached). ShellFeature.Load wires the ReachedBlock delegate to ReachedWithLayers when the mod
// loads (no window or frame involved): the same guards as Reached; a block on a shot line that crosses a usable
// cassette plate in layers runs this copy of ReachedIntervals, every other block goes to the unchanged Reached. No new hook. It is the same route
// with one difference: the pieces of a plate in layers are paired per face (first in, first out), each looks up its entry face in the per-mesh
// table, and pays the factor of its own layer face (LayerHit) instead of the recipe table. Every other member of such a block is paid exactly as
// in ReachedIntervals; a block without a plate in layers never comes here. The write-back (WriteFactor) is unchanged.
internal static partial class NativeMaterialReactions
{
    private sealed class LayerShot{internal readonly Dictionary<int,(PlateStructure Plate,CassetteModel Model,LayerHitIndex? Index,LayerLayoutKind Kind)?> Plates=new();internal readonly HashSet<int> Marked=new();internal int Fallbacks;}
    private static readonly ConditionalWeakTable<ShellImpactContext,LayerShot> layerShots=new();
    private static readonly ConditionalWeakTable<IntervalRay,Dictionary<int,List<LayerChord>>> layerChords=new();
    /// <summary>The plate in layers behind an entity, once per shot; null for everything else (ordinary plates, other components).</summary>
    private static (PlateStructure Plate,CassetteModel Model,LayerHitIndex? Index,LayerLayoutKind Kind)? LayeredPlate(NativeArmourBlockContext reached,LayerShot shot,int entity)
    {
        if(shot.Plates.TryGetValue(entity,out var known))return known;
        (PlateStructure,CassetteModel,LayerHitIndex?,LayerLayoutKind)? result=null;
        if(RuntimeArmourResponses.TryResolvePlate(reached.Impact,reached.Sim,entity,out var component,out _,out _)&&component.TryCast<PlateStructure>() is {} plate&&
            CompositePlate.IsCassette(plate.armourTechID)&&reached.Impact.ArmourSnapshot?.Cassettes?.TryGetValue(plate.armourTechID,out var model)==true&&model!=null)
        {
            // 0.22.69 uniform: only "is this a plate in layers, and which layout" is asked (the name of its mesh); the per-mesh table of faces and ranks is not built or read.
            if(LayerHit.Model==LayerHit.HitModel.Uniform){if(RuntimeLayerFaces.TryKind(plate,out var kind))result=(plate,model,null,kind);}
            else if(RuntimeLayerFaces.TryTable(plate,model,out var index))result=(plate,model,index,index.Kind);
        }
        if(shot.Plates.Count<1024)shot.Plates[entity]=result;
        return result;
    }
    /// <summary>
    /// The armour-response delegate (ShellFeature.Load wires RuntimeArmourResponses.ReachedBlock to it; Unload and the load failure set it to null).
    /// The guards of Reached; a per-interval block that crosses a plate in layers is paid here, everything else goes to Reached unchanged.
    /// </summary>
    internal static void ReachedWithLayers(NativeArmourBlockContext reached)
    {
        var sim=reached.Sim;var block=reached.Block;var hits=reached.Intersections;
        if(GroupedProtection.Mode==GroupedBlockMode.PerInterval&&block.Valid&&reached.Index>=0&&reached.Index<sim.FragmentCount)
        {
            int entry=block.EnterIntersectionIndex,exit=block.ExitIntersectionIndex;
            if(entry>=0&&exit>=0&&entry<reached.IntersectionCount&&exit<reached.IntersectionCount&&entry<hits.Length&&exit<hits.Length)
            {
                var first=hits[entry];var last=hits[exit];
                if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length&&ReachedLayered(reached,first,last))return;
            }
        }
        Reached(reached);
    }
    /// <summary>True when this block was handled here (the shot line crosses a plate in layers); false: the caller runs ReachedIntervals unchanged.</summary>
    private static bool ReachedLayered(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last)
    {
        var sim=reached.Sim;
        if(reached.Impact==null||reached.Impact.ArmourSnapshot?.Cassettes is not {Count:>0})return false;
        var shot=layerShots.GetOrCreateValue(reached.Impact);bool any=false;
        for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length&&!any;i++)
        {
            var o=reached.Intersections[i].objectIndex;
            if(o>=0&&o<sim.objects.Length&&LayeredPlate(reached,shot,sim.objects[o].ID)!=null)any=true;
        }
        if(!any)return false;
        ReachedIntervalsLayered(reached,first,last,shot);
        return true;
    }
    // A copy of ReachedIntervals (NativeMaterialIntervals.cs, 0.22.51-0.22.59); the only addition is the branch marked 0.22.60.
    private static void ReachedIntervalsLayered(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last,LayerShot shot)
    {
        RuntimeDamageFeed.LayerStarted(reached);
        var sim=reached.Sim;var block=reached.Block;
        if(block.MaterialIndex>=sim.materials.Length)return;
        bool solid=block.MaterialIndex>=0;
        var fragment=sim.fragments[reached.Index];
        if((fragment.flags&(FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator)return;
        if(ProtectionFactorTable.ThreatOf(Threats(reached.Impact,true)) is not {} threat)return;
        if(fragment.projectileProfileIndex<0||fragment.projectileProfileIndex>=sim.projectileProfiles.Length)return;
        double nativeLength=last.distance-first.distance;
        if(!double.IsFinite(nativeLength)||nativeLength<=0)return; // boundary contact: no charge, no ledger claim
        var state=impacts.GetOrCreateValue(reached.Impact);var key=(sim.Pointer,IntervalLineageRoot(sim,reached.Index));
        if(!state.Rays.TryGetValue(key,out var rays))state.Rays[key]=rays=new();
        var direction=fragment.direction.normalized;
        var ray=rays.LastOrDefault(r=>r.Contains(fragment.spawnPoint,direction));
        if(ray==null){ray=new(fragment.spawnPoint,direction);rays.Add(ray);}
        double start=ray.Distance(fragment.spawnPoint,direction,first.distance),end=ray.Distance(fragment.spawnPoint,direction,last.distance);
        var ids=new HashSet<int>{first.objectIndex,last.objectIndex};
        if(block.IntersectedObjectIndices is {} grouped)foreach(var id in grouped)ids.Add(id);
        for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
        {
            var h=reached.Intersections[i];ids.Add(h.objectIndex);
        }
        var candidates=new List<MaterialInterval>();
        foreach(var id in ids.OrderBy(i=>i))
        {
            if(id<0||id>=sim.objects.Length)continue;
            var obj=sim.objects[id];int entity=obj.ID;string material="native:"+obj.materialIndex,label=OtherLabel(sim,entity);
            // Cache all confirmed own chords from this ray trace, including later material that a child may start inside.
            var boundaries=new List<MaterialBoundary>();
            for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
            {
                var hit=reached.Intersections[i];if(hit.objectIndex!=id)continue;
                boundaries.Add(new(ray.Distance(fragment.spawnPoint,direction,hit.distance),(hit.flags&IntersectionFlags.Enter)!=0,(hit.flags&IntersectionFlags.Exit)!=0,AngleOf(reached,hit)));
            }
            if(!ray.Geometry.TryGetValue(entity,out var known))ray.Geometry[entity]=known=new();
            foreach(var own in ConfirmedMaterialIntervals.Pair(boundaries))
                if(!known.Any(k=>Math.Abs(k.Start-own.Start)<1e-6&&Math.Abs(k.End-own.End)<1e-6))known.Add(own);
            // 0.22.60: a plate in layers keeps its own pieces (paired per face) and is paid per layer face.
            var layered=solid?LayeredPlate(reached,shot,entity):null;
            List<LayerChord>? chords=null;
            // 0.22.69 uniform: the pieces are the confirmed solid chords of the plate on the line, whatever face they enter; no face, rank or side wall is looked up.
            // Per layer (0.22.61): a side wall pays only where no face of the plate claims the line.
            bool uniform=LayerHit.Model==LayerHit.HitModel.Uniform;
            if(layered!=null)chords=uniform?LayeredChords(reached,ray,fragment.spawnPoint,direction,id,entity,layered.Value.Index,layered.Value.Plate,false):
                LayerHit.ClipSideWalls(LayeredChords(reached,ray,fragment.spawnPoint,direction,id,entity,layered.Value.Index!,layered.Value.Plate,true));
            if(!solid||!(chords!=null?chords.Any(k=>k.EndM>start&&k.StartM<end):known.Any(k=>k.End>start&&k.Start<end)))continue;
            ProtectionFactorTable? table=null;
            if(RuntimeArmourResponses.TryResolvePlate(reached.Impact,sim,entity,out var component,out var spawn,out _)&&component.TryCast<PlateStructure>() is {} plate)
            {
                material=plate.armourTechID;
                if(!Usable(reached,plate,spawn,entity,material,reached.Impact.ArmourSnapshot?.Protection,out table))table=null;
                label=table?.Label??material;
            }
            else if(RuntimeArmourResponses.ResolveCompositeModule(reached.Impact,sim,entity) is {} model)
            {
                var design=model.Vehicle?.DesignInfo;if(design==null)continue;var date=design.Date;
                if(!VehicleEraAvailability.TryCalendarDate(date.Year,date.Month,date.Day,out var calendar)||!CompositeModule.Current.Allows(calendar))continue;
                material=CompositeModule.MaterialId;label=CompositeModule.DisplayName;
                reached.Impact.ArmourSnapshot?.Protection?.TryGetValue(material,out table);
            }
            // 0.22.60: the pieces of a usable plate in layers, each with the factor of its own face (a piece without face or rank: the recipe table).
            if(table!=null&&layered!=null&&chords!=null)
            {
                var recipe=RuntimeLayerFaces.Recipe(layered.Value.Model,layered.Value.Kind);
                if(uniform)
                {
                    // 0.22.69: one factor per solid millimetre (table factor at the angle of the piece x recipe depth / solid depth). One marker for the report, which shows the plate as one line.
                    var even=new double[chords.Count];LayerHit.UniformFactors(recipe,table,threat,chords,even);
                    for(int i=0;i<chords.Count;i++)candidates.Add(new(entity,material,table.Label,chords[i].StartM,chords[i].EndM,even[i],chords[i].Angle,null));
                    if(shot.Marked.Add(entity))
                    {
                        // what the game knows of the whole line now: the solid material of the plate (where chords overlap, once) and its extent; the report needs them when the shot stops inside the plate
                        var ordered=chords.Where(c=>c.EndM>c.StartM).OrderBy(c=>c.StartM).ToList();double solidMm=0,reachM=double.NegativeInfinity;
                        foreach(var c in ordered){var from=Math.Max(c.StartM,reachM);if(c.EndM>from)solidMm+=(c.EndM-from)*1000;reachM=Math.Max(reachM,c.EndM);}
                        double extentMm=ordered.Count>0?(ordered.Max(c=>c.EndM)-ordered[0].StartM)*1000:0;
                        Report(reached,entity,"layeredPlate",string.Create(System.Globalization.CultureInfo.InvariantCulture,$"{table.Label}: plate in layers, {recipe.Layout.Groups.Length} layers; {solidMm:0.###} mm solid in {extentMm:0.###} mm."),entity);
                    }
                    if(RuntimeGroupedProtection.LayerHitLog)Plugin.ModLog.LogInfo(LayerHitLogText.Uniform(entity,chords,even)); // 0.22.71: the text is only built when the setting is on
                    continue;
                }
                var factors=new double[chords.Count];var thickness=new double[chords.Count];
                var fallbacks=LayerHit.Factors(recipe,table,threat,chords,factors,thickness);
                for(int i=0;i<chords.Count;i++)
                    candidates.Add(new(entity,material,LayerHit.Label(table.Label,recipe,chords[i],thickness[i]),chords[i].StartM,chords[i].EndM,factors[i],chords[i].Angle,null));
                if(fallbacks>0)
                {
                    shot.Fallbacks+=fallbacks;
                    Report(reached,entity,"materialReaction",$"{table.Label}: {fallbacks} of {chords.Count} pieces of this plate in layers found no layer face and use the recipe table, as an ordinary composite plate ({shot.Fallbacks} so far in this shot).");
                }
                if(RuntimeGroupedProtection.LayerHitLog)Plugin.ModLog.LogInfo(LayerHitLogText.PerLayer(entity,chords,factors,fallbacks));
                continue;
            }
            // 0.22.51: a simple member is paid at its own native rhaFactor from before any EB write in this shot (the 0.22.49 lesson:
            // never read back a value EB itself wrote). Without an active write this is the live value, bit-exact as before.
            float? nativeRha=table==null&&obj.materialIndex>=0&&obj.materialIndex<sim.materials.Length?OriginalMaterial(sim,obj.materialIndex).RhaFactor:null;
            foreach(var interval in known)
            {
                double factor;
                if(table!=null)factor=table.Factor(threat,interval.Angle);
                else
                {
                    if(nativeRha is not {} own)continue;
                    var profile=sim.projectileProfiles[fragment.projectileProfileIndex];var angle=AngleOf(reached,first);
                    double om=profile.obliquityExponent==1?0:ProtectionFactorTable.Overmatch(fragment.mass,fragment.speed,fragment.diameter,nativeLength,angle);
                    double multiplier=ProtectionFactorTable.NativeRhaFactor(1,angle,profile.obliquityExponent,om,profile.apModifier);
                    if(!(multiplier>0))continue;
                    // Preserve native passive factors for simple/non-table members, including ERA material. Active ERA stays in Apply.
                    factor=own/multiplier;
                }
                candidates.Add(new(entity,material,label,interval.Start,interval.End,factor,interval.Angle,table==null?nativeRha:null));
            }
        }
        if(!solid)return; // Air confirms geometry but never pays it.
        var blockProfile=sim.projectileProfiles[fragment.projectileProfileIndex];var blockAngle=AngleOf(reached,first);
        var blockOm=blockProfile.obliquityExponent==1?0:ProtectionFactorTable.Overmatch(fragment.mass,fragment.speed,fragment.diameter,nativeLength,blockAngle);
        var blockMultiplier=ProtectionFactorTable.NativeRhaFactor(1,blockAngle,blockProfile.obliquityExponent,blockOm,blockProfile.apModifier);
        if(!(blockMultiplier>0))return;
        var plan=ray.Ledger.Plan(start,end,candidates.Select(c=>c.NativeRha is {} native?c with{Factor=native/blockMultiplier}:c),nativeLength);
        if(plan==null)return;
        var selected=plan.Pieces.FirstOrDefault();
        var member=new Member(first.objectIndex,selected?.Entity??sim.objects[first.objectIndex].ID,null!,0,selected?.Material??"uncovered",null!,first,last,true);
        WriteFactor(reached,member,threat,first,last,interval:plan,ray:ray);
        if(plan.Ambiguous&&!state.IntervalOverlapReported)
        {
            state.IntervalOverlapReported=true;
            const string text="Ambiguous material overlap: the highest confirmed factor is applied once to each shared ray interval.";
            var overlapping=plan.Pieces.First(p=>plan.Claims.Any(c=>c.Entity!=p.Entity&&c.StartM<p.EndM&&c.EndM>p.StartM));
            Report(reached,overlapping.Entity,"materialIntervalOverlap",text);
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Protection] "+text);
        }
    }
    /// <summary>
    /// The own pieces of a plate in layers on this ray. Its Enter and Exit hits are paired first in, first out, so two faces that overlap by a
    /// fraction of a millimetre stay two pieces; each new piece looks up the face of its entry triangle once (RuntimeLayerFaces.TryFace) and is
    /// kept on the ray, sorted along it.
    /// </summary>
    private static List<LayerChord> LayeredChords(NativeArmourBlockContext reached,IntervalRay ray,Vector3 spawn,Vector3 direction,int objectIndex,int entity,LayerHitIndex? index,PlateStructure plate,bool withRanks)
    {
        var perRay=layerChords.GetOrCreateValue(ray);
        if(!perRay.TryGetValue(entity,out var list))perRay[entity]=list=new();
        var hits=new List<(double Distance,bool Enter,bool Exit,int Hit)>();
        for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
        {
            var hit=reached.Intersections[i];if(hit.objectIndex!=objectIndex)continue;
            var at=ray.Distance(spawn,direction,hit.distance);if(!double.IsFinite(at))continue;
            hits.Add((at,(hit.flags&IntersectionFlags.Enter)!=0,(hit.flags&IntersectionFlags.Exit)!=0,i));
        }
        hits.Sort((a,b)=>a.Distance!=b.Distance?a.Distance.CompareTo(b.Distance):(a.Enter?1:0).CompareTo(b.Enter?1:0));
        var open=new Queue<(double Distance,int Hit)>();bool added=false;
        foreach(var h in hits)
        {
            if(h.Exit&&open.Count>0)
            {
                var entry=open.Dequeue();
                if(h.Distance>entry.Distance&&!list.Any(k=>Math.Abs(k.StartM-entry.Distance)<1e-6&&Math.Abs(k.EndM-h.Distance)<1e-6))
                {
                    var entryHit=reached.Intersections[entry.Hit];var angle=AngleOf(reached,entryHit);
                    int rank=-1;bool back=false;
                    if(!withRanks){}
                    else if(index!=null&&RuntimeLayerFaces.TryFace(reached.Sim,entryHit,index,out var face,out back))rank=index.RankOf(face);
                    else if(index!=null&&MidpointFace(reached,ray,(entry.Distance+h.Distance)*.5,plate,index,out var inside,out var reverse)){rank=index.RankOf(inside);back=reverse;}
                    if(double.IsFinite(angle)){list.Add(new(entry.Distance,h.Distance,angle,rank,back));added=true;}
                }
            }
            if(h.Enter&&!h.Exit)open.Enqueue((h.Distance,h.Hit));
        }
        if(added)list.Sort((a,b)=>a.StartM.CompareTo(b.StartM));
        return list;
    }
    /// <summary>
    /// 0.22.61: the face of a piece whose entry triangle names none (a side entry): the face whose solid holds the middle of the piece, from the
    /// per-mesh table (plate frame); entered from the back when the shot runs along the face normal.
    /// </summary>
    private static bool MidpointFace(NativeArmourBlockContext reached,IntervalRay ray,double distance,PlateStructure plate,LayerHitIndex index,out int face,out bool back)
    {
        face=-1;back=false;
        try
        {
            if(index.Solids==null)return false;
            var world=reached.Sim.SimToWorldSpacePoint(ray.Origin+ray.Direction*(float)distance);var local=plate.transform.InverseTransformPoint(world);
            face=index.FaceContaining(new System.Numerics.Vector3(local.x,local.y,local.z));if(face<0)return false;
            var d=plate.transform.InverseTransformDirection(reached.Sim.SimToWorldSpaceDirection(ray.Direction));
            back=System.Numerics.Vector3.Dot(new System.Numerics.Vector3(d.x,d.y,d.z),index.Solids.Normals[face])>0;
            return true;
        }
        catch(Exception){face=-1;return false;}
    }
}