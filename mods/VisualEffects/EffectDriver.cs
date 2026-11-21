using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Attributes;
using Object=UnityEngine.Object;
using NV=System.Numerics.Vector3;
namespace SprocketVisualEffects;

public sealed class EffectDriver:MonoBehaviour
{
    public EffectDriver(IntPtr pointer):base(pointer){}
    private sealed class Panel
    {
        internal readonly GameObject Root;
        internal readonly Mesh Mesh;
        internal readonly Material Material;
        internal readonly Il2CppStructArray<Vector3> Vertices=new(4);
        internal readonly Il2CppStructArray<Vector2> Uvs=new(4);
        internal Panel(Transform parent,Shader shader,Texture2D texture,string name,List<Object> resources)
        {
            Root=new GameObject(name);resources.Add(Root);Root.transform.SetParent(parent,false);
            Mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};resources.Add(Mesh);Mesh.MarkDynamic();
            Mesh.vertices=Vertices;
            Mesh.triangles=new int[]{0,1,2,0,2,3};
            Material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};resources.Add(Material);
            foreach(var n in new[]{"_UnlitColorMap","_BaseMap","_MainTex"})if(Material.HasProperty(n))Material.SetTexture(n,texture);
            foreach(var (n,v) in new (string,float)[]{("_SurfaceType",1),("_BlendMode",0),("_ZWrite",0),("_TransparentZWrite",0),("_SrcBlend",5),("_DstBlend",10),("_AlphaSrcBlend",1),("_AlphaDstBlend",10),("_CullMode",0),("_CullModeForward",0),("_ZTest",4),("_ZTestTransparent",4)})
                if(Material.HasProperty(n))Material.SetFloat(n,v);
            Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");Material.EnableKeyword("_UNLIT_COLOR_MAP");Material.renderQueue=3000;
            Root.AddComponent<MeshFilter>().sharedMesh=Mesh;
            var renderer=Root.AddComponent<MeshRenderer>();renderer.sharedMaterial=Material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            Root.SetActive(false);
        }
        internal void Render(Vector3 horizontal,Vector3 vertical,bool front,float scale,int frame,float alpha)
        {
            Root.SetActive(alpha>.001f);if(alpha<=.001f)return;
            for(var i=0;i<4;i++)
            {
                var x=i is 1 or 2?1:0;var y=i>=2?1:0;
                var v=EffectMath.Corner(ToN(horizontal),ToN(vertical),x,y,front,scale);
                Vertices[i]=ToU(v);var uv=EffectMath.Uv(frame,x,y);Uvs[i]=new Vector2(uv.X,uv.Y);
            }
            Mesh.vertices=Vertices;Mesh.uv=Uvs;Mesh.RecalculateBounds();
            foreach(var n in ColorProperties)if(Material.HasProperty(n))Material.SetColor(n,new Color(1,1,1,alpha));
        }
        internal void Hide()=>Root.SetActive(false);
        internal void Destroy(){Object.Destroy(Root);Object.Destroy(Mesh);Object.Destroy(Material);}
    }
    private sealed class Shot
    {
        internal GameObject Root;
        internal Panel Side,Front;
        internal Vector3 Origin,Forward,Up;
        internal float Scale;
        internal Shot(Transform parent,Shader shader,Texture2D side,Texture2D front,int id,List<Object> resources)
        {
            Root=new GameObject("Visual shot "+id);resources.Add(Root);Root.transform.SetParent(parent,false);
            Side=new Panel(Root.transform,shader,side,"Side",resources);Front=new Panel(Root.transform,shader,front,"Front",resources);
        }
        internal void Hide(){Side.Hide();Front.Hide();}
        internal void Destroy(){Side.Destroy();Front.Destroy();Object.Destroy(Root);}
    }
    private static readonly string[] ColorProperties={"_UnlitColor","_BaseColor","_Color"};
    private readonly List<Shot> pool=new();
    private readonly List<Object> resources=new();
    private Texture2D? side,front;
    private float[] births=Array.Empty<float>();
    private bool[] active=Array.Empty<bool>();
    private VolumetricRenderer? volume;
    private int mainThread;
    private int scene=-1,reports,impactReports,loadingSkips;
    private bool attempted,rendererAttempted,disposed;
    // 0 = volumetric bundle loading, 1 = volumetric, 2 = billboard, 3 = none.
    private int mode;
    public bool Ready{get;private set;}
    public bool IsMainThread=>Environment.CurrentManagedThreadId==mainThread;
    private static NV ToN(Vector3 v)=>new(v.x,v.y,v.z);
    private static Vector3 ToU(NV v)=>new(v.X,v.Y,v.Z);
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    public void Awake(){mainThread=Environment.CurrentManagedThreadId;}
    private Texture2D LoadAtlas(string path)
    {
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
        try
        {
            if(!ImageConversion.LoadImage(tex,File.ReadAllBytes(path),true)||tex.width!=4096||tex.height!=2048)throw new InvalidDataException("Atlas dimensions/decode failed: "+Path.GetFileName(path));
            return tex;
        }
        catch{Object.Destroy(tex);throw;}
    }
    private void ChooseRenderer()
    {
        var path=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,VolumetricRenderer.BundleFile);
        try
        {
            var p=Plugin.Instance;
            var imp=new ImpactTuning(p.ImpactsEnabled.Value,p.ImpactSparks.Value,p.ImpactShockwaves.Value,p.ImpactLights.Value,p.CookoffEffects.Value,p.ImpactScaleMultiplier.Value,p.SparkIntensity.Value,p.ExplosionFireballIntensity.Value,p.ImpactMaxDistance.Value);
            var spk=new SparkTuning(p.SparkIntensity.Value,p.ImpactScaleMultiplier.Value);
            volume=new VolumetricRenderer(transform,path,p.RaymarchSteps.Value,p.MaxVolumes.Value,p.Distance.Value,p.VolumetricDensity.Value,p.VolumetricScale.Value,p.MuzzleLight.Value,p.GroundDust.Value,p.LegacyVolumeSmoke.Value,p.MaxPuffs.Value,p.WindSpeed.Value,p.WindDirection.Value,new PuffTuning(p.SmokeAmount.Value,p.SmokeSize.Value,p.SmokeLifetime.Value,p.SmokeBrightness.Value,p.DustAmount.Value),p.MuzzleFireIntensity.Value,p.MuzzleSparks.Value,p.MuzzleShockwave.Value,p.MaxSparks.Value,p.MaxImpactVolumes.Value,imp,spk);
            var size=File.Exists(path)?new FileInfo(path).Length.ToString()+" bytes":"missing";
            p.Log.LogInfo($"Renderer=Volumetric loading bundle={path} size={size} (async LoadFromFileAsync + LoadAllAssetsAsync(Type), every call guarded; SelfTest lines follow)");
            if(volume.BeginLoad()){mode=0;Ready=true;return;}
            VolumetricFailed(volume.FailureReason);
        }
        catch(Exception ex){VolumetricFailed(ex.GetType().Name+": "+ex.Message);}
    }
    // Polls the async load once per frame; switches to volumetric or the fallback.
    private void PollVolumetricLoad()
    {
        if(volume==null){VolumetricFailed("renderer missing");return;}
        volume.Poll();
        if(volume.State==VolumetricRenderer.LoadState.Ready)
        {
            mode=1;
            Plugin.Instance.Log.LogInfo($"Renderer=Volumetric READY bundle={volume.BundlePath} size={volume.BundleBytes} bytes loaded in {volume.LoadSeconds:F2} s shader={volume.LoadedShaderName} supported={volume.ShaderSupported} material={volume.MaterialName} volumes={volume.Capacity} steps={volume.Steps} density=x{volume.DensityFactor:F2} (base {EffectMath.HeavyDensity:F1}) scale=x{volume.ScaleFactor:F2} lifetime={EffectMath.HeavyLifetime:F1}s muzzleLight={volume.LightEnabled} groundDust={volume.DustEnabled} puffs={volume.PuffStatus} sparks={volume.SparkStatus} waves={volume.WaveStatus} legacySmoke={volume.LegacySmoke}");
            if(loadingSkips>0)Plugin.Instance.Trace($"{loadingSkips} shot(s) before the bundle finished loading were not drawn");
        }
        else if(volume.State==VolumetricRenderer.LoadState.Failed)VolumetricFailed(volume.FailureReason);
    }
    private void VolumetricFailed(string failure)
    {
        volume?.Dispose();volume=null;Ready=false;
        Plugin.Instance.Log.LogWarning("Renderer=Volumetric FAILED: "+failure);
        if(Plugin.Instance.FallbackToBillboard.Value)
        {
            mode=2;
            Plugin.Instance.Log.LogInfo("Renderer=Billboard (FallbackToBillboard=true; existing 0.1.0 overlay after volumetric failure)");
        }
        else
        {
            mode=3;Ready=false;
            Plugin.Instance.Log.LogWarning("Renderer=None (volumetric failed and FallbackToBillboard=false)");
        }
    }
    private void InitializeBillboard()
    {
        attempted=true;
        var assets=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"assets");
        AssetContract.Validate(assets);
        side=LoadAtlas(Path.Combine(assets,"muzzle-side.png"));front=LoadAtlas(Path.Combine(assets,"muzzle-front.png"));
        var shader=Shader.Find("HDRP/Unlit")??Shader.Find("Unlit/Transparent");
        if(shader==null)throw new InvalidOperationException("No transparent unlit shader available");
        var capacity=Math.Clamp(Plugin.Instance.Capacity.Value,1,24);
        births=new float[capacity];active=new bool[capacity];
        for(var i=0;i<capacity;i++)pool.Add(new Shot(transform,shader,side,front,i,resources));
        Ready=true;Plugin.Instance.Trace($"Visual pool ready: {capacity} shots, two 4096x2048 atlases, shader {shader.name}");
    }
    public void Update()
    {
        if(disposed)return;
        try
        {
            if(!rendererAttempted)
            {
                rendererAttempted=true;ChooseRenderer();scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            }
            if(mode==3)return;
            if(mode==0){PollVolumetricLoad();if(mode!=1&&mode!=2)return;}
            var newScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if(newScene!=scene){if(mode==1)volume?.Clear();else ClearShots();scene=newScene;}
            if(!Plugin.Instance.Enabled.Value){if(mode==1)volume?.Clear();else ClearShots();return;}
            var camera=Camera.main;
            var now=Time.time;
            if(mode==1){volume?.UpdateFrame(now,camera);return;}
            if(!attempted)InitializeBillboard();
            if(!Ready)return;
            for(var i=0;i<pool.Count;i++)
            {
                if(!active[i])continue;
                var shot=pool[i];var frame=EffectMath.Frame(now-births[i]);
                if(frame<0){active[i]=false;shot.Hide();continue;}
                if(camera==null){shot.Hide();continue;}
                Render(shot,camera,frame);
            }
        }
        catch(Exception ex){Plugin.Instance.Warn(ex);Dispose();}
    }
    [HideFromIl2Cpp]
    internal void Emit(Vector3 origin,Quaternion rotation,float calibre,Transform? cannonTransform=null)
    {
        if(!Ready||disposed||!IsMainThread||!Finite(origin)||!float.IsFinite(calibre))return;
        if(mode==0){loadingSkips++;return;}
        if(mode==1)
        {
            var drawn=volume?.Emit(origin,rotation,calibre,cannonTransform);
            reports++;
            Plugin.Instance.Trace(drawn!=null
                ?$"Shot #{reports} Renderer=Volumetric calibre={calibre}mm world=({origin.x:F2},{origin.y:F2},{origin.z:F2}) {drawn}"
                :$"Shot #{reports} Renderer=Volumetric not drawn (no camera, beyond MaxDistance or invalid transform) calibre={calibre}mm");
            return;
        }
        if(mode!=2)return;
        var forward=rotation*Vector3.forward;var up=rotation*Vector3.up;
        if(!Finite(forward)||!Finite(up)||forward.sqrMagnitude<.9f)return;
        var camera=Camera.main;
        var max=EffectMath.Safe(Plugin.Instance.Distance.Value,1500,50,5000);
        if(camera==null||(camera.transform.position-origin).sqrMagnitude>max*max)return;
        var i=EffectMath.SelectSlot(births,active);var shot=pool[i];
        // Capture values only; no retained native transform/parent or recoil-driven position.
        shot.Origin=origin;shot.Forward=forward.normalized;shot.Up=up.normalized;
        shot.Scale=EffectMath.Scale(calibre,Plugin.Instance.Multiplier.Value);
        shot.Root.transform.position=origin;shot.Root.transform.rotation=Quaternion.identity;
        births[i]=Time.time;active[i]=true;
        Render(shot,camera,0);
        reports++;Plugin.Instance.Trace($"Shot #{reports} Renderer=Billboard calibre={calibre}mm slot={i} world=({origin.x:F2},{origin.y:F2},{origin.z:F2}) scale={shot.Scale:F2} (ScaleMultiplier; atlas opacity, no density)");
    }
    [HideFromIl2Cpp]
    internal void EmitImpact(ImpactKind kind,Vector3 pos,Vector3 normal,Vector3 velocity,float calibreMm)
    {
        if(!Ready||disposed||!IsMainThread||mode!=1||volume==null)return;
        var desc=volume.EmitImpact(kind,pos,normal,velocity,calibreMm);
        if(desc!=null&&Plugin.Instance.Detailed.Value&&impactReports++<80)
            Plugin.Instance.Trace($"Impact #{impactReports} {desc} world=({pos.x:F2},{pos.y:F2},{pos.z:F2})");
    }
    [HideFromIl2Cpp]
    internal void EmitCookoff(Vector3 pos,float mass,float impulse)
    {
        if(!Ready||disposed||!IsMainThread||mode!=1||volume==null)return;
        var desc=volume.EmitCookoff(pos,mass,impulse);
        if(desc!=null&&Plugin.Instance.Detailed.Value&&impactReports++<80)
            Plugin.Instance.Trace($"Cookoff #{impactReports} {desc} world=({pos.x:F2},{pos.y:F2},{pos.z:F2})");
    }
    // Static and hidden from Il2Cpp: the private Shot parameter is not an Il2Cpp
    // type, which made class injection warn about an unsupported parameter.
    [HideFromIl2Cpp]
    private static void Render(Shot shot,Camera camera,int frame)
    {
        var view=camera.transform.position-shot.Origin;
        var max=EffectMath.Safe(Plugin.Instance.Distance.Value,1500,50,5000);
        if(view.sqrMagnitude>max*max||view.sqrMagnitude<.0001f){shot.Hide();return;}
        view=view.normalized;
        var alignment=Vector3.Dot(view,shot.Forward);var frontAlpha=EffectMath.FrontWeight(alignment);
        var vertical=ToU(EffectMath.SideVertical(ToN(shot.Forward),ToN(shot.Up),ToN(view)));
        // Side axis stays in shot direction. Front remains camera-facing at axial views.
        // Both panels retain full 7x3.5 source extent and their own source muzzle anchor.
        shot.Side.Render(shot.Forward,vertical,false,shot.Scale,frame,1-frontAlpha);
        shot.Front.Render(camera.transform.right,camera.transform.up,true,shot.Scale,frame,frontAlpha);
    }
    private void ClearShots(){for(var i=0;i<pool.Count;i++){active[i]=false;pool[i].Hide();}}
    public void OnDisable()
    {
        if(disposed)return;
        if(mode==1)volume?.Clear();
        else ClearShots();
    }
    public void OnDestroy()=>Dispose();
    [HideFromIl2Cpp]
    public void Dispose()
    {
        if(disposed)return;disposed=true;Ready=false;
        if(volume!=null){volume.Dispose();volume=null;}
        // Track allocations as they happen: even a partial preload is reclaimed.
        for(var i=resources.Count-1;i>=0;i--)if(resources[i]!=null)Object.Destroy(resources[i]);
        resources.Clear();pool.Clear();
        if(side!=null)Object.Destroy(side);if(front!=null)Object.Destroy(front);
    }
}
