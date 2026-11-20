using System.Net;
using System.Text;
namespace SprocketExpandedBallistics;
internal static class CombatForensicHtml
{
    internal const string Css=".forensic{border-top:1px solid #45505b;margin-top:20px;padding-top:8px}.forensic svg{width:100%;height:auto}.fx-heat{display:none}.forensic input:checked~.fx-material{display:none}.forensic input:checked~.fx-heat{display:block}.fx-chip{display:inline-block;border-radius:4px;padding:4px 9px;background:#39424b}.fx-cols{display:grid;grid-template-columns:1fr 1fr;gap:20px}.fx-cols.fx-one{grid-template-columns:1fr}.fx-row{display:grid;grid-template-columns:32px 14px minmax(120px,1fr) 160px 65px;align-items:center;gap:8px;margin:7px 0}.fx-row.fx-sub{margin:2px 0;font-size:12px;color:#c9d1d8}.fx-row.fx-sub[data-stop]{color:#ffac91;font-weight:600}.fx-row.fx-sub .fx-name{padding-left:22px}.fx-row.fx-deep{font-size:11.5px;color:#aeb8c1}.fx-row.fx-deep .fx-name{padding-left:44px}.fx-swatch{width:12px;height:12px;display:inline-block}.fx-sub .fx-swatch{width:10px;height:10px;outline:1px solid #5a6570}.fx-track{height:8px;background:#141a20}.fx-track i{display:block;height:100%}.fx-bar-label{text-align:right}.fx-note{color:#b8ad94;font-size:12px;margin:8px 0}.fx-legend{background:linear-gradient(90deg,#377da0,#ff5537);height:8px;max-width:250px}.fx-mode{cursor:pointer;display:inline-block;margin:10px 0}@media(max-width:650px){.fx-cols{grid-template-columns:1fr}.fx-row{grid-template-columns:25px 12px 1fr 65px}.fx-track{display:none}}";
    internal static string Render(ForensicModel m,string key)
    {
        static string E(string s)=>WebUtility.HtmlEncode(s);
        // All ids, including clip paths, must be unique across both modes and every impact.
        string Svg(bool heat)=>CombatForensicDrawing.Svg(m,heat,key+(heat?"-heat":"-material"));
        var s=new StringBuilder($"<div class='forensic'><h3>{E(ForensicModel.Title)}</h3><p><strong class='fx-chip' style='color:{m.Colour}'>{E(m.Outcome)}</strong> {E(m.Explanation)}</p><p class='context'>{E(m.Context)}</p><input type='checkbox' id='{E(key)}-mode'/><label class='fx-mode' for='{E(key)}-mode'>{E(ForensicModel.ModeLabel)}</label><div class='fx-material'>{Svg(false)}</div><div class='fx-heat'>{Svg(true)}</div><p class='fx-note'>{E(ForensicModel.GeometryNote)}</p><h4>{E(ForensicModel.ListTitle)}</h4>");
        void Row(string number,string name,string colour,double? percent,string detail)
        {
            // Only measured shares are shown; a layer without one keeps an empty bar and no value.
            string value=percent!=null?CombatForensics.N(percent)+"%":"";
            s.Append($"<div class='fx-row'><span>{E(number)}</span><i class='fx-swatch' style='background:{colour}'></i><span class='fx-name'>{E(name)}{(detail.Length>0?" <small>"+E(detail)+"</small>":"")}</span><span class='fx-track'><i style='width:{CombatForensics.N(Math.Clamp(percent??0,0,100))}%;background:{colour}'></i></span><span class='fx-bar-label'>{E(value)}</span></div>");
        }
        // An indented row without a share: a preset constituent, an array plate, or a constituent of an array plate (one level deeper).
        void Sub(string css,string colour,string name,string detail,bool stop=false)=>
            s.Append($"<div class='{css}'{(stop?" data-stop='1'":"")}><span></span><i class='fx-swatch' style='background:{colour}'></i><span class='fx-name'>{E(name)}{(detail.Length>0?" <small>"+E(detail)+"</small>":"")}</span><span></span><span></span></div>");
        foreach(var l in m.Layers)
        {
            if(l.GapMm!=null){s.Append($"<div class='fx-row'><span>{l.Number}</span><i class='fx-swatch' style='background:{l.Colour}'></i><span class='fx-name'>Air gap <small>{E(l.Geometry)}</small></span><span></span><span class='fx-bar-label'>&#8211;</span></div>");continue;}
            Row(l.Number.ToString(),CombatForensicGeometry.LayerName(l),l.Colour,l.Percent,CombatForensicGeometry.Detail(l));
            // 0.22.41: the constituents of a composer preset, indented, without shares (the preset has one protection factor).
            foreach(var paid in l.PaidIntervals??Array.Empty<string>())Sub("fx-row fx-sub fx-paid",l.Colour,paid,"");
            // 0.22.91: the constituents of a layered module (its confirmed intervals), indented, with path, derived normal, factor, RHA-eq and the derived share of the reduction.
            if(l.ModuleLayers!=null)foreach(var ml in l.ModuleLayers)Sub("fx-row fx-sub fx-module-layer",ml.Colour,ml.Label,ml.Detail);
            else foreach(var sub in l.Sublayers??Array.Empty<CompositeSublayer>())Sub("fx-row fx-sub",sub.Colour,sub.Label,CombatForensicGeometry.SubDetail(l,sub),CombatForensicGeometry.IsStopSublayer(l,sub));
            // 0.22.43: an internal module in the same native group as the plate, indented without a share (the share is the layer's).
            if(l.Grouped)Sub("fx-row fx-sub fx-module",CombatEnvelope.Grey,CombatForensics.Capital(l.Module),CombatForensicGeometry.ModuleDetail(l));
            // 0.22.42: the plates of a composite array front to back, indented without shares; under a preset plate its constituents.
            foreach(var plate in l.Plates??Array.Empty<ForensicArrayPlate>())
            {
                Sub("fx-row fx-sub fx-plate",plate.Colour,plate.Name,CombatForensicGeometry.PlateDetail(plate));
                foreach(var sub in plate.Sublayers??Array.Empty<CompositeSublayer>())Sub("fx-row fx-sub fx-deep",sub.Colour,sub.Label,CombatForensicGeometry.SubDetail(sub));
            }
        }
        if(m.UnattributedMm is {} u&&Math.Abs(u)>.01)Row("",m.BetweenLabel,"#7c7566",m.Initial is >0?100*Math.Abs(u)/m.Initial:null,CombatForensics.N(Math.Abs(u))+" mm RHA-eq");
        // 0.22.43: the residual row names the last layer ("Residual penetration behind layer 16").
        if(m.HasResidualRow)Row("",m.RestLabel,"#8fd99c",m.ResidualPercent,CombatForensics.N(m.Residual)+" mm RHA-eq");
        s.Append($"<p class='fx-note'>{E(ForensicModel.Footnote)}</p><div class='fx-legend'></div>");
        s.Append(m.Advice.Count>0?"<div class='fx-cols'>":"<div class='fx-cols fx-one'>");
        s.Append($"<div><h4>{E(ForensicModel.FindingsTitle)}</h4><ul>");
        foreach(var f in m.Findings)s.Append("<li>"+E(f)+"</li>");s.Append("</ul></div>");
        if(m.Advice.Count>0){s.Append($"<div><h4>{E(ForensicModel.AdviceTitle)}</h4><ul>");foreach(var a in m.Advice)s.Append("<li>"+E(a)+"</li>");s.Append("</ul></div>");}
        return s.Append("</div></div>").ToString();
    }
}
