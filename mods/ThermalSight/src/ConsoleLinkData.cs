using System.Text.Json;
namespace SprocketThermalSight;
internal sealed record ConsoleLinkEntry(int Sight,int Console);
internal sealed record ConsoleLinkDocument(int Version,ConsoleLinkEntry[] Links);
internal sealed record SavedConsoleLinks(bool Canonical,ConsoleLinkEntry[] Links,int Legacy);
internal sealed class ConsoleVuidMap
{
    private readonly Dictionary<int,int> map=new();
    private readonly HashSet<int> ambiguous=new(),ambiguousNext=new();
    internal IReadOnlyDictionary<int,int> Values=>map;
    private void PoisonNext(int next)
    {
        ambiguousNext.Add(next);
        foreach(int old in map.Where(p=>p.Value==next).Select(p=>p.Key).ToArray())
        {map.Remove(old);ambiguous.Add(old);}
    }
    internal void Add(int old,int next)
    {
        if(old<0||next<0||ambiguous.Contains(old))return;
        if(map.TryGetValue(old,out int previous))
        {PoisonNext(previous);PoisonNext(next);ambiguous.Add(old);return;}
        if(ambiguousNext.Contains(next)){ambiguous.Add(old);return;}
        if(map.Values.Contains(next)){PoisonNext(next);ambiguous.Add(old);return;}
        map[old]=next;
    }
}
internal static class ConsoleLinkData
{
    internal const string Key="sprocketFcsLinksV1";
    internal const string LegacyKey="thermalConsoleVuid";
    internal static SavedConsoleLinks Read(IEnumerable<string> keys,Func<string,string> getString,Func<string,int> getInt)
    {
        var names=keys.ToHashSet();
        if(names.Contains(Key))return new(true,Decode(getString(Key)),-1);
        return new(false,Array.Empty<ConsoleLinkEntry>(),names.Contains(LegacyKey)?getInt(LegacyKey):-1);
    }
    internal static string Encode(IEnumerable<ConsoleLinkEntry> links)
    {
        var rows=links.ToArray();Validate(rows);
        return JsonSerializer.Serialize(new ConsoleLinkDocument(1,rows));
    }
    internal static ConsoleLinkEntry[] Decode(string json)
    {
        if(json.Length>16384)throw new InvalidDataException("FCS document too large.");
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;Properties(root,"Version","Links");
        if(!root.TryGetProperty("Version",out var version)||!version.TryGetInt32(out int v)||v!=1||
           !root.TryGetProperty("Links",out var links)||links.ValueKind!=JsonValueKind.Array||links.GetArrayLength()>64)
            throw new InvalidDataException("Unsupported FCS link document.");
        var rows=new List<ConsoleLinkEntry>();
        foreach(var row in links.EnumerateArray())
        {
            Properties(row,"Sight","Console");
            if(!row.TryGetProperty("Sight",out var source)||!source.TryGetInt32(out int sight)||
               !row.TryGetProperty("Console",out var target)||!target.TryGetInt32(out int console))
                throw new InvalidDataException("Missing FCS link identity.");
            rows.Add(new(sight,console));
        }
        Validate(rows.ToArray());return rows.ToArray();
    }
    private static void Properties(JsonElement node,params string[] allowed)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid FCS object.");
        var names=new HashSet<string>();
        foreach(var p in node.EnumerateObject())
            if(!allowed.Contains(p.Name)||!names.Add(p.Name))throw new InvalidDataException("Unknown/duplicate FCS field.");
    }
    private static void Validate(ConsoleLinkEntry[] rows)
    {
        if(rows.Length>64||rows.Any(r=>r==null||r.Sight<0||r.Console<0)||rows.Select(r=>r.Sight).Distinct().Count()!=rows.Length)
            throw new InvalidDataException("Invalid or ambiguous FCS links.");
    }
    internal static ConsoleLinkEntry[] ForOwner(IEnumerable<ConsoleLinkEntry> rows,IReadOnlySet<int> sourceIds) =>
        rows.Where(r=>sourceIds.Contains(r.Sight)).ToArray();
    internal static ConsoleLinkEntry[] Remap(IEnumerable<ConsoleLinkEntry> rows,IReadOnlyDictionary<int,int> map)
    {
        var input=rows.ToArray();
        var unique=input.GroupBy(r=>r.Sight).Where(g=>g.Count()==1).Select(g=>g.Single());
        return unique.Where(r=>map.ContainsKey(r.Sight)&&map.ContainsKey(r.Console))
            .Select(r=>new ConsoleLinkEntry(map[r.Sight],map[r.Console])).ToArray();
    }
}


