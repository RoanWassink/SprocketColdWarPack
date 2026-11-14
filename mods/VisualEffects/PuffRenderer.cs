using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
using NV=System.Numerics.Vector3;
namespace SprocketVisualEffects;

// 0.3.0 lit six-way flipbook puffs (smoke + ground dust) in ONE dynamic mesh:
// one draw call, quads written back-to-front every frame (camera distance), so
// dust and smoke are one correctly sorted layer. Motion is closed-form (PuffMath);
// the vertex shader (SprocketVfx/SixWayPuff) billboards and relights.
// Vertex layout (must match SixWayPuff.shader and VfxProofS2.cs):
//   vertices=centre, uv=corner(-1..1), uv2=(quad m, rotation), uv3=(frame, slice),
//   uv4=(alpha, heat), colors=(tint rgb, dissolve).
internal sealed class PuffRenderer
{
    private readonly Puff[] pool;
    private int next;
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly Il2CppStructArray<Vector3> verts;
    private readonly Il2CppStructArray<Vector2> uv0,uv1,uv2,uv3;
    private readonly Il2CppStructArray<Color> cols;
    private readonly float[] keys;
    private readonly int[] order;
    private readonly System.Random rng=new(1234);
    private bool meshDirty=true,disposed;
    private float nextSunSearch;
    private Light? sun;
    internal int Capacity=>pool.Length;
    internal NV Wind{get;set;}
    internal float Opacity{get;set;}=1f;
    internal PuffTuning Tuning{get;set;}=PuffTuning.Default;
    private string loggedSun="";
    internal string SunSource{get;private set;}="fallback";
    internal int LastLive{get;private set;}
    internal PuffRenderer(Transform parent,Material source,int capacity)
    {
        var n=EffectMath.PuffCapacity(capacity);
        pool=new Puff[n];
        for(var i=0;i<n;i++)pool[i].Life=0;                       // all slots dead
        material=new Material(source){hideFlags=HideFlags.HideAndDontSave,renderQueue=3001};
        root=new GameObject("Visual effects puffs"){hideFlags=HideFlags.HideAndDontSave};
        root.transform.SetParent(parent,false);
        mesh=new Mesh{hideFlags=HideFlags.HideAndDontSave};mesh.MarkDynamic();
        verts=new Il2CppStructArray<Vector3>(n*4);
        uv0=new Il2CppStructArray<Vector2>(n*4);uv1=new Il2CppStructArray<Vector2>(n*4);
        uv2=new Il2CppStructArray<Vector2>(n*4);uv3=new Il2CppStructArray<Vector2>(n*4);
        cols=new Il2CppStructArray<Color>(n*4);
        var tri=new Il2CppStructArray<int>(n*6);
        for(var k=0;k<n;k++)
        {
            uv0[k*4]=new Vector2(-1,-1);uv0[k*4+1]=new Vector2(1,-1);uv0[k*4+2]=new Vector2(1,1);uv0[k*4+3]=new Vector2(-1,1);
            int b=k*4,o=k*6;tri[o]=b;tri[o+1]=b+2;tri[o+2]=b+1;tri[o+3]=b;tri[o+4]=b+3;tri[o+5]=b+2;
        }
        mesh.vertices=verts;mesh.uv=uv0;mesh.uv2=uv1;mesh.uv3=uv2;mesh.uv4=uv3;mesh.colors=cols;mesh.triangles=tri;
        keys=new float[n];order=new int[n];
        root.AddComponent<MeshFilter>().sharedMesh=mesh;
        var r=root.AddComponent<MeshRenderer>();r.sharedMaterial=material;
        r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
        r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;r.allowOcclusionWhenDynamic=false;
        root.SetActive(false);
    }
    // Spawns one shot (smoke out of the muzzle, dust ring at the ground point if any).
    internal string Emit(Vector3 muzzle,Vector3 forward,Vector3 up,float scale,Vector3? ground,float cameraDistance)
    {
        if(disposed)return "off";
        var now=Time.time;
        var lod=cameraDistance>500?.25f:cameraDistance>150?.5f:1f;   // distance LOD
        Span<Puff> tmp=stackalloc Puff[64];
        var t=Tuning;
        PuffMath.SpawnSmoke(rng,N(muzzle),N(forward),N(up),scale,now,t,tmp,out var smoke);
        var keepSmoke=smoke==0?0:Math.Max(2,(int)(smoke*lod));
        for(var i=0;i<keepSmoke;i++)Put(tmp[i]);
        var dustCount=0;
        if(ground.HasValue&&lod>=.5f)
        {
            PuffMath.SpawnDust(rng,N(ground.Value),N(forward),scale,now,t,tmp,out dustCount);
            dustCount=(int)(dustCount*lod);
            for(var i=0;i<dustCount;i++)Put(tmp[i]);
        }
        root.SetActive(true);
        return $"puffs smoke={keepSmoke} dust={dustCount} lod=x{lod:F2}";
    }
    private void Put(in Puff p){pool[next]=p;next=(next+1)%pool.Length;}       // ring: oldest recycled first
    internal void UpdateFrame(float now,Camera? camera)
    {
        if(disposed||!root.activeSelf)return;
        if(camera==null){root.SetActive(false);return;}
        var cam=camera.transform.position;
        var up=NV.UnitY;var live=0;
        for(var i=0;i<pool.Length;i++)
        {
            if(pool[i].Life<=0)continue;
            var age=now-pool[i].Born;
            if(age>=pool[i].Life){pool[i].Life=0;continue;}
            if(age<=0)continue;
            var c=PuffMath.State(pool[i],now,Wind,up);if(c==null)continue;
            var d=(U(c.Value.Center)-cam).sqrMagnitude;
            keys[live]=-d;order[live]=i;live++;                     // far first
        }
        LastLive=live;
        if(live==0)
        {
            if(meshDirty){Clear(false);meshDirty=false;}
            root.SetActive(false);return;
        }
        Array.Sort(keys,order,0,live);
        UpdateSun(now);
        for(var k=0;k<pool.Length;k++)
        {
            var b=k*4;
            if(k<live)
            {
                ref var p=ref pool[order[k]];
                var s=PuffMath.State(p,now,Wind,up)!.Value;
                var centre=U(s.Center);
                var br=Tuning.SmokeBrightness;
                var tint=p.Dust?new Color(EffectMath.PuffDustR,EffectMath.PuffDustG,EffectMath.PuffDustB,s.Dissolve)
                               :new Color(EffectMath.PuffSmokeR*br,EffectMath.PuffSmokeG*br,EffectMath.PuffSmokeB*br,s.Dissolve);
                var a=Math.Clamp(s.Alpha*Opacity,0,1);
                for(var c=0;c<4;c++)
                {
                    verts[b+c]=centre;uv1[b+c]=new Vector2(s.Quad,s.Rotation);uv2[b+c]=new Vector2(s.Frame,p.Slice);
                    uv3[b+c]=new Vector2(a,s.Heat);cols[b+c]=tint;
                }
            }
            else for(var c=0;c<4;c++){uv3[b+c]=Vector2.zero;verts[b+c]=cam;}   // dead slot: collapsed, invisible
        }
        mesh.vertices=verts;mesh.uv2=uv1;mesh.uv3=uv2;mesh.uv4=uv3;mesh.colors=cols;
        mesh.bounds=new Bounds(cam,Vector3.one*6000);              // vertex shader expands quads; never cull
        meshDirty=true;
    }
    // Main directional light: RenderSettings.sun, else the brightest active directional
    // light (searched at most every 3 s); safe fixed fallback otherwise.
    private void UpdateSun(float now)
    {
        try
        {
            if(sun==null||!sun.isActiveAndEnabled||now>=nextSunSearch)
            {
                nextSunSearch=now+3f;
                var s=RenderSettings.sun;
                if(s!=null&&s.isActiveAndEnabled&&s.type==LightType.Directional){sun=s;SunSource="RenderSettings.sun";}
                else
                {
                    Light? best=null;
                    var all=Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                    if(all!=null)for(var i=0;i<all.Length;i++){var l=all[i];if(l!=null&&l.isActiveAndEnabled&&l.type==LightType.Directional&&(best==null||l.intensity>best.intensity))best=l;}
                    sun=best;SunSource=best!=null?"brightest directional '"+best.name+"'":"fallback";
                }
            }
            if(sun!=null)
            {
                var dir=-sun.transform.forward;
                var strength=EffectMath.PuffSunStrength(sun.intensity,dir.y);
                var c=sun.color;
                material.SetVector("_SunDir",new Vector4(dir.x,dir.y,dir.z,0));
                material.SetColor("_SunColor",new Color(c.r*strength,c.g*strength,c.b*strength,1));
                var amb=EffectMath.PuffAmbient(strength);
                // Log the chosen sun once per change of source (0.3.0 open point).
                material.SetFloat("_SunLux",sun.intensity);
                if(SunSource!=loggedSun){loggedSun=SunSource;Plugin.Instance.Log.LogInfo($"Puff sun: {SunSource} lux={sun.intensity:F0} colour=({c.r:F2},{c.g:F2},{c.b:F2}) towardsSunY={dir.y:F2} -> display strength {strength:F2}, ambient {amb:F2}, capped by lux x exposure in the shader");}
                material.SetColor("_Ambient",new Color(amb*.92f,amb*.97f,amb*1.08f,1));
                return;
            }
        }
        catch(Exception ex){if(SunSource!="error")Plugin.Instance.Trace("Puff sun lookup failed: "+ex.GetType().Name+" (fixed fallback)");SunSource="error";sun=null;}
        if(loggedSun!="fallback"){loggedSun="fallback";Plugin.Instance.Log.LogInfo("Puff sun: fallback (no active directional light found); fixed direction, strength 0.90");}
        material.SetFloat("_SunLux",0);material.SetVector("_SunDir",new Vector4(-.4f,.75f,.5f,0));material.SetColor("_SunColor",new Color(.9f,.86f,.8f,1));material.SetColor("_Ambient",new Color(.30f,.32f,.36f,1));
    }
    internal void Clear(bool killPuffs=true)
    {
        if(disposed)return;
        if(killPuffs)for(var i=0;i<pool.Length;i++)pool[i].Life=0;
        for(var i=0;i<uv3.Length;i++)uv3[i]=Vector2.zero;
        mesh.uv4=uv3;
        root.SetActive(false);
    }
    internal void Dispose()
    {
        if(disposed)return;disposed=true;
        if(root!=null)Object.Destroy(root);
        if(mesh!=null)Object.Destroy(mesh);
        if(material!=null)Object.Destroy(material);
    }
    private static NV N(Vector3 v)=>new(v.x,v.y,v.z);
    private static Vector3 U(NV v)=>new(v.X,v.Y,v.Z);
}
