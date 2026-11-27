using BepInEx.Configuration;
using HarmonyLib;
using Sprocket;
using Sprocket.DamageModelling;
using Sprocket.PlayerControl;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using PlayerController=Sprocket.Gameplay.VehicleControl.VehicleController;
using UnityEngine;
namespace SprocketExpandedBallistics;
[HarmonyPatch]
internal static class RuntimeDamageFeed
{
    internal sealed class Shot
    {
        internal readonly int Number;internal readonly int NativeId;internal readonly float SpawnTime;internal readonly List<string> Lines=new();
        internal readonly Dictionary<(IntPtr,int),ComponentDamage> Damage=new();
        internal readonly DamageFeedTimeline Timeline=new();
        internal readonly Dictionary<IntPtr,PendingLayer> Pending=new();
        internal readonly HashSet<string> Seen=new();
        internal readonly List<CombatReportEvent> Events=new();internal int OmittedEvents;
        internal string Profile="Unknown",Direction="Unknown direction";internal double? Speed;
        /// <summary>Blueprint name of the vehicle that was hit and the health register it belongs to (0.22.6B); both stay empty when unresolved.</summary>
        internal string ShooterName="unknown";
        internal string? Target;internal IntPtr TargetRegister;
        internal double? InitialPenetration;
        internal IntPtr Shooter;internal IntPtr Player;
        internal void Record(CombatReportEvent row){if(Events.Count<256)Events.Add(row);else OmittedEvents=CombatReportTruth.Count(OmittedEvents,1);}
        internal Shot(int number,int nativeId,float spawnTime){Number=number;NativeId=nativeId;SpawnTime=spawnTime;}
        internal bool HeExplosive; // 0.22.86 HE
        internal int BlastSeq;internal int? BlastCrewTotal,BlastCrewAlive;
        internal void Add(string key,string text,string stage,bool terminal=false,bool report=true,int? related=null){Timeline.Add(key,stage,text,terminal);if(report&&Seen.Add(stage+":"+key))Record(new(terminal?"outcome":"interaction",stage,text,HitId:active?.HitId,RelatedEntity:related));}
    }
    internal sealed record PendingLayer(short Index,string Label,string Stage,string? HitId,double? EntryBudget,string[]? Members);
    private sealed class Scope
    {
        internal readonly PenetrationSimulation Sim;internal readonly short Index;internal readonly int Count;
        internal string? Label;internal string? Key;internal bool Solid,Armour;internal readonly string Stage;
        internal double? EntryBudget;internal string[]? Members;
        internal string? HitId;internal CombatLayerInfo? Layer;internal int StartRow=-1;
        internal Scope(PenetrationSimulation sim,short index){Sim=sim;Index=index;Count=sim.FragmentCount;Stage=RuntimeTandem.DamageStage;}
    }
    private sealed record Pool(IntPtr Register,VehicleComponent Owner,bool Alive,float Value);
    [ThreadStatic] private static Shot? current;
    [ThreadStatic] private static Scope? active;
    // A synchronous comparison is not a live combat report. Preserve any enclosing
    // capture instead of clearing history or changing global report readiness.
    internal static IDisposable SuspendForComparison()=>new ComparisonSuspension();
    private sealed class ComparisonSuspension:IDisposable
    {
        private readonly Shot? previousShot=current;
        private readonly Scope? previousScope=active;
        private readonly int thread=Environment.CurrentManagedThreadId;
        private bool disposed;
        internal ComparisonSuspension(){current=null;active=null;}
        public void Dispose()
        {
            if(disposed)return;
            if(Environment.CurrentManagedThreadId!=thread)throw new InvalidOperationException("Comparison report scope must finish on its original thread.");
            current=previousShot;active=previousScope;disposed=true;
        }
    }
    private static readonly Dictionary<IntPtr,Pool> pools=new();
    internal static readonly HitPanelBuffer Buffer=new();
    internal static readonly CombatReportHistory History=new();
    private static IntPtr playerVehicle;
    private sealed record LaunchOwner(IntPtr Vehicle,string Name,int Id,float Spawn,string Definition);
    private static readonly Dictionary<IntPtr,LaunchOwner> launchOwners=new();
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Launch))]
    private static void Launched(ProjectileRegister __instance,Il2CppSystem.Object source,ref ProjectileFireInfo fireInfo)
    {var projectileId=fireInfo.ProjectileID;Safe(()=>{var cannon=source?.TryCast<CannonBehaviour>()?.mount?.TryCast<Cannon>();if(cannon==null||!__instance.activeIdMap.TryGetValue(projectileId,out var i))return;var p=__instance.pool[i];var transform=p.transform?.Pointer??IntPtr.Zero;if(transform==IntPtr.Zero)return;if(launchOwners.Count>=2048)launchOwners.Clear();launchOwners[transform]=new(cannon.Vehicle.Behaviour?.Pointer??IntPtr.Zero,CombatReportTruth.Text(VehicleName(cannon),"unknown"),p.ID,p.spawnTime,p.Definition.Guid.ToString());});}
    private static ConfigEntry<bool> enabled=null!,console=null!;
    private static ConfigEntry<string> corner=null!;
    private static int number,mainThread;
    internal static bool Ready;
    [ThreadStatic] private static int directDamageDepth;
    [ThreadStatic] private static IntPtr directDamageRegister;
    internal readonly record struct HealthScope(int Depth,IntPtr Register);
    private static float playingUntil;
    private static bool editor=true;
    internal static bool IsEditor=>editor;
    private static int warnings;private static int probes;private static int poolProbes,healthProbes;
    private static bool HealthProbeEnabled(bool registration)=>ReleaseDiagnostics.Enabled&&(registration?poolProbes<24:healthProbes<48);
    internal static bool ProbeEnabled=>ReleaseDiagnostics.Enabled&&probes<24;
    private static void HealthProbe(bool registration,string text){if(!HealthProbeEnabled(registration))return;if(registration)poolProbes++;else healthProbes++;Plugin.ModLog.LogInfo("[Damage feed health] "+text);}
    internal static void Probe(string text){if(!ProbeEnabled)return;probes++;Plugin.ModLog.LogInfo("[Damage feed probe] "+text);}
    internal static bool Enabled=>Ready&&enabled?.Value==true;
    internal static bool Left=>corner?.Value=="left";
    internal static bool Show=>Enabled&&!editor&&Time.unscaledTime<playingUntil&&!Cursor.visible;
    internal static void Configure(ConfigFile config)
    {
        mainThread=Environment.CurrentManagedThreadId;
        enabled=config.Bind("Damage feed","Enabled",false,"Show a compact live damage feed in Play. Toggle with the global Sprocket Keybinds action \"Toggle damage feed in Play\" (unbound by default).");
        corner=config.Bind("Damage feed","Corner","right",new ConfigDescription("Upper screen corner, below native HUD. Cycle with the global Sprocket Keybinds action \"Cycle damage feed corner\" (unbound by default).",new AcceptableValueList<string>("right","left")));
        console=config.Bind("Damage feed","Console debug",false,"Also log emitted player damage-feed rows.");
        Safe(()=>CombatReportStore.Load(History));
        CombatReportMenu.Configure();
        HitPanelHud.Configure(config);
        CombatReportPanel.Configure(config);
        DamageFeedKeys.Configure();
    }
    private static void Safe(Action action)
    {try{action();}catch(Exception ex){if(warnings++<4)Plugin.ModLog.LogWarning("[Damage feed] Optional report unavailable: "+ex.Message);}}
    internal static void DrawOptions(IGUIElementDrawer ui,Action refresh)
    {
        if(enabled==null)return;
        var menuTip=new UITooltip{Header="Combat report",Body="View the last ten impacts in game. Set Combat report in Settings / Mod keybinds; default unbound."};
        ui.Button("Combat report — last 10 impacts",Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)CombatReportMenu.Open),ref menuTip);
        ui.InfoField("Combat report: "+History.Shots.Count+" impacts. Last session remains available after Play.",2);
        var viewTip=new UITooltip{Header="Last Play session",Body="Open the saved local HTML report in your default browser. No gameplay input capture."};
        ui.Button("View combat report",Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(()=>Safe(()=>{CombatReportStore.Save(History);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(CombatReportStore.DirectoryPath,"last-session.html")){UseShellExecute=true});}))),ref viewTip);
        ui.InfoField("Damage feed visibility and corner are global Sprocket Keybinds actions, unbound by default. This panel keeps the combat report controls.",2);
    }
    private static void SetEnabled(bool value)
    {
        if(enabled==null||enabled.Value==value)return;
        enabled.Value=value;
        if(!value)Buffer.Clear();
        Plugin.ModLog.LogInfo("[Damage feed] Play HUD "+(value?"enabled":"disabled")+" from the global keybind.");
    }
    private static void CycleCorner()
    {
        if(corner==null)return;
        corner.Value=Left?"right":"left";
        Plugin.ModLog.LogInfo("[Damage feed] Corner set to "+corner.Value+" from the global keybind.");
    }
    [HarmonyPostfix,HarmonyPatch(typeof(PlayerController),nameof(PlayerController.UpdateControl))]
    private static void Playing(PlayerController __instance)
    {Safe(()=>{
        if(__instance.controlTarget!=null){if(!History.Playing){Probe("native controlled-vehicle update; enabling Play observation");History.Begin();}playerVehicle=__instance.ControlledVehicle?.Pointer??IntPtr.Zero;editor=false;playingUntil=Time.unscaledTime+1;}
        // Global controls are only acted on during fresh Play with the cursor released.
        // Sprocket Keybinds itself suppresses menu rebinding, unfocused windows and
        // text-field input, so the feed never reacts while configuring bindings.
        if(!Ready||editor||Cursor.visible||Time.unscaledTime>=playingUntil)return;
        DamageFeedKeys.Configure();
        var press=DamageFeedKeys.Poll();
        if(press.Toggle)SetEnabled(!Enabled);
        if(press.Corner)CycleCorner();
    });}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleEditorScenarioGameState),nameof(VehicleEditorScenarioGameState.OnPlayerStateChanged))]
    private static void State(VehicleEditorScenarioGameState __instance,PlayerStateChangeEventArgs __1)
    {Safe(()=>{if(__1?.NewState==null)return;editor=__instance.editorPlayerState?.Pointer==__1.NewState.Pointer;if(ProbeEnabled)Probe("committed state editor="+editor);if(editor){Buffer.Clear();playingUntil=0;if(History.Playing){History.End();CombatReportStore.Save(History);}}if(Ready)DamageFeedKeys.Configure();});}
    [HarmonyPrefix,HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
    private static void BeginImpact(ref ProjectileInstance __0,out Shot? __state)
    {__state=current;current=null;try{if(ProbeEnabled)Probe($"impact enabled={Enabled} editor={editor} freshControl={Time.unscaledTime<playingUntil} cursor={Cursor.visible}");if(Ready&&!editor&&Time.unscaledTime<playingUntil&&Environment.CurrentManagedThreadId==mainThread){current=new Shot(++number,__0.ID,__0.spawnTime){Profile=RuntimeShellSelection.ProjectileProfile(__0)?.Label??__0.Definition.Name,Speed=__0.velocity.magnitude,Player=playerVehicle};if(launchOwners.TryGetValue(__0.transform?.Pointer??IntPtr.Zero,out var launch)&&CombatReportTruth.LaunchMatches(__0.ID,__0.spawnTime,__0.Definition.Guid.ToString(),launch.Id,launch.Spawn,launch.Definition)){current.Shooter=launch.Vehicle;current.ShooterName=launch.Name;}}}catch(Exception ex){if(warnings++<4)Plugin.ModLog.LogWarning("[Damage feed] Impact report unavailable: "+ex.Message);}}
    [HarmonyFinalizer,HarmonyPatch(typeof(ArmourPiercingProjectileFunction),nameof(ArmourPiercingProjectileFunction.HitDamageModel))]
    private static void EndImpact(Shot? __state)
    {Safe(()=>{if(current is {} shot){
                if(HeDefer(shot))return; // 0.22.86 HE
                // 0.22.16: a remembered skipped plate whose line never reaches
                // another solid block still gets a concrete reason.
                foreach(var row in CombatReportNoise.Collapse(NativeMaterialReactions.ClosePending().Select(p=>new CombatReportEvent("materialUnavailable","","Skipped composite plate #"+p.Entity+": line ended before another solid block; cassette and passive resistance not applied.",HitId:p.HitId,RelatedEntity:p.Entity))))shot.Record(row);
                if(shot.Events.Count==0)shot.Events.Add(new("outcome","","Unknown: no resolved native armour interaction"));var crew=CrewCounts(shot.TargetRegister);var crewTotal=shot.BlastCrewTotal is >0?shot.BlastCrewTotal:crew.Total;var crewAlive=shot.BlastCrewTotal is >0?(crew.Alive is {} ca?Math.Min(ca,shot.BlastCrewAlive??shot.BlastCrewTotal.Value):shot.BlastCrewAlive):crew.Alive;var report=new CombatReportShot(shot.Number,shot.Direction,shot.Profile,shot.Speed,shot.Events.ToArray(),shot.OmittedEvents,shot.InitialPenetration,shot.Target,crewTotal,crewAlive,shot.ShooterName);if(Enabled)Buffer.Add(Time.unscaledTime,HitBlockBuilder.Build(report));History.Add(report);if(shot.BlastSeq>0)History.RegisterBlastSeq(shot.BlastSeq,report.Number);if(ProbeEnabled)Probe("native="+shot.NativeId+" summary=2 rows");if(console.Value)foreach(var line in shot.Timeline.All.Select(t=>$"#{shot.Number} {t}"))if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Damage feed] native="+shot.NativeId+" spawn="+shot.SpawnTime+" "+line);}});current=__state;}
    [HarmonyPrefix,HarmonyPatch(typeof(ExplosiveProjectileFunction),nameof(ExplosiveProjectileFunction.HitDamageModel))]
    private static void BeginExplosive(ref ProjectileInstance __0,out Shot? __state){if(HeResume(ref __0,out __state))return;BeginImpact(ref __0,out __state);if(current!=null)current.HeExplosive=true;} // 0.22.86 HE replaced
    [HarmonyFinalizer,HarmonyPatch(typeof(ExplosiveProjectileFunction),nameof(ExplosiveProjectileFunction.HitDamageModel))]
    private static void EndExplosive(Shot? __state)=>EndImpact(__state);
    [HarmonyPrefix,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    [HarmonyPriority(Priority.First)]
    private static void Begin(PenetrationSimulation sim,short index,out Scope? __state)
    {__state=active;active=null;Safe(()=>{if(current!=null&&index>=0&&index<sim.FragmentCount&&(sim.fragments[index].flags&FragmentFlag.OriginalPenetrator)!=0){active=new(sim,index);if(active.Stage!="Precursor"&&current.InitialPenetration==null){var f=sim.fragments[index];current.InitialPenetration=CombatReportTruth.Value(f.GetBasePenetration(sim.projectileProfiles[f.projectileProfileIndex].penetratorConstant)*1000);}}});}
    internal static void LayerStarted(NativeArmourBlockContext context)
    {ArmourComparisonEvidence.Reached(context);Safe(()=>{
        if(current==null||active is not {} scope||scope.Sim.Pointer!=context.Sim.Pointer||scope.Index!=context.Index)return;
        var block=context.Block;var hits=context.Intersections;int e=block.EnterIntersectionIndex,x=block.ExitIntersectionIndex;
        if(!block.Valid||e<0||x<0||e>=context.IntersectionCount||x>=context.IntersectionCount||e>=hits.Length||x>=hits.Length)return;
        var key=$"{scope.Sim.Pointer}:{scope.Index}:{e}:{x}";if(scope.HitId==key)return;
        var first=hits[e];var last=hits[x];var indices=new HashSet<int>{first.objectIndex,last.objectIndex};
        if(block.IntersectedObjectIndices is {} extra)foreach(var i in extra){if(indices.Count<32)indices.Add(i);}
        for(int i=0;i<context.IntersectionCount&&i<hits.Length&&indices.Count<32;i++)if(hits[i].distance>=first.distance&&hits[i].distance<=last.distance)indices.Add(hits[i].objectIndex);
        var parts=new List<CombatLayerPart>();var reader=scope.Sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
        var air=block.MaterialIndex<0;
        double FirstDistance(int objectIndex){for(int i=0;i<context.IntersectionCount&&i<hits.Length;i++)if(hits[i].objectIndex==objectIndex&&hits[i].distance>=first.distance)return hits[i].distance;return double.MaxValue;}
        foreach(var index in indices.OrderBy(FirstDistance))
        {
            if(index<0||index>=scope.Sim.objects.Length)continue;int entity=scope.Sim.objects[index].ID;
            VehicleComponent? owner=null;if(reader!=null)reader.TryGet<VehicleComponent>(new VUID(entity),out owner);
            var plate=owner?.TryCast<PlateStructure>();string? material=plate?.armourTechID;double? thickness=null,angle=null;bool armour=plate!=null;
            if(owner?.ComponentID==CompositeModule.ComponentId){material=CompositeModule.MaterialId;armour=true;thickness=CompositeModule.DepthMm*Math.Abs(owner.TryCast<VehicleObjectModel>()?.GetModelScale().y??1);}
            if(owner!=null&&context.Impact.ArmourSnapshot?.Bindings?.TryGetCassette(owner.ComponentID,out var cassette)==true){material=cassette.MaterialId;armour=true;}
            for(int i=0;i<context.IntersectionCount&&i<hits.Length;i++)
            {
                var hit=hits[i];if(hit.objectIndex!=index||hit.distance<first.distance||hit.distance>last.distance)continue;
                var normal=context.Structure.GetIntersectionNormal(ref hit).normalized;
                angle=Math.Acos(Math.Clamp(Math.Abs(Vector3.Dot(scope.Sim.fragments[scope.Index].direction.normalized,normal)),0,1))*180/Math.PI;
                if(plate!=null)thickness=NativeHitFace.ReportThickness(scope.Sim,hit,plate);
                else if(armour&&owner!=null)thickness=ActiveEraThickness(owner,context.Impact.ArmourSnapshot);
                break;
            }
            // 0.22.16: an air block is not a plate layer. It never claims plate
            // thickness or armour; the row exists only for its gap and reasons.
            if(air){armour=false;thickness=null;angle=null;}
            var definition=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==material);
            var name=ResolveLabel(scope.Sim,entity);if(name.Equals("Unnamed Structure",StringComparison.OrdinalIgnoreCase)||plate!=null&&CombatReportView.IsNumericName(name))name="Plate #"+entity;
            parts.Add(new(entity,name,material,definition==null?EraDisplayName.Material(material):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(definition),thickness,angle,armour));
        }
        var fragment=scope.Sim.fragments[scope.Index];scope.EntryBudget=CombatReportTruth.Value(fragment.GetBasePenetration(scope.Sim.projectileProfiles[fragment.projectileProfileIndex].penetratorConstant)*1000);
        var gap=air?Math.Abs(last.distance-first.distance)*1000:double.NaN;
        scope.HitId=key;scope.Layer=new(key,parts,parts.Count>1,air&&double.IsFinite(gap)?Math.Max(0,gap):null);scope.StartRow=current.Events.Count;
        current.Record(new("layerStart",scope.Stage,"Reached layer",scope.EntryBudget,HitId:key,Layer:scope.Layer));
    });}
    private static double? ActiveEraThickness(VehicleComponent owner,ArmourCatalogueSnapshot? snapshot)
    {
        if(snapshot?.Bindings?.TryGetCassette(owner.ComponentID,out var binding)!=true||binding==null||
            snapshot.Catalogue?.ForResponse(binding.ResponseId) is not {} response||
            !binding.MatchesRecipe(response.ResponseId,response.Kind,response.CompatibleMaterialIds)||
            owner.TryCast<VehicleObjectModel>() is not {} model)return null;
        var scale=model.GetModelScale();
        if(SprocketExpandedBallistics.Materials.EraPresetRegistry.Visual(owner.ComponentID,out var preset,out var active)&&active&&preset.WholeCells&&preset.StageIndex>0)
            return CombatReportMeasurement.ActiveEraThickness(ModularCellSpec.ForVariant(preset.ModularVariant),preset.StageIndex,scale.y);
        if(model.Model?.CollisionMesh is not {} mesh)return null;
        var measured=PlacedPartDepth.Measure(mesh.vertices.Select(v=>new System.Numerics.Vector3(v.x,v.y,v.z)).ToArray(),mesh.triangles.ToArray(),new(scale.x,scale.y,scale.z));
        return double.IsFinite(measured)&&measured>0?measured:null;
    }
    internal static void UpdateEraLayer(PenetrationSimulation sim,short index,int entity,string material,string responseId,double thickness,double angle)
    {ArmourComparisonEvidence.Update(sim,index,entity,material,thickness,angle);Safe(()=>{
        if(current==null||active is not {} scope||scope.Sim.Pointer!=sim.Pointer||scope.Index!=index||scope.Layer==null)return;
        var definition=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==material);
        var display=definition==null?EraDisplayName.Material(material,responseId):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(definition);
        var parts=scope.Layer.Parts.Select(p=>p.Entity==entity?p with {MaterialId=material,MaterialLabel=display,ThicknessMm=CombatReportTruth.Value(thickness),AngleDegrees=CombatReportTruth.Value(angle),Armour=true}:p).ToArray();
        scope.Layer=scope.Layer with {Parts=parts};if(scope.StartRow>=0&&scope.StartRow<current.Events.Count)current.Events[scope.StartRow]=current.Events[scope.StartRow] with {Layer=scope.Layer};
    });}
    [HarmonyPostfix,HarmonyPatch(typeof(StructureIntersection),nameof(StructureIntersection.GetNextMaterialBlock))]
    [HarmonyAfter("sprocket.expandedballistics.armourresponses")]
    private static void Block(MaterialBlock __result,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<StructureIntersection> __0,int __1)
    {Safe(()=>{
        if(active is not {} scope||current==null||!__result.Valid||__result.MaterialIndex<0)return;
        var e=__result.EnterIntersectionIndex;var x=__result.ExitIntersectionIndex;
        if(e<0||x<0||e>=__1||x>=__1||e>=__0.Length||x>=__0.Length)return;
        var first=__0[e];var last=__0[x];if(first.objectIndex<0||first.objectIndex>=scope.Sim.objects.Length)return;
        var id=scope.Sim.objects[first.objectIndex].ID;
        bool combined=first.objectIndex!=last.objectIndex||(__result.IntersectedObjectIndices?.Length??0)>1;
        for(int i=0;i<__1&&i<__0.Length;i++){var candidate=__0[i];if(candidate.distance>first.distance&&candidate.distance<last.distance&&candidate.objectIndex!=first.objectIndex)combined=true;}
        if(current is {} shot&&scope.Sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader?.TryGet<VehicleComponent>(new VUID(id),out var target)==true&&target!=null)
        {
            shot.Direction=CombatReportTruth.Direction(shot.Player,shot.Shooter,target.Vehicle?.Behaviour?.Pointer??IntPtr.Zero);
            shot.Target??=VehicleName(target);
            if(shot.TargetRegister==IntPtr.Zero)shot.TargetRegister=scope.Sim.damageApplier?.Pointer??IntPtr.Zero;
        }
        if(combined)
        {
            var members=new HashSet<int>{first.objectIndex,last.objectIndex};
            for(int i=0;i<__1&&i<__0.Length&&members.Count<64;i++)
            {var hit=__0[i];if(hit.distance>=first.distance&&hit.distance<=last.distance)members.Add(hit.objectIndex);}
            if(__result.IntersectedObjectIndices is {} intersected)
                foreach(var member in intersected){if(members.Count>=64)break;members.Add(member);}
            var labels=members.Where(i=>i>=0&&i<scope.Sim.objects.Length).Select(i=>PlateReportLabel(scope.Sim,scope.Sim.objects[i].ID)).Distinct().ToArray();
            scope.Label="Combined armour block";scope.Members=labels;
        }
        else scope.Label=PlateReportLabel(scope.Sim,id);
        var reportReader=scope.Sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
        scope.Armour=reportReader?.TryGet<PlateStructure>(new VUID(id),out var reportPlate)==true&&reportPlate!=null;
        if(combined)scope.Armour=scope.Members?.Any(m=>m.Contains("[Material:",StringComparison.Ordinal))==true;
        scope.Key=$"{scope.Sim.Pointer}:{scope.Index}:{id}";
        var original=scope.Sim.fragments[scope.Index];
        scope.EntryBudget??=CombatReportTruth.Value(original.GetBasePenetration(scope.Sim.projectileProfiles[original.projectileProfileIndex].penetratorConstant)*1000);
        if(scope.Stage!="Precursor")current!.InitialPenetration??=scope.EntryBudget;
        scope.Solid=true;
    });}
    [HarmonyFinalizer,HarmonyPatch(typeof(CompoundStructure),nameof(CompoundStructure.SimulateFragment))]
    [HarmonyPriority(Priority.Last),HarmonyAfter("sprocket.expandedballistics.armourresponses")]
    private static void End(Scope? __state,Exception? __exception)
    {Safe(()=>{
        if(active is not {} scope||current is not {} shot||scope.Index>=scope.Sim.FragmentCount)return;
        var f=scope.Sim.fragments[scope.Index];var budgets=new List<double>();
        for(int i=scope.Count;i<scope.Sim.FragmentCount;i++)
        {var c=scope.Sim.fragments[i];if(c.parentIndex==scope.Index&&(c.flags&FragmentFlag.OriginalPenetrator)!=0&&(c.flags&FragmentFlag.Killed)==0)budgets.Add(c.GetBasePenetration(scope.Sim.projectileProfiles[c.projectileProfileIndex].penetratorConstant)*1000);}
        var outcome=DamageFeedTruth.Outcome((f.flags&FragmentFlag.Penetrated)!=0,(f.flags&FragmentFlag.Embedded)!=0,(f.flags&FragmentFlag.Deflected)!=0,budgets.Count,__exception!=null);
        var label=scope.Label;
        if(label==null&&outcome==PlateOutcome.Stopped&&shot.Pending.TryGetValue(scope.Sim.Pointer,out var pending)&&f.parentIndex==pending.Index&&scope.Stage==pending.Stage)
        {
            // Native embedded continuation finishes the same reached block on its child.
            // Keep its proven parent metadata rather than creating an unrelated stop row.
            label=pending.Label;scope.HitId=pending.HitId;scope.EntryBudget=pending.EntryBudget;scope.Members=pending.Members;
        }
        if(label==null)return;
        if(scope.Members is {Length:>0})
            foreach(var group in scope.Members.Chunk(3))RecordArmourInteraction(scope.Sim,scope.Index,"blockMembers","Native grouped objects — membership, not confirmed hits:\n• "+string.Join("\n• ",group));
        if(scope.Solid&&outcome==PlateOutcome.Unknown&&(f.flags&FragmentFlag.Embedded)!=0&&budgets.Count==1&&(shot.Pending.Count<32||shot.Pending.ContainsKey(scope.Sim.Pointer)))shot.Pending[scope.Sim.Pointer]=new(scope.Index,label,scope.Stage,scope.HitId,scope.EntryBudget,scope.Members);
        if(outcome==PlateOutcome.Penetrated&&scope.Solid)
        {
            var remaining=DamageFeedTruth.Remaining(budgets);
            shot.Add(scope.Key!,"Passed "+label+(remaining is {} mm?$" - {mm:0} mm after plate":""),scope.Stage,report:false);RecordArmourInteraction(scope.Sim,scope.Index,scope.Armour?"plateResidual":"componentResidual","Traversed "+label+"; block exit includes native resistance and any listed extra reactions.",scope.EntryBudget,remaining);shot.Pending.Remove(scope.Sim.Pointer);
        }
        else if(outcome==PlateOutcome.Stopped)
        {shot.Add("stop:"+scope.Sim.Pointer,"Stopped in "+label,scope.Stage,true,report:false);RecordArmourInteraction(scope.Sim,scope.Index,"nativeStop",scope.Members!=null||label.StartsWith("Combined armour block",StringComparison.Ordinal)?"Native stop in grouped block; exact stopping object unresolved. No outgoing original core recorded.":"Native stop in "+label+"; no outgoing original core recorded.",scope.EntryBudget);shot.Pending.Remove(scope.Sim.Pointer);}
        else if(outcome==PlateOutcome.Deflected&&scope.Solid)shot.Add(scope.Key!,"Deflected at "+label,scope.Stage,true);
        // 0.22.78 HESH begin
        if(outcome==PlateOutcome.Stopped&&HeshSurfaceStop(scope.Sim))RecordArmourInteraction(scope.Sim,scope.Index,"materialNote",HeshRules.SurfaceText);
        // 0.22.78 HESH end
    });active=__state;}
    // 0.22.78 HESH begin
    // A HESH original that stops without having made a scab detonated on the surface of what stopped it (minAngleDegrees above 10 only).
    private static bool HeshSurfaceStop(PenetrationSimulation sim)=>RuntimeSpall.Hesh.SurfaceDetonation&&RuntimeSpall.Impact is {} impact&&impact.Behavior=="hesh"&&!impact.PayloadBursts.Contains(sim.Pointer);
    // 0.22.78 HESH end
    private static string ResolveLabel(PenetrationSimulation sim,int id)
    {
        var reader=sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
        if(reader!=null&&reader.TryGet<VehicleComponent>(new VUID(id),out var owner)&&owner!=null&&owner.VUID.Value==id)return Label(owner);
        return "Armour #"+id;
    }
    private static string PlateReportLabel(PenetrationSimulation sim,int id)
    {
        var label=ResolveLabel(sim,id);
        var reader=sim.damageApplier?.TryCast<VehicleHealthRegister>()?.objectReader;
        if(reader==null||!reader.TryGet<VehicleComponent>(new VUID(id),out var owner)||owner==null||owner.VUID.Value!=id)
            return label+" [material unavailable]";
        var plate=owner.TryCast<PlateStructure>();
        if(plate==null)return label;
        if(label.Equals("Unnamed Structure",StringComparison.OrdinalIgnoreCase))label="Plate #"+id;
        var materialId=plate?.armourTechID;
        if(string.IsNullOrWhiteSpace(materialId))return label+" [material unavailable]";
        var material=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==materialId);
        var materialLabel=material==null?EraDisplayName.Material(materialId):SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(material);
        return label+" [Material: "+materialLabel+"]";
    }
    /// <summary>Blueprint name of the vehicle a component belongs to (the design name shown in the garage), or null.</summary>
    private static string? VehicleName(VehicleComponent? component)
    {
        try{var clean=CombatReportTruth.Text(component?.Vehicle?.DesignInfo?.Name,"");return clean.Length==0?null:clean;}
        catch(Exception){return null;}
    }
    /// <summary>Crew seats of one vehicle that the health tracker knows, and how many are alive after this shot (null when none are known).</summary>
    private static (int? Total,int? Alive) CrewCounts(IntPtr register)
    {
        if(register==IntPtr.Zero)return (null,null);
        int total=0,alive=0;
        foreach(var pool in pools.Values)
        {
            if(pool.Register!=register||pool.Owner==null)continue;
            try{if(pool.Owner.TryCast<CrewSeat>()==null)continue;}catch(Exception){continue;}
            total++;if(pool.Alive)alive++;
        }
        return total>0?(total,alive):(null,null);
    }
    private static string Label(VehicleComponent owner)
    {
        var plate=owner.TryCast<PlateStructure>();
        // Native editor structureName reads/writes PlateStructure.Mesh.Name, not StructureBlueprint.Name.
        if(plate!=null)return DamageFeedTruth.StructureLabel(plate.Mesh?.Name,plate.Blueprint?.Name,"Plate #"+owner.VUID.Value);
        var crew=owner.TryCast<CrewSeat>();if(crew!=null)return DamageFeedTruth.Label(crew.SeatBlueprint?.Name,"Crew member #"+owner.VUID.Value);
        if(owner.TryCast<VehicleObjectModel>()!=null&&SprocketExpandedBallistics.Materials.EraPresetRegistry.Visual(owner.ComponentID,out _,out _))
            return EraDisplayName.Component(owner.ComponentID);
        return DamageFeedTruth.Label(owner.ComponentID switch {"reliktCassette"=>"Relikt ERA brick", "reliktTurretCassette"=>"Relikt turret ERA brick", "kontakt1Cassette"=>"Kontakt-1 ERA brick", "kontakt5Cassette"=>"Kontakt-5 ERA brick", "turretDrive"=>"Turret drive",_=>owner.ComponentID},"Component #"+owner.VUID.Value);
    }
    internal static string EraReportLabel(PenetrationSimulation sim,int id)=>ResolveLabel(sim,id);
    internal static void EraActivated(PenetrationSimulation sim,int id,EraCell cell,string kind,bool live,bool first)
    {Safe(()=>{current?.Timeline.Era($"{sim.Pointer}:{cell}",RuntimeTandem.DamageStage,live,first,kind,ResolveLabel(sim,id));if(live&&first)RecordArmourInteraction(sim,active?.Sim.Pointer==sim.Pointer?active.Index:(short)0,kind,ResolveLabel(sim,id),relatedEntity:id);});}
    internal static void EraBudget(PenetrationSimulation sim,short index,EraCell cell,double before,double angle,double angularWeight)
    {Safe(()=>{if(current==null)return;var f=sim.fragments[index];var after=f.GetBasePenetration(sim.projectileProfiles[f.projectileProfileIndex].penetratorConstant)*1000;current.Timeline.EraBudget($"{sim.Pointer}:{cell}",RuntimeTandem.DamageStage,before,after);RecordArmourInteraction(sim,index,"eraResidual",$"ERA response before plate; impact {angle:0.#}°; angle weight {angularWeight:P1}",before,after);});}
    internal static void RecordCompositeModule(PenetrationSimulation sim,short index,CompositeModuleInterval layer,ProtectionThreat threat,bool grouped)
    {Safe(()=>{if(current is not {} shot||editor)return;var scope=active?.Sim.Pointer==sim.Pointer?active:null;
        var evt=CompositeModule.Report(layer,threat,scope?.Stage??RuntimeTandem.DamageStage,scope?.HitId??$"{sim.Pointer}:{index}",grouped);
        if(scope?.Layer is {} nativeLayer&&scope.StartRow>=0&&scope.StartRow<shot.Events.Count)
        {
            if(!grouped)
            {
                var own=evt.Layer! with{Id=nativeLayer.Id};scope.Layer=own;
                shot.Events[scope.StartRow]=shot.Events[scope.StartRow] with{Layer=own};
                shot.Record(evt with{HitId=nativeLayer.Id,Layer=null});return;
            }
            var remaining=nativeLayer.Parts.Where(p=>p.Entity!=layer.Entity).ToArray();
            scope.Layer=nativeLayer with{Parts=remaining,Combined=remaining.Length>1};
            shot.Events[scope.StartRow]=shot.Events[scope.StartRow] with{Layer=scope.Layer};
        }
        foreach(var e in CompositeModule.ReportEvents(evt))shot.Record(e);
    });}
    internal static void RecordPaidIntervals(PenetrationSimulation sim,short index,MaterialIntervalPlan plan,double before,double? after,bool stopped)
    {
        var kind=stopped?"materialStop":"materialReaction";var text=plan.Text;
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[Protection] "+text);
        ArmourComparisonEvidence.Record(sim,index,kind,text,before,after,null);
        Safe(()=>{if(current is not {} shot||editor)return;var scope=active?.Sim.Pointer==sim.Pointer?active:null;
            shot.Record(new CombatReportEvent(kind,scope?.Stage??RuntimeTandem.DamageStage,CombatReportTruth.EventText(text),CombatReportTruth.Value(before),CombatReportTruth.Value(after),scope?.HitId){PaidIntervals=plan.Pieces});});
    }
    internal static void RecordArmourInteraction(PenetrationSimulation sim,short fragmentIndex,string kind,string label,double? beforeMm=null,double? afterMm=null,int? relatedEntity=null,string? hitId=null)
    {ArmourComparisonEvidence.Record(sim,fragmentIndex,kind,label,beforeMm,afterMm,relatedEntity);Safe(()=>{if(current is not {} shot||editor)return;var scope=active?.Sim.Pointer==sim.Pointer?active:null;var key=hitId??scope?.HitId;var stage=scope?.Stage??RuntimeTandem.DamageStage;shot.Record(new(DamageFeedTruth.Label(kind,"interaction"),stage,CombatReportTruth.EventText(label),CombatReportTruth.Value(beforeMm),CombatReportTruth.Value(afterMm),key,RelatedEntity:relatedEntity));});}

    internal static void PoolCreated(VehicleHealthRegister __instance,VehicleComponent __0,IDurable __result)
    {Safe(()=>{if(Environment.CurrentManagedThreadId!=mainThread||__result==null||__0==null)return;if(pools.Count<8192||pools.ContainsKey(__result.Pointer)){var info=__result.HealthInfo;pools[__result.Pointer]=new(__instance.Pointer,__0,info.Alive,info.Current);if(HealthProbeEnabled(true))HealthProbe(true,$"created register={__instance.Pointer} durable={__result.Pointer} component={__0.ComponentID} vuid={__0.VUID.Value} value={info.Current}");}});}
    
    internal static void HealthChanged(VehicleHealthRegister __instance,IDurable __0,HealthChangeInfo __1)
    {Safe(()=>{
        if(Environment.CurrentManagedThreadId!=mainThread||__0==null)return;
        if(!pools.TryGetValue(__0.Pointer,out var previous)){if(HealthProbeEnabled(false))HealthProbe(false,$"unregistered callback register={__instance.Pointer} durable={__0.Pointer} delta={__1.Delta}");return;}
        var info=__0.HealthInfo;bool alive=info.Alive;pools[__0.Pointer]=previous with {Alive=alive,Value=info.Current};
        if(HealthProbeEnabled(false))HealthProbe(false,$"callback durable={__0.Pointer} component={previous.Owner.ComponentID} delta={__1.Delta} alive={alive} enabled={Enabled} editor={editor}");
        Emit(previous,alive,__1.Delta);
    });}
    private static void Emit(Pool previous,bool alive,float delta)
    {
        if(!Ready||editor||Time.unscaledTime>=playingUntil||!DamageFeedTruth.DirectDamage(current!=null,directDamageDepth>0,delta,previous.Register==directDamageRegister))return;
        var owner=previous.Owner;if(owner==null||owner.VehicleRoot is not {} root)return;
        if(current is {} ownerShot){ownerShot.Direction=CombatReportTruth.Direction(ownerShot.Player,ownerShot.Shooter,owner.Vehicle?.Behaviour?.Pointer??IntPtr.Zero);ownerShot.Target??=VehicleName(owner);ownerShot.TargetRegister=previous.Register;}
        var label=Label(owner);bool dead=DamageFeedTruth.Died(previous.Alive,alive,delta);
        label=HeBlast.Tag(owner,label); // 0.22.86 HE
        // The fallback crew label is already "Crew member #12"; strip it so the text is "Crew member #12 killed", not "Crew member Crew member #12 killed".
        var crewLabel=CombatDamageSummary.CrewLabel(label);
        string line=owner.TryCast<CrewSeat>()!=null?(dead?$"Crew member {crewLabel} killed":$"Crew member {crewLabel} wounded"):(dead?label+" destroyed":label+" damaged");
        
        if(current is {} shot)
        {shot.Add("damage:"+root.Pointer+":"+owner.VUID.Value+":"+line,line,RuntimeTandem.DamageStage,related:(int)owner.VUID.Value);}

    }
    internal static void Linked(VehicleHealthRegister register,IDurable durable,int id)
    {Safe(()=>{if(Environment.CurrentManagedThreadId!=mainThread||durable==null)return;if(register.objectReader?.TryGet<VehicleComponent>(new VUID(id),out var owner)==true&&owner!=null&&owner.VUID.Value==id)PoolCreated(register,owner,durable);else if(HealthProbeEnabled(true))HealthProbe(true,$"link unresolved register={register.Pointer} durable={durable.Pointer} id={id}");});}
    internal static void ObserveRegister(VehicleHealthRegister register)
    {Safe(()=>{if(Environment.CurrentManagedThreadId!=mainThread)return;var lookup=register.register?.lookup;if(lookup==null)return;foreach(var pair in lookup){var durable=pair.Value;if(durable==null)continue;if(!pools.TryGetValue(durable.Pointer,out var previous)){Linked(register,durable.Cast<IDurable>(),pair.Key);continue;}var info=durable.HealthInfo;var delta=info.Current-previous.Value;pools[durable.Pointer]=previous with {Alive=info.Alive,Value=info.Current};if(float.IsFinite(delta)&&delta<0){if(HealthProbeEnabled(false))HealthProbe(false,$"observed native pool durable={durable.Pointer} component={previous.Owner.ComponentID} delta={delta} alive={info.Alive}");Emit(previous,info.Alive,delta);}}});}

    internal static HealthScope BeginDirectDamage(VehicleHealthRegister register)
    {
        var previous=new HealthScope(directDamageDepth,directDamageRegister);
        if(current==null||Environment.CurrentManagedThreadId!=mainThread)return previous;
        // Preserve an outer committed delta before resetting the same nested target baseline.
        if(previous.Depth>0&&previous.Register==register.Pointer)ObserveRegister(register);
        // Sample this register before the exact native damage call; unrelated earlier
        // fire/health changes become baseline and cannot be credited to this shot.
        directDamageDepth=0;
        ObserveRegister(register);
        directDamageDepth=current!=null?previous.Depth+1:0;
        directDamageRegister=current!=null?register.Pointer:IntPtr.Zero;
        return previous;
    }
    internal static void EndDirectDamage(VehicleHealthRegister register,HealthScope previous)
    {try{if(directDamageDepth>0&&directDamageRegister==register.Pointer)ObserveRegister(register);}finally{directDamageDepth=previous.Depth;directDamageRegister=previous.Register;}}

    internal static void ReleaseHealth(VehicleHealthRegister __instance)
    {Safe(()=>{foreach(var pair in pools.Where(p=>p.Value.Register==__instance.Pointer).ToArray())pools.Remove(pair.Key);});}
    // 0.22.86 HE begin
    // One impact of an HE round with a perforating part is one shot (HeShotLink): the shot the AP part opened is kept open and the HE call of the same
    // hit continues it, so the number advances once and the report holds one row. A kept shot that no HE call picks up is recorded later, exactly once.
    [ThreadStatic] private static HeShotLink<Shot>? heLink;
    private static bool HeDefer(Shot shot)=>(heLink??=new()).Defer(shot,shot.NativeId,shot.SpawnTime,shot.HeExplosive,HeScopeState.ProxyImpact(shot.NativeId,shot.SpawnTime),HeRecord);
    // Records a kept shot as the AP finalizer would have (EndImpact), then restores the shot that is current now.
    private static void HeRecord(Shot shot){var outer=current;shot.HeExplosive=true;current=shot;EndImpact(outer);}
    private static bool HeResume(ref ProjectileInstance projectile,out Shot? state)
    {
        state=current;
        if(heLink?.Pending==null)return false;
        try
        {
            var shot=heLink.Resume(projectile.ID,projectile.spawnTime,HeRecord);
            state=current;
            if(shot==null)return false;
            shot.HeExplosive=true;current=shot;return true;
        }
        catch(Exception ex){state=current;if(warnings++<4)Plugin.ModLog.LogWarning("[Damage feed] HE shot link unavailable: "+ex.Message);return false;}
    }
    /// <summary>An HE line in the open shot ("materialNote"; with the key of its layer when there is one).</summary>
    internal static void HeNote(string text,string? hitId)
    {Safe(()=>{if(current is not {} shot||editor)return;shot.Record(new("materialNote",RuntimeTandem.DamageStage,CombatReportTruth.EventText(text),HitId:hitId));});}
    /// <summary>The module that owns a pool, as the damage feed registered it (the fallback of the blast filter).</summary>
    internal static VehicleComponent? PoolOwner(IntPtr durable)=>pools.TryGetValue(durable,out var pool)?pool.Owner:null;
    // 0.22.86 HE end
    // 0.22.111 blast back-channel: link BallisticsEffectApi.CurrentExplosionSeq to live or committed HE/HESH shots.
    internal static void RecordBlastTelemetry(int seq,BlastTelemetry telemetry)=>RecordBlastTelemetry(seq,telemetry.Kind,telemetry.CalibreMm,telemetry.Power,telemetry.BreachMm);
    internal static void RecordBlastTelemetry(int seq,string kind,double calibreMm,double power,double breachMm)
    {Safe(()=>{
        if(seq<=0||kind is not ("he" or "hesh"))return;
        var summary=CombatBlastReport.SummaryEvent(kind,calibreMm,power,breachMm,RuntimeTandem.DamageStage);
        if(current is {} shot)
        {
            shot.BlastSeq=seq;
            if(!CombatBlastReport.HasEvent(shot.Events,summary))shot.Record(summary);
            return;
        }
        if(History.Shots.Count>0&&CombatBlastReport.IsHeOrHeshShot(History.Shots[^1]))
        {
            History.RegisterBlastSeq(seq,History.Shots[^1].Number);
            var updated=History.ApplyBlastEvent(seq,summary);
            if(updated!=null&&Enabled)Buffer.Replace(updated.Number,HitBlockBuilder.Build(updated));
        }
    });}
    internal static void AcceptBlastEvent(int seq,CombatReportEvent evt,int? crewAliveAfter,int? crewTotal)
    {Safe(()=>{
        if(seq<=0||evt==null)return;
        if(current is {} live&&live.BlastSeq==seq)
        {
            if(!CombatBlastReport.HasEvent(live.Events,evt))live.Record(evt);
            if(crewTotal is >0 and <=256)
            {
                int ct=crewTotal.Value;
                int ca=Math.Clamp(crewAliveAfter??ct,0,ct);
                live.BlastCrewTotal=ct;
                live.BlastCrewAlive=live.BlastCrewAlive is {} ex?Math.Min(ex,ca):ca;
            }
            return;
        }
        var updated=History.ApplyBlastEvent(seq,evt,crewAliveAfter,crewTotal);
        if(updated!=null&&Enabled)Buffer.Replace(updated.Number,HitBlockBuilder.Build(updated));
    });}
    internal static void Clear(){CombatReportMenu.Clear();DamageFeedKeys.Clear();Buffer.Clear();History.ClearBlastMappings();pools.Clear();launchOwners.Clear();playerVehicle=IntPtr.Zero;directDamageDepth=0;directDamageRegister=IntPtr.Zero;active=null;current=null;DamageFeedHud.Destroy();}
}

[HarmonyPatch]
internal static class RuntimeDamageHealth
{
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Sprocket_DamageModelling_IDamageApplier_HealthDelta),new[]{typeof(int),typeof(float),typeof(DamageModelDamageModifiers)})]
    private static void BeginDirectOne(VehicleHealthRegister __instance,out RuntimeDamageFeed.HealthScope __state)=>__state=RuntimeDamageFeed.BeginDirectDamage(__instance);
    [HarmonyFinalizer,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Sprocket_DamageModelling_IDamageApplier_HealthDelta),new[]{typeof(int),typeof(float),typeof(DamageModelDamageModifiers)})]
    private static void EndDirectOne(VehicleHealthRegister __instance,RuntimeDamageFeed.HealthScope __state)=>RuntimeDamageFeed.EndDirectDamage(__instance,__state);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Sprocket_DamageModelling_IDamageApplier_HealthDelta),new[]{typeof(float),typeof(DamageModelDamageModifiers)})]
    private static bool BeginDirectAll(VehicleHealthRegister __instance,float __0,DamageModelDamageModifiers __1,out RuntimeDamageFeed.HealthScope __state){__state=RuntimeDamageFeed.BeginDirectDamage(__instance);return HeBlast.Filter(__instance,__0,__1);} // 0.22.86 HE replaced
    [HarmonyFinalizer,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Sprocket_DamageModelling_IDamageApplier_HealthDelta),new[]{typeof(float),typeof(DamageModelDamageModifiers)})]
    private static void EndDirectAll(VehicleHealthRegister __instance,RuntimeDamageFeed.HealthScope __state)=>RuntimeDamageFeed.EndDirectDamage(__instance,__state);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.LinkHealthPool))]
    private static void Linked(VehicleHealthRegister __instance,IDurable __0,int __1)=>RuntimeDamageFeed.Linked(__instance,__0,__1);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Update))]
    private static void Updating(VehicleHealthRegister __instance,out bool __state){__state=false;try{__state=__instance.register?.HealthChanged==true;}catch{}}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Update))]
    private static void Updated(VehicleHealthRegister __instance,bool __state){if(__state)RuntimeDamageFeed.ObserveRegister(__instance);}
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.CreateHealthPoolInternal))]
    private static void Created(VehicleHealthRegister __instance,VehicleComponent __0,IDurable __result)=>RuntimeDamageFeed.PoolCreated(__instance,__0,__result);
    [HarmonyPostfix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.OnHealthChanged))]
    private static void Changed(VehicleHealthRegister __instance,IDurable __0,HealthChangeInfo __1)=>RuntimeDamageFeed.HealthChanged(__instance,__0,__1);
    [HarmonyPrefix,HarmonyPatch(typeof(VehicleHealthRegister),nameof(VehicleHealthRegister.Release))]
    private static void Released(VehicleHealthRegister __instance)=>RuntimeDamageFeed.ReleaseHealth(__instance);
}
// 0.22.86 HE begin
/// <summary>
/// The blast filter of HE in the game (0.22.86). The game's own Detonate computes the value of every vehicle in the sphere (min(P, P/d²), the Pressure
/// modifiers) and calls VehicleHealthRegister.HealthDelta(float,mods), whose loop calls HealthPool.HealthDelta(value, mods) on every pool in its list. Inside the
/// HE scope the existing prefix of that method runs the same loop here (<see cref="HeRules.Run"/>), with per pool the game's own value or value x shock, and
/// skips the game's loop. Outside the HE scope (mines, mission zones, everything else) <see cref="Filter"/> returns after one test of a thread-local field.
/// </summary>
internal static class HeBlast
{
    private static bool planWarned,applyWarned;private static int probes;
    private static readonly HashSet<string> unknownLogged=new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr,string[]> chains=new();
    internal static void ProxyStarted(int id,float spawn)=>HeScopeState.ProxyStarted(id,spawn);
    internal static void Perforated(HePerforation perforation)=>HeScopeState.Perforated(perforation);
    internal static void Spaced(HeSpacedPerforation perforation)=>HeScopeState.Spaced(perforation); // 0.22.89
    internal static void LastLayer(string? hitId)=>HeScopeState.LastLayer(hitId);
    internal static void Begin(int id,float spawn,Vector3 point,double power,double calibreMm,string guid,bool ground)
    {
        var scope=HeScopeState.Begin(id,spawn,point.x,point.y,point.z,power,calibreMm,guid,ground,RuntimeSpall.He);
        long firstRegister=0;
        foreach(var ptr in scope.Perforated){firstRegister=ptr.ToInt64();break;}
        BallisticsEffectApi.Set(BlastTelemetryMath.ForExplosion("he",null,scope.CalibreMm,RuntimeSpall.He,scope.Power,scope.Perforated.Count>0,scope.Spaced!=null,firstRegister));
        RuntimeDamageFeed.RecordBlastTelemetry(BallisticsEffectApi.CurrentExplosionSeq,"he",BallisticsEffectApi.CurrentExplosionCalibreMm,BallisticsEffectApi.CurrentExplosionPower,BallisticsEffectApi.CurrentExplosionBreachMm);
        if(ReleaseDiagnostics.Enabled&&probes++<48)Plugin.ModLog.LogInfo($"[HE] detonation at ({point.x:0.00}, {point.y:0.00}, {point.z:0.00}): P {power:0.0}, {calibreMm:0.#} mm, {(ground?"ground hit":"hit")}; armour perforated on {scope.Perforated.Count} vehicle(s); interior shock {HeRules.Percent(scope.Shock)} %.");
    }
    internal static void End(){HeScopeState.End();BallisticsEffectApi.Clear();}
    private static void Warn(ref bool warned,string text){if(warned)return;warned=true;Plugin.ModLog.LogWarning(text);}
    internal static bool Filter(VehicleHealthRegister register,float value,DamageModelDamageModifiers modifiers)
    {
        if(HeScopeState.Active is not {} scope)return true;
        NativePools pools;
        try{pools=new NativePools(register,modifiers);}
        catch(Exception ex){Warn(ref planWarned,"[HE] Blast filter unavailable before any damage; the game's own blast is used: "+ex.Message);return true;}
        var plan=HeRules.Run(pools,value,scope.IsPerforated(register.Pointer),scope.Shock,RuntimeSpall.He);
        if(plan.Outcome==HeBlastOutcome.Native){Warn(ref planWarned,"[HE] Blast filter unavailable before any damage; the game's own blast is used: "+plan.Error?.Message);return true;}
        if(plan.Outcome==HeBlastOutcome.Failed)Warn(ref applyWarned,$"[HE] Blast filter stopped after {plan.Applied} of {plan.Count} modules; the game's own blast is not applied again (no double damage): {plan.Error?.Message}");
        try{After(scope,pools,plan);}catch(Exception){/* tags, report line and log are optional */}
        return false;
    }
    // Tags of the damage lines, the report line without a perforation (once per shot), one line per unknown module type per session, the diagnostic line.
    private static void After(HeScopeData scope,NativePools pools,HeBlastPlan plan)
    {
        var tags=scope.Tags??=new();
        for(int i=0;i<plan.Count&&i<pools.Owners.Length;i++)
        {
            var owner=pools.Owners[i];
            var tag=HeRules.Tag(plan.Classes[i],plan.Perforated,plan.Shock);
            // the damage line names the module the damage feed registered for the pool (for a linked pool such as a track: a wheel or an axle): tag both
            if(tag.Length>0){if(owner!=null)tags[owner.Pointer]=tag;if(pools.FeedOwners[i] is {} feedOwner)tags[feedOwner.Pointer]=tag;}
            if(plan.Classes[i]!=HePoolClass.Unknown)continue;
            var name=owner==null?"(no owner)":plan.TypeNames[i];
            if(unknownLogged.Count<64&&unknownLogged.Add(name))Plugin.ModLog.LogInfo($"[HE] Blast filter: module type {name} is on neither list; it keeps the game's own blast value.");
        }
        if(scope.Perforated.Count==0&&plan.Shocked&&!scope.NoteRecorded){scope.NoteRecorded=true;var spaced=scope.Spaced is {} s&&s.Register==pools.Register?s:null;RuntimeDamageFeed.HeNote(spaced!=null?HeSpaced.Text(spaced.NormalMm,spaced.NextArmourMm,plan.Shock):HeRules.OutsideText(plan.Shock),spaced!=null?spaced.HitId:scope.LastHitId);} // 0.22.89 replaced: the spaced-armour line on the vehicle whose skirt was perforated
        if(ReleaseDiagnostics.Enabled&&probes++<48)
            Plugin.ModLog.LogInfo($"[HE blast] vehicle {pools.Register}: {(plan.Perforated?"perforated":"not perforated")}; {plan.Count} pools: {plan.Of(HePoolClass.Exterior)} exterior, {plan.Of(HePoolClass.Interior)} interior (x {(plan.Perforated?"1":HeRules.Percent(plan.Shock)+" %")}), {plan.Of(HePoolClass.Unknown)} unknown; applied {plan.Applied}.");
    }
    /// <summary>The label of a damage line inside the HE scope with its tag ("Gunner [shock 9.2 %]", "main gun [exterior]"); any other label unchanged.</summary>
    internal static string Tag(VehicleComponent owner,string label)
    {
        if(HeScopeState.Active?.Tags is not {} tags||!tags.TryGetValue(owner.Pointer,out var tag))return label;
        return (owner.TryCast<CrewSeat>()!=null?label:CombatReportLanguage.Module(label))+" "+tag;
    }
    /// <summary>The report line of the perforation, once the fragments behind the plate are known.</summary>
    internal static void Fragments(PenetrationSimulation sim,int count)
    {
        if(HeScopeState.Proxy?.Perforation is not {} p||sim.damageApplier is not {} applier||applier.Pointer!=p.Register)return;
        RuntimeDamageFeed.HeNote(HeRules.PerforationText(p.NormalMm,p.RhaEqMm,p.AngleDegrees,count),p.HitId);
    }
    // The class names of a module, own class first (cached per class; the game's names, for example CrewSeat, FuelTank, TrackAssembly).
    private static string[] Chain(VehicleComponent owner)
    {
        var klass=owner.ObjectClass;
        if(chains.TryGetValue(klass,out var known))return known;
        var names=new List<string>();
        for(var k=klass;k!=IntPtr.Zero&&names.Count<16;k=Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(k))
        {
            var name=Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(k);
            if(string.IsNullOrEmpty(name))break;
            names.Add(name);
            if(name=="VehicleComponent")break;
        }
        var chain=names.ToArray();
        if(chains.Count<512)chains[klass]=chain;
        return chain;
    }
    /// <summary>The pools of one vehicle in the order of the game's loop, with the blast's modifiers; the owners from the register's own entries.</summary>
    private sealed class NativePools:IHeBlastPools
    {
        private readonly Il2CppSystem.Collections.Generic.List<HealthPool> list;
        private readonly Dictionary<IntPtr,VehicleComponent> owners=new();
        private readonly DamageModifiers modifiers;
        internal readonly VehicleComponent?[] Owners,FeedOwners;
        internal readonly IntPtr Register;
        public int Count{get;}
        internal NativePools(VehicleHealthRegister register,DamageModelDamageModifiers m)
        {
            Register=register.Pointer;
            list=register.register?.register??throw new InvalidOperationException("the vehicle has no pool list");
            Count=list.Count;Owners=new VehicleComponent?[Math.Max(0,Count)];FeedOwners=new VehicleComponent?[Math.Max(0,Count)];
            // The game's loop copies the 16 bytes of the blast's modifiers into each call: the same four floats (Piercing, BluntForce, Pressure, Thermal at 0/4/8/12).
            if(System.Runtime.CompilerServices.Unsafe.SizeOf<DamageModifiers>()!=16||System.Runtime.CompilerServices.Unsafe.SizeOf<DamageModelDamageModifiers>()!=16)throw new InvalidOperationException("unexpected modifier layout");
            modifiers=System.Runtime.CompilerServices.Unsafe.As<DamageModelDamageModifiers,DamageModifiers>(ref m);
            var entries=register.entries;
            if(entries!=null)for(int i=0;i<entries.Count;i++)
            {
                var entry=entries[i];if(entry==null)continue;
                var pool=entry.HealthPool;var owner=entry.Owner;
                if(pool!=null&&owner!=null)owners[pool.Pointer]=owner;
            }
        }
        public IReadOnlyList<string>? Types(int index)
        {
            var pool=list[index]??throw new NullReferenceException("pool "+index+" is null");
            var feedOwner=RuntimeDamageFeed.PoolOwner(pool.Pointer);FeedOwners[index]=feedOwner;
            var owner=owners.TryGetValue(pool.Pointer,out var known)?known:feedOwner;
            Owners[index]=owner;
            return owner==null?null:Chain(owner);
        }
        public void Apply(int index,float value)=>list[index].HealthDelta(value,modifiers);
    }
}
// 0.22.86 HE end











