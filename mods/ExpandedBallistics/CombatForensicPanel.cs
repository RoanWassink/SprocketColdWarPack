using SprocketUiKit;
using UnityEngine;
namespace SprocketExpandedBallistics;

/// <summary>Report snapshot. No Update method, polling model builder, new hooks, or private Kit API.</summary>
internal static class CombatForensicPanel
{
    internal static bool IsOpen=>panel?.IsOpen==true;
    private static Panel? panel;
    internal static void Clear()
    {
        var old=panel;panel=null;
        if(old!=null)UiKit.DestroyPanel(old); // Kit Unbuild destroys the CanvasView Texture2D and text pools.
    }
    internal static void Open(ForensicModel model,IReadOnlyList<CombatImpactView>? impacts=null)
    {
        Clear();
        var p=UiKit.CreatePanel(BallisticsKeybindMetadata.Owner,"combat-forensic",ForensicModel.Title,"Expanded Ballistics | impact #"+model.Number,1180,850);
        panel=p;p.Resizable=false;p.Maximizable=false;
        p.AllowedContexts=UiContexts.All;
        p.StayOpenWhilePaused=true;
        p.OnClose+=()=>{if(ReferenceEquals(panel,p))Clear();};
        var c=p.Columns(0)[0];c.Height(0);var content=c.ScrollView().Fill();
        var head=content.Row(8);head.Chip(model.Outcome,UiKit.Colour(model.Colour)).Width(CombatReportView.ResultChipWidth);
        head.Label(model.Explanation,TextStyle.Small).Wrap();
        content.Label(model.Context,TextStyle.Small).Wrap();
        if(model.BlastSummary is {Count:>0} blast)
        {
            content.Label(ForensicModel.BlastTitle,TextStyle.Section);
            foreach(var line in blast)content.Label("- "+line,TextStyle.Small).Colour(line.Contains("breached",StringComparison.OrdinalIgnoreCase)?"#ffac91":"#f3cf86").Wrap();
        }
        bool heat=false;CanvasView? diagram=null;
        var mode=content.Row(8);mode.Label(ForensicModel.ModeLabel,TextStyle.Small);
        mode.Toggle(()=>heat,value=>{heat=value;diagram?.Invalidate();});
        diagram=content.Canvas(CombatForensicDrawing.Height(model),d=>CombatForensicDrawing.Draw(new PainterSink(d),model,heat)).Fps(0);
        content.Label(ForensicModel.GeometryNote,TextStyle.Small).Wrap();
        content.Label(ForensicModel.ListTitle,TextStyle.Section);
        foreach(var l in model.Layers)
        {
            var r=content.Row(6);
            r.Label(l.Number.ToString("00"),TextStyle.Small).Width(28);
            r.Canvas(18,d=>{d.ViewBox(0,0,18,18);d.Rect(2,2,14,14,l.Colour);}).Fps(0).Width(18);
            var detail=l.GapMm!=null?l.Geometry:CombatForensicGeometry.Detail(l);
            var name=l.GapMm!=null?"Air gap":CombatForensicGeometry.LayerName(l);
            var text=r.Label(name+(detail.Length>0?" | "+detail:""),TextStyle.Small).Width(650).Wrap();
            if(l.Array||l.Era.Length>0)text.Tooltip(l.Array?CombatForensicGeometry.Members(l)+(l.Era.Length>0?" | "+CombatForensicGeometry.EraLabel(l):""):CombatForensicGeometry.EraLabel(l));
            if(l.GapMm==null)AbsorptionBar(r,l.Percent,l.Colour);
            else r.Label("",TextStyle.Small).Width(160);
            // Only measured shares: a gap shows a dash, a layer without a measured share shows nothing.
            r.Label(l.GapMm!=null?"–":l.Percent!=null?CombatForensics.N(l.Percent)+"%":"",TextStyle.Value).Width(65).AlignRight();
            // 0.22.41: the constituents of a composer preset, indented under the plate, without shares.
            foreach(var paid in l.PaidIntervals??Array.Empty<string>())SubRow(content,46,l.Colour,paid);
            // 0.22.91: the constituents of a layered module (its confirmed intervals), indented, without shares.
            if(l.ModuleLayers!=null)foreach(var ml in l.ModuleLayers)SubRow(content,46,ml.Colour,ml.Label+" | "+ml.Detail);
            else foreach(var sub in l.Sublayers??Array.Empty<CompositeSublayer>())
                SubRow(content,46,sub.Colour,sub.Label+" | "+CombatForensicGeometry.SubDetail(l,sub),CombatForensicGeometry.IsStopSublayer(l,sub)?"#ffac91":null);
            // 0.22.43: an internal module in the same native group as the plate, indented without a share.
            if(l.Grouped)SubRow(content,46,CombatEnvelope.Grey,CombatForensics.Capital(l.Module)+" | "+CombatForensicGeometry.ModuleDetail(l));
            // 0.22.42: the plates of a composite array front to back, indented without shares; under a preset plate its constituents.
            foreach(var plate in l.Plates??Array.Empty<ForensicArrayPlate>())
            {
                var detail2=CombatForensicGeometry.PlateDetail(plate);
                SubRow(content,46,plate.Colour,plate.Name+(detail2.Length>0?" | "+detail2:""));
                foreach(var sub in plate.Sublayers??Array.Empty<CompositeSublayer>())SubRow(content,68,sub.Colour,sub.Label+" | "+CombatForensicGeometry.SubDetail(sub));
            }
        }
        if(model.UnattributedMm is {} u&&Math.Abs(u)>.01)
        {
            var between=content.Row(8);between.Label(model.BetweenLabel+": "+CombatForensics.N(Math.Abs(u))+" mm RHA-eq",TextStyle.Small).Width(700);
            AbsorptionBar(between,model.Initial is >0?100*Math.Abs(u)/model.Initial:null,"#7c7566");
        }
        if(model.HasResidualRow)
        {
            // 0.22.43: the residual row names the last layer ("Residual penetration behind layer 16").
            var rest=content.Row(8);rest.Label(model.RestLabel+": "+CombatForensics.N(model.Residual)+" mm RHA-eq",TextStyle.Label).Width(700);
            AbsorptionBar(rest,model.ResidualPercent,"#8fd99c");
            rest.Label(CombatForensics.N(model.ResidualPercent)+"%",TextStyle.Value).Width(65);
        }
        content.Label(ForensicModel.Footnote,TextStyle.Small).Wrap();
        // A column with nothing measured to say is left out.
        if(model.Advice.Count>0)
        {
            var cols=content.Columns(0,0);
            cols[0].Label(ForensicModel.FindingsTitle,TextStyle.Section);foreach(var t in model.Findings)cols[0].Label("- "+t,TextStyle.Small).Wrap();
            cols[1].Label(ForensicModel.AdviceTitle,TextStyle.Section);foreach(var t in model.Advice)cols[1].Label("- "+t,TextStyle.Small).Wrap();
        }
        else
        {
            content.Label(ForensicModel.FindingsTitle,TextStyle.Section);foreach(var t in model.Findings)content.Label("- "+t,TextStyle.Small).Wrap();
        }
        var footer=c.Footer().Row(8);
        var nav=impacts?.Where(x=>x.Forensic!=null).ToArray();
        int idx=nav!=null?Array.FindIndex(nav,x=>x.Forensic!.Number==model.Number):-1;
        if(nav!=null&&nav.Length>1&&idx>=0)
        {
            if(idx>0)footer.Button("Newer impact",()=>Open(nav[idx-1].Forensic!,impacts),ButtonStyle.Quiet);
            footer.Label($"Impact {idx+1} of {nav.Length}",TextStyle.Small);
            if(idx<nav.Length-1)footer.Button("Older impact",()=>Open(nav[idx+1].Forensic!,impacts),ButtonStyle.Quiet);
        }
        footer.Spacer();
        footer.Button("Close",()=>p.Close(),ButtonStyle.Quiet);
        if(!p.Open())Clear();
    }
    /// <summary>An indented row without a share: a small swatch and one text (preset constituent, array plate, or a constituent of an array plate).</summary>
    private static void SubRow(Container content,float indent,string colour,string text,string? textColour=null)
    {
        var sr=content.Row(6);
        sr.Label("",TextStyle.Small).Width(indent);
        sr.Canvas(14,d=>{d.ViewBox(0,0,14,14);d.Rect(1,1,12,12,"#5a6570");d.Rect(2,2,10,10,colour);}).Fps(0).Width(14);
        var label=sr.Label(text,TextStyle.Small).Width(676-indent).Wrap();
        if(textColour!=null)label.Colour(textColour);
    }
    private static void AbsorptionBar(Row row,double? percent,string colour)=>row.Canvas(18,d=>
    {
        d.ViewBox(0,0,160,18);d.Rect(0,4,160,10,"#141a20");
        var width=(float)(160*Math.Clamp((percent??0)/100,0,1));
        if(width>0)d.Rect(0,4,width,10,colour);
    }).Fps(0).Width(160);
    private sealed class PainterSink:ISectionSink
    {
        private readonly Painter p;internal PainterSink(Painter p)=>this.p=p;
        public void ViewBox(float width,float height)=>p.ViewBox(0,0,width,height);
        public void BeginClip(float width,float height){p.Push(0,0);p.Clip(0,0,width,height);}
        public void EndClip()=>p.Pop();
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)=>p.Poly(xy,fill,stroke,strokePx);
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx)=>p.Hatch(xy,colour,angleDeg,spacingPx,widthPx);
        public void Outline(float[] xy,string colour,float widthPx)=>p.Poly(xy,"#00000000",colour,widthPx);
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>p.Line(x0,y0,x1,y1,colour,widthPx);
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)=>p.Arrow(x0,y0,x1,y1,colour,widthPx,headPx);
        public void Region(string id,float[] xy)=>p.Region(id,xy);
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>
            p.Text(x,y,text,TextStyle.Small,colour,anchor switch{SectionAnchor.MiddleLeft=>TextAnchor.MiddleLeft,SectionAnchor.MiddleRight=>TextAnchor.MiddleRight,_=>TextAnchor.MiddleCenter},0,background);
    }
}
