using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.91 (brief FEAT-0.22.91-FORENSIC-MODULE-LAYERS, user 16-11: "ik zou bij dat soort platen als Varma ook de laagjes in de forensic willen zien. hoeft allemaal niet echt
/// gesimuleerd te worden"). Fixture: the user's TOW shot #10 on the VARMA module of the Bradley M3A3 (tests\fixtures\sessions\varma-tow-1116, read only).
/// A: the module is one layer with its three confirmed intervals as sublayers; B: engine, fuel tank and track are not named "ERA"; C: the module's reduction is booked on the
/// module, not "Between layers". Bit-equality: the 0.22.90 test build (environment variable EB_BASE_TESTS; the delivery used out-0.22.91\base) is loaded side by side for the Solid fixture of 0.22.81 and the
/// derived thicknesses of 0.22.88 (T-72A). Writes FEAT91-RESULT.json, MODULE-LAYERS-CASES.tsv and preview\module-layers-panels.json to test-output/.
/// </summary>
internal static class Feat02291ModuleLayersTests
{
    private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    /// <summary>The 0.22.90 build of the same pure report code, loaded by reflection (its own statics and preset lookup).</summary>
    private sealed class Old90
    {
        private readonly Assembly a;
        private readonly JsonSerializerOptions opts=new(){PropertyNameCaseInsensitive=true};
        internal Old90(string dll)
        {
            var dir=Path.GetDirectoryName(dll)!;var alc=new AssemblyLoadContext("eb-0.22.90-report",false);
            alc.Resolving+=(c,n)=>{var p=Path.Combine(dir,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
            a=alc.LoadFromAssemblyPath(dll);
            var catalogue=T("CassetteCatalogue");
            var parsed=catalogue.GetMethod("Parse",F,new[]{typeof(string)})!.Invoke(null,new object[]{File.ReadAllText(Path.Combine(dir,(string)catalogue.GetField("FileName",F)!.GetValue(null)!))})!;
            var presets=(System.Collections.IEnumerable)parsed.GetType().GetProperty("Presets",F)!.GetValue(parsed)!;var presetType=T("CassettePreset");
            var dict=(System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),presetType))!;
            foreach(var p in presets)dict[(string)presetType.GetProperty("MaterialId",F)!.GetValue(p)!]=p;
            var lookup=typeof(Feat02291ModuleLayersTests).GetMethod(nameof(Lookup),F)!.MakeGenericMethod(presetType).Invoke(null,new object[]{dict})!;
            T("CombatCompositeLayers").GetMethod("Configure",F)!.Invoke(null,new object?[]{lookup,null});
        }
        internal Type T(string n)=>a.GetType("SprocketExpandedBallistics."+n,true)!;
        internal Array Shots(string json){using var d=JsonDocument.Parse(json);return (Array)JsonSerializer.Deserialize(d.RootElement.GetProperty("shots").GetRawText(),T("CombatReportShot").MakeArrayType(),opts)!;}
        internal static int Number(object shot)=>(int)shot.GetType().GetProperty("Number",F)!.GetValue(shot)!;
        internal object Model(object shot)=>T("CombatForensics").GetMethod("Build",F)!.Invoke(null,new[]{shot})!;
        internal static string Json(object model)=>JsonSerializer.Serialize(model,model.GetType());
        internal string Svg(object model,bool heat)=>(string)T("CombatForensicDrawing").GetMethod("Svg",F)!.Invoke(null,new object?[]{model,heat,null})!;
        internal string Html(Array shots,int omitted){var h=Activator.CreateInstance(T("CombatReportHistory"),true)!;T("CombatReportHistory").GetMethod("Restore",F)!.Invoke(h,new object[]{shots,omitted});return (string)T("CombatReportHtml").GetMethod("Render",F)!.Invoke(null,new[]{h})!;}
        internal object Normal(object shot)=>T("CombatReportTruth").GetMethod("Normalize",F,new[]{T("CombatReportShot")})!.Invoke(null,new[]{shot})!;
        internal string[] Summaries(object shot)
        {
            var hits=(Array)T("CombatHitProjection").GetMethod("Build",F)!.Invoke(null,new[]{Normal(shot)})!;
            return Enumerable.Range(0,hits.Length).Select(i=>{var h=hits.GetValue(i)!;return (string)h.GetType().GetMethod("Summary",F)!.Invoke(h,new object[]{i+1})!;}).ToArray();
        }
        internal string Waterfall(object shot)
        {
            var wf=T("PenetrationWaterfall").GetMethod("Build",F,new[]{T("CombatReportShot")})!.Invoke(null,new[]{Normal(shot)});
            return wf==null?"":(string)T("PenetrationWaterfall").GetMethod("Text",F)!.Invoke(null,new[]{wf})!;
        }
        internal object Panel(string title,object model)=>a.GetType("SprocketExpandedBallistics.Feat02281SolidForensicTests",true)!.GetMethod("Panel",F)!.Invoke(null,new object[]{title,model})!;
        internal string LayerName(object layer)=>(string)T("CombatForensicGeometry").GetMethod("LayerName",F)!.Invoke(null,new[]{layer})!;
        internal string Detail(object layer)=>(string)T("CombatForensicGeometry").GetMethod("Detail",F)!.Invoke(null,new[]{layer})!;
        internal string SubDetail(object sub)=>(string)T("CombatForensicGeometry").GetMethod("SubDetail",F)!.Invoke(null,new[]{sub})!;
    }
    private static Func<string,T?> Lookup<T>(Dictionary<string,T> d) where T:class=>id=>id!=null&&d.TryGetValue(id,out var p)?p:null;
    private static string N(double? v,string f="0.###")=>v is {} x?x.ToString(f,CultureInfo.InvariantCulture):"null";
    private static object? P(object o,string name)=>o.GetType().GetProperty(name,F)!.GetValue(o);
    private static string[] Summaries(CombatReportShot shot){var hits=CombatHitProjection.Build(CombatReportTruth.Normalize(shot));return hits.Select((h,i)=>h.Summary(i+1)).ToArray();}
    private static string Html(IEnumerable<CombatReportShot> shots,int omitted){var h=new CombatReportHistory();h.Restore(shots,omitted);return CombatReportHtml.Render(h);}
    private static double Between(string seq)=>System.Text.RegularExpressions.Regex.Matches(seq,@"Between layers (-?[\d.]+)").Sum(m=>Math.Abs(double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)));
    private static string Ends(string seq){var a=seq.IndexOf(" mm",StringComparison.Ordinal);var b=seq.LastIndexOf(" = ",StringComparison.Ordinal);return (a<0?"":seq[..a])+"|"+(b<0?"":seq[b..]);}
    private static string Waterfall(CombatReportShot shot)=>PenetrationWaterfall.Build(CombatReportTruth.Normalize(shot)) is {} wf?PenetrationWaterfall.Text(wf):"";

    /// <summary>The session JSON with every part relabelled as 0.22.91 names it (B) and nothing else changed: the input for the 0.22.90 build in the bit-equality checks.</summary>
    internal static string Relabelled(string sessionJson,out int changed)
    {
        var root=JsonNode.Parse(sessionJson)!;var opts=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};changed=0;
        foreach(var shot in root["shots"]!.AsArray())
            foreach(var e in shot!["Events"]!.AsArray())
            {
                if((string?)e!["Kind"]!="layerStart"||e["Layer"] is not JsonObject layer)continue;
                foreach(var p in layer["Parts"]!.AsArray())
                {
                    var part=p.Deserialize<CombatLayerPart>(opts)!;
                    if(CombatModuleLayers.Label(part,(string?)e["Stage"]??"") is {} label){p!["MaterialLabel"]=label;changed++;}
                }
            }
        return root.ToJsonString();
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT91: "+message);}
        string output=Fix02228Tests.OutputRoot(),sessions=Fix02228Tests.SessionRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets.ToDictionary(p=>p.MaterialId,p=>p);
        CombatCompositeLayers.Configure(id=>id!=null&&presets.TryGetValue(id,out var found)?found:null);
        var cases=new StringBuilder("item\t0.22.90\t0.22.91\tnote\n");var panels=new List<object>();
        var opts=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        try
        {
            var fixture=Path.Combine(sessions,"varma-tow-1116","last-session-20261116-varma-tow.json");var fixtureText=File.ReadAllText(fixture);
            using var doc=JsonDocument.Parse(fixtureText);int omitted=doc.RootElement.GetProperty("omittedShots").GetInt32();
            var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),opts)!;
            var storedBefore=shots.Select(s=>JsonSerializer.Serialize(s)).ToArray();
            var s10=shots.Single(s=>s.Number==10);var n10=CombatReportTruth.Normalize(s10);var applied=LayeredPlateReport.Apply(n10);
            const int housing=447,core=452,engine=161,fuel=482,track=171;

            // ================= A. one module with three sublayers =================
            var modules=applied.Events.Where(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Armour&&p.MaterialLabel==CombatModuleLayers.ModuleLabel)).ToArray();
            Check(modules.Length==1&&modules[0].Layer!.Parts.Count==1,$"shot 10: one module layer after Apply ({modules.Length}), one part");
            var mp=modules[0].Layer!.Parts[0];
            Check(mp.Entity==housing&&mp.Name=="VARMA-style passive side armour"+LayeredPlateReport.DerivedLabel&&mp.ThicknessMm is {} t&&Math.Abs(t-241.332*Math.Cos(41.767*Math.PI/180))<.05&&Math.Abs(mp.AngleDegrees!.Value-41.767)<.001,
                $"shot 10: the module part: '{mp.Name}', {N(mp.ThicknessMm,"0.##")} mm normal (241.33 mm path × cos 41.77°), {N(mp.AngleDegrees,"0.###")}°");
            var m=CombatForensics.Build(s10);var withLayers=m.Layers.Where(l=>l.ModuleLayers!=null).ToArray();
            Check(withLayers.Length==1&&withLayers[0].ModuleLayers!.Count==3,$"shot 10 Forensic: 1 module with 3 sublayers ({withLayers.Length} module(s), {withLayers.FirstOrDefault()?.ModuleLayers?.Count??0} sublayers)");
            var ml=withLayers[0];var sub=ml.ModuleLayers!;
            foreach(var x in sub)cases.AppendLine($"shot 10 sublayer {x.Order}\t-\t{x.Label}: {x.Detail}\tentity {x.Entity}");
            Check(sub[0].Entity==housing&&sub[0].Material=="steel"&&Math.Abs(sub[0].PathMm-2.682)<.002&&Math.Abs(sub[0].NormalMm-2.0)<.01&&sub[0].Factor==1&&Math.Abs(sub[0].RhaMm-2.682)<.002&&sub[0].Name.EndsWith(", housing",StringComparison.Ordinal),
                $"sublayer 1: {sub[0].Label}, {N(sub[0].PathMm)} mm path, {N(sub[0].NormalMm)} mm normal, × {N(sub[0].Factor)} = {N(sub[0].RhaMm)} mm RHA-eq (expected steel 2.7 mm path, ≈ 2.0 mm normal)");
            Check(sub[1].Entity==core&&sub[1].Material=="2500 kg/m³"&&Math.Abs(sub[1].PathMm-235.969)<.002&&Math.Abs(sub[1].Factor-.4)<1e-6&&Math.Abs(sub[1].RhaMm-94.388)<.002&&Math.Round(sub[1].RhaMm)==94&&sub[1].Name.EndsWith(", core",StringComparison.Ordinal),
                $"sublayer 2: {sub[1].Label}, {N(sub[1].PathMm)} mm × {N(sub[1].Factor)} = {N(sub[1].RhaMm)} mm RHA-eq (expected core 236 mm × 0.4 = 94 mm RHA-eq)");
            Check(sub[2].Entity==housing&&sub[2].Material=="steel"&&Math.Abs(sub[2].PathMm-2.681)<.002&&Math.Abs(sub[2].NormalMm-2.0)<.01,$"sublayer 3: {sub[2].Label}, {N(sub[2].PathMm)} mm path, {N(sub[2].NormalMm)} mm normal (expected steel 2.7 mm)");
            Check(sub.All(x=>x.Detail.Contains(" mm normal"+LayeredPlateReport.DerivedLabel,StringComparison.Ordinal)&&x.Detail.Contains("(derived)",StringComparison.Ordinal)),"every sublayer labels its normal thickness \"(derived from the path)\" and its share of the reduction \"(derived)\"");
            // penetration in -> out: the measured value; the division over the sublayers is derived and adds up to it
            Check(ml.Before is {} b&&Math.Abs(b-497.368)<.01&&ml.After is {} a&&Math.Abs(a-423.319)<.01&&ml.AbsorbedMm is {} drop&&Math.Abs(drop-74.05)<.01,$"the module row: {N(ml.Before,"0.#")} -> {N(ml.After,"0.#")} mm (measured, expected 497.4 -> 423.3), reduction {N(ml.AbsorbedMm,"0.##")} mm");
            Check(Math.Abs(sub.Sum(x=>x.ReductionMm!.Value)-ml.AbsorbedMm!.Value)<1e-9&&Math.Abs(sub[1].ReductionMm!.Value-ml.AbsorbedMm.Value*94.388/99.751)<.01,
                $"the reduction per sublayer in proportion to its RHA-eq: {string.Join(" + ",sub.Select(x=>N(x.ReductionMm,"0.##")))} = {N(ml.AbsorbedMm,"0.##")} mm (the module total stays the measured value)");
            Check(ml.ModuleIntervalsMm is {} sum&&Math.Abs(sum-99.751)<.01&&CombatForensicGeometry.Detail(ml).Contains("measured 74 mm RHA-eq, confirmed intervals 99.8 mm RHA-eq",StringComparison.Ordinal),
                $"the difference with the sum of the intervals is shown: '{CombatForensicGeometry.Detail(ml)}'");
            var finding=m.Findings.FirstOrDefault(f=>f.StartsWith($"Layer {ml.Number} (",StringComparison.Ordinal)&&f.Contains("constituent layers along the shot line",StringComparison.Ordinal))??"";
            Check(finding.Contains("measured reduction 74 mm RHA-eq (497.4 → 423.3)",StringComparison.Ordinal)&&finding.Contains("add up to 99.8 mm RHA-eq, 25.7 mm more than measured",StringComparison.Ordinal)&&finding.Contains("derived",StringComparison.Ordinal),"the finding: '"+finding+"'");
            Check(CombatReportLanguage.JargonIn(finding).Count==0&&CombatReportLanguage.JargonIn(string.Join(" ",sub.Select(x=>x.Label+" "+x.Detail))).Count==0,"no game jargon in the module texts ("+string.Join(", ",CombatReportLanguage.JargonIn(finding+" "+string.Join(" ",sub.Select(x=>x.Label+" "+x.Detail))))+")");
            // the "uncovered" groups that were already paid belong to the module: no layer, no row of their own
            string[] uncovered={"1906716371456:1:14:12","1906716371456:2:11:10"};
            var rows=CombatHitProjection.Build(n10);
            Check(!m.Layers.Any(l=>uncovered.Contains(l.HitId))&&!rows.Any(r=>uncovered.Contains(r.Id))&&!applied.Events.Any(e=>e.Kind=="layerStart"&&uncovered.Contains(e.HitId)),"the two already-paid groups (0 mm RHA-eq) are part of the module, not layers or rows of their own");
            // the layer table is not empty
            var view=CombatReportView.Build(new[]{s10})[0];
            var moduleRow=view.Layers.FirstOrDefault(r=>r.Material==CombatModuleLayers.ModuleLabel);
            Check(view.Layers.Length>0&&moduleRow!=null&&moduleRow.BeforeAfter=="497 → 423"&&moduleRow.Thickness=="180 mm",$"the layer table: {view.Layers.Length} row(s); module row '{moduleRow?.Name.Replace("\n"," ")} | {moduleRow?.Material} | {moduleRow?.Thickness} | {moduleRow?.Angle} | {moduleRow?.BeforeAfter} | {moduleRow?.Reaction.Replace("\n"," / ")}'");
            cases.AppendLine($"shot 10 layer table\t0 rows (\"{CombatReportView.NoLayersText}\")\t{string.Join(" || ",view.Layers.Select(r=>$"{r.Index} {r.Name.Replace("\n"," ")} | {r.Material} | {r.Thickness} | {r.Angle} | {r.BeforeAfter} | {r.Reaction.Replace("\n"," / ")}"))}\t");
            // cross-section and heat-map: the module is a band with its sublayers in it
            var g=CombatForensicGeometry.Build(m);var band=g.Bands.Single(x=>x.Layer.Number==ml.Number);
            Check(band.Polygon.Length>0&&band.Subs.Count==3&&ml.Sublayers is {Count:3} drawn&&Math.Abs(drawn.Sum(x=>x.Fraction)-1)<1e-9,$"the cross-section draws the module as a band with 3 sublayers ({band.Subs.Count}), fractions {string.Join(" / ",ml.Sublayers!.Select(x=>N(x.Fraction,"0.####")))}");
            var svg=CombatForensicDrawing.Svg(m,false);var heat=CombatForensicDrawing.Svg(m,true);
            Check(svg.Contains(sub[1].Colour+"ee",StringComparison.Ordinal)&&heat.Length>0,"material view: the core is filled in its own colour inside the band; the heat-map is drawn");

            // ================= point 3 of the review: two different generic models in one group are no module =================
            {
                var models=n10 with{Events=n10.Events.Select(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Entity==core)?e with{Layer=e.Layer with{Parts=e.Layer.Parts.Select(p=>p with{Name=p.Entity==core?"model":"baseModel"}).ToArray()}}:e).ToArray()};
                var renamed=n10 with{Events=n10.Events.Select(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Entity==core)?e with{Layer=e.Layer with{Parts=e.Layer.Parts.Select(p=>p with{Name=p.Entity==core?"Other part":p.Name}).ToArray()}}:e).ToArray()};
                bool Merged(CombatReportShot s)=>LayeredPlateReport.Apply(s).Events.Any(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.MaterialLabel==CombatModuleLayers.ModuleLabel&&p.Armour));
                Check(!Merged(models)&&!Merged(renamed)&&Merged(n10),"a group of differently named parts (generic 'model' + 'baseModel', as T-72A shot 4 of 15-11, or two named parts) is not merged; one module whose objects share its name is");
            }

            // ================= B. internal modules are not "ERA" =================
            CombatLayerPart Part(int entity)=>applied.Events.Where(e=>e.Kind=="layerStart").SelectMany(e=>e.Layer!.Parts).First(p=>p.Entity==entity);
            Check(n10.Events.Where(e=>e.Kind=="layerStart").SelectMany(e=>e.Layer!.Parts).Where(p=>p.Entity is engine or fuel or track).All(p=>p.MaterialLabel=="ERA"),"recorded (unchanged): engine, fuel tank and track carry the fallback label \"ERA\" (events 19, 23, 30)");
            Check(Part(engine).MaterialLabel==CombatModuleLayers.InternalLabel&&Part(fuel).MaterialLabel==CombatModuleLayers.InternalLabel&&Part(track).MaterialLabel=="Track",
                $"after Apply: engine '{Part(engine).MaterialLabel}', fuel tank '{Part(fuel).MaterialLabel}', track '{Part(track).MaterialLabel}'");
            var modelJson=JsonSerializer.Serialize(m);
            Check(!m.Layers.SelectMany(l=>l.Parts).Any(p=>p.Entity is engine or fuel or track&&p.MaterialLabel=="ERA"),"Forensic: engine, fuel tank and track carry no \"ERA\"");
            Check(!applied.Events.Where(e=>e.Kind=="layerStart").SelectMany(e=>e.Layer!.Parts).Any(p=>p.MaterialLabel=="ERA"),"after Apply no part of shot 10 is named ERA (the VARMA parts in the air gap read '"+CombatModuleLayers.ModuleLabel+"')");
            cases.AppendLine($"shot 10 labels\tengine ERA, fuelTank ERA, track ERA\tengine {Part(engine).MaterialLabel}, fuelTank {Part(fuel).MaterialLabel}, track {Part(track).MaterialLabel}\t");
            var realEra=new CombatLayerPart(9,"Kontakt-5 brick","kontakt5Active","Kontakt-5",13,30,true);var unbound=new CombatLayerPart(9,"Kontakt-5 turret brick",null,"ERA",null,30,false);
            Check(CombatModuleLayers.Label(realEra,"")==null&&CombatModuleLayers.Label(unbound,"")==null&&CombatModuleLayers.Label(new CombatLayerPart(5,"Crew member #5",null,"ERA",null,null,false),"")==CombatModuleLayers.CrewLabel,
                "real ERA (a material) and a named add-on part keep their label; a crew position reads 'Crew position'");

            // ================= C. no -142.6 mm "Between layers" =================
            var wf=PenetrationWaterfall.Build(n10)!;var wfText=PenetrationWaterfall.Text(wf);
            var between=wf.Steps.Where(x=>x.Label=="Between layers").ToArray();
            Check(!between.Any(x=>Math.Abs(x.Delta+142.6)<.5),$"no -142.6 mm 'Between layers' step: {wfText}");
            Check(wf.Steps.Any(x=>x.Label.StartsWith("VARMA",StringComparison.Ordinal)&&Math.Abs(x.Delta+74.05)<.01),"the module's measured reduction (-74.05 mm) is booked on the module");
            // C, owner review 16-11: the engine and the fuel tank (no rows in the hit list) keep their own measured reduction; nothing is left "Between layers"
            var eng=wf.Steps.FirstOrDefault(x=>x.Label=="engine");var tank=wf.Steps.FirstOrDefault(x=>x.Label=="fuel tank");
            Check(eng!=null&&Math.Abs(eng.Delta-(400.3139-423.3186))<.01&&tank!=null&&Math.Abs(tank.Delta-(354.7559-400.3139))<.01,$"engine {N(eng?.Delta,"0.##")} mm and fuel tank {N(tank?.Delta,"0.##")} mm are their own steps (measured -23.0 and -45.6)");
            Check(between.Length==0&&Math.Abs(wf.Start+wf.Steps.Sum(x=>x.Delta)-wf.End)<1e-9&&Math.Abs(wf.End-354.7559)<.01,$"no 'Between layers' left; start + steps = end ({N(wf.End,"0.##")} mm, the exit of the last armour layer as before)");
            cases.AppendLine($"shot 10 penetration sequence\t497 mm → Between layers -143 = 355 mm left\t{wfText}\tthe track (-4 mm) lies behind the last armour layer (Plate #27) and stays outside the sequence, as in 0.22.90");

            // ================= 2. the drawing at the end of the band: no text over another text or over a drawn line =================
            {
                var rec=new TextRecorder();CombatForensicDrawing.Draw(rec,m,false);
                var boxes=rec.Texts.Select(t=>(t.Text,X0:t.Anchor==SectionAnchor.MiddleCenter?t.X-(t.Text.Length*6.3f+8)/2:t.Anchor==SectionAnchor.MiddleRight?t.X-(t.Text.Length*6.3f+8):t.X-4,Y0:t.Y-9,W:t.Text.Length*6.3f+8)).ToArray();
                var clashes=new List<string>();
                for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++)
                {
                    var p=boxes[i];var q=boxes[j];
                    if(p.X0<q.X0+q.W&&q.X0<p.X0+p.W&&p.Y0<q.Y0+18&&q.Y0<p.Y0+18)clashes.Add($"'{p.Text}' / '{q.Text}'");
                }
                // a vertical line (the envelope marker, a module connector) through the residual label
                var residual=boxes.FirstOrDefault(b=>b.Text.StartsWith("Residual ",StringComparison.Ordinal));
                if(residual.Text!=null)foreach(var l in rec.Lines.Where(l=>Math.Abs(l.X0-l.X1)<.5f&&l.X0>residual.X0&&l.X0<residual.X0+residual.W&&Math.Min(l.Y0,l.Y1)<residual.Y0+18&&Math.Max(l.Y0,l.Y1)>residual.Y0))clashes.Add($"a line at x {N(l.X0,"0")} through '{residual.Text}'");
                Check(clashes.Count==0,"shot 10: no text overlaps another text, and no marker line crosses the residual label"+(clashes.Count>0?": "+string.Join("; ",clashes):$" ({boxes.Length} texts)"));
                Check(rec.Texts.Any(t=>t.Text=="core")&&rec.Texts.Any(t=>t.Text=="Layer 1:")&&rec.Texts.Count(t=>t.Text=="housing (steel)")==2,"the band names its core; the steel housings (2.7 mm, too thin for a name) are in a legend on the top line: "+string.Join(" ",rec.Texts.Where(t=>t.Y<25).Select(t=>t.Text)));
            }

            // ================= one place, report only =================
            Check(storedBefore.SequenceEqual(shots.Select(s=>JsonSerializer.Serialize(s))),"the stored shots (last-session.json content) are not changed by the readers");
            Check(JsonSerializer.Serialize(LayeredPlateReport.Apply(applied))==JsonSerializer.Serialize(applied),"Apply twice gives the same report");
            var html=Html(shots,omitted);var section=html[html.IndexOf("<section><h2",StringComparison.Ordinal)..];section=section[..section.IndexOf("</section>",StringComparison.Ordinal)];
            Check(section.Contains("#10 OUT",StringComparison.Ordinal)&&!section.Contains(CombatReportView.NoLayersText,StringComparison.Ordinal)&&section.Contains("fx-module-layer",StringComparison.Ordinal)&&section.Contains("VARMA-style passive side armour, core (2500 kg/m&#179;)",StringComparison.Ordinal),
                "HTML of shot 10: the layer table has rows, the Forensic list has the module sublayers");
            File.WriteAllText(Path.Combine(preview,"module-layers-varma-0.22.91.html"),html);

            // ================= bit-equality with the 0.22.90 build =================
            var basePath=Environment.GetEnvironmentVariable("EB_BASE_TESTS")??"";
            if(!File.Exists(basePath)){Console.WriteLine("FEAT91: bit-equality with 0.22.90 SKIPPED (EB_BASE_TESTS does not name the 0.22.90 test build); not counted as a check");}
            else
            {
                var old=new Old90(basePath);
                // the Solid fixture of 0.22.81: byte for byte, without any relabelling
                foreach(var rel in new[]{Path.Combine("diag-0.22.79-solid-forensic","last-session-20261114-solid.json"),Path.Combine("diag-0.22.84-t72a","last-session-20261115-b.json")})
                {
                    var text=File.ReadAllText(Path.Combine(sessions,rel));using var d=JsonDocument.Parse(text);int om=d.RootElement.TryGetProperty("omittedShots",out var o)?o.GetInt32():0;
                    var ns=JsonSerializer.Deserialize<CombatReportShot[]>(d.RootElement.GetProperty("shots").GetRawText(),opts)!;
                    var relabelled=Relabelled(text,out int relabels);var os=old.Shots(relabelled);var raw=old.Shots(text);
                    int items=1,equal=Html(ns,om)==old.Html(os,om)?1:0,rawEqual=Html(ns,om)==old.Html(raw,om)?1:0;
                    for(int i=0;i<ns.Length;i++)
                    {
                        var nm=CombatForensics.Build(ns[i]);var o2=old.Model(os.GetValue(i)!);var r2=old.Model(raw.GetValue(i)!);items+=5;
                        bool[] same={JsonSerializer.Serialize(nm)==Old90.Json(o2),CombatForensicDrawing.Svg(nm,false)==old.Svg(o2,false),CombatForensicDrawing.Svg(nm,true)==old.Svg(o2,true),Summaries(ns[i]).SequenceEqual(old.Summaries(os.GetValue(i)!)),Waterfall(ns[i])==old.Waterfall(os.GetValue(i)!)};
                        equal+=same.Count(x=>x);
                        rawEqual+=new[]{JsonSerializer.Serialize(nm)==Old90.Json(r2),CombatForensicDrawing.Svg(nm,false)==old.Svg(r2,false),CombatForensicDrawing.Svg(nm,true)==old.Svg(r2,true),Summaries(ns[i]).SequenceEqual(old.Summaries(raw.GetValue(i)!)),Waterfall(ns[i])==old.Waterfall(raw.GetValue(i)!)}.Count(x=>x);
                    }
                    var solid=rel.Contains("solid",StringComparison.Ordinal);
                    if(solid)Check(relabels==0&&rawEqual==items,$"Solid fixture {rel}: HTML, Forensic JSON, both SVGs, hit list and penetration sequence of {ns.Length} shot(s) byte for byte 0.22.90 ({rawEqual} of {items}; no part relabelled)");
                    else
                    {
                        // round 2 (owner review 16-11): C changes penetration sequences that booked "Between layers", and the layout fix (point 2) may move marks and labels
                        // in a drawing. The Forensic model JSON and the hit list stay byte for byte those of 0.22.90 with the labels renamed (B); the HTML differs only in its
                        // drawings and sequences; a changed sequence keeps its start and end and books less "Between layers".
                        int jsonEq=0,listEq=0,svgDiff=0,seqDiff=0;bool seqOk=true;
                        for(int i=0;i<ns.Length;i++)
                        {
                            var nm=CombatForensics.Build(ns[i]);var o2=old.Model(os.GetValue(i)!);
                            if(JsonSerializer.Serialize(nm)==Old90.Json(o2))jsonEq++;
                            if(Summaries(ns[i]).SequenceEqual(old.Summaries(os.GetValue(i)!)))listEq++;
                            if(CombatForensicDrawing.Svg(nm,false)!=old.Svg(o2,false)||CombatForensicDrawing.Svg(nm,true)!=old.Svg(o2,true))svgDiff++;
                            string w0=old.Waterfall(os.GetValue(i)!),w1=Waterfall(ns[i]);
                            if(w0!=w1){seqDiff++;seqOk&=w0.Contains("Between layers",StringComparison.Ordinal)&&Ends(w0)==Ends(w1)&&Between(w1)<Between(w0);}
                        }
                        static string Strip(string h)=>System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(h,"<svg.*?</svg>","",System.Text.RegularExpressions.RegexOptions.Singleline),"<p class='context'>Penetration sequence: [^<]*</p>","");
                        Check(jsonEq==ns.Length&&listEq==ns.Length&&seqOk&&Strip(Html(ns,om))==Strip(old.Html(os,om)),
                            $"T-72A fixture {rel}: with the {relabels} recorded \"ERA\" labels of vehicle parts renamed (B), Forensic JSON {jsonEq}/{ns.Length} and hit list {listEq}/{ns.Length} byte for byte 0.22.90; the HTML equal apart from drawings and sequences; {seqDiff} sequences changed, each only in its 'Between layers' bookings (same start and end); {svgDiff} drawings changed by the layout fix");
                    }
                    cases.AppendLine($"{rel}\traw equal {rawEqual}/{items}\trelabelled equal {equal}/{items}\t{relabels} vehicle parts renamed");
                    if(!solid)
                    {
                        // the derived thicknesses of 0.22.88: every derived part (name and thickness) identical to 0.22.90 on the raw input
                        var mine=ns.SelectMany(s=>CombatForensics.Build(s).Layers.SelectMany(l=>l.Parts.Concat((l.Plates??Array.Empty<ForensicArrayPlate>()).Select(p=>p.Part)).Where(p=>p.Name.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal)).Select(p=>$"{s.Number}:{l.HitId}:{p.Entity}:{p.Name}:{p.ThicknessMm?.ToString("R",CultureInfo.InvariantCulture)}"))).ToArray();
                        var theirs=new List<string>();
                        foreach(var s in raw)
                        {
                            using var oj=JsonDocument.Parse(Old90.Json(old.Model(s)));
                            foreach(var l in oj.RootElement.GetProperty("Layers").EnumerateArray())
                            {
                                var parts=l.GetProperty("Parts").EnumerateArray().ToList();
                                if(l.TryGetProperty("Plates",out var plates)&&plates.ValueKind==JsonValueKind.Array)parts.AddRange(plates.EnumerateArray().Select(p=>p.GetProperty("Part")));
                                foreach(var p in parts.Where(p=>p.GetProperty("Name").GetString()!.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal)))
                                    theirs.Add($"{Old90.Number(s)}:{l.GetProperty("HitId").GetString()}:{p.GetProperty("Entity").GetInt32()}:{p.GetProperty("Name").GetString()}:{(p.GetProperty("ThicknessMm").ValueKind==JsonValueKind.Number?p.GetProperty("ThicknessMm").GetDouble().ToString("R",CultureInfo.InvariantCulture):null)}");
                            }
                        }
                        Check(mine.Length>0&&mine.SequenceEqual(theirs),$"T-72A: the {mine.Length} derived thicknesses of 0.22.88 (name and value, bit for bit) are those of 0.22.90 ({theirs.Count})");
                    }
                }
                // contact sheet: shot 10 before and after
                var oldShots=old.Shots(fixtureText);var o10=oldShots.Cast<object>().Single(s=>Old90.Number(s)==10);var om10=old.Model(o10);
                Check(old.Html(oldShots,omitted).Contains(CombatReportView.NoLayersText,StringComparison.Ordinal)&&old.Waterfall(o10).Contains("Between layers -143",StringComparison.Ordinal),"0.22.90 on the same fixture: 'No armour layer was measured' and 'Between layers -143' (the user's report)");
                cases.AppendLine($"shot 10 Forensic layers\t{string.Join(" → ",((System.Collections.IEnumerable)P(om10,"Layers")!).Cast<object>().Select(l=>old.LayerName(l)))}\t{string.Join(" → ",m.Layers.Select(CombatForensicGeometry.LayerName))}\t");
                panels.Add(OldPanel(old,"Shot 10 with 0.22.90 (as the user saw it): VARMA as one grey layer, no rows in the layer table",om10,old.Waterfall(o10)));
                panels.Add(NewPanel("Shot 10 with 0.22.91: the VARMA module as one layer with its housing, core and housing",m,wfText));
            }
        }
        catch(Exception error){Check(false,"FEAT91 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"MODULE-LAYERS-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(preview,"module-layers-panels.json"),JsonSerializer.Serialize(panels));
        File.WriteAllText(Path.Combine(output,"FEAT91-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT91 PASS: {checks} checks, {failures} failures");
    }
    /// <summary>Records the texts and lines of the production drawing (for the overlap check).</summary>
    private sealed class TextRecorder:ISectionSink
    {
        internal readonly List<(float X,float Y,string Text,SectionAnchor Anchor)> Texts=new();internal readonly List<(float X0,float Y0,float X1,float Y1)> Lines=new();
        public void ViewBox(float width,float height){}
        public void BeginClip(float width,float height){}
        public void EndClip(){}
        public void Poly(float[] xy,string fill,string? stroke,float strokePx){}
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx){}
        public void Outline(float[] xy,string colour,float widthPx){}
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>Lines.Add((x0,y0,x1,y1));
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx){}
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background)=>Texts.Add((x,y,text,anchor));
    }
    /// <summary>The drawing instructions (production drawing) and the rows of the list as the HTML shows them: layer, the interval rows, the module or recipe rows.</summary>
    private static object NewPanel(string title,ForensicModel m,string sequence)
    {
        var panel=typeof(Feat02281SolidForensicTests).GetMethod("Panel",F)!.Invoke(null,new object[]{title,m})!;
        var rows=new List<object>();
        foreach(var l in m.Layers)
        {
            if(l.GapMm!=null){rows.Add(new{level=0,n=l.Number.ToString(CultureInfo.InvariantCulture),colour=l.Colour,name="Air gap",detail=l.Geometry,value="-"});continue;}
            rows.Add(new{level=0,n=l.Number.ToString(CultureInfo.InvariantCulture),colour=l.Colour,name=CombatForensicGeometry.LayerName(l),detail=CombatForensicGeometry.Detail(l),value=l.Percent is {} p?CombatForensics.N(p)+"%":""});
            foreach(var s in l.PaidIntervals??Array.Empty<string>())rows.Add(new{level=1,n="",colour=l.Colour,name=s,detail="",value=""});
            if(l.ModuleLayers!=null)foreach(var x in l.ModuleLayers)rows.Add(new{level=1,n="",colour=x.Colour,name=x.Label,detail=x.Detail,value=""});
            else foreach(var s in l.Sublayers??Array.Empty<CompositeSublayer>())rows.Add(new{level=1,n="",colour=s.Colour,name=s.Label,detail=CombatForensicGeometry.SubDetail(s),value=""});
        }
        if(m.HasResidualRow)rows.Add(new{level=0,n="",colour="#8fd99c",name=m.RestLabel,detail=CombatForensics.N(m.Residual)+" mm RHA-eq",value=CombatForensics.N(m.ResidualPercent)+"%"});
        return new{title,explanation="Penetration sequence: "+sequence,ops=P(panel,"ops"),rows};
    }
    private static object OldPanel(Old90 old,string title,object model,string sequence)
    {
        var panel=old.Panel(title,model);var rows=new List<object>();
        foreach(var l in ((System.Collections.IEnumerable)P(model,"Layers")!).Cast<object>())
        {
            string n=((int)P(l,"Number")!).ToString(CultureInfo.InvariantCulture),colour=(string)P(l,"Colour")!;
            if(P(l,"GapMm")!=null){rows.Add(new{level=0,n,colour,name="Air gap",detail=(string)P(l,"Geometry")!,value="-"});continue;}
            rows.Add(new{level=0,n,colour,name=old.LayerName(l),detail=old.Detail(l),value=P(l,"Percent") is double p?CombatForensics.N(p)+"%":""});
            foreach(var s in (IEnumerable<string>?)P(l,"PaidIntervals")??Array.Empty<string>())rows.Add(new{level=1,n="",colour,name=s,detail="",value=""});
            if(P(l,"Sublayers") is System.Collections.IEnumerable subs)foreach(var s in subs.Cast<object>())rows.Add(new{level=1,n="",colour=(string)P(s,"Colour")!,name=(string)P(s,"Label")!,detail=old.SubDetail(s),value=""});
        }
        if((bool)P(model,"HasResidualRow")!)rows.Add(new{level=0,n="",colour="#8fd99c",name=(string)P(model,"RestLabel")!,detail=CombatForensics.N((double?)P(model,"Residual"))+" mm RHA-eq",value=CombatForensics.N((double?)P(model,"ResidualPercent"))+"%"});
        return new{title,explanation="Penetration sequence: "+sequence,ops=P(panel,"ops"),rows};
    }
}
