using System.Globalization;
using System.Numerics;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>0.22.60 Convert to layers, frozen (copied from src-0.22.60/ComposerFill.cs) for the 0.22.61 check that a loose plate and a Cheek fill (shell) filling give byte-identical output.</summary>
internal static class LegacyLayerFaces060
{
    private const string Title="Convert to layers - inspect before applying";
    private const string Tooltip="Splits each face into one face per armour layer. Air layers become real gaps.";
    internal const int MinimumMm=3;
    /// <summary>A normal-stacked corner whose layer face is tilted so much that the native back point would leave the corner's line by more than this (mm) gets a rib instead.</summary>
    internal const double ParallelDriftMm=.25;
    private static readonly double SinOneDegree=Math.Sin(-Math.PI/180);
    private sealed record SourceFace(int[] V,ushort[] T,byte[] Tm,int[] Te,string Raw,bool Packed);
    private sealed record Line(Vector3 Origin,Vector3 U,double LengthMm,bool Rib,int OriginalEdge,string Key,bool Changed);

    internal static LayerConversion Convert(string json,CassetteModel recipe,Func<string,string>? label=null,Func<string,double>? density=null)
    {
        var parsed=Parse(json);
        LayerConversion? result=null;
        foreach(var kind in new[]{LayerLayoutKind.PerLayer,LayerLayoutKind.PerBlock})
        {
            var layout=LayerLayout.Build(recipe.Preset,kind,label);
            var attempt=Attempt(json,parsed,recipe,layout,density,out var thinnest,out var problem);
            if(attempt!=null){result=attempt;break;}
            if(problem!=null)throw new InvalidOperationException(problem);
            if(kind==LayerLayoutKind.PerBlock)throw new InvalidOperationException($"This plate is too thin for layers: even a block of touching layers would be thinner than 3 mm (thinnest {thinnest:0.#} mm). Make the plate thicker, or use a cassette with thicker layers.");
        }
        return result!;
    }

    private static (float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var vertices=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        bool hasFlags=root.TryGetProperty("edgeFlags",out var fl);
        var flags=hasFlags?fl.EnumerateArray().Select(x=>x.GetInt32()).ToArray():new int[edges.Length/2];
        if(flags.Length!=edges.Length/2)flags=flags.Concat(new int[Math.Max(0,edges.Length/2-flags.Length)]).Take(edges.Length/2).ToArray();
        var faces=new List<SourceFace>();
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var v=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var t=f.GetProperty("t").EnumerateArray().Select(x=>x.GetUInt16()).ToArray();
            bool packed=true;var tm=new byte[v.Length];var te=new int[v.Length];
            foreach(var p in f.EnumerateObject())if(p.Name is not ("v" or "t" or "tm" or "te"))packed=false;
            if(f.TryGetProperty("tm",out var tmv))
            {
                if(tmv.ValueKind==JsonValueKind.Array){int j=0;foreach(var x in tmv.EnumerateArray()){if(j<tm.Length)tm[j]=(byte)x.GetInt32();j++;}}
                else{var bits=tmv.GetUInt64();if(v.Length>ComposerFill.PackedCorners){if(bits!=0)packed=false;}else for(int j=0;j<v.Length;j++)tm[j]=(byte)(bits>>(8*j));}
            }
            if(f.TryGetProperty("te",out var tev))
            {
                if(tev.ValueKind==JsonValueKind.Array){int j=0;foreach(var x in tev.EnumerateArray()){if(j<te.Length)te[j]=x.GetInt32();j++;}}
                else{var bits=tev.GetUInt64();if(v.Length>ComposerFill.PackedCorners){if(bits!=0)packed=false;}else for(int j=0;j<v.Length;j++)te[j]=(int)((bits>>(16*j))&0xffff);}
            }
            faces.Add(new(v,t,tm,te,f.GetRawText(),packed));
        }
        return (vertices,edges,flags,hasFlags,faces.ToArray());
    }

    private static LayerConversion? Attempt(string json,(float[] Vertices,int[] Edges,int[] Flags,bool HasFlags,SourceFace[] Faces) mesh,CassetteModel recipe,LayerLayout layout,Func<string,double>? density,out double thinnest,out string? problem)
    {
        thinnest=double.PositiveInfinity;problem=null;
        var p0=mesh.Vertices;int v0=p0.Length/3;Vector3 P(int i)=>new(p0[3*i],p0[3*i+1],p0[3*i+2]);
        var groups=layout.Groups;int G=groups.Length;
        // Boundary fractions: the front of every group and the end of every group (touching groups share one boundary).
        var fractions=new List<double>{0};int Boundary(double f){for(int i=0;i<fractions.Count;i++)if(Math.Abs(fractions[i]-f)<1e-12)return i;fractions.Add(f);return fractions.Count-1;}
        var front=new int[G];var end=new int[G];
        for(int g=0;g<G;g++){front[g]=Boundary(groups[g].StartFraction);end[g]=Boundary(groups[g].EndFraction);}
        var sources=new List<LayerSourceInfo>();var lines=new List<Line[]?>();
        int skipped=0,changed=0;
        // 1. Corner lines of every face that can be converted.
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var f=mesh.Faces[i];var corners=f.V.Select(P).ToArray();
            var n=CheekShellFill.Newell(corners);
            string? skip=f.V.Length>4?"more than 4 corners":f.V.Length<3?"degenerate":!f.Packed?"unknown face data":n.LengthSquared()<1e-14f||!ComposerFill.Finite(n)?"degenerate":null;
            if(skip==null&&f.V.Distinct().Count()!=f.V.Length)skip="degenerate";
            if(skip!=null)
            {
                skipped++;var nn=n.LengthSquared()>1e-14f?Vector3.Normalize(n):Vector3.Zero;
                sources.Add(new(i,false,skip,corners,corners.Select((c,j)=>c-nn*(j<f.T.Length?f.T[j]:0)*.001f).ToArray(),nn,false,false,Array.Empty<Vector3>(),Array.Empty<double>()));lines.Add(null);continue;
            }
            n=Vector3.Normalize(n);var faceLines=new Line[f.V.Length];bool faceChanged=false;
            var back=new Vector3[f.V.Length];
            for(int j=0;j<f.V.Length;j++)
            {
                var v=corners[j];var next=corners[(j+1)%corners.Length];var prev=corners[(j+corners.Length-1)%corners.Length];
                bool rib=false;Vector3 u=-n;double length=f.T[j];int edge=-1;string key=$"n:{i}:{j}";
                if(f.Tm[j]==CheekShellFill.AlongEdgeManual)
                {
                    int e=f.Te[j];
                    if(e>=0&&2*e+1<mesh.Edges.Length&&(mesh.Edges[2*e]==f.V[j]||mesh.Edges[2*e+1]==f.V[j]))
                    {
                        int w=mesh.Edges[2*e]==f.V[j]?mesh.Edges[2*e+1]:mesh.Edges[2*e];var dir=P(w)-v;
                        if(w>=0&&w<v0&&dir.LengthSquared()>1e-14f)
                        {
                            var cand=Vector3.Normalize(dir);double d=Vector3.Dot(n,cand);
                            // The native rule: a rib at least 1 degree behind the face; otherwise the game itself uses the face normal.
                            if(d<SinOneDegree){rib=true;u=cand;length=f.T[j]/-d;edge=e;key=$"r:{f.V[j]}:{e}";}
                        }
                    }
                    back[j]=rib?CheekShellFill.NativeBack(v,next,prev,u,f.T[j],n):v-n*(f.T[j]*.001f);
                }
                else
                {
                    // Auto and the other native modes choose their own direction; the layers stack along the face normal.
                    if(f.Tm[j]!=0)faceChanged=true;
                    back[j]=v-n*(f.T[j]*.001f);
                }
                faceLines[j]=new(v,u,length,rib,edge,key,f.Tm[j]!=0&&!rib&&f.Tm[j]!=CheekShellFill.AlongEdgeManual);
            }
            if(faceChanged)changed++;
            lines.Add(faceLines);
            sources.Add(new(i,true,null,corners,back,n,faceChanged,false,faceLines.Select(l=>l.U).ToArray(),faceLines.Select(l=>l.LengthMm).ToArray()));
        }
        // 2. Corners that follow the same rib from the same vertex share their boundary points when their own lengths along the rib agree within
        // 1 mm (a shell built by Cheek fill (shell)); they then use the mean length. A face whose thickness was changed by hand keeps its own points.
        var byRib=new Dictionary<string,List<(int Face,int Corner,double Length)>>(StringComparer.Ordinal);
        for(int i=0;i<lines.Count;i++)if(lines[i] is {} fl)for(int j=0;j<fl.Length;j++)if(fl[j].Rib){if(!byRib.TryGetValue(fl[j].Key,out var list))byRib[fl[j].Key]=list=new();list.Add((i,j,fl[j].LengthMm));}
        var shared=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var (key,list) in byRib)
        {
            var sorted=list.OrderBy(x=>x.Length).ToList();int cluster=0;
            for(int s=0;s<sorted.Count;)
            {
                int e=s;while(e+1<sorted.Count&&sorted[e+1].Length-sorted[s].Length<=1)e++;
                var mean=sorted.Skip(s).Take(e-s+1).Average(x=>x.Length);var name=$"{key}:{cluster++}";shared[name]=mean;
                for(int k=s;k<=e;k++){var (face,corner,_)=sorted[k];lines[face]![corner]=lines[face]![corner] with{Key=name};}
                s=e+1;
            }
        }
        double Length(Line l)=>l.Rib?shared[l.Key]:l.LengthMm;
        // 3. Points, edges and faces.
        var newVertices=new List<Vector3>();var points=new Dictionary<(string,int),int>();
        int Point(int originalVertex,Line l,int boundary)
        {
            if(fractions[boundary]==0)return originalVertex;
            if(points.TryGetValue((l.Key,boundary),out var id))return id;
            var q=l.Origin+l.U*(float)(fractions[boundary]*Length(l)*.001);
            if(!ComposerFill.Finite(q))throw new InvalidOperationException("Convert to layers: a layer point is not finite.");
            id=v0+newVertices.Count;newVertices.Add(q);points[(l.Key,boundary)]=id;return id;
        }
        Vector3 At(int id)=>id<v0?P(id):newVertices[id-v0];
        var edgeIndex=new Dictionary<(int,int),int>();var origEdges=mesh.Edges.Length/2;
        for(int e=0;e<origEdges;e++)edgeIndex.TryAdd((Math.Min(mesh.Edges[2*e],mesh.Edges[2*e+1]),Math.Max(mesh.Edges[2*e],mesh.Edges[2*e+1])),e);
        var newEdges=new List<int>();var newFlags=new List<int>();
        int Edge(int a,int b,int flag)
        {
            var k=(Math.Min(a,b),Math.Max(a,b));if(edgeIndex.TryGetValue(k,out var e))return e;
            e=origEdges+newEdges.Count/2;newEdges.Add(a);newEdges.Add(b);newFlags.Add(flag);edgeIndex[k]=e;return e;
        }
        var faceTexts=new List<string>();var faces=new List<LayerFaceInfo>();var backCorners=new List<Vector3[]>();int ribbed=0;double volumeBefore=0,volumeAfter=0,trueMass=0;
        double GroupDensity(LayerGroup g)
        {
            if(density==null)return 0;double mass=0;
            foreach(var i in g.Layers){var l=recipe.Preset.Layers[i];mass+=density(l.Material)*LayerLayout.LayerDepth(l);}
            return mass/g.DepthMm;
        }
        for(int i=0;i<mesh.Faces.Length;i++)
        {
            var f=mesh.Faces[i];var src=sources[i];var fl=lines[i];
            volumeBefore+=ComposerFill.PrismVolume(src.Front,src.Back);
            if(fl==null)
            {
                faceTexts.Add(f.Raw);backCorners.Add(src.Back);volumeAfter+=ComposerFill.PrismVolume(src.Front,src.Back);
                faces.Add(new(i,-1,f.V,src.Front,src.Back,f.T,f.Tm,f.Te,src.Normal));continue;
            }
            int n=f.V.Length;bool faceRibbed=false;
            for(int g=0;g<G;g++)
            {
                var ids=new int[n];for(int j=0;j<n;j++)ids[j]=Point(f.V[j],fl[j],front[g]);
                var corners=ids.Select(At).ToArray();var nn=CheekShellFill.Newell(corners);
                if(nn.LengthSquared()<1e-14f){problem=$"Convert to layers: face {i+1} would get a degenerate layer; nothing changed.";return null;}
                var ng=Vector3.Normalize(nn);
                if(Vector3.Dot(ng,src.Normal)<=0){problem=$"Convert to layers: a layer of face {i+1} would face the wrong way; nothing changed.";return null;}
                var t=new ushort[n];var modes=new byte[n];var ribs=new int[n];var back=new Vector3[n];
                for(int j=0;j<n;j++)
                {
                    var l=fl[j];var p=corners[j];var next=corners[(j+1)%n];var prev=corners[(j+n-1)%n];
                    double len=(groups[g].EndFraction-groups[g].StartFraction)*Length(l);
                    var target=l.Origin+l.U*(float)(fractions[end[g]]*Length(l)*.001);
                    bool useRib=l.Rib;
                    if(!useRib)
                    {
                        // Normal stacking is exact while the layer face is parallel to the original face; a tilted layer of a wedge gets a rib along the original normal.
                        double depth=Vector3.Dot(p-target,ng)*1000;
                        double drift=Math.Max(0,depth)*Vector3.Cross(ng,-l.U).Length();
                        if(drift<=ParallelDriftMm)
                        {
                            int tt=(int)Math.Round(depth,MidpointRounding.AwayFromZero);thinnest=Math.Min(thinnest,tt);
                            if(tt<MinimumMm)return null;
                            t[j]=(ushort)tt;modes[j]=0;ribs[j]=0;back[j]=p-ng*(tt*.001f);continue;
                        }
                        useRib=true;faceRibbed=true;
                    }
                    double d=Vector3.Dot(ng,l.U);
                    if(d>SinOneDegree){problem=$"Convert to layers: a layer of face {i+1} would lie along its rib (corner {j+1}); nothing changed.";return null;}
                    double offsetDepth=Vector3.Dot(CheekShellFill.Offset(p,next,prev),ng)*1000;
                    int value=(int)Math.Round(len*-d+offsetDepth,MidpointRounding.AwayFromZero);
                    // The last layer never ends behind the original back: on a shallow rib one whole millimetre is several millimetres along the rib.
                    if(g==G-1)while(value>MinimumMm&&Vector3.Dot(CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng)-target,l.U)>.0001f)value--;
                    thinnest=Math.Min(thinnest,value);
                    if(value<MinimumMm)return null;
                    if(value>short.MaxValue){problem="Convert to layers: a layer would be thicker than the game allows; nothing changed.";return null;}
                    t[j]=(ushort)value;modes[j]=CheekShellFill.AlongEdgeManual;
                    // The rib of this corner: the original rib for the first layer, otherwise a loose edge to the next point on the same line (only its direction counts).
                    if(l.Rib&&fractions[front[g]]==0)ribs[j]=l.OriginalEdge;
                    else{int to=g+1<G?Point(f.V[j],l,front[g+1]):Point(f.V[j],l,end[G-1]);ribs[j]=Edge(ids[j],to,0);}
                    back[j]=CheekShellFill.NativeBack(p,next,prev,l.U,(ushort)value,ng);
                }
                var nb=CheekShellFill.Newell(back);
                if(nb.LengthSquared()<1e-16f||Vector3.Dot(nb,ng)<=0){problem=$"Convert to layers: the back of a layer of face {i+1} would turn over; nothing changed.";return null;}
                // Boundary edges of the layer face (the first layer reuses the original ones).
                for(int j=0;j<n;j++)
                {
                    int a=ids[j],b=ids[(j+1)%n];
                    var k=(Math.Min(f.V[j],f.V[(j+1)%n]),Math.Max(f.V[j],f.V[(j+1)%n]));int flag=edgeIndex.TryGetValue(k,out var oe)&&oe<origEdges?mesh.Flags[oe]:0;
                    Edge(a,b,flag);
                }
                ulong tm=0,te=0;for(int j=0;j<n;j++){tm|=(ulong)modes[j]<<(8*j);te|=(ulong)(uint)ribs[j]<<(16*j);}
                faceTexts.Add("{\"v\":["+string.Join(",",ids)+"],\"t\":["+string.Join(",",t)+"],\"tm\":"+tm.ToString(CultureInfo.InvariantCulture)+",\"te\":"+te.ToString(CultureInfo.InvariantCulture)+"}");
                faces.Add(new(i,g,ids,corners,back,t,modes,ribs,ng));backCorners.Add(back);
                var v=ComposerFill.PrismVolume(corners,back);volumeAfter+=v;trueMass+=v*GroupDensity(groups[g]);
            }
            if(faceRibbed){ribbed++;sources[i]=src with{Ribbed=true};}
        }
        if(origEdges+newEdges.Count/2>=65535){problem="Convert to layers: the plate would get too many edges for the game's corner data; nothing changed.";return null;}
        // 4. The payload: the original JSON with the vertex, edge and face arrays extended; every other property is kept as it was.
        var payload=Assemble(json,newVertices,newEdges,newFlags,mesh.HasFlags,faceTexts);
        var after=ComposerFill.Read(payload);
        if(after.FaceCount!=faces.Count)throw new InvalidOperationException("Convert to layers: payload face count mismatch.");
        int converted=sources.Count(s=>s.Converted);
        var mode=layout.Kind==LayerLayoutKind.PerLayer?ThicknessMode.Layers:ThicknessMode.LayerBlocks;
        double rho=recipe.Passive.Density;
        var lines2=new List<string>
        {
            $"{converted} faces become {converted*G} faces: {G} layers each.",
            layout.Kind==LayerLayoutKind.PerLayer?"One face per layer.":"Touching layers share one face, because some layers would be thinner than 3 mm.",
            $"Thinnest layer: {thinnest.ToString("0",CultureInfo.InvariantCulture)} mm."
        };
        if(changed>0)lines2.Add($"{changed} faces now thicken straight back; their rear may shift slightly.");
        if(skipped>0)lines2.Add($"{skipped} faces have more than 4 corners and stay as they are.");
        lines2.Add($"Weight {(volumeBefore*rho).ToString("0.#",CultureInfo.InvariantCulture)} kg now, {(volumeAfter*rho).ToString("0.#",CultureInfo.InvariantCulture)} kg as layers.");
        var inspection=string.Join("\n",lines2);
        var summary=$"{converted} faces became {converted*G} faces ({(layout.Kind==LayerLayoutKind.PerLayer?"one face per layer":"touching layers share one face")}). Undo removes this change in one step.";
        var plan=new ComposerMeshPlan(payload,after,0,0,0,summary,true){Action=ComposerMeshAction.LayerConvert,Total=after.Faces.Sum(x=>x.VertexCount),MaximumMm=short.MaxValue,BackCorners=backCorners.ToArray(),CopyCheck=copy=>VerifyCopy(payload,copy)};
        return new(plan,layout,mode,sources.ToArray(),faces.ToArray(),converted,skipped,changed,ribbed,thinnest,volumeBefore,volumeAfter,volumeBefore*rho,volumeAfter*rho,density==null?null:trueMass+faces.Where(x=>x.Rank<0).Sum(x=>ComposerFill.PrismVolume(x.Front,x.Back))*rho,inspection);
    }

    /// <summary>The copy of the game must keep every planned point, edge and face with its tm/te token text (the same check as Cheek fill (shell)).</summary>
    internal static string? VerifyCopy(string planned,string copy)
    {
        var problem=CheekShellFill.VerifyCopy(planned,copy);
        return problem==null?null:"The game did not keep the layer faces"+problem[CheekShellFill.LinkedBackMarker.Length..]+"; nothing changed.";
    }

    private static string Assemble(string json,List<Vector3> vertices,List<int> edges,List<int> flags,bool hasFlags,List<string> faces)
    {
        using var doc=JsonDocument.Parse(json);var parts=new List<string>();
        static string Extend(string raw,IEnumerable<string> items)
        {
            var list=items.ToList();if(list.Count==0)return raw;
            var body=raw.Trim();body=body[1..^1].Trim();
            return "["+(body.Length==0?"":body+",")+string.Join(",",list)+"]";
        }
        static string F(float x)=>x.ToString("R",CultureInfo.InvariantCulture);
        bool wroteFlags=false;
        foreach(var p in doc.RootElement.EnumerateObject())
        {
            string value=p.Name switch
            {
                "vertices"=>Extend(p.Value.GetRawText(),vertices.SelectMany(v=>new[]{F(v.X),F(v.Y),F(v.Z)})),
                "edges"=>Extend(p.Value.GetRawText(),edges.Select(e=>e.ToString(CultureInfo.InvariantCulture))),
                "edgeFlags"=>Extend(p.Value.GetRawText(),flags.Select(e=>e.ToString(CultureInfo.InvariantCulture))),
                "faces"=>"["+string.Join(",",faces)+"]",
                _=>p.Value.GetRawText()
            };
            if(p.Name=="edgeFlags")wroteFlags=true;
            parts.Add(JsonSerializer.Serialize(p.Name)+":"+value);
        }
        if(!hasFlags&&!wroteFlags&&flags.Count>0)throw new InvalidOperationException("Convert to layers: the plate mesh has no edge flags.");
        return "{"+string.Join(",",parts)+"}";
    }
}

