using BepInEx;
using SprocketExpandedBallistics.Era;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.TechTrees;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

[HarmonyPatch]
internal static class RuntimeArmourResponses
{
    private sealed record Plate(VehicleComponent Component,long Spawn,IntPtr Root,bool Placed=false,EraPartBinding? Binding=null);
    private sealed class Shot
    {
        internal readonly ArmourShotState State=new();
        internal readonly EraCells Preview=new();
        internal (string Material,double Thickness,double Angle,Vector3 Exit,ArmourHitKey Key)? Pending;
        internal Vector3? SteelExit;
        internal bool PlacedHit;
        // 0.22.51: the housing-only statement is decided once per housing, at its first crossing in this shot, while the
        // registered trace still holds the module's interior. A later wall of the same housing never repeats or contradicts it.
        internal readonly HashSet<int> HousingsEvaluated=new();
    }
    private sealed record Scope(PenetrationSimulation Sim,short Index,CompoundStructure Structure,Shot Shot,int CountBefore,float SpeedBefore)
    {
        internal float ResidualSpeedRatio=1;internal bool Absorbed;
        // Computed once per shot in Begin: the ERA threat bucket, the gun calibre
        // and the calibre trigger. Never re-evaluated per block.
        internal string Threat="fragment";internal double CalibreMm;internal bool EraTrigger;
        internal bool Hesh;internal double HeshAngle=double.NaN; // 0.22.78 HESH
        internal bool He,HeArmour;internal double HePathMm=double.NaN,HeRha=double.NaN;internal string? HeHit; // 0.22.86 HE
        internal double HeNextMm=double.PositiveInfinity; // 0.22.89: distance from the exit of the perforated block to the next armour of this vehicle (skirt rule)
    }
    internal static SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmourCatalogue? Experimental {get;private set;}
    private static ArmourResponseCatalogue? catalogue;
    internal static ArmourCatalogueSnapshot Snapshot {get;private set;}=ArmourCatalogueSnapshot.Empty;
    private static EraPartBindingCatalogue bindings=EraPartBindings.Parse("{\"schemaVersion\":1,\"matchMode\":\"componentId\",\"bindings\":[]}");
    private static bool bindingsStarted;
    private static readonly Dictionary<IntPtr,long> spawns=new();
    // Placed ERA collision-mesh depth is immutable per part instance. Cleared on
    // vehicle release, model release and editor reset; mesh pointer covers redesign.
    private static readonly PlacedDepthCache<(IntPtr Model,IntPtr Mesh,long Spawn)> placedDepth=new();
    private static readonly EraCells combat=new();
    private static readonly PlacedEraState placedState=new();
    private static long generation;
    private static readonly HashSet<string> warnings=new();
    private static int diagnostics;
    private static int cellDiagnostics;
    private static int stackTraces;
    private static int placedDiagnostics;
    internal static void PlacedTrace(ref PlacedEraTraceHandler detail)=>PlacedEraTrace.Write(ref detail);
    internal static Action<NativeArmourBlockContext>? ReachedBlock;
    private static int mainThread;
    [ThreadStatic] private static Scope? active;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ShellImpactContext,Dictionary<IntPtr,Shot>> shots=new();
    internal static bool Configure()
    {
        mainThread=Environment.CurrentManagedThreadId;
        PlacedEraTrace.Enabled=()=>ReleaseDiagnostics.Enabled;
        PlacedEraTrace.Live=()=>RuntimeSpall.Impact?.LiveImpact==true;
        PlacedEraTrace.Sink=message=>Plugin.ModLog.LogInfo(message);
        // Publish only after both candidate files validate. Existing flights retain their pair.
        var responsePath=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.responses.json");
        var experimentalPath=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.experimental.json");
        var candidate=ArmourCatalogueSnapshot.Read(()=>File.Exists(responsePath)?File.ReadAllText(responsePath):null,
            ()=>RodResponseDefaults.Read(Paths.ConfigPath),ReferenceEquals(Snapshot,ArmourCatalogueSnapshot.Empty),()=>MaterialReactionDefaults.Read(Paths.ConfigPath));
        if(candidate.MaterialReactions is {Enabled:true} reactions)
        {
            try {var cassettes=CassetteDefaults.Load(reactions,candidate.Catalogue);candidate=candidate with {Cassettes=cassettes.Models,CassetteJson=cassettes.Json,Protection=cassettes.Tables};}
            catch(Exception ex)
            {
                if(!ReferenceEquals(Snapshot,ArmourCatalogueSnapshot.Empty))throw new FormatException("Cassette reload rejected; previous snapshot retained. "+ex.Message,ex);
                Plugin.ModLog.LogWarning("[Cassettes] Native passive resistance only: "+ex.Message);
            }
        }

        if(candidate.Protection is {} existingTables)
        {
            var tables=existingTables.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.Ordinal);
            tables[CompositeModule.MaterialId]=ProtectionFactorTable.BuildModule(CompositeModuleEraGeometry.Effective(CompositeModule.Current));
            candidate=candidate with{Protection=tables};
        }
        if(!bindingsStarted)
        {
            bindingsStarted=true;
            try{bindings=EraPartBindings.Parse(File.ReadAllText(Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.era.bindings.json")));if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Placed ERA] Immutable exact binding snapshot: {bindings.Bindings.Count} cassettes.");}
            catch(Exception ex){Plugin.ModLog.LogWarning("[Placed ERA] Bindings unavailable; placed active classification disabled, oldplate adapter retained: "+ex.Message);}
        }
        if(CompositeModule.Current.Era.Enabled&&!bindings.TryGetCassette(CompositeModule.ComponentId,out _))
            bindings=new EraPartBindingCatalogue(bindings.Bindings.Append(CompositeModule.Current.Era.Binding).ToList());
        bindings=EraWtVariants.With(bindings); // 0.22.73: exact bindings of the four Kontakt-5 War Thunder variants (the config file is never rewritten)
        bindings=EraStageCellDefaults.With(bindings); // 0.22.96: built-in stage-cell/experimental bindings only fill gaps; the config file is never rewritten and a user entry wins
        Snapshot=candidate with{Bindings=bindings};catalogue=Snapshot.Catalogue;Experimental=Snapshot.Experimental;
        if(ArmourResponses.LegacyMinimumEraUpgraded)
        {
            ArmourResponses.ClearLegacyMinimumEraFlag();
            Plugin.ModLog.LogWarning("[Armour responses] Legacy minimumEra upgraded to minimumDate 1945.09.03; run the 0.22.9 installer to persist the migration.");
        }
        if(Snapshot.ExperimentalError is {} experimentalError)
            Plugin.ModLog.LogWarning("[Dynamic ERA] Invalid sidecar; experimental KE disabled, file preserved and legacy responses retained: "+experimentalError);
        if(Experimental is {} experimental)
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Dynamic ERA] model={experimental.ParsedModelVersion} enabled={experimental.Enabled} recipes={experimental.Recipes.Count} revision={experimental.Revision}; finite physical-cell response.");
        if(catalogue==null)
        {
            if(Snapshot.Error is {} error)Plugin.ModLog.LogWarning("[Armour response] Invalid catalogue; adapter disabled and file preserved: "+error);
            else if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Armour response] Catalogue absent; native behaviour retained.");
            return Snapshot.MaterialReactions?.Enabled==true;
        }
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Armour response] enabled={catalogue.Enabled}; exact component VUIDs, scalar response only.");
        return catalogue.Enabled||Snapshot.MaterialReactions?.Enabled==true;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObject),nameof(VehicleObject.Release))]
    private static void Remove(VehicleObject __instance)
    {
        if(Environment.CurrentManagedThreadId!=mainThread)return;
        placedDepth.Clear();
        if(!spawns.Remove(__instance.Pointer,out var spawn))return;
        combat.RemoveSpawn(spawn);placedState.RemoveSpawn(spawn);NativeMaterialReactions.RemoveSpawn(spawn);
    }
    [HarmonyPrefix,HarmonyPatch(typeof(PlateStructure),nameof(PlateStructure.Release))]
    private static void RemovePlate(PlateStructure __instance)
    {
        if(Environment.CurrentManagedThreadId!=mainThread)return;
        if(__instance.VehicleRoot is {} cellRoot&&spawns.TryGetValue(cellRoot.Pointer,out var cellSpawn))combat.RemoveElement(cellSpawn,__instance.VUID.Value);
    }
    internal static bool PlacedSpent(IntPtr root,int vuid)=>Environment.CurrentManagedThreadId==mainThread&&spawns.TryGetValue(root,out var spawn)&&(placedState.Spent(combat,spawn,vuid)||combat.Any(spawn,vuid));
    internal static bool CurrentPlaced(PlacedEraActivation e)=>spawns.TryGetValue(e.VehicleRootPointer,out var spawn)&&spawn==e.Spawn
        &&(placedState.Spent(combat,spawn,e.ComponentVuid)||placedState.Spent(combat,spawn,e.ComponentVuid,e.Surface,e.CellX,e.CellY));
    internal static EraCell[] PlacedCells(IntPtr root,int vuid)=>Environment.CurrentManagedThreadId==mainThread&&spawns.TryGetValue(root,out var spawn)
        ?placedState.SpentCells(combat,spawn,vuid):Array.Empty<EraCell>();
    internal static long PlacedCellsVersion(IntPtr root,int vuid)=>Environment.CurrentManagedThreadId==mainThread&&spawns.TryGetValue(root,out var spawn)?combat.Version(spawn,vuid):0;
    internal static void ResetPlacedForEdit()
    {if(Environment.CurrentManagedThreadId==mainThread){placedDepth.Clear();placedState.Reset(combat);combat.Clear();NativeMaterialReactions.Reset();}}
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void RemoveModel(VehicleObjectModel __instance)
    {
        if(Environment.CurrentManagedThreadId!=mainThread||!bindings.TryGetCassette(__instance.ComponentID,out _))return;
        placedDepth.Clear();
        if(__instance.VehicleRoot is {} root&&spawns.TryGetValue(root.Pointer,out var spawn))
        {combat.RemoveElement(spawn,__instance.VUID.Value);placedState.RemoveElement(spawn,__instance.VUID.Value);}
    }
    private static Plate? PlacedModel(PenetrationSimulation sim,int entity,bool live)
    {
        VehicleObjectModel? model=null;
        if(live)
        {
            var health=sim.damageApplier?.TryCast<VehicleHealthRegister>();
            if(health?.objectReader is {} reader&&reader.TryGet<VehicleObjectModel>(new VUID(entity),out var target))model=target;
        }
        else
        {
            var reader=RuntimeArmourSimulator.CurrentOwner?.ObjectReader;
            var items=reader?.Items;
            if(items!=null)for(var i=0;i<reader!.Count;i++)
            {
                var item=items[i];if(item?.behaviours is not {} components)continue;
                for(var j=0;j<components.Length;j++)
                {
                    var candidate=components[j]?.TryCast<VehicleObjectModel>();
                    if(candidate==null||candidate.VUID.Value!=entity||!bindings.TryGetCassette(candidate.ComponentID,out _))continue;
                    if(model!=null&&model.Pointer!=candidate.Pointer)return null;
                    model=candidate;
                }
            }
        }
        if(model==null||model.VUID.Value!=entity||!bindings.TryGetCassette(model.ComponentID,out var binding)||model.VehicleRoot is not {} root)return null;
        if(!spawns.TryGetValue(root.Pointer,out var spawn))spawns[root.Pointer]=spawn=++generation;
        if(placedDiagnostics++<24)if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Placed ERA identity] live={live} objectInfoID={entity} modelVUID={model.VUID.Value} component={model.ComponentID} root={root.Pointer} spawn={spawn}; exact target-local model resolved.");
        return new(model,spawn,root.Pointer,true,binding);
    }
    internal static VehicleObjectModel? ResolveCompositeModule(ShellImpactContext context,PenetrationSimulation sim,int entity)
    {
        VehicleObjectModel? exact=null;
        if(context.LiveImpact)
        {
            var health=sim.damageApplier?.TryCast<VehicleHealthRegister>();
            if(health?.objectReader is {} reader&&reader.TryGet<VehicleObjectModel>(new VUID(entity),out var found))exact=found;
        }
        else
        {
            var reader=RuntimeArmourSimulator.CurrentOwner?.ObjectReader;var items=reader?.Items;
            if(items!=null)for(int i=0;i<reader!.Count;i++)
            {
                var components=items[i]?.behaviours;if(components==null)continue;
                for(int j=0;j<components.Length;j++)
                {
                    var found=components[j]?.TryCast<VehicleObjectModel>();
                    if(found?.VUID.Value!=entity||found.ComponentID!=CompositeModule.ComponentId)continue;
                    if(exact!=null&&exact.Pointer!=found.Pointer)return null;exact=found;
                }
            }
        }
        return exact?.VUID.Value==entity&&exact.ComponentID==CompositeModule.ComponentId?exact:null;
    }
    internal static bool TryResolvePlate(ShellImpactContext context,PenetrationSimulation sim,int entity,
        out VehicleComponent component,out long spawn,out IntPtr rootPointer)
    {
        component=null!;spawn=0;rootPointer=IntPtr.Zero;
        PlateStructure? exact=null;
        if(context.LiveImpact)
        {
            var health=sim.damageApplier?.TryCast<VehicleHealthRegister>();
            if(health?.objectReader is {} reader&&reader.TryGet<PlateStructure>(new VUID(entity),out var found))exact=found;
        }
        else
        {
            var reader=RuntimeArmourSimulator.CurrentOwner?.ObjectReader;var items=reader?.Items;
            if(items!=null)for(int i=0;i<reader!.Count;i++)
            {
                var components=items[i]?.behaviours;if(components==null)continue;
                for(int j=0;j<components.Length;j++)
                {
                    var found=components[j]?.TryCast<PlateStructure>();
                    if(found?.VUID.Value!=entity)continue;
                    if(exact!=null&&exact.Pointer!=found.Pointer)return false;
                    exact=found;
                }
            }
        }
        if(exact==null||exact.VUID.Value!=entity||exact.VehicleRoot is not {} root)return false;
        rootPointer=root.Pointer;if(!spawns.TryGetValue(rootPointer,out spawn))spawns[rootPointer]=spawn=++generation;
        component=exact;return true;
    }
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void Begin(CompoundStructure __instance,PenetrationSimulation sim,short index,out Scope? __state)
    {
        __state=active;active=null;
        if(Environment.CurrentManagedThreadId!=mainThread){return;}
        var context=RuntimeSpall.Impact;
        if(context==null||index<0||index>=sim.FragmentCount){return;}
        // Spall fragments have no ERA, preconditioning or protection-table work.
        if((sim.fragments[index].flags&FragmentFlag.OriginalPenetrator)==0)return;
        var map=shots.GetOrCreateValue(context);
        if(!map.TryGetValue(sim.Pointer,out var shot))map[sim.Pointer]=shot=new();
        active=new(sim,index,__instance,shot,sim.FragmentCount,sim.fragments[index].speed);
        var fragment=sim.fragments[index];
        var tags=NativeMaterialReactions.Threats(context,(fragment.flags&FragmentFlag.OriginalPenetrator)!=0);
        active.Threat=ArmourResponseModel.EraThreat(tags);
        // Mod profiles use the gun bore; a native full-bore round keeps the
        // fragment diameter that the game itself reports.
        active.CalibreMm=context.Profile!=null&&context.GunDiameter>0
            ?context.GunDiameter*1000
            :fragment.diameter*1000;
        var config=context.ArmourSnapshot is {} captured?captured.Catalogue:catalogue;
        active.EraTrigger=ArmourResponseModel.EraTriggerCalibre(active.CalibreMm,config?.MinimumTriggerCalibreMm??20);
        // 0.22.78 HESH begin
        if(context.Behavior=="hesh")HeshBegin(active,context);
        // 0.22.78 HESH end
        // 0.22.86 HE begin
        if(context.Behavior=="he"&&context.LiveImpact&&RuntimeSpall.He.Perforation)active.He=true;
        // 0.22.86 HE end
    }
    // GetNextMaterialBlock runs after native has cached the parent's kinetic
    // energy. Chemical gap losses must use the same aggregate continuation
    // correction as ERA; replacing the ratio avoids charging either loss twice.
    internal static void NotifyParentSpeedChange(PenetrationSimulation sim,short index)
    {
        var scope=active;
        if(scope==null||scope.Sim.Pointer!=sim.Pointer||scope.Index!=index||index<0||index>=sim.FragmentCount||scope.SpeedBefore<=0)return;
        var speed=sim.fragments[index].speed;
        if(!float.IsFinite(speed)||speed<0||speed>scope.SpeedBefore)return;
        scope.ResidualSpeedRatio=Math.Clamp(speed/scope.SpeedBefore,0,1);
        if(speed==0)scope.Absorbed=true;
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    private static void End(Scope? __state)
    {
        var current=active;
        try
        {
            if(current is {} observed&&observed.Shot.PlacedHit&&observed.Index>=0&&observed.Index<observed.Sim.FragmentCount)
            {
                try
                {
                var final=observed.Sim.fragments[observed.Index];
                var count=0;
                for(var j=observed.CountBefore;j<observed.Sim.FragmentCount;j++)
                {var child=observed.Sim.fragments[j];if(child.parentIndex==observed.Index&&(child.flags&FragmentFlag.OriginalPenetrator)!=0&&(child.flags&FragmentFlag.Killed)==0)count++;}
                if(ReleaseDiagnostics.Enabled)PlacedTrace($"stage=native-fragment-end flags={final.flags} nativeKilled={(final.flags&FragmentFlag.Killed)!=0} originalContinuations={count} residualBudget={final.GetBasePenetration(observed.Sim.projectileProfiles[final.projectileProfileIndex].penetratorConstant)*1000:0.000}mm; status of this fragment only, not a proven full vehicle/ERA stop");
                }
                catch(Exception ex){if(ReleaseDiagnostics.Enabled)PlacedTrace($"stage=native-fragment-trace-unavailable {ex.Message}");}
            }
            if(current is not {ResidualSpeedRatio:<1})return;
            // Native caches parent KE before GetNextMaterialBlock. Its new original
            // continuation speed must carry the same energy reduction as the array.
            for(var i=current.CountBefore;i<current.Sim.FragmentCount;i++)
            {
                var child=current.Sim.fragments[i];
                if(child.parentIndex!=current.Index||(child.flags&FragmentFlag.OriginalPenetrator)==0)continue;
                child.speed=ArmourResponseModel.ResidualSpeed(child.speed,current.ResidualSpeedRatio);
                if(current.Absorbed){child.speed=0;child.flags|=FragmentFlag.Killed;}
                current.Sim.fragments[i]=child;
            }
        }
        catch(Exception ex){if(warnings.Count<32&&warnings.Add("energy:"+ex.Message))Plugin.ModLog.LogWarning("[Armour response] Residual correction unavailable: "+ex.Message);}
        finally
        {
            // 0.22.78 HESH begin
            if(current is {Hesh:true})HeshSurface(current);
            // 0.22.78 HESH end
            // 0.22.86 HE begin
            if(current is {He:true})HeEnd(current);
            // 0.22.86 HE end
            // 0.22.28: the block is done, so a protection-table rhaFactor is restored here (and its report line recorded).
            try{if(current!=null)NativeMaterialReactions.Close(current.Sim,current.Index,current.CountBefore);}
            catch(Exception ex){if(warnings.Count<32&&warnings.Add("restore:"+ex.Message))Plugin.ModLog.LogWarning("[Protection] Restore unavailable: "+ex.Message);}
            finally{active=__state;}
        }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(StructureIntersection),nameof(StructureIntersection.GetNextMaterialBlock))]
    [HarmonyPriority(Priority.Last)]
    private static void Layer(MaterialBlock __result,Il2CppStructArray<StructureIntersection> __0,int __1,Il2CppStructArray<MaterialInfo> __5)
    {
        var scope=active;var impact=RuntimeSpall.Impact;
        // 0.22.78 HESH begin
        if(scope is {Hesh:true}&&__result.Valid&&__result.MaterialIndex>=0&&RuntimeSpall.Hesh.SurfaceDetonation)HeshAngle(scope,__result,__0,__1);
        // 0.22.78 HESH end
        // 0.22.86 HE begin
        if(scope is {He:true}&&__result.Valid&&__result.MaterialIndex>=0)HeBlock(scope,__result,__0,__1,__5);
        // 0.22.86 HE end
        if(scope!=null&&impact!=null&&__result.Valid)
        {
            try{ReachedBlock?.Invoke(new(scope.Sim,scope.Index,scope.Structure,__result,__0,__1,impact,__5));}
            catch(Exception ex){if(warnings.Count<32&&warnings.Add("material:"+ex.Message))Plugin.ModLog.LogWarning("[Material reaction] Reached block unavailable: "+ex.Message);}
        }
        try{Apply(__result,__0,__1);}catch(Exception ex){if(warnings.Count<32&&warnings.Add(ex.Message))Plugin.ModLog.LogWarning("[Armour response] Hit skipped: "+ex.Message);}
        try{if(scope!=null&&impact!=null)ReportHousingPath(scope,impact,__result,__0,__1);}
        catch(Exception ex){if(warnings.Count<32&&warnings.Add("housing:"+ex.Message))Plugin.ModLog.LogWarning("[Armour response] Housing observation unavailable: "+ex.Message);}
        try{if(scope!=null)NativeMaterialReactions.CaptureBlockBudget(scope.Sim,scope.Index);}
        catch(Exception ex){if(warnings.Count<32&&warnings.Add("factor:"+ex.Message))Plugin.ModLog.LogWarning("[Protection] Final budget capture unavailable: "+ex.Message);}
    }
    private static void ObserveEra(Scope scope,string material,string kind,string reason,int? entity=null)
    {
        // Observer only: report owns per-stage deduplication and bounded event storage.
        // Never let optional UI bookkeeping change native impact or cell accounting.
        try
        {
            RuntimeDamageFeed.RecordArmourInteraction(scope.Sim,scope.Index,kind,EraRejectionReport.Reason(reason),relatedEntity:entity);
        }
        catch { }
    }
    private static void ReportHousingPath(Scope scope,ShellImpactContext context,MaterialBlock block,Il2CppStructArray<StructureIntersection> hits,int count)
    {
        if(!block.Valid||block.MaterialIndex<0||(scope.Sim.fragments[scope.Index].flags&FragmentFlag.OriginalPenetrator)==0||
            block.EnterIntersectionIndex<0||block.ExitIntersectionIndex<0||block.EnterIntersectionIndex>=count||block.ExitIntersectionIndex>=count||
            block.EnterIntersectionIndex>=hits.Length||block.ExitIntersectionIndex>=hits.Length)return;
        var sim=scope.Sim;var first=hits[block.EnterIntersectionIndex];var last=hits[block.ExitIntersectionIndex];
        if(first.objectIndex<0||first.objectIndex>=sim.objects.Length)return;
        VehicleComponent? Resolve(int entity)
        {
            if(context.LiveImpact)
            {
                var reader=sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
                return reader!=null&&reader.TryGet<VehicleComponent>(new VUID(entity),out var found)&&found?.VUID.Value==entity?found:null;
            }
            var preview=RuntimeArmourSimulator.CurrentOwner?.ObjectReader;var items=preview?.Items;VehicleComponent? exact=null;
            if(items!=null)for(int i=0;i<preview!.Count;i++)
                if(items[i]?.behaviours is {} components)foreach(var component in components)
                    if(component?.VUID.Value==entity){if(exact!=null&&exact.Pointer!=component.Pointer)return null;exact=component;}
            return exact;
        }
        var entryEntity=sim.objects[first.objectIndex].ID;
        if(Resolve(entryEntity) is not {} entryComponent||!bindings.TryGetMount(entryComponent.ComponentID,out _))return;
        if(scope.Shot.HousingsEvaluated.Count>=256||!scope.Shot.HousingsEvaluated.Add(entryEntity))return;
        var parts=new List<EraRejectionReport.PathPart>();
        foreach(var group in Enumerable.Range(0,Math.Min(count,hits.Length)).Select(i=>hits[i]).GroupBy(h=>h.objectIndex))
        {
            if(group.Key<0||group.Key>=sim.objects.Length)return;
            var entity=sim.objects[group.Key].ID;var component=Resolve(entity);
            var activeMaterial=component?.TryCast<PlateStructure>() is {} plate&&(context.ArmourSnapshot?.Catalogue??catalogue)?.ForMaterial(plate.armourTechID)?.Kind is "lightEra" or "heavyEra";
            parts.Add(new(entity,component?.ComponentID,activeMaterial,group.Select(h=>new MaterialBoundary(h.distance,(h.flags&IntersectionFlags.Enter)!=0,(h.flags&IntersectionFlags.Exit)!=0,0)).ToArray()));
        }
        if(EraRejectionReport.HousingBetweenElements(bindings,parts,entryEntity,first.distance,last.distance))
            RuntimeDamageFeed.RecordArmourInteraction(sim,scope.Index,"eraUnavailable",EraRejectionReport.Housing,relatedEntity:entryEntity);
    }
    private static void Apply(MaterialBlock block,Il2CppStructArray<StructureIntersection> hits,int count,int declaredEntry=-1)
    {
        var scope=active;var context=RuntimeSpall.Impact;var config=context?.ArmourSnapshot is {} captured?captured.Catalogue:catalogue;
        if(scope==null||context==null||config is not {Enabled:true}||!block.Valid||block.MaterialIndex<0||
            (scope.Sim.fragments[scope.Index].flags&FragmentFlag.OriginalPenetrator)==0)return;
        var entry=block.EnterIntersectionIndex;var exit=block.ExitIntersectionIndex;
        if(entry<0||exit<0||entry>=count||exit>=count||entry>=hits.Length||exit>=hits.Length)return;
        var first=hits[declaredEntry>=0?declaredEntry:entry];var last=hits[exit];var sim=scope.Sim;
        // Only the block's actual native entry is proven reached here. Internal
        // candidate intersections are geometric, not proof the shot reaches them.
        if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length&&
            TryResolvePlate(context,sim,sim.objects[first.objectIndex].ID,out var entryComponent,out _,out _)&&
            entryComponent.TryCast<PlateStructure>() is {} entryPlate&&
            config.ForMaterial(entryPlate.armourTechID) is {Kind:"lightEra" or "heavyEra"})
            declaredEntry=entry;
        if(first.objectIndex<0||first.objectIndex>=sim.objects.Length)return;
        var diagnosticEntity=sim.objects[first.objectIndex].ID;
        Plate? diagnosticModel=null;
        try{diagnosticModel=PlacedModel(sim,diagnosticEntity,context.LiveImpact);}catch{ /* Probe failures must not suppress native/old-plate processing. */ }
        void Trace(ref PlacedEraTraceHandler reason){if(diagnosticModel!=null&&reason.Building){reason.Prepend($"id={diagnosticEntity} live={context.LiveImpact} ");PlacedTrace(ref reason);}else reason.Discard();}
        if(diagnosticModel!=null)
        {
            scope.Shot.PlacedHit=true;
            if(ReleaseDiagnostics.Enabled)Trace($"stage=native-block targetModelResolved=True entry={entry} exit={exit} entryObject={first.objectIndex} exitObject={last.objectIndex} entryID={diagnosticEntity} exitID={(last.objectIndex>=0&&last.objectIndex<sim.objects.Length?sim.objects[last.objectIndex].ID:-1)} material={block.MaterialIndex}");
        }
        // Require one resolved object, not a blended material block spanning multiple components.
        double placedPhysicalDepth=0;
        if(diagnosticModel?.Component.TryCast<VehicleObjectModel>() is {} ownModel&&ownModel.Model?.CollisionMesh is {} ownMesh)
        {
            var depthKey=(ownModel.Pointer,ownMesh.Pointer,diagnosticModel.Spawn);
            if(!placedDepth.TryGetValue(depthKey,out placedPhysicalDepth))
            {
                var scale=ownModel.GetModelScale();
                placedPhysicalDepth=PlacedPartDepth.Measure(ownMesh.vertices.Select(v=>new System.Numerics.Vector3(v.x,v.y,v.z)).ToArray(),ownMesh.triangles.ToArray(),new(scale.x,scale.y,scale.z));
                placedDepth[depthKey]=placedPhysicalDepth;
            }
        }
        var combinedModule=diagnosticModel?.Component.ComponentID==CompositeModule.ComponentId;
        if(combinedModule)placedPhysicalDepth=CompositeModule.Current.Era.ActiveThicknessMm;
        bool ownBrick=PlacedBrickTrigger.Reached(diagnosticModel!=null,(sim.fragments[scope.Index].flags&FragmentFlag.OriginalPenetrator)!=0,
            (first.flags&IntersectionFlags.Enter)!=0,(first.flags&IntersectionFlags.Exit)!=0,first.distance,placedPhysicalDepth);
        if(diagnosticModel!=null&&!ownBrick){ObserveEra(scope,diagnosticModel.Binding!.MaterialId,"eraUnavailable","not-reached",diagnosticEntity);if(ReleaseDiagnostics.Enabled)Trace($"reject=not-reached-native-cassette-entry flags={first.flags} distance={first.distance:0.000} physicalDepth={placedPhysicalDepth:0.000}");return;}
        if(ownBrick)if(ReleaseDiagnostics.Enabled)Trace($"stage=reached-native-cassette physicalDepth={placedPhysicalDepth:0.000}mm entryFlags={first.flags}; no own-exit requirement, native passive block unchanged");
        var objectCount=block.IntersectedObjectIndices?.Length??0;
        if(declaredEntry<0&&!ownBrick&&objectCount>1){if(ReleaseDiagnostics.Enabled)Trace($"reject=multi-object-block count={objectCount}");return;}
        // Preview can omit the supplementary object list. Require the entire native
        // intersection span to belong to one object, including the exit boundary.
        if(declaredEntry<0&&!ownBrick&&objectCount==0)
        {
            // Native scans this unsorted buffer by distance (GetNextMaterialBlock
            // RVA1d7e290..1d7e418). Entry/exit array indices need not be ordered.
            if(!float.IsFinite(first.distance)||!float.IsFinite(last.distance)||last.distance<=first.distance)
            {if(ReleaseDiagnostics.Enabled)Trace($"reject=invalid-span-distance");return;}
            if(last.objectIndex!=first.objectIndex){if(ReleaseDiagnostics.Enabled)Trace($"reject=exit-object-mismatch");return;}
            for(var i=0;i<count;i++)
            {
                var hit=hits[i];
                if(hit.distance>=first.distance&&hit.distance<=last.distance&&hit.objectIndex!=first.objectIndex)
                {if(ReleaseDiagnostics.Enabled)Trace($"reject=mixed-span object={hit.objectIndex}");return;}
            }
        }
        var entity=sim.objects[first.objectIndex].ID;
        Plate? plate=null;
        if(!context.LiveImpact)
        {
            if(plate==null)
            {
            // Resolve against the currently bound editor owner, not a global ID cache.
            var owner=RuntimeArmourSimulator.CurrentOwner;
            var items=owner?.ObjectReader?.Items;
            PlateStructure? exact=null;
            if(items!=null)for(var i=0;i<owner!.ObjectReader.Count;i++)
            {
                var item=items[i];if(item?.behaviours is not {} components)continue;
                for(var b=0;b<components.Length;b++)
                {
                    var candidate=components[b]?.TryCast<PlateStructure>();
                    // ObjectInfo.ID is StructureContext.ObjectID, passed from the
                    // component VUID, not the container EntityID (native1e7991d,
                    // 1ff7eff,1d77cf9). Search exact bound-owner components only.
                    if(candidate==null||candidate.VUID.Value!=entity)continue;
                    if(exact!=null&&exact.Pointer!=candidate.Pointer){return;}
                    exact=candidate;
                }
            }
            if(exact!=null&&exact.VehicleRoot is {} root)
            {
                if(!spawns.TryGetValue(root.Pointer,out var spawn))spawns[root.Pointer]=spawn=++generation;
                plate=new(exact,spawn,root.Pointer);
                
            }
            }
        }
        else
        {
            // Native combat damage is applied through VehicleHealthRegister's
            // target-local VUID reader; never search a global EntityID registry.
            var health=sim.damageApplier?.TryCast<VehicleHealthRegister>();
            if(health?.objectReader is {} reader&&reader.TryGet<PlateStructure>(new VUID(entity),out var target)&&target!=null&&target.VUID.Value==entity&&target.VehicleRoot is {} root)
            {
                if(!spawns.TryGetValue(root.Pointer,out var spawn))spawns[root.Pointer]=spawn=++generation;
                plate=new(target,spawn,root.Pointer);
                
            }
        }
        if(plate==null)plate=diagnosticModel??PlacedModel(sim,entity,context.LiveImpact);
        if(plate==null){return;}
        var component=plate.Component;var material=plate.Placed?plate.Binding!.MaterialId:component.TryCast<PlateStructure>()!.armourTechID;
        void RejectEra(string reason)
        {
            if(plate.Placed||config.ForMaterial(material)?.Kind is "lightEra" or "heavyEra")ObserveEra(scope,material,"eraUnavailable",reason,entity);
        }
        var tech=component.Vehicle?.Tech;var design=component.Vehicle?.DesignInfo;
        if(tech==null||design==null||!RuntimeShellEra.HasNativeContext(component.Vehicle)||!tech.TryGetTech(material,out var armourTech)||armourTech==null){RejectEra("native-tech-context");if(ReleaseDiagnostics.Enabled)Trace($"reject=native-tech-context material={material} tech={tech!=null} design={design!=null}");return;}
        if(ReleaseDiagnostics.Enabled)Trace($"stage=native-tech-pass material={material} date={design.Date}");
        var fragment=sim.fragments[scope.Index];var direction=fragment.direction.normalized;
        var normal=scope.Structure.GetIntersectionNormal(ref first).normalized;
        var cosine=Mathf.Abs(Vector3.Dot(direction,normal));
        if(cosine<.001f){RejectEra("normal");if(ReleaseDiagnostics.Enabled)Trace($"reject=invalid-grazing-normal");return;}
        var angle=Math.Acos(Math.Clamp(cosine,0,1))*180/Math.PI;
        var thickness=(last.distance-first.distance)*1000*cosine;
        if(ReleaseDiagnostics.Enabled)Trace($"stage=geometry thicknessNormal={thickness:0.000}mm path={((last.distance-first.distance)*1000):0.000}mm angle={angle:0.00}deg normal=({normal.x:0.000},{normal.y:0.000},{normal.z:0.000})");
        var position=sim.SimToWorldSpacePoint(fragment.spawnPoint+direction*first.distance);
        var exitPosition=sim.SimToWorldSpacePoint(fragment.spawnPoint+direction*last.distance);
        var worldDirection=sim.SimToWorldSpaceDirection(direction).normalized;
        if(combinedModule)
        {
            var owner=component.TryCast<VehicleObjectModel>();var transform=component.VehicleTransform;
            if(owner==null||transform==null||!CompositeModule.Current.Era.Enabled){RejectEra("layout");return;}
            var local=transform.WorldToLocalSpace(position);
            var rotation=Quaternion.Inverse(transform.Rotation);var scale=owner.GetModelScale();
            var localNormal=rotation*sim.SimToWorldSpaceDirection(normal);var localDirection=rotation*worldDirection;
            // The combined collider remains the approved envelope. Only an entry
            // on a cover's exterior can detonate; edges, gaps and the back are passive.
            if(!PlacedEraCells.ValidScale(scale)||!CompositeModuleEraGeometry.Exterior(localNormal.y*Math.Sign(scale.y),localDirection.y*Math.Sign(scale.y))||
                !CompositeModuleEraGeometry.TryCell(local.x,local.z,out _,out _)){RejectEra("layout");return;}
        }
        var shot=scope.Shot;
        // Native next block is reached only by a continuing original fragment. Prove the
        // previous exit lies behind this entry; stopped projectiles never precondition.
        if(shot.Pending is {} pending&&Vector3.Dot(position-pending.Exit,worldDirection)>=-.0001f)
        {
            if(ArmourResponseModel.PerforatedSteel(shot.State,config.Preconditioning,pending.Material,pending.Thickness,pending.Angle,0,pending.Key))shot.SteelExit=pending.Exit;
            shot.Pending=null;
        }
        var key=new ArmourHitKey(plate.Spawn,entity,first.triangleIndex,first.distance,last.distance);
        if(context.Behavior=="apfsds"&&config.Preconditioning.SteelIds.Contains(material))
            shot.Pending=(material,thickness,angle,exitPosition,key);
        var response=plate.Placed?config.ForResponse(plate.Binding!.ResponseId):config.ForMaterial(material);
        if(response==null||plate.Placed&&!plate.Binding!.MatchesRecipe(response.ResponseId,response.Kind,response.CompatibleMaterialIds)){RejectEra("material");if(ReleaseDiagnostics.Enabled)Trace($"reject=missing-or-wrong-recipe material={material} response={response?.ResponseId??"absent"}");return;}
        // The owning vehicle's actual design date decides availability. The native
        // technology-frame lookup above remains a separate per-recipe gate; it must
        // not let a global scenario frame bypass a WW1/pre-modern vehicle.
        var vehicleEraAllowed=plate.Placed
            ? VehicleEraAccess.AllowsPlacedPart(design.Date,response.MinimumDate,tech!=null,armourTech!=null)
            : VehicleEraAccess.AllowsMaterial(design.Date,response.MinimumDate,tech!=null,armourTech!=null);
        if(!vehicleEraAllowed)
        {
            RejectEra("vehicle-era");
            if(ReleaseDiagnostics.Enabled)Trace($"reject=vehicle-era material={material} design={design.Date} minimumDate={response.MinimumDate:yyyy.MM.dd} response={response.ResponseId} placed={plate.Placed}");
            return;
        }
        // 0.22.28: a material with a protection table has its whole effect in the block's rhaFactor; a non-ERA legacy response would count twice.
        if(!plate.Placed&&response.Kind is not ("lightEra" or "heavyEra")&&NativeMaterialReactions.HasTable(context,material))return;
        if(!ArmourResponseModel.ThreatAllowed(response,context.ThreatClass))
        {RejectEra("unsupported-threat");if(ReleaseDiagnostics.Enabled)Trace($"reject=unsupported-threat-class profile={context.ProfileId} class={context.ThreatClass} response={response.ResponseId}");return;}
        // A combined block may select the backing's passive material. ERA belongs
        // to the exact reached entry face, so validate that object's own recipe.
        // Native block resistance is still processed once by the original game.
        var passiveIndex=ownBrick||declaredEntry>=0?sim.objects[first.objectIndex].materialIndex:block.MaterialIndex;
        if(passiveIndex<0||passiveIndex>=sim.materials.Length){RejectEra("material");if(ReleaseDiagnostics.Enabled)Trace($"reject=material-index");return;}
        // 0.22.49 for ERA, 0.22.51 for every recipe check: the declared recipe is compared with the material from before any EB write
        // in this shot. A PerInterval block write is a temporary resistance, not the material's identity (equal to live when none is active).
        var passive=NativeMaterialReactions.OriginalMaterial(sim,passiveIndex);

        if(combinedModule
            ?!ArmourResponses.PassiveMatches(response,(float)CompositeModule.Current.Era.ActiveDensity,(float)CompositeModule.Current.Era.ActiveRhaFactor,(float)CompositeModule.Current.Era.ActiveSpallFactor)
            :!ArmourResponses.PassiveMatches(response,passive.Density,passive.RhaFactor,passive.SpallFactor))
        {RejectEra("material");if(ReleaseDiagnostics.Enabled)Trace($"reject=passive-mismatch density={passive.Density:0.000} rha={passive.RhaFactor:0.000} spall={passive.SpallFactor:0.000}");if(warnings.Count<32&&warnings.Add("recipe:"+material))Plugin.ModLog.LogWarning("[Armour response] Custom passive recipe differs for "+material+"; additional response skipped.");return;}
        if(ReleaseDiagnostics.Enabled)Trace($"stage=recipe-pass response={response.ResponseId} density={passive.Density:0.000} rha={passive.RhaFactor:0.000} spall={passive.SpallFactor:0.000}");
        // 0.22.12: every original projectile at or above the trigger calibre can
        // detonate an intact cell. The effect differs per threat; it is no longer
        // a gate. The calibre is the gun bore for mod profiles and the native
        // fragment diameter for full-bore rounds.
        var eraThreat=scope.Threat;
        var calibreGate=scope.EraTrigger&&ArmourResponseModel.EraTriggerCalibre(scope.CalibreMm,config.MinimumTriggerCalibreMm);
        var eligibleThreat=response.Kind is "lightEra" or "heavyEra"&&calibreGate&&
            eraThreat is not ("fragment" or "");
        DeclaredFace? declaredFace=null;
        if(!plate.Placed&&response.Kind is "lightEra" or "heavyEra")
        {
            if(!NativeHitFace.TryResolve(sim,first,component.TryCast<PlateStructure>()!,out var resolvedFace,out var faceReason))
            {ObserveEra(scope,material,"eraUnavailable",faceReason,entity);return;}
            declaredFace=resolvedFace;
        }
        var eligibilityThickness=ownBrick?placedPhysicalDepth:declaredFace?.NormalThicknessMm??thickness;
        if(response.Kind is "heavyEra" or "lightEra")
            RuntimeDamageFeed.UpdateEraLayer(sim,scope.Index,entity,material,response.ResponseId,eligibilityThickness,angle);
        var stageCount=plate.Placed?1:FaceEraStackPolicy.Stages(eligibilityThickness,response.Stack);
        // Multistage native continuation is not yet proven. Fail closed rather than
        // spending rear charges before the projectile reaches their paid depth.
        if(stageCount!=1)
        {
            ObserveEra(scope,material,"eraUnavailable",stageCount>1?"sequential stack unavailable":"no complete ERA charge",entity);
            if(stageCount>1&&warnings.Count<32&&warnings.Add("stack:"+response.ResponseId))
                Plugin.ModLog.LogWarning("[ERA stack] Sequential native stage traversal unavailable; passive armour retained for "+response.ResponseId);
            return;
        }
        var calibratedThickness=!plate.Placed&&response.Stack is {Enabled:true} stack?stack.UnitNormalThicknessMm:eligibilityThickness;
        var eligibleGeometry=calibratedThickness>=response.Geometry.MinNormalThicknessMm&&calibratedThickness<=response.Geometry.MaxNormalThicknessMm;
        var angularWeight=ArmourResponseModel.EraAngularWeight(response,eraThreat,angle);
        if(response.Kind is "lightEra" or "heavyEra")
        {
            if(!eligibleGeometry)
            {
                var minChoice=Math.Ceiling(response.Geometry.MinNormalThicknessMm);
                var maxChoice=Math.Floor(response.Geometry.MaxNormalThicknessMm);
                var required=minChoice==maxChoice?$"{minChoice:0} mm":minChoice<maxChoice?$"{minChoice:0}-{maxChoice:0} mm":
                    $"{response.Geometry.MinNormalThicknessMm:0.##}-{response.Geometry.MaxNormalThicknessMm:0.##} mm";
                ObserveEra(scope,material,"eraUnavailable",$"face {calibratedThickness:0.##} mm; requires {required}",entity);
            }
            else if(!calibreGate)ObserveEra(scope,material,"eraUnavailable",$"below trigger calibre ({scope.CalibreMm:0.#} < {config.MinimumTriggerCalibreMm:0.#} mm)",entity);
            else if(!eligibleThreat)ObserveEra(scope,material,"eraUnavailable","projectile type unsupported",entity);
            else if(shot.State.ContainsApplied(key))RejectEra("duplicate");
        }
        if(ReleaseDiagnostics.Enabled)Trace($"stage=eligibility behavior={context.Behavior} class={context.ThreatClass} eraThreat={eraThreat} calibre={scope.CalibreMm:0.0}mm gate={calibreGate} threat={eligibleThreat} geometry={eligibleGeometry} calibrationDepth={eligibilityThickness:0.000}mm nativeChordNormal={thickness:0.000}mm allowed={response.Geometry.MinNormalThicknessMm:0.000}..{response.Geometry.MaxNormalThicknessMm:0.000}mm angleWeight={angularWeight:0.000} alreadyApplied={shot.State.ContainsApplied(key)}");
        if(!eligibleThreat)if(ReleaseDiagnostics.Enabled)Trace($"reject=unsupported-threat");
        else if(!eligibleGeometry)if(ReleaseDiagnostics.Enabled)Trace($"reject=normal-thickness-outside-gate");
        else if(shot.State.ContainsApplied(key))if(ReleaseDiagnostics.Enabled)Trace($"reject=duplicate-block-in-shot");
        double? experimentalLoss=null;double? experimentalCap=null;float? experimentalSpeed=null;double experimentalIncoming=0,experimentalOutgoing=0;
        if(ArmourResponseModel.KineticEraThreat(eraThreat)&&ExperimentalRodModel.UsesDynamic(response,context.Experimental))
        {
            var snapshot=context.Experimental;
            if(response.Kind=="heavyEra"&&response.Geometry.Mode=="declaredCassette"&&
                snapshot is {Enabled:true}&&snapshot.Recipes.TryGetValue(response.ResponseId,out var recipe)&&
                response.Calibration.IntactRodRetention==1&&response.Calibration.DisturbedRodRetention==1&&
                response.Calibration.DisturbanceContribution==0)
            {
                var scale=response.ThreatScale?.For(eraThreat)??1;
                var loss=ExperimentalRodModel.Loss(recipe,context.Geometry,angularWeight)*scale;
                var cap=snapshot.EffectiveKineticCap(config.MaximumAdditionalKineticLoss);
                if(loss>0&&ExperimentalRodModel.Next(shot.State.AdditionalRetention,loss,cap)<shot.State.AdditionalRetention)
                {
                    var experimentalConstant=sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant;
                    experimentalIncoming=fragment.GetBasePenetration(experimentalConstant)*1000;
                    var factor=ExperimentalRodModel.Next(shot.State.AdditionalRetention,loss,cap)/shot.State.AdditionalRetention;
                    var target=experimentalIncoming*factor;
                    if(double.IsFinite(experimentalIncoming)&&experimentalIncoming>0&&double.IsFinite(target)&&target>0)
                    {
                        var candidateSpeed=PenetrationUtils.ComputeRequiredPenetrationSpeed(fragment.diameter*1000,fragment.mass,(float)target,experimentalConstant);
                        var proposed=sim.fragments[scope.Index];proposed.speed=candidateSpeed;
                        experimentalOutgoing=proposed.GetBasePenetration(experimentalConstant)*1000;
                        if(ExperimentalRodModel.ValidConversion(experimentalIncoming,target,experimentalOutgoing,candidateSpeed,fragment.speed))
                        {experimentalLoss=loss;experimentalCap=cap;experimentalSpeed=candidateSpeed;}
                        else if(ReleaseDiagnostics.Enabled)Trace($"reject=experimental-native-conversion; no dynamic effect, charge still consumed");
                    }
                }
                else if(ReleaseDiagnostics.Enabled)Trace($"reject=experimental-loss-zero; charge still consumed");
            }
            else if(ReleaseDiagnostics.Enabled)Trace($"reject=experimental-recipe-unavailable; base rod recipe retained");
        }
        var intact=false;EraCell? activatedCell=null;
        var placedModel=plate.Placed?plate.Component.TryCast<VehicleObjectModel>():null;
        EraVisualPreset? placedPreset=null;var placedCassette=false;
        var partitioned=placedModel!=null&&EraPresetRegistry.Visual(placedModel.ComponentID,out placedPreset,out placedCassette)
            &&placedCassette&&PlacedEraCells.Partitioned(placedPreset!);
        if(partitioned&&!PlacedEraCells.TryIndices(placedModel!,placedPreset!,position,out _,out _,out _))
        {RejectEra("layout");if(ReleaseDiagnostics.Enabled)Trace($"reject=invalid-resizable-cell-layout; no charge consumed");return;}
        if(plate.Placed?PlacedBrickTrigger.CanConsume(eligibleThreat,eligibleGeometry,shot.State.ContainsApplied(key)):
            eligibleThreat&&eligibleGeometry&&!shot.State.ContainsApplied(key))
        {
            // Rotation-only local basis preserves metres even on a scaled plate.
            var rotation=Quaternion.Inverse(component.VehicleTransform.Rotation);
            var local=rotation*(position-component.VehicleTransform.Position);
            var localNormal=rotation*sim.SimToWorldSpaceDirection(normal);
            var axis=Math.Abs(localNormal.x)>Math.Abs(localNormal.y)?(Math.Abs(localNormal.x)>Math.Abs(localNormal.z)?0:2):(Math.Abs(localNormal.y)>Math.Abs(localNormal.z)?1:2);
            var u=axis==0?local.y:local.x;var v=axis==2?local.y:local.z;
            var sign=(axis==0?localNormal.x:axis==1?localNormal.y:localNormal.z)>=0?1:-1;
            EraCell cell;
            if(partitioned)
            {
                PlacedEraCells.TryIndices(placedModel!,placedPreset!,position,out _,out var gridX,out var gridY);
                cell=PlacedEraState.Key(plate.Spawn,entity,0,gridX,gridY);
            }
            else cell=plate.Placed?PlacedEraState.Key(plate.Spawn,entity):new EraCell(plate.Spawn,entity,declaredFace is {} actualFace?actualFace.FaceIndex+1:(axis+1)*sign,(int)Math.Floor(u/response.CellPitchM),(int)Math.Floor(v/response.CellPitchM));
            // Tandem guard (0.20.0): a precursor pass spends only the front layer of a layer stack. Checks are
            // ordered cheapest first and the key uses values already known here; other parts and non-precursor
            // shots never evaluate the thread-static test.
            var stackGuard=partitioned&&placedPreset!.StageIndex>0&&placedPreset.HousingActiveIds is {Length:>=2}&&RuntimeTandem.InPrecursor;
            var rearGuarded=false;
            intact=ArmourResponseModel.ConsumeStacked(context.LiveImpact?combat:context.SharedPreviewCells??shot.Preview,shot.State,cell,
                stackGuard?placedPreset!.StageIndex:0,stackGuard?placedPreset!.Mount.GetHashCode():0,stackGuard,out rearGuarded);
            if(rearGuarded){RejectEra("rear");if(ReleaseDiagnostics.Enabled&&stackTraces++<8)Plugin.ModLog.LogInfo("[Tandem stack] precursor already spent the front layer; rear layer kept for the main charge");}
            else if(!intact)ObserveEra(scope,material,"eraSpent","charge already spent",entity);
            if(ReleaseDiagnostics.Enabled)Trace($"stage=consume spawn={plate.Spawn} vuid={entity} first={intact} spentBefore={!intact} preview={!context.LiveImpact}");
            if(cellDiagnostics++<40)if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Armour cell] cell={cell} intact={intact} live={context.LiveImpact}");
            if(intact&&context.LiveImpact)
            {
                activatedCell=cell;
                RuntimeDamageFeed.EraActivated(sim,entity,cell,response.Kind,context.LiveImpact,intact);
                context.ArmourExplosions.Add(position);
                if(plate.Placed)
                {
                    placedState.Track(cell);
                    var worldNormal=sim.SimToWorldSpaceDirection(normal).normalized;
                    PlacedEra.Queue(context,new(component.Vehicle!.Pointer,plate.Root,plate.Spawn,entity,
                        new(position.x,position.y,position.z),new(worldNormal.x,worldNormal.y,worldNormal.z),response.ResponseId,
                        cell.Surface,cell.X,cell.Y));
                }
            }
        }
        // A kinetic detonation also owns the spaced-plate seam behind it: one
        // ERA box plus an air gap plus the hull must not pay twice. This is before
        // the later hull block evaluates the gap. A detonation without effect (scale 0: light ERA against KE,
        // heavy ERA against full-bore) leaves the spaced-plate effect standing.
        if(intact&&ArmourResponseModel.KineticEraThreat(eraThreat)&&(response.ThreatScale?.For(eraThreat)??1)>0)
            RuntimeSpall.Impact?.SpacedKineticShots.TryClaim(sim.Pointer);
        var gap=shot.SteelExit is {} steelExit?Vector3.Dot(position-steelExit,worldDirection)*1000:double.NaN;
        var retention=ArmourResponseModel.Apply(config,response,shot.State,key,context.Behavior,true,true,calibratedThickness,angle,gap,intact,experimentalLoss,experimentalCap,context.ThreatClass,eraThreat);
        var constant=sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant;
        var remaining=fragment.GetBasePenetration(constant)*1000;
        string? eraReport=null;
        if(intact)
        {
            var tags=NativeMaterialReactions.Threats(context,true);
            eraReport=ArmourResponseModel.EraReactionText(RuntimeDamageFeed.EraReportLabel(sim,entity),tags.ShellLabel(),eraThreat,response.Kind,retention,response.ThreatScale?.For(eraThreat)??1);
        }
        if(retention>=1)
        {
            if(eraReport!=null&&(context.LiveImpact||RuntimeArmourComparisonRunner.IsProbing))
                RuntimeDamageFeed.RecordArmourInteraction(sim,scope.Index,"eraReaction",eraReport,remaining,remaining,entity);
            if(ReleaseDiagnostics.Enabled)Trace($"stage=response retention={retention:0.000000} additionalLoss=none; no claim of armour stop");
            return;
        }
        var speed=experimentalSpeed??PenetrationUtils.ComputeRequiredPenetrationSpeed(fragment.diameter*1000,fragment.mass,(float)(remaining*retention),constant);
        if(!float.IsFinite(speed)||speed<=0){if(ReleaseDiagnostics.Enabled)Trace($"reject=invalid-inverse-budget-speed");return;}
        fragment.speed=Math.Min(fragment.speed,speed);sim.fragments[scope.Index]=fragment;
        var afterEraMm=fragment.GetBasePenetration(constant)*1000;
        if(eraReport!=null&&(context.LiveImpact||RuntimeArmourComparisonRunner.IsProbing))
            RuntimeDamageFeed.RecordArmourInteraction(sim,scope.Index,"eraReaction",eraReport,remaining,afterEraMm,entity);
        if(RuntimeArmourComparisonRunner.IsProbing)ArmourComparisonEvidence.Record(sim,scope.Index,"eraResidual",
            $"Reached ERA response; active-layer angle {angle:0.#} degrees; not added again to block total.",remaining,afterEraMm,entity);
        if(experimentalSpeed!=null&&intact&&diagnostics++<80)
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Dynamic ERA committed] recipe={response.ResponseId} VUID={entity} intact=True LD={context.Geometry!.LengthToDiameter:0.00} boreLength={context.Geometry.BoreLengthRatio:0.00} incoming={experimentalIncoming:0.000000}mm outgoing={experimentalOutgoing:0.000000}mm factor={retention:0.000000} cap={experimentalCap!.Value:0.000} live={context.LiveImpact}; before native passive once.");
        if(activatedCell is {} activated)RuntimeDamageFeed.EraBudget(sim,scope.Index,activated,remaining,angle,angularWeight);
        if(ReleaseDiagnostics.Enabled)Trace($"stage=response incomingBudget={remaining:0.000}mm retention={retention:0.000000} outgoingBudget={fragment.GetBasePenetration(constant)*1000:0.000}mm (before native plate consumption; reduction does not mean full stop)");
        if(scope.SpeedBefore>0)scope.ResidualSpeedRatio=Math.Clamp(fragment.speed/scope.SpeedBefore,0,1);
        if(diagnostics++<40)if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Armour response] {response.ResponseId} VUID={entity} live={context.LiveImpact} angle={angle:0.0} normal={thickness:0.0} gap={gap:0.0} disturbance={shot.State.Disturbance:0.000} retention={retention:0.000} ERAintact={intact}");
    }
    // 0.22.78 HESH begin
    private static int heshProbes;private static bool heshWarned;
    // First contact of a HESH shot (with diagnostics, once per impact, live only, right under the [Chemical] line): the d, mass, apModifier
    // and budget of the native ricochet rule "no perforation, B <= d and at least 10 degrees".
    private static void HeshBegin(Scope scope,ShellImpactContext context)
    {
        scope.Hesh=true;
        if(!ReleaseDiagnostics.Enabled||!context.LiveImpact||context.HeshLogged)return;
        context.HeshLogged=true;
        try
        {
            var f=scope.Sim.fragments[scope.Index];var projectile=scope.Sim.projectileProfiles[f.projectileProfileIndex];
            var budget=f.GetBasePenetration(projectile.penetratorConstant)*1000*projectile.apModifier;var d=f.diameter*1000;
            Plugin.ModLog.LogInfo($"[Chemical HESH] profile={context.Profile?.Id??context.ProfileId} calibre={context.GunDiameter*1000:0.#}mm penetrator d={d:0.0}mm ({(context.GunDiameter>0?f.diameter/context.GunDiameter:0):0.000} calibre) mass={f.mass:0.000}kg apModifier={projectile.apModifier:0.000} budget B={budget:0.0}mm (B/d={(d>0?budget/d:0):0.000}); native ricochet: no perforation, B/rha<=d and >=10deg; surface detonation below {RuntimeSpall.Hesh.MinAngleDegrees:0.#}deg.");
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Chemical HESH] Diagnostic line unavailable: "+ex.Message);}
    }
    // The angle from the normal of the solid block this call reaches, the same acos(|direction . normal(entry)|) the native ricochet rule uses.
    private static void HeshAngle(Scope scope,MaterialBlock block,Il2CppStructArray<StructureIntersection> hits,int count)
    {
        try
        {
            var entry=block.EnterIntersectionIndex;
            if(entry<0||entry>=count||entry>=hits.Length||scope.Index<0||scope.Index>=scope.Sim.FragmentCount){scope.HeshAngle=double.NaN;return;}
            var first=hits[entry];var normal=scope.Structure.GetIntersectionNormal(ref first).normalized;
            var direction=scope.Sim.fragments[scope.Index].direction.normalized;var dot=Vector3.Dot(direction,normal);
            scope.HeshAngle=float.IsFinite(dot)&&normal.sqrMagnitude>.5f&&direction.sqrMagnitude>.5f?Math.Acos(Math.Clamp(Math.Abs((double)dot),0,1))*180/Math.PI:double.NaN;
        }
        catch(Exception ex){scope.HeshAngle=double.NaN;if(!heshWarned){heshWarned=true;Plugin.ModLog.LogWarning("[HESH] Surface detonation unavailable; the native outcome is kept: "+ex.Message);}}
    }
    // After the native call: a HESH ricochet below the limit becomes a stop where it struck (no child flies on). Every other outcome stays native.
    private static void HeshSurface(Scope scope)
    {
        var rules=RuntimeSpall.Hesh;
        if(!rules.SurfaceDetonation)return;
        try
        {
            var result=HeshRules.Surface(new NativeHeshFragments(scope.Sim),scope.Index,scope.CountBefore,scope.HeshAngle,rules.MinAngleDegrees);
            if(result.Converted&&ReleaseDiagnostics.Enabled&&heshProbes++<24)
                Plugin.ModLog.LogInfo($"[HESH] detonated on the surface at {scope.HeshAngle:0.0} deg (< {rules.MinAngleDegrees:0.#}): native ricochet replaced by a stop; {result.ChildrenStopped} ricochet child(ren) stopped.");
        }
        catch(Exception ex){if(!heshWarned){heshWarned=true;Plugin.ModLog.LogWarning("[HESH] Surface detonation unavailable; the native outcome is kept: "+ex.Message);}}
    }
    // 0.22.78 HESH end
    // 0.22.86 HE begin
    private static int heProbes;private static bool heWarned;
    // The solid block the perforating part of an HE round reaches in this call: the angle (the native ricochet rule's acos(|direction . normal|), as HESH),
    // the line-of-sight path, the rhaFactor, whether the block starts with armour (a plate or the composite module, not a module or an ERA element) and its
    // report key. A failure marks the block as no armour, so a perforation of it never counts as a perforation of the armour.
    private static void HeBlock(Scope scope,MaterialBlock block,Il2CppStructArray<StructureIntersection> hits,int count,Il2CppStructArray<MaterialInfo> materials)
    {
        HeshAngle(scope,block,hits,count);
        scope.HeArmour=false;scope.HePathMm=double.NaN;scope.HeRha=double.NaN;scope.HeHit=null;
        scope.HeNextMm=double.PositiveInfinity; // 0.22.89
        try
        {
            int entry=block.EnterIntersectionIndex,exit=block.ExitIntersectionIndex;
            if(entry<0||exit<0||entry>=count||exit>=count||entry>=hits.Length||exit>=hits.Length)return;
            var sim=scope.Sim;var first=hits[entry];
            scope.HePathMm=Math.Max(0,(hits[exit].distance-first.distance)*1000d);
            if(block.MaterialIndex<materials.Length)scope.HeRha=materials[block.MaterialIndex].rhaFactor;
            scope.HeHit=$"{sim.Pointer}:{scope.Index}:{entry}:{exit}";
            if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length&&sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader is {} reader)
            {
                var id=sim.objects[first.objectIndex].ID;
                scope.HeArmour=reader.TryGet<PlateStructure>(new VUID(id),out var plate)&&plate!=null||
                    reader.TryGet<VehicleObjectModel>(new VUID(id),out var model)&&model!=null&&model.ComponentID==CompositeModule.ComponentId;
            }
            HeBlast.LastLayer(scope.HeHit);
            if(scope.HeArmour&&RuntimeSpall.He.SpacedArmourRule)scope.HeNextMm=HeNextArmour(scope,hits,count,exit); // 0.22.89: skirt rule, only with the rule on
        }
        catch(Exception ex){scope.HeArmour=false;if(!heWarned){heWarned=true;Plugin.ModLog.LogWarning("[HE] Perforation check unavailable; the plate counts as not perforated: "+ex.Message);}}
    }
    // After the native call of the part: a perforation of armour is recorded for the blast filter (that vehicle gets the game's own blast) and for the report;
    // without a perforation a ricochet below the limit becomes a stop (the HESH rule) and from the limit on nothing flies on; a continuation behind the plate
    // is stopped after its own call (the burst has already been made there), so the body never flies on.
    private static void HeEnd(Scope scope)
    {
        try
        {
            var fragments=new NativeHeshFragments(scope.Sim);var index=scope.Index;
            if(index<0||index>=fragments.Count)return;
            var flags=fragments.Flags(index);
            if((flags&HeshRules.Original)==0)return;
            if((flags&HeshRules.Penetrated)!=0)
            {
                if(scope.HeArmour&&scope.Sim.damageApplier is {} applier&&double.IsFinite(scope.HePathMm))
                {
                    var angle=double.IsFinite(scope.HeshAngle)?scope.HeshAngle:0;
                    var normal=scope.HePathMm*Math.Cos(angle*Math.PI/180);var rhaEq=scope.HePathMm*(double.IsFinite(scope.HeRha)?scope.HeRha:1);
                    // 0.22.89 begin
                    // Skirt rule: armour of this vehicle within spacedArmourGapMm behind the exit makes the plate spaced armour (a skirt, a mudguard, an ERA
                    // box): the round detonates in the gap, this vehicle is not marked perforated and its interior keeps the shock.
                    if(HeSpaced.Applies(RuntimeSpall.He,scope.HeNextMm))
                    {
                        HeBlast.Spaced(new HeSpacedPerforation(applier.Pointer,normal,rhaEq,angle,scope.HeNextMm,scope.HeHit));
                        if(ReleaseDiagnostics.Enabled&&heProbes++<48)Plugin.ModLog.LogInfo($"[HE] the part perforated {normal:0.0} mm of spaced armour; next armour {scope.HeNextMm:0} mm behind (rule {RuntimeSpall.He.SpacedArmourGapMm:0} mm): detonation in the gap, the interior of this vehicle receives the shock only.");
                    }
                    else
                    {
                    // 0.22.89 end
                    HeBlast.Perforated(new HePerforation(applier.Pointer,normal,rhaEq,angle,scope.HeHit));
                    if(ReleaseDiagnostics.Enabled&&heProbes++<48)Plugin.ModLog.LogInfo($"[HE] the part perforated {normal:0.0} mm ({rhaEq:0.0} mm RHA-eq) at {angle:0.0} deg; the blast reaches the interior of this vehicle.");
                    } // 0.22.89
                }
            }
            else
            {
                var result=HeRules.NoPerforation(fragments,index,scope.CountBefore,scope.HeshAngle,RuntimeSpall.He.MinAngleDegrees);
                if((result.Converted||result.Grazed)&&ReleaseDiagnostics.Enabled&&heProbes++<48)
                    Plugin.ModLog.LogInfo(result.Converted?$"[HE] detonated on the surface at {scope.HeshAngle:0.0} deg (< {RuntimeSpall.He.MinAngleDegrees:0.#}): ricochet replaced by a stop; {result.ChildrenStopped} child(ren) stopped."
                        :$"[HE] graze at {scope.HeshAngle:0.0} deg: the ricochet stays, {result.ChildrenStopped} child(ren) stopped (no residual projectile).");
            }
            var stopped=HeRules.StopContinuation(fragments,index,scope.CountBefore);
            if(stopped>0&&ReleaseDiagnostics.Enabled&&heProbes++<48)Plugin.ModLog.LogInfo($"[HE] continuation {index} behind the plate stopped ({stopped} fragment(s)); no residual projectile.");
            // The part itself never deals kinetic damage: the game pays the hits of a fragment after the simulation with ½mv² of its final speed, and the
            // part's speed is only a proxy for its budget. Its speed is 0 once its own call is done (its continuation was already made in that call).
            var self=scope.Sim.fragments[index];
            if(self.speed!=0){self.speed=0;scope.Sim.fragments[index]=self;}
        }
        catch(Exception ex){if(!heWarned){heWarned=true;Plugin.ModLog.LogWarning("[HE] Part end unavailable; the native outcome is kept: "+ex.Message);}}
    }
    // 0.22.89 begin
    private static bool heNextWarned;
    // The skirt rule's look-ahead on the sorted intersection list of this block: the nearest Enter hit of armour of the same vehicle (a plate or the composite
    // module, the test of HeBlock) behind the exit, within spacedArmourGapMm. A failure counts as "no armour behind" (the perforation of 0.22.88), once logged.
    private static double HeNextArmour(Scope scope,Il2CppStructArray<StructureIntersection> hits,int count,int exit)
    {
        try
        {
            var sim=scope.Sim;var n=Math.Min(count,hits.Length);
            var reader=sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
            if(reader==null)return double.PositiveInfinity;
            return HeSpaced.NextArmourMm(n,k=>hits[k].distance,k=>(hits[k].flags&IntersectionFlags.Enter)!=0,k=>
            {
                var o=hits[k].objectIndex;
                if(o<0||o>=sim.objects.Length||sim.objects[o].materialIndex<0)return false;
                var id=sim.objects[o].ID;
                return reader.TryGet<PlateStructure>(new VUID(id),out var plate)&&plate!=null||
                    reader.TryGet<VehicleObjectModel>(new VUID(id),out var model)&&model!=null&&model.ComponentID==CompositeModule.ComponentId;
            },exit,RuntimeSpall.He.SpacedArmourGapMm);
        }
        catch(Exception ex){if(!heNextWarned){heNextWarned=true;Plugin.ModLog.LogWarning("[HE] Spaced-armour look-ahead unavailable; the perforation counts as in 0.22.88: "+ex.Message);}return double.PositiveInfinity;}
    }
    // 0.22.89 end
    // 0.22.86 HE end
}



