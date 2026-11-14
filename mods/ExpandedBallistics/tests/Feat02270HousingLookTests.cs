using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpandedBallistics.Materials;
namespace SprocketExpandedBallistics;

/// 0.22.70: after an own unit fires, an ERA housing draws its existing damaged form (render only, EraSpentRender).
/// Pure rules, the binding book on a managed double, every preset's state files, source guards and bit-exactness
/// against 0.22.69. The production adapter itself runs on doubles in test-output/render-harness (tests 1-5).
internal static class Feat02270HousingLookTests
{
    internal static readonly string[] Changed={"Material/EraSpentRender.cs","Material/ReliktPrototype.cs","HousingSpentLook.cs","Plugin.cs","SprocketExpandedBallistics.csproj",
        // 0.22.71 (relayering, Apply choice, several layers, Forensic bands, log switch) on top of 0.22.70:
        "ComposerFill.cs","RuntimeComposerFill.cs","RuntimeGroupedProtection.cs","LayeredPlateReport.cs","CassetteComposerModel.cs","CassetteComposerPanel.cs","CassetteSectionDrawing.cs","CombatForensicGeometry.cs","CombatForensicDrawing.cs",
        // 0.22.72 (Forensic draws internal modules as framed icons) on top of 0.22.71:
        "CombatForensics.cs"};
        // Deterministic suite outputs that carry protection, ledger, report and JSON results (test 7).
    internal static readonly string[] SameOutputs={"PROTECTION-TABLES.tsv","PROTECTION-02244.tsv","PROTECTION-02245.tsv","PROTECTION-02246.tsv","GOLDEN-02239.tsv","MEAN-GOLDEN.tsv","INTERVAL-IMPACT.tsv",
        "READABLE-INTERVALS.tsv","LAYER-PROTECTION.tsv","UNIFORM-HIT.tsv","UNIFORM-SHOTS.tsv","UNIFORM-STOP.tsv","UNIFORM-REPORT-LINES.txt","ERA-02238.tsv","ERA-02246-PARITY.tsv","ERA-FINGERPRINT-CASES.tsv","MASS-02238.tsv",
        "MODULE-FACTORS.tsv","CHEEK-FACTORS.tsv","DEPTH-CHECK.tsv","GEOMETRY-02233.tsv","EXPECTED-CASES.tsv","SPENT-RENDER-COUNTS.tsv","CHAIN-02251-NEW.tsv","SIMULATED-MODULE-HIT.json","FORENSIC-LAYERS.json","THREE-PIECES.json",
        "APPLY-SCALE-FORENSIC.json","BOX-EVIDENCE.json","DUMP-SAMPLE-plate-366.json"};
    // Not compared (differ for reasons outside the change): GOLDEN-SOURCE.tsv lists HousingSpentLook.cs, LAYER-COST.tsv/LAYER-EVIDENCE.json hold
    // timings, BLUEPRINT-PREVIEW.json holds out-folder paths, PLAY-MUTATION-INVENTORY.tsv holds ReliktPrototype line numbers (+2).
    private sealed class FakeMesh{internal string Asset="";internal int Submeshes=1;internal bool Destroyed;}
    private sealed class FakeSlot{internal FakeMesh? Mesh;internal int Instance;internal bool Alive=true;}
    private sealed class FakeHost:IHousingLookHost<FakeSlot,FakeMesh>
    {
        internal int Created,Destroyed,Binds;internal int CopySubmeshes=1;
        public FakeMesh? Current(FakeSlot s)=>s.Mesh;
        public bool Same(FakeMesh? a,FakeMesh b)=>ReferenceEquals(a,b);
        public int Instance(FakeSlot s)=>s.Instance;
        public bool Alive(FakeSlot s)=>s.Alive;
        public void Bind(FakeSlot s,FakeMesh m){Binds++;s.Mesh=m;}
        public FakeMesh Create(string asset){Created++;return new(){Asset=asset,Submeshes=CopySubmeshes};}
        public void Destroy(FakeMesh m){if(!m.Destroyed){m.Destroyed=true;Destroyed++;}}
        public int Submeshes(FakeMesh m)=>m.Submeshes;
    }
    internal static void Run(Action<bool,string> sink)
    {
        int checks=0;void Check(bool ok,string message){checks++;sink(ok,"FEAT70 housing look: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        // Count rule (0.22.50): visuals[spent own units]; anything inexact fails closed.
        string[] k5={"kontakt5Stage1Cell1","kontakt5Stage1Cell2"};string[] k5v={"s0","s1","s2"};
        HousingMember M(HousingMember a,HousingMember b,string id)=>id==k5[0]?a:b;
        Check(HousingSpentPolicy.State(k5,k5v,id=>M(HousingMember.Intact,HousingMember.Intact,id))==0,"two intact units: state 0");
        Check(HousingSpentPolicy.State(k5,k5v,id=>M(HousingMember.Spent,HousingMember.Intact,id))==1&&HousingSpentPolicy.State(k5,k5v,id=>M(HousingMember.Intact,HousingMember.Spent,id))==1,"either unit spent: state 1");
        Check(HousingSpentPolicy.State(k5,k5v,id=>HousingMember.Spent)==2,"both spent: state 2");
        foreach(var bad in new[]{HousingMember.Missing,HousingMember.Duplicate,HousingMember.Foreign})
            Check(HousingSpentPolicy.State(k5,k5v,id=>M(HousingMember.Spent,bad,id))==HousingSpentPolicy.FailClosed,bad+" member fails closed");
        Check(HousingSpentPolicy.State(k5,new[]{"s0","s1"},id=>HousingMember.Spent)==-1&&HousingSpentPolicy.State(null,k5v,id=>HousingMember.Spent)==-1
            &&HousingSpentPolicy.State(new[]{"a","a"},k5v,id=>HousingMember.Spent)==-1&&HousingSpentPolicy.State(Array.Empty<string>(),new[]{"s0"},id=>HousingMember.Spent)==-1,"malformed series or repeated id fails closed");
        Check(HousingSpentPolicy.Fits(1,1,1)&&!HousingSpentPolicy.Fits(1,2,1)&&!HousingSpentPolicy.Fits(1,1,2)&&!HousingSpentPolicy.Fits(0,0,0)&&HousingSpentPolicy.Fits(2,2,2),"submesh/material fit");
        Check(HousingSpentPolicy.Candidate(5,5,5,true)&&!HousingSpentPolicy.Candidate(5,5,0,true)&&!HousingSpentPolicy.Candidate(5,6,5,true)&&!HousingSpentPolicy.Candidate(5,5,5,false),"only exact renderers showing the intact mesh");
        // Book: atomic, owned, restored, never leaking.
        var host=new FakeHost();var intact=new FakeMesh{Asset="s0"};var slots=Enumerable.Range(1,7).Select(i=>new FakeSlot{Mesh=intact,Instance=i}).ToList();
        var book=new HousingLookBook<FakeSlot,FakeMesh>();var cands=slots.Select(s=>(s,intact)).ToList();
        Check(book.Apply(host,cands,1,"s1",1)==null&&slots.All(s=>s.Mesh!=intact&&s.Mesh!.Asset=="s1")&&book.State==1&&book.Count==7&&book.AllOwned(host),"apply binds one private copy per renderer");
        var first=slots.Select(s=>s.Mesh!).ToList();book.Restore(host);
        Check(slots.All(s=>s.Mesh==intact)&&first.All(m=>m.Destroyed)&&book.State==0&&book.Count==0,"restore binds the original and destroys every copy");
        Check(book.Apply(host,cands,2,"s2",1)==null,"re-apply");slots[0].Mesh=new FakeMesh{Asset="native"};slots[1].Instance=99;slots[2].Alive=false;
        Check(!book.AllOwned(host),"a newer native binding/renderer is detected");var kept=slots[0].Mesh;book.Restore(host);
        Check(slots[0].Mesh==kept&&slots[1].Mesh!.Asset=="s2"&&slots.Skip(3).All(s=>s.Mesh==intact),"restore never overwrites a binding it does not own");
        host.CopySubmeshes=2;int binds=host.Binds;var fresh=Enumerable.Range(1,7).Select(i=>new FakeSlot{Mesh=intact,Instance=i}).ToList();
        Check(book.Apply(host,fresh.Select(s=>(s,intact)).ToList(),1,"s1",1) is {} reason&&reason.Contains("submesh")&&host.Binds==binds&&fresh.All(s=>s.Mesh==intact)&&book.Count==0,"submesh mismatch: nothing bound");
        Check(book.Apply(host,new List<(FakeSlot,FakeMesh)>(),1,"s1",1)!=null&&host.Binds==binds,"no candidate: nothing bound");
        Check(host.Created==host.Destroyed,$"book leaks nothing ({host.Created} created, {host.Destroyed} destroyed)");
        // Test 6: every preset's damage series exists in era-assets with a fitting submesh count.
        var assets=Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets");
        var table=new StringBuilder("housing\tunits\tstate\tasset\ttriangles\tsubmeshes\tsha256\n");int presets=0;var housings=new HashSet<string>();
        foreach(var p in EraPresetRegistry.Presets.Where(p=>p.HousingDamageVisuals!=null))
        {
            presets++;var ids=p.HousingActiveIds!;var visuals=p.HousingDamageVisuals!;
            Check(visuals.Length==ids.Length+1&&ids.Contains(p.Cassette)&&!p.WholeCells,p.Mount+": series has units+1 states and names its own unit");
            Check(EraPresetRegistry.Presets.Where(q=>q.Mount==p.Mount).All(q=>q.HousingDamageVisuals!.SequenceEqual(visuals)&&q.HousingActiveIds!.SequenceEqual(ids)),p.Mount+": every own unit declares the same series");
            if(!housings.Add(p.Mount))continue;
            for(int s=0;s<visuals.Length;s++)
            {
                var path=Path.Combine(visuals[s].StartsWith("kontakt5Wt",StringComparison.Ordinal)?Path.Combine(root,"Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets"):assets,visuals[s]);bool exists=File.Exists(path); // 0.22.73: the War Thunder variants ship from Material/files
                Check(exists,p.Mount+" state "+s+" file exists: "+visuals[s]);if(!exists)continue;
                var lines=File.ReadAllLines(path);
                var tris=EraObjGeometry.Read(lines,"mount",(x,y,z)=>new System.Numerics.Vector3(x,y,z),v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)).Count/3;
                bool single=!lines.Any(l=>l.StartsWith("usemtl ",StringComparison.Ordinal))&&lines.Where(l=>l.StartsWith("g ",StringComparison.Ordinal)).Select(l=>l.Trim()).Distinct().SequenceEqual(new[]{"g mount"});
                Check(tris>0&&single,p.Mount+" state "+s+": one 'mount' group, one submesh like the intact render ("+tris+" triangles)");
                table.AppendLine($"{p.Mount}\t{ids.Length}\t{s}\t{visuals[s]}\t{tris}\t1\t{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}");
            }
        }
        Check(presets==52&&housings.Count==29,$"all housing presets covered ({presets} presets, {housings.Count} housings)");
        File.WriteAllText(Path.Combine(output,"HOUSING-PRESETS.tsv"),table.ToString());
        // The harness preset rows are the production rows.
        var defs=File.ReadAllText(Path.Combine(root,"Material/EraGeneratedDefinitions.cs"));
        foreach(var row in new[]{"new string[]{\"kontakt1Stage1Cell1\"},new string[]{\"kontakt1-housing-state-0.obj\",\"kontakt1-housing-state-1.obj\"}",
            "new string[]{\"kontakt5Stage1Cell1\",\"kontakt5Stage1Cell2\"},new string[]{\"kontakt5-housing-state-0.obj\",\"kontakt5-housing-state-1.obj\",\"kontakt5-housing-state-2.obj\"}",
            "new string[]{\"reliktStage1Cell1\",\"reliktStage1Cell2\"},new string[]{\"relikt-housing-state-0.obj\",\"relikt-housing-state-1.obj\",\"relikt-housing-state-2.obj\"}"})
            Check(defs.Contains(row),"harness uses the production series "+row[..40]);
        // Test 4 and route: render-only source guards.
        var render=File.ReadAllText(Path.Combine(root,"Material/EraSpentRender.cs"));var look=File.ReadAllText(Path.Combine(root,"HousingSpentLook.cs"));
        foreach(var forbidden in new[]{"RefreshModel(","SetCollisionMesh(","MarkCollidersDirty(","RequestRebuild(",".SetVisible(",".MarkDynamic(",".SetMesh(",".SetActive(","SetInteractionMesh(","SetShadowProxy(","SetMaterials(","SetCost(","[HarmonyPatch",".mass","ResetForEdit()"})
            Check(!look.Contains(forbidden)&&(forbidden=="ResetForEdit()"||!render.Contains(forbidden)),"housing look cannot call "+forbidden);
        Check(render.Contains("if(LiveImpact||Environment.CurrentManagedThreadId!=mainThread"),"the drain still pauses for LiveImpact");
        var activated=render[render.IndexOf("private static void Activated(",StringComparison.Ordinal)..render.IndexOf("internal static void ResetForEdit(",StringComparison.Ordinal)];
        Check(!activated.Contains("Bind(")&&!activated.Contains("Restore(")&&!activated.Contains("Create(")&&!activated.Contains("Components"),"activation only queues (no binding, mesh or component walk)");
        var track=render[render.IndexOf("private static void TrackHousing(",StringComparison.Ordinal)..render.IndexOf("private static Vector3[] ReadHousingLook(",StringComparison.Ordinal)];
        Check(track.Contains("ReadHousingLook(")&&!render[render.IndexOf("private static bool ReconcileHousing(",StringComparison.Ordinal)..].Contains("ReadAllLines"),"state files are read at load/build (Track), never in the drain");
        Check(render.Contains("PlacedEra.IsSpent(root,match.VUID.Value)")&&render.Contains("return HousingMember.Foreign")&&render.Contains("return HousingMember.Duplicate"),"own units are counted from the authoritative ledger with exact membership");
        var proto=File.ReadAllText(Path.Combine(root,"Material/ReliktPrototype.cs"));
        Check(!proto.Contains("PlacedEra.",StringComparison.Ordinal)&&proto.Contains("visualOverride:IntactHousingVisual(model)"),"construction still reads no ledger and builds state 0");
        File.WriteAllText(Path.Combine(output,"FEAT70-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,native=false}));
        Console.WriteLine($"FEAT70 PASS: {checks} checks (managed; the drawn housing in the game is still unverified).");
    }
}
