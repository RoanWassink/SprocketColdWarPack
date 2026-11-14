using System.Text.Json;
using UnityEngine;
using Object=UnityEngine.Object;
namespace SprocketExpandedBallistics;

// All Texture2D allocation and pixel conversion is confined to this startup loader.
internal static class RuntimeCustomTextures
{
    internal const string ConfigName="sprocket.expandedballistics.material-looks.json";
    private static readonly Dictionary<string,CustomTextureSet<Texture2D>> sets=new();
    private static readonly List<Texture2D> owned=new();
    private static bool initialized;
    internal static void Initialize()
    {
        if(initialized)return;initialized=true;
        var settings=CustomTextureSettings.Defaults();
        var path=Path.Combine(BepInEx.Paths.ConfigPath,ConfigName);
        try
        {
            if(File.Exists(path))
            {
                var user=JsonSerializer.Deserialize<Dictionary<string,CustomTextureSettings>>(File.ReadAllText(path))??throw new InvalidDataException("Empty config");
                // Missing material sections retain defaults; each present section is validated by its scan.
                foreach(var pair in user)if(settings.ContainsKey(pair.Key))settings[pair.Key]=pair.Value;
            }
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Custom textures] "+ex.Message+"; native looks retained.");return;}
        var scan=new CustomTextureScan<Texture2D>();
        foreach(var pair in settings)
        {
            var set=scan.Read(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,"material-looks",pair.Key),pair.Value,Decode,Destroy,
                text=>Plugin.ModLog.LogWarning("[Custom textures] "+text));
            if(set==null)continue;
            try
            {
                if(set.Normal!=null)
                {
                    var pixels=set.Normal.GetPixels32(0);
                    for(int i=0;i<pixels.Length;i++){var p=pixels[i];p.g=CustomTexturePixels.NormalY(p.g,set.Settings.DirectX);p.a=255;pixels[i]=p;}
                    set.Normal.SetPixels32(pixels,0);set.Normal.Apply(true,false);
                }
                owned.Add(set.Albedo);if(set.Normal!=null)owned.Add(set.Normal);if(set.Roughness!=null)owned.Add(set.Roughness);if(set.Ao!=null)owned.Add(set.Ao);
                sets.Add("eb"+char.ToUpperInvariant(pair.Key[0])+pair.Key.Substring(1),set);
                Plugin.ModLog.LogInfo("[Custom textures] "+pair.Key+": loaded once; repeat tile "+set.Settings.tileSizeM+" m; "+set.Settings.normalConvention+" normal.");
            }
            catch(Exception ex)
            {
                foreach(var tex in new[]{set.Albedo,set.Normal,set.Roughness,set.Ao})if(tex!=null){owned.Remove(tex);Destroy(tex);}
                Plugin.ModLog.LogWarning("[Custom textures] "+pair.Key+": "+ex.Message+"; native look retained.");
            }
        }
    }
    internal static CustomTextureSet<Texture2D>? For(string id)=>sets.TryGetValue(id,out var set)?set:null;
    private static Texture2D Decode(string path,bool linear)
    {
        var info=new FileInfo(path);if(info.Length>32*1024*1024)throw new InvalidDataException("PNG exceeds 32 MiB");
        var bytes=File.ReadAllBytes(path);var size=CustomTexturePixels.Dimensions(bytes);
        // RGBA32 keeps uploaded PNG channels unchanged; RGB normals use R/G (alpha forced to 1).
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){hideFlags=HideFlags.HideAndDontSave,name=Path.GetFileName(path)};
        try
        {
            if(!ImageConversion.LoadImage(texture,bytes,false)||texture.width!=size.Width||texture.height!=size.Height)throw new InvalidDataException("PNG decode failed");
            Configure(texture);texture.Apply(true,false);return texture;
        }
        catch{Destroy(texture);throw;}
    }
    private static void Configure(Texture2D texture){texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=4;}
    internal static void Apply(Material material,CustomTextureSet<Texture2D> set)
    {
        var s=set.Settings;
        material.SetTexture("_BaseColorMap",set.Albedo);material.SetTextureScale("_BaseColorMap",Vector2.one);material.SetTextureOffset("_BaseColorMap",Vector2.zero);
        material.SetColor("_BaseColor",new Color(s.tint[0],s.tint[1],s.tint[2],s.tint[3]));material.SetFloat("_Smoothness",s.smoothness);
        if(set.Normal!=null){material.SetTexture("_NormalMap",set.Normal);material.SetFloat("_NormalScale",s.normalStrength);}
        if(set.Roughness!=null||set.Ao!=null)
        {
            var reference=set.Roughness??set.Ao!;int w=reference.width,h=reference.height;
            var rough=set.Roughness?.GetPixels32(0);var ao=set.Ao?.GetPixels32(0);
            var packed=new Color32[w*h];
            byte Sample(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> p,Texture2D t,int x,int y)=>p[(y*t.height/h)*t.width+x*t.width/w].r;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                byte r=rough==null?(byte)(255-Math.Clamp((int)MathF.Round(s.smoothness*255),0,255)):Sample(rough,set.Roughness!,x,y);
                var p=CustomTexturePixels.Mask(r,ao==null?(byte)255:Sample(ao,set.Ao!,x,y));packed[y*w+x]=new Color32(p.R,p.G,p.B,p.A);
            }
            var mask=new Texture2D(w,h,TextureFormat.RGBA32,true,true){hideFlags=HideFlags.HideAndDontSave,name="EB HDRP mask"};
            try{mask.SetPixels32(packed,0);Configure(mask);mask.Apply(true,false);owned.Add(mask);material.SetTexture("_MaskMap",mask);}
            catch{Destroy(mask);throw;}
            material.SetFloat("_AORemapMin",0);material.SetFloat("_AORemapMax",1);material.SetFloat("_SmoothnessRemapMin",0);material.SetFloat("_SmoothnessRemapMax",1);
        }
    }
    private static void Destroy(Texture2D texture){if(texture!=null)Object.DestroyImmediate(texture);}
    internal static void Clear(){foreach(var texture in owned)Destroy(texture);owned.Clear();sets.Clear();initialized=false;}
}
