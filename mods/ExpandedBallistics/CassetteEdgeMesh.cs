using System.Numerics;
namespace SprocketExpandedBallistics;

// 0.22.16.1 fork: CornerThicknessMm lets the layer overlay follow a back-plane taper instead of one
// average depth per face. Older callers pass only ThicknessMm and keep the uniform strip.
internal sealed record CassetteVisualFace(int[] Vertices,double ThicknessMm,double[]? CornerThicknessMm=null);
internal sealed record CassetteVisualMesh(Vector3[] Vertices,Vector2[] Uv,int[] Triangles);
internal sealed record CassetteEdgeResult(CassetteVisualMesh Edges,CassetteVisualMesh Front,CassetteVisualMesh Arrows,int BoundaryEdges,bool TintOnly,string? Warning);

internal static class CassetteEdgeMesh
{
    private sealed class Builder
    {
        internal readonly List<Vector3> V=new();internal readonly List<Vector2> U=new();internal readonly List<int> T=new();
        internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float repeat=1)
        {
            int s=V.Count;V.AddRange(new[]{a,b,c,d});U.AddRange(new[]{new Vector2(0,0),new(0,repeat),new(1,repeat),new(1,0)});
            T.AddRange(new[]{s,s+1,s+2,s,s+2,s+3});
        }
        internal CassetteVisualMesh Done()=>new(V.ToArray(),U.ToArray(),T.ToArray());
    }
    private static int[] Triangulate(IReadOnlyList<Vector3> vertices,int[] polygon,Vector3 normal)
    {
        var remaining=Enumerable.Range(0,polygon.Length).ToList();var result=new List<int>();
        bool Contains(Vector3 point,Vector3 a,Vector3 b,Vector3 c)=>
            Vector3.Dot(Vector3.Cross(b-a,point-a),normal)>=-1e-8f&&
            Vector3.Dot(Vector3.Cross(c-b,point-b),normal)>=-1e-8f&&
            Vector3.Dot(Vector3.Cross(a-c,point-c),normal)>=-1e-8f;
        while(remaining.Count>3)
        {
            bool found=false;
            for(int i=0;i<remaining.Count;i++)
            {
                int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                var pa=vertices[polygon[a]];var pb=vertices[polygon[b]];var pc=vertices[polygon[c]];
                if(Vector3.Dot(Vector3.Cross(pb-pa,pc-pa),normal)<=1e-10f)continue;
                if(remaining.Any(p=>p!=a&&p!=b&&p!=c&&Contains(vertices[polygon[p]],pa,pb,pc)))continue;
                result.AddRange(new[]{a,b,c});remaining.RemoveAt(i);found=true;break;
            }
            if(!found)return Array.Empty<int>();
        }
        result.AddRange(remaining);return result.ToArray();
    }
    internal static CassetteEdgeResult Build(IReadOnlyList<Vector3> vertices,IReadOnlyList<CassetteVisualFace> faces,bool opaqueFrame=false)
    {
        var edges=new Builder();var front=new Builder();var arrows=new Builder();
        var counts=new Dictionary<(int,int),int>();var normals=new Vector3[faces.Count];var triangles=new int[faces.Count][];string? warning=null;
        for(int f=0;f<faces.Count;f++)
        {
            var face=faces[f];
            if(face.Vertices.Length<3||face.Vertices.Any(i=>i<0||i>=vertices.Count)||face.Vertices.Any(i=>!float.IsFinite(vertices[i].LengthSquared())))
            {warning="Unreadable edit mesh; layers unavailable.";continue;}
            var p=face.Vertices;Vector3 area=default;
            for(int j=1;j<p.Length-1;j++)area+=Vector3.Cross(vertices[p[j]]-vertices[p[0]],vertices[p[j+1]]-vertices[p[0]]);
            if(area.LengthSquared()<1e-12f){warning="Degenerate face; layers unavailable.";continue;}
            normals[f]=Vector3.Normalize(area);
            triangles[f]=Triangulate(vertices,p,normals[f]);
            if(triangles[f].Length==0)warning="Face triangulation unavailable; front frame only.";
            if(!double.IsFinite(face.ThicknessMm)||face.ThicknessMm<=0)warning="Invalid face thickness; tint only.";
            for(int j=0;j<p.Length;j++)
            {int a=p[j],b=p[(j+1)%p.Length];var key=a<b?(a,b):(b,a);counts[key]=counts.GetValueOrDefault(key)+1;}
        }
        int boundary=counts.Count(e=>e.Value==1);
        if(counts.Any(e=>e.Value>2))warning="Non-manifold mesh; tint only.";
        if(boundary>512)warning="More than 512 open edges; tint only.";
        for(int f=0;f<faces.Count;f++)
        {
            var n=normals[f];if(n.LengthSquared()<.5f)continue;
            var face=faces[f];var p=face.Vertices;Vector3 centre=default;double area=0;
            var tessellation=triangles[f];var centres=new List<Vector3>();
            for(int j=0;j+2<tessellation.Length;j+=3)
            {
                Vector3 a=vertices[p[tessellation[j]]],b=vertices[p[tessellation[j+1]]],c=vertices[p[tessellation[j+2]]];
                double tri=Vector3.Cross(b-a,c-a).Length()*.5;area+=tri;centre+=(a+b+c)*(float)(tri/3);
                centres.Add((a+b+c)/3);
            }
            if(area>0)centre/=(float)area;
            // Transparent copy, or the approved opaque 1 cm frame fallback.
            if(!opaqueFrame&&tessellation.Length>0)
            {
                int start=front.V.Count;
                foreach(int i in p){front.V.Add(vertices[i]+n*.0003f);front.U.Add(default);}
                foreach(int corner in tessellation)front.T.Add(start+corner);
            }
            double CornerDepth(int at)=>face.CornerThicknessMm is {} corners&&corners.Length==p.Length&&double.IsFinite(corners[at])?corners[at]:face.ThicknessMm;
            for(int j=0;j<p.Length;j++)
            {
                int ai=p[j],bi=p[(j+1)%p.Length];var a=vertices[ai];var b=vertices[bi];
                if((b-a).LengthSquared()<1e-12f)continue;
                var outwards=Vector3.Normalize(Vector3.Cross(b-a,n));
                if(opaqueFrame||tessellation.Length==0)front.Quad(a+n*.0003f,b+n*.0003f,b-outwards*.01f+n*.0003f,a-outwards*.01f+n*.0003f);
                if(warning!=null||counts[ai<bi?(ai,bi):(bi,ai)]!=1)continue;
                var offset=outwards*.0004f;
                var depthA=n*(float)(CornerDepth(j)*.001);
                var depthB=n*(float)(CornerDepth((j+1)%p.Length)*.001);
                edges.Quad(a+offset,b+offset,b-depthB+offset,a-depthA+offset,(b-a).Length()/.05f);
            }
            if(warning!=null||area<.02)continue;
            var u=Vector3.Normalize(vertices[p[1]]-vertices[p[0]]);var v=Vector3.Cross(n,u);
            float shortSide=Math.Min(p.Max(i=>Vector3.Dot(vertices[i],u))-p.Min(i=>Vector3.Dot(vertices[i],u)),p.Max(i=>Vector3.Dot(vertices[i],v))-p.Min(i=>Vector3.Dot(vertices[i],v)));
            float size=Math.Clamp(shortSide*.06f,.03f,.1f),radius=size*.5f,width=size*.06f;
            int count=Math.Max(1,(int)Math.Floor(area/.5));count=Math.Min(count,64);
            for(int k=0;k<count;k++)
            {
                // Distribute marks along the long in-face direction.
                var candidate=centre+u*((k-(count-1)*.5f)*size*1.5f);
                bool inside=false;
                for(int j=0;j<tessellation.Length;j+=3)
                {
                    var a=vertices[p[tessellation[j]]];var b=vertices[p[tessellation[j+1]]];var d=vertices[p[tessellation[j+2]]];
                    if(Vector3.Dot(Vector3.Cross(b-a,candidate-a),n)>=-1e-8f&&Vector3.Dot(Vector3.Cross(d-b,candidate-b),n)>=-1e-8f&&Vector3.Dot(Vector3.Cross(a-d,candidate-d),n)>=-1e-8f){inside=true;break;}
                }
                var c=(inside?candidate:centres[k%centres.Count])+n*.0006f;
                for(int j=0;j<24;j++)
                {
                    float a=j*MathF.Tau/24,b=(j+1)*MathF.Tau/24;
                    var x=u*MathF.Cos(a)+v*MathF.Sin(a);var y=u*MathF.Cos(b)+v*MathF.Sin(b);
                    arrows.Quad(c+x*radius,c+y*radius,c+y*(radius-width),c+x*(radius-width));
                }
                arrows.Quad(c-u*radius-v*width,c+u*radius-v*width,c+u*radius+v*width,c-u*radius+v*width);
                arrows.Quad(c-v*radius-u*width,c+v*radius-u*width,c+v*radius+u*width,c-v*radius+u*width);
            }
        }
        return new(edges.Done(),front.Done(),arrows.Done(),boundary,warning!=null,warning);
    }
}

internal static class CassetteStripRaster
{
    internal const int Width=256,Height=32;
    // One colour set for the plate stripes and the composer cross-section.
    internal static uint Colour(string material)=>CassettePalette.Colour(material);
    internal static uint[] Build(CassettePreset? preset)
    {
        var layers=preset?.Layers??new[]{new CassetteLayer("rha",1,0,"up")};
        var depths=layers.Select(l=>l.Material=="air"?l.ThicknessMm:l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180)).ToArray();
        double total=depths.Sum();var pixels=new uint[Width*Height];
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
        {
            double at=(x+.5)/Width*total,low=0;int index=0;
            while(index<layers.Length-1&&at>=low+depths[index])low+=depths[index++];
            var l=layers[index];uint colour=Colour(l.Material);
            if(l.Material=="air"&&(x+y)%8<2)colour=CassettePalette.AirHatch;
            if(l.TiltDegrees>0&&(int)(x+y*Math.Tan(l.TiltDegrees*Math.PI/180))%16<1)colour=CassettePalette.AirHatch;
            if(Math.Abs(at-low)<total/Width)colour=CassettePalette.Separator;
            if(x<Width*.02)colour=preset==null?CassettePalette.Missing:CassettePalette.Accent;
            // Repeated amber chevron points inward (+u).
            if(x>=6&&x<=13&&Math.Abs(y-Height/2)==13-x)colour=CassettePalette.Accent;
            pixels[y*Width+x]=colour;
        }
        return pixels;
    }
}
