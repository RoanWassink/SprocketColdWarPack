using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.88 (brief FEAT-0.22.88-FORENSIC-DERIVED-THICKNESS, user 15-11 "Ja, als 0.22.88"): when the hit gives no normal thickness for an armour part with a measured obliquity,
/// LayeredPlateReport.Apply derives it for the readers from the paid path of that part in the same hit (sum of EndM − StartM × cos(obliquity)) and labels it
/// "(derived from the path)". Fixture: the user's T-72A session diag-0.22.84-t72a\last-session-20261115-b.json (read only). The 0.22.87 build (src-0.22.87\tests\bin, read only)
/// is loaded side by side for the before/after comparisons. Writes DERIVED-THICKNESS-CASES.tsv, FEAT88-RESULT.json and preview\derived-thickness-panels.json to test-output/.
/// </summary>
internal static class Feat02288DerivedThicknessTests
{
    /// <summary>The production files 0.22.88 may change on top of 0.22.87.</summary>
    internal static bool IsPlanned(string rel)=>rel is "LayeredPlateReport.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private const string StartMark="    /// <summary>The shot with its plates in layers merged (the shot itself when it has none).";
    private const string EndMark="    internal static IReadOnlyList<CombatReportEvent> Collapse(";
    /// <summary>The Apply of 0.22.87 (LayeredPlateReport.cs), byte for byte, up to the next member.</summary>
    private static readonly string OldApply=string.Join("\r\n",new[]{
        "    /// <summary>The shot with its plates in layers merged (the shot itself when it has none). Called by the readers of a report (the list, Forensic, the HTML); the stored report, its JSON and <see cref=\"CombatReportTruth.Normalize\"/> stay as they are.</summary>",
        "    internal static CombatReportShot Apply(CombatReportShot shot)",
        "    {",
        "        var events=Collapse(shot.Events);",
        "        return ReferenceEquals(events,shot.Events)?shot:shot with{Events=events};",
        "    }",
        "",""});
    /// <summary>The bytes of a production file without its 0.22.88 change: for LayeredPlateReport.cs the new Apply and DeriveThickness are replaced by the Apply of 0.22.87; any other file as it is.</summary>
    internal static byte[] Un88Bytes(string path)
    {
        var bytes=Feat02289Un.Un89Bytes(path); // 0.22.89 adapted: the 0.22.88 bytes first
        if(Path.GetFileName(path)!="LayeredPlateReport.cs")return bytes;
        var text=Encoding.UTF8.GetString(bytes);int s=text.IndexOf(StartMark,StringComparison.Ordinal),e=text.IndexOf(EndMark,StringComparison.Ordinal);
        return s<0||e<s?bytes:Encoding.UTF8.GetBytes(text[..s]+OldApply+text[e..]);
    }

    private const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    /// <summary>The 0.22.87 build of the same pure report code, loaded by reflection (its own statics, its own preset lookup).</summary>
    private sealed class Old87
    {
        private readonly Assembly a;
        private readonly JsonSerializerOptions opts=new(){PropertyNameCaseInsensitive=true};
        internal Old87(string dll)
        {
            var dir=Path.GetDirectoryName(dll)!;var alc=new AssemblyLoadContext("eb-0.22.87-report",false);
            alc.Resolving+=(c,n)=>{var p=Path.Combine(dir,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;};
            a=alc.LoadFromAssemblyPath(dll);
            var catalogue=T("CassetteCatalogue");
            var parsed=catalogue.GetMethod("Parse",F,new[]{typeof(string)})!.Invoke(null,new object[]{File.ReadAllText(Path.Combine(dir,(string)catalogue.GetField("FileName",F)!.GetValue(null)!))})!;
            var presets=(System.Collections.IEnumerable)parsed.GetType().GetProperty("Presets",F)!.GetValue(parsed)!;var presetType=T("CassettePreset");
            var dict=(System.Collections.IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string),presetType))!;
            foreach(var p in presets)dict[(string)presetType.GetProperty("MaterialId",F)!.GetValue(p)!]=p;
            var lookup=typeof(Feat02288DerivedThicknessTests).GetMethod(nameof(Lookup),F)!.MakeGenericMethod(presetType).Invoke(null,new object[]{dict})!;
            T("CombatCompositeLayers").GetMethod("Configure",F)!.Invoke(null,new object?[]{lookup,null});
        }
        private Type T(string n)=>a.GetType("SprocketExpandedBallistics."+n,true)!;
        internal Array Shots(string json){using var d=JsonDocument.Parse(json);return (Array)JsonSerializer.Deserialize(d.RootElement.GetProperty("shots").GetRawText(),T("CombatReportShot").MakeArrayType(),opts)!;}
        internal static int Number(object shot)=>(int)shot.GetType().GetProperty("Number",F)!.GetValue(shot)!;
        internal object Model(object shot)=>T("CombatForensics").GetMethod("Build",F)!.Invoke(null,new[]{shot})!;
        internal static string Json(object model)=>JsonSerializer.Serialize(model,model.GetType());
        internal string Svg(object model,bool heat)=>(string)T("CombatForensicDrawing").GetMethod("Svg",F)!.Invoke(null,new object?[]{model,heat,null})!;
        internal int ScaleBar(object model){var g=T("CombatForensicGeometry").GetMethod("Build",F)!.Invoke(null,new object[]{model,true})!;return (int)g.GetType().GetProperty("ScaleBarMm",F)!.GetValue(g)!;}
        internal string Html(Array shots,int omitted){var h=Activator.CreateInstance(T("CombatReportHistory"),true)!;T("CombatReportHistory").GetMethod("Restore",F)!.Invoke(h,new object[]{shots,omitted});return (string)T("CombatReportHtml").GetMethod("Render",F)!.Invoke(null,new[]{h})!;}
        internal string[] Summaries(object shot)
        {
            var normal=T("CombatReportTruth").GetMethod("Normalize",F,new[]{T("CombatReportShot")})!.Invoke(null,new[]{shot})!;
            var hits=(Array)T("CombatHitProjection").GetMethod("Build",F)!.Invoke(null,new[]{normal})!;
            return Enumerable.Range(0,hits.Length).Select(i=>{var h=hits.GetValue(i)!;return (string)h.GetType().GetMethod("Summary",F)!.Invoke(h,new object[]{i+1})!;}).ToArray();
        }
        internal object Panel(string title,object model)=>a.GetType("SprocketExpandedBallistics.Feat02281SolidForensicTests",true)!.GetMethod("Panel",F)!.Invoke(null,new object[]{title,model})!;
    }
    private static Func<string,T?> Lookup<T>(System.Collections.Generic.Dictionary<string,T> d) where T:class=>id=>id!=null&&d.TryGetValue(id,out var p)?p:null;
    private static object NewPanel(string title,ForensicModel m)=>typeof(Feat02281SolidForensicTests).GetMethod("Panel",F)!.Invoke(null,new object[]{title,m})!;
    private static string[] Summaries(CombatReportShot shot){var hits=CombatHitProjection.Build(CombatReportTruth.Normalize(shot));return hits.Select((h,i)=>h.Summary(i+1)).ToArray();}
    private static string Html(IEnumerable<CombatReportShot> shots,int omitted){var h=new CombatReportHistory();h.Restore(shots,omitted);return CombatReportHtml.Render(h);}
    private static string N(double? v,string f="0.###")=>v is {} x?x.ToString(f,CultureInfo.InvariantCulture):"null";

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT88: "+message);}
        string root=Fix02228Tests.SourceRoot(),parent=Fix02228Tests.SessionRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets.ToDictionary(p=>p.MaterialId,p=>p);
        CombatCompositeLayers.Configure(id=>id!=null&&presets.TryGetValue(id,out var found)?found:null);
        var cases=new StringBuilder("shot\tlayer (hit)\tentity\tobliquity deg\tpaid path mm\tthickness 0.22.87\tthickness 0.22.88\tlabel\trecipe layers\tscale bar 0.22.87 -> 0.22.88\tbrief expectation 215 +-0.5\n");
        var panels=new List<object>();const int glacis=430;
        try
        {
            var opts=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
            var fixture=Path.Combine(parent,"diag-0.22.84-t72a","last-session-20261115-b.json");var fixtureText=File.ReadAllText(fixture);
            using var doc=JsonDocument.Parse(fixtureText);int omitted=doc.RootElement.GetProperty("omittedShots").GetInt32();
            var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),opts)!;
            var storedBefore=shots.Select(s=>JsonSerializer.Serialize(s)).ToArray();
            var oldPath=Path.Combine(parent,"src-0.22.87","tests","bin","Release","net8.0","ExpandedBallistics.Tests.dll");var old=File.Exists(oldPath)?new Old87(oldPath):null!; // herpak: the 0.22.87 test build is not here
            var oldShots=old is null?Array.Empty<object>():old.Shots(fixtureText);object OldShot(int n)=>oldShots.Cast<object>().Single(s=>Old87.Number(s)==n);
            CombatReportShot Shot(int n)=>shots.Single(s=>s.Number==n);

            // ================= 1. Per shot on the glacis: what 0.22.87 showed and what 0.22.88 shows =================
            var models=new Dictionary<int,ForensicModel>();var oldModels=new Dictionary<int,object>();
            if(old is not null){ // herpak: section 1 compares with the 0.22.87 test build
            foreach(var n in new[]{1,2,3,4,5,10,19,20,21})
            {
                var m=CombatForensics.Build(Shot(n));models[n]=m;var om=old.Model(OldShot(n));oldModels[n]=om;
                using var oj=JsonDocument.Parse(Old87.Json(om));
                var normal=CombatReportTruth.Normalize(Shot(n));
                foreach(var l in m.Layers.Where(l=>l.GapMm==null&&l.Parts.Any(p=>p.Armour&&p.Entity==glacis)))
                {
                    var part=l.Parts.First(p=>p.Armour&&p.Entity==glacis);
                    var oldLayer=oj.RootElement.GetProperty("Layers").EnumerateArray().First(x=>x.GetProperty("HitId").GetString()==l.HitId);
                    var oldPart=oldLayer.GetProperty("Parts").EnumerateArray().First(x=>x.GetProperty("Entity").GetInt32()==glacis);
                    double? oldT=oldPart.GetProperty("ThicknessMm").ValueKind==JsonValueKind.Number?oldPart.GetProperty("ThicknessMm").GetDouble():null;
                    var paid=normal.Events.Where(e=>e.HitId==l.HitId&&e.Kind is "materialReaction" or "materialStop"&&e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==glacis).Select(p=>(p.StartM,p.EndM)).Distinct().Sum(p=>(p.EndM-p.StartM)*1000);
                    bool labelled=part.Name.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal);
                    var subs=l.Array?l.Plates?.FirstOrDefault(p=>p.Part.Entity==glacis)?.Sublayers:l.Sublayers;
                    int barOld=old.ScaleBar(om),barNew=CombatForensicGeometry.Build(m).ScaleBarMm;
                    bool near215=part.ThicknessMm is {} t&&Math.Abs(t-215)<=.5;
                    cases.AppendLine($"{n}\t{l.Number} ({l.HitId.Split(':',2)[1]}){(l.Array?" array":"")}\t{glacis}\t{N(part.AngleDegrees,"0.####")}\t{N(paid,"0.###")}\t{N(oldT,"0.###")}\t{N(part.ThicknessMm,"0.###")}\t{(labelled?"(derived from the path)":"-")}\t{subs?.Count??0}\t{barOld} -> {barNew}\t{(near215?"met":"not met")}");
                    if(oldT!=null)
                        Check(part.ThicknessMm==oldT&&!labelled,$"shot {n}, layer {l.Number}: the thickness the hit gave stays ({N(oldT)} mm), no label");
                    else if(paid>0&&part.AngleDegrees is {} a)
                    {
                        double expect=paid*Math.Cos(a*Math.PI/180);
                        Check(labelled&&part.ThicknessMm is {} d&&Math.Abs(d-expect)<1e-9,$"shot {n}, layer {l.Number}: null in 0.22.87 -> {N(part.ThicknessMm)} mm = paid {N(paid)} mm x cos {N(a,"0.####")}° (derived from the path)");
                        Check(subs?.Count==3,$"shot {n}, layer {l.Number}: the three layers of the T-72A hull recipe are listed ({subs?.Count??0})");
                    }
                }
            }
            // shot 10: the case of the brief
            {
                var m=models[10];var l=m.Layers.Single(l=>l.GapMm==null&&l.Parts.Any(p=>p.Entity==glacis));var part=l.Parts.Single(p=>p.Entity==glacis);
                double expect=601.929*Math.Cos(69.0726*Math.PI/180);
                Check(part.ThicknessMm is {} t&&Math.Abs(t-215)<=.5&&Math.Abs(t-expect)<.01,$"shot 10: the glacis is {N(part.ThicknessMm)} mm (215.0 +- 0.5; 601.929 x cos 69.0726° = {N(expect)})");
                Check(l.Geometry=="215 mm at 69.1°"&&CombatForensicGeometry.Detail(l)=="Composite armour plate (derived from the path) · 215 mm at 69.1°",$"shot 10, the list 'Penetration absorbed per layer': '{CombatForensicGeometry.LayerName(l)}  {CombatForensicGeometry.Detail(l)}'");
                Check(l.Sublayers is {Count:3} s&&s[0].Label.Contains("Rolled",StringComparison.OrdinalIgnoreCase)&&Math.Abs(s.Sum(x=>x.NormalMm)-part.ThicknessMm!.Value)<1e-6,
                    "shot 10: three recipe layers scaled to the derived thickness ("+string.Join(", ",(l.Sublayers??Array.Empty<CompositeSublayer>()).Select(x=>x.Label+" "+N(x.NormalMm,"0.#")))+" mm)");
                Check(l.PathMm is {} p&&Math.Abs(p-601.929)<.001,$"shot 10: the band is drawn over the paid path ({N(l.PathMm)} mm)");
                var g=CombatForensicGeometry.Build(m);var band=g.Bands.Single(b=>b.Layer.Number==l.Number);
                Check(band.Polygon.Length>0&&band.Subs.Count==3,$"shot 10: the cross-section draws the glacis as a band with 3 recipe layers (0.22.87: a grey mark only)");
                // the scale follows the drawn extent (CombatForensicGeometry.Build): with the glacis path in it the extent is that of a shot whose glacis thickness the hit gave
                double Extent(ForensicModel x)=>x.Layers.Select(y=>y.StartMm+(y.PathMm??0)).DefaultIfEmpty(0).Max();
                using(var oj10=JsonDocument.Parse(Old87.Json(oldModels[10])))
                {
                    double oldExtent=oj10.RootElement.GetProperty("Layers").EnumerateArray().Select(y=>y.GetProperty("StartMm").GetDouble()+(y.GetProperty("PathMm").ValueKind==JsonValueKind.Number?y.GetProperty("PathMm").GetDouble():0)).DefaultIfEmpty(0).Max();
                    var m1=models[1];var g1=CombatForensicGeometry.Build(m1);
                    double pxPerMm10=(CombatForensicDrawing.Width-270)/Math.Max(200,Extent(m)),pxPerMm1=(CombatForensicDrawing.Width-270)/Math.Max(200,Extent(m1));
                    cases.AppendLine($"scale\tshot 10: extent {N(oldExtent,"0.#")} -> {N(Extent(m),"0.#")} mm, bar {old.ScaleBar(oldModels[10])} -> {g.ScaleBarMm} mm, glacis band {N(l.PathMm!.Value*pxPerMm10,"0.#")} px; shot 1: extent {N(Extent(m1),"0.#")} mm, bar {g1.ScaleBarMm} mm, glacis band {N(m1.Layers.First(y=>y.GapMm==null&&y.Parts.Any(p=>p.Armour&&p.Entity==glacis)).PathMm!.Value*pxPerMm1,"0.#")} px");
                    Check(Math.Abs(Extent(m)-oldExtent-601.929)<.01&&g.ScaleBarMm>old.ScaleBar(oldModels[10]),$"shot 10: the scale includes the glacis (extent {N(oldExtent,"0.#")} -> {N(Extent(m),"0.#")} mm), the scale bar {old.ScaleBar(oldModels[10])} -> {g.ScaleBarMm} mm (shot 1, which runs on into the hull: {g1.ScaleBarMm} mm)");
                }
                Check(m.Findings.Any(f=>f.Contains("constituent layers of the composer preset, scaled to the 215 mm",StringComparison.Ordinal))&&m.Findings.Any(f=>f.StartsWith("Obliquity:",StringComparison.Ordinal)),"shot 10: the findings name the recipe layers and the obliquity of the glacis");
                var sv=Summaries(Shot(10));
                Check(sv.Any(s=>s.Contains("Composite armour plate (derived from the path)",StringComparison.Ordinal)&&s.Contains("T-72A glacis | 215 mm | 69.1°",StringComparison.Ordinal)),"shot 10, the hit list: '"+sv.FirstOrDefault(s=>s.Contains("T-72A glacis",StringComparison.Ordinal))?.Replace("\n"," / ")+"'");
                var t21=models[21].Layers.Single(l=>l.GapMm==null&&l.Parts.Any(p=>p.Entity==glacis)).Parts.Single(p=>p.Entity==glacis).ThicknessMm;
                Check(t21 is {} x21&&Math.Abs(x21-215)<=.5,$"shot 21: the glacis is {N(t21)} mm (215.0 +- 0.5)");
                panels.Add(old.Panel("Shot 10 with 0.22.87 (as the user saw it): the hit gave no thickness for the glacis, only a grey mark",oldModels[10]));
                panels.Add(NewPanel("Shot 10 with 0.22.88: thickness derived from the paid path, 601.9 mm x cos 69.1° = 215 mm (derived from the path)",m));
                panels.Add(old.Panel("Shot 1 with 0.22.87: the hit gave 215 mm",oldModels[1]));
                panels.Add(NewPanel("Shot 1 with 0.22.88: unchanged (the thickness of the hit)",models[1]));
            }
            // the HTML of the session: the technical log and the Forensic list carry the label; the shots with a thickness of the hit are byte for byte 0.22.87
            {
                var html=Html(shots,omitted);var oldHtml=old.Html(oldShots,omitted);
                File.WriteAllText(Path.Combine(preview,"derived-thickness-t72a-0.22.88.html"),html);File.WriteAllText(Path.Combine(preview,"derived-thickness-t72a-0.22.87.html"),oldHtml);
                Check(Regex.IsMatch(html,@"  Composite armour plate \(derived from the path\): T-72A glacis; 215 mm; 69[.,]1 degrees"),"HTML technical log of shot 10: 'Composite armour plate (derived from the path): T-72A glacis; 215 mm; 69.1 degrees'");
                Check(html.Contains("Composite armour plate (derived from the path) &#183; 215 mm at 69.1&#176;",StringComparison.Ordinal),"HTML Forensic list of shot 10: 'Composite armour plate (derived from the path) · 215 mm at 69.1°'");
                int same=0,derived=0;var unchanged=new List<int>();
                foreach(var s in shots)
                {
                    var os=OldShot(s.Number);var m=CombatForensics.Build(s);var om=old.Model(os);
                    bool any=m.Layers.Any(l=>l.Parts.Any(p=>p.Name.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal)));if(any){derived++;continue;}
                    bool eq=JsonSerializer.Serialize(m)==Old87.Json(om)&&CombatForensicDrawing.Svg(m,false)==old.Svg(om,false)&&CombatForensicDrawing.Svg(m,true)==old.Svg(om,true)&&Summaries(s).SequenceEqual(old.Summaries(os));
                    if(eq){same++;unchanged.Add(s.Number);}
                }
                Check(same==shots.Length-derived&&new[]{1,19,20}.All(unchanged.Contains),$"shots without a derived thickness ({same} of {shots.Length}, among them 1, 19 and 20): Forensic JSON, both SVGs and the hit list byte for byte 0.22.87; {derived} shots with a derived thickness");
                Check(storedBefore.SequenceEqual(shots.Select(s=>JsonSerializer.Serialize(s))),"the stored shots (last-session.json content) are not changed by the readers");
            }
            } // herpak
            // ================= 2. The rule, on shot 10 changed by hand =================
            {
                var s10=CombatReportTruth.Normalize(Shot(10));var reached=s10.Events.First(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Entity==glacis&&p.Armour));
                CombatReportShot With(Func<CombatReportEvent,CombatReportEvent> f)=>s10 with{Events=s10.Events.Select(f).ToArray()};
                CombatLayerPart Glacis(CombatReportShot s)=>LayeredPlateReport.Apply(s).Events.First(e=>e.Kind=="layerStart"&&e.HitId==reached.HitId).Layer!.Parts.First(p=>p.Entity==glacis);
                var noPaid=With(e=>e.HitId==reached.HitId&&e.PaidIntervals!=null?e with{PaidIntervals=null}:e);
                Check(Glacis(noPaid).ThicknessMm==null&&!Glacis(noPaid).Name.Contains("derived",StringComparison.Ordinal)&&ReferenceEquals(LayeredPlateReport.Apply(noPaid).Events,noPaid.Events),"no paid interval: the thickness stays null, no label, the same list");
                CombatReportEvent Part(CombatReportEvent e,Func<CombatLayerPart,CombatLayerPart> f)=>e==reached?e with{Layer=e.Layer! with{Parts=e.Layer.Parts.Select(p=>p.Entity==glacis?f(p):p).ToArray()}}:e;
                var noAngle=With(e=>Part(e,p=>p with{AngleDegrees=null}));
                Check(Glacis(noAngle).ThicknessMm==null&&ReferenceEquals(LayeredPlateReport.Apply(noAngle).Events,noAngle.Events),"no obliquity: the thickness stays null, the same list");
                var given=With(e=>Part(e,p=>p with{ThicknessMm=100}));
                Check(Glacis(given).ThicknessMm==100&&Glacis(given).Name=="Composite armour plate","a thickness the hit gave (100 mm, unlike the path) stays, no label");
                var notArmour=With(e=>Part(e,p=>p with{Armour=false}));
                Check(Glacis(notArmour).ThicknessMm==null,"a part that is not armour stays without thickness");
                var other=With(e=>e.HitId==reached.HitId&&e.PaidIntervals!=null?e with{PaidIntervals=e.PaidIntervals.Select(p=>p with{Entity=999}).ToArray()}:e);
                Check(Glacis(other).ThicknessMm==null,"a paid interval of another entity does not count");
                var twice=With(e=>e.HitId==reached.HitId&&e.PaidIntervals!=null?e with{PaidIntervals=e.PaidIntervals.Concat(e.PaidIntervals).ToArray()}:e);
                Check(Glacis(twice).ThicknessMm is {} tw&&Math.Abs(tw-Glacis(s10).ThicknessMm!.Value)<1e-12,"the same interval listed twice counts once");
                // a plate in layers or a solid block keeps the thickness of its own path
                var marker=new CombatReportEvent(LayeredPlateReport.Marker,"","T-72A glacis: plate in layers, 3 layers.",RelatedEntity:glacis);
                Check(ReferenceEquals(LayeredPlateReport.DeriveThickness(s10.Events,s10.Events.Append(marker).ToArray()),s10.Events),"an entity with a 'layeredPlate' marker in the recorded report: nothing derived (the same list)");
                var solid=new CombatReportEvent(LayeredPlateReport.SolidMarker,"","x",RelatedEntity:glacis);
                Check(ReferenceEquals(LayeredPlateReport.DeriveThickness(s10.Events,s10.Events.Append(solid).ToArray()),s10.Events),"an entity with a 'solidBlock' marker in the recorded report: nothing derived");
                var named=With(e=>Part(e,p=>p with{Name="Composite armour plate [Solid block]"}));
                var derivedNamed=LayeredPlateReport.DeriveThickness(named.Events,named.Events).First(e=>e.Kind=="layerStart"&&e.HitId==reached.HitId).Layer!.Parts.First(p=>p.Entity==glacis);
                Check(derivedNamed.ThicknessMm is {} nt&&Math.Abs(nt-Glacis(s10).ThicknessMm!.Value)<1e-12&&derivedNamed.Name.EndsWith(LayeredPlateReport.DerivedLabel,StringComparison.Ordinal),"0.22.97: a part marked [Solid block] without a legacy solidBlock marker derives thickness from its solid path");
                var merged=s10 with{Events=s10.Events.Append(marker).ToArray()};var mm=LayeredPlateReport.Apply(merged).Events;
                var mp=mm.First(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Entity==glacis&&p.Armour)).Layer!.Parts.First(p=>p.Entity==glacis);
                Check(mm.Any(e=>e.Kind==LayeredPlateReport.Merged)&&!mp.Name.Contains("derived",StringComparison.Ordinal),$"with a 'layeredPlate' marker the merged plate keeps the thickness of that path ({N(mp.ThicknessMm)} mm), no label");
                var n1=CombatReportTruth.Normalize(Shot(1));
                Check(ReferenceEquals(LayeredPlateReport.Apply(n1),n1),"shot 1 (every armour part has the thickness of the hit): Apply returns the shot itself, as 0.22.87");
            }
            // ================= 3. The Solid fixture of 0.22.81: byte for byte as before =================
            if(old is not null&&Directory.Exists(Path.Combine(parent,"out-0.22.81"))) // herpak: the 0.22.87 build and out-0.22.81 are not here
            {
                var solidFile=Path.Combine(parent,"diag-0.22.79-solid-forensic","last-session-20261114-solid.json");var replay=Path.Combine(parent,"out-0.22.81","SOLID-FORENSIC-SHOT2-0.22.81.json");
                foreach(var file in new[]{solidFile,replay})
                {
                    var text=File.ReadAllText(file);using var d=JsonDocument.Parse(text);int om=d.RootElement.TryGetProperty("omittedShots",out var o)?o.GetInt32():0;
                    var ns=JsonSerializer.Deserialize<CombatReportShot[]>(d.RootElement.GetProperty("shots").GetRawText(),opts)!;var os=old.Shots(text);
                    var html=Html(ns,om);bool eq=html==old.Html(os,om);int items=1,equal=eq?1:0;
                    for(int i=0;i<ns.Length;i++)
                    {
                        var m=CombatForensics.Build(ns[i]);var o2=old.Model(os.GetValue(i)!);items+=4;
                        equal+=(JsonSerializer.Serialize(m)==Old87.Json(o2)?1:0)+(CombatForensicDrawing.Svg(m,false)==old.Svg(o2,false)?1:0)+(CombatForensicDrawing.Svg(m,true)==old.Svg(o2,true)?1:0)+(Summaries(ns[i]).SequenceEqual(old.Summaries(os.GetValue(i)!))?1:0);
                    }
                    Check(equal==items,$"Solid fixture {Path.GetRelativePath(parent,file)}: HTML, Forensic JSON, both SVGs and the hit list of {ns.Length} shot(s) byte for byte 0.22.87 ({equal} of {items})");
                    if(file==solidFile)Check(html==File.ReadAllText(Path.Combine(parent,"out-0.22.81","preview","solid-forensic-before.html")),"Solid fixture: the HTML equals out-0.22.81\\preview\\solid-forensic-before.html byte for byte");
                }
                var after81=Path.Combine(parent,"out-0.22.81","preview","solid-forensic-after.html");var after88=Path.Combine(preview,"solid-forensic-after.html");
                if(File.Exists(after88))Check(File.ReadAllBytes(after88).SequenceEqual(File.ReadAllBytes(after81)),"Solid replay: solid-forensic-after.html of this run (FEAT81) equals out-0.22.81 byte for byte");
            }
            // ================= 4. The files: only the planned ones changed; LayeredPlateReport.cs is 0.22.87 plus Apply/DeriveThickness; no hook added =================
            if(Directory.Exists(Path.Combine(parent,"src-0.22.87"))) // herpak: the 0.22.87 source tree is not here
            {
                var old87=Path.Combine(parent,"src-0.22.87");
                bool Skip(string rel)=>rel.Split('/').Any(p=>p is "bin" or "obj" or "tests" or ".git");
                Dictionary<string,string> Inventory(string dir)=>Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Select(f=>(Rel:Path.GetRelativePath(dir,f).Replace('\\','/'),Full:f)).Where(x=>!Skip(x.Rel)).ToDictionary(x=>x.Rel,x=>x.Full);
                var a=Inventory(old87);var b=Inventory(root);
                var changed=a.Keys.Union(b.Keys).Where(k=>!a.ContainsKey(k)||!b.ContainsKey(k)||!File.ReadAllBytes(a[k]).SequenceEqual(Feat02289Un.Un89Bytes(b[k]))).OrderBy(k=>k,StringComparer.Ordinal).ToArray(); // 0.22.89 adapted: the 0.22.88 bytes
                Check(changed.All(IsPlanned)&&changed.Length==4,"only the planned production files differ from 0.22.87: "+string.Join(", ",changed)+$" ({a.Count-changed.Length} files byte for byte)");
                Check(Un88Bytes(Path.Combine(root,"LayeredPlateReport.cs")).SequenceEqual(File.ReadAllBytes(Path.Combine(old87,"LayeredPlateReport.cs"))),"LayeredPlateReport.cs: without the new Apply/DeriveThickness block it is 0.22.87 byte for byte");
                string V(string f,string from,string to)=>System.Text.Encoding.UTF8.GetString(Feat02289Un.Un89Bytes(Path.Combine(root,f))).Replace(from,to,StringComparison.Ordinal); // 0.22.89 adapted: the 0.22.88 text
                Check(V("Plugin.cs","\"0.22.88\"","\"0.22.87\"")==File.ReadAllText(Path.Combine(old87,"Plugin.cs"))&&V("SprocketExpandedBallistics.csproj","<Version>0.22.88</Version>","<Version>0.22.87</Version>")==File.ReadAllText(Path.Combine(old87,"SprocketExpandedBallistics.csproj")),"Plugin.cs and the csproj differ only by the version");
                var notes=System.Text.Encoding.UTF8.GetString(Feat02289Un.Un89Bytes(Path.Combine(root,"RELEASE-NOTES.md"))); // 0.22.89 adapted: without the 0.22.89 section
                Check(notes.Remove(notes.IndexOf("## 0.22.88 ",StringComparison.Ordinal),notes.IndexOf("## 0.22.87 ",StringComparison.Ordinal)-notes.IndexOf("## 0.22.88 ",StringComparison.Ordinal))==File.ReadAllText(Path.Combine(old87,"RELEASE-NOTES.md")),"RELEASE-NOTES.md: only the 0.22.88 section is added");
                static int Hooks(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
                int h87=a.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks(File.ReadAllText(p.Value))),h88=b.Where(p=>p.Key.EndsWith(".cs",StringComparison.Ordinal)).Sum(p=>Hooks(File.ReadAllText(p.Value)));
                Check(h87==47&&h88==47,$"[HarmonyPatch annotations: {h87} before, {h88} after");
                foreach(var f in new[]{"RuntimeDamageFeed.cs","NativeHitFace.cs","CombatReportHtml.cs","CombatHitProjection.cs","CombatReportStore.cs","CombatForensics.cs","CombatForensicGeometry.cs","CombatForensicDrawing.cs","CombatForensicHtml.cs","CombatForensicPanel.cs","RuntimeGroupedProtection.cs"})
                    Check(Feat02289Un.Un89Bytes(Path.Combine(root,f)).SequenceEqual(File.ReadAllBytes(Path.Combine(old87,f))),f+" byte for byte 0.22.87"); // 0.22.89 adapted: RuntimeDamageFeed.cs without its marked 0.22.89 change
                Check(!File.ReadAllText(Path.Combine(root,"CombatReportStore.cs")).Contains("LayeredPlateReport",StringComparison.Ordinal),"the store of last-session.json does not read LayeredPlateReport (the stored report is unchanged)");
            }
        }
        catch(Exception error){Check(false,"FEAT88 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"DERIVED-THICKNESS-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(preview,"derived-thickness-panels.json"),JsonSerializer.Serialize(panels));
        File.WriteAllText(Path.Combine(output,"FEAT88-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT88 PASS: {checks} checks, {failures} failures");
    }
}
