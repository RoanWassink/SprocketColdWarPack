using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.42: presentation only. The plates of a composite array (consecutive native groups of several plates) are shown front to back, each drawn
/// once with its own normal thickness and obliquity, a preset plate with its composer constituents; the list indents the plates under the array
/// row and the constituents one level deeper. "(layer sequence not resolved)" is gone. Protection and runtime are the 0.22.41 bytes.
/// Main case: forensic-fixtures-1108b shot 4 (T-80BVM glacis: ERA frame → T-80U glacis → High Hardness Steel).
/// </summary>
internal static class Fix02242Tests
{
    /// <summary>The only source files 0.22.42 may change.</summary>
    internal static readonly string[] Presentation={"CombatReportLanguage.cs","CombatForensics.cs","CombatForensicGeometry.cs","CombatForensicDrawing.cs","CombatForensicHtml.cs","CombatForensicPanel.cs",
        "Plugin.cs","SprocketExpandedBallistics.csproj","RELEASE-NOTES.md"};
    private const int Frame=1225,Cassette=336,Hhs=1;
    private static readonly string[] Shot4Layers={"1637134881408:73:13:8","1637134881408:129:7:11","1637134881408:174:11:9"};
    private static CombatReportShot[] Load(string path)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        return JsonSerializer.Deserialize<CombatReportShot[]>(json.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    internal static void Run(Action<bool,string> check)
    {
        int count=0;var callback=check;check=(ok,text)=>{count++;callback(ok,"fix02242: "+text);};
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"Material/defaults/sprocket.expandedballistics.cassettes.json"))).Presets;
        // The game reads the loaded snapshot (built-in and the user's own presets); the tests use the shipped default catalogue.
        CombatCompositeLayers.Configure(id=>presets.FirstOrDefault(p=>p.MaterialId==id));
        try{Body(check,root,output,preview,presets);}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"FIX42-RESULT.json"),JsonSerializer.Serialize(new{checks=count,liveGameTest=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FIX02242 PASS: {count} checks.");
    }
    private static void Body(Action<bool,string> check,string root,string output,string preview,CassettePreset[] presets)
    {
        var parent=Fix02228Tests.SessionRoot();
        var sets=new (string Name,CombatReportShot[] Shots)[]{
            ("forensic-fixtures-1108b",Load(Path.Combine(parent,"forensic-fixtures-1108b/last-session.json"))),
            ("zero-loss-1107",Load(Path.Combine(parent,"zero-loss-1107/last-session.json"))),
            ("forensic-fixtures",Load(Path.Combine(parent,"forensic-fixtures/last-session.json"))),
            ("forensic-fixtures-1108",Load(Path.Combine(parent,"forensic-fixtures-1108/last-session.json")))};
        check(sets[0].Shots.Length==4&&sets[1].Shots.Length==128&&sets[2].Shots.Length==2&&sets[3].Shots.Length==4,"fixtures: 08-11b (4 shots) and the 0.22.41 fixtures 128 + 2 + 4");
        var summary=new StringBuilder();
        var shot4=sets[0].Shots.Single(s=>s.Number==4);
        check(shot4.Target=="T-80BVM"&&shot4.Profile=="APFSDS gen 3","08-11b shot 4 is the APFSDS shot on the T-80BVM");
        Order(check,shot4,summary);
        Constituents(check,shot4,presets,summary);
        DrawnOnce(check,shot4);
        List(check,shot4,root);
        AllFixtures(check,sets,presets,summary);
        Language(check,sets,output,summary);
        Previews(check,sets[0].Shots,preview);
        File.WriteAllText(Path.Combine(output,"FIX42-SUMMARY.txt"),summary.ToString());
    }

    // ---- 1. order of the plates, derived from the sequence of the array's layers ----
    private static void Order(Action<bool,string> check,CombatReportShot shot4,StringBuilder summary)
    {
        var m=CombatForensics.Build(shot4);
        var layers=Shot4Layers.Select(id=>m.Layers.Single(l=>l.HitId==id)).ToArray();
        check(layers.All(l=>l.Array)&&layers.Select(l=>l.Number).SequenceEqual(Enumerable.Range(layers[0].Number,3)),"shot 4: the three native groups are consecutive composite-array layers");
        check(m.ArrayRuns.Count==1&&m.ArrayRuns[0].Layers.SequenceEqual(layers.Select(l=>l.Number)),"shot 4: the three groups form one composite array");
        var a=m.ArrayRuns[0];
        check(a.Plates.Select(p=>p.Part.Entity).SequenceEqual(new[]{Frame,Cassette,Hhs}),"shot 4: plate order frame → cassette → HHS ("+string.Join(" → ",a.Plates.Select(p=>p.Name))+")");
        check(a.Plates[0].Name=="ERA frame T80BVM"&&a.Plates[1].Material=="T-80U glacis"&&a.Plates[2].Material=="High Hardness Steel","shot 4: ERA frame T80BVM, T-80U glacis, High Hardness Steel");
        check(layers[0].Plates!.Select(p=>p.Part.Entity).SequenceEqual(new[]{Frame,Hhs})&&layers[1].Plates!.Select(p=>p.Part.Entity).SequenceEqual(new[]{Frame,Cassette,Hhs})&&layers[2].Plates!.Select(p=>p.Part.Entity).SequenceEqual(new[]{Cassette,Hhs}),
            "shot 4: each layer lists its own plates in the array order (A {frame, HHS}, B {frame, cassette, HHS}, C {cassette, HHS})");
        check(layers.All(l=>l.ArrayFirst==layers[0].Number),"shot 4: every layer refers to the array's first layer");
        check(layers.All(l=>l.Plates!.All(p=>p.Order==Array.IndexOf(new[]{Frame,Cassette,Hhs},p.Part.Entity)+1)),"shot 4: plate numbers count front to back");
        // Each layer keeps its own measured values (the HHS obliquity is 67.0° in A and B, 75.2° in C).
        check(layers[2].Plates!.Single(p=>p.Part.Entity==Hhs).Part.AngleDegrees>75&&layers[0].Plates!.Single(p=>p.Part.Entity==Hhs).Part.AngleDegrees<67.5,"shot 4: a plate row shows the obliquity measured in its own layer");
        check(a.Plates[2].Part.AngleDegrees<67.5,"shot 4: the drawing takes a plate's first measured obliquity (HHS 67.0°)");
        check(layers.All(l=>CombatForensicGeometry.LayerName(l)=="Composite array"),"the array rows are named Composite array");
        check(CombatForensicGeometry.Detail(layers[1])=="Rolled Homogeneous Armour → T-80U glacis → High Hardness Steel","the array row names its plates in order ("+CombatForensicGeometry.Detail(layers[1])+")");
        check(CombatForensicGeometry.Detail(layers[0])=="Rolled Homogeneous Armour → High Hardness Steel"&&CombatForensicGeometry.Detail(layers[2])=="T-80U glacis → High Hardness Steel","every array row names its own plates in order");
        var finding=m.Findings.SingleOrDefault(f=>f.Contains("composite array",StringComparison.Ordinal)&&f.Contains("front to back",StringComparison.Ordinal));
        check(finding==$"Layers {layers[0].Number}, {layers[1].Number} and {layers[2].Number} form one composite array of 3 plates, front to back: Rolled Homogeneous Armour, T-80U glacis, High Hardness Steel; each layer states the reduction of its plates together.","the finding states the array and its plates front to back ("+finding+")");
        // The rule itself, on synthetic sequences.
        CombatLayerPart P(int id)=>new(id,"Plate #"+id,"rha","Rolled Homogeneous Armour",10,30,true);
        IReadOnlyList<int> Ord(params int[][] seq)=>CombatForensics.PlateOrder(seq.Select(ids=>(IReadOnlyList<CombatLayerPart>)ids.Select(P).ToArray()).ToArray());
        check(Ord(new[]{Frame,Hhs},new[]{Frame,Cassette,Hhs},new[]{Cassette,Hhs}).SequenceEqual(new[]{Frame,Cassette,Hhs}),"rule: T-80BVM sequence gives frame → cassette → HHS");
        check(Ord(new[]{3,1,2}).SequenceEqual(new[]{3,1,2}),"rule: a single group keeps its plate list order");
        check(Ord(new[]{1,2},new[]{2}).SequenceEqual(new[]{1,2}),"rule: a plate that leaves before the last layer lies in front");
        check(Ord(new[]{2,1},new[]{1,3},new[]{3}).SequenceEqual(new[]{2,1,3}),"rule: plates ordered by the last layer that holds them");
        check(Ord(new[]{1,2},new[]{2,1}).SequenceEqual(new[]{2,1}),"rule: plates that remain to the last layer keep that layer's list order");
        check(Ord(new[]{5,5,6}).SequenceEqual(new[]{5,6}),"rule: a plate listed twice in one group counts once");
        summary.AppendLine("shot 4 array: layers "+string.Join(", ",a.Layers)+"; plates front to back: "+string.Join(" → ",a.Plates.Select(p=>$"{p.Name} ({p.Material}, {CombatForensicGeometry.PlateGeometry(p.Part)})")));
        summary.AppendLine("rule: plates ordered by the last array layer that holds them (a plate that leaves earlier lies in front, one that remains to the last layer lies behind); ties keep that layer's plate list order");
    }

    // ---- 2. constituents of the T-80U glacis preset inside the cassette plate ----
    private static void Constituents(Action<bool,string> check,CombatReportShot shot4,CassettePreset[] presets,StringBuilder summary)
    {
        var m=CombatForensics.Build(shot4);var a=m.ArrayRuns[0];var preset=presets.Single(p=>p.MaterialId=="ebCassette_t80u_hull");
        check(preset.Label=="T-80U glacis"&&preset.Layers.Length==3,"the shipped T-80U glacis preset has three layers");
        var cassette=a.Plates.Single(p=>p.Part.Entity==Cassette);
        check(cassette.Sublayers is {Count:3} subs&&subs.Select(s=>s.Material).SequenceEqual(preset.Layers.Select(l=>l.Material))&&subs.Select(s=>s.TiltDegrees).SequenceEqual(preset.Layers.Select(l=>l.Material=="air"?0:l.TiltDegrees)),
            "shot 4: the cassette shows the T-80U glacis layers in preset order (front to back) with their tilt");
        check(Math.Abs(cassette.Sublayers!.Sum(s=>s.NormalMm)-250)<=.5,$"shot 4: the constituent thicknesses add up to 250 mm (±0.5): {cassette.Sublayers!.Sum(s=>s.NormalMm).ToString("0.###",CultureInfo.InvariantCulture)}");
        check(cassette.Sublayers!.Select(s=>Math.Round(s.NormalMm,3)).SequenceEqual(new[]{70d,110,70}),"shot 4: Rolled Homogeneous Armour 70 mm, glass textolite 110 mm, Rolled Homogeneous Armour 70 mm");
        check(cassette.Sublayers!.All(s=>s.Colour==CassettePalette.Hex(s.Material)),"constituents use the composer colours");
        foreach(var l in m.Layers.Where(l=>l.ArrayFirst==a.First))
            foreach(var p in l.Plates!)check((p.Sublayers is {Count:>0})==(p.Part.Entity==Cassette),"only the preset plate carries constituents, in every layer that holds it");
        var g=CombatForensicGeometry.Build(m);var band=g.Arrays.Single().Plates.Single(p=>p.Plate.Part.Entity==Cassette);
        var sink=new Sink();CombatForensicDrawing.Draw(sink,m,false);
        check(band.Subs.Count==3&&Math.Abs(band.Subs.Sum(s=>s.ExitX-s.EntryX)-(band.ExitX-band.EntryX))<1,"the constituent bands fill the cassette band along the shot line (within 1 px)");
        check(band.Subs.All(s=>sink.Polygons.Count(p=>p.SequenceEqual(s.Polygon))==1),"the drawing paints each constituent once inside the cassette band");
        check(band.Subs.All(s=>s.ExitX-s.EntryX>=60),"the T-80U glacis constituents are clearly visible (each at least 60 px along the shot line): "+string.Join(", ",band.Subs.Select(s=>(s.ExitX-s.EntryX).ToString("0",CultureInfo.InvariantCulture)+" px")));
        var cassetteLayers=m.Layers.Where(l=>l.Plates?.Any(p=>p.Part.Entity==Cassette)==true).Select(l=>l.Number).ToArray();
        check(m.Findings.Contains($"Layers {cassetteLayers[0]} and {cassetteLayers[1]}, T-80U glacis: 3 constituent layers of the composer preset, scaled to the 250 mm normal thickness of the plate.",StringComparer.Ordinal),"a finding names the three constituent layers of the T-80U glacis and the layers that list them");
        // A user's own preset reads the same way from the loaded snapshot (here a synthetic snapshot entry).
        var own=new CassettePreset("user_t80u_copy","Own glacis","1985.01.01",80,120,new[]{new CassetteLayer("rha",40,0,"up"),new CassetteLayer("air",20,0,"up"),new CassetteLayer("highHardnessSteel",40,30,"up")},CassettePreset.Hull);
        var snapshot=presets.Append(own).ToArray();
        CombatCompositeLayers.Configure(id=>snapshot.FirstOrDefault(p=>p.MaterialId==id));
        try
        {
            var events=shot4.Events.Select(e=>e.Layer==null?e:e with{Layer=e.Layer with{Parts=e.Layer.Parts.Select(p=>p.Entity==Cassette?p with{MaterialId=own.MaterialId,MaterialLabel=own.Label}:p).ToArray()}}).ToArray();
            var mine=CombatForensics.Build(shot4 with{Events=events}).ArrayRuns.Single().Plates.Single(p=>p.Part.Entity==Cassette);
            check(mine.Material=="Own glacis"&&mine.Sublayers is {Count:3} s&&Math.Abs(s.Sum(x=>x.NormalMm)-250)<=.5&&s[1].Air&&s[2].TiltDegrees==30,"a user's own preset (snapshot) gives its constituents inside the array plate as well");
        }
        finally{CombatCompositeLayers.Configure(id=>presets.FirstOrDefault(p=>p.MaterialId==id));}
        summary.AppendLine("shot 4 cassette constituents: "+string.Join(" | ",cassette.Sublayers!.Select(s=>$"{s.Label} {CombatForensicGeometry.SubDetail(s)}"))+$" = {cassette.Sublayers!.Sum(s=>s.NormalMm).ToString("0.###",CultureInfo.InvariantCulture)} mm");
    }

    // ---- 3. each plate drawn once, as one continuous band ----
    private static void DrawnOnce(Action<bool,string> check,CombatReportShot shot4)
    {
        var m=CombatForensics.Build(shot4);var g=CombatForensicGeometry.Build(m);
        foreach(bool heat in new[]{false,true})
        {
            var sink=new Sink();CombatForensicDrawing.Draw(sink,m,heat);
            var plates=g.Arrays.Single().Plates;
            check(plates.Select(p=>p.Plate.Part.Entity).SequenceEqual(new[]{Frame,Cassette,Hhs}),"shot 4: three plate bands, frame → cassette → HHS");
            check(plates.All(p=>sink.Polygons.Count(x=>x.SequenceEqual(p.Polygon))==1),"shot 4: every plate is drawn exactly once"+(heat?" (heat-map)":""));
            check(g.Bands.Where(b=>b.Layer.ArrayFirst>0).All(b=>b.Polygon.Length==0),"shot 4: the array layers draw no band of their own (no plate three times)");
            check(sink.Marks==g.Bands.Count(b=>b.Polygon.Length==0&&!g.Drawn(b.Layer)),"shot 4: no mark line for a layer whose plates are drawn");
        }
        var a=g.Arrays.Single();
        for(int i=1;i<a.Plates.Count;i++)check(Math.Abs(a.Plates[i].EntryX-a.Plates[i-1].ExitX)<.01,"the plates follow each other without a gap along the shot line");
        foreach(var p in a.Plates)
        {
            var los=p.NormalMm/Math.Cos(p.Angle*Math.PI/180);
            check(Math.Abs((p.ExitX-p.EntryX)-los*g.Scale)<1,"a plate band is its own normal thickness at its own obliquity (within 1 px)");
            {
                // 0.22.71: a band that reaches another band ends along its edge; the slant is measured on the full band (Build with clip off) and a cut band must lie inside it
                var full=CombatForensicGeometry.Build(m,false).Arrays.Single().Plates.First(q=>q.Plate.Order==p.Plate.Order).Polygon;
                check(Math.Abs(full[0]-full[6]-2*(g.ShotY-g.Top)*Math.Tan(p.Angle*Math.PI/180))<.5,"a plate band slants at its own obliquity");
                check(p.Top==null?p.Polygon.SequenceEqual(full):Enumerable.Range(0,p.Polygon.Length/2).All(k=>p.Polygon[2*k]>=Math.Min(full[0],full[6])-2*(g.ShotY-g.Top)*Math.Tan(p.Angle*Math.PI/180)-.5),"a plate band that touches no other band is the full band; a cut band stays inside it");
            }
        }
        var stop=g.Bands.Single(b=>b.Layer.HitId==Shot4Layers[2]);var layer13=stop.Layer;
        check(stop.EntryX>=a.EntryX-.01&&stop.ExitX<=a.ExitX+.01&&layer13.After==0,"the arrest is marked inside the array, in the last layer");
        check(Math.Abs(a.ExitX-a.EntryX-(float)(m.ArrayRuns[0].PathMm*g.Scale))<1&&Math.Abs(m.ArrayRuns[0].PathMm-a.Plates.Sum(p=>p.NormalMm/Math.Cos(p.Angle*Math.PI/180)))<1e-6,"the array's extent is the sum of its plates' line-of-sight thicknesses, each once");
    }

    // ---- 4. the list: array row with its share, plates indented, constituents deeper ----
    private static void List(Action<bool,string> check,CombatReportShot shot4,string root)
    {
        var m=CombatForensics.Build(shot4);var html=CombatForensicHtml.Render(m,"k");
        var rows=XDocument.Parse("<r>"+html+"</r>").Descendants("div").Where(d=>((string?)d.Attribute("class")??"").StartsWith("fx-row",StringComparison.Ordinal)).ToArray();
        string Kind(XElement d)=>(string?)d.Attribute("class") switch{"fx-row fx-sub fx-plate"=>"plate","fx-row fx-sub fx-deep"=>"deep","fx-row fx-sub"=>"sub",_=>"layer"};
        string Name(XElement d)=>d.Elements("span").ElementAt(1).Nodes().OfType<XText>().FirstOrDefault()?.Value.Trim()??"";
        var layers=Shot4Layers.Select(id=>m.Layers.Single(l=>l.HitId==id)).ToArray();
        var expected=new Dictionary<int,string[]>{
            [layers[0].Number]=new[]{"plate:ERA frame T80BVM","plate:Plate #1"},
            [layers[1].Number]=new[]{"plate:ERA frame T80BVM","plate:Composite armour plate","deep:Rolled Homogeneous Armour","deep:Glass Textolite","deep:Rolled Homogeneous Armour","plate:Plate #1"},
            [layers[2].Number]=new[]{"plate:Composite armour plate","deep:Rolled Homogeneous Armour","deep:Glass Textolite","deep:Rolled Homogeneous Armour","plate:Plate #1"}};
        foreach(var l in layers)
        {
            int at=Array.FindIndex(rows,r=>Kind(r)=="layer"&&r.Elements("span").First().Value==l.Number.ToString(CultureInfo.InvariantCulture));
            check(at>=0&&Name(rows[at])=="Composite array","list: layer "+l.Number+" reads Composite array");
            var label=rows[at].Elements("span").Last().Value;
            check(l.Percent!=null&&label==CombatForensics.N(l.Percent)+"%","list: the array row keeps its share ("+label+")");
            var under=rows.Skip(at+1).TakeWhile(r=>Kind(r)!="layer").Select(r=>Kind(r)+":"+Name(r)).ToArray();
            var want=expected[l.Number];
            bool glass=under.Length==want.Length&&under.Zip(want).All(x=>x.Second.Contains("Glass Textolite")?x.First.StartsWith("deep:",StringComparison.Ordinal)&&x.First.Contains("extolite",StringComparison.OrdinalIgnoreCase):x.First==x.Second);
            check(glass,"list: under layer "+l.Number+" the plates indented and the T-80U glacis constituents one level deeper: "+string.Join(" | ",under));
            check(rows.Skip(at+1).TakeWhile(r=>Kind(r)!="layer").All(r=>!r.Value.Contains('%')),"list: plate and constituent rows carry no share");
        }
        var plateRow=rows.First(r=>Kind(r)=="plate"&&Name(r)=="ERA frame T80BVM");
        check(plateRow.Value.Contains("Rolled Homogeneous Armour · 5 mm at 67.3°",StringComparison.Ordinal),"list: a plate row gives its material, normal thickness and obliquity ("+plateRow.Value+")");
        check(rows.Where(r=>Kind(r)=="deep").All(r=>r.Value.Contains(" mm",StringComparison.Ordinal)),"list: a constituent row gives its thickness");
        check(html.Contains(".fx-row.fx-deep .fx-name{padding-left:44px}",StringComparison.Ordinal)||CombatForensicHtml.Css.Contains(".fx-row.fx-deep .fx-name{padding-left:44px}",StringComparison.Ordinal),"the deeper indentation is styled");
        // The Kit panel builds the same rows (the panel itself needs the game; its source is checked here).
        var panel=File.ReadAllText(Path.Combine(root,"CombatForensicPanel.cs"));
        check(panel.Contains("foreach(var plate in l.Plates??Array.Empty<ForensicArrayPlate>())",StringComparison.Ordinal)&&panel.Contains("SubRow(content,46,plate.Colour,",StringComparison.Ordinal)&&panel.Contains("SubRow(content,68,sub.Colour,",StringComparison.Ordinal),
            "Kit panel: plates indented under the array row, constituents one level deeper");
        check(!panel.Contains("Update(",StringComparison.Ordinal)&&panel.Contains(".Fps(0)",StringComparison.Ordinal),"Kit panel: no per-frame work for the new rows");
        // A plate without a measured thickness is listed, not drawn.
        var noThickness=CombatForensics.Build(shot4 with{Events=shot4.Events.Select(e=>e.Layer==null?e:e with{Layer=e.Layer with{Parts=e.Layer.Parts.Select(p=>p.Entity==Frame?p with{ThicknessMm=null}:p).ToArray()}}).ToArray()});
        var gn=CombatForensicGeometry.Build(noThickness);
        check(noThickness.ArrayRuns.Single().Plates.Any(p=>p.Part.Entity==Frame)&&gn.Arrays.Single().Plates.All(p=>p.Plate.Part.Entity!=Frame),"a plate without a measured thickness stays in the list and gets no assumed width");
        var frameRows=XDocument.Parse("<r>"+CombatForensicHtml.Render(noThickness,"n")+"</r>").Descendants("div").Where(d=>(string?)d.Attribute("class")=="fx-row fx-sub fx-plate"&&d.Value.Contains("ERA frame T80BVM",StringComparison.Ordinal)).ToArray();
        check(frameRows.Length==2&&frameRows.All(d=>d.Value=="ERA frame T80BVM Rolled Homogeneous Armour · obliquity 67.3°"),"its row gives only what was measured ("+string.Join(" | ",frameRows.Select(d=>d.Value))+")");
    }

    // ---- the rule on every 0.22.41 fixture ----
    private static void AllFixtures(Action<bool,string> check,(string Name,CombatReportShot[] Shots)[] sets,CassettePreset[] presets,StringBuilder summary)
    {
        int arrays=0,runs=0,plates=0,presetPlates=0;var ledger=new StringBuilder("fixture\tshot\tlayers\tplates_front_to_back\n");
        foreach(var (name,shots) in sets)foreach(var raw in shots)
        {
            var m=CombatForensics.Build(raw);var g=CombatForensicGeometry.Build(m);var sink=new Sink();CombatForensicDrawing.Draw(sink,m,false);
            double expected=0;
            foreach(var l in m.Layers){check(Math.Abs(l.StartMm-expected)<1e-6,"drawn positions follow each other along the shot line");expected+=l.PathMm??0;}
            foreach(var l in m.Layers.Where(l=>l.Array))
            {
                arrays++;
                check(l.ArrayFirst>0&&m.ArrayRuns.Count(a=>a.Layers.Contains(l.Number))==1&&m.ArrayRuns.Single(a=>a.Layers.Contains(l.Number)).First==l.ArrayFirst,"every array layer belongs to exactly one composite array");
                check(l.Plates!.Select(p=>p.Part.Entity).OrderBy(x=>x).SequenceEqual(l.Parts.Where(p=>p.Armour).Select(p=>p.Entity).Distinct().OrderBy(x=>x)),"an array layer lists each of its plates once");
                check(l.Sublayers==null,"an array layer has no constituents of its own");
            }
            check(m.Layers.Where(l=>!l.Array).All(l=>l.ArrayFirst==0&&l.Plates==null),"other layers are not part of an array");
            foreach(var a in m.ArrayRuns)
            {
                runs++;var members=a.Layers.Select(n=>m.Layers[n-1]).ToArray();
                check(a.Layers.Zip(a.Layers.Skip(1)).All(x=>x.Second==x.First+1)&&members.All(l=>l.Stage==members[0].Stage),"an array is a run of consecutive layers of one stage");
                check(members.Zip(members.Skip(1)).All(x=>x.First.Parts.Any(p=>p.Armour&&x.Second.Parts.Any(q=>q.Armour&&q.Entity==p.Entity))),"consecutive layers of an array share a plate");
                var ids=a.Plates.Select(p=>p.Part.Entity).ToArray();
                check(ids.Distinct().Count()==ids.Length,"each plate of an array appears once");
                check(ids.SequenceEqual(CombatForensics.PlateOrder(members.Select(l=>l.Parts).ToArray())),"array order follows the rule");
                // The rule as a property: a plate that leaves earlier lies in front; ties keep the list order of that layer.
                (int Layer,int Index) Last(int id){int k=Array.FindLastIndex(members,l=>l.Parts.Any(p=>p.Armour&&p.Entity==id));return (k,members[k].Parts.Where(p=>p.Armour).Select(p=>p.Entity).Distinct().ToList().IndexOf(id));}
                for(int i=1;i<ids.Length;i++){var x=Last(ids[i-1]);var y=Last(ids[i]);check(x.Layer<y.Layer||x.Layer==y.Layer&&x.Index<y.Index,"front plates leave the array first; ties in list order");}
                foreach(var l in members)check(l.Plates!.Select(p=>p.Part.Entity).SequenceEqual(ids.Where(id=>l.Plates!.Any(p=>p.Part.Entity==id))),"each layer lists its plates in the array order");
                var band=g.Arrays.Single(x=>x.Array.First==a.First);
                check(band.Plates.Select(p=>p.Plate.Part.Entity).SequenceEqual(a.Plates.Where(p=>CombatForensics.Path(p.Part)!=null).Select(p=>p.Part.Entity)),"exactly the plates with a measured thickness and obliquity are drawn, in order");
                check(band.Plates.All(p=>sink.Polygons.Count(x=>x.SequenceEqual(p.Polygon))==1),"every drawn plate appears once in the drawing");
                plates+=band.Plates.Count;
                foreach(var p in band.Plates)check(Math.Abs((p.ExitX-p.EntryX)-p.NormalMm/Math.Cos(p.Angle*Math.PI/180)*g.Scale)<1,"plate band width is its line-of-sight thickness (within 1 px)");
                check(members.All(l=>g.Bands.Single(b=>ReferenceEquals(b.Layer,l)).Polygon.Length==0),"array layers draw no band of their own");
                if(band.Plates.Count>0)check(Math.Abs(a.PathMm-band.Plates.Sum(p=>p.NormalMm/Math.Cos(p.Angle*Math.PI/180)))<1e-6&&Math.Abs(members.Sum(l=>l.PathMm??0)-a.PathMm)<1e-6,"the array layers share the array's extent");
                foreach(var p in a.Plates.Where(p=>p.Sublayers is {Count:>0}))
                {
                    presetPlates++;var preset=presets.Single(x=>x.MaterialId==p.Part.MaterialId);
                    check(Math.Abs(p.Sublayers!.Sum(s=>s.NormalMm)-p.Part.ThicknessMm!.Value)<=.5&&p.Sublayers!.Select(s=>s.Material).SequenceEqual(preset.Layers.Select(x=>x.Material)),"array preset plate: constituents add up to its normal thickness (±0.5 mm), in preset order");
                    if(band.Plates.SingleOrDefault(b=>b.Plate.Part.Entity==p.Part.Entity) is {} pb)check(pb.Subs.Count==p.Sublayers!.Count&&Math.Abs(pb.Subs.Sum(s=>s.ExitX-s.EntryX)-(pb.ExitX-pb.EntryX))<1,"array preset plate: constituent bands fill the plate band");
                }
                ledger.AppendLine($"{name}\t{raw.Number}\t{string.Join(",",a.Layers)}\t{string.Join(" → ",a.Plates.Select(p=>p.Name+" ("+p.Material+")"))}");
            }
            // 08-11 shot 3: the Oplot plate has a measured thickness only in layer 6, so only layer 6 lists (and the finding names) its constituents.
            if(name=="forensic-fixtures-1108"&&raw.Number==3)
                check(m.Findings.Contains("Layer 6, Oplot (T-84) glacis: 4 constituent layers of the composer preset, scaled to the 215 mm normal thickness of the plate.",StringComparer.Ordinal)
                    &&m.Layers[6].Plates!.All(p=>p.Sublayers==null),"a preset finding names only the layers that list the constituents (08-11 shot 3: layer 6)");
        }
        check(arrays>=40&&runs>=30&&presetPlates>=5,$"the rule is exercised on every fixture ({arrays} array layers, {runs} arrays, {plates} drawn plates, {presetPlates} preset plates)");
        File.WriteAllText(Path.Combine(Fix02228Tests.OutputRoot(),"ARRAY-ORDER.tsv"),ledger.ToString());
        summary.AppendLine($"all fixtures: {arrays} array layers in {runs} composite arrays, {plates} plate bands drawn once each, {presetPlates} preset plates with constituents (ARRAY-ORDER.tsv)");
    }

    // ---- 5. language ----
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
    /// <summary>Every visible forensic text, including the 0.22.42 plate and constituent rows.</summary>
    internal static IEnumerable<string> ForensicTexts(ForensicModel m)
    {
        foreach(var t in Fix02241Tests.ForensicTexts(m))yield return t;
        foreach(var l in m.Layers)foreach(var p in l.Plates??Array.Empty<ForensicArrayPlate>())
        {
            yield return p.Name;yield return p.Material;yield return CombatForensicGeometry.PlateDetail(p);
            foreach(var s in p.Sublayers??Array.Empty<CompositeSublayer>()){yield return s.Label;yield return CombatForensicGeometry.SubDetail(s);}
        }
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
        var hits=new StringBuilder("fixture\tshot\tsurface\tterm\ttext\n");int scanned=0,violations=0,uncertain=0,retired=0;
        void Scan(string fixture,int shot,string surface,IEnumerable<string> texts,IEnumerable<string> exempt,bool forensic)
        {
            var ex=exempt.Where(e=>!string.IsNullOrWhiteSpace(e)).Distinct().ToArray();
            foreach(var text in texts.Where(t=>!string.IsNullOrEmpty(t)))
            {
                scanned++;
                foreach(var term in CombatReportLanguage.JargonIn(text,ex)){violations++;hits.AppendLine($"{fixture}\t{shot}\t{surface}\t{term}\t{text}");}
                if(forensic)foreach(var term in CombatReportLanguage.UncertaintyIn(text,ex)){uncertain++;hits.AppendLine($"{fixture}\t{shot}\t{surface} (uncertainty)\t{term}\t{text}");}
                if(forensic&&(text.Contains("sequence not resolved",StringComparison.OrdinalIgnoreCase)||text.Contains("not resolved",StringComparison.OrdinalIgnoreCase))){retired++;hits.AppendLine($"{fixture}\t{shot}\t{surface} (retired phrase)\tnot resolved\t{text}");}
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
            check(!html.Contains("layer sequence not resolved",StringComparison.Ordinal),name+": the HTML no longer says \"layer sequence not resolved\"");
        }
        File.WriteAllText(Path.Combine(output,"LANGUAGE-CHECK-02242.tsv"),hits.ToString());
        check(violations==0,$"forbidden-term list: 0 hits in panel, hit panel, HTML and forensic text, plate rows included ({violations}; LANGUAGE-CHECK-02242.tsv)");
        check(uncertain==0,$"no uncertainty statements in the forensic screen on any fixture ({uncertain})");
        check(retired==0,$"\"(layer sequence not resolved)\" is gone from the forensic screen ({retired})");
        check(scanned>5000,"language scan covers the generated text ("+scanned+" strings)");
        check(CombatReportLanguage.UncertaintyIn("Composite array (layer sequence not resolved)").Count==1,"the checker no longer exempts the retired phrase");
        summary.AppendLine($"language: {scanned} visible strings scanned, {violations} forbidden terms, {uncertain} uncertainty statements, {retired} retired phrases");
    }

    // ---- previews ----
    private static void Previews(Action<bool,string> check,CombatReportShot[] shots,string preview)
    {
        var css=CombatReportHtml.Render(new CombatReportHistory());css=css[(css.IndexOf("<style>",StringComparison.Ordinal)+7)..css.IndexOf("</style>",StringComparison.Ordinal)];
        foreach(var shot in shots)Forensic(CombatForensics.Build(shot),$"forensic-1108b-shot-{shot.Number}",preview,css);
        // Shot 4's cross-section at a large size (the same SVG as the HTML report, 2.25 times the Kit drawing width).
        var m4=CombatForensics.Build(shots.Single(s=>s.Number==4));
        string Large(bool heat)=>CombatForensicDrawing.Svg(m4,heat,"large-"+(heat?"heat":"material")).Replace("class='forensic-section'","class='forensic-section' width='2385' height='945'");
        File.WriteAllText(Path.Combine(preview,"forensic-1108b-shot-4-section-large.html"),"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>Cross-section, impact 4</title><style>body{margin:0;padding:20px;background:#10151a;color:#e7edf2;font:16px system-ui}svg{display:block;font-family:system-ui}h1{font-size:18px;color:#f1d29a;margin:0 0 10px}p{color:#b8ad94;margin:6px 0 18px}</style></head><body><div class='window'><h1>Impact 4: APFSDS gen 3 → T-80BVM, cross-section along the shot line (material)</h1>"+Large(false)+
            "<p>"+WebUtility.HtmlEncode(ForensicModel.GeometryNote)+"</p><h1>Heat-map</h1>"+Large(true)+"<p>"+WebUtility.HtmlEncode(ForensicModel.Footnote)+"</p></div><script>document.body.setAttribute('data-mockup-height',Math.ceil(document.querySelector('.window').getBoundingClientRect().bottom+20));</script></body></html>");
        check(shots.All(s=>File.Exists(Path.Combine(preview,$"forensic-1108b-shot-{s.Number}.html")))&&File.Exists(Path.Combine(preview,"forensic-1108b-shot-4-section-large.html")),"forensic previews of every 08-11b shot and the large cross-section of shot 4");
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
        File.WriteAllText(Path.Combine(preview,stem+".html"),"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'/><title>Panel mockup</title><style>"+css+".forensic{border:0;margin:0}.forensic h3{display:none}"+
            "body{max-width:none;margin:0;padding:20px;background:#10151a}.window{background:#232930;border:1px solid #5a6268;border-radius:8px;width:1180px}.title{padding:15px 20px;background:#1b211c;color:#f1d29a;font-size:18px}.viewport{padding:0 20px 12px}.footer{padding:12px 20px;background:#1b211c;color:#b8ad94;font-size:12px}.fx-row{font-size:13px}</style></head><body><div class='window'><div class='title'>"+
            $"{ForensicModel.Title} <small>| Expanded Ballistics | impact #{m.Number}</small><span style='float:right'>X</span></div><div class='viewport'>"+fragment+"</div><div class='footer'>Mockup from the shared report model (expanded content; the panel in the game scrolls).</div></div><script>document.body.setAttribute('data-mockup-height',Math.ceil(document.querySelector('.window').getBoundingClientRect().bottom+20));</script></body></html>");
    }
    private sealed class Sink:ISectionSink
    {
        internal List<float[]> Polygons=new();internal List<string> Texts=new();internal int Marks;
        public void ViewBox(float w,float h){}public void BeginClip(float w,float h){}public void EndClip(){}
        public void Poly(float[] p,string f,string? s,float w)=>Polygons.Add(p);
        public void Hatch(float[] p,string c,float a,float s,float w){}
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
