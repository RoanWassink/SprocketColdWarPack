using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.98: Photo Mode UI Kit Combat Report & Forensic, exact stopping sublayer localization,
/// PenetrationWaterfall label & folded-component double-count fixes, Forensic thick-path band override,
/// CombatReportView.PlateCell multi-line tags, and HTML TOC/Technical log polish.
/// </summary>
internal static class Feat02298ReportPolishTests
{
    private static string SourceRoot()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"SprocketExpandedBallistics.csproj")))return d.FullName;
        throw new DirectoryNotFoundException("Could not find source root containing SprocketExpandedBallistics.csproj");
    }

    private static string F(double v,string fmt="0.###")=>v.ToString(fmt,CultureInfo.InvariantCulture);

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;
        void Check(bool ok,string msg){checks++;if(!ok)failures++;sink(ok,"FEAT98: "+msg);}

        var root=SourceRoot();
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets.ToDictionary(p=>p.MaterialId,p=>p);
        CombatCompositeLayers.Configure(id=>id!=null&&presets.TryGetValue(id,out var found)?found:null);
        try
        {
            // ================= 1. Exact stopping sublayer on T-80BVM [Solid block] & Challenger 1 composite =================
            {
                const int box=350;
                const string stage="",hitId="sim:1:24:25",mat="ebCassette_t80u",matLabel="T-80U cellular cast armour";
                var piece=new PaidMaterialPiece(box,mat,matLabel,7.120699,7.481385,0.57693,28.189);
                var start=new CombatReportEvent("layerStart",stage,"Reached layer",171.408,null,hitId,
                    new CombatLayerInfo(hitId,new[]{new CombatLayerPart(box,CompositePlate.SolidBlockName,mat,matLabel,null,27.3,true)},false));
                var reaction=new CombatReportEvent("materialReaction",stage,"Confirmed material intervals: T-80U cellular cast armour [ebCassette_t80u, entity 350]: confirmed unpaid path 360.686 mm × 0.57693 = 208.091 mm RHA-eq; ray [7.120699, 7.481385] m; incidence 28.189°",171.408,0,hitId){PaidIntervals=new[]{piece}};
                var stop=new CombatReportEvent("nativeStop",stage,$"Native stop in {CompositePlate.SolidBlockName} [Material: {matLabel}]; no outgoing original core recorded.",171.408,0,hitId);
                var shot6=new CombatReportShot(6,"Outgoing","APFSDS gen 3",1357,new[]{start,reaction,stop},0,467.8,"T-80BVM",3,3);

                var located=LayeredPlateReport.LocateComposite(mat,new[]{piece},171.408);
                var forensic=CombatForensics.Build(shot6);
                var layer=forensic.Layers.Single();
                var stopSub=CombatForensics.StopSublayer(layer);

                Check(located!=null&&located.Groups==presets[mat].Layers.Length&&Math.Abs(located.Fraction-0.5)>0.05,
                    $"1 LocateComposite T-80BVM Shot #6: layer {located?.Group}/{located?.Groups} at fraction {F(located?.Fraction??0)} (not 0.5)");
                Check(layer.PlateStop==located&&stopSub!=null&&!stopSub.Air&&forensic.Explanation.Contains($"layer {located!.Group} of {located.Groups} of the plate ({stopSub.Label})",StringComparison.Ordinal),
                    $"1 Forensic explanation names exact stopping sublayer: {forensic.Explanation}");

                var svgMat=CombatForensicDrawing.Svg(forensic,false);
                var svgHeat=CombatForensicDrawing.Svg(forensic,true);
                Check(svgMat.Contains($"Arrested in layer {located!.Group} of {located.Groups} ({stopSub!.Label})",StringComparison.Ordinal)
                    &&svgHeat.Contains($"Arrested in layer {located.Group} of {located.Groups} ({stopSub.Label})",StringComparison.Ordinal),
                    $"1 Drawing SVG marks exact stopping sublayer in Material and Heat views");
                var svgDoc=XDocument.Parse(svgMat);
                var arrestX=svgDoc.Descendants().Where(x=>x.Name.LocalName=="text"&&x.Value.StartsWith("Arrested in layer",StringComparison.Ordinal))
                    .Select(x=>double.Parse(x.Attribute("x")!.Value,CultureInfo.InvariantCulture)).Single();
                Check(arrestX>5&&arrestX<CombatForensicDrawing.Width-5,$"1 Drawing SVG arrest label clamped inside [0, {CombatForensicDrawing.Width}]: x={F(arrestX)}");

                var htmlFrag=CombatForensicHtml.Render(forensic,"f6");
                var fxDoc=XDocument.Parse("<r>"+htmlFrag+"</r>");
                var subDivs=fxDoc.Descendants("div").Where(d=>(string?)d.Attribute("class")=="fx-row fx-sub").ToArray();
                Check(subDivs.Length==layer.Sublayers!.Count&&subDivs.Count(d=>(string?)d.Attribute("data-stop")=="1")==1
                    &&subDivs.Single(d=>(string?)d.Attribute("data-stop")=="1").Value.Contains("arrested here",StringComparison.Ordinal)
                    &&subDivs.All(d=>!d.Value.Contains('%')),
                    "1 HTML Forensic keeps class='fx-row fx-sub', highlights exactly 1 sublayer with data-stop='1' + 'arrested here', and contains no '%'");

                var g=CombatForensicGeometry.Build(forensic);
                var band=g.Bands.Single();
                float expectedEnd=band.EntryX+(float)layer.PlateStop!.Fraction*(band.ExitX-band.EntryX);
                Check(Math.Abs(expectedEnd-(band.EntryX+band.ExitX)/2)>5f,"1 Stopping mark X is separated from 50% band midpoint");

                // Language guard: zero jargon or uncertainty across visible Forensic texts
                var exempt=new[]{"T-80BVM","APFSDS gen 3",CompositePlate.SolidBlockName,matLabel,CombatReportLanguage.Material(matLabel,mat)}
                    .Concat(layer.Sublayers!.Select(s=>s.Label)).ToArray();
                var texts=new[]{forensic.Outcome,forensic.Explanation,forensic.Context,CombatForensicGeometry.Detail(layer)}
                    .Concat(layer.Sublayers!.Select(s=>s.Label+" | "+CombatForensicGeometry.SubDetail(layer,s)))
                    .Concat(forensic.Findings).Concat(forensic.Advice);
                Check(texts.All(t=>CombatReportLanguage.JargonIn(t,exempt).Count==0&&CombatReportLanguage.UncertaintyIn(t,exempt).Count==0),
                    "1 Forensic exact-stop texts contain 0 jargon and 0 uncertainty terms");

                // Standard composite cassette stop (Challenger 1 Shot #5 pattern: ebCassette_burlington)
                const int challyEntity=7167;
                const string chHit="sim:2:0:1",chMat="ebCassette_burlington",chLabel="Challenger 1 (Burlington)";
                var chPiece=new PaidMaterialPiece(challyEntity,chMat,chLabel,13.906347,14.845052,0.69253,49.079);
                var chStart=new CombatReportEvent("layerStart","","Reached layer",453.116,null,chHit,
                    new CombatLayerInfo(chHit,new[]{new CombatLayerPart(challyEntity,"Composite armour plate",chMat,chLabel,600,55.5,true)},false));
                var chReact=new CombatReportEvent("materialReaction","","Confirmed material intervals: Challenger 1 (Burlington) [ebCassette_burlington, entity 7167]: confirmed unpaid path 938.705 mm × 0.69253 = 650.084 mm RHA-eq; ray [13.906347, 14.845052] m; incidence 49.079°",453.116,0,chHit){PaidIntervals=new[]{chPiece}};
                var chStop=new CombatReportEvent("nativeStop","","Native stop in Composite armour plate [Material: Challenger 1 (Burlington)]; no outgoing original core recorded.",453.116,0,chHit);
                var chShot=new CombatReportShot(5,"Outgoing","APFSDS gen 2",1729,new[]{chStart,chReact,chStop},0,513.7,"Challenger 1",4,4);
                var chForensic=CombatForensics.Build(chShot);
                var chLayer=chForensic.Layers.Single();
                var chSub=CombatForensics.StopSublayer(chLayer);
                Check(chLayer.PlateStop!=null&&chSub!=null&&!chSub.Air&&chForensic.Explanation.Contains($"layer {chLayer.PlateStop.Group} of {chLayer.PlateStop.Groups} of the plate ({chSub.Label})",StringComparison.Ordinal),
                    $"1 Challenger 1 standard composite stop: layer {chLayer.PlateStop?.Group}/{chLayer.PlateStop?.Groups} ({chSub?.Label}) at {F((chLayer.PlateStop?.Fraction??0)*100)}%");
            }

            // ================= 2. Waterfall labels & folded hidden-component double-counting fix =================
            {
                // Reconstruct Shot #6 ERA -> hidden Relikt housing #294 (folded into visible cell #272) -> ERA #272 -> hidden housing #270 (folded into HHS #256) -> HHS #256 -> Solid block #350
                var ev=new CombatReportEvent[]
                {
                    new("layerStart","","Reached layer",450.283,null,"s:0:0:1",new CombatLayerInfo("s:0:0:1",new[]{new CombatLayerPart(313,"Relikt 2+1","reliktActive","ERA - Relikt hull",13,69.5,true)},false)),
                    new("heavyEra","","ERA",null,null,"s:0:0:1",RelatedEntity:313),
                    new("eraReaction","","ERA Relikt 2+1: APFSDS −29.4 %",450.283,317.924,"s:0:0:1",RelatedEntity:313),
                    new("materialReaction","","Confirmed material intervals: Relikt 2+1 #313 [native:5, entity 313]: confirmed unpaid path 37.127 mm × 0.15 = 5.569 mm RHA-eq; ray [6.622, 6.660] m; incidence 69.503°",317.924,313.527,"s:0:0:1"){PaidIntervals=new[]{new PaidMaterialPiece(313,"native:5","Relikt 2+1 #313",6.62246,6.659587,0.15,69.503)}},
                    new("componentResidual","","Traversed Relikt 2+1",450.283,313.527,"s:0:0:1"),

                    // Hidden housing #294 (Armour=false) ahead of ERA #272
                    new("layerStart","","Reached layer",313.527,null,"s:0:1:2",new CombatLayerInfo("s:0:1:2",new[]{new CombatLayerPart(294,"Relikt 2+1",null,null,null,60.472,false)},false)),
                    new("materialReaction","","Confirmed material intervals: Relikt 2+1 #294 [native:4, entity 294]: confirmed unpaid path 4.058 mm × 1 = 4.058 mm RHA-eq; ray [6.687, 6.691] m; incidence 60.472°",313.527,310.228,"s:0:1:2"){PaidIntervals=new[]{new PaidMaterialPiece(294,"native:4","Relikt 2+1 #294",6.686882,6.690939,1.0,60.472)}},
                    new("componentResidual","","Traversed Relikt 2+1",313.527,310.228,"s:0:1:2"),

                    new("layerStart","","Reached layer",310.228,null,"s:0:2:3",new CombatLayerInfo("s:0:2:3",new[]{new CombatLayerPart(272,"Relikt 2+1","reliktActive","ERA - Relikt hull",13,50.159,true)},false)),
                    new("heavyEra","","ERA",null,null,"s:0:2:3",RelatedEntity:272),
                    new("eraReaction","","ERA Relikt 2+1: APFSDS −20.9 %",310.228,245.322,"s:0:2:3",RelatedEntity:272),
                    new("materialReaction","","Confirmed material intervals: Relikt 2+1 #272 [native:5, entity 272]: confirmed unpaid path 3.21 mm × 0.15 = 0.482 mm RHA-eq; ray [6.770, 6.774] m; incidence 50.159°",245.322,244.884,"s:0:2:3"){PaidIntervals=new[]{new PaidMaterialPiece(272,"native:5","Relikt 2+1 #272",6.770436,6.773646,0.15,50.159)}},
                    new("componentResidual","","Traversed Relikt 2+1",310.228,244.884,"s:0:2:3"),

                    // Hidden housing #270 (Armour=false) ahead of HHS #256
                    new("layerStart","","Reached layer",244.884,null,"s:0:3:4",new CombatLayerInfo("s:0:3:4",new[]{new CombatLayerPart(270,"Relikt 2+1",null,null,null,67.835,false)},false)),
                    new("materialReaction","","Confirmed material intervals: Relikt 2+1 #270 [native:4, entity 270]: confirmed unpaid path 5.301 mm × 1 = 5.301 mm RHA-eq; ray [6.797, 6.803] m; incidence 67.835°",244.884,240.650,"s:0:3:4"){PaidIntervals=new[]{new PaidMaterialPiece(270,"native:4","Relikt 2+1 #270",6.797324,6.802625,1.0,67.835)}},
                    new("componentResidual","","Traversed Relikt 2+1",244.884,240.650,"s:0:3:4"),

                    new("layerStart","","Reached layer",240.650,null,"s:0:4:5",new CombatLayerInfo("s:0:4:5",new[]{new CombatLayerPart(256,"Plate #256","highHardnessSteel","High Hardness Steel",5,32.3,true)},false)),
                    new("materialReaction","","Confirmed material intervals: High hardness steel [highHardnessSteel, entity 256]: confirmed unpaid path 77.831 mm × 1.16 = 90.284 mm RHA-eq; ray [6.881, 6.959] m; incidence 19.24°",240.650,171.408,"s:0:4:5"){PaidIntervals=new[]{new PaidMaterialPiece(256,"highHardnessSteel","highHardnessSteel",6.881252,6.959083,1.16,19.24)}},
                    new("plateResidual","","Traversed Plate #256",240.650,171.408,"s:0:4:5"),

                    new("layerStart","","Reached layer",171.408,null,"s:0:5:6",new CombatLayerInfo("s:0:5:6",new[]{new CombatLayerPart(350,CompositePlate.SolidBlockName,"ebCassette_t80u","T-80U cellular cast armour",null,27.3,true)},false)),
                    new("materialReaction","","Confirmed material intervals: T-80U cellular cast armour [ebCassette_t80u, entity 350]: confirmed unpaid path 360.686 mm × 0.57693 = 208.091 mm RHA-eq; ray [7.121, 7.481] m; incidence 28.189°",171.408,0,"s:0:5:6"){PaidIntervals=new[]{new PaidMaterialPiece(350,"ebCassette_t80u","T-80U cellular cast armour",7.120699,7.481385,0.57693,28.189)}},
                    new("nativeStop","",$"Native stop in {CompositePlate.SolidBlockName}",171.408,0,"s:0:5:6")
                };
                var shot=new CombatReportShot(6,"Outgoing","APFSDS gen 3",1357,ev,0,450.283,"T-80BVM",3,3);
                var norm=CombatReportTruth.Normalize(shot);
                var wf=PenetrationWaterfall.Build(norm)!;
                var wfText=PenetrationWaterfall.Text(wf);
                var wfSvg=PenetrationWaterfall.Svg(wf);
                double sum=wf.Steps.Sum(s=>s.Delta);
                Check(Math.Abs(wf.Start+sum-wf.End)<1e-9&&wf.Stopped&&wf.End==0,$"2 Waterfall closes on 0 (Start+Sum={F(wf.Start+sum,"0.######")})");
                Check(!wfText.Contains("Confirmed mat",StringComparison.Ordinal)&&!wfSvg.Contains("Confirmed mat",StringComparison.Ordinal)&&wf.Steps.All(s=>!s.Label.StartsWith("Confirmed mat",StringComparison.Ordinal)),
                    $"2 Waterfall contains zero 'Confirmed mat…' labels: {wfText}");
                var eraSteps=wf.Steps.Where(s=>s.Label=="ERA").ToArray();
                Check(eraSteps.Length==2&&Math.Abs(eraSteps[1].Delta+(310.228-245.322))<0.01,
                    $"2 Folded Relikt housing #294 not double-counted; second ERA step keeps full {F(-(310.228-245.322))} mm (got {F(eraSteps.ElementAtOrDefault(1)?.Delta??0)})");
                var hhsStep=wf.Steps.FirstOrDefault(s=>s.Label.Contains("High-hardness",StringComparison.Ordinal));
                Check(hhsStep!=null&&Math.Abs(hhsStep.Delta+(240.650-171.408))<0.01,
                    $"2 Folded Relikt housing #270 not double-counted; HHS step keeps full {F(-(240.650-171.408))} mm (got {F(hhsStep?.Delta??0)})");
                Check(wf.Steps.Any(s=>s.Label.StartsWith("T-80U",StringComparison.Ordinal))&&wf.Steps.All(s=>CombatReportLanguage.JargonIn(s.Tooltip,new[]{"T-80U cellular cast armour","Relikt 2+1","Plate #256"}).Count==0),
                    "2 Waterfall labels name real materials and tooltips contain 0 jargon");
            }

            // ================= 3. Forensic thick-path band override (Plate #256) vs normal plate =================
            {
                const int hhs=256;
                var piece=new PaidMaterialPiece(hhs,"highHardnessSteel","highHardnessSteel",6.881252,6.959083,1.16,19.24);
                var sHhs=new CombatReportEvent("layerStart","","Reached layer",240.65,null,"s:1:0:1",new CombatLayerInfo("s:1:0:1",new[]{new CombatLayerPart(hhs,"Plate #256","highHardnessSteel","High Hardness Steel",5,32.3,true)},false));
                var rHhs=new CombatReportEvent("materialReaction","","Confirmed material intervals: High hardness steel",240.65,171.408,"s:1:0:1"){PaidIntervals=new[]{piece}};
                var pHhs=new CombatReportEvent("plateResidual","","Traversed Plate #256",240.65,171.408,"s:1:0:1");
                var shotHhs=new CombatReportShot(1,"Outgoing","APFSDS",1357,new[]{sHhs,rHhs,pHhs},0,240.65);
                var fHhs=CombatForensics.Build(shotHhs);
                var lHhs=fHhs.Layers.Single();
                Check(lHhs.PathFromIntervals&&lHhs.PathMm is {} lp&&Math.Abs(lp-77.831)<0.001
                    &&CombatForensicGeometry.Detail(lHhs).Contains("77.8 mm along the shot line",StringComparison.Ordinal)
                    &&lHhs.Parts[0].ThicknessMm==5,
                    $"3 Thick-path override uses 77.831 mm for Forensic band while table ThicknessMm stays 5 ({CombatForensicGeometry.Detail(lHhs)})");

                // Normal plate where paid interval matches face path: no override
                var normPiece=new PaidMaterialPiece(1,"rha","rha",1.0,1.0052,1.0,15.0);
                var sNorm=new CombatReportEvent("layerStart","","Reached layer",100,null,"s:1:0:1",new CombatLayerInfo("s:1:0:1",new[]{new CombatLayerPart(1,"Plate #1","rha","Rolled Homogeneous Armour",5,15,true)},false));
                var rNorm=new CombatReportEvent("materialReaction","","Confirmed material intervals",100,94,"s:1:0:1"){PaidIntervals=new[]{normPiece}};
                var lNorm=CombatForensics.Build(new CombatReportShot(2,"Outgoing","APFSDS",1357,new[]{sNorm,rNorm},0,100)).Layers.Single();
                Check(!lNorm.PathFromIntervals&&lNorm.PathMm is {} np&&Math.Abs(np-5/Math.Cos(15*Math.PI/180))<1e-6,
                    $"3 Normal plate (5.2 mm paid vs 5.18 mm face) keeps face path without PathFromIntervals override");
            }

            // ================= 4. CombatReportView.PlateCell multi-line tags =================
            {
                string RenderPlateCell(string plateName,double? thickness,bool secondCrossing=false,CombatLayerPart[]? extra=null)
                {
                    var parts=new List<CombatLayerPart>{new(350,plateName,"ebCassette_t80u","T-80U",thickness,25,true)};
                    if(extra!=null)parts.AddRange(extra);
                    var events=new List<CombatReportEvent>();
                    if(secondCrossing)
                    {
                        events.Add(new("layerStart","","Reached layer",450,null,"a:0:0:1",new CombatLayerInfo("a:0:0:1",parts.ToArray(),false)));
                        events.Add(new("plateResidual","","Traversed",450,400,"a:0:0:1"));
                        events.Add(new("layerStart","","Reached layer",400,null,"b:0:1:2",new CombatLayerInfo("b:0:1:2",new[]{new CombatLayerPart(99,"Plate #99","rha","RHA",20,0,true)},false)));
                        events.Add(new("plateResidual","","Traversed",400,380,"b:0:1:2"));
                    }
                    var hitId=secondCrossing?"c:0:2:3":"a:0:0:1";
                    events.Add(new("layerStart","","Reached layer",380,null,hitId,new CombatLayerInfo(hitId,parts.ToArray(),false)));
                    events.Add(new("materialReaction","","Paid",380,200,hitId){PaidIntervals=new[]{new PaidMaterialPiece(350,"ebCassette_t80u","T-80U",1.0,1.2,0.5,25)}});
                    events.Add(new("plateResidual","","Traversed",380,200,hitId));
                    var shot=new CombatReportShot(1,"Outgoing","APFSDS",1500,events,0,450);
                    var rows=CombatReportView.Build(new[]{shot}).Single().Layers;
                    return secondCrossing?rows[2].Name:rows[0].Name;
                }

                var solidDerived=RenderPlateCell(CompositePlate.SolidBlockName,null);
                Check(solidDerived=="Composite armour plate\n[Solid block]"&&!solidDerived.Contains('…'),
                    $"4 Solid block derived cell wraps cleanly: {solidDerived.Replace("\n"," / ")}");

                var solidCross=RenderPlateCell(CompositePlate.SolidBlockName,120,secondCrossing:true);
                Check(solidCross=="Composite armour plate\n[Solid block]\n(second crossing)",
                    $"4 Solid block second crossing wraps cleanly: {solidCross.Replace("\n"," / ")}");

                var plainDerived=RenderPlateCell("Composite armour plate",null);
                Check(plainDerived=="Composite armour plate\n(from path)",
                    $"4 Derived cell wraps cleanly: {plainDerived.Replace("\n"," / ")}");

                var arrCell=RenderPlateCell("Plate #1",50,extra:new[]{new CombatLayerPart(351,"Plate #2","rha","RHA",50,25,true)});
                Check(arrCell=="Composite array\n(2 plates)",
                    $"4 Composite array wraps cleanly: {arrCell.Replace("\n"," / ")}");
            }

            // ================= 5. CombatReportHtml TOC & null-thickness Technical log fix =================
            {
                CombatReportShot MakeShot(int num)
                {
                    var layer=new CombatLayerInfo($"s:{num}:0:1",new[]{new CombatLayerPart(10+num,CompositePlate.SolidBlockName,"ebCassette_t80u","T-80U cellular cast armour",null,null,true)},false);
                    return new CombatReportShot(num,"Outgoing","APFSDS",1500,new[]
                    {
                        new CombatReportEvent("layerStart","","Reached layer",400,null,$"s:{num}:0:1",layer),
                        new CombatReportEvent("plateResidual","","Traversed",400,300,$"s:{num}:0:1")
                    },0,400,"T-80BVM",3,3);
                }

                var h2=new CombatReportHistory();h2.Restore(new[]{MakeShot(1),MakeShot(2)},0);
                var html2=CombatReportHtml.Render(h2);
                Check(!html2.Contains("<nav class='toc'>",StringComparison.Ordinal)&&!html2.Contains(";  mm;",StringComparison.Ordinal)&&!html2.Contains(";  degrees",StringComparison.Ordinal)
                    &&html2.Contains("; —; —",StringComparison.Ordinal),
                    "5 Two-shot HTML omits <nav class='toc'> and renders missing thickness/angle as '—' in Technical log");
                _ = XDocument.Parse(html2);

                var h3=new CombatReportHistory();h3.Restore(new[]{MakeShot(1),MakeShot(2),MakeShot(3)},0);
                var html3=CombatReportHtml.Render(h3);
                Check(html3.Contains("<nav class='toc'>",StringComparison.Ordinal)&&html3.Contains("<h2 id='impact-3'",StringComparison.Ordinal)&&html3.Contains("href='#impact-3'",StringComparison.Ordinal),
                    "5 Three-shot HTML includes <nav class='toc'> jump links matching section <h2 id='impact-...'>");
                _ = XDocument.Parse(html3);
            }

            // ================= 6. Source invariants (Photo Mode, Forensic nav, 0 new hooks, guarded files) =================
            {
                string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
                var reportPanel=Read("CombatReportPanel.cs");
                var forensicPanel=Read("CombatForensicPanel.cs");
                var menu=Read("CombatReportMenu.cs");
                Check(reportPanel.Contains("internal static bool IsOpen=>",StringComparison.Ordinal)
                    &&reportPanel.Contains("p.AllowedContexts=UiContexts.All;",StringComparison.Ordinal)
                    &&reportPanel.Contains("p.StayOpenWhilePaused=true;",StringComparison.Ordinal)
                    &&reportPanel.Contains("CombatForensicPanel.Open(forensic,Current())",StringComparison.Ordinal),
                    "6 CombatReportPanel supports UiContexts.All + StayOpenWhilePaused + IsOpen + passes Current() to Forensic");
                Check(forensicPanel.Contains("internal static bool IsOpen=>",StringComparison.Ordinal)
                    &&forensicPanel.Contains("p.AllowedContexts=UiContexts.All;",StringComparison.Ordinal)
                    &&forensicPanel.Contains("p.StayOpenWhilePaused=true;",StringComparison.Ordinal)
                    &&forensicPanel.Contains("\"Newer impact\"",StringComparison.Ordinal)
                    &&forensicPanel.Contains("\"Older impact\"",StringComparison.Ordinal),
                    "6 CombatForensicPanel supports UiContexts.All + StayOpenWhilePaused + IsOpen + Newer/Older impact navigation");
                Check(menu.Contains("UiKit.Context==UiContexts.Other",StringComparison.Ordinal)
                    &&menu.Contains("Cursor.visible=true;Cursor.lockState=CursorLockMode.None;",StringComparison.Ordinal),
                    "6 CombatReportMenu manages cursor in UiContexts.Other (Photo Mode)");

                int hooks=Directory.EnumerateFiles(root,"*.cs",SearchOption.AllDirectories)
                    .Where(f=>{var r=f.Replace('\\','/');return !r.Contains("/tests/")&&!r.Contains("/bin/")&&!r.Contains("/obj/");})
                    .Sum(f=>File.ReadLines(f).Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal)));
                Check(hooks==48,$"6 Non-test [HarmonyPatch line count stays 48 (actual={hooks})");

                Check(Read("Plugin.cs").Contains("\"0.22.102\"",StringComparison.Ordinal)
                    &&Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.102</Version>",StringComparison.Ordinal)
                    &&Read("RELEASE-NOTES.md").Contains("## 0.22.102",StringComparison.Ordinal),
                    "6 Plugin.cs, csproj, and RELEASE-NOTES.md carry version 0.22.102");
            }

            // ================= 7. Live saved session smoke test (read-only) =================
            {
                const string livePath=@"E:\SteamLibrary\steamapps\common\Sprocket\BepInEx\config\sprocket.expandedballistics.combat-report\last-session.json";
                if(File.Exists(livePath))
                {
                    using var doc=JsonDocument.Parse(File.ReadAllText(livePath));
                    var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                    var hist=new CombatReportHistory();hist.Restore(shots,doc.RootElement.GetProperty("omittedShots").GetInt32());
                    var html=CombatReportHtml.Render(hist);
                    var htmlDoc=XDocument.Parse(html);

                    var f6=CombatForensics.Build(shots.Single(s=>s.Number==6));
                    var l6=f6.Layers.Last(l=>l.GapMm==null);
                    var s6=CombatForensics.StopSublayer(l6);

                    var f5=CombatForensics.Build(shots.Single(s=>s.Number==5));
                    var l5=f5.Layers.Last(l=>l.GapMm==null);
                    var s5=CombatForensics.StopSublayer(l5);

                    bool noConfirmedMatSvg=!htmlDoc.Descendants("svg").Any(svg=>svg.ToString().Contains("Confirmed mat",StringComparison.Ordinal));
                    bool noBlankMm=!html.Contains(";  mm;",StringComparison.Ordinal);
                    Check(shots.Length==6&&noConfirmedMatSvg&&noBlankMm&&html.Contains("<nav class='toc'>",StringComparison.Ordinal),
                        $"7 Live last-session.json rendered cleanly to valid XML HTML ({shots.Length} shots, 0 'Confirmed mat…' SVGs, 0 ';  mm;')");
                    Check(l6.PlateStop!=null&&s6!=null&&l5.PlateStop!=null&&s5!=null,
                        $"7 Live Shot #6 stopped at layer {l6.PlateStop?.Group}/{l6.PlateStop?.Groups} ({s6?.Label}, {F((l6.PlateStop?.Fraction??0)*100)}%); Shot #5 stopped at layer {l5.PlateStop?.Group}/{l5.PlateStop?.Groups} ({s5?.Label}, {F((l5.PlateStop?.Fraction??0)*100)}%)");
                }
                else
                {
                    Console.WriteLine("FEAT98: [info] live last-session.json not present; section 7 skipped.");
                }
            }
        }
        finally{CombatCompositeLayers.Configure(null);}

        Console.WriteLine($"FEAT98: {checks} checks, {failures} failures");
    }
}
