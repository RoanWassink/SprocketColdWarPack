using System.Globalization;
using System.Text;
namespace SprocketExpandedBallistics;
internal static class Feat02240R2GeometryTests
{
    internal static void Run(Action<bool,string> check,CombatReportShot[] shots,string output)
    {
        var table=new StringBuilder("case\tlayer\tnormal_mm\tangle_deg\tscale_px_mm\tband_path_px\texpected_px\terror_px\tband_height_px\n");
        double Crossing(float[] p,float y)
        {
            var xs=new List<float>();
            for(int i=0;i<p.Length;i+=2){int j=(i+2)%p.Length;float ay=p[i+1],by=p[j+1];if((y>=ay&&y<by)||(y>=by&&y<ay))xs.Add(p[i]+(y-ay)*(p[j]-p[i])/(by-ay));}
            return xs.Count>=2?xs.Max()-xs.Min():0;
        }
        void Validate(ForensicModel model,string name)
        {
            var g=CombatForensicGeometry.Build(model);var sink=new Sink();CombatForensicDrawing.Draw(sink,model,false);
            check(g.Bands.Count==model.Layers.Count(l=>l.GapMm==null),"R2: exactly one band per solid/grouped block");
            foreach(var b in g.Bands.Where(b=>b.Polygon.Length>0))
            {
                var actual=Crossing(b.Polygon,g.ShotY);double expected=b.NormalMm!.Value/Math.Cos(b.Angle!.Value*Math.PI/180)*g.Scale;
                check(Math.Abs(actual-expected)<1,"R2: normal thickness / cos(theta) agrees with shot-line band width within 1 px");
                check(Math.Abs((actual*Math.Cos(b.Angle.Value*Math.PI/180))-b.NormalMm.Value*g.Scale)<1,"R2: perpendicular band width equals normal thickness in pixels");
                check(b.Top==null?Math.Abs(b.Polygon.MaxAtY()-b.Polygon.MinAtY()-CombatForensicGeometry.Height*.8f)<.01:b.Polygon.MaxAtY()-b.Polygon.MinAtY()<=CombatForensicGeometry.Height*.8f+.01&&b.Top<=g.ShotY+.01&&b.Bottom>=g.ShotY-.01,"R2: every known band occupies 80 percent of drawing height (0.22.71: a band that reaches another band ends along its edge, shorter, and still crosses the shot line)");
                check(sink.Polygons.Any(p=>p.SequenceEqual(b.Polygon)),"R2: production drawing uses tested polygon");
                table.AppendLine(FormattableString.Invariant($"{name}\t{b.Layer.Number}\t{b.NormalMm}\t{b.Angle}\t{g.Scale}\t{actual}\t{expected}\t{Math.Abs(actual-expected)}\t{g.Bottom-g.Top}"));
            }
            check(sink.Hatches==g.Bands.Count(b=>b.Layer.Array&&b.Polygon.Length>0&&b.Subs.Count==0),"R2: a composite array of several plates is one hatched band");
            foreach(var l in model.Layers)check(sink.Texts.Contains(l.Number.ToString(CultureInfo.InvariantCulture)),"R2: layer number appears on band/gap and agrees with list");
            check(new[]{50,100,200,500}.Contains(g.ScaleBarMm),"R2: supported adaptive scale bar");
            check(new[]{50,100,200,500}.All(mm=>Math.Abs(g.ScaleBarMm*g.Scale-1060*.15)<=Math.Abs(mm*g.Scale-1060*.15)+.001),"R2: scale bar nearest 15 percent of drawing width");
            check(!sink.Colours.Contains("#536370"),"R2: no right-hand label fan");
            check(sink.CalloutLengths.All(n=>n<=100),"R2: reaction label leaders are local");
        }
        foreach(var shot in shots)
        {
            var m=CombatForensics.Build(shot);Validate(m,"fixture-"+shot.Number);
            var sink=new Sink();CombatForensicDrawing.Draw(sink,m,false);
            check(sink.Texts.Any(t=>t.StartsWith("Arrested in layer ",StringComparison.Ordinal))&&!sink.Texts.Contains("depth not recorded"),"R2: stop marker identifies the layer; no depth statement (0.22.41)");
            check(m.Layers.Where(l=>l.Array).All(l=>CombatForensicGeometry.LayerName(l)=="Composite array"&&CombatForensicGeometry.Members(l).Length>0),"R2: an array names its plates in order (0.22.42)");
            check(!string.Join(" ",sink.Texts).Contains("activation not recorded"),"R2: no-reaction wording updated");
            check(sink.Texts.Any(t=>t.Contains("ERA initiated")),"R2: compact ERA-initiated label (0.22.41)");
        }
        foreach(double angle in new[]{0d,15,30,45,65,80})foreach(double normal in new[]{.5,5,12,50,215})
        {
            var part=new CombatLayerPart(1,"Plate","rha","Steel",normal,angle,true);var info=new CombatLayerInfo("plate",new[]{part},false);
            var shot=new CombatReportShot(100,"Outgoing","Geometry test",1000,new[]{new CombatReportEvent("layerStart","","",100,null,"plate",info),new CombatReportEvent("plateResidual","","",100,60,"plate")},InitialPenetrationMm:100);
            var m=CombatForensics.Build(shot);Validate(m,$"normal-{normal}-angle-{angle}");
            var sink=new Sink();CombatForensicDrawing.Draw(sink,m,false);
            check(sink.Texts.Any(t=>t=="Residual 60 mm RHA-eq"),"R2: penetrated shot has residual arrow/label");
        }
        // 0.22.41: the R2-against-R1 source comparison (out-0.22.40/r1-baseline) is retired; the model text changed by design.
        // The arithmetic is guarded by the share, zero-span and golden checks of Fix02241Tests instead.
        var root=Fix02228Tests.SourceRoot();var model=File.ReadAllText(Path.Combine(root,"CombatForensics.cs"));
        check(model.Contains("var after=terminal?.AfterMm??events.LastOrDefault(x=>x.AfterMm!=null&&x.Kind is \"materialReaction\" or \"eraResidual\" or \"eraReaction\")?.AfterMm;")&&model.Contains("main&&initial is >0&&loss!=null?100*loss/initial:null"),"R2: forensic exit and share arithmetic kept (0.22.41 source)");
        var panel=File.ReadAllText(Path.Combine(root,"CombatForensicPanel.cs"));
        check(panel.Contains("AbsorptionBar(r,l.Percent,l.Colour)")&&panel.Contains("d.Rect(2,2,14,14,l.Colour)"),"R2: actual filled material swatches and coloured proportional bars in Kit panel");
        check(panel.Contains("var cols=content.Columns(0,0)")&&panel.Contains("cols[1].Label(ForensicModel.AdviceTitle"),"R2: Findings and Advice remain two columns when Advice has content");
        File.WriteAllText(Path.Combine(output,"R2-GEOMETRY.tsv"),table.ToString());
    }
    private static float MaxAtY(this float[] p)=>Enumerable.Range(0,p.Length/2).Max(i=>p[i*2+1]);
    private static float MinAtY(this float[] p)=>Enumerable.Range(0,p.Length/2).Min(i=>p[i*2+1]);
    private sealed class Sink:ISectionSink
    {
        internal List<float[]> Polygons=new();internal List<string> Texts=new(),Colours=new();internal List<double> CalloutLengths=new();internal int Hatches;
        public void ViewBox(float w,float h){}public void BeginClip(float w,float h){}public void EndClip(){}
        public void Poly(float[] p,string f,string? s,float w)=>Polygons.Add(p);
        public void Hatch(float[] p,string c,float a,float s,float w)=>Hatches++;
        public void Outline(float[] p,string c,float w){}
        public void Line(float x,float y,float a,float b,string c,float w){Colours.Add(c);if(c=="#d9af71")CalloutLengths.Add(Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b)));}
        public void Arrow(float x,float y,float a,float b,string c,float w,float h){}
        public void Region(string id,float[] p){}
        public void Text(float x,float y,string text,string c,SectionAnchor a,string? b)=>Texts.Add(text);
    }
}
