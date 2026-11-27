using System.Net;
namespace SprocketExpandedBallistics;
/// <summary>
/// The saved HTML report (0.22.7 / 0.22.112): matches the Kit panel (<see cref="CombatReportView"/>) — header line,
/// clean cause-grouped damage summary, context line, then either the dedicated HE/HESH Blast KPI grid + armour-response
/// table or the standard ballistic layer table. Raw measurement events stay in the Technical log.
/// </summary>
internal static class CombatReportHtml
{
    /// <summary>0.22.41: the visible fixed texts. The per-impact raw event list is the debug section, headed "Technical log".</summary>
    internal const string Title="Combat report",TechnicalLog="Technical log",SequencePrefix="Penetration sequence: ",
        Intro="Newest impact first. For each impact: the outcome, the behind-armour effects, and every measured layer with its material, normal thickness, obliquity and the penetration at entry and exit. The raw measurement events of each impact are in its technical log.";
    internal static string Render(CombatReportHistory history)
    {
        static string E(string text)=>WebUtility.HtmlEncode(text);
        // Every shot, newest first (the panel shows the last ten the same way).
        var shots=history.Shots.Where(s=>s!=null).ToArray();
        var impacts=CombatReportView.Build(shots,int.MaxValue);
        var byNumber=shots.Select(CombatReportTruth.Normalize).GroupBy(s=>s.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToDictionary(g=>g.Key,g=>g.Last());
        string Details(CombatReportShot shot)
        {
            static string Num(double? v,string unit)=>v is {} x&&double.IsFinite(x)?x.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+unit:"—";
            var lines=shot.Events.Where(CombatReportNoise.IsEndedPlate).Select(e=>e.Text).ToList();
            var hits=CombatHitProjection.Build(shot);
            if(hits.Length==0)lines.AddRange(shot.Events.Where(e=>e.Kind is "blastSummary" or "blastPlate" or "blastEra" or "interaction").Select(e=>e.Text));
            foreach(var hit in hits)
            {
                lines.Add(hit.Name+(hit.Stage.Length>0?" ("+hit.Stage+")":""));
                if(hit.Layer?.Combined==true)lines.Add("  Native aggregate: penetration is measured for the whole block, not each member.");
                foreach(var part in hit.Layer?.Parts??Array.Empty<CombatLayerPart>())lines.Add($"  {part.Name}: {part.MaterialLabel??"component"}; {Num(part.ThicknessMm," mm")}; {Num(part.AngleDegrees," degrees")}");
                lines.AddRange(hit.Details.Where(e=>e.Kind!="blockMembers"&&e.Kind!="materialPending"&&!CombatReportNoise.IsEndedPlate(e)).Select(e=>"  "+e.Text+(e.BeforeMm!=null||e.AfterMm!=null?$" [Pen {e.BeforeMm:0.#} -> {e.AfterMm:0.#} mm]":"")));
            }
            return string.Join("\n",lines);
        }
        string BlastKpis(CombatImpactView impact)
        {
            if(impact.BlastKpis.Length==0)return "";
            var sb=new System.Text.StringBuilder("<div class='blast-kpis'>");
            foreach(var k in impact.BlastKpis)
            {
                sb.Append($"<div class='blast-kpi' style='border-top-color:{k.Accent}'><div class='kpi-l'>{E(k.Label)}</div><div class='kpi-v' style='color:{k.Accent}'>{E(k.Value)}</div>");
                if(k.Sub.Length>0)sb.Append($"<div class='kpi-s'>{E(k.Sub)}</div>");
                sb.Append("</div>");
            }
            return sb.Append("</div>").ToString();
        }
        string BlastTable(CombatImpactView impact)
        {
            var sb=new System.Text.StringBuilder("<table><thead><tr>");
            for(int i=0;i<CombatReportView.BlastColumns.Length;i++)sb.Append("<th"+(CombatReportView.RightAligned[i]?" class='r'":"")+" style='width:"+CombatReportView.Widths[i].ToString("0",System.Globalization.CultureInfo.InvariantCulture)+"px'>"+E(CombatReportView.BlastColumns[i])+"</th>");
            sb.Append("</tr></thead><tbody>");
            foreach(var br in impact.BlastRows)
            {
                var cells=new[]{br.Index,br.Plate,br.Material,br.Thickness,br.Angle,br.Capacity,br.Result};
                sb.Append("<tr>");
                for(int i=0;i<cells.Length;i++)sb.Append("<td"+(CombatReportView.RightAligned[i]?" class='r'":"")+(i==0?" style='color:"+br.Colour+"'":"")+">"+E(cells[i]).Replace("\n","<br/>")+"</td>");
                sb.Append("</tr>");
            }
            return sb.Append("</tbody></table>").ToString();
        }
        string Table(CombatImpactView impact)
        {
            if(impact.Blast is {IsPureBlastDetonation:true})return BlastKpis(impact)+BlastTable(impact);
            var sb=new System.Text.StringBuilder("<table><thead><tr>");
            for(int i=0;i<CombatReportView.Columns.Length;i++)sb.Append("<th"+(CombatReportView.RightAligned[i]?" class='r'":"")+" style='width:"+CombatReportView.Widths[i].ToString("0",System.Globalization.CultureInfo.InvariantCulture)+"px'>"+E(CombatReportView.Columns[i])+"</th>");
            sb.Append("</tr></thead><tbody>");
            foreach(var l in impact.Layers)
            {
                var cells=new[]{l.Index,l.Name,l.Material,l.Thickness,l.Angle,l.BeforeAfter,l.Reaction};
                sb.Append("<tr>");
                for(int i=0;i<cells.Length;i++)sb.Append("<td"+(CombatReportView.RightAligned[i]?" class='r'":"")+(i==0?" style='color:"+l.Colour+"'":"")+">"+E(cells[i]).Replace("\n","<br/>")+"</td>");
                sb.Append("</tr>");
                if(l.Note.Length>0)sb.Append("<tr class='note'><td></td><td colspan='"+(cells.Length-1)+"'>"+E(l.Note)+"</td></tr>");
            }
            if(impact.Layers.Length==0)sb.Append("<tr><td colspan='"+CombatReportView.Columns.Length+"'>"+E(CombatReportView.NoLayersText)+"</td></tr>");
            return sb.Append("</tbody></table>").ToString();
        }
        var sections=impacts.Select(impact=>
            "<section><h2 id='impact-"+E(impact.Key)+"' style='border-color:"+impact.Block.Colour+"'>"+E(impact.Head.Replace(" · "+CombatReportView.DestroyedText,""))+(impact.Destroyed?" · <strong class='kill'>"+CombatReportView.DestroyedText+"</strong>":"")+"</h2>"
            +(impact.SummaryLine.Length>0?"<p class='damage'>"+E(impact.SummaryLine)+"</p>":"")
            +"<p class='context'>"+E(impact.Summary)+"</p>"
            +Table(impact)
            +(impact.Forensic is {} forensic?CombatForensicHtml.Render(forensic,"forensic-"+impact.Key):"")
            +(impact.Block.Waterfall is {Steps.Count:>0} wf?"<p class='context'>"+E(SequencePrefix+PenetrationWaterfall.Text(wf))+"</p><div class='wfbox'>"+PenetrationWaterfall.Svg(wf)+"</div>":"")
            +(byNumber.TryGetValue(impact.Key,out var shot)?"<details><summary>"+TechnicalLog+"</summary><pre>"+E(Details(shot))+"</pre></details>":"")
            +"</section>");
        string nav=impacts.Count>=3
            ?"<nav class='toc'>"+string.Join("",impacts.Select(i=>$"<a href='#impact-{E(i.Key)}' style='border-left-color:{i.Block.Colour}'><strong>{E(i.Block.NumberText)}</strong> {E(i.Block.Result)}{(i.Target.Length>0?" · "+E(i.Target):"")}</a>"))+"</nav>"
            :"";
        return "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>"+Title+"</title><style>body{max-width:1100px;margin:2em auto;padding:0 1em;font:16px system-ui;background:#171a1d;color:#e7edf2}section{margin:1.6em 0;background:#232930;border-radius:8px;padding:.2em 1em 1em}h2{font-size:1.05em;margin:.8em 0 .4em;padding-left:.7em;border-left:4px solid}p{margin:.25em 0}.damage{color:#ffcbb3}.context{color:#b8ad94;font-size:.9em}.kill{color:#e5533d}.blast-kpis{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:.75em 0}.blast-kpi{background:#191f25;border-top:2px solid #f39c52;border-radius:5px;padding:8px 11px}.kpi-l{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:#9aa6b2}.kpi-v{font-size:15px;font-weight:600;margin-top:2px}.kpi-s{font-size:12px;color:#b8ad94;margin-top:2px}.toc{display:flex;flex-wrap:wrap;gap:6px;margin:.8em 0 1.2em}.toc a{color:#e7edf2;text-decoration:none;background:#232930;border-left:3px solid;border-radius:4px;padding:4px 9px;font-size:.85em}.toc a:hover{background:#2d353e}table{border-collapse:collapse;width:100%;table-layout:fixed;margin:.6em 0;font-size:.9em}th,td{padding:.25em .5em;text-align:left;overflow-wrap:anywhere}th{color:#b8ad94;font-weight:normal;border-bottom:1px solid #3b434b}.r{text-align:right}tr.note td{color:#ffcbb3;padding-top:0}details{color:#bbc5ce}summary{cursor:pointer}.wfbox{overflow-x:auto;margin:.3em 0 .6em}svg.wf{max-width:100%;height:auto;font-family:system-ui}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px system-ui;line-height:1.6}@media(max-width:720px){.blast-kpis{grid-template-columns:1fr 1fr}}"+CombatForensicHtml.Css+"</style></head><body><h1>"+Title+"</h1><p>"+E(Intro)+"</p>"+nav+string.Join("",sections)+"</body></html>";
    }
}
