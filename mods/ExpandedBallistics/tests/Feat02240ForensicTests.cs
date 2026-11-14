using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

internal static class Feat02240ForensicTests
{
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var callback=check;check=(ok,text)=>{count++;callback(ok,"feat02240: "+text);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(Fix02228Tests.SessionRoot(),"forensic-fixtures/last-session.json")));
        var shots=JsonSerializer.Deserialize<CombatReportShot[]>(json.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        check(shots.Length==2,"real session contains both shots");
        var rows=new List<object>();
        foreach(var shot in shots)
        {
            var m=CombatForensics.Build(shot);var starts=shot.Events.Where(e=>e.Kind=="layerStart").ToArray();
            File.WriteAllText(Path.Combine(preview,$"shot-{shot.Number}-model.json"),JsonSerializer.Serialize(m,new JsonSerializerOptions{WriteIndented=true}));
            check(m.Layers.Count==starts.Length,"all reached blocks and gaps retained");
            double expectedPosition=0;
            for(int i=0;i<starts.Length;i++)
            {
                var l=m.Layers[i];var info=starts[i].Layer!;
                check(l.HitId==starts[i].HitId&&l.Number==i+1,"event order and HitId retained");
                check(l.Parts.SequenceEqual(info.Parts),"every part, angle, thickness and material equals events");
                check(l.GapMm==info.AirGapMm&&l.Combined==info.Combined,"gap and native aggregate provenance retained");
                check(Math.Abs(l.StartMm-expectedPosition)<1e-9,"geometry cumulative block order");expectedPosition+=l.PathMm??0;
                var events=shot.Events.Where(e=>e.HitId==l.HitId).ToArray();
                bool activated=events.Any(e=>e.Kind is "heavyEra" or "lightEra" or "eraReaction" or "eraResidual");
                check((l.Era=="ERA activated")==activated,"ERA activation iff event records it");
                if(l.Percent!=null)check(Math.Abs(l.Percent.Value-100*(l.Before!.Value-l.After!.Value)/shot.InitialPenetrationMm!.Value)<1e-9,"share uses budget loss, no duplicated reaction");
                rows.Add(new{shot=shot.Number,layer=l.Number,l.HitId,l.Name,l.Before,l.After,l.AbsorbedMm,l.Percent,l.GapMm,l.Era,l.StartMm,l.PathMm});
            }
            var total=m.Layers.Sum(l=>l.Percent??0)+(m.ResidualPercent??0)+100*(m.UnattributedMm??0)/m.Initial!.Value;
            check(Math.Abs(total-100)<=.5,"absorption plus rest totals 100 percent");
            check(m.Residual==0&&m.Outcome=="STOPPED","zero material reaction / nativeStop both establish stop");
            check(m.UnattributedMm is {} u&&Math.Abs(u)<1e-7,"real fixture needs no unattributed balancing loss");
            check(!m.Layers.Any(l=>l.Array)||m.Findings.Any(x=>x.Contains("composite array")&&x.Contains("front to back")),"a composite array of several plates is stated with its plates front to back (0.22.42 wording)");
            check(m.Advice.Any(x=>x.StartsWith("No additional protection was required",StringComparison.Ordinal))&&!string.Join(" ",m.Advice).Contains("margin"),"zero residual does not invent a spare margin (0.22.41 wording)");
            string text=string.Join(" ",m.Findings.Concat(m.Advice));
            foreach(var word in new[]{"erodeerde","eroded","erosion","regime","delamin","temperature","cone angle","rod length","fragment count"})
                check(!text.Contains(word,StringComparison.OrdinalIgnoreCase),"no unsupported physics: "+word);
            check(m.Damage==ForensicModel.NoDamage+".","fixture does not invent internal damage");
            foreach(bool heat in new[]{false,true})
            {
                var svg=CombatForensicDrawing.Svg(m,heat);var xml=XDocument.Parse(svg);
                check(xml.Root!.Name.LocalName=="svg"&&xml.Descendants().Any(x=>x.Name.LocalName=="polygon"),"well-formed SVG with layer geometry");
                check(svg.Contains(" mm")&&(svg.Contains("&#952; =")||svg.Contains("\u03b8 =")),"adaptive scale and measured angle present");
                var sink=new RasterSink((int)CombatForensicDrawing.Width,(int)CombatForensicDrawing.Height(m));
                CombatForensicDrawing.Draw(sink,m,heat);
                check(sink.Polygons>0&&sink.Lines>0&&sink.Texts.Count>0,"actual Kit managed raster runs headless");
                string stem=Path.Combine(preview,$"shot-{shot.Number}-"+(heat?"heat":"material"));
                sink.Save(stem);File.WriteAllText(stem+".svg",svg);
            }
        }
        File.WriteAllText(Path.Combine(output,"FORENSIC-LAYERS.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        var history=new CombatReportHistory();history.Restore(shots,0);string html=CombatReportHtml.Render(history);
        File.WriteAllText(Path.Combine(preview,"forensic.html"),html);
        var document=XDocument.Parse(html);XNamespace ns="http://www.w3.org/2000/svg";
        check(document.Descendants(ns+"svg").Count(x=>(string?)x.Attribute("class")=="forensic-section")==shots.Length*2,"each impact has both inline SVG modes");
        var ids=document.Descendants().Attributes("id").Select(a=>a.Value).ToArray();
        check(ids.Distinct().Count()==ids.Length,"no duplicate SVG clip or toggle ids across impacts");
        check(!html.Contains("<script")&&!Regex.IsMatch(html,@"(?:src|href)\s*=\s*['"" ]https?://"),"self-contained HTML, no external script/resources");
        check(html.Contains("Penetration absorbed per layer")&&html.Contains(ForensicModel.Footnote),"honest budget labels and footnote");
        var changed=shots[1] with{Events=shots[1].Events.Select(e=>e.Kind is "heavyEra" or "eraReaction" or "eraResidual"?e with{Kind="eraSpent",BeforeMm=null,AfterMm=null}:e).ToArray()};
        check(CombatForensics.Build(changed).Layers.Any(l=>l.Era=="ERA already spent"),"spent event distinguished from activation");
        var first=new CombatLayerInfo("a",new[]{new CombatLayerPart(1,"Plate","rha","Steel",10,60,true)},false);
        var synthetic=new CombatReportShot(99,"Incoming","Test",1000,new[]{new CombatReportEvent("layerStart","","",100,null,"a",first),new CombatReportEvent("plateResidual","","",100,60,"a"),new CombatReportEvent("interaction","","Crew member Gunner wounded",RelatedEntity:7),new CombatReportEvent("interaction","","Engine damaged",RelatedEntity:8)},InitialPenetrationMm:100);
        var passed=CombatForensics.Build(synthetic);
        check(passed.Residual==60&&passed.Advice.Any(a=>a.Contains("60 mm RHA-eq of additional protection")),"measured penetration deficit only");
        check(passed.Damage.Contains("wounded")&&passed.Damage.Contains("engine"),"damage summary crew/modules shown");
        var missing=CombatForensics.Build(synthetic with{InitialPenetrationMm=null,Events=new[]{new CombatReportEvent("layerStart","","",null,null,"a",first)}});
        check(missing.Layers[0].Percent==null&&missing.ResidualPercent==null,"missing budget stays unknown");
        var rico=CombatForensics.Build(synthetic with{Events=new[]{new CombatReportEvent("layerStart","","",100,null,"a",first),new CombatReportEvent("outcome","","Deflected",100,null,"a")}});
        check(rico.Outcome=="RICOCHET"&&rico.ResidualPercent==null&&rico.Layers[0].Percent==null,"ricochet never becomes absorption");
        var noGeometry=CombatForensics.Build(synthetic with{Events=Array.Empty<CombatReportEvent>()});
        check(noGeometry.Layers.Count==0&&noGeometry.Residual==null,"no geometry fallback");
        var hostile=synthetic with{Profile="<script>alert('x')</script>",Target="<img onerror='bad'>"};
        var escaped=CombatForensicHtml.Render(CombatForensics.Build(hostile),"safe");XDocument.Parse("<root>"+escaped+"</root>");
        check(!escaped.Contains("<script")&&!escaped.Contains("<img"),"names HTML escaped");
        var panel=File.ReadAllText(Path.Combine(root,"CombatForensicPanel.cs"));
        check(panel.Contains(".Fps(0)")&&panel.Contains("diagram?.Invalidate()")&&!panel.Contains("Update("),"no frame-driven model or raster work");
        check(panel.Contains("p.OnClose+=")&&panel.Contains("UiKit.DestroyPanel(old)"),"all close routes destroy panel and owned canvas texture");
        check(!panel.Contains("Harmony")&&!panel.Contains("GetField("),"Kit public API only");
        File.WriteAllText(Path.Combine(output,"FORENSIC-RESULT.json"),JsonSerializer.Serialize(new{checks=count,shots=shots.Length,headlessRaster=true,liveGameTest=false},new JsonSerializerOptions{WriteIndented=true}));
        Feat02240R2GeometryTests.Run(check,shots,output);
        File.WriteAllText(Path.Combine(output,"FORENSIC-RESULT.json"),JsonSerializer.Serialize(new{checks=count,shots=shots.Length,headlessRaster=true,liveGameTest=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FORENSIC PASS: {count} checks.");
    }
    private sealed class RasterSink:ISectionSink
    {
        private readonly Raster r=new();internal int Polygons,Lines;
        internal List<object> Texts=new();
        internal RasterSink(int w,int h){r.Resize(w,h);r.Clear(Raster.Pack(19,25,31,255));}
        private static uint U(string c){var s=c.TrimStart('#');return Raster.Pack(byte.Parse(s[..2],NumberStyles.HexNumber),byte.Parse(s[2..4],NumberStyles.HexNumber),byte.Parse(s[4..6],NumberStyles.HexNumber),s.Length==8?byte.Parse(s[6..8],NumberStyles.HexNumber):(byte)255);}
        public void ViewBox(float w,float h){}
        public void BeginClip(float w,float h)=>r.SetClip(0,0,w,h);
        public void EndClip()=>r.SetClip(0,0,r.Width,r.Height);
        public void Poly(float[] xy,string fill,string? stroke,float width){Polygons++;r.FillPolygon(xy,U(fill));if(stroke!=null)r.Polyline(xy,width,U(stroke),0,true);}
        public void Hatch(float[] xy,string c,float a,float s,float w)=>r.FillPolygon(xy,U(c),a,s,w);
        public void Outline(float[] xy,string c,float w)=>r.Polyline(xy,w,U(c),0,true);
        public void Line(float a,float b,float c,float d,string colour,float w){Lines++;r.Segment(a,b,c,d,w,U(colour));}
        public void Arrow(float a,float b,float c,float d,string colour,float w,float h)=>r.Arrow(a,b,c,d,w,h,U(colour));
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>Texts.Add(new{x,y,text,colour,background,anchor=anchor.ToString()});
        internal void Save(string stem)
        {
            // Raster packs RGBA bytes, matching Kit's Texture2D upload. Labels are separate pooled TMP objects in game.
            var bytes=new byte[r.Width*r.Height*4];for(int i=0;i<r.Width*r.Height;i++){uint p=r.Pixels[i];bytes[4*i]=(byte)p;bytes[4*i+1]=(byte)(p>>8);bytes[4*i+2]=(byte)(p>>16);bytes[4*i+3]=(byte)(p>>24);}
            File.WriteAllBytes(stem+".rgba",bytes);File.WriteAllText(stem+".labels.json",JsonSerializer.Serialize(new{width=r.Width,height=r.Height,texts=Texts}));
        }
    }
}
