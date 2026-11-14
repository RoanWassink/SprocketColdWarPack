using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
using Il2CppInterop.Runtime.Attributes;
using Object=UnityEngine.Object;
namespace SprocketVisualEffects;

// 0.4.5 (route B, user decision 2026-11-12): the lamps are plain static parts with the stock headlight /
// tail-light model, so mirroring, moving, paint and the atlas stay fully native. VE never mutates the
// native part: no MarkDynamic, mesh, shadow/interaction mesh or material setter. Everything VE shows (lens
// glow disc, halo, real light) is an own GameObject parented to this LampRuntime object, never to the
// vehicle, and placed every frame on the lens of the part's native model transform (LampLens).
public sealed class LampRuntime:MonoBehaviour
{
    public LampRuntime(IntPtr pointer):base(pointer){}
    internal const string ComponentId="vfxLamp",DataKey="visualeffects.lamp";
    internal sealed class Lamp
    {
        internal readonly VehicleObjectModel Owner;
        internal readonly int Form;
        internal LampSettings Settings;
        internal string? LastJson;
        internal bool On,LoggedOn;
        internal float Fade,Distance,Lod,Scale=1;
        internal LampLens.Pose Pose;
        internal int Seen;
        internal readonly IntPtr PartPointer;
        internal Lamp(VehicleObjectModel owner,int form) {
            Owner=owner;Form=form;PartPointer=owner.gameObject.Pointer;Settings=Plugin.Instance.LampDefaults(form);
        }
        internal bool Alive=>LampEligibility.Check(Owner)>=0;
    }
    // Own renderer object: a child of this runtime object only (never of a vehicle transform), so the
    // native RendererMerger, which only walks the vehicle's VehicleRendererRegister, can never see it.
    private sealed class Glow
    {
        internal readonly GameObject Root;
        internal readonly Material Material;
        internal Glow(string name,Transform parent,Shader shader,Mesh mesh,Texture2D? texture,bool transparent) {
            Root=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};Root.transform.SetParent(parent,false);
            Material=new Material(shader){name=name,hideFlags=HideFlags.HideAndDontSave};
            if(texture!=null)foreach(var n in new[]{"_UnlitColorMap","_BaseMap","_MainTex"})if(Material.HasProperty(n))Material.SetTexture(n,texture);
            // Display-referred emission (exposure weight 0, see LampLook) and both faces drawn.
            foreach(var (n,v) in new (string,float)[]{("_EmissiveExposureWeight",0),("_CullMode",0),("_CullModeForward",0)})
                if(Material.HasProperty(n))Material.SetFloat(n,v);
            if(transparent) {
                // Same proven HDRP/Unlit transparent setup as the 0.1.0 shot billboards (EffectDriver.Panel).
                foreach(var (n,v) in new (string,float)[]{("_SurfaceType",1),("_BlendMode",0),("_ZWrite",0),("_TransparentZWrite",0),("_SrcBlend",5),("_DstBlend",10),("_AlphaSrcBlend",1),("_AlphaDstBlend",10),("_ZTest",4),("_ZTestTransparent",4)})
                    if(Material.HasProperty(n))Material.SetFloat(n,v);
                Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");Material.EnableKeyword("_UNLIT_COLOR_MAP");Material.renderQueue=3000;
            }
            Root.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=Root.AddComponent<MeshRenderer>();renderer.sharedMaterial=Material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            Root.SetActive(false);
        }
        internal void Destroy(){Object.Destroy(Root);Object.Destroy(Material);}
    }
    private readonly Dictionary<IntPtr,Lamp> lamps=new();
    private readonly List<Lamp> ordered=new(),glowing=new();
    private readonly List<Light> lights=new();
    private readonly List<Glow> halos=new(),lenses=new();
    private Mesh? quad,disc;
    private Texture2D? haloTexture;
    private Shader? shader;
    private float scanAt;
    private int generation,scene=-1,errors,acceptedLogs,scanLogs,onLogs,lastCandidates=-1;
    private bool disposed,ready,managed,outputRegistered;
    internal const int MaxHalos=64,MaxLenses=128,MaxOnLogs=40;
    private string Assets=>Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"lamp-assets");
    internal static bool HasScene(string name) {
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).name==name)return true;
        return false;
    }
    internal bool Designer=>HasScene("VehicleDesignerUI")&&!HasScene("VehicleControlUI");
    public void Start() {
        try {
            // Shader.Find was proven in 0.1.0; guard its native pointer as well.
            var f=typeof(Shader).GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Where(f=>f.Name.StartsWith("NativeMethodInfoPtr_Find_")).ToArray();
            if(f.Length!=1)throw new InvalidOperationException("Shader.Find pointer ambiguous");
            var guard=NativeGuard.Check(typeof(Shader),f[0].Name,"Find",1);
            Plugin.Instance.Log.LogInfo("SelfTest lamp Shader.Find "+(guard.Ok?"PASS ":"FAIL ")+guard.Detail);
            if(!guard.Ok)return;
            // Read-only native model transform getters used for the lens pose (Smoke Launchers chain).
            bool transformOk=true;
            foreach(var (name,count) in new[]{("get_ModelPosition",0),("get_ModelRotation",0),("GetModelScale",0)}) {
                var fields=typeof(VehicleObjectModel).GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).Where(x=>x.Name.StartsWith("NativeMethodInfoPtr_"+name+"_")).ToArray();
                var result=fields.Length==1?NativeGuard.Check(typeof(VehicleObjectModel),fields[0].Name,name,count):new NativeGuard.Result(false,"missing/ambiguous field");
                Plugin.Instance.Log.LogInfo("SelfTest lamp lens VehicleObjectModel."+name+" "+(result.Ok?"PASS ":"FAIL ")+result.Detail);
                transformOk&=result.Ok;
            }
            if(!transformOk)return;
            shader=Shader.Find("HDRP/Unlit");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("HDRP/Unlit unavailable");
            if(!LampEligibility.Prepare())return;
            var (vertices,triangles)=LampLens.Disc();
            disc=new Mesh{name="VFX lamp lens disc",hideFlags=HideFlags.HideAndDontSave};
            disc.vertices=vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();disc.triangles=triangles;
            disc.RecalculateNormals();disc.RecalculateBounds();
            try{PrepareHalo();}catch(Exception ex){Plugin.Instance.Log.LogWarning("Lamp halo disabled: "+ex.Message);quad=null;}
            ready=true;
            Plugin.Instance.Log.LogInfo("Lamp READY 0.4.5: static stock-model lamps (native part untouched), own lens disc + halo + light on the stock lens, display-referred lens (max channel "+Plugin.Instance.LensBrightness.Value.ToString("0.##")+"), halo="+(quad!=null)+", inspector section via part right-click, Mast revision="+IoApiClient.ApiRevision+" managed="+managed);
        } catch(Exception ex){Fail(ex);Clear();}
    }
    private void PrepareHalo() {
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(Path.Combine(Assets,"lamp-halo.png")),true)||tex.width!=128||tex.height!=128){Object.Destroy(tex);throw new InvalidDataException("lamp-halo.png decode/size");}
        haloTexture=tex;
        var m=new Mesh{name="VFX lamp halo quad",hideFlags=HideFlags.HideAndDontSave};
        m.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
        m.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
        m.triangles=new[]{0,2,1,0,3,2};m.RecalculateBounds();quad=m;
    }
    // Facade registration belongs in Plugin.Load, after the soft dependency.
    [HideFromIl2Cpp] internal void RegisterIo() {
        IoApiClient.TryBind();
        managed=IoApiClient.RegisterManagedOutput("visualeffects.lamp","Lamp",ComponentId,Apply,"Lamp on","Lamp off");
        outputRegistered=managed;
        if(managed) {
            for(int i=0;i<3;i++)if(!IoApiClient.RegisterPartIcon(LampSettings.Guids[i],Path.Combine(Assets,"lamp-"+LampSettings.Forms[i]+"-icon.png")))Plugin.Instance.Log.LogWarning("Lamp icon registration refused: "+LampSettings.Forms[i]);
        } else {
            outputRegistered=IoApiClient.RegisterOutput("visualeffects.lamp","Lamp",ComponentId,Apply,LoadRules,SaveRules,"Lamp on","Lamp off");
            Plugin.Instance.Log.LogInfo("Lamp: Mast IO-API revision 1 unavailable; shape defaults, no per-part storage or registered icons. Revision 0 flow is session-only.");
        }
    }
    private readonly Dictionary<IntPtr,string> sessionRules=new();
    private readonly Dictionary<IntPtr,bool> states=new();
    [HideFromIl2Cpp] private string? LoadRules(GameObject part)=>sessionRules.TryGetValue(part.Pointer,out var s)?s:null;
    [HideFromIl2Cpp] private void SaveRules(GameObject part,string json){if(sessionRules.Count<4096||sessionRules.ContainsKey(part.Pointer))sessionRules[part.Pointer]=json;}
    [HideFromIl2Cpp] private void Apply(GameObject part,bool on) {
        if(disposed||part==null)return;
        var model=part.GetComponent<VehicleObjectModel>();
        if(LampEligibility.Check(model)<0)return;
        states[part.Pointer]=on;
        if(!ready)return;
        if(Ensure(model!) is {} lamp)lamp.On=on;
    }
    // Adoption only reads the part: eligibility, cache entry, Mast settings. No native writes.
    [HideFromIl2Cpp] private Lamp? Ensure(VehicleObjectModel model) {
        int form=LampEligibility.Check(model);
        if(form<0)return null;
        if(!model.HasModel){LampEligibility.Reject(model,"native model not loaded yet");return null;}
        if(!lamps.TryGetValue(model.Pointer,out var lamp)) {
            if(lamps.Count>=4096)return null;
            lamp=new Lamp(model,form);lamps.Add(model.Pointer,lamp);
            if(acceptedLogs++<20)Plugin.Instance.Log.LogInfo("Lamp ACCEPT part="+model.VehicleObject.Pointer+" form="+LampSettings.Forms[form]+" componentInstalled="+model.IsInstalled+" (static stock model, not modified)");
            if(states.TryGetValue(lamp.PartPointer,out var on))lamp.On=on;
        }
        lamp.Seen=generation;
        if(managed) {
            var json=IoApiClient.GetPartData(model.gameObject,DataKey);
            if(json!=lamp.LastJson) {lamp.Settings=LampSettings.Parse(json,Plugin.Instance.LampDefaults(form));lamp.LastJson=json;}
        }
        return lamp;
    }
    private void Scan() {
        generation++;int candidates=0;
        foreach(var vehicle in Object.FindObjectsOfType<Vehicle>()) {
            if(vehicle==null||vehicle.Released)continue;
            var items=vehicle.ObjectReader?.Items;if(items==null)continue;
            int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<n;i++) {
                var obj=items[i];if(obj==null||obj.Released)continue;
                var components=obj.Components;if(components==null)continue;
                int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<count;j++)if(components[j]?.ComponentID==ComponentId && components[j].TryCast<VehicleObjectModel>() is {} model){candidates++;Ensure(model);}
            }
        }
        if(candidates!=lastCandidates && scanLogs++<5) {
            lastCandidates=candidates;
            Plugin.Instance.Log.LogInfo("Lamp SCAN candidates="+candidates+" cached="+lamps.Count);
        }
        // Retire released owners. VE holds no native resources for a lamp, only this cache entry.
        foreach(var pair in lamps.ToArray())if(pair.Value.Owner==null || pair.Value.Owner.VehicleObject==null || pair.Value.Owner.VehicleObject.Released) {
            lamps.Remove(pair.Key);sessionRules.Remove(pair.Value.PartPointer);states.Remove(pair.Value.PartPointer);
        }
    }
    // Lens pose from the native model transform (read-only getters, signed scale incl. mirror/flip).
    private static bool Locate(Lamp lamp) {
        var model=lamp.Owner;
        var p=model.ModelPosition;var q=model.ModelRotation;var s=model.GetModelScale();
        if(!LampLens.TryWorld(lamp.Form,new System.Numerics.Vector3(p.x,p.y,p.z),new System.Numerics.Quaternion(q.x,q.y,q.z,q.w),new System.Numerics.Vector3(s.x,s.y,s.z),out var pose))return false;
        lamp.Pose=pose;lamp.Scale=pose.InPlaneScale;return true;
    }
    private static Vector3 U(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
    private static Quaternion U(System.Numerics.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    public void Update() {
        if(disposed||!ready)return;
        try {
            int current=SceneManager.GetActiveScene().handle;
            // Mast may send spawn state earlier this frame. Retire stale owners
            // in the scan instead of erasing a just-delivered callback state.
            if(scene!=current) {HideAll();scene=current;scanAt=0;acceptedLogs=0;scanLogs=0;onLogs=0;lastCandidates=-1;}
            bool designer=Designer,playing=HasScene("VehicleControlUI");
            if(!designer&&!playing) {if(lamps.Count>0)Clear();else HideAll();return;}
            if(Time.unscaledTime>=scanAt){scanAt=Time.unscaledTime+.5f;Scan();}
            var p=Plugin.Instance;
            bool enabled=p.Enabled.Value;
            float maxChannel=p.LensBrightness.Value,haloStrength=p.HaloStrength.Value;
            var camera=Camera.main;
            ordered.Clear();glowing.Clear();
            foreach(var lamp in lamps.Values) {
                if(!lamp.Alive||!Locate(lamp))continue;
                lamp.Distance=camera==null?float.PositiveInfinity:Vector3.Distance(camera.transform.position,U(lamp.Pose.Centre));
                lamp.Lod=LampPolicy.Lod(lamp.Distance,p.LampDistance.Value);
                bool on=enabled && (designer || (outputRegistered?lamp.On:p.LampDefaultOn.Value));
                lamp.Fade=LampPolicy.Fade(lamp.Fade,on,Time.unscaledDeltaTime);
                if(lamp.Fade<=0)lamp.LoggedOn=false;
                if(!lamp.Settings.Infrared&&lamp.Fade>0)glowing.Add(lamp);
                if(!designer&&!lamp.Settings.Infrared&&lamp.Fade>0&&lamp.Lod>0)ordered.Add(lamp);
            }
            ordered.Sort((a,b)=>{int x=a.Distance.CompareTo(b.Distance);return x!=0?x:a.Owner.Pointer.ToInt64().CompareTo(b.Owner.Pointer.ToInt64());});
            glowing.Sort((a,b)=>{int x=a.Distance.CompareTo(b.Distance);return x!=0?x:a.Owner.Pointer.ToInt64().CompareTo(b.Owner.Pointer.ToInt64());});
            int budget=Math.Clamp(p.MaxLamps.Value,0,128),needed=Math.Min(budget,ordered.Count);
            while(lights.Count<needed) {
                var go=new GameObject("VFX lamp pooled light"){hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(transform,false);
                try {
                    var light=go.AddComponent<Light>();light.enabled=false;light.shadows=LightShadows.None;
                    go.AddComponent<HDAdditionalLightData>();lights.Add(light);
                } catch {Object.Destroy(go);throw;}
            }
            for(int i=0;i<lights.Count;i++) {
                var light=lights[i];
                if(i>=needed){light.enabled=false;continue;}
                var lamp=ordered[i];var (r,g,b)=lamp.Settings.Rgb();
                light.enabled=LampPolicy.Allocate(i,budget,playing,true,lamp.Fade,lamp.Lod);
                light.type=lamp.Form==2?LightType.Point:LightType.Spot;light.spotAngle=lamp.Settings.Angle;
                light.transform.position=U(lamp.Pose.LightOrigin);light.transform.rotation=Quaternion.LookRotation(U(lamp.Pose.Normal));
                light.range=lamp.Settings.Range;light.color=new Color(r,g,b,1);light.intensity=lamp.Settings.Intensity*lamp.Fade*lamp.Lod;
            }
            // One bounded line per lamp that comes on: form, colour, intensity and whether a real Light was assigned.
            foreach(var lamp in glowing) {
                if(lamp.LoggedOn)continue;
                lamp.LoggedOn=true;
                if(onLogs++>=MaxOnLogs)continue;
                int slot=ordered.IndexOf(lamp);
                string light=designer?"none (designer preview: lens + halo only)":slot<0?"none (distance LOD)":slot<needed?"assigned":"none (budget "+budget+" in use; lens + halo only)";
                p.Log.LogInfo($"Lamp ON part={lamp.Owner.VehicleObject.Pointer} form={LampSettings.Forms[lamp.Form]} color={lamp.Settings.Color} intensity={lamp.Settings.Intensity:0.#}cd range={lamp.Settings.Range:0.##}m lens={LampLook.Brightness(lamp.Settings.Intensity,maxChannel):0.00} light={light}");
            }
            UpdateLenses(maxChannel);
            UpdateHalos(camera,haloStrength,maxChannel);
        } catch(Exception ex){Fail(ex);HideAll();}
    }
    // Lit lens: own emissive disc on the stock glass. Off/IR lamps show the untouched stock lens.
    private void UpdateLenses(float maxChannel) {
        if(disc==null||shader==null){HideLenses();return;}
        int needed=Math.Min(MaxLenses,glowing.Count);
        while(lenses.Count<needed)lenses.Add(new Glow("VFX lamp lens",transform,shader,disc,null,false));
        for(int i=0;i<lenses.Count;i++) {
            var lens=lenses[i];
            if(i>=needed){lens.Root.SetActive(false);continue;}
            var lamp=glowing[i];var (r,g,b)=lamp.Settings.Rgb();
            var (er,eg,eb)=LampLook.Emission(r,g,b,lamp.Settings.Intensity,maxChannel,lamp.Fade);
            // Unlit base: the lens colour at low level, so the disc reads as coloured glass while fading.
            if(lens.Material.HasProperty("_UnlitColor"))lens.Material.SetColor("_UnlitColor",new Color(r*.06f,g*.06f,b*.06f,1));
            if(lens.Material.HasProperty("_EmissiveColor"))lens.Material.SetColor("_EmissiveColor",new Color(er,eg,eb,1));
            var t=lens.Root.transform;t.position=U(lamp.Pose.Centre);t.rotation=U(lamp.Pose.Rotation);t.localScale=U(lamp.Pose.DiscScale);
            lens.Root.SetActive(true);
        }
    }
    private void UpdateHalos(Camera? camera,float strength,float maxChannel) {
        if(quad==null||haloTexture==null||shader==null||camera==null||strength<=0){HideHalos();return;}
        int needed=Math.Min(MaxHalos,glowing.Count);
        while(halos.Count<needed)halos.Add(new Glow("VFX lamp halo",transform,shader,quad,haloTexture,true));
        var rotation=Quaternion.LookRotation(camera.transform.forward,camera.transform.up);
        for(int i=0;i<halos.Count;i++) {
            var halo=halos[i];
            if(i>=needed){halo.Root.SetActive(false);continue;}
            var lamp=glowing[i];var (r,g,b)=lamp.Settings.Rgb();
            float alpha=LampLook.HaloAlpha(lamp.Settings.Intensity,strength,lamp.Fade);
            if(alpha<=.001f){halo.Root.SetActive(false);continue;}
            var (er,eg,eb)=LampLook.Emission(r,g,b,lamp.Settings.Intensity,maxChannel,1);
            var t=halo.Root.transform;t.position=U(lamp.Pose.HaloPoint);t.rotation=rotation;
            float d=LampLook.HaloDiameter(lamp.Form,lamp.Scale);t.localScale=new Vector3(d,d,d);
            if(halo.Material.HasProperty("_UnlitColor"))halo.Material.SetColor("_UnlitColor",new Color(0,0,0,alpha));
            if(halo.Material.HasProperty("_EmissiveColor"))halo.Material.SetColor("_EmissiveColor",new Color(er*.7f,eg*.7f,eb*.7f,1));
            halo.Root.SetActive(true);
        }
    }
    // Native inspector support (LampInspector). Returns only already adopted, eligible lamps;
    // never creates cache entries or touches native parts from OnGUI.
    [HideFromIl2Cpp] internal Lamp? Find(VehicleObject? obj) {
        if(!ready||disposed||obj==null||obj.Released)return null;
        var components=obj.Components;if(components==null)return null;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for(int j=0;j<count;j++)
            if(components[j]?.ComponentID==ComponentId && components[j].TryCast<VehicleObjectModel>() is {} model)
                return lamps.TryGetValue(model.Pointer,out var lamp)&&lamp.Alive?lamp:null;
        return null;
    }
    [HideFromIl2Cpp] internal bool IsLampObject(VehicleObject? obj) {
        if(obj==null||obj.Released)return false;
        var components=obj.Components;if(components==null)return false;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for(int j=0;j<count;j++)if(components[j]?.ComponentID==ComponentId)return true;
        return false;
    }
    [HideFromIl2Cpp] internal bool Save(Lamp lamp,LampSettings settings) {
        if(!Designer||!managed||!lamp.Alive)return false;
        settings=settings.Sanitize(Plugin.Instance.LampDefaults(lamp.Form));var json=settings.Json();
        if(!IoApiClient.SetPartData(lamp.Owner.gameObject,DataKey,json))return false;
        lamp.Settings=settings;lamp.LastJson=json;return true;
    }
    internal bool CanSave=>managed;
    private void HideLights(){foreach(var light in lights)if(light!=null)light.enabled=false;}
    private void HideHalos(){foreach(var halo in halos)if(halo.Root!=null)halo.Root.SetActive(false);}
    private void HideLenses(){foreach(var lens in lenses)if(lens.Root!=null)lens.Root.SetActive(false);}
    private void HideAll(){HideLights();HideHalos();HideLenses();}
    private void Fail(Exception ex){if(errors++<3)Plugin.Instance.Log.LogWarning("Lamp optional path: "+ex.GetType().Name+": "+ex.Message);}
    private void Clear() {
        HideAll();
        lamps.Clear();ordered.Clear();glowing.Clear();sessionRules.Clear();states.Clear();
    }
    [HideFromIl2Cpp] public void Dispose() {
        if(disposed)return;disposed=true;Clear();
        foreach(var light in lights)if(light!=null)Object.Destroy(light.gameObject);lights.Clear();
        foreach(var halo in halos)halo.Destroy();halos.Clear();
        foreach(var lens in lenses)lens.Destroy();lenses.Clear();
        if(quad!=null)Object.Destroy(quad);if(disc!=null)Object.Destroy(disc);if(haloTexture!=null)Object.Destroy(haloTexture);
    }
    public void OnDestroy()=>Dispose();
}
