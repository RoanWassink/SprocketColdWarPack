using System.Numerics;
namespace SprocketExpandedBallistics;

internal sealed record MaterialLook(string Id,string Guid,string Asset,Vector4 Rect,Vector2 Tile,float Smoothness)
{
    // Rect: min U,V / size U,V. Pixel rectangles are top-down; Unity V is bottom-up.
    internal static readonly MaterialLook[] All={
        new("ebWood","952c18e622dfe1a4aa366948d5d17e07","TrimVaneMaterial",Pixels(4,48,204,208,256),new(.36f,.40f),.22f),
        new("ebRubber","45d3ffb6d5462f54cb6415e3738bcf75","ShermanTrackBeltSegmentMaterial",Pixels(12,80,228,308,512),new(.24f,.26f),.12f),
        new("ebFabric","499923018d52fc549b162ffe410f941f","sandbags",Pixels(240,28,350,185,512),new(.12f,.17f),.06f),
        new("ebGlass","","",new(0,0,1,1),new(1,1),.96f)
    };
    private static Vector4 Pixels(float x,float y,float right,float bottom,float size)=>new(x/size,1-bottom/size,(right-x)/size,(bottom-y)/size);
    internal static MaterialLook? For(string? id)=>Array.Find(All,l=>l.Id==id);
    internal bool Glass=>Id=="ebGlass";
    internal static bool TransparentGlass(bool shaderSupported,bool properties,bool validated,bool surface,bool queue)=>shaderSupported&&properties&&validated&&surface&&queue;
}

// Shared by the runtime and headless lifecycle/cost tests. Hiding keeps the cache;
// replacing/releasing a plate disposes exactly its owned view.
internal sealed class MaterialLookCache<T> where T:class
{
    private readonly Dictionary<long,(string Signature,T View)> entries=new();
    private readonly Action<T> dispose;
    internal MaterialLookCache(Action<T> dispose){this.dispose=dispose;}
    internal int Rebuilds {get;private set;}
    internal IEnumerable<KeyValuePair<long,(string Signature,T View)>> Entries=>entries;
    internal bool TryReuse(long owner,string signature,out T? view)
    {if(entries.TryGetValue(owner,out var e)&&e.Signature==signature){view=e.View;return true;}view=null;return false;}
    internal bool TryGet(long owner,out T? view)
    {if(entries.TryGetValue(owner,out var e)){view=e.View;return true;}view=null;return false;}
    internal void Put(long owner,string signature,T view){Remove(owner);entries.Add(owner,(signature,view));Rebuilds++;}
    internal void Remove(long owner){if(entries.Remove(owner,out var e))dispose(e.View);}
    internal void Clear(){foreach(var e in entries.Values)dispose(e.View);entries.Clear();}
}

internal static class MaterialLookGeometry
{
    private readonly record struct Point(Vector3 P,Vector3 N,Vector2 Projected);
    internal const int MaxVertices=300000;
    internal static OverlayCopy Copy(MaterialLook look,Vector3 scale,params OverlaySurface[] surfaces)
        =>CopyCore(look,scale,false,surfaces);
    internal static OverlayCopy CopyRepeat(MaterialLook look,Vector3 scale,float tileSizeM,params OverlaySurface[] surfaces)
        =>CopyCore(look with {Tile=new(tileSizeM,tileSizeM)},scale,true,surfaces);
    private static OverlayCopy CopyCore(MaterialLook look,Vector3 scale,bool repeat,params OverlaySurface[] surfaces)
    {
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();
        scale=Vector3.Abs(scale);
        if(!Finite(scale)||Math.Min(scale.X,Math.Min(scale.Y,scale.Z))<1e-6f)throw new InvalidOperationException("Invalid visual scale.");
        foreach(var s in surfaces)
        {
            if(s.Vertices.Length!=s.Normals.Length||s.Triangles.Length%3!=0||s.Vertices.Any(v=>!Finite(v))||s.Normals.Any(v=>!Finite(v))||s.Triangles.Any(i=>i<0||i>=s.Vertices.Length))
                throw new InvalidOperationException("Invalid native visual snapshot.");
            for(int t=0;t<s.Triangles.Length;t+=3)
            {
                var a=s.Vertices[s.Triangles[t]]*scale;var b=s.Vertices[s.Triangles[t+1]]*scale;var c=s.Vertices[s.Triangles[t+2]]*scale;
                var normal=Vector3.Abs(Vector3.Cross(b-a,c-a));
                int axis=normal.X>normal.Y&&normal.X>normal.Z?0:normal.Y>normal.Z?1:2;
                Vector2 Project(Vector3 p)=>axis==0?new(p.Z,p.Y):axis==1?new(p.X,p.Z):new(p.X,p.Y);
                var triangle=new List<Point>();
                for(int j=0;j<3;j++){int i=s.Triangles[t+j];triangle.Add(new(s.Vertices[i],s.Normals[i],Project(s.Vertices[i]*scale)/look.Tile));}
                if(repeat||look.Glass){Emit(triangle,0,0,0);continue;}
                int minX=(int)MathF.Floor(triangle.Min(p=>p.Projected.X)),maxX=(int)MathF.Floor(triangle.Max(p=>p.Projected.X));
                if((long)maxX-minX>2048)throw new InvalidOperationException("Visual tiling budget exceeded.");
                for(int x=minX;x<=maxX;x++)
                {
                    var column=Clip(Clip(triangle,p=>p.Projected.X-x),p=>x+1-p.Projected.X);
                    if(column.Count<3)continue;
                    // Alternate columns: the seams do not form a conspicuous cross-grid.
                    float shift=(x&1)==0?0:.37f;
                    int minY=(int)MathF.Floor(column.Min(p=>p.Projected.Y)-shift),maxY=(int)MathF.Floor(column.Max(p=>p.Projected.Y)-shift);
                    if((long)maxY-minY>2048)throw new InvalidOperationException("Visual tiling budget exceeded.");
                    for(int y=minY;y<=maxY;y++)Emit(Clip(Clip(column,p=>p.Projected.Y-y-shift),p=>y+shift+1-p.Projected.Y),x,y,shift);
                }
                void Emit(List<Point> polygon,int x,int y,float shift)
                {
                    for(int i=1;i<polygon.Count-1;i++)
                    {
                        if(Vector3.Cross(polygon[i].P-polygon[0].P,polygon[i+1].P-polygon[0].P).LengthSquared()<1e-18f)continue;
                        if(vertices.Count+3>MaxVertices)throw new InvalidOperationException("Visual vertex budget exceeded; native retained.");
                        foreach(var p in new[]{polygon[0],polygon[i],polygon[i+1]})
                        {
                            vertices.Add(p.P);normals.Add(p.N);
                            if(repeat){uv.Add(p.Projected);continue;}
                            var local=Vector2.Clamp(p.Projected-new Vector2(x,y+shift),Vector2.Zero,Vector2.One);
                            uv.Add(new(look.Rect.X+local.X*look.Rect.Z,look.Rect.Y+local.Y*look.Rect.W));
                        }
                    }
                }
            }
        }
        if(vertices.Count<3)throw new InvalidOperationException("No visual triangles.");
        return new(vertices.ToArray(),Enumerable.Range(0,vertices.Count).ToArray(),normals.ToArray(),uv.ToArray());
    }
    internal static Vector4[] Tangents(OverlayCopy data)
    {
        var result=new Vector4[data.Vertices.Length];
        for(int i=0;i<data.Triangles.Length;i+=3)
        {
            int a=data.Triangles[i],b=data.Triangles[i+1],c=data.Triangles[i+2];
            var e1=data.Vertices[b]-data.Vertices[a];var e2=data.Vertices[c]-data.Vertices[a];
            var u1=data.Uv[b]-data.Uv[a];var u2=data.Uv[c]-data.Uv[a];float det=u1.X*u2.Y-u1.Y*u2.X;
            var tangent=Math.Abs(det)>1e-12f?(e1*u2.Y-e2*u1.Y)/det:e1;
            var bitangent=Math.Abs(det)>1e-12f?(e2*u1.X-e1*u2.X)/det:e2;
            foreach(int index in new[]{a,b,c})
            {
                var n=Vector3.Normalize(data.Normals[index]);var dir=tangent-n*Vector3.Dot(n,tangent);
                if(dir.LengthSquared()<1e-12f)dir=Vector3.Cross(n,Math.Abs(n.Y)<.9f?Vector3.UnitY:Vector3.UnitX);
                dir=Vector3.Normalize(dir);result[index]=new(dir,Vector3.Dot(Vector3.Cross(n,dir),bitangent)<0?-1:1);
            }
        }
        return result;
    }
    private static List<Point> Clip(List<Point> input,Func<Point,float> distance)
    {
        var output=new List<Point>();if(input.Count==0)return output;
        var previous=input[^1];float before=distance(previous);
        foreach(var current in input)
        {
            float after=distance(current);bool inside=after>=0,wasInside=before>=0;
            if(inside!=wasInside)
            {float t=before/(before-after);var n=Vector3.Lerp(previous.N,current.N,t);output.Add(new(Vector3.Lerp(previous.P,current.P,t),n.LengthSquared()>1e-12f?Vector3.Normalize(n):current.N,Vector2.Lerp(previous.Projected,current.Projected,t)));}
            if(inside)output.Add(current);previous=current;before=after;
        }
        return output;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
