using System.Globalization;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.79: the [Solid block] mark is noted at build and only looked up during the shot; an unknown mark is not kept for the shot; the cavity line belongs to the
/// block of the front skin; one [Solid block] diag line per shot per cassette plate. The production pure part of RuntimeGroupedProtection.cs (SolidMarks,
/// SolidShotCache, SolidDiagGate, SolidDiag; compiled here with EB_PURE_SOLID_MARK), the real PlateMeshGate and CompositePlate.SolidBlock run as they are. The order of
/// the game-side glue (SolidMarkBuilt in the Build postfix; SolidCavities, SolidPlate, SolidCavity in the hit; the unchanged interval route after it) is replayed here,
/// statement for statement; that glue itself is IL2CPP code the managed tests cannot run.
/// </summary>
internal static class Feat02279SolidMarkTests
{
    /// <summary>The production files 0.22.79 may change on top of 0.22.78.</summary>
    internal static bool IsPlanned(string rel)=>rel is "RuntimeGroupedProtection.cs" or "RuntimeComposerFill.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md";
    private readonly record struct Surface(double D,bool Enter,bool Exit);
    private sealed record LinePlate(int Entity,long Pointer,long Mesh,bool Cassette,string Name,List<Surface> Surfaces,ProtectionFactorTable Table,string Material);
    /// <summary>The managed DamageFeed: LayerStarted is idempotent per block; Note attaches to the current block (hitId null), as RecordArmourInteraction does.</summary>
    private sealed class Feed
    {
        internal string? Current;internal readonly List<CombatReportEvent> Events=new();
        internal void LayerStarted(string id,CombatLayerInfo layer,double budget){if(Current==id)return;Current=id;Events.Add(new("layerStart","","Reached layer",budget,null,id,layer));}
        internal void Note(string text,double before,double after)=>Events.Add(new("materialReaction","",text,before,after,Current));
    }
    private sealed class Seen{internal SolidMarkAnswer Answer;internal LinePlate Plate=null!;internal SolidMarks.Entry Mark;internal string Reason="";}
    private sealed class Shot
    {
        internal readonly SolidShotCache<Seen> Plates=new();internal readonly HashSet<int> Odd=new();internal readonly SolidDiagGate Diag=new();
        internal readonly Dictionary<int,bool?> Old=new(); // the 0.22.78 per-shot cache: null for the whole shot once the name was not found
    }
    private sealed class Result
    {
        internal double Cavity,Skins,Budget;internal int Charges;internal readonly List<string> Diag=new(),Logs=new();internal Feed Feed=new();internal readonly List<(string Block,string Note)> CavityNotes=new();
        internal double Total=>Cavity+Skins;
    }
    /// <summary>0.22.78 RuntimeLayerFaces.MeshName (names cached per mesh pointer for 1 s; a busy gate returns the cache or null), with the real PlateMeshGate.</summary>
    private sealed class OldMeshName
    {
        private readonly Dictionary<long,(string? Name,float Until)> names=new();
        internal int Reads;
        internal string? Get(long mesh,PlateMeshGate gate,float now,string name)
        {
            if(names.TryGetValue(mesh,out var known)&&now<known.Until)return known.Name;
            if(gate.Busy)return names.TryGetValue(mesh,out known)?known.Name:null;
            Reads++;names[mesh]=(name,now+1f);return name;
        }
    }
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
    /// <summary>
    /// One shot along the line, block by block, in the order of ReachedWithLayers: SolidCavities (scan with SolidPlate, then per plate SolidCavity inside SolidBlock.Guard and
    /// the diag line), then the unchanged interval route (LayerStarted, own intervals paired on the flags, table factor, ledger plan and commit).
    /// version 78: the 0.22.78 code (name read through the busy gate at the hit, null kept for the shot, cavity note before LayerStarted).
    /// </summary>
    private static Result Run(IReadOnlyList<LinePlate> plates,ProtectionThreat threat,double angle,double budget,int version=79,PlateMeshGate? gate=null,float now=100,OldMeshName? old=null,
        Action<int>? beforeBlock=null,Func<double,double>? factorAt=null,ISet<int>? failed=null,MaterialIntervalLedger? ledger=null,double fromM=double.NegativeInfinity)
    {
        gate??=new PlateMeshGate(()=>now,()=>false);old??=new OldMeshName();failed??=new HashSet<int>();ledger??=new MaterialIntervalLedger();
        var r=new Result{Budget=budget};var shot=new Shot();int block=0;double? lastEnd=null;
        var all=plates.SelectMany(p=>p.Surfaces).ToList();
        foreach(var (s0,e) in NativeBlocks(all))
        {
            if(e<=fromM)continue;var s=Math.Max(s0,fromM);
            beforeBlock?.Invoke(block);
            if(lastEnd is {} gapStart&&s>gapStart){string gid="a"+block;r.Feed.LayerStarted(gid,new(gid,Array.Empty<CombatLayerPart>(),false,(s-gapStart)*1000),r.Budget);}
            lastEnd=e;string id="b"+(block++);
            var inBlock=plates.Where(p=>p.Surfaces.Any(x=>x.D>=s-1e-12&&x.D<=e+1e-12)).ToList();
            var layer=new CombatLayerInfo(id,inBlock.Select(p=>new CombatLayerPart(p.Entity,p.Name,p.Material,p.Table.Label,(e-s)*1000,angle,true)).ToArray(),false);
            // ---- SolidCavities: the scan (every surface of the line, as the game hands every block the whole list) ----
            var collected=new Dictionary<int,(Seen Seen,int Own)>();
            foreach(var p in plates)foreach(var _ in p.Surfaces)
            {
                Seen? seen;
                if(version==78)
                {
                    if(!shot.Old.TryGetValue(p.Entity,out var marked)){marked=p.Cassette&&CompositePlate.IsSolidBlock(old.Get(p.Mesh,gate,now,p.Name))?true:null;shot.Old[p.Entity]=marked;}
                    seen=marked==true?new Seen{Answer=SolidMarkAnswer.Marked,Plate=p}:null;
                }
                else if(!shot.Plates.TryCached(p.Entity,out seen))
                {
                    seen=null;
                    if(p.Cassette){var answer=SolidMarks.Answer(p.Pointer,out var mark,out var reason);seen=new Seen{Answer=answer,Plate=p,Mark=mark,Reason=reason};}
                    shot.Plates.Remember(p.Entity,seen,seen==null||seen.Answer!=SolidMarkAnswer.Unknown);
                }
                if(seen==null)continue;
                if(seen.Answer!=SolidMarkAnswer.Marked&&(version==78||!shot.Diag.Wants(p.Entity)))continue;
                collected[p.Entity]=(seen,collected.TryGetValue(p.Entity,out var c)?c.Own+1:1);
            }
            foreach(var (entity,(seen,own)) in collected)
            {
                var diag=version==79&&shot.Diag.Wants(entity)?new SolidDiag{Shot=shot.Diag.Id,Entity=entity,Name=seen.Mark.Name,Marked=seen.Answer==SolidMarkAnswer.Unknown?null:seen.Answer==SolidMarkAnswer.Marked,
                    Source=seen.Answer==SolidMarkAnswer.Unknown?"unknown: "+seen.Reason:seen.Mark.Source,Busy=gate.Busy,Surfaces=own}:null;
                if(seen.Answer==SolidMarkAnswer.Marked)
                    SolidBlock.Guard(entity,failed,r.Logs.Add,()=>Cavity(r,seen.Plate,threat,angle,s,e,id,layer,shot,ledger,factorAt,diag,version));
                else if(diag!=null)diag.Exit=seen.Answer==SolidMarkAnswer.Unmarked?"unmarked":"markUnknown";
                if(diag!=null&&shot.Diag.Allow(entity,diag.Exit))r.Diag.Add(diag.Text());
            }
            // ---- the unchanged route (ReachedIntervals): LayerStarted, own intervals on the flags, ledger plan ----
            r.Feed.LayerStarted(id,layer,r.Budget);
            var candidates=new List<MaterialInterval>();
            foreach(var p in inBlock)
                foreach(var k in ConfirmedMaterialIntervals.Pair(p.Surfaces.Select(x=>new MaterialBoundary(x.D,x.Enter,x.Exit,angle))))
                    candidates.Add(new(p.Entity,p.Material,p.Table.Label,k.Start,k.End,p.Table.Factor(threat,k.Angle),k.Angle));
            var plan=ledger.Plan(s,e,candidates,e-s);
            if(plan==null)continue;
            var b=r.Budget;r.Budget=CassetteImpactPath.NativePassive(b,plan.RhaMm);r.Skins+=plan.RhaMm;ledger.Commit(plan);
            r.Feed.Events.Add(new("materialReaction","",plan.Text,b,r.Budget,id){PaidIntervals=plan.Pieces});
            r.Feed.Events.Add(new("plateResidual","","Traversed plate",b,r.Budget,id));
        }
        return r;
    }
    /// <summary>SolidCavity after the plate and table checks: Analyse, the odd line, the plan of the due passages, LayerStarted (0.22.79), ApplyPassive, Commit, Note.</summary>
    private static void Cavity(Result r,LinePlate p,ProtectionThreat threat,double angle,double s,double e,string id,CombatLayerInfo layer,Shot shot,MaterialIntervalLedger ledger,Func<double,double>? factorAt,SolidDiag? diag,int version)
    {
        var line=SolidBlock.Analyse(p.Surfaces.Select(x=>x.D));
        diag?.Line(line,s,e);
        if(line.Odd)
        {
            if(diag!=null)diag.Exit="odd";
            if(shot.Odd.Add(p.Entity)){if(version==79)r.Feed.LayerStarted(id,layer,r.Budget);r.Feed.Note(SolidBlock.OddText(p.Table.Label,line),r.Budget,r.Budget);}
            return;
        }
        double EntryAngle(SolidPassage _){if(diag!=null)diag.Angle=angle;return angle;}
        var charges=SolidBlock.Plan(line,s,e,ledger,p.Entity,p.Material,p.Table.Label,EntryAngle,a=>{var f=(factorAt??(x=>p.Table.Factor(threat,x)))(a);if(diag!=null)diag.Factor=f;return f;});
        if(diag!=null){diag.Charges=charges.Count;diag.Exit=charges.Count==0?"notDue":"applyPassiveFalse";}
        bool started=false;
        foreach(var c in charges)
        {
            if(version==79&&!started){started=true;r.Feed.LayerStarted(id,layer,r.Budget);}
            var before=r.Budget;var target=CassetteImpactPath.NativePassive(before,c.RhaMm);
            if(!(c.RhaMm>0)||!(before>0)||!(target<before)){diag?.Applied(c,false,before>0&&c.RhaMm>0?before:0,0);continue;}
            r.Budget=target;diag?.Applied(c,true,before,target);
            ledger.Commit(c.Plan);r.Cavity+=c.RhaMm;r.Charges++;
            var text=SolidBlock.ReportText(p.Table.Label,c,ProtectionFactorTable.Text(threat));r.Feed.Note(text,before,target);r.CavityNotes.Add((r.Feed.Current??"(none)",text));
        }
    }
    // ---------------- geometry ----------------
    private static List<Surface> Box(double front,double depth=.5,double skin=.005)=>new(){new(front,true,false),new(front+skin,false,true),new(front+depth-skin,true,false),new(front+depth,false,true)};
    private static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);
    private static long Allocated(Action a){a();var before=GC.GetAllocatedBytesForCurrentThread();a();return GC.GetAllocatedBytesForCurrentThread()-before;}

    /// <summary>A small managed fragment array for HeshRules.Surface (reference semantics, as the boxed fragments of the game).</summary>
    private sealed class Fragments:IHeshFragments
    {
        internal readonly List<(ushort Flags,int Parent,double Speed)> Items=new();
        public int Count=>Items.Count;public ushort Flags(int i)=>Items[i].Flags;public int Parent(int i)=>Items[i].Parent;
        public void SetFlags(int i,ushort f)=>Items[i]=Items[i] with{Flags=f};public void Stop(int i)=>Items[i]=Items[i] with{Speed=0,Flags=(ushort)(Items[i].Flags|HeshRules.Killed)};
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT79: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        ProtectionFactorTable Table(string id)=>ProtectionFactorTable.Build(new CassetteModel(presets.First(p=>p.Id==id),materials,reactions));
        var t80u=Table("t80u");const ProtectionThreat rod=ProtectionThreat.LongRod;const double budget=900;
        double f0=t80u.Factor(rod,0),hand=f0*500,cavity=f0*490,skins=f0*10;
        var rows=new StringBuilder("case\tversion\tblock\tmark at hit\tcavity mm RHA-eq\tskins mm RHA-eq\ttotal mm RHA-eq\texpected mm RHA-eq\tname reads during the shot\n");
        var diagOut=new StringBuilder();
        LinePlate BoxPlate(long pointer,long mesh,double front=1)=>new(501,pointer,mesh,true,CompositePlate.SolidBlockName,Box(front),t80u,"ebCassette_t80u");
        SolidMarks.Reset();SolidDiagGate.ResetSession();

        // ---------- (1) a closed gate (Busy = true) and an empty name cache ----------
        {
            float now=100;bool mouse=true;var gate=new PlateMeshGate(()=>now,()=>mouse);
            Check(gate.Busy&&!gate.Quiet,"the gate is busy: a mouse button is held (in Play: the fire button)");
            // 0.22.78: nothing read the name of the spawned mesh before the shot, so the name cache is empty
            var old=new OldMeshName();var box=BoxPlate(0x7000,0x7100);
            var r78=Run(new[]{box},rod,0,budget,78,gate,now,old);
            Check(old.Reads==0&&r78.Charges==0&&Math.Abs(r78.Total-skins)<1e-9,$"0.22.78 code (replica of MeshName and the per-shot cache): the name is not found behind the busy gate, the cavity is not paid: total {F(r78.Total)} mm RHA-eq = skins only ({F(skins)}); the test fails on 0.22.78, as the user saw in the game (no [Solid block] line)");
            rows.AppendLine(string.Join("\t","closed gate, empty name cache","0.22.78","b0","not found",F(r78.Cavity),F(r78.Skins),F(r78.Total),F(hand),old.Reads));
            // 0.22.79: the spawned plate was built (Play: not the designer) while the button was held; the Build postfix reads the name there
            int reads=0;
            var outcome=SolidMarks.Built(box.Pointer,box.Mesh,true,false,gate.Busy,()=>{reads++;return box.Name;},"Build",false);
            var r79=Run(new[]{box},rod,0,budget,79,gate,now);
            Check(outcome==SolidMarks.BuildOutcome.Noted&&reads==1,"0.22.79: the Build postfix of the spawned plate notes the mark (Play reads the name at build even with the button held; one read)");
            Check(r79.Charges==1&&Math.Abs(r79.Cavity-cavity)<1e-9&&Math.Abs(r79.Total-hand)<1e-9*hand,$"0.22.79 with the gate still busy and no name cache: the plate is a solid block and the cavity is paid: {F(r79.Cavity)} mm RHA-eq, total {F(r79.Total)} = hand {F(hand)} ({F(f0,"0.####")} x 500 mm)");
            rows.AppendLine(string.Join("\t","closed gate, empty name cache","0.22.79","b0","Build table",F(r79.Cavity),F(r79.Skins),F(r79.Total),F(hand),0));
        }
        // ---------- (2) the hit of a spawned vehicle: Build postfix -> shot, nothing else reads the name ----------
        {
            SolidMarks.Reset();SolidDiagGate.ResetSession();
            float now=200;bool mouse=false;var gate=new PlateMeshGate(()=>now,()=>mouse);
            var designerBox=BoxPlate(0x8000,0x8100);var spawned=BoxPlate(0x9000,0x9100); // the Play vehicle: new plate and mesh pointers
            int reads=0;Func<string?> Read(LinePlate p)=>()=>{reads++;return p.Name;};
            // designer: Build while the plate is being edited (quiet period) waits; the deferred attempt reads once the gate is free
            gate.NoteModified(designerBox.Mesh);
            var waiting=SolidMarks.Built(designerBox.Pointer,designerBox.Mesh,true,true,gate.Busy,Read(designerBox),"Build",false);
            var again=SolidMarks.Built(designerBox.Pointer,designerBox.Mesh,true,true,gate.Busy,Read(designerBox),"Build",false);
            var a1=SolidMarks.Answer(designerBox.Pointer,out _,out var why);
            Check(waiting==SolidMarks.BuildOutcome.Defer&&again==SolidMarks.BuildOutcome.Deferring&&reads==0&&a1==SolidMarkAnswer.Unknown&&why.StartsWith("deferred",StringComparison.Ordinal),$"designer, gate busy at build: no read; one deferred attempt is scheduled (a second build does not add one); the hit meanwhile knows nothing ({why})");
            now+=PlateMeshGate.QuietSeconds+.01f;
            SolidMarks.Built(designerBox.Pointer,designerBox.Mesh,true,true,gate.Busy,Read(designerBox),"Build (deferred)",true);SolidMarks.EndDefer(designerBox.Pointer);
            Check(reads==1&&SolidMarks.Answer(designerBox.Pointer,out var dm,out _)==SolidMarkAnswer.Marked&&dm.Source=="Build (deferred)","designer: the deferred attempt reads the name once the gate is free and notes the mark");
            // Play: the vehicle is spawned (its plates are built), then the user fires with the button held; no other reader touched the name
            var logs=new List<string>();
            SolidMarks.Built(spawned.Pointer,spawned.Mesh,true,false,gate.Busy,Read(spawned),"Build",false,logs.Add,287);
            int atBuild=reads;mouse=true;
            var r=Run(new[]{spawned,new LinePlate(600,0xA000,0xA100,false,"Glacis",new(){new(1.6,true,false),new(1.62,false,true)},t80u,"rha")},rod,0,budget,79,gate,now);
            Check(atBuild==2&&reads==2,"the shot reads no name: the only read of the spawned plate was in its Build postfix");
            Check(r.Charges==1&&Math.Abs(r.Cavity-cavity)<1e-9,$"the spawned plate pays its cavity ({F(r.Cavity)} mm RHA-eq) after Build postfix -> shot");
            Check(logs.Count==1&&logs[0].StartsWith("[Solid block] mark noted: plate #287 (0x9000, mesh 0x9100), Build, outside the designer",StringComparison.Ordinal),"Play build of a marked plate writes one evidence line: "+logs.FirstOrDefault());
            Check(r.Diag.Count==1&&r.Diag[0].Contains("mark yes (Build)",StringComparison.Ordinal)&&r.Diag[0].Contains("gate busy True",StringComparison.Ordinal)&&r.Diag[0].EndsWith("exit paid.",StringComparison.Ordinal),"the diag line says where the mark came from (Build) and that the gate was busy: "+r.Diag.FirstOrDefault());
            diagOut.AppendLine("== (2) spawned vehicle, fire button held ==");diagOut.AppendLine(logs[0]);foreach(var l in r.Diag)diagOut.AppendLine(l);
            // a read that fails at build leaves the mark unknown (never "unmarked"); a non-cassette plate is forgotten
            SolidMarks.Built(0xB000,0xB100,true,false,false,()=>throw new InvalidOperationException("x"),"Build",false);
            Check(SolidMarks.Answer(0xB000,out _,out var failedWhy)==SolidMarkAnswer.Unknown&&failedWhy.Contains("read failed (InvalidOperationException)",StringComparison.Ordinal),"a failed read at build leaves the mark unknown with the reason: "+failedWhy);
            SolidMarks.Built(0xC000,0,false,false,false,()=>"x","Build",true);
            Check(SolidMarks.Answer(0xC000,out _,out var noMesh)==SolidMarkAnswer.Unknown&&noMesh.EndsWith("no mesh",StringComparison.Ordinal),"the last deferred attempt without a mesh leaves the mark unknown: "+noMesh);
            SolidMarks.Forget(spawned.Pointer);
            Check(SolidMarks.Answer(spawned.Pointer,out _,out _)==SolidMarkAnswer.Unknown,"a plate rebuilt without a recipe is forgotten");
            rows.AppendLine(string.Join("\t","spawned vehicle: Build postfix -> shot","0.22.79","b0","Build table",F(r.Cavity),F(r.Skins),F(r.Total),F(hand+f0*20),reads-atBuild));
        }
        // ---------- (3) no knowledge at block 1, knowledge at block 2 of the same shot ----------
        {
            SolidMarks.Reset();SolidDiagGate.ResetSession();
            var skirt=new LinePlate(400,0xD000,0xD100,false,"Skirt",new(){new(.5,true,false),new(.51,false,true)},t80u,"rha");
            var box=BoxPlate(0xE000,0xE100);
            void Learn(int block){if(block==1)SolidMarks.Built(box.Pointer,box.Mesh,true,false,false,()=>box.Name,"Build (deferred)",true);}
            var r=Run(new[]{skirt,box},rod,0,budget,79,beforeBlock:Learn);
            Check(r.Charges==1&&Math.Abs(r.Cavity-cavity)<1e-9,$"block 1 (the skirt) knew no mark, block 2 (the front skin) did: block 2 pays the cavity ({F(r.Cavity)} mm RHA-eq); the unknown was not kept for the shot");
            Check(r.Diag.Count==2&&r.Diag[0].Contains("mark unknown (unknown: no build entry)",StringComparison.Ordinal)&&r.Diag[0].EndsWith("exit markUnknown.",StringComparison.Ordinal)&&r.Diag[1].EndsWith("exit paid.",StringComparison.Ordinal),"two diag lines for this plate: the first (not decisive) says why, the second shows the payment");
            diagOut.AppendLine("== (3) unknown at block 1, known at block 2 ==");foreach(var l in r.Diag)diagOut.AppendLine(l);
            // the same line with the 0.22.78 cache: the first answer (null) stays for the whole shot
            SolidMarks.Reset();var old=new OldMeshName();var busy=new PlateMeshGate(()=>100,()=>true);
            var r78=Run(new[]{skirt,box},rod,0,budget,78,busy,100,old);
            Check(r78.Charges==0,"0.22.78: the null of the first block stays for the whole shot; no block pays the cavity");
            rows.AppendLine(string.Join("\t","unknown at block 1, known at block 2","0.22.79","b1","Build table (later)",F(r.Cavity),F(r.Skins),F(r.Total),"",0));
            rows.AppendLine(string.Join("\t","unknown at block 1, known at block 2","0.22.78","b1","null for the shot",F(r78.Cavity),F(r78.Skins),F(r78.Total),"",old.Reads));
        }
        // ---------- (4) the cavity line belongs to the block of the front skin ----------
        {
            SolidMarks.Reset();SolidDiagGate.ResetSession();
            var skirt=new LinePlate(400,0xD000,0xD100,false,"Skirt",new(){new(.5,true,false),new(.51,false,true)},t80u,"rha");
            var box=BoxPlate(0xF000,0xF100);SolidMarks.Built(box.Pointer,box.Mesh,true,false,true,()=>box.Name,"Build",false);
            var r=Run(new[]{skirt,box},rod,0,700,79);
            Check(r.CavityNotes.Count==1&&r.CavityNotes[0].Block=="b1","0.22.79: the cavity line is recorded in block b1, the block of the front skin");
            var shot=new CombatReportShot(979,"Outgoing","APFSDS",1600,r.Feed.Events,0,700,"Solid block proof",3,3,"Solid block proof");
            var views=CombatHitProjection.Build(CombatReportTruth.Normalize(shot));
            int row=Array.FindIndex(views,v=>v.Details.Any(d=>d.Text.Contains("solid block: front skin 5 mm, cavity 490 mm",StringComparison.Ordinal)));
            var front=row<0?null:views[row];
            Check(front!=null&&front.Layer?.Parts.Any(p=>p.Entity==501)==true&&front.Layer.Parts.All(p=>p.Entity!=400),$"in the report the solid-block line sits under the row of the box's front skin (row {row+1} of {views.Length}), not under the skirt or the air gap");
            // 0.22.78 order: the note came before LayerStarted of its block, so it attached to the block before (here the air gap behind the skirt)
            var feed78=new Feed();feed78.LayerStarted("b0",new("b0",Array.Empty<CombatLayerPart>(),false),700);feed78.LayerStarted("a1",new("a1",Array.Empty<CombatLayerPart>(),false,490),690);
            feed78.Note("cavity",690,350);var attached=feed78.Events[^1].HitId;feed78.LayerStarted("b1",new("b1",Array.Empty<CombatLayerPart>(),false),350);
            Check(attached=="a1","the 0.22.78 order (SolidCavities before LayerStarted) attached the line to the previous block (a1), as the diagnosis found");
        }
        // ---------- (5) the diag line: content, the limit of 64, nothing for a plate without cassette ----------
        {
            SolidMarks.Reset();SolidDiagGate.ResetSession();
            var box=BoxPlate(0x11000,0x11100);SolidMarks.Built(box.Pointer,box.Mesh,true,false,false,()=>box.Name,"Build",false);
            var r=Run(new[]{box},rod,0,budget);
            var line=r.Diag.Single();diagOut.AppendLine("== (5) content ==");diagOut.AppendLine(line);
            foreach(var part in new[]{"[Solid block] diag shot 1 plate #501:","name \"Composite armour plate [Solid block]\"","mark yes (Build)","gate busy False","4 surfaces, 2 skins, 1 passages","block [1,1.005] m","due 1, charges 1","angle 0°",$"factor {F(f0,"0.####")}",$"ApplyPassive {F(cavity,"0.#")} mm RHA-eq paid, 900 -> ","exit paid."})
                Check(line.Contains(part,StringComparison.Ordinal),"diag content: "+part);
            Check(r.Diag.Count==1,"one line per shot per plate, though the plate is on the line of both of its blocks");
            // every way out has its name
            var unmarked=BoxPlate(0x12000,0x12100) with{Entity=502,Name=CompositePlate.Name};SolidMarks.Built(unmarked.Pointer,unmarked.Mesh,true,false,false,()=>unmarked.Name,"Build",false);
            var ru=Run(new[]{unmarked},rod,0,budget);
            var unknown=BoxPlate(0x13000,0x13100) with{Entity=503};var rk=Run(new[]{unknown},rod,0,budget);
            var odd=BoxPlate(0x14000,0x14100) with{Entity=504,Surfaces=new(){new(1,true,false),new(1.005,false,true),new(1.495,true,false)}};SolidMarks.Built(odd.Pointer,odd.Mesh,true,false,false,()=>odd.Name,"Build",false);
            var ro=Run(new[]{odd},rod,0,budget);
            var boom=BoxPlate(0x15000,0x15100) with{Entity=505};SolidMarks.Built(boom.Pointer,boom.Mesh,true,false,false,()=>boom.Name,"Build",false);
            var rb=Run(new[]{boom},rod,0,budget,factorAt:_=>throw new InvalidOperationException("test"));
            var empty=BoxPlate(0x16000,0x16100) with{Entity=506};SolidMarks.Built(empty.Pointer,empty.Mesh,true,false,false,()=>empty.Name,"Build",false);
            var re=Run(new[]{empty},rod,0,0);
            var child=BoxPlate(0x17000,0x17100) with{Entity=507};SolidMarks.Built(child.Pointer,child.Mesh,true,false,false,()=>child.Name,"Build",false);
            var rc=Run(new[]{child},rod,0,budget,fromM:1.1);
            Check(ru.Diag.Single().Contains("mark no (Build)")&&ru.Diag[0].EndsWith("exit unmarked.")&&ru.Charges==0,"unmarked composite plate: one line, mark no, exit unmarked");
            Check(rk.Diag.Count==1&&rk.Diag[0].EndsWith("exit markUnknown.")&&rk.Diag[0].Contains("no build entry"),"no build entry: exit markUnknown with the reason (one line: the second block is not decisive either)");
            Check(ro.Diag.Single().EndsWith("exit odd.")&&ro.Diag[0].Contains("3 surfaces"),"odd count: exit odd");
            Check(rb.Diag.Single().EndsWith("exit exception.")&&rb.Logs.Count==1&&Math.Abs(rb.Total-skins)<1e-9,"an exception in the calculation: exit exception, one log line, skins only");
            Check(re.Diag.Single().Contains("ApplyPassive false for")&&re.Diag[0].EndsWith("exit applyPassiveFalse."),"no budget left: ApplyPassive refuses, exit applyPassiveFalse");
            Check(rc.Diag.Single().Contains("due 0, charges 0")&&rc.Diag[0].EndsWith("exit notDue."),"a child that starts inside: exit notDue");
            diagOut.AppendLine("== (5) ways out ==");foreach(var l in ru.Diag.Concat(rk.Diag).Concat(ro.Diag).Concat(rb.Diag).Concat(re.Diag).Concat(rc.Diag))diagOut.AppendLine(l);
            // the limit of 64 per session
            SolidDiagGate.ResetSession();int lines=0;
            for(int i=0;i<100;i++)lines+=Run(new[]{box},rod,0,budget).Diag.Count;
            Check(lines==SolidDiagGate.SessionLimit&&SolidDiagGate.Written==64,$"100 shots: {lines} diag lines (at most 64 per session)");
            var after=Run(new[]{box},rod,0,budget);
            Check(after.Diag.Count==0&&after.Charges==1,"after the limit no line is written and the calculation is unchanged");
            // a plate without cassette: no line and no allocation on its path
            SolidDiagGate.ResetSession();var steel=new LinePlate(700,0x18000,0x18100,false,"Glacis",new(){new(1,true,false),new(1.05,false,true)},t80u,"rha");
            var rs=Run(new[]{steel},rod,0,budget);
            Check(rs.Diag.Count==0&&SolidDiagGate.Written==0,"a line with only a plate without cassette writes no diag line");
            var cache=new SolidShotCache<Seen>();cache.Remember(700,null,true);
            long bytes=Allocated(()=>{for(int i=0;i<1000;i++){if(cache.TryCached(700,out var v)&&v==null)continue;}});
            Check(bytes==0,$"the per-shot lookup of a plate without cassette allocates nothing ({bytes} bytes for 1000 lookups)");
            var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
            int scan=gp.IndexOf("if(SolidPlate(reached,shot,entity) is not {} seen)continue;",StringComparison.Ordinal),alloc=gp.IndexOf("plates??=new();",StringComparison.Ordinal),diagNew=gp.IndexOf("new SolidDiag{",StringComparison.Ordinal);
            Check(scan>0&&alloc>scan&&diagNew>alloc,"in the glue the plate without cassette leaves the scan before the first allocation (plates??=new) and before any diag object");
            var plateFn=gp[gp.IndexOf("private static SolidSeen? SolidPlate(",StringComparison.Ordinal)..gp.IndexOf("private static void SolidCavities(",StringComparison.Ordinal)];
            Check(plateFn.Contains("if(shot.Plates.TryCached(entity,out var known))return known;",StringComparison.Ordinal)&&plateFn.IndexOf("new SolidSeen",StringComparison.Ordinal)>plateFn.IndexOf("CompositePlate.IsCassette(plate.armourTechID)",StringComparison.Ordinal),"a SolidSeen is made only for a cassette plate; later blocks find the answer in the shot cache");
        }
        // ---------- HESH on a solid block (the 0.22.78 HESH code is in the same block postfix and the finalizer) ----------
        {
            SolidMarks.Reset();SolidDiagGate.ResetSession();
            var box=BoxPlate(0x19000,0x19100);SolidMarks.Built(box.Pointer,box.Mesh,true,false,false,()=>box.Name,"Build",false);
            double fh=t80u.Factor(ProtectionThreat.Hesh,0);var r=Run(new[]{box},ProtectionThreat.Hesh,0,450);
            Check(fh>0&&r.Charges==1&&Math.Abs(r.Cavity-fh*490)<1e-9*Math.Max(1,fh*490)&&r.Feed.Events.Count(e=>e.Text.Contains("solid block:",StringComparison.Ordinal))==1,$"HESH on the solid block: the cavity is charged once ({F(r.Cavity)} mm RHA-eq at factor {F(fh,"0.####")}), claimed on the ledger, never again in the back-skin block");
            // the finalizer's HESH step only turns a native ricochet into a stop; a fragment that is already stopped or embedded is left alone (no second stop)
            var frags=new Fragments();frags.Items.Add((HeshRules.Original|HeshRules.Deflected,-1,300));frags.Items.Add((HeshRules.Original,0,250));
            var first=HeshRules.Surface(frags,0,1,20,75);var second=HeshRules.Surface(frags,0,1,20,75);
            Check(first.Converted&&!second.Converted&&frags.Items[1].Speed==0,"the HESH surface stop runs once: a second pass over the same fragments changes nothing (the parent is Embedded)");
        }
        // ---------- source: one route for the name, no new hook, the Weigh line ----------
        {
            var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
            int hit=gp.IndexOf("private static SolidSeen? SolidPlate(",StringComparison.Ordinal),hitEnd=gp.IndexOf("#endif",hit,StringComparison.Ordinal);var hitPath=gp[hit..hitEnd];
            Check(!hitPath.Contains("MeshName(",StringComparison.Ordinal)&&!hitPath.Contains("ToMeshBlueprint",StringComparison.Ordinal)&&!hitPath.Contains("ToJson(",StringComparison.Ordinal),"the hit path (SolidPlate, SolidCavities, SolidCavity) reads no name and no mesh: no second route");
            Check(gp.Split("ToMeshBlueprint(false)?.Name").Length==2&&gp.IndexOf("ToMeshBlueprint(false)?.Name",StringComparison.Ordinal)<hit,"the only name read of the solid path is in the Build step (SolidMarkNote)");
            Check(!gp.Contains("[HarmonyPatch",StringComparison.Ordinal)&&!fill.Contains("[HarmonyPatch",StringComparison.Ordinal),"no hook in the two files");
            var weigh=fill[fill.IndexOf("internal static void Weigh(PlateStructure plate)",StringComparison.Ordinal)..];
            Check(weigh.Contains("        try\r\n        {\r\n            NativeMaterialReactions.SolidMarkBuilt(plate);",StringComparison.Ordinal),"Weigh (the existing PlateStructure.Build postfix, RuntimeCassettePlateVisual.Built) calls SolidMarkBuilt first");
            Check(gp.Contains("                if(first.objectIndex>=0&&first.objectIndex<sim.objects.Length)SolidCavities(reached,first,last);",StringComparison.Ordinal),"SolidCavities is still called at the same place, before the unchanged route");
        }
        // ---------- (6) the FEAT77 tests still pass (this run) ----------
        {
            var f77=Path.Combine(output,"FEAT77-RESULT.json");
            using var doc=File.Exists(f77)?JsonDocument.Parse(File.ReadAllText(f77)):null;
            Check(doc!=null&&doc.RootElement.GetProperty("passed").GetBoolean()&&doc.RootElement.GetProperty("failures").GetInt32()==0&&File.GetLastWriteTimeUtc(f77)>DateTime.UtcNow.AddHours(-2),"FEAT77 passed in this run ("+(doc?.RootElement.GetProperty("checks").GetInt32().ToString(CultureInfo.InvariantCulture)??"missing")+" checks)");
        }
        File.WriteAllText(Path.Combine(output,"SOLID-MARK-CASES.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"SOLID-DIAG-LINES.txt"),diagOut.ToString());
        File.WriteAllText(Path.Combine(output,"FEAT79-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures,diagSessionLimit=SolidDiagGate.SessionLimit},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT79 PASS: {checks} checks, {failures} failures");
        SolidMarks.Reset();SolidDiagGate.ResetSession();
    }
}
