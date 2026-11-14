using System.Globalization;
using System.Text.Json;
namespace SprocketExpandedBallistics;

// Result of the backend availability query used by the shell editor. Reason is
// one of: "", "From yyyy", "Retired yyyy", "Launcher only",
// "Archive (retired yyyy)" or "Unavailable".
internal sealed record ShellEraAvailability(bool Selectable,string Reason,ShellNativeDate? From,ShellNativeDate? Until);

// Selection policy for lists that let the player choose a profile. Deliberately
// separate from ShellEraPolicy.Allowed/Effective: a stored choice stays usable
// and only the choice lists hide retired or launcher-only profiles.
internal static class ShellEraSelection
{
    internal static ShellEraAvailability Availability(ShellProfile p,ShellEraContext? c,bool? isLauncher)
    {
        var until=p.RetiredAfter;
        if(!ShellEraPolicy.Valid(c))return new(false,"Unavailable",null,until);
        var context=c!;
        // The stock HEAT profile is only usable while its dated native technology exists.
        if(ShellChemicalBudget.IsStock(p)&&context.HeatFactor==null)return new(false,"Unavailable",null,until);
        var available=context.AvailableTechnologies??context.AvailableBehaviors.Select(b=>"expandedBallistics_"+b).ToHashSet(StringComparer.Ordinal);
        ShellNativeDate? from=null;
        if(p.MinimumDate is {} minimum)
        {
            if(context.Date<minimum)from=minimum;
        }
        foreach(var id in ShellBalance.RequiredTechnologyIds(p).Distinct(StringComparer.Ordinal))
        {
            if(available.Contains(id))continue;
            var date=ShellTechnologyDates.Date(id);
            // A missing or already-passed technology that is still absent is disabled
            // or unavailable, not merely early: do not invent a future unlock year.
            if(date is null||date.Value<=context.Date)return new(false,"Unavailable",null,until);
            if(from is null||date.Value>from.Value)from=date;
        }
        if(from is {} future)return new(false,$"From {future.Year}",future,until);
        if(until is {} retired&&context.Date>retired)
            return p.KeepAfterRetirement
                ?new(true,$"Archive (retired {retired.Year})",null,until)
                :new(false,$"Retired {retired.Year}",null,until);
        if(isLauncher is false&&AtgmLauncherModel.Allows(p))return new(false,"Launcher only",null,until);
        if(isLauncher is true&&!AtgmLauncherModel.Allows(p))return new(false,"Unavailable",null,until);
        return new(true,"",null,until);
    }
    /// <summary>Availability for a list, retaining an already selected profile when <paramref name="keep"/> is set.</summary>
    internal static ShellEraAvailability Selectable(ShellProfile p,ShellEraContext? c,bool? isLauncher,bool keep)
    {
        var availability=Availability(p,c,isLauncher);
        if(availability.Selectable||!keep)return availability;
        return availability.Reason.StartsWith("Retired ",StringComparison.Ordinal)||availability.Reason=="Launcher only"
            ?availability with{Selectable=true}
            :availability;
    }
    internal static IReadOnlyList<ShellProfile> Selectable(IReadOnlyList<ShellProfile> all,ShellEraContext? c,bool? isLauncher,IReadOnlySet<string>? keep)
    {
        var result=new List<ShellProfile>(all.Count);
        foreach(var profile in all)
            if(Selectable(profile,c,isLauncher,keep?.Contains(profile.Id)==true).Selectable)result.Add(profile);
        return result;
    }
    /// <summary>Earliest date at which every known requirement is available, or null when a date is unknown.</summary>
    internal static ShellNativeDate? EarliestAvailability(ShellProfile p)
    {
        ShellNativeDate? earliest=null;
        foreach(var id in ShellBalance.RequiredTechnologyIds(p).Distinct(StringComparer.Ordinal))
        {
            var date=ShellTechnologyDates.Date(id);
            if(date is null)return null;
            if(earliest is null||date.Value>earliest.Value)earliest=date;
        }
        if(p.MinimumDate is {} minimum&&(earliest is null||minimum>earliest.Value))earliest=minimum;
        return earliest;
    }
}

// Native technology introduction dates, read from the installed Technology JSON
// files (never guessed in code). One map is loaded lazily; lookups are O(1).
internal static class ShellTechnologyDates
{
    private static readonly object Gate=new();
    private static string? configured;
    private static Dictionary<string,ShellNativeDate>? cached;
    internal static void Configure(string? directory)
    {
        if(string.IsNullOrWhiteSpace(directory))return;
        lock(Gate)
        {
            if(string.Equals(configured,directory,StringComparison.OrdinalIgnoreCase)&&cached!=null)return;
            configured=directory;cached=null;
        }
    }
    internal static ShellNativeDate? Date(string id)
    {
        if(string.IsNullOrWhiteSpace(id))return null;
        var map=Map();
        return map.TryGetValue(id,out var date)?date:null;
    }
    private static Dictionary<string,ShellNativeDate> Map()
    {
        var map=cached;
        if(map!=null)return map;
        lock(Gate)
        {
            if(cached!=null)return cached;
            cached=Load(FindDirectory());
            return cached;
        }
    }
    private static string? FindDirectory()
    {
        if(!string.IsNullOrWhiteSpace(configured)&&Directory.Exists(configured))return configured;
        foreach(var directory in Candidates())if(Directory.Exists(directory))return directory;
        return null;
    }
    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory,"NativeData","Technology");
        yield return Path.Combine(AppContext.BaseDirectory,"native-data","Technology");
        // The test host copies Technology files flat into NativeData.
        yield return Path.Combine(AppContext.BaseDirectory,"NativeData");
        yield return Path.Combine(AppContext.BaseDirectory,"native-data");
        yield return Path.Combine(AppContext.BaseDirectory,"Sprocket_Data","StreamingAssets","Technology");
        var current=Directory.GetCurrentDirectory();
        yield return Path.Combine(current,"native-data","Technology");
        yield return Path.Combine(current,"..","native-data","Technology");
        yield return Path.Combine(current,"..","..","native-data","Technology");
    }
    private static Dictionary<string,ShellNativeDate> Load(string? directory)
    {
        var result=new Dictionary<string,ShellNativeDate>(StringComparer.Ordinal);
        if(directory==null)return result;
        try
        {
            foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
            {
                try
                {
                    using var document=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions{AllowTrailingCommas=true});
                    var root=document.RootElement;
                    if(root.ValueKind!=JsonValueKind.Object)continue;
                    if(!root.TryGetProperty("type",out var typeNode)||typeNode.ValueKind!=JsonValueKind.String)continue;
                    if(!root.TryGetProperty("date",out var dateNode)||dateNode.ValueKind!=JsonValueKind.String)continue;
                    var type=typeNode.GetString()??"";
                    if(string.IsNullOrWhiteSpace(type))continue;
                    if(!DateTime.TryParseExact(dateNode.GetString(),"yyyy.MM.dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))continue;
                    var enabled=root.TryGetProperty("properties",out var properties)&&properties.ValueKind==JsonValueKind.Object&&
                        properties.TryGetProperty("enabled",out var enabledNode)&&enabledNode.ValueKind==JsonValueKind.True;
                    if(!enabled)continue;
                    var value=new ShellNativeDate(date.Year,date.Month,date.Day);
                    // Multiple records of one type are dated upgrades; the first
                    // introduction is what "From yyyy" must report.
                    if(!result.TryGetValue(type,out var previous)||value<previous)result[type]=value;
                }
                catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        return result;
    }
}
