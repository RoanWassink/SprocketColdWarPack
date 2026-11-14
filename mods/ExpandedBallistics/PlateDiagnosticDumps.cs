namespace SprocketExpandedBallistics;

// The cap includes existing diagnostics from earlier sessions. Never overwrite a plate dump.
internal sealed class PlateDiagnosticDumps
{
    private readonly HashSet<string> attempted=new();
    internal const int Maximum=10;
    internal bool Write(string directory,string id,OverlaySurface front,OverlaySurface back,OverlaySurface sides,OverlayCopy result)
    {
        if(result.SideArea<=0||result.FallbackArea/result.SideArea<=.05||!attempted.Add(id))return false;
        Directory.CreateDirectory(directory);
        if(Directory.EnumerateFiles(directory,"plate-*.json").Take(Maximum).Count()>=Maximum)return false;
        string path=Path.Combine(directory,"plate-"+id+".json");if(File.Exists(path))return false;
        var json=System.Text.Json.JsonSerializer.Serialize(new{front,back,sides,result.FallbackTriangles,result.FallbackArea,result.SideArea},new System.Text.Json.JsonSerializerOptions{IncludeFields=true});
        File.WriteAllText(path,json);return true;
    }
}
