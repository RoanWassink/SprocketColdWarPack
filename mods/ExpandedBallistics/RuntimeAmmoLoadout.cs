using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Blueprints;
using Sprocket.DamageModelling;
using Sprocket.ProjectilePhysicsSystems;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;
using PlayerVehicleController=Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketExpandedBallistics;

/// <summary>
/// FR-002 carried ammunition mix. The cannon editor stores one mix per design
/// (shell profile plus rounds). In battle finite typed native sources share each
/// physical storage owner and its conservative slot capacity. Switching changes
/// only which native type the next load request asks for;
/// the chambered round keeps its own type and its own ballistics.
/// </summary>
[HarmonyPatch]
internal static partial class RuntimeAmmoLoadout
{
    internal const string Owner="sprocket.expandedballistics.ammoloadout";
    private const string SaveKey="sprocketAmmoLoadoutV1";
    internal static bool Ready;

    private sealed record Registration(IProjectileTypeRegister Register,Il2CppSystem.Guid Guid);
    private sealed record RackInfo(AmmoRack Rack,AmmoRackBehaviour? Behaviour,int Capacity);
    private sealed record RackBinding(AmmoRack Rack,AmmoRackBehaviour Behaviour,string ProfileId);
    private sealed class Distribution
    {
        internal readonly List<RackBinding> Racks=new();
        internal readonly Dictionary<string,ProjectileTypeID> Types=new(StringComparer.Ordinal);
        // A frozen mix was never stocked in this instance (the gun was already active,
        // the racks are shared with another cannon or a registration failed). It is
        // reported once and never rewritten during the battle.
        internal bool Frozen;
        internal string? FailureReason;
        // The battle cannon this distribution belongs to; player-control lookups use it.
        internal Cannon? Instance;
        internal readonly List<PhysicalStore> Stores=new();
        internal string? InitialChamberProfile;
        internal int InitialNativeType=-1;
        // 0.22.17: the native type that was in the chamber when the round became Loaded; the request may name the next one.
        internal int LoadedNativeType=-1;
    }

    private static readonly Dictionary<IntPtr,List<AmmoLoadoutEntry>> Loadouts=new();
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid),Distribution> Distributions=new();
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid),string> Switched=new();
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid,string Profile),Registration> Registrations=new();
    private static readonly HashSet<string> Reported=new();
    private static readonly HashSet<(IntPtr Vehicle,int Vuid)> Waited=new();
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid),(string Text,float Until)> SwitchFeedback=new();
    // Every battle cannon seen by its native load task (with or without a mix): the HUD picks the active one.
    private static readonly Dictionary<(IntPtr Vehicle,int Vuid),Cannon> Live=new();
    private static CannonEditor? pendingInspector;
    private static bool nativeSliderEditing;
    private static int capacityTraceCount;
    private sealed class CapacityWatch
    {
        internal CannonEditor Inspector=null!;
        internal Cannon Cannon=null!;
        internal string Design="";
        internal int Capacity,Ready,Attempt;
        internal float Start;
    }
    private static CapacityWatch? capacityWatch;
    private static readonly float[] CapacitySettleTimes={.2f,.5f,1f};
    private static ConfigEntry<bool> enabled=null!,sectionOpen=null!,hud=null!,diagnostics=null!;

    internal static void Configure(ConfigFile config)
    {
        const string section="Ammunition loadout";
        enabled=config.Bind(section,"Enabled",true,"Carry the mix configured per cannon design and switch between carried shell types in battle.");
        sectionOpen=config.Bind(section,"Section open",true,"Legacy state for the merged Shell profile and ammunition section; kept in sync with its open control.");
        hud=config.Bind(section,"HUD",true,"Show the loaded shell (and the carried mix) during battle.");
        AmmoLoadoutHud.Configure(config);
        diagnostics=config.Bind(section,"Log switching",false,"Log rack stocking and shell type switching.");
    }

    internal static bool Enabled=>enabled?.Value==true;
    internal static bool HudEnabled=>hud?.Value==true;
    internal static bool SectionOpen=>sectionOpen?.Value==true;
    internal static void SyncSectionOpen(bool value)
    {
        if(sectionOpen!=null&&sectionOpen.Value!=value)sectionOpen.Value=value;
    }
    internal static bool HasLoadout(Cannon cannon)=>Enabled&&cannon.Blueprint is {} b&&Loadouts.TryGetValue(b.Pointer,out var entries)&&AmmoLoadoutRules.Total(entries)>0;
    internal static IReadOnlyList<AmmoLoadoutEntry> DesignEntries(Cannon cannon)=>cannon.Blueprint is {} b&&Loadouts.TryGetValue(b.Pointer,out var e)?e:Array.Empty<AmmoLoadoutEntry>();
    internal static string? InitialProfile(Cannon cannon)
    {
        var entries=DesignEntries(cannon);
        return AmmoInspectorPolicy.StartingProfile(entries,RuntimeShellSelection.StoredSelection(cannon.Blueprint));
    }

    internal static void Clear()
    {
        pendingInspector=null;nativeSliderEditing=false;capacityTraceCount=0;capacityWatch=null;ClearPhysicalStores();Loadouts.Clear();Distributions.Clear();Switched.Clear();Registrations.Clear();Reported.Clear();Waited.Clear();Live.Clear();ClearChamber();
    }

    private static void Guard(string area,Action action)
    {
        try { action(); }
        catch(Exception ex) { Plugin.ModLog.LogError($"[Ammo loadout] {area}: {ex}"); }
    }
    private static void Report(string text)
    {
        if(!Reported.Add(text)||Reported.Count>64)return;
        Plugin.ModLog.LogWarning("[Ammo loadout] "+text);
    }
    internal static void PrepareForExternalLoader(LoadTask task)=>Guard("External loader request",()=>Stock(task));
    private static void SetFeedback((IntPtr Vehicle,int Vuid) key,string text){SwitchFeedback[key]=(text,Time.unscaledTime+2.5f);AmmoLoadoutHud.RefreshNow();}
    private static void Detail(string text)
    {
        if(diagnostics?.Value==true)Plugin.ModLog.LogInfo("[Ammo loadout] "+text);
    }
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list)=>list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    private static Il2CppSystem.Action<float> FloatSlider(Action<float> action)=>DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(action)!;
    private static string Label(string? id)
    {
        if(id==null||id==ShellProfiles.Vanilla)return "Vanilla";
        return RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==id)?.Label??id;
    }
    private static List<AmmoLoadoutEntry> Stored(CannonBlueprint blueprint)
    {
        if(!Loadouts.TryGetValue(blueprint.Pointer,out var entries))
        {
            entries=new List<AmmoLoadoutEntry>();
            Loadouts[blueprint.Pointer]=entries;
        }
        return entries;
    }

    [HarmonyPostfix,HarmonyPatch(typeof(DynamicGUI.PropertyFieldSlider),nameof(DynamicGUI.PropertyFieldSlider.FinishEdit))]
    private static void FinishAmmoEdit()
    {
        nativeSliderEditing=false;
        var inspector=pendingInspector;if(inspector==null)return;
        pendingInspector=null;if(inspector.Active)Guard("Refresh carried capacity",()=>inspector.RequestRedraw());
    }

    [HarmonyPrefix,HarmonyPatch(typeof(DynamicGUI.PropertyFieldSlider),nameof(DynamicGUI.PropertyFieldSlider.StartEdit))]
    private static void BeginAmmoEdit(){nativeSliderEditing=true;}
    // Dragging the slider handle uses its own begin/end events (StartEdit/FinishEdit
    // belong to the number field). Without these the panel was rebuilt mid-drag,
    // which cancelled every drag: only clicks worked.
    [HarmonyPrefix,HarmonyPatch(typeof(DynamicGUI.PropertyFieldSlider),nameof(DynamicGUI.PropertyFieldSlider.OnSliderBeginDrag))]
    private static void BeginAmmoDrag(){nativeSliderEditing=true;}
    [HarmonyPostfix,HarmonyPatch(typeof(DynamicGUI.PropertyFieldSlider),nameof(DynamicGUI.PropertyFieldSlider.OnSliderEndDrag))]
    private static void FinishAmmoDrag()=>FinishAmmoEdit();
    private static void WatchCapacity(CannonEditor inspector,Cannon cannon,int capacity,int ready,IReadOnlyList<AmmoLoadoutEntry> entries)
    {
        var design=string.Join(";",entries.Select(e=>e.ProfileId+"="+e.Count))+"|"+cannon.Blueprint.Pointer+"|"+cannon.ShellBlueprint.Diameter+"|"+cannon.ShellBlueprint.PropellantLength;
        if(capacityWatch is {} watch&&watch.Inspector.Pointer==inspector.Pointer&&watch.Cannon.Pointer==cannon.Pointer&&watch.Design==design)
        {watch.Capacity=capacity;watch.Ready=ready;return;}
        if(capacityTraceCount++<8)Plugin.ModLog.LogInfo($"[Ammo capacity trace] cannon={cannon.VUID.Value} initial={capacity} ready={ready} ordinary={capacity-ready}; bounded editor settle queued.");
        capacityWatch=new CapacityWatch{Inspector=inspector,Cannon=cannon,Capacity=capacity,Ready=ready,Design=design,Start=Time.realtimeSinceStartup};
    }
    internal static void UpdateEditor()
    {
        if(capacityWatch?.Inspector.Active!=true&&pendingInspector?.Active!=true)
        {capacityWatch=null;pendingInspector=null;nativeSliderEditing=false;return;}
        if(nativeSliderEditing)return;
        // Never rebuild the panel while the mouse button is still held on a control.
        if(UnityEngine.InputSystem.Mouse.current?.leftButton.isPressed==true)return;
        if(pendingInspector is {} pending){pendingInspector=null;if(pending.Active)pending.RequestRedraw();}
        var watch=capacityWatch;
        if(watch==null||watch.Attempt>=CapacitySettleTimes.Length||Time.realtimeSinceStartup-watch.Start<CapacitySettleTimes[watch.Attempt])return;
        watch.Attempt++;
        try
        {
            if(watch.Inspector.Component?.Pointer!=watch.Cannon.Pointer){capacityWatch=null;return;}
            var settledRacks=ScanRacks(watch.Cannon,false);
            var ready=CarouselCapacity(watch.Cannon)+settledRacks.Where(r=>IsReadyMagazine(watch.Cannon,r.Rack)).Sum(r=>ConservativeRackCapacity(watch.Cannon,r.Rack));
            var capacity=CarouselCapacity(watch.Cannon)+settledRacks.Sum(r=>ConservativeRackCapacity(watch.Cannon,r.Rack));
            if(capacity==watch.Capacity&&ready==watch.Ready)return;
            if(capacityTraceCount++<16)Plugin.ModLog.LogInfo($"[Ammo capacity trace] cannon={watch.Cannon.VUID.Value} settled={capacity} ready={ready} ordinary={capacity-ready} previous={watch.Capacity}; retry={watch.Attempt}/3.");
            watch.Capacity=capacity;watch.Ready=ready;
            watch.Inspector.RequestRedraw();
        }
        catch{capacityWatch=null;throw;}
    }

    // ----------------------------------------------------------------- save and load

    [HarmonyPostfix,HarmonyPatch(typeof(CannonBlueprint),nameof(CannonBlueprint.Save))]
    private static void Save(CannonBlueprint __instance,IBlueprintSerializer info)
    {
        if(Loadouts.TryGetValue(__instance.Pointer,out var entries)&&AmmoLoadoutRules.Total(entries)>0)
            info.AddValue(SaveKey,AmmoLoadoutRules.Serialize(entries));
    }

    [HarmonyPostfix,HarmonyPatch(typeof(CannonBlueprint),nameof(CannonBlueprint.Load))]
    private static void Load(CannonBlueprint __instance,IBlueprintDeserializer info)
    {
        Guard("Load carried mix",()=>
        {
            var hasValue=false;
            var copier=info.TryCast<BlueprintCopier>();
            if(copier!=null)hasValue=copier.TryGetValue(SaveKey,out _);
            else
            {
                var wrapper=info.TryCast<SerializationBlueprintWrapper>();
                if(wrapper!=null)
                {
                    var entries=wrapper.info.GetEnumerator();
                    while(entries.MoveNext())if(entries.Name==SaveKey)hasValue=true;
                }
                else Plugin.ModLog.LogWarning("[Ammo loadout] Unknown blueprint reader; using an empty mix.");
            }
            var parsed=hasValue?AmmoLoadoutRules.Parse(info.GetString(SaveKey)):new List<AmmoLoadoutEntry>();
            Loadouts[__instance.Pointer]=parsed.ToList();
            if(parsed.Count>0)Detail($"design {__instance.Caliber} mm mix loaded: {AmmoLoadoutRules.Serialize(parsed)}");
        });
    }

    [HarmonyPostfix,HarmonyPatch(typeof(CannonBlueprint),nameof(CannonBlueprint.Clone))]
    private static void Clone(CannonBlueprint __instance,Il2CppSystem.Object __result)
    {
        var copy=__result.TryCast<CannonBlueprint>();
        if(copy!=null&&Loadouts.TryGetValue(__instance.Pointer,out var entries))
            Loadouts[copy.Pointer]=entries.ToList();
    }

    // ------------------------------------------------------------------ inspector UI

    /// <summary>
    /// Controls for the ammunition half of the merged inspector section. The caller
    /// owns the section header; this method never starts an independent section.
    /// </summary>
    internal static void DrawControls(CannonEditor __instance,Cannon cannon,IGUIElementDrawer ui)
    {
        if(cannon==null||RuntimeAtgmLauncher.IsLauncher(cannon))return;
        // The merged section decides whether ammunition contributes controls. Keep
        // the old independent ammunition gate here as the final safety check.
        if(!Ready||!Enabled)return;
        var blueprint=cannon.Blueprint;
        if(blueprint==null)return;
        Guard("Inspector",()=>
        {
            var entries=Stored(blueprint);
            // 0.22.3: the native selection and the carried mix never contradict each other.
            var reconciled=AmmoInspectorPolicy.Reconcile(entries,RuntimeShellSelection.StoredSelection(blueprint));
            if(!ReferenceEquals(reconciled.Entries,entries)){entries=reconciled.Entries.ToList();Loadouts[blueprint.Pointer]=entries;blueprint.MarkModified();Detail("single-row mix follows the native shell: "+AmmoLoadoutRules.Serialize(entries));}
            if(reconciled.Selection!=null&&reconciled.Selection!=RuntimeShellSelection.StoredSelection(blueprint))
            {RuntimeShellSelection.SetDesignSelection(blueprint,reconciled.Selection);blueprint.MarkModified();Detail("native selection follows the mix: "+reconciled.Selection);}
            var racks=ScanRacks(cannon,false);
            var capacities=new List<int>();
            var carouselCapacity=CarouselCapacity(cannon);
            if(carouselCapacity>0)capacities.Add(carouselCapacity);
            var readyRackCapacity=0;
            foreach(var r in racks)
            {
                var rackCap=ConservativeRackCapacity(cannon,r.Rack);
                capacities.Add(rackCap);
                if(IsReadyMagazine(cannon,r.Rack))readyRackCapacity+=rackCap;
            }
            var readyCapacity=carouselCapacity+readyRackCapacity;
            var capacity=capacities.Sum();
            var total=AmmoLoadoutRules.Total(entries);
            WatchCapacity(__instance,cannon,capacity,readyCapacity,entries);
            var available=RuntimeShellEra.Selectable(RuntimeShellSelection.Profiles,cannon.Vehicle,entries.Select(e=>e.ProfileId),cannon);
            var starting=AmmoInspectorPolicy.StartingProfile(entries,RuntimeShellSelection.StoredSelection(blueprint));

            // War Thunder-style summary: carried / capacity, then load order, then one row per type.
            ui.InfoField(total==0&&entries.Count==0
                ? "Ammunition: native racks, selected shell only. Add shell types below for a carried mix."
                : $"Ammunition {total} / {capacity}   ({readyCapacity} ready, {Math.Max(0,capacity-readyCapacity)} reserve)",2);
            if(entries.Count>0&&total>0)
            {
                var orderLine=AmmoInspectorPolicy.FormatLoadOrder(entries,starting,Label);
                if(orderLine.Length>0)ui.InfoField(orderLine,2);
            }
            if(!LoaderSupportsMix(out var loaderReason))ui.InfoField(loaderReason!,3);
            if(racks.Count==0&&readyCapacity==0)ui.InfoField("No ammunition rack on this vehicle matches this cannon's shell size.",2);
            else if(capacity==0&&total>0)ui.InfoField("The longest carried round does not fit these racks. Set it to 0 or remove it, or enlarge the racks.",2);
            else if(total>capacity)ui.InfoField($"{total-capacity} rounds more than the racks hold: loading is blocked. Lower a type.",2);
            if(Message!=null)ui.InfoField(Message,2);
            var disallowed=AmmoInspectorPolicy.Disallowed(entries,id=>Allowed(cannon,id));
            if(disallowed.Count>0)ui.InfoField($"Not available at this vehicle date: {string.Join(", ",disallowed.Select(Label))}. These rounds are carried but never loaded; change the date or set them to 0.",2);

            void Rebuild(){blueprint.MarkModified();__instance.RequestRedraw();}
            // 0.22.3: a single-type mix is the shell type; changing it changes the row (count kept) and the
            // saved selection together, so what the inspector shows is what loads in battle.
            if(entries.Count==1)
            {
                var single=entries[0];
                var typeIds=new List<string>{ShellProfiles.Vanilla};var typeLabels=new Il2CppSystem.Collections.Generic.List<string>();typeLabels.Add("Vanilla ammunition");
                foreach(var profile in available){typeIds.Add(profile.Id);typeLabels.Add(profile.Label);}
                if(!typeIds.Contains(single.ProfileId)){typeIds.Add(single.ProfileId);typeLabels.Add(Label(single.ProfileId)+" (not available)");}
                ui.Dropdown("Shell type",typeLabels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),typeIds.IndexOf(single.ProfileId),Ui.IntCallback(index=>Guard("Single-type shell",()=>
                {
                    if(index<0||index>=typeIds.Count)return;
                    var current=Stored(blueprint);
                    if(current.Count!=1||current[0].ProfileId==typeIds[index])return;
                    Loadouts[blueprint.Pointer]=new List<AmmoLoadoutEntry>{current[0] with{ProfileId=typeIds[index]}};
                    RuntimeShellSelection.SetDesignSelection(blueprint,typeIds[index]);cannon.RequestRebuild();Message=null;
                    Rebuild();
                })),"One carried type: this is the shell the gun loads. Add a second type below for a mix.");
            }
            for(var i=0;i<entries.Count;i++)
            {
                var id=entries[i].ProfileId;
                // Slider end = everything still free for this type: the racks (with
                // this type's envelope included) minus the other types. Dragging to
                // the end fills exactly the free room; the total can never exceed it.
                var fits=ShellGeometryApi.GetLoaderGeometryForProfile(cannon,id,out var p,out var c,out var d)==1;
                var ownReady=fits?CarouselCapacityForGeometry(cannon,p,c,d):0;
                var own=fits?ownReady+racks.Sum(r=>RackCapacity(cannon,r.Rack,p,c,d)):0;
                var others=total-entries[i].Count;
                var room=capacity>0?Math.Min(own,capacity):own;
                var max=Math.Max(0,room-others);
                ui.Slider($"{i+1}. {(id==starting?"[1st] ":"")}{Label(id)}",Math.Clamp(entries[i].Count,0,max),0,Math.Max(max,1),FloatSlider(value=>Guard("Set carried rounds",()=>
                {
                    var current=Stored(blueprint);
                    var index=AmmoLoadoutRules.IndexOf(current,id);
                    if(index<0)return;
                    var count=Math.Clamp((int)MathF.Round(value),0,max);
                    if(count==current[index].Count)return;
                    var changed=current.ToList();
                    changed[index]=current[index] with {Count=count};
                    Loadouts[blueprint.Pointer]=changed;
                    blueprint.MarkModified();
                    Detail($"design {blueprint.Caliber} mm mix changed: {AmmoLoadoutRules.Serialize(changed)}");
                    // Rebuild after the native edit finishes, never during the drag.
                    pendingInspector=__instance;
                })));
                if(!fits||own==0)
                    ui.InfoField(fits?$"Does not fit these racks ({p:0}+{c:0} mm long, {d:0} mm wide)."
                        :"Shell geometry unavailable for this cannon; cannot be stocked.",2);
                // Native tab rows need a valid selected tab: tab 0 is this row's own
                // badge (always selected, no action); the actions follow it.
                var multi=entries.Count>1;
                var actions=new Il2CppStringArray(multi
                    ?new[]{id==starting?$"#{i+1} 1st":$"#{i+1}","First","\u2191","\u2193","Fill","Remove"}
                    :new[]{"#1 1st","Fill","Remove"});
                var ownCapacity=own;
                try{ui.TabGroup(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>((Action<int>)(action=>Guard("Ammunition row action",()=>
                {
                    var current=Stored(blueprint);
                    var index=AmmoLoadoutRules.IndexOf(current,id);
                    if(index<0)return;
                    var next=current.ToList();
                    if(!multi)
                    {
                        switch(action)
                        {
                            case 1:
                                var singleRoom=capacity>0?Math.Min(capacity,ownCapacity):ownCapacity;
                                var singleFree=Math.Max(0,singleRoom-AmmoLoadoutRules.Total(current));
                                next[index]=current[index] with {Count=current[index].Count+singleFree};
                                Loadouts[blueprint.Pointer]=next;Message=singleFree==0?"No free rack space.":null;break;
                            case 2:
                                next.RemoveAt(index);Loadouts[blueprint.Pointer]=next;Message=null;break;
                        }
                        Rebuild();
                        return;
                    }
                    switch(action)
                    {
                        case 1:
                            if(current[index].Count<=0){Message=$"{Label(id)} has no rounds; give it rounds before loading it first.";break;}
                            next=AmmoInspectorPolicy.MoveFirst(current,index).ToList();
                            Loadouts[blueprint.Pointer]=next;
                            RuntimeShellSelection.SetDesignSelection(blueprint,id);cannon.RequestRebuild();Message=null;break;
                        case 2:
                            if(index==0){Message=$"{Label(id)} is already #1 in load order.";break;}
                            next=AmmoInspectorPolicy.Move(current,index,-1).ToList();
                            Loadouts[blueprint.Pointer]=next;
                            var upFirst=next.FirstOrDefault(e=>e.Count>0)?.ProfileId;
                            var prevUpFirst=current.FirstOrDefault(e=>e.Count>0)?.ProfileId;
                            if(upFirst!=null&&(upFirst!=prevUpFirst||index==1)){RuntimeShellSelection.SetDesignSelection(blueprint,upFirst);cannon.RequestRebuild();}
                            Message=null;break;
                        case 3:
                            if(index>=current.Count-1){Message=$"{Label(id)} is already last in load order.";break;}
                            next=AmmoInspectorPolicy.Move(current,index,1).ToList();
                            Loadouts[blueprint.Pointer]=next;
                            var downFirst=next.FirstOrDefault(e=>e.Count>0)?.ProfileId;
                            var prevDownFirst=current.FirstOrDefault(e=>e.Count>0)?.ProfileId;
                            if(downFirst!=null&&(downFirst!=prevDownFirst||index==0)){RuntimeShellSelection.SetDesignSelection(blueprint,downFirst);cannon.RequestRebuild();}
                            Message=null;break;
                        case 4:
                            var fillRoom=capacity>0?Math.Min(capacity,ownCapacity):ownCapacity;
                            var free=Math.Max(0,fillRoom-AmmoLoadoutRules.Total(current));
                            next[index]=current[index] with {Count=current[index].Count+free};
                            Loadouts[blueprint.Pointer]=next;Message=free==0?"No free rack space: lower another type first.":null;break;
                        case 5:
                            next.RemoveAt(index);Loadouts[blueprint.Pointer]=next;Message=null;break;
                    }
                    Rebuild();
                })))!,0,actions);}
                catch(Exception ex){Report("Row actions unavailable: "+ex.Message);}
            }
            if(entries.Count<AmmoLoadoutRules.MaximumTypes)
            {
                var ids=new List<string>{""};
                var options=new Il2CppSystem.Collections.Generic.List<string>();
                options.Add("Add shell type");
                foreach(var profile in available)if(AmmoLoadoutRules.IndexOf(entries,profile.Id)<0){ids.Add(profile.Id);options.Add(profile.Label);}
                if(AmmoLoadoutRules.IndexOf(entries,ShellProfiles.Vanilla)<0){ids.Add(ShellProfiles.Vanilla);options.Add("Vanilla ammunition");}
                ui.Dropdown("Add shell type",options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),0,Ui.IntCallback(index=>Guard("Add carried type",()=>
                {
                    if(index<=0||index>=ids.Count)return;
                    var current=Stored(blueprint);
                    if(current.Count>=AmmoLoadoutRules.MaximumTypes||AmmoLoadoutRules.IndexOf(current,ids[index])>=0)return;
                    // 0.22.3: one click gives two types. The first add keeps the native selection
                    // as row 1 with all its rounds, plus the new type at 0 (AmmoInspectorPolicy.Add).
                    var native=RuntimeShellSelection.StoredSelection(blueprint)??ShellProfiles.Vanilla;
                    var next=AmmoInspectorPolicy.Add(current,native,ids[index],capacity).ToList();
                    Loadouts[blueprint.Pointer]=next;
                    if(current.Count==0)RuntimeShellSelection.SetDesignSelection(blueprint,next[0].ProfileId);
                    Message=next.Count>1?$"{Label(ids[index])} added with 0 rounds: lower {Label(next[0].ProfileId)}, then raise this one or press Fill.":null;
                    Rebuild();
                })),"Each type shares the same racks. A longer round lowers how many fit.");
            }
            if(diagnostics.Value&&total>0)
            {
                var plan=AmmoLoadoutRules.DistributeMixed(capacities,entries);
                var text=new List<string>();
                foreach(var group in plan.GroupBy(a=>a.RackIndex))text.Add($"Store {group.Key+1}: "+string.Join(" + ",group.Select(a=>$"{a.Amount}x {Label(a.ProfileId)}")));
                ui.InfoField("Physical plan: "+string.Join(" | ",text),Math.Max(2,text.Count+1));
            }
            if(diagnostics.Value)ui.InfoField("In battle the next-load keys choose the following round; a round already in the gun keeps its type.",2);
        });
    }
    // Last action result shown in the panel (e.g. why a type cannot be filled).
    private static string? Message;
    private static int RackCapacity(Cannon cannon,AmmoRack rack,double projectile,double charge,double diameter)
    {
        if(TryLoaderRackCapacity(cannon,rack,projectile,charge,diameter,out var loaderCap))
            return Math.Clamp(loaderCap,0,ushort.MaxValue);
        var b=rack.Blueprint;if(b==null||projectile+charge>ushort.MaxValue||diameter>ushort.MaxValue||diameter<=0)return 0;
        var s=rack.spacing;var a=rack.minPadding;var p=rack.maxPadding;
        return AmmoRackGrid.Capacity(b.Width,b.Height,b.Length,(int)Math.Ceiling(diameter),(int)Math.Ceiling(projectile+charge),
            s.x,s.y,s.z,a.x,a.y,a.z,p.x,p.y,p.z);
    }

    // -------------------------------------------------------------- rack stocking

    private static List<RackInfo> ScanRacks(Cannon cannon,bool live)
    {
        var found=new List<RackInfo>();
        var vehicle=cannon.Vehicle;
        var items=vehicle?.ObjectReader?.Items;
        if(items==null)return found;
        var shell=cannon.ShellBlueprint; // 0.22.90 replaced: part I, the projectile size of the cannon's shell, as the load request
        if(shell==null)return found; // 0.22.90: no shell design, no projectile size to match (an empty list, as when no rack matches)
        var sizeId=ProjectileSizeID.FromPropellantLength(shell.Diameter,shell.PropellantLength).Value; // 0.22.90
        for(var i=0;i<Count(items);i++)
        {
            var parts=items[i].Components;
            if(parts==null)continue;
            for(var j=0;j<Count(parts);j++)
            {
                var rack=parts[j].TryCast<AmmoRack>();
                if(rack==null||rack.Vehicle?.Pointer!=vehicle!.Pointer)continue;
                if(rack.projectileSizeID.Value!=sizeId)continue; // 0.22.90 replaced: part I, match the projectile size, not the physical slot (EB-FOLLOWUP)
                var ready=IsReadyMagazine(cannon,rack);
                var behaviour=rack.behaviour;
                if(live)
                {
                    if(behaviour==null)continue;
                    var capacity=(int)behaviour.Capacity;
                    if(capacity<=0)capacity=rack.Capacity;
                    if(capacity<=0&&(!ready||ConservativeRackCapacity(cannon,rack)<=0))continue;
                    found.Add(new RackInfo(rack,behaviour,capacity));
                }
                else
                {
                    var capacity=rack.Capacity;
                    if(capacity<=0&&(!ready||ConservativeRackCapacity(cannon,rack)<=0))continue;
                    found.Add(new RackInfo(rack,null,capacity));
                }
            }
        }
        found.Sort((a,b)=>
        {
            var readyCmp=IsReadyMagazine(cannon,b.Rack).CompareTo(IsReadyMagazine(cannon,a.Rack));
            if(readyCmp!=0)return readyCmp;
            return a.Rack.VUID.Value!=b.Rack.VUID.Value
                ?a.Rack.VUID.Value.CompareTo(b.Rack.VUID.Value)
                :a.Rack.Pointer.ToInt64().CompareTo(b.Rack.Pointer.ToInt64());
        });
        return found;
    }

    private static bool RegisterTypes(Distribution dist,Cannon cannon,List<AmmoLoadoutEntry> entries,IntPtr vehiclePointer)
    {
        var shell=cannon.ShellBlueprint;
        var types=shell?.GeneratedProjectileBlueprint;
        if(shell==null||types==null||Count(types)==0){Report("native projectile blueprints unavailable; mix not stocked.");return false;}
        var register=IProjectileTypeRegister.Instance;
        if(register==null){Report("native projectile register unavailable; mix not stocked.");return false;}
        var baseProjectile=types[0];
        foreach(var entry in entries)
        {
            var id=entry.ProfileId;
            if(dist.Types.ContainsKey(id))continue;
            var registrationKey=(vehiclePointer,cannon.VUID.Value,id);
            if(Registrations.TryGetValue(registrationKey,out var known))
            {
                if(known.Register.IsRegistered(known.Guid)){dist.Types[id]=known.Register.GetID(known.Guid);continue;}
                Registrations.Remove(registrationKey);
            }
            // Native AP base for the vanilla entry; an own registered type per carried
            // profile, same protocol as the accepted carousel and ATGM initial round.
            var guid=id==ShellProfiles.Vanilla
                ?baseProjectile.ProjectileTypeGuid
                :(Il2CppSystem.Guid)new Il2CppSystem.Guid(System.Guid.NewGuid().ToString());
            if(!register.IsRegistered(guid))
                register.Create($"{shell.Diameter} mm {Label(id)} mix",guid,MathF.Pow(shell.Diameter,3)*1.58999992e-5f,
                    shell.Diameter*.001f,shell.Diameter*.003f,1f,10f,baseProjectile.FunctionDefinitions);
            if(!register.IsRegistered(guid)||!register.TryGet(guid,out var native)||native==null||native.ID.Value==ProjectileTypeID.Invalid.Value)
            {Report($"native registration readback failed for {id}; mix not stocked.");return false;}
            register.AddUser(guid);
            var type=register.GetID(guid);
            if(type.Value!=native.ID.Value){register.RemoveUser(guid);Report($"native registration ID mismatch for {id}; mix not stocked.");return false;}
            Registrations[registrationKey]=new Registration(register,guid);
            dist.Types[id]=type;
        }
        return dist.Types.Count>0;
    }

    private static Distribution? Define(Cannon cannon,LoadTask task,List<AmmoLoadoutEntry> entries,IntPtr vehiclePointer)
    {
        if(!LoaderSupportsMix(out var loaderReason))return Freeze(cannon,loaderReason!);
        var designRacks=ScanRacks(cannon,false);
        var racks=ScanRacks(cannon,true);
        if(racks.Count<designRacks.Count&&designRacks.Any(r=>r.Rack.IsInstalled&&r.Rack.behaviour==null)
            &&Waited.Add((vehiclePointer,cannon.VUID.Value)))return null;
        var carousel=CarouselStore(cannon);
        if(racks.Count==0&&carousel.Aggregate==null)return Freeze(cannon,"No physical ammunition storage is available.");
        if(racks.Any(r=>Stores.ContainsKey(r.Behaviour!.Pointer))||carousel.Aggregate!=null&&Stores.ContainsKey(carousel.Aggregate.Pointer))
        {return Freeze(cannon,"Physical storage already belongs to another cannon.");}
        var dist=new Distribution{Instance=cannon};
        if(!RegisterTypes(dist,cannon,entries,vehiclePointer))return Freeze(cannon,"Native ammunition type registration failed.");
        var reserve=entries.ToList();
        if(HoldsRound(task))
        {
            string? chamber=null;var nativeType=task.Request.TypeID.Value;
            foreach(var type in dist.Types)if(type.Value.Value==nativeType){chamber=type.Key;break;}
            if(chamber==null&&task.rack?.TryCast<AmmoRackBehaviour>() is {} source&&source.StoredTypeID.Value==nativeType)
            {
                var generated=cannon.ShellBlueprint.GeneratedProjectileBlueprint;
                var register=IProjectileTypeRegister.Instance;
                if(generated!=null&&Count(generated)>0&&register!=null&&register.IsRegistered(generated[0].ProjectileTypeGuid)&&register.GetID(generated[0].ProjectileTypeGuid).Value==nativeType)
                    chamber=RuntimeShellSelection.StoredSelection(cannon.Blueprint);
            }
            var at=chamber==null?-1:AmmoLoadoutRules.IndexOf(reserve,chamber);
            if(at<0||reserve[at].Count<=0)
            {return Freeze(cannon,"The existing loaded round cannot be identified in this carried mix.");}
            reserve[at]=reserve[at] with{Count=reserve[at].Count-1};
            dist.InitialChamberProfile=chamber;dist.InitialNativeType=nativeType;
            CreditInitial(task,vehiclePointer,chamber); // the game debited this round before the mix was stocked
        }
        var capacities=new List<int>();
        // Ready carousel is filled first, then actual ordinary/bustle reserve racks.
        if(carousel.Aggregate!=null)capacities.Add(carousel.Capacity);
        capacities.AddRange(racks.Select(r=>ConservativeRackCapacity(cannon,r.Rack)));
        if(AmmoLoadoutRules.Total(reserve)>capacities.Sum())
        {return Freeze(cannon,$"The carried mix needs {AmmoLoadoutRules.Total(reserve)} storage slots; conservative fit provides {capacities.Sum()}.");}
        try
        {
            if(carousel.Aggregate!=null)MakeStore(cannon,carousel.Aggregate,null,carousel.Capacity,carousel.Vuid,dist);
            foreach(var rack in racks)MakeStore(cannon,rack.Behaviour!,rack.Rack,ConservativeRackCapacity(cannon,rack.Rack),rack.Rack.VUID.Value,dist);
            var plan=AmmoLoadoutRules.DistributeMixed(capacities,reserve);
            foreach(var a in plan)
            {
                if(a.ProfileId==null)continue;
                var store=dist.Stores[a.RackIndex];var view=store.Views[a.ProfileId];
                var before=view.AmountStored;
                if(view.LoadAmmo((ushort)a.Amount)!=0||view.AmountStored!=before+a.Amount)throw new InvalidOperationException("Typed native stock did not accept its physical allocation.");
            }
            foreach(var store in dist.Stores){Project(store);store.Initializing=false;}
        }
        catch(Exception ex)
        {RollbackStores(dist);return Freeze(cannon,"Native ammunition storage initialization failed: "+ex.Message);}
        Plugin.ModLog.LogInfo($"[Ammo loadout] cannon {cannon.VUID.Value}: {dist.Stores.Sum(s=>s.Total)} ready/reserve + {(dist.InitialChamberProfile==null?0:1)} in gun; {dist.Stores.Count} physical stores, typed mix conserved.");
        return dist;
    }
    // ----------------------------------------------------- request steering per frame

    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.Update)),HarmonyPriority(Priority.First)]
    private static void Steer(LoadTask __instance)=>Guard("Native load request",()=>Stock(__instance));

    // Some native assembly paths fill the first chamber through LoadInstantly
    // instead of the per-frame Update; the mix must be stocked before that too.
    [HarmonyPrefix,HarmonyPatch(typeof(LoadTask),nameof(LoadTask.LoadInstantly)),HarmonyPriority(Priority.First)]
    private static void Instant(LoadTask __instance)=>Guard("Native instant load",()=>Stock(__instance));

    private static void Stock(LoadTask task)
    {
        if(!Ready)return;
        var cannon=task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        if(cannon!=null&&cannon.Vehicle?.Behaviour is {} owner)Live[(owner.Pointer,cannon.VUID.Value)]=cannon;
        if(!Enabled)return;
        if(cannon==null||RuntimeAtgmLauncher.IsLauncher(cannon))return;
        var blueprint=cannon.Blueprint;
        if(blueprint==null||!Loadouts.TryGetValue(blueprint.Pointer,out var entries)||entries.Count==0||AmmoLoadoutRules.Total(entries)<=0)return;
        var instance=cannon.Vehicle?.Behaviour;
        if(instance==null)return;
        var key=(instance.Pointer,cannon.VUID.Value);
        if(!Distributions.TryGetValue(key,out var dist))
        {
            // Define preserves and debits a proven existing loading/chambered round.
            dist=Define(cannon,task,entries,instance.Pointer);
            if(dist==null)return;
            Waited.Remove(key);
            Distributions[key]=dist;
        }
        if(dist.Frozen||dist.Stores.Count==0)return;
        dist.LoadedNativeType=ChamberIdentity.Track(task.State==LoadState.Loaded&&HoldsRound(task),task.State!=LoadState.Loaded,dist.LoadedNativeType,task.Request.TypeID.Value);
        if(!CycleBaselines.ContainsKey(task.Pointer)&&task.State is LoadState.NeverLoaded or LoadState.Unloaded)CaptureCycle(task);
        // Missing credit alone cannot prove a phantom: an external loader or inlined native debit may have loaded a real round.
        if(task.State==LoadState.Loaded&&!Chamber.Has(task.Pointer)&&ProvenPhantom(cannon,task))RemovePhantom(cannon,task);
        // Loading+Consumed is the separate crew-wait state, with no occupied round or running native reload.
        if(!AmmoReloadPolicy.CanSteer(task.State==LoadState.Loading&&task.currentRequest!=LoadRequest.Consumed,HoldsRound(task)))return;
        var chosen=Choose(cannon,blueprint,key,entries,dist,task);
        if(chosen==null||!dist.Types.TryGetValue(chosen,out var type))return;
        var size=ProjectileSizeID.FromPropellantLength(cannon.ShellBlueprint.Diameter,cannon.ShellBlueprint.PropellantLength);

        var request=task.Request;
        if(request.TypeID.Value!=type.Value||request.SizeID.Value!=size.Value||request.Amount!=1)
        {
            request.TypeID=type;request.SizeID=size;request.Amount=1;request.Destination=cannon.LoadPosition;
            task.Request=request;
            Detail($"cannon {cannon.VUID.Value} next request {Label(chosen)} type={type.Value} state={task.State} remaining={Remaining(dist,task,chosen)}.");
        }
        // A request with no pending state is ignored by the native update; wake it while
        // the gun is empty, exactly like the native instant chamber load does.
        if(task.currentRequest==LoadRequest.None&&(task.State==LoadState.NeverLoaded||task.State==LoadState.Unloaded))
            task.currentRequest=LoadRequest.Unused;
    }

    private static string? Choose(Cannon cannon,CannonBlueprint blueprint,(IntPtr Vehicle,int Vuid) key,List<AmmoLoadoutEntry> entries,Distribution dist,LoadTask task)
    {
        // This is a next-load preference. It never changes the round already held.
        var switched=Switched.TryGetValue(key,out var value)?value:null;
        var stored=switched??RuntimeShellSelection.StoredSelection(blueprint);
        if(stored==null||!dist.Types.ContainsKey(stored)||!Allowed(cannon,stored))stored=entries[0].ProfileId;
        // 0.22.7: the pure rule (AmmoSelection.Choose): keep the preference while it has rounds; when it has run out, the next carried type
        // with rounds, which loads normally; with nothing left, no request (the gun stays empty and cannot fire).
        var preferredHasRounds=Usable(cannon,dist,stored)&&ReachableRemaining(cannon,dist,stored,out _)>0;
        var choice=AmmoSelection.Choose(entries,stored,id=>Usable(cannon,dist,id)?ReachableRemaining(cannon,dist,id,out _):0);
        EmptyEvent(cannon,key,stored,preferredHasRounds,choice);
        if(choice.Type==null)return null;
        if(!Switched.TryGetValue(key,out var current)||current!=choice.Type)Switched[key]=choice.Type;
        return choice.Type;
    }

    private static bool HoldsRound(LoadTask task)=>
        (task.State==LoadState.Loading||task.State==LoadState.Loaded)&&task.currentRequest!=LoadRequest.Consumed&&
        !IsFailedConsumption(task);

    private static string? Chambered(Distribution dist,LoadTask task)
    {
        if(!HoldsRound(task))return null;
        var type=ChamberIdentity.Resolve(task.State==LoadState.Loaded,dist.LoadedNativeType,task.Request.TypeID.Value);
        if(dist.InitialChamberProfile!=null&&dist.InitialNativeType==type)return dist.InitialChamberProfile;
        foreach(var pair in dist.Types)if(pair.Value.Value==type)return pair.Key;
        return null;
    }

    private static bool Usable(Cannon cannon,Distribution dist,string id)=>
        dist.Types.ContainsKey(id)&&Allowed(cannon,id)&&ReachableRemaining(cannon,dist,id,out _)>0;

    private static bool Allowed(Cannon cannon,string id)
    {
        if(id==ShellProfiles.Vanilla)return true;
        var profile=RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==id);
        return profile!=null&&RuntimeShellEra.Allowed(profile,cannon.Vehicle);
    }

    private static int StoredRemaining(Distribution dist,string id)=>
        dist.Stores.Sum(s=>!s.Destroyed&&s.Views.TryGetValue(id,out var view)?(int)view.AmountStored:0);

    private static int ReachableRemaining(Cannon cannon,Distribution dist,string id,out string? restriction)
    {
        restriction=null;
        if(TryGetReachableStorage(cannon,out var readyAgg,out var why)&&readyAgg!=null)
        {
            var readyStore=dist.Stores.FirstOrDefault(s=>s.Aggregate.Pointer==readyAgg.Pointer);
            if(readyStore!=null)
            {
                restriction=why;
                return !readyStore.Destroyed&&readyStore.Views.TryGetValue(id,out var view)?(int)view.AmountStored:0;
            }
        }
        return StoredRemaining(dist,id);
    }

    /// <summary>Rounds of one carried type: its racks, the chambered round and any
    /// other native source the load task currently uses for the same type.</summary>
    private static int Remaining(Distribution dist,LoadTask task,string id)
    {
        var sum=StoredRemaining(dist,id);
        if(Chambered(dist,task)==id)sum++;
        return sum;
    }

    // ------------------------------------------------------------ player controls

    [HarmonyPostfix,HarmonyPatch(typeof(PlayerVehicleController),nameof(PlayerVehicleController.UpdateControl)),HarmonyPriority(Priority.Last)]
    private static void Tick(PlayerVehicleController __instance)
    {
        if(!Ready)return;
        Guard("Play controls",()=>
        {
            AmmoLoadoutKeys.Configure();
            var press=Enabled?AmmoLoadoutKeys.Poll():default;
            var cannon=Enabled?ActiveCannon(__instance):null;
            if(press.Command!=KeyCommand.None)
            {
                if(cannon!=null)Switch(cannon,press);
                else Report("Switch key pressed, but no single cannon with a carried mix is active on this vehicle.");
            }
            if(AmmoLoadoutHud.Tick())AmmoLoadoutHud.Update(HudCannon(__instance,cannon));
        });
    }

    private static Cannon? ActiveCannon(PlayerVehicleController controller)
    {
        var controlled=controller.ControlledVehicle;
        if(controlled==null)return null;
        // The native ControlledVehicle is the battle VehicleBehaviour instance; the
        // same instance identity keys every distribution (cannon.Vehicle.Behaviour).
        var pointer=controlled.Pointer;
        var lives=new List<Cannon>();
        Cannon? frozen=null;var frozenCount=0;
        foreach(var pair in Distributions)
        {
            if(pair.Key.Vehicle!=pointer)continue;
            var cannon=pair.Value.Instance;
            if(cannon==null)continue;
            var blueprint=cannon.Blueprint;
            if(blueprint==null||!Loadouts.TryGetValue(blueprint.Pointer,out var mix)||mix.Count==0||AmmoLoadoutRules.Total(mix)<=0)continue;
            if(pair.Value.Frozen){frozenCount++;frozen=cannon;continue;}
            lives.Add(cannon);
        }
        if(lives.Count==1)return lives[0];
        // Several cannons carry a mix (e.g. main gun plus ATGM launcher): switch the
        // one whose gunner serves the selected native fire group.
        if(lives.Count>1)
        {
            var group=controller.FireGroup;
            var selected=lives.Where(c=>c.operable?.behaviour?.TryCast<Gunner>() is {} gunner&&(gunner.FireGroup&group)!=0).ToArray();
            if(selected.Length==1)return selected[0];
            Report($"{lives.Count} cannons carry a mix and {selected.Length} serve fire group {group}; select one fire group with exactly one of them.");
            return null;
        }
        // Exactly one frozen cannon keeps its one-line battle notice visible while every
        // switch attempt stays native; anything else is ambiguous and stays untouched.
        return frozenCount==1?frozen:null;
    }

    private static void Switch(Cannon cannon,KeyPress press)
    {
        var blueprint=cannon.Blueprint;
        if(blueprint==null||!Loadouts.TryGetValue(blueprint.Pointer,out var entries)||entries.Count==0||AmmoLoadoutRules.Total(entries)<=0)return;
        var instance=cannon.Vehicle?.Behaviour;
        if(instance==null)return;
        var key=(instance.Pointer,cannon.VUID.Value);
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        var frozen=Distributions.TryGetValue(key,out var dist)&&dist!.Frozen;
        if(frozen)
        {
            Detail($"cannon {cannon.VUID.Value}: ammunition mix is unavailable; return to Edit to fix the loadout.");
            return;
        }
        var hasMix=dist!=null&&task!=null;
        int AvailableForNext(string id)=>hasMix
            ?(Allowed(cannon,id)?ReachableRemaining(cannon,dist!,id,out _):0)
            :(Allowed(cannon,id)?Planned(entries,id):0);
        var current=Switched.TryGetValue(key,out var switched)?switched:RuntimeShellSelection.StoredSelection(blueprint);
        string? next;
        if(press.Command==KeyCommand.Slot)
        {
            next=AmmoLoadoutRules.Slot(entries,press.Slot);
            if(next!=null&&!Allowed(cannon,next)){SetFeedback(key,$"{Label(next)}: unavailable in this era.");return;}
            if(next!=null&&AvailableForNext(next)<=0)
            {
                if(hasMix)
                {
                    ReachableRemaining(cannon,dist!,next,out var why);
                    var inReserve=StoredRemaining(dist!,next);
                    if(inReserve>0&&!string.IsNullOrEmpty(why))SetFeedback(key,$"{Label(next)}: 0 ready ({inReserve} in reserve - {why}).");
                    else if(Chambered(dist!,task!)==next)SetFeedback(key,$"{Label(next)}: 0 in storage (last round in gun).");
                    else SetFeedback(key,$"{Label(next)}: empty.");
                }
                else SetFeedback(key,$"{Label(next)}: empty.");
                Detail($"slot {press.Slot+1} ({Label(next)}) has no loadable rounds.");return;
            }
        }
        else next=AmmoLoadoutRules.Step(entries,AvailableForNext,current,press.Command==KeyCommand.Next?1:-1);
        if(next==null||next==current)
        {
            SetFeedback(key,"No other carried type available.");
            Detail($"cannon {cannon.VUID.Value}: no other carried type has rounds; nothing switched.");
            return;
        }
        Switched[key]=next;
        BeginSwitchTrace(cannon,task,next);
        var left=AvailableForNext(next);
        var busyLoading=task!=null&&task.State==LoadState.Loading&&task.currentRequest!=LoadRequest.Consumed;
        SetFeedback(key,busyLoading?$"After current load: {Label(next)} ({left})":$"Next load: {Label(next)} ({left})");
        Plugin.ModLog.LogInfo($"[Ammo loadout] cannon {cannon.VUID.Value} next load: {Label(next)} ({left} ready/loadable); any chambered round keeps its identity.");
    }

    private static int Planned(List<AmmoLoadoutEntry> entries,string id)
    {
        var index=AmmoLoadoutRules.IndexOf(entries,id);
        return index<0?0:entries[index].Count;
    }

    /// <summary>
    /// Immutable identity for one exact cannon. It is the single source for the battle
    /// HUD and <see cref="BallisticsAmmoApi"/>; no process-wide selected-round state exists.
    /// </summary>
    internal static bool TrySnapshot(Cannon cannon,out AmmoSnapshot snapshot)
    {
        snapshot=null!;
        if(!Enabled)return false;
        var blueprint=cannon.Blueprint;
        if(blueprint==null||!Loadouts.TryGetValue(blueprint.Pointer,out var entries)||entries.Count==0||AmmoLoadoutRules.Total(entries)<=0)return false;
        var instance=cannon.Vehicle?.Behaviour;
        if(instance==null)return false;
        var key=(instance.Pointer,cannon.VUID.Value);
        var hasMix=Distributions.TryGetValue(key,out var dist);
        if(hasMix&&dist!.Frozen)
        {
            snapshot=new AmmoSnapshot(null,null,null,null,null,null,false,0,Array.Empty<AmmoSnapshotRound>(),true,dist.FailureReason??"Invalid loadout.");
            return true;
        }
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        var live=hasMix&&task!=null;
        var selected=AmmoSnapshotFormat.NextSelection(entries,
            Switched.TryGetValue(key,out var switched)?switched:RuntimeShellSelection.StoredSelection(blueprint));
        var chambered=live?Chambered(dist!,task!):null;
        var rounds=new List<AmmoSnapshotRound>();
        var total=0;
        foreach(var entry in entries)
        {
            var id=entry.ProfileId;
            var count=live?Remaining(dist!,task!,id):entry.Count;
            total+=count;
            rounds.Add(new AmmoSnapshotRound(id,Label(id),Code(id),count,id==chambered,id==selected));
        }
        double? muzzle=null;
        if(chambered!=null)
            try
            {
                var shell=chambered==ShellProfiles.Vanilla?null:RuntimeShellEra.Resolve(RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==chambered),cannon.Vehicle);
                muzzle=AmmoMuzzle.Velocity(shell,cannon.Blueprint.Caliber,cannon.Blueprint.MuzzleVelocity,cannon.Blueprint.PenetratorConstant);
            }
            catch(Exception){muzzle=null;}
        snapshot=new AmmoSnapshot(selected,Label(selected),Code(selected),
            chambered,chambered==null?null:Label(chambered),chambered==null?null:Code(chambered),
            chambered!=null,total,rounds,false,null,muzzle);
        return true;
    }

    private static string Code(string? id)
    {
        if(id==null||id==ShellProfiles.Vanilla)return "STOCK";
        var profile=RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==id);
        return AmmoSnapshotFormat.CodeFor(profile?.Behavior,profile?.Label??id);
    }

    /// <summary>Compact battle display of the carried mix, or null when hidden.</summary>
    // ---------------------------------------------------------- 0.22.3 Kit HUD data

    /// <summary>What the Kit HUD shows for one battle cannon: always the loaded shell, plus the mix rows when carried.</summary>
    internal static AmmoHudView? HudView(Cannon cannon)
    {
        if(hud?.Value==false)return null;
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        var state=task==null?AmmoLoadState.Unknown:HoldsRound(task)?(task.State==LoadState.Loaded?AmmoLoadState.Loaded:AmmoLoadState.Loading):AmmoLoadState.Empty;
        var notice=MixNotice(cannon);
        if(Enabled&&TrySnapshot(cannon,out var snapshot))return AmmoHudFormat.Mix(snapshot,state,notice);
        var profile=RuntimeShellSelection.CannonProfile(cannon);
        var name=profile?.Label??"Native shell";
        var code=profile==null?"STOCK":AmmoSnapshotFormat.CodeFor(profile.Behavior,profile.Label);
        return AmmoHudFormat.Native(name,code,NativeRounds(cannon,task),state,notice);
    }
    /// <summary>One line explaining an inactive or partly unusable mix (logged once); empty when all is well.</summary>
    private static string MixNotice(Cannon cannon)
    {
        var blueprint=cannon.Blueprint;
        if(!Enabled||blueprint==null||!Loadouts.TryGetValue(blueprint.Pointer,out var entries)||AmmoLoadoutRules.Total(entries)<=0)return "";
        var instance=cannon.Vehicle?.Behaviour;if(instance==null)return "";
        var off=AmmoInspectorPolicy.Disallowed(entries,id=>Allowed(cannon,id));
        if(Distributions.TryGetValue((instance.Pointer,cannon.VUID.Value),out var dist))
        {
            if(dist.Frozen)return "";
            // 0.22.7: nothing left in any carried type: say so on the HUD instead of leaving a silent, empty gun.
            var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
            if(task!=null&&!HoldsRound(task))
            {
                var preferred=Switched.TryGetValue((instance.Pointer,cannon.VUID.Value),out var last)?last:RuntimeShellSelection.StoredSelection(blueprint);
                string? reserveReason=null;
                var empty=AmmoSelection.OutNotice(entries,preferred,id=>Usable(cannon,dist,id)?ReachableRemaining(cannon,dist,id,out reserveReason):0,Label);
                if(empty.Length>0)
                {
                    if(entries.Any(e=>StoredRemaining(dist,e.ProfileId)>0)&&TryGetReachableStorage(cannon,out _,out var why)&&!string.IsNullOrEmpty(why))
                        return $"Ready magazine empty ({why}).";
                    return empty;
                }
            }
            if(off.Count>0)
            {
                var text=$"{Label(off[0])}{(off.Count>1?$" +{off.Count-1}":"")}: not available in this era, skipped.";
                Report(text);return text;
            }
            var key=(instance.Pointer,cannon.VUID.Value);
            if(SwitchFeedback.TryGetValue(key,out var fb))
            {
                if(Time.unscaledTime<=fb.Until)return fb.Text;
                SwitchFeedback.Remove(key);
            }
            return "";
        }
        var stored=RuntimeShellSelection.StoredSelection(blueprint);
        if(stored!=null&&AmmoLoadoutRules.IndexOf(entries,stored)<0){Report("Mix inactive: selected shell not in mix ("+Label(stored)+").");return "Mix inactive: selected shell not in mix.";}
        return "Mix not stocked yet (stocks at the first load).";
    }
    /// <summary>Native rounds left for a cannon without a mix: its racks and carousel plus the round in the gun.</summary>
    private static int? NativeRounds(Cannon cannon,LoadTask? task)
    {
        try
        {
            var sum=0;
            foreach(var rack in ScanRacks(cannon,true))sum+=(int)rack.Behaviour!.AmountStored;
            if(CarouselStore(cannon).Aggregate is {} carousel)sum+=(int)carousel.AmountStored;
            if(task!=null&&HoldsRound(task))sum++;
            return sum;
        }
        catch(Exception){return null;}
    }
    /// <summary>The cannon the HUD follows: the active mix cannon, else the controlled vehicle's cannon in the selected fire group.</summary>
    private static Cannon? HudCannon(PlayerVehicleController controller,Cannon? mix)
    {
        if(mix!=null)return mix;
        var pointer=controller.ControlledVehicle?.Pointer??IntPtr.Zero;
        if(pointer==IntPtr.Zero)return null;
        Cannon? first=null,grouped=null;int count=0;var group=controller.FireGroup;
        foreach(var pair in Live)
        {
            if(pair.Key.Vehicle!=pointer)continue;
            var c=pair.Value;count++;
            if(first==null||c.VUID.Value<first.VUID.Value)first=c;
            if(grouped==null&&c.operable?.behaviour?.TryCast<Gunner>() is {} gunner&&(gunner.FireGroup&group)!=0)grouped=c;
        }
        return count==1?first:grouped??first;
    }

    // -------------------------------------------------------------------- cleanup

    [HarmonyPrefix,HarmonyPatch(typeof(VehicleBehaviour),nameof(VehicleBehaviour.Release))]
    private static void End(VehicleBehaviour __instance)
    {
        Guard("Release",()=>
        {
            foreach(var key in Registrations.Keys.Where(k=>k.Vehicle==__instance.Pointer).ToArray())
            {
                var registration=Registrations[key];
                Registrations.Remove(key);
                registration.Register.RemoveUser(registration.Guid);
            }
            foreach(var key in FailedConsumptions.Where(p=>p.Value.Vehicle==__instance.Pointer).Select(p=>p.Key).ToArray())FailedConsumptions.Remove(key);
            foreach(var key in Distributions.Keys.Where(k=>k.Vehicle==__instance.Pointer).ToArray())Distributions.Remove(key);
            foreach(var key in Switched.Keys.Where(k=>k.Vehicle==__instance.Pointer).ToArray())Switched.Remove(key);
            foreach(var key in Waited.Where(k=>k.Vehicle==__instance.Pointer).ToArray())Waited.Remove(key);
            foreach(var key in Live.Keys.Where(k=>k.Vehicle==__instance.Pointer).ToArray())Live.Remove(key);
            ForgetVehicle(__instance.Pointer);
            foreach(var store in Stores.Values.Where(s=>s.Cannon.Vehicle?.Behaviour?.Pointer==__instance.Pointer).ToArray())
            {foreach(var view in store.Views.Values)ViewOwners.Remove(view.Pointer);Stores.Remove(store.Aggregate.Pointer);}
        });
    }
}

