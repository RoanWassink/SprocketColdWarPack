using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

internal sealed record CassetteLayer(string Material,double ThicknessMm,double TiltDegrees,string TiltDirection);
/// <summary>
/// One cassette. <paramref name="Location"/> (0.22.7) is metadata for the composer's Turret / Hull switch:
/// "turret" (also when the file has no field) or "hull". It never changes the protection maths.
/// </summary>
internal sealed record CassettePreset(string Id,string Label,string Date,double MinimumDepthMm,double MaximumDepthMm,CassetteLayer[] Layers,string Location=CassettePreset.Turret)
{
    internal const string Turret="turret",Hull="hull";
    internal string MaterialId=>"ebCassette_"+Id;
    internal static bool ValidLocation(string? location)=>location is Turret or Hull;
}
internal sealed record CassetteCatalogue(CassettePreset[] Presets)
{
    internal const string FileName="sprocket.expandedballistics.cassettes.json";
    internal static CassetteCatalogue Parse(string json)
    {
        try
        {
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            Fields(root,new[]{"schemaVersion","presets"});
            if(root.GetProperty("schemaVersion").GetInt32()!=1)throw new FormatException("Unsupported cassette schemaVersion.");
            var list=root.GetProperty("presets");Array(list,0,128,"presets");
            var presets=new List<CassettePreset>();var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in list.EnumerateArray())
            {
                Fields(p,new[]{"id","label","date","window","layers"},new[]{"location"});var id=Text(p,"id");
                if(!Regex.IsMatch(id,"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}$")||!ids.Add(id))throw new FormatException("Invalid/duplicate cassette id: "+id);
                var date=Text(p,"date");if(!DateTime.TryParseExact(date,"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new FormatException("Invalid cassette date: "+id);
                var location=CassettePreset.Turret;
                if(p.TryGetProperty("location",out var loc))
                {
                    location=loc.ValueKind==JsonValueKind.String?loc.GetString()!:"";
                    if(!CassettePreset.ValidLocation(location))throw new FormatException("Unknown cassette location (use \"turret\" or \"hull\"): "+id);
                }
                var w=p.GetProperty("window");Fields(w,new[]{"minDepthMm","maxDepthMm"});
                double min=Number(w,"minDepthMm",1,4000),max=Number(w,"maxDepthMm",1,4000);
                if(min>max)throw new FormatException("Inverted cassette depth window: "+id);
                var source=p.GetProperty("layers");Array(source,1,64,"layers");var layers=new List<CassetteLayer>();
                foreach(var l in source.EnumerateArray())
                {
                    Fields(l,new[]{"material","thicknessMm","tiltDegrees","tiltDirection"});
                    var material=Text(l,"material");var direction=Text(l,"tiltDirection");
                    if(direction is not ("up" or "down" or "left" or "right"))throw new FormatException("Unknown cassette tiltDirection: "+direction);
                    var tilt=Number(l,"tiltDegrees",0,75);
                    if(material=="air"&&tilt!=0)throw new FormatException("Air layers have depth only; tiltDegrees must be zero.");
                    layers.Add(new(material,Number(l,"thicknessMm",.01,2000),tilt,direction));
                }
                if(layers.All(l=>l.Material=="air"))throw new FormatException("Cassette must contain a solid layer: "+id);
                presets.Add(new(id,Text(p,"label"),date,min,max,layers.ToArray(),location));
            }
            return new(presets.ToArray());
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {throw new FormatException("Invalid cassette catalogue: "+e.Message,e);}
    }
    private static void Fields(JsonElement e,string[] required,string[]? optional=null)
    {
        if(e.ValueKind!=JsonValueKind.Object)throw new FormatException("Expected cassette object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in e.EnumerateObject())
            if(!(required.Contains(p.Name,StringComparer.Ordinal)||optional?.Contains(p.Name,StringComparer.Ordinal)==true)||!seen.Add(p.Name))throw new FormatException("Unknown/duplicate cassette field: "+p.Name);
        foreach(var n in required)if(!seen.Contains(n))throw new FormatException("Missing cassette field: "+n);
    }
    private static string Text(JsonElement e,string name)
    {var s=e.GetProperty(name).GetString();if(string.IsNullOrWhiteSpace(s)||s.Length>120||s.Any(char.IsControl))throw new FormatException("Invalid cassette text: "+name);return s;}
    private static double Number(JsonElement e,string n,double min,double max)
    {var v=e.GetProperty(n).GetDouble();if(!double.IsFinite(v)||v<min||v>max)throw new FormatException("Invalid cassette range: "+n);return v;}
    private static void Array(JsonElement e,int min,int max,string name)
    {if(e.ValueKind!=JsonValueKind.Array||e.GetArrayLength()<min||e.GetArrayLength()>max)throw new FormatException("Invalid cassette array: "+name);}
}
