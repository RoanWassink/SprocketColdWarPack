using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using SprocketExpandedBallistics.Materials;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Tests;
internal static class PerfDetailChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new ModularFastState(-2,1,3,new IntPtr(10),new IntPtr(11),new IntPtr(12),3,true);
        check(state.Valid,"signed mirror scale is valid in fast native-generation guard");
        check(state!=state with{Wrapper=new IntPtr(20)},"same-count loaded/cloned wrapper invalidates fast guard");
        check(state!=state with{Interaction=new IntPtr(20)},"same-wrapper reset/default visual invalidates fast guard");
        check(state!=state with{Collision=IntPtr.Zero},"lost native collision invalidates fast guard");
        check(state!=state with{ScaleX=3},"resize invalidates fast guard");
        check(state!=state with{LodCount=1},"native LOD replacement invalidates fast guard");
        check(state!=state with{Visible=false},"native visibility transition invalidates fast guard");
        check(!(state with{ScaleX=float.NaN}).Valid,"invalid temporary load scale never fast-commits");
        var models=new Dictionary<IntPtr,ModularFastState>{{new IntPtr(101),state},{new IntPtr(202),state}};
        models[new IntPtr(101)]=state with{ScaleX=-3};
        check(models[new IntPtr(202)]==state,"mirror owners cannot share mutable fast-generation state");
        // Warmup first. This is managed guard cost, not a native game FPS test.
        var key=new IntPtr(202);int hits=0;
        for(int i=0;i<1000;i++)if(models.TryGetValue(key,out var old)&&old==state&&state.Valid)hits++;
        var allocated=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();
        for(int i=0;i<100000;i++)if(models.TryGetValue(key,out var old)&&old==state&&state.Valid)hits++;
        clock.Stop();var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(hits==101000&&bytes<256,"100000 unchanged native-generation guards allocate no per-event objects");
        var metrics=new List<object>();
        foreach(var spec in new[]{ModularCellSpec.DupletV1,ModularCellSpec.DupletSlim})
        foreach(var layout in new[]{new ModularCellLayout(1,1),new ModularCellLayout(2,2),new ModularCellLayout(8,8)})
        {
            var near=ModularCellGeometry.Build(ModularPartKind.Stage2Visual,layout,spec,mountingDetail:true);
            var far=ModularCellGeometry.Build(ModularPartKind.Stage2Visual,layout,spec);
            foreach(var scale in new[]{1.0,8.0})
            {
                var compensated=ModularCellGeometry.Build(ModularPartKind.Stage2Visual,layout,spec,compensateX:scale,compensateZ:scale,mountingDetail:true);
                check(Enumerable.Range(0,compensated.Triangles.Length/3).All(t=>{
                    var a=compensated.Vertices[compensated.Triangles[3*t]];var b=compensated.Vertices[compensated.Triangles[3*t+1]];var c=compensated.Vertices[compensated.Triangles[3*t+2]];
                    return Vector3.Cross(b-a,c-a).LengthSquared()>1e-20f;
                }),"every detailed compensated triangle is nondegenerate at the production mesh conversion threshold");
            }
            check(near.TriangleCells.All(c=>c>=0)&&far.TriangleCells.All(c=>c>=0),"render slabs omit redundant shared perimeter faces hidden by the housing");
            check(near.Triangles.Length>far.Triangles.Length,"near cover contains actual mounting recess geometry");
            check(near.Triangles.Length<=24000,"detailed maximum layout remains within flat visual vertex limit");
            check(near.Vertices.Min(v=>v.X)==far.Vertices.Min(v=>v.X)&&near.Vertices.Max(v=>v.X)==far.Vertices.Max(v=>v.X)
                &&near.Vertices.Min(v=>v.Z)==far.Vertices.Min(v=>v.Z)&&near.Vertices.Max(v=>v.Z)==far.Vertices.Max(v=>v.Z)
                &&near.Vertices.Min(v=>v.Y)==far.Vertices.Min(v=>v.Y)&&near.Vertices.Max(v=>v.Y)==far.Vertices.Max(v=>v.Y),
                "detailed near and baseline far cover envelopes match exactly");
            var collision=ModularCellGeometry.Build(ModularPartKind.Stage2Collision,layout,spec);
            var collisionDetail=ModularCellGeometry.Build(ModularPartKind.Stage2Collision,layout,spec,mountingDetail:true);
            check(collision.Vertices.SequenceEqual(collisionDetail.Vertices)&&collision.Triangles.SequenceEqual(collisionDetail.Triangles),
                "decorative mounting option cannot change paid collision geometry");
            var spent=ModularCellGeometry.Build(ModularPartKind.Stage2Visual,layout,spec,new HashSet<int>{0},mountingDetail:true);
            check(!spent.TriangleCells.Contains(0),"spent cell removes its own lid AND mounting recesses");
            metrics.Add(new{depthMm=spec.DepthM*1000,layout=$"{layout.CountU}x{layout.CountV}",nearTriangles=near.Triangles.Length/3,
                farTriangles=far.Triangles.Length/3,nearFlatVertices=near.Triangles.Length,collisionTriangles=collision.Triangles.Length/3});
        }
        var dir=Environment.GetEnvironmentVariable("MODULAR_PREVIEW_DIR");
        if(!string.IsNullOrEmpty(dir))File.WriteAllText(Path.Combine(dir,"performance-detail-metrics.json"),
            JsonSerializer.Serialize(new{guardIterations=100000,guardAllocatedBytes=bytes,guardElapsedMs=clock.Elapsed.TotalMilliseconds,geometry=metrics},new JsonSerializerOptions{WriteIndented=true}));
    }
}
