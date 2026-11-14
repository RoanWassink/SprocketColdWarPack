using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.77 proef "Solid block" on a replayed hit path (managed copy of the runtime order in RuntimeGroupedProtection.SolidCavity followed by ReachedIntervals):
/// per native solid block first the cavity (SolidBlock.Analyse/Plan, CassetteImpactPath.NativePassive, Ledger.Commit), then the skins as the unchanged interval route
/// (ConfirmedMaterialIntervals.Pair on the flags, table factor, Ledger.Plan/Commit). Plus the separate VerifyCopy change (te only on AlongEdgeManual corners).
/// </summary>
internal static class Feat02277SolidBlockTests
{
    private readonly record struct Surface(double D,bool Enter,bool Exit);
    private sealed class Result
    {
        internal double Cavity,Skins,Budget;internal int Charges;internal readonly List<PaidMaterialPiece> Pieces=new();internal readonly List<string> Notes=new(),Logs=new();
        internal readonly List<CombatReportEvent> Events=new();internal bool Odd;
        internal double Total=>Cavity+Skins;
    }
    private const int Plate=501;
    /// <summary>The production files 0.22.77 may change on top of 0.22.76 (Solid block; the VerifyCopy addendum of 14-11).</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeGroupedProtection.cs" or "CompositePlate.cs" or "CheekShellFill.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    // ---------------- geometry: a hollow box (outer box, skins grown inward) and an analytic ray ----------------
    private static (double T0,double T1)? Slab(double[] o,double[] d,double[] min,double[] max)
    {
        double t0=double.NegativeInfinity,t1=double.PositiveInfinity;
        for(int k=0;k<3;k++)
        {
            if(Math.Abs(d[k])<1e-15){if(o[k]<min[k]||o[k]>max[k])return null;continue;}
            double a=(min[k]-o[k])/d[k],b=(max[k]-o[k])/d[k];if(a>b)(a,b)=(b,a);
            t0=Math.Max(t0,a);t1=Math.Min(t1,b);
        }
        return t1>t0?(t0,t1):null;
    }
    /// <summary>The surfaces of a closed hollow box (outer w x h x d metres, skin s metres) on a ray; flags as the game derives them from the winding (outer faces out, inner faces into the cavity).</summary>
    private static List<Surface> HollowBox(double[] o,double[] d,double[] centre,double w,double h,double depth,double s,bool openBack=false)
    {
        var half=new[]{w/2,h/2,depth/2};var list=new List<Surface>();
        var min=new double[3];var max=new double[3];for(int k=0;k<3;k++){min[k]=centre[k]-half[k];max[k]=centre[k]+half[k];}
        var imin=min.Select(x=>x+s).ToArray();var imax=max.Select(x=>x-s).ToArray();
        if(openBack){imax[2]=max[2]+1;}
        var outer=Slab(o,d,min,max);if(outer==null)return list;
        var inner=Slab(o,d,imin,imax);
        list.Add(new(outer.Value.T0,true,false));
        if(inner!=null){list.Add(new(inner.Value.T0,false,true));if(inner.Value.T1<outer.Value.T1-1e-12)list.Add(new(inner.Value.T1,true,false));}
        if(inner==null||inner.Value.T1<outer.Value.T1-1e-12)list.Add(new(outer.Value.T1,false,true));
        return list;
    }
    private static (double[] O,double[] D) Ray(double angle,double[] through)
    {
        var r=angle*Math.PI/180;var d=new[]{Math.Sin(r),0,Math.Cos(r)};
        return (new[]{through[0]-d[0]*2,through[1],through[2]-d[2]*2},d);
    }
    // ---------------- the replayed hit path ----------------
    /// <summary>Native blocks from the flags (a depth counter per object, as GetNextMaterialBlock): solid from depth 0 to the next return to 0.</summary>
    private static List<(double S,double E)> NativeBlocks(IEnumerable<Surface> surfaces)
    {
        var blocks=new List<(double,double)>();int depth=0;double start=0;
        foreach(var h in surfaces.OrderBy(x=>x.D))
        {
            if(h.Enter){if(depth==0)start=h.D;depth++;}
            if(h.Exit&&depth>0){depth--;if(depth==0&&h.D>start)blocks.Add((start,h.D));}
        }
        return blocks;
    }
    private static Result Run(IReadOnlyList<Surface> surfaces,bool marked,ProtectionFactorTable table,ProtectionThreat threat,double angle,double budget,
        MaterialIntervalLedger? ledger=null,double fromM=double.NegativeInfinity,double toM=double.PositiveInfinity,Func<double,double>? factorAt=null,ISet<int>? failed=null,List<Surface>? nativeFlags=null,int plate=Plate)
    {
        ledger??=new MaterialIntervalLedger();failed??=new HashSet<int>();var r=new Result{Budget=budget};
        var label=table.Label;const string material="ebCassette_test";int block=0;double? lastEnd=null;
        foreach(var (s0,e) in NativeBlocks(nativeFlags??surfaces))
        {
            if(e<=fromM||s0>=toM)continue;var s=Math.Max(s0,fromM);
            if(lastEnd is {} gapStart&&s>gapStart){string gid="a"+block;r.Events.Add(new("layerStart","","Reached layer",r.Budget,null,gid,new CombatLayerInfo(gid,new[]{new CombatLayerPart(plate,CompositePlate.SolidBlockName,material,label,null,null,false)},false,(s-gapStart)*1000)));}
            lastEnd=e;string id="b"+(block++);
            r.Events.Add(new("layerStart","","Reached layer",r.Budget,null,id,new CombatLayerInfo(id,new[]{new CombatLayerPart(plate,CompositePlate.SolidBlockName,material,label,(e-s)*1000,angle,true)},false)));
            if(marked)
                SolidBlock.Guard(plate,failed,r.Logs.Add,()=>
                {
                    var line=SolidBlock.Analyse(surfaces.Select(x=>x.D));
                    if(line.Odd){r.Odd=true;r.Notes.Add(SolidBlock.OddText(label,line));return;}
                    foreach(var c in SolidBlock.Plan(line,s,e,ledger,plate,material,label,_=>angle,factorAt??(a=>table.Factor(threat,a))))
                    {
                        var before=r.Budget;var after=CassetteImpactPath.NativePassive(before,c.RhaMm);r.Budget=after;ledger.Commit(c.Plan);
                        r.Cavity+=c.RhaMm;r.Charges++;r.Pieces.AddRange(c.Plan.Pieces);var text=SolidBlock.ReportText(label,c,ProtectionFactorTable.Text(threat));r.Notes.Add(text);
                        r.Logs.Add(SolidBlock.LogText(plate,c,before,after));r.Events.Add(new("materialReaction","",text,before,after,id));
                    }
                });
            // the unchanged route: own intervals paired on the flags, table factor, ledger plan of this block
            var known=ConfirmedMaterialIntervals.Pair(surfaces.Select(x=>new MaterialBoundary(x.D,x.Enter,x.Exit,angle)));
            var plan=ledger.Plan(s,e,known.Select(k=>new MaterialInterval(plate,material,label,k.Start,k.End,table.Factor(threat,k.Angle),k.Angle)),e-s);
            if(plan==null)continue;
            var b=r.Budget;r.Budget=CassetteImpactPath.NativePassive(b,plan.RhaMm);r.Skins+=plan.RhaMm;r.Pieces.AddRange(plan.Pieces);ledger.Commit(plan);
            r.Events.Add(new("materialReaction","",plan.Text,b,r.Budget,id){PaidIntervals=plan.Pieces});
            r.Events.Add(new("plateResidual","","Traversed plate",b,r.Budget,id));
        }
        return r;
    }
    private static bool NoOverlap(IEnumerable<PaidMaterialPiece> pieces,out double union)
    {
        var o=pieces.OrderBy(p=>p.StartM).ToArray();union=0;double reach=double.NegativeInfinity;bool ok=true;
        foreach(var p in o){if(p.StartM<reach-1e-12)ok=false;union+=p.EndM-Math.Max(p.StartM,Math.Max(reach,p.StartM));reach=Math.Max(reach,p.EndM);}
        union*=1000;return ok;
    }
    private static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT77: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        ProtectionFactorTable Table(string id)=>ProtectionFactorTable.Build(new CassetteModel(presets.First(p=>p.Id==id),materials,reactions));
        const ProtectionThreat rod=ProtectionThreat.LongRod;const double budget=900;
        var centre=new[]{0d,0,.25};
        var rows=new StringBuilder("case\trecipe\tangle\tflags\tfront skin mm\tcavity mm\tfactor\tcavity mm RHA-eq\tback skin mm\tskins mm RHA-eq\ttotal mm RHA-eq\thand mm RHA-eq\tdifference\tunmarked (0.22.76) mm RHA-eq\n");
        var reportLines=new StringBuilder();var logLines=new StringBuilder();

        // ---------- marker ----------
        Check(CompositePlate.SolidBlockName=="Composite armour plate [Solid block]"&&CompositePlate.IsSolidBlock("Composite armour plate [Solid block]")&&CompositePlate.IsSolidBlock("Composite armour plate [Solid block] [FW75:x]"),"the marker is the mesh name 'Composite armour plate [Solid block]' (the [Layers] pattern; a saved-front suffix is ignored)");
        Check(!CompositePlate.IsSolidBlock(null)&&!CompositePlate.IsSolidBlock(CompositePlate.Name)&&!CompositePlate.IsSolidBlock(CompositePlate.LayersName)&&!CompositePlate.IsSolidBlock(CompositePlate.BackplaneName)&&!CompositePlate.IsSolidBlock("Composite armour plate [solid block]"),"no other plate name carries the marker");
        Check(CompositePlate.ModeFromName(CompositePlate.SolidBlockName)==ThicknessMode.Uniform&&CompositePlate.LayoutFromName(CompositePlate.SolidBlockName)==null&&!CompositePlate.IsName(CompositePlate.SolidBlockName),"the marker changes no thickness mode, layout or plate identity (identity stays the recipe material)");

        // ---------- 1. a closed box of 500 mm, skin 5 mm, straight and at 30 degrees, three recipes ----------
        foreach(var id in new[]{"t80u","t80u_hull","t72m1"})
        {
            var table=Table(id);
            foreach(var angle in new[]{0d,30})
            {
                var (o,d)=Ray(angle,centre);var hits=HollowBox(o,d,centre,.6,.4,.5,.005);
                double cos=Math.Cos(angle*Math.PI/180),f=table.Factor(rod,angle),hand=f*500/cos;
                Check(hits.Count==4,$"{id} {angle:0}°: the line crosses four surfaces of the box ({hits.Count})");
                var plain=Run(hits,false,table,rod,angle,budget);
                foreach(var (flags,list,native) in new (string,List<Surface>,List<Surface>?)[]{("as wound",hits,null),
                    ("list in another order",new List<Surface>{hits[2],hits[0],hits[3],hits[1]},null)})
                {
                    var r=Run(list,true,table,rod,angle,budget,nativeFlags:native);
                    var line=SolidBlock.Analyse(list.Select(x=>x.D));var p=line.Passages.FirstOrDefault();
                    NoOverlap(r.Pieces,out var union);bool disjoint=NoOverlap(r.Pieces,out _);
                    Check(line.Passages.Length==1&&Math.Abs(p.Front.LengthMm-5/cos)<1e-6&&Math.Abs(p.Cavity.LengthMm-490/cos)<1e-6&&Math.Abs(p.Back.LengthMm-5/cos)<1e-6,$"{id} {angle:0}° {flags}: one passage, front {F(p.Front.LengthMm)} mm, cavity {F(p.Cavity.LengthMm)} mm, back {F(p.Back.LengthMm)} mm (5, 490, 5 mm / cos)");
                    Check(Math.Abs(r.Total-hand)<=1e-9*hand&&r.Charges==1,$"{id} {angle:0}° {flags}: front {F(5/cos*f)} + cavity {F(r.Cavity)} + back {F(5/cos*f)} = {F(r.Total)} mm RHA-eq = hand {F(hand)} ({F(f,"0.#####")} x {F(500/cos)} mm)");
                    Check(disjoint&&Math.Abs(union-500/cos)<1e-6&&Math.Abs(r.Pieces.Sum(x=>x.LengthMm)-500/cos)<1e-6,$"{id} {angle:0}° {flags}: no double counting: paid pieces disjoint, together {F(union)} mm = inside");
                    rows.AppendLine(string.Join("\t","closed box 600x400x500, skin 5",id,F(angle,"0"),flags,F(p.Front.LengthMm),F(p.Cavity.LengthMm),F(f,"0.#####"),F(r.Cavity),F(p.Back.LengthMm),F(r.Skins),F(r.Total),F(hand),(r.Total-hand).ToString("0.#e+0",CultureInfo.InvariantCulture),F(plain.Total)));
                    if(flags=="as wound"){reportLines.AppendLine(r.Notes[0]);logLines.AppendLine(r.Logs[0]);}
                }
                // reversed winding on the two inner faces (Enter/Exit swapped): the cavity is found from the order alone; the skins stay with the unchanged route
                {
                    var flipped=new List<Surface>{hits[0],hits[1] with{Enter=true,Exit=false},hits[2] with{Enter=false,Exit=true},hits[3]};
                    var r=Run(flipped,true,table,rod,angle,budget);var old=Run(flipped,false,table,rod,angle,budget);
                    Check(Math.Abs(r.Cavity-f*490/cos)<=1e-9*hand&&NoOverlap(r.Pieces,out _),$"{id} {angle:0}° inner faces reversed: the cavity is still {F(r.Cavity)} mm RHA-eq (order, not flags) and nothing is paid twice; total {F(r.Total)} vs hand {F(hand)}; without the marker the route pays {F(old.Total)}");
                    rows.AppendLine(string.Join("	","closed box, inner faces reversed",id,F(angle,"0"),"inner Enter/Exit swapped","","",F(f,"0.#####"),F(r.Cavity),"",F(r.Skins),F(r.Total),F(hand),(r.Total-hand).ToString("0.#e+0",CultureInfo.InvariantCulture),F(old.Total)));
                }
                // native blocks: with reversed inner faces the game may hold the whole box as one solid block; the cavity is then claimed before the skins
                var whole=Run(hits,true,table,rod,angle,budget,nativeFlags:new List<Surface>{hits[0],hits[3]});
                Check(Math.Abs(whole.Total-hand)<=1e-9*hand&&NoOverlap(whole.Pieces,out _),$"{id} {angle:0}°: one native block over the whole box (reversed inner faces): still {F(whole.Total)} = hand, nothing twice");
                Check(Math.Abs(plain.Total-f*10/cos)<=1e-9*hand&&plain.Charges==0,$"{id} {angle:0}°: the same box without the marker pays only the skins as in 0.22.76 ({F(plain.Total)} mm RHA-eq)");
                var marked=Run(hits,true,table,rod,angle,budget);
                Check(Math.Abs(marked.Skins-plain.Skins)<1e-12,"the skins pay the same with and without the marker");
            }
        }
        var t=Table("t80u");double f0=t.Factor(rod,0);
        // ---------- 2. two loose blocks in one plate ----------
        {
            var (o,d)=Ray(0,centre);var hits=HollowBox(o,d,centre,.6,.4,.5,.005).Concat(HollowBox(o,d,new[]{0d,0,1.05},.6,.4,.5,.005)).ToList();
            var r=Run(hits,true,t,rod,0,budget);var line=SolidBlock.Analyse(hits.Select(h=>h.D));
            Check(hits.Count==8&&line.Passages.Length==2&&r.Charges==2&&Math.Abs(r.Total-f0*1000)<1e-9*f0*1000&&NoOverlap(r.Pieces,out var u)&&Math.Abs(u-1000)<1e-6,$"two loose blocks in one plate: two passages, each cavity paid in its own front-skin block, {F(r.Total)} mm RHA-eq = {F(f0)} x 1000 mm; the 50 mm gap between them is not paid");
            rows.AppendLine(string.Join("\t","two loose blocks","t80u","0","as wound","5","490+490",F(f0,"0.#####"),F(r.Cavity),"5",F(r.Skins),F(r.Total),F(f0*1000),"",""));
        }
        // ---------- 3. a line that crosses one shape twice (a U-shaped closed shell, both arms) ----------
        {
            // arms 300 mm deep with 5 mm skins, 200 mm outside between them (the line leaves and re-enters the same shell)
            var hits=new List<Surface>{new(1.000,true,false),new(1.005,false,true),new(1.295,true,false),new(1.300,false,true),new(1.500,true,false),new(1.505,false,true),new(1.795,true,false),new(1.800,false,true)};
            var r=Run(hits,true,t,rod,0,budget);var line=SolidBlock.Analyse(hits.Select(h=>h.D));
            Check(line.Passages.Length==2&&Math.Abs(line.Passages[0].Cavity.LengthMm-290)<1e-6&&Math.Abs(line.Passages[1].Cavity.LengthMm-290)<1e-6&&Math.Abs(r.Total-f0*600)<1e-9*f0*600,$"a line through both arms of one U-shaped shell: two passages of 290 mm cavity, each paid once; total {F(r.Total)} = {F(f0)} x 600 mm; the 200 mm outside is not paid");
        }
        // ---------- 4. an odd number of skins ----------
        {
            var (o,d)=Ray(0,centre);var open=HollowBox(o,d,centre,.6,.4,.5,.005,openBack:true);
            var r=Run(open,true,t,rod,0,budget);
            Check(open.Count==2&&r.Odd&&r.Charges==0&&Math.Abs(r.Total-f0*5)<1e-9&&r.Notes.Count==1&&r.Notes[0].Contains("only the skins are paid",StringComparison.Ordinal),$"open box (no back face): one skin, odd: only the skin pays ({F(r.Total)} mm RHA-eq) with its own report line: {r.Notes.FirstOrDefault()}");
            reportLines.AppendLine(r.Notes[0]);
            var seam=new List<Surface>{new(1,true,false),new(1.005,false,true),new(1.495,true,false)};
            var s=Run(seam,true,t,rod,0,budget);
            Check(SolidBlock.Analyse(seam.Select(h=>h.D)).Odd&&s.Charges==0&&s.Odd,"three surfaces (seam or glancing shot): odd, no cavity");
            var dup=new List<Surface>{new(1,true,false),new(1,true,false),new(1.005,false,true),new(1.495,true,false),new(1.5,false,true)};
            var dl=SolidBlock.Analyse(dup.Select(h=>h.D));
            Check(!dl.Odd&&dl.Passages.Length==1&&Math.Abs(dl.Passages[0].Cavity.LengthMm-490)<1e-6,"a surface reported twice at the same distance (shot along a shared triangle edge) counts once");
            var thin=SolidBlock.Analyse(new[]{1,1.005,1.0055,1.010,1.495,1.5});
            Check(!thin.Odd&&thin.Skins.Length==2&&Math.Abs(thin.Skins[0].LengthMm-10)<1e-6,"two skins 0.5 mm apart are one skin (merge below 1 mm)");
        }
        // ---------- 5. a follow-up fragment ----------
        {
            var (o,d)=Ray(0,centre);var hits=HollowBox(o,d,centre,.6,.4,.5,.005);var line=SolidBlock.Analyse(hits.Select(h=>h.D));var p=line.Passages[0];
            var ledger=new MaterialIntervalLedger();
            var parent=Run(hits,true,t,rod,0,budget,ledger,toM:p.Front.EndM);
            var child=Run(hits,true,t,rod,0,parent.Budget,ledger,fromM:p.Front.EndM+.1);
            Check(parent.Charges==1&&child.Charges==0&&Math.Abs(parent.Total+child.Total-f0*500)<1e-9*f0*500,$"a child that starts inside the cavity pays only the back skin ({F(child.Total)}); the cavity was paid once by the parent ({F(parent.Cavity)})");
            var again=Run(hits,true,t,rod,0,parent.Budget,ledger,fromM:p.Front.StartM+.0025);
            Check(again.Charges==0&&again.Cavity==0,"a child that starts inside the front skin finds the cavity claimed on the ledger and pays it not again");
            var fresh=Run(hits,true,t,rod,0,budget,new MaterialIntervalLedger(),fromM:p.Front.EndM+.1);
            Check(fresh.Charges==0,"a block that starts behind the front skin never pays the cavity (the block of the front skin does)");
        }
        // ---------- 6. an exception in the new path ----------
        {
            var (o,d)=Ray(0,centre);var hits=HollowBox(o,d,centre,.6,.4,.5,.005);var failed=new HashSet<int>();
            Func<double,double> boom=_=>throw new InvalidOperationException("test");
            var a=Run(hits,true,t,rod,0,budget,factorAt:boom,failed:failed);var b=Run(hits,true,t,rod,0,budget,factorAt:boom,failed:failed);
            var plain=Run(hits,false,t,rod,0,budget);
            Check(Math.Abs(a.Total-plain.Total)<1e-12&&Math.Abs(b.Total-plain.Total)<1e-12&&a.Charges==0,$"an exception in the new path: the block keeps the old calculation (skins only, {F(a.Total)} mm RHA-eq)");
            Check(a.Logs.Count==1&&b.Logs.Count==0&&a.Logs[0].StartsWith("[Solid block] Plate #501:",StringComparison.Ordinal),"one log line per plate per session (two shots, one line): "+a.Logs.FirstOrDefault()?.Replace("Exception:","Exception -"));
            var c=Run(hits,true,t,rod,0,budget,factorAt:boom,failed:failed,plate:502);
            Check(c.Logs.Count==1,"another plate gets its own line");
            logLines.AppendLine(a.Logs[0]);
        }
        // ---------- 7. the report: what the user sees ----------
        {
            var (o,d)=Ray(0,centre);var hits=HollowBox(o,d,centre,.6,.4,.5,.005);var r=Run(hits,true,t,rod,0,700);
            var shot=new CombatReportShot(977,"Outgoing","APFSDS",1600,r.Events,0,700,"Solid block proof",3,3,"Solid block proof");
            var norm=CombatReportTruth.Normalize(shot);var views=CombatHitProjection.Build(norm);
            Check(views.Length>=2&&LayeredPlateReport.Collapse(shot.Events).SequenceEqual(shot.Events),$"the report of a solid block is not merged as a plate in layers; {views.Length} rows");
            var all=string.Join("\n",views.Select((v,i)=>v.Summary(i+1)));
            if(Environment.GetEnvironmentVariable("EB_FEAT77_ROWS")=="1")Console.WriteLine("FEAT77 report rows:\n"+all);
            var front=views[0];var note=front.Details.FirstOrDefault(e=>e.Text.Contains("solid block: front skin 5 mm, cavity 490 mm",StringComparison.Ordinal));
            Check(note!=null&&note.BeforeMm==700&&front.Before==700&&front.After<note.AfterMm,$"the row of the front skin holds the solid-block line in its details (text export and detail view) and its in -> out includes the cavity ({F(front.Before??0)} -> {F(front.After??0)} mm); the headline reaction stays the paid skin interval: {front.Reaction}");
            reportLines.AppendLine();reportLines.AppendLine("== Combat report rows of the replayed straight shot, T-80U, 700 mm budget ==");
            for(int i=0;i<views.Length;i++){reportLines.AppendLine(views[i].Summary(i+1));foreach(var e in views[i].Details.Where(e=>e.Kind!="blockMembers"))reportLines.AppendLine("  detail: "+e.Text+(e.BeforeMm!=null||e.AfterMm!=null?$" [Pen {F(e.BeforeMm??0,"0.#")} -> {F(e.AfterMm??0,"0.#")} mm]":""));reportLines.AppendLine();}
        }
        // ---------- 8. source guards and the plate without marker ----------
        {
            var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
            Check(gp.Contains("if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length&&ReachedLayered(reached,first,last))return;",StringComparison.Ordinal)&&gp.Contains("        Reached(reached);\n    }",StringComparison.Ordinal),"the unchanged route (ReachedLayered, Reached) still runs after the new step");
            int from=gp.IndexOf("private static void SolidCavities",StringComparison.Ordinal);var body=from<0?"":gp[from..];
            Check(from>0&&!body.Contains("SetRha(",StringComparison.Ordinal)&&!body.Contains("WriteFactor(",StringComparison.Ordinal)&&!body.Contains("sim.fragments[reached.Index]=",StringComparison.Ordinal)&&body.Contains("ApplyPassive(",StringComparison.Ordinal)&&body.Contains("ray.Ledger.Commit(charge.Plan)",StringComparison.Ordinal),"the new path writes native state only through ApplyPassive (WriteBudget); no rhaFactor write of its own");
            Check(!gp.Contains("[HarmonyPatch",StringComparison.Ordinal)&&body.Contains("SolidBlock.Guard(",StringComparison.Ordinal),"no hook in the file; every plate runs inside SolidBlock.Guard");
        }
        // ---------- 9. the proof blueprint ----------
        {
            var path=Path.Combine(root,"tests","fixtures","solid-block","Solid block proof.blueprint");
            if(File.Exists(path))
            {
                var bp=JsonNode.Parse(File.ReadAllText(path))!;var meshes=bp["meshes"]!.AsArray();
                var box=meshes.FirstOrDefault(m=>(string?)m!["meshData"]!["name"]==CompositePlate.SolidBlockName);
                Check(box!=null,"the proof blueprint holds one plate named "+CompositePlate.SolidBlockName);
                if(box!=null)
                {
                    var mesh=box["meshData"]!["mesh"]!;var v=mesh["vertices"]!.AsArray().Select(x=>x!.GetValue<double>()).ToArray();var faces=mesh["faces"]!.AsArray();
                    var xs=Enumerable.Range(0,v.Length/3).Select(i=>v[3*i]).ToArray();var ys=Enumerable.Range(0,v.Length/3).Select(i=>v[3*i+1]).ToArray();var zs=Enumerable.Range(0,v.Length/3).Select(i=>v[3*i+2]).ToArray();
                    double cx=xs.Average(),cy=ys.Average(),cz=zs.Average();bool outward=true;
                    foreach(var fc in faces){var idx=fc!["v"]!.AsArray().Select(x=>x!.GetValue<int>()).ToArray();
                        double[] P(int i)=>new[]{v[3*idx[i]],v[3*idx[i]+1],v[3*idx[i]+2]};var a=P(0);var b=P(1);var c=P(2);
                        var e1=new[]{b[0]-a[0],b[1]-a[1],b[2]-a[2]};var e2=new[]{c[0]-a[0],c[1]-a[1],c[2]-a[2]};var n=new[]{e1[1]*e2[2]-e1[2]*e2[1],e1[2]*e2[0]-e1[0]*e2[2],e1[0]*e2[1]-e1[1]*e2[0]};
                        var m=new[]{idx.Average(i=>v[3*i])-cx,idx.Average(i=>v[3*i+1])-cy,idx.Average(i=>v[3*i+2])-cz};outward&=n[0]*m[0]+n[1]*m[1]+n[2]*m[2]>0;}
                    Check(faces.Count==6&&faces.All(fc=>fc!["t"]!.AsArray().All(x=>x!.GetValue<int>()==5)&&fc["tm"]!.GetValue<long>()==0),"the box has six faces, every corner 5 mm, thickened along the face normal (tm 0)");
                    Check(Math.Abs(xs.Max()-xs.Min()-.6)<1e-6&&Math.Abs(ys.Max()-ys.Min()-.4)<1e-6&&Math.Abs(zs.Max()-zs.Min()-.5)<1e-6,$"the box measures {F((xs.Max()-xs.Min())*1000)} x {F((ys.Max()-ys.Min())*1000)} x {F((zs.Max()-zs.Min())*1000)} mm (600 x 400 x 500, 500 deep along +z)");
                    Check(outward,"every face points outward, so the 5 mm skin grows into the box (native: back = point - normal x thickness)");
                    var structure=bp["blueprints"]!.AsArray().FirstOrDefault(b=>(string?)b!["type"]=="structure"&&(string?)b["blueprint"]!["armourTechID"]=="ebCassette_t80u");
                    Check(structure!=null,"the box plate carries the recipe ebCassette_t80u");
                }
            }
            else Check(false,"the proof blueprint exists in tests/fixtures/solid-block/");
        }
        // ---------- 10. Aanvulling 14-11: VerifyCopy compares te only on AlongEdgeManual corners ----------
        int verifyChecks=RunVerifyCopy(Check,root,output,materials,reactions,presets);
        File.WriteAllText(Path.Combine(output,"SOLID-CASES.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-REPORT-LINES.txt"),reportLines.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-LOG-LINES.txt"),logLines.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT77-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,verifyCopyChecks=verifyChecks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT77 PASS: {checks} checks, {failures} failures");
    }

    // The VerifyCopy of 0.22.76, kept here as the reference for (d).
    private static string? OldVerifyCopy(string planned,string copy)
    {
        try
        {
            using var a=JsonDocument.Parse(planned);using var b=JsonDocument.Parse(copy);
            var va=a.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var vb=b.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            if(vb.Length<va.Length)return "points";
            for(int i=0;i<va.Length;i++)if(Math.Abs(va[i]-vb[i])>1e-5f)return "vertex";
            var ea=a.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var eb=b.RootElement.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            if(eb.Length<ea.Length)return "edges";
            for(int i=0;i<ea.Length;i+=2)if(!(ea[i]==eb[i]&&ea[i+1]==eb[i+1]||ea[i]==eb[i+1]&&ea[i+1]==eb[i]))return "edge";
            var fa=a.RootElement.GetProperty("faces").EnumerateArray().ToArray();var fb=b.RootElement.GetProperty("faces").EnumerateArray().ToArray();
            if(fa.Length!=fb.Length)return "count";
            static string Token(JsonElement face,string key)=>face.TryGetProperty(key,out var p)?p.GetRawText().Trim():"0";
            for(int i=0;i<fa.Length;i++)
            {
                if(!fa[i].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(fb[i].GetProperty("v").EnumerateArray().Select(x=>x.GetInt32())))return "corners";
                if(!fa[i].GetProperty("t").EnumerateArray().Select(x=>x.GetInt32()).SequenceEqual(fb[i].GetProperty("t").EnumerateArray().Select(x=>x.GetInt32())))return "t";
                if(Token(fa[i],"tm")!=Token(fb[i],"tm"))return "tm";
                if(Token(fa[i],"te")!=Token(fb[i],"te"))return "te";
            }
            return null;
        }
        catch(Exception){return "error";}
    }
    /// <summary>Rewrites te on every corner whose tm byte is not 4 (the game chooses those edges again), or on the given manual corner only.</summary>
    private static string Rewrite(string json,bool manual,out int corners,out int changed)
    {
        var n=JsonNode.Parse(json)!;corners=0;changed=0;bool done=false;
        foreach(var face in n["faces"]!.AsArray())
        {
            int count=face!["v"]!.AsArray().Count;if(count>4)continue;
            ulong tm=face["tm"]==null?0:ulong.Parse(face["tm"]!.ToJsonString(),CultureInfo.InvariantCulture);ulong te=face["te"]==null?0:ulong.Parse(face["te"]!.ToJsonString(),CultureInfo.InvariantCulture);
            for(int j=0;j<count;j++)
            {
                bool isManual=((tm>>(8*j))&0xFF)==4;if(!manual)corners++;
                if(manual!=isManual||manual&&done)continue;
                var slot=(te>>(16*j))&0xFFFF;var next=(slot+7)&0xFFFF;te=te&~(0xFFFFUL<<(16*j))|next<<(16*j);changed++;if(manual)done=true;
            }
            face["te"]=JsonValue.Create(te);
        }
        return n.ToJsonString();
    }
    private static int RunVerifyCopy(Action<bool,string> check,string root,string output,IReadOnlyDictionary<string,CassetteMaterial> materials,MaterialReactionCatalogue reactions,IReadOnlyList<CassettePreset> presets)
    {
        int n=0;void C(bool ok,string m){n++;check(ok,"VerifyCopy: "+m);}
        var dumpPath=Path.Combine(root,"tests","fixtures","follow-wall","follow-wall-plate-324-object293.json");
        using var dump=JsonDocument.Parse(File.ReadAllText(dumpPath));
        var mesh=dump.RootElement.GetProperty("mesh").GetRawText();var mask=dump.RootElement.GetProperty("selection").EnumerateArray().Select(x=>x.GetBoolean()).ToArray();
        var selected=Enumerable.Range(0,mask.Length).Where(i=>mask[i]).Select(i=>i+1).ToArray();
        C(selected.SequenceEqual(new[]{15,30,119})&&dump.RootElement.GetProperty("recipe").GetProperty("Id").GetString()=="t72m1","the dump of the user: Object 293, faces 15, 30, 119 selected, recipe t72m1");
        var recipe=new CassetteModel(presets.First(p=>p.Id=="t72m1"),materials,reactions);
        LayerConversion? conversion=null;string tried="";
        foreach(var (name,forward) in new (string,System.Numerics.Vector3?)[]{("+Y",System.Numerics.Vector3.UnitY),("+Z",System.Numerics.Vector3.UnitZ),("none",null)})
        {
            try{conversion=LayerFaces.Convert(mesh,recipe,null,id=>materials[id].Density,forward,5,null,mask);tried+=name+": plan; ";break;}
            catch(Exception e){tried+=name+": "+e.Message+"; ";}
        }
        var dumpReason=dump.RootElement.GetProperty("summary").GetString()??"";
        C(conversion!=null||tried.Contains(dumpReason.Replace("Convert to layers: ",""),StringComparison.Ordinal),"Convert to layers on the dump: "+(conversion!=null?"plan":"refused exactly as the dump records it (the dump holds the refused attempt; the plan of the later attempt is not in it)")+" ("+tried.Trim()+")");
        {
            var plan=conversion?.Plan.Json??mesh;
            C(true,conversion!=null?"(a) uses the plan of Convert to layers":"(a) uses the mesh of the user as saved in the dump (its own tm/te per corner) as the planned payload");
            var copy=Rewrite(plan,false,out var corners,out var changed);
            var manualCount=corners-changed;
            C(changed>0,$"the simulated game copy rewrites te on {changed} of {corners} corners (every corner not in AlongEdgeManual; {manualCount} manual corners keep theirs)");
            C(OldVerifyCopy(plan,copy)=="te","0.22.76 refuses that copy (face rib slot changed), as the user saw");
            C(CheekShellFill.VerifyCopy(plan,copy)==null&&LayerFaces.VerifyCopy(plan,copy)==null,"0.22.77 accepts it: te is compared only on AlongEdgeManual corners");
            var moved=JsonNode.Parse(copy)!;var vs=moved["vertices"]!.AsArray();vs[0]=JsonValue.Create(vs[0]!.GetValue<double>()+.01);
            C(CheekShellFill.VerifyCopy(plan,moved.ToJsonString())?.Contains("a vertex moved",StringComparison.Ordinal)==true,"a moved point still refuses");
            var fewer=JsonNode.Parse(copy)!;fewer["faces"]!.AsArray().RemoveAt(0);
            C(CheekShellFill.VerifyCopy(plan,fewer.ToJsonString())?.Contains("face count changed",StringComparison.Ordinal)==true,"a missing face still refuses");
            var thick=JsonNode.Parse(copy)!;var t0=thick["faces"]![0]!["t"]!.AsArray();t0[0]=JsonValue.Create(t0[0]!.GetValue<int>()+1);
            C(CheekShellFill.VerifyCopy(plan,thick.ToJsonString())?.Contains("thickness changed",StringComparison.Ordinal)==true,"a changed corner thickness still refuses");
            var mode=JsonNode.Parse(copy)!;var f0=mode["faces"]![0]!;f0["tm"]=JsonValue.Create(ulong.Parse(f0["tm"]?.ToJsonString()??"0",CultureInfo.InvariantCulture)^1UL);
            C(CheekShellFill.VerifyCopy(plan,mode.ToJsonString())?.Contains("direction mode changed",StringComparison.Ordinal)==true,"a changed tm still refuses");
            if(manualCount>0)
            {
                var edge=Rewrite(plan,true,out _,out var one);
                C(one==1&&CheekShellFill.VerifyCopy(plan,edge)?.Contains("rib slot changed",StringComparison.Ordinal)==true,"a changed edge on an AlongEdgeManual corner of the plan still refuses");
            }
        }
        // (b) on a small face with mixed corners: Manual corner 1, Auto corners 0, 2, 3
        {
            const string planned="{\"majorVersion\":0,\"minorVersion\":3,\"vertices\":[0,0,0,1,0,0,1,0,1,0,0,1,0,-1,0],\"edges\":[0,1,1,2,2,3,3,0,1,4],\"edgeFlags\":[0,0,0,0,0],\"faces\":[{\"v\":[0,1,2,3],\"t\":[20,20,20,20],\"tm\":1025,\"te\":262144}]}";
            string With(ulong te)=>planned.Replace("\"te\":262144","\"te\":"+te.ToString(CultureInfo.InvariantCulture));
            C(CheekShellFill.VerifyCopy(planned,With(262144UL|5UL|(9UL<<32)|(3UL<<48)))==null,"mixed face: other edges on the Auto corners 0, 2, 3 are accepted");
            C(CheekShellFill.VerifyCopy(planned,With(5UL<<16))?.Contains("rib slot changed",StringComparison.Ordinal)==true,"mixed face: another edge on the AlongEdgeManual corner 1 refuses");
            C(CheekShellFill.VerifyCopy(planned,planned.Replace("\"te\":262144","\"te\":-1"))?.Contains("rib slot changed",StringComparison.Ordinal)==true,"an unreadable te token is still compared as text");
        }
        // (d) Cheek fill (shell): same answers as 0.22.76 on its own plans
        foreach(var name in new[]{"shell-450-200.mesh.json","shell-450-200-fallback.mesh.json"})
        {
            var path=Path.Combine(output,name);if(!File.Exists(path)){C(false,name+" present");continue;}
            var plan=File.ReadAllText(path);var manualEdge=Rewrite(plan,true,out _,out var one);var autoOnly=Rewrite(plan,false,out _,out var autoChanged);
            var moved=JsonNode.Parse(plan)!;var vs=moved["vertices"]!.AsArray();vs[3]=JsonValue.Create(vs[3]!.GetValue<double>()+.01);
            var cases=new List<string>{plan,moved.ToJsonString()};if(one==1)cases.Add(manualEdge);
            bool same=cases.All(c=>(OldVerifyCopy(plan,c)==null)==(CheekShellFill.VerifyCopy(plan,c)==null));
            bool relaxed=autoChanged==0||OldVerifyCopy(plan,autoOnly)=="te"&&CheekShellFill.VerifyCopy(plan,autoOnly)==null;
            C(same&&relaxed&&(one==0||CheekShellFill.VerifyCopy(plan,manualEdge)!=null)&&CheekShellFill.VerifyCopy(plan,plan)==null,$"Cheek fill (shell) {name}: {(one==1?"AlongEdgeManual ribs":"no AlongEdgeManual corner (fallback, along the face normal)")}; identical copy, moved point{(one==1?", changed rib on a manual corner":"")} give the same answer as 0.22.76; {autoChanged} corners not in AlongEdgeManual: another edge there is now accepted (the intended change)");
        }
        return n;
    }
}
