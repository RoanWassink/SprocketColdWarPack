using System.Text.Json;
namespace SprocketExpandedBallistics;

internal static class CassetteTechnology
{
    // 0.22.90 begin
    /// <summary>The sink for the one line per Technology file with a missing field (warning, text); set by CassetteDefaults. Without it the note is dropped.</summary>
    internal static Action<bool,string>? Log;
    private static readonly HashSet<string> noted=new(StringComparer.OrdinalIgnoreCase);
    private static void Note(string file,bool warning,string text)
    {
        lock(noted){if(!noted.Add(file))return;}
        try{Log?.Invoke(warning,text);}catch{/* a note never stops loading */}
    }
    // 0.22.90 end
    internal static Dictionary<string,CassetteMaterial> ReadDirectory(string directory)
    {
        var result=new Dictionary<string,CassetteMaterial>(StringComparer.Ordinal);
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
            var root=doc.RootElement;
            if(!root.TryGetProperty("type",out var t)||!root.TryGetProperty("properties",out var p)||!p.TryGetProperty("rhaFactor",out var rha))continue;
            string id=t.GetString()!;
            // 0.22.90 begin
            // A stock Technology file may lack a field: Sand.json of a fresh install has no costMultiplier (E:, 16-11). GetProperty threw KeyNotFoundException,
            // and RuntimeArmourResponses.Configure then switched off every cassette preset. Missing density or spallFactor: the file is skipped with one
            // warning. Missing costMultiplier only: 1.0 (cost only, never protection) with one line. A file with every field gives the same CassetteMaterial.
            if(!p.TryGetProperty("density",out var density)||!p.TryGetProperty("spallFactor",out var spallFactor))
            {Note(file,true,"Technology "+Path.GetFileName(file)+" ("+id+") has no density or spallFactor; skipped for cassettes.");continue;}
            var cost=1.0;
            if(p.TryGetProperty("costMultiplier",out var costMultiplier))cost=costMultiplier.GetDouble();
            else Note(file,false,"Technology "+Path.GetFileName(file)+" ("+id+") has no costMultiplier; 1.0 used (cost only, not protection).");
            // 0.22.90 end
            var material=new CassetteMaterial(id,density.GetDouble(),rha.GetDouble(),spallFactor.GetDouble(),cost); // 0.22.90 replaced
            // Some technology types have dated variants. For armour the latest
            // file wins only when its passive signature is identical.
            if(result.TryGetValue(id,out var old)&&old!=material)throw new FormatException("Ambiguous passive material technology: "+id);
            result[id]=material;
        }
        return result;
    }
    /// <summary>Earliest enabled date per Technology type (0.22.7): the first date a vehicle can use that material.</summary>
    internal static Dictionary<string,DateTime> ReadDates(string directory)
    {
        var result=new Dictionary<string,DateTime>(StringComparer.Ordinal);
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            try
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip});
                var root=doc.RootElement;
                if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var t)||!root.TryGetProperty("date",out var d))continue;
                if(root.TryGetProperty("properties",out var p)&&p.ValueKind==JsonValueKind.Object&&p.TryGetProperty("enabled",out var en)&&en.ValueKind==JsonValueKind.False)continue;
                if(t.GetString() is not {} type||!DateTime.TryParseExact(d.GetString(),"yyyy.MM.dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date))continue;
                if(!result.TryGetValue(type,out var old)||date<old)result[type]=date;
            }
            catch(JsonException){}
        }
        return result;
    }
    internal static string Generate(CassetteModel model)=>JsonSerializer.Serialize(new
    {
        v="0.0",id="",type=model.Preset.MaterialId,date=model.Preset.Date,
        properties=new{rhaFactor=model.Passive.RhaFactor,costMultiplier=model.Passive.CostMultiplier,spallFactor=model.Passive.SpallFactor,density=model.Passive.Density}
    },new JsonSerializerOptions{WriteIndented=true})+"\n";
}
