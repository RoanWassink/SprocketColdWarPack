using System.Globalization;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>One native era (Sprocket_Data/StreamingAssets/Eras/*.json).</summary>
internal sealed record ShellEra(string Name,DateTime Start,string Friendly)
{
    internal string Group=>Name.StartsWith("Coldwar",StringComparison.OrdinalIgnoreCase)?"Cold War":"World wars";
    internal string StartText=>Start.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}

/// <summary>
/// Date helper for the shell <c>minimumDate</c> setting. The text field stores a
/// real date; the dropdown only inserts an era's start date. Era names remain
/// labels/grouping and never act as the availability gate.
/// </summary>
internal static class ShellEraCatalogue
{
    internal static readonly ShellEra[] BuiltIn=
    {
        new("WWI",new(1914,7,28),"WWI"),new("Interwar",new(1918,11,12),"Interwar"),new("Earlywar",new(1939,9,2),"Early war"),
        new("Midwar",new(1942,1,1),"Mid war"),new("Latewar",new(1944,1,1),"Late war"),new("Coldwar",new(1945,9,3),"Cold War"),
        new("Coldwar2",new(1960,1,1),"Cold War II"),new("Coldwar3",new(1975,1,1),"Cold War III")
    };
    private static readonly JsonDocumentOptions Lenient=new(){AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip};
    internal const int None=-1,UnknownEra=-2;

    internal static string Friendly(string name)=>BuiltIn.FirstOrDefault(e=>string.Equals(e.Name,name,StringComparison.OrdinalIgnoreCase))?.Friendly??name;

    /// <summary>Parse era files (name + "yyyy.MM.dd" start; the game's files have trailing commas). Invalid files are skipped.</summary>
    internal static IReadOnlyList<ShellEra> Parse(IEnumerable<string> jsonFiles)
    {
        var result=new List<ShellEra>();
        foreach(var json in jsonFiles)
        {
            try
            {
                using var doc=JsonDocument.Parse(json,Lenient);
                var root=doc.RootElement;
                if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("name",out var n)||!root.TryGetProperty("start",out var s))continue;
                var name=n.GetString()?.Trim()??"";
                if(name.Length is <1 or >40||name.Any(char.IsControl))continue;
                if(!DateTime.TryParseExact(s.GetString(),new[]{"yyyy.MM.dd","yyyy-MM-dd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var start))continue;
                if(result.Any(e=>string.Equals(e.Name,name,StringComparison.OrdinalIgnoreCase)))continue;
                result.Add(new(name,start,Friendly(name)));
            }
            catch(JsonException){}
        }
        return result.OrderBy(e=>e.Start).ThenBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    /// <summary>The game's eras, or the built-in list when the folder is missing or unreadable.</summary>
    internal static IReadOnlyList<ShellEra> Load(string? directory)
    {
        try
        {
            if(directory!=null&&Directory.Exists(directory))
            {
                var eras=Parse(Directory.EnumerateFiles(directory,"*.json").Select(File.ReadAllText));
                if(eras.Count>0)return eras;
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        return BuiltIn;
    }
    /// <summary>Index of the stored value: <see cref="None"/> for blank, <see cref="UnknownEra"/> for text that matches no era.</summary>
    internal static int Find(IReadOnlyList<ShellEra> eras,string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return None;
        var text=value.Trim();
        for(int i=0;i<eras.Count;i++)
            if(string.Equals(eras[i].Name,text,StringComparison.OrdinalIgnoreCase)||
               string.Equals(text,"ww1",StringComparison.OrdinalIgnoreCase)&&string.Equals(eras[i].Name,"WWI",StringComparison.OrdinalIgnoreCase))return i;
        return UnknownEra;
    }
    /// <summary>Dropdown entries: "always" first, then era start dates as insert helpers, then a custom date if typed.</summary>
    internal static IReadOnlyList<(string Label,string? Group,string Value)> Options(IReadOnlyList<ShellEra> eras,string? current)
    {
        var list=new List<(string,string?,string)>{("No restriction (always available)",null,"")};
        foreach(var e in eras)list.Add(($"{e.Friendly} · from {e.StartText}",e.Group,e.Start.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)));
        if(!string.IsNullOrWhiteSpace(current)&&eras.All(e=>e.Start.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)!=current.Trim()))
            list.Add(($"Custom date: {current.Trim()}","Custom",current.Trim()));
        return list;
    }
    /// <summary>Selected option index in <see cref="Options"/> for the stored date.</summary>
    internal static int OptionIndex(IReadOnlyList<ShellEra> eras,string? current)
    {
        if(string.IsNullOrWhiteSpace(current))return 0;
        var text=current.Trim();
        for(var i=0;i<eras.Count;i++)
            if(eras[i].Start.ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)==text)return i+1;
        return eras.Count+1;
    }
    internal static string? Error(IReadOnlyList<ShellEra> eras,string? current)
    {
        if(string.IsNullOrWhiteSpace(current))return null;
        return ShellNativeDate.TryParse(current.Trim(),out var date)&&date.Valid
            ?null
            :"Use a real date as yyyy.MM.dd.";
    }
    /// <summary>"Available from 1975.01.01 · this vehicle: Coldwar III ✓".</summary>
    internal static string Availability(IReadOnlyList<ShellEra> eras,string? current,string? vehicleEra)
    {
        ShellNativeDate? date=null;string text;
        if(string.IsNullOrWhiteSpace(current))text="Available in every era";
        else if(!ShellNativeDate.TryParse(current.Trim(),out var parsed)||!parsed.Valid)return "Never available: enter a real date.";
        else{date=parsed;text=$"Available from {parsed.Text}";}
        if(vehicleEra==null)return text;
        var v=Find(eras,vehicleEra);
        if(v<0)return text;
        var start=new ShellNativeDate(eras[v].Start.Year,eras[v].Start.Month,eras[v].Start.Day);
        return text+$" · this vehicle: {eras[v].Friendly} "+(date is null||date.Value>=start?"✓":"✗");
    }
}
