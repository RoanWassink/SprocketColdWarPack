using System.Globalization;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// One reached layer of an impact, as table text (built once per report refresh). <paramref name="Note"/> (0.22.7) is a short
/// "↳ motor destroyed" line shown under the row, only when damage arose in a layer other than the last one; the damage of the whole
/// impact stays in the summary line. <paramref name="Reaction"/> is empty for plain steel.
/// </summary>
internal sealed record CombatLayerRow(string Index,string Name,string Material,string Thickness,string Angle,string BeforeAfter,string Reaction,string Note,string Colour);
/// <summary>One impact in the Kit combat report: the hit-panel block (colour, result) plus its layer table.</summary>
internal sealed class CombatImpactView
{
    internal string Key="";internal HitBlock Block=new();
    internal ForensicModel? Forensic;
    /// <summary>The context line "Outgoing · 1265 m/s · 1 layer" (the penetration is in the header).</summary>
    internal string Summary="";
    /// <summary>Blueprint name of the vehicle that was hit ("" when unknown) and the TANK DESTROYED flag.</summary>
    internal string Target="";internal bool Destroyed;
    /// <summary>The header as one line of plain text: "#11 OUT · APDS (tungsten, late) → M40A1 «Ridgway» · 530 → 406 mm · PENETRATED" (HTML heading, tooltips).</summary>
    internal string Head="";
    /// <summary>"Crew 2/4 killed · ammo rack, motor destroyed; gun damaged" (0.22.7): one summary of the whole impact; empty when nothing was damaged.</summary>
    internal string SummaryLine="";
    /// <summary>The summary line cut into lines of at most <see cref="CombatReportView.LineColumns"/> characters at word breaks.</summary>
    internal string[] SummaryLines=Array.Empty<string>();
    internal string BlastHeadline="";
    internal string[] BlastLines=Array.Empty<string>();
    internal CombatLayerRow[] Layers=Array.Empty<CombatLayerRow>();
    internal string[] Diagnostics=Array.Empty<string>();
}

/// <summary>
/// Pure builder for the Kit combat report (0.22.3): the last ten impacts, newest first, with the same
/// colour and result word as the hit panel (<see cref="HitBlockBuilder"/>) and one row per reached layer
/// from <see cref="CombatHitProjection"/>. 0.22.7 tidies it: a one-line header, one damage summary, one context line,
/// and a layer table with fixed column widths (<see cref="Columns"/> / <see cref="Widths"/>) that line up with their header.
/// </summary>
internal static class CombatReportView
{
    internal const int Shown=10,LineColumns=118,MaxLines=8;
    /// <summary>Layer table: # | Plate | Material | Thickness | Angle | Before → after | Reaction. All widths are fixed (no flexible column), so header and rows always line up.</summary>
    internal static readonly string[] Columns={"#","Plate","Material","Thickness","Obliquity","Pen. in → out mm","Reaction"};
    /// <summary>0.22.41: the fixed texts of the Kit report panel, here so the language test reads exactly what the panel shows.</summary>
    internal const string PanelTitle="Combat report",PanelSubtitle="Expanded Ballistics · last 10 impacts",SectionTitle="Impacts",
        HelpText="Newest first. Colour and result as on the hit panel. Click an impact to show or hide its layers: plate, material, normal thickness, obliquity, penetration at entry and exit, and any material response.",
        EmptyText="No impacts yet. Impacts of the last session remain available in the designer.",
        ForensicTooltip="Open the cross-section to scale, the share of penetration absorbed per layer, findings and advice.",
        TargetTooltip="Name of the target vehicle.",DestroyedTooltip="All crew of the target were killed.",
        NoLayersText="No armour layer was measured for this impact.",SequenceTitle="PENETRATION SEQUENCE",
        ShowLayers="show layers",HideLayers="hide layers";
    internal static readonly string[] PanelButtons={"Forensic","Expand all","Collapse all","Save combat report","Open HTML","Close"};
    internal static readonly float[] Widths={40,190,250,90,70,170,360};
    /// <summary>Right-aligned columns (numbers), in the header and in every row.</summary>
    internal static readonly bool[] RightAligned={false,false,false,true,true,true,false};
    /// <summary>Characters the table text of the small and the value style take per pixel of column width (a conservative estimate).</summary>
    internal const float PixelsPerCharacter=7.4f,CellPadding=6;
    /// <summary>How many characters of the 'Reaction' column fit on one line; longer text wraps to <see cref="ReactionMaxLines"/> lines.</summary>
    internal static int Fit(int column)=>Math.Max(4,(int)((Widths[column]-CellPadding)/PixelsPerCharacter));
    internal const int ReactionMaxLines=3;
    /// <summary>Fixed chip width in pixels for the longest word it must hold (about 10 px per capital letter plus padding), so the result never gets cut off ("PENETRAT…").</summary>
    internal static float ChipWidth(string longest)=>MathF.Ceiling(longest.Length*10f+28f);
    internal static readonly float ResultChipWidth=ChipWidth(new[]{HitBlockBuilder.Penetrated,HitBlockBuilder.Stopped,HitBlockBuilder.StoppedSpall,HitBlockBuilder.StoppedBlast,HitBlockBuilder.Breached,HitBlockBuilder.Ricochet,HitBlockBuilder.NoArmour}.OrderByDescending(s=>s.Length).First());
    internal const string DestroyedText="TANK DESTROYED";
    internal static readonly float DestroyedChipWidth=ChipWidth(DestroyedText);
    private static readonly Regex NumericName=new(@"^\s*#?\d+\s*$",RegexOptions.CultureInvariant);
    private static string N(double? v,string format="0.#")=>v is {} x&&double.IsFinite(x)?x.ToString(format,CultureInfo.InvariantCulture):"—";
    private static string Clean(string? text,int columns)=>CombatHitSummaryModel.Fit(CombatReportPresentation.Escape(text??""),columns);
    /// <summary>A plate that the game numbers only ("1") reads "Plate #entity" instead; a real component or plate name stays.</summary>
    internal static string PlateName(string? name,int entity)=>string.IsNullOrWhiteSpace(name)||NumericName.IsMatch(name)||name.Equals("Unnamed Structure",StringComparison.OrdinalIgnoreCase)?"Plate #"+entity.ToString(CultureInfo.InvariantCulture):name.Trim();
    internal static bool IsNumericName(string? name)=>!string.IsNullOrWhiteSpace(name)&&NumericName.IsMatch(name);
    /// <summary>
    /// Greedy word wrap into lines of at most <paramref name="columns"/> characters (longer words are cut), at most <see cref="MaxLines"/>
    /// lines. The kit in the game (0.1.2) does not wrap a Label created inside a Repeat template and cuts it with "…", so long
    /// text is split here and shown as one label per line; it also reads correctly when the kit does wrap (0.1.3).
    /// </summary>
    internal static string[] Lines(string text,int columns,int maxLines=MaxLines)
    {
        columns=Math.Max(8,columns);var lines=new List<string>();var current=new System.Text.StringBuilder();
        void Flush(){if(current.Length>0)lines.Add(current.ToString());current.Clear();}
        foreach(var raw in (text??"").Split(' ',StringSplitOptions.RemoveEmptyEntries))
        {
            var word=raw;
            while(word.Length>columns){Flush();lines.Add(word[..columns]);word=word[columns..];}
            if(current.Length>0&&current.Length+1+word.Length>columns)Flush();
            if(current.Length>0)current.Append(' ');
            current.Append(word);
        }
        Flush();
        if(lines.Count>maxLines){lines=lines.Take(maxLines).ToList();lines[^1]=CombatHitSummaryModel.Fit(lines[^1]+" …",columns);}
        return lines.ToArray();
    }
    // Each material piece starts a separate paragraph; never discard an equivalent thickness.
    internal static string[] ReactionLines(string text)=>text.Split('\n').SelectMany(line=>Lines(line,Fit(6),int.MaxValue)).ToArray();
    private static string Plate(CombatHitView v,CombatLayerPart[] parts)
        =>v.Layer?.AirGapMm is {} gap?GapName(v.Layer,gap):parts.Length==1?PlateName(parts[0].Name,parts[0].Entity)+CombatLayerContinuity.Suffix(v.Crossing):parts.Length>1?$"{CombatReportLanguage.ArrayName} ({parts.Length} plates)":v.Layer?.Parts.FirstOrDefault() is {} first?ModuleName(first)+CombatLayerContinuity.Suffix(v.Crossing):CombatReportLanguage.Module(v.Name);
    private static string PlateCell(CombatHitView v,CombatLayerPart[] parts)
    {
        var name=Plate(v,parts);
        var lines=new List<string>();
        var suffix=CombatLayerContinuity.Suffix(v.Crossing);
        string? crossing=null,tag=null;
        if(suffix.Length>0&&name.EndsWith(suffix,StringComparison.Ordinal))
        {
            name=name[..^suffix.Length].TrimEnd();
            crossing=suffix.TrimStart();
        }
        bool derived=name.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal);
        if(derived)name=name[..^LayeredPlateReport.DerivedLabel.Length].TrimEnd();
        if(name.EndsWith(" [Solid block]",StringComparison.Ordinal))
        {
            name=name[..^" [Solid block]".Length].TrimEnd();
            tag="[Solid block]";
        }
        else if(derived)
        {
            tag="(from path)";
        }
        if(tag==null&&name.StartsWith(CombatReportLanguage.ArrayName+" (",StringComparison.Ordinal)&&name.EndsWith(")",StringComparison.Ordinal))
        {
            int open=CombatReportLanguage.ArrayName.Length+1;
            tag=name[open..];
            name=CombatReportLanguage.ArrayName;
        }
        lines.Add(Clean(name,Fit(1)));
        if(tag!=null)lines.Add(Clean(tag,Fit(1)));
        if(crossing!=null)lines.Add(crossing);
        return string.Join("\n",lines);
    }
    /// <summary>0.22.41: a vehicle module or crew position as the report names it ("ammunition stowage", "main gun", "Crew").</summary>
    internal static string ModuleName(CombatLayerPart part)=>part.Armour?PlateName(part.Name,part.Entity):CombatReportLanguage.Module(IsNumericName(part.Name)?PlateName(part.Name,part.Entity):part.Name);
    /// <summary>"Air gap 54.5 mm before Composite armour plate", with the same plate choice as the projection and readable module names.</summary>
    internal static string GapName(CombatLayerInfo layer,double gap)
    {
        var before=layer.Parts.FirstOrDefault(p=>p.MaterialId?.StartsWith("ebCassette_",StringComparison.Ordinal)==true)
            ??layer.Parts.LastOrDefault(p=>p.Name.Contains("Composite",StringComparison.OrdinalIgnoreCase))
            ??layer.Parts.LastOrDefault();
        return $"Air gap {gap.ToString("0.###",CultureInfo.InvariantCulture)} mm before {(before==null?"plate":ModuleName(before))}";
    }
    /// <summary>Reaction text: only something other than plain steel (ERA, a material reaction, a cassette); steel loss is the Before → after column.</summary>
    private static string Reaction(CombatHitView v)
    {
        // 0.22.41: the generic sandwich wording is replaced by the block's own recorded material calculation where there is one.
        if(v.Reaction.StartsWith("Backing confirmed",StringComparison.Ordinal))
        {
            var lines=v.Details.Where(e=>e.Kind is "materialReaction" or "materialStop"&&ProtectionLine.Is(e.Text.Split(';')[0])).Select(e=>CombatReportLanguage.Reaction(e.Text)).Distinct().ToArray();
            if(lines.Length>0)return string.Join(" · ",lines.Concat(EraRejectionReport.Lines(v.Details)));
        }
        if(v.Reaction.Length>0)return CombatReportLanguage.Reaction(v.Reaction);
        return CombatReportLanguage.Scrub(string.Join(" · ",HitBlockBuilder.Tokens(v.Details.Where(e=>!MaterialIntervalDisplay.Is(e)).ToArray(),Array.Empty<CombatHitView>()).Where(t=>!t.StartsWith("steel ",StringComparison.Ordinal))));
    }
    /// <summary>The "Pen. in → out mm" cell of a projected layer ("94 → 94", "63 → 0 (stopped)", "190 → ricochet").</summary>
    internal static string BeforeAfter(CombatHitView v)
    {
        var exit=v.Status.StartsWith("Stopped",StringComparison.Ordinal)?"0 (stopped)":v.Status=="Deflected"?"ricochet":v.After is {} a?N(a,"0"):"?";
        return $"{N(v.Before,"0")} → {exit}";
    }
    /// <summary>The header as plain text, in the order of the panel: number, direction · ammunition → target · penetration · result.</summary>
    internal static string HeadText(HitBlock block,string target,bool destroyed)
    {
        var parts=new List<string>{block.NumberText+(block.Direction.Length>0?" "+block.Direction:""),block.Ammo+(target.Length>0?" → "+target:"")};
        if(block.Tandem)parts.Add("TANDEM");
        parts.Add(block.Values);parts.Add(block.Result);
        if(destroyed)parts.Add(DestroyedText);
        return string.Join(" · ",parts);
    }
    internal static IReadOnlyList<CombatImpactView> Build(IReadOnlyList<CombatReportShot> shots,int count=Shown)
    {
        var list=new List<CombatImpactView>();
        foreach(var raw in shots.Where(s=>s!=null).TakeLast(Math.Max(0,count)).Reverse())
        {
            var shot=CombatReportTruth.Normalize(raw);
            var views=CombatHitProjection.Build(shot);
            var rows=new List<CombatLayerRow>();
            // 0.22.41 (user decision 08-11): a layer with a 0 mm measured path that leaves the penetration unchanged is not shown; numbering stays continuous.
            var shown=views.Where(v=>v.Status!="Diagnostic only"&&!CombatZeroSpan.Is(v)).ToArray();
            for(int i=0;i<shown.Length;i++)
            {
                var v=shown[i];
                var parts=v.Layer?.Parts.Where(p=>p.Armour).ToArray()??Array.Empty<CombatLayerPart>();
                var air=v.Layer?.AirGapMm!=null;
                var material=air?"air gap":parts.Length==0?(v.Layer==null?"—":v.Layer.Parts.Any(p=>p.Name.StartsWith("Crew",StringComparison.OrdinalIgnoreCase))?"crew":"internal module"):string.Join(" / ",parts.Select(p=>CombatReportLanguage.Material(p) is {Length:>0} m?m:"armour").Distinct());
                IReadOnlyList<double> walls=v.Walls.Count>1&&v.Walls.All(w=>Math.Abs(w-v.Walls[0])<1e-9)?new[]{v.Walls[0]}:v.Walls;
                var thickness=air?"—":parts.Length==1?(walls.Count>1?string.Join(" + ",walls.Select(w=>N(w,"0")))+" mm":N(walls.Count==1?walls[0]:parts[0].ThicknessMm,"0")+" mm"):parts.Length>1?parts.Length+" plates":"—";
                var angle=air?"—":parts.Length==1?N(parts[0].AngleDegrees,"0")+"°":parts.Length>1?"multiple":"—";
                // Damage that arose in an earlier layer than the last one is noted under that layer; the last layer's damage is the summary line.
                var note="";
                if(i<shown.Length-1&&shown[i+1].Stage==v.Stage)
                {
                    var own=CombatDamageSummary.From(v.Details).Line(null);
                    if(own.Length>0)note=Clean("↳ "+own,LineColumns);
                }
                rows.Add(new((v.Stage=="Precursor"?"P":"")+(rows.Count+1).ToString("00",CultureInfo.InvariantCulture),PlateCell(v,parts),Clean(material,Fit(2)),thickness,angle,
                    BeforeAfter(v),CombatReportPresentation.Escape(Reaction(v)),note,parts.Length==1&&parts[0].MaterialId is "ebWood" or "ebRubber" or "ebFabric" or "ebGlass"?CassettePalette.Hex(parts[0].MaterialId!):CombatHitSummaryModel.Category(v).Colour));
            }
            var block=HitBlockBuilder.Build(shot);
            var damage=CombatDamageSummary.From(shot.Events);
            var line=CombatReportPresentation.Escape(damage.Line(shot.CrewTotal));
            var target=CombatReportPresentation.Escape(CombatHitSummaryModel.Fit(shot.Target??"",48));var destroyed=CombatDamageSummary.IsDestroyed(shot);
            var blastLines=BuildBlastLines(shot.Events);
            list.Add(new CombatImpactView
            {
                Key=shot.Number.ToString(CultureInfo.InvariantCulture),Block=block,Layers=rows.ToArray(),Forensic=CombatForensics.Build(shot),
                Diagnostics=shot.Events.Where(CombatReportNoise.IsEndedPlate).Select(e=>CombatReportPresentation.Escape(CombatReportLanguage.Diagnostic(e.Text))).ToArray(),
                BlastLines=blastLines,BlastHeadline=BuildBlastHeadline(shot.Events,blastLines),
                Target=target,Destroyed=destroyed,Head=HeadText(block,target,destroyed),
                SummaryLine=line,SummaryLines=line.Length==0?Array.Empty<string>():Lines(line,LineColumns),
                Summary=(shot.Direction=="Incoming"?"From: "+CombatReportPresentation.Escape(CombatReportTruth.Text(shot.Shooter,"unknown"))+" · ":"")+$"{CombatReportPresentation.Escape(shot.Direction)} · {N(shot.SpeedMps,"0")} m/s · {rows.Count} layer{(rows.Count==1?"":"s")}"
                    +(shot.OmittedEvents>0?$" · {shot.OmittedEvents} diagnostics omitted (full list in the saved report)":"")
            });
        }
        return list;
    }
    private static string[] BuildBlastLines(IReadOnlyList<CombatReportEvent> events)
    {
        var list=new List<string>();
        foreach(var e in events)
            if(e.Kind=="blastSummary")list.Add(CombatReportPresentation.Escape(CombatReportLanguage.BlastLine(e.Text)));
        foreach(var e in events)
            if(e.Kind=="blastPlate"&&e.Text.Contains("BREACHED",StringComparison.Ordinal))list.Add(CombatReportPresentation.Escape(CombatReportLanguage.BlastLine(e.Text)));
        foreach(var e in events)
            if(e.Kind=="blastPlate"&&!e.Text.Contains("BREACHED",StringComparison.Ordinal))list.Add(CombatReportPresentation.Escape(CombatReportLanguage.BlastLine(e.Text)));
        foreach(var e in events)
            if(e.Kind=="blastEra")list.Add(CombatReportPresentation.Escape(CombatReportLanguage.BlastLine(e.Text)));
        return list.ToArray();
    }
    private static string BuildBlastHeadline(IReadOnlyList<CombatReportEvent> events,string[] blastLines)
    {
        if(blastLines.Length==0)return "";
        var summary=events.FirstOrDefault(e=>e.Kind=="blastSummary");
        var breached=events.FirstOrDefault(e=>e.Kind=="blastPlate"&&e.Text.Contains("BREACHED",StringComparison.Ordinal));
        var resisted=events.FirstOrDefault(e=>e.Kind=="blastPlate"&&!e.Text.Contains("BREACHED",StringComparison.Ordinal));
        var era=events.FirstOrDefault(e=>e.Kind=="blastEra");
        var parts=new List<string>();
        if(summary!=null)parts.Add(CombatReportLanguage.BlastLine(summary.Text));
        if(breached!=null)parts.Add(CombatReportLanguage.BlastLine(breached.Text));
        else if(resisted!=null)parts.Add(CombatReportLanguage.BlastLine(resisted.Text));
        if(era!=null)parts.Add(CombatReportLanguage.BlastLine(era.Text));
        return CombatReportPresentation.Escape(string.Join(" · ",parts));
    }
}
