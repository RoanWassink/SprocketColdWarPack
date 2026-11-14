using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.51 part B (user 08-11: "lijkt net alsof elke plaat 2 keer in het forensic menu wordt getoond"). Presentation only:
/// a block whose material was already paid earlier in the shot joins the preceding row that paid it; a later crossing of one object is named.
/// </summary>
internal static class Fix02251DuplicateRowTests
{
    /// <summary>Source files 0.22.51 may change on top of 0.22.50 (parts A-D; see test-output/SOURCE-CHANGES.tsv).</summary>
    internal static readonly string[] Changed={"CombatLayerContinuity.cs","CombatForensics.cs","CombatHitProjection.cs","CombatReportView.cs","CombatReportPanel.cs","Material/ReliktPrototype.cs","Material/ReliktSpentVisuals.cs",
        "NativeMaterialIntervals.cs","NativeMaterialReactions.cs","ProtectionFactorTable.cs","RuntimeArmourResponses.cs","ModularMeshOwnership.cs","ModularGeometryGate.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    internal static readonly string[] Fixtures={"forensic-dup-1108","era-1108c","era-1108d","t80u-1108","t80u-1108b","forensic-fixtures","forensic-fixtures-1108",
        "forensic-fixtures-1108b","forensic-fixtures-1108c","forensic-fixtures-1108d","zero-loss-1107"};
    internal static CombatReportShot[] Load(string parent,string fixture)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(parent,fixture,"last-session.json")));
        return JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    /// <summary>The paid length (mm) the interval ledger recorded for one native block; null when the report has no ledger data (Legacy, pre-0.22.47).</summary>
    internal static double? PaidMm(CombatReportShot shot,string stage,string hitId)
    {
        var paying=shot.Events.Where(e=>e.Stage==stage&&e.HitId==hitId&&e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).ToArray();
        return paying.Length==0?null:paying.SelectMany(e=>e.PaidIntervals!).Sum(p=>p.LengthMm);
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var sink=check;check=(ok,msg)=>{count++;sink(ok,"fix02251-B: "+msg);};
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot();
        Directory.CreateDirectory(Path.Combine(output,"preview"));
        // ---- forensic-dup-1108 #7 (the user's report) ----
        var dup=Load(parent,"forensic-dup-1108");
        var s7=dup.Single(s=>s.Number==7);var m7=CombatForensics.Build(s7);var rows7=CombatHitProjection.Build(s7);
        var c7=CombatLayerContinuity.Analyse(s7);
        string[] settled7={"1997832908544:516:21:20","1997832908544:552:13:15","1997832908544:657:9:10","1997832908544:693:12:7"};
        foreach(var id in settled7)
        {
            check(c7.IsSettled("",id),"#7 block already paid earlier is recognised: "+id);
            check(m7.Layers.All(l=>l.HitId!=id),"#7 forensic list has no own row for "+id);
            check(rows7.All(r=>r.Id!=id),"#7 Reaction table has no own row for "+id);
        }
        check(!m7.Layers.Any(l=>l.Before is {} b&&l.After is {} a&&(Math.Abs(b-543.2)<.05&&Math.Abs(a-541.69)<.05||Math.Abs(b-373.62)<.05&&Math.Abs(a-373.01)<.05||Math.Abs(b-373.01)<.05&&Math.Abs(a-372.55)<.05)),
            "#7 no rows Plate #256 543 → 542, Plate #311 374 → 373, composite plate 373 → 373");
        check(m7.Residual is {} r7&&Math.Abs(r7-362.9)<.05,"#7 end value 362.9 mm unchanged");
        // The array that paid HHS #256 now leaves at 541.7 (its own 544.1 plus the two settled blocks), continuous with the next layer.
        var hhs=m7.Layers.Single(l=>l.HitId=="1997832908544:474:12:17");
        check(hhs.Before is {} hb&&Math.Abs(hb-564.9)<.05&&hhs.After is {} ha&&Math.Abs(ha-541.69)<.05,"#7 paying layer absorbs the settled native losses (564.9 → 541.7)");
        var cast=m7.Layers.Single(l=>l.HitId=="1997832908544:629:15:7");
        check(cast.After is {} ca&&Math.Abs(ca-372.55)<.05,"#7 cast array absorbs Plate #311 and the zero-length composite block (→ 372.5)");
        Continuity(m7,check,"#7");
        // Plate #516: two crossings with their own paid material, the second named.
        var p516=m7.Layers.Where(l=>l.Parts.Any(p=>p.Entity==516&&p.Armour)&&l.GapMm==null).ToArray();
        check(p516.Length==2&&!p516[0].Plate.Contains("crossing")&&p516[1].Plate=="Plate #516 (second crossing)","#7 Plate #516 twice, second named (forensic)");
        var r516=rows7.Where(r=>r.Layer?.Parts.Any(p=>p.Entity==516&&p.Armour)==true&&r.Layer.AirGapMm==null).ToArray();
        check(r516.Length==2&&r516[0].Name=="Plate #516"&&r516[1].Name=="Plate #516 (second crossing)","#7 Plate #516 twice, second named (Reaction table)");
        check(CombatReportView.Build(new[]{s7},1)[0].Layers.Any(l=>l.Name=="Plate #516\n(second crossing)"),"#7 full second-crossing qualifier fits as a separate table line");
        check(m7.Context.Contains("Relikt 2+1 (second crossing)"),"#7 a housing's far wall is named in the layer sentence");
        // ---- #8 ----
        var s8=dup.Single(s=>s.Number==8);var m8=CombatForensics.Build(s8);
        foreach(var id in new[]{"2017194977792:132:9:10","2017194977792:282:5:6","2017194977792:359:3:2"})check(m8.Layers.All(l=>l.HitId!=id),"#8 no own row for "+id);
        Continuity(m8,check,"#8");
        // ---- every fixture: no 0 mm paid row, sums and JSON unchanged ----
        var tsv=new StringBuilder("fixture\tshot\tsettled_blocks_merged\tforensic_rows\treaction_rows\tlater_crossings_named\tresidual_mm\n");
        var crossings=new StringBuilder("fixture\tshot\thit\tordinal\tobject\tpreceded_by\n");
        int merged=0,named=0,shots=0;
        foreach(var fixture in Fixtures)
        {
            var saved=Load(parent,fixture);
            foreach(var shot in saved)
            {
                shots++;var json=JsonSerializer.Serialize(shot);
                var m=CombatForensics.Build(shot);var rows=CombatHitProjection.Build(shot);var a=CombatLayerContinuity.Analyse(shot);
                foreach(var l in m.Layers.Where(l=>l.GapMm==null))
                    check(PaidMm(shot,l.Stage,l.HitId) is not {} paid||paid>=CombatLayerContinuity.SettledMm||l.Era=="ERA activated"||l.Internal||l.Number==1,
                        $"no forensic row with 0 mm paid material: {fixture} #{shot.Number} {l.HitId}");
                foreach(var r in rows.Where(r=>r.Status!="Diagnostic only"&&r.Layer is {AirGapMm:null}))
                    check(PaidMm(shot,r.Stage,r.Id) is not {} paid||paid>=CombatLayerContinuity.SettledMm||r.Reaction.Contains("ERA",StringComparison.Ordinal)||r.Order==0,
                        $"no Reaction row with 0 mm paid material: {fixture} #{shot.Number} {r.Id}");
                check(JsonSerializer.Serialize(shot)==json,"presentation leaves the JSON bytes intact "+fixture+" #"+shot.Number);
                Continuity(m,check,fixture+" #"+shot.Number);
                // Every merge keeps the shot's total: the residual is the last measured exit.
                if(m.Residual is {} residual&&m.Layers.LastOrDefault(l=>l.Stage!="Precursor") is {After:{} last})check(Math.Abs(residual-last)<1e-9||m.Arrested,"residual equals the last layer exit "+fixture+" #"+shot.Number);
                merged+=a.Settled.Count;named+=a.Crossing.Count;
                // Every named later crossing has air or another object between it and the previous crossing of that object.
                var starts=shot.Events.Where(e=>e.Kind=="layerStart"&&e.Layer!=null&&!a.IsSettled(e.Stage,e.HitId)).ToArray();
                for(int i=0;i<starts.Length;i++)
                {
                    if(a.Ordinal(starts[i].Stage,starts[i].HitId)<2||CombatLayerContinuity.SingleObject(starts[i].Layer!) is not {} entity)continue;
                    var j=i-1;while(j>=0&&starts[j].Stage==starts[i].Stage&&starts[j].Layer!.AirGapMm==null&&starts[j].Layer!.Parts.Any(p=>p.Entity==entity)&&
                        a.Ordinal(starts[j].Stage,starts[j].HitId)==a.Ordinal(starts[i].Stage,starts[i].HitId))j--;
                    var between=j>=0?(starts[j].Layer!.AirGapMm!=null?"air gap":starts[j].Layer!.Parts.Any(p=>p.Entity==entity)?"same object, earlier crossing":"other object"):"start";
                    check(between!="start"&&between!="same object, earlier crossing"||starts.Take(i).Any(s=>s.Layer!.AirGapMm!=null||s.Layer.Parts.All(p=>p.Entity!=entity)),
                        $"later crossing is separated from the earlier one: {fixture} #{shot.Number} {starts[i].HitId}");
                    crossings.AppendLine($"{fixture}\t{shot.Number}\t{starts[i].HitId}\t{a.Ordinal(starts[i].Stage,starts[i].HitId)}\t{starts[i].Layer!.Parts.First(p=>p.Entity==entity).Name}\t{between}");
                }
                tsv.AppendLine(FormattableString.Invariant($"{fixture}\t{shot.Number}\t{a.Settled.Count}\t{m.Layers.Count}\t{rows.Length}\t{a.Crossing.Count}\t{m.Residual:0.###}"));
            }
        }
        check(merged==12,"twelve settled blocks over all fixtures are merged (counted 0.22.51 inventory)");
        // ---- HTML and in-game list: one model, one visible drawing ----
        var html=File.ReadAllText(Path.Combine(root,"CombatForensicHtml.cs"));var panel=File.ReadAllText(Path.Combine(root,"CombatForensicPanel.cs"));
        check(html.Contains("Render(ForensicModel m,string key)")&&panel.Contains("Open(ForensicModel model)")&&File.ReadAllText(Path.Combine(root,"CombatReportView.cs")).Contains("Forensic=CombatForensics.Build(shot)"),
            "the in-game forensic panel and the HTML export render the same ForensicModel list");
        check(CombatForensicHtml.Css.Contains(".fx-heat{display:none}")&&CombatForensicHtml.Css.Contains("input:checked~.fx-material{display:none}")&&CombatForensicHtml.Css.Contains("input:checked~.fx-heat{display:block}"),
            "HTML shows one drawing at a time (material, or heat-map with the switch)");
        var rendered=CombatForensicHtml.Render(m7,"k");
        check(rendered.IndexOf("type='checkbox'",StringComparison.Ordinal)<rendered.IndexOf("class='fx-material'",StringComparison.Ordinal)&&rendered.IndexOf("class='fx-material'",StringComparison.Ordinal)<rendered.IndexOf("class='fx-heat'",StringComparison.Ordinal),
            "the switch precedes both drawings as their sibling (CSS ~ selector applies)");
        // ---- previews of #7 and #8 (forensic and Reaction table) ----
        var css=CombatForensicHtml.Css;
        var era2=Load(parent,"era-1108d").Single(s=>s.Number==2);
        foreach(var (stem,shot,model) in new[]{("dup-1108-shot-7",s7,m7),("dup-1108-shot-8",s8,m8),("era-1108d-shot-2",era2,CombatForensics.Build(era2))})
        {
            File.WriteAllText(Path.Combine(output,"preview",stem+"-forensic.html"),Page(CombatForensicHtml.Render(model,stem),css));
            File.WriteAllText(Path.Combine(output,"preview",stem+"-reaction.html"),ReactionPage(shot));
        }
        File.WriteAllText(Path.Combine(output,"DUPLICATE-ROWS-02251.tsv"),tsv.ToString());
        File.WriteAllText(Path.Combine(output,"LATER-CROSSINGS-02251.tsv"),crossings.ToString());
        File.WriteAllText(Path.Combine(output,"FIX51B-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=count,shots,mergedRows=merged,namedLaterCrossings=named}));
        Console.WriteLine($"FIX02251 B PASS: {count} checks; {merged} settled rows merged; {named} later crossings named; {shots} shots.");
    }
    /// <summary>Within one stage, every solid layer starts where the previous one ended (no row leaves a jump in the residual penetration).</summary>
    internal static void Continuity(ForensicModel m,Action<bool,string> check,string label)
    {
        foreach(var pair in m.Layers.Zip(m.Layers.Skip(1)).Where(p=>p.First.Stage==p.Second.Stage&&p.First.After is not null&&p.Second.Before is not null&&p.First.Stage!="Precursor"))
            check(pair.Second.Before<=pair.First.After+1e-6,$"{label}: layer {pair.Second.Number} does not start above the exit of layer {pair.First.Number}");
    }
    internal static string Page(string body,string css)=>"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><style>body{background:#232930;color:#e7edf2;font:15px system-ui;max-width:1180px;margin:18px auto}"+css+"</style></head><body>"+body+"</body></html>";
    internal static string ReactionPage(CombatReportShot shot)
    {
        var view=CombatReportView.Build(new[]{shot},1)[0];
        static string E(string s)=>System.Net.WebUtility.HtmlEncode(s);
        var sb=new StringBuilder("<table style='border-collapse:collapse;width:100%;font-size:13px'><tr><th style='text-align:left'>#</th><th style='text-align:left'>Layer</th><th style='text-align:left'>Material</th><th>Thickness</th><th>Angle</th><th>Pen. in → out (mm)</th><th style='text-align:left'>Reaction</th></tr>");
        foreach(var l in view.Layers)
            sb.Append($"<tr style='border-top:1px solid #45505b;vertical-align:top'><td>{E(l.Index)}</td><td><i style='display:inline-block;width:10px;height:10px;background:{l.Colour}'></i> {E(l.Name)}</td><td>{E(l.Material)}</td><td style='text-align:right'>{E(l.Thickness)}</td><td style='text-align:right'>{E(l.Angle)}</td><td style='text-align:right'>{E(l.BeforeAfter)}</td><td style='white-space:pre-line'>{E(l.Reaction)}{(l.Note.Length>0?"<br/><small>"+E(l.Note)+"</small>":"")}</td></tr>");
        return Page($"<h3>Impact {shot.Number}: {E(view.Head)}</h3>"+sb+"</table>","th,td{padding:5px 8px}td:nth-child(2){white-space:pre-line}tr{line-height:1.5}");
    }
}
