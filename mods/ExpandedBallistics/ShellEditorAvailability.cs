using System.Globalization;
using System.Text.Json;
using BepInEx;
using Sprocket.Vehicles.Cannons;
namespace SprocketExpandedBallistics;

/// <summary>
/// All availability logic of the shell editor in one file (0.22.3). The panel and model only call
/// <see cref="For"/> (row subtitle, lamp/dim, chip) and <see cref="Date"/> (display-only sort).
/// Merge note: when the shell rebalance adds <c>RuntimeShellEra.Availability</c> (retiredAfter,
/// launcher-only rules), <see cref="For"/> now uses that helper (0.22.5).
/// </summary>
internal static class ShellEditorAvailability
{
    /// <summary>Selectable as in the inspector's "Shell type" dropdown (dates, retiredAfter, launcher-only, archive),
    /// via <c>RuntimeShellEra.Availability</c>. Reason: "From yyyy", "Retired yyyy", "Launcher only", "Archive (retired yyyy)"
    /// or "Unavailable"; an available shell shows its first year as information.</summary>
    internal static (bool Available,string Reason) For(ShellProfile profile,Cannon? cannon)
    {
        var date=Date(profile);var since=date is {} d?"From "+d.Year.ToString(CultureInfo.InvariantCulture):"";
        if(cannon==null)return (true,since); // no vehicle context: nothing dimmed
        try{var a=RuntimeShellEra.Availability(profile,cannon);return (a.Selectable,a.Reason.Length>0?a.Reason:since);}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Shell editor] Availability unknown for "+profile.Id+": "+ex.Message);return (true,since);}
    }
    /// <summary>Latest of the required technology dates and the optional minimumDate: the first date the profile can appear.</summary>
    internal static DateTime? Date(ShellProfile profile)
    {
        var dates=TechDates();DateTime? latest=null;
        foreach(var id in ShellBalance.RequiredTechnologyIds(profile))
            if(dates.TryGetValue(id,out var d)&&(latest==null||d>latest))latest=d;
        if(profile.MinimumDate is {} minimum&&(latest==null||minimum.Key>latest.Value.Year*10000+latest.Value.Month*100+latest.Value.Day))
            latest=new DateTime(minimum.Year==0?400:minimum.Year,minimum.Month,minimum.Day);
        return latest;
    }
    private static Dictionary<string,DateTime>? techDates;
    /// <summary>Earliest enabled date per Technology type, read once per session.</summary>
    private static Dictionary<string,DateTime> TechDates()
    {
        if(techDates!=null)return techDates;
        var result=new Dictionary<string,DateTime>(StringComparer.Ordinal);
        var options=new JsonDocumentOptions{AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip};
        try
        {
            foreach(var file in Directory.EnumerateFiles(Path.Combine(Paths.GameRootPath,"Sprocket_Data","StreamingAssets","Technology"),"*.json"))
            {
                try
                {
                    using var doc=JsonDocument.Parse(File.ReadAllText(file),options);var root=doc.RootElement;
                    if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("type",out var t)||!root.TryGetProperty("date",out var dt))continue;
                    if(root.TryGetProperty("properties",out var p)&&p.ValueKind==JsonValueKind.Object&&p.TryGetProperty("enabled",out var en)&&en.ValueKind==JsonValueKind.False)continue;
                    if(t.GetString() is not {} type||!DateTime.TryParseExact(dt.GetString(),"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))continue;
                    if(!result.TryGetValue(type,out var old)||date<old)result[type]=date;
                }
                catch(JsonException){}
            }
        }
        catch(Exception ex) when (ex is IOException or UnauthorizedAccessException){Plugin.ModLog.LogWarning("[Shell editor] Technology dates unavailable: "+ex.Message);}
        return techDates=result;
    }
}
