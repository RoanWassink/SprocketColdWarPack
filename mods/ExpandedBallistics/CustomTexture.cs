using System.Buffers.Binary;
using System.Text.Json;
namespace SprocketExpandedBallistics;

internal sealed class CustomTextureSettings
{
    public string useCustomTexture {get;set;}="auto";
    public float tileSizeM {get;set;}=1;
    public float normalStrength {get;set;}=1;
    public float[] tint {get;set;}=new[]{1f,1f,1f,1f};
    public float smoothness {get;set;}=.2f;
    public string normalConvention {get;set;}="OpenGL";
    internal bool DirectX=>normalConvention.Equals("DirectX",StringComparison.OrdinalIgnoreCase);
    internal void Validate()
    {
        if(!new[]{"auto","on","off"}.Contains(useCustomTexture)||!float.IsFinite(tileSizeM)||tileSizeM<.01f||tileSizeM>100||
           !float.IsFinite(normalStrength)||normalStrength<0||normalStrength>8||!float.IsFinite(smoothness)||smoothness<0||smoothness>1||
           tint==null||tint.Length!=4||tint.Any(v=>!float.IsFinite(v)||v<0||v>1)||
           !new[]{"OpenGL","DirectX"}.Contains(normalConvention,StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid texture settings (see README.txt).");
    }
    internal static Dictionary<string,CustomTextureSettings> Defaults()=>new()
    {
        ["wood"]=new(),["rubber"]=new(){tileSizeM=.5f,smoothness=.12f},["fabric"]=new(){tileSizeM=.5f,smoothness=.06f},
        ["glass"]=new(){smoothness=.96f,tint=new[]{.60f,.86f,.85f,.23f}}
    };
}

// This transaction is shared by the Unity loader and the headless failure tests.
// A present but invalid optional map invalidates the whole custom set. Missing optional maps are allowed.
internal sealed record CustomTextureSet<T>(CustomTextureSettings Settings,T Albedo,T? Normal,T? Roughness,T? Ao) where T:class;
internal sealed class CustomTextureScan<T> where T:class
{
    private readonly HashSet<string> warned=new();
    internal CustomTextureSet<T>? Read(string directory,CustomTextureSettings settings,Func<string,bool,T> decode,Action<T> dispose,Action<string> warning)
    {
        var owned=new List<T>();
        try
        {
            settings.Validate();if(settings.useCustomTexture=="off")return null;
            var path=Path.Combine(directory,"albedo.png");
            if(!File.Exists(path))throw new FileNotFoundException("albedo.png missing");
            T Load(string name,bool linear){var value=decode(Path.Combine(directory,name+".png"),linear);owned.Add(value);return value;}
            var albedo=Load("albedo",false);
            T? Optional(string name)=>File.Exists(Path.Combine(directory,name+".png"))?Load(name,true):null;
            return new(settings,albedo,Optional("normal"),Optional("roughness"),Optional("ao"));
        }
        catch(Exception ex)
        {
            foreach(var value in owned)dispose(value);
            if(warned.Add(directory))warning(directory+": "+ex.Message+"; using the 0.22.45 native look.");
            return null;
        }
    }
}
internal static class CustomTexturePixels
{
    internal static byte NormalY(byte green,bool directX)=>directX?(byte)(255-green):green;
    internal static byte Smoothness(byte roughness)=>(byte)(255-roughness);
    internal static (byte R,byte G,byte B,byte A) Mask(byte roughness,byte ao)=>(0,ao,255,Smoothness(roughness));
    internal static (int Width,int Height) Dimensions(byte[] png)
    {
        byte[] signature={137,80,78,71,13,10,26,10};
        if(png.Length<33||!png.AsSpan(0,8).SequenceEqual(signature)||!png.AsSpan(12,4).SequenceEqual(new byte[]{73,72,68,82}))throw new InvalidDataException("Invalid PNG header");
        int w=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)),h=BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4));
        if(w!=h||w is not (1024 or 2048))throw new InvalidDataException("PNG must be square, 1024 or 2048 pixels");
        return (w,h);
    }
}
