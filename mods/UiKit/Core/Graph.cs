namespace SprocketUiKit;

[Flags]
internal enum GraphDirty { None = 0, Layout = 1, Colour = 2, Shape = 4, Order = 8 }
internal sealed class GraphChanges
{
    internal GraphDirty Value;
    internal void Mark(GraphDirty flags) => Value |= flags;
    internal GraphDirty Take() { var v = Value; Value = GraphDirty.None; return v; }
    internal bool Set<T>(ref T field, T value, GraphDirty flags)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Mark(flags); return true;
    }
}

internal static class GraphMath
{
    internal const float Head = 30, Row = 24, Foot = 30;
    internal static float Height(int inputs, int outputs) => Head + 6 + Row * Math.Max(1, Math.Max(inputs, outputs)) + Foot;
    internal static RectF Visible(Vec pan, float zoom, float width, float height) => new(-pan.X/zoom,-pan.Y/zoom,width/zoom,height/zoom);
    internal static float PortRadius(float px) => float.IsFinite(px) && px >= 0 ? px : throw new ArgumentOutOfRangeException(nameof(px));
    internal static Vec Local(Vec graph, Vec pan, float zoom) => new(graph.X * zoom + pan.X, graph.Y * zoom + pan.Y);
    internal static Vec Graph(Vec local, Vec pan, float zoom) => new((local.X - pan.X) / zoom, (local.Y - pan.Y) / zoom);
    internal static float Snap(float v, float grid) => grid > 0 ? MathF.Round(v / grid, MidpointRounding.AwayFromZero) * grid : v;
    internal static Vec ZoomAt(Vec cursor, Vec pan, float oldZoom, float newZoom)
    { var g = Graph(cursor, pan, oldZoom); return new(cursor.X - g.X * newZoom, cursor.Y - g.Y * newZoom); }
    internal static (Vec Pan,float Zoom) Fit(RectF bounds,float width,float height,float margin,float minZoom,float maxZoom)
    {
        float z=Math.Clamp(MathF.Min(MathF.Max(1,width-2*margin)/MathF.Max(1,bounds.W),MathF.Max(1,height-2*margin)/MathF.Max(1,bounds.H)),minZoom,maxZoom);
        return (new((width-bounds.W*z)*.5f-bounds.X*z,(height-bounds.H*z)*.5f-bounds.Y*z),z);
    }
    internal static bool Dragged(Vec start, Vec now, float scale) => DistanceSq(start, now) * scale * scale > 16;
    internal static float DistanceSq(Vec a, Vec b) { float x = a.X - b.X, y = a.Y - b.Y; return x * x + y * y; }
    internal static Vec Bezier(Vec a, Vec b, float t)
    {
        float arm = MathF.Max(40, MathF.Abs(b.X - a.X) * .5f), u = 1 - t;
        return new(u*u*u*a.X + 3*u*u*t*(a.X+arm) + 3*u*t*t*(b.X-arm) + t*t*t*b.X,
            u*u*u*a.Y + 3*u*u*t*a.Y + 3*u*t*t*b.Y + t*t*t*b.Y);
    }
    internal static int Segments(Vec a, Vec b) => Math.Clamp((int)(MathF.Sqrt(DistanceSq(a,b)) / 24), 16, 32);
    internal static float SegmentDistanceSq(Vec p, Vec a, Vec b)
    {
        float x=b.X-a.X, y=b.Y-a.Y, d=x*x+y*y;
        float t=d == 0 ? 0 : Math.Clamp(((p.X-a.X)*x+(p.Y-a.Y)*y)/d,0,1);
        return DistanceSq(p,new(a.X+x*t,a.Y+y*t));
    }
}

// Shared runtime/test buffers. One quad per segment; even segments form the pending dashed wire.
internal sealed class GraphStrip
{
    internal readonly Vec[] Points = new Vec[33];
    internal readonly Vec[] Vertices = new Vec[128];
    internal int Count;
    internal void Build(Vec a, Vec b, float width, bool dashed = false)
    {
        Count = GraphMath.Segments(a,b);
        for(int i=0;i<=Count;i++) Points[i]=GraphMath.Bezier(a,b,(float)i/Count);
        for(int i=0;i<Count;i++)
        {
            var p=Points[i]; var q=Points[i+1]; float x=q.X-p.X,y=q.Y-p.Y;
            float length=MathF.Sqrt(x*x+y*y); float k=(width*.5f+1)/MathF.Max(.001f,length);
            float nx=-y*k,ny=x*k;
            if(dashed && (i&1)==1) { nx=ny=0; q=p; }
            int v=i*4;
            Vertices[v]=new(p.X+nx,p.Y+ny); Vertices[v+1]=new(p.X-nx,p.Y-ny);
            Vertices[v+2]=new(q.X+nx,q.Y+ny); Vertices[v+3]=new(q.X-nx,q.Y-ny);
        }
    }
    internal bool Hit(Vec p,float radius)
    {
        // Bounding rectangle avoids walking all segments for distant curves.
        float minX=float.MaxValue,minY=minX,maxX=float.MinValue,maxY=maxX;
        for(int i=0;i<=Count;i++) { var q=Points[i]; minX=MathF.Min(minX,q.X); minY=MathF.Min(minY,q.Y); maxX=MathF.Max(maxX,q.X); maxY=MathF.Max(maxY,q.Y); }
        if(p.X<minX-radius||p.X>maxX+radius||p.Y<minY-radius||p.Y>maxY+radius) return false;
        for(int i=0;i<Count;i++) if(GraphMath.SegmentDistanceSq(p,Points[i],Points[i+1])<=radius*radius) return true;
        return false;
    }
}

internal readonly struct GraphNodeBounds
{
    internal readonly RectF Body, Footer;
    internal readonly int Inputs, Outputs;
    internal GraphNodeBounds(RectF body, int inputs, int outputs, bool button)
    { Body=body; Inputs=inputs; Outputs=outputs; Footer=button ? new(body.Right-60,body.Bottom-26,54,22) : RectF.Empty; }
    internal Vec Port(bool output,int index) => new(output ? Body.Right : Body.X,Body.Y+GraphMath.Head+6+GraphMath.Row*(index+.5f));
    internal int HitPort(Vec p,float radius, bool output)
    {
        int count=output ? Outputs : Inputs;
        for(int i=0;i<count;i++) if(GraphMath.DistanceSq(p,Port(output,i))<=radius*radius) return i;
        return -1;
    }
}

internal enum GraphPickKind { Canvas, Node, Port, Wire, FooterButton }
internal readonly record struct GraphPick(GraphPickKind Kind,int Index=-1,int Port=-1,bool Output=false);
internal static class GraphPicking
{
    internal static GraphPick Hit(IReadOnlyList<GraphNodeBounds> nodes,IReadOnlyList<GraphStrip> wires,Vec p,float scale, float portRadius = 7)
    {
        for(int i=nodes.Count-1;i>=0;i--)
        {
            var b=nodes[i];
            if(b.Footer.Contains(p))return new(GraphPickKind.FooterButton,i);
            int k=b.HitPort(p,portRadius/scale,false); if(k>=0)return new(GraphPickKind.Port,i,k);
            k=b.HitPort(p,portRadius/scale,true); if(k>=0)return new(GraphPickKind.Port,i,k,true);
            if(b.Body.Contains(p))return new(GraphPickKind.Node,i);
        }
        for(int i=wires.Count-1;i>=0;i--)if(wires[i].Hit(p,6/scale))return new(GraphPickKind.Wire,i);
        return new(GraphPickKind.Canvas);
    }
}
