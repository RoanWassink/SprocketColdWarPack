using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.41: report language and presentation only. Zero-span layers hidden with continuous numbering, the After value of a block without a
/// residual event (#122: 94 → 94), scientific wording with a forbidden-term list, no uncertainty statements in the forensic screen, the
/// constituents of a composer preset inside its plate band, and a golden proof that protection and runtime are the 0.22.40 bytes.
/// </summary>
internal static class Fix02241Tests
{
    /// <summary>The only source files 0.22.41 may change (presentation, version, release notes, the two new presentation files).</summary>
    internal static readonly string[] Presentation={"CombatReportLanguage.cs","CombatCompositeLayers.cs","CombatHitProjection.cs","CombatReportView.cs","CombatReportPanel.cs","CombatReportHtml.cs",
        "CombatForensics.cs","CombatForensicGeometry.cs","CombatForensicDrawing.cs","CombatForensicHtml.cs","CombatForensicPanel.cs","CombatDamageSummary.cs","HitPanelModel.cs",
        "PenetrationWaterfall.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    internal static readonly (int Shot,string HitId)[] ExpectedZeroSpans={(122,"1854486707808:97:10:10"),(123,"1854214803456:7:1:1"),(136,"1854215031936:7:15:15"),(165,"1855234184288:71:8:8"),(165,"1855234184288:101:5:5")};
    private static CombatReportShot[] Load(string path)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        return JsonSerializer.Deserialize<CombatReportShot[]>(json.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var callback=check;check=(ok,text)=>{count++;callback(ok,"fix02241: "+text);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.cassettes.json"))).Presets;
        // The game reads the loaded snapshot (built-in and user presets); the tests use the shipped default catalogue.
        CombatCompositeLayers.Configure(id=>presets.FirstOrDefault(p=>p.MaterialId==id));
        try{Body(check,root,output,preview,presets);}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"FIX41-RESULT.json"),JsonSerializer.Serialize(new{checks=count,liveGameTest=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FIX02241 PASS: {count} checks.");
    }
    private static void Body(Action<bool,string> check,string root,string output,string preview,CassettePreset[] presets)
    {
        var parent=Fix02228Tests.SessionRoot();
        var sets=new (string Name,CombatReportShot[] Shots)[]{
            ("zero-loss-1107",Load(Path.Combine(parent,"zero-loss-1107/last-session.json"))),
            ("forensic-fixtures",Load(Path.Combine(parent,"forensic-fixtures/last-session.json"))),
            ("forensic-fixtures-1108",Load(Path.Combine(parent,"forensic-fixtures-1108/last-session.json")))};
        check(sets[0].Shots.Length==128&&sets[1].Shots.Length==2&&sets[2].Shots.Length==4,"fixtures: 128 + 2 + 4 real shots");
        var summary=new StringBuilder();
        ZeroSpans(check,sets,output,summary);
        After(check,sets[0].Shots,summary);
        Shares(check,sets,summary);
        Sublayers(check,sets,presets,summary);
        Language(check,sets,output,summary);
        Previews(check,sets,preview);
        File.WriteAllText(Path.Combine(output,"FIX41-SUMMARY.txt"),summary.ToString());
    }

    // ---- 1. zero spans ----
    private static void ZeroSpans(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,string output,StringBuilder summary)
    {
        var ledger=new StringBuilder("fixture\tshot\tlayer\tbefore_mm\tafter_mm\tforensic_hidden\ttable_hidden\n");
        foreach(var (name,shots) in sets)
        {
            var forensicHidden=new List<(int,string)>();var tableHidden=new List<(int,string)>();
            foreach(var raw in shots)
            {
                var shot=CombatReportTruth.Normalize(raw);var m=CombatForensics.Build(shot);
                var starts=shot.Events.Where(e=>e.Kind=="layerStart"&&e.Layer!=null).Select(e=>e.HitId??e.Layer!.Id).ToList();
                var visible=m.Layers.Select(l=>l.HitId).ToList();
                var hiddenIds=new List<string>(starts);foreach(var v in visible)hiddenIds.Remove(v);
                check(hiddenIds.Count==m.HiddenZeroSpans&&m.Layers.Count+m.HiddenZeroSpans==starts.Count,"every native block is either a visible layer or a counted zero span");
                forensicHidden.AddRange(hiddenIds.Select(id=>(shot.Number,id)));
                for(int i=0;i<m.Layers.Count;i++)
                {
                    var l=m.Layers[i];
                    check(l.Number==i+1,"forensic numbering is continuous");
                    bool span=l.After!=0&&CombatZeroSpan.Is(l.HitId,l.GapMm??l.PathMm,l.Before,l.After);
                    check(!span,"no visible forensic layer with a 0 mm measured path that leaves the penetration unchanged");
                }
                var views=CombatHitProjection.Build(shot);var shown=views.Where(v=>v.Status!="Diagnostic only").ToArray();
                var zeroViews=shown.Where(CombatZeroSpan.Is).ToArray();
                tableHidden.AddRange(zeroViews.Select(v=>(shot.Number,v.Id)));
                var impact=CombatReportView.Build(new[]{raw})[0];
                check(impact.Layers.Length==shown.Length-zeroViews.Length,"the layer table drops exactly the zero spans");
                for(int i=0;i<impact.Layers.Length;i++)check(impact.Layers[i].Index.EndsWith((i+1).ToString("00",CultureInfo.InvariantCulture),StringComparison.Ordinal),"table numbering is continuous");
                var block=HitBlockBuilder.Build(raw);
                foreach(var z in zeroViews)check(!block.Breakdown.Any(line=>line.Contains(z.Before is {} b?$"{Math.Round(b):0} → {Math.Round(b):0}  −0":"\u0000",StringComparison.Ordinal)),"hit panel breakdown omits the zero span");
                foreach(var id in hiddenIds){var v=views.FirstOrDefault(x=>x.Id==id);ledger.AppendLine(FormattableString.Invariant($"{name}\t{shot.Number}\t{id}\t{v?.Before}\t{v?.After}\ttrue\t{zeroViews.Any(z=>z.Id==id)}"));}
            }
            // Besides the five zero-path blocks, native air gaps of at most 0.05 mm LOS that change nothing are hidden too (the same rule).
            var gaps=shots.Select(CombatReportTruth.Normalize).SelectMany(s=>s.Events.Where(e=>e.Kind=="layerStart"&&e.Layer?.AirGapMm is <=CombatZeroSpan.PathMm).Select(e=>(s.Number,e.HitId??e.Layer!.Id))).ToHashSet();
            check(forensicHidden.All(x=>ExpectedZeroSpans.Contains(x)||gaps.Contains(x)),name+": every hidden layer is a measured zero span or an air gap of at most 0.05 mm");
            summary.AppendLine($"{name}: hidden sub-0.05 mm air gaps {forensicHidden.Count(gaps.Contains)}");
            forensicHidden.RemoveAll(gaps.Contains);
            if(name=="zero-loss-1107")
            {
                check(forensicHidden.OrderBy(x=>x).SequenceEqual(ExpectedZeroSpans.OrderBy(x=>x)),"forensic screen hides exactly the five measured zero spans of ZERO-LOSS-MAP (#122, #123, #136, #165 x2): "+string.Join(", ",forensicHidden.Select(x=>x.Item1+"/"+x.Item2)));
                check(tableHidden.OrderBy(x=>x).SequenceEqual(ExpectedZeroSpans.OrderBy(x=>x)),"layer table hides the same five, including the #122 wedge after the After fix: "+string.Join(", ",tableHidden.Select(x=>x.Item1+"/"+x.Item2)));
            }
            else check(forensicHidden.Count==0&&tableHidden.Count==0,name+": no zero spans to hide");
            summary.AppendLine($"{name}: hidden zero spans forensic {forensicHidden.Count}, table {tableHidden.Count}");
        }
        File.WriteAllText(Path.Combine(output,"ZERO-SPANS.tsv"),ledger.ToString());
        // The raw events of a hidden layer stay in the Technical log of the HTML.
        var zero=sets[0].Shots;var history=new CombatReportHistory();history.Restore(zero,0);var html=CombatReportHtml.Render(history);
        var doc=XDocument.Parse(html);
        var sections=doc.Descendants("section").ToDictionary(s=>s.Element("h2")!.Value.Split(' ')[0],s=>s);
        var log122=sections["#122"].Descendants("details").Single();
        check(log122.Element("summary")!.Value==CombatReportHtml.TechnicalLog&&log122.Value.Contains("= 0 mm RHA [Pen 94",StringComparison.Ordinal),"#122: the hidden wedge stays in the Technical log with its raw 94.4 → 94.4 event");
        var log165=sections["#165"].Descendants("details").Single().Value;
        check(log165.Contains("La4 turret",StringComparison.Ordinal)&&log165.Contains("[Pen 92",StringComparison.Ordinal),"#165: the hidden RHA zero span stays in the Technical log");
        check(!html.Contains("<summary>Details</summary>",StringComparison.Ordinal)&&doc.Descendants("details").All(d=>d.Element("summary")!.Value=="Technical log"),"the debug section is headed Technical log");
    }

    // ---- 2. the After value ----
    private static void After(Action<bool,string> check,CombatReportShot[] zero,StringBuilder summary)
    {
        var s122=zero.Single(s=>s.Number==122);var views=CombatHitProjection.Build(CombatReportTruth.Normalize(s122));
        var wedge=views.Single(v=>v.Id=="1854486707808:97:10:10");
        check(wedge.After is {} after&&Math.Abs(after-94.40230560302734)<1e-9&&CombatReportView.BeforeAfter(wedge)=="94 → 94","#122: the wedge reads 94 → 94 (its own measured exit), not 94 → 93 ("+CombatReportView.BeforeAfter(wedge)+")");
        var rows=CombatReportView.Build(new[]{s122})[0].Layers;
        check(!rows.Any(r=>r.BeforeAfter=="94 → 93")&&rows.Any(r=>r.BeforeAfter=="93 → 53"),"#122: no 94 → 93 row; the La4 turret keeps its own entry 93 (the 0.9 mm crew pass is not charged to the wedge)");
        summary.AppendLine("#122 wedge: "+CombatReportView.BeforeAfter(wedge)+" (0.22.40: 94 → 93); table rows: "+string.Join(" | ",rows.Select(r=>r.Index+" "+r.BeforeAfter)));
        // Every block without a residual exits at its measured protection line, else at the next native block's entry.
        int checkedRows=0;
        foreach(var raw in zero)
        {
            var shot=CombatReportTruth.Normalize(raw);
            foreach(var v in CombatHitProjection.Build(shot).Where(v=>v.Status=="Reached"&&v.After!=null&&v.Walls.Count==0&&(v.MeasuredExit!=null||v.NextEntry!=null)))
            {checkedRows++;check(v.After==(v.MeasuredExit??v.NextEntry),"a block without residual exits at its measured exit or the next block's entry");}
        }
        check(checkedRows>0,"the After rule is exercised on the 128-shot fixture ("+checkedRows+" rows)");
        // Synthetic: a protection-line block, a hidden crew pass with its own loss, then a plate.
        var wedgePart=new CombatLayerPart(1,"Composite armour plate","ebCassette_x","Wedge",100,30,true);
        var crew=new CombatLayerPart(2,"Crew member #2",null,"ERA",null,40,false);var back=new CombatLayerPart(3,"Back","rha","Rolled Homogeneous Armour",20,10,true);
        CombatReportEvent Start(string id,double before,CombatLayerPart p)=>new("layerStart","","Reached layer",before,null,id,new CombatLayerInfo(id,new[]{p},false));
        var events=new List<CombatReportEvent>{Start("s:1:2:3",300,wedgePart),new("materialReaction","","Wedge: 115 mm × 0.50 (long rod @30°) = 58 mm RHA",300,250,"s:1:2:3",RelatedEntity:1),
            Start("s:2:3:4",250,crew),new("componentResidual","","Traversed Crew member #2",250,240,"s:2:3:4"),Start("s:3:4:5",240,back),new("plateResidual","","Traversed Back",240,200,"s:3:4:5")};
        var synthetic=new CombatReportShot(1,"Outgoing","APFSDS",1600,events,0,300);
        check(CombatHitProjection.Build(synthetic).First().After==250,"synthetic: the protection line is the exit (250), not the next visible entry (240)");
        var noLine=synthetic with{Events=events.Where(e=>e.Kind!="materialReaction").ToArray()};
        check(CombatHitProjection.Build(noLine).First().After==250,"synthetic: without a protection line the next native block's entry (250) is the exit");
    }

    // ---- shares ----
    private static void Shares(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,StringBuilder summary)
    {
        int covered=0,total=0;
        foreach(var (name,shots) in sets)foreach(var raw in shots)
        {
            total++;var m=CombatForensics.Build(raw);
            if(m.Initial is not >0||m.Residual==null||m.Outcome=="RICOCHET")continue;
            covered++;
            var sum=m.Layers.Where(l=>l.Stage!="Precursor").Sum(l=>l.Percent??0)+(m.ResidualPercent??0)+100*(m.UnattributedMm??0)/m.Initial.Value;
            check(Math.Abs(sum-100)<=.5,"shares of the layers plus the residual (and a measured change between layers) total 100 %");
            if(m.UnattributedMm is {} u&&Math.Abs(u)>.01)check(CombatForensicHtml.Render(m,"k").Contains(WebUtility.HtmlEncode(m.BetweenLabel),StringComparison.Ordinal),"a measured change between layers is listed, so the visible shares close");
        }
        check(covered>0,"share closure exercised");
        summary.AppendLine($"share closure: {covered} of {total} shots (the rest have no measured residual: ricochet or open exit)");
    }

    // ---- 5. constituents of a composite array ----
    private static void Sublayers(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,CassettePreset[] presets,StringBuilder summary)
    {
        int plates=0;var seen=new SortedSet<string>(StringComparer.Ordinal);
        foreach(var (name,shots) in sets)foreach(var raw in shots)
        {
            var m=CombatForensics.Build(raw);var g=CombatForensicGeometry.Build(m);var sink=new Sink();CombatForensicDrawing.Draw(sink,m,false);
            foreach(var l in m.Layers)
            {
                var armour=l.Parts.Where(p=>p.Armour).ToArray();
                bool preset=l.GapMm==null&&armour.Length==1&&armour[0].MaterialId is {} id&&presets.Any(p=>p.MaterialId==id)&&armour[0].ThicknessMm>0;
                check(preset==(l.Sublayers is {Count:>0}),"a single composer-preset plate has its constituents, other layers none");
                if(l.Array)check(l.Sublayers==null,"a native array of several plates has no constituents of its own (0.22.42: they are on its plates)");
                if(l.Array)check(l.Plates!.All(p=>(p.Sublayers is {Count:>0})==(p.Part.ThicknessMm>0&&presets.Any(x=>x.MaterialId==p.Part.MaterialId))),"0.22.42: an array plate with a composer preset carries that preset's constituents");
                if(l.Sublayers is not {Count:>0} subs)continue;
                plates++;var p0=presets.Single(p=>p.MaterialId==armour[0].MaterialId);seen.Add(p0.Label);
                check(Math.Abs(subs.Sum(s=>s.NormalMm)-armour[0].ThicknessMm!.Value)<=.5,"constituent thicknesses add up to the plate's normal thickness (±0.5 mm)");
                check(subs.Select(s=>s.Material).SequenceEqual(p0.Layers.Select(x=>x.Material))&&subs.Select(s=>s.TiltDegrees).SequenceEqual(p0.Layers.Select(x=>x.Material=="air"?0:x.TiltDegrees)),"constituents in preset order (front to back) with their tilt");
                check(subs.All(s=>s.Colour==CassettePalette.Hex(s.Material)),"constituents use the composer colours");
                var band=g.Bands.Single(b=>ReferenceEquals(b.Layer,l));
                check(band.Subs.Count==subs.Count&&Math.Abs(band.Subs.Sum(s=>s.ExitX-s.EntryX)-(band.ExitX-band.EntryX))<1,"constituent bands fill the plate band along the shot line (within 1 px)");
                check(band.Subs.All(s=>sink.Polygons.Any(p=>p.SequenceEqual(s.Polygon))),"the drawing paints every constituent inside the band");
                check(subs.Where(s=>s.TiltDegrees>0).All(s=>sink.HatchAngles.Contains(CombatForensicDrawing.FaceHatch(band.Angle!.Value,s.TiltDegrees))),"tilted constituents are hatched at their tilt");
                var html=CombatForensicHtml.Render(m,"k");
                check(XDocument.Parse("<r>"+html+"</r>").Descendants("div").Where(d=>((string?)d.Attribute("class"))=="fx-row fx-sub").Count()>=subs.Count,"indented constituent rows under the plate");
                check(XDocument.Parse("<r>"+html+"</r>").Descendants("div").Where(d=>((string?)d.Attribute("class"))=="fx-row fx-sub").All(d=>!d.Value.Contains('%')),"constituent rows carry no share (no per-layer contribution in the composer table)");
            }
        }
        var s1108=CombatForensics.Build(sets[2].Shots.Single(s=>s.Number==2));
        var wedge=s1108.Layers.Single(l=>l.HitId=="3098285301920:88:13:12");
        check(wedge.Sublayers!.Count==15&&wedge.Sublayers[0].Material=="highHardnessSteel"&&wedge.Sublayers[^1].Material=="rha"&&Math.Abs(wedge.Sublayers.Sum(s=>s.NormalMm)-296)<=.5,"08-11 shot 2: the 296 mm Leopard 2A7 wedge shows its 15 preset layers front to back");
        var oplot=CombatForensics.Build(sets[2].Shots.Single(s=>s.Number==4)).Layers.Single(l=>l.HitId=="3098255364416:224:5:4");
        check(oplot.Sublayers!.Count==28&&Math.Abs(oplot.Sublayers.Sum(s=>s.NormalMm)-910)<=.5,"08-11 shot 4: the 910 mm Oplot-M base array shows its 28 preset layers");
        check(plates>=2,"constituents exercised on the real fixtures ("+plates+" plates)");
        // No per-layer shares exist, so none are shown; a share of the array would need contributions that add up exactly to its factor.
        check(typeof(CompositeSublayer).GetProperties().All(p=>!p.Name.Contains("Percent")&&!p.Name.Contains("Share")),"constituents carry no share field");
        summary.AppendLine($"composite plates with constituents: {plates} ({string.Join(", ",seen)}); shares per constituent: none (one protection factor per preset)");
    }

    // ---- 3/4. language ----
    private static IEnumerable<string> Exempt(IEnumerable<CombatReportShot> shots,CassettePreset[]? presets=null)
    {
        foreach(var s in shots)
        {
            if(s.Target!=null)yield return s.Target;if(s.Shooter!=null)yield return s.Shooter;yield return s.Profile;
            foreach(var e in s.Events)foreach(var p in e.Layer?.Parts??Array.Empty<CombatLayerPart>())
            {
                yield return CombatReportLanguage.Material(p);if(p.MaterialLabel!=null)yield return CombatReportLanguage.Material(p.MaterialLabel,p.MaterialId);
                if(p.Armour){yield return CombatReportView.PlateName(p.Name,p.Entity);yield return p.Name;}
                foreach(var sub in CombatCompositeLayers.For(p)??Array.Empty<CompositeSublayer>())yield return sub.Label;
            }
        }
    }
    internal static IEnumerable<string> PanelTexts(CombatImpactView i)
    {
        var b=i.Block;
        foreach(var t in new[]{i.Head,i.Summary,i.SummaryLine,i.Target,b.NumberText,b.Direction,b.Ammo,b.Values,b.Result,b.Shooter})yield return t;
        foreach(var t in i.SummaryLines)yield return t;
        foreach(var l in i.Layers)foreach(var t in new[]{l.Index,l.Name,l.Material,l.Thickness,l.Angle,l.BeforeAfter,l.Reaction,l.Note})yield return t;
        foreach(var t in i.Diagnostics)yield return t;
        if(b.Waterfall is {} wf){yield return PenetrationWaterfall.Text(wf);yield return wf.EndLabel;foreach(var s in wf.Steps){yield return s.Label;yield return s.Tooltip;}}
    }
    internal static IEnumerable<string> HudTexts(HitBlock b){yield return b.Detail;yield return b.DamageShort;yield return b.DamageExpanded;foreach(var t in b.Breakdown)yield return t;}
    internal static IEnumerable<string> ForensicTexts(ForensicModel m)
    {
        foreach(var t in new[]{m.Outcome,m.Explanation,m.Context,m.Damage,m.BetweenLabel,"Expanded Ballistics | impact #"+m.Number})yield return t;
        foreach(var t in m.Findings.Concat(m.Advice).Concat(CombatForensicGeometry.FixedTexts))yield return t;
        foreach(var l in m.Layers)
        {
            yield return CombatForensicGeometry.LayerName(l);yield return CombatForensicGeometry.Detail(l);yield return l.Geometry;yield return CombatForensicGeometry.EraLabel(l);yield return CombatForensics.EraText(l);
            if(l.Array)yield return CombatForensicGeometry.Members(l);
            foreach(var s in l.Sublayers??Array.Empty<CompositeSublayer>()){yield return s.Label;yield return CombatForensicGeometry.SubDetail(s);}
        }
        foreach(bool heat in new[]{false,true}){var sink=new Sink();CombatForensicDrawing.Draw(sink,m,heat);foreach(var t in sink.Texts)yield return t;}
    }
    private static IEnumerable<string> HtmlVisibleText(string html,bool forensicOnly)
    {
        var doc=XDocument.Parse(html);
        foreach(var d in doc.Descendants("details").ToArray())d.Remove();   // the Technical log may stay technical
        foreach(var s in doc.Descendants("style").ToArray())s.Remove();
        IEnumerable<XElement> scope=forensicOnly?doc.Descendants("div").Where(d=>(string?)d.Attribute("class")=="forensic"):new[]{doc.Root!};
        foreach(var e in scope)foreach(var t in e.DescendantNodes().OfType<XText>())yield return t.Value;
        if(!forensicOnly)foreach(var t in doc.Descendants().Where(x=>x.Name.LocalName=="title"))yield return t.Value;
    }
    private static void Language(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,string output,StringBuilder summary)
    {
        var list=new StringBuilder("# Forbidden in visible report text (panel, HTML without the Technical log, forensic screen, hit panel)\n");
        foreach(var t in CombatReportLanguage.Jargon)list.AppendLine(t);
        list.AppendLine("\n# Additionally forbidden in the forensic screen (statements that something is not known)");
        foreach(var t in CombatReportLanguage.Uncertainty)list.AppendLine(t);
        list.AppendLine("\n# Exempt: names the user gave (vehicle, shell profile, plate and composer material names). 0.22.42: no allowed exception (\"layer sequence not resolved\" is gone).");
        File.WriteAllText(Path.Combine(output,"FORBIDDEN-WORDS.txt"),list.ToString());
        var hits=new StringBuilder("fixture\tshot\tsurface\tterm\ttext\n");int scanned=0,violations=0,uncertain=0;
        void Scan(string fixture,int shot,string surface,IEnumerable<string> texts,IEnumerable<string> exempt,bool forensic)
        {
            var ex=exempt.Where(e=>!string.IsNullOrWhiteSpace(e)).Distinct().ToArray();
            foreach(var text in texts.Where(t=>!string.IsNullOrEmpty(t)))
            {
                scanned++;
                foreach(var term in CombatReportLanguage.JargonIn(text,ex)){violations++;hits.AppendLine($"{fixture}\t{shot}\t{surface}\t{term}\t{text}");}
                if(forensic)foreach(var term in CombatReportLanguage.UncertaintyIn(text,ex)){uncertain++;hits.AppendLine($"{fixture}\t{shot}\t{surface} (uncertainty)\t{term}\t{text}");}
            }
        }
        var fixedPanel=new[]{CombatReportView.PanelTitle,CombatReportView.PanelSubtitle,CombatReportView.SectionTitle,CombatReportView.HelpText,CombatReportView.EmptyText,CombatReportView.ForensicTooltip,
            CombatReportView.TargetTooltip,CombatReportView.DestroyedTooltip,CombatReportView.NoLayersText,CombatReportView.SequenceTitle,CombatReportView.ShowLayers,CombatReportView.HideLayers,CombatReportView.DestroyedText}
            .Concat(CombatReportView.Columns).Concat(CombatReportView.PanelButtons).Concat(new[]{CombatReportHtml.Title,CombatReportHtml.Intro,CombatReportHtml.SequencePrefix});
        Scan("fixed",0,"panel",fixedPanel,Array.Empty<string>(),false);
        Scan("fixed",0,"forensic",CombatForensicGeometry.FixedTexts.Append(CombatForensicDrawing.TopLabel).Append(CombatForensicDrawing.NoLayers),Array.Empty<string>(),true);
        foreach(var (name,shots) in sets)
        {
            foreach(var raw in shots)
            {
                var ex=Exempt(new[]{raw}).ToArray();
                var impact=CombatReportView.Build(new[]{raw})[0];
                Scan(name,raw.Number,"panel",PanelTexts(impact),ex,false);
                Scan(name,raw.Number,"hit panel",HudTexts(impact.Block),ex,false);
                Scan(name,raw.Number,"forensic",ForensicTexts(impact.Forensic!),ex,true);
            }
            var history=new CombatReportHistory();history.Restore(shots,0);var html=CombatReportHtml.Render(history);
            var exAll=Exempt(shots).ToArray();
            Scan(name,0,"html",HtmlVisibleText(html,false),exAll,false);
            Scan(name,0,"html forensic",HtmlVisibleText(html,true),exAll,true);
        }
        File.WriteAllText(Path.Combine(output,"LANGUAGE-CHECK.tsv"),hits.ToString());
        check(violations==0,$"forbidden-term list: 0 hits in the visible panel, hit panel, HTML and forensic text ({violations} hits; see LANGUAGE-CHECK.tsv)");
        check(uncertain==0,$"no uncertainty statements in the forensic screen on any fixture ({uncertain} hits)");
        check(scanned>5000,"language scan covers the generated text ("+scanned+" strings)");
        // Positive checks on the wording the brief asks for.
        var z=sets[0].Shots;var m165=CombatForensics.Build(z.Single(s=>s.Number==165));
        // 0.22.43: the ammunition stowage lies behind the armour, so the head first states the perforation of the protective envelope.
        check(m165.Explanation.Contains("projectile arrested in layer ",StringComparison.OrdinalIgnoreCase)&&m165.Explanation.Contains("(ammunition stowage)",StringComparison.Ordinal),"#165: \"… projectile arrested in layer N (ammunition stowage)\" ("+m165.Explanation+")");
        var f1108=sets[2].Shots;var m3=CombatForensics.Build(f1108.Single(s=>s.Number==3));
        check(m3.Findings.Any(f=>f.Contains("ERA element initiated",StringComparison.Ordinal))&&m3.Layers.Any(l=>l.Array&&CombatForensicGeometry.LayerName(l)=="Composite array"&&CombatForensicGeometry.Detail(l).Contains(" → ",StringComparison.Ordinal)),"ERA element initiated; a native group reads Composite array with its plates in order (0.22.42 wording)");
        var r165=CombatReportView.Build(new[]{z.Single(s=>s.Number==165)})[0];
        check(r165.SummaryLine.Contains("ammunition stowage",StringComparison.Ordinal)&&r165.SummaryLine.Contains("main gun",StringComparison.Ordinal),"modules read ammunition stowage and main gun ("+r165.SummaryLine+")");
        check(CombatReportLanguage.Reaction("Leopard 2A7 turret wedge (add-on): 29 mm × 0.68 (long rod @66°) = 20 mm RHA")=="Leopard 2A7 turret wedge: LOS 29 mm × 0.68 (long rod, 66°) = 20 mm RHA-eq","protection line in report wording");
        check(CombatReportLanguage.Material("Leopard 2A4 Gun Mantlet (add-on)","ebCassette_user_x")=="Leopard 2A4 Gun Mantlet (add-on)","a user's own composer name stays exactly as given");
        check(CombatReportLanguage.JargonIn("ammoRack destroyed; native block").Count>=3&&CombatReportLanguage.UncertaintyIn("depth unknown?").Count==2&&CombatReportLanguage.UncertaintyIn("Composite array (layer sequence not resolved)").Count==1,"the checker itself finds jargon and uncertainty, including the retired array phrase (0.22.42)");
        summary.AppendLine($"language: {scanned} visible strings scanned, {violations} forbidden terms, {uncertain} uncertainty statements");
    }

    // ---- previews ----
    private static void Previews(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,string preview)
    {
        var css=CombatReportHtml.Render(new CombatReportHistory());css=css[(css.IndexOf("<style>",StringComparison.Ordinal)+7)..css.IndexOf("</style>",StringComparison.Ordinal)];
        var zero=sets[0].Shots;var history=new CombatReportHistory();history.Restore(zero,0);
        var report=CombatReportHtml.Render(history);File.WriteAllText(Path.Combine(preview,"combat-report-128.html"),report);
        check(XDocument.Parse(report).Descendants("section").Count()==128,"HTML report of the 128-shot fixture");
        var s165=zero.Single(s=>s.Number==165);
        File.WriteAllText(Path.Combine(preview,"panel-165.html"),PanelMockup(CombatReportView.Build(new[]{s165})[0],css));
        Forensic(CombatForensics.Build(s165),"forensic-165",preview,css);
        foreach(var shot in sets[2].Shots)Forensic(CombatForensics.Build(shot),$"forensic-1108-shot-{shot.Number}",preview,css);
        check(File.Exists(Path.Combine(preview,"forensic-1108-shot-4.html")),"forensic previews of every 08-11 shot");
    }
    private static void Forensic(ForensicModel m,string stem,string preview,string css)
    {
        File.WriteAllText(Path.Combine(preview,stem+".model.json"),JsonSerializer.Serialize(m,new JsonSerializerOptions{WriteIndented=true}));
        foreach(bool heat in new[]{false,true})
        {
            var sink=new RasterSink((int)CombatForensicDrawing.Width,(int)CombatForensicDrawing.Height(m));CombatForensicDrawing.Draw(sink,m,heat);
            sink.Save(Path.Combine(preview,stem+(heat?"-heat":"-material")));
        }
        var fragment=CombatForensicHtml.Render(m,stem);
        File.WriteAllText(Path.Combine(preview,stem+".html"),Window($"{ForensicModel.Title} <small>| Expanded Ballistics | impact #{m.Number}</small>",fragment,css+".forensic{border:0;margin:0}.forensic h3{display:none}"));
    }
    private static string Window(string title,string body,string css)=>"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>Panel mockup</title><style>"+css+
        "body{max-width:none;margin:0;padding:20px;background:#10151a}.window{background:#232930;border:1px solid #5a6268;border-radius:8px;width:1180px}.title{padding:15px 20px;background:#1b211c;color:#f1d29a;font-size:18px}.viewport{padding:0 20px 12px}.footer{padding:12px 20px;background:#1b211c;color:#b8ad94;font-size:12px}.fx-row{font-size:13px}.btn{display:inline-block;border:1px solid #5a6268;border-radius:4px;padding:2px 10px;color:#e7edf2;font-size:13px;margin:6px 0}</style></head><body><div class='window'><div class='title'>"+
        title+"<span style='float:right'>X</span></div><div class='viewport'>"+body+"</div><div class='footer'>Mockup from the shared report model (expanded content; the panel in the game scrolls).</div></div><script>document.body.setAttribute('data-mockup-height',Math.ceil(document.querySelector('.window').getBoundingClientRect().bottom+20));</script></body></html>";
    private static string PanelMockup(CombatImpactView impact,string css)
    {
        static string E(string s)=>WebUtility.HtmlEncode(s);
        var b=impact.Block;var sb=new StringBuilder();
        sb.Append($"<p class='context'>{E(CombatReportView.HelpText)}</p><section><span class='btn'>Forensic</span><h2 style='border-color:{b.Colour}'>{E(impact.Head)}</h2>");
        foreach(var l in impact.SummaryLines)sb.Append($"<p class='damage'>{E(l)}</p>");
        if(b.Shooter.Length>0)sb.Append($"<p>{E(b.Shooter)}</p>");
        sb.Append($"<p class='context'>{E(impact.Summary)}</p><table><thead><tr>");
        for(int i=0;i<CombatReportView.Columns.Length;i++)sb.Append($"<th{(CombatReportView.RightAligned[i]?" class='r'":"")} style='width:{CombatReportView.Widths[i]}px'>{E(CombatReportView.Columns[i])}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach(var l in impact.Layers)
        {
            var cells=new[]{l.Index,l.Name,l.Material,l.Thickness,l.Angle,l.BeforeAfter,l.Reaction};sb.Append("<tr>");
            for(int i=0;i<cells.Length;i++)sb.Append($"<td{(CombatReportView.RightAligned[i]?" class='r'":"")}{(i==0?$" style='color:{l.Colour}'":"")}>{E(cells[i])}</td>");
            sb.Append("</tr>");if(l.Note.Length>0)sb.Append($"<tr class='note'><td></td><td colspan='6'>{E(l.Note)}</td></tr>");
        }
        sb.Append("</tbody></table>");
        foreach(var d in impact.Diagnostics)sb.Append($"<p class='context'>{E(d)}</p>");
        if(b.Waterfall is {} wf)sb.Append($"<h4>{E(CombatReportView.SequenceTitle)}</h4><div class='wfbox'>{PenetrationWaterfall.Svg(wf)}</div>");
        sb.Append("<h4>Hit panel (expanded)</h4><pre>"+E(string.Join("\n",new[]{b.NumberText+" "+b.Direction+" "+b.Ammo+" "+b.Values+" "+b.Result,b.Detail,b.DamageExpanded}.Concat(b.Breakdown)))+"</pre></section>");
        return Window(CombatReportView.PanelTitle+" <small>| "+E(CombatReportView.PanelSubtitle)+"</small>",sb.ToString(),css.Replace("max-width:1100px;","").Replace("margin:2em auto;",""));
    }
    private sealed class Sink:ISectionSink
    {
        internal List<float[]> Polygons=new();internal List<string> Texts=new();internal List<float> HatchAngles=new();
        public void ViewBox(float w,float h){}public void BeginClip(float w,float h){}public void EndClip(){}
        public void Poly(float[] p,string f,string? s,float w)=>Polygons.Add(p);
        public void Hatch(float[] p,string c,float a,float s,float w){HatchAngles.Add(a);Polygons.Add(p);}
        public void Outline(float[] p,string c,float w){}
        public void Line(float x,float y,float a,float b,string c,float w){}
        public void Arrow(float x,float y,float a,float b,string c,float w,float h){}
        public void Region(string id,float[] p){}
        public void Text(float x,float y,string text,string c,SectionAnchor a,string? b)=>Texts.Add(text);
    }
    private sealed class RasterSink:ISectionSink
    {
        private readonly Raster r=new();private readonly List<object> texts=new();
        internal RasterSink(int w,int h){r.Resize(w,h);r.Clear(Raster.Pack(19,25,31,255));}
        private static uint U(string c){var s=c.TrimStart('#');return Raster.Pack(byte.Parse(s[..2],NumberStyles.HexNumber),byte.Parse(s[2..4],NumberStyles.HexNumber),byte.Parse(s[4..6],NumberStyles.HexNumber),s.Length==8?byte.Parse(s[6..8],NumberStyles.HexNumber):(byte)255);}
        public void ViewBox(float w,float h){}
        public void BeginClip(float w,float h)=>r.SetClip(0,0,w,h);
        public void EndClip()=>r.SetClip(0,0,r.Width,r.Height);
        public void Poly(float[] xy,string fill,string? stroke,float width){r.FillPolygon(xy,U(fill));if(stroke!=null)r.Polyline(xy,width,U(stroke),0,true);}
        public void Hatch(float[] xy,string c,float a,float s,float w)=>r.FillPolygon(xy,U(c),a,s,w);
        public void Outline(float[] xy,string c,float w)=>r.Polyline(xy,w,U(c),0,true);
        public void Line(float a,float b,float c,float d,string colour,float w)=>r.Segment(a,b,c,d,w,U(colour));
        public void Arrow(float a,float b,float c,float d,string colour,float w,float h)=>r.Arrow(a,b,c,d,w,h,U(colour));
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>texts.Add(new{x,y,text,colour,background,anchor=anchor.ToString()});
        internal void Save(string stem)
        {
            // The Kit Raster packs RGBA bytes, the Texture2D upload format; labels are pooled TMP texts in the game.
            var bytes=new byte[r.Width*r.Height*4];for(int i=0;i<r.Width*r.Height;i++){uint p=r.Pixels[i];bytes[4*i]=(byte)p;bytes[4*i+1]=(byte)(p>>8);bytes[4*i+2]=(byte)(p>>16);bytes[4*i+3]=(byte)(p>>24);}
            File.WriteAllBytes(stem+".rgba",bytes);File.WriteAllText(stem+".labels.json",JsonSerializer.Serialize(new{width=r.Width,height=r.Height,texts}));
        }
    }
}
