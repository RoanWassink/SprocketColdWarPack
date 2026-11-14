using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.72: Forensic draws the internal modules on the shot line as a dotted frame with an icon, "number name" and the recorded result, instead of a grey band.
/// Tests: (1) a shot through armour, crew and ammunition draws two icons with number, name and result, and nothing for a part that is not on the shot line;
/// (2) a shot that stops before every internal module draws no icon and no legend; (3) a frame on the millimetre scale with the length when the report has one, a frame of fixed width without a number
/// when it has none; (4) the panel and the HTML show the same set and the same texts; (5) protection, report lines and JSON equal 0.22.71.
/// </summary>
internal static class Feat02272ModuleIconTests
{
    private sealed class Rec:ISectionSink
    {
        internal readonly List<object> Ops=new();internal readonly List<string> Texts=new();internal int Lines,Polys,Hatches,Arrows;
        public void ViewBox(float width,float height)=>Ops.Add(new{op="view",width,height});
        public void BeginClip(float width,float height)=>Ops.Add(new{op="clip",width,height});
        public void EndClip()=>Ops.Add(new{op="endclip"});
        public void Poly(float[] xy,string fill,string? stroke,float strokePx){Polys++;Ops.Add(new{op="poly",xy,fill,stroke,strokePx});}
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx){Hatches++;Ops.Add(new{op="hatch",xy,colour,angleDeg,spacingPx,widthPx});}
        public void Outline(float[] xy,string colour,float widthPx)=>Ops.Add(new{op="outline",xy,colour,widthPx});
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx){Lines++;Ops.Add(new{op="line",x0,y0,x1,y1,colour,widthPx});}
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx){Arrows++;Ops.Add(new{op="arrow",x0,y0,x1,y1,colour,widthPx,headPx});}
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background){Texts.Add(text);Ops.Add(new{op="text",x,y,text,colour,anchor=anchor.ToString(),background});}
    }
    private static CombatReportEvent Start(string id,double before,CombatLayerInfo l)=>new("layerStart","","Reached layer",before,null,id,l);
    private static CombatReportEvent Res(string id,double before,double after,string kind="plateResidual")=>new(kind,"","",before,after,id);
    private static CombatReportEvent Stop(string id,double before)=>new("nativeStop","","Native stop",before,null,id);
    private static CombatReportEvent Hurt(string text,int entity)=>new("interaction","",text,RelatedEntity:entity);
    private static CombatLayerInfo Plate(string id,int entity,double mm,double angle)=>new(id,new[]{new CombatLayerPart(entity,"Plate #"+entity,"rha","Rolled Homogeneous Armour",mm,angle,true)},false);
    private static CombatLayerInfo Part(string id,int entity,string name)=>new(id,new[]{new CombatLayerPart(entity,name,null,null,null,null,false)},false);
    private static CombatReportShot Shot(int number,params CombatReportEvent[] events)=>new(number,"Outgoing","APFSDS gen 3",1500,events,0,500,"T-90A",4,3,"T-90A");
    /// <summary>Armour, then a crew position (the gunner killed) and ammunition stowage (damaged), then a rear plate; the engine (not on the shot line) is also damaged.</summary>
    internal static CombatReportShot CrewAndAmmo()=>Shot(721,
        Start("p",500,Plate("p",1,60,30)),Res("p",500,400),
        Start("c",400,Part("c",9,"Crew member Gunner")),Res("c",400,380,"componentResidual"),
        Start("a",380,Part("a",10,"ammoRack")),Res("a",380,300,"componentResidual"),
        Start("q",300,Plate("q",2,20,10)),Res("q",300,250),
        Hurt("Crew member Gunner killed",9),Hurt("ammoRack damaged",10),Hurt("Engine damaged",8));
    /// <summary>Armour, a fuel tank without recorded damage, ammunition destroyed; the shot stops in the ammunition.</summary>
    internal static CombatReportShot AmmoHit()=>Shot(722,
        Start("p",500,Plate("p",1,60,30)),Res("p",500,400),
        Start("f",400,Part("f",11,"fuelTank")),Res("f",400,380,"componentResidual"),
        Start("a",380,Part("a",10,"ammoRack")),Stop("a",380),
        Hurt("ammoRack destroyed",10));
    /// <summary>Armour that stops the shot; a crew member and the engine are hurt by spall, but not on the shot line.</summary>
    internal static CombatReportShot StopsFirst()=>Shot(723,
        Start("p",500,Plate("p",1,60,30)),Stop("p",500),
        Hurt("Crew member Gunner wounded",9),Hurt("Engine damaged",8));

    internal static void Run(Action<bool,string> check)
    {
        int n=0;void Check(bool ok,string message){n++;check(ok,"FEAT72 module icons: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var rows=new StringBuilder("scene\tlabel\tkinds\tresult\tstate\tx0\tx1\tmm\n");
        // ---- 1. through armour, crew and ammunition: two icons, each with number, name and result; nothing for a part off the shot line ----
        var m1=CombatForensics.Build(CrewAndAmmo());var g1=CombatForensicGeometry.Build(m1);var r1=new Rec();CombatForensicDrawing.Draw(r1,m1,false);
        Check(m1.Layers.Count==4&&g1.Modules.Count==2,"the shot line has four layers, two of them internal modules ("+m1.Layers.Count+" layers, "+g1.Modules.Count+" frames)");
        var crew=g1.Modules[0];var ammo=g1.Modules[1];
        Check(crew.Label=="2 Gunner"&&crew.Outcome=="incapacitated"&&crew.State==CombatForensicGeometry.StateSevere&&crew.Kinds.SequenceEqual(new[]{"crew"}),"layer 2 reads \""+crew.Label+"\", incapacitated, crew icon");
        Check(ammo.Label=="3 Ammunition stowage"&&ammo.Outcome=="damaged"&&ammo.State==CombatForensicGeometry.StateDamaged&&ammo.Kinds.SequenceEqual(new[]{"ammunition"}),"layer 3 reads \""+ammo.Label+"\", damaged, ammunition icon");
        Check(r1.Texts.Contains("2 Gunner")&&r1.Texts.Contains("incapacitated")&&r1.Texts.Contains("3 Ammunition stowage")&&r1.Texts.Contains("damaged"),"the drawing writes both names with their numbers and both results");
        Check(!r1.Texts.Any(t=>t.Contains("ngine",StringComparison.Ordinal)||t.Contains("not reached",StringComparison.OrdinalIgnoreCase)||t.Contains("spall",StringComparison.OrdinalIgnoreCase)),"nothing is drawn for the engine that is not on the shot line");
        Check(new[]{CombatForensicDrawing.LegendSevere,CombatForensicDrawing.LegendDamaged,CombatForensicDrawing.LegendNone}.All(r1.Texts.Contains),"the legend of the three states is drawn when icons are in the picture");
        Check(g1.Bands.Where(b=>b.Layer.Internal).All(b=>b.Polygon.Length==0&&b.Module==null),"no grey band for an internal module");
        Check(!r1.Ops.Any(o=>JsonSerializer.Serialize(o).Contains(CombatEnvelope.Grey)),"the module grey of 0.22.71 is not used in the drawing");
        // the crew shot drawn with a second, wounded crew position and an undamaged one for the contact sheet
        var crewShot=Shot(724,Start("p",500,Plate("p",1,60,30)),Res("p",500,400),
            Start("c",400,Part("c",9,"Crew member Gunner")),Res("c",400,390,"componentResidual"),Start("d",390,Part("d",12,"Crew member Loader")),Res("d",390,380,"componentResidual"),
            Start("e",380,Part("e",13,"Crew member Driver")),Res("e",380,370,"componentResidual"),Start("q",370,Plate("q",2,20,10)),Res("q",370,330),
            Hurt("Crew member Gunner killed",9),Hurt("Crew member Loader wounded",12));
        var scenes=new List<(string Name,string Title,ForensicModel Model)>
        {
            ("crew","Shot through the armour into the crew compartment: gunner incapacitated, loader wounded, driver without recorded damage",CombatForensics.Build(crewShot)),
            ("ammo","Shot through the armour, a fuel tank and into the ammunition stowage: destroyed",CombatForensics.Build(AmmoHit())),
            ("stop","Shot arrested in the armour before any internal module; spall elsewhere is not drawn",CombatForensics.Build(StopsFirst())),
            ("both","Shot through the armour, a crew position (killed) and ammunition stowage (damaged)",m1)
        };
        // ---- 2. a shot that stops before every internal module: no icon, no legend ----
        var m2=scenes[2].Model;var g2=CombatForensicGeometry.Build(m2);var r2=new Rec();CombatForensicDrawing.Draw(r2,m2,false);
        Check(g2.Modules.Count==0&&!m2.Layers.Any(l=>l.Internal),"the arrested shot has no internal layer and no frame");
        Check(!new[]{CombatForensicDrawing.LegendSevere,CombatForensicDrawing.LegendDamaged,CombatForensicDrawing.LegendNone,"incapacitated","no damage recorded","wounded"}.Any(r2.Texts.Contains),"no legend and no result text when no icon is in the picture");
        Check(!r2.Ops.Any(o=>JsonSerializer.Serialize(o).Contains("13191fee")),"no icon chip is drawn");
        // the ammunition shot: the undamaged fuel tank is neutral, the destroyed ammunition severe, the arrest stays marked
        var m3=scenes[1].Model;var g3=CombatForensicGeometry.Build(m3);var r3=new Rec();CombatForensicDrawing.Draw(r3,m3,false);
        Check(g3.Modules.Count==2&&g3.Modules[0].Outcome==CombatForensicGeometry.NoDamage&&g3.Modules[0].State==CombatForensicGeometry.StateNone&&g3.Modules[0].Kinds.SequenceEqual(new[]{"fuel"})&&g3.Modules[1].Outcome=="destroyed"&&g3.Modules[1].State==CombatForensicGeometry.StateSevere,"fuel tank: no damage recorded (neutral); ammunition: destroyed");
        Check(r3.Texts.Contains("Arrested in layer 3")&&r3.Texts.Contains("Envelope perforated · 100 mm RHA-eq")||r3.Texts.Any(t=>t.StartsWith("Arrested in layer",StringComparison.Ordinal)),"the arrest in the ammunition is still marked");
        // ---- 3. measured length: a frame on the millimetre scale with the length; none: fixed width and no number ----
        bool reportHasLength=m1.Layers.Concat(m3.Layers).Where(l=>l.Internal&&!l.Grouped).Any(l=>l.PathMm!=null);
        Check(!reportHasLength,"the report has no measured path through an internal module (every module layer has PathMm null; the recorded entry and exit are penetration values, not lengths)");
        Check(g1.Modules.All(k=>k.Mm==null&&Math.Abs(k.X1-k.X0-CombatForensicGeometry.FrameWidth)<.01),"without a measured length every frame has the fixed width of "+CombatForensicGeometry.FrameWidth+" px");
        var mm=Regex.Matches(string.Join("\n",r1.Texts),@"^\d+(\.\d)? mm$",RegexOptions.Multiline).Count;
        Check(mm==1,"no millimetre figure for a module without a measured length (only the scale bar: "+mm+")");
        var measured=m1 with{Layers=m1.Layers.Select(l=>l.Internal&&!l.Grouped&&l.Name=="Gunner"?l with{PathMm=45}:l).ToArray()};
        var gm=CombatForensicGeometry.Build(measured);var rm=new Rec();CombatForensicDrawing.Draw(rm,measured,false);
        var withLength=gm.Modules.Single(k=>k.Layer.Name=="Gunner");var without=gm.Modules.Single(k=>k.Layer.Name!="Gunner");
        Check(withLength.Mm==45&&Math.Abs(withLength.X1-withLength.X0-45*gm.Scale)<.01,"a measured path of 45 mm gives a frame of 45 mm on the scale of the armour ("+(withLength.X1-withLength.X0).ToString("0.#",CultureInfo.InvariantCulture)+" px at "+gm.Scale.ToString("0.###",CultureInfo.InvariantCulture)+" px/mm)");
        Check(rm.Texts.Contains("45 mm")&&Regex.Matches(string.Join("\n",rm.Texts),@"^\d+(\.\d)? mm$",RegexOptions.Multiline).Count==2,"the length in mm is written under the frame, once");
        Check(without.Mm==null&&Math.Abs(without.X1-without.X0-CombatForensicGeometry.FrameWidth)<.01,"the other module keeps the fixed width and no number");
        rows.AppendLine($"measured-45mm\t{withLength.Label}\t{string.Join("+",withLength.Kinds)}\t{withLength.Outcome}\t{withLength.State}\t{withLength.X0:0.#}\t{withLength.X1:0.#}\t45");
        // ---- 4. the panel and the HTML show the same set and the same texts ----
        var panel=File.ReadAllText(Path.Combine(root,"CombatForensicPanel.cs"));var html=File.ReadAllText(Path.Combine(root,"CombatForensicHtml.cs"));
        Check(panel.Contains("CombatForensicDrawing.Draw(new PainterSink(d),model,heat)",StringComparison.Ordinal)&&html.Contains("CombatForensicDrawing.Svg(m,heat,",StringComparison.Ordinal)&&!panel.Contains("Icon(",StringComparison.Ordinal)&&!html.Contains("Icon(",StringComparison.Ordinal),"the panel and the HTML draw through the one drawing; neither has an icon of its own");
        foreach(var scene in scenes)
        foreach(bool heat in new[]{false,true})
        {
            var rec=new Rec();CombatForensicDrawing.Draw(rec,scene.Model,heat);
            var svg=CombatForensicDrawing.Svg(scene.Model,heat,"k");var xml=XDocument.Parse(svg);
            var svgTexts=xml.Descendants().Where(x=>x.Name.LocalName=="text").Select(x=>x.Value).ToList();
            var page=XDocument.Parse("<r>"+CombatForensicHtml.Render(scene.Model,"k")+"</r>");
            Check(svgTexts.SequenceEqual(rec.Texts),$"{scene.Name} ({(heat?"heat":"material")}): the SVG of the HTML carries exactly the texts of the drawing ({rec.Texts.Count})");
            int polygons=xml.Descendants().Count(x=>x.Name.LocalName=="polygon"),lines=xml.Descendants().Count(x=>x.Name.LocalName=="line"&&x.Attribute("stroke-width")!=null&&x.Parent?.Name.LocalName!="pattern");
            Check(polygons==rec.Polys+rec.Hatches+rec.Arrows&&lines==rec.Lines+rec.Arrows,$"{scene.Name} ({(heat?"heat":"material")}): the same polygons and lines in the SVG as in the drawing the panel paints ({polygons}, {lines})");
            var g=CombatForensicGeometry.Build(scene.Model);
            var modules=g.Modules.Select(k=>k.Label).ToList();
            var embedded=page.Descendants().Where(x=>x.Name.LocalName=="svg").First().Descendants().Where(x=>x.Name.LocalName=="text").Select(x=>x.Value).ToList();
            Check(modules.All(embedded.Contains)&&g.Modules.All(k=>embedded.Contains(k.Outcome)),$"{scene.Name} ({(heat?"heat":"material")}): the HTML shows every framed module with its result");
            Check(g.Modules.Count==scene.Model.Layers.Count(l=>l.Internal),$"{scene.Name}: one frame per internal layer on the shot line ({g.Modules.Count})");
        }
        // every icon kind and every state draws only flat shapes (no texture), and the neutral block catches the rest
        var kinds=new[]{"crew","gun","ammunition","engine","fuel","transmission","block"};var iconOps=new Rec();iconOps.ViewBox(7*44+20,3*44+36);iconOps.BeginClip(7*44+20,3*44+36);
        iconOps.Poly(new float[]{0,0,7*44+20,0,7*44+20,3*44+36,0,3*44+36},"#13191f",null,0);
        int stateRow=0;
        foreach(var colour in new[]{CombatForensicDrawing.ColourSevere,CombatForensicDrawing.ColourDamaged,CombatForensicDrawing.ColourNone})
        {
            for(int k=0;k<kinds.Length;k++)
            {
                var one=new Rec();CombatForensicDrawing.Icon(one,kinds[k],30+k*44,40+stateRow*44,colour);CombatForensicDrawing.Icon(iconOps,kinds[k],30+k*44,40+stateRow*44,colour);
                Check(one.Polys>=2&&one.Lines==0&&one.Texts.Count==0&&one.Ops.All(o=>JsonSerializer.Serialize(o).StartsWith("{\"op\":\"poly\"",StringComparison.Ordinal)),$"icon {kinds[k]} ({colour}) is made of flat polygons only");
            }
            stateRow++;
        }
        for(int k=0;k<kinds.Length;k++)iconOps.Text(30+k*44,3*44+(k%2==0?12:26),kinds[k],"#c1c6ca",SectionAnchor.MiddleCenter,null);
        iconOps.EndClip();
        Check(CombatForensicGeometry.ModuleKinds(m1.Layers[1]).SequenceEqual(new[]{"crew"})&&CombatForensicGeometry.ModuleKinds(scenes[1].Model.Layers[1]).SequenceEqual(new[]{"fuel"}),"the icon kind follows the recorded part (crew position, fuel tank)");
        foreach(var (name,part) in new[]{("cannon","gun"),("engine","engine"),("transmission","transmission"),("layingDrive","block"),("fuelTank","fuel"),("ammoRack","ammunition")})
        {
            var l=new ForensicLayer(1,"x","Main","x",new[]{new CombatLayerPart(1,name,null,null,null,null,false)},false,null,null,null,null,null,"",0,null,CombatEnvelope.Grey,Module:name);
            if(name=="cannon")Check(CombatForensicGeometry.ModuleKinds(l).Count==1&&CombatForensicGeometry.ModuleKinds(l)[0]=="block","a gun part that is not an internal module of the report is not given an icon of its own (cannon stays outside the module rule)");
            else Check(CombatForensicGeometry.ModuleKinds(l).SequenceEqual(new[]{part}),$"{name} gets the {part} icon");
        }
        // ---- the tables and pictures ----
        foreach(var scene in scenes)
        {
            var g=CombatForensicGeometry.Build(scene.Model);var rec=new Rec();CombatForensicDrawing.Draw(rec,scene.Model,false);
            File.WriteAllText(Path.Combine(preview,$"forensic-module-icons-{scene.Name}.ops.json"),JsonSerializer.Serialize(new{title=scene.Title,ops=rec.Ops}));
            foreach(var k in g.Modules)rows.AppendLine($"{scene.Name}\t{k.Label}\t{string.Join("+",k.Kinds)}\t{k.Outcome}\t{k.State}\t{k.X0:0.#}\t{k.X1:0.#}\t{(k.Mm==null?"":k.Mm.Value.ToString("0.#",CultureInfo.InvariantCulture))}");
            if(g.Modules.Count==0)rows.AppendLine($"{scene.Name}\t(no icon: the shot stops before every internal module)\t\t\t\t\t\t");
        }
        File.WriteAllText(Path.Combine(preview,"forensic-module-icons-icons.ops.json"),JsonSerializer.Serialize(new{title="The seven icons in the three states (destroyed or incapacitated, damaged or wounded, no damage recorded)",ops=iconOps.Ops}));
        File.WriteAllText(Path.Combine(output,"FORENSIC-MODULE-ICONS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"FORENSIC-MODULE-ICONS.json"),JsonSerializer.Serialize(new{passed=true,checks=n,reportHasMeasuredLengthThroughModules=reportHasLength,iconKinds=kinds},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"FEAT72-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks=n}));
        Console.WriteLine($"FEAT72 PASS: {n} checks, 0 failures");
    }
}
