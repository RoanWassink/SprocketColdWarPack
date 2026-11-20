using System.Text.Json;
using NVector=System.Numerics.Vector3;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.97: 3x4 affine transform matching Unity's Matrix4x4 column-vector layout (MultiplyPoint3x4).
/// Pure value type so geometry and mirror tests run without Unity or IL2CPP.
/// </summary>
internal readonly record struct SolidAffine(
    float M00,float M01,float M02,float M03,
    float M10,float M11,float M12,float M13,
    float M20,float M21,float M22,float M23)
{
    internal NVector TransformPoint(NVector p)=>new(
        M00*p.X+M01*p.Y+M02*p.Z+M03,
        M10*p.X+M11*p.Y+M12*p.Z+M13,
        M20*p.X+M21*p.Y+M22*p.Z+M23);

    internal double LinearDeterminant=>
        (double)M00*((double)M11*M22-(double)M12*M21)
        -(double)M01*((double)M10*M22-(double)M12*M20)
        +(double)M02*((double)M10*M21-(double)M11*M20);
}

internal sealed record SolidColliderMesh(NVector[] Vertices,int[] Triangles,int Pieces,double VolumeM3,int ReversedFaces);
internal sealed record SolidColliderResult(SolidColliderMesh? Mesh,string? Refusal);

/// <summary>
/// 0.22.97: builds a watertight, outward-wound triangle mesh from a closed PlateStructureMesh
/// for submission via the 6-argument IVehicleComponentDamageModelBuilder.AddMesh.
/// Positive signed volume aligns (B-A)x(C-A) outward, matching MeshStructure.WriteIntersectionsToBuffer
/// so native damage blocks pair Enter and Exit cleanly through the entire closed volume.
/// </summary>
internal static class SolidColliderGeometry
{
    private readonly record struct Vec3d(double X,double Y,double Z)
    {
        public static Vec3d operator +(Vec3d a,Vec3d b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vec3d operator -(Vec3d a,Vec3d b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static double Dot(Vec3d a,Vec3d b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static Vec3d Cross(Vec3d a,Vec3d b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public double LengthSq=>X*X+Y*Y+Z*Z;
        public Vec3d Normalized(){double l=Math.Sqrt(LengthSq);return l>1e-15?new(X/l,Y/l,Z/l):default;}
    }

    private static SolidColliderResult Refused(string why)=>new(null,why);

    internal static SolidColliderResult FromSnapshot(PlateMeshSnapshot? snapshot)
    {
        if(snapshot==null)return Refused("The plate mesh could not be read.");
        for(int f=0;f<snapshot.Faces.Length;f++)
            if(snapshot.Faces[f].HoleLoops>0)return Refused(SolidShape.NotClosed+$" Face {f+1} has a hole.");
        return Build(snapshot.Vertices,snapshot.Faces.Select(f=>f.VertexIds).ToArray());
    }

    internal static SolidColliderResult FromJson(string? json)
    {
        if(string.IsNullOrWhiteSpace(json))return Refused("The plate mesh JSON is empty.");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var flat=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var points=new NVector[flat.Length/3];
        for(int i=0;i<points.Length;i++)points[i]=new(flat[3*i],flat[3*i+1],flat[3*i+2]);
        var faces=root.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        return Build(points,faces);
    }

    internal static SolidColliderResult Build(IReadOnlyList<NVector>? vertices,IReadOnlyList<int[]>? faces)
    {
        if(vertices==null||faces==null||faces.Count==0)return Refused("This plate has no faces.");
        int n=faces.Count;
        for(int f=0;f<n;f++)
        {
            var v=faces[f];
            if(v==null||v.Length<3||v.Distinct().Count()!=v.Length||v.Any(i=>i<0||i>=vertices.Count)
                ||v.Any(i=>!float.IsFinite(vertices[i].X)||!float.IsFinite(vertices[i].Y)||!float.IsFinite(vertices[i].Z)))
                return Refused(SolidShape.NotClosed+$" Face {f+1} is not a proper polygon.");
        }

        var users=new Dictionary<(int,int),List<(int Face,bool Forward)>>();
        for(int f=0;f<n;f++)
        {
            var face=faces[f];
            for(int j=0;j<face.Length;j++)
            {
                int a=face[j],b=face[(j+1)%face.Length];
                var key=(Math.Min(a,b),Math.Max(a,b));
                if(!users.TryGetValue(key,out var list))users[key]=list=new();
                list.Add((f,a<b));
            }
        }

        var parent=Enumerable.Range(0,n).ToArray();
        int Find(int i){while(parent[i]!=i)i=parent[i]=parent[parent[i]];return i;}
        foreach(var list in users.Values)
            for(int k=1;k<list.Count;k++)parent[Find(list[k].Face)]=Find(list[0].Face);

        var pieceOf=new int[n];var ids=new Dictionary<int,int>();
        for(int f=0;f<n;f++){var r=Find(f);if(!ids.TryGetValue(r,out var id))ids[r]=id=ids.Count;pieceOf[f]=id;}
        int pieces=ids.Count;

        int openCount=users.Values.Count(l=>l.Count==1);
        if(openCount>0)return Refused(SolidShape.NotClosed+$" {openCount} open edge{(openCount==1?"":"s")}.");
        int sharedCount=users.Values.Count(l=>l.Count>2);
        if(sharedCount>0)return Refused(SolidShape.NotClosed+$" {sharedCount} non-manifold edge{(sharedCount==1?"":"s")}.");

        var flip=new bool?[n];
        var queue=new Queue<int>();
        for(int start=0;start<n;start++)
        {
            if(flip[start]!=null)continue;
            flip[start]=false;queue.Enqueue(start);
            while(queue.Count>0)
            {
                int f=queue.Dequeue();
                var face=faces[f];
                for(int j=0;j<face.Length;j++)
                {
                    int a=face[j],b=face[(j+1)%face.Length];
                    var list=users[(Math.Min(a,b),Math.Max(a,b))];
                    var mine=list[0].Face==f?list[0]:list[1];
                    var other=list[0].Face==f?list[1]:list[0];
                    bool want=(mine.Forward^flip[f]!.Value)==other.Forward;
                    if(flip[other.Face]==null){flip[other.Face]=want;queue.Enqueue(other.Face);}
                    else if(flip[other.Face]!=want)
                        return Refused(SolidShape.NotClosed+$" Its faces cannot all face outward (around faces {f+1} and {other.Face+1}).");
                }
            }
        }

        var tris=new List<int>(n*6);
        var triPieces=new List<int>(n*2);
        for(int f=0;f<n;f++)
        {
            var raw=faces[f];int k=raw.Length;
            var ring=new int[k];
            bool rev=flip[f]==true;
            for(int j=0;j<k;j++)ring[j]=rev?raw[(k-j)%k]:raw[j];
            int p=pieceOf[f];
            TriangulateFace(vertices,ring,p,tris,triPieces);
        }

        var triArray=tris.ToArray();
        var volumes=new double[pieces];
        for(int t=0,ti=0;t+2<triArray.Length;t+=3,ti++)
            volumes[triPieces[ti]]+=SignedTriVolume(vertices[triArray[t]],vertices[triArray[t+1]],vertices[triArray[t+2]]);

        var flipPiece=new bool[pieces];
        for(int p=0;p<pieces;p++)
        {
            if(volumes[p]<0){flipPiece[p]=true;volumes[p]=-volumes[p];}
            if(!(volumes[p]>1e-9))return Refused(SolidShape.NotClosed+$" Piece {p+1} encloses no volume.");
        }

        for(int t=0,ti=0;t+2<triArray.Length;t+=3,ti++)
            if(flipPiece[triPieces[ti]])(triArray[t+1],triArray[t+2])=(triArray[t+2],triArray[t+1]);

        int reversedFaces=0;
        for(int f=0;f<n;f++)
        {
            bool turned=(flip[f]==true)^flipPiece[pieceOf[f]];
            if(turned)reversedFaces++;
        }

        var vertsArray=new NVector[vertices.Count];
        for(int i=0;i<vertsArray.Length;i++)vertsArray[i]=vertices[i];
        return new SolidColliderResult(new SolidColliderMesh(vertsArray,triArray,pieces,volumes.Sum(),reversedFaces),null);
    }

    internal static SolidColliderMesh AppendMirrored(SolidColliderMesh mesh,SolidAffine mirror)
    {
        int vc=mesh.Vertices.Length,tc=mesh.Triangles.Length;
        var outVerts=new NVector[vc*2];
        var outTris=new int[tc*2];
        Array.Copy(mesh.Vertices,0,outVerts,0,vc);
        Array.Copy(mesh.Triangles,0,outTris,0,tc);
        for(int i=0;i<vc;i++)outVerts[vc+i]=mirror.TransformPoint(mesh.Vertices[i]);

        bool flipWinding=mirror.LinearDeterminant<0;
        for(int t=0;t+2<tc;t+=3)
        {
            int a=mesh.Triangles[t]+vc,b=mesh.Triangles[t+1]+vc,c=mesh.Triangles[t+2]+vc;
            outTris[tc+t]=a;
            if(flipWinding){outTris[tc+t+1]=c;outTris[tc+t+2]=b;}
            else{outTris[tc+t+1]=b;outTris[tc+t+2]=c;}
        }
        return new SolidColliderMesh(outVerts,outTris,mesh.Pieces*2,mesh.VolumeM3*2,mesh.ReversedFaces);
    }

    internal static bool IsWatertightOutward(SolidColliderMesh? mesh,out string reason)
    {
        reason="";
        if(mesh==null){reason="mesh is null";return false;}
        if(mesh.Triangles.Length==0||mesh.Triangles.Length%3!=0){reason="invalid triangle array length";return false;}
        int vc=mesh.Vertices.Length;
        var halfEdges=new HashSet<(int,int)>();
        double volume=0;
        for(int t=0;t+2<mesh.Triangles.Length;t+=3)
        {
            int a=mesh.Triangles[t],b=mesh.Triangles[t+1],c=mesh.Triangles[t+2];
            if(a<0||a>=vc||b<0||b>=vc||c<0||c>=vc||a==b||b==c||c==a){reason=$"invalid triangle at {t}";return false;}
            if(!halfEdges.Add((a,b))||!halfEdges.Add((b,c))||!halfEdges.Add((c,a)))
            {
                reason=$"duplicate directed half-edge at triangle {t/3}";
                return false;
            }
            volume+=SignedTriVolume(mesh.Vertices[a],mesh.Vertices[b],mesh.Vertices[c]);
        }
        foreach(var (u,v) in halfEdges)
            if(!halfEdges.Contains((v,u))){reason=$"open or oppositely wound half-edge ({u},{v})";return false;}
        if(!(volume>1e-9)){reason=$"non-positive signed volume {volume}";return false;}
        return true;
    }

    private static void TriangulateFace(IReadOnlyList<NVector> vertices,int[] ring,int piece,List<int> tris,List<int> triPieces)
    {
        int k=ring.Length;
        if(k==3)
        {
            tris.Add(ring[0]);tris.Add(ring[1]);tris.Add(ring[2]);triPieces.Add(piece);
            return;
        }
        if(k==4)
        {
            var p0=D(vertices[ring[0]]);var p1=D(vertices[ring[1]]);var p2=D(vertices[ring[2]]);var p3=D(vertices[ring[3]]);
            var normal=Newell(new[]{p0,p1,p2,p3});
            bool d02=Vec3d.Dot(Vec3d.Cross(p1-p0,p2-p0),normal)>0&&Vec3d.Dot(Vec3d.Cross(p2-p0,p3-p0),normal)>0;
            bool d13=Vec3d.Dot(Vec3d.Cross(p2-p1,p3-p1),normal)>0&&Vec3d.Dot(Vec3d.Cross(p3-p1,p0-p1),normal)>0;
            bool use02=d02!=d13?d02:(p2-p0).LengthSq<=(p3-p1).LengthSq;
            if(use02)
            {
                tris.Add(ring[0]);tris.Add(ring[1]);tris.Add(ring[2]);triPieces.Add(piece);
                tris.Add(ring[0]);tris.Add(ring[2]);tris.Add(ring[3]);triPieces.Add(piece);
            }
            else
            {
                tris.Add(ring[1]);tris.Add(ring[2]);tris.Add(ring[3]);triPieces.Add(piece);
                tris.Add(ring[1]);tris.Add(ring[3]);tris.Add(ring[0]);triPieces.Add(piece);
            }
            return;
        }

        // k > 4: project to Newell normal plane and clip convex empty ears; fall back cleanly to a remaining fan if degenerate.
        var pts=new Vec3d[k];
        for(int i=0;i<k;i++)pts[i]=D(vertices[ring[i]]);
        var nUnit=Newell(pts).Normalized();
        if(nUnit.LengthSq<0.5)
        {
            EmitFan(ring,piece,tris,triPieces);
            return;
        }
        var refAxis=Math.Abs(nUnit.X)<0.6?new Vec3d(1,0,0):new Vec3d(0,1,0);
        var u=Vec3d.Cross(refAxis,nUnit).Normalized();
        var v=Vec3d.Cross(nUnit,u);
        var x=new double[k];var y=new double[k];
        for(int i=0;i<k;i++){x[i]=Vec3d.Dot(pts[i],u);y[i]=Vec3d.Dot(pts[i],v);}
        double area2=0;
        for(int i=0;i<k;i++){int nxt=(i+1)%k;area2+=x[i]*y[nxt]-x[nxt]*y[i];}
        double orient=area2>=0?1.0:-1.0;

        var rem=new List<int>(k);
        for(int i=0;i<k;i++)rem.Add(i);

        static double Cross2d(double ax,double ay,double bx,double by,double cx,double cy)=>(bx-ax)*(cy-ay)-(by-ay)*(cx-ax);
        static bool InsideOrOn(double px,double py,double ax,double ay,double bx,double by,double cx,double cy,double sign)
        {
            const double eps=-1e-12;
            return Cross2d(ax,ay,bx,by,px,py)*sign>=eps
                &&Cross2d(bx,by,cx,cy,px,py)*sign>=eps
                &&Cross2d(cx,cy,ax,ay,px,py)*sign>=eps;
        }

        while(rem.Count>3)
        {
            int m=rem.Count,ear=-1;
            for(int pass=0;pass<2&&ear<0;pass++)
            {
                double minCross=pass==0?1e-12:-1e-12;
                for(int idx=0;idx<m;idx++)
                {
                    int ip=rem[(idx+m-1)%m],ic=rem[idx],inext=rem[(idx+1)%m];
                    if(Cross2d(x[ip],y[ip],x[ic],y[ic],x[inext],y[inext])*orient<=minCross)continue;
                    bool empty=true;
                    for(int j=0;j<m;j++)
                    {
                        if(j==idx||j==(idx+m-1)%m||j==(idx+1)%m)continue;
                        int iv=rem[j];
                        if(InsideOrOn(x[iv],y[iv],x[ip],y[ip],x[ic],y[ic],x[inext],y[inext],orient)){empty=false;break;}
                    }
                    if(empty){ear=idx;break;}
                }
            }
            if(ear<0)break;
            int a=rem[(ear+m-1)%m],b=rem[ear],c=rem[(ear+1)%m];
            tris.Add(ring[a]);tris.Add(ring[b]);tris.Add(ring[c]);triPieces.Add(piece);
            rem.RemoveAt(ear);
        }

        if(rem.Count==3)
        {
            tris.Add(ring[rem[0]]);tris.Add(ring[rem[1]]);tris.Add(ring[rem[2]]);triPieces.Add(piece);
        }
        else if(rem.Count>3)
        {
            for(int j=1;j+1<rem.Count;j++)
            {
                tris.Add(ring[rem[0]]);tris.Add(ring[rem[j]]);tris.Add(ring[rem[j+1]]);triPieces.Add(piece);
            }
        }
    }

    private static void EmitFan(int[] ring,int piece,List<int> tris,List<int> triPieces)
    {
        for(int j=1;j+1<ring.Length;j++)
        {
            tris.Add(ring[0]);tris.Add(ring[j]);tris.Add(ring[j+1]);triPieces.Add(piece);
        }
    }

    private static Vec3d D(NVector p)=>new(p.X,p.Y,p.Z);

    private static Vec3d Newell(IReadOnlyList<Vec3d> pts)
    {
        double nx=0,ny=0,nz=0;
        for(int i=0;i<pts.Count;i++)
        {
            var a=pts[i];var b=pts[(i+1)%pts.Count];
            nx+=(a.Y-b.Y)*(a.Z+b.Z);
            ny+=(a.Z-b.Z)*(a.X+b.X);
            nz+=(a.X-b.X)*(a.Y+b.Y);
        }
        return new(nx,ny,nz);
    }

    private static double SignedTriVolume(NVector a,NVector b,NVector c)=>
        ((double)a.X*((double)b.Y*c.Z-(double)b.Z*c.Y)
        -(double)a.Y*((double)b.X*c.Z-(double)b.Z*c.X)
        +(double)a.Z*((double)b.X*c.Y-(double)b.Y*c.X))/6.0;
}
