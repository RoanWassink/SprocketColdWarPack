using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
namespace SprocketVisualEffects;

// Loads the already-built Unity bundle once and owns a small preallocated pool
// of frozen muzzle proxies. The shader raymarches in each proxy's local space;
// the proxy itself never follows recoil or the moving density field.
//
// 0.2.3 adds, per shot: an HDRP point-light flash (pool of <= 2, ~0.12 s, no
// shadows) and a ground dust ring volume placed by a downward Physics.RaycastAll
// (same shader, _Mode 1). Proxy boxes come from EffectMath and are passed to the
// shader as _BoxMin/_BoxMax, which also fades density softly before the faces.
//
// 0.2.2 load path (each step guarded by NativeGuard, logged as a one-time self-test):
//   open   AssetBundle.LoadFromFileAsync(string) -> AssetBundleCreateRequest.assetBundle
//   assets AssetBundle.LoadAllAssetsAsync(Il2CppSystem.Type) -> AssetBundleRequest.allAssets
//          fallback: LoadAssetAsync(name, Material) -> .asset, shader from material.shader
// Not used: LoadFromFile/LoadAllAssets/LoadFromMemory/LoadAsset (missing span helper,
// 0.2.0) and the parameterless LoadAllAssetsAsync() (interop field collision invokes
// the generic definition LoadAllAssetsAsync<T>, 0.2.1 "Invalid call to method").
// checks/InteropScan proves every interop call of this DLL statically.
internal sealed class VolumetricRenderer
{
    internal const string ShaderName="SprocketVfx/HeavyMuzzleVolume";
    internal const string BundleFile="sprocketvfx.bundle";
    internal const float LoadTimeoutSeconds=20f;
    private static readonly string[] RequiredUniforms={"_Age","_Seed","_Steps","_Density","_FireStrength","_CurlStrength","_SmokeColor","_Lifetime","_DepthClamp"};
    private static readonly string[] OptionalUniforms={"_Direction","_CalibreScale"};
    internal enum LoadState{Idle,LoadingBundle,LoadingAssets,Ready,Failed}
    private enum AssetRoute{AllAssetsByType,MaterialByName}
    internal const string MaterialAssetName="assets/sprocketvfx/heavymuzzle.mat";
    private sealed class Volume
    {
        internal readonly GameObject Root;
        internal readonly MeshRenderer Renderer;
        internal readonly MaterialPropertyBlock Block;
        internal Volume(Transform parent,Mesh mesh,Material material,string name)
        {
            Root=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};
            Root.transform.SetParent(parent,false);
            Root.AddComponent<MeshFilter>().sharedMesh=mesh;
            Renderer=Root.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial=material;
            Renderer.shadowCastingMode=ShadowCastingMode.Off;
            Renderer.receiveShadows=false;
            Renderer.lightProbeUsage=LightProbeUsage.Off;
            Renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            Renderer.allowOcclusionWhenDynamic=false;
            Block=new MaterialPropertyBlock();
            Renderer.SetPropertyBlock(Block);
            Root.SetActive(false);
        }
    }
    private readonly Transform parent;
    private readonly string bundlePath;
    private readonly float maxDistance;
    private AssetBundle? bundle;
    private AssetBundleCreateRequest? bundleRequest;
    private AssetBundleRequest? assetRequest;
    private float loadStarted;
    private AssetRoute route;
    private bool fallbackTried;
    private Mesh? muzzleMesh,dustMesh;
    private Material? material;
    private Volume[]? volumes,dust;
    private float[] births=Array.Empty<float>(),dustBirths=Array.Empty<float>();
    private bool[] active=Array.Empty<bool>(),dustActive=Array.Empty<bool>();
    private int[] emitFrame=Array.Empty<int>();
    private bool[] firstUpdatePending=Array.Empty<bool>();
    private string[] anchorInfo=Array.Empty<string>();
    private Light[]? flashes;
    private float[] flashBirths=Array.Empty<float>(),flashPeak=Array.Empty<float>();
    private bool[] flashActive=Array.Empty<bool>();
    private int seedSerial,shots;
    private bool hasDirection,hasCalibreScale,disposed;
    internal LoadState State{get;private set;}=LoadState.Idle;
    internal bool Ready=>State==LoadState.Ready&&!disposed;
    internal string FailureReason{get;private set;}="";
    internal string MaterialName{get;private set;}="";
    internal string LoadedShaderName{get;private set;}="";
    internal bool ShaderSupported{get;private set;}
    internal long BundleBytes{get;private set;}
    internal float LoadSeconds{get;private set;}
    internal string BundlePath=>bundlePath;
    internal int Capacity{get;}
    internal int Steps{get;}
    internal float DensityFactor{get;}
    internal float ScaleFactor{get;}
    internal bool LightEnabled{get;}
    internal bool DustEnabled{get;}
    internal bool LegacySmoke{get;private set;}
    internal string PuffStatus{get;private set;}="not loaded";
    private PuffRenderer? puffs;
    private readonly int maxPuffs;private readonly float windSpeed,windDirection;private readonly PuffTuning tuning;
    // Volume lifetime: 0.3.0 keeps only the fire/blast + jet (0.6 s) unless legacy smoke.
    private float VolumeLifetime=>LegacySmoke?EffectMath.HeavyLifetime:EffectMath.VolumeFireLifetime;
    internal VolumetricRenderer(Transform parent,string bundlePath,int steps,int maxVolumes,float maxDistance,float densityFactor,float scaleFactor,bool muzzleLight,bool groundDust,bool legacySmoke=false,int maxPuffs=160,float windSpeed=2.5f,float windDirection=200f,PuffTuning? tuning=null)
    {
        this.tuning=tuning??PuffTuning.Default;
        LegacySmoke=legacySmoke;this.maxPuffs=maxPuffs;this.windSpeed=windSpeed;this.windDirection=windDirection;
        LightEnabled=muzzleLight;DustEnabled=groundDust;
        this.parent=parent;
        this.bundlePath=Path.GetFullPath(bundlePath);
        Steps=EffectMath.VolumeSteps(steps);
        Capacity=EffectMath.VolumeCapacity(maxVolumes);
        this.maxDistance=EffectMath.Safe(maxDistance,1500,50,5000);
        DensityFactor=EffectMath.VolumetricDensityFactor(densityFactor);
        ScaleFactor=EffectMath.VolumetricScaleFactor(scaleFactor);
    }
    private void Fail(string reason){FailureReason=reason;State=LoadState.Failed;Dispose();}
    private static void SelfTest(string step,bool ok,string detail)=>Plugin.Instance.Log.LogInfo($"SelfTest {step} {(ok?"PASS":"FAIL")}: {detail}");
    private static void Skip(params string[] steps){foreach(var s in steps)Plugin.Instance.Log.LogInfo($"SelfTest {s} SKIP");}
    private static bool Guard(string label,Type type,string field,string name,int parms)
    {
        var r=NativeGuard.Check(type,field,name,parms);
        SelfTest("guard "+label,r.Ok,r.Detail);
        return r.Ok;
    }
    // Non-short-circuit '&' so every guard line is logged once.
    private static bool OpenGuards()=>
        Guard("AssetBundle.LoadFromFileAsync(string)",typeof(AssetBundle),"NativeMethodInfoPtr_LoadFromFileAsync_Public_Static_AssetBundleCreateRequest_String_0","LoadFromFileAsync",1)
        &Guard("AsyncOperation.isDone",typeof(AsyncOperation),"NativeMethodInfoPtr_get_isDone_Public_get_Boolean_0","get_isDone",0)
        &Guard("AssetBundleCreateRequest.assetBundle",typeof(AssetBundleCreateRequest),"NativeMethodInfoPtr_get_assetBundle_Public_get_AssetBundle_0","get_assetBundle",0);
    private static bool AllAssetsGuards()=>
        Guard("AssetBundle.LoadAllAssetsAsync(Type)",typeof(AssetBundle),"NativeMethodInfoPtr_LoadAllAssetsAsync_Public_AssetBundleRequest_Type_0","LoadAllAssetsAsync",1)
        &Guard("AssetBundleRequest.allAssets",typeof(AssetBundleRequest),"NativeMethodInfoPtr_get_allAssets_Public_get_Il2CppReferenceArray_1_Object_0","get_allAssets",0);
    private static bool ByNameGuards()=>
        Guard("AssetBundle.LoadAssetAsync(string,Type)",typeof(AssetBundle),"NativeMethodInfoPtr_LoadAssetAsync_Public_AssetBundleRequest_String_Type_0","LoadAssetAsync",2)
        &Guard("AssetBundleRequest.asset",typeof(AssetBundleRequest),"NativeMethodInfoPtr_get_asset_Public_get_Object_0","get_asset",0);
    private void FailBeforeOpen(string selfTest,string reason)
    {
        SelfTest("open",false,selfTest);Skip("load-shader","load-material","isSupported");Fail(reason);
    }
    // Starts the asynchronous bundle load; returns false (with FailureReason) on an immediate failure.
    internal bool BeginLoad()
    {
        if(disposed||State!=LoadState.Idle){Fail("renderer already used or disposed");return false;}
        try
        {
            // Negative control, never called: proves in the log that the guard
            // recognises the exact 0.2.1 failure (expected: FAIL alias).
            var control=NativeGuard.Check(typeof(AssetBundle),"NativeMethodInfoPtr_LoadAllAssetsAsync_Public_AssetBundleRequest_0","LoadAllAssetsAsync",0);
            Plugin.Instance.Log.LogInfo($"SelfTest guard-control AssetBundle.LoadAllAssetsAsync() (0.2.1 route, not used) {(control.Ok?"PASS (unexpected)":"FAIL as expected")}: {control.Detail}");
            var guardsOk=OpenGuards();
            if(!File.Exists(bundlePath)){FailBeforeOpen("bundle missing at "+bundlePath,"bundle missing at "+bundlePath);return false;}
            BundleBytes=new FileInfo(bundlePath).Length;
            if(!guardsOk){FailBeforeOpen("open route not callable (see SelfTest guard lines)","stripped/alias in bundle open route (see SelfTest guard lines)");return false;}
            loadStarted=Time.realtimeSinceStartup;
            bundleRequest=AssetBundle.LoadFromFileAsync(bundlePath);
            if(bundleRequest==null){FailBeforeOpen("LoadFromFileAsync returned null","AssetBundle.LoadFromFileAsync returned null: "+bundlePath);return false;}
            State=LoadState.LoadingBundle;
            return true;
        }
        catch(Exception ex){FailBeforeOpen(ex.GetType().Name+": "+ex.Message,ex.GetType().Name+": "+ex.Message);return false;}
    }
    // Starts the first guarded asset route that is callable; false when none is.
    private bool StartAssetRoute()
    {
        if(!fallbackTried)
        {
            if(AllAssetsGuards())
            {
                route=AssetRoute.AllAssetsByType;
                assetRequest=bundle!.LoadAllAssetsAsync(Il2CppType.Of<Object>());
                if(assetRequest!=null)return true;
                Plugin.Instance.Log.LogInfo("SelfTest route LoadAllAssetsAsync(Type) FAIL: returned null; next: LoadAssetAsync(name)");
            }
            else Plugin.Instance.Log.LogInfo("SelfTest route LoadAllAssetsAsync(Type) FAIL: not callable (stripped/alias); next: LoadAssetAsync(name)");
        }
        if(fallbackTried)return false;
        fallbackTried=true;route=AssetRoute.MaterialByName;
        if(!ByNameGuards())return false;
        assetRequest=bundle!.LoadAssetAsync(MaterialAssetName,Il2CppType.Of<Material>());
        return assetRequest!=null;
    }
    // Called once per frame while loading. Never blocks the main thread.
    internal void Poll()
    {
        if(disposed||(State!=LoadState.LoadingBundle&&State!=LoadState.LoadingAssets))return;
        var step="open";
        try
        {
            var elapsed=Time.realtimeSinceStartup-loadStarted;
            if(elapsed>LoadTimeoutSeconds)throw new TimeoutException($"bundle load not finished after {LoadTimeoutSeconds:F0} s ({State})");
            if(State==LoadState.LoadingBundle)
            {
                if(bundleRequest==null||!bundleRequest.isDone)return;
                bundle=bundleRequest.assetBundle;bundleRequest=null;
                if(bundle==null)throw new InvalidDataException("LoadFromFileAsync produced no bundle (wrong Unity version/platform or corrupt file): "+bundlePath);
                SelfTest("open",true,$"{bundlePath} ({BundleBytes} bytes) in {elapsed:F2} s");
                step="load-shader";
                if(!StartAssetRoute())throw new InvalidOperationException("no callable asset route (see SelfTest guard lines)");
                State=LoadState.LoadingAssets;
                return;
            }
            step="load-shader";
            if(assetRequest==null||!assetRequest.isDone)return;
            Il2CppReferenceArray<Object>? assets;
            try
            {
                if(route==AssetRoute.AllAssetsByType)assets=assetRequest.allAssets;
                else{var one=assetRequest.asset;assets=one==null?null:new Il2CppReferenceArray<Object>(new Object[]{one});}
            }
            catch(Exception ex)when(route==AssetRoute.AllAssetsByType&&!fallbackTried)
            {
                Plugin.Instance.Log.LogInfo($"SelfTest route LoadAllAssetsAsync(Type) FAIL: {ex.GetType().Name}: {ex.Message}; next: LoadAssetAsync(name)");
                assetRequest=null;
                if(!StartAssetRoute())throw new InvalidOperationException("no callable asset route (see SelfTest guard lines)");
                return;
            }
            assetRequest=null;
            CreatePool(assets);
            LoadSeconds=elapsed;
            State=LoadState.Ready;
        }
        catch(SelfTestReported ex){Fail(ex.Message);}
        catch(Exception ex)
        {
            var reason=ex.GetType().Name+": "+ex.Message;
            SelfTest(step,false,reason);
            Skip(step=="open"?new[]{"load-shader","load-material","isSupported"}:new[]{"load-material","isSupported"});
            Fail(reason);
        }
    }
    // Thrown by CreatePool after it has already written its own SelfTest lines.
    private sealed class SelfTestReported:Exception{internal SelfTestReported(string m):base(m){}}
    private void CreatePool(Il2CppReferenceArray<Object>? assets)
    {
        if(assets==null||assets.Length==0)throw new InvalidDataException("bundle contains no loadable assets: "+bundlePath);
        Shader? shader=null;
        foreach(var asset in assets)
        {
            if(asset==null)continue;
            var candidate=asset.TryCast<Shader>();
            if(candidate!=null&&candidate.name==ShaderName){shader=candidate;break;}
        }
        Material? source=null;
        foreach(var asset in assets)
        {
            if(asset==null)continue;
            var candidate=asset.TryCast<Material>();
            if(candidate==null)continue;
            // By-name route delivers only the material; its shader is the bundled one.
            if(shader==null&&candidate.shader!=null&&candidate.shader.name==ShaderName)shader=candidate.shader;
            if(shader!=null&&candidate.shader==shader){source=candidate;break;}
        }
        var via=route==AssetRoute.AllAssetsByType?"LoadAllAssetsAsync(Type)":"LoadAssetAsync(name).shader";
        if(shader==null){SelfTest("load-shader",false,$"{ShaderName} not in bundle ({assets.Length} assets via {via})");Skip("load-material","isSupported");throw new SelfTestReported("shader "+ShaderName+" not found in "+Path.GetFileName(bundlePath));}
        SelfTest("load-shader",true,shader.name+" via "+via);
        if(source==null){SelfTest("load-material",false,"no material using "+ShaderName);Skip("isSupported");throw new SelfTestReported("no material using "+ShaderName+" in "+Path.GetFileName(bundlePath));}
        SelfTest("load-material",true,source.name);
        LoadedShaderName=shader.name;MaterialName=source.name;
        var supportedGuard=NativeGuard.Check(typeof(Shader),"NativeMethodInfoPtr_get_isSupported_Public_get_Boolean_0","get_isSupported",0);
        if(!supportedGuard.Ok){SelfTest("isSupported",false,"Shader.isSupported not callable: "+supportedGuard.Detail);throw new SelfTestReported("Shader.isSupported not callable");}
        ShaderSupported=shader.isSupported;
        SelfTest("isSupported",ShaderSupported,ShaderSupported?"shader runs on this GPU/graphics API":"shader NOT supported on this GPU/graphics API");
        if(!ShaderSupported)throw new SelfTestReported("shader "+ShaderName+" is not supported on this GPU/graphics API");

        // Own clone: the bundle can be unloaded with true on dispose without
        // destroying a material another loaded asset still references.
        var mat=new Material(source){hideFlags=HideFlags.HideAndDontSave,renderQueue=3000};   // fire/jet draws before the puff layer (3001)
        material=mat;
        var missing=new List<string>();
        foreach(var name in RequiredUniforms)if(!mat.HasProperty(name))missing.Add(name);
        if(missing.Count>0){SelfTest("uniforms",false,"material lacks "+string.Join(",",missing));throw new SelfTestReported("material missing required uniform(s): "+string.Join(",",missing));}
        var optional=new List<string>();
        foreach(var name in OptionalUniforms)if(!mat.HasProperty(name))optional.Add(name);
        hasDirection=mat.HasProperty("_Direction");
        hasCalibreScale=mat.HasProperty("_CalibreScale");
        if(optional.Count>0)Plugin.Instance.Trace("Renderer=Volumetric optional uniforms absent: "+string.Join(",",optional)+" (direction/calibre applied by proxy transform)");
        // 0.3.0 puff layer: material with the SixWayPuff shader + both six-way arrays.
        Material? puffSource=null;
        foreach(var asset in assets)
        {
            if(asset==null)continue;
            var m=asset.TryCast<Material>();
            if(m!=null&&m.shader!=null&&m.shader.name==EffectMath.PuffShaderName){puffSource=m;break;}
        }
        if(puffSource!=null&&puffSource.HasProperty("_SixWayA")&&puffSource.GetTexture("_SixWayA")!=null&&puffSource.GetTexture("_SixWayB")!=null&&puffSource.shader.isSupported)
        {
            puffs=new PuffRenderer(parent,puffSource,maxPuffs){Wind=PuffMath.Wind(windSpeed,windDirection),Opacity=DensityFactor,Tuning=tuning};
            PuffStatus=$"{EffectMath.PuffShaderName}, two six-way arrays, pool {puffs.Capacity}, wind {windSpeed:F1} m/s @ {windDirection:F0} deg, smoke amount x{tuning.SmokeAmount:F2} size x{tuning.SmokeSize:F2} life x{tuning.SmokeLifetime:F2} brightness x{tuning.SmokeBrightness:F2}, dust x{tuning.DustAmount:F2}";
            SelfTest("load-puffs",true,PuffStatus+(LegacySmoke?" (LegacyVolumeSmoke=true: volume smoke kept as well)":""));
        }
        else
        {
            PuffStatus="unavailable";LegacySmoke=true;
            SelfTest("load-puffs",false,"SixWayPuff material/arrays missing or unsupported in the bundle; falling back to the 0.2.3 volume smoke + dust");
        }
        muzzleMesh=CreateProxyMesh(U(EffectMath.MuzzleBoxMin),U(EffectMath.MuzzleBoxMax));
        volumes=new Volume[Capacity];
        births=new float[Capacity];
        active=new bool[Capacity];
        emitFrame=new int[Capacity];firstUpdatePending=new bool[Capacity];anchorInfo=new string[Capacity];
        for(var i=0;i<Capacity;i++)volumes[i]=new Volume(parent,muzzleMesh,mat,"Volumetric muzzle "+i);
        if(DustEnabled&&LegacySmoke)
        {
            dustMesh=CreateProxyMesh(U(EffectMath.DustBoxMin),U(EffectMath.DustBoxMax));
            dust=new Volume[Capacity];dustBirths=new float[Capacity];dustActive=new bool[Capacity];
            for(var i=0;i<Capacity;i++)dust[i]=new Volume(parent,dustMesh,mat,"Ground dust "+i);
            SelfTest("ground-dust",true,$"{Capacity} dust ring volume(s), ground by Physics.RaycastAll <= {EffectMath.DustMaxDrop:F0} m");
        }
        else Plugin.Instance.Log.LogInfo(DustEnabled?"SelfTest ground-dust PASS: dust as puffs in the sorted puff layer (volume dust off)":"SelfTest ground-dust SKIP: GroundDust=false");
        if(LightEnabled)
        {
            try
            {
                var count=Math.Min(Capacity,EffectMath.FlashLightPool);
                flashes=new Light[count];flashBirths=new float[count];flashPeak=new float[count];flashActive=new bool[count];
                for(var i=0;i<count;i++)
                {
                    var go=new GameObject("Muzzle flash light "+i){hideFlags=HideFlags.HideAndDontSave};
                    go.transform.SetParent(parent,false);
                    var light=go.AddComponent<Light>();
                    light.type=LightType.Point;light.shadows=LightShadows.None;light.range=EffectMath.FlashRange;
                    light.color=new Color(EffectMath.FlashR,EffectMath.FlashG,EffectMath.FlashB,1f);light.intensity=0;
                    // HDRP per-light data; without it HDRP uses its defaults, so a failure here is not fatal.
                    try{go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();}
                    catch(Exception ex){Plugin.Instance.Log.LogInfo("SelfTest flash-light note: HDAdditionalLightData not added ("+ex.GetType().Name+"); HDRP defaults used");}
                    light.enabled=false;go.SetActive(true);
                    flashes[i]=light;
                }
                SelfTest("flash-light",true,$"{count} HDRP point light(s), peak {EffectMath.FlashPeakCandela:F0} cd, {EffectMath.FlashDuration*1000:F0} ms, no shadows");
            }
            catch(Exception ex){SelfTest("flash-light",false,ex.GetType().Name+": "+ex.Message+" (smoke continues without light)");DestroyFlashes();}
        }
        else Plugin.Instance.Log.LogInfo("SelfTest flash-light SKIP: MuzzleLight=false");
    }
    private static Vector3 U(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
    private static Mesh CreateProxyMesh(Vector3 lo,Vector3 hi)
    {
        // Same local box as _BoxMin/_BoxMax in HeavyMuzzle.shader (set per volume).
        // A uniform transform scale enlarges the whole plume; the pivot stays put.
        var vertices=new Il2CppStructArray<Vector3>(8);
        vertices[0]=new Vector3(lo.x,lo.y,lo.z);
        vertices[1]=new Vector3(hi.x,lo.y,lo.z);
        vertices[2]=new Vector3(hi.x,hi.y,lo.z);
        vertices[3]=new Vector3(lo.x,hi.y,lo.z);
        vertices[4]=new Vector3(lo.x,lo.y,hi.z);
        vertices[5]=new Vector3(hi.x,lo.y,hi.z);
        vertices[6]=new Vector3(hi.x,hi.y,hi.z);
        vertices[7]=new Vector3(lo.x,hi.y,hi.z);
        var mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};
        mesh.vertices=vertices;
        mesh.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7};
        mesh.RecalculateBounds();
        return mesh;
    }
    private static void Box(MaterialPropertyBlock block,Vector3 lo,Vector3 hi)
    {
        block.SetVector("_BoxMin",new Vector4(lo.x,lo.y,lo.z,0));
        block.SetVector("_BoxMax",new Vector4(hi.x,hi.y,hi.z,0));
    }
    // Returns a short per-shot description for the log, or null when the shot was not drawn.
    internal string? Emit(Vector3 origin,Quaternion rotation,float calibre,Transform? cannonTransform=null)
    {
        if(!Ready||volumes==null||births.Length==0)return null;
        if(!float.IsFinite(origin.x)||!float.IsFinite(origin.y)||!float.IsFinite(origin.z)||!float.IsFinite(calibre))return null;
        var forward=rotation*Vector3.forward;var up=rotation*Vector3.up;
        if(!float.IsFinite(forward.x)||!float.IsFinite(forward.y)||!float.IsFinite(forward.z)||forward.sqrMagnitude<.9f)return null;
        if(!float.IsFinite(up.x)||!float.IsFinite(up.y)||!float.IsFinite(up.z))return null;
        var camera=Camera.main;
        if(camera==null)return null;
        var position=camera.transform.position;
        if((position-origin).sqrMagnitude>maxDistance*maxDistance)return null;
        var slot=EffectMath.SelectSlot(births,active);
        if(active[slot])Plugin.Instance.Trace($"Volume slot {slot} recycled at age {Time.time-births[slot]:F2} s (pool of {Capacity} full; raise MaxVolumes to keep older smoke)");
        var volume=volumes[slot];
        var scale=EffectMath.Scale(calibre,ScaleFactor);
        var density=EffectMath.HeavyDensity*DensityFactor;
        seedSerial=(seedSerial+1)%100000;
        var seed=(seedSerial*0.61803398875f)%97f;
        var now=Time.time;
        births[slot]=now;
        active[slot]=true;
        emitFrame[slot]=Time.frameCount;firstUpdatePending[slot]=true;
        anchorInfo[slot]=Plugin.Instance.Detailed.Value&&shots<40?AnchorInfo(origin,forward,cannonTransform):"";
        volume.Root.transform.position=origin;
        volume.Root.transform.rotation=Quaternion.LookRotation(forward,up);
        volume.Root.transform.localScale=Vector3.one*scale;
        volume.Block.SetFloat("_Age",.001f);
        volume.Block.SetFloat("_Seed",seed);
        volume.Block.SetFloat("_Steps",Steps);
        volume.Block.SetFloat("_Density",density);
        volume.Block.SetFloat("_FireStrength",EffectMath.HeavyFireStrength);
        volume.Block.SetFloat("_FireLuminance",EffectMath.HeavyFireLuminance);
        volume.Block.SetFloat("_CurlStrength",EffectMath.HeavyCurlStrength);
        volume.Block.SetColor("_SmokeColor",new Color(EffectMath.HeavySmokeR,EffectMath.HeavySmokeG,EffectMath.HeavySmokeB,1f));
        volume.Block.SetFloat("_Lifetime",VolumeLifetime);
        volume.Block.SetFloat("_SmokeFadeEnd",LegacySmoke?99f:EffectMath.VolumeSmokeFadeEnd);
        volume.Block.SetFloat("_DepthClamp",1f);
        volume.Block.SetFloat("_Mode",0f);
        Box(volume.Block,U(EffectMath.MuzzleBoxMin),U(EffectMath.MuzzleBoxMax));
        if(hasDirection)volume.Block.SetVector("_Direction",new Vector4(forward.x,forward.y,forward.z,0));
        if(hasCalibreScale)volume.Block.SetFloat("_CalibreScale",scale);
        volume.Renderer.SetPropertyBlock(volume.Block);
        volume.Root.SetActive(true);
        shots++;
        var light=EmitFlash(origin+forward*(EffectMath.FlashForward*scale),scale,now);
        var groundHit=FindGround(origin,scale,out var groundPoint,out var groundDrop);
        var ground=LegacySmoke?EmitDust(groundHit,groundPoint,groundDrop,forward,scale,seed,now):(groundHit?$"{groundDrop:F1}m-below":"no-ground");
        var puffText=puffs!=null?puffs.Emit(origin,forward,up,scale,groundHit&&DustEnabled?groundPoint:null,Mathf.Sqrt((position-origin).sqrMagnitude)):"puffs=off";
        return $"slot={slot} density={density:F2} (x{DensityFactor:F2}) scale={scale:F2} (x{ScaleFactor:F2}) seed={seed:F2} light={light} dust={ground} {puffText}";
    }
    // Where the anchor (= the MuzzleFlashEffect transform) sits relative to the
    // cannon transform it is parented to, and to the foremost geometry along the
    // bore (barrel tip estimate from renderer bounds). No hook parameters needed.
    private static string AnchorInfo(Vector3 origin,Vector3 forward,Transform? cannonTransform)
    {
        try
        {
            if(cannonTransform==null)return "anchor: effect transform has no parent";
            var local=cannonTransform.InverseTransformPoint(origin);
            var text=$"anchor in parent '{cannonTransform.name}' local=({local.x:F2},{local.y:F2},{local.z:F2}) along-bore={Vector3.Dot(origin-cannonTransform.position,forward):F2} m";
            var renderers=cannonTransform.GetComponentsInChildren<Renderer>();
            var tip=float.NegativeInfinity;
            if(renderers!=null)
                for(var i=0;i<renderers.Length;i++)
                {
                    var r=renderers[i];if(r==null)continue;
                    var b=r.bounds;var mn=b.min;var mx=b.max;
                    for(var c=0;c<8;c++)
                    {
                        var corner=new Vector3((c&1)==0?mn.x:mx.x,(c&2)==0?mn.y:mx.y,(c&4)==0?mn.z:mx.z);
                        tip=MathF.Max(tip,Vector3.Dot(corner-origin,forward));
                    }
                }
            return text+(float.IsFinite(tip)?$"; foremost geometry {tip:+0.00;-0.00} m along bore from anchor (negative = anchor ahead of the barrel tip; AABB estimate)":"; no renderers under parent");
        }
        catch(Exception ex){return "anchor diagnostic failed: "+ex.GetType().Name;}
    }
    private string EmitFlash(Vector3 at,float scale,float now)
    {
        if(flashes==null||flashes.Length==0)return "off";
        var i=EffectMath.SelectSlot(flashBirths,flashActive);
        var light=flashes[i];if(light==null)return "missing";
        light.transform.position=at;
        flashPeak[i]=EffectMath.FlashPeakCandela*scale*scale;
        flashBirths[i]=now;flashActive[i]=true;
        light.intensity=flashPeak[i]*EffectMath.FlashCurve(.001f);light.enabled=true;
        return $"{flashPeak[i]:F0}cd";
    }
    // Ground below the muzzle: the lowest hit within reach (skips the own hull/
    // turret above the terrain). No hit (cliff edge, airborne) = no dust.
    private static bool FindGround(Vector3 muzzle,float scale,out Vector3 point,out float drop)
    {
        point=Vector3.zero;drop=-1f;
        var hits=Physics.RaycastAll(muzzle,Vector3.down,EffectMath.DustMaxDrop*scale);
        if(hits==null||hits.Length==0)return false;
        for(var h=0;h<hits.Length;h++){var hit=hits[h];if(hit.distance>drop){drop=hit.distance;point=hit.point;}}
        return true;
    }
    private string EmitDust(bool hit,Vector3 point,float best,Vector3 forward,float scale,float seed,float now)
    {
        if(dust==null)return "off";
        if(!hit)return "no-ground";
        var flat=new Vector3(forward.x,0,forward.z);
        if(flat.sqrMagnitude<1e-4f)flat=Vector3.forward;
        var i=EffectMath.SelectSlot(dustBirths,dustActive);
        var v=dust[i];
        dustBirths[i]=now;dustActive[i]=true;
        v.Root.transform.position=point;
        v.Root.transform.rotation=Quaternion.LookRotation(flat.normalized,Vector3.up);
        v.Root.transform.localScale=Vector3.one*scale;
        v.Block.SetFloat("_Age",.001f);
        v.Block.SetFloat("_Seed",seed*1.7f+3.1f);
        v.Block.SetFloat("_Steps",Steps);
        v.Block.SetFloat("_Density",EffectMath.DustDensity*DensityFactor);
        v.Block.SetFloat("_Lifetime",EffectMath.DustLifetime);
        v.Block.SetFloat("_DepthClamp",1f);
        v.Block.SetFloat("_Mode",1f);
        v.Block.SetColor("_DustColor",new Color(EffectMath.DustR,EffectMath.DustG,EffectMath.DustB,1f));
        Box(v.Block,U(EffectMath.DustBoxMin),U(EffectMath.DustBoxMax));
        v.Renderer.SetPropertyBlock(v.Block);
        v.Root.SetActive(true);
        return $"{best:F1}m-below";
    }
    internal void UpdateFrame(float now,Camera? camera)
    {
        if(!Ready||volumes==null)return;
        // B3 measurement: one line per shot at its first driver update after Emit.
        for(var i=0;i<volumes.Length&&i<firstUpdatePending.Length;i++)
        {
            if(!firstUpdatePending[i]||!active[i])continue;
            firstUpdatePending[i]=false;
            if(anchorInfo[i].Length>0)Plugin.Instance.Trace($"Shot timing+anchor slot {i}: emit frame {emitFrame[i]} renders _Age=0.001; first update after {Time.frameCount-emitFrame[i]} frame(s) at age {now-births[i]:F3} s; {anchorInfo[i]}");
        }
        UpdatePool(volumes,births,active,VolumeLifetime,now,camera,"Volume");
        puffs?.UpdateFrame(now,camera);
        if(dust!=null)UpdatePool(dust,dustBirths,dustActive,EffectMath.DustLifetime,now,camera,"Dust");
        if(flashes!=null)
            for(var i=0;i<flashes.Length;i++)
            {
                if(!flashActive[i]||flashes[i]==null)continue;
                var age=now-flashBirths[i];
                var f=EffectMath.FlashCurve(age);
                if(!float.IsFinite(age)||age<0||age>=EffectMath.FlashDuration||f<=0){flashActive[i]=false;flashes[i].intensity=0;flashes[i].enabled=false;continue;}
                flashes[i].intensity=flashPeak[i]*f;
            }
    }
    private void UpdatePool(Volume[] pool,float[] born,bool[] on,float lifetime,float now,Camera? camera,string label)
    {
        var maxDistanceSquared=maxDistance*maxDistance;
        for(var i=0;i<pool.Length;i++)
        {
            if(!on[i])continue;
            var volume=pool[i];
            var age=now-born[i];
            if(!float.IsFinite(age)||age<=0||age>=lifetime)
            {
                on[i]=false;
                volume.Root.SetActive(false);
                Plugin.Instance.Trace($"{label} slot {i} ended: age={age:F2} s reason=lifetime ({lifetime:F1} s)");
                continue;
            }
            if(camera==null)
            {
                if(volume.Root.activeSelf){volume.Root.SetActive(false);Plugin.Instance.Trace($"{label} slot {i} hidden: age={age:F2} s reason=no Camera.main");}
                continue;
            }
            var delta=camera.transform.position-volume.Root.transform.position;
            if(delta.sqrMagnitude>maxDistanceSquared)
            {
                if(volume.Root.activeSelf){volume.Root.SetActive(false);Plugin.Instance.Trace($"{label} slot {i} hidden: age={age:F2} s reason=beyond MaxDistance");}
                continue;
            }
            if(!volume.Root.activeSelf)volume.Root.SetActive(true);
            volume.Block.SetFloat("_Age",MathF.Max(age,.001f));
            volume.Renderer.SetPropertyBlock(volume.Block);
        }
    }
    internal void Clear()
    {
        ClearPool(volumes,active,"Volume");ClearPool(dust,dustActive,"Dust");puffs?.Clear();
        if(flashes!=null)for(var i=0;i<flashes.Length;i++){flashActive[i]=false;if(flashes[i]!=null){flashes[i].intensity=0;flashes[i].enabled=false;}}
    }
    private static void ClearPool(Volume[]? pool,bool[] on,string label)
    {
        if(pool==null)return;
        for(var i=0;i<pool.Length;i++)
        {
            if(on.Length>i&&on[i])Plugin.Instance.Trace($"{label} slot {i} ended: reason=cleared (scene change or Enabled=false)");
            if(on.Length>i)on[i]=false;
            if(pool[i]!=null&&pool[i].Root!=null)pool[i].Root.SetActive(false);
        }
    }
    private void DestroyFlashes()
    {
        if(flashes!=null)foreach(var l in flashes)if(l!=null)Object.Destroy(l.gameObject);
        flashes=null;
    }
    internal void Dispose()
    {
        if(disposed)return;
        disposed=true;
        if(State!=LoadState.Failed)State=LoadState.Idle;
        foreach(var pool in new[]{volumes,dust})
        {
            if(pool==null)continue;
            for(var i=0;i<pool.Length;i++)
            {
                if(pool[i]==null||pool[i].Root==null)continue;
                pool[i].Root.SetActive(false);
                Object.Destroy(pool[i].Root);
            }
        }
        volumes=null;dust=null;
        DestroyFlashes();
        puffs?.Dispose();puffs=null;
        if(muzzleMesh!=null){Object.Destroy(muzzleMesh);muzzleMesh=null;}
        if(dustMesh!=null){Object.Destroy(dustMesh);dustMesh=null;}
        if(material!=null){Object.Destroy(material);material=null;}
        bundleRequest=null;assetRequest=null;
        // An in-flight async load cannot be cancelled; the bundle (if it arrives)
        // is simply never used. A loaded bundle is unloaded with its assets.
        if(bundle!=null){try{bundle.Unload(true);}catch{}bundle=null;}
        births=Array.Empty<float>();
        active=Array.Empty<bool>();
    }
}
