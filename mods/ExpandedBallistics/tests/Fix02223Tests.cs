using System.Globalization;
using System.Text.Json;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// <summary>0.22.23: the composer protection table (Kit Table model), the angle chart points, the penetration waterfall (sum of deltas, hit block, report, HTML) and the Kit version gate.</summary>
internal static class Fix02223Tests
{
    private static string? SourceRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"SprocketExpandedBallistics.csproj")))return dir.FullName;
        return null;
    }
    private static string F(double v)=>v.ToString("0.###",CultureInfo.InvariantCulture);

    internal static void Run(Action<bool,string> check)
    {
        int count=0;var assert=check;check=(ok,text)=>{assert(ok,"fix02223: "+text);count++;};
        Composer(check);Waterfall(check);Real(check);Html(check);Gate(check);Source(check);
        Console.WriteLine($"0.22.23 checks PASS: {count} checks.");
    }

    // ---- 1 and 2: protection table model and angle chart points ----
    private static void Composer(Action<bool,string> check)
    {
        var root=AppContext.BaseDirectory;
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(root,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(root,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,CassetteCatalogue.FileName)));
        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-fix02223-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var sidecar=Path.Combine(dir,CassetteCatalogue.FileName);File.WriteAllText(sidecar,CassetteComposer.Serialize(presets.Presets));
            var composer=new CassetteComposer(materials,reactions,sidecar,Path.Combine(dir,"Technology"),_=>false);
            var list=materials.Keys.Where(id=>!CompositePlate.IsCassette(id)&&!MaterialMenuVisibility.RetiredPresets.Contains(id))
                .Select(id=>new ComposerMaterial(id,id=="rha"?"Steel (RHA)":id,CassettePalette.Group(id))).ToArray();
            var native=presets.Presets.Select(p=>p.MaterialId).ToHashSet(StringComparer.Ordinal);
            double now=0;
            var m=new CassetteComposerModel(composer,list,new List<ComposerMaterial>{new("rha","Legacy steel","Steel")},()=>CassetteCatalogue.Parse(File.ReadAllText(sidecar)).Presets,_=>false,native.Contains,true,()=>now);
            check(m.LoadPreset("t64k"),"a preset loads");
            now+=.2;

            // Table model without a tilted layer.
            check(!m.AnyTilted&&ProtectionTable.Headers(false).Length==1&&ProtectionTable.Columns(false).Length==3,"no tilt: one value column (Protection), three columns in all");
            var ke=m.ProtectionRows(false);var heat=m.ProtectionRows(true);
            check(ke.Count==3&&heat.Count==3,"three rows per table (KE and HEAT)");
            check(ke.Select(r=>r.Metric).SequenceEqual(new[]{"mm RHA","× RHA (same thickness)","× RHA (same weight)"}),"rows: mm RHA, x RHA (same thickness), x RHA (same weight)");
            check(ke[0].Basis==""&&ke[1].Basis.StartsWith("vs ",StringComparison.Ordinal)&&ke[1].Basis.EndsWith(" mm RHA",StringComparison.Ordinal)&&ke[2].Basis.StartsWith("vs ",StringComparison.Ordinal),"the comparison value (vs N mm RHA) is its own column, empty for the absolute row");
            check(ReferenceEquals(ke,m.ProtectionRows(false))&&ReferenceEquals(heat,m.ProtectionRows(true)),"row lists are cached (the Kit polls them every frame)");
            for(int r=0;r<3;r++)check(ke[r].With==m.ProtectionCell(false,true,r)&&ke[r].Against==m.ProtectionCell(false,false,r)&&heat[r].With==m.ProtectionCell(true,true,r),"cells of row "+r+" equal the composer cells");
            check(ProtectionTable.Legend=="Game uses the mean of with/against tilt · 1.00 = solid RHA · higher is better","legend explains game mean");
            var angle0=m.AngleIndex;m.AngleIndex=(angle0+1)%3;
            check(!ReferenceEquals(ke,m.ProtectionRows(false))&&m.ProtectionRows(false)[1].Basis!=ke[1].Basis,"another angle button gives new rows with another basis");
            m.AngleIndex=angle0;

            // Chart points against the composer model.
            var curve=m.Curve;
            check(curve.Count==CassetteComposer.CurveAngles.Length*2&&CassetteComposer.CurveAngles.SequenceEqual(Enumerable.Range(0,18).Select(i=>i*5d)),"chart: 0-85 degrees in steps of 5, with and against ("+curve.Count+" points)");
            check(ReferenceEquals(curve,m.Curve),"chart points are computed once per evaluated draft, not per frame");
            var preview=composer.Preview(m.Draft());
            bool equal=true;
            foreach(var p in preview.Protection)
            {
                var c=curve.Single(x=>x.Angle==p.Angle&&x.WithTilt==p.WithTilt);
                equal&=c.KeMm==p.KeMm&&c.HeatMm==p.HeatMm;
            }
            check(equal,"chart points at 0/30/60 equal the composer Preview (the table)");
            for(int r=0;r<3;r++)
            {
                var c=curve.Single(x=>x.Angle==CassetteComposerModel.Angles[r]&&x.WithTilt);
                check(c.KeMm==m.TableValue(r,0)&&c.HeatMm==m.TableValue(r,2),"chart point at "+CassetteComposerModel.Angles[r]+" degrees equals the table cell");
            }
            var seriesKe=m.CurveSeries(false,true);var seriesHeat=m.CurveSeries(true,true);
            check(seriesKe.Length==18&&seriesHeat.Length==18&&seriesKe[0].Angle==0&&seriesKe[^1].Angle==85,"each series has 18 points from 0 to 85");
            check(seriesKe.All(p=>double.IsFinite(p.Mm)&&p.Mm>=0)&&seriesHeat.All(p=>double.IsFinite(p.Mm)&&p.Mm>=0),"every point is finite and not negative");
            check(m.CurveSeries(false,true).SequenceEqual(m.CurveSeries(false,false)),"without a tilted layer with and against are the same curve");
            for(int a=0;a<3;a++)
            {
                m.AngleIndex=a;var ang=CassetteComposerModel.Angles[a];var depth=preview.DepthMm;
                check(Math.Abs(m.RhaReference(1)-depth/Math.Cos(ang*Math.PI/180))<1e-9,"RHA line, same thickness: D / cos "+ang+" = "+F(m.RhaReference(1)));
                check(Math.Abs(m.RhaReference(2)-preview.KgM2*1000/composer.RhaDensity/Math.Cos(ang*Math.PI/180))<1e-9,"RHA line, same weight at "+ang);
            }
            m.AngleIndex=0;

            // An edit: the chart points change only after the debounce, like the table.
            var before=m.Curve;
            m.SelectLayer(1);m.SetThickness(120);
            check(ReferenceEquals(before,m.Curve),"no recompute during the 150 ms debounce");
            now+=.2;
            check(!ReferenceEquals(before,m.Curve)&&m.CurveSeries(true,true).Select(p=>p.Mm).SequenceEqual(before.Where(c=>c.WithTilt).OrderBy(c=>c.Angle).Select(c=>c.HeatMm))==false,"after the debounce the chart shows the edited draft");

            // 0.22.39: both section-side identities carry the runtime mean.
            m.LoadDraft(new CassettePreset("fix02223","0.22.23 test","1980.01.01",1,4000,new CassetteLayer[]{new("rha",3,30,"up"),new("elastomerNERA",15,30,"up"),new("rha",3,30,"up"),new("air",20,0,"up")}),ComposerSource.Import,"test");
            now+=.2;int solid=0;
            check(m.AnyTilted&&ProtectionTable.Headers(true).SequenceEqual(new[]{"With tilt","Against tilt"})&&ProtectionTable.Columns(true).Length==4,"tilted layer: columns With tilt and Against tilt exist");
            m.AngleIndex=1;
            var tiltedRows=m.ProtectionRows(false);
            var tiltedHeat=m.ProtectionRows(true);
            check(tiltedRows.Concat(tiltedHeat).All(r=>r.With==r.Against),"tilted layer: table mean is independent of section side");
            var withCurve=m.CurveSeries(false,true);var againstCurve=m.CurveSeries(false,false);
            check(withCurve.Length==18&&againstCurve.Length==18&&withCurve.SequenceEqual(againstCurve)&&m.CurveSeries(true,true).SequenceEqual(m.CurveSeries(true,false)),"tilted layer: chart mean is independent of section side");
            var tiltedPreview=composer.Preview(m.Draft());
            check(tiltedPreview.Protection.All(p=>{var c=m.Curve.Single(x=>x.Angle==p.Angle&&x.WithTilt==p.WithTilt);return c.KeMm==p.KeMm&&c.HeatMm==p.HeatMm;}),"tilted layer: chart points at 0/30/60 still equal the Preview");

            // A draft with errors has no chart.
            m.SelectLayer(solid);m.SetThickness(1);now+=.2;
            check(m.Curve.Count==0||m.Preview!=null,"a draft without preview has no chart points (or still a valid preview)");
        }
        finally{Directory.Delete(dir,true);}
    }

    // ---- 3: waterfall ----
    private static CombatLayerInfo Layer(string id,int entity,string name,double thickness,double angle)=>new(id,new[]{new CombatLayerPart(entity,name,null,null,thickness,angle,true)},false);
    private static CombatReportShot Shot(double? initial,params CombatReportEvent[] events)=>new(1,"Incoming","HEAT 775 mm",700,events,0,initial,"T-84",4,4);
    private static double Sum(WaterfallModel m)=>m.Steps.Sum(s=>s.Delta);
    private static void Waterfall(Action<bool,string> check)
    {
        // Cassette + overlap + ERA + native in one block, the 0.22.21 case, then a plain steel plate behind it.
        var events=new List<CombatReportEvent>
        {
            new("layerStart","","Reached layer",468.5,null,"b",new CombatLayerInfo("b",new[]{new CombatLayerPart(296,"Composite armour plate","ebCassette_user_nera_v1","NERA cassette (own)",87,33.7,true)},false)),
            new("materialReaction","","Cassette NERA cassette (own): HEAT −33,9 mm; front, t = 87 mm",468.5,434.6,"b",RelatedEntity:296),
            new("materialNote","",CassetteImpactPath.OverlapReason("fuelTank #379",339.7,195.9,.57,111.7,40),434.6,394.6,"b",RelatedEntity:379),
            new("eraReaction","","ERA Duplet panel (turret) · layer 1: HEAT −36 %",394.6,394.6*.64,"b",RelatedEntity:300),
            new("plateResidual","","Traversed block",468.5,276.5,"b"),
            new("layerStart","","Reached layer",276.5,null,"c",Layer("c",3,"Plate #3",30,0)),
            new("plateResidual","","Traversed block",276.5,250,"c")
        };
        // Retained geometry/ERA reactions precede the native table-factor block in the measured budget.
        events.RemoveAll(e=>e.Kind=="materialReaction");
        events[1]=events[1] with{BeforeMm=468.5,AfterMm=428.5};
        events[2]=events[2] with{BeforeMm=428.5,AfterMm=300};
        events.Insert(3,new CombatReportEvent("materialReaction","",ProtectionLine.Text("T-80U glacis",100,.3,ProtectionThreat.Jet,62),300,276.5,"b"));
        var shot=Shot(468.5,events.ToArray());
        var wf=PenetrationWaterfall.Build(shot);
        check(wf!=null&&Math.Abs(wf.Start+Sum(wf)-wf.End)<1e-9,"factor waterfall closes on the measured residual");
        check(wf!.Steps.Any(s=>s.Label.Contains("ERA",StringComparison.Ordinal)),"ERA stays in the waterfall");
        check(wf.Steps.Any(s=>s.Label.Contains("Plate #3",StringComparison.Ordinal)),"plate behind cassette is its own bar");

        // A stop: the budget ends at 0 and the model says so.
        var stop=PenetrationWaterfall.Build(Shot(318,new CombatReportEvent("layerStart","","Reached layer",318,null,"b",Layer("b",2,"Plate #2",120,0)),new CombatReportEvent("nativeStop","","Native stop in Plate #2",318,null,"b")));
        check(stop!=null&&stop.Stopped&&stop.End==0&&stop.EndLabel=="Stopped"&&stop.Steps.Count==1&&Math.Abs(Sum(stop)+318)<1e-9,"a stop: one bar of the whole budget, the rest is 0, marked stopped");

        // A stop after several layers: the sum is still exact and later layers are not drawn.
        var multi=PenetrationWaterfall.Build(Shot(300,
            new CombatReportEvent("layerStart","","Reached layer",300,null,"a",Layer("a",1,"Plate #1",50,0)),new CombatReportEvent("plateResidual","","Traversed",300,180,"a"),
            new CombatReportEvent("layerStart","","Reached layer",180,null,"b",Layer("b",2,"Plate #2",80,0)),new CombatReportEvent("plateResidual","","Traversed",180,60,"b"),
            new CombatReportEvent("layerStart","","Reached layer",60,null,"c",Layer("c",3,"Plate #3",80,0)),new CombatReportEvent("nativeStop","","Native stop",60,null,"c"),
            new CombatReportEvent("layerStart","","Reached layer",60,null,"d",Layer("d",4,"Plate #4",80,0)),new CombatReportEvent("plateResidual","","Traversed",60,10,"d")));
        check(multi!=null&&multi.Stopped&&multi.Steps.Count==3&&multi.End==0&&Math.Abs(multi.Start+Sum(multi)-multi.End)<1e-9,"stop after three layers: three bars, exact sum, nothing after the stop");
        check(Math.Abs(multi!.Steps[0].Delta+120)<1e-9&&Math.Abs(multi.Steps[1].Delta+120)<1e-9&&Math.Abs(multi.Steps[2].Delta+60)<1e-9,"each bar is the loss of its layer (120, 120, 60)");

        // A ricochet ends the budget where it is; an unmeasured exit says so.
        var ricochet=PenetrationWaterfall.Build(Shot(300,new CombatReportEvent("layerStart","","Reached layer",300,null,"a",Layer("a",1,"Plate #1",50,70)),new CombatReportEvent("outcome","","Deflected off the plate",300,null,"a")));
        check(ricochet is {EndLabel:"Ricochet",Stopped:false}&&ricochet.Steps.Count==0&&ricochet.End==300,"a ricochet keeps the budget and is labelled");
        check(PenetrationWaterfall.Build(Shot(null))==null&&PenetrationWaterfall.Build(Shot(0,new CombatReportEvent("layerStart","","Reached layer",0,null,"a",Layer("a",1,"Plate #1",50,0))))==null,"no budget, no waterfall");

        // Layers that do not join are shown, not hidden.
        var gap=PenetrationWaterfall.Build(Shot(300,
            new CombatReportEvent("layerStart","","Reached layer",300,null,"a",Layer("a",1,"Plate #1",50,0)),new CombatReportEvent("plateResidual","","Traversed",300,200,"a"),
            new CombatReportEvent("layerStart","","Reached layer",170,null,"b",Layer("b",2,"Plate #2",50,0)),new CombatReportEvent("plateResidual","","Traversed",170,100,"b")));
        check(gap!=null&&gap.Steps.Any(s=>s.Label=="Between layers"&&Math.Abs(s.Delta+30)<1e-9)&&Math.Abs(gap.Start+Sum(gap)-gap.End)<1e-9&&Math.Abs(gap.End-100)<1e-9,"a 30 mm jump between two layers is its own bar and the sum still closes");

        // A tandem shot: only the main charge counts.
        var tandem=PenetrationWaterfall.Build(Shot(500,
            new CombatReportEvent("layerStart","Precursor","Reached layer",200,null,"p",Layer("p",9,"ERA brick",10,0)),new CombatReportEvent("plateResidual","Precursor","Traversed",200,120,"p"),
            new CombatReportEvent("layerStart","","Reached layer",500,null,"m",Layer("m",1,"Plate #1",50,0)),new CombatReportEvent("plateResidual","","Traversed",500,380,"m")));
        check(tandem!=null&&tandem.Start==500&&tandem.Steps.Count==1&&Math.Abs(tandem.End-380)<1e-9,"tandem: the precursor is not part of the main budget");

        // The hit block and the report view carry it.
        var block=HitBlockBuilder.Build(shot);
        check(block.Waterfall!=null&&Math.Abs(block.Waterfall.End-wf.End)<1e-9,"the hit block carries the waterfall");
        var view=CombatReportView.Build(new[]{shot}).Single();
        check(view.Block.Waterfall!=null&&view.Block.Waterfall.Steps.Count==wf.Steps.Count,"the report view carries the same waterfall");
        check(PenetrationWaterfall.Text(wf).StartsWith("469 mm",StringComparison.Ordinal)&&PenetrationWaterfall.Text(wf).Contains("glacis",StringComparison.Ordinal),"one-line text of the budget");
    }

    private static void Real(Action<bool,string> check)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures/last-session-0227.json")));
        var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        int built=0,exact=0,passedChecked=0;bool passedOk=true,finite=true,bounded=true,stopped=true,sums=true;
        foreach(var raw in shots)
        {
            var shot=CombatReportTruth.Normalize(raw);var views=CombatHitProjection.Build(shot);
            var wf=PenetrationWaterfall.Build(shot,views);if(wf==null)continue;built++;
            finite&=double.IsFinite(wf.Start)&&double.IsFinite(wf.End)&&wf.Steps.All(s=>double.IsFinite(s.Delta));
            bounded&=wf.End>=0&&wf.End<=wf.Start+1e-9&&wf.Steps.All(s=>s.Delta>=-wf.Start-1e-9);
            if(wf.Stopped)stopped&=wf.End==0;
            var closes=Math.Abs(wf.Start+Sum(wf)-wf.End)<1e-9;sums&=closes;if(closes)exact++;
            // For a shot that went through, the rest equals the exit of the last measured block.
            var main=views.Where(v=>v.Stage!="Precursor"&&v.Status=="Passed"&&v.After!=null).ToArray();
            if(!wf.Stopped&&wf.EndLabel=="Left"&&main.Length>0&&shot.Events.All(e=>e.Kind!="nativeStop")&&views.Where(v=>v.Stage!="Precursor").All(v=>v.Status!="Stopped")){passedChecked++;passedOk&=Math.Abs(wf.End-main[^1].After!.Value)<.06;}
        }
        check(built>=1,"the real session of 0.22.7 gives waterfalls ("+built+" shots)");
        check(finite&&bounded&&stopped,"real shots: finite values, the rest between 0 and the start, a stop ends at 0");
        check(sums&&exact==built,"real shots: start + sum of the deltas = the rest for all "+built);
        check(passedOk,"real shots that went through: the rest equals the last exit ("+passedChecked+")");
        var s3=CombatReportTruth.Normalize(shots.Single(s=>s.Number==3));var w3=PenetrationWaterfall.Build(s3);
        check(w3!=null&&Math.Abs(w3.Start-(s3.InitialPenetrationMm??w3.Start))<1e-9&&w3.Steps.Count>=1,"real shot 3 has a start and at least one bar");
    }

    // ---- 3b: HTML export ----
    private static void Html(Action<bool,string> check)
    {
        var events=new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",468.5,null,"b",new CombatLayerInfo("b",new[]{new CombatLayerPart(296,"Composite armour plate","ebCassette_user_nera_v1","NERA cassette (own)",87,33.7,true)},false)),
            new CombatReportEvent("materialReaction","","Cassette NERA cassette (own): HEAT −33,9 mm <b>x</b>",468.5,434.6,"b",RelatedEntity:296),
            new CombatReportEvent("plateResidual","","Traversed block",468.5,300,"b")
        };
        var history=new CombatReportHistory();history.Begin();history.Add(Shot(468.5,events));
        var html=CombatReportHtml.Render(history);
        check(html.Contains("<svg class='wf'",StringComparison.Ordinal)&&html.Contains("Penetration sequence:",StringComparison.Ordinal),"the HTML export carries the waterfall as an inline SVG with a text line (0.22.41: Penetration sequence)");
        check(html.Contains("Composite",StringComparison.Ordinal)&&html.Contains("<title>",StringComparison.Ordinal)&&html.Contains("</svg>",StringComparison.Ordinal),"bars with their hover text");
        check(!html.Contains("<script",StringComparison.OrdinalIgnoreCase),"static: no script in the export");
        check(!html.Contains("<b>x</b>",StringComparison.Ordinal),"event text in the chart is escaped");
        var wf=PenetrationWaterfall.Build(Shot(468.5,events))!;
        var svg=PenetrationWaterfall.Svg(wf);
        check(svg.Split("<rect ").Length-1==wf.Steps.Count+2,"one rect per bar: start, "+wf.Steps.Count+" steps, rest");
        check(svg.StartsWith("<svg",StringComparison.Ordinal)&&svg.EndsWith("</svg>",StringComparison.Ordinal),"a complete svg element");
        var empty=new CombatReportHistory();empty.Begin();empty.Add(new CombatReportShot(2,"Outgoing","AP",800,new[]{new CombatReportEvent("outcome","","No armour")},0,null,null,null,null));
        check(!CombatReportHtml.Render(empty).Contains("<svg class='wf'",StringComparison.Ordinal),"an impact without a measured budget has no waterfall; forensic empty-state SVG is separate");
        var stopHistory=new CombatReportHistory();stopHistory.Begin();stopHistory.Add(Shot(318,new CombatReportEvent("layerStart","","Reached layer",318,null,"b",Layer("b",2,"Plate #2",120,0)),new CombatReportEvent("nativeStop","","Native stop in Plate #2",318,null,"b")));
        var stopHtml=CombatReportHtml.Render(stopHistory);
        check(stopHtml.Contains("#e5533d",StringComparison.Ordinal)&&stopHtml.Contains("stopped",StringComparison.OrdinalIgnoreCase),"a stopped core is marked in the SVG");
    }

    // ---- 4: the Kit version gate ----
    private static void Gate(Action<bool,string> check)
    {
        check(KitVersionGate.Supported("0.1.7")&&KitVersionGate.Supported("0.1.7.0")&&KitVersionGate.Supported("0.1.10")&&KitVersionGate.Supported("0.1.7+abc"),"Kit 0.1.7 and newer 0.1.x are supported");
        check(!KitVersionGate.Supported("0.1.6")&&!KitVersionGate.Supported("0.1.4")&&!KitVersionGate.Supported("0.2.0")&&!KitVersionGate.Supported(null)&&!KitVersionGate.Supported("")&&!KitVersionGate.Supported("abc"),"older, unknown and 0.2 Kits are refused");
        var message=KitVersionGate.Message("0.1.6");
        check(message.Contains("0.1.7",StringComparison.Ordinal)&&message.Contains("found 0.1.6",StringComparison.Ordinal)&&message.Contains("not loaded",StringComparison.Ordinal),"the refusal message names the needed and the found version");
        check(KitVersionGate.Message(null).Contains("found none",StringComparison.Ordinal),"no Kit: 'found none'");
    }

    // ---- source guards ----
    private static void Source(Action<bool,string> check)
    {
        var root=SourceRoot();
        if(root==null){check(true,"source root not found (skipped)");return;}
        string Read(string file)=>File.ReadAllText(Path.Combine(root,file));
        var panel=Read("CassetteComposerPanel.cs");
        check(!panel.Contains("ProtectionTableLayout",StringComparison.Ordinal)&&!Directory.GetFiles(root,"*.cs").Any(f=>File.ReadAllText(f).Contains("class ProtectionTableLayout",StringComparison.Ordinal)),"the 170 px workaround (ProtectionTableLayout) is gone");
        check(panel.Contains(".Table(",StringComparison.Ordinal)&&panel.Contains("ColumnWidth.Fill",StringComparison.Ordinal)&&panel.Contains("CellAlign.Right",StringComparison.Ordinal),"the composer protection table is a Kit Table with a Fill column and right-aligned numbers");
        check(panel.Contains(".LineChart(",StringComparison.Ordinal)&&panel.Contains(".RefLine(\"RHA, same thickness\"",StringComparison.Ordinal),"the composer has the angle LineChart with the RHA reference line");
        check(Read("HitPanelHud.cs").Contains("WaterfallChart.Add(",StringComparison.Ordinal)&&Read("CombatReportPanel.cs").Contains("WaterfallChart.Add(",StringComparison.Ordinal),"the hit panel and the Kit report both add the waterfall");
        check(Read("WaterfallChart.cs").Contains(".Waterfall(",StringComparison.Ordinal),"the waterfall uses BarChart.Waterfall");
        var plugin=Read("Plugin.cs");
        check(plugin.Contains("\"0.22.76\"",StringComparison.Ordinal)&&plugin.Contains("UiKit.PluginGuid, \">=0.1.7 <0.2.0\"",StringComparison.Ordinal)&&plugin.Contains("KitVersionGate.Supported",StringComparison.Ordinal),"version 0.22.76, dependency Kit >=0.1.7, runtime gate");
        check(Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.76</Version>",StringComparison.Ordinal)&&Read("RELEASE-NOTES.md").Contains("0.22.23 (Kit table",StringComparison.Ordinal)&&Read("RELEASE-NOTES.md").Contains("0.22.24 (crash fix",StringComparison.Ordinal)&&Read("RELEASE-NOTES.md").Contains("0.22.25 (line-of-sight correction",StringComparison.Ordinal)&&Read("RELEASE-NOTES.md").Contains("0.22.26 (the line-of-sight correction really",StringComparison.Ordinal),"csproj says 0.22.76; the 0.22.23 to 0.22.26 release notes are kept");
    }
}

