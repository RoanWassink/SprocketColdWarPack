using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class CombatReportStore
{
    private sealed record Saved(int SchemaVersion,int OmittedShots,List<CombatReportShot> Shots);
    internal static void Load(CombatReportHistory history)
    {
        var path=Path.Combine(DirectoryPath,"last-session.json");if(!File.Exists(path))return;
        if(new FileInfo(path).Length>8*1024*1024)throw new InvalidDataException("Combat report exceeds supported size.");
        var saved=JsonSerializer.Deserialize<Saved>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
        if(saved?.SchemaVersion!=1||saved.Shots==null)throw new InvalidDataException("Unknown combat report format.");
        history.Restore(saved.Shots,saved.OmittedShots);
    }
    internal static string DirectoryPath=>Path.Combine(BepInEx.Paths.ConfigPath,"sprocket.expandedballistics.combat-report");
    internal static void Save(CombatReportHistory history)
    {
        Directory.CreateDirectory(DirectoryPath);
        var payload=new {schemaVersion=1,omittedShots=history.Omitted,shots=history.Shots};
        var json=JsonSerializer.Serialize(payload,new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(DirectoryPath,"last-session.json.tmp"),json);
        File.Move(Path.Combine(DirectoryPath,"last-session.json.tmp"),Path.Combine(DirectoryPath,"last-session.json"),true);
        File.WriteAllText(Path.Combine(DirectoryPath,"last-session.html"),Html(history));
    }
    internal static string Html(CombatReportHistory history)=>CombatReportHtml.Render(history);
}
