using System.Numerics;
namespace SprocketExpandedBallistics;

// A wall is bounded by two native boundary edges and two native thickness rails.
// No nearest-face, vehicle-front or index-order correspondence is assumed.
internal sealed record OverlayWallDepth(Vector3 F0,Vector3 F1,Vector3 B0,Vector3 B1,Vector3 FrontNormal)
{
    internal const float Epsilon=2e-6f;
    internal Vector3 SideNormal=>Vector3.Normalize(Vector3.Cross(F1-F0,B0-F0));
    internal Vector3 At(float s,float d)=>Vector3.Lerp(Vector3.Lerp(F0,F1,s),Vector3.Lerp(B0,B1,s),d);
    internal float Depth(Vector3 point)
    {
        // Solve the ruled native quad P=F(s)+d(B(s)-F(s)). For parallel planes
        // h0==h1 and this reduces exactly to (P-F).n / (B-F).n.
        var edge=F1-F0;var rail=B0-F0;var change=B1-F1-rail;var p=point-F0;
        double h0=Vector3.Dot(rail,FrontNormal),h1=Vector3.Dot(B1-F1,FrontNormal);
        if(Math.Abs(h0)<1e-9||h0*h1<=0)throw new InvalidOperationException("Native wall has zero/crossed thickness.");
        if(Math.Abs(h1-h0)<1e-7*Math.Max(Math.Abs(h0),Math.Abs(h1)))
            return (float)(Vector3.Dot(p,FrontNormal)/h0);
        double s=Vector3.Dot(p,edge)/edge.LengthSquared(),d=Vector3.Dot(p,FrontNormal)/h0;
        for(int i=0;i<24;i++)
        {
            var error=edge*(float)s+rail*(float)d+change*(float)(s*d)-p;
            var ds=edge+change*(float)d;var dd=rail+change*(float)s;
            double a=Vector3.Dot(ds,ds),b=Vector3.Dot(ds,dd),c=Vector3.Dot(dd,dd),det=a*c-b*b;
            if(det<1e-18)throw new InvalidOperationException("Degenerate native wall coordinates.");
            double x=Vector3.Dot(ds,error),y=Vector3.Dot(dd,error);
            double stepS=(c*x-b*y)/det,stepD=(a*y-b*x)/det;s-=stepS;d-=stepD;
            if(Math.Abs(stepS)+Math.Abs(stepD)<1e-6)return (float)d;
        }
        throw new InvalidOperationException("Native wall coordinates did not converge.");
    }
    internal bool Contains(Vector3 p)=>OnTriangle(p,F0,F1,B1)||OnTriangle(p,F0,B1,B0);
    internal static bool OnTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        var x=b-a;var y=c-a;var n=Vector3.Cross(x,y);float length=n.Length();
        if(length<1e-10f||Math.Abs(Vector3.Dot(p-a,n))/length>Epsilon)return false;
        static double Dot(Vector3 u,Vector3 v)=>(double)u.X*v.X+(double)u.Y*v.Y+(double)u.Z*v.Z;
        double xx=Dot(x,x),xy=Dot(x,y),yy=Dot(y,y),den=xx*yy-xy*xy;
        if(den<1e-18)return false;
        double px=Dot(p-a,x),py=Dot(p-a,y),u=(px*yy-py*xy)/den,v=(py*xx-px*xy)/den;
        return u>=-Epsilon&&v>=-Epsilon&&u+v<=1+Epsilon;
    }
    private sealed record Edge(Vector3 A,Vector3 B,Vector3 Normal);
    private static List<Edge> Boundary(OverlaySurface surface)
    {
        var edges=new Dictionary<(Vector3,Vector3),Edge>();var counts=new Dictionary<(Vector3,Vector3),int>();
        static (Vector3,Vector3) Key(Vector3 a,Vector3 b)=>Compare(a,b)<0?(a,b):(b,a);
        for(int t=0;t<surface.Triangles.Length;t+=3)
        {
            var ids=surface.Triangles;var v=surface.Vertices;
            var n=Vector3.Cross(v[ids[t+1]]-v[ids[t]],v[ids[t+2]]-v[ids[t]]);
            if(n.LengthSquared()<1e-18f)continue;n=Vector3.Normalize(n);
            if(Vector3.Dot(n,surface.Normals[ids[t]])<0)n=-n;
            for(int e=0;e<3;e++)
            {
                var a=v[ids[t+e]];var b=v[ids[t+(e+1)%3]];var k=Key(a,b);
                counts.TryGetValue(k,out int count);counts[k]=count+1;edges[k]=new(a,b,n);
            }
        }
        return edges.Where(e=>counts[e.Key]==1).Select(e=>e.Value).ToList();
    }
    private static int Compare(Vector3 a,Vector3 b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y!=b.Y?a.Y.CompareTo(b.Y):a.Z.CompareTo(b.Z);
    internal static List<OverlayWallDepth> Derive(OverlaySurface front,OverlaySurface back,OverlaySurface sides)
    {
        var result=new List<OverlayWallDepth>();var rear=Boundary(back);
        var sideEdges=new List<(Vector3 A,Vector3 B)>();
        for(int t=0;t<sides.Triangles.Length;t+=3)for(int e=0;e<3;e++)
            sideEdges.Add((sides.Vertices[sides.Triangles[t+e]],sides.Vertices[sides.Triangles[t+(e+1)%3]]));
        bool Rail(Vector3 a,Vector3 b)
        {
            var delta=b-a;float len=delta.LengthSquared();if(len<1e-16f)return false;
            var intervals=new List<(float Lo,float Hi)>();
            foreach(var edge in sideEdges)
            {
                float u=Vector3.Dot(edge.A-a,delta)/len,v=Vector3.Dot(edge.B-a,delta)/len;
                if(Vector3.DistanceSquared(edge.A,a+u*delta)>Epsilon*Epsilon||Vector3.DistanceSquared(edge.B,a+v*delta)>Epsilon*Epsilon)continue;
                intervals.Add((Math.Min(u,v),Math.Max(u,v)));
            }
            float covered=0;foreach(var interval in intervals.OrderBy(i=>i.Lo))
            {if(interval.Hi<covered)continue;if(interval.Lo>covered+Epsilon)return false;covered=interval.Hi;if(covered>=1-Epsilon)return true;}
            return false;
        }
        foreach(var f in Boundary(front))foreach(var b in rear)foreach(bool flip in new[]{false,true})
        {
            var b0=flip?b.B:b.A;var b1=flip?b.A:b.B;
            if(!Rail(f.A,b0)||!Rail(f.B,b1))continue;
            var cross=Vector3.Cross(f.B-f.A,b0-f.A);if(cross.LengthSquared()<1e-16f)continue;
            var sn=Vector3.Normalize(cross);
            // Both rails must lead behind this very front plane; crossing quads
            // or another front wall's back edge cannot establish correspondence.
            double h0=Vector3.Dot(b0-f.A,f.Normal),h1=Vector3.Dot(b1-f.B,f.Normal);
            if(h0>=-1e-8||h1>=-1e-8||Vector3.Dot(Vector3.Cross(b1-f.B,b0-f.B),sn)<=0)continue;
            var wall=new OverlayWallDepth(f.A,f.B,b0,b1,f.Normal);
            // Check native coverage, rather than accepting a rail diagonal through empty space.
            bool covered=false;var middle=(f.A+f.B+b1)/3;
            for(int t=0;t<sides.Triangles.Length&&!covered;t+=3)
                covered=OnTriangle(middle,sides.Vertices[sides.Triangles[t]],sides.Vertices[sides.Triangles[t+1]],sides.Vertices[sides.Triangles[t+2]]);
            if(covered)result.Add(wall);
        }
        return result;
    }
}
