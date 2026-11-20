using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.78 HESH (user 14-11, "Ja, zo doen"): below minAngleDegrees a native HESH ricochet becomes a stop where it struck (the head detonates on
/// the surface; the reflected and gouge children are stopped); a perforation ends in a scab whose mass grows with calibre^3 and the body that
/// carried it is consumed. The native block outcome is the LOS-MAP model of SimulateFragment (test-only); the HESH decisions, the scab sizes, the
/// report wording and the presentation are the production code. The DamageFeed.End and SimulateFragment child bookkeeping are replayed here.
/// </summary>
internal static class Feat02278HeshTests
{
    /// <summary>The production files 0.22.78 may change on top of 0.22.77.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeSpall.cs" or "RuntimeArmourResponses.cs" or "RuntimeDamageFeed.cs" or "ShellBalance.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    /// <summary>Files whose 0.22.78 change consists only of marked insertions ("// 0.22.78 HESH begin" ... "// 0.22.78 HESH end" and lines ending in "// 0.22.78 HESH").</summary>
    internal static readonly string[] Marked={"RuntimeSpall.cs","RuntimeArmourResponses.cs","RuntimeDamageFeed.cs","ShellBalance.cs"};
    internal static string Unhesh(string text)
    {
        text=Feat02286HeTests.Un86(text); // 0.22.86: without the marked HE change first (TEST-ADAPTATIONS.md)
        var lines=text.Split('\n');var kept=new List<string>(lines.Length);bool skip=false;
        foreach(var line in lines)
        {
            var t=line.Trim();
            if(t=="// 0.22.78 HESH begin"){skip=true;continue;}
            if(t=="// 0.22.78 HESH end"){skip=false;continue;}
            if(skip||line.TrimEnd('\r').EndsWith("// 0.22.78 HESH",StringComparison.Ordinal))continue;
            kept.Add(line);
        }
        return string.Join("\n",kept);
    }
    /// <summary>The bytes of a source file without its marked 0.22.78 HESH insertions (unchanged bytes for any other file).</summary>
    internal static byte[] UnheshBytes(string path)
    {
        var bytes=Feat02286HeTests.Un86Bytes(path); // 0.22.86: without the marked HE change first (TEST-ADAPTATIONS.md)
        return Marked.Contains(Path.GetFileName(path))?Encoding.UTF8.GetBytes(Unhesh(Encoding.UTF8.GetString(bytes))):bytes;
    }

    // ---------------- managed fragment array (reference semantics, as the boxed IL2CPP Fragment) ----------------
    internal sealed class Frag{internal ushort Flags;internal float Speed;internal int Parent=-1;internal Frag Copy()=>new(){Flags=Flags,Speed=Speed,Parent=Parent};}
    private readonly struct Frags:IHeshFragments
    {
        internal readonly List<Frag> List;internal Frags(List<Frag> list)=>List=list;
        public int Count=>List.Count;
        public ushort Flags(int index)=>List[index].Flags;
        public int Parent(int index)=>List[index].Parent;
        public void SetFlags(int index,ushort flags)=>List[index].Flags=flags;
        public void Stop(int index){List[index].Speed=0;List[index].Flags|=HeshRules.Killed;}
    }
    private const ushort O=HeshRules.Original,K=HeshRules.Killed,D=HeshRules.Deflected,E=HeshRules.Embedded,P=HeshRules.Penetrated,PC=HeshRules.Precomputed,GS=HeshRules.GenerateSpalling,ModelExit=2;
    private enum Native{Perforated,Ricochet,Embedded}
    /// <summary>One native block (LOS-MAP section 1, Sprocket 0.2.56.2): B = budget x apModifier / rha x [cos^(e-1)(1-om)+om]; perforation if B &gt; path,
    /// ricochet if B &lt;= d and at least 10 degrees, otherwise embedded.</summary>
    private static (Native Outcome,double Effective,double Path) Block(double budget,double apModifier,double rha,double exponent,double angle,double normalMm,double dMm,double overmatch)
    {
        var b=Feat02290NativeDivisorTests.NativeBudget(budget,apModifier,rha,exponent,angle,overmatch); // 0.22.90 adapted: the native order B=b*ap/rha; om<1: (B-c*B)*om+c*B (VANISH-MAP section 2)
        var path=ProtectionFactorTable.PathMm(normalMm,angle);
        var used=b>path?path:b; // 0.22.90 adapted: the native charge (1D78360)
        return (b/path>1?Native.Perforated:!(used>dMm)&&angle>=HeshSettings.NativeRicochetDegrees?Native.Ricochet:Native.Embedded,b,path); // 0.22.90 adapted: native tests (perforation B/path>1; embedded when used>d or under 10 degrees)
    }
    /// <summary>The fragments SimulateFragment leaves for the original (index 0) and its children (disassembly 1D78A35/1D78CDC, 1D783B9/1D784C0/1D78858, 1D78972/1D789E5).</summary>
    private static List<Frag> Outcome(Native outcome,double angle,bool gouge,float speed=600)
    {
        var list=new List<Frag>{new(){Flags=O,Speed=speed}};
        switch(outcome)
        {
            case Native.Perforated:list[0].Flags|=P;list.Add(new(){Flags=O|PC|GS,Speed=speed*.6f,Parent=0});break;
            case Native.Ricochet:
                list[0].Flags|=D;
                if(gouge)list.Add(new(){Flags=O|D|E,Speed=speed*.2f,Parent=0});
                list.Add(new(){Flags=O|GS,Speed=(float)(speed*Math.Sqrt(Math.Max(0,.9-.7*Math.Cos(angle*Math.PI/180)))),Parent=0});break;
            default:list[0].Flags|=E;list.Add(new(){Flags=O|ModelExit|E,Speed=0,Parent=0});break;
        }
        return list;
    }
    private sealed record Plate(int Entity,string MaterialId,string MaterialLabel,double ThicknessMm,double Angle);
    /// <summary>Replay of RuntimeDamageFeed.End for the original and, for a native embedded stop, its pending child (same texts and kinds).</summary>
    private static List<CombatReportEvent> Report(List<Frag> f,Plate plate,double entry,double? remaining,HeshSettings s,bool hesh,bool burst)
    {
        var id="7:0:2:3";var label=$"Plate #{plate.Entity} [Material: {plate.MaterialLabel}]";
        var events=new List<CombatReportEvent>{new("layerStart","","Reached layer",entry,null,id,new CombatLayerInfo(id,new[]{new CombatLayerPart(plate.Entity,"Plate #"+plate.Entity,plate.MaterialId,plate.MaterialLabel,plate.ThicknessMm,plate.Angle,true)},false))};
        int Live(int i){int n=0;for(int j=i+1;j<f.Count;j++)if(f[j].Parent==i&&(f[j].Flags&O)!=0&&(f[j].Flags&K)==0)n++;return n;}
        PlateOutcome Of(int i)=>DamageFeedTruth.Outcome((f[i].Flags&P)!=0,(f[i].Flags&E)!=0,(f[i].Flags&D)!=0,Live(i));
        var outcome=Of(0);
        if(outcome==PlateOutcome.Unknown&&(f[0].Flags&E)!=0&&Live(0)==1)
        {
            var child=f.FindIndex(1,x=>x.Parent==0&&(x.Flags&O)!=0&&(x.Flags&K)==0);
            if(child>0)outcome=Of(child);
        }
        if(outcome==PlateOutcome.Penetrated)events.Add(new("plateResidual","","Traversed "+label+"; block exit includes native resistance and any listed extra reactions.",entry,remaining,id));
        else if(outcome==PlateOutcome.Stopped)events.Add(new("nativeStop","","Native stop in "+label+"; no outgoing original core recorded.",entry,null,id));
        else if(outcome==PlateOutcome.Deflected)events.Add(new("outcome","","Deflected at "+label,null,null,id));
        if(outcome==PlateOutcome.Stopped&&s.SurfaceDetonation&&hesh&&!burst)events.Add(new("materialNote","",HeshRules.SurfaceText,null,null,id));
        return events;
    }
    private static CombatReportShot Shot(List<CombatReportEvent> events,double entry)=>new(1,"Outgoing","HESH",520,events,0,entry,"Target tank",null,null,"Own tank");
    private static string F(double v,string format="0.###")=>v.ToString(format,CultureInfo.InvariantCulture);
    private static bool Same(List<Frag> a,List<Frag> b)=>a.Count==b.Count&&a.Zip(b).All(x=>x.First.Flags==x.Second.Flags&&x.First.Speed==x.Second.Speed&&x.First.Parent==x.Second.Parent);
    private static List<Frag> Clone(List<Frag> l)=>l.Select(x=>x.Copy()).ToList();

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT78: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot();Directory.CreateDirectory(output);
        var profile=ShellProfiles.Parse(ReleaseProfiles.Defaults()).First(p=>p.Id=="hesh");
        var spallDefaults=new SpallSettings();var e=spallDefaults.HeshObliquityExponent;
        var on=HeshSettings.Default;var legacy=new HeshSettings(10,true);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var t72=ProtectionFactorTable.Build(new CassetteModel(presets.First(p=>p.Id=="t72m1"),materials,reactions));
        (double Budget,double D,double Mass) Hesh(double calibre){var g=ShellBallistics.CoreGeometry(calibre,profile.Settings);return (ShellBalance.ChemicalPenetration(profile,calibre),g.Diameter*1000,g.Mass);}
        var cases=new StringBuilder("case\tcalibre mm\tplate\tangle deg\tbudget B mm\td mm\teffective B mm\tpath mm\tovermatch\tnative outcome\t0.22.78 outcome\tchildren stopped\tresult word 0.22.78\tresult word 0.22.77\n");
        var reportLines=new StringBuilder();

        // ---------- 0. the HESH penetrator d (the native ricochet rule compares B with it) ----------
        Check(e==0&&profile.Behavior=="hesh"&&Math.Abs(profile.Settings.DiameterRatio-.9)<1e-12&&profile.ChemicalPenetrationMm==140,"HESH profile: exponent 0, penetratorDiameterFactor 0.9, 140 mm at 100 mm (budget 1.4 x calibre) unchanged");
        var h152=Hesh(152);var h120=Hesh(120);
        Check(Math.Abs(h152.D-136.8)<1e-9&&Math.Abs(h120.D-108)<1e-9&&Math.Abs(h152.Budget-212.8)<1e-9&&Math.Abs(h120.Budget-168)<1e-9,$"d = 0.9 x calibre: 152 mm -> d {F(h152.D)} mm, B {F(h152.Budget)} mm, mass {F(h152.Mass,"0.000")} kg; 120 mm -> d {F(h120.D)} mm, B {F(h120.Budget)} mm, mass {F(h120.Mass,"0.000")} kg; B/d = {F(h152.Budget/h152.D)}");

        // ---------- 1. surface detonation, not a ricochet, below 75 degrees ----------
        string Word(List<CombatReportEvent> events,double entry)=>HitBlockBuilder.Build(Shot(events,entry)).Result;
        void Case(string name,double calibre,string plateName,double budget,double rha,double angle,double normalMm,double dMm,double mass,bool gouge,string expected,Plate plate)
        {
            var om=ProtectionFactorTable.Overmatch(mass,1000,dMm/1000,normalMm/1000/Math.Cos(angle*Math.PI/180),angle); // assumed upper bound 1000 m/s
            var block=Block(budget,1,rha,e,angle,normalMm,dMm,om);
            var before=Outcome(block.Outcome,angle,gouge);var after=Clone(before);
            var r=HeshRules.Surface(new Frags(after),0,1,angle,on.MinAngleDegrees);
            var outcome=(after[0].Flags&E)!=0?"stopped":(after[0].Flags&D)!=0?"ricochet":(after[0].Flags&P)!=0?"perforated":"?";
            var wordNew=Word(Report(after,plate,budget,null,on,true,false),budget);var wordOld=Word(Report(before,plate,budget,null,legacy,true,false),budget);
            cases.AppendLine($"{name}\t{F(calibre)}\t{plateName}\t{F(angle)}\t{F(budget)}\t{F(dMm)}\t{F(block.Effective)}\t{F(block.Path)}\t{om:0.0e0}\t{block.Outcome}\t{outcome}\t{r.ChildrenStopped}\t{wordNew}\t{wordOld}");
            Check(outcome==expected,$"{name}: native {block.Outcome} (B {F(block.Effective)} mm vs path {F(block.Path)} mm, d {F(dMm)} mm) -> 0.22.78 {outcome} (expected {expected})");
            if(expected=="stopped")Check(wordNew==HitBlockBuilder.Stopped&&after.Skip(1).All(x=>x.Speed==0&&(x.Flags&K)!=0||x.Speed==0&&(x.Flags&E)!=0),$"{name}: hit panel says {wordNew}, no child flies on");
            if(expected=="ricochet")Check(wordNew==HitBlockBuilder.Ricochet&&Same(before,after),$"{name}: from the limit on the native ricochet stays exactly as it was ({wordNew})");
        }
        var Rha=new Func<double,double,Plate>((t,a)=>new Plate(12,"rha","Rolled homogeneous armour",t,a));
        foreach(var calibre in new[]{152d,120d})foreach(var angle in new[]{0d,30,60})
        {
            var h=Hesh(calibre);
            Case($"{calibre:0} mm on 300 mm RHA at {angle:0} deg",calibre,"300 mm RHA",h.Budget,1,angle,300,h.D,h.Mass,false,"stopped",Rha(300,angle));
        }
        // The cassette of the 14-11 log (plate #324, ebCassette_t72m1, 44 degrees, 3.2 mm left after ERA and gaps), at 75 and 80 degrees, and with the full budget.
        foreach(var (budget,angle,expected,label) in new[]{(3.2,44d,"stopped","log 14-11 (3.2 mm left)"),(3.2,80d,"ricochet","3.2 mm left"),(3.2,75d,"ricochet","3.2 mm left, at the limit"),(212.8,44d,"stopped","full budget"),(3.2,74.9d,"stopped","3.2 mm left, just below the limit")})
        {
            var factor=t72.Factor(ProtectionThreat.Hesh,angle);var nativeRha=ProtectionFactorTable.NativeRhaFactor(factor,angle,e);
            Case($"152 mm on ebCassette_t72m1 at {angle:0.#} deg, {label}",152,$"ebCassette_t72m1 {F(t72.DepthMm)} mm (HESH factor {F(factor)}, rhaFactor written {F(nativeRha)})",budget,nativeRha,angle,t72.DepthMm,h152.D,h152.Mass,angle<70,expected,new Plate(324,"ebCassette_t72m1",t72.Label,t72.DepthMm,angle));
        }
        // A drained budget on plain RHA (map example b: 125.6 mm at 20 degrees on 200 mm): native ricochet, now a stop.
        Case("152 mm, 125.6 mm left, on 200 mm RHA at 20 deg",152,"200 mm RHA",125.6,1,20,200,h152.D,h152.Mass,true,"stopped",Rha(200,20));
        Case("152 mm, 125.6 mm left, on 200 mm RHA at 80 deg (native: B/cos > d, no ricochet)",152,"200 mm RHA",125.6,1,80,200,h152.D,h152.Mass,true,"stopped",Rha(200,80));
        // The native rule never ricochets below 10 degrees, and a perforation is never touched.
        {
            var f=Outcome(Native.Perforated,30,false);var g=Clone(f);HeshRules.Surface(new Frags(g),0,1,30,75);Check(Same(f,g),"a perforation is never changed by the surface rule");
            f=Outcome(Native.Embedded,30,false);g=Clone(f);HeshRules.Surface(new Frags(g),0,1,30,75);Check(Same(f,g),"a native stop stays exactly as it is (the report adds the HESH line)");
            f=Outcome(Native.Ricochet,40,true);g=Clone(f);HeshRules.Surface(new Frags(g),0,1,double.NaN,75);Check(Same(f,g),"no measured angle: the native ricochet stays");
            f=Outcome(Native.Ricochet,40,true);f.Add(new(){Flags=0,Speed=300,Parent=0});f.Add(new(){Flags=O|GS,Speed=300,Parent=5});g=Clone(f);var r=HeshRules.Surface(new Frags(g),0,1,40,75);
            Check(r.Converted&&r.ChildrenStopped==2&&g[3].Speed==300&&g[3].Flags==0&&g[4].Speed==300&&(g[0].Flags&(D|E))==E,"only the original children of this call are stopped; spall (no original bit) and other parents keep their state");
        }

        // ---------- 5. report, hit panel, Dutch summary, forensic, HTML; forbidden words ----------
        {
            var budget=3.2;var angle=44d;var factor=t72.Factor(ProtectionThreat.Hesh,angle);var nativeRha=ProtectionFactorTable.NativeRhaFactor(factor,angle,e);
            var block=Block(budget,1,nativeRha,e,angle,t72.DepthMm,h152.D,0);var before=Outcome(block.Outcome,angle,true);var after=Clone(before);
            HeshRules.Surface(new Frags(after),0,1,angle,on.MinAngleDegrees);
            var plate=new Plate(324,"ebCassette_t72m1",t72.Label,t72.DepthMm,angle);
            var shotNew=Shot(Report(after,plate,budget,null,on,true,false),budget);var shotOld=Shot(Report(before,plate,budget,null,legacy,true,false),budget);
            var hitNew=HitBlockBuilder.Build(shotNew);var hitOld=HitBlockBuilder.Build(shotOld);
            var nlNew=CombatHitSummaryModel.Build(shotNew);var nlOld=CombatHitSummaryModel.Build(shotOld);
            var viewNew=CombatReportView.Build(new[]{shotNew})[0];var viewOld=CombatReportView.Build(new[]{shotOld})[0];
            var forNew=CombatForensics.Build(shotNew);var forOld=CombatForensics.Build(shotOld);
            var history=new CombatReportHistory();history.Begin();history.Add(shotNew);var html=CombatReportHtml.Render(history);
            Check(hitOld.Result==HitBlockBuilder.Ricochet&&nlOld.Header.Contains("AFGEKETST",StringComparison.Ordinal)&&forOld.Outcome=="RICOCHET","0.22.77 (and legacy settings): hit panel RICOCHET, Dutch AFGEKETST, forensic RICOCHET");
            Check(hitNew.Result==HitBlockBuilder.Stopped&&!hitNew.Detail.Contains("ricochet",StringComparison.OrdinalIgnoreCase)&&hitNew.Detail.Contains("stopped in",StringComparison.Ordinal),$"hit panel: {hitNew.Result} · {hitNew.Detail}");
            Check(nlNew.Header.Contains("GESTOPT",StringComparison.Ordinal)&&nlNew.Detail.Contains("gestopt in",StringComparison.Ordinal)&&!(nlNew.Header+nlNew.Detail).Contains("AFGEKETST",StringComparison.OrdinalIgnoreCase),$"Dutch summary: {nlNew.Header} | {nlNew.Detail}");
            var row=viewNew.Layers[0];
            Check(viewNew.Layers.Length==1&&row.Reaction.Contains(HeshRules.SurfaceText,StringComparison.Ordinal)&&row.BeforeAfter.Contains("(stopped)",StringComparison.Ordinal)&&!viewOld.Layers[0].Reaction.Contains("HESH",StringComparison.Ordinal),$"report row: {row.BeforeAfter} | {row.Reaction}");
            Check(forNew.Outcome=="STOPPED"&&forNew.Explanation.StartsWith("Projectile arrested in layer 1",StringComparison.Ordinal)&&forNew.Layers.Count==1&&forNew.Envelope?.Perforated!=true,$"forensic: {forNew.Outcome}, {forNew.Explanation} (stopped at the outer plate, layer 1)");
            Check(html.Contains(HeshRules.SurfaceText,StringComparison.Ordinal)&&!html.Contains(">RICOCHET<",StringComparison.Ordinal),"HTML export carries the text and no ricochet");
            // Forbidden words: every visible text of the new shot (hit panel, Dutch summary, report rows, forensic, HTML without the Technical log).
            var exempt=new[]{"Plate #324",t72.Label,"Target tank","Own tank","HESH"};
            var texts=new List<string>{hitNew.Result,hitNew.Detail,hitNew.Values,hitNew.DamageShort,nlNew.Header,nlNew.Detail,forNew.Outcome,forNew.Explanation,HeshRules.SurfaceText};
            texts.AddRange(hitNew.Breakdown);texts.AddRange(forNew.Findings);texts.AddRange(forNew.Advice);
            foreach(var l in viewNew.Layers){texts.Add(l.Name);texts.Add(l.Material);texts.Add(l.Reaction);texts.Add(l.BeforeAfter);texts.Add(l.Note);}
            var doc=XDocument.Parse(html);foreach(var d in doc.Descendants("details").ToArray())d.Remove();foreach(var s in doc.Descendants("style").ToArray())s.Remove();
            texts.AddRange(doc.Root!.DescendantNodes().OfType<XText>().Select(t=>t.Value));
            var jargon=texts.Where(t=>!string.IsNullOrEmpty(t)).SelectMany(t=>CombatReportLanguage.JargonIn(t,exempt).Select(j=>j+" in: "+t)).ToArray();
            Check(jargon.Length==0&&CombatReportLanguage.JargonIn(HeshRules.SurfaceText).Count==0,"no game jargon in the visible texts of the HESH stop"+(jargon.Length>0?": "+string.Join(" | ",jargon.Take(4)):""));
            reportLines.AppendLine("== HESH 152 mm on ebCassette_t72m1 at 44 deg, 3.2 mm left (log 14-11) ==");
            reportLines.AppendLine("0.22.77 hit panel: "+hitOld.Result+" · "+hitOld.Detail);reportLines.AppendLine("0.22.77 Dutch:     "+nlOld.Header+" | "+nlOld.Detail);reportLines.AppendLine("0.22.77 forensic:  "+forOld.Outcome+": "+forOld.Explanation);
            reportLines.AppendLine("0.22.78 hit panel: "+hitNew.Result+" · "+hitNew.Detail);reportLines.AppendLine("0.22.78 Dutch:     "+nlNew.Header+" | "+nlNew.Detail);
            reportLines.AppendLine("0.22.78 report:    "+row.Name+" | "+row.Material+" | "+row.BeforeAfter+" | "+row.Reaction);reportLines.AppendLine("0.22.78 forensic:  "+forNew.Outcome+": "+forNew.Explanation);
            // A native stop (300 mm RHA) carries the same line; a stop after a scab (legacy body) or with minAngleDegrees 10 does not.
            var thick=Outcome(Native.Embedded,0,false);
            Check(Report(thick,Rha(300,0),212.8,null,on,true,false).Any(x=>x.Text==HeshRules.SurfaceText)&&!Report(thick,Rha(300,0),212.8,null,on,true,true).Any(x=>x.Text==HeshRules.SurfaceText)&&
                !Report(thick,Rha(300,0),212.8,null,legacy,true,false).Any(x=>x.Text==HeshRules.SurfaceText)&&!Report(thick,Rha(300,0),212.8,null,on,false,false).Any(x=>x.Text==HeshRules.SurfaceText),
                "the line follows every HESH stop without a scab, only with the surface rule on and only for HESH");
            var thickView=CombatReportView.Build(new[]{Shot(Report(thick,Rha(300,0),212.8,null,on,true,false),212.8)})[0];
            reportLines.AppendLine("0.22.78 report, 152 mm on 300 mm RHA at 0 deg: "+thickView.Head+" | "+thickView.Layers[0].BeforeAfter+" | "+thickView.Layers[0].Reaction);
        }

        // ---------- 4. perforation = scab, no residual projectile ----------
        var table=new StringBuilder("calibre mm\tbefore: fragments\tbefore: kg per fragment\tbefore: total kg\tbefore: speed m/s\tbefore: residual projectile\tafter: fragments\tafter: kg per fragment\tafter: total kg\tafter: speed m/s\tafter: residual projectile\n");
        var perforation=new StringBuilder("case\tstep\tfragment\tflags\tspeed m/s\n");
        foreach(var calibre in new[]{76d,105,120,152,165})
        {
            var oldCount=Math.Clamp(ShellBalance.FragmentTarget("hesh",calibre)*1,4,32);var oldVolume=ShellBalance.FragmentVolume("hesh",calibre)*1;var oldMass=oldVolume*HeshRules.SteelDensity;
            var scab=HeshRules.Scab(calibre,1,on);var speed=ShellBalance.FragmentSpeed("hesh",calibre);
            table.AppendLine($"{calibre:0}\t{HeshRules.NativeCount(oldCount)}\t{F(oldMass/HeshRules.NativeCount(oldCount),"0.00")}\t{F(oldMass,"0.0")}\t{F(speed,"0")}-{F(speed*1.25,"0")}\tyes (flies on)\t{HeshRules.NativeCount(scab.Count)}\t{F(scab.MassPerFragmentKg,"0.00")}\t{F(scab.MassKg,"0.0")}\t{F(speed,"0")}-{F(speed*1.25,"0")}\tnone");
            Check(scab.Count==oldCount&&HeshRules.NativeCount(scab.Count)<=32&&Math.Abs(scab.MassKg-25.6*Math.Pow(calibre/100,3))<1e-9&&Math.Abs(scab.MassKg/oldMass-25.6/(16*.26*Math.PI*.25*.001*7850))<1e-9,
                $"{calibre:0} mm: {HeshRules.NativeCount(scab.Count)} fragments x {F(scab.MassPerFragmentKg,"0.00")} kg = {F(scab.MassKg,"0.0")} kg (25.6 kg x (c/100)^3; 0.22.77 {F(oldMass,"0.0")} kg)");
        }
        Check(HeshRules.Scab(152,1,on).MassPerFragmentKg>HeshRules.Scab(120,1,on).MassPerFragmentKg&&HeshRules.NativeCount(HeshRules.Scab(50,1,on).Count)<32&&HeshRules.NativeCount(HeshRules.Scab(152,1,on).Count)==32,"below the cap the count grows; at 32 the mass per fragment grows");
        Check(Math.Abs(HeshRules.Scab(152,1,new HeshSettings(ScabMassKgAt100Mm:10,ScabCalibreExponent:2)).MassKg-10*1.52*1.52)<1e-9&&Math.Abs(HeshRules.Scab(152,1.5,on).MassKg-1.5*25.6*Math.Pow(1.52,3))<1e-9,"mass and exponent are settings; the profile spallMultiplier still scales mass and count");
        {
            // 152 mm HESH on 100 mm RHA at 0 degrees: native perforation, child 0x181 at the exit; its own call meets the air block, bursts (GenerateSpalling), then copies itself over the air gap (0x81).
            var block=Block(h152.Budget,1,1,e,0,100,h152.D,0);
            Check(block.Outcome==Native.Perforated,$"152 mm on 100 mm RHA: native perforation (B {F(block.Effective)} > path {F(block.Path)} mm)");
            foreach(var settings in new[]{on,legacy})
            {
                var name=settings.LegacyScab?"legacy":"0.22.78";
                var f=Outcome(Native.Perforated,0,false);int c1=1;
                var speedBefore=f[c1].Speed;                                             // RuntimeArmourResponses.Begin: SpeedBefore of the c1 call
                var applies=HeshRules.ScabApplies(settings,f[c1].Flags);
                var stopped=HeshRules.StopBody(new Frags(f),c1,settings);               // BurstDone (CreateSpallBurst postfix)
                var ratio=Math.Clamp(f[c1].Speed/speedBefore,0f,1f);bool absorbed=f[c1].Speed==0; // NotifyParentSpeedChange: ResidualSpeedRatio, speed 0 -> Absorbed
                f.Add(new(){Flags=(ushort)((f[c1].Flags&O)|PC),Speed=f[c1].Speed,Parent=c1});   // native air copy: NewFragment copies the live element, keeps only the original bit, ORs 0x80
                if(ratio<1)for(int i=2;i<f.Count;i++)if(f[i].Parent==c1&&(f[i].Flags&O)!=0)   // RuntimeArmourResponses.End: continuation speed by the ratio, killed when absorbed
                {f[i].Speed=ArmourResponseModel.ResidualSpeed(f[i].Speed,ratio);if(absorbed){f[i].Speed=0;f[i].Flags|=K;}}
                for(int i=0;i<f.Count;i++)perforation.AppendLine($"{name}\tafter the scab call\t{i}\t0x{f[i].Flags:X}\t{F(f[i].Speed,"0.0")}");
                var flying=f.Skip(1).Count(x=>(x.Flags&O)!=0&&(x.Flags&K)==0&&x.Speed>0);
                if(settings.LegacyScab)Check(!applies&&!stopped&&flying==2,"legacy: the body flies on behind the plate as in 0.22.77 (continuation and air copy keep their speed)");
                else Check(applies&&stopped&&flying==0&&f[c1].Speed==0&&(f[c1].Flags&K)!=0&&f[2].Speed==0,"0.22.78: after the scab no original fragment behind the plate has speed; the air copy starts at 0 and is stopped");
            }
            var scab=HeshRules.Scab(152,1,on);
            Check(HeshRules.NativeCount(scab.Count)==32&&Math.Abs(scab.MassKg-25.6*Math.Pow(1.52,3))<1e-9,$"152 mm on 100 mm RHA: scab {HeshRules.NativeCount(scab.Count)} fragments x {F(scab.MassPerFragmentKg,"0.00")} kg = {F(scab.MassKg,"0.0")} kg from the rear face");
            // Only the burst of a perforation continuation is a scab; the burst of a ricochet child (0x101, from 75 degrees) stays as in 0.22.77.
            Check(HeshRules.ScabApplies(on,O|PC|GS)&&!HeshRules.ScabApplies(on,O|GS)&&!HeshRules.ScabApplies(on,PC|GS)&&!HeshRules.ScabApplies(on,0),"the scab rule needs an original perforation continuation (0x181); a ricochet child (0x101) and spall keep the old burst");
        }

        // ---------- 2. minAngleDegrees 10 and legacyScab: bit-identical HESH output ----------
        {
            int grid=0,changed=0;
            var angles=new[]{0d,5,9.99,10,10.01,30,44,60,74.9,75,80,89.9,double.NaN};
            foreach(var minimum in new[]{10d,9,0,-5})
            for(int flags=0;flags<512;flags++)foreach(var angle in angles)foreach(var kids in new[]{0,1,2})
            {
                var f=new List<Frag>{new(){Flags=(ushort)flags,Speed=500}};
                for(int k=0;k<kids;k++)f.Add(new(){Flags=(ushort)(k==0?O|GS:O|D|E),Speed=100+k,Parent=0});
                var g=Clone(f);var r=HeshRules.Surface(new Frags(g),0,1,angle,minimum);grid++;
                if(r.Converted||!Same(f,g))changed++;
            }
            Check(changed==0,$"minAngleDegrees 10 (or lower): the surface rule changes nothing in {grid} fragment states x angles");
            int scabChanged=0;for(int flags=0;flags<65536;flags++){var f=new List<Frag>{new(){Flags=(ushort)flags,Speed=500}};if(HeshRules.ScabApplies(legacy,(ushort)flags)||HeshRules.StopBody(new Frags(f),0,legacy)||f[0].Speed!=500)scabChanged++;}
            Check(scabChanged==0,"legacyScab: no burst is resized and no body is stopped, for all 65536 flag values");
            Check(!legacy.SurfaceDetonation&&!new HeshSettings(10).SurfaceDetonation&&new HeshSettings(10.5).SurfaceDetonation&&on.SurfaceDetonation,"the report line, the angle capture and the finalizer step are all gated by SurfaceDetonation (minAngleDegrees above 10)");
            // Converted exactly when the native ricochet pattern meets an angle below the limit.
            int wrong=0;
            for(int flags=0;flags<512;flags++)foreach(var angle in angles)
            {
                var f=new List<Frag>{new(){Flags=(ushort)flags,Speed=500},new(){Flags=O|GS,Speed=200,Parent=0}};
                var r=HeshRules.Surface(new Frags(f),0,1,angle,75);
                var expected=(flags&O)!=0&&(flags&D)!=0&&(flags&(K|E|P))==0&&angle<75;
                if(r.Converted!=expected||(expected&&((f[0].Flags&(D|E))!=E||f[1].Speed!=0||(f[1].Flags&K)==0)))wrong++;
            }
            Check(wrong==0,"with the default the surface rule converts exactly: original, Deflected, not Killed/Embedded/Penetrated, angle below 75 degrees");
            // No allocation on the decision for a fragment that does not match (struct stand-in, generic call).
            var none=new List<Frag>{new(){Flags=O|P,Speed=500}};var view=new Frags(none);HeshRules.Surface(view,0,1,30,75);
            var bytes=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++)HeshRules.Surface(view,0,1,30,75);bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Check(bytes==0,$"the decision for a non-matching fragment allocates nothing ({bytes} bytes for 100000 calls)");
        }

        // ---------- 3. source: insertions only, every one behind a HESH gate, no new hook ----------
        foreach(var file in Marked)
        {
            var now=File.ReadAllBytes(Path.Combine(root,file)); // herpak: the byte comparison with src-0.22.77 (process check) is removed
            static int Hooks(string s)=>s.Split('\n').Count(l=>l.Contains("[HarmonyPatch",StringComparison.Ordinal));
            Check(Hooks(Encoding.UTF8.GetString(now))==Hooks(Encoding.UTF8.GetString(UnheshBytes(Path.Combine(root,file)))),$"{file}: no [HarmonyPatch in the marked HESH insertions");
        }
        {
            string Read(string f)=>File.ReadAllText(Path.Combine(root,f)).Replace("\r\n","\n");
            var responses=Read("RuntimeArmourResponses.cs");var spall=Read("RuntimeSpall.cs");var feed=Read("RuntimeDamageFeed.cs");
            Check(responses.Contains("            // 0.22.78 HESH begin\n            if(current is {Hesh:true})HeshSurface(current);\n            // 0.22.78 HESH end\n",StringComparison.Ordinal),"finalizer: one property test (current is {Hesh:true}) before any new work; nothing else for other fragments");
            Check(responses.Contains("if(scope is {Hesh:true}&&__result.Valid&&__result.MaterialIndex>=0&&RuntimeSpall.Hesh.SurfaceDetonation)HeshAngle(scope,__result,__0,__1);",StringComparison.Ordinal),"block postfix: the angle is read for HESH originals only");
            Check(responses.Contains("        if(context.Behavior==\"hesh\")HeshBegin(active,context);\n",StringComparison.Ordinal)&&responses.Contains("if(!ReleaseDiagnostics.Enabled||!context.LiveImpact||context.HeshLogged)return;",StringComparison.Ordinal),"prefix: HESH originals only; the diagnostic line once per impact, live, with diagnostics on");
            Check(spall.Contains("if(context.Behavior==\"hesh\"&&HeshRules.ScabApplies(Hesh,(ushort)parent.flags)){var scab=HeshRules.Scab(calibre,tuning,Hesh);o.volume=(float)scab.VolumeM3;count=scab.Count;}",StringComparison.Ordinal)&&
                spall.Contains("if(count>0&&__state.Aphe&&__state.ParentIndex>=0&&Impact?.Behavior==\"hesh\")HeshScabDone(simulation,__state.ParentIndex,count);",StringComparison.Ordinal),"burst: HESH only, scab only for a perforation continuation unless legacyScab");
            Check(feed.Contains("if(outcome==PlateOutcome.Stopped&&HeshSurfaceStop(scope.Sim))RecordArmourInteraction(scope.Sim,scope.Index,\"materialNote\",HeshRules.SurfaceText);",StringComparison.Ordinal)&&
                feed.Contains("RuntimeSpall.Hesh.SurfaceDetonation&&RuntimeSpall.Impact is {} impact&&impact.Behavior==\"hesh\"&&!impact.PayloadBursts.Contains(sim.Pointer)",StringComparison.Ordinal),"report: the line only for a HESH stop without a scab, with the surface rule on");
            var block=spall[spall.IndexOf("        // 0.22.78 HESH begin\n        // The optional \"hesh\" object",StringComparison.Ordinal)..];block=block[..block.IndexOf("// 0.22.78 HESH end",StringComparison.Ordinal)];
            Check(!block.Contains("Write",StringComparison.Ordinal)&&block.Contains("HeshRules.Read(heshText",StringComparison.Ordinal)&&block.Contains("if(!ReferenceEquals(heshFree,heshText))",StringComparison.Ordinal),"configuration: the user's spall.json is read, never written; without the key the 0.22.77 lines run unchanged");
            var stop=spall[spall.IndexOf("private static void HeshScabDone",StringComparison.Ordinal)..];stop=stop[..stop.IndexOf("// 0.22.78 HESH end",StringComparison.Ordinal)];
            Check(stop.Contains("catch(Exception ex){if(!heshWarned){heshWarned=true;",StringComparison.Ordinal)&&responses.Contains("Plugin.ModLog.LogWarning(\"[HESH] Surface detonation unavailable; the native outcome is kept: \"+ex.Message);}}",StringComparison.Ordinal),"an exception keeps the native outcome and logs once per session");
            var surface=responses[responses.IndexOf("private static void HeshSurface(Scope scope)",StringComparison.Ordinal)..];surface=surface[..surface.IndexOf("// 0.22.78 HESH end",StringComparison.Ordinal)];
            Check(Regex.IsMatch(surface,@"if\(!rules\.SurfaceDetonation\)return;\s*try\s*\{")&&surface.Contains("catch(Exception ex)",StringComparison.Ordinal),"the finalizer step runs inside its own try/catch, so the 0.22.28 restore after it in the same finally block always runs");
        }

        // ---------- 6. configuration ----------
        var config=new StringBuilder("case\tminAngleDegrees\tlegacyScab\tscabMassKgAt100mm\tscabCalibreExponent\tlog lines\tlog\n");
        {
            var baseJson=SpallBalance.ToJson(new SpallSettings{SpacedKinetic=SpacedKineticSettings.Default,FragmentRicochet=FragmentRicochetSettings.Default});
            string With(string hesh)=>baseJson.TrimEnd().TrimEnd('}')+",\n  \"hesh\": "+hesh+"\n}";
            (HeshSettings S,List<string> Log,string Free) R(string json){var log=new List<string>();var s=HeshRules.Read(json,log.Add,out var free);return (s,log,free);}
            void Row(string name,(HeshSettings S,List<string> Log,string Free) r)=>config.AppendLine($"{name}\t{F(r.S.MinAngleDegrees)}\t{r.S.LegacyScab}\t{F(r.S.ScabMassKgAt100Mm)}\t{F(r.S.ScabCalibreExponent)}\t{r.Log.Count}\t{string.Join(" | ",r.Log)}");
            var missing=R(baseJson);Row("key missing",missing);
            Check(missing.S==HeshSettings.Default&&missing.Log.Count==0&&ReferenceEquals(missing.Free,baseJson),"missing key: code defaults (75 deg, scab 25.6 kg x (c/100)^3), the same text goes to the spall parser, no log");
            var empty=R(With("{}"));Row("empty object",empty);
            Check(empty.S==HeshSettings.Default&&empty.Log.Count==0&&SpallBalance.ToJson(SpallBalance.Parse(empty.Free))==SpallBalance.ToJson(SpallBalance.Parse(baseJson)),"empty object: defaults; the spall settings parse exactly as without the key");
            var threw=false;try{SpallBalance.Parse(With("{}"));}catch(FormatException){threw=true;}
            Check(threw,"the strict spall parser itself would reject the key, so it is read and removed first");
            foreach(var (json,expected,lines,name) in new[]{("{\"minAngleDegrees\": 5}",10d,1,"5 -> 10"),("{\"minAngleDegrees\": 120}",90d,1,"120 -> 90"),("{\"minAngleDegrees\": 10}",10d,0,"10 (native rule)"),("{\"minAngleDegrees\": 60}",60d,0,"60"),
                ("{\"minAngleDegrees\": \"75\"}",75d,1,"text -> default"),("{\"minAngleDegrees\": true}",75d,1,"boolean -> default"),("{\"minAngleDegrees\": -1e308}",10d,1,"huge negative -> 10")})
            {
                var r=R(With(json));Row("minAngleDegrees "+name,r);
                Check(r.S.MinAngleDegrees==expected&&r.Log.Count==lines&&(lines==0||r.Log[0].StartsWith("[HESH] spall.json hesh: minAngleDegrees",StringComparison.Ordinal)),$"minAngleDegrees {name}: {F(r.S.MinAngleDegrees)}, {r.Log.Count} log line(s) {string.Join(" ",r.Log)}");
            }
            var many=R(With("{\"minAngleDegrees\": 3, \"legacyScab\": \"no\", \"scabMassKgAt100mm\": 0, \"scabCalibreExponent\": 9, \"other\": 1}"));Row("four invalid values and an unknown key",many);
            Check(many.S==new HeshSettings(10,false,.1,4)&&many.Log.Count==1&&many.Log[0].Contains("unknown key other",StringComparison.Ordinal)&&many.Log[0].Contains("legacyScab is not true or false",StringComparison.Ordinal),"several invalid values: each clamped or defaulted, together one log line");
            var notObject=R(With("5"));Row("not an object",notObject);
            Check(notObject.S==HeshSettings.Default&&notObject.Log.Count==1,"a hesh value that is not an object: defaults, one log line");
            var valid=R(With("{\"minAngleDegrees\": 10, \"legacyScab\": true, \"scabMassKgAt100mm\": 12.5, \"scabCalibreExponent\": 2.5}"));Row("legacy settings",valid);
            Check(valid.S==new HeshSettings(10,true,12.5,2.5)&&valid.Log.Count==0,"valid values are read as given");
            var broken="{\"coneMultiplier\": ";var b=R(broken);
            Check(b.S==HeshSettings.Default&&ReferenceEquals(b.Free,broken)&&b.Log.Count==0,"malformed JSON: defaults, the text goes unchanged to the spall parser (which reports it as before)");
            Check(HeshRules.Describe(on).Contains("below 75 deg",StringComparison.Ordinal)&&HeshRules.Describe(legacy).Contains("legacyScab",StringComparison.Ordinal),"diagnostic description of the active rule");
        }

        File.WriteAllText(Path.Combine(output,"HESH-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(output,"HESH-SCAB-TABLE.tsv"),table.ToString());
        File.WriteAllText(Path.Combine(output,"HESH-PERFORATION.tsv"),perforation.ToString());
        File.WriteAllText(Path.Combine(output,"HESH-REPORT-LINES.txt"),reportLines.ToString());
        File.WriteAllText(Path.Combine(output,"HESH-CONFIG-CASES.tsv"),config.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT78-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,
            d152Mm=h152.D,mass152Kg=h152.Mass,budget152Mm=h152.Budget,d120Mm=h120.D,mass120Kg=h120.Mass,budget120Mm=h120.Budget,minAngleDegrees=on.MinAngleDegrees,scabMassKgAt100mm=on.ScabMassKgAt100Mm},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT78 PASS: {checks} checks, {failures} failures");
    }
}
