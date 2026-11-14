namespace SprocketExpandedBallistics;

internal static class CombatMaterialReason
{
    /// <summary>
    /// 0.22.12: the cassette row gets the cassette's own compact text instead of
    /// the sandwich wording. "Cassette NERA cassette: APDS −9,4 % (NERA ×0.5);
    /// back, t = 99,6 mm, θ = 12,7°" becomes "Cassette NERA: APDS −44 mm (×0.5),
    /// back 99,6 mm @12,7°". The mm reduction is the event's own before minus after.
    /// </summary>
    internal static string Cassette(CombatReportEvent e)
    {
        var text=CombatReportTruth.EventText(e.Text);
        if(!text.StartsWith("Cassette ",StringComparison.OrdinalIgnoreCase))return text;
        var body=text["Cassette ".Length..];
        var colon=body.IndexOf(':');
        var name=(colon>=0?body[..colon]:body).Trim();
        const string suffix=" cassette";
        if(name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))name=name[..^suffix.Length].TrimEnd();
        var details=colon>=0?body[(colon+1)..].Trim():"";
        var segments=details.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        var head=segments.Length>0?segments[0]:"";
        var space=head.IndexOf(' ');
        var threat=space>0?head[..space]:head;
        var factor="";
        var open=head.IndexOf('(');
        if(open>=0)
        {
            var close=head.IndexOf(')',open+1);
            if(close>open)
            {
                var inner=head[(open+1)..close];
                var times=inner.IndexOf('\u00D7');
                if(times>=0)factor=" ("+inner[times..].Trim()+")";
            }
        }
        var reduction="";
        if(e.BeforeMm is {} before&&e.AfterMm is {} after&&double.IsFinite(before)&&double.IsFinite(after)&&before>=after)
            reduction="\u2212"+Math.Max(0,before-after).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+" mm";
        if(reduction.Length==0)
        {
            var start=head.IndexOfAny(new[]{'\u2212','-'});
            if(start>=0)
            {
                var end=head.IndexOf(' ',start+1);
                reduction=(end>start?head[start..end]:head[start..]).Trim();
            }
        }
        var facts="";
        foreach(var segment in segments.Skip(1))
        {
            var side=segment.StartsWith("front",StringComparison.OrdinalIgnoreCase)?"front":
                segment.StartsWith("back",StringComparison.OrdinalIgnoreCase)?"back":
                segment.StartsWith("edge",StringComparison.OrdinalIgnoreCase)?"edge":null;
            var t=Between(segment,"t = "," mm");
            var theta=Between(segment,"\u03B8 = ","\u00B0");
            if(side==null||t==null||theta==null)continue;
            facts=", "+side+" "+t+" mm @"+theta+"\u00B0";
            break;
        }
        var effective=Between(text,"\u03B8_eff \u2248 ","\u00B0");
        var effectiveText=effective!=null?", \u03B8_eff "+effective+"\u00B0":
            text.Contains("\u03B8_eff \u2265 80\u00B0",StringComparison.Ordinal)?", \u03B8_eff \u2265 80\u00B0":"";
        // 0.22.17: one angle per tilt group and the source of the thickness stay visible in the compact line.
        if(Between(text,"tilt groups [","]") is {} groups)effectiveText+=", tilt groups "+groups;
        var source=segments.FirstOrDefault(s=>s.StartsWith("thickness: ",StringComparison.Ordinal));
        var sourceText=source==null?"":" ["+source["thickness: ".Length..].Trim()+"]";
        return ("Cassette "+name+": "+threat+" "+reduction+factor+facts+effectiveText+sourceText).TrimEnd();
    }
    internal const string SpacedPrefix="Spaced plate";
    /// <summary>
    /// 0.22.14: a spaced-plate reaction ("Spaced plate: −3 % (yaw)") keeps its own text instead of the sandwich wording
    /// "Backing confirmed | extra reduction N mm", followed by the event's own reduction: "Spaced plate: −3 % (yaw), −13,4 mm".
    /// The number uses the same culture as the recorded percentage.
    /// </summary>
    internal static string Spaced(CombatReportEvent e)
    {
        var text=CombatReportTruth.EventText(e.Text);
        if(e.BeforeMm is {} before&&e.AfterMm is {} after&&double.IsFinite(before)&&double.IsFinite(after)&&Math.Round(before-after,1)>0)
            text+=", \u2212"+(before-after).ToString("0.#",System.Globalization.CultureInfo.CurrentCulture)+" mm";
        return text;
    }
    private static string? Between(string text,string start,string end)
    {
        var i=text.IndexOf(start,StringComparison.Ordinal);
        if(i<0)return null;
        i+=start.Length;
        var j=text.IndexOf(end,i,StringComparison.Ordinal);
        return j>i?text[i..j].Trim():null;
    }
    internal static string From(CombatReportEvent e)
    {
        var text=CombatReportTruth.EventText(e.Text);
        bool Has(string value)=>text.Contains(value,StringComparison.OrdinalIgnoreCase);
        // Older exports combine two possible causes; retain that uncertainty.
        if(Has("date is later than the vehicle, or"))return "Cassette unavailable: not loaded or vehicle date too early";
        if(Has("not loaded")||Has("Technology missing")||Has("Missing cassette preset"))return "Cassette not loaded - restart Sprocket";
        if(Has("not in vehicle technology"))return text.Split(';')[0].TrimEnd('.');
        if(Has("no steel backing")||Has("awaiting steel backing"))return "Sandwich: no steel backing confirmed";
        if(Has("blended native block"))return "Cassette: exact depth unavailable in blended block";
        if(Has("declared face")&&Has("unavailable"))return "Material: declared face unavailable";
        if(Has("passive values differ")||Has("settings differ"))return "Material settings differ from reaction binding";
        // Preserve a specific unrecognised reason instead of replacing it with a promise of details.
        return text.Split(';')[0].TrimEnd('.');
    }
}

internal static class CombatReportMeasurement
{
    internal static double? ActiveEraThickness(ModularCellSpec spec,int stage,double scaleY)
    {
        var metres=stage==1?spec.Stage1TopM-spec.Stage1BottomM:stage==2?spec.Stage2TopM-spec.Stage2BottomM:double.NaN;
        var mm=metres*Math.Abs(scaleY)*1000;
        return double.IsFinite(mm)&&mm>0?mm:null;
    }
    internal static double? NormalThickness(double? declared,double a,double b,double c)
    {
        if(declared is >0&&double.IsFinite(declared.Value))return declared;
        // Native samples are normal thickness in mm. Never turn the -1 sentinel into a measurement.
        return a>0&&b>0&&c>0&&double.IsFinite(a)&&double.IsFinite(b)&&double.IsFinite(c)?(a+b+c)/3:null;
    }
}
