using System.Diagnostics;
using SprocketUiKit;

internal static class GraphTests
{
    internal static void Run(Action<bool,string> check)
    {
        bool Near(float a,float b)=>MathF.Abs(a-b)<.001f;
        var pan=new Vec(23,-17); var p=new Vec(-42,300);
        foreach(float zoom in new[]{.5f,1,1.5f})
        {
            var local=GraphMath.Local(p,pan,zoom);var back=GraphMath.Graph(local,pan,zoom);
            check(Near(back.X,p.X)&&Near(back.Y,p.Y),"graph transform round trip at "+zoom);
            var cursor=new Vec(183,71); var g=GraphMath.Graph(cursor,pan,zoom); var next=GraphMath.ZoomAt(cursor,pan,zoom,1.2f); var same=GraphMath.Graph(cursor,next,1.2f);
            check(Near(g.X,same.X)&&Near(g.Y,same.Y),"graph zoom preserves cursor anchor at "+zoom);
        }
        check(GraphMath.Snap(11,8)==8&&GraphMath.Snap(12,8)==16,"graph snap nearest grid and midpoint");
        check(GraphMath.Snap(-12,8)==-16&&GraphMath.Snap(-11,8)==-8,"graph snap negative coordinates");
        check(GraphMath.Snap(12.3f,0)==12.3f,"graph snap disabled");
        var fit=GraphMath.Fit(new(-100,-50,400,200),800,600,40,.5f,1.5f);
        check(Near(fit.Zoom,1.5f)&&Near(fit.Pan.X,250)&&Near(fit.Pan.Y,225),"graph FitAll centres negative bounds and clamps maximum zoom");
        fit=GraphMath.Fit(new(0,0,2000,1000),800,600,40,.5f,1.5f);
        check(fit.Zoom==.5f,"graph FitAll respects minimum zoom for oversized scenes");
        fit=GraphMath.Fit(new(0,0,400,200),800,600,40,.1f,10);
        check(Near(fit.Zoom,1.8f)&&Near(fit.Pan.X,40)&&Near(fit.Pan.Y,120),"graph FitAll preserves requested margin and aspect");
        fit=GraphMath.Fit(RectF.Empty,1,1,40,.5f,1.5f);
        check(float.IsFinite(fit.Pan.X)&&float.IsFinite(fit.Zoom),"graph fit is finite for degenerate bounds and tiny viewport");
        check(!GraphMath.Dragged(default,new(4,0),1)&&GraphMath.Dragged(default,new(4.01f,0),1),"graph drag threshold is 4 screen px");
        check(GraphMath.Dragged(default,new(3,0),2)&&!GraphMath.Dragged(default,new(3,0),1),"graph drag threshold respects Kit scale");
        check(GraphMath.Height(0,0)==90&&GraphMath.Height(3,1)==138,"graph shared node height uses the largest port column");
        var a=new Vec(0,0);var b=new Vec(400,150);var strip=new GraphStrip(); strip.Build(a,b,2);
        check(strip.Points[0].X==0&&strip.Points[strip.Count].X==400&&strip.Points[strip.Count].Y==150,"graph bezier endpoints exact");
        var half=GraphMath.Bezier(a,b,.5f);check(Near(half.X,200)&&Near(half.Y,75),"graph symmetric bezier midpoint");
        check(GraphMath.Segments(default,new(10,0))==16&&GraphMath.Segments(default,new(10000,0))==32,"graph tessellation 16 to 32 segments");
        check(strip.Count>16&&strip.Count<=32,"graph segment count grows with wire length");
        bool finite=true;for(int i=0;i<strip.Count*4;i++)finite&=float.IsFinite(strip.Vertices[i].X)&&float.IsFinite(strip.Vertices[i].Y);
        check(finite,"graph strip has finite vertices");
        strip.Build(default,default,2);check(strip.Hit(default,6)&&float.IsFinite(strip.Vertices[0].X),"graph zero-length wire remains finite and hittable");
        strip.Build(new(400,120),new(0,0),2);check(strip.Hit(GraphMath.Bezier(new(400,120),default,.5f),6),"graph reversed wire hit test");
        strip.Build(default,new(400,0),2);check(strip.Hit(new(200,6),6)&&!strip.Hit(new(200,6.1f),6),"graph wire hit radius inclusive at 6 px");
        strip.Build(default,new(400,0),2,true);check(GraphMath.DistanceSq(strip.Vertices[4],strip.Vertices[7])==0&&GraphMath.DistanceSq(strip.Vertices[0],strip.Vertices[3])>0,"graph pending wire alternates visible and collapsed quads");
        strip.Build(default,new(400,0),2);float oldWidth=MathF.Abs(strip.Vertices[0].Y-strip.Vertices[1].Y);strip.Build(default,new(400,0),5);
        check(Near(MathF.Abs(strip.Vertices[0].Y-strip.Vertices[1].Y)-oldWidth,3),"graph strip width includes a soft fringe");
        var bounds=new GraphNodeBounds(new(0,0,176,114),2,1,true);
        check(bounds.Port(false,0).X==0&&bounds.Port(true,0).X==176&&bounds.Port(false,0).Y==48,"graph port centres match uGUI layout");
        var nodes=new List<GraphNodeBounds>{bounds};var wires=new List<GraphStrip>{strip};
        var hit=GraphPicking.Hit(nodes,wires,new(140,100),1);check(hit.Kind==GraphPickKind.FooterButton,"graph footer wins over node body");
        hit=GraphPicking.Hit(nodes,wires,new(3,48),1);check(hit.Kind==GraphPickKind.Port&&hit.Port==0&&!hit.Output,"graph input port wins over node");
        hit=GraphPicking.Hit(nodes,wires,new(179,48),1);check(hit.Kind==GraphPickKind.Port&&hit.Output,"graph output port hit extends outside body");
        check(GraphPicking.Hit(nodes,wires,new(182,48),2).Kind==GraphPickKind.Canvas,"graph port radius stays constant on screen");
        check(GraphPicking.Hit(nodes,wires,new(80,20),1).Kind==GraphPickKind.Node,"graph body hit");
        check(GraphPicking.Hit(nodes,wires,new(300,4),1).Kind==GraphPickKind.Wire,"graph wire hit after nodes");
        check(GraphPicking.Hit(nodes,wires,new(300,7),1).Kind==GraphPickKind.Canvas,"graph empty canvas hit");
        nodes.Add(bounds);check(GraphPicking.Hit(nodes,wires,new(80,20),1).Index==1,"graph overlapping nodes hit topmost");
        check(GraphPicking.Hit(nodes,wires,new(0,48),1).Kind==GraphPickKind.Port&&GraphPicking.Hit(nodes,wires,new(0,48),1).Index==1,"graph top block port wins within its own z layer");
        var changes=new GraphChanges();int number=7;
        check(!changes.Set(ref number,7,GraphDirty.Shape)&&changes.Take()==GraphDirty.None,"graph equal setter leaves clean state");
        check(changes.Set(ref number,8,GraphDirty.Colour)&&changes.Take()==GraphDirty.Colour,"graph colour change never marks geometry or layout");
        changes.Mark(GraphDirty.Layout);changes.Mark(GraphDirty.Shape);changes.Mark(GraphDirty.Shape);
        check(changes.Take()==(GraphDirty.Layout|GraphDirty.Shape)&&changes.Take()==GraphDirty.None,"graph dirty changes coalesce and flush once");
        var other=new GraphChanges();changes.Mark(GraphDirty.Colour);check(other.Take()==GraphDirty.None,"graph dirty state isolated per object");changes.Take();
        // Benchmark the exact buffers and complete hit-test used by the runtime, including a worst-case miss.
        nodes.Clear();wires.Clear();var starts=new Vec[100];var ends=new Vec[100];
        for(int i=0;i<60;i++)nodes.Add(new(new((i%10)*220,(i/10)*160,176,114),2,1,true));
        for(int i=0;i<100;i++) { starts[i]=nodes[i%60].Port(true,0);ends[i]=nodes[(i*7+1)%60].Port(false,i/60);var w=new GraphStrip();w.Build(starts[i],ends[i],2);wires.Add(w); }
        for(int i=0;i<3000;i++) { for(int j=0;j<100;j++)wires[j].Build(starts[j],ends[j],2);GraphPicking.Hit(nodes,wires,new(2190,950),1); }
        double bestGeometry=double.MaxValue,bestHit=double.MaxValue;
        var samples=new List<(double Geometry,double Hit)>();
        for(int sample=0;sample<7;sample++)
        {
            long t=Stopwatch.GetTimestamp();for(int i=0;i<1000;i++)for(int j=0;j<100;j++)wires[j].Build(starts[j],ends[j],2);
            double geometry=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency/1000;
            t=Stopwatch.GetTimestamp();int sink=0;
            for(int i=0;i<10000;i++) { var q=i%3==0?new Vec(2190,950):i%3==1?new Vec(2000,900):new Vec(1200,700); sink+=(int)GraphPicking.Hit(nodes,wires,q,1).Kind; }
            double hitMs=(Stopwatch.GetTimestamp()-t)*1000.0/Stopwatch.Frequency/10000;
            samples.Add((geometry,hitMs));bestGeometry=Math.Min(bestGeometry,geometry);bestHit=Math.Min(bestHit,hitMs);GC.KeepAlive(sink);
        }
        var sortedGeometry=samples.Select(s=>s.Geometry).Order().ToArray();var sortedHit=samples.Select(s=>s.Hit).Order().ToArray();
        Console.WriteLine($"INFO graph benchmark: 100 wire geometry median {sortedGeometry[3]:F6} ms (best {bestGeometry:F6}); full 60 node / 100 wire hit median {sortedHit[3]:F6} ms (best {bestHit:F6})");
        check(sortedGeometry[3]<.5,"graph 100-wire geometry median below 0.5 ms");check(sortedHit[3]<.1,"graph complete 60-node / 100-wire hit median below 0.1 ms");
        // No setters/native uploads run on an unchanged runtime frame. Verify the pure state/hit path and reused buffers.
        for(int i=0;i<1000;i++) { changes.Set(ref number,8,GraphDirty.Shape); changes.Take();GraphPicking.Hit(nodes,wires,new(2190,950),1); }
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++) { changes.Set(ref number,8,GraphDirty.Shape);changes.Take();GraphPicking.Hit(nodes,wires,new(2190,950),1); }
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine($"INFO graph unchanged pure state/hit path: {bytes} bytes / 1000 frames");check(bytes==0,"graph unchanged pure frame path allocates zero bytes");
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)for(int j=0;j<100;j++)wires[j].Build(starts[j],ends[j],2);
        bytes=GC.GetAllocatedBytesForCurrentThread()-before;check(bytes==0,"graph geometry rebuild reuses buffers without allocations");
    }
}
