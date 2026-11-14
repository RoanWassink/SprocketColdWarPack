using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.62: the outer shape of a converted box, measured with what the game builds (native back points per tm value), not with the plan.
/// Models: the boxes of 0.22.61 and a kinked two-piece beam (10 faces: 2 front, 2 back, 2 top, 2 bottom, 2 ends) as an add-on is kneaded (tm 1 = Auto on every corner).
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    internal sealed record NativeFace(int Index,Vector3[] Front,Vector3[] Back,int[] Tm,bool AutoUsed);
    /// <summary>
    /// The faces of a mesh JSON as the game builds them: back point per corner by the native formulas (ALONG-EDGE-FORMULA.md): tm 0 along the normal,
    /// tm 4 along the stored edge (te) when it touches the corner and lies at least 1 degree behind the face, else along the normal; the offset of 0.25 mm included.
    /// tm 1 (Auto) cannot be predicted (the game picks an edge or the vertex normal at run time): the point along the normal is used and the face is flagged.
    /// </summary>
    internal static List<NativeFace> NativeBuild(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var v=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var edges=root.TryGetProperty("edges",out var e)?e.EnumerateArray().Select(x=>x.GetInt32()).ToArray():Array.Empty<int>();
        var result=new List<NativeFace>();int index=0;
        foreach(var f in root.GetProperty("faces").EnumerateArray())
        {
            var id=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            var t=f.TryGetProperty("t",out var tv)?tv.EnumerateArray().Select(x=>(int)x.GetUInt16()).ToArray():new int[id.Length];
            ulong tm=f.TryGetProperty("tm",out var tmv)&&tmv.ValueKind==JsonValueKind.Number?tmv.GetUInt64():0,te=f.TryGetProperty("te",out var tev)&&tev.ValueKind==JsonValueKind.Number?tev.GetUInt64():0;
            var p=id.Select(P).ToArray();var n=CheekShellFill.Newell(p);n=n.LengthSquared()<1e-14f?Vector3.Zero:Vector3.Normalize(n);
            var back=new Vector3[p.Length];var modes=new int[p.Length];bool auto=false;
            for(int j=0;j<p.Length;j++)
            {
                int mode=(int)((tm>>(8*j))&0xff);modes[j]=mode;back[j]=p[j]-n*(t[j]*.001f);
                if(mode==CheekShellFill.AlongEdgeManual)
                {
                    int edge=(int)((te>>(16*j))&0xffff);
                    if(2*edge+1<edges.Length&&(edges[2*edge]==id[j]||edges[2*edge+1]==id[j]))
                    {
                        int w=edges[2*edge]==id[j]?edges[2*edge+1]:edges[2*edge];
                        if((P(w)-p[j]).LengthSquared()>1e-14f)back[j]=CheekShellFill.NativeBack(p[j],p[(j+1)%p.Length],p[(j+p.Length-1)%p.Length],Vector3.Normalize(P(w)-p[j]),(ushort)t[j],n);
                    }
                }
                else if(mode!=0)auto=true;
            }
            result.Add(new(index++,p,back,modes,auto));
        }
        return result;
    }
    /// <summary>How far a point lies outside the closed surface of the old front faces: 0 when inside (ray parity) or on it, else the distance to the surface (metres).</summary>
    internal static double OutsideDistance(Vector3 point,IReadOnlyList<Vector3[]> polygons)
    {
        double best=double.PositiveInfinity;var tris=new List<FillTriangle>();
        foreach(var poly in polygons)for(int k=1;k+1<poly.Length;k++){tris.Add(new(poly[0],poly[k],poly[k+1]));best=Math.Min(best,LayerOverlay.Distance(point,new[]{poly[0],poly[k],poly[k+1]}));}
        if(best<1e-9)return 0;
        int votes=0;foreach(var d in new[]{new Vector3(.5773f,.5779f,.5768f),new Vector3(-.3f,.9f,.31f),new Vector3(.2f,-.4f,.89f)}){var dir=Vector3.Normalize(d);int c=0;foreach(var tr in tris)if(ComposerFill.Ray(point,dir,new[]{tr},100) is {} h&&h>0)c++;if(c%2==1)votes++;}
        return votes>=2?0:best;
    }
    /// <summary>The largest distance (mm) of any native front or back point of the converted mesh outside the old outer shape, and the face and kind where it happens.</summary>
    internal static (double Mm,string Where) ShapeDeviation(string oldJson,string newJson,Func<int,bool>? include=null)
    {
        var old=LayerMesh.Read(oldJson).Polygons.Where(p=>p.Length>=3).ToArray();double worst=0;string where="";
        foreach(var f in NativeBuild(newJson))
        {
            if(include!=null&&!include(f.Index))continue;
            for(int j=0;j<f.Front.Length;j++)
                foreach(var (kind,pt) in new[]{("front",f.Front[j]),("back",f.Back[j])})
                {var d=OutsideDistance(pt,old)*1000;if(d>worst){worst=d;where=$"face {f.Index} corner {j} {kind} tm {f.Tm[j]}";}}
        }
        return (worst,where);
    }


    internal static string UserTank=>Path.Combine(Fix02228Tests.SourceRoot(),"tests","fixtures","user-tanks","T-84 Oplot-M.blueprint");
    private static string UserMeshRaw()
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(UserTank));
        return doc.RootElement.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("vuid").GetInt32()==70).GetProperty("meshData").GetProperty("mesh").GetRawText();
    }
    /// <summary>The user's box before the conversion, rebuilt from the 12 original points of the converted mesh: 10 faces on Auto (tm 1), walls 5 mm, the right end 854 mm.</summary>
    private static string UserBoxBefore()
    {
        // 0.22.63: the blueprint of the user was replaced by a newer one (same file name); the 12 points of the box of 0.22.61/0.22.62 are kept here in millimetres.
        var v=new float[]{-835,0,-125, -835,0,125, 462,0,-425, 462,0,-675, -835,250,-125, -835,250,125, 462,250,-425, 462,250,-675, -528,250,-125, -528,0,-125, -528,0,125, -528,250,125}.Select(x=>x*.001f).ToArray();
        Vector3 P(int i)=>new(v[3*i],v[3*i+1],v[3*i+2]);
        var faces=new List<(int[] V,Vector3 Hint,int Mm)>
        {
            (new[]{1,10,11,5},Vector3.UnitZ,40),(new[]{10,2,6,11},Vector3.UnitZ,35),(new[]{0,9,8,4},-Vector3.UnitZ,5),(new[]{9,3,7,8},-Vector3.UnitZ,5),
            (new[]{1,10,9,0},-Vector3.UnitY,5),(new[]{10,2,3,9},-Vector3.UnitY,5),(new[]{5,11,8,4},Vector3.UnitY,5),(new[]{11,6,7,8},Vector3.UnitY,5),
            (new[]{0,1,5,4},-Vector3.UnitX,5),(new[]{2,3,7,6},Vector3.UnitX,854)
        };
        var list=new List<(int[],int)>();
        foreach(var (idx,hint,mm) in faces){var n=CheekShellFill.Newell(idx.Select(P).ToArray());list.Add((Vector3.Dot(n,hint)<0?idx.Reverse().ToArray():idx,mm));}
        var edges=new List<(int,int)>();foreach(var (fc,_) in list)for(int j=0;j<fc.Length;j++){var k=(Math.Min(fc[j],fc[(j+1)%fc.Length]),Math.Max(fc[j],fc[(j+1)%fc.Length]));if(!edges.Contains(k))edges.Add(k);}
        return Mesh(Enumerable.Range(0,12).Select(P),edges,list.Select(x=>Face(x.Item1,Enumerable.Repeat(x.Item2,x.Item1.Length),0x01010101UL)));
    }
    /// <summary>A kinked two-piece beam as a kneaded add-on: front chain of three points in the XZ plane, back chain behind it, height, every face 20 mm on Auto (tm 1).</summary>
    private static (string Json,Vector3 Forward) BeamJson(float[] xs,float[] zs,float depth,float height,float turnDegrees,bool perpendicular,int mm=20)
    {
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,turnDegrees*MathF.PI/180);
        var pts=new List<Vector3>();int Vi(int level,int chain,int i)=>(level*2+chain)*3+i;
        for(int level=0;level<2;level++)for(int chain=0;chain<2;chain++)for(int i=0;i<3;i++)
        {
            float y=level==0?-height/2:height/2;var front=new Vector3(xs[i],y,zs[i]);Vector3 p;
            if(chain==0)p=front;
            else if(!perpendicular)p=front-new Vector3(0,0,depth);
            else
            {
                Vector3 Dir(int a,int b){var d=new Vector3(xs[b]-xs[a],0,zs[b]-zs[a]);d=Vector3.Normalize(d);return new Vector3(-d.Z,0,d.X);}
                var nrm=i==0?Dir(0,1):i==2?Dir(1,2):Vector3.Normalize(Dir(0,1)+Dir(1,2));if(nrm.Z<0)nrm=-nrm;p=front-nrm*depth;
            }
            pts.Add(p);
        }
        var faces=new List<(int[] V,Vector3 Hint)>();
        for(int i=0;i<2;i++)
        {
            faces.Add((new[]{Vi(0,0,i),Vi(0,0,i+1),Vi(1,0,i+1),Vi(1,0,i)},Vector3.UnitZ));      // front
            faces.Add((new[]{Vi(0,1,i),Vi(0,1,i+1),Vi(1,1,i+1),Vi(1,1,i)},-Vector3.UnitZ));     // back
            faces.Add((new[]{Vi(1,0,i),Vi(1,0,i+1),Vi(1,1,i+1),Vi(1,1,i)},Vector3.UnitY));      // top
            faces.Add((new[]{Vi(0,0,i),Vi(0,0,i+1),Vi(0,1,i+1),Vi(0,1,i)},-Vector3.UnitY));     // bottom
        }
        faces.Add((new[]{Vi(0,0,0),Vi(1,0,0),Vi(1,1,0),Vi(0,1,0)},-Vector3.UnitX));              // end
        faces.Add((new[]{Vi(0,0,2),Vi(1,0,2),Vi(1,1,2),Vi(0,1,2)},Vector3.UnitX));              // end
        var list=new List<int[]>();
        foreach(var (idx,hint) in faces)
        {
            var poly=idx.Select(k=>pts[k]).ToArray();var n=CheekShellFill.Newell(poly);list.Add(Vector3.Dot(n,hint)<0?idx.Reverse().ToArray():idx);
        }
        var edges=new List<(int,int)>();foreach(var fc in list)for(int j=0;j<fc.Length;j++){var k=(Math.Min(fc[j],fc[(j+1)%fc.Length]),Math.Max(fc[j],fc[(j+1)%fc.Length]));if(!edges.Contains(k))edges.Add(k);}
        var rotated=pts.Select(p=>Vector3.Transform(p,q));
        return (Mesh(rotated,edges,list.Select(fc=>Face(fc,Enumerable.Repeat(mm,fc.Length),0x01010101UL))),Vector3.Transform(Vector3.UnitZ,Quaternion.Identity));
    }

    /// <summary>A kinked beam with explicit front and back chains (x,z) of three points each.</summary>
    private static string BeamJson2(Vector2[] front,Vector2[] back,float height,float turnDegrees,int mm=20)
    {
        var (json,_)=BeamJson(new[]{0f,1f,2f},new[]{0f,0f,0f},0,height,turnDegrees,false,mm);
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,turnDegrees*MathF.PI/180);
        var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;var v=node["vertices"]!.AsArray();
        int Vi(int level,int chain,int i)=>(level*2+chain)*3+i;
        for(int level=0;level<2;level++)for(int chain=0;chain<2;chain++)for(int i=0;i<3;i++)
        {
            var c=chain==0?front[i]:back[i];var pt=Vector3.Transform(new Vector3(c.X,level==0?-height/2:height/2,c.Y),q);int k=Vi(level,chain,i);
            v[3*k]=pt.X;v[3*k+1]=pt.Y;v[3*k+2]=pt.Z;
        }
        return node.ToJsonString();
    }
    /// <summary>A kneaded beam of n-1 pieces: front chain and back chain of n points (x,z), height, every face on Auto (tm 1) with its thickness; the pieces share their end faces.</summary>
    private static string ChainJson(Vector2[] front,Vector2[] back,float height,float turnDegrees,int mm)
    {
        int n=front.Length;var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,turnDegrees*MathF.PI/180);
        int Vi(int level,int chain,int i)=>(level*2+chain)*n+i;var pts=new Vector3[4*n];
        for(int level=0;level<2;level++)for(int chain=0;chain<2;chain++)for(int i=0;i<n;i++){var c=chain==0?front[i]:back[i];pts[Vi(level,chain,i)]=Vector3.Transform(new Vector3(c.X,level==0?-height/2:height/2,c.Y),q);}
        var faces=new List<(int[] V,Vector3 Hint)>();
        for(int i=0;i+1<n;i++)
        {
            faces.Add((new[]{Vi(0,0,i),Vi(0,0,i+1),Vi(1,0,i+1),Vi(1,0,i)},Vector3.UnitZ));faces.Add((new[]{Vi(0,1,i),Vi(0,1,i+1),Vi(1,1,i+1),Vi(1,1,i)},-Vector3.UnitZ));
            faces.Add((new[]{Vi(1,0,i),Vi(1,0,i+1),Vi(1,1,i+1),Vi(1,1,i)},Vector3.UnitY));faces.Add((new[]{Vi(0,0,i),Vi(0,0,i+1),Vi(0,1,i+1),Vi(0,1,i)},-Vector3.UnitY));
        }
        faces.Add((new[]{Vi(0,0,0),Vi(1,0,0),Vi(1,1,0),Vi(0,1,0)},-Vector3.UnitX));faces.Add((new[]{Vi(0,0,n-1),Vi(1,0,n-1),Vi(1,1,n-1),Vi(0,1,n-1)},Vector3.UnitX));
        var list=new List<int[]>();
        foreach(var (idx,hint) in faces){var poly=idx.Select(k=>pts[k]).ToArray();var nn=CheekShellFill.Newell(poly.Select(p=>Vector3.Transform(p,Quaternion.Inverse(q))).ToArray());list.Add(Vector3.Dot(nn,hint)<0?idx.Reverse().ToArray():idx);}
        var edges=new List<(int,int)>();foreach(var fc in list)for(int j=0;j<fc.Length;j++){var k=(Math.Min(fc[j],fc[(j+1)%fc.Length]),Math.Max(fc[j],fc[(j+1)%fc.Length]));if(!edges.Contains(k))edges.Add(k);}
        return Mesh(pts,edges,list.Select(fc=>Face(fc,Enumerable.Repeat(mm,fc.Length),0x01010101UL)));
    }
    /// <summary>Random kinked beams: the worst native deviation of layer faces and of the other faces.</summary>
    private static void ShapeFuzz(CassetteModel t90,Func<string,double> density,StringBuilder rows,Action<bool,string> check)
    {
        var rng=new Random(62);int tried=0,refused=0;var reasons=new Dictionary<string,int>();double worstL=0,worstS=0;string wl="",ws="";
        for(int n=0;n<250;n++)
        {
            float[] xs={-1.2f+(float)rng.NextDouble()*.2f,(float)rng.NextDouble()*.4f-.2f,.5f+(float)rng.NextDouble()*.3f};
            var front=new Vector2[3];var back=new Vector2[3];float depth=.25f+(float)rng.NextDouble()*.6f;
            for(int i=0;i<3;i++){front[i]=new(xs[i],(float)(rng.NextDouble()*.4-.1)+(i==1?.2f:0));back[i]=new(xs[i]+(float)(rng.NextDouble()*.3-.15),front[i].Y-depth+(float)(rng.NextDouble()*.2-.1));}
            var json=BeamJson2(front,back,.2f+(float)rng.NextDouble()*.4f,(float)(rng.NextDouble()*120-60),n%2==0?20:46);tried++;
            LayerConversion? r=null;try{r=LayerFaces.Convert(json,t90,null,density,Vector3.UnitZ);}catch(InvalidOperationException ex){refused++;reasons[ex.Message.Length>110?ex.Message[..110]:ex.Message]=reasons.GetValueOrDefault(ex.Message.Length>110?ex.Message[..110]:ex.Message)+1;continue;}
            if(r.Box==null)continue;
            var (a,wa)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (b,wb)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank<0);
            if(a>worstL){worstL=a;wl=$"#{n} {wa}";}if(b>worstS){worstS=b;ws=$"#{n} {wb}";}
            check(a<=1&&b<=r.Faces.Where(f=>f.Rank==LayerHit.SideWallRank).Select(f=>(double)f.Thickness.Max()).DefaultIfEmpty(5.0).Max()+.5,$"fuzz #{n}: converted beam: layers within 1 mm ({a:0.###}), walls within their thickness ({b:0.###} mm)");if(a>1)rows.AppendLine($"FUZZ #{n} layer faces {a:0.###} mm ({wa}); front {string.Join(" ",front)} back {string.Join(" ",back)}");
        }
        foreach(var kv in reasons)rows.AppendLine($"FUZZ REFUSED {kv.Value}x: {kv.Key}");rows.AppendLine($"FUZZ SUMMARY tried {tried} refused {refused} worst layer face {worstL:0.###} mm ({wl}); worst other face {worstS:0.###} mm ({ws})");
    }
    /// <summary>The solids the game builds from a mesh JSON (front polygon, native back points, sides), coloured by what each face is (layer: blue to brown by rank, side wall: grey, other: green).</summary>
    private static List<SolidTriangle> NativeTriangles(string json)
    {
        var ranks=LayerRanks.FromJson(json);var tris=new List<SolidTriangle>();var faces=NativeBuild(json);
        for(int i=0;i<faces.Count;i++)
        {
            var f=faces[i];int k=f.Front.Length;int rank=i<ranks.Length?ranks[i]:-1;
            float share=rank>=0?Math.Clamp(rank/11f,0,1):0;
            var (cr,cg,cb)=rank>=0?((byte)(70+share*110),(byte)(90-share*20),(byte)(150-share*110)):rank==-2?((byte)150,(byte)150,(byte)156):((byte)60,(byte)120,(byte)70);
            void Tri(Vector3 a,Vector3 b,Vector3 c)
            {
                var n=Vector3.Cross(b-a,c-a);if(n.LengthSquared()<1e-18f)return;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                tris.Add(new(a,b,c,n,Raster.Pack((byte)(cr*l),(byte)(cg*l),(byte)(cb*l),255)));
            }
            for(int j=1;j+1<k;j++){Tri(f.Front[0],f.Front[j],f.Front[j+1]);Tri(f.Back[0],f.Back[j+1],f.Back[j]);}
            for(int j=0;j<k;j++){int q=(j+1)%k;Tri(f.Front[j],f.Front[q],f.Back[q]);Tri(f.Front[j],f.Back[q],f.Back[j]);}
        }
        return tris;
    }
    /// <summary>The forward axis of the designer stays +Z; the beam is turned instead.</summary>
    internal static void RunLayerShape(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Layer shape: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot();Directory.CreateDirectory(output);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var t90=new CassetteModel(presets.First(p=>p.Id=="t90"),materials,reactions);
        double Density(string id)=>materials[id].Density;var forward=Vector3.UnitZ;
        var cases=new List<(string Tag,string Json)>
        {
            ("box 1000 x 800 x 803",BoxJson(Corners(1,.8f,.803f,0))),("box turned 30",BoxJson(Corners(1,.8f,.803f,30))),("box 300",BoxJson(Corners(1,.8f,.3f,0))),
            ("wedge",WedgeJson()),
        };
        foreach(var turn in new[]{0f,30f,-30f,90f})
            foreach(var (name,zs) in new[]{("kink back",new[]{0f,.15f,0f}),("kink forward",new[]{.15f,0f,.15f}),("kink asymmetric",new[]{0f,.30f,.05f}),("kink steep",new[]{0f,.9f,.1f})})
                foreach(var perp in new[]{false,true})
                    foreach(var mm in new[]{20,46})cases.Add(($"beam {name}{(perp?" (offset along normals)":"")} turned {turn} walls {mm} mm",BeamJson(new[]{-1.2f,.1f,.6f},zs,.4f,.35f,turn,perp,mm).Json));
        foreach(var turn in new[]{0f,15f,-15f,90f})foreach(var mm in new[]{5,854})
        {
            // Three pieces like the user's box before the turret: a short piece left and behind, a long straight piece, a short piece on the right (14 faces).
            cases.Add(($"three pieces, turned {turn}, faces {mm} mm",ChainJson(new[]{new Vector2(-1.0f,-.3f),new Vector2(-.6f,-.1f),new Vector2(.6f,-.1f),new Vector2(1.0f,-.35f)},new[]{new Vector2(-1.0f,-.7f),new Vector2(-.6f,-.5f),new Vector2(.6f,-.5f),new Vector2(1.0f,-.75f)},.3f,turn,mm)));
        }
        var rows=new StringBuilder("model\tstatus\tlayers\tfaces\tauto corners\tworst native point outside mm\twhere\n");
        var refusedForLayers=new List<string>();
        foreach(var (tag,json) in cases)
        {
            LayerConversion? r=null;string refused="";
            try{r=LayerFaces.Convert(json,t90,null,Density,forward);}catch(InvalidOperationException ex){refused=ex.Message;}
            if(r==null)
            {
                // 0.22.63: only the layers themselves may be a reason to refuse: a box too thin for 3 mm layers, or a layer that would lie along its own rib; the player can make the box deeper. Everything else must convert.
                bool layerLimit=refused.StartsWith("This box is too thin for layers")||refused.Contains("would lie along its rib");
                Check(layerLimit,$"{tag}: refused for a reason of the layers themselves, in plain words: {refused}");refusedForLayers.Add(tag);
                rows.AppendLine($"{tag}	refused					{refused}");continue;
            }
            var (mmL,whereL)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (mmS,whereS)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank<0);
            rows.AppendLine($"DETAIL {tag}: layer faces worst {mmL:0.###} mm ({whereL}); other faces worst {mmS:0.###} mm ({whereS})");
            var (mm,where)=ShapeDeviation(json,r.Plan.Json);var native=NativeBuild(r.Plan.Json);int auto=native.Sum(f=>f.Tm.Count(m=>m!=0&&m!=CheekShellFill.AlongEdgeManual));
            Check(mmL<=1.0,$"{tag}: no layer point the game builds lies more than 1 mm outside the old shape (worst {mmL:0.###} mm at {whereL}; {r.Faces.Length} faces)");
            Check(mmS<=r.Faces.Where(f=>f.Rank==LayerHit.SideWallRank).Select(f=>(double)f.Thickness.Max()).DefaultIfEmpty(5.0).Max()+.5,$"{tag}: no side wall point sticks out of the old shape by more than the wall thickness (worst {mmS:0.###} mm at {whereS}); {r.Box?.SideWallsKept} walls kept, {r.Box?.SideWallsDropped} left out");
            rows.AppendLine($"WALLS {tag}: kept {r.Box?.SideWallsKept} whole {r.Box?.SideWallsWhole} left out {r.Box?.SideWallsDropped}");
            rows.AppendLine(string.Join("\t",tag,"converted",r.Layout.Groups.Length,r.Faces.Length,auto,mm.ToString("0.###",CultureInfo.InvariantCulture),where));
        }
        // The user's own box (T-84 Oplot-M, plate 348): the converted mesh that 0.22.61 made, and the box rebuilt from its 12 original points.
        if(File.Exists(UserTank))
        {
            var before=UserBoxBefore();
            foreach(var fwd in new[]{new Vector3(0,.407f,.914f),new Vector3(0,-.407f,.914f),Vector3.UnitZ})
            {
                LayerConversion? r=null;string refused="";
                try{r=LayerFaces.Convert(before,t90,null,Density,fwd);}catch(InvalidOperationException ex){refused=ex.Message;}
                if(r==null){Check(false,$"user box, forward {fwd}: must be converted, but was refused: {refused}");rows.AppendLine($"user box forward {fwd}\trefused\t\t\t\t\t{refused}");continue;}
                var (mmNew,whereNew)=ShapeDeviation(before,r.Plan.Json,i=>r.Faces[i].Rank>=0);
                Check(mmNew<=1,$"user box, forward {fwd}: converted, {r.Faces.Length} faces, worst native point {mmNew:0.###} mm outside ({whereNew}); box={r.Box!=null}");
                rows.AppendLine($"user box forward {fwd}\tconverted\t{r.Layout.Groups.Length}\t{r.Faces.Length}\t\t{mmNew:0.###}\t{whereNew}");
                if(fwd==Vector3.UnitZ)
                {
                    Check(r.Faces.Where(f=>f.Rank==LayerHit.SideWallRank).All(f=>f.Thickness.All(t=>t<=5&&t>=3))&&r.Inspection.Contains("Side walls are 5 mm thick"),"user box: every side wall strip is at most the 5 mm of the setting (3 mm minimum) and the window says so");
                    double Kg(string j,LayerConversion c){var m=LayerMesh.Read(j);var rk=LayerRanks.FromJson(j);var w=LayerWeight.Of(m,rk,c.Layout,t90.Preset,Density,t90.Passive.Density);return w.Density*w.VolumeM3;}
                    double kgNew=Kg(r.Plan.Json,r);
                    rows.AppendLine($"USER BOX WEIGHT managed (layers at their density, walls as steel): this build {kgNew:0.#} kg (0.22.61 gave 720.2 kg for this box)");
                    var (copy,owners)=SyntheticOverlay(r,true);var mesh=LayerMesh.Read(r.Plan.Json);var ranks=LayerRanks.FromJson(r.Plan.Json);
                    var sw=System.Diagnostics.Stopwatch.StartNew();var mapped=LayerOverlay.Remap(copy,mesh,ranks,r.Layout);sw.Stop();
                    var sw2=System.Diagnostics.Stopwatch.StartNew();
                    for(int t=0;t+2<copy.Triangles.Length;t+=3){var c0=(copy.Vertices[copy.Triangles[t]]+copy.Vertices[copy.Triangles[t+1]]+copy.Vertices[copy.Triangles[t+2]])/3;LayerOverlay.Owner(mesh,c0,2);}
                    sw2.Stop();
                    rows.AppendLine($"OVERLAY TIME synthetic copy of the user box ({copy.Triangles.Length/3} triangles): Remap {sw.ElapsedMilliseconds} ms; the owner search of 0.22.61 alone (every face for every triangle) {sw2.ElapsedMilliseconds} ms; triangles in {copy.Triangles.Length/3}, out {mapped.Triangles.Length/3}");
                    Check(mapped.Triangles.Length==copy.Triangles.Length,"user box overlay: as many triangles out as in (zero-area triangles for the air), so the native coverage check passes");
                }
                if(fwd==Vector3.UnitZ)
                {
                    var preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
                    foreach(var (stem,js) in new[]{("before",before),("after",r.Plan.Json)})
                    {
                        var tris=NativeTriangles(js);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);
                        var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                        foreach(var (view,dir) in new[]{("oblique",new Vector3(-.55f,.6f,1)),("top",new Vector3(0,1,.001f))})
                            File.WriteAllBytes(Path.Combine(preview,$"shape-{stem}-{view}.png"),Render(tris,new Camera(bounds,dir)));
                    }
                }
            }
        }
        ShapeFuzz(t90,Density,rows,Check);
        rows.AppendLine($"REFUSED FOR THE LAYERS THEMSELVES {refusedForLayers.Count} of {cases.Count}: "+string.Join("; ",refusedForLayers));
        File.WriteAllText(Path.Combine(output,"SHAPE-MODELS.tsv"),rows.ToString());
        Console.WriteLine($"FEAT62 PASS: {checks} checks, {failures} failures");
    }
}
