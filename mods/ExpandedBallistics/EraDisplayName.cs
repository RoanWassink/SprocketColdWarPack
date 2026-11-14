using System.Text.RegularExpressions;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

// Readable names for native component/material ids in the combat report, hit
// panel and HTML export. The visible ERA presets keep the same menu labels as
// the Addon Structures cards; only the nine 0.20/0.22 panels are not in the
// generated MenuLabels table, so their localized names are listed here.
internal static class EraDisplayName
{
    private static readonly Dictionary<string,string> FamilyNames=new(StringComparer.Ordinal)
    {
        ["b3de0597-85c1-5720-bf38-463d6900058c"]="Duplet modular",
        ["1873e6e7-451d-53a7-ad0b-1c496d0c1205"]="Duplet modular slim",
        ["6b4e1dc7-9020-5443-90c2-0f52c80933f9"]="Relikt panel (turret)",
        ["c8bc7cd9-1e9a-5a53-a613-306846df488a"]="Relikt panel (hull)",
        ["dd04b5c1-ed08-5406-bec5-de39cfe21814"]="Relikt side box",
        ["d706e182-a7c5-5a2a-9231-681480af74cc"]="Nizh panel",
        ["ea9df1d4-18f5-5c8f-a6d0-d2c20cd3bc91"]="Duplet panel (turret)",
        ["3532720a-7150-51da-85a5-00d94530dd46"]="Duplet panel (hull)",
        ["ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c"]="Duplet side module",
    };
    private static readonly Regex CamelCase=new("(?<=[a-z0-9])([A-Z])",RegexOptions.CultureInvariant);
    private static readonly Regex BetweenWordAndDigit=new("(?<=[A-Za-z])(?=[0-9])",RegexOptions.CultureInvariant);
    private static readonly Regex Spaces=new(@"\s+",RegexOptions.CultureInvariant);

    internal static string Component(string? id)
    {
        var value=(id??"").Trim();
        if(value.Length==0)return "ERA";
        var custom=MaterialAvailability.Label(value,"");
        if(custom.Length>0)return custom;
        if(EraPresetRegistry.Visual(value,out var preset,out var cassette))
        {
            var name=Family(preset);
            return cassette&&preset.StageIndex>0?name+" · layer "+preset.StageIndex:name;
        }
        return Humanize(value);
    }
    internal static string Material(string? id,string? responseId=null)
    {
        var value=(id??"").Trim();
        var custom=MaterialAvailability.Label(value,"");
        if(custom.Length>0)return custom;
        var name=Humanize(value);
        if(name.Length==0)name=Humanize(responseId);
        return name.Length==0?"ERA":name;
    }
    private static string Family(EraVisualPreset preset)
    {
        if(preset.PartGuid==CompositeModule.PartGuid)return CompositeModule.DisplayName;
        if(EraGeneratedDefinitions.MenuLabels.TryGetValue(preset.PartGuid,out var label))return label;
        if(FamilyNames.TryGetValue(preset.PartGuid,out var name))return name;
        return Humanize(preset.Cassette.Length>0?preset.Cassette:preset.Mount);
    }
    /// <summary>Fallback for an id without an ERA preset: split camelCase, digits and separators.</summary>
    internal static string Humanize(string? raw)
    {
        var text=(raw??"").Trim();
        if(text.Length==0)return "";
        text=text.Replace('_',' ').Replace('-',' ');
        text=CamelCase.Replace(text," $1");
        text=BetweenWordAndDigit.Replace(text," ");
        text=Spaces.Replace(text," ").Trim();
        if(text.Length==0)return "";
        text=text.ToLowerInvariant();
        return char.ToUpperInvariant(text[0])+text[1..];
    }
}
