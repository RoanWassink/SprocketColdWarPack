using System.Globalization;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.81: the report and Forensic show a passage through a plate marked [Solid block] as one part with the layers of its recipe over the whole length (front skin, cavity,
/// back skin); the false "odd" line of the back skin is gone; labels hold 48 characters. The production pure code runs as it is: LayeredPlateReport (CollapseSolid,
/// SolidBlockMarker), CombatForensics/Geometry/Drawing/Html, CombatHitProjection, PenetrationWaterfall, SolidBlock (CompositePlate.cs), SolidLineSeen (RuntimeGroupedProtection.cs,
/// compiled with EB_PURE_SOLID_MARK) and DamageFeedTruth. The game-side glue (SolidCavity and the interval route) is IL2CPP code; its order is replayed here statement for
/// statement, for 0.22.80 (as it is) and 0.22.81 (B: SolidLineSeen decides the odd line; A: the marker after the cavity line).
/// </summary>
internal static class Feat02281SolidForensicTests
{
    /// <summary>The production files 0.22.81 may change on top of 0.22.80.</summary>
    internal static bool IsPlanned(string rel)=>rel is "LayeredPlateReport.cs" or "CombatForensics.cs" or "CombatForensicGeometry.cs" or "RuntimeGroupedProtection.cs" or "DamageFeedModel.cs"
        or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private sealed class Rec:ISectionSink
    {
        internal readonly List<object> Ops=new();internal readonly List<string> Texts=new();
        public void ViewBox(float width,float height)=>Ops.Add(new{op="view",width,height});
        public void BeginClip(float width,float height)=>Ops.Add(new{op="clip",width,height});
        public void EndClip()=>Ops.Add(new{op="endclip"});
        public void Poly(float[] xy,string fill,string? stroke,float strokePx)=>Ops.Add(new{op="poly",xy,fill,stroke,strokePx});
        public void Hatch(float[] xy,string colour,float angleDeg,float spacingPx,float widthPx)=>Ops.Add(new{op="hatch",xy,colour,angleDeg,spacingPx,widthPx});
        public void Outline(float[] xy,string colour,float widthPx)=>Ops.Add(new{op="outline",xy,colour,widthPx});
        public void Line(float x0,float y0,float x1,float y1,string colour,float widthPx)=>Ops.Add(new{op="line",x0,y0,x1,y1,colour,widthPx});
        public void Arrow(float x0,float y0,float x1,float y1,string colour,float widthPx,float headPx)=>Ops.Add(new{op="arrow",x0,y0,x1,y1,colour,widthPx,headPx});
        public void Region(string id,float[] xy){}
        public void Text(float x,float y,string text,string colour,SectionAnchor anchor,string? background){Texts.Add(text);Ops.Add(new{op="text",x,y,text,colour,anchor=anchor.ToString(),background});}
    }
    /// <summary>One plate on the replayed line: its surfaces along the ray (metres, sorted; 1-2, 3-4 ... are its solid chords). Table null: plain steel (factor 1).</summary>
    private sealed record Plate(int Entity,string Mesh,string MaterialId,string Label,ProtectionFactorTable? Table,double[] Surfaces,bool Solid);
    private sealed class Replay
    {
        internal readonly List<CombatReportEvent> Events=new();internal double Budget;internal string? Current;internal bool Killed;internal double Cavity;internal int Charges;internal int OddLines;
    }
    private static string Old32(string name)=>name.Length>32?name[..32]:name; // the 0.22.80 label bound (DamageFeedModel.cs)
    private static void Started(Replay r,string id,CombatLayerInfo layer){if(r.Current==id)return;r.Current=id;r.Events.Add(new("layerStart","","Reached layer",r.Budget,null,id,layer));}
    /// <summary>
    /// One shot along the line, native block by native block, in the order of ReachedWithLayers: SolidCavities/SolidCavity (Analyse, the odd line, Plan, LayerStarted, ApplyPassive
    /// with the native law, Commit, the cavity line; 0.22.81: SolidLineSeen and the marker), then the unchanged interval route (LayerStarted, ledger plan, the interval line, the
    /// block exit or the stop). <paramref name="fromBlock"/>: each block lists only the surfaces from its own start on (what the user's line showed at the back skin, "2 surfaces").
    /// The budget of the route is the native law on the paid steel equivalent (the game adds its own native resistance there; the same in both versions).
    /// </summary>
    private static (CombatReportShot Shot,Replay R) Run(int number,int version,IReadOnlyList<Plate> plates,ProtectionThreat threat,double angle,double budget,bool fromBlock=false,string profile="APFSDS gen 3")
    {
        var chords=plates.SelectMany(p=>Enumerable.Range(0,p.Surfaces.Length/2).Select(i=>(P:p,S:p.Surfaces[2*i],E:p.Surfaces[2*i+1]))).OrderBy(c=>c.S).ToList();
        var blocks=new List<(double S,double E,List<Plate> Plates)>();
        foreach(var c in chords)
        {
            if(blocks.Count>0&&c.S<=blocks[^1].E+1e-9){var b=blocks[^1];if(!b.Plates.Contains(c.P))b.Plates.Add(c.P);blocks[^1]=(b.S,Math.Max(b.E,c.E),b.Plates);}
            else blocks.Add((c.S,c.E,new List<Plate>{c.P}));
        }
        var ledger=new MaterialIntervalLedger();var odd=new HashSet<int>();var lines=new Dictionary<int,SolidLineSeen>();var r=new Replay{Budget=budget};
        string Name(Plate p)=>version>=81?DamageFeedTruth.StructureLabel(p.Mesh,null,"Plate #"+p.Entity):Old32(DamageFeedTruth.StructureLabel(p.Mesh,null,"Plate #"+p.Entity));
        double cos=Math.Cos(angle*Math.PI/180);
        for(int k=0;k<blocks.Count&&!r.Killed;k++)
        {
            var (s,e,inBlock)=blocks[k];string id=$"{number}:{k}";
            if(k>0&&s>blocks[k-1].E+1e-9)
            {
                var near=blocks[k-1].Plates.Concat(inBlock).Distinct().ToArray();string gid=$"{number}:g{k}";
                r.Events.Add(new("layerStart","","Reached layer",r.Budget,null,gid,new CombatLayerInfo(gid,near.Select(p=>new CombatLayerPart(p.Entity,Name(p),p.MaterialId,p.Label,null,null,false)).ToArray(),near.Length>1,(s-blocks[k-1].E)*1000)));
                r.Current=gid;
            }
            var layer=new CombatLayerInfo(id,inBlock.Select(p=>new CombatLayerPart(p.Entity,Name(p),p.MaterialId,p.Label,
                chords.Where(c=>c.P==p&&c.S>=s-1e-9&&c.E<=e+1e-9).Sum(c=>c.E-c.S)*1000*cos,angle,true)).ToArray(),inBlock.Count>1);
            double entry=r.Budget;
            // ---- SolidCavities / SolidCavity ----
            foreach(var p in plates.Where(p=>p.Solid&&p.Table!=null))
            {
                var visible=p.Surfaces.Where(d=>!fromBlock||d>=s-1e-9).ToList();if(visible.Count==0)continue;
                var line=SolidBlock.Analyse(visible);
                SolidLineSeen? seen=null;
                if(version>=81){if(!lines.TryGetValue(p.Entity,out seen))lines[p.Entity]=seen=new();seen.See(visible);}
                if(line.Odd)
                {
                    var report=version>=81?seen!.OddLine(visible):line;
                    if(report!=null&&odd.Add(p.Entity)){Started(r,id,layer);r.Events.Add(new("materialReaction","",SolidBlock.OddText(p.Label,report),HitId:id));r.OddLines++;}
                    continue;
                }
                foreach(var c in SolidBlock.Plan(line,s,e,ledger,p.Entity,p.MaterialId,p.Label,_=>angle,a=>p.Table!.Factor(threat,a)))
                {
                    Started(r,id,layer);
                    var before=r.Budget;if(!(before>0))continue;var target=CassetteImpactPath.NativePassive(before,c.RhaMm);if(!(target<before))continue;
                    r.Budget=target;if(target<=0)r.Killed=true;
                    ledger.Commit(c.Plan);seen?.Paid(c.Passage);r.Cavity+=c.RhaMm;r.Charges++;
                    r.Events.Add(new("materialReaction","",SolidBlock.ReportText(p.Label,c,ProtectionFactorTable.Text(threat)),before,target,id,RelatedEntity:p.Entity));
                    if(version>=81)r.Events.Add(new(LayeredPlateReport.SolidMarker,"",SolidBlockMarker.Text(p.Label,c),HitId:id,RelatedEntity:p.Entity));
                }
            }
            // ---- the unchanged interval route ----
            Started(r,id,layer);
            var first=inBlock[0];string label=$"{Name(first)} [Material: {first.Label}]";
            if(r.Killed){r.Events.Add(new("nativeStop","",$"Native stop in {label}; no outgoing original core recorded.",entry,null,id));break;}
            var candidates=chords.Where(c=>c.S<e&&c.E>s).Select(c=>new MaterialInterval(c.P.Entity,c.P.MaterialId,c.P.Table?.Label??"rha",c.S,c.E,c.P.Table?.Factor(threat,angle)??1,angle)).ToArray();
            var plan=ledger.Plan(s,e,candidates,e-s);
            if(plan!=null&&plan.Pieces.Length>0)
            {
                var b=r.Budget;var t=CassetteImpactPath.NativePassive(b,plan.RhaMm);ledger.Commit(plan);r.Budget=t;
                r.Events.Add(new("materialReaction","",plan.Text,b,t,id){PaidIntervals=plan.Pieces});
                if(t<=0){r.Killed=true;r.Events.Add(new("nativeStop","",$"Native stop in {label}; no outgoing original core recorded.",b,null,id));break;}
            }
            r.Events.Add(new("plateResidual","",$"Traversed {label}; block exit includes native resistance and any listed extra reactions.",entry,r.Budget,id));
        }
        return (new CombatReportShot(number,"Outgoing",profile,1450,r.Events,0,budget,"Solid block proof",4,4,"Solid block proof"),r);
    }
    /// <summary>The (before, after) of every event that the calculation wrote, without the 0.22.81 report-only changes (the odd line, the marker).</summary>
    private static string Budgets(CombatReportShot shot)=>string.Join(";",shot.Events.Where(e=>e.Kind!=LayeredPlateReport.SolidMarker&&!e.Text.Contains("surfaces on this shot line (odd)",StringComparison.Ordinal))
        .Select(e=>$"{e.Kind}:{e.BeforeMm?.ToString("R",CultureInfo.InvariantCulture)}>{e.AfterMm?.ToString("R",CultureInfo.InvariantCulture)}"));
    private static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);
    private static double[] Box(double front,double depthMm,double angle,double skinMm=5)
    {
        double c=Math.Cos(angle*Math.PI/180),d=depthMm/1000/c,t=skinMm/1000/c;
        return new[]{front,front+t,front+d-t,front+d};
    }
    private static List<object> Rows(ForensicModel m)
    {
        var rows=new List<object>();
        foreach(var l in m.Layers)
        {
            if(l.GapMm!=null){rows.Add(new{level=0,n=l.Number.ToString(CultureInfo.InvariantCulture),colour=l.Colour,name="Air gap",detail=l.Geometry,value="-"});continue;}
            rows.Add(new{level=0,n=l.Number.ToString(CultureInfo.InvariantCulture),colour=l.Colour,name=CombatForensicGeometry.LayerName(l),detail=CombatForensicGeometry.Detail(l),value=l.Percent is {} p?CombatForensics.N(p)+"%":""});
            foreach(var s in l.Sublayers??Array.Empty<CompositeSublayer>())rows.Add(new{level=1,n="",colour=s.Colour,name=s.Label,detail=CombatForensicGeometry.SubDetail(s),value=""});
        }
        if(m.HasResidualRow)rows.Add(new{level=0,n="",colour="#8fd99c",name=m.RestLabel,detail=CombatForensics.N(m.Residual)+" mm RHA-eq",value=CombatForensics.N(m.ResidualPercent)+"%"});
        return rows;
    }
    private static object Panel(string title,ForensicModel m)
    {
        var rec=new Rec();CombatForensicDrawing.Draw(rec,m,false);
        return new{title,explanation=m.Explanation,ops=rec.Ops,rows=Rows(m)};
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT81: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        ProtectionFactorTable Table(string id)=>ProtectionFactorTable.Build(new CassetteModel(presets.First(p=>p.Id==id),materials,reactions));
        var presetLookup=presets.ToDictionary(p=>p.MaterialId,p=>p);
        CombatCompositeLayers.Configure(id=>id!=null&&presetLookup.TryGetValue(id,out var found)?found:null);
        var cases=new StringBuilder("case\tversion\tparts of the block\tlength along the line mm\tnormal thickness mm\trecipe layers\tsmallest solid layer mm\tabsorbed mm (in -> out)\twaterfall of the part mm\tcavity line\tmarker\todd line\tbudgets equal to 0.22.80\n");
        var panels=new List<object>();var jsonFields=new StringBuilder("event\tfield\t0.22.79/0.22.80 (recorded)\t0.22.81 (replayed)\n");
        try
        {
            // ================= 1. The fixture: shot 2 of the user's report with 0.22.79 on "Solid block proof" =================
            var fixture=Path.Combine(parent,"diag-0.22.79-solid-forensic","last-session-20261114-solid.json");
            using var doc=JsonDocument.Parse(File.ReadAllText(fixture));
            var opts=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
            var shots=JsonSerializer.Deserialize<CombatReportShot[]>(doc.RootElement.GetProperty("shots").GetRawText(),opts)!;
            var recorded=shots.Single(s=>s.Number==2);const int box=288;
            var t80u=Table("t80u");string label=t80u.Label;
            Check(label=="T-80U cellular cast armour","the recipe of the box is the T-80U preset ("+label+")");
            // ---- before: the report as recorded, read by 0.22.81 (no marker in it: the list comes back as it is) ----
            var before=CombatReportTruth.Normalize(recorded);
            Check(ReferenceEquals(LayeredPlateReport.Collapse(before.Events),before.Events),"before: a report without a solid-block marker (as 0.22.79/0.22.80 recorded it) comes back from LayeredPlateReport unchanged (the same list)");
            var mBefore=CombatForensics.Build(recorded);
            var boxBefore=mBefore.Layers.Where(l=>l.GapMm==null&&l.Parts.Any(p=>p.Armour&&p.Entity==box)).ToArray();
            Check(boxBefore.Length==2&&boxBefore[1].Plate.Contains("(second crossing)",StringComparison.Ordinal)&&boxBefore[0].Sublayers!.Min(s=>s.NormalMm)<.05&&Math.Abs(boxBefore[0].Percent!.Value-54.3)<.05,
                $"before (as the user saw it): two rows for the box ({boxBefore.Length}), the second '(second crossing)', the recipe pressed into the 5 mm skin (smallest layer {F(boxBefore[0].Sublayers!.Min(s=>s.NormalMm),"0.###")} mm), layer 1 {F(boxBefore[0].Percent!.Value,"0.#")} %");
            var history=new CombatReportHistory();history.Restore(shots,doc.RootElement.GetProperty("omittedShots").GetInt32());
            var htmlBefore=CombatReportHtml.Render(history);
            File.WriteAllText(Path.Combine(preview,"solid-forensic-before.html"),htmlBefore);
            var beforeDoc=System.Xml.Linq.XDocument.Parse(htmlBefore);
            Check(beforeDoc.Descendants("section").Count()==shots.Length&&!beforeDoc.Descendants("svg").Any(svg=>svg.ToString().Contains("Confirmed mat",StringComparison.Ordinal)),"before: HTML renders cleanly as XML for all "+shots.Length+" shots with 0 'Confirmed mat…' waterfall labels");
            Check(htmlBefore.Contains("2 surfaces on this shot line (odd)",StringComparison.Ordinal)&&htmlBefore.Contains("[Solid bl (second crossing)",StringComparison.Ordinal),"before: the HTML holds the false odd line and the second crossing");
            panels.Add(Panel("Shot 2 of the user's report (0.22.79, 120 mm APFSDS on 'Solid block proof'): Forensic as recorded and shown before 0.22.81",mBefore));

            // ---- the replay: the recorded calculation of shot 2 with the production pure code ----
            var skins=recorded.Events.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Where(p=>p.Entity==box).OrderBy(p=>p.StartM).ToArray();
            Check(skins.Length==2,"the fixture holds the two paid skins of the box (front and back)");
            var surfaces=new[]{skins[0].StartM,skins[0].EndM,skins[1].StartM,skins[1].EndM};
            var frontId=recorded.Events.First(e=>e.Kind=="layerStart"&&e.Layer!.Parts.Any(p=>p.Armour&&p.Entity==box)).HitId!;
            var backId=recorded.Events.Last(e=>e.Kind=="layerStart"&&e.Layer!.AirGapMm==null&&e.Layer.Parts.Any(p=>p.Armour&&p.Entity==box)).HitId!;
            var cavityEvent=recorded.Events.Single(e=>e.Kind=="materialReaction"&&e.Text.StartsWith(label+": solid block:",StringComparison.Ordinal));
            var oddEvent=recorded.Events.Single(e=>e.Text.Contains("(odd)",StringComparison.Ordinal));
            var ledger=new MaterialIntervalLedger();var lineA=SolidBlock.Analyse(surfaces);
            var charge=SolidBlock.Plan(lineA,skins[0].StartM,skins[0].EndM,ledger,box,"ebCassette_t80u",label,_=>skins[0].Angle,_=>skins[0].Factor).Single();
            var cavityText=SolidBlock.ReportText(label,charge,ProtectionFactorTable.Text(ProtectionThreat.LongRod));
            Check(cavityText==cavityEvent.Text,$"replay: the cavity line of the production SolidBlock on the four recorded surfaces is the recorded line, character for character ({cavityText})");
            var target=CassetteImpactPath.NativePassive(cavityEvent.BeforeMm!.Value,charge.RhaMm);
            Check(Math.Abs(target-cavityEvent.AfterMm!.Value)<.001,$"replay: the native law on {F(charge.RhaMm)} mm RHA-eq gives {F(target,"0.####")} mm, the recorded {F(cavityEvent.AfterMm.Value,"0.####")} mm (the calculation of 0.22.81 is that of 0.22.79)");
            var lineC=SolidBlock.Analyse(new[]{surfaces[2],surfaces[3]});
            Check(lineC.Odd&&SolidBlock.OddText(label,lineC)==oddEvent.Text,"replay: the back-skin block listed only its own two surfaces; SolidBlock gives exactly the recorded odd line ("+oddEvent.Text[..60]+"…)");
            var seen=new SolidLineSeen();seen.See(surfaces);seen.Paid(charge.Passage);seen.See(new[]{surfaces[2],surfaces[3]});
            Check(seen.OddLine(new[]{surfaces[2],surfaces[3]})==null&&seen.Surfaces==4,"B: 0.22.81 writes no odd line at the back skin: the line knows 4 surfaces and the cavity of this passage is paid");
            // what 0.22.81 records for the same shot: the odd line is not written, the marker follows the cavity line, the name keeps 48 characters
            var fullName=DamageFeedTruth.StructureLabel(CompositePlate.SolidBlockName,null,"Plate #288");
            Check(fullName=="Composite armour plate [Solid block]"&&fullName.Length==36&&Old32(fullName)=="Composite armour plate [Solid bl","the plate label: 36 characters, whole with the bound of 48 (0.22.80 cut it at 32: 'Composite armour plate [Solid bl')");
            string Rename(string text)=>text.Replace("Composite armour plate [Solid bl [","Composite armour plate [Solid block] [",StringComparison.Ordinal);
            var replayed=new List<CombatReportEvent>();
            foreach(var e in recorded.Events)
            {
                if(ReferenceEquals(e,oddEvent)){jsonFields.AppendLine($"event {recorded.Events.ToList().IndexOf(e)} (materialReaction, block {e.HitId})\twhole event\t{e.Text}\tnot written (B)");continue;}
                var layer=e.Layer is {} l?l with{Parts=l.Parts.Select(p=>p.Entity==box?p with{Name=fullName}:p).ToArray()}:null;
                var x=e with{Layer=layer,Text=Rename(e.Text)};
                if(e.Layer!=null&&e.Layer.Parts.Any(p=>p.Entity==box))jsonFields.AppendLine($"event {recorded.Events.ToList().IndexOf(e)} (layerStart {e.HitId})\tLayer.Parts[entity 288].Name\t{e.Layer.Parts.First(p=>p.Entity==box).Name}\t{fullName}");
                if(x.Text!=e.Text)jsonFields.AppendLine($"event {recorded.Events.ToList().IndexOf(e)} ({e.Kind} {e.HitId})\tText (the plate name in it)\t{e.Text}\t{x.Text}");
                replayed.Add(x);
                if(ReferenceEquals(e,cavityEvent))
                {
                    var marker=new CombatReportEvent(LayeredPlateReport.SolidMarker,"",SolidBlockMarker.Text(label,charge),HitId:e.HitId,RelatedEntity:box);
                    replayed.Add(marker);jsonFields.AppendLine($"new event after the cavity line (block {e.HitId})\tKind, Text, HitId, RelatedEntity\t(none)\tsolidBlock: {marker.Text}; HitId {e.HitId}; RelatedEntity 288");
                }
            }
            var after=recorded with{Events=replayed};
            File.WriteAllText(Path.Combine(output,"SOLID-FORENSIC-SHOT2-0.22.81.json"),JsonSerializer.Serialize(new{schemaVersion=1,omittedShots=0,shots=new[]{after}},new JsonSerializerOptions{WriteIndented=true}));
            var info=SolidBlockMarker.Parse(replayed.Single(e=>e.Kind==LayeredPlateReport.SolidMarker).Text);
            Check(info!=null&&info.Number==1&&info.Count==1&&Math.Abs(info.FrontStartM-surfaces[0])<1e-6&&Math.Abs(info.BackEndM-surfaces[3])<1e-6&&Math.Abs(info.CavityMm-charge.CavityMm)<.001&&Math.Abs(info.Factor-charge.Factor)<1e-6,"the marker reads back: passage 1 of 1, front and back skin on the ray, cavity and factor");
            // ---- after: what the user sees ----
            var mAfter=CombatForensics.Build(after);var nAfter=CombatReportTruth.Normalize(after);
            var views=CombatHitProjection.Build(nAfter);
            var boxAfter=mAfter.Layers.Where(l=>l.GapMm==null&&l.Parts.Any(p=>p.Armour&&p.Entity==box)).ToArray();
            double length=(surfaces[3]-surfaces[0])*1000;
            Check(boxAfter.Length==1&&boxAfter[0].PathMm is {} path&&Math.Abs(path-length)<.01&&Math.Abs(path-519)<1,$"A: one part for the block, {F(boxAfter.FirstOrDefault()?.PathMm??0,"0.##")} mm along the shot line (front skin + cavity + back skin = {F(length,"0.##")} mm at {F(skins[0].Angle,"0.##")}°)");
            var part=boxAfter[0];var subs=part.Sublayers!;var rec80=presetLookup["ebCassette_t80u"];
            Check(subs.Count==rec80.Layers.Length&&subs.Where(s=>!s.Air).Min(s=>s.NormalMm)>=1&&Math.Abs(subs.Sum(s=>s.NormalMm)-part.Parts[0].ThicknessMm!.Value)<1e-6,
                $"A: the {subs.Count} layers of the recipe over the whole part, none below 1 mm (smallest solid {F(subs.Where(s=>!s.Air).Min(s=>s.NormalMm),"0.##")} mm, air {F(subs.Where(s=>s.Air).Min(s=>s.NormalMm),"0.#")} mm, cast armour {F(subs[0].NormalMm,"0.#")} mm)");
            double absorbed=cavityEvent.BeforeMm!.Value-recorded.Events.Last(e=>e.HitId==backId&&e.Kind=="plateResidual").AfterMm!.Value;
            Check(part.AbsorbedMm is {} ab&&Math.Abs(ab-absorbed)<1e-9&&part.Before==cavityEvent.BeforeMm&&Math.Abs(part.Percent!.Value-100*absorbed/recorded.InitialPenetrationMm!.Value)<1e-9,
                $"A: the absorption of the part is that of the block (cavity and both skins): {F(absorbed,"0.##")} mm of {F(recorded.InitialPenetrationMm!.Value,"0.#")} ({F(part.Percent!.Value,"0.#")} %)");
            var row=views.Single(v=>v.Layer?.Parts.Any(p=>p.Armour&&p.Entity==box)==true);
            var wf=PenetrationWaterfall.Build(nAfter,views)!;double run=wf.Start,steps=0;
            foreach(var s in wf.Steps){if(run<=row.After!.Value+1e-6)break;run+=s.Delta;steps-=s.Delta;}
            Check(Math.Abs(steps-absorbed)<1e-6&&Math.Abs(row.Before!.Value-row.After!.Value-absorbed)<1e-9&&wf.Steps.Any(s=>Math.Abs(s.Delta+(cavityEvent.BeforeMm.Value-cavityEvent.AfterMm.Value))<1e-9),
                $"A: the waterfall of the part adds up to the same {F(steps,"0.##")} mm (cavity {F(cavityEvent.BeforeMm.Value-cavityEvent.AfterMm.Value,"0.##")} + skins); the row reads {F(row.Before!.Value,"0.#")} -> {F(row.After!.Value,"0.#")}");
            var paidPieces=nAfter.Events.Count(e=>e.Text.Contains("solid block: front skin",StringComparison.Ordinal));
            var merged=LayeredPlateReport.Apply(nAfter);
            var lines=merged.Events.Where(e=>e.HitId==part.HitId).ToArray();
            Check(lines.Count(e=>e.Text.Contains(", cavity =",StringComparison.Ordinal)||e.Text.Contains("solid block: front skin",StringComparison.Ordinal))==1&&row.Reaction.Split('\n').Count(l=>l.Contains("331 mm RHA-eq",StringComparison.Ordinal))==1&&paidPieces==1,
                "A: what was paid for the cavity (331 mm RHA-eq) stands once in the part (the recorded cavity line) and once in its reaction: "+row.Reaction.Replace("\n"," / "));
            var piecesSum=lines.Where(e=>e.PaidIntervals!=null).SelectMany(e=>e.PaidIntervals!).Sum(p=>p.RhaMm);
            Check(Math.Abs(piecesSum-(charge.RhaMm+skins.Sum(p=>p.RhaMm)))<1e-6,$"A: the interval line of the part is front skin + cavity + back skin = {F(piecesSum)} mm RHA-eq, no double count");
            var htmlAfter=CombatReportHtml.Render(Restore(new[]{shots[0],after}));
            File.WriteAllText(Path.Combine(preview,"solid-forensic-after.html"),htmlAfter);
            var allText=htmlAfter+string.Join("\n",views.Select(v=>v.Name+"\n"+v.Summary(1)))+mAfter.Context+string.Join("\n",mAfter.Findings);
            Check(!allText.Contains("(odd)",StringComparison.Ordinal)&&!allText.Contains(" odd",StringComparison.Ordinal),"B: no odd line in the report, Forensic or the HTML");
            Check(!mAfter.Context.Contains("crossing",StringComparison.Ordinal)&&mAfter.Layers.All(l=>!l.Plate.Contains("crossing",StringComparison.Ordinal))&&views.Where(v=>v.Layer?.Parts.Any(p=>p.Entity==box)==true).All(v=>!v.Name.Contains("crossing",StringComparison.Ordinal)),"A: no '(second crossing)' row: "+mAfter.Context);
            Check(part.Plate=="Composite armour plate [Solid block]"&&CombatForensicGeometry.Detail(part).StartsWith("Composite armour plate [Solid block] · 500 mm at 15.5° · 518.8 mm along the shot line",StringComparison.Ordinal)&&
                CountOf(htmlAfter,"[Solid bl")==CountOf(htmlAfter,"[Solid block]"),"A: the name is whole in Forensic and the report: "+CombatForensicGeometry.Detail(part));
            var json=JsonSerializer.Serialize(mAfter);
            Check(!json.Contains("\"Solid\"",StringComparison.Ordinal),"the Solid flag of a Forensic layer is not serialised (the JSON of a model without a solid block is that of 0.22.80)");
            panels.Add(Panel("Shot 2 replayed with 0.22.81 (same calculation; the block is one part from front skin to back skin with the layers of the T-80U recipe)",mAfter));
            cases.AppendLine(string.Join("\t","fixture shot 2 (recorded)","0.22.79",boxBefore.Length,F(boxBefore.Sum(l=>l.PathMm??0),"0.#"),F(boxBefore[0].Parts[0].ThicknessMm!.Value,"0.#"),boxBefore[0].Sublayers!.Count,F(boxBefore[0].Sublayers!.Where(s=>!s.Air).Min(s=>s.NormalMm),"0.###"),
                F(boxBefore.Sum(l=>l.AbsorbedMm??0),"0.##"),"-",1,0,1,"recorded"));
            cases.AppendLine(string.Join("\t","fixture shot 2 (replayed)","0.22.81",boxAfter.Length,F(part.PathMm!.Value,"0.##"),F(part.Parts[0].ThicknessMm!.Value,"0.#"),subs.Count,F(subs.Where(s=>!s.Air).Min(s=>s.NormalMm),"0.##"),F(absorbed,"0.##"),F(steps,"0.##"),1,1,0,"recorded calculation"));

            // ================= 2. Replayed lines: recipes, the Challenger box, two passages, odd, HESH =================
            var t72=Table("t72m1");var burl=Table("burlington");
            Plate SolidBox(ProtectionFactorTable table,double[] s,int entity=301)=>new(entity,CompositePlate.SolidBlockName,"ebCassette_"+(table==t80u?"t80u":table==t72?"t72m1":"burlington"),table.Label,table,s,true);
            Plate Hull(double at,double mm,double angle,int entity=27)=>new(entity,"Plate #"+entity,"rha","Rolled Homogeneous Armour",null,new[]{at,at+mm/1000/Math.Cos(angle*Math.PI/180)},false);
            var scenes=new List<(string Name,Plate[] Plates,ProtectionThreat Threat,double Angle,double Budget,bool FromBlock,double Length,int Parts,bool Stop)>
            {
                ("T-80U box 500 mm, straight",new[]{SolidBox(t80u,Box(1,500,0)),Hull(1.86,300,0)},ProtectionThreat.LongRod,0,502.34,false,500,1,false),
                ("T-80U box 500 mm, 15.5 deg, each block lists its own surfaces on (the user's line)",new[]{SolidBox(t80u,Box(1,500,15.46)),Hull(1.9,300,15.46)},ProtectionThreat.LongRod,15.46,502.34,true,500/Math.Cos(15.46*Math.PI/180),1,false),
                ("T-72M1 box 500 mm (recipe without air), straight",new[]{SolidBox(t72,Box(1,500,0)),Hull(1.86,300,0)},ProtectionThreat.LongRod,0,700,false,500,1,false),
                ("Challenger 1 box 600 mm (Burlington), straight",new[]{SolidBox(burl,Box(1,600,0),287),Hull(1.95,300,0)},ProtectionThreat.LongRod,0,700,false,600,1,false),
                ("Challenger 1 box 600 mm (Burlington), 30 deg",new[]{SolidBox(burl,Box(1,600,30),287),Hull(2.05,300,30)},ProtectionThreat.LongRod,30,700,false,600/Math.Cos(30*Math.PI/180),1,false),
                ("two passages through one block (300 mm each)",new[]{SolidBox(t80u,Box(1,300,0).Concat(Box(1.6,300,0)).ToArray()),Hull(2.3,300,0)},ProtectionThreat.LongRod,0,900,false,300,2,false),
                ("odd: three surfaces (open side)",new[]{SolidBox(t80u,new[]{1,1.005,1.495}),Hull(1.86,300,0)},ProtectionThreat.LongRod,0,502.34,false,5,0,false),
                ("HESH stops in the cavity (T-80U box 500 mm)",new[]{SolidBox(t80u,Box(1,500,0)),Hull(1.86,300,0)},ProtectionThreat.Hesh,0,450,false,500,1,true),
            };
            int number=10;
            foreach(var sc in scenes)
            {
                var (s80,r80)=Run(number,80,sc.Plates,sc.Threat,sc.Angle,sc.Budget,sc.FromBlock);var (s81,r81)=Run(number,81,sc.Plates,sc.Threat,sc.Angle,sc.Budget,sc.FromBlock);number++;
                var solid=sc.Plates[0];
                bool sameBudgets=Budgets(s80)==Budgets(s81)&&r80.Budget==r81.Budget&&r80.Cavity==r81.Cavity&&r80.Charges==r81.Charges;
                Check(sameBudgets,$"{sc.Name}: the calculation is that of 0.22.80 (every budget of every event, cavity {F(r81.Cavity)} mm RHA-eq in {r81.Charges} charge(s), end {F(r81.Budget,"0.##")} mm)");
                var n80=CombatReportTruth.Normalize(s80);
                Check(ReferenceEquals(LayeredPlateReport.Collapse(n80.Events),n80.Events),
                    $"{sc.Name}: the 0.22.80 record (no marker) reads as it did");
                var m=CombatForensics.Build(s81);var n=CombatReportTruth.Normalize(s81);var vs=CombatHitProjection.Build(n);
                var own=m.Layers.Where(l=>l.GapMm==null&&l.Parts.Any(p=>p.Armour&&p.Entity==solid.Entity)).ToArray();
                var text=string.Join("\n",vs.Select(v=>v.Summary(1)))+m.Context+string.Join("\n",m.Findings)+CombatReportHtml.Render(Restore(new[]{s81}));
                int markers=s81.Events.Count(e=>e.Kind==LayeredPlateReport.SolidMarker);
                if(sc.Parts==0)
                {
                    Check(markers==0&&r81.OddLines==1&&r80.OddLines==1&&text.Contains("3 surfaces on this shot line (odd)",StringComparison.Ordinal)&&own.Length==1&&own[0].Sublayers is {Count:>0}&&own[0].PathMm<6,
                        $"{sc.Name}: a real odd count keeps its line with the right count (3 surfaces) and the display of 0.22.80 (skin only, no part over a cavity)");
                    cases.AppendLine(string.Join("\t",sc.Name,"0.22.81",own.Length,F(own.Sum(l=>l.PathMm??0),"0.#"),"-","-","-",F(own.Sum(l=>l.AbsorbedMm??0),"0.##"),"-",0,0,r81.OddLines,sameBudgets));
                    continue;
                }
                Check(own.Length==sc.Parts&&own.All(l=>l.Solid&&l.PathMm is {} p&&Math.Abs(p-sc.Length)<.01),$"{sc.Name}: {own.Length} part(s) for the block, each {string.Join(" / ",own.Select(l=>F(l.PathMm??0,"0.##")))} mm along the line (expected {sc.Parts} x {F(sc.Length,"0.##")})");
                var preset=presetLookup[solid.MaterialId];
                foreach(var l in own)
                {
                    var ss=l.Sublayers!;
                    Check(ss.Count==preset.Layers.Length&&ss.Where(x=>!x.Air).Min(x=>x.NormalMm)>=1&&Math.Abs(ss.Sum(x=>x.NormalMm)-l.Parts[0].ThicknessMm!.Value)<1e-6&&ss.Select(x=>x.Air).SequenceEqual(preset.Layers.Select(x=>x.Material=="air")),
                        $"{sc.Name}, layer {l.Number}: the {ss.Count} recipe layers over {F(l.Parts[0].ThicknessMm!.Value,"0.#")} mm normal thickness, air as air, smallest solid {F(ss.Where(x=>!x.Air).Min(x=>x.NormalMm),"0.##")} mm");
                }
                // two passages: the second part is the second crossing of the shape by the line (named so, with its own length); the back skin of a passage never is
                bool crossings=sc.Parts==1?!text.Contains("crossing",StringComparison.Ordinal):own[0].Plate==CompositePlate.SolidBlockName&&own[1].Plate==CompositePlate.SolidBlockName+" (second crossing)"&&CountOf(text,"(third crossing)")==0;
                Check(markers==sc.Parts&&r81.OddLines==0&&!text.Contains("(odd)",StringComparison.Ordinal)&&crossings,$"{sc.Name}: {markers} marker(s), no odd line, "+(sc.Parts==1?"no second crossing":"the second passage named as the second crossing of the shape: "+string.Join(" | ",own.Select(l=>l.Plate)))+(r80.OddLines>0?$" (0.22.80 wrote {r80.OddLines} false odd line(s) here)":""));
                // the absorption of each part equals what the blocks of its passage took
                foreach(var l in own)
                {
                    var v=vs.Single(x=>x.Id==l.HitId);
                    double outMm=v.Status.StartsWith("Stopped",StringComparison.Ordinal)?0:v.After!.Value;
                    Check(l.AbsorbedMm is {} taken&&Math.Abs(taken-(v.Before!.Value-outMm))<1e-9,$"{sc.Name}, layer {l.Number}: absorbed {F(l.AbsorbedMm??0,"0.##")} mm = in {F(v.Before!.Value,"0.##")} - out {F(outMm,"0.##")} ({v.Status})");
                }
                if(sc.Stop)
                {
                    var l=own[0];var stop=l.PlateStop;double f=solid.Table!.Factor(sc.Threat,0),expected=(5+sc.Budget/f)/500;
                    Check(m.Arrested&&l.After==0&&stop!=null&&Math.Abs(stop.Fraction-expected)<.001&&m.Explanation.Contains($"layer {stop.Group} of {stop.Groups} of the plate",StringComparison.Ordinal),
                        $"{sc.Name}: the part shows where: arrested at {F(100*(stop?.Fraction??0),"0.#")} % of the block (5 mm skin + {F(sc.Budget/f,"0.#")} mm of cavity at factor {F(f,"0.####")}), recipe layer {stop?.Group} of {stop?.Groups}: {m.Explanation}");
                    var rec=new Rec();CombatForensicDrawing.Draw(rec,m,false);
                    Check(rec.Texts.Any(t=>t.StartsWith($"Arrested in layer {stop!.Group} of {stop.Groups}",StringComparison.Ordinal)),"the cross-section marks the arrest inside the part");
                }
                cases.AppendLine(string.Join("\t",sc.Name,"0.22.81",own.Length,string.Join("/",own.Select(l=>F(l.PathMm??0,"0.##"))),string.Join("/",own.Select(l=>F(l.Parts[0].ThicknessMm??0,"0.#"))),own[0].Sublayers!.Count,
                    F(own.Min(l=>l.Sublayers!.Where(x=>!x.Air).Min(x=>x.NormalMm)),"0.##"),string.Join("/",own.Select(l=>F(l.AbsorbedMm??0,"0.##"))),"-",r81.Charges,markers,r81.OddLines,sameBudgets));
                if(sc.Name.StartsWith("Challenger",StringComparison.Ordinal))panels.Add(Panel(sc.Name+" (replayed hit path, 0.22.81)",m));
                if(sc.Stop)panels.Add(Panel(sc.Name+" (replayed hit path, 0.22.81): the arrest is drawn inside the part",m));
            }
            // ---- a marker that cannot be merged (no front block in the report) is dropped; the rest stays ----
            {
                var lone=new List<CombatReportEvent>{new("layerStart","","Reached layer",500,null,"x:0",new CombatLayerInfo("x:0",new[]{new CombatLayerPart(5,"Plate #5","rha","Rolled Homogeneous Armour",50,0,true)},false)),
                    new(LayeredPlateReport.SolidMarker,"","garbage",HitId:"x:1",RelatedEntity:9)};
                var c=LayeredPlateReport.Collapse(lone);
                Check(c.Count==1&&ReferenceEquals(c[0],lone[0]),"a marker that cannot be read is dropped; the rest of the report stays as recorded");
            }

            // ================= 3. Without a solid block: the readers are unchanged (C) =================
            {
                var files=new List<string>();
                foreach(var dir in new[]{Path.Combine(root,"tests","fixtures"),parent})
                    if(Directory.Exists(dir))files.AddRange(Directory.EnumerateFiles(dir,"last-session*.json",dir==parent?SearchOption.AllDirectories:SearchOption.TopDirectoryOnly)
                        .Where(f=>!f.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)&&!f.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)&&!Path.GetFileName(Path.GetDirectoryName(f)!).StartsWith("out-",StringComparison.Ordinal)&&!f.Contains(Path.DirectorySeparatorChar+"src-",StringComparison.Ordinal)));
                files=files.Distinct().OrderBy(f=>f,StringComparer.Ordinal).ToList();
                int shotsSeen=0,same=0,withMarker=0;var listed=new StringBuilder("file\tshots\tunchanged by LayeredPlateReport (same list or equal events)\n");
                foreach(var f in files)
                {
                    CombatReportShot[] ss;
                    try{using var d=JsonDocument.Parse(File.ReadAllText(f));ss=JsonSerializer.Deserialize<CombatReportShot[]>(d.RootElement.GetProperty("shots").GetRawText(),opts)!;}catch(Exception){continue;}
                    int fileSame=0;
                    foreach(var s in ss)
                    {
                        var nn=CombatReportTruth.Normalize(s);shotsSeen++;
                        if(nn.Events.Any(e=>e.Kind==LayeredPlateReport.SolidMarker)){withMarker++;continue;}
                        // the reference behaviour of 0.22.80: without a layered marker the same list, with one the CollapseOne result; 0.22.81 adds nothing to either
                        var c=LayeredPlateReport.Collapse(nn.Events);
                        bool ok=nn.Events.Any(e=>e.Kind==LayeredPlateReport.Marker&&e.RelatedEntity!=null)?c.All(e=>e.Kind!=LayeredPlateReport.SolidMarker)&&!c.Any(LayeredPlateReport.IsSolidMerged):ReferenceEquals(c,nn.Events);
                        if(ok){same++;fileSame++;}
                    }
                    listed.AppendLine($"{Path.GetRelativePath(parent,f)}\t{ss.Length}\t{fileSame}");
                }
                File.WriteAllText(Path.Combine(output,"SOLID-FORENSIC-SWEEP.tsv"),listed.ToString());
                Check(files.Count>=8&&shotsSeen>=100&&withMarker==0&&same==shotsSeen,$"C: {files.Count} saved sessions of the user ({shotsSeen} shots, none with a solid-block marker): LayeredPlateReport leaves every one as 0.22.80 did ({same} of {shotsSeen}); HTML/SVG/JSON are compared with the 0.22.80 build in RENDER-COMPARE.tsv");
            }

            // ================= 4. Source: no hook, the calculation untouched =================
            {
                var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
                Check(!gp.Contains("[HarmonyPatch",StringComparison.Ordinal),"no hook in RuntimeGroupedProtection.cs");
                var body=gp[gp.IndexOf("private static void SolidCavity(",StringComparison.Ordinal)..gp.IndexOf("#endif",gp.IndexOf("private static void SolidCavity(",StringComparison.Ordinal),StringComparison.Ordinal)];
                Check(body.Contains("var charges=SolidBlock.Plan(line,start,end,ray.Ledger,entity,material,table.Label,EntryAngle,",StringComparison.Ordinal)&&!body.Contains("SolidBlock.Plan(whole",StringComparison.Ordinal)&&body.Contains("if(!ApplyPassive(reached,entity,\"Solid block cavity\",charge.RhaMm,out var before,out var after))",StringComparison.Ordinal),
                    "the cavity is still planned on the line of this block (not on the whole line) and paid by the same ApplyPassive");
                var dfm=File.ReadAllText(Path.Combine(root,"DamageFeedModel.cs")); // herpak: the comparison with src-0.22.80 is removed; the 48-character bound is checked on the current source
                Check(dfm.Contains("clean.Length>48?clean[..48]:clean",StringComparison.Ordinal)&&!dfm.Contains("clean.Length>32?clean[..32]:clean",StringComparison.Ordinal),"DamageFeedModel.cs: the label bound is 48");
            }
        }
        catch(Exception error){Check(false,"FEAT81 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        finally{CombatCompositeLayers.Configure(null);}
        File.WriteAllText(Path.Combine(output,"SOLID-FORENSIC-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-FORENSIC-JSON-FIELDS.tsv"),jsonFields.ToString());
        File.WriteAllText(Path.Combine(preview,"solid-forensic-panels.json"),JsonSerializer.Serialize(panels));
        File.WriteAllText(Path.Combine(output,"FEAT81-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT81 PASS: {checks} checks, {failures} failures");
    }
    private static int CountOf(string text,string part){int n=0,i=0;while((i=text.IndexOf(part,i,StringComparison.Ordinal))>=0){n++;i+=part.Length;}return n;}
    private static CombatReportHistory Restore(IEnumerable<CombatReportShot> shots){var h=new CombatReportHistory();h.Restore(shots,0);return h;}
}
