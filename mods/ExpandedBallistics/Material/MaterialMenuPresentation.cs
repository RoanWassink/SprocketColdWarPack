using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics.Materials;

// Presentation only: hidden aliases remain in the native database and saves.
internal static class MaterialMenuPresentation
{
    internal sealed record Entry(string Id,string Label,string Family,string Contract,bool RegisteredAlias);
    internal static IReadOnlyList<Entry> Select(IEnumerable<Entry> source,string selectedId)
    {
        var result=new List<Entry>();
        foreach(var group in source.GroupBy(e=>e.RegisteredAlias&&!string.IsNullOrEmpty(e.Contract)
            ?e.Family+"|"+e.Contract:"unique|"+e.Id,StringComparer.Ordinal))
        {
            // A saved alias stays selected, never silently replaced by another ID.
            result.Add(group.FirstOrDefault(e=>e.Id==selectedId)??group.OrderBy(e=>e.Id.StartsWith("experimental",StringComparison.Ordinal)?1:0).ThenBy(e=>e.Id,StringComparer.Ordinal).First());
        }
        return result;
    }
    internal static string Family(string id,string kind,string mode,string label)
    {
        if(kind is "lightEra" or "heavyEra")return "ERA";
        if(kind=="nera"||id=="elastomerNERA"||label.Contains("NERA",StringComparison.OrdinalIgnoreCase))return "NERA";
        if(kind=="glassTextolite"||label.Contains("textolite",StringComparison.OrdinalIgnoreCase))return "Textolite";
        if(id=="ceramicArray"||label.Contains("ceramic",StringComparison.OrdinalIgnoreCase))return "Ceramic";
        return "";
    }
    internal static string Label(string id,string label,string family)
    {
        if(family.Length==0)return label;
        // Preserve meaningful custom names; replace generated camel-case labels.
        var known=id switch
        {
            "kontakt1Active"=>"Kontakt-1 active", "kontakt1Cassette"=>"Kontakt-1 cassette",
            "kontakt5Active"=>"Kontakt-5 active", "kontakt5Cassette" or "experimentalKontakt5Cassette"=>"Kontakt-5 cassette",
            "reliktActive"=>"Relikt hull", "reliktTurretActive"=>"Relikt turret", "reliktCassette" or "experimentalReliktCassette"=>"Relikt cassette",
            "nizh34Active"=>"Nizh KhSChKV-34 (36 mm)", "nizh19Active"=>"Nizh KhSChKV-19 (26 mm)",
            "experimentalNizh34Insert"=>"Nizh KhSChKV-34 insert (36 mm)", "experimentalNizh19Insert"=>"Nizh KhSChKV-19 insert (26 mm)",
            "nizhCassette" or "experimentalNizhCassette"=>"Nizh cassette", "nizhLegacyActive"=>"Nizh legacy active", "nizh1MActive"=>"Nizh-1M", "nizhLmInsert"=>"Nizh-LM",
            "dupletCassette"=>"Duplet cassette", "dupletLegacySingleActive"=>"Duplet legacy single", "dupletLegacyIntegratedActive"=>"Duplet legacy integrated",
            "dupletHullActive"=>"Duplet hull", "dupletHullZonesActive"=>"Duplet hull zones", "duplet2MActive"=>"Duplet-2M", "duplet2MHullActive"=>"Duplet-2M hull",
            "aratIActive"=>"ARAT-I", "aratIIActive"=>"ARAT-II", "dynaActive"=>"DYNA", "romorAActive"=>"ROMOR-A", "bratActive"=>"BRAT",
            "cwepLightEraCassette"=>"Light cassette", "cwepHeavyEraCassette"=>"Heavy cassette", "cwepNeraCassette"=>"Sandwich cassette",
            "cwepGlassTextolite"=>"Glass laminate", "elastomerNERA"=>"Elastomer", "ceramicArray"=>"Array", _=>null
        };
        // Explicit category prefixes are idempotent, including user labels.
        if(Regex.IsMatch(label,@"^\s*(ERA|NERA|Textolite|Ceramic)\s*-",RegexOptions.IgnoreCase))
            return family+" - "+Regex.Replace(label,@"^\s*(ERA|NERA|Textolite|Ceramic)\s*-\s*","",RegexOptions.IgnoreCase).Trim();
        static string Compact(string text)=>Regex.Replace(text,"[^a-z0-9]","",RegexOptions.IgnoreCase).ToLowerInvariant();
        var generated=Compact(label)==Compact(id)||Compact(label)==Compact(Regex.Replace(id,"^cwep","",RegexOptions.IgnoreCase));
        return family+" - "+(generated&&known!=null?known:label).Trim();
    }
    // Canonical order means JSON property ordering cannot create false variants.
    // Only top-level identity fields are ignored. Every nested behavior stays.
    internal static string Contract(JsonElement value,params string[] identities)
    {
        using var stream=new System.IO.MemoryStream();
        using(var writer=new Utf8JsonWriter(stream))Write(writer,value,identities);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void Write(Utf8JsonWriter writer,JsonElement value,string[]? ignore=null)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach(var property in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal))
            {if(ignore?.Contains(property.Name,StringComparer.Ordinal)==true)continue;writer.WritePropertyName(property.Name);Write(writer,property.Value);}
            writer.WriteEndObject();
        }
        else if(value.ValueKind==JsonValueKind.Array){writer.WriteStartArray();foreach(var item in value.EnumerateArray())Write(writer,item);writer.WriteEndArray();}
        else value.WriteTo(writer);
    }
}
