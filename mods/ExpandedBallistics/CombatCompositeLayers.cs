namespace SprocketExpandedBallistics;

/// <summary>
/// One constituent of a composer preset inside a reported plate (0.22.41). <paramref name="NormalMm"/> is its extent along the plate normal
/// (the composer depth t / cos tilt, air: t), scaled so the constituents add up to the plate's reported normal thickness;
/// <paramref name="Fraction"/> is that extent over the preset depth. Display data only: shares per constituent are not reported, because
/// the protection table gives one factor for the whole preset (no per-layer contribution that adds up exactly to it).
/// </summary>
internal sealed record CompositeSublayer(int Index,string Material,string Label,string Colour,bool Air,double TiltDegrees,double NormalMm,double Fraction);

/// <summary>
/// The known layer build-up of a composer preset (an <c>ebCassette_*</c> material): material, depth and tilt per layer, front to back as the
/// composer orders it. The game snapshot supplies the loaded presets (built-in and the user's own); tests supply the default catalogue.
/// </summary>
internal static class CombatCompositeLayers
{
    private static Func<string,CassettePreset?>? presets;
    private static Func<string,string?>? labels;
    /// <summary>Set once by the report owner (game) or a test. Lookups never throw into the report.</summary>
    internal static void Configure(Func<string,CassettePreset?>? presetLookup,Func<string,string?>? materialLabels=null){presets=presetLookup;labels=materialLabels;}
    internal static CassettePreset? Preset(string? materialId)
    {
        if(materialId==null||!materialId.StartsWith("ebCassette_",StringComparison.Ordinal))return null;
        try{var p=presets?.Invoke(materialId);return p!=null&&p.MaterialId==materialId&&p.Layers.Length>0?p:null;}
        catch(Exception){return null;}
    }
    /// <summary>The constituents of a single reported composer plate, scaled to its normal thickness; null when the build-up is not available.</summary>
    internal static IReadOnlyList<CompositeSublayer>? For(CombatLayerPart part)
    {
        if(!part.Armour||part.ThicknessMm is not {} thickness||!(thickness>0)||!double.IsFinite(thickness))return null;
        var preset=Preset(part.MaterialId);if(preset==null)return null;
        double depth=0;foreach(var l in preset.Layers)depth+=Depth(l);
        if(!(depth>0)||!double.IsFinite(depth))return null;
        var list=new List<CompositeSublayer>();
        for(int i=0;i<preset.Layers.Length;i++)
        {
            var l=preset.Layers[i];var d=Depth(l);
            list.Add(new(i+1,l.Material,Label(l.Material),CassettePalette.Hex(l.Material),l.Material=="air",l.Material=="air"?0:l.TiltDegrees,thickness*d/depth,d/depth));
        }
        return list;
    }
    private static double Depth(CassetteLayer l){var d=CassetteSection.Depth(l);return double.IsFinite(d)&&d>0?d:0;}
    /// <summary>The composer colour of the solid constituent with the largest depth: the hatch colour of a grouped array that holds this preset.</summary>
    internal static string? DominantColour(string? materialId)
    {
        var preset=Preset(materialId);if(preset==null)return null;
        var best=preset.Layers.Where(l=>l.Material!="air").GroupBy(l=>l.Material).Select(g=>(g.Key,g.Sum(Depth))).OrderByDescending(x=>x.Item2).FirstOrDefault();
        return best.Key==null?null:CassettePalette.Hex(best.Key);
    }
    /// <summary>The material name as the composer shows it.</summary>
    internal static string Label(string material)
    {
        if(material=="air")return "Air";
        try{if(labels?.Invoke(material) is {Length:>0} label)return CombatReportLanguage.Material(label,material);}catch(Exception){}
        return material switch
        {
            "rha"=>"Rolled Homogeneous Armour","highHardnessSteel"=>"High Hardness Steel","elastomerNERA"=>"NERA - Elastomer","sheetMetal"=>"Sheet Metal",
            "structuralSteel"=>"Structural Steel","castArmour"=>"Cast Armour","advancedArmourSteel"=>"Advanced Armour Steel",
            _=>CombatReportLanguage.Material(SprocketExpandedBallistics.Materials.MaterialAvailability.Label(material,EraDisplayName.Humanize(material)),material)
        };
    }
}
