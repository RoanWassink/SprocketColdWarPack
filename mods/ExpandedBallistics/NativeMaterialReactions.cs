using System.Runtime.CompilerServices;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NumericVector3=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// 0.22.28 (user decision 03-11): the protection of a mod material (a cassette on a composite plate, or a loose recipe material) is computed
// in advance (ProtectionFactorTable). For a block of a mod shot on such a material the plugin writes rhaFactor = factor(threat, θ) into the
// block's MaterialInfo before native simulates it, and restores it when the block is done; native decides stop, ricochet, child and spall.
// θ is the native entry normal of the block, so the factor is applied to exactly the path native charges. What native cannot see is still
// added from the same table (user decision 02-11, "Ja, bijtellen"): a composite plate it skipped behind an air block, the rest of a plate
// cut short by an overlapping component, and a plate co-located in a block that carries another material.
internal static partial class NativeMaterialReactions
{
    private sealed class ImpactState
    {
        internal readonly HashSet<(IntPtr Sim,short Index,int Entity,string Kind,string Label)> Reported=new();
        internal readonly Dictionary<(IntPtr Sim,short Root),List<IntervalRay>> Rays=new();
        internal bool IntervalOverlapReported;
        internal readonly HashSet<(IntPtr Sim,short Lineage,int Entity,int Face)> Awarded=new();
    }
    // 0.22.16: an air block can end on the front face of a composite plate that native then never reaches. The plate is remembered per
    // penetrator line and paid on the next solid block; first one wins and the table is bounded.
    private sealed record PendingPlate(IntPtr Sim,short Root,int Entity,int ObjectIndex,double GapMm,string HitId,NumericVector3 EntryWorld);
    // One rhaFactor write, restored when its SimulateFragment call ends (Close).
    private readonly record struct FactorWrite(IntPtr Sim,short Index,Il2CppStructArray<MaterialInfo> Materials,int MaterialIndex,float Original,Il2CppStructArray<MaterialInfo>? Extra,float ExtraOriginal,
        int Entity,string Label,double PathMm,double Factor,ProtectionThreat Threat,double Angle,double BeforeMm,MaterialFingerprintWrite Fingerprint,CompositeModuleBlock? Module=null,MaterialIntervalPlan? Interval=null,IntervalRay? Ray=null,bool Committed=false);
    private readonly record struct Member(int ObjectIndex,int Entity,PlateStructure Plate,long Spawn,string Material,
        ProtectionFactorTable Table,StructureIntersection First,StructureIntersection Last,bool Hit);
    [ThreadStatic] private static List<FactorWrite>? writes;
    internal static MaterialFingerprint OriginalMaterial(PenetrationSimulation sim,int materialIndex)
    {
        var material=sim.materials[materialIndex];
        return MaterialFingerprint.BeforeWrites(sim.materials.Pointer,materialIndex,new(material.density,material.rhaFactor,material.spallFactor),
            writes?.Select(w=>w.Fingerprint)??Enumerable.Empty<MaterialFingerprintWrite>());
    }
    private static readonly Dictionary<(IntPtr Sim,short Root),PendingPlate> pendingPlates=new();
    private static readonly ConditionalWeakTable<ShellImpactContext,ImpactState> impacts=new();
    private static readonly HashSet<(long Spawn,int Entity)> warned=new();
    private static int writeWarnings;
    internal static void RemoveSpawn(long spawn)=>warned.RemoveWhere(c=>c.Spawn==spawn);
    internal static void Reset()
    {
        // A reset cannot discard an active restore record, even during a nested callback.
        while(writes is {Count:>0} list){var last=list[^1];Restore(last.Sim,last.Index);}
        warned.Clear();pendingPlates.Clear();
    }
    /// <summary>A mod material with a protection table: its non-ERA legacy armour response is replaced by the table.</summary>
    internal static bool HasTable(ShellImpactContext impact,string material)=>impact.ArmourSnapshot?.Protection?.ContainsKey(material)==true;
    internal static void Reached(NativeArmourBlockContext reached)
    {
        var sim=reached.Sim;var block=reached.Block;var hits=reached.Intersections;
        if(!block.Valid||reached.Index<0||reached.Index>=sim.FragmentCount)return;
        int entry=block.EnterIntersectionIndex,exit=block.ExitIntersectionIndex;
        if(entry<0||exit<0||entry>=reached.IntersectionCount||exit>=reached.IntersectionCount||entry>=hits.Length||exit>=hits.Length)return;
        var first=hits[entry];var last=hits[exit];
        if(first.objectIndex<0||first.objectIndex>=sim.objects.Length)return;
        if(GroupedProtection.Mode==GroupedBlockMode.PerInterval){ReachedIntervals(reached,first,last);return;}
        // 0.22.16: a pending skipped plate is paid before the next block's report row reads its entry budget. Air blocks only remember.
        ResolvePendingPlate(reached);
        RuntimeDamageFeed.LayerStarted(reached);
        if(block.MaterialIndex<0){RememberAirGap(reached,first,last,entry,exit);return;}
        if(block.MaterialIndex>=sim.materials.Length)return;
        var fragment=sim.fragments[reached.Index];
        if((fragment.flags&(FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator)return;
        if(WriteModule(reached,first,last))return;
        if(!FindMember(reached,first,last,out var member))return;
        var threat=ProtectionFactorTable.ThreatOf(Threats(reached.Impact,true));
        if(threat is not {} kind)
        {
            Report(reached,member.Entity,"materialUnavailable",$"{member.Table.Label}: no protection table vs {Threats(reached.Impact,true).ShellLabel()}; native resistance only.");
            return;
        }
        if(sim.objects[member.ObjectIndex].materialIndex==block.MaterialIndex)WriteFactor(reached,member,kind,first,last);
        else Blended(reached,member,kind);
        Overlap(reached,member,kind,last);
    }
    private static bool WriteModule(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last)
    {
        var sim=reached.Sim;
        if(reached.Impact.ArmourSnapshot?.Protection?.TryGetValue(CompositeModule.MaterialId,out var table)!=true||table==null)return false;
        var threat=ProtectionFactorTable.ThreatOf(Threats(reached.Impact,true));if(threat is not {} kind)return false;
        var candidates=new Dictionary<int,VehicleObjectModel>();
        for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
        {
            var hit=reached.Intersections[i];
            if(hit.distance<first.distance||hit.distance>last.distance||hit.objectIndex<0||hit.objectIndex>=sim.objects.Length)continue;
            if(candidates.ContainsKey(hit.objectIndex))continue;
            var entity=sim.objects[hit.objectIndex].ID;
            if(RuntimeArmourResponses.ResolveCompositeModule(reached.Impact,sim,entity) is {} model)candidates.Add(hit.objectIndex,model);
        }
        if(candidates.Count==0)return false;
        var intervals=new List<CompositeModuleInterval>();
        foreach(var candidate in candidates)
        {
            var design=candidate.Value.Vehicle?.DesignInfo;
            if(design==null)continue;
            var date=design.Date;
            if(!VehicleEraAvailability.TryCalendarDate(date.Year,date.Month,date.Day,out var calendar)||!CompositeModule.Current.Allows(calendar))continue;
            double start=double.PositiveInfinity,end=double.NegativeInfinity,angle=double.NaN;
            bool entered=false,exited=false;
            for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
            {
                var hit=reached.Intersections[i];if(hit.objectIndex!=candidate.Key)continue;
                if((hit.flags&IntersectionFlags.Enter)!=0&&hit.distance<start){start=hit.distance;angle=AngleOf(reached,hit);entered=true;}
                if((hit.flags&IntersectionFlags.Enter)==0&&hit.distance>end){end=hit.distance;exited=true;}
            }
            if(entered&&exited&&end>start)intervals.Add(new(sim.objects[candidate.Key].ID,start,end,angle,table));
        }
        var moduleEntities=intervals.Select(i=>i.Entity).ToHashSet();
        // Preserve the existing table plate's own response when a composite hull
        // and the new module share a native block, using its confirmed interval.
        if(FindMember(reached,first,last,out var plateMember)&&plateMember.Hit&&plateMember.Last.distance>plateMember.First.distance)
            intervals.Add(new(plateMember.Entity,plateMember.First.distance,plateMember.Last.distance,AngleOf(reached,plateMember.First),plateMember.Table));
        var original=sim.materials[reached.Block.MaterialIndex].rhaFactor;
        var fragment=sim.fragments[reached.Index];
        if(fragment.projectileProfileIndex<0||fragment.projectileProfileIndex>=sim.projectileProfiles.Length)return true;
        var profile=sim.projectileProfiles[fragment.projectileProfileIndex];var blockAngle=AngleOf(reached,first);var path=last.distance-first.distance;
        var om=profile.obliquityExponent==1?0:ProtectionFactorTable.Overmatch(fragment.mass,fragment.speed,fragment.diameter,path,blockAngle);
        var multiplier=ProtectionFactorTable.NativeRhaFactor(1,blockAngle,profile.obliquityExponent,om,profile.apModifier);
        var plan=multiplier>0?CompositeModule.Plan(first.distance,last.distance,original/multiplier,kind,intervals):null;
        if(plan==null||moduleEntities.Count==0)
        {
            foreach(var candidate in candidates)Report(reached,sim.objects[candidate.Key].ID,"materialUnavailable","Composite module interval unresolved/overlapping; native resistance only, grouped protection not proven.");
            return true;
        }
        var primary=plan.Layers.First(i=>moduleEntities.Contains(i.Entity));
        var member=new Member(first.objectIndex,primary.Entity,null!,0,CompositeModule.MaterialId,table,first,last,true);
        WriteFactor(reached,member,kind,first,last,plan);
        foreach(var layer in plan.Layers)
        {
            if(moduleEntities.Contains(layer.Entity))RuntimeDamageFeed.RecordCompositeModule(sim,reached.Index,layer,kind,plan.Grouped);
            else RuntimeDamageFeed.RecordArmourInteraction(sim,reached.Index,"materialReaction",ProtectionLine.Text(layer.Table.Label,(layer.EndM-layer.StartM)*1000,layer.Table.Factor(kind,layer.Angle),kind,layer.Angle),relatedEntity:layer.Entity);
        }
        return true;
    }
    // The table member of this block: the entry object itself, or for a grouped block the earliest plate with a table in the block span.
    private static bool FindMember(NativeArmourBlockContext reached,StructureIntersection blockFirst,StructureIntersection blockLast,out Member member)
    {
        member=default;
        var sim=reached.Sim;var hits=reached.Intersections;var block=reached.Block;
        Span<int> ids=stackalloc int[32];Span<double> distances=stackalloc double[32];
        var count=0;var objects=sim.objects.Length;
        Add(ids,distances,ref count,objects,blockFirst.objectIndex,blockFirst.distance);
        for(int i=0;i<reached.IntersectionCount&&i<hits.Length;i++)
        {
            var hit=hits[i];
            if(hit.distance>=blockFirst.distance&&hit.distance<=blockLast.distance)Add(ids,distances,ref count,objects,hit.objectIndex,hit.distance);
        }
        if(block.IntersectedObjectIndices is {} members)foreach(var objectIndex in members)Add(ids,distances,ref count,objects,objectIndex,blockFirst.distance);
        var tables=reached.Impact.ArmourSnapshot?.Protection;
        var best=-1;
        for(int i=0;i<count;i++)
        {
            if(best>=0&&distances[i]>=distances[best])continue;
            var entity=sim.objects[ids[i]].ID;
            if(!RuntimeArmourResponses.TryResolvePlate(reached.Impact,sim,entity,out var component,out var spawn,out _)||component.TryCast<PlateStructure>() is not {} plate)continue;
            var material=plate.armourTechID;
            if(!Usable(reached,plate,spawn,entity,material,tables,out var table))continue;
            best=i;member=new(ids[i],entity,plate,spawn,material,table!,blockFirst,blockLast,false);
        }
        if(best<0)return false;
        // The member's own intersections inside the block (its entry and exit), for the blended and overlap paths.
        double entry=double.PositiveInfinity,exit=double.NegativeInfinity;
        for(int i=0;i<reached.IntersectionCount&&i<hits.Length;i++)
        {
            var hit=hits[i];
            if(hit.objectIndex!=member.ObjectIndex||hit.distance<blockFirst.distance||hit.distance>blockLast.distance)continue;
            if(hit.distance<entry){entry=hit.distance;member=member with{First=hit,Hit=true};}
            if(hit.distance>exit){exit=hit.distance;member=member with{Last=hit};}
        }
        return true;
    }
    private static void Add(Span<int> ids,Span<double> distances,ref int count,int objects,int objectIndex,double distance)
    {
        if(objectIndex<0||objectIndex>=objects||!double.IsFinite(distance))return;
        for(int j=0;j<count;j++)if(ids[j]==objectIndex){if(distance<distances[j])distances[j]=distance;return;}
        if(count<ids.Length){ids[count]=objectIndex;distances[count]=distance;count++;}
    }
    // A cassette works only on a composite plate whose preset is loaded and allowed on this vehicle (31-10, 0.22.7); a loose material always.
    private static bool Usable(NativeArmourBlockContext reached,PlateStructure plate,long spawn,int entity,string material,
        IReadOnlyDictionary<string,ProtectionFactorTable>? tables,out ProtectionFactorTable? table)
    {
        table=null;
        var cassette=CompositePlate.IsCassette(material);
        if(cassette&&!RuntimeCompositePlate.Is(plate))
        {
            if(warned.Add((spawn,entity)))
            {
                const string message="Cassette on an ordinary plate; native resistance only. Use a Composite armour plate.";
                Plugin.ModLog.LogWarning("[Cassettes] Plate #"+entity+": "+message);
                Report(reached,entity,"materialUnavailable",message);
            }
            return false;
        }
        if(tables==null||!tables.TryGetValue(material,out table))
        {
            if(cassette)CassetteUnavailable(reached,spawn,entity,material);
            return false;
        }
        if(!cassette)return true;
        if(reached.Impact.ArmourSnapshot?.Cassettes?.TryGetValue(material,out var model)!=true||model==null){CassetteUnavailable(reached,spawn,entity,material);return false;}
        if(!CassetteUsable(plate,spawn,entity,material,model,out var blocked)){Report(reached,entity,"materialUnavailable",blocked);table=null;return false;}
        return true;
    }
    private static double AngleOf(NativeArmourBlockContext reached,StructureIntersection hit)
    {
        var sim=reached.Sim;var direction=sim.fragments[reached.Index].direction.normalized;
        var normal=reached.Structure.GetIntersectionNormal(ref hit).normalized;
        var cosine=Math.Abs(Vector3.Dot(direction,normal));
        if(!float.IsFinite(cosine)||normal.sqrMagnitude<.5f)return double.NaN;
        return Math.Acos(Math.Clamp(cosine,0,1))*180/Math.PI;
    }
    private static void WriteFactor(NativeArmourBlockContext reached,Member member,ProtectionThreat threat,StructureIntersection first,StructureIntersection last,CompositeModuleBlock? module=null,MaterialIntervalPlan? interval=null,IntervalRay? ray=null)
    {
        var sim=reached.Sim;var block=reached.Block;
        var angle=AngleOf(reached,first);
        if(!double.IsFinite(angle))return;
        var fragment=sim.fragments[reached.Index];
        if(fragment.projectileProfileIndex<0||fragment.projectileProfileIndex>=sim.projectileProfiles.Length)return;
        var factor=interval?.Factor??module?.Factor??member.Table.Factor(threat,angle);
        var label=interval!=null?"Confirmed material intervals":member.Table.Label;
        var exponent=sim.projectileProfiles[fragment.projectileProfileIndex].obliquityExponent;
        var path=Math.Max(0,last.distance-first.distance)*1000;
        var profile=sim.projectileProfiles[fragment.projectileProfileIndex];
        var om=exponent==1?0:ProtectionFactorTable.Overmatch(fragment.mass,fragment.speed,fragment.diameter,path*.001,angle);
        var value=(float)ProtectionFactorTable.NativeRhaFactor(factor,angle,exponent,om,profile.apModifier);
        if(!float.IsFinite(value)||value<0||value==0&&interval==null&&GroupedProtection.CompositeProtectionScale!=0)return;
        var floored=value<ProtectionFactorTable.MinimumNativeRhaFactor; // 0.22.90: the guard above is unchanged; then the floor: never 0 to native ("uncovered", a factor-0 face or module, scale 0), from 1e-6 up bit for bit
        value=ProtectionFactorTable.NativeSafe(value); // 0.22.90
        Restore(sim.Pointer,reached.Index);
        // MaterialInfo is a real value type in an Il2CppStructArray: the element read is a copy, so it is written back explicitly and read back.
        var materials=sim.materials;var original=materials[block.MaterialIndex].rhaFactor;
        var extra=reached.Materials is {} passed&&passed.Pointer!=materials.Pointer&&block.MaterialIndex<passed.Length?passed:null;
        var extraOriginal=extra==null?0:extra[block.MaterialIndex].rhaFactor;
        var own=materials[block.MaterialIndex];var other=extra==null?own:extra[block.MaterialIndex];
        var fingerprint=new MaterialFingerprintWrite(materials.Pointer,block.MaterialIndex,new(own.density,own.rhaFactor,own.spallFactor),
            extra?.Pointer??IntPtr.Zero,new(other.density,other.rhaFactor,other.spallFactor));
        // Register the restore before the first mutation: the finalizer also handles a native accessor throwing midway through the writes.
        (writes??=new()).Add(new(sim.Pointer,reached.Index,materials,block.MaterialIndex,original,extra,extraOriginal,member.Entity,label,path,factor,threat,angle,CurrentBudgetMm(sim,reached.Index,out _),fingerprint,module,interval,ray));
        float readBack;
        try
        {
            SetRha(materials,block.MaterialIndex,value,out readBack);
            if(readBack!=value)throw new InvalidOperationException("MaterialInfo write-back mismatch.");
            if(extra!=null){SetRha(extra,block.MaterialIndex,value,out var extraRead);if(extraRead!=value)throw new InvalidOperationException("MaterialInfo argument write-back mismatch.");}
        }
        catch
        {
            Restore(sim.Pointer,reached.Index);
            throw;
        }
        ArmourComparisonEvidence.Update(sim,reached.Index,member.Entity,member.Material,path*Math.Cos(angle*Math.PI/180),angle);
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[Protection] plate #{member.Entity} {member.Material}: {ProtectionLine.Text(label,path,factor,threat,angle)}; exponent {exponent:0.##}; rhaFactor {original:g6} -> {value:g6} (read back {readBack:g6}){(floored?" (floor; no unpaid path)":"")}")); // 0.22.90 replaced: six significant digits, so 0 and 1e-06 differ in the log
    }
    // 0.22.90 begin
    /// <summary>Diagnostics only (RuntimeOriginalTrajectory.End): "#plate (rhaFactor x)" of the last EB write still open for this SimulateFragment call,
    /// x read back from the native array; null when EB wrote nothing for that call. Reads only, never throws.</summary>
    internal static string? OpenWriteText(IntPtr sim,short index)
    {
        try
        {
            if(writes is not {Count:>0} list)return null;
            for(int i=list.Count-1;i>=0;i--)
            {
                var w=list[i];
                if(w.Sim==sim&&w.Index==index)return string.Create(System.Globalization.CultureInfo.InvariantCulture,$"#{w.Entity} (rhaFactor {w.Materials[w.MaterialIndex].rhaFactor:g6})");
            }
            return null;
        }
        catch(Exception){return null;}
    }
    // 0.22.90 end
    internal static void CaptureBlockBudget(PenetrationSimulation sim,short index)
    {
        if(writes is not {} list)return;
        for(int i=list.Count-1;i>=0;i--)if(list[i].Sim==sim.Pointer&&list[i].Index==index)
        {
            var w=list[i];
            var fragment=sim.fragments[index];var profile=sim.projectileProfiles[fragment.projectileProfileIndex];
            // ERA or a retained geometry charge can change the speed after WriteFactor. Native overmatch uses that final speed.
            if(profile.obliquityExponent!=1)
            {
                var om=ProtectionFactorTable.Overmatch(fragment.mass,fragment.speed,fragment.diameter,w.PathMm*.001,w.Angle);
                if(w.Module is {} module)
                {
                    var multiplier=ProtectionFactorTable.NativeRhaFactor(1,w.Angle,profile.obliquityExponent,om,profile.apModifier);
                    var refreshed=multiplier>0?CompositeModule.Plan(module.StartM,module.EndM,w.Original/multiplier,w.Threat,module.Layers):null;
                    if(refreshed==null){Restore(sim.Pointer,index);return;}
                    w=w with{Factor=refreshed.Factor,Module=refreshed};
                }
                if(w.Interval is {} intervals)
                {
                    var multiplier=ProtectionFactorTable.NativeRhaFactor(1,w.Angle,profile.obliquityExponent,om,profile.apModifier);
                    var refreshed=multiplier>0?w.Ray!.Ledger.Plan(intervals.StartM,intervals.EndM,
                        intervals.Claims.Select(c=>c.NativeRha is {} native?c with{Factor=native/multiplier}:c),w.PathMm*.001):null;
                    if(refreshed==null){Restore(sim.Pointer,index);return;}
                    w=w with{Factor=refreshed.Factor,Interval=refreshed};
                }
                var value=ProtectionFactorTable.NativeSafe((float)ProtectionFactorTable.NativeRhaFactor(w.Factor,w.Angle,profile.obliquityExponent,om,profile.apModifier)); // 0.22.90 replaced: the floor on the refresh (a re-planned interval can be empty: factor 0)
                if(!float.IsFinite(value)){Restore(sim.Pointer,index);return;} // 0.22.90: DIVISOR-AUDIT: a non-finite refresh is never written; native keeps its own factor (as the guard of WriteFactor)
                SetRha(w.Materials,w.MaterialIndex,value,out var read);
                if(read!=value){Restore(sim.Pointer,index);return;}
                if(w.Extra!=null){SetRha(w.Extra,w.MaterialIndex,value,out read);if(read!=value){Restore(sim.Pointer,index);return;}}
            }
            if(w.Interval is {} interval&&!w.Committed){w.Ray!.Ledger.Commit(interval);w=w with{Committed=true};}
            list[i]=w with{BeforeMm=CurrentBudgetMm(sim,index,out _)};return;
        }
    }
    // The block carries another material while a table plate sits inside it: native charges the member's path at the block's rha.
    private static void Blended(NativeArmourBlockContext reached,Member member,ProtectionThreat threat)
    {
        var sim=reached.Sim;
        if(!Claim(reached,member.Entity))return;
        var angle=AngleOf(reached,member.First);
        if(!double.IsFinite(angle))return;
        var pathMm=member.Hit?Math.Max(0,member.Last.distance-member.First.distance)*1000d:0;
        if(!(pathMm>0)&&NativeHitFace.ReportThickness(sim,member.First,member.Plate) is {} face)pathMm=ProtectionFactorTable.PathMm(face,angle);
        if(!(pathMm>0)){Report(reached,member.Entity,"materialUnavailable",member.Table.Label+": blended member path unavailable; native resistance only.");return;}
        var factor=member.Table.Factor(threat,angle);
        var blockRha=sim.materials[reached.Block.MaterialIndex].rhaFactor;
        var missing=pathMm*Math.Max(0,factor-Math.Max(0,blockRha));
        if(!ApplyPassive(reached,member.Entity,"Blended plate",missing,out var before,out var after))return;
        Note(reached,member.Entity,null,ProtectionLine.Text(member.Table.Label,pathMm,factor,threat,angle)+
            string.Create(System.Globalization.CultureInfo.InvariantCulture,$"; blended block charged at {blockRha:0.##}, +{missing:0} mm RHA by the native law"),before,after,"materialReaction");
    }
    // A (user 02-11): native stopped the plate's block on another component's entry. Add the plate path it did not charge, minus what native
    // charges it as that component.
    private static void Overlap(NativeArmourBlockContext reached,Member member,ProtectionThreat threat,StructureIntersection blockLast)
    {
        var sim=reached.Sim;var hit=blockLast;
        if((hit.flags&IntersectionFlags.Enter)==0||hit.objectIndex==member.ObjectIndex||hit.objectIndex<0||hit.objectIndex>=sim.objects.Length)return;
        if(NativeHitFace.ReportThickness(sim,member.First,member.Plate) is not {} face||!(face>0))return;
        var nativeSpan=(hit.distance-member.First.distance)*1000;
        if(!double.IsFinite(nativeSpan)||nativeSpan<=0)return;
        var angle=AngleOf(reached,member.First);
        if(!double.IsFinite(angle))return;
        var cosine=Math.Cos(angle*Math.PI/180);
        var factor=member.Table.Factor(threat,angle);
        var otherIndex=sim.objects[hit.objectIndex].materialIndex;
        var otherRha=otherIndex>=0&&otherIndex<sim.materials.Length?sim.materials[otherIndex].rhaFactor:0;
        var remaining=CassetteImpactPath.OverlapRemainingMm(face,cosine,nativeSpan);
        var missing=CassetteImpactPath.OverlapMissingMm(face,cosine,nativeSpan,factor,otherRha);
        if(!(missing>0)||!Claim(reached,member.Entity))return;
        var otherEntity=sim.objects[hit.objectIndex].ID;
        if(!ApplyPassive(reached,otherEntity,"Overlapped plate passive",missing,out var before,out var after))return;
        Note(reached,otherEntity,null,CassetteImpactPath.OverlapReason(OtherLabel(sim,otherEntity),nativeSpan,remaining,Math.Max(0,factor-Math.Max(0,otherRha)),missing,Math.Max(0,before-after)),before,after);
    }
    /// <summary>
    /// The end of one SimulateFragment call: the material is restored, then the block's one report line is recorded with the budget native
    /// used and the continuation it left (after the block's speed ratio). Called from the finalizer, so the write never outlives its block.
    /// </summary>
    internal static void Close(PenetrationSimulation sim,short index,int countBefore)
    {
        var write=Restore(sim.Pointer,index);
        if(write is not {} recorded||RuntimeSpall.Impact is not {} impact||!impact.LiveImpact&&!RuntimeArmourComparisonRunner.IsProbing)return;
        try
        {
            double? after=null;var stopped=true;
            for(int i=countBefore;i<sim.FragmentCount;i++)
            {
                var child=sim.fragments[i];
                if(child.parentIndex!=index||(child.flags&FragmentFlag.OriginalPenetrator)==0||(child.flags&FragmentFlag.Killed)!=0)continue;
                after=CurrentBudgetMm(sim,(short)i,out _);stopped=false;break;
            }
            var parent=sim.fragments[index];
            if((parent.flags&FragmentFlag.Deflected)!=0)stopped=false;
            if(recorded.Interval is {} interval)
            {
                RuntimeDamageFeed.RecordPaidIntervals(sim,index,interval,recorded.BeforeMm,stopped?0:after,stopped);
                return;
            }
            if(recorded.Module!=null)return; // Separate module rows are emitted from confirmed intervals.
            RuntimeDamageFeed.RecordArmourInteraction(sim,index,stopped?"materialStop":"materialReaction",ProtectionLine.Text(recorded.Label,recorded.PathMm,recorded.Factor,recorded.Threat,recorded.Angle),recorded.BeforeMm,stopped?0:after,recorded.Entity,null);
        }
        catch(Exception){/* The report line never changes the impact. */}
    }
    private static FactorWrite? Restore(IntPtr sim,short index)
    {
        if(writes is not {Count:>0} list)return null;
        FactorWrite? found=null;
        for(int i=list.Count-1;i>=0;i--)
        {
            var w=list[i];
            if(w.Sim!=sim||w.Index!=index)continue;
            list.RemoveAt(i);found??=w;
            var primary=new NativeRhaArray(w.Materials);var extra=new NativeRhaArray(w.Extra??w.Materials);
            MaterialFactorWrite.RestorePair(ref primary,ref extra,w.Extra!=null,w.MaterialIndex,w.Original,w.ExtraOriginal,
                ex=>{if(writeWarnings++<4)Plugin.ModLog.LogWarning("[Protection] Restore failed: "+ex.Message);});
        }
        return found;
    }
    /// <summary>Sets the rhaFactor of one MaterialInfo element (a copy until written back), reads it back, and returns the value it had.</summary>
    private readonly struct NativeRhaArray:IRhaArray
    {
        private readonly Il2CppStructArray<MaterialInfo> values;
        internal NativeRhaArray(Il2CppStructArray<MaterialInfo> values)=>this.values=values;
        public float Read(int index)=>values[index].rhaFactor;
        public void Write(int index,float value)
        {
            var element=values[index];
            Unsafe.AsRef(in element.rhaFactor)=value;
            values[index]=element;
        }
    }
    private static float SetRha(Il2CppStructArray<MaterialInfo> materials,int index,float value,out float readBack)
    {
        var array=new NativeRhaArray(materials);
        return MaterialFactorWrite.Set(ref array,index,value,out readBack);
    }
    private static bool Claim(NativeArmourBlockContext reached,int entity)
    {
        var state=impacts.GetOrCreateValue(reached.Impact);
        return CassetteImpactPath.Claim(state.Awarded,reached.Sim.Pointer,LineageRoot(reached.Sim,reached.Index),entity);
    }
    private static short LineageRoot(PenetrationSimulation sim,short index)
    {
        var root=index;
        if(index<0||index>=sim.FragmentCount)return index;
        if((sim.fragments[root].flags&FragmentFlag.OriginalPenetrator)==0)return root;
        for(int step=0;step<128;step++)
        {
            var parent=sim.fragments[root].parentIndex;
            if(parent<0||parent>=root||(sim.fragments[parent].flags&FragmentFlag.OriginalPenetrator)==0)break;
            root=parent;
        }
        return root;
    }
    // B: the air block's exit boundary is the front entry of the plate native is about to miss. Remember it, per line, before the next solid block.
    private static void RememberAirGap(NativeArmourBlockContext reached,StructureIntersection first,StructureIntersection last,int entry,int exit)
    {
        var sim=reached.Sim;var hit=last;
        if((hit.flags&IntersectionFlags.Enter)==0)
        {
            hit=first;
            for(int i=0;i<reached.IntersectionCount&&i<reached.Intersections.Length;i++)
            {
                var candidate=reached.Intersections[i];
                if((candidate.flags&IntersectionFlags.Enter)==0||candidate.objectIndex==first.objectIndex)continue;
                if(!double.IsFinite(candidate.distance)||candidate.distance<first.distance||candidate.distance>last.distance)continue;
                if(!AirGapCassette(candidate.objectIndex,reached,out _,out _))continue;
                hit=candidate;break;
            }
        }
        if(hit.objectIndex<0||hit.objectIndex>=sim.objects.Length)return;
        if(!AirGapCassette(hit.objectIndex,reached,out var plate,out _))return;
        var material=plate.armourTechID;
        if(!CompositePlate.IsCassette(material)||!RuntimeCompositePlate.Is(plate))return;
        var root=LineageRoot(sim,reached.Index);
        if(pendingPlates.ContainsKey((sim.Pointer,root))||pendingPlates.Count>=1024)return;
        var state=impacts.GetOrCreateValue(reached.Impact);
        if(state.Awarded.Contains((sim.Pointer,root,sim.objects[hit.objectIndex].ID,CassetteImpactPath.AwardFace)))return;
        var gap=Math.Abs(last.distance-first.distance)*1000;
        if(!double.IsFinite(gap)||gap<0)gap=0;
        var fragment=sim.fragments[reached.Index];
        var point=sim.SimToWorldSpacePoint(fragment.spawnPoint+fragment.direction.normalized*hit.distance);
        pendingPlates[(sim.Pointer,root)]=new(sim.Pointer,root,sim.objects[hit.objectIndex].ID,hit.objectIndex,gap,$"{sim.Pointer}:{reached.Index}:{entry}:{exit}",new(point.x,point.y,point.z));
    }
    private static bool AirGapCassette(int objectIndex,NativeArmourBlockContext reached,out PlateStructure plate,out long spawn)
    {
        plate=null!;spawn=0;
        var sim=reached.Sim;
        if(objectIndex<0||objectIndex>=sim.objects.Length)return false;
        if(!RuntimeArmourResponses.TryResolvePlate(reached.Impact,sim,sim.objects[objectIndex].ID,out var component,out spawn,out _))return false;
        if(component.TryCast<PlateStructure>() is not {} found)return false;
        plate=found;return true;
    }
    private static void ResolvePendingPlate(NativeArmourBlockContext reached)
    {
        var sim=reached.Sim;var block=reached.Block;
        if(block.MaterialIndex<0||block.MaterialIndex>=sim.materials.Length)return;
        var root=LineageRoot(sim,reached.Index);
        if(!pendingPlates.TryGetValue((sim.Pointer,root),out var pending))return;
        var entry=block.EnterIntersectionIndex;
        if(entry<0||entry>=reached.IntersectionCount||entry>=reached.Intersections.Length)return;
        var first=reached.Intersections[entry];
        var state=impacts.GetOrCreateValue(reached.Impact);
        var claimed=state.Awarded.Contains((sim.Pointer,root,pending.Entity,CassetteImpactPath.AwardFace));
        var same=first.objectIndex==pending.ObjectIndex||first.objectIndex>=0&&first.objectIndex<sim.objects.Length&&sim.objects[first.objectIndex].ID==pending.Entity;
        switch(CassetteImpactPath.AirGap(true,claimed,true,same))
        {
            case CassetteImpactPath.AirGapOutcome.SamePlate:
            case CassetteImpactPath.AirGapOutcome.Claimed:
                pendingPlates.Remove((sim.Pointer,root));return;
            case CassetteImpactPath.AirGapOutcome.Skipped:
                pendingPlates.Remove((sim.Pointer,root));
                if(!CassetteImpactPath.Claim(state.Awarded,sim.Pointer,root,pending.Entity))
                {Report(reached,pending.Entity,"materialUnavailable","Cassette plate #"+pending.Entity+": award table full; native resistance only.",hitId:pending.HitId);return;}
                ApplySkippedPlate(reached,pending);return;
        }
    }
    // The plate native skipped is paid from its drawn face: face thickness / cos θ × factor(threat, θ), by the native law.
    private static void ApplySkippedPlate(NativeArmourBlockContext reached,PendingPlate pending)
    {
        var sim=reached.Sim;var fragment=sim.fragments[reached.Index];
        if((fragment.flags&(FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator)return;
        if(!RuntimeArmourResponses.TryResolvePlate(reached.Impact,sim,pending.Entity,out var component,out var spawn,out _)||component.TryCast<PlateStructure>() is not {} plate)
        {Report(reached,pending.Entity,"materialUnavailable","Skipped composite plate #"+pending.Entity+": exact plate binding unavailable; native resistance only.",hitId:pending.HitId);return;}
        var material=plate.armourTechID;
        if(!Usable(reached,plate,spawn,pending.Entity,material,reached.Impact.ArmourSnapshot?.Protection,out var table)||table==null)return;
        var worldDirection=sim.SimToWorldSpaceDirection(fragment.direction.normalized).normalized;
        if(!NativeCassetteGeometry.TryWorldSide(plate,worldDirection,out var worldHit,out var front,out var alignment)||
            !NativeHitFace.TryAuthoredFaceThickness(plate,worldHit.FaceIndex,out var face)||!(face>0))
        {Report(reached,pending.Entity,"materialUnavailable",$"Skipped composite plate #{pending.Entity}: drawn face unreadable; native resistance only.",hitId:pending.HitId);return;}
        var angle=Math.Acos(Math.Clamp(alignment,0,1))*180/Math.PI;
        var threat=ProtectionFactorTable.ThreatOf(Threats(reached.Impact,true));
        var factor=threat is {} t?table.Factor(t,angle):table.PassiveRha;
        var path=ProtectionFactorTable.PathMm(face,angle);
        if(ApplyPassive(reached,pending.Entity,"Skipped plate",path*factor,out var before,out var after))
            Note(reached,pending.Entity,pending.HitId,(threat is {} shown?ProtectionLine.Text(table.Label,path,factor,shown,angle):table.Label+": passive only")+"; "+CassetteImpactPath.SkipReason(pending.GapMm),before,after,"materialReaction");
    }
    internal static (int Entity,string HitId)[] ClosePending()
    {
        var list=new (int,string)[pendingPlates.Count];var i=0;
        foreach(var p in pendingPlates.Values)list[i++]=(p.Entity,p.HitId);
        pendingPlates.Clear();
        return list;
    }
    private static double CurrentBudgetMm(PenetrationSimulation sim,short index,out ushort constant)
    {
        constant=0;
        if(index<0||index>=sim.FragmentCount)return 0;
        var fragment=sim.fragments[index];
        if(fragment.projectileProfileIndex<0||fragment.projectileProfileIndex>=sim.projectileProfiles.Length)return 0;
        constant=sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant;
        var value=fragment.GetBasePenetration(constant)*1000;
        return double.IsFinite(value)&&value>0?value:0;
    }
    // A resistance native never charged, applied to the current budget by the native law P (1 - L/P)^0.715 (or a stop).
    private static bool ApplyPassive(NativeArmourBlockContext reached,int entity,string label,double resistance,out double beforeMm,out double afterMm)
    {
        beforeMm=0;afterMm=0;
        if(!(resistance>0))return false;
        var sim=reached.Sim;
        var before=CurrentBudgetMm(sim,reached.Index,out var constant);
        if(!(before>0))return false;
        var target=CassetteImpactPath.NativePassive(before,resistance);
        if(!(target<before))return false;
        beforeMm=before;
        return WriteBudget(reached,entity,label,constant,before,target,out afterMm);
    }
    private static bool WriteBudget(NativeArmourBlockContext reached,int entity,string label,ushort constant,double beforeMm,double targetMm,out double afterMm)
    {
        afterMm=0;var sim=reached.Sim;var fragment=sim.fragments[reached.Index];var beforeSpeed=fragment.speed;
        if(targetMm<=0){fragment.speed=0;fragment.flags|=FragmentFlag.Killed;}
        else
        {
            var speed=PenetrationUtils.ComputeRequiredPenetrationSpeed(fragment.diameter*1000,fragment.mass,(float)targetMm,constant);
            if(!float.IsFinite(speed)||speed<=0||speed>beforeSpeed)
            {Report(reached,entity,"materialUnavailable",$"{label}: native speed conversion unavailable; native resistance only.");return false;}
            fragment.speed=speed;
        }
        afterMm=targetMm<=0?0:fragment.GetBasePenetration(constant)*1000;
        if(!double.IsFinite(afterMm)||afterMm<0||afterMm>beforeMm+Math.Max(.01,beforeMm*.002))
        {Report(reached,entity,"materialUnavailable",$"{label}: native round trip invalid; native resistance only.");return false;}
        sim.fragments[reached.Index]=fragment;
        reached.RecordSpeedReduction(beforeSpeed,fragment.speed);
        return true;
    }
    private static string OtherLabel(PenetrationSimulation sim,int entity)
    {
        string label;
        try{label=RuntimeDamageFeed.EraReportLabel(sim,entity);}catch{label="Component";}
        if(string.IsNullOrWhiteSpace(label))label="Component";
        var marker="#"+entity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return label.Contains(marker,StringComparison.Ordinal)?label:label+" "+marker;
    }
    private static void Note(NativeArmourBlockContext reached,int entity,string? hitId,string text,double? beforeMm=null,double? afterMm=null,string kind="materialNote")
    {
        if(!reached.Impact.LiveImpact&&!RuntimeArmourComparisonRunner.IsProbing)return;
        if((reached.Sim.fragments[reached.Index].flags&FragmentFlag.OriginalPenetrator)==0)return;
        try{RuntimeDamageFeed.RecordArmourInteraction(reached.Sim,reached.Index,kind,text,beforeMm,afterMm,entity,hitId);}
        catch(Exception){/* The native impact is never changed by a report note. */}
    }
    private static void CassetteUnavailable(NativeArmourBlockContext reached,long spawn,int entity,string material)
    {
        const string reason="Technology file or cassette model missing/not loaded; native resistance only.";
        if(warned.Add((spawn,entity)))Plugin.ModLog.LogWarning("[Cassettes] Plate #"+entity+" "+material+": "+reason);
        Report(reached,entity,"materialUnavailable","Cassette "+material+": "+reason);
    }
    // Same date source as the composer: the owning vehicle's native TechDate and its native technology frame. The Coldwar3 sentinel allows all.
    private static bool CassetteUsable(PlateStructure plate,long spawn,int entity,string material,CassetteModel cassette,out string reason)
    {
        int? date=null;try{date=CassetteComposerPanel.VehicleDateKey(plate);}catch(Exception){}
        bool? inFrame=null;try{if(plate.Vehicle?.Tech is {} tech)inFrame=tech.TryGetTech(material,out _);}catch(Exception){}
        reason=CassetteAvailability.HitBlockReason(cassette.Preset.Date,date,inFrame)??"";
        if(reason.Length>0&&CassetteAvailability.Key(cassette.Preset.Date) is {} need&&date is {} vehicle&&vehicle!=CassetteAvailability.Sentinel&&need>vehicle)
            reason=$"{cassette.Preset.Label} exists from {cassette.Preset.Date}; this vehicle is dated {CassetteAvailability.Text(vehicle)}; native resistance only.";
        if(reason.Length==0)return true;
        if(warned.Add((spawn,entity)))Plugin.ModLog.LogWarning("[Cassettes] Plate #"+entity+" "+material+": "+reason);
        return false;
    }
    private static void Report(NativeArmourBlockContext reached,int entity,string kind,string label,int? relatedEntity=null,string? hitId=null)
    {
        if(!reached.Impact.LiveImpact&&!RuntimeArmourComparisonRunner.IsProbing||(reached.Sim.fragments[reached.Index].flags&FragmentFlag.OriginalPenetrator)==0)return;
        var state=impacts.GetOrCreateValue(reached.Impact);
        if(state.Reported.Count>=128||!state.Reported.Add((reached.Sim.Pointer,reached.Index,entity,kind,label)))return;
        RuntimeDamageFeed.RecordArmourInteraction(reached.Sim,reached.Index,kind,label,null,null,relatedEntity,hitId);
    }
    internal static MaterialThreatTags Threats(ShellImpactContext context,bool original)=>
        new(context.Behavior,context.ThreatClass,context.Profile?.ArmourThreatTags,context.Geometry?.LengthToDiameter,original,context.Profile?.EffectiveKineticKind);
}
