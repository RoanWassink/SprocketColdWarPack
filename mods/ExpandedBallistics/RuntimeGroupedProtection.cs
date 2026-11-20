#if !EB_PURE_SOLID_MARK
using BepInEx.Configuration;
using System.Runtime.CompilerServices;
using Sprocket.DamageModelling;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
#endif
namespace SprocketExpandedBallistics;
#if !EB_PURE_SOLID_MARK
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
                // 0.22.77: a plate marked [Solid block] pays its cavity here, before the unchanged route below pays the skins (SolidCavities; it only reads without such a plate).
                if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length)SolidCavities(reached,first,last);
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
// 0.22.77 (proef "Solid block", user decision 14-11): a plate whose mesh name carries [Solid block] and whose material is a recipe counts as one solid shape. Its cavity
// (inside minus its own skins, SolidBlock in CompositePlate.cs) is paid in the block of its front skin, before the route above pays that skin: through the existing
// ApplyPassive (the native law, WriteBudget) at the table factor of the recipe x the path, and claimed on the ray ledger (Commit) at once, so a native block that spans the
// cavity pays only the skins and a child that starts inside never pays it again. The skins are paid exactly as before. No new hook; no other native write (rhaFactor stays
// with WriteFactor and its finalizer). An exception falls back to the old calculation for that block with one log line per plate per session (SolidBlock.Guard).
// 0.22.79: the mark is noted at a safe moment, the existing PlateStructure.Build postfix (RuntimeLayerFaces.Weigh calls SolidMarkBuilt), in SolidMarks; the hit only looks it
// up there and never reads the mesh name. A cassette plate whose mark is not known yet is "no Solid block" for that block only (not kept for the shot), so a later block of
// the same shot asks again. The report line of the cavity belongs to the block of the front skin (LayerStarted first). One [Solid block] diag line per shot per cassette plate.
internal static partial class NativeMaterialReactions
{
    /// <summary>What a shot knows of one cassette plate on its line (null in the cache: not a cassette plate).</summary>
    private sealed class SolidSeen
    {
        internal SolidMarkAnswer Answer;internal PlateStructure? Plate;internal long Spawn;internal SolidMarks.Entry Mark;internal bool MeshSame;internal string Reason="";
    }
    private sealed class SolidShot{internal readonly SolidShotCache<SolidSeen> Plates=new();internal readonly HashSet<int> Odd=new();internal readonly SolidDiagGate Diag=new();}
    private static readonly ConditionalWeakTable<ShellImpactContext,SolidShot> solidShots=new();
    private static readonly HashSet<int> solidFailed=new();
    private static bool solidScanFailed,solidBuildFailed;
    /// <summary>
    /// 0.22.79: called first from RuntimeLayerFaces.Weigh, the existing PlateStructure.Build postfix (no new hook). Notes whether a cassette plate carries the mark; in the
    /// designer it waits while the gate is busy (an edit or a held mouse button), in Play the plate is not edited and the name is read at once. Never throws.
    /// </summary>
    internal static void SolidMarkBuilt(PlateStructure plate)
    {
        try
        {
            if(plate==null)return;
            var pointer=plate.Pointer;long key=pointer.ToInt64();
            if(!CompositePlate.IsCassette(plate.armourTechID)){SolidMarks.Forget(key);return;}
            if(SolidMarkNote(plate,"Build",false)!=SolidMarks.BuildOutcome.Defer)return;
            PlateMeshDeferred.WhenReady("solid mark",()=>plate==null||plate.Pointer!=pointer||plate.HasMesh&&SolidMarks.CanRead(RuntimeComposerFill.Designer,PlateMeshAccess.Gate.Busy),()=>
            {
                try{if(plate!=null&&plate.Pointer==pointer)SolidMarkNote(plate,"Build (deferred)",true);}
                catch(Exception error){SolidMarks.Unread(key,0,"Build (deferred): "+error.GetType().Name);}
                finally{SolidMarks.EndDefer(key);}
            },10f);
        }
        catch(Exception error)
        {
            if(!solidBuildFailed){solidBuildFailed=true;Plugin.ModLog.LogWarning("[Solid block] The mark of a plate could not be noted at build ("+error.GetType().Name+": "+error.Message+"); such a plate counts as no solid block. Logged once per session.");}
        }
    }
    private static SolidMarks.BuildOutcome SolidMarkNote(PlateStructure plate,string source,bool last)
    {
        bool hasMesh=plate.HasMesh;long mesh=hasMesh?plate.Mesh.Pointer.ToInt64():0;
        return SolidMarks.Built(plate.Pointer.ToInt64(),mesh,hasMesh,RuntimeComposerFill.Designer,PlateMeshAccess.Gate.Busy,()=>plate.Mesh.ToMeshBlueprint(false)?.Name,source,last,
            text=>Plugin.ModLog.LogInfo(text),plate.VUID.Value);
    }
    /// <summary>A cassette plate behind an entity and what is known of its mark; null for everything else (no allocation for those after the first block). Definite answers are kept for the shot, an unknown mark is not.</summary>
    private static SolidSeen? SolidPlate(NativeArmourBlockContext reached,SolidShot shot,int entity)
    {
        if(shot.Plates.TryCached(entity,out var known))return known;
        SolidSeen? result=null;
        if(RuntimeArmourResponses.TryResolvePlate(reached.Impact,reached.Sim,entity,out var component,out var spawn,out _)&&component.TryCast<PlateStructure>() is {} plate&&
            CompositePlate.IsCassette(plate.armourTechID))
        {
            long key=plate.Pointer.ToInt64();var answer=SolidMarks.Answer(key,out var mark,out var reason);
            long mesh=0;try{if(plate.HasMesh)mesh=plate.Mesh.Pointer.ToInt64();}catch(Exception){}
            result=new SolidSeen{Answer=answer,Plate=plate,Spawn=spawn,Mark=mark,MeshSame=mark.Mesh!=0&&mark.Mesh==mesh,Reason=reason};
        }
        shot.Plates.Remember(entity,result,result==null||result.Answer!=SolidMarkAnswer.Unknown);
        return result;
    }
    /// <summary>Pays the cavities of the plates marked [Solid block] whose front skin lies in this block. Never throws; without a cassette plate on the line it only reads.</summary>
    private static void SolidCavities(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last)
    {
        Dictionary<int,(SolidSeen Seen,List<(double Distance,int Hit)> Own)>? plates=null;SolidShot? shot=null;bool verbose=false;
        try
        {
            if(reached.Impact==null||reached.Impact.ArmourSnapshot?.Cassettes is not {Count:>0})return;
            var sim=reached.Sim;var block=reached.Block;
            if(block.MaterialIndex<0||block.MaterialIndex>=sim.materials.Length)return;
            shot=solidShots.GetOrCreateValue(reached.Impact);verbose=RuntimeGroupedProtection.LayerHitLog;
            for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
            {
                var hit=reached.Intersections[i];var o=hit.objectIndex;
                if(o<0||o>=sim.objects.Length)continue;
                var entity=sim.objects[o].ID;
                if(SolidPlate(reached,shot,entity) is not {} seen)continue; // not a cassette plate: nothing more is done or allocated
                if(seen.Answer!=SolidMarkAnswer.Marked&&!shot.Diag.Wants(entity,verbose,seen.Answer))continue;
                plates??=new();if(!plates.TryGetValue(entity,out var entry))plates[entity]=entry=(seen,new());
                entry.Own.Add((hit.distance,i));
            }
        }
        catch(Exception error)
        {
            if(!solidScanFailed){solidScanFailed=true;Plugin.ModLog.LogWarning("[Solid block] The shot line could not be read ("+error.GetType().Name+": "+error.Message+"); blocks keep the calculation without the cavity. Logged once per session.");}
            return;
        }
        if(plates==null||shot==null)return;
        foreach(var pair in plates)
        {
            var (seen,own)=pair.Value;
            var diag=shot.Diag.Wants(pair.Key,verbose,seen.Answer)?new SolidDiag{Shot=shot.Diag.Id,Entity=pair.Key,Name=seen.Mark.Name,Marked=seen.Answer==SolidMarkAnswer.Unknown?null:seen.Answer==SolidMarkAnswer.Marked,
                Source=seen.Answer==SolidMarkAnswer.Unknown?"unknown: "+seen.Reason:seen.Mark.Source+(seen.MeshSame?"":" (other mesh)"),Busy=SolidDiagBusy(),Surfaces=own.Count}:null;
            if(seen.Answer==SolidMarkAnswer.Marked)
                SolidBlock.Guard(pair.Key,solidFailed,text=>Plugin.ModLog.LogWarning(text),()=>SolidCavity(reached,first,last,shot,pair.Key,seen,own,diag));
            else if(diag!=null)diag.Exit=seen.Answer==SolidMarkAnswer.Unmarked?"unmarked":"markUnknown";
            if(diag!=null&&shot.Diag.Allow(pair.Key,diag.Exit,verbose))Plugin.ModLog.LogInfo(diag.Text());
        }
    }
    private static bool SolidDiagBusy(){try{return PlateMeshAccess.Gate.Busy;}catch(Exception){return false;}}
    /// <summary>0.22.81: per persistent ray, what the line showed of each plate marked [Solid block] (only plates that reach SolidCavity, so only marked cassette plates).</summary>
    private static readonly ConditionalWeakTable<IntervalRay,Dictionary<int,SolidLineSeen>> solidLines=new();
    private static SolidLineSeen SolidLines(IntervalRay ray,int entity)
    {
        var perRay=solidLines.GetOrCreateValue(ray);
        if(!perRay.TryGetValue(entity,out var seen)){seen=new();if(perRay.Count<1024)perRay[entity]=seen;}
        return seen;
    }
    private static void SolidCavity(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last,SolidShot shot,int entity,SolidSeen found,List<(double Distance,int Hit)> own,SolidDiag? diag)
    {
        var sim=reached.Sim;var fragment=sim.fragments[reached.Index];
        if((fragment.flags&(FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator){if(diag!=null)diag.Exit="notOriginalPenetrator";return;}
        if(fragment.projectileProfileIndex<0||fragment.projectileProfileIndex>=sim.projectileProfiles.Length){if(diag!=null)diag.Exit="noProfile";return;}
        if(ProtectionFactorTable.ThreatOf(Threats(reached.Impact,true)) is not {} threat){if(diag!=null)diag.Exit="noThreat";return;}
        double nativeLength=last.distance-first.distance;
        if(!double.IsFinite(nativeLength)||nativeLength<=0){if(diag!=null)diag.Exit="boundaryContact";return;}
        if(found.Plate is not {} plate){if(diag!=null)diag.Exit="noPlate";return;}
        if(RuntimeSolidCollider.IsNativeSolid(plate)){if(diag!=null)diag.Exit="nativeSolid";return;}
        var material=plate.armourTechID;
        if(!Usable(reached,plate,found.Spawn,entity,material,reached.Impact.ArmourSnapshot?.Protection,out var table)||table==null){if(diag!=null)diag.Exit="notUsable";return;}
        // The persistent ray of this penetrator line, found or made exactly as ReachedIntervals does (it then finds the same one).
        var state=impacts.GetOrCreateValue(reached.Impact);var key=(sim.Pointer,IntervalLineageRoot(sim,reached.Index));
        if(!state.Rays.TryGetValue(key,out var rays))state.Rays[key]=rays=new();
        var direction=fragment.direction.normalized;
        var ray=rays.LastOrDefault(r=>r.Contains(fragment.spawnPoint,direction));
        if(ray==null){ray=new(fragment.spawnPoint,direction);rays.Add(ray);}
        double start=ray.Distance(fragment.spawnPoint,direction,first.distance),end=ray.Distance(fragment.spawnPoint,direction,last.distance);
        var along=new List<(double At,int Hit)>(own.Count);
        foreach(var (distance,hit) in own){var at=ray.Distance(fragment.spawnPoint,direction,distance);if(double.IsFinite(at))along.Add((at,hit));}
        var line=SolidBlock.Analyse(along.Select(a=>a.At));
        diag?.Line(line,start,end);
        // 0.22.81: what this shot line showed of the plate over all its blocks; read only for the report line of an odd count (the calculation keeps using line)
        var seen=SolidLines(ray,entity);seen.See(along.Select(a=>a.At));
        if(line.Odd)
        {
            if(diag!=null)diag.Exit="odd";
            // 0.22.81: no line when the cavity of this plate on this line was paid already (the back skin of a paid passage); the count is that of the whole line, odd only when it is
            if(seen.OddLine(along.Select(a=>a.At)) is {} whole&&shot.Odd.Count<1024&&shot.Odd.Add(entity))
            {
                RuntimeDamageFeed.LayerStarted(reached); // 0.22.79: the line belongs to this block (idempotent per block; the route below starts the same layer)
                Report(reached,entity,"materialReaction",SolidBlock.OddText(table.Label,whole));
                if(RuntimeGroupedProtection.LayerHitLog)Plugin.ModLog.LogInfo(SolidBlock.OddLogText(entity,whole));
            }
            return;
        }
        double EntryAngle(SolidPassage passage)
        {
            int best=-1;double gap=double.PositiveInfinity;
            foreach(var (at,hit) in along){var d=Math.Abs(at-passage.Front.StartM);if(d<gap){gap=d;best=hit;}}
            var angle=best<0?double.NaN:AngleOf(reached,reached.Intersections[best]);
            if(diag!=null)diag.Angle=angle;
            return angle;
        }
        var charges=SolidBlock.Plan(line,start,end,ray.Ledger,entity,material,table.Label,EntryAngle,angle=>{var f=table.Factor(threat,angle);if(diag!=null)diag.Factor=f;return f;});
        if(diag!=null){diag.Charges=charges.Count;diag.Exit=charges.Count==0?"notDue":"applyPassiveFalse";}
        bool started=false;
        foreach(var charge in charges)
        {
            if(!started){started=true;RuntimeDamageFeed.LayerStarted(reached);} // 0.22.79: before the cavity is paid, so its report line belongs to the block of the front skin
            if(!ApplyPassive(reached,entity,"Solid block cavity",charge.RhaMm,out var before,out var after)){diag?.Applied(charge,false,before,after);continue;}
            diag?.Applied(charge,true,before,after);
            ray.Ledger.Commit(charge.Plan);
            seen.Paid(charge.Passage);
            Note(reached,entity,null,SolidBlock.ReportText(table.Label,charge,ProtectionFactorTable.Text(threat)),before,after,"materialReaction");
            // 0.22.81: the marker of the report: the readers draw this passage (front skin, cavity, back skin) as one part (LayeredPlateReport); only read, never paid
            Report(reached,entity,LayeredPlateReport.SolidMarker,SolidBlockMarker.Text(table.Label,charge),entity);
            if(RuntimeGroupedProtection.LayerHitLog)Plugin.ModLog.LogInfo(SolidBlock.LogText(entity,charge,before,after));
        }
    }
}
#endif

// ---------------- 0.22.79: the pure part (no game types; the managed tests compile this file with EB_PURE_SOLID_MARK) ----------------

/// <summary>What is known of the [Solid block] mark of a cassette plate when a block of a shot asks.</summary>
internal enum SolidMarkAnswer:byte{Unknown,Unmarked,Marked}

/// <summary>
/// 0.22.79: the [Solid block] mark per plate (keyed by the plate's native pointer), noted in the existing PlateStructure.Build postfix and only looked up during a hit.
/// In the designer the name is read only while the mesh gate is not busy (an edit or a held mouse button); otherwise the note waits (deferred). In Play the plate is not
/// edited and the name is read at once, as the uniform hit path (RuntimeLayerFaces.TryKind) already does. A read that fails or is deferred leaves the mark unknown.
/// </summary>
internal static class SolidMarks
{
    internal readonly record struct Entry(long Mesh,bool Read,bool Marked,string? Name,string Source);
    internal enum BuildOutcome:byte{Noted,Defer,Deferring}
    internal const int Capacity=4096;
    internal const int BuildNoteLimit=16;
    private static readonly Dictionary<long,Entry> plates=new();
    private static readonly HashSet<long> pending=new();
    private static int buildNotes;
    internal static int Count=>plates.Count;
    internal static int BuildNotes=>buildNotes;
    /// <summary>For the tests: a fresh session.</summary>
    internal static void Reset(){plates.Clear();pending.Clear();buildNotes=0;}
    /// <summary>The designer reads the mesh only while the gate is not busy; Play always.</summary>
    internal static bool CanRead(bool designer,bool busy)=>!designer||!busy;
    internal static void Forget(long plate){plates.Remove(plate);pending.Remove(plate);}
    internal static void ForgetMesh(long mesh){foreach(var k in plates.Where(p=>p.Value.Mesh==mesh).Select(p=>p.Key).ToArray())plates.Remove(k);}
    internal static void EndDefer(long plate)=>pending.Remove(plate);
    private static void Put(long plate,Entry entry){if(plates.Count>=Capacity&&!plates.ContainsKey(plate))plates.Clear();plates[plate]=entry;}
    internal static void Unread(long plate,long mesh,string source)=>Put(plate,new(mesh,false,false,null,source));
    /// <summary>
    /// The Build postfix step for a cassette plate. Reads the name when it may (<see cref="CanRead"/> and a mesh) and notes the mark; otherwise Defer (the caller schedules
    /// one later attempt) or Deferring (one is already scheduled). <paramref name="last"/>: the deferred attempt; when it still may not read, the mark stays unknown.
    /// </summary>
    internal static BuildOutcome Built(long plate,long mesh,bool hasMesh,bool designer,bool busy,Func<string?> readName,string source,bool last,Action<string>? log=null,long vuid=0)
    {
        if(!hasMesh||!CanRead(designer,busy))
        {
            if(last){Unread(plate,mesh,source+": "+(hasMesh?"gate busy":"no mesh"));return BuildOutcome.Noted;}
            return pending.Add(plate)?BuildOutcome.Defer:BuildOutcome.Deferring;
        }
        string? name;
        try{name=readName();}
        catch(Exception error){Unread(plate,mesh,source+": read failed ("+error.GetType().Name+")");pending.Remove(plate);return BuildOutcome.Noted;}
        var marked=CompositePlate.IsSolidBlock(name);
        Put(plate,new(mesh,true,marked,name,source));pending.Remove(plate);
        if(marked&&!designer&&log!=null&&buildNotes<BuildNoteLimit)
        {
            buildNotes++;
            log(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Solid block] mark noted: plate #{vuid} (0x{plate:x}, mesh 0x{mesh:x}), {source}, outside the designer, gate busy {busy}. At most {BuildNoteLimit} such lines per session."));
        }
        return BuildOutcome.Noted;
    }
    /// <summary>The hit: what is known of this plate. Only a lookup; nothing is read from the plate.</summary>
    internal static SolidMarkAnswer Answer(long plate,out Entry entry,out string reason)
    {
        if(plates.TryGetValue(plate,out entry))
        {
            if(entry.Read){reason="";return entry.Marked?SolidMarkAnswer.Marked:SolidMarkAnswer.Unmarked;}
            reason=entry.Source;return SolidMarkAnswer.Unknown;
        }
        entry=new(0,false,false,null,"");
        reason=pending.Contains(plate)?"deferred at build (designer gate busy or no mesh)":"no build entry";
        return SolidMarkAnswer.Unknown;
    }
}

/// <summary>0.22.79: per shot, the plates of the line. A definite answer (not a cassette plate: null; unmarked; marked) is kept for the shot; an unknown mark is asked again by the next block.</summary>
internal sealed class SolidShotCache<T> where T:class
{
    private readonly Dictionary<int,T?> known=new();
    internal const int Limit=1024;
    internal int Count=>known.Count;
    internal bool TryCached(int entity,out T? value)=>known.TryGetValue(entity,out value);
    internal void Remember(int entity,T? value,bool definite){if(definite&&known.Count<Limit)known[entity]=value;}
}

/// <summary>
/// 0.22.79: which [Solid block] diag lines a shot writes. One line per cassette plate; a second one only when the first was not decisive (notDue, markUnknown) and a later
/// block of the same shot is. At most <see cref="SessionLimit"/> lines per session. Built only for cassette plates.
/// </summary>
internal sealed class SolidDiagGate
{
    internal const int SessionLimit=64;
    /// <summary>0.22.82 (point F): without Composite fill / Layer hit log only the ways out where a plate with the mark (or a mark not known) does not pay are written, at most this many per session.</summary>
    internal const int FailureLimit=16;
    private static int written,shots,failures;
    internal static int Written=>written;
    internal static int Failures=>failures;
    internal static void ResetSession(){written=0;shots=0;failures=0;}
    internal readonly int Id=++shots;
    private Dictionary<int,bool>? state; // entity -> the line written so far was decisive
    internal static bool Decisive(string exit)=>exit is not ("notDue" or "markUnknown");
    /// <summary>0.22.82: the ways out that are always written (one line, at most <see cref="FailureLimit"/> per session): the mark is not known, the calculation threw, ApplyPassive refused.</summary>
    internal static bool Failure(string exit)=>exit is "markUnknown" or "exception" or "applyPassiveFalse";
    private bool Open(int entity)=>state==null||!state.TryGetValue(entity,out var decisive)||!decisive;
    /// <summary>The 0.22.79 form: the gate with Composite fill / Layer hit log on.</summary>
    internal bool Wants(int entity)=>Wants(entity,true,SolidMarkAnswer.Marked);
    /// <summary>0.22.82: with the setting on (<paramref name="verbose"/>) as in 0.22.79; off, only for a plate whose mark is not "no" and while the failure limit allows a line.</summary>
    internal bool Wants(int entity,bool verbose,SolidMarkAnswer answer)=>verbose?written<SessionLimit&&Open(entity):answer!=SolidMarkAnswer.Unmarked&&failures<FailureLimit&&Open(entity);
    internal bool Allow(int entity,string exit)=>Allow(entity,exit,true);
    internal bool Allow(int entity,string exit,bool verbose)
    {
        if(verbose?written>=SessionLimit:!Failure(exit)||failures>=FailureLimit)return false;
        state??=new();
        if(state.TryGetValue(entity,out var decisive)&&(decisive||!Decisive(exit)))return false;
        state[entity]=Decisive(exit);
        if(verbose)written++;else failures++;
        return true;
    }
}

/// <summary>0.22.79: the content of one [Solid block] diag line. 0.22.82: written with Composite fill / Layer hit log on; without it only for the ways out of <see cref="SolidDiagGate.Failure"/>.</summary>
internal sealed class SolidDiag
{
    internal int Shot,Entity;internal string? Name;internal bool? Marked;internal string Source="";internal bool Busy;
    internal int Surfaces,Skins=-1,Passages=-1,Due=-1,Charges=-1;
    internal double BlockStartM=double.NaN,BlockEndM=double.NaN,Angle=double.NaN,Factor=double.NaN;
    internal string Apply="-";
    /// <summary>The way out: paid, unmarked, markUnknown, notDue, odd, applyPassiveFalse, notOriginalPenetrator, noProfile, noThreat, boundaryContact, noPlate, notUsable; exception when the calculation threw.</summary>
    internal string Exit="exception";
    internal void Line(SolidLine line,double startM,double endM)
    {
        Skins=line.Skins.Length;Passages=line.Passages.Length;BlockStartM=startM;BlockEndM=endM;Due=SolidBlock.Due(line,startM,endM).Count();
    }
    internal void Applied(SolidCharge charge,bool ok,double beforeMm,double afterMm)
    {
        var text=ok?string.Create(System.Globalization.CultureInfo.InvariantCulture,$"{charge.RhaMm:0.#} mm RHA-eq paid, {beforeMm:0.#} -> {afterMm:0.#} mm")
            :string.Create(System.Globalization.CultureInfo.InvariantCulture,$"false for {charge.RhaMm:0.#} mm RHA-eq (budget {beforeMm:0.#} mm)");
        Apply=Apply=="-"?text:Apply+"; "+text;
        if(ok)Exit="paid";
    }
    private static string N(double v,string f)=>double.IsFinite(v)?v.ToString(f,System.Globalization.CultureInfo.InvariantCulture):"-";
    private static string I(int v)=>v<0?"-":v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal string Text()=>string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"[Solid block] diag shot {Shot} plate #{Entity}: name \"{Name??"(unknown)"}\", mark {(Marked is {} m?(m?"yes":"no"):"unknown")} ({Source}), gate busy {Busy}; {Surfaces} surfaces, {I(Skins)} skins, {I(Passages)} passages; block [{N(BlockStartM,"0.######")},{N(BlockEndM,"0.######")}] m, due {I(Due)}, charges {I(Charges)}; angle {N(Angle,"0.#")}°, factor {N(Factor,"0.####")}; ApplyPassive {Apply}; exit {Exit}.");
}

/// <summary>
/// 0.22.81 (point B of the brief): what one shot line (one persistent ray) has shown of one plate marked [Solid block] over all its blocks: its surfaces (metres on the ray,
/// whichever block listed them) and the passages whose cavity this line paid. It decides only the report line of an odd count; the calculation never reads it. A block that
/// lists an odd count reports it only when that count is odd for the whole line, and never when every surface it lists lies in a passage whose cavity was paid (the back skin
/// of a paid passage, listed on its own by its block). The count in the line is that of the whole line.
/// </summary>
internal sealed class SolidLineSeen
{
    /// <summary>Two distances closer than this (metres on the ray) are the same surface listed by two blocks.</summary>
    internal const double SameM=1e-5;
    internal const int Limit=512;
    private readonly List<double> surfaces=new();
    private readonly List<SolidPassage> paid=new();
    internal int Surfaces=>surfaces.Count;
    internal int PaidPassages=>paid.Count;
    internal void See(IEnumerable<double> along)
    {
        foreach(var d in along)
        {
            if(!double.IsFinite(d)||surfaces.Count>=Limit)continue;
            int i=surfaces.BinarySearch(d);if(i<0)i=~i;
            if(i<surfaces.Count&&surfaces[i]-d<SameM||i>0&&d-surfaces[i-1]<SameM)continue;
            surfaces.Insert(i,d);
        }
    }
    internal void Paid(SolidPassage passage){if(paid.Count<Limit)paid.Add(passage);}
    /// <summary>True when the distance lies in a passage whose cavity this line paid (its skins included, within the 1 mm of one skin).</summary>
    internal bool InPaidPassage(double d)
    {
        foreach(var p in paid)if(d>=p.Front.StartM-SolidBlock.MergeGapM&&d<=p.Back.EndM+SolidBlock.MergeGapM)return true;
        return false;
    }
    /// <summary>The line to report for a block that lists an odd count (<paramref name="block"/>): null when its surfaces all lie in a paid passage or when the whole line is even.</summary>
    internal SolidLine? OddLine(IEnumerable<double> block)
    {
        var own=block.Where(double.IsFinite).ToList();
        if(paid.Count>0&&own.Count>0&&own.All(InPaidPassage))return null;
        var whole=SolidBlock.Analyse(surfaces);
        return whole.Odd?whole:null;
    }
}
