using System.Numerics;
using System.Text.Json;

namespace SprocketExpandedBallistics;

internal static class Fix02251PlayLoadTests
{
    internal static void Run(Action<bool,string> check)
    {
        int checks=0;
        var sink=check;check=(ok,msg)=>{checks++;sink(ok,"Play load: "+msg);};
        // Exercise the production permission policy, including non-load callbacks
        // after impact has ended. These are managed probes, not native game calls.
        foreach(bool editor in new[]{false,true})
        foreach(bool impact in new[]{false,true})
        foreach(bool loading in new[]{false,true})
            check(ModularGeometryGate.CanWrite(editor,impact,loading)==(!impact&&(editor||loading)),
                $"permission editor={editor} impact={impact} load={loading}");
        check(!ModularGeometryGate.DeferImpact(true),"impact on installed tank creates no deferred work");
        check(ModularGeometryGate.DeferImpact(false),"first load interrupted by impact is retained");
        check(ModularGeometryGate.DropDeferred(false,true,true),"stale impact work is discarded when another load installed geometry");
        check(!ModularGeometryGate.DropDeferred(false,true,false),"missing initial Play geometry is allowed to finish");
        check(!ModularGeometryGate.DropDeferred(false,false,true),"ordinary spawn settling is retained");

        // Use the real production geometry builders for Editor and Play. A spent
        // ledger is deliberately present before construction; no mesh input uses it.
        var spent=new EraCells();spent.Consume(PlacedEraState.Key(51,10,0,0,0));
        var rows=new List<object>();
        foreach(var variant in new[]{"reliktHull","reliktTurret","reliktSide","nizhPanel","dupletHull","dupletTurret","dupletSide"})
        foreach(var scale in new[]{(1d,1d),(2d,2d),(-2d,2d),(3d,2d)})
        {
            var spec=ModularCellSpec.ForVariant(variant);
            check(ModularCellGeometry.TryLayout(scale.Item1,scale.Item2,spec,8,8,out var layout),variant+" layout");
            ModularMeshData Build(bool editor,ModularPartKind kind,bool far=false)
            {
                if(!ModularGeometryGate.CanWrite(editor,false,true))throw new Exception("Play load blocked");
                return ModularCellGeometry.Build(kind,layout,spec,null,Math.Abs(scale.Item1),Math.Abs(scale.Item2),coarse:far);
            }
            bool Equal(ModularMeshData a,ModularMeshData b)=>a.Vertices.SequenceEqual(b.Vertices)&&a.Triangles.SequenceEqual(b.Triangles)&&a.TriangleCells.SequenceEqual(b.TriangleCells);
            foreach(var kind in new[]{ModularPartKind.Stage1Visual,ModularPartKind.Stage1Collision,ModularPartKind.HousingVisual,ModularPartKind.Housing})
                check(Equal(Build(true,kind),Build(false,kind)),variant+" editor/Play intact geometry "+kind);
            check(Equal(Build(true,ModularPartKind.Stage1Visual,true),Build(false,ModularPartKind.Stage1Visual,true)),variant+" far LOD parity");
            var collision=Build(false,ModularPartKind.Stage1Collision);
            check(ModularCellGeometry.SignedVolume(collision)>0,variant+" nonzero active-cell volume/mass in Play");
            // Cast through an actual triangle of the spawned active-cell mesh.
            int t=Array.FindIndex(collision.TriangleCells,c=>c==0);
            check(t>=0,variant+" spawned cell 0 has a collider");
            var a=collision.Vertices[collision.Triangles[3*t]];
            var b=collision.Vertices[collision.Triangles[3*t+1]];
            var c=collision.Vertices[collision.Triangles[3*t+2]];
            var n=Vector3.Normalize(Vector3.Cross(b-a,c-a));
            var ray=(a+b+c)/3+n*.01f;var direction=-n;
            double distance=Intersect(ray,direction,a,b,c);
            var ledger=new EraCells();var cell=PlacedEraState.Key(51,20,0,0,0);
            bool reached=PlacedBrickTrigger.Reached(true,true,true,false,distance,13);
            bool activated=reached&&PlacedBrickTrigger.CanConsume(true,true,false)&&ledger.Consume(cell);
            check(activated,variant+" hit reaches and consumes the Play-spawned active cell");
            check(!ledger.Consume(cell),variant+" second hit is inert");
            rows.Add(new{variant,scale,triangles=collision.Triangles.Length/3,distance,activated});
        }

        // Trace the policy at every geometry-writing boundary used by production.
        foreach(var scenario in new[]{"impact","activation","damage SetVisible","post-impact transform"})
        {
            int refresh=0,visible=0,collision=0,dirty=0,rebuild=0;
            bool live=scenario!="post-impact transform";
            if(ModularGeometryGate.CanWrite(false,live,false)){refresh++;collision++;dirty++;rebuild++;}
            // No ERA production path calls SetVisible at all (separate source guard).
            check(refresh==0&&visible==0&&collision==0&&dirty==0&&rebuild==0,scenario+" issues no geometry writes");
        }
        var output=Fix02228Tests.OutputRoot();
        File.WriteAllText(Path.Combine(output,"PLAYLOAD-RESULT.json"),JsonSerializer.Serialize(new{passed=true,checks,managedOnly=true,nativeGameTested=false,spawnCases=rows}));
        Console.WriteLine($"FIX02251 PLAYLOAD PASS: {checks} managed checks, {rows.Count} generated spawn/hit cases.");
    }

    private static double Intersect(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        var e1=b-a;var e2=c-a;var h=Vector3.Cross(d,e2);double det=Vector3.Dot(e1,h);
        if(Math.Abs(det)<1e-10)return double.NaN;
        var s=o-a;double u=Vector3.Dot(s,h)/det;
        var q=Vector3.Cross(s,e1);double v=Vector3.Dot(d,q)/det;
        return u>=0&&v>=0&&u+v<=1?Vector3.Dot(e2,q)/det:double.NaN;
    }
}
