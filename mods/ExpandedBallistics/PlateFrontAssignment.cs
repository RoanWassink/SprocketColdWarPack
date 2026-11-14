using System.Numerics;
namespace SprocketExpandedBallistics;

// Position-based ownership survives native UV seams, subdivisions, bevels and disconnected triangles.
internal sealed class PlateFrontAssignment
{
    internal sealed record Face(Vector3 A,Vector3 B,Vector3 C,Vector3 Normal,float Area);
    private readonly Face[] faces;
    private readonly OverlaySurface back;
    internal PlateFrontAssignment(OverlaySurface front,OverlaySurface back)
    {
        this.back=back;var list=new List<Face>();
        for(int t=0;t<front.Triangles.Length;t+=3)
        {
            var a=front.Vertices[front.Triangles[t]];var b=front.Vertices[front.Triangles[t+1]];var c=front.Vertices[front.Triangles[t+2]];
            var cross=Vector3.Cross(b-a,c-a);float length=cross.Length();if(length<1e-10f)continue;
            var n=cross/length;if(Vector3.Dot(n,front.Normals[front.Triangles[t]])<0)n=-n;
            list.Add(new(a,b,c,n,length*.5f));
        }
        faces=list.ToArray();
    }
    internal Face? Assign(params Vector3[] points)
    {
        var centre=points.Aggregate(Vector3.Zero,(a,b)=>a+b)/points.Length;
        return faces.OrderByDescending(f=>points.Count(p=>Vector3.DistanceSquared(p,Closest(p,f.A,f.B,f.C))<1e-9f))
            .ThenBy(f=>Vector3.DistanceSquared(centre,Closest(centre,f.A,f.B,f.C)))
            .ThenByDescending(f=>f.Area).FirstOrDefault();
    }
    internal float Thickness(Face face,Vector3 point)
    {
        // Closest native back triangle supplies B; its projection measures T along the owning front normal.
        var f=Closest(point,face.A,face.B,face.C);float best=float.PositiveInfinity,thickness=0;
        for(int t=0;t<back.Triangles.Length;t+=3)
        {
            var a=back.Vertices[back.Triangles[t]];var b=back.Vertices[back.Triangles[t+1]];var c=back.Vertices[back.Triangles[t+2]];
            var bpoint=Closest(f,a,b,c);float h=Vector3.Dot(bpoint-face.A,face.Normal);
            if(h>=-1e-8f)continue;
            float distance=Vector3.DistanceSquared(f,bpoint);
            if(distance<best){best=distance;thickness=h;}
        }
        return thickness;
    }
    internal static float Depth(Face face,Vector3 p,float thickness)=>Math.Clamp(Vector3.Dot(p-face.A,face.Normal)/thickness,0,1);
    internal static OverlayCopy Copy(OverlaySurface front,OverlaySurface back,OverlaySurface sides,CassettePreset? preset)
    {
        var owner=new PlateFrontAssignment(front,back);
        var vertices=front.Vertices.Concat(back.Vertices).Concat(sides.Vertices).ToList();
        var normals=front.Normals.Concat(back.Normals).Concat(sides.Normals).ToList();
        var uv=Enumerable.Repeat(new Vector2(.5f,0),front.Vertices.Length).Concat(Enumerable.Repeat(new Vector2(.5f,1),back.Vertices.Length)).ToList();
        // Retain unused native vertices as value copies, too.
        foreach(var p in sides.Vertices){var face=owner.Assign(p);var h=face==null?0:owner.Thickness(face,p);uv.Add(new(.5f,h<0?Depth(face!,p,h):0));}
        var triangles=front.Triangles.Concat(back.Triangles.Select(i=>i+front.Vertices.Length)).ToList();
        int fallback=0;double area=0,total=0;
        var boundaries=CassetteDepthStrip.Boundaries(preset);
        for(int t=0;t<sides.Triangles.Length;t+=3)
        {
            var ids=sides.Triangles.Skip(t).Take(3).ToArray();var p=ids.Select(i=>sides.Vertices[i]).ToArray();
            double triangleArea=Vector3.Cross(p[1]-p[0],p[2]-p[0]).Length()*.5;total+=triangleArea;
            var face=owner.Assign(p);var centre=(p[0]+p[1]+p[2])/3;float h=face==null?0:owner.Thickness(face,centre);
            bool unresolved=face==null||!float.IsFinite(h)||h>=-1e-8f;
            float closestLayer=0;
            if(unresolved)
            {
                fallback++;area+=triangleArea;
                // No usable thickness: choose the closest native endpoint, then the corresponding layer centre.
                float fd=front.Vertices.Length==0?float.PositiveInfinity:front.Vertices.Min(v=>Vector3.DistanceSquared(v,centre));
                float bd=back.Vertices.Length==0?float.PositiveInfinity:back.Vertices.Min(v=>Vector3.DistanceSquared(v,centre));
                closestLayer=fd<=bd?(float)((boundaries[0]+boundaries[1])*.5):(float)((boundaries[^2]+boundaries[^1])*.5);
            }
            // Independent indices per triangle keep distinct face normals/depth frames at creases.
            foreach(int i in ids){triangles.Add(vertices.Count);vertices.Add(sides.Vertices[i]);normals.Add(sides.Normals[i]);uv.Add(new(.5f,unresolved?closestLayer:Depth(face!,sides.Vertices[i],h)));}
        }
        return new(vertices.ToArray(),triangles.ToArray(),normals.ToArray(),uv.ToArray()){FallbackTriangles=fallback,FallbackArea=area,SideArea=total};
    }
    internal static double Area(OverlaySurface s)
    {double a=0;for(int t=0;t<s.Triangles.Length;t+=3)a+=Vector3.Cross(s.Vertices[s.Triangles[t+1]]-s.Vertices[s.Triangles[t]],s.Vertices[s.Triangles[t+2]]-s.Vertices[s.Triangles[t]]).Length()*.5;return a;}
    internal static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        // Closest point on a triangle, including the boundary (Ericson region tests).
        var ab=b-a;var ac=c-a;var ap=p-a;float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return a;
        var bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
        float den=va+vb+vc;if(Math.Abs(den)<1e-20f)return a;
        return a+ab*(vb/den)+ac*(vc/den);
    }
}

internal static class PlateOverlayGeometry
{
    internal static OverlayCopy Mirror(OverlayCopy source,Matrix4x4 mirror)
    {
        if(!Matrix4x4.Invert(mirror,out var inverse))throw new InvalidOperationException("Singular native mirror matrix.");
        var normalMatrix=Matrix4x4.Transpose(inverse);int offset=source.Vertices.Length;
        var indices=new List<int>(source.Triangles);
        for(int t=0;t<source.Triangles.Length;t+=3)indices.AddRange(new[]{source.Triangles[t]+offset,source.Triangles[t+2]+offset,source.Triangles[t+1]+offset});
        return source with{Vertices=source.Vertices.Concat(source.Vertices.Select(v=>Vector3.Transform(v,mirror))).ToArray(),
            Normals=source.Normals.Concat(source.Normals.Select(n=>n.LengthSquared()>1e-12f?Vector3.Normalize(Vector3.TransformNormal(n,normalMatrix)):Vector3.Zero)).ToArray(),
            Triangles=indices.ToArray(),Uv=source.Uv.Concat(source.Uv).ToArray()};
    }
    internal static bool Covers(Vector3[] overlay,int overlayTriangles,Vector3[] nativeBounds,int nativeTriangles)
    {
        if(overlay.Length==0||nativeBounds.Length==0||nativeTriangles<=0||overlayTriangles<nativeTriangles)return false;
        var min=overlay.Aggregate(Vector3.Min);var max=overlay.Aggregate(Vector3.Max);const float e=.001f;
        return nativeBounds.All(p=>p.X>=min.X-e&&p.Y>=min.Y-e&&p.Z>=min.Z-e&&p.X<=max.X+e&&p.Y<=max.Y+e&&p.Z<=max.Z+e);
    }
}
