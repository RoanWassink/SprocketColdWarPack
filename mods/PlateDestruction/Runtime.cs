using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PartDecoupling;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace PlateDestructionP0;

// 0.8.4: Thin MonoBehaviour host so Il2CppInterop registers only Start/Update/OnDestroy (zero unsupported-parameter warnings).
public sealed class RuntimeDriver:MonoBehaviour
{
    public RuntimeDriver(IntPtr pointer):base(pointer){}
    private Runtime? core;
    public void Start(){core=new Runtime();core.Start();}
    public void Update(){core?.Update();}
    public void OnDestroy(){core?.OnDestroy();core=null;}
}

// 0.5.0: Destructible and deformable plates, fast hit/blast filtering, core-part attachment guards, and InPlace armour updates.
public sealed partial class Runtime
{

    private sealed class PlateEntry
    {
        internal int Vuid;internal PlateStructure Plate=null!;internal Renderer[] Rends=Array.Empty<Renderer>();
        internal MeshFilter? OccFilter;internal IntPtr OccMeshPtr;internal V3[]? OccVerts;internal int[]? OccTris;internal TriAccel? OccAccel;
    }
    private sealed class Tracked
    {
        internal Vehicle V=null!;internal IntPtr Pointer;internal long Root;internal string Name="";
        internal IReadOnlyCompoundStructure Model=null!;internal IntPtr ModelPtr;
        internal Il2CppSystem.EventHandler<PenetrationSimulationRunEvent> Handler=null!;
        internal float Seen;internal bool InventoryDone;internal Dictionary<int,VehicleComponent> Map=new();
        internal int Shots;
        internal List<int> Destr=new();
        internal HashSet<int> DestrSet=new();
        internal List<PlateEntry> AllPlates=new();
    }
    private sealed class Att
    {
        internal VehicleComponent Comp=null!;internal int Vuid;internal string Type="",Source="";
        internal PlateSide Side;internal bool Covered,Done;internal int Cell=-1;
        internal List<Renderer>? CachedRenderers;
    }
    private readonly Dictionary<(long,int),List<Att>> attached=new();
    private LogBudget blastBudget=new(200,50);
    private readonly BlastDedup blastDedup=new();
    private bool kineticWarned;
    private sealed class Hit
    {
        internal Tracked T=null!;internal VehicleComponent Comp=null!;internal int Vuid;internal Vector3 Pos,Normal,Dir;internal int CalibreMm;
    }
    private sealed class Measure
    {
        internal Tracked T=null!;internal int Vuid;internal bool On;internal Stopwatch Clock=null!;internal int StartFrame,HashBefore,SkipBefore,BuiltBefore;
        internal IntPtr ModelBefore;internal bool Built;
    }

    private readonly Dictionary<IntPtr,Tracked> tracked=new();
    private LogBudget budget=new(200,50);
    private readonly SessionTracker session=new();
    private readonly HitTally tally=new();
    private Hit? last;
    private Measure? pending;
    private VisualCheck? visualCheck;
    private (long Root,int Vuid,bool Expect)? probe;
    private bool probeReady;
    private Key keyB=Key.Delete;
    private readonly DamageLedger led=new();
    private readonly List<BreakJob> breakQueue=new();
    private readonly Queue<Hit> decalQueue=new();
    private readonly Dictionary<(long,int),PlateStats> stats=new();
    private readonly List<GameObject> hiddenDecals=new();
    private readonly List<(Tracked T,VehicleComponent Comp,int UntilFrame,PlateSide Side)> sweeps=new();
    private readonly HashSet<long> activeRoots=new();
    private readonly List<SimHitMark> recentSimHits=new();
    private float scanAt;
    private ProjectileEffectConfig? effectConfig;
    private int unresolved;
    private bool simTypeWarned;
    private bool cachedControl,cachedDesigner,cachedMainMenu,cachedPhoto,inSession,inPhotomode;
    private string lastSceneSig="";
    private int lastSceneHandle=int.MinValue,lastSceneCount=-1,playingCheckedFrame=-100;
    private static PlateState State=>Patches.State;
    private static System.Collections.Concurrent.ConcurrentDictionary<(long,int),SidePlan> Plans=>BuilderPatches.Plans;

    private void SampleScenes(out bool hasControl,out bool hasDesigner,out bool hasMainMenu,out bool hasPhoto,out int activeHandle,out string? sceneList)
    {
        var act=SceneManager.GetActiveScene();
        activeHandle=act.handle;
        int sc=SceneManager.sceneCount;
        int fc=Time.frameCount;
        sceneList=null;
        if(activeHandle==lastSceneHandle&&sc==lastSceneCount&&fc-playingCheckedFrame<15)
        {
            hasControl=cachedControl;hasDesigner=cachedDesigner;hasMainMenu=cachedMainMenu;hasPhoto=cachedPhoto;
            return;
        }
        lastSceneHandle=activeHandle;lastSceneCount=sc;playingCheckedFrame=fc;
        bool ctrl=false,des=false,menu=false,pho=false;
        StringBuilder? sb=Plugin.Instance.DetailedLogging.Value?new StringBuilder():null;
        for(int i=0;i<sc;i++)
        {
            string nm=SceneManager.GetSceneAt(i).name;
            if(sb!=null){if(i>0)sb.Append(',');sb.Append(nm);}
            if(nm=="VehicleControlUI")ctrl=true;
            else if(nm=="VehicleDesignerUI")des=true;
            else if(nm.Contains("MainMenu",StringComparison.OrdinalIgnoreCase))menu=true;
            if(nm.Contains("Photo",StringComparison.OrdinalIgnoreCase))pho=true;
        }
        cachedControl=hasControl=ctrl;
        cachedDesigner=hasDesigner=des;
        cachedMainMenu=hasMainMenu=menu;
        cachedPhoto=hasPhoto=pho;
        if(sb!=null)sceneList=$"active='{act.name}' loaded=[{sb}]";
    }
    private string? RefreshSession()
    {
        SampleScenes(out bool hasControl,out bool hasDesigner,out bool hasMainMenu,out bool hasPhoto,out int activeHandle,out string? sceneList);
        var reason=session.Update(hasControl,hasDesigner,hasMainMenu,hasPhoto,activeHandle,out inSession,out inPhotomode);
        if(sceneList!=null&&sceneList!=lastSceneSig)
        {
            lastSceneSig=sceneList;
            if(Plugin.Instance.DetailedLogging.Value)
                Plugin.Info($"[SCENE] {sceneList} session={inSession} photo={inPhotomode}{(reason!=null?" reset="+reason:"")}");
        }
        return reason;
    }
    private static string ClassName(Il2CppObjectBase? o)
    {
        if(o==null)return "null";
        try{var klass=IL2CPP.il2cpp_object_get_class(o.Pointer);return Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass))??"?";}catch{return "?";}
    }
    private static long Key64(VehicleComponent c)=>c.VehicleRoot?.Pointer.ToInt64()??0;

    // ---- lifecycle -------------------------------------------------------------------------------------------
    public void Start()
    {
        try
        {
            var p=Plugin.Instance;
            if(!Enum.TryParse(p.KeyBreak.Value,true,out keyB)){Plugin.Warn("bad key "+p.KeyBreak.Value+", using Delete");keyB=Key.Delete;}
            budget=new LogBudget(Math.Max(0,p.MaxLines.Value),Math.Max(1,p.SummaryEvery.Value));
            blastBudget=new LogBudget(Math.Max(0,p.MaxLines.Value),Math.Max(1,p.SummaryEvery.Value));
            if(!p.KineticLogOnly.Value&&!kineticWarned){kineticWarned=true;Plugin.Warn("Kinetic/LogOnly=false is not supported: kinetic hits never tear off a plate (log only).");}
            StartDents();
            RefreshSession();
            Plugin.Info("runtime started; playing="+inSession+" break key "+keyB);
        }
        catch(Exception e){Plugin.Warn("start: "+e);}
    }

    public void Update()
    {
        try
        {
            bool wasInSession=inSession;
            var reason=RefreshSession();
            if(reason!=null)ResetAll(reason);
            if(!inSession){Blasts.Clear();return;}
            if(!wasInSession)scanAt=0;
            if(Time.realtimeSinceStartup>=scanAt){scanAt=Time.realtimeSinceStartup+1f;Scan();}
            if(!inPhotomode)PollKeys();
            PollMeasure();
            PollCoalescedRebuilds();
            ProcessBlasts();
            ProcessDents();
            SnapMarks();
            ProcessBreaks();
            StepDebris();
            ProcessDecals();
            SweepDecals();
            PollKeep();
            PollVisual();
        }
        catch(Exception e){Plugin.Warn("update: "+e.Message);}
    }
    public void OnDestroy(){try{ResetAll("plugin destroyed");}catch{}}

    private void PollKeys()
    {
        var kb=Keyboard.current;if(kb==null)return;
        if(kb[keyB].wasPressedThisFrame)Safe("break key",BreakNow);
        if(kb[keyDent].wasPressedThisFrame)Safe("dent key",TestDent);
    }
    private static void Safe(string what,Action a){try{a();}catch(Exception e){Plugin.Warn(what+" failed: "+e);}}

    // ---- vehicle tracking ------------------------------------------------------------------------------------
    private void Scan()
    {
        var found=Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None);
        var alive=new HashSet<IntPtr>();
        foreach(var v in found)
        {
            if(v==null)continue;
            bool released;try{released=v.Released;}catch{released=true;}
            if(released)continue;
            alive.Add(v.Pointer);
            IReadOnlyCompoundStructure? dm;try{dm=v.DamageModel;}catch{dm=null;}
            if(!tracked.TryGetValue(v.Pointer,out var t))
            {
                if(dm==null)continue;
                int count;try{count=v.ObjectReader?.Count??0;}catch{count=0;}
                if(count==0)continue;
                long rootPtr=0;try{rootPtr=v.ObjectReader?.Root?.Pointer.ToInt64()??0;}catch{}
                t=new Tracked{V=v,Pointer=v.Pointer,Root=rootPtr,Name=v.gameObject.name+"#"+v.Pointer.ToInt64().ToString("X"),Seen=Time.realtimeSinceStartup};
                tracked[v.Pointer]=t;
                Subscribe(t,dm);
                BuildMap(t);
                LogFlags(t);
                Plugin.Info($"vehicle adopted {t.Name}: components registered={count} structureHash={dm.StructureHash}");
            }
            else if(dm!=null&&dm.Pointer!=t.ModelPtr)
            {
                Plugin.Info($"{t.Name}: DamageModel instance REPLACED (was {t.ModelPtr.ToInt64():X}, now {dm.Pointer.ToInt64():X}); resubscribing.");
                Subscribe(t,dm);
            }
            if(!t.InventoryDone&&Time.realtimeSinceStartup-t.Seen>=2f)
            {
                t.InventoryDone=true;
                var tt=t;
                if(Plugin.Instance.EnableInventory.Value&&Plugin.Instance.DetailedLogging.Value)Safe("inventory",()=>Inventory(tt));
                if(t.AllPlates.Count==0){BuildMap(t);LogFlags(t);}
            }
        }
        foreach(var key in tracked.Keys.Where(k=>!alive.Contains(k)).ToArray())
        {
            var t=tracked[key];
            Plugin.Info($"vehicle released {t.Name}: clearing its excluded/hidden plates and damage counters.");
            long relRoot=t.Root!=0?t.Root:RootOf(t);
            RestoreInPlaceArmour(r=>r==relRoot);
            coalescer.ClearRoot(relRoot);
            foreach(var hk in holeDmg.Keys.Where(k=>k.Root==relRoot).ToArray())holeDmg.Remove(hk);
            recentSimHits.RemoveAll(s=>s.Root==relRoot);
            RestoreSnappedMarks(relRoot);
            foreach(var pk in Plans.Keys.Where(k=>k.Item1==relRoot).ToArray())if(Plans.TryRemove(pk,out var rp))SideOps.Restore(rp);
            State.ClearRoot(relRoot);led.ClearRoot(relRoot);
            foreach(var ak in attached.Keys.Where(k=>k.Item1==relRoot).ToArray())attached.Remove(ak);
            keepMeshes.RemoveAll(x=>x.Root==relRoot);torn.RemoveAll(x=>x.Root==relRoot);
            foreach(var ck in BuilderPatches.Cells.Keys.Where(k=>k.Item1==relRoot).ToArray())BuilderPatches.Cells.TryRemove(ck,out _);
            cellFailed.RemoveWhere(k=>k.Item1==relRoot);
            RestoreMerged(m=>m.Root==relRoot);
            { int dn=DisposeDents(k=>k.Item1==relRoot); if(dn>0)Plugin.Info($"[RESET] released vehicle: {dn} private dent mesh(es) destroyed."); }
            if(lastDestructible?.T==t)lastDestructible=null;
            tracked.Remove(key);
            if(last?.T==t)last=null;
            if(pending?.T==t)pending=null;
            if(visualCheck?.T==t)visualCheck=null;
        }
    }
    private static long RootOf(Tracked t)
    {
        if(t.Root!=0)return t.Root;
        try{t.Root=t.V.ObjectReader?.Root?.Pointer.ToInt64()??0;}catch{}
        return t.Root;
    }
    private void Subscribe(Tracked t,IReadOnlyCompoundStructure dm)
    {
        if(t.Handler!=null&&t.Model!=null){try{t.Model.remove_SimulationRan(t.Handler);}catch{}}
        t.Model=dm;t.ModelPtr=dm.Pointer;
        if(!Plugin.Instance.EnableHitLog.Value)return;
        t.Handler=DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler<PenetrationSimulationRunEvent>>(
            (Action<Il2CppSystem.Object,PenetrationSimulationRunEvent>)((_,e)=>{try{OnSim(t,e);}catch(Exception ex){Plugin.Warn("SimulationRan handler: "+ex.Message);}}))!;
        dm.add_SimulationRan(t.Handler);
    }
    private void BuildMap(Tracked t)
    {
        t.Map.Clear();
        t.AllPlates.Clear();
        var reader=t.V.ObjectReader;if(reader==null)return;
        var items=reader.Items;
        for(int i=0;i<reader.Count;i++)
        {
            var comps=items[i]?.behaviours;if(comps==null)continue;
            foreach(var c in comps)
            {
                if(c==null)continue;
                int vuid=c.VUID.Value;
                if(t.Map.TryAdd(vuid,c))
                {
                    var pl=c.TryCast<PlateStructure>();
                    if(pl!=null)t.AllPlates.Add(new PlateEntry{Vuid=vuid,Plate=pl,Rends=PlateUnityRenderers(c,t.V).ToArray()});
                }
            }
        }
    }
    private VehicleComponent? Resolve(Tracked t,int id)
    {
        if(t.Map.TryGetValue(id,out var c)&&c!=null&&!c.WasCollected)return c;
        BuildMap(t);
        return t.Map.TryGetValue(id,out c)?c:null;
    }

    // ---- inventory -------------------------------------------------------------------------------------------
    private void Inventory(Tracked t)
    {
        BuildMap(t);
        var reader=t.V.ObjectReader;var items=reader.Items;
        int plates=0,models=0,modelsWithColliders=0,all=0;var types=new Dictionary<string,int>();var decouplers=new List<string>();
        for(int i=0;i<reader.Count;i++)
        {
            var comps=items[i]?.behaviours;if(comps==null)continue;
            foreach(var c in comps)
            {
                if(c==null)continue;all++;
                var n=ClassName(c);types[n]=types.GetValueOrDefault(n)+1;
                if(c.TryCast<PlateStructure>()!=null)plates++;
                var m=c.TryCast<VehicleObjectModel>();
                if(m!=null){models++;if(m.generateDamageColliders)modelsWithColliders++;}
                var d=c.TryCast<PartDecoupler>();
                if(d!=null)decouplers.Add($"vuid={c.VUID.Value} id={c.ComponentID} decoupled={d.Decoupled} usePhysics={d.usePhysics} mass={d.decoupleMass:0.##}");
            }
        }
        var sb=new StringBuilder();foreach(var kv in types.OrderByDescending(k=>k.Value).Take(14))sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        Plugin.Info($"[INVENTORY] {t.Name}: components={all} PlateStructure={plates} VehicleObjectModel={models} (with damage colliders={modelsWithColliders}); types: {sb}");
        try
        {
            var applier=t.Model.TryCast<CompoundStructure>()?.DamageApplier;
            var health=applier?.TryCast<VehicleHealthRegister>();
            if(health==null)Plugin.Info("[INVENTORY] health register: not reachable from the damage applier ("+ClassName(applier)+")");
            else
            {
                var pools=health.ComponentHealthPools;int n=pools==null?0:pools.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponentHealth>>()?.Count??-1;if(n<0){Plugin.Info("[INVENTORY] ComponentHealthPools: count not readable through IReadOnlyCollection cast");n=0;}int plateOwners=0,modelOwners=0;var owners=new Dictionary<string,int>();
                for(int i=0;i<n;i++)
                {
                    var o=pools![i].Owner;var name=ClassName(o);owners[name]=owners.GetValueOrDefault(name)+1;
                    if(o?.TryCast<PlateStructure>()!=null)plateOwners++;
                    if(o?.TryCast<VehicleObjectModel>()!=null)modelOwners++;
                }
                var os=new StringBuilder();foreach(var kv in owners.OrderByDescending(k=>k.Value))os.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                Plugin.Info($"[INVENTORY] ComponentHealthPools={n}; owned by PlateStructure={plateOwners}, VehicleObjectModel={modelOwners}; owner types: {os}");
            }
        }
        catch(Exception e){Plugin.Warn("health inventory: "+e.Message);}
        if(Plugin.Instance.LogDecouplers.Value)
        {
            Plugin.Info($"[INVENTORY] PartDecoupler components: {decouplers.Count}");
            foreach(var s in decouplers.Take(20))Plugin.Info("[INVENTORY]   decoupler "+s);
        }
    }

    // ---- hit log ---------------------------------------------------------------------------------------------
    private static string Outcome(FragmentFlag f)
    {
        if((f&FragmentFlag.Penetrated)!=0)return (f&FragmentFlag.ModelExit)!=0?"through (exited model)":"penetrated";
        if((f&FragmentFlag.Embedded)!=0)return "stopped (embedded)";
        if((f&FragmentFlag.Deflected)!=0)return "deflected";
        if((f&FragmentFlag.Killed)!=0)return "killed";
        return "flags="+f;
    }
    private void Line(string s){if(budget.TryLine())Plugin.Info(s);}

    private void OnSim(Tracked t,PenetrationSimulationRunEvent e)
    {
        var sim=e?.Simulation?.TryCast<PenetrationSimulation>();
        if(sim==null){if(!simTypeWarned){simTypeWarned=true;Plugin.Warn("SimulationRan: simulation is not a PenetrationSimulation ("+ClassName(e?.Simulation)+")");}return;}
        t.Shots++;
        bool summary=budget.Shot();
        var p=Plugin.Instance;
        bool verbose=p.DetailedLogging.Value;
        var objs=sim.objects;int n=sim.FragmentCount;
        var seen=probe!=null?new HashSet<int>():null;
        int origHitsLogged=0,spallWithHits=0;
        bool detail=verbose&&p.LogEveryHitObject.Value;
        bool rawLogged=false;bool haveSeg=false;Vector3 seg0=default,seg1=default;
        var path=new List<PathHit>();
        bool needSpallScan=probe!=null||(verbose&&!budget.Exhausted);

        if(verbose)Line($"[HIT] shot {budget.Shots} on {t.Name}: fragments={n} simHit={sim.Hit}");
        for(int i=0;i<n;i++)
        {
            var f=sim.fragments[i];
            bool orig=(f.flags&FragmentFlag.OriginalPenetrator)!=0;
            if(!orig&&!needSpallScan)continue;
            var ho=f.hitObjects;
            if(ho==null||ho.Length==0)continue;
            if(!orig)
            {
                spallWithHits++;
                if(seen!=null)for(int k=0;k<ho.Length;k++){int o=ho[k];if(o>=0&&o<objs.Length)seen.Add(objs[o].ID);}
                continue;
            }
            var world=sim.SimToWorldSpacePoint(f.spawnPoint+f.direction*f.travelDistance);
            var dir=sim.SimToWorldSpaceDirection(f.direction).normalized;
            var spawnW=sim.SimToWorldSpacePoint(f.spawnPoint);
            if(!haveSeg){haveSeg=true;seg0=spawnW;seg1=world;}
            int cal=(int)Math.Round(f.diameter*1000);
            if(verbose&&!rawLogged){rawLogged=true;RawFields(t,sim,f,i);}
            for(int j=0;j<ho.Length&&origHitsLogged<16;j++)
            {
                int oi=ho[j];
                if(oi<0||oi>=objs.Length){if(verbose)Line($"[HIT]   object index {oi} out of range ({objs.Length})");continue;}
                int id=objs[oi].ID;seen?.Add(id);origHitsLogged++;
                var comp=Resolve(t,id);
                if(comp==null)unresolved++;
                if(detail)
                {
                    string type=comp==null?"UNRESOLVED":ClassName(comp);
                    Line($"[HIT]   id={id} component vuid={(comp==null?-1:comp.VUID.Value)} type={type} componentId={comp?.ComponentID} objIdx={oi} tri={f.hitTriangleIndex} struct={f.hitStructureIndex} plateSpace={PlateSpace(comp,world)} world=({world.x:0.00},{world.y:0.00},{world.z:0.00}) calibre={cal}mm speed={f.speed:0}m/s mass={f.mass*1000:0}g outcome={Outcome(f.flags)} excludedByRouteA={(comp!=null&&State.IsExcluded(t.Root,id))}");
                }
                if(comp!=null)
                {
                    tally.Add(id);
                    path.Add(new PathHit{Comp=comp,Vuid=id,Eligible=Eligible(t,comp),Spawn=spawnW,End=world,Dir=dir,CalibreMm=cal,Mass=f.mass,Speed=f.speed,Flags=f.flags});
                }
            }
        }
        ChooseTarget(t,path);
        CheckTorn(t,haveSeg,seg0,seg1,path);
        if(verbose&&spallWithHits>0)Line($"[HIT]   spall/child fragments with hit objects: {spallWithHits}");
        if(probe is {} pr&&probeReady&&seen!=null)
        {
            bool present=seen.Contains(pr.Vuid);
            Plugin.Info($"[ROUTEA] next shot after toggle: target vuid {pr.Vuid} {(present?"PRESENT":"ABSENT")} in hits (expected {(pr.Expect?"present":"absent")}) -> {(present==pr.Expect?"as expected":"UNEXPECTED")}; hit ids: {string.Join(",",seen.Take(24))}");
            probe=null;probeReady=false;
        }
        if(summary)
            Plugin.Info($"[SUMMARY] shots={budget.Shots} lines written={budget.Lines} suppressed={budget.Suppressed} resolvedHits={tally.Total} unresolved={unresolved} top: {tally.Top()}");
    }

    private sealed class PathHit{internal VehicleComponent Comp=null!;internal int Vuid;internal bool Eligible;internal Vector3 Spawn,End,Dir;internal int CalibreMm;internal float Mass,Speed;internal FragmentFlag Flags;}
    private static bool IsFinite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    private static DentStyle StyleFromFlags(FragmentFlag f)
        =>(f&FragmentFlag.Deflected)!=0?DentStyle.Ricochet:(f&FragmentFlag.Embedded)!=0?DentStyle.Embedded:DentStyle.Kinetic;
    private void ChooseTarget(Tracked t,List<PathHit> path)
    {
        if(path.Count==0)return;
        var p=Plugin.Instance;
        var mode=p.TargetMode.Value.Equals("LastPlate",StringComparison.OrdinalIgnoreCase)?TargetMode.LastPlate:TargetMode.FirstPlate;
        int idx=TargetChooser.Choose(path.Select(x=>x.Eligible).ToList(),mode);
        if(idx<0)
        {
            var p0=path[0];
            if(IsFinite(p0.End))
            {
                var d0=p0.Dir.sqrMagnitude>1e-8f?p0.Dir.normalized:Vector3.down;
                if(recentSimHits.Count>=12)recentSimHits.RemoveAt(0);
                recentSimHits.Add(new SimHitMark(Time.frameCount,new V3(p0.End.x,p0.End.y,p0.End.z),new V3(d0.x,d0.y,d0.z),p0.CalibreMm,t.Root,-1));
            }
            if(p.DetailedLogging.Value)Plugin.Info($"[TARGET] none eligible (mode {mode}); path vuids: {string.Join(",",path.Select(x=>x.Vuid))}");
            return;
        }
        var h=path[idx];
        var entry=EntryPoint(h,out string how);
        var exactEntry=IsFinite(h.End)?h.End:entry;
        var dir=h.Dir;
        last=new Hit{T=t,Comp=h.Comp,Vuid=h.Vuid,Pos=exactEntry,Normal=-dir,Dir=dir,CalibreMm=h.CalibreMm};
        if(recentSimHits.Count>=12)recentSimHits.RemoveAt(0);
        recentSimHits.Add(new SimHitMark(Time.frameCount,new V3(exactEntry.x,exactEntry.y,exactEntry.z),new V3(dir.x,dir.y,dir.z),h.CalibreMm,t.Root,h.Vuid));
        if(p.DetailedLogging.Value||IsDestructible(h.Comp))
            Plugin.Info($"[TARGET] chosen vuid={h.Vuid} type={ClassName(h.Comp)} reason={(mode==TargetMode.FirstPlate?"first plate on path":"last plate on path")}; path vuids: {string.Join(",",path.Select(x=>x.Vuid))}; entry=({exactEntry.x:0.00},{exactEntry.y:0.00},{exactEntry.z:0.00}) via {(IsFinite(h.End)?"fragment hit":how)}");

        int dented=0;
        var seenHits=new Dictionary<(int,PlateSide),Vector3>();
        if(IsDestructible(h.Comp))
        {
            var hPlan=PlanFor(t,h.Comp,h.Vuid);
            var hSide=SideOfPoint(h.Comp,hPlan,exactEntry);
            Safe("count hit",()=>CountHit(t,h,last,StyleFromFlags(h.Flags)));
            seenHits[(h.Vuid,hSide)]=exactEntry;
            dented++;
        }

        if(p.DentNonDestructiblePlates.Value&&p.DentEnabled.Value)
        {
            int cap=Math.Max(1,p.MaxKineticDentsPerShot.Value);
            foreach(var ph in path)
            {
                if(dented>=cap)break;
                if(!ph.Eligible||ph.Comp.TryCast<PlateStructure>()==null)continue;
                DentStyle style=StyleFromFlags(ph.Flags);
                Vector3 ent=IsFinite(ph.End)?ph.End:(ph==h?entry:EntryPoint(ph,out _));
                bool isDestr=IsDestructible(ph.Comp);
                PlateSide pSide=PlateSide.Whole;
                if(isDestr&&Plans.TryGetValue((t.Root,ph.Vuid),out var dPlan))pSide=SideOfPoint(ph.Comp,dPlan,ent);
                var hitKey=(ph.Vuid,pSide);
                if(seenHits.TryGetValue(hitKey,out var prevPos))
                {
                    if(!p.ExitBlowoutEnabled.Value||(ent-prevPos).sqrMagnitude<0.0625f)continue;
                    style=DentStyle.ExitBlowout;
                }
                else seenHits[hitKey]=ent;

                var hit=new Hit{T=t,Comp=ph.Comp,Vuid=ph.Vuid,Pos=ent,Normal=-ph.Dir,Dir=ph.Dir,CalibreMm=ph.CalibreMm};
                if(isDestr&&style!=DentStyle.ExitBlowout)
                {
                    Safe("count hit",()=>CountHit(t,ph,hit,style));
                }
                else
                {
                    double energy=DamageFormula.KineticMJ(ph.Mass,ph.Speed);
                    QueueKineticDent(t,ph.Comp,ph.Vuid,pSide,hit,energy,style,!isDestr);
                }
                dented++;
            }
        }
    }
    private static Vector3 EntryPoint(PathHit h,out string how)
    {
        how="end point (no bounds)";
        try
        {
            var vr=PlateVRs(h.Comp);Bounds b=default;bool have=false;
            foreach(var v in vr){var rend=v.Renderer;if(rend==null)continue;if(!have){b=rend.bounds;have=true;}else b.Encapsulate(rend.bounds);}
            if(!have)return h.End;
            var seg=h.End-h.Spawn;float len=seg.magnitude;if(len<1e-6f){how="spawn (zero-length ray)";return h.Spawn;}
            if(b.Contains(h.Spawn)){how="spawn point (ray starts inside bounds)";return h.Spawn;}
            var ray=new Ray(h.Spawn,seg/len);
            if(b.IntersectRay(ray,out float d)&&d<=len+0.05f){how="ray entry into renderer bounds";return ray.GetPoint(d);}
            Vector3 best=h.End;float bd=float.MaxValue;
            for(int i=0;i<=32;i++){var pt=h.Spawn+seg*(i/32f);var c=b.ClosestPoint(pt);float dd=(c-pt).sqrMagnitude;if(dd<bd){bd=dd;best=c;}}
            how="bounds point closest to the ray";return best;
        }
        catch{return h.End;}
    }

    private static string PlateSpace(VehicleComponent? c,Vector3 world)
    {
        try{var p=c?.TryCast<PlateStructure>();if(p==null)return "n/a";var v=p.WorldToStructureSpace(world);return $"({v.x:0.000},{v.y:0.000},{v.z:0.000})";}catch{return "err";}
    }
    private void RawFields(Tracked t,PenetrationSimulation sim,Fragment f,int index)
    {
        if(!Plugin.Instance.LogRawFields.Value)return;
        try
        {
            var dl=f.depthLookup;var dls=new StringBuilder();
            if(dl!=null)for(int k=0;k<dl.Length&&k<8;k++)dls.Append(Convert.ToString(dl[k],System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            var ho=f.hitObjects;var hos=new StringBuilder();
            if(ho!=null)for(int k=0;k<ho.Length&&k<12;k++)hos.Append(ho[k]).Append(' ');
            Line($"[RAW] fragment[{index}] hitTriangleIndex={f.hitTriangleIndex} hitStructureIndex={f.hitStructureIndex} materialIndex={f.materialIndex} pathIndex={f.pathIndex} parentIndex={f.parentIndex} profileIdx={f.projectileProfileIndex} flags={f.flags} travel={f.travelDistance:0.000} spawn=({f.spawnPoint.x:0.000},{f.spawnPoint.y:0.000},{f.spawnPoint.z:0.000}) dir=({f.direction.x:0.000},{f.direction.y:0.000},{f.direction.z:0.000}) end=({f.EndPoint.x:0.000},{f.EndPoint.y:0.000},{f.EndPoint.z:0.000}) hitObjects.Length={ho?.Length} [{hos}] depthLookup.Length={dl?.Length} [{dls}] penetratorDiameter={sim.Penetrator.Diameter*1000:0.0}mm");
            var buf=CompoundStructure.HitBuffer;int size=CompoundStructure.HitBufferSize;
            if(buf!=null)
            {
                var sb=new StringBuilder();
                for(int k=0;k<size&&k<buf.Length&&k<12;k++){var x=buf[k];sb.Append($"[obj={x.objectIndex} tri={x.triangleIndex} d={x.distance:0.000} struct={x.structureIndex} flags={x.flags}] ");}
                Line($"[RAW] CompoundStructure.HitBuffer size={size} (last ray): {sb}");
            }
            else Line("[RAW] CompoundStructure.HitBuffer not readable");
        }
        catch(Exception e){Plugin.Warn("raw fields: "+e.Message);}
    }
    private bool Eligible(Tracked t,VehicleComponent c)
    {
        var p=Plugin.Instance;
        if(led.IsGone(t.Root,c.VUID.Value))return false;
        if(p.AffectPlateStructure.Value&&c.TryCast<PlateStructure>()!=null)return true;
        if(p.AffectModelParts.Value){var m=c.TryCast<VehicleObjectModel>();if(m!=null&&m.generateDamageColliders)return true;}
        return false;
    }

    // ---- target selection -----------------------------------------------------------------------------------
    private Hit? Target()
    {
        if(last==null||last.Comp==null||last.Comp.WasCollected){Plugin.Info("no eligible plate hit yet: fire one shot at a plate first (target = plate hit last).");return null;}
        return last;
    }

    // ---- destructible flag, strength, damage ------------------------------------------------------------------
    private static bool IsDestructible(VehicleComponent c)
    {
        try{var p=c.TryCast<PlateStructure>();return p!=null&&DestructibleFlag.Has(p.Blueprint?.Description);}catch{return false;}
    }
    private void LogFlags(Tracked t)
    {
        var ids=new List<int>();
        foreach(var kv in t.Map){var c=kv.Value;if(c==null||c.WasCollected)continue;if(c.TryCast<PlateStructure>()!=null&&IsDestructible(c))ids.Add(kv.Key);}
        ids.Sort();
        t.Destr=ids;
        t.DestrSet=new HashSet<int>(ids);
        Plugin.Info($"[FLAG] {t.Name}: destructible plates vuid={(ids.Count==0?"none":string.Join(",",ids))}");
        foreach(var id in ids)
        {
            if(!t.Map.TryGetValue(id,out var c)||c==null)continue;
            var cc=c;int vid=id;
            Safe("probe vuid "+id,()=>{var pl=PlanFor(t,cc,vid);CellsFor(t,cc,vid,pl);FindAttached(t,cc,vid,pl);});
        }
    }

    // ---- attached parts (ERA etc.) ----------------------------------------------------------------------------
    private static int ChildCount(Il2CppSystem.Collections.Generic.IReadOnlyList<VehicleTransform>? l)
    {
        if(l==null)return 0;
        var c=l.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleTransform>>();
        return c==null?0:c.Count;
    }
    private void FindAttached(Tracked t,VehicleComponent plate,int vuid,SidePlan? plan)
    {
        long root=t.Root;var list=new List<Att>();var seenV=new HashSet<int>{vuid};var seenT=new HashSet<IntPtr>();
        BuilderPatches.Cells.TryGetValue((root,vuid),out var cells);
        var vehicleGo=t.V.gameObject;
        void Add(VehicleComponent? comp,string src)
        {
            if(comp==null||comp.WasCollected)return;
            int cv=comp.VUID.Value;if(!seenV.Add(cv))return;
            var go=comp.gameObject;if(go==null||go==vehicleGo)return;
            string typeName=ClassName(comp);
            if(AttachedFilter.IsCoreStructuralPart(typeName))return;
            var side=PlateSide.Whole;
            try{if(plan is {PerSide:true,Geo:not null}){var p=plate.TryCast<PlateStructure>();if(p!=null){var l=p.WorldToStructureSpace(comp.transform.position);side=plan.Geo.Choose(new V3(l.x,l.y,l.z));}}}catch{}
            bool covered=comp.TryCast<PlateStructure>()!=null||comp.TryCast<VehicleObjectModel>()!=null;
            int cell=-1;
            try{if(cells!=null){var pp=plate.TryCast<PlateStructure>();if(pp!=null){var l=pp.WorldToStructureSpace(comp.transform.position);var lp=new V3(l.x,l.y,l.z);cell=cells.Plan.CellOfPoint(lp,cells.Plan.SideOfPoint(lp));}}}catch{}
            list.Add(new Att{Comp=comp,Vuid=cv,Type=typeName,Source=src,Side=side,Covered=covered,Cell=cell});
        }
        void Walk(VehicleTransform? node,int depth,string src)
        {
            if(node==null||depth>4||!seenT.Add(node.Pointer))return;
            var ch=node.Children;int n=ChildCount(ch);
            for(int i=0;i<n;i++)
            {
                var child=ch![i];if(child==null)continue;
                var obj=child.VehicleObject;
                var comps=obj?.behaviours;
                if(comps!=null)foreach(var cc in comps)Add(cc,src);
                Walk(child,depth+1,src);
            }
        }
        var vt=plate.VehicleTransform;
        Walk(vt,0,"children");
        try{if(plan is {Mirrored:true}){var mt=vt?.Mirror;if(mt!=null)Walk(mt,0,"mirror children");}}catch{}
        try
        {
            if(plate.gameObject!=vehicleGo)
                foreach(var cc in plate.gameObject.GetComponentsInChildren<VehicleComponent>(true))Add(cc,"unity child");
        }catch{}
        attached[(root,vuid)]=list;
        if(Plugin.Instance.DetailedLogging.Value)
        {
            var sb=new StringBuilder($"[ATTACHED] {vuid} -> ");
            if(list.Count==0)sb.Append("none");
            else
            {
                sb.Append(string.Join(", ",list.Take(12).Select(a=>$"({a.Vuid}, {a.Type}, {SideName(a.Side)}{(a.Covered?"":", visual only")})")));
                if(list.Count>12)sb.Append($" ... (+{list.Count-12} more)");
            }
            Plugin.Info(sb.ToString());
        }
        if(cells!=null&&list.Count>0)
        {
            var perCell=list.Where(a=>a.Cell>=0).GroupBy(a=>a.Cell).Count();
            Plugin.Info($"[ATTACHED] {vuid}: {list.Count(a=>a.Cell>=0)} of {list.Count} part(s) assigned to {perCell} cell(s)");
        }
        var probePart=list.FirstOrDefault(a=>a.Type=="VehicleObjectModel");
        if(probePart!=null)EraProbe(t,probePart);
    }
    private static List<Renderer> AttachedRenderers(Att a,VehicleComponent plate,Vehicle v)
    {
        if(a.CachedRenderers!=null)return a.CachedRenderers;
        var comp=a.Comp;
        var res=new List<Renderer>();var vgo=v.gameObject;
        void AddR(Renderer? r){if(r==null||r.gameObject==vgo||res.Any(x=>x.Pointer==r.Pointer))return;res.Add(r);}
        void AddTree(GameObject? go)
        {
            if(go==null||go==vgo)return;
            try{if(plate.transform!=null&&plate.transform.IsChildOf(go.transform))return;}catch{}
            foreach(var r in go.GetComponentsInChildren<Renderer>(true))AddR(r);
        }
        foreach(var vr in PlateVRs(comp)){AddR(vr.Renderer);}
        foreach(var r in ModelRenderers(comp))AddR(r);
        AddTree(comp.gameObject);
        try{AddTree(comp.vehicleObject?.gameObject);}catch{}
        try{AddTree(comp.VehicleTransform?.gameObject);}catch{}
        a.CachedRenderers=res;
        return res;
    }
    private void HideAttached(BreakJob j,long root)
    {
        if(!attached.TryGetValue((root,j.Vuid),out var list)||list.Count==0)return;
        int hid=0,excl=0,tris=0;var visual=new List<string>();
        var plateOwn=new HashSet<IntPtr>();
        foreach(var vr in PlateVRs(j.Comp)){var r=vr.Renderer;if(r!=null)plateOwn.Add(r.Pointer);}
        var otherSide=new HashSet<IntPtr>();
        if(j.Side!=PlateSide.Whole)
            foreach(var o in list)if(o.Side!=j.Side&&o.Comp!=null&&!o.Comp.WasCollected)
                try{foreach(var r in AttachedRenderers(o,j.Comp,j.T.V))otherSide.Add(r.Pointer);}catch{}
        bool verbose=Plugin.Instance.DetailedLogging.Value;
        foreach(var a in list)
        {
            if(a.Done)continue;
            if(j.CellOnly){if(j.Cells==null||!j.Cells.Contains(a.Cell))continue;}
            else if(j.Side!=PlateSide.Whole&&a.Side!=j.Side)continue;
            if(a.Comp==null||a.Comp.WasCollected)continue;
            a.Done=true;
            var key=(root,a.Vuid);
            if(!hideRecords.TryGetValue(key,out var rec)){rec=new HideRecord();hideRecords[key]=rec;}
            int visibleOwn=0,allHidden=0,found=0;
            foreach(var r in AttachedRenderers(a,j.Comp,j.T.V))
            {
                found++;
                if(plateOwn.Contains(r.Pointer)||otherSide.Contains(r.Pointer))continue;
                if(ScreenVisible(r))
                {
                    visibleOwn++;
                    CollectPart(a.Vuid,j,r);
                }
                if(!rec.Enabled.Any(x=>x.R!=null&&x.R.Pointer==r.Pointer))
                    rec.Enabled.Add((r,r.enabled));
                r.enabled=false;
                allHidden++;
            }
            string path="a (own renderers)",note="";
            int removed=0;
            if(visibleOwn==0)
            {
                path="b (merged mesh)";
                try{removed=HideMerged(a,j,root,plateOwn,otherSide,out note);}catch(Exception e){note="failed: "+e.Message;}
                if(removed==0)path="c (nothing found to hide; see [ERAPROBE])";
            }
            hid+=visibleOwn;tris+=removed;
            State.SetHidden(root,a.Vuid,true);
            if(a.Covered&&Plugin.Instance.EnableRouteA.Value){State.SetExcluded(root,a.Vuid,true);j.HiddenAttachedVuids.Add(a.Vuid);excl++;}
            else visual.Add($"{a.Vuid}:{a.Type}");
            if(verbose)
                Plugin.Info($"[ATTACHED] part vuid={a.Vuid} type={a.Type} side={SideName(a.Side)} cell={a.Cell}: path {path}: {visibleOwn} visible / {allHidden} total renderer(s) locked ({found} found), {removed} triangle(s) cut from merged meshes{(note.Length>0?"; "+note:"")}");
        }
        FlushMerged(j);
        Plugin.Info($"[ATTACHED] vuid={j.Vuid} side={SideName(j.Side)}: {hid} renderer(s) and {tris} merged-mesh triangle(s) of attached parts hidden, {excl} excluded from the damage model{(visual.Count>0?"; visual only: "+string.Join(", ",visual.Take(10))+(visual.Count>10?$" (+{visual.Count-10} more)":""):"")}");
    }

    // ---- keep the visual state ------------------------------------------------------------------------------
    private sealed class KeepMesh{internal MeshFilter F=null!;internal Mesh Want=null!;internal int Vuid;internal string Side="";}
    private readonly List<(long Root,KeepMesh K)> keepMeshes=new();
    private readonly Dictionary<(long,int),int> keepCounts=new();
    private void NoteKeep(long root,int vuid,string what)
    {
        keepCounts.TryGetValue((root,vuid),out var n);n++;keepCounts[(root,vuid)]=n;
        if(n==1||n==10||n==100||n==1000)Plugin.Info($"[KEEP] game restored mesh/renderer of vuid={vuid} {what}; re-applied ({n})");
    }
    private void PollKeep()
    {
        if(!inPhotomode&&Time.frameCount%5!=0)return;
        if(keepMeshes.Count==0&&hideRecords.Count==0&&dents.Count==0)return;
        activeRoots.Clear();foreach(var t in tracked.Values)activeRoots.Add(t.Root);
        foreach(var (root,k) in keepMeshes)
        {
            if(!activeRoots.Contains(root))continue;
            try
            {
                if(k.F==null||k.Want==null)continue;
                var cur=k.F.sharedMesh;
                if(cur==null||cur.Pointer!=k.Want.Pointer){k.F.sharedMesh=k.Want;NoteKeep(root,k.Vuid,"side="+k.Side+" (mesh)");}
            }catch{}
        }
        foreach(var kv in hideRecords)
        {
            if(!activeRoots.Contains(kv.Key.Item1))continue;
            try{foreach(var (r,_) in kv.Value.Enabled)if(r!=null&&r.enabled){r.enabled=false;NoteKeep(kv.Key.Item1,kv.Key.Item2,"(renderer)");}}catch{}
        }
        foreach(var ds in dents.Values)
        {
            if(activeRoots.Contains(ds.Root))Safe("flush low detail",()=>FlushLowDetail(ds));
        }
    }

    // ---- test aid: a shot through a torn side must hit nothing there --------------------------------------------
    private sealed class TornSide{internal long Root;internal int Vuid;internal PlateSide Side;}
    private readonly List<TornSide> torn=new();
    private int checkLines;
    private void CheckTorn(Tracked t,bool haveSeg,Vector3 spawn,Vector3 end,List<PathHit> path)
    {
        if(!haveSeg||torn.Count==0||pending!=null||checkLines>=30)return;
        long root=t.Root;
        foreach(var ts in torn.Where(x=>x.Root==root).ToArray())
        {
            try
            {
                if(!Plans.TryGetValue((root,ts.Vuid),out var plan)||plan.Geo==null)continue;
                var comp=Resolve(t,ts.Vuid);var plate=comp?.TryCast<PlateStructure>();if(plate==null)continue;
                var a=plate.WorldToStructureSpace(spawn);var b=plate.WorldToStructureSpace(end);
                var bb=plan.Geo.Bounds(ts.Side);
                if(!SegmentBox.Intersects(new V3(a.x,a.y,a.z),new V3(b.x,b.y,b.z),bb.Min,bb.Max))continue;
                var sides=new List<PlateSide>();
                foreach(var h in path)if(h.Vuid==ts.Vuid){var l=plate.WorldToStructureSpace(h.End);sides.Add(plan.Geo.Choose(new V3(l.x,l.y,l.z)));}
                var r=TornCheck.Evaluate(sides,ts.Side);
                checkLines++;
                Plugin.Info($"[CHECK] shot through torn vuid={ts.Vuid} side={SideName(ts.Side)}: hits on that side = {r.Hits} (expected 0) -> {(r.Pass?"PASS":"FAIL")}");
                torn.Remove(ts);
            }
            catch(Exception e){Plugin.Warn("check torn: "+e.Message);}
        }
    }
    private SidePlan? PlanFor(Tracked t,VehicleComponent c,int vuid)
    {
        long root=t.Root;
        if(Plans.TryGetValue((root,vuid),out var existing))return existing;
        var p=c.TryCast<PlateStructure>();if(p==null)return null;
        var sb=new StringBuilder();
        var plan=SideOps.Build(p,c,root,vuid,Plugin.Instance.PerSideBreak.Value&&BuilderPatches.Installed,sb);
        Plans[(root,vuid)]=plan;
        Plugin.Info(sb.ToString());
        return plan;
    }
    private static PlateSide SideOfPoint(VehicleComponent c,SidePlan? plan,Vector3 world)
    {
        if(plan==null||!plan.PerSide||plan.Geo==null)return PlateSide.Whole;
        var p=c.TryCast<PlateStructure>();if(p==null)return PlateSide.Whole;
        var local=p.WorldToStructureSpace(world);
        return plan.Geo.Choose(new V3(local.x,local.y,local.z));
    }
    private static string SideName(PlateSide s)=>s==PlateSide.Original?"original":s==PlateSide.Mirror?"mirror":"whole";
    private sealed class PlateStats{internal double ThicknessMm,AreaM2,Strength;internal string Source="";}
    private PlateStats StatsFor(long root,VehicleComponent c,int vuid)
    {
        if(stats.TryGetValue((root,vuid),out var cached))return cached;
        var p=Plugin.Instance;double th=0,area=0;string src="default (no mesh)";
        try
        {
            var mesh=c.TryCast<PlateStructure>()?.Mesh;
            if(mesh!=null)
            {
                var tv=mesh.ThicknessVectors;double sum=0;int n=0;
                if(tv!=null)for(int i=0;i<tv.Length;i++){float m=tv[i].magnitude;if(m>1e-6f&&!float.IsNaN(m)&&!float.IsInfinity(m)){sum+=m;n++;}}
                double vol=mesh.SolidVolume;double mean=n>0?sum/n:0;
                th=n>0?BlastMath.ThicknessMm(mean):0;
                var g=n>0?DamageFormula.FromMesh(mean,vol):null;
                if(g is {} gg)area=gg.AreaM2;
                src=th>0?$"mesh (mean vertex thickness {mean:0.####}, solid volume {vol:0.#####}, {n} vectors)":$"default (mesh values unusable: mean thickness {mean:0.####}, {n} vectors)";
            }
        }
        catch(Exception e){src="default (read failed: "+e.Message+")";}
        var st=new PlateStats{ThicknessMm=th,AreaM2=area,Strength=BlastMath.Strength(p.BlastStrengthPerMm.Value,th,p.DefaultPlateStrength.Value),Source=src};
        stats[(root,vuid)]=st;
        if(p.LogDamage.Value)Plugin.Info($"[PLATE] vuid={vuid}: mean thickness {st.ThicknessMm:0.0} mm, area {st.AreaM2:0.000} m2, blast strength per side {st.Strength:0.0} = {p.BlastStrengthPerMm.Value:0.###}/mm x thickness; source: {st.Source}");
        return st;
    }
    private sealed class BreakJob
    {
        internal Tracked T=null!;internal VehicleComponent Comp=null!;internal int Vuid,Hits;internal Hit H=null!;
        internal PlateSide Side;internal SidePlan? Plan;internal bool HasBlast;internal Vector3 BlastPos;internal V3 BlastLocal;internal double Ratio=1;
        internal readonly Dictionary<string,(Renderer R,int Verts)> Parts=new();
        internal HashSet<int>? Cells;internal PlateCells? PC;internal bool CellOnly;internal DebrisPack? Pack;
        internal readonly List<PartPack> Extra=new();
        internal readonly List<int> HiddenAttachedVuids=new();
        internal List<MergedCandidate>? MergedCandidates;
        internal readonly HashSet<MergedCopy> DirtyMerged=new();
    }

    private void CountHit(Tracked t,PathHit ph,Hit h,DentStyle style=DentStyle.Kinetic)
    {
        if(!IsDestructible(ph.Comp))return;
        var p=Plugin.Instance;long root=t.Root;
        var plan=PlanFor(t,ph.Comp,ph.Vuid);
        var side=SideOfPoint(ph.Comp,plan,h.Pos);
        double energy=KineticPolicy.Apply(led,root,ph.Vuid,side,ph.Mass,ph.Speed,out _);
        if(p.LogDamage.Value)Line($"[KIN] vuid={ph.Vuid} side={SideName(side)} energy={energy:0.00} MJ (no break damage; kinetic hits only make holes/craters)");
        lastDestructible=h;
        if(!led.IsBroken(root,ph.Vuid,side)&&!(side!=PlateSide.Whole&&State.IsSideBroken(root,ph.Vuid,side)))QueueKineticDent(t,ph.Comp,ph.Vuid,side,h,energy,style,false);
        if(p.DecalOnHit.Value&&p.EnableEffects.Value&&!led.IsBroken(root,ph.Vuid,side))decalQueue.Enqueue(h);
    }

    private void QueueBreak(Tracked t,VehicleComponent comp,int vuid,PlateSide side,SidePlan? plan,int hits,Hit h,Vector3? blast=null,double ratio=1)
    {
        long root=t.Root;
        var resolved=SideBreak.Resolve(plan is {PerSide:true},side,side!=PlateSide.Whole&&led.IsBroken(root,vuid,SideBreak.Other(side)));
        if(resolved==PlateSide.Whole&&side!=PlateSide.Whole)led.ForceBreak(root,vuid,PlateSide.Whole);
        V3 bl=new(0,0,0);
        if(blast.HasValue){try{var pl=comp.TryCast<PlateStructure>();if(pl!=null){var l=pl.WorldToStructureSpace(blast.Value);bl=new V3(l.x,l.y,l.z);}}catch{}}
        breakQueue.Add(new BreakJob{T=t,Comp=comp,Vuid=vuid,Hits=hits,H=h,Side=resolved,Plan=plan,HasBlast=blast.HasValue,BlastPos=blast??Vector3.zero,BlastLocal=bl,Ratio=ratio});
    }

    // ---- blasts ----------------------------------------------------------------------------------------------
    private void BLine(string s){if(blastBudget.TryLine())Plugin.Info(s);}
    private static float BoundsDistance(Renderer[] rends,Vector3 world,out Vector3 closest)
    {
        closest=world;
        try
        {
            Bounds b=default;bool have=false;
            for(int i=0;i<rends.Length;i++){var r=rends[i];if(r==null)continue;if(!have){b=r.bounds;have=true;}else b.Encapsulate(r.bounds);}
            if(!have)return float.MaxValue;
            closest=b.ClosestPoint(world);return (closest-world).magnitude;
        }
        catch{return float.MaxValue;}
    }
    private static float BoundsDistance(VehicleComponent c,Vehicle v,Vector3 world,out Vector3 closest)
        =>BoundsDistance(PlateUnityRenderers(c,v).ToArray(),world,out closest);
    private void ProcessBlasts()
    {
        foreach(var e in Blasts.Drain(16))
        {
            var ev=e;
            Safe("blast",()=>HandleBlast(ev));
        }
    }
    private int OrigTriCount(long root,int vuid,PlateStructure? plate)
    {
        if(dents.TryGetValue((root,vuid),out var ds)&&ds.OrigTris>0)return ds.OrigTris;
        try
        {
            var mm=plate?.Mesh?.MaxDetailMesh;if(mm!=null)return (int)(IndexCount(mm)/3);
            var fm=plate?.highDetailRenderer?.Filter?.sharedMesh;if(fm!=null)return (int)(IndexCount(fm)/3);
        }catch{}
        return 0;
    }
    private bool UseBlastHole(long root,int vuid,PlateCells? pcs,PlateStructure? plate)
    {
        var mode=Plugin.Instance.BlastHoleMode.Value;
        if(mode.Equals("Off",StringComparison.OrdinalIgnoreCase))return false;
        if(mode.Equals("Always",StringComparison.OrdinalIgnoreCase))return true;
        return pcs==null||pcs.Total<=8||OrigTriCount(root,vuid,plate)<180;
    }

    private sealed class BlastTarget{internal Tracked T=null!;internal VehicleComponent Comp=null!;internal int Vuid;internal PlateSide Side;internal float Dist;internal Vector3 Closest;internal SidePlan? Plan;}
    private void HandleBlast(BlastEvent e)
    {
        var p=Plugin.Instance;
        if(blastDedup.IsDuplicate(e.Frame,new V3(e.Pos.x,e.Pos.y,e.Pos.z),e.Calibre))return;
        var filter=BlastEffectFilter.Classify(e.Kind,e.VisualOnly,p.HeatBlastScale.Value,p.MaxNonDestructibleBlastDents.Value);
        if(filter.SkipBlast)
        {
            if(p.DetailedLogging.Value)BLine($"[BLAST] skipped visual-only '{e.Kind}' calibre={e.Calibre}mm");
            return;
        }
        if(BlastSnap.TrySnap(recentSimHits,e.Frame,new V3(e.Pos.x,e.Pos.y,e.Pos.z),e.Calibre,out var snap,out double shift))
        {
            var hitPos=new Vector3(snap.Pos.X,snap.Pos.Y,snap.Pos.Z);
            var hitDir=new Vector3(snap.Dir.X,snap.Dir.Y,snap.Dir.Z);
            hitDir=hitDir.sqrMagnitude>1e-8f?hitDir.normalized:Vector3.down;
            e.HasSnap=true;e.SnapRoot=snap.Root;e.SnapVuid=snap.Vuid;e.SnapHitPos=hitPos;e.SnapDir=hitDir;
            e.Pos=hitPos-hitDir*0.08f;
            BLine($"[BLAST] snapped offset {shift:0.00}m -> hit ({hitPos.x:0.00},{hitPos.y:0.00},{hitPos.z:0.00}) vuid={snap.Vuid} cal={snap.CalibreMm}mm dir=({hitDir.x:0.00},{hitDir.y:0.00},{hitDir.z:0.00})");
        }
        else if(BlastSnap.TryMatchContact(recentSimHits,e.Frame,new V3(e.Pos.x,e.Pos.y,e.Pos.z),e.Calibre,out var contact,out double cdist,out double calong))
        {
            var hitPos=new Vector3(contact.Pos.X,contact.Pos.Y,contact.Pos.Z);
            var hitDir=new Vector3(contact.Dir.X,contact.Dir.Y,contact.Dir.Z);
            hitDir=hitDir.sqrMagnitude>1e-8f?hitDir.normalized:Vector3.down;
            e.HasSnap=true;e.SnapRoot=contact.Root;e.SnapVuid=contact.Vuid;e.SnapHitPos=hitPos;e.SnapDir=hitDir;
            if(calong>-0.08)e.Pos=hitPos-hitDir*0.08f;
            BLine($"[BLAST] matched contact hit d={cdist:0.00}m -> hit ({hitPos.x:0.00},{hitPos.y:0.00},{hitPos.z:0.00}) vuid={contact.Vuid} cal={contact.CalibreMm}mm->{e.Calibre}mm dir=({hitDir.x:0.00},{hitDir.y:0.00},{hitDir.z:0.00})");
        }
        double cal=e.Calibre;
        double effBase=p.BlastBase.Value*filter.DamageScale;
        double reach=BlastMath.Reach(p.BlastRadiusPer100mm.Value,cal);
        double vehicleCutoffSq=(reach+45.0)*(reach+45.0);
        var targets=new List<BlastTarget>();
        var cellPlates=new List<CellPlate>();
        var holePlates=new List<(Tracked T,VehicleComponent Comp,int Vuid,PlateStructure Plate,SidePlan? Plan,PlateCells? PC,double Dist)>();
        bool dentAll=p.DentNonDestructiblePlates.Value&&p.DentEnabled.Value&&cal>=p.MinCalibreMm.Value&&filter.MaxNonDestructibleDents>0&&filter.DamageScale>0;
        var ndCandidates=dentAll?new List<(Tracked T,PlateEntry Pe,float Dist)>():null;
        double ndReach=string.Equals(e.Kind,"heat",StringComparison.OrdinalIgnoreCase)?Math.Min(reach,0.75):reach;

        foreach(var t in tracked.Values)
        {
            try{if((t.V.transform.position-e.Pos).sqrMagnitude>vehicleCutoffSq)continue;}catch{}
            long root=t.Root;
            foreach(var vuid in t.Destr)
            {
                var comp=Resolve(t,vuid);if(comp==null||led.IsGone(root,vuid))continue;
                var plan=PlanFor(t,comp,vuid);
                var plate=comp.TryCast<PlateStructure>();
                var pcs=plate==null?null:CellsFor(t,comp,vuid,plan);
                if(plate!=null&&UseBlastHole(root,vuid,pcs,plate))
                {
                    Vector3 distPos=e.HasSnap&&root==e.SnapRoot&&vuid==e.SnapVuid?e.SnapHitPos:e.Pos;
                    double hd;
                    if(plan is {PerSide:true,Geo:not null})
                    {
                        var hl=plate.WorldToStructureSpace(distPos);var hlp=new V3(hl.x,hl.y,hl.z);
                        hd=Math.Min(plan.Geo.Distance(hlp,PlateSide.Original),plan.Geo.Distance(hlp,PlateSide.Mirror));
                    }
                    else hd=BoundsDistance(comp,t.V,distPos,out _);
                    holePlates.Add((t,comp,vuid,plate,plan,pcs,hd));
                    continue;
                }
                if(pcs!=null&&plate!=null)
                {
                    var cl=plate.WorldToStructureSpace(e.Pos);var clp=new V3(cl.x,cl.y,cl.z);
                    double near=double.MaxValue;foreach(var kv in pcs.Info)if(!pcs.IsBroken(kv.Key))near=Math.Min(near,BoxMath.Distance(clp,kv.Value.Min,kv.Value.Max));
                    cellPlates.Add(new CellPlate{T=t,Comp=comp,Vuid=vuid,PC=pcs,Plan=plan,Plate=plate,Local=clp,Nearest=near});
                    continue;
                }
                if(plan is {PerSide:true,Geo:not null}&&plate!=null)
                {
                    var l=plate.WorldToStructureSpace(e.Pos);var lp=new V3(l.x,l.y,l.z);
                    foreach(var side in new[]{PlateSide.Original,PlateSide.Mirror})
                    {
                        var cl=plan.Geo.Closest(lp,side);
                        targets.Add(new BlastTarget{T=t,Comp=comp,Vuid=vuid,Side=side,Dist=plan.Geo.Distance(lp,side),Closest=plate.StructureToWorldSpace(new Vector3(cl.X,cl.Y,cl.Z)),Plan=plan});
                    }
                }
                else
                {
                    float d=BoundsDistance(comp,t.V,e.Pos,out var cp);
                    targets.Add(new BlastTarget{T=t,Comp=comp,Vuid=vuid,Side=PlateSide.Whole,Dist=d,Closest=cp,Plan=plan});
                }
            }
            if(ndCandidates!=null)
            {
                foreach(var pe in t.AllPlates)
                {
                    if(t.DestrSet.Contains(pe.Vuid)||pe.Plate==null||pe.Plate.WasCollected)continue;
                    Vector3 distPos=e.HasSnap&&root==e.SnapRoot&&pe.Vuid==e.SnapVuid?e.SnapHitPos:e.Pos;
                    float d=BoundsDistance(pe.Rends,distPos,out _);
                    if(d<=ndReach)ndCandidates.Add((t,pe,d));
                }
            }
        }
        if(ndCandidates!=null&&ndCandidates.Count>0)
        {
            ndCandidates.Sort((a,b)=>
            {
                bool da=IsDirectSnap(e,a.T.Root,a.Pe.Vuid);
                bool db=IsDirectSnap(e,b.T.Root,b.Pe.Vuid);
                if(da!=db)return da?-1:1;
                return a.Dist.CompareTo(b.Dist);
            });
            int limit=Math.Min(filter.MaxNonDestructibleDents,ndCandidates.Count);
            for(int i=0;i<limit;i++)
            {
                var c=ndCandidates[i];
                double th=QuickThicknessMm(c.T.Root,c.Pe.Plate,c.Pe.Vuid);
                QueueNonDestructibleBlastDent(c.T,c.Pe.Plate,c.Pe.Vuid,e,c.Dist,th,filter.DamageScale);
            }
        }
        BlastTarget? nearest=null;foreach(var x in targets)if(x.Dist<float.MaxValue&&(nearest==null||x.Dist<nearest.Dist))nearest=x;
        CellPlate? nearestCell=null;foreach(var x in cellPlates)if(nearestCell==null||x.Nearest<nearestCell.Nearest)nearestCell=x;
        double bestNear=nearest==null?double.MaxValue:nearest.Dist;
        string nearestText=nearest==null?"none":nearest.Vuid+"/"+SideName(nearest.Side);
        if(nearestCell!=null&&nearestCell.Nearest<bestNear){bestNear=nearestCell.Nearest;nearestText=nearestCell.Vuid+"/cells";}
        foreach(var x in holePlates)if(x.Dist<bestNear){bestNear=x.Dist;nearestText=x.Vuid+"/hole";}
        string nearestDist=bestNear<double.MaxValue?bestNear.ToString("0.00"):"n/a";
        string scale=e.MinScale<0?"unreadable":$"min={e.MinScale:0.###} max={e.MaxScale:0.###}";
        if(p.DetailedLogging.Value||nearestCell!=null||nearest!=null||holePlates.Count>0)
            BLine($"[BLAST] calibre={e.Calibre}mm kind={(e.Kind==""?"native":e.Kind)} pos=({e.Pos.x:0.00},{e.Pos.y:0.00},{e.Pos.z:0.00}) type={e.Type} normal=({e.Normal.x:0.00},{e.Normal.y:0.00},{e.Normal.z:0.00}) parent={e.Parent} nearestDestructible={nearestText} dist={nearestDist}m explosionAssets {scale}{(cal<p.MinCalibreMm.Value?" -> below MinCalibreMm "+p.MinCalibreMm.Value+": ignored":"")}");
        if(cal<p.MinCalibreMm.Value||filter.DamageScale<=0)return;
        foreach(var x in targets)
        {
            long root=x.T.Root;
            if(led.IsGone(root,x.Vuid)||led.IsBroken(root,x.Vuid,x.Side))continue;
            double dmg=BlastMath.Damage(effBase,cal,x.Dist,p.BlastRadiusPer100mm.Value,p.MinCalibreMm.Value);
            if(dmg<=0)continue;
            var st=StatsFor(root,x.Comp,x.Vuid);
            bool breaks=led.Add(root,x.Vuid,x.Side,dmg,p.BreakEnabled.Value?st.Strength:double.PositiveInfinity,out int hits,out double total);
            if(p.LogDamage.Value)Plugin.Info($"[DMG] vuid={x.Vuid} side={SideName(x.Side)} +{dmg:0.0} (blast cal={e.Calibre}mm d={x.Dist:0.00}m) {total:0.0}/{st.Strength:0.0}{(breaks?" -> TEAR OFF":"")}");
            if(!breaks){QueueBlastDent(x,e,dmg,st.Strength);continue;}
            var normal=e.Pos-x.Closest;normal=normal.sqrMagnitude>1e-8f?normal.normalized:Vector3.up;
            var h=new Hit{T=x.T,Comp=x.Comp,Vuid=x.Vuid,Pos=x.Closest,Normal=normal,Dir=-normal,CalibreMm=e.Calibre};
            QueueBreak(x.T,x.Comp,x.Vuid,x.Side,x.Plan,hits,h,e.Pos,st.Strength>0?dmg/st.Strength:1);
        }
        foreach(var x in cellPlates)
        {
            if(led.IsGone(x.T.Root,x.Vuid))continue;
            HandleCellBlast(x,e,filter.DamageScale);
        }
        foreach(var x in holePlates)
        {
            if(led.IsGone(x.T.Root,x.Vuid))continue;
            HandleHoleBlast(x.T,x.Comp,x.Vuid,x.Plate,x.Plan,x.PC,e,filter.DamageScale);
        }
    }
    private void ProcessDecals()
    {
        int guard=0;
        while(decalQueue.Count>0&&guard++<8)
        {
            var h=decalQueue.Dequeue();
            if(h.Comp==null||h.Comp.WasCollected||led.IsBroken(h.T.Root,h.Vuid))continue;
            Safe("decal",()=>PlayDecal(h));
        }
        decalQueue.Clear();
    }

    // ---- break (Delete key = break the last targeted plate now) -----------------------------------------------
    private void BreakNow()
    {
        if(!inSession||inPhotomode)return;
        var h=Target();if(h==null)return;
        if(!IsDestructible(h.Comp)){Plugin.Info($"[BREAK] test key: vuid={h.Vuid} not destructible (tick Destructable in the plate editor first).");return;}
        long root=h.T.Root;
        var plan=PlanFor(h.T,h.Comp,h.Vuid);
        var side=SideOfPoint(h.Comp,plan,h.Pos);
        if(BuilderPatches.Cells.TryGetValue((root,h.Vuid),out var bpc)&&h.Comp.TryCast<PlateStructure>() is {} bplate)
        {
            var rest=bpc.BreakAll(side);
            if(rest.Count==0){Plugin.Info($"[BREAK] test key: vuid={h.Vuid} side={SideName(side)} has no cell left.");return;}
            var cam0=Camera.main;Vector3 from0=cam0!=null?cam0.transform.position:h.Pos-h.Dir*3f;
            var lw=bplate.WorldToStructureSpace(from0);
            Plugin.Info($"[CELL] vuid={h.Vuid} side={SideName(side)} test key: {rest.Count} cell(s) torn off");
            QueueCellBreak(h.T,h.Comp,h.Vuid,side,plan,bpc,rest,from0,1,bplate,new V3(lw.x,lw.y,lw.z));
            return;
        }
        if(!led.ForceBreak(root,h.Vuid,side)){Plugin.Info($"[BREAK] test key: vuid={h.Vuid} side={SideName(side)} is already broken.");return;}
        var resolved=SideBreak.Resolve(plan is {PerSide:true},side,side!=PlateSide.Whole&&led.IsBroken(root,h.Vuid,SideBreak.Other(side)));
        if(resolved==PlateSide.Whole&&side!=PlateSide.Whole)led.ForceBreak(root,h.Vuid,PlateSide.Whole);
        var cam=Camera.main;Vector3 from=cam!=null?cam.transform.position:h.Pos-h.Dir*3f;
        QueueBreak(h.T,h.Comp,h.Vuid,resolved,plan,led.Hits(root,h.Vuid,side),h,from,1);
    }
    private void ProcessBreaks()
    {
        while(breakQueue.Count>0)
        {
            var j=breakQueue[0];breakQueue.RemoveAt(0);
            if(j.Comp==null||j.Comp.WasCollected||!tracked.ContainsKey(j.T.Pointer)){Plugin.Info($"[BREAK] vuid={j.Vuid} skipped: vehicle or plate no longer present.");continue;}
            Safe("break",()=>ExecuteBreak(j));
        }
    }
    private void ExecuteBreak(BreakJob j)
    {
        if(j.Cells!=null&&j.CellOnly){ExecuteCellBreak(j);return;}
        if(j.Side!=PlateSide.Whole){ExecuteSideBreak(j);return;}
        PrepareDebris(j);
        var clock=Stopwatch.StartNew();long root=j.T.Root;
        var before=Plugin.Instance.DetailedLogging.Value?Snapshot(j.T):null;
        var failed=BreakPipeline.Run(step=>
        {
            switch(step)
            {
                case BreakStep.HideDecals:HideDecals(j.T,j.Comp);break;
                case BreakStep.HidePlate:HidePlate(j,root);break;
                case BreakStep.HideAttached:HideAttached(j,root);break;
                case BreakStep.PlayExplosion:if(Plugin.Instance.EnableEffects.Value)PlayExplosion(j.H);break;
                case BreakStep.RemoveFromModel:RemoveFromModel(j,root);break;
                case BreakStep.Log:clock.Stop();Plugin.Info($"[BREAK] vuid={j.Vuid} after {j.Hits} hits, ms={clock.Elapsed.TotalMilliseconds:0.0}");break;
            }
        });
        foreach(var f in failed)Plugin.Warn($"[BREAK] step {f.Step} failed: {f.Error}");
        Safe("debris",()=>SpawnDebris(j));
        sweeps.Add((j.T,j.Comp,Time.frameCount+180,PlateSide.Whole));
        if(before!=null)visualCheck=new VisualCheck{T=j.T,Vuid=j.Vuid,Frame=Time.frameCount,Before=before};
    }
    private void ExecuteSideBreak(BreakJob j)
    {
        var clock=Stopwatch.StartNew();long root=j.T.Root;
        var plan=j.Plan!;
        PrepareDebris(j);
        State.SetSideBroken(root,j.Vuid,j.Side,true);
        var failed=BreakPipeline.Run(step=>
        {
            switch(step)
            {
                case BreakStep.HideSideDecals:HideDecals(j.T,j.Comp,j.Side,plan);break;
                case BreakStep.HideSideMesh:HideSideMesh(j,root,plan);break;
                case BreakStep.HideAttached:HideAttached(j,root);break;
                case BreakStep.PlayExplosion:if(Plugin.Instance.EnableEffects.Value)PlayExplosion(j.H);break;
                case BreakStep.RaiseSide:RaiseSide(j,root);break;
                case BreakStep.Log:clock.Stop();Plugin.Info($"[BREAK] vuid={j.Vuid} side={SideName(j.Side)} after {j.Hits} hits, ms={clock.Elapsed.TotalMilliseconds:0.0}");break;
            }
        },BreakPipeline.SideOrder);
        foreach(var f in failed)Plugin.Warn($"[BREAK] side step {f.Step} failed: {f.Error}");
        Safe("debris",()=>SpawnDebris(j));
        sweeps.Add((j.T,j.Comp,Time.frameCount+180,j.Side));
        torn.Add(new TornSide{Root=root,Vuid=j.Vuid,Side=j.Side});
    }
    private void HideSideMesh(BreakJob j,long root,SidePlan plan)
    {
        bool bo=State.IsSideBroken(root,j.Vuid,PlateSide.Original),bm=State.IsSideBroken(root,j.Vuid,PlateSide.Mirror);
        if(bo==bm)throw new InvalidOperationException("side mesh: both or neither side broken (whole-plate route handles that)");
        int changed=0;var sb=new StringBuilder();
        foreach(var pr in plan.Pairs)
        {
            if(!pr.Valid||pr.Filter==null)continue;
            Mesh? show;
            if(bm)show=pr.Source;
            else{pr.Half??=SideOps.BuildHalf(pr.Source!,plan.Mirror!);show=pr.Half;}
            if(show==null)throw new InvalidOperationException(pr.Name+": no mesh to show");
            show=RebaseDents(root,j.Vuid,pr.Filter,show,plan);
            pr.Filter.sharedMesh=show;changed++;
            keepMeshes.RemoveAll(x=>x.K.F==null||x.K.F.Pointer==pr.Filter.Pointer);
            keepMeshes.Add((root,new KeepMesh{F=pr.Filter,Want=show,Vuid=j.Vuid,Side=SideName(j.Side)}));
            sb.Append($" {pr.Name}->{(bm?"source":"mirrorHalf")}(v={show.vertexCount})");
        }
        SyncShadowProxy(root,j.Vuid);
        Plugin.Info($"[BREAK] plate vuid={j.Vuid} side={SideName(j.Side)} mesh: {changed} renderer mesh(es) rebound:{sb}");
    }
    private void RaiseSide(BreakJob j,long root)
    {
        var plate=j.Comp.TryCast<PlateStructure>();
        ScheduleArmourUpdate(j.T,root,j.Vuid,plate,j.Plan,null,j.Side,null,j.HiddenAttachedVuids,wholePlate:false);
    }

    private int HideDecals(Tracked t,VehicleComponent c,PlateSide side=PlateSide.Whole,SidePlan? plan=null)
    {
        var go=c.gameObject;if(go==null||go==t.V.gameObject)return 0;
        PlateStructure? plate=side==PlateSide.Whole?null:c.TryCast<PlateStructure>();
        if(side!=PlateSide.Whole&&(plate==null||plan?.Geo==null))return 0;
        int n=0;
        foreach(var tr in go.GetComponentsInChildren<Transform>(true))
        {
            if(tr==null||tr.gameObject==go||!tr.name.Contains("ProjectileImpactMark"))continue;
            if(!tr.gameObject.activeSelf)continue;
            if(plate!=null){var l=plate.WorldToStructureSpace(tr.position);if(plan!.Geo!.Choose(new V3(l.x,l.y,l.z))!=side)continue;}
            tr.gameObject.SetActive(false);hiddenDecals.Add(tr.gameObject);n++;
        }
        return n;
    }
    private void SweepDecals()
    {
        if(sweeps.Count==0||Time.frameCount%2!=0)return;
        for(int i=sweeps.Count-1;i>=0;i--)
        {
            var s=sweeps[i];
            if(Time.frameCount>s.UntilFrame||s.Comp==null||s.Comp.WasCollected){sweeps.RemoveAt(i);continue;}
            try{SidePlan? sp=null;if(s.Side!=PlateSide.Whole)Plans.TryGetValue((s.T.Root,s.Comp.VUID.Value),out sp);int n=HideDecals(s.T,s.Comp,s.Side,sp);if(n>0)Plugin.Info($"[BREAK] hid {n} late decal(s) on vuid={s.Comp.VUID.Value} side={SideName(s.Side)}");}catch{}
        }
    }
    private void HidePlate(BreakJob j,long root)
    {
        var key=(root,j.Vuid);
        if(!hideRecords.TryGetValue(key,out var rec)){rec=new HideRecord();hideRecords[key]=rec;}
        State.SetHidden(root,j.Vuid,true);
        foreach(var r in PlateUnityRenderers(j.Comp,j.T.V)){rec.Enabled.Add((r,r.enabled));r.enabled=false;}
        Plugin.Info($"[BREAK] plate vuid={j.Vuid} hidden: {rec.Enabled.Count} Unity renderer(s) disabled ({string.Join(",",rec.Enabled.Select(e=>e.R.gameObject.name))})");
    }
    private void RemoveFromModel(BreakJob j,long root)
    {
        if(!Plugin.Instance.EnableRouteA.Value){Plugin.Info($"[BREAK] vuid={j.Vuid}: removal from the damage model switched off (Switches/RouteA=false); the plate is only hidden.");return;}
        State.SetExcluded(root,j.Vuid,true);
        var plate=j.Comp.TryCast<PlateStructure>();
        ScheduleArmourUpdate(j.T,root,j.Vuid,plate,j.Plan,null,PlateSide.Whole,null,j.HiddenAttachedVuids,wholePlate:true);
    }
    private void PollMeasure()
    {
        var m=pending;if(m==null)return;
        var v=m.T.V;
        if(v==null||v.WasCollected){pending=null;return;}
        var flags=v.DirtyFlags;bool dirty=(flags&VehicleDirtyFlags.DamageModel)!=0;
        var p=Plugin.Instance;
        if(dirty&&!m.Built&&p.RebuildMode.Value.Equals("RaiseAndBuild",StringComparison.OrdinalIgnoreCase)&&m.Clock.ElapsedMilliseconds>=p.ForceBuildAfterMs.Value)
        {
            m.Built=true;var c=Stopwatch.StartNew();var r=v.Build();c.Stop();
            Plugin.Info($"[ROUTEA] rebuild did not start by itself within {p.ForceBuildAfterMs.Value} ms; called Vehicle.Build() -> returned {r} in {c.Elapsed.TotalMilliseconds:0.0} ms");
            return;
        }
        if(!dirty||m.Clock.ElapsedMilliseconds>10000)
        {
            m.Clock.Stop();
            var dm=v.DamageModel;
            int skipped=Patches.PlateSkipped+Patches.ModelSkipped-m.SkipBefore,built=Patches.PlateBuilt+Patches.ModelBuilt-m.BuiltBefore;
            string status=dirty?"NOT DONE (timeout, DamageModel flag still set)":"done";
            Plugin.Info($"[ROUTEA] rebuild {status}: {m.Clock.Elapsed.TotalMilliseconds:0.0} ms wall / {Time.frameCount-m.StartFrame} frames; BuildDamageModelColliders calls: built={built} skipped(by us)={skipped}; structureHash {m.HashBefore} -> {dm?.StructureHash}; DamageModel instance {(dm!=null&&dm.Pointer==m.ModelBefore?"same":"REPLACED")}; dirtyFlags now={flags}");
            if(dm!=null&&dm.Pointer!=m.T.ModelPtr)Subscribe(m.T,dm);
            BuildMap(m.T);
            if(!dirty)probeReady=true;else probe=null;
            pending=null;
        }
    }

    // ---- hiding helpers -------------------------------------------------------------------------------------
    private sealed class HideRecord{internal readonly List<(Renderer R,bool Was)> Enabled=new();}
    private readonly Dictionary<(long,int),HideRecord> hideRecords=new();
    private static List<VehicleRenderer> PlateVRs(VehicleComponent c)
    {
        var list=new List<VehicleRenderer>();
        var plate=c.TryCast<PlateStructure>();
        if(plate!=null){if(plate.highDetailRenderer!=null)list.Add(plate.highDetailRenderer);if(plate.lowDetailRenderer!=null)list.Add(plate.lowDetailRenderer);if(plate.shadowProxyRenderer!=null)list.Add(plate.shadowProxyRenderer);}
        foreach(var g in c.gameObject.GetComponentsInChildren<VehicleRendererGroup>(true))
        {
            if(g.lods!=null)foreach(var l in g.lods)if(l!=null&&!list.Any(x=>x.Pointer==l.Pointer))list.Add(l);
            if(g.shadow!=null&&!list.Any(x=>x.Pointer==g.shadow.Pointer))list.Add(g.shadow);
        }
        return list;
    }
    private static List<Renderer> PlateUnityRenderers(VehicleComponent c,Vehicle v)
    {
        var res=new List<Renderer>();
        foreach(var vr in PlateVRs(c)){var r=vr.Renderer;if(r!=null&&!res.Any(x=>x.Pointer==r.Pointer))res.Add(r);}
        if(c.gameObject!=v.gameObject)
            foreach(var r in c.gameObject.GetComponentsInChildren<Renderer>(true))if(r!=null&&!res.Any(x=>x.Pointer==r.Pointer))res.Add(r);
        return res;
    }
    private static long IndexCount(Mesh? m)
    {
        if(m==null)return 0;long n=0;try{for(int s=0;s<m.subMeshCount;s++)n+=m.GetIndexCount(s);}catch{n=-1;}return n;
    }
    private static bool ScreenVisible(Renderer r)
    {
        try
        {
            if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)return false;
            var f=r.GetComponent<MeshFilter>();
            if(f!=null)return IndexCount(f.sharedMesh)!=0;
            return true;
        }catch{return false;}
    }
    private static bool CompVisible(VehicleComponent c,Vehicle v)
    {
        foreach(var r in PlateUnityRenderers(c,v))if(ScreenVisible(r))return true;
        return false;
    }
    private Dictionary<int,bool> Snapshot(Tracked t)
    {
        var d=new Dictionary<int,bool>();
        foreach(var kv in t.Map){var c=kv.Value;if(c==null||c.WasCollected)continue;if(c.TryCast<PlateStructure>()!=null||c.TryCast<VehicleObjectModel>()!=null)d[kv.Key]=CompVisible(c,t.V);}
        return d;
    }
    private sealed class VisualCheck{internal Tracked T=null!;internal int Vuid,Frame;internal Dictionary<int,bool> Before=new();}
    private void PollVisual()
    {
        var c=visualCheck;if(c==null||Time.frameCount-c.Frame<3)return;
        visualCheck=null;
        var after=Snapshot(c.T);var changed=new List<string>();
        foreach(var kv in after)if(kv.Key!=c.Vuid&&c.Before.TryGetValue(kv.Key,out var was)&&was!=kv.Value)changed.Add($"{kv.Key}:{was}->{kv.Value}");
        bool visible=after.TryGetValue(c.Vuid,out var tv)&&tv;
        Plugin.Info($"[BREAK] after 3 frames: vuid={c.Vuid} screen-visible={visible} (wanted False); other plates/models changed: {changed.Count}{(changed.Count>0?" -> "+string.Join(", ",changed.Take(20)):"")}");
    }

    // ---- effects ----------------------------------------------------------------------------------------------
    private ProjectileEffectConfig? Config()
    {
        if(effectConfig!=null&&!effectConfig.WasCollected)return effectConfig;
        var all=Resources.FindObjectsOfTypeAll<ProjectileEffectConfig>();
        effectConfig=all.Length>0?all[0]:null;
        Plugin.Info("[EFFECT] ProjectileEffectConfig instances found: "+all.Length);
        return effectConfig;
    }
    private void PlayExplosion(Hit h)
    {
        var cfg=Config();if(cfg==null){Plugin.Warn("[EFFECT] no ProjectileEffectConfig: no explosion.");return;}
        var p=Plugin.Instance;
        int cal=Math.Clamp(p.EffectCalibreMm.Value,1,65535);
        var pos=h.Pos+h.Normal*p.EffectOffsetM.Value;
        var boost=Math.Max(1f,p.EffectScaleBoost.Value);
        try
        {
            EffectBoost.Factor=boost;EffectBoost.Own=true;
            cfg.PlayEffect(new ProjectileEffectInfo{Type=ProjectileEffectType.Explosion,Position=pos,HitNormal=h.Normal,HitVelocity=h.Dir*100f,Parent=h.T.V.transform,Calibre=(ushort)cal});
            Plugin.Info($"[EFFECT] explosion at ({pos.x:0.00},{pos.y:0.00},{pos.z:0.00}) calibre {cal} mm boost {boost:0.##}");
        }
        catch(Exception e){Plugin.Warn("[EFFECT] explosion failed: "+e.Message);}
        finally{EffectBoost.Factor=1f;EffectBoost.Own=false;}
    }
    private void PlayDecal(Hit h)
    {
        var cfg=Config();if(cfg==null)return;
        var p=Plugin.Instance;
        int cal=Math.Clamp(p.EffectCalibreMm.Value,1,65535);
        var pos=h.Pos+h.Normal*p.EffectOffsetM.Value;
        Transform parent=h.Comp.transform!=null?h.Comp.transform:h.T.V.transform;
        try{EffectBoost.Own=true;cfg.PlayEffect(new ProjectileEffectInfo{Type=ProjectileEffectType.Penetration,Position=pos,HitNormal=h.Normal,HitVelocity=h.Dir*300f,Parent=parent,Calibre=(ushort)cal});}finally{EffectBoost.Own=false;}
        Plugin.Info($"[EFFECT] decal on non-breaking hit, vuid={h.Vuid} calibre {cal} mm");
    }

    // ---- reset ----------------------------------------------------------------------------------------------
    private void ResetAll(string reason)
    {
        int hidden=State.HiddenCount,excluded=State.ExcludedCount,broken=led.BrokenCount,sides=State.SideCount;
        int debrisGone=ClearDebris("reset");
        RestoreInPlaceArmour(_=>true);coalescer.Clear();
        RestoreMerged(_=>true);BuilderPatches.Cells.Clear();cellFailed.Clear();
        RestoreAllSnappedMarks();
        int dentMeshes=DisposeDents(_=>true);dentQueue.Clear();lastDestructible=null;dentBudget.Reset();ground.Clear();
        foreach(var rec in hideRecords.Values)try{foreach(var (r,was) in rec.Enabled)try{if(r!=null)r.enabled=was;}catch{}rec.Enabled.Clear();}catch{}
        hideRecords.Clear();
        foreach(var pl in Plans.Values)SideOps.Restore(pl);
        Plans.Clear();
        foreach(var g in hiddenDecals)try{if(g!=null)g.SetActive(true);}catch{}
        hiddenDecals.Clear();sweeps.Clear();breakQueue.Clear();decalQueue.Clear();stats.Clear();led.Clear();attached.Clear();Blasts.Clear();keepMeshes.Clear();keepCounts.Clear();torn.Clear();holeDmg.Clear();holeSeq=0;recentSimHits.Clear();scanAt=0;checkLines=0;activeRoots.Clear();
        State.ClearAll();
        foreach(var t in tracked.Values)try{if(t.Handler!=null&&t.Model!=null)t.Model.remove_SimulationRan(t.Handler);}catch{}
        tracked.Clear();
        pending=null;visualCheck=null;probe=null;probeReady=false;last=null;
        budget.Reset();blastBudget.Reset();tally.Clear();unresolved=0;
        Plugin.Info($"[RESET] {reason}: cleared excluded={excluded} hidden={hidden} broken={broken} (sides={sides}), side meshes restored, {dentMeshes} private dent mesh(es) destroyed and the original meshes put back, {debrisGone} debris object(s) removed, damage counters, attached parts, blast queue, log budget, tracked vehicles, last hit.");
    }
}
