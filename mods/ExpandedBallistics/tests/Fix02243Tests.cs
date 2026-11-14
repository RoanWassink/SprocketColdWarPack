using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.43: presentation and analysis only. The verdict follows the perforation of the protective envelope (the armour layers ahead of the first
/// internal module on the shot line), not the place where the projectile came to rest. A projectile that reaches an internal module with
/// residual penetration perforated the envelope, also when it is arrested in the far-side armour; the chip reads PENETRATED, the advice is the
/// residual on entry into the compartment, the internal modules are rows of their own and grey bands without hatching, and the residual row
/// names the last layer. Protection and runtime are the 0.22.42 bytes (and so the 0.22.41 bytes).
/// Main case: forensic-fixtures-1108c shot 6 (APFSDS gen 4, 619 mm, on the Lowe 2A2 Kavallerie Warkit).
/// </summary>
internal static class Fix02243Tests
{
    /// <summary>The only source files 0.22.43 may change (against 0.22.42).</summary>
    internal static readonly string[] Presentation={"CombatForensics.cs","CombatForensicGeometry.cs","CombatForensicDrawing.cs","CombatForensicHtml.cs","CombatForensicPanel.cs",
        "HitPanelModel.cs","Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    private const int FuelFront=351,Motor=290,FuelRear=499,RearPlate=508;
    private const string L1="1639073935104:0:15:16",L3="1639073935104:9:15:13",L5="1639073935104:48:15:14",L7="1639073935104:105:13:12",
        L9="1639073935104:227:11:10",L11="1639073935104:368:11:10",L13="1639073935104:461:1:5",L14="1639073935104:496:6:5",L16="1639073935104:569:3:2";
    private static CombatReportShot[] Load(string path)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        return JsonSerializer.Deserialize<CombatReportShot[]>(json.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var callback=check;check=(ok,text)=>{count++;callback(ok,"fix02243: "+text);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.cassettes.json"))).Presets;
        // The game reads the loaded snapshot (built-in and the user's own presets); the tests use the shipped default catalogue.
        CombatCompositeLayers.Configure(id=>presets.FirstOrDefault(p=>p.MaterialId==id));
        try{Body(check,root,output,preview);}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"FIX43-RESULT.json"),JsonSerializer.Serialize(new{checks=count,liveGameTest=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FIX02243 PASS: {count} checks.");
    }
    private static void Body(Action<bool,string> check,string root,string output,string preview)
    {
        var parent=Fix02228Tests.SessionRoot();
        var sets=new (string Name,CombatReportShot[] Shots)[]{
            ("forensic-fixtures-1108c",Load(Path.Combine(parent,"forensic-fixtures-1108c/last-session.json"))),
            ("forensic-fixtures-1108b",Load(Path.Combine(parent,"forensic-fixtures-1108b/last-session.json"))),
            ("zero-loss-1107",Load(Path.Combine(parent,"zero-loss-1107/last-session.json"))),
            ("forensic-fixtures",Load(Path.Combine(parent,"forensic-fixtures/last-session.json"))),
            ("forensic-fixtures-1108",Load(Path.Combine(parent,"forensic-fixtures-1108/last-session.json")))};
        check(sets[0].Shots.Length==6&&sets[1].Shots.Length==4&&sets[2].Shots.Length==128&&sets[3].Shots.Length==2&&sets[4].Shots.Length==4,"fixtures: 08-11c (6 shots) and the earlier fixtures 4 + 128 + 2 + 4");
        var summary=new StringBuilder();
        var shot6=sets[0].Shots.Single(s=>s.Number==6);
        check(shot6.Target=="Lowe 2A2 Kavallerie Warkit"&&shot6.Profile=="APFSDS gen 4"&&Math.Abs(shot6.InitialPenetrationMm!.Value-619)<1,"08-11c shot 6 is the APFSDS gen 4 shot (619 mm) on the Lowe 2A2 Kavallerie Warkit");
        Envelope(check,shot6,summary);
        Verdict(check,shot6,summary);
        Rows(check,shot6);
        Drawing(check,shot6);
        Arrested(check,sets[2].Shots,summary);
        Rule(check);
        AllFixtures(check,sets,output,summary);
        Language(check,sets,output,summary);
        Previews(check,shot6,preview);
        File.WriteAllText(Path.Combine(output,"FIX43-SUMMARY.txt"),summary.ToString());
    }
    private static ForensicLayer At(ForensicModel m,string hitId)=>m.Layers.Single(l=>l.HitId==hitId);

    // ---- 1. the envelope of shot 6 ----
    private static void Envelope(Action<bool,string> check,CombatReportShot shot6,StringBuilder summary)
    {
        var m=CombatForensics.Build(shot6);var e=m.Protection;
        check(m.HiddenZeroSpans==0&&m.Layers.Count==16,"shot 6: 16 visible layers ("+m.Layers.Count+")");
        check(e.Perforated,"shot 6: the protective envelope is perforated");
        check(e.EntryMm is {} entry&&Math.Abs(entry-203)<=.5,"shot 6: residual penetration on entry into the envelope's compartment 203 mm RHA-eq (±0.5): "+CombatForensics.N(e.EntryMm));
        check(e.FirstLayer==At(m,L9).Number&&At(m,L9).Parts.Single().Entity==FuelFront,"shot 6: the first internal module is the front fuel tank (layer "+e.FirstLayer+")");
        check(e.ArmourLayers.SequenceEqual(new[]{L1,L3,L5,L7}.Select(id=>At(m,id).Number)),"shot 6: the envelope is RHA → Challenger 1 (Burlington) composite → RHA 467 (2×): layers "+string.Join(", ",e.ArmourLayers));
        check(At(m,L1).Name=="Rolled Homogeneous Armour"&&At(m,L3).Name=="Challenger 1 (Burlington) hull"&&At(m,L5).Plate=="Plate #467"&&At(m,L7).Plate=="Plate #467 (second crossing)","shot 6: the envelope layers are the RHA plate, the Burlington composite and Plate #467 twice (0.22.51: the second crossing named)");
        check(e.Compartment=="engine compartment","shot 6: the compartment behind the envelope is the engine compartment ("+e.Compartment+")");
        check(Math.Abs(At(m,L7).After!.Value-e.EntryMm!.Value)<1e-9,"shot 6: the entry value is the exit of the last envelope layer (Plate #467)");
        // The compartments in order, with their readable names and entry values.
        var mods=e.Modules;
        check(mods.Select(x=>m.Layers[x.Layer-1].Parts.First(CombatEnvelope.IsInternal).Entity).SequenceEqual(new[]{FuelFront,Motor,FuelRear}),"shot 6: internal modules in order fuel tank → motor → fuel tank");
        check(mods.Select(x=>x.Name).SequenceEqual(new[]{"fuel tank","motor","fuel tank"}),"shot 6: readable names ("+string.Join(", ",mods.Select(x=>x.Name))+")");
        var want=new[]{203.1468,172.7704,133.4136};
        check(mods.Count==3&&mods.Zip(want).All(x=>x.First.EntryMm is {} v&&Math.Abs(v-x.Second)<.01),"shot 6: entry values 203.1, 172.8 and 133.4 mm RHA-eq ("+string.Join(", ",mods.Select(x=>CombatForensics.N(x.EntryMm)))+")");
        check(mods[2].With=="Plate #508"&&mods[0].With.Length==0&&mods[1].With.Length==0,"shot 6: the rear fuel tank is grouped with the hull plate #508");
        check(Math.Abs(At(m,L14).After!.Value-40)<=.5&&At(m,L14).Plate=="Plate #508","shot 6: the rear plate #508 leaves a residual of 40 mm RHA-eq");
        check(m.Arrested&&At(m,L16).After==0&&At(m,L16).Number==16&&At(m,L16).Plate=="Plate #1 (second crossing)","shot 6: arrested in layer 16, Plate #1 at the rear (0.22.51: its second crossing)");
        var finding=m.Findings.SingleOrDefault(f=>f.StartsWith("Internal modules on the shot line, in order: ",StringComparison.Ordinal))??"";
        int a=finding.IndexOf("layer 9 fuel tank",StringComparison.Ordinal),b=finding.IndexOf("layer 11 motor",StringComparison.Ordinal),c=finding.IndexOf("layer 13 fuel tank together with Plate #508",StringComparison.Ordinal);
        check(a>0&&a<b&&b<c,"finding names the compartments in order ("+finding+")");
        check(finding.Contains("layer 9 fuel tank, entry 203.1 mm RHA-eq",StringComparison.Ordinal)&&finding.Contains("layer 11 motor, entry 172.8 mm RHA-eq",StringComparison.Ordinal)&&finding.Contains("entry 133.4 mm RHA-eq",StringComparison.Ordinal),"finding gives the entry value of each compartment");
        check(m.Findings.Contains("Protective envelope ahead of the engine compartment: layers 1, 3, 5 and 7, reducing the penetration from 619.3 to 203.1 mm RHA-eq.",StringComparer.Ordinal),"finding states the envelope and its reduction");
        summary.AppendLine("envelope: the armour layers ahead of the first internal module (crew position, engine/motor, fuel tank, ammunition stowage, transmission, laying drive, turret basket) on the shot line; perforated when that module is reached with residual > 0");
        summary.AppendLine($"shot 6: envelope layers {string.Join(", ",e.ArmourLayers)}; entry {CombatForensics.N(e.EntryMm)} mm RHA-eq into the {e.Compartment} (layer {e.FirstLayer}); modules: "+string.Join(" → ",mods.Select(x=>$"{x.Name} {CombatForensics.N(x.EntryMm)} mm"+(x.With.Length>0?" with "+x.With:"")))+"; arrested in layer 16 (Plate #1)");
    }

    // ---- 2. outcome, chip, head and advice ----
    private static void Verdict(Action<bool,string> check,CombatReportShot shot6,StringBuilder summary)
    {
        var m=CombatForensics.Build(shot6);
        check(m.Outcome==HitBlockBuilder.Penetrated,"shot 6: the forensic chip reads PENETRATED ("+m.Outcome+")");
        var block=HitBlockBuilder.Build(shot6);
        check(block.Result==HitBlockBuilder.Penetrated&&m.Colour==block.Colour,"shot 6: the hit panel and the forensic chip agree (PENETRATED, "+block.Colour+")");
        check(HitResultRule.Resolve(HitBlockBuilder.Stopped,shot6.Events,false)==HitBlockBuilder.StoppedSpall,"shot 6: the 0.22.22 rule alone gives STOPPED · spall damage, so the envelope rule decides PENETRATED");
        check(block.Values=="619 → 0 mm"&&block.Detail.EndsWith("stopped in Plate #1 (Rolled Homogeneous Armour)",StringComparison.Ordinal),"shot 6: the measured numbers and the arrest place stay ("+block.Values+" | "+block.Detail+")");
        var view=CombatReportView.Build(new[]{shot6})[0];
        check(view.Head.EndsWith(" · 619 → 0 mm · PENETRATED",StringComparison.Ordinal)&&view.Block.Result==HitBlockBuilder.Penetrated,"shot 6: the Kit combat report header reads PENETRATED ("+view.Head+")");
        var history=new CombatReportHistory();history.Restore(new[]{shot6},0);var html=CombatReportHtml.Render(history);
        check(html.Contains(WebUtility.HtmlEncode(" · 619 → 0 mm · PENETRATED")+"</h2>",StringComparison.Ordinal)&&!html.Contains("STOPPED",StringComparison.Ordinal),"shot 6: the HTML heading reads PENETRATED and nothing says STOPPED");
        check(html.Contains("<strong class='fx-chip' style='color:"+m.Colour+"'>PENETRATED</strong> "+WebUtility.HtmlEncode(m.Explanation),StringComparison.Ordinal),"shot 6: the HTML forensic chip and head");
        const string head="Perforation of the protective envelope: residual penetration 203.1 mm RHA-eq on entry into the engine compartment (layer 9, fuel tank) (32.8 % of initial penetration); projectile arrested in layer 16 (Rolled Homogeneous Armour) after traversing the engine compartment.";
        check(m.Explanation==head,"shot 6: head \""+m.Explanation+"\"");
        check(m.Findings[0]==m.Explanation,"shot 6: the first finding is the head");
        var advice=m.Advice.FirstOrDefault()??"";var number=Regex.Match(advice,@"^Preventing perforation required at least ([\d.]+) mm RHA-eq of additional protection ahead of the engine compartment");
        check(number.Success&&double.Parse(number.Groups[1].Value,CultureInfo.InvariantCulture)>=203,"shot 6: the advice names at least 203 mm RHA-eq ahead of the engine compartment ("+advice+")");
        check(Math.Abs(m.Protection.RequiredMm-203.1468)<.001,"shot 6: required additional protection = entry residual ("+CombatForensics.N(m.Protection.RequiredMm)+" mm)");
        string all=string.Join(" ",m.Findings.Concat(m.Advice));
        check(!all.Contains("No additional protection",StringComparison.OrdinalIgnoreCase)&&!all.Contains("no extra protection",StringComparison.OrdinalIgnoreCase),"shot 6: never \"no additional protection\" when internal modules were hit");
        check(m.Findings.Contains("Behind-armour effects: motor, fuel tank, transmission destroyed.",StringComparer.Ordinal),"shot 6: behind-armour effects list the destroyed motor, fuel tanks and transmission");
        // The Kit panel shows the same model (the panel itself needs the game; its source is checked).
        var panel=File.ReadAllText(Path.Combine(Fix02228Tests.SourceRoot(),"CombatForensicPanel.cs"));
        check(panel.Contains("head.Chip(model.Outcome,UiKit.Colour(model.Colour))",StringComparison.Ordinal)&&panel.Contains("head.Label(model.Explanation,",StringComparison.Ordinal)&&panel.Contains("model.RestLabel+\": \"",StringComparison.Ordinal),
            "Kit forensic panel: chip, head and residual row from the shared model");
        summary.AppendLine("shot 6 head: "+m.Explanation);
        summary.AppendLine("shot 6 advice: "+advice);
        summary.AppendLine($"shot 6 chip: forensic {m.Outcome}, hit panel {block.Result}, Kit report \"{view.Head}\"");
    }

    // ---- 3. list rows: internal modules with their share, residual row names the last layer ----
    private static void Rows(Action<bool,string> check,CombatReportShot shot6)
    {
        var m=CombatForensics.Build(shot6);var html=CombatForensicHtml.Render(m,"k");
        var rows=XDocument.Parse("<r>"+html+"</r>").Descendants("div").Where(d=>(string?)d.Attribute("class")=="fx-row").ToArray();
        string Num(XElement d)=>d.Elements("span").First().Value;
        string Name(XElement d)=>d.Elements("span").ElementAt(1).Nodes().OfType<XText>().FirstOrDefault()?.Value.Trim()??"";
        foreach(var (id,name) in new[]{(L9,"Fuel tank"),(L11,"Motor")})
        {
            var l=At(m,id);var row=rows.SingleOrDefault(r=>Num(r)==l.Number.ToString(CultureInfo.InvariantCulture));
            check(row!=null&&Name(row)==name,"list: layer "+l.Number+" is its own row \""+name+"\"");
            check(l.Percent is >0&&row!.Elements("span").Last().Value==CombatForensics.N(l.Percent)+"%","list: layer "+l.Number+" carries its share ("+CombatForensics.N(l.Percent)+" %)");
            check(row!.Element("i")?.Attribute("style")?.Value=="background:"+CombatEnvelope.Grey&&l.Colour==CombatEnvelope.Grey,"list: an internal module has the grey swatch");
            check(CombatForensicGeometry.LayerName(l)==name&&!l.Grouped&&CombatForensicGeometry.Detail(l)=="internal module","the layer reads "+name+" · internal module");
        }
        // The rear fuel tank is in one native group with the hull plate #508: the row keeps the plate (measured armour is never hidden) with its share,
        // and the fuel tank is an indented row of its own directly under it.
        var g13=At(m,L13);var row13=rows.Single(r=>Num(r)==g13.Number.ToString(CultureInfo.InvariantCulture));
        check(g13.Grouped&&g13.Module=="fuel tank"&&Name(row13)=="Rolled Homogeneous Armour"&&CombatForensicGeometry.Detail(g13)=="Plate #508 · 55 mm at 23.8° · with fuel tank"&&row13.Elements("span").Last().Value==CombatForensics.N(g13.Percent)+"%",
            "list: layer 13 reads Rolled Homogeneous Armour · Plate #508 · 55 mm at 23.8° · with fuel tank, with its share ("+CombatForensicGeometry.Detail(g13)+")");
        var under=row13.ElementsAfterSelf("div").FirstOrDefault();
        check(under!=null&&(string?)under.Attribute("class")=="fx-row fx-sub fx-module"&&Name(under)=="Fuel tank"&&under.Value.Contains("internal module in the same layer as Plate #508",StringComparison.Ordinal)
            &&under.Element("i")?.Attribute("style")?.Value=="background:"+CombatEnvelope.Grey&&!under.Value.Contains('%'),"list: the grouped fuel tank is an indented grey row of its own under layer 13 ("+under?.Value+")");
        check(XDocument.Parse("<r>"+html+"</r>").Descendants("div").Count(d=>(string?)d.Attribute("class")=="fx-row fx-sub fx-module")==1,"list: exactly one grouped-module row in shot 6");
        var panelSource=File.ReadAllText(Path.Combine(Fix02228Tests.SourceRoot(),"CombatForensicPanel.cs"));
        check(panelSource.Contains("if(l.Grouped)SubRow(content,46,CombatEnvelope.Grey,CombatForensics.Capital(l.Module)+\" | \"+CombatForensicGeometry.ModuleDetail(l));",StringComparison.Ordinal),"Kit panel: the same indented module row");
        var rest=rows.Last();
        check(m.RestLabel=="Residual penetration behind layer 16"&&Name(rest)==m.RestLabel&&rest.Value.Contains("0 mm RHA-eq",StringComparison.Ordinal)&&rest.Elements("span").Last().Value=="0%","list: the residual row refers to the last layer (\""+Name(rest)+"\")");
        var shares=m.Layers.Where(l=>l.Stage!="Precursor").Sum(l=>l.Percent??0)+(m.ResidualPercent??0)+100*(m.UnattributedMm??0)/m.Initial!.Value;
        check(Math.Abs(shares-100)<=.5,"list: the shares, the residual and the change between layers total 100 % ("+shares.ToString("0.##",CultureInfo.InvariantCulture)+")");
        // A crew position reads "crew position"; a layer that is not an internal module keeps its 0.22.42 detail.
        var crew=new CombatReportShot(1,"Outgoing","T",1000,new[]{new CombatReportEvent("layerStart","","",100,null,"c",new CombatLayerInfo("c",new[]{new CombatLayerPart(9,"Crew member #9",null,"ERA",null,null,false)},false)),
            new CombatReportEvent("componentResidual","","",100,90,"c")},InitialPenetrationMm:100);
        var cm=CombatForensics.Build(crew);
        check(cm.Layers[0].Internal&&CombatForensicGeometry.Detail(cm.Layers[0])=="crew position"&&CombatForensicGeometry.LayerName(cm.Layers[0])=="Crew","a crew position reads Crew · crew position");
    }

    // ---- 4. drawing: each internal module a framed icon (0.22.72; before: its own grey band) ----
    private static void Drawing(Action<bool,string> check,CombatReportShot shot6)
    {
        var m=CombatForensics.Build(shot6);var g=CombatForensicGeometry.Build(m);
        var modules=g.Bands.Where(b=>b.Layer.Internal).ToArray();
        check(modules.Select(b=>b.Layer.HitId).SequenceEqual(new[]{L9,L11,L13}),"drawing: three internal modules (fuel tank, motor, fuel tank)");
        // 0.22.72: an internal module is a framed icon, not a grey band: a module on its own has a band without polygon, the grouped fuel tank keeps the plate's band.
        var marks=g.Modules;
        check(marks.Select(k=>k.Layer.HitId).SequenceEqual(new[]{L9,L11,L13})&&marks.All(k=>k.Mm==null&&Math.Abs(k.X1-k.X0-CombatForensicGeometry.FrameWidth)<.01),"drawing: three framed icons of fixed width (fuel tank, motor, fuel tank), no measured length");
        check(modules.All(b=>b.Layer.Grouped?b.Polygon.Length==8&&b.Module==null:b.Polygon.Length==0&&b.Subs.Count==0),"drawing: no grey band for any internal module");
        var plate13=modules.Single(b=>b.Layer.HitId==L13);
        foreach(bool heat in new[]{false,true})
        {
            var sink=new Sink();CombatForensicDrawing.Draw(sink,m,heat);
            foreach(var k in marks)
            {
                var frame=new[]{k.X0,g.ShotY-13,k.X1,g.ShotY-13,k.X1,g.ShotY+13,k.X0,g.ShotY+13};
                var fills=sink.Fills.Where(f=>f.Polygon.SequenceEqual(frame)).ToArray();
                check(fills.Length==1&&fills[0].Fill==CombatForensicDrawing.StateColour(k.State)+"26","drawing: layer "+k.Layer.Number+" module is one dotted frame on the shot line ("+string.Join(",",fills.Select(f=>f.Fill))+")");
                check(!sink.Hatched.Any(h=>h.SequenceEqual(frame)),"drawing: layer "+k.Layer.Number+" module frame carries no hatching"+(heat?" (heat-map)":""));
                check(sink.Texts.Contains(k.Label)&&sink.Texts.Contains(k.Outcome),"drawing: layer "+k.Layer.Number+" module is named with its number and carries its result ("+k.Label+", "+k.Outcome+")");
            }
            var plateFill=sink.Fills.Where(f=>f.Polygon.SequenceEqual(plate13.Polygon)).ToArray();
            check(plateFill.Length==1&&plateFill[0].Fill==(heat?CombatForensics.Heat(plate13.Layer.Percent):CombatForensics.MaterialColour("rha"))+"c0","drawing: plate #508 of layer 13 keeps its RHA band"+(heat?" (heat-map)":""));
            check(sink.Marks==g.Bands.Count(b=>b.Polygon.Length==0&&!g.Drawn(b.Layer)&&!b.Layer.Internal&&b.Module==null),"drawing: no thin mark line for a drawn module");
            check(sink.Texts.Contains("Arrested in layer 16")&&sink.Texts.Contains("Envelope perforated · 203.1 mm RHA-eq"),"drawing: the arrest in layer 16 and the envelope perforation are marked"+(heat?" (heat-map)":""));
        }
        // The frames follow each other along the shot line in layer order, none over another.
        check(Enumerable.Range(1,marks.Count-1).All(i=>marks[i].X0>=marks[i-1].X1+9.99f),"drawing: the module frames follow each other without overlap");
        var grouped=plate13;
        check(grouped.Angle is {} a&&Math.Abs(a-23.79)<.01&&Math.Abs(grouped.ExitX-grouped.EntryX-(float)(grouped.Layer.PathMm!.Value*g.Scale))<.01,"drawing: plate #508 of layer 13 spans the group's measured path at its obliquity");
        check(marks.Single(k=>k.Layer.HitId==L13).X0>=grouped.EntryX-.01f&&marks.Single(k=>k.Layer.HitId==L13).Kinds.SequenceEqual(new[]{"fuel"}),"drawing: the grouped fuel tank is a framed fuel icon at the place of its plate");
        // Nothing on the millimetre scale moves: every layer keeps its 0.22.42 place on the shot line.
        double at=0;foreach(var l in m.Layers){check(Math.Abs(l.StartMm-at)<1e-9,"drawing: layer "+l.Number+" keeps its place on the shot line");at+=l.PathMm??0;}
        var stop=g.Bands.Single(b=>b.Layer.HitId==L16);
        check(stop.Polygon.Length==8&&Math.Abs(stop.Angle!.Value-74.863)<.01,"drawing: the arresting rear plate is drawn at its measured obliquity");
        check(ForensicModel.GeometryNote=="Cross-section to scale along the shot line; each plate band is its normal thickness at the measured obliquity; framed icons mark the internal modules on the shot line.","the note under the drawing names the framed icons (one line)");
    }

    // ---- 5. an arrest in the front armour stays an arrest, with the right margin ----
    private static void Arrested(Action<bool,string> check,CombatReportShot[] zero,StringBuilder summary)
    {
        var raw=zero.Single(s=>s.Number==106);var m=CombatForensics.Build(raw);
        var solid=m.Layers.Where(l=>l.GapMm==null&&l.Stage!="Precursor").ToArray();
        check(solid.Length==1&&m.Arrested&&solid[0].After==0&&!solid[0].Internal,"#106 is arrested in the front (first) armour layer, layer "+solid[0].Number);
        check(!m.Protection.Perforated&&m.Protection.RequiredMm==0&&m.Protection.ArmourLayers.SequenceEqual(new[]{solid[0].Number}),"#106: the envelope is not perforated; no additional protection required (0 mm RHA-eq)");
        check(m.Outcome=="STOPPED"&&HitBlockBuilder.Build(raw).Result==HitBlockBuilder.Stopped,"#106: the chip stays STOPPED ("+m.Outcome+")");
        check(m.Explanation==$"Projectile arrested in layer {solid[0].Number} ({solid[0].Name}); residual penetration 0 mm RHA-eq.","#106: head \""+m.Explanation+"\"");
        check(m.Advice.FirstOrDefault()==$"No additional protection was required against this impact: the projectile was arrested in layer {solid[0].Number} ({solid[0].Name}).","#106: the margin is stated as no additional protection required ("+m.Advice.FirstOrDefault()+")");
        check(solid[0].Before is {} b&&solid[0].AbsorbedMm==b&&solid[0].Percent is {} p&&Math.Abs(p-100*b/m.Initial!.Value)<1e-9,"#106: the arresting layer absorbed its full entry penetration ("+CombatForensics.N(solid[0].Before)+" mm RHA-eq)");
        check(m.RestLabel==$"Residual penetration behind layer {m.Layers[^1].Number}","#106: the residual row names the last layer");
        // An arrest ahead of every internal module with internal damage (spall) keeps STOPPED · spall damage and never says "no additional protection".
        var spall=raw with{Events=raw.Events.Append(new CombatReportEvent("interaction","","Crew member Gunner killed",RelatedEntity:7)).ToArray()};
        var ms=CombatForensics.Build(spall);
        check(ms.Outcome==HitBlockBuilder.StoppedSpall&&HitBlockBuilder.Build(spall).Result==HitBlockBuilder.StoppedSpall&&!ms.Protection.Perforated,"an arrest ahead of every internal module with spall damage stays STOPPED · spall damage");
        check(ms.Advice.FirstOrDefault()==$"The projectile was arrested in layer {solid[0].Number} ({solid[0].Name}); the internal damage arose from behind-armour effects (spall).","no \"no additional protection\" when internal modules were hit ("+ms.Advice.FirstOrDefault()+")");
        summary.AppendLine("#106 (zero-loss-1107): arrested in layer "+solid[0].Number+" ("+solid[0].Name+"), envelope not perforated, required 0 mm RHA-eq; "+m.Advice[0]);
    }

    // ---- 6. the rule on synthetic shots ----
    private static void Rule(Action<bool,string> check)
    {
        CombatLayerInfo Plate(string id,int e)=>new(id,new[]{new CombatLayerPart(e,"Plate #"+e,"rha","Rolled Homogeneous Armour",50,30,true)},false);
        CombatLayerInfo Part(string id,int e,string name)=>new(id,new[]{new CombatLayerPart(e,name,null,"ERA",null,null,false)},false);
        CombatReportShot Shot(params CombatReportEvent[] ev)=>new(7,"Outgoing","APFSDS",1500,ev,InitialPenetrationMm:300);
        CombatReportEvent Start(string id,double before,CombatLayerInfo l)=>new("layerStart","","Reached layer",before,null,id,l);
        CombatReportEvent Res(string id,double before,double after,string kind="plateResidual")=>new(kind,"","",before,after,id);
        CombatReportEvent Stop(string id,double before)=>new("nativeStop","","Native stop",before,null,id);
        // An ERA add-on, a track or the main gun ahead of the armour is no internal module.
        foreach(var name in new[]{"VARMA-style passive side armour (game proxy)","trackBeltSegmentArray","cannon","turretRing","model","Relikt 2x2"})
        {
            var s=Shot(Start("a",300,Part("a",5,name)),Res("a",300,250,"componentResidual"),Start("b",250,Plate("b",1)),Res("b",250,200),Start("c",200,Plate("c",2)),Stop("c",200));
            check(!CombatForensics.Build(s).Protection.Perforated&&HitBlockBuilder.Build(s).Result==HitBlockBuilder.Stopped,"rule: "+name+" is not an internal module");
        }
        foreach(var (name,compartment) in new[]{("Crew member #4","crew compartment"),("engine","engine compartment"),("motor","engine compartment"),("transmission","engine compartment"),
            ("ammoRack","ammunition stowage"),("layingDrive","crew compartment"),("basket","crew compartment"),("fuelTank","fuel tank")})
        {
            var s=Shot(Start("a",300,Plate("a",1)),Res("a",300,120),Start("b",120,Part("b",5,name)),Res("b",120,100,"componentResidual"),Start("c",100,Plate("c",2)),Stop("c",100));
            var m=CombatForensics.Build(s);
            check(m.Protection.Perforated&&m.Protection.EntryMm==120&&m.Protection.Compartment==compartment&&m.Outcome==HitBlockBuilder.Penetrated&&HitBlockBuilder.Build(s).Result==HitBlockBuilder.Penetrated,"rule: "+name+" behind the armour is an internal module of the "+compartment);
        }
        // A fuel tank takes the compartment of the next module on the shot line.
        var tank=Shot(Start("a",300,Plate("a",1)),Res("a",300,120),Start("b",120,Part("b",5,"fuelTank")),Res("b",120,100,"componentResidual"),Start("m",100,Part("m",6,"Crew member #6")),Res("m",100,90,"componentResidual"),Start("c",90,Plate("c",2)),Stop("c",90));
        check(CombatForensics.Build(tank).Protection.Compartment=="crew compartment","rule: a fuel tank next to the crew lies in the crew compartment");
        // A stop inside the module itself: perforation of the envelope, arrested in the module.
        var inside=Shot(Start("a",300,Plate("a",1)),Res("a",300,80),Start("b",80,Part("b",5,"ammoRack")),Stop("b",80));
        var mi=CombatForensics.Build(inside);
        check(mi.Protection.Perforated&&mi.Outcome==HitBlockBuilder.Penetrated&&mi.Explanation.EndsWith("; projectile arrested in layer 2 (ammunition stowage).",StringComparison.Ordinal)&&mi.Explanation.Contains("(layer 2)",StringComparison.Ordinal),"rule: arrested inside the ammunition stowage ("+mi.Explanation+")");
        // A module grouped with a plate counts when the projectile left the group; its entry value is the group's exit (a lower bound).
        CombatLayerInfo Group(string id)=>new(id,new[]{new CombatLayerPart(5,"Crew member #5",null,"ERA",null,null,false),new CombatLayerPart(1,"Plate #1","rha","Rolled Homogeneous Armour",50,30,true)},true);
        var left=Shot(Start("g",300,Group("g")),Res("g",300,140),Start("c",140,Plate("c",2)),Stop("c",140));
        check(CombatForensics.Build(left).Protection is {Perforated:true,EntryMm:140}&&HitBlockBuilder.Build(left).Result==HitBlockBuilder.Penetrated,"rule: a crew position grouped with a plate, left with residual, perforates the envelope (entry = group exit 140 mm)");
        var held=Shot(Start("g",300,Group("g")),Stop("g",300),new CombatReportEvent("interaction","","Crew member Gunner killed",RelatedEntity:5));
        check(!CombatForensics.Build(held).Protection.Perforated&&HitBlockBuilder.Build(held).Result==HitBlockBuilder.StoppedSpall,"rule: arrested inside such a group is no perforation (STOPPED · spall damage)");
        // A projectile that leaves the vehicle after an internal module: the advice is the entry value, the head also gives the exit residual.
        var through=Shot(Start("a",300,Plate("a",1)),Res("a",300,200),Start("b",200,Part("b",5,"engine")),Res("b",200,180,"componentResidual"),Start("c",180,Plate("c",2)),Res("c",180,60));
        var mt=CombatForensics.Build(through);
        check(mt.Outcome==HitBlockBuilder.Penetrated&&mt.Explanation.EndsWith("; residual penetration 60 mm RHA-eq behind layer 3 (20 % of initial penetration).",StringComparison.Ordinal)&&mt.Advice[0].StartsWith("Preventing perforation required at least 200 mm RHA-eq of additional protection ahead of the engine compartment",StringComparison.Ordinal),
            "rule: a projectile that leaves the vehicle ("+mt.Explanation+" | "+mt.Advice[0]+")");
        // A ricochet stays a ricochet.
        var rico=Shot(Start("a",300,Plate("a",1)),Res("a",300,200),Start("b",200,Part("b",5,"engine")),Res("b",200,180,"componentResidual"),Start("c",180,Plate("c",2)),new CombatReportEvent("outcome","","Deflected",180,null,"c"));
        check(CombatForensics.Build(rico).Outcome=="RICOCHET"&&HitBlockBuilder.Build(rico).Result==HitBlockBuilder.Ricochet,"rule: a ricochet is never turned into a penetration");
        check(!CombatForensics.EnvelopePerforated(new CombatReportShot(1,"","",null,null!)),"rule: the result-word check is exception-safe");
    }

    // ---- the rule on every fixture: a ledger, and consistency of all surfaces ----
    private static void AllFixtures(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,string output,StringBuilder summary)
    {
        var ledger=new StringBuilder("fixture\tshot\tperforated\tfirst_layer\tentry_mm\tcompartment\tmodules\tarrested\tforensic_chip\thit_panel\thead\n");
        int perforated=0,promoted=0,total=0;
        foreach(var (name,shots) in sets)foreach(var raw in shots)
        {
            total++;var m=CombatForensics.Build(raw);var e=m.Protection;var block=HitBlockBuilder.Build(raw);var view=CombatReportView.Build(new[]{raw})[0];
            check(view.Forensic!.Outcome==m.Outcome,"the report carries the same forensic model");
            if(e.Perforated){perforated++;if(m.Arrested)promoted++;}
            if(e.Perforated&&m.Outcome=="NO ARMOUR")check(block.Result==HitBlockBuilder.NoArmour&&e.ArmourLayers.Count==0&&m.Explanation.StartsWith($"No armour ahead of the {e.Compartment}: ",StringComparison.Ordinal),$"{name} #{raw.Number}: a module reached without armour ahead keeps NO ARMOUR ({m.Explanation})");
            if(e.Perforated&&m.Outcome!="RICOCHET"&&m.Outcome!="NO ARMOUR")check(m.Outcome==HitBlockBuilder.Penetrated&&block.Result==HitBlockBuilder.Penetrated&&view.Block.Result==HitBlockBuilder.Penetrated&&
                m.Explanation.StartsWith(e.ArmourLayers.Count>0?"Perforation of the protective envelope: ":$"No armour ahead of the {e.Compartment}: ",StringComparison.Ordinal),
                $"{name} #{raw.Number}: a perforated envelope reads PENETRATED on every surface ({m.Outcome} / {block.Result} / {view.Block.Result}; {m.Explanation})");
            if(e.Perforated&&m.Outcome!="RICOCHET")check(e.EntryMm is >0&&m.Advice.Count>0&&m.Advice[0].StartsWith($"Preventing perforation required at least {CombatForensics.N(e.EntryMm)} mm RHA-eq",StringComparison.Ordinal),
                $"{name} #{raw.Number}: the advice is the entry value ({m.Outcome}; {string.Join(" | ",m.Advice)})");
            if(HitResultRule.InternalDamage(raw.Events)||e.Perforated)check(!m.Advice.Concat(m.Findings).Any(t=>t.StartsWith("No additional protection",StringComparison.Ordinal)),$"{name} #{raw.Number}: no \"no additional protection\" when internal modules were hit");
            if(e.Perforated)check(e.Modules.Count>0&&e.Modules[0].Layer==e.FirstLayer&&e.Modules.Zip(e.Modules.Skip(1)).All(x=>x.First.Layer<x.Second.Layer),$"{name} #{raw.Number}: modules in shot-line order");
            check(m.Layers.Where(l=>l.Internal&&!l.Grouped).All(l=>l.Colour==CombatEnvelope.Grey&&l.GapMm==null),$"{name} #{raw.Number}: internal modules on their own are grey layers");
            var g=CombatForensicGeometry.Build(m);
            check(g.Bands.Where(b=>b.Layer.Internal&&!b.Layer.Array).All(b=>g.Modules.Count(k=>ReferenceEquals(k.Layer,b.Layer))==1&&(b.Layer.Grouped?b.Module==null:b.Polygon.Length==0&&b.Subs.Count==0&&b.Module==null)),$"{name} #{raw.Number}: every internal module has a framed icon of its own (0.22.72; no grey band)");
            check(m.Layers.Where(l=>l.Grouped).All(l=>l.Colour==CombatForensics.MaterialColour(l.Parts.First(p=>p.Armour).MaterialId)&&l.Name==CombatForensics.PlateMaterial(l.Parts.First(p=>p.Armour))),$"{name} #{raw.Number}: a module grouped with a plate keeps the plate's name and colour");
            if(m.HasResidualRow)check(m.RestLabel==$"Residual penetration behind layer {m.Layers.Last(l=>l.Stage!="Precursor").Number}",$"{name} #{raw.Number}: the residual row names the last layer");
            ledger.AppendLine($"{name}\t{raw.Number}\t{e.Perforated}\t{(e.Perforated?e.FirstLayer:"")}\t{CombatForensics.N(e.EntryMm).Replace("?","")}\t{e.Compartment}\t{string.Join(" → ",e.Modules.Select(x=>x.Name))}\t{m.Arrested}\t{m.Outcome}\t{block.Result}\t{m.Explanation}");
        }
        File.WriteAllText(Path.Combine(output,"ENVELOPE-02243.tsv"),ledger.ToString());
        check(perforated>=20&&promoted>=5,$"the rule is exercised on every fixture ({perforated} perforated envelopes of {total} shots, {promoted} of them arrested further on)");
        summary.AppendLine($"all fixtures: {perforated} of {total} shots perforate the envelope; {promoted} of these are arrested further on and now read PENETRATED (ENVELOPE-02243.tsv)");
    }

    // ---- language ----
    private static IEnumerable<string> Exempt(IEnumerable<CombatReportShot> shots)
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
    internal static IEnumerable<string> ForensicTexts(ForensicModel m)
    {
        foreach(var t in Fix02242Tests.ForensicTexts(m))yield return t;
        yield return m.RestLabel;yield return CombatForensicDrawing.EnvelopeLabel(m);
    }
    private static IEnumerable<string> HtmlVisibleText(string html,bool forensicOnly)
    {
        var doc=XDocument.Parse(html);
        foreach(var d in doc.Descendants("details").ToArray())d.Remove();
        foreach(var s in doc.Descendants("style").ToArray())s.Remove();
        IEnumerable<XElement> scope=forensicOnly?doc.Descendants("div").Where(d=>(string?)d.Attribute("class")=="forensic"):new[]{doc.Root!};
        foreach(var e in scope)foreach(var t in e.DescendantNodes().OfType<XText>())yield return t.Value;
    }
    private static void Language(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,string output,StringBuilder summary)
    {
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
        Scan("fixed",0,"forensic",CombatForensicGeometry.FixedTexts.Append(CombatForensicDrawing.TopLabel).Append(CombatForensicDrawing.NoLayers),Array.Empty<string>(),true);
        foreach(var (name,shots) in sets)
        {
            foreach(var raw in shots)
            {
                var ex=Exempt(new[]{raw}).ToArray();var impact=CombatReportView.Build(new[]{raw})[0];
                Scan(name,raw.Number,"panel",Fix02241Tests.PanelTexts(impact),ex,false);
                Scan(name,raw.Number,"hit panel",Fix02241Tests.HudTexts(impact.Block),ex,false);
                Scan(name,raw.Number,"forensic",ForensicTexts(impact.Forensic!),ex,true);
            }
            var history=new CombatReportHistory();history.Restore(shots,0);var html=CombatReportHtml.Render(history);var exAll=Exempt(shots).ToArray();
            Scan(name,0,"html",HtmlVisibleText(html,false),exAll,false);
            Scan(name,0,"html forensic",HtmlVisibleText(html,true),exAll,true);
        }
        File.WriteAllText(Path.Combine(output,"LANGUAGE-CHECK-02243.tsv"),hits.ToString());
        check(violations==0,$"forbidden-term list: 0 hits in panel, hit panel, HTML and forensic text, the new envelope texts included ({violations}; LANGUAGE-CHECK-02243.tsv)");
        check(uncertain==0,$"no uncertainty statements in the forensic screen on any fixture ({uncertain})");
        check(scanned>5000,"language scan covers the generated text ("+scanned+" strings)");
        summary.AppendLine($"language: {scanned} visible strings scanned, {violations} forbidden terms, {uncertain} uncertainty statements");
    }

    /// <summary>True when a 0.22.42 outcome may differ from the current one: a stop after a perforated envelope now reads PENETRATED.</summary>
    internal static bool Promoted(string? before,ForensicModel now)=>before is "STOPPED" or HitBlockBuilder.StoppedSpall&&now.Outcome==HitBlockBuilder.Penetrated&&now.Protection.Perforated&&now.Arrested;
    /// <summary>
    /// The combat report outside the forensic sections equals an earlier HTML, except for the result word (and its colour) in the heading of an
    /// impact whose stop followed a perforated envelope: "… · STOPPED · spall damage" / "… · STOPPED" there reads "… · PENETRATED".
    /// </summary>
    internal static bool SameOutsideForensic(string before,string now,IReadOnlyList<CombatReportShot> shots,out int promoted,out string detail)
    {
        promoted=0;detail="";
        var a=XDocument.Parse(before);var b=XDocument.Parse(now);
        foreach(var d in new[]{a,b}){foreach(var x in d.Descendants("div").Where(x=>(string?)x.Attribute("class")=="forensic").ToArray())x.Remove();foreach(var x in d.Descendants("style").ToArray())x.Remove();}
        var impacts=CombatReportView.Build(shots,int.MaxValue);var sa=a.Descendants("section").ToArray();var sb=b.Descendants("section").ToArray();
        if(sa.Length!=sb.Length||sa.Length!=impacts.Count){detail="section count";return false;}
        var byNumber=shots.Select(CombatReportTruth.Normalize).GroupBy(s=>s.Number.ToString(CultureInfo.InvariantCulture)).ToDictionary(g=>g.Key,g=>g.Last());
        for(int i=0;i<sa.Length;i++)
        {
            var ha=sa[i].Element("h2");var hb=sb[i].Element("h2");
            if(ha==null||hb==null){detail="heading missing";return false;}
            if(XNode.DeepEquals(ha,hb))continue;
            if(!byNumber.TryGetValue(impacts[i].Key,out var shot)||!CombatForensics.EnvelopePerforated(shot)){detail="heading of #"+impacts[i].Key+" changed without a perforated envelope";return false;}
            var expected=Regex.Replace(ha.Value,@" · STOPPED( · spall damage)?(?= · TANK DESTROYED$|$)"," · PENETRATED");
            if(hb.Value!=expected||(string?)hb.Attribute("style")!="border-color:"+impacts[i].Block.Colour){detail="heading of #"+impacts[i].Key+": "+hb.Value+" ≠ "+expected;return false;}
            promoted++;ha.ReplaceWith(new XElement(hb));
        }
        // 0.22.49 deliberately replaces ERA rejection text. Keep every other
        // cell and the full report structure in this historical HTML comparison.
        // 0.22.51 deliberately names later crossings ("Plate #516 (second crossing)"); compare the rest of every name.
        foreach(var text in b.DescendantNodes().OfType<XText>())text.Value=Regex.Replace(text.Value,@"\s\((second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|\d+th) crossing\)","");
        // HTML renders the new separate qualifier line with <br/>; remove only that
        // proven qualifier and its separator, retaining the base name and all other cells.
        foreach(var cell in b.Descendants("td"))
            foreach(var br in cell.Elements("br").ToArray())
                if(br.NextNode is XText tail&&Regex.IsMatch(tail.Value,@"^\((second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|\d+th) crossing\)$")){tail.Remove();br.Remove();}
        var oldRows=a.Descendants("tr").ToArray();var newRows=b.Descendants("tr").ToArray();
        if(oldRows.Length!=newRows.Length){detail="report row count differs";return false;}
        for(int i=0;i<oldRows.Length;i++)
        {
            var oldCells=oldRows[i].Elements("td").ToArray();var newCells=newRows[i].Elements("td").ToArray();
            if(oldCells.Length==7&&newCells.Length==7&&(newCells[6].Value.Contains("Explosive reactive element",StringComparison.Ordinal)||newCells[6].Value.Contains(EraRejectionReport.Housing,StringComparison.Ordinal)||oldCells[6].Value.Contains(EraRejectionReport.Housing,StringComparison.Ordinal)))
                oldCells[6].ReplaceWith(new XElement(newCells[6]));
        }
        bool same=a.ToString(SaveOptions.DisableFormatting)==b.ToString(SaveOptions.DisableFormatting);
        if(!same){detail="content outside the headings differs";var ea=a.Descendants().Where(x=>!x.HasElements).Select(x=>x.Value).ToArray();var eb=b.Descendants().Where(x=>!x.HasElements).Select(x=>x.Value).ToArray();for(int k=0;k<Math.Min(ea.Length,eb.Length);k++)if(ea[k]!=eb[k]){detail+=": «"+ea[k]+"» vs «"+eb[k]+"»";break;}}
        return same;
    }
    // ---- previews ----
    private static void Previews(Action<bool,string> check,CombatReportShot shot6,string preview)
    {
        var css=CombatReportHtml.Render(new CombatReportHistory());css=css[(css.IndexOf("<style>",StringComparison.Ordinal)+7)..css.IndexOf("</style>",StringComparison.Ordinal)];
        var m=CombatForensics.Build(shot6);const string stem="forensic-1108c-shot-6";
        File.WriteAllText(Path.Combine(preview,stem+".model.json"),JsonSerializer.Serialize(m,new JsonSerializerOptions{WriteIndented=true}));
        foreach(bool heat in new[]{false,true})
        {
            var sink=new RasterSink((int)CombatForensicDrawing.Width,(int)CombatForensicDrawing.Height(m));CombatForensicDrawing.Draw(sink,m,heat);
            sink.Save(Path.Combine(preview,stem+(heat?"-heat":"-material")));
        }
        var fragment=CombatForensicHtml.Render(m,stem);
        File.WriteAllText(Path.Combine(preview,stem+".html"),"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>Panel mockup</title><style>"+css+".forensic{border:0;margin:0}.forensic h3{display:none}"+
            "body{max-width:none;margin:0;padding:20px;background:#10151a}.window{background:#232930;border:1px solid #5a6268;border-radius:8px;width:1180px}.title{padding:15px 20px;background:#1b211c;color:#f1d29a;font-size:18px}.viewport{padding:0 20px 12px}.footer{padding:12px 20px;background:#1b211c;color:#b8ad94;font-size:12px}.fx-row{font-size:13px}</style></head><body><div class='window'><div class='title'>"+
            $"{ForensicModel.Title} <small>| Expanded Ballistics | impact #{m.Number}</small><span style='float:right'>X</span></div><div class='viewport'>"+fragment+"</div><div class='footer'>Mockup from the shared report model (expanded content; the panel in the game scrolls).</div></div><script>document.body.setAttribute('data-mockup-height',Math.ceil(document.querySelector('.window').getBoundingClientRect().bottom+20));</script></body></html>");
        string Large(bool heat)=>CombatForensicDrawing.Svg(m,heat,"large-"+(heat?"heat":"material")).Replace("class='forensic-section'","class='forensic-section' width='2385' height='945'");
        File.WriteAllText(Path.Combine(preview,stem+"-section-large.html"),"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>Cross-section, impact 6</title><style>body{margin:0;padding:20px;background:#10151a;color:#e7edf2;font:16px system-ui}svg{display:block;font-family:system-ui}h1{font-size:18px;color:#f1d29a;margin:0 0 10px}p{color:#b8ad94;margin:6px 0 18px}</style></head><body><div class='window'><h1>Impact 6: APFSDS gen 4 → Lowe 2A2 Kavallerie Warkit, cross-section along the shot line (material)</h1>"+Large(false)+
            "<p>"+WebUtility.HtmlEncode(ForensicModel.GeometryNote)+"</p><h1>Heat-map</h1>"+Large(true)+"<p>"+WebUtility.HtmlEncode(ForensicModel.Footnote)+"</p></div><script>document.body.setAttribute('data-mockup-height',Math.ceil(document.querySelector('.window').getBoundingClientRect().bottom+20));</script></body></html>");
        check(File.Exists(Path.Combine(preview,stem+".html"))&&File.Exists(Path.Combine(preview,stem+"-section-large.html")),"forensic preview of 08-11c shot 6 and its large cross-section");
    }
    private sealed class Sink:ISectionSink
    {
        internal List<(float[] Polygon,string Fill)> Fills=new();internal List<float[]> Hatched=new();internal List<string> Texts=new();internal int Marks;
        public void ViewBox(float w,float h){}public void BeginClip(float w,float h){}public void EndClip(){}
        public void Poly(float[] p,string f,string? s,float w)=>Fills.Add((p,f));
        public void Hatch(float[] p,string c,float a,float s,float w)=>Hatched.Add(p);
        public void Outline(float[] p,string c,float w){}
        public void Line(float x,float y,float a,float b,string c,float w){if(c=="#9aa6b2")Marks++;}
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
