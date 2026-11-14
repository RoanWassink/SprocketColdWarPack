using Sprocket.DamageModelling;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
namespace SprocketExpandedBallistics;

internal static partial class NativeMaterialReactions
{
    private sealed class IntervalRay
    {
        internal readonly Vector3 Origin,Direction;
        private readonly MaterialRayCoordinates coordinates;
        internal readonly MaterialIntervalLedger Ledger=new();
        internal readonly Dictionary<int,List<(double Start,double End,double Angle)>> Geometry=new();
        internal IntervalRay(Vector3 origin,Vector3 direction){Origin=origin;Direction=direction;coordinates=new(new(origin.x,origin.y,origin.z),new(direction.x,direction.y,direction.z));}
        // Double arithmetic before adding a distance avoids child-spawn float cancellation.
        internal double Distance(Vector3 spawn,Vector3 direction,double distance)=>
            coordinates.Project(new(spawn.x,spawn.y,spawn.z),new(direction.x,direction.y,direction.z),distance);
        internal bool Contains(Vector3 spawn,Vector3 direction)=>Vector3.Dot(Direction,direction)>.999999f&&Vector3.Cross(spawn-Origin,Direction).sqrMagnitude<1e-8f;
    }
    private static void ReachedIntervals(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last)
    {
        // PerInterval replaces all legacy blended/overlap/skipped compensation: only confirmed own hits supply material.
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
            if(!solid||!known.Any(k=>k.End>start&&k.Start<end))continue;
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
    private static short IntervalLineageRoot(PenetrationSimulation sim,short index)
    {
        var root=index;
        // Parent indices strictly decrease. Unlike the Legacy compensation key this covers lines longer than 128 fragments.
        for(int step=0;step<sim.FragmentCount;step++)
        {
            var parent=sim.fragments[root].parentIndex;
            if(parent<0||parent>=root||(sim.fragments[parent].flags&FragmentFlag.OriginalPenetrator)==0)break;
            root=parent;
        }
        return root;
    }
}
