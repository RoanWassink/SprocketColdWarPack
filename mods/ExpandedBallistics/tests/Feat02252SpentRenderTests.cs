using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
namespace SprocketExpandedBallistics;
internal static class Feat02252SpentRenderTests
{
    // 0.22.70 adds the render-only housing look core next to the cell cut.
    internal static readonly string[] Changed={"Material/EraSpentRender.cs","SpentCellRenderPolicy.cs","HousingSpentLook.cs"};
    internal static void Run(Action<bool,string> sink)
    {
        int checks=0;void Check(bool ok,string message){checks++;sink(ok,"Spent render: "+message);}
        var root=Fix02228Tests.SourceRoot();var output=Fix02228Tests.OutputRoot();
        var rows=new StringBuilder("variant\tlayout\tlod\tbefore_triangles\tafter_one\tafter_two\tframe_triangles\trenderers_before\trenderers_after\n");
        // Exact VUID is mandatory even when a shared/combined renderer contains a known mesh.
        Check(SpentCellRenderPolicy.ExactOwner(984,984,984,true),"exact component/group/renderer accepted");
        Check(!SpentCellRenderPolicy.ExactOwner(984,984,0,true),"combined vehicle renderer cannot be hidden");
        Check(!SpentCellRenderPolicy.ExactOwner(984,985,984,true),"foreign group rejected");
        Check(!SpentCellRenderPolicy.ExactOwner(984,984,985,true),"neighbour cell renderer rejected");
        Check(!SpentCellRenderPolicy.ExactOwner(984,984,984,false),"replaced/default mesh rejected");
        foreach(var variant in new[]{"reliktHull","reliktTurret","reliktSide","nizhPanel","dupletHull","dupletTurret","dupletSide"})
        foreach(var size in new[]{(1d,1d),(2d,2d),(-2d,2d),(3d,2d)})
        {
            var spec=ModularCellSpec.ForVariant(variant);
            Check(ModularCellGeometry.TryLayout(size.Item1,size.Item2,spec,8,8,out var layout),"layout "+variant);
            foreach(bool far in new[]{false,true})
            foreach(var kind in new[]{ModularPartKind.Stage1Visual,ModularPartKind.Stage2Visual})
            {
                if(kind==ModularPartKind.Stage2Visual&&(spec.ReliktStyle!=0||DupletModularGeometry.SingleLayer(spec)))continue;
                var mesh=ModularCellGeometry.Build(kind,layout,spec,null,Math.Abs(size.Item1),Math.Abs(size.Item2),mountingDetail:!far&&kind==ModularPartKind.Stage2Visual,coarse:far);
                var original=(int[])mesh.Triangles.Clone();var spent=new HashSet<int>{0};
                var one=SpentCellRenderPolicy.Filter(original,mesh.TriangleCells,spent);
                int removed=mesh.TriangleCells.Count(c=>c==0);
                Check(removed>0&&one.Length==original.Length-3*removed,$"{variant} {kind} exact single cell cut far={far}");
                var expected=Enumerable.Range(0,mesh.TriangleCells.Length).Where(t=>mesh.TriangleCells[t]!=0).SelectMany(t=>original.Skip(t*3).Take(3));
                Check(one.SequenceEqual(expected),"neighbours and persistent frame indices byte-exact");
                Check(!spent.Add(0)&&one.SequenceEqual(SpentCellRenderPolicy.Filter(original,mesh.TriangleCells,spent)),"same cell twice is idempotent");
                int[] two=one;
                if(layout.CellCount>1)
                {
                    spent.Add(1);two=SpentCellRenderPolicy.Filter(original,mesh.TriangleCells,spent);
                    Check(two.Length==one.Length-3*mesh.TriangleCells.Count(c=>c==1),"second distinct cell only removes its own triangles");
                }
                Check(mesh.Triangles.SequenceEqual(original),"shared source/collision generation remains unchanged for another owner");
                var frame=ModularCellGeometry.Build(ModularPartKind.HousingVisual,layout,spec,null,Math.Abs(size.Item1),Math.Abs(size.Item2));
                Check(frame.Triangles.Length>0,"housing remains visible");
                // Same pure index projection used by every native renderer slot: body, housing, adjacent active layer stay independent.
                int rest=12000+frame.Triangles.Length/3;int before=rest+original.Length/3;int after=rest+one.Length/3;
                Check(before-after==removed&&after>=rest,"whole tank retains body and housing triangles");
                spent.Clear();
                Check(SpentCellRenderPolicy.Filter(original,mesh.TriangleCells,spent).SequenceEqual(original),"Edit reset restores all original triangles");
                foreach(string mode in new[]{"AI","Expedition","Sandbox","Edit-to-Play"})
                    Check(ModularGeometryGate.CanWrite(false,false,true)&&SpentCellRenderPolicy.Filter(original,mesh.TriangleCells,spent).Length==original.Length,mode+" load has all cells visible");
                rows.AppendLine($"{variant}/{kind}\t{layout.CountU}x{layout.CountV}\t{(far?"far/shadow":"near")}\t{original.Length/3}\t{one.Length/3}\t{two.Length/3}\t{mesh.TriangleCells.Count(c=>c<0)}\t3\t{2+(one.Length>0?1:0)}");
            }
        }
        // The logged #984/#985 are independent single-cell components: even identical shared source indices are immutable.
        int[] legacy={0,1,2,3,4,5};int[] own={0,0};
        var cell984=SpentCellRenderPolicy.Filter(legacy,own,new HashSet<int>{0});
        Check(cell984.Length==0&&legacy.Length==6,"legacy #984 disappears while #985 sharing the source stays visible");
        Check(SpentCellRenderPolicy.Filter(legacy,own,new HashSet<int>{0}).Length==0,"legacy #985 subsequently disappears independently");
        var ledger=new EraCells();var a=PlacedEraState.Key(52,984,0,0,0);var b=PlacedEraState.Key(52,985,0,0,0);
        Check(ledger.Consume(a)&&!ledger.Consume(a)&&ledger.Consume(b),"actual ledger detonates two distinct cells once each");
        ledger.Clear();Check(ledger.Consume(a),"actual Edit ledger reset rearms the cell");
        var render=File.ReadAllText(Path.Combine(root,"Material/EraSpentRender.cs"));
        Check(render.Contains("if(LiveImpact||Environment.CurrentManagedThreadId!=mainThread"),"render drain pauses for the existing LiveImpact signal");
        foreach(var forbidden in new[]{"RefreshModel(","SetCollisionMesh(","MarkCollidersDirty(","RequestRebuild(",".SetVisible(",".MarkDynamic(",".SetMesh(",".SetActive(","[HarmonyPatch"})
            Check(!render.Contains(forbidden),"render-only path cannot call "+forbidden);
        Check(render.Contains("PlacedEra.Activated+=Activated")&&render.Contains("PlacedEra.Activated-=Activated"),"existing activation signal has symmetric subscribe/unsubscribe");
        Check(render.Contains("m.VehicleRoot?.Pointer==a.VehicleRootPointer&&m.Vehicle?.Pointer==a.VehiclePointer"),"activation is exact vehicle/root/VUID scoped");
        Check(render.Contains("if(OwnCopy)Bind(Source);")&&render.Contains("Object.Destroy(Copy)"),"restore owns exact renderer instance and destroys only private copies");
        Check(render.Contains("source.GetTriangles(sub)")&&render.Contains("copy.SetTriangles("),"all material submesh slots preserved");
        var proto=File.ReadAllText(Path.Combine(root,"Material/ReliktPrototype.cs"));
        Check(proto.Contains("EraSpentRender.Register(mesh,data.TriangleCells)")&&proto.Contains("EraSpentRender.Tick();"),"production cell metadata and existing late callback connected");
        Check(proto.IndexOf("EraSpentRender.BeforeGeometry(model)")<proto.IndexOf("var ready=ApplyCore(model)"),"render copies detached before native generation replacement");
        File.WriteAllText(Path.Combine(output,"SPENT-RENDER-COUNTS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"SPENT-RENDER-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,native=false,rootCauseReproduced=false,aliasContainment=true}));
        Console.WriteLine($"0.22.52 SPENT RENDER PASS: {checks} checks (managed; native visibility still unverified).");
    }
}
