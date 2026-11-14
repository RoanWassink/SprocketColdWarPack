using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
using N=System.Numerics;
namespace SprocketEmitter;

public sealed class EmitterRuntime:MonoBehaviour
{
    public EmitterRuntime(IntPtr pointer):base(pointer){}
    internal const string ComponentId="sprocketEmitterModel",PartGuid="0a61b6bf-6c74-47f8-a783-4217380e50ee",DataKey="sprocket.emitter";
    internal const float ArrowLength=.12f;
    private sealed class Part {
        internal VehicleObjectModel Model;
        internal readonly Signal Signal=new();
        internal Il2CppSystem.EventHandler? Loaded;
        internal int Scene;
        internal bool Alive,OutletLogged;
        internal bool? Visible;          // last SetVisible written by us; null = unknown, rewrite
        internal Settings? Settings;     // cached between rising edges / onChanged
        internal Part(VehicleObjectModel model,int scene){Model=model;Scene=scene;}
    }
    private sealed class Visual {
        internal GameObject Root=null!;
        internal MeshFilter Filter=null!;
        internal MeshRenderer Renderer=null!;
        internal MeshAssets.Item? Item;
        internal Vector3 Scale;
    }
    private readonly Dictionary<IntPtr,Part> parts=new();
    private readonly List<IntPtr> retire=new();
    private readonly HashSet<(IntPtr,string)> meshLogged=new();
    private Simulation simulation=null!;
    private UnityWorld world=null!;
    private MeshAssets assets=new();
    private Mesh? arrow;
    private Material? brass,steel;
    private Visual[] visuals=Array.Empty<Visual>();
    private bool ready,disposed,playingLast,invalidWarned;
    private float scanAt,harvestAt;
    private int scene=int.MinValue,logs,errors;
    private readonly System.Random random=new(9173);
    internal static N.Vector3 Num(Vector3 v)=>new(v.x,v.y,v.z);
    internal static Vector3 Unity(N.Vector3 v)=>new(v.X,v.Y,v.Z);
    internal static N.Quaternion Num(Quaternion q)=>new(q.x,q.y,q.z,q.w);
    internal static Quaternion Unity(N.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    internal static bool HasScene(string name){for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).name==name)return true;return false;}
    private static bool Playing=>HasScene("VehicleControlUI")&&!HasScene("VehicleDesignerUI");
    private static bool CanThrow=>Playing&&Time.deltaTime>0&&!GameState.Paused();
    private void Trace(string s){if(logs++<120&&Plugin.Instance.Detailed.Value)Plugin.Instance.Trace(s);}
    [HideFromIl2Cpp] internal void RegisterIo() {
        IoApiClient.TryBind();
        bool registered=IoApiClient.RegisterManagedOutput(DataKey,"Emitter",ComponentId,Apply,"Eject","Stop");
        Plugin.Instance.Log.LogInfo("[EMITTER] I/O revision="+IoApiClient.ApiRevision+" registered="+registered);
        if(!registered)return;
        var icon=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"emitter-icon.png"));
        if(!IoApiClient.RegisterPartIcon(PartGuid,icon))Plugin.Instance.Log.LogWarning("[EMITTER] icon registration refused.");
        // IO-API §7: after RegisterManagedOutput, once per ComponentID. Revision < 2: cfg defaults, silently.
        if(IoApiClient.Revision2Available) {
            bool settings=IoApiClient.RegisterPartSettings(ComponentId,DataKey,Settings.Schema(false),SettingsChanged);
            Plugin.Instance.Log.LogInfo("[EMITTER] settings registered="+settings);
            if(!settings)Plugin.Instance.Log.LogWarning("[EMITTER] settings registration refused; configured defaults remain in use.");
        }
    }
    [HideFromIl2Cpp] private void SettingsChanged(GameObject go) {
        try {
            if(disposed||go==null)return;var model=go.GetComponent<VehicleObjectModel>();if(model==null)return;
            if(!parts.TryGetValue(model.Pointer,out var p))return;
            p.Settings=Read(p);Trace($"settings part={model.Pointer} item={p.Settings.Item} speed={p.Settings.Speed} count={p.Settings.Count} continuous={p.Settings.Continuous}");
        }catch(Exception e){Fail(e);}
    }
    public void Start() {
        try {
            if(!EmitterEligibility.Prepare())return;
            var shader=Shader.Find("HDRP/Unlit");if(shader==null||!shader.isSupported)throw new InvalidOperationException("HDRP/Unlit unavailable");
            brass=Material(shader,new Color(204/255f,167/255f,83/255f,1));steel=Material(shader,new Color(.48f,.52f,.55f,1));
            arrow=MeshAssets.Make(ArrowMeshData.Arrow);
            simulation=new(Math.Clamp(Plugin.Instance.MaxObjects.Value,1,512));world=new UnityWorld(transform);
            simulation.Landed=(piece,slope)=>Trace($"land part={piece.Owner} slope={slope:F1}°");
            visuals=new Visual[simulation.Pieces.Length];
            for(int i=0;i<visuals.Length;i++) {
                var go=new GameObject("Emitter pooled object"){hideFlags=HideFlags.HideAndDontSave,layer=2};go.SetActive(false);
                var filter=go.AddComponent<MeshFilter>();var renderer=go.AddComponent<MeshRenderer>();Configure(renderer);
                visuals[i]=new Visual{Root=go,Filter=filter,Renderer=renderer};
            }
            scene=SceneManager.GetActiveScene().handle;playingLast=Playing;ready=true;Scan();
        }catch(Exception e){Fail(e);Dispose();}
    }
    internal static Material Material(Shader shader,Color color) {
        var m=new Material(shader){name="Emitter own material",hideFlags=HideFlags.HideAndDontSave};
        m.SetColor("_UnlitColor",color);m.SetColor("_EmissiveColor",Color.black);return m;
    }
    internal static void Configure(MeshRenderer r){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;r.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;}
    // Revision 2: Mast stores the normalized schema object; apply the same fixed defaults and limits.
    // Below revision 2 there is no settings UI and the cfg [Defaults] apply.
    [HideFromIl2Cpp] private Settings Read(Part p)=>IoApiClient.Revision2Available?Settings.Parse(IoApiClient.GetPartData(p.Model.gameObject,DataKey),Settings.Fixed,()=>{if(!invalidWarned){invalidWarned=true;Plugin.Instance.Log.LogWarning("[EMITTER] invalid setting; default used (once per session).");}}):Plugin.Instance.Defaults;
    [HideFromIl2Cpp] private Part? Ensure(VehicleObjectModel model) {
        if(disposed||EmitterEligibility.Check(model)<0)return null;
        if(!parts.TryGetValue(model.Pointer,out var p)) {
            if(parts.Count>=4096)return null;p=new(model,SceneManager.GetActiveScene().handle);parts.Add(model.Pointer,p);
            // Ordinary completion event, never a detour. Keep the delegate alive.
            p.Loaded=DelegateSupport.ConvertDelegate<Il2CppSystem.EventHandler>((Action<Il2CppSystem.Object,Il2CppSystem.EventArgs>)((_,_)=>{try{p.Visible=null;Adopt(p);}catch(Exception e){Fail(e);}}));
            model.add_LoadFinished(p.Loaded);
            if(ready)Adopt(p);
        }
        int current=SceneManager.GetActiveScene().handle;
        if(p.Scene!=current){p.Scene=current;p.Signal.Reset();p.Visible=null;}
        p.Alive=true;return p;
    }
    // Called from the 0.5 s scan, LoadFinished, first discovery and Play/designer transitions; never per frame.
    [HideFromIl2Cpp] private void Adopt(Part p) {
        var m=p.Model;if(EmitterEligibility.Check(m)<0||m.Model==null||arrow==null)return;
        var native=m.Model;
        if(native.InteractionMesh!=arrow){m.MarkDynamic();native.SetMesh(arrow);native.SetShadowProxy(arrow);native.SetInteractionMesh(arrow);p.Visible=null;
            Trace($"adapter part={m.Pointer} selection={m.interactionColliderType} damage={m.generateDamageColliders}");
            if((int)m.interactionColliderType!=2||m.generateDamageColliders)Plugin.Instance.Log.LogWarning("[EMITTER] adapter geometry deviates from selection-only; no collider state changed.");}
        bool want=!Playing;
        if(p.Visible!=want){m.SetVisible(want);p.Visible=want;Trace("visible part="+m.Pointer+" value="+want);}
    }
    [HideFromIl2Cpp] private void Apply(GameObject go,bool on) {
        try {
            if(disposed||go==null)return;var model=go.GetComponent<VehicleObjectModel>();if(model==null)return;
            var p=Ensure(model);if(p==null)return;
            bool playing=Playing,baseline=playing&&!p.Signal.Initialized;
            bool fire=p.Signal.Apply(on,playing,ready&&CanThrow);
            if(baseline)Trace("baseline part="+model.Pointer+" on="+on);
            if(fire)Throw(p);
        }catch(Exception e){Fail(e);}
    }
    [HideFromIl2Cpp] private void Throw(Part p) {
        if(!ready||!CanThrow||EmitterEligibility.Check(p.Model)<0)return;
        var s=p.Settings=Read(p);var vt=p.Model.VehicleTransform;var vehicle=p.Model.Vehicle?.TryCast<Vehicle>();
        if(vt==null||vehicle==null||vehicle.Released)return;
        var zero=vt.LocalToWorldSpace(Vector3.zero);var axis=vt.LocalToWorldSpace(Vector3.forward)-zero;float unit=axis.magnitude;
        if(!float.IsFinite(unit)||unit<1e-4f)return;var forward=axis/unit;
        // If the node mapping carries no part scale (unit length 1), take it from the model.
        float length=unit;
        if(MathF.Abs(unit-1)<1e-3f){try{float z=MathF.Abs(p.Model.ModelScale.z);if(float.IsFinite(z)&&z>.01f)length=Math.Clamp(z,.1f,10);}catch{}}
        if(!p.OutletLogged){p.OutletLogged=true;Trace($"outlet part={p.Model.Pointer} nodeScale={unit:F3} used={length:F3} arrow={ArrowLength*length:F3}m");}
        var tip=zero+forward*(ArrowLength*length);
        float diameter=s.Caliber*.001f;var origin=tip+forward*(diameter*.5f+.002f);
        var item=assets.Get(vehicle,s.Item);if(item==null)return;float factor=Settings.Finite(s.Item=="casing"?Plugin.Instance.CasingLength.Value:Plugin.Instance.ProjectileLength.Value,s.Item=="casing"?6.08f:3,.1f,20);
        var scale=MeshAssets.Scale(item,diameter,diameter*factor);var direction=Quaternion.LookRotation(forward);
        var inherited=Vector3.zero;
        if(s.Inherit) {var body=vehicle.Rigidbody;if(body!=null){try{inherited=body.GetPointVelocity(origin);}catch{try{inherited=body.linearVelocity;}catch{}}}}
        Trace($"throw part={p.Model.Pointer} item={s.Item} count={s.Count} speed={s.Speed}");
        if(meshLogged.Add((vehicle.Pointer,s.Item)))Trace($"mesh item={s.Item} source={item.Source} bounds={item.Size} scale={scale}");
        float life=Math.Min(s.Lifetime,Settings.Finite(Plugin.Instance.MaxLifetime.Value,60,2,60));
        for(int i=0;i<s.Count;i++) {
            var cone=Simulation.Cone(s.Spread,(float)random.NextDouble(),(float)random.NextDouble());
            var spinAxis=Simulation.Cone(180,(float)random.NextDouble(),(float)random.NextDouble());
            int slot=simulation.Spawn(p.Model.Pointer.ToInt64(),Num(origin),Num(direction*Unity(cone)*s.Speed+inherited),Num(direction),spinAxis,s.Spin,diameter*.5f,life);
            var v=visuals[slot];v.Item=item;v.Scale=scale;v.Filter.sharedMesh=item.Mesh;v.Renderer.sharedMaterial=s.Item=="casing"?brass:steel;v.Root.transform.localScale=scale;v.Root.SetActive(true);
        }
    }
    private void Scan() {
        int found=0;
        foreach(var p in parts.Values)p.Alive=false;
        foreach(var vehicle in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) {
            if(vehicle==null||vehicle.Released)continue;var items=vehicle.ObjectReader?.Items;if(items==null)continue;
            int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<n;i++){var o=items[i];if(o==null||o.Released)continue;var c=o.Components;if(c==null)continue;int count=c.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;for(int j=0;j<count;j++)if(c[j]?.ComponentID==ComponentId&&c[j].TryCast<VehicleObjectModel>() is {} m){found++;if(Ensure(m) is {} p)Adopt(p);}}
        }
        if(found!=lastFound){Trace("scan found="+found);lastFound=found;}
        retire.Clear();foreach(var e in parts)if(e.Value.Model==null||e.Value.Model.VehicleObject==null||e.Value.Model.VehicleObject.Released||e.Value.Model.Vehicle==null||e.Value.Model.Vehicle.TryCast<Vehicle>() is not {} owner||owner.Released)retire.Add(e.Key);
        foreach(var key in retire){simulation.RemoveOwner(key.ToInt64());Unsubscribe(parts[key]);parts.Remove(key);Trace("cleanup reason=owner-released part="+key);}
        // Parts still registered but not eligible this scan (ghost, foreign, uninstalled) stop emitting.
        foreach(var p in parts.Values)if(!p.Alive){simulation.RemoveOwner(p.Model.Pointer.ToInt64());p.Signal.Reset();}
        world.Prune(simulation.Pieces);
    }
    private int lastFound=-1;
    public void Update() {
        if(!ready||disposed)return;
        try {
            int current=SceneManager.GetActiveScene().handle;bool playing=Playing;
            if(scene!=current){Clear("scene-change");GameState.Reset();scene=current;scanAt=0;logs=0;lastFound=-1;RetireScene(current);}
            if(playingLast!=playing) {
                // Leaving Play clears signals; entering Play must not, because Mast may already have
                // delivered this frame's baseline before this Update runs.
                if(!playing){Clear("designer");foreach(var p in parts.Values)p.Signal.Reset();}
                foreach(var p in parts.Values)p.Visible=null;
                scanAt=0; // re-adopt immediately: arrow visible in the designer, hidden in Play
            }
            playingLast=playing;
            if(Time.unscaledTime>=scanAt){scanAt=Time.unscaledTime+.5f;Scan();}
            // 0.1.1: keep one owned clone of the stock shell meshes while an Emitter exists; designer and Play alike.
            if(parts.Count>0&&MeshAssets.Missing&&Time.unscaledTime>=harvestAt) {
                harvestAt=Time.unscaledTime+3f;
                foreach(var vehicle in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))MeshAssets.Harvest(vehicle,playing?"play":"designer");
            }
            bool active=playing&&Time.deltaTime>0&&!GameState.Paused();
            if(active)foreach(var p in parts.Values)if(p.Alive&&p.Signal.Tick(Time.deltaTime,p.Settings??Settings.Fixed))Throw(p);
            if(active) {
                simulation.MaxSlope=Settings.Finite(Plugin.Instance.Slope.Value,35,0,80);simulation.Friction=Settings.Finite(Plugin.Instance.Friction.Value,.65f,0,10);
                simulation.Tick(Time.deltaTime,Num(Physics.gravity),world,Math.Clamp(Plugin.Instance.MaxCasts.Value,1,4096));
            }
            for(int i=0;i<visuals.Length;i++) {
                var v=visuals[i];var p=simulation.Pieces[i];if(v.Root.activeSelf!=p.Active)v.Root.SetActive(p.Active);if(!p.Active||v.Item==null)continue;
                var rotation=Unity(p.Rotation)*MeshAssets.AxisRotation(v.Item);
                v.Root.transform.rotation=rotation;v.Root.transform.position=Unity(p.Position)-rotation*Vector3.Scale(v.Item.Center,v.Scale);
            }
        }catch(Exception e){Fail(e);Clear("runtime-error");}
    }
    private void RetireScene(int current){retire.Clear();foreach(var e in parts)if(e.Value.Scene!=current)retire.Add(e.Key);foreach(var key in retire){Unsubscribe(parts[key]);parts.Remove(key);}}
    private void Unsubscribe(Part p){try{if(p.Model!=null&&p.Loaded!=null)p.Model.remove_LoadFinished(p.Loaded);}catch{}p.Loaded=null;}
    private void Clear(string reason){if(simulation!=null)simulation.Clear();foreach(var v in visuals)if(v.Root!=null){v.Root.SetActive(false);v.Item=null;}world?.Clear();assets.Dispose();meshLogged.Clear();Trace("cleanup reason="+reason+" remaining="+(simulation?.ActiveCount??0));}
    private void Fail(Exception e){if(errors++<5)Plugin.Instance.Log.LogWarning("[EMITTER] optional path: "+e.GetType().Name+": "+e.Message);}
    [HideFromIl2Cpp] public void Dispose() {
        if(disposed)return;disposed=true;Clear("destroy");foreach(var p in parts.Values)Unsubscribe(p);parts.Clear();
        foreach(var v in visuals)if(v.Root!=null)Object.Destroy(v.Root);visuals=Array.Empty<Visual>();
        MeshAssets.DisposeStock();
        if(arrow!=null)Object.Destroy(arrow);if(brass!=null)Object.Destroy(brass);if(steel!=null)Object.Destroy(steel);ready=false;
    }
    public void OnDestroy()=>Dispose();
}

internal sealed class UnityWorld:IWorld
{
    private const int UiLayer=5;
    private readonly Il2CppStructArray<RaycastHit> hits=new(128);
    private readonly Dictionary<long,Collider> surfaces=new();
    private readonly Dictionary<long,bool> excluded=new();   // camera/UI parents, decided once per collider
    private readonly List<long> stale=new();
    private readonly Transform host;
    internal UnityWorld(Transform host){this.host=host;}
    private bool Excluded(Collider c) {
        long id=c.Pointer.ToInt64();if(excluded.TryGetValue(id,out bool no))return no;
        no=c.GetComponentInParent<Camera>()!=null||c.GetComponentInParent<Canvas>()!=null;
        if(excluded.Count<4096)excluded[id]=no;return no;
    }
    public bool Cast(N.Vector3 from,N.Vector3 to,float radius,out Contact contact) {
        contact=default;var origin=EmitterRuntime.Unity(from);var delta=EmitterRuntime.Unity(to-from);float length=delta.magnitude;if(length<1e-7)return false;
        int n=Physics.RaycastNonAlloc(origin,delta/length,hits,length,-5,QueryTriggerInteraction.Ignore);
        if(n==hits.Length)throw new InvalidOperationException("cast buffer saturated; simulation cleared to avoid an ambiguous nearest hit");
        float best=float.PositiveInfinity;Collider? target=null;RaycastHit chosen=default;
        for(int i=0;i<n;i++) {
            var h=hits[i];var c=h.collider;if(c==null||!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy)continue;
            int layer=c.gameObject.layer;if(layer==2||layer==UiLayer)continue;
            var t=c.transform;if(t==null||t==host||t.IsChildOf(host))continue;
            if(h.distance>=best||Excluded(c))continue;
            best=h.distance;chosen=h;target=c;
        }
        if(target==null)return false;long id=target.Pointer.ToInt64();surfaces[id]=target;
        contact=new Contact(id,EmitterRuntime.Num(chosen.point),EmitterRuntime.Num(chosen.normal));return true;
    }
    public bool Surface(long id,out SurfacePose pose) {
        pose=default;if(!surfaces.TryGetValue(id,out var c)||c==null||!c.enabled||!c.gameObject.activeInHierarchy||c.transform==null)return false;
        var t=c.transform;pose=new(EmitterRuntime.Num(t.position),EmitterRuntime.Num(t.rotation));return true;
    }
    internal void Prune(Piece[] pieces){stale.Clear();foreach(var pair in surfaces){bool used=false;foreach(var p in pieces)if(p.Active&&p.Surface==pair.Key){used=true;break;}if(!used)stale.Add(pair.Key);}foreach(var key in stale)surfaces.Remove(key);}
    internal void Clear(){surfaces.Clear();excluded.Clear();}
    public N.Vector3 ToLocal(long id,N.Vector3 point)=>EmitterRuntime.Num(surfaces[id].transform.InverseTransformPoint(EmitterRuntime.Unity(point)));
    public N.Vector3 ToWorld(long id,N.Vector3 point)=>EmitterRuntime.Num(surfaces[id].transform.TransformPoint(EmitterRuntime.Unity(point)));
}
