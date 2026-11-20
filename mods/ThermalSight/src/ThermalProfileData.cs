using System.Text.Json;
namespace SprocketThermalSight;
internal sealed record ThermalProfileEntry(int Sight,string Profile);
internal sealed record ThermalProfileDocument(int Version,ThermalProfileEntry[] Profiles);
// Per-sight profile choice in the native object state, next to sprocketFcsLinksV1.
// The codec keeps unknown (well-formed) profile ids, so a profile removed from thermal-models.json
// comes back once it is restored; Runtime.FindProfile warns and uses the default meanwhile.
internal static class ThermalProfileData
{
    internal const string Key="sprocketThermalProfilesV1";
    internal const int MaxRows=64,MaxIdLength=128;
    internal static string Encode(IEnumerable<ThermalProfileEntry> rows)
    {
        var all=rows.ToArray();Validate(all);
        return JsonSerializer.Serialize(new ThermalProfileDocument(1,all));
    }
    internal static ThermalProfileEntry[] Decode(string json)
    {
        if(json==null||json.Length>16384)throw new InvalidDataException("Profile document too large.");
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;ObjectStateRows.Properties(root,"profile","Version","Profiles");
        if(!root.TryGetProperty("Version",out var version)||!version.TryGetInt32(out int v)||v!=1||
           !root.TryGetProperty("Profiles",out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>MaxRows)
            throw new InvalidDataException("Unsupported profile document.");
        var rows=new List<ThermalProfileEntry>();
        foreach(var row in list.EnumerateArray())
        {
            ObjectStateRows.Properties(row,"profile","Sight","Profile");
            if(!row.TryGetProperty("Sight",out var s)||!s.TryGetInt32(out int sight)||
               !row.TryGetProperty("Profile",out var p)||p.ValueKind!=JsonValueKind.String)
                throw new InvalidDataException("Missing profile row identity.");
            rows.Add(new(sight,p.GetString()!));
        }
        var result=rows.ToArray();Validate(result);return result;
    }
    internal static bool ValidId(string? id)=>
        !string.IsNullOrWhiteSpace(id)&&id.Length<=MaxIdLength&&id.Trim()==id&&!id.Any(char.IsControl);
    private static void Validate(ThermalProfileEntry[] rows)
    {
        if(rows.Length>MaxRows||rows.Any(r=>r==null||r.Sight<0||!ValidId(r.Profile))||rows.Select(r=>r.Sight).Distinct().Count()!=rows.Length)
            throw new InvalidDataException("Invalid or ambiguous profile rows.");
    }
    internal static ThermalProfileEntry[] ForOwner(IEnumerable<ThermalProfileEntry> rows,IReadOnlySet<int> sourceIds)=>
        rows.Where(r=>sourceIds.Contains(r.Sight)).ToArray();
    internal static ThermalProfileEntry[] Remap(IEnumerable<ThermalProfileEntry> rows,IReadOnlyDictionary<int,int> map)=>
        ObjectStateRows.UniqueBySight(rows,r=>r.Sight).Where(r=>map.ContainsKey(r.Sight))
            .Select(r=>new ThermalProfileEntry(map[r.Sight],r.Profile)).ToArray();
}
