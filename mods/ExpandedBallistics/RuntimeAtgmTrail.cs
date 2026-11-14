using BepInEx.Configuration;
using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using NVector = System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

// Optional mesh/billboard visuals; observes native flight without editing physics.
internal static class RuntimeAtgmTrail
{
    // Keep visual variation independent of Unity's global gameplay RNG.
    private static readonly System.Random VisualRandom=new(521093);
    private static float VisualRange(float min,float max)=>min+(max-min)*(float)VisualRandom.NextDouble();
    private static Vector3 VisualDirection()
    {
        var y=VisualRange(-1f,1f);
        var azimuth=VisualRange(0f,2f*MathF.PI);
        var radius=MathF.Sqrt(Math.Max(0,1f-y*y));
        return new Vector3(radius*MathF.Cos(azimuth),y,radius*MathF.Sin(azimuth));
    }
    // Bounded smoke spans cover observed movement between updates. If the pool
    // fills, recycle the oldest span rather than starving a fresh missile.
    private const int PuffCap=72;
    private const float PuffLifetime=2.5f;
    private const float PuffInterval=.04f;
    private const int ImpactCap=12;
    private const int DustPerImpact=4;
    private const int SparksPerImpact=3;
    private const float ImpactFlashLifetime=.16f;
    private const float ImpactDustLifetime=2f;
    private const float ImpactSparkLifetime=.35f;
    private const float ImpactLifetime=2.2f;
    private static readonly string[] ColorProperties={"_UnlitColor","_BaseColor","_Color"};
    private static readonly string[] TextureProperties={"_UnlitColorMap","_BaseMap","_MainTex"};
    private static readonly (string Name,float Value)[] TransparentProperties=
    {
        ("_SurfaceType",1),("_BlendMode",0),("_ZWrite",0),("_SrcBlend",5),("_DstBlend",10),("_AlphaSrcBlend",1),("_AlphaDstBlend",10)
    };
    private static readonly float[] DustScale={1f,.85f,1.1f,.75f};
    private sealed record Hidden(Renderer Renderer,bool Enabled);
    private sealed record Effect(float Spawn,GameObject Root,GameObject Flame,GameObject Flare,Light Glow,List<Hidden> Native)
    {internal float LastPuff=-100;internal Vector3 LastPuffPosition;internal bool HavePuff;}
    private sealed record Puff(GameObject Root,Material Material)
    {internal float Born,Life;internal AtgmSmokeSpan Span;internal Vector3 Origin,Drift;internal IntPtr Register;internal bool Active;}
    private sealed class Impact
    {
        internal readonly GameObject Root,Flash;
        internal readonly GameObject[] Dust,Sparks;
        internal readonly Vector3[] DustDrift,SparkDrift;
        internal readonly Material FlashMaterial,DustMaterial,SparkMaterial;
        internal float Born,Size;internal IntPtr Register;internal bool Active,ReportFirstFrame;
        internal Impact(GameObject root,GameObject flash,GameObject[] dust,GameObject[] sparks,Vector3[] dustDrift,Vector3[] sparkDrift,
            Material flashMaterial,Material dustMaterial,Material sparkMaterial)
        {Root=root;Flash=flash;Dust=dust;Sparks=sparks;DustDrift=dustDrift;SparkDrift=sparkDrift;FlashMaterial=flashMaterial;DustMaterial=dustMaterial;SparkMaterial=sparkMaterial;}
    }
    private static readonly Dictionary<(IntPtr,int),Effect> Effects=new();
    private static readonly List<Puff> Puffs=new();
    private static readonly List<Impact> Impacts=new();
    private static ConfigEntry<bool> enabled=null!,guidanceFlare=null!,impactVisuals=null!;
    private static ConfigEntry<float> flareSize=null!,flareDistance=null!,flareLifetime=null!;
    private static Shader? unlitShader;
    private static Material? smokeMaterial,flameMaterial,bodyMaterial,finMaterial,flareMaterial;
    private static Material? impactFlashMaterial,impactDustMaterial;
    private static Texture2D? texture,smokeTexture;
    private static Mesh? noseMesh;
    private static bool failed,smokeFailed,impactFailed;
    private static int smokeReports,impactReports;
    internal static void Configure(ConfigFile cfg)
    {
        enabled=cfg.Bind("ATGM Visuals","Enabled",true,"ATGM-only procedural missile, motor flame and short smoke puffs. Exhaust starts at motor ignition; no flight changes.");
        guidanceFlare=cfg.Bind("ATGM Visuals","GuidanceFlare",true,"Show a depth-tested rear guidance flare distinct from motor exhaust. Visual only.");
        flareSize=cfg.Bind("ATGM Visuals","GuidanceFlareSize",.5f,new ConfigDescription("Rear flare base world size at 250 m; grows with distance up to the clamped bound (0.05-3).",new AcceptableValueRange<float>(.05f,3)));
        flareDistance=cfg.Bind("ATGM Visuals","GuidanceFlareMaxDistance",5000f,new ConfigDescription("Hide the guidance flare beyond this camera distance in metres.",new AcceptableValueRange<float>(100f,10000)));
        flareLifetime=cfg.Bind("ATGM Visuals","GuidanceFlareLifetime",45f,new ConfigDescription("Maximum guidance-flare lifetime in seconds.",new AcceptableValueRange<float>(1f,120)));
        impactVisuals=cfg.Bind("ATGM Visuals","ImpactVisuals",true,"Show a brief warm flash and outward dust cloud on the first native contact of a tracked ATGM. Visual only; no damage, collision or extra explosion is added.");
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    private static void Tint(Material material,Color color)
    {foreach(var name in ColorProperties)if(material.HasProperty(name))material.SetColor(name,color);}
    private static Material MakeMaterial(Shader shader,Color color)
    {var result=new Material(shader){hideFlags=HideFlags.HideAndDontSave};Tint(result,color);return result;}
    private static Shader ResolveShader()
    {
        if(unlitShader!=null)return unlitShader;
        unlitShader=Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Color");
        if(unlitShader==null)throw new InvalidOperationException("No available unlit visual shader.");
        return unlitShader;
    }
    private static void SetupTransparent(Material material)
    {
        foreach(var setting in TransparentProperties)if(material.HasProperty(setting.Name))material.SetFloat(setting.Name,setting.Value);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
    }
    private static void AssignTexture(Material material,Texture2D image)
    {
        foreach(var name in TextureProperties)if(material.HasProperty(name))material.SetTexture(name,image);
        material.EnableKeyword("_UNLIT_COLOR_MAP");
    }
    // CC0 images are embedded by the coordinator. A missing or undecodable image
    // only falls back to the generated circle; it never breaks flight or impact.
    private static Texture2D? LoadResourceTexture(string resource)
    {
        Texture2D? image=null;
        try
        {
            using var stream=typeof(RuntimeAtgmTrail).Assembly.GetManifestResourceStream(resource);
            if(stream==null)return null;
            using var memory=new MemoryStream();
            stream.CopyTo(memory);
            if(memory.Length==0)return null;
            image=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp};
            if(!ImageConversion.LoadImage(image,memory.ToArray(),false)){Object.Destroy(image);return null;}
            return image;
        }
        catch(Exception ex)
        {
            if(image!=null)Object.Destroy(image);
            Plugin.ModLog.LogWarning($"[ATGM trail] Resource {resource} unavailable; generated texture fallback retained: {ex.Message}");
            return null;
        }
    }
    private static Texture2D GeneratedTexture()
    {
        if(texture!=null)return texture;
        texture=new Texture2D(32,32){hideFlags=HideFlags.HideAndDontSave};
        var pixels=new Color[1024];
        for(var y=0;y<32;y++)for(var x=0;x<32;x++)
        {var radius=MathF.Sqrt(MathF.Pow((x-15.5f)/15.5f,2)+MathF.Pow((y-15.5f)/15.5f,2));pixels[y*32+x]=new Color(1,1,1,MathF.Pow(Math.Max(0,1-radius),2));}
        texture.SetPixels(pixels);texture.Apply();
        return texture;
    }
    private static void Prepare()
    {
        if(bodyMaterial!=null && noseMesh!=null && flareMaterial!=null && smokeMaterial!=null)return;
        var shader=ResolveShader();
        flameMaterial=MakeMaterial(shader,new Color(18,.08f,.015f,1));
        bodyMaterial=MakeMaterial(shader,new Color(.18f,.22f,.13f,1));
        finMaterial=MakeMaterial(shader,new Color(.09f,.1f,.08f,1));
        smokeMaterial=MakeMaterial(shader,new Color(.68f,.68f,.66f,.42f));
        flareMaterial=MakeMaterial(shader,new Color(1f,.52f,.1f,.9f));
        var fallback=GeneratedTexture();
        // The exhaust smoke uses the supplied CC0 smoke_07; the guidance flare
        // deliberately keeps the original generated circle.
        smokeTexture=LoadResourceTexture("ExpandedBallistics.Vfx.smoke_07.png");
        AssignTexture(smokeMaterial,smokeTexture??fallback);
        AssignTexture(flareMaterial,fallback);
        SetupTransparent(smokeMaterial);
        SetupTransparent(flareMaterial);
        // Explicit LEqual depth test for the persistent flare only; the flare
        // never draws through terrain or hulls.
        if(flareMaterial!.HasProperty("_ZTest"))flareMaterial.SetFloat("_ZTest",4);
        var vertices=new Vector3[14];var triangles=new int[72];vertices[12]=new Vector3(0,0,1);vertices[13]=Vector3.zero;
        for(var i=0;i<12;i++)
        {var a=i*MathF.PI*2/12;vertices[i]=new Vector3(MathF.Cos(a)*.5f,MathF.Sin(a)*.5f,0);var j=(i+1)%12;
         triangles[i*6]=i;triangles[i*6+1]=j;triangles[i*6+2]=12;triangles[i*6+3]=j;triangles[i*6+4]=i;triangles[i*6+5]=13;}
        noseMesh=new Mesh(){hideFlags=HideFlags.HideAndDontSave};noseMesh.vertices=vertices;noseMesh.triangles=triangles;noseMesh.RecalculateNormals();noseMesh.RecalculateBounds();
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[ATGM trail] Missile/smoke materials ready: {shader.name} exhaust={(smokeTexture!=null?"smoke_07":"generated fallback")}");
    }
    private static void PrepareImpact()
    {
        if(impactFlashMaterial!=null && impactDustMaterial!=null)return;
        var shader=ResolveShader();
        var fallback=GeneratedTexture();
        var fire=LoadResourceTexture("ExpandedBallistics.Vfx.fire_01.png");
        var dirt=LoadResourceTexture("ExpandedBallistics.Vfx.dirt_01.png");
        impactFlashMaterial=MakeMaterial(shader,new Color(1f,.6f,.22f,.9f));
        impactDustMaterial=MakeMaterial(shader,new Color(.6f,.53f,.42f,.45f));
        AssignTexture(impactFlashMaterial,fire??fallback);
        AssignTexture(impactDustMaterial,dirt??fallback);
        SetupTransparent(impactFlashMaterial);
        SetupTransparent(impactDustMaterial);
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[ATGM trail] Impact visuals ready: flash={(fire!=null?"fire_01":"generated fallback")} dust={(dirt!=null?"dirt_01":"generated fallback")}");
    }
    private static void SetRenderer(Renderer renderer,Material? material)
    {renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
    private static GameObject Part(PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material? material)
    {
        GameObject? part=null;
        try
        {
            part=GameObject.CreatePrimitive(type);
            var collider=part.GetComponent<Collider>();if(collider!=null){collider.enabled=false;Object.Destroy(collider);}
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
            SetRenderer(part.GetComponent<Renderer>(),material);return part;
        }
        catch{if(part!=null)Object.Destroy(part);throw;}
    }
    private static Effect Create(float spawn,Vector3 position,float calibre,Transform native)
    {
        Prepare();var root=new GameObject("Expanded Ballistics missile");root.transform.position=position;
        var hidden=new List<Hidden>();
        try
        {
            var diameter=Math.Clamp(calibre*.9f,.03f,.35f);var length=diameter*8;
            var body=Part(PrimitiveType.Cylinder,root.transform,Vector3.zero,new Vector3(diameter,length*.5f,diameter),bodyMaterial);
            body.transform.localRotation=Quaternion.Euler(90,0,0);
            var nose=new GameObject("Missile nose");nose.transform.SetParent(root.transform,false);nose.transform.localPosition=new Vector3(0,0,length*.5f);
            nose.transform.localScale=new Vector3(diameter,diameter,diameter*1.7f);nose.AddComponent<MeshFilter>().sharedMesh=noseMesh;SetRenderer(nose.AddComponent<MeshRenderer>(),bodyMaterial);
            for(var i=0;i<4;i++)
            {var a=i*MathF.PI*.5f;var fin=Part(PrimitiveType.Cube,root.transform,new Vector3(MathF.Cos(a)*diameter*.65f,MathF.Sin(a)*diameter*.65f,-length*.33f),new Vector3(diameter*.9f,diameter*.07f,diameter*1.6f),finMaterial);fin.transform.localRotation=Quaternion.Euler(0,0,i*90);}
            var flame=Part(PrimitiveType.Sphere,root.transform,new Vector3(0,0,-length*.5f-diameter),new Vector3(diameter*.7f,diameter*.7f,diameter*1.5f),flameMaterial);flame.SetActive(false);
            // Persistent guidance tail flare; the motor flame above remains ignition-only.
            var flare=Part(PrimitiveType.Quad,root.transform,new Vector3(0,0,-length*.5f-diameter*1.7f),Vector3.one*diameter*.8f,flareMaterial);flare.SetActive(false);
            var glow=flame.AddComponent<Light>();glow.type=LightType.Point;
            glow.color=new Color(1,.025f,.005f);glow.intensity=8;
            glow.range=Math.Clamp(diameter*20,1.5f,5);glow.shadows=LightShadows.None;
            if(native!=null)foreach(var renderer in native.GetComponentsInChildren<MeshRenderer>(true))
            {hidden.Add(new(renderer,renderer.enabled));renderer.enabled=false;}
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo("[ATGM trail] Missile model created; exhaust waits for motor ignition.");
            return new(spawn,root,flame,flare,glow,hidden);
        }
        catch{foreach(var item in hidden)if(item.Renderer!=null)item.Renderer.enabled=item.Enabled;Object.Destroy(root);throw;}
    }
    private static void EmitPuff(IntPtr register,Vector3 from,Vector3 position,float calibre)
    {
        if(!AtgmVfxGeometry.TrySpan(new(from.x,from.y,from.z),new(position.x,position.y,position.z),calibre,out var span)&&
           !AtgmVfxGeometry.TrySpan(new(position.x,position.y,position.z),new(position.x,position.y,position.z),calibre,out span))return;
        // Drop holders destroyed by a scene change so the fixed pool can recover.
        for(var i=Puffs.Count-1;i>=0;i--)if(Puffs[i].Root==null){Object.Destroy(Puffs[i].Material);Puffs.RemoveAt(i);}
        Puff? puff=null;
        for(var i=0;i<Puffs.Count;i++)if(!Puffs[i].Active && Puffs[i].Root!=null){puff=Puffs[i];break;}
        if(puff==null&&Puffs.Count>=PuffCap)
        {
            for(var i=0;i<Puffs.Count;i++)if(puff==null||Puffs[i].Born<puff.Born)puff=Puffs[i];
        }
        if(puff==null)
        {
            if(Puffs.Count>=PuffCap)return;
            GameObject? holder=null;Material? material=null;
            try
            {
                holder=new GameObject("ATGM smoke puff holder");
                var quad=Part(PrimitiveType.Quad,holder.transform,Vector3.zero,Vector3.one,smokeMaterial);
                material=new Material(smokeMaterial!){hideFlags=HideFlags.HideAndDontSave};
                quad.GetComponent<Renderer>().sharedMaterial=material;
                puff=new(holder,material);Puffs.Add(puff);
            }
            catch
            {
                if(holder!=null)Object.Destroy(holder);
                if(material!=null)Object.Destroy(material);
                throw;
            }
        }
        puff.Register=register;puff.Born=Time.time;puff.Life=PuffLifetime*VisualRange(.8f,1.15f);
        puff.Span=span;puff.Origin=new Vector3(span.Centre.X,span.Centre.Y,span.Centre.Z);
        puff.Drift=Vector3.up*VisualRange(.18f,.38f)+new Vector3(VisualRange(-.1f,.1f),0,VisualRange(-.1f,.1f));
        puff.Active=true;puff.Root.SetActive(true);
        if(ReleaseDiagnostics.Enabled&&smokeReports++<12)
            Plugin.ModLog.LogInfo($"[ATGM VFX] SMOKE span={span.Length:0.00}m width={span.Width:0.00}m life={puff.Life:0.00}s owner={register}; world-space connected smoke_07");
    }
    private static void Face(Transform transform,Camera? camera,float spin=0)
    {
        if(camera==null)return;
        var away=transform.position-camera.transform.position;
        if(!Finite(away)||away.sqrMagnitude<=1e-6f)return;
        var up=MathF.Abs(away.normalized.y)>.98f?new Vector3(1,0,0):Vector3.up;
        transform.rotation=Quaternion.LookRotation(away,up);
        if(spin!=0)transform.rotation*=Quaternion.Euler(0,0,spin);
    }
    private static void UpdatePuffs()
    {
        if(Puffs.Count==0)return;
        var camera=Camera.main;var now=Time.time;
        for(var i=0;i<Puffs.Count;i++)
        {
            var puff=Puffs[i];if(!puff.Active)continue;
            if(puff.Root==null){puff.Active=false;continue;}
            var age=now-puff.Born;
            if(!float.IsFinite(age)||age<0||puff.Life<=0||age>=puff.Life){puff.Active=false;puff.Root.SetActive(false);continue;}
            var fraction=age/puff.Life;
            puff.Root.transform.position=puff.Origin+puff.Drift*age;
            if(camera!=null)
            {
                var point=puff.Root.transform.position;var eye=camera.transform.position;
                if(AtgmVfxGeometry.TryView(puff.Span,new(point.x,point.y,point.z),new(eye.x,eye.y,eye.z),fraction,out var view))
                {
                    puff.Root.transform.rotation=Quaternion.LookRotation(new Vector3(view.Facing.X,view.Facing.Y,view.Facing.Z),new Vector3(view.Up.X,view.Up.Y,view.Up.Z));
                    puff.Root.transform.localScale=new Vector3(view.Width,view.Length,1);
                }
            }
            var fade=1-fraction;fade*=fade;
            Tint(puff.Material,new Color(.68f,.68f,.66f,.42f*fade));
        }
    }
    internal static void SpawnImpact(IntPtr register,Vector3 position,float diameter)
    {
        try
        {
            if(impactFailed || !RuntimeAtgm.Ready || !RuntimeAtgm.Enabled)return;
            if(impactVisuals!=null && !impactVisuals.Value)return;
            if(register==IntPtr.Zero || !Finite(position))return;
            PrepareImpact();
            var impact=AcquireImpact();
            if(impact==null)return;
            impact.Register=register;impact.Born=Time.time;impact.Size=ImpactDiameter(diameter);impact.Active=true;
            impact.ReportFirstFrame=ReleaseDiagnostics.Enabled&&impactReports++<12;
            if(impact.ReportFirstFrame)Plugin.ModLog.LogInfo($"[ATGM VFX] IMPACT SPAWN at={position} diameter={impact.Size:0.000}m owner={register}; flash=fire_01 dust=dirt_01");
            impact.Root.transform.position=position;impact.Root.SetActive(true);
            impact.Flash.SetActive(true);
            for(var i=0;i<impact.Dust.Length;i++)
            {
                var dust=impact.Dust[i];if(dust==null)continue;
                dust.SetActive(true);dust.transform.localPosition=Vector3.zero;
                var angle=VisualRange(0f,MathF.PI*2f);
                impact.DustDrift[i]=new Vector3(MathF.Cos(angle)*VisualRange(.15f,.45f),VisualRange(.12f,.35f),MathF.Sin(angle)*VisualRange(.15f,.45f));
            }
            for(var i=0;i<impact.Sparks.Length;i++)
            {
                var spark=impact.Sparks[i];if(spark==null)continue;
                spark.SetActive(true);spark.transform.localPosition=Vector3.zero;
                var direction=VisualDirection();direction.y=MathF.Abs(direction.y)*.8f+.35f;
                impact.SparkDrift[i]=direction.normalized*VisualRange(2.5f,5.5f);
            }
        }
        catch(Exception ex){impactFailed=true;ClearImpacts();Plugin.ModLog.LogWarning("[ATGM trail] Impact visuals disabled; native impact unchanged: "+ex);}
    }
    // Definition diameters are metre-scaled in the current pool; tolerate a
    // millimetre-shaped value defensively and always return a finite bound.
    private static float ImpactDiameter(float diameter)
    {
        if(!float.IsFinite(diameter)||diameter<=0)return .12f;
        if(diameter>2f)diameter*=.001f;
        return Math.Clamp(diameter,.03f,.45f);
    }
    private static Impact? AcquireImpact()
    {
        // Drop holders destroyed by a scene change so the fixed pool can recover.
        for(var i=Impacts.Count-1;i>=0;i--)if(Impacts[i].Root==null)
        {
            if(Impacts[i].FlashMaterial!=null)Object.Destroy(Impacts[i].FlashMaterial);
            if(Impacts[i].DustMaterial!=null)Object.Destroy(Impacts[i].DustMaterial);
            if(Impacts[i].SparkMaterial!=null)Object.Destroy(Impacts[i].SparkMaterial);
            Impacts.RemoveAt(i);
        }
        for(var i=0;i<Impacts.Count;i++)if(!Impacts[i].Active && Impacts[i].Root!=null)return Impacts[i];
        if(Impacts.Count>=ImpactCap)return null;
        var root=new GameObject("ATGM impact");
        Material? flashMaterial=null,dustMaterial=null,sparkMaterial=null;
        try
        {
            flashMaterial=new Material(impactFlashMaterial!){hideFlags=HideFlags.HideAndDontSave};
            dustMaterial=new Material(impactDustMaterial!){hideFlags=HideFlags.HideAndDontSave};
            sparkMaterial=new Material(impactFlashMaterial!){hideFlags=HideFlags.HideAndDontSave};
            var flash=Part(PrimitiveType.Quad,root.transform,Vector3.zero,Vector3.one,flashMaterial);
            var dust=new GameObject[DustPerImpact];var dustDrift=new Vector3[DustPerImpact];
            for(var i=0;i<dust.Length;i++)dust[i]=Part(PrimitiveType.Quad,root.transform,Vector3.zero,Vector3.one,dustMaterial);
            var sparks=new GameObject[SparksPerImpact];var sparkDrift=new Vector3[SparksPerImpact];
            for(var i=0;i<sparks.Length;i++)sparks[i]=Part(PrimitiveType.Quad,root.transform,Vector3.zero,Vector3.one,sparkMaterial);
            var impact=new Impact(root,flash,dust,sparks,dustDrift,sparkDrift,flashMaterial,dustMaterial,sparkMaterial);
            Impacts.Add(impact);return impact;
        }
        catch
        {
            if(root!=null)Object.Destroy(root);
            if(flashMaterial!=null)Object.Destroy(flashMaterial);
            if(dustMaterial!=null)Object.Destroy(dustMaterial);
            if(sparkMaterial!=null)Object.Destroy(sparkMaterial);
            throw;
        }
    }
    private static void UpdateImpacts()
    {
        if(Impacts.Count==0)return;
        var camera=Camera.main;var now=Time.time;
        for(var i=0;i<Impacts.Count;i++)
        {
            var impact=Impacts[i];if(!impact.Active)continue;
            if(impact.Root==null){impact.Active=false;continue;}
            var age=now-impact.Born;
            if(!float.IsFinite(age)||age<0||age>=ImpactLifetime){impact.Active=false;impact.Root.SetActive(false);continue;}
            if(age<ImpactFlashLifetime)
            {
                if(!impact.Flash.activeSelf)impact.Flash.SetActive(true);
                var fraction=age/ImpactFlashLifetime;var scale=AtgmVfxGeometry.ImpactFlash(impact.Size,fraction);
                impact.Flash.transform.localScale=new Vector3(scale,scale,scale);
                Face(impact.Flash.transform,camera);
                Tint(impact.FlashMaterial,new Color(3f,1.4f,.35f,.9f*(1-fraction)));
            }
            else if(impact.Flash.activeSelf)impact.Flash.SetActive(false);
            var dustFraction=age/ImpactDustLifetime;
            if(dustFraction<1f)
            {
                Tint(impact.DustMaterial,new Color(.6f,.53f,.42f,.42f*(1f-dustFraction)));
                for(var d=0;d<impact.Dust.Length;d++)
                {
                    var dust=impact.Dust[d];if(dust==null)continue;
                    var localAge=age-d*.04f;
                    if(localAge<0){if(dust.activeSelf)dust.SetActive(false);continue;}
                    var fraction=Mathf.Clamp01(localAge/ImpactDustLifetime);
                    if(fraction>=1f){if(dust.activeSelf)dust.SetActive(false);continue;}
                    if(!dust.activeSelf)dust.SetActive(true);
                    dust.transform.localPosition=impact.DustDrift[d]*localAge+Vector3.up*(localAge*.18f);
                    var scale=AtgmVfxGeometry.ImpactDust(impact.Size,fraction)*DustScale[d];
                    dust.transform.localScale=new Vector3(scale,scale,scale);
                    Face(dust.transform,camera,d*67f);
                }
            }
            else
            {
                for(var d=0;d<impact.Dust.Length;d++)if(impact.Dust[d]!=null&&impact.Dust[d].activeSelf)impact.Dust[d].SetActive(false);
            }
            if(age<ImpactSparkLifetime)
            {
                var fraction=age/ImpactSparkLifetime;
                Tint(impact.SparkMaterial,new Color(1f,.55f,.18f,.55f*(1f-fraction)));
                for(var s=0;s<impact.Sparks.Length;s++)
                {
                    var spark=impact.Sparks[s];if(spark==null)continue;
                    if(!spark.activeSelf)spark.SetActive(true);
                    spark.transform.localPosition=impact.SparkDrift[s]*age+Vector3.down*(1.5f*age*age);
                    var scale=impact.Size*(.18f*(1f-fraction))+.004f;
                    spark.transform.localScale=new Vector3(scale,scale*2.2f,scale);
                    Face(spark.transform,camera,s*40f);
                }
            }
            else
            {
                for(var s=0;s<impact.Sparks.Length;s++)if(impact.Sparks[s]!=null&&impact.Sparks[s].activeSelf)impact.Sparks[s].SetActive(false);
            }
            if(impact.ReportFirstFrame)
            {
                impact.ReportFirstFrame=false;
                Plugin.ModLog.LogInfo($"[ATGM VFX] IMPACT TICK age={age:0.000}s flashActive={impact.Flash.activeSelf} flashScale={impact.Flash.transform.localScale} dust0Scale={impact.Dust[0].transform.localScale} camera={(camera!=null)}");
            }
        }
    }
    private static void UpdatePuffsSafe()
    {
        if(failed || smokeFailed)return;
        try{UpdatePuffs();}
        catch(Exception ex){DisableSmoke(ex);}
    }
    private static void DisableSmoke(Exception ex)
    {
        smokeFailed=true;
        try{ClearPuffs();}catch(Exception cleanup){Plugin.ModLog.LogWarning("[ATGM trail] Smoke cleanup failed: "+cleanup.Message);}
        Plugin.ModLog.LogWarning("[ATGM trail] Smoke visuals disabled; missile and flare remain active: "+ex.Message);
    }
    private static void EmitPuffSafe(IntPtr register,Vector3 from,Vector3 position,float calibre)
    {
        if(smokeFailed)return;
        try{EmitPuff(register,from,position,calibre);}
        catch(Exception ex){DisableSmoke(ex);}
    }
    private static void UpdateImpactsSafe()
    {
        if(impactFailed)return;
        try{UpdateImpacts();}
        catch(Exception ex)
        {
            impactFailed=true;
            try{ClearImpacts();}catch(Exception cleanup){Plugin.ModLog.LogWarning("[ATGM trail] Impact visual cleanup failed: "+cleanup.Message);}
            Plugin.ModLog.LogWarning("[ATGM trail] Impact visuals disabled; native impact unchanged: "+ex.Message);
        }
    }
    [HarmonyPostfix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.FixedUpdate))]
    private static void UpdateVisuals(ProjectileRegister __instance)
    {
        // Most battle ticks have no missiles or lingering visual objects. Avoid
        // allocating a set and enumerators on every native physics update.
        if(!RuntimeAtgm.HasFlights&&Effects.Count==0&&Puffs.Count==0&&Impacts.Count==0)return;
        if(failed || !enabled.Value || !RuntimeAtgm.Ready || !RuntimeAtgm.Enabled){ClearRegister(__instance.Pointer);UpdatePuffsSafe();UpdateImpactsSafe();return;}
        // Lingering puffs and impacts outlive flight release; tick them without
        // rebuilding the flight-tracking set when no missile effect remains.
        if(!RuntimeAtgm.HasFlights&&Effects.Count==0){UpdatePuffsSafe();UpdateImpactsSafe();return;}
        try
        {
            var live=new HashSet<(IntPtr,int)>();
            var camera=Camera.main;
            var policy=new AtgmTracerSettings(guidanceFlare.Value,flareSize.Value,.12,2.5,flareDistance.Value,flareLifetime.Value);
            foreach(var flight in RuntimeAtgm.VisualFlightDetails(__instance))
            {
                var key=(__instance.Pointer,flight.Id);live.Add(key);
                if(Effects.TryGetValue(key,out var old) && (old.Spawn!=flight.Spawn || old.Root==null))Remove(key);
                if(!Effects.TryGetValue(key,out var effect))Effects[key]=effect=Create(flight.Spawn,flight.Position,flight.Calibre,flight.Native);
                effect.Root.transform.position=flight.Position;
                if(flight.Direction.sqrMagnitude>.1f)effect.Root.transform.rotation=Quaternion.LookRotation(flight.Direction);
                effect.Flame.SetActive(flight.Burning);
                var showFlare=false;var flareScale=0d;
                if(camera!=null && flight.FlareEligible)
                {
                    var distance=Vector3.Distance(camera.transform.position,flight.Position);
                    var rear=AtgmTracerPolicy.RearFactor(new NVector(camera.transform.position.x,camera.transform.position.y,camera.transform.position.z),
                        new NVector(flight.Position.x,flight.Position.y,flight.Position.z),
                        new NVector(flight.Direction.x,flight.Direction.y,flight.Direction.z));
                    var frame=AtgmTracerPolicy.Evaluate(true,flight.Age,distance,policy,rear);
                    showFlare=frame.Visible;flareScale=frame.SizeM;
                }
                effect.Flare.SetActive(showFlare);
                if(showFlare && camera!=null)
                {
                    effect.Flare.transform.localScale=Vector3.one*(float)flareScale;
                    var away=effect.Flare.transform.position-camera.transform.position;
                    if(away.sqrMagnitude>1e-6f)effect.Flare.transform.rotation=Quaternion.LookRotation(away);
                }
                if(flight.Burning && Time.time-effect.LastPuff>=PuffInterval)
                {
                    var nozzle=flight.Position-flight.Direction*flight.Calibre*4.5f;
                    effect.LastPuff=Time.time;
                    EmitPuffSafe(__instance.Pointer,effect.HavePuff?effect.LastPuffPosition:nozzle,nozzle,flight.Calibre);
                    effect.LastPuffPosition=nozzle;effect.HavePuff=true;
                }
            }
            foreach(var key in Effects.Keys.Where(k=>k.Item1==__instance.Pointer && !live.Contains(k)).ToArray())Remove(key);
            UpdatePuffsSafe();
        }
        catch(Exception ex){failed=true;ClearTrail();Plugin.ModLog.LogWarning("[ATGM trail] Visuals disabled; flight unchanged: "+ex);}
        UpdateImpactsSafe();
    }
    private static void Remove((IntPtr,int) key)
    {
        if(!Effects.Remove(key,out var effect))return;
        foreach(var item in effect.Native)if(item.Renderer!=null)item.Renderer.enabled=item.Enabled;
        if(effect.Root!=null)Object.Destroy(effect.Root);
    }
    private static void ClearRegister(IntPtr register){foreach(var key in Effects.Keys.Where(k=>k.Item1==register).ToArray())Remove(key);}
    private static void ClearPuffs()
    {foreach(var puff in Puffs){if(puff.Root!=null)Object.Destroy(puff.Root);if(puff.Material!=null)Object.Destroy(puff.Material);}Puffs.Clear();}
    private static void ClearTrail()
    {foreach(var key in Effects.Keys.ToArray())Remove(key);ClearPuffs();}
    private static void ClearImpacts()
    {
        foreach(var impact in Impacts)
        {
            if(impact.Root!=null)Object.Destroy(impact.Root);
            if(impact.FlashMaterial!=null)Object.Destroy(impact.FlashMaterial);
            if(impact.DustMaterial!=null)Object.Destroy(impact.DustMaterial);
            if(impact.SparkMaterial!=null)Object.Destroy(impact.SparkMaterial);
        }
        Impacts.Clear();
    }
    private static void ClearOwnedVisuals(IntPtr register)
    {
        ClearRegister(register);
        for(var i=Puffs.Count-1;i>=0;i--)if(Puffs[i].Register==register)
        {
            var puff=Puffs[i];if(puff.Root!=null)Object.Destroy(puff.Root);if(puff.Material!=null)Object.Destroy(puff.Material);Puffs.RemoveAt(i);
        }
        for(var i=Impacts.Count-1;i>=0;i--)if(Impacts[i].Register==register)
        {
            var impact=Impacts[i];if(impact.Root!=null)Object.Destroy(impact.Root);
            if(impact.FlashMaterial!=null)Object.Destroy(impact.FlashMaterial);
            if(impact.DustMaterial!=null)Object.Destroy(impact.DustMaterial);
            if(impact.SparkMaterial!=null)Object.Destroy(impact.SparkMaterial);
            Impacts.RemoveAt(i);
        }
    }
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.Release))]
    private static void Release(ProjectileRegister __instance,int __0)=>Remove((__instance.Pointer,__0));
    [HarmonyPrefix,HarmonyPatch(typeof(ProjectileRegister),nameof(ProjectileRegister.DestroyAll))]
    private static void Reset(ProjectileRegister __instance)
    {
        try{ClearOwnedVisuals(__instance.Pointer);}
        catch(Exception ex){Plugin.ModLog.LogWarning("[ATGM trail] Register visual cleanup failed: "+ex.Message);}
    }
}
