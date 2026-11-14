using System.Numerics;
namespace SprocketExpandedBallistics;

// Value copies only. The native tessellator supplies all positions, topology and normals.
internal sealed record OverlaySurface(Vector3[] Vertices,int[] Triangles,Vector3[] Normals);
internal sealed record OverlayCopy(Vector3[] Vertices,int[] Triangles,Vector3[] Normals,Vector2[] Uv)
{
    internal int FallbackTriangles {get;init;}
    internal double FallbackArea {get;init;}
    internal double SideArea {get;init;}
}
internal static class CassetteNativeOverlay
{
    internal static OverlayCopy Copy(OverlaySurface front,OverlaySurface back,OverlaySurface sides,CassettePreset? preset=null)
    {
        Validate(front);Validate(back);Validate(sides);
        // Exact ruled walls remain an optimization, never a coverage gate.
        try{return CopyExact(front,back,sides,preset) with{SideArea=PlateFrontAssignment.Area(sides)};}
        catch(InvalidOperationException){return PlateFrontAssignment.Copy(front,back,sides,preset);}
    }
    private static void Validate(OverlaySurface part)
    {
        if(part.Vertices.Length!=part.Normals.Length||part.Triangles.Length%3!=0||
           part.Vertices.Concat(part.Normals).Any(v=>!Finite(v))||part.Triangles.Any(i=>i<0||i>=part.Vertices.Length))
            throw new InvalidOperationException("Invalid native plate tessellation.");
    }
    private static OverlayCopy CopyExact(OverlaySurface front,OverlaySurface back,OverlaySurface sides,CassettePreset? preset=null)
    {
        foreach(var part in new[]{front,back,sides})
            if(part.Vertices.Length!=part.Normals.Length||part.Triangles.Length%3!=0||
                part.Vertices.Concat(part.Normals).Any(v=>!Finite(v))||part.Triangles.Any(i=>i<0||i>=part.Vertices.Length))
                throw new InvalidOperationException("Invalid native plate tessellation.");
        if(front.Vertices.Length<3||back.Vertices.Length<3)throw new InvalidOperationException("Native front/back not ready.");
        var walls=OverlayWallDepth.Derive(front,back,sides);
        int offset=front.Vertices.Length+back.Vertices.Length;
        var vertices=front.Vertices.Concat(back.Vertices).Concat(sides.Vertices).ToList();
        var normals=front.Normals.Concat(back.Normals).Concat(sides.Normals).ToList();
        var triangles=front.Triangles.Concat(back.Triangles.Select(i=>i+front.Vertices.Length)).ToList();
        var uv=Enumerable.Repeat(new Vector2(.5f,0),front.Vertices.Length)
            .Concat(Enumerable.Repeat(new Vector2(.5f,1),back.Vertices.Length))
            .Concat(Enumerable.Repeat(new Vector2(.5f,float.NaN),sides.Vertices.Length)).ToList();
        var owners=new OverlayWallDepth?[sides.Vertices.Length];
        var duplicates=new Dictionary<(int,OverlayWallDepth),int>();
        int Index(int original,OverlayWallDepth wall)
        {
            float d=wall.Depth(sides.Vertices[original]);
            if(!float.IsFinite(d)||d < -1e-4f||d>1.0001f)throw new InvalidOperationException("Native side depth outside its wall.");
            d=Math.Clamp(d,0,1);int index=offset+original;
            if(float.IsNaN(uv[index].Y)){uv[index]=new(.5f,d);owners[original]=wall;return index;}
            if(owners[original]==wall&&Math.Abs(uv[index].Y-d)<1e-6f)return index;
            if(duplicates.TryGetValue((original,wall),out int duplicate))return duplicate;
            // One native vertex may have different depth in adjacent wall frames.
            vertices.Add(vertices[index]);normals.Add(normals[index]);uv.Add(new(.5f,d));duplicates[(original,wall)]=vertices.Count-1;return vertices.Count-1;
        }
        var boundaries=preset==null?Array.Empty<double>():CassetteDepthStrip.Boundaries(preset)[1..^1];
        for(int t=0;t<sides.Triangles.Length;t+=3)
        {
            int a=sides.Triangles[t],b=sides.Triangles[t+1],c=sides.Triangles[t+2];
            var whole=walls.Where(w=>w.Contains(sides.Vertices[a])&&w.Contains(sides.Vertices[b])&&w.Contains(sides.Vertices[c])).ToArray();
            if(whole.Length>0)
            {
                var wall=whole[0];
                if(whole.Skip(1).Any(w=>new[]{a,b,c}.Any(i=>Math.Abs(w.Depth(sides.Vertices[i])-wall.Depth(sides.Vertices[i]))>1e-4)))
                    throw new InvalidOperationException("Ambiguous native side wall; native plate retained.");
                // Constant-depth planes give an affine UV on every native triangle.
                // A tapered wall gets exact cuts at each requested layer boundary.
                float h0=Vector3.Dot(wall.B0-wall.F0,wall.FrontNormal),h1=Vector3.Dot(wall.B1-wall.F1,wall.FrontNormal);
                if(boundaries.Length==0||Math.Abs(h1-h0)<1e-7f)
                {triangles.Add(Index(a,wall));triangles.Add(Index(b,wall));triangles.Add(Index(c,wall));continue;}
            }
            // A native triangle crossing a crease is clipped into its actual wall
            // quads. New points/normals are interpolated on the native triangle.
            double coveredArea=0;var originalArea=Vector3.Cross(sides.Vertices[b]-sides.Vertices[a],sides.Vertices[c]-sides.Vertices[a]).Length()*.5;
            foreach(var wall in whole.Length>0?whole.Take(1):walls)
            {
                var sn=wall.SideNormal;
                if(new[]{a,b,c}.Any(i=>Math.Abs(Vector3.Dot(sides.Vertices[i]-wall.F0,sn))>OverlayWallDepth.Epsilon))continue;
                var polygon=new List<(Vector3 P,Vector3 N)>{(sides.Vertices[a],sides.Normals[a]),(sides.Vertices[b],sides.Normals[b]),(sides.Vertices[c],sides.Normals[c])};
                var quad=new[]{wall.F0,wall.F1,wall.B1,wall.B0};
                for(int e=0;e<4;e++)
                {var origin=quad[e];var inward=Vector3.Cross(sn,quad[(e+1)%4]-origin);polygon=Clip(polygon,p=>Vector3.Dot(p-origin,inward));}
                if(polygon.Count<3)continue;
                var levels=new[]{0d}.Concat(boundaries).Append(1d).ToArray();
                for(int level=0;level<levels.Length-1;level++)
                {
                    float lo=(float)levels[level],hi=(float)levels[level+1];
                    float Distance(Vector3 p,float d)
                    {
                        var origin=wall.At(0,d);var line=wall.At(1,d)-origin;
                        var positive=Vector3.Cross(sn,line);
                        if(Vector3.Dot(wall.At(.5f,1)-wall.At(.5f,0),positive)<0)positive=-positive;
                        return Vector3.Dot(p-origin,positive);
                    }
                    var strip=Clip(Clip(polygon,p=>Distance(p,lo)),p=>-Distance(p,hi));
                    for(int i=1;i<strip.Count-1;i++)
                    {
                        var area=Vector3.Cross(strip[i].P-strip[0].P,strip[i+1].P-strip[0].P).Length()*.5;
                        if(area<1e-12)continue;coveredArea+=area;
                        foreach(var v in new[]{strip[0],strip[i],strip[i+1]})
                        {triangles.Add(vertices.Count);vertices.Add(v.P);normals.Add(v.N);uv.Add(new(.5f,Math.Clamp(wall.Depth(v.P),0,1)));}
                    }
                }
            }
            if(Math.Abs(coveredArea-originalArea)>Math.Max(1e-9,originalArea*1e-4))
                throw new InvalidOperationException($"Native side wall correspondence incomplete (triangle {t/3}, {walls.Count} walls, area {coveredArea}/{originalArea}); native plate retained.");
        }
        // Preserve unused native vertices as independent value copies, too.
        for(int i=0;i<sides.Vertices.Length;i++)if(float.IsNaN(uv[offset+i].Y))
        {
            var wall=walls.FirstOrDefault(w=>w.Contains(sides.Vertices[i]));
            if(wall==null)throw new InvalidOperationException($"Native side point {i} at {sides.Vertices[i]} has no exact wall ({walls.Count}); native plate retained.");
            uv[offset+i]=new(.5f,Math.Clamp(wall.Depth(sides.Vertices[i]),0,1));
        }
        return new(vertices.ToArray(),triangles.ToArray(),normals.ToArray(),uv.ToArray());
    }
    private static List<(Vector3 P,Vector3 N)> Clip(List<(Vector3 P,Vector3 N)> input,Func<Vector3,float> signed)
    {
        var output=new List<(Vector3 P,Vector3 N)>();if(input.Count==0)return output;
        var previous=input[^1];float before=signed(previous.P);
        foreach(var current in input)
        {
            float after=signed(current.P);bool inside=after>=-1e-8f,wasInside=before>=-1e-8f;
            if(inside!=wasInside)
            {float u=before/(before-after);var n=Vector3.Lerp(previous.N,current.N,u);output.Add((Vector3.Lerp(previous.P,current.P,u),n.LengthSquared()>1e-12f?Vector3.Normalize(n):current.N));}
            if(inside)output.Add(current);previous=current;before=after;
        }
        return output;
    }
    internal static string Fingerprint(params OverlaySurface[] parts)
    {
        ulong hash=14695981039346656037;
        void Mix(int value){unchecked{hash=(hash^(uint)value)*1099511628211;}}
        foreach(var p in parts)
        {
            Mix(p.Vertices.Length);Mix(p.Normals.Length);Mix(p.Triangles.Length);
            foreach(var v in p.Vertices){Mix(BitConverter.SingleToInt32Bits(v.X));Mix(BitConverter.SingleToInt32Bits(v.Y));Mix(BitConverter.SingleToInt32Bits(v.Z));}
            foreach(var n in p.Normals){Mix(BitConverter.SingleToInt32Bits(n.X));Mix(BitConverter.SingleToInt32Bits(n.Y));Mix(BitConverter.SingleToInt32Bits(n.Z));}
            foreach(var t in p.Triangles)Mix(t);
        }
        return hash.ToString("X16",System.Globalization.CultureInfo.InvariantCulture);
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}

internal static class CassetteDepthStrip
{
    // One vertical texel per depth sample; point sampling, clamp, no mipmaps.
    internal const int Height=8192;
    internal static double[] Boundaries(CassettePreset? preset)
    {
        var layers=preset?.Layers??new[]{new CassetteLayer("rha",1,0,"up")};
        var result=new double[layers.Length+1];
        for(int i=0;i<layers.Length;i++)result[i+1]=result[i]+(layers[i].Material=="air"?layers[i].ThicknessMm:layers[i].ThicknessMm/Math.Cos(layers[i].TiltDegrees*Math.PI/180));
        double total=result[^1];if(!double.IsFinite(total)||total<=0)throw new InvalidOperationException("Invalid layer depths.");
        for(int i=1;i<result.Length;i++)result[i]/=total;
        return result;
    }
    internal static uint Colour(string material)=>material switch
    {
        "air"=>0x101912ff,"ceramicArray" or "boronCarbide" or "aluminaCeramic"=>0xa5894aff,
        "siliconCarbide"=>0x48796fff,"cwepGlassTextolite" or "varmaComposite"=>0x657d1fff,
        "elastomerNERA" or "neraCore"=>0x192b39ff,"aramidSpallLiner"=>0xb59712ff,
        "heavyAlloyInsert"=>0x3b4b68ff,"duMesh"=>0x47581cff,"castArmour"=>0x79431fff,
        "aluminiumArmour" or "titaniumArmour" or "magnesiumAlloy"=>0x627f95ff,
        "quartzFiller" or "sand"=>0x8c652dff,_=>0x385c7aff
    };
    internal static uint[] Build(CassettePreset? preset)
    {
        var layers=preset?.Layers??new[]{new CassetteLayer("rha",1,0,"up")};
        var bounds=Boundaries(preset);var pixels=new uint[Height];int layer=0;
        for(int y=0;y<Height;y++)
        {while(layer<layers.Length-1&&(y+.5)/Height>=bounds[layer+1])layer++;pixels[y]=Colour(layers[layer].Material);}
        for(int i=1;i<bounds.Length-1;i++)pixels[Math.Clamp((int)Math.Round(bounds[i]*Height),1,Height-2)]=0x090c10ff;
        return pixels;
    }
}

// Used by the real runtime and lifetime tests. Every entry must be a mod-created object.
internal sealed class OverlayResources<T> where T:class
{
    private readonly List<T> owned=new();private readonly Action<T> destroy;
    internal OverlayResources(Action<T> destroy){this.destroy=destroy;}
    internal int Count=>owned.Count;
    internal T Own(T value){owned.Add(value);return value;}
    internal void Clear()
    {
        Exception? failure=null;
        for(int i=owned.Count-1;i>=0;i--)try{destroy(owned[i]);}catch(Exception ex){failure??=ex;}
        owned.Clear();if(failure!=null)throw failure;
    }
}
