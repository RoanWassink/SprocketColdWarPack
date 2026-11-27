using BepInEx.Configuration;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// Combat report on a Sprocket UI Kit panel (0.22.3): the last ten impacts with the hit-panel colour and
/// result, each with an expandable layer table. "Save combat report" writes the same HTML/JSON as before.
/// The JSON Editor session stays as the fallback behind <c>[Combat report] Use UI Kit = false</c>.
/// </summary>
internal static class CombatReportPanel
{
    private static ConfigEntry<bool>? useKit;
    internal static bool UseKit=>useKit?.Value!=false;
    internal static void Configure(ConfigFile config)
    {
        useKit=config.Bind("Combat report","Use UI Kit",true,
            "Show the combat report as a UI Kit panel. false = the older JSON Editor session (kept as a fallback).");
        // 0.22.41: the forensic screen draws the layers of a composer preset from the loaded snapshot (read only, never per frame).
        CombatCompositeLayers.Configure(id=>RuntimeArmourResponses.Snapshot.Cassettes is {} cassettes&&cassettes.TryGetValue(id,out var model)?model.Preset:null,CompositeLabel);
    }
    private static string? CompositeLabel(string id)
    {
        try{var m=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(x=>x.Id==id);return m==null?null:SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(m);}
        catch(Exception){return null;}
    }
    internal static bool IsOpen=>panel?.IsOpen==true;
    private static Panel? panel;
    private static IReadOnlyList<CombatImpactView> impacts=Array.Empty<CombatImpactView>();
    private static (int Count,int Last,int Revision) seen=(-1,-1,-1);
    private static string countText="0";
    private static readonly HashSet<string> expanded=new(StringComparer.Ordinal);
    private static string[] Columns=>CombatReportView.Columns;
    private static float[] Widths=>CombatReportView.Widths;

    /// <summary>Keybind: open or close.</summary>
    internal static bool Toggle()
    {
        try{Ensure();if(panel!.IsOpen){panel.Close();return true;}return Open();}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Combat report] UI Kit panel unavailable: "+ex.Message);return false;}
    }
    internal static bool Open()
    {
        try{Ensure();seen=(-1,-1,-1);_=Current();if(impacts.Count>0&&expanded.Count==0)expanded.Add(impacts[0].Key);return panel!.Open();}
        catch(Exception ex){Plugin.ModLog.LogWarning("[Combat report] UI Kit panel unavailable: "+ex.Message);return false;}
    }
    internal static void Clear(){CombatForensicPanel.Clear();try{if(panel!=null)UiKit.DestroyPanel(panel);}catch(Exception){}panel=null;expanded.Clear();impacts=Array.Empty<CombatImpactView>();seen=(-1,-1,-1);}
    /// <summary>Rebuilt only when the history changes (count, newest shot, or post-commit revision), never per frame.</summary>
    private static IReadOnlyList<CombatImpactView> Current()
    {
        var shots=RuntimeDamageFeed.History.Shots;var key=(shots.Count,shots.Count>0?shots[^1].Number:-1,RuntimeDamageFeed.History.Revision);
        if(key==seen)return impacts;
        seen=key;impacts=CombatReportView.Build(shots);countText=impacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return impacts;
    }
    private static void Ensure()
    {
        if(panel!=null)return;
        var p=UiKit.CreatePanel(BallisticsKeybindMetadata.Owner,"combat-report",CombatReportView.PanelTitle,CombatReportView.PanelSubtitle,1400,820);
        panel=p;
        p.AllowedContexts=UiContexts.All;
        p.StayOpenWhilePaused=true;
        var c=p.Columns(0)[0];
        var head=c.Section(CombatReportView.SectionTitle,()=>countText);
        head.Label(CombatReportView.HelpText,TextStyle.Small).Wrap();
        var scroll=c.ScrollView().Fill();
        scroll.Label(CombatReportView.EmptyText,TextStyle.Body).VisibleWhen(()=>Current().Count==0);
        scroll.Repeat<CombatImpactView>(Current,Impact,6);
        var f=c.Footer();
        var row=f.Row(8);
        row.Label(()=>status,TextStyle.Small).Flex();
        row.Button("Expand all",()=>{foreach(var i in Current())expanded.Add(i.Key);}).Compact();
        row.Button("Collapse all",expanded.Clear).Compact();
        row.Button("Save combat report",Save,ButtonStyle.Primary).Tooltip(()=>"Write HTML and JSON of this session to "+CombatReportStore.DirectoryPath);
        row.Button("Open HTML",OpenHtml).Tooltip("Save, then open the HTML report in your browser.");
        row.Button("Close",()=>p.Close(),ButtonStyle.Quiet);
    }
    private static string status="";
    private static void Impact(CombatImpactView impact,Stack s)
    {
        var b=impact.Block;var key=impact.Key;
        var card=s.Card().Stripe(()=>b.Colour).OnClick(()=>{if(!expanded.Add(key))expanded.Remove(key);}).MinHeight(52);
        var st=card.Stack(4);
        if(impact.Forensic is {} forensic)st.Row(8).Button("Forensic",()=>CombatForensicPanel.Open(forensic,Current()),ButtonStyle.Quiet).Tooltip(CombatReportView.ForensicTooltip);
        // 0.22.7, one header line: #11 OUT · ammunition → target · 530 → 406 mm · [PENETRATED]  ... show layers.
        // The result and TANK DESTROYED chips have a fixed width that holds their longest word, so nothing is cut off.
        var head=st.Row(8);
        head.Label(b.NumberText,TextStyle.Label).Colour(b.Colour);
        if(b.Direction.Length>0)head.Chip(b.Direction,ChipStyle.Tag);
        head.Label(b.Ammo,TextStyle.Label);
        if(impact.Target.Length>0)head.Label("→ "+impact.Target,TextStyle.Label).Colour("#f3cf86").Tooltip(CombatReportView.TargetTooltip);
        if(b.Tandem)head.Chip("TANDEM",ChipStyle.Tag);
        head.Label(b.Values,TextStyle.Value);
        head.Spacer();
        head.Chip(b.Result,UiKit.Colour(b.Colour)).Width(CombatReportView.ResultChipWidth);
        if(impact.Destroyed)head.Chip(CombatReportView.DestroyedText,UiKit.Colour("#e5533d")).Width(CombatReportView.DestroyedChipWidth).Tooltip(CombatReportView.DestroyedTooltip);
        head.Label(()=>expanded.Contains(key)?CombatReportView.HideLayers:CombatReportView.ShowLayers,TextStyle.Faint).Width(84).AlignRight();
        // One summary line of the damage (crew and parts, grouped by status); it wraps instead of being cut off.
        foreach(var line in impact.SummaryLines)st.Label(line,TextStyle.Small).Colour("#ffcbb3").Wrap();
        if(impact.BlastHeadline.Length>0)st.Label(impact.BlastHeadline,TextStyle.Small).Colour(b.Result==HitBlockBuilder.Breached?"#ffac91":"#f3cf86").Wrap();
        if(b.Shooter.Length>0)st.Label(CombatReportPresentation.Escape(b.Shooter),TextStyle.Small).Wrap();
        var table=st.Stack(2);
        table.VisibleWhen(()=>expanded.Contains(key));
        table.Label(impact.Summary,TextStyle.Small).Colour("#b8ad94");
        if(impact.BlastLines.Length>0)
        {
            table.Label("BLAST ASSESSMENT",TextStyle.Section);
            foreach(var bl in impact.BlastLines)table.Label(bl,TextStyle.Small).Colour(bl.Contains("BREACHED",StringComparison.Ordinal)?"#ffac91":"#f3cf86").Wrap();
        }
        var header=table.Row(6);
        for(int i=0;i<Columns.Length;i++)Cell(header,Columns[i],i,TextStyle.Small).Colour("#b8ad94");
        foreach(var l in impact.Layers)
        {
            var r=table.Row(6);
            Cell(r,l.Index,0,TextStyle.Value).Colour(l.Colour);
            NameCell(r,l.Name);Cell(r,l.Material,2,TextStyle.Small);Cell(r,l.Thickness,3,TextStyle.Small);Cell(r,l.Angle,4,TextStyle.Small);
            Cell(r,l.BeforeAfter,5,TextStyle.Value);ReactionCell(r,l.Reaction);
            // Damage that arose in a layer other than the last one: a short note under that row.
            if(l.Note.Length>0){var n=table.Row(6);n.Label("",TextStyle.Small).Width(Widths[0]);n.Label(l.Note,TextStyle.Small).Colour("#ffcbb3");}
        }
        if(impact.Layers.Length==0)table.Label(CombatReportView.NoLayersText,TextStyle.Small);
        foreach(var diagnostic in impact.Diagnostics)table.Label(diagnostic,TextStyle.Small).Wrap();
        // 0.22.23: the penetration sequence of this impact (only built for an expanded impact, as the whole table is).
        if(b.Waterfall!=null){table.Label(CombatReportView.SequenceTitle,TextStyle.Section);WaterfallChart.Add(table,b.Waterfall,170);}
    }
    /// <summary>Reaction (ERA, material, cassette; empty for plain steel): wrapped over up to three lines of the fixed column, never cut.</summary>
    private static void ReactionCell(Row row,string text)
    {
        var lines=CombatReportView.ReactionLines(text);
        if(lines.Length<=1){Cell(row,text,6,TextStyle.Small).Tooltip(text.Length>0?text:null);return;}
        var stack=row.Stack(1).Width(Widths[6]);
        foreach(var line in lines)stack.Label(line,TextStyle.Small).Tooltip(text);
    }
    private static void NameCell(Row row,string text)
    {
        if(!text.Contains('\n')){Cell(row,text,1,TextStyle.Value);return;}
        var stack=row.Stack(1).Width(Widths[1]);
        foreach(var line in text.Split('\n'))stack.Label(line,TextStyle.Value).Tooltip(text);
    }
    /// <summary>A fixed-width cell; numbers are right-aligned in the header and in every row, so the columns line up.</summary>
    private static Label Cell(Row row,string text,int column,TextStyle style)
    {
        var l=row.Label(text,style).Width(Widths[column]);
        return CombatReportView.RightAligned[column]?l.AlignRight():l;
    }
    private static void Save()
    {
        try{CombatReportStore.Save(RuntimeDamageFeed.History);status="Saved HTML and JSON to "+CombatReportStore.DirectoryPath;panel?.SetStatus(status,StatusKind.Success);}
        catch(Exception ex){status="Not saved: "+ex.Message;panel?.SetStatus(status,StatusKind.Error);}
    }
    private static void OpenHtml()
    {
        Save();
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(CombatReportStore.DirectoryPath,"last-session.html")){UseShellExecute=true});}
        catch(Exception ex){panel?.SetStatus("Cannot open the HTML report: "+ex.Message,StatusKind.Error);}
    }
}
