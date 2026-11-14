using System.Text.Json;
namespace SprocketVisualEffects;

internal static class AssetContract
{
    internal static void Validate(string directory)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"effect-manifest.json")));
        var root=doc.RootElement;
        foreach(var (name,value) in new[]{("columns",8),("rows",8),("frames",64),("frameWidth",512),("frameHeight",256)})
            if(root.GetProperty(name).GetInt32()!=value)throw new InvalidDataException("Unsupported atlas contract: "+name);
        if(Math.Abs(root.GetProperty("duration").GetSingle()-EffectMath.Duration)>.00001||root.GetProperty("order").GetString()!="row-major-top-left")throw new InvalidDataException("Unsupported atlas duration/order");
        var times=root.GetProperty("frameTimes");
        if(times.GetArrayLength()!=64)throw new InvalidDataException("Frame schedule differs");
        for(var i=0;i<64;i++)if(Math.Abs(times[i].GetDouble()-(i<24?i*.01:.24+(i-24)*.06))>1e-5)throw new InvalidDataException("Frame timing differs");
        foreach(var view in new[]{"side","front"})
        {
            var v=root.GetProperty("views").GetProperty(view);
            if(v.GetProperty("image").GetString()!="muzzle-"+view+".png")throw new InvalidDataException("Unexpected atlas filename");
            var uv=v.GetProperty("muzzleUV");
            if(Math.Abs(uv[0].GetSingle()-(view=="side"?.1f:.5f))>1e-5||Math.Abs(uv[1].GetSingle()-.5f)>1e-5)throw new InvalidDataException("Unexpected muzzle UV");
            var bounds=v.GetProperty("worldBounds");
            var horizontal=bounds.GetProperty(view=="side"?"x":"y");var vertical=bounds.GetProperty("z");
            if(Math.Abs(horizontal[0].GetSingle()-(view=="side"?-.7f:-3.5f))>1e-5||Math.Abs(horizontal[1].GetSingle()-(view=="side"?6.3f:3.5f))>1e-5||Math.Abs(vertical[0].GetSingle()+1.75f)>1e-5||Math.Abs(vertical[1].GetSingle()-1.75f)>1e-5)throw new InvalidDataException("Unexpected source world bounds");
        }
    }
}
