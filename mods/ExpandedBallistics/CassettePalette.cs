using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>
/// The one cassette colour set (PLATE-VISUAL.md families). The designer plate stripes
/// (<see cref="CassetteStripRaster"/>) and the composer cross-section both read it here,
/// so a layer has the same colour on the plate edge and in the composer.
/// </summary>
internal static class CassettePalette
{
    internal const uint Separator=0x14150fff,AirHatch=0x8f8670ff,Accent=0xdc9e33ff,Missing=0xe07a4fff;
    internal const string SeparatorHex="#14150f",AirHatchHex="#8f8670",AccentHex="#dc9e33",ErrorHex="#e07a4f",TextHex="#fff0cc",TiltHex="#fff0cc73";
    /// <summary>Family colour as RGBA (0xRRGGBBAA).</summary>
    internal static uint Colour(string material)=>material switch
    {
        "ebWood"=>0x8b5a2bff,"ebRubber"=>0x202124ff,"ebFabric"=>0x777b3fff,"ebGlass"=>0xa8dcefff,
        "air"=>0x1b1c15ff,"highHardnessSteel" or "advancedArmourSteel" or "perforatedHhs"=>0x6f8aa3ff,
        "castArmour"=>0x8b6b4bff,"cwepGlassTextolite" or "varmaComposite"=>0xa9b347ff,
        "ceramicArray" or "boronCarbide"=>0xe8e1d0ff,"aluminaCeramic"=>0xf0e4c4ff,"siliconCarbide"=>0xa3aaa6ff,"elastomerNERA" or "neraCore"=>0x2b2c2eff,
        "aramidSpallLiner"=>0xe6cf3cff,"heavyAlloyInsert"=>0x55595eff,"duMesh"=>0x5b6236ff,
        "aluminiumArmour" or "titaniumArmour" or "magnesiumAlloy"=>0xb4c3cfff,
        "quartzFiller" or "sand"=>0xcbb68aff,_=>0x8e9296ff
    };
    /// <summary>"#rrggbb" of <see cref="Colour"/> for the kit (swatches, diagram).</summary>
    internal static string Hex(string material)=>"#"+(Colour(material)>>8).ToString("x6",CultureInfo.InvariantCulture);
    /// <summary>Dropdown group for a constituent; the order follows <see cref="GroupOrder"/>.</summary>
    internal static string Group(string material)=>material switch
    {
        "air"=>"Air",
        "rha" or "highHardnessSteel" or "advancedArmourSteel" or "perforatedHhs" or "castArmour" or "structuralSteel" or "sheetMetal"=>"Steel",
        "cwepGlassTextolite" or "varmaComposite" or "quartzFiller" or "sand"=>"Composite and filler",
        "ceramicArray" or "boronCarbide" or "aluminaCeramic" or "siliconCarbide"=>"Ceramic",
        "elastomerNERA" or "neraCore"=>"NERA",
        "heavyAlloyInsert" or "duMesh"=>"Heavy metal",
        "aluminiumArmour" or "titaniumArmour" or "magnesiumAlloy"=>"Light alloy",
        "aramidSpallLiner"=>"Liner",
        _=>"Other"
    };
    internal static readonly string[] GroupOrder={"Steel","Composite and filler","Ceramic","NERA","Heavy metal","Light alloy","Liner","Other","Air"};
    internal static int GroupIndex(string group){var i=Array.IndexOf(GroupOrder,group);return i<0?GroupOrder.Length:i;}
}
