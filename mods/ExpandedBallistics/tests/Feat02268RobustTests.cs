using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.68: the overlay painted must never leave a hole, whatever the game does with the quads. The copy of the game is imitated in many variants (both diagonals chosen per quad, points welded or
/// doubled per triangle, quads up to 20 mm out of plane, noise of 0.5 mm on every point, the sides of every thickened face present or absent, triangles in random order) on every model, and on the
/// plates of the T-90A save. For each variant: the front of the outermost layer face of every piece, the back of the innermost and every side wall are drawn and none is left out.
/// The 0.22.67 logic is repeated here (Painted67) only to report which variants it fails; that is no proof of what the game does (the dump of the overlay gives that).
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private sealed record CopyVariant(string Name,int Seed,bool RandomDiagonal,bool Weld,float OutOfPlaneMm,float NoiseMm,bool Sides,bool Shuffle);
    private static readonly CopyVariant[] CopyVariants=
    {
        new("plain",1,false,false,0,0,true,false),
        new("random diagonals",2,true,false,0,0,true,false),
        new("welded points",3,false,true,0,0,true,false),
        new("quads 20 mm out of plane",4,true,false,20,0,true,false),
        new("noise 0.5 mm",5,false,false,0,.5f,true,false),
        new("without sides",6,false,false,0,0,false,false),
        new("shuffled",7,false,false,0,0,true,true),
        new("everything: diagonals, welded, 20 mm, noise, sides absent, shuffled",8,true,true,20,.5f,false,true),
        new("everything with sides present",9,true,false,20,.5f,true,true),
        new("welded, noise, shuffled",10,false,true,0,.5f,true,true),
    };
    private static (OverlayCopy Copy,int[] Owner,int[] Part,LayerMesh Mesh) MakeCopy(LayerMesh m,CopyVariant v)
    {
        var rng=new Random(v.Seed);var polys=m.Polygons.Select(p=>p.ToArray()).ToArray();var backs=m.Backs.Select(p=>p.ToArray()).ToArray();
        if(v.OutOfPlaneMm>0)
            for(int i=0;i<polys.Length;i++)if(polys[i].Length==4&&backs[i].Length==4)
            {
                var n=m.Normals[i];int c=rng.Next(4);float off=(float)(rng.NextDouble()*2-1)*v.OutOfPlaneMm*.001f;polys[i][c]+=n*off;backs[i][c]+=n*off;
            }
        var mesh=m with{Polygons=polys,Backs=backs};
        var tris=new List<(Vector3 A,Vector3 B,Vector3 C,float Da,float Db,float Dc,int Owner,int Part)>();
        void Tri(Vector3 a,float da,Vector3 b,float db,Vector3 c,float dc,int owner,int part){if(Vector3.Cross(b-a,c-a).LengthSquared()<1e-16f)return;tris.Add((a,b,c,da,db,dc,owner,part));}
        for(int i=0;i<polys.Length;i++)
        {
            var f=polys[i];var b=backs[i];int k=f.Length;if(k<3||b.Length!=k)continue;bool other=v.RandomDiagonal&&k==4&&rng.Next(2)==1;
            if(other){Tri(f[1],0,f[2],0,f[3],0,i,0);Tri(f[1],0,f[3],0,f[0],0,i,0);Tri(b[1],1,b[3],1,b[2],1,i,1);Tri(b[1],1,b[0],1,b[3],1,i,1);}
            else for(int j=1;j+1<k;j++){Tri(f[0],0,f[j],0,f[j+1],0,i,0);Tri(b[0],1,b[j+1],1,b[j],1,i,1);}
            if(!v.Sides&&!(i<m.SideWalls.Length&&m.SideWalls[i]))continue; // sides of the thickened faces absent (the sides of a wall are 5 mm thin and stay)
            for(int j=0;j<k;j++){int q=(j+1)%k;if(other&&k==4){Tri(f[q],0,b[q],1,b[j],1,i,2);Tri(f[q],0,b[j],1,f[j],0,i,2);}else{Tri(f[j],0,f[q],0,b[q],1,i,2);Tri(f[j],0,b[q],1,b[j],1,i,2);}}
        }
        if(v.Shuffle)tris=tris.OrderBy(_=>rng.Next()).ToList();
        Vector3 Jit(Vector3 p)=>v.NoiseMm>0?p+new Vector3((float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1),(float)(rng.NextDouble()*2-1))*v.NoiseMm*.001f/MathF.Sqrt(3):p;
        var verts=new List<Vector3>();var norm=new List<Vector3>();var uv=new List<Vector2>();var idx=new List<int>();var owners=new List<int>();var parts=new List<int>();var weld=new Dictionary<(long,long,long,int),int>();
        int Add(Vector3 p,float d,Vector3 nn)
        {
            if(v.Weld){var key=((long)Math.Round(p.X*1e5),(long)Math.Round(p.Y*1e5),(long)Math.Round(p.Z*1e5),(int)Math.Round(d*1000));if(weld.TryGetValue(key,out var e))return e;weld[key]=verts.Count;}
            verts.Add(p);norm.Add(nn);uv.Add(new(.5f,d));return verts.Count-1;
        }
        foreach(var t in tris)
        {
            var nn=Vector3.Normalize(Vector3.Cross(t.B-t.A,t.C-t.A));
            idx.Add(Add(Jit(t.A),t.Da,nn));idx.Add(Add(Jit(t.B),t.Db,nn));idx.Add(Add(Jit(t.C),t.Dc,nn));owners.Add(t.Owner);parts.Add(t.Part);
        }
        return (new(verts.ToArray(),idx.ToArray(),norm.ToArray(),uv.ToArray()),owners.ToArray(),parts.ToArray(),mesh);
    }
    /// <summary>The logic of 0.22.67 for painted (owner by distance to the polygon, 0.6 mm), repeated to report which variants it fails.</summary>
    private static OverlayCopy Painted67(OverlayCopy copy,LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout)
    {
        var fractions=mesh.Fractions(ranks,layout);int first=int.MaxValue,last=-1;for(int i=0;i<ranks.Count;i++)if(ranks[i]>=0&&ranks[i]<layout.Groups.Length){first=Math.Min(first,ranks[i]);last=Math.Max(last,ranks[i]);}
        var walls=Enumerable.Range(0,Math.Min(ranks.Count,mesh.Polygons.Length)).Where(i=>ranks[i]==LayerHit.SideWallRank&&mesh.Polygons[i].Length>=3).ToArray();
        bool WallCovers(Vector3 p){foreach(var i in walls)if(LayerOverlay.Distance(p,mesh.Polygons[i])<LayerOverlay.ToleranceM)return true;return false;}
        var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int t=0;t+2<copy.Triangles.Length;t+=3)
        {
            int a=copy.Triangles[t],b=copy.Triangles[t+1],c=copy.Triangles[t+2];var pa=copy.Vertices[a];var centre=(pa+copy.Vertices[b]+copy.Vertices[c])/3;
            float da=copy.Uv[a].Y,db=copy.Uv[b].Y,dc=copy.Uv[c].Y;int part=da==0&&db==0&&dc==0?0:da==1&&db==1&&dc==1?1:2;
            int layerOwner=-1,wallOwner=-1;
            foreach(var cd in LayerOverlay.Owners(mesh,centre,part)){if(cd>=ranks.Count)continue;if(ranks[cd]==LayerHit.SideWallRank){if(wallOwner<0)wallOwner=cd;}else if(ranks[cd]>=0&&ranks[cd]<layout.Groups.Length&&layerOwner<0)layerOwner=cd;}
            bool keep=false;
            if(layerOwner>=0){int r=ranks[layerOwner];if(part==0&&r==first||part==1&&r==last||part==2&&!WallCovers(centre))keep=true;}
            if(!keep&&wallOwner>=0&&part==0)keep=true;
            if(!keep){for(int k=0;k<3;k++){triangles.Add(vertices.Count);vertices.Add(pa);normals.Add(copy.Normals[a]);uv.Add(copy.Uv[a]);}continue;}
            foreach(var i in new[]{a,b,c}){triangles.Add(vertices.Count);vertices.Add(copy.Vertices[i]);normals.Add(copy.Normals[i]);uv.Add(copy.Uv[i]);}
        }
        return new(vertices.ToArray(),triangles.ToArray(),normals.ToArray(),uv.ToArray());
    }
    private static bool IsCollapsed(OverlayCopy o,int t){var m0=o.Vertices[o.Triangles[t*3]];return m0==o.Vertices[o.Triangles[t*3+1]]&&m0==o.Vertices[o.Triangles[t*3+2]];}
    /// <summary>Counts of what is missing in the drawn overlay: triangles of the outermost fronts, the innermost backs and the wall fronts that were left out (looked at, not ranked).</summary>
    private static (int Fronts,int Backs,int Walls,int Total) HolesOf(LayerMesh original,IReadOnlyList<int> ranks,OverlayCopy copy,int[] owner,int[] part,OverlayCopy drawn)
    {
        var (outer,inner)=OutsideInside(original,ranks);var o=new HashSet<int>(outer);var n=new HashSet<int>(inner);int f=0,b=0,w=0,total=0;
        for(int t=0;t<owner.Length;t++)
        {
            bool collapsed=IsCollapsed(drawn,t);
            if(part[t]==0&&o.Contains(owner[t])){total++;if(collapsed)f++;}
            else if(part[t]==1&&n.Contains(owner[t])){total++;if(collapsed)b++;}
            else if(part[t]==0&&owner[t]<ranks.Count&&ranks[owner[t]]==LayerHit.SideWallRank){total++;if(collapsed)w++;}
        }
        return (f,b,w,total);
    }

    internal static void RunRobust(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Robust: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;CassetteModel Recipe(string id)=>new(presets.First(p=>p.Id==id),materials,reactions);
        var t90a=Recipe("t90a");var report=new StringBuilder("model\tvariant\ttriangles checked\t0.22.67 holes (fronts/backs/walls)\t0.22.68 holes\n");var fail67=new Dictionary<string,int>();
        var meshes=new List<(string Tag,string Json,CassetteModel Recipe)>
        {
            ("T-90A plate of 21:32 (mesh 2)",T90AMesh2132,t90a),("T-90A plate of 21:22 (mesh 2)",T90AMesh2,t90a),
        };
        var models=new List<(string Tag,string Json,Vector3 Forward,Vector3? Centre,string Recipe)>
        {
            ("user block (mesh 51)",UserBlock(),UserBlockForward,UserBlockCentre,"leopard2a4"),("user box 15:57",NewUserBox,Vector3.UnitZ,null,"leopard2a4"),("user box 0.22.61",UserBoxBefore(),Vector3.UnitZ,null,"t90"),
            ("three pieces",ThreePieces(0,5),Vector3.UnitZ,null,"t90"),("Challenger box",ChallengerBox,ChallengerForward,ChallengerCentre,"burlington"),("T-90A open (mesh 20)",T90AOpen,T90AForward,T90ACentre,"t90a"),
            ("T-90A closed (mesh 20)",T90AClosed(),T90AForward,T90ACentre,"t90a"),("box 1000 x 800 x 803",BoxJson(Corners(1,.8f,.803f,0)),Vector3.UnitZ,null,"t90"),("wedge",WedgeJson(),Vector3.UnitZ,null,"t90"),
            ("kinked beam",ChainJson(new[]{new Vector2(-1.0f,-.2f),new Vector2(-.1f,0f),new Vector2(.8f,-.3f)},new[]{new Vector2(-1.0f,-.45f),new Vector2(-.1f,-.55f),new Vector2(.8f,-.85f)},.3f,0,5),Vector3.UnitZ,null,"t90"),
            ("open box (front and four sides)",BoxJson(Corners(1,.8f,.803f,0),withBack:false),Vector3.UnitZ,null,"t90")
        };
        var cleanup=new StringBuilder("model\tpoints before\tpoints after\tedges before\tedges after\thelper points\tmerged points\n");
        foreach(var (tag,json,fwd,centre,rid) in models)
        {
            LayerConversion? r=null;try{r=LayerFaces.Convert(json,Recipe(rid),null,Density,fwd,5,centre);}catch(InvalidOperationException){}
            if(r==null){Check(false,$"{tag}: must be converted");continue;}
            meshes.Add((tag,r.Plan.Json,Recipe(rid)));
            var c=r.Cleanup!;cleanup.AppendLine($"{tag}\t{c.VerticesBefore}\t{c.VerticesAfter}\t{c.EdgesBefore}\t{c.EdgesAfter}\t{c.Helpers}\t{c.Merged}");
        }
        File.WriteAllText(Path.Combine(output,"CLEANUP-COUNTS.tsv"),cleanup.ToString());
        foreach(var (tag,json,recipe) in meshes)
        {
            var m=LayerMesh.Read(json);var ranks=LayerRanks.FromJson(json);var layout=LayerLayout.Build(recipe.Preset,json.Contains("Layer blocks")||m.Polygons.Length>0?LayerLayoutKind.PerBlock:LayerLayoutKind.PerLayer,null);
            // the layout of a converted model: the recipe's layers (blocks or one per layer) as the conversion chose
            int layerFaces=ranks.Count(x=>x>=0);int maxRank=ranks.Where(x=>x>=0).DefaultIfEmpty(0).Max();
            if(layout.Groups.Length<=maxRank)layout=LayerLayout.Build(recipe.Preset,LayerLayoutKind.PerLayer,null);
            foreach(var v in CopyVariants)
            {
                var (copy,owner,part,vm)=MakeCopy(m,v);
                var drawn=LayerOverlay.Remap(copy,vm,ranks,layout,true);var holes=HolesOf(m,ranks,copy,owner,part,drawn);
                var old=Painted67(copy,vm,ranks,layout);var holes67=HolesOf(m,ranks,copy,owner,part,old);
                if(holes67.Fronts+holes67.Backs+holes67.Walls>0)fail67[v.Name]=fail67.GetValueOrDefault(v.Name)+1;
                report.AppendLine($"{tag}\t{v.Name}\t{holes.Total}\t{holes67.Fronts}/{holes67.Backs}/{holes67.Walls}\t{holes.Fronts}/{holes.Backs}/{holes.Walls}");
                Check(holes.Fronts==0&&holes.Backs==0&&holes.Walls==0&&drawn.Triangles.Length==copy.Triangles.Length,$"{tag}, {v.Name}: no front, back or wall left out ({holes.Fronts} fronts, {holes.Backs} backs, {holes.Walls} walls of {holes.Total} triangles); {drawn.Triangles.Length/3} triangles out for {copy.Triangles.Length/3} in");
            }
        }
        report.AppendLine();foreach(var kv in fail67)report.AppendLine($"0.22.67 fails variant '{kv.Key}' on {kv.Value} models");
        File.WriteAllText(Path.Combine(output,"ROBUST-VARIANTS.tsv"),report.ToString());
        // The plate of 21:32: the numbers of the dump and the hint of the user (a loose point).
        {
            var m=LayerMesh.Read(T90AMesh2132);var ranks=LayerRanks.FromJson(T90AMesh2132);
            using var doc=System.Text.Json.JsonDocument.Parse(T90AMesh2132);var jroot=doc.RootElement;
            var edges=jroot.GetProperty("edges").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var verts=jroot.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();Vector3 P(int i)=>new(verts[3*i],verts[3*i+1],verts[3*i+2]);
            int ribs=0,fallbacks=0;double minAngle=90;var rows=new StringBuilder();
            foreach(var (f,fi) in jroot.GetProperty("faces").EnumerateArray().Select((f,i)=>(f,i)))
            {
                ulong tm=JsonBits.Tm(f.GetProperty("tm")),te=JsonBits.Te(f.GetProperty("te"));var ids=f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray();var pts=ids.Select(P).ToArray();var n=Vector3.Normalize(CheekShellFill.Newell(pts));
                for(int j=0;j<ids.Length&&j<4;j++)
                {
                    if(((tm>>(8*j))&0xff)!=CheekShellFill.AlongEdgeManual)continue;int e=(int)((te>>(16*j))&0xffff);int w=edges[2*e]==ids[j]?edges[2*e+1]:edges[2*e];var u=Vector3.Normalize(P(w)-pts[j]);float d=Vector3.Dot(n,u);
                    ribs++;if(d>-0.0174524f)fallbacks++;minAngle=Math.Min(minAngle,Math.Asin(Math.Clamp(-d,-1,1))*180/Math.PI);
                }
            }
            Check(ranks.Count(x=>x>=0)==26&&ranks.Count(x=>x==-2)==175&&PiecesOutsideIn(m,ranks).Count==2&&PiecesOutsideIn(m,ranks).All(pc=>pc.Select(i=>ranks[i]).SequenceEqual(Enumerable.Range(0,13))),$"plate of 21:32: 26 layer faces and 175 walls, the ranks of both pieces 0 to 12 from the outside in (layer faces {ranks.Count(x=>x>=0)}, walls {ranks.Count(x=>x==-2)}); the outermost layer of the right piece (face 113, 77 mm) has an edge (points 4-10) that runs along a helper rib (10-164, cos 0.99995) and was read as a wall by 0.22.67");
            Check(fallbacks==0&&minAngle>45,$"plate of 21:32: {ribs} corners use a thickness rib, none within 1 degree of the face (the native formula never falls back; smallest angle between rib and face {minAngle:0.#} degrees)");
            File.WriteAllText(Path.Combine(output,"RIBS-2132.txt"),$"corners with a rib: {ribs}; fallbacks: {fallbacks}; smallest angle rib to face: {minAngle:0.##} degrees\n");
            // The dump, as the mod writes it.
            var (copy,owner,part,vm)=MakeCopy(m,CopyVariants[0]);var layout=LayerLayout.Build(t90a.Preset,LayerLayoutKind.PerBlock,null);var rep=new LayerOverlay.OverlayReport();
            LayerOverlay.RemapPainted(copy,m,ranks,layout,rep);var text=LayerOverlayDump.Build(T90AMesh2132,m,ranks,layout,copy,rep,"painted",366,"ebCassette_t90a");
            File.WriteAllText(Path.Combine(output,"DUMP-SAMPLE-plate-366.json"),text);
            Check(text.Length<5_000_000&&text.Contains("\"corners\"")&&text.Contains("\"predictedBack\"")&&text.Contains("\"triangleRole\""),$"the dump of a plate in layers holds the faces with tm, te, rib, angle and predicted back points, and the triangles with their role ({text.Length/1024} kB for this plate)");
        }
        // The pictures: the plate of the user (21:32) in painted for three variants of the copy of the game.
        {
            var m=LayerMesh.Read(T90AMesh2132);var ranks=LayerRanks.FromJson(T90AMesh2132);var layout=LayerLayout.Build(t90a.Preset,LayerLayoutKind.PerBlock,null);var strip=CassetteDepthStrip.Build(t90a.Preset);
            foreach(var vi in new[]{0,3,7})
            {
                var (copy,_,_,vm)=MakeCopy(m,CopyVariants[vi]);var data=LayerOverlay.Remap(copy,vm,ranks,layout,true);
                var pts=data.Vertices;var b2=(pts.Aggregate(Vector3.Min)-new Vector3(.05f),pts.Aggregate(Vector3.Max)+new Vector3(.05f));
                foreach(var (view,dir) in new[]{("a",new Vector3(-1,.5f,.5f)),("top",new Vector3(0,1,.001f))})
                {
                    var tris=new List<SolidTriangle>();
                    for(int t=0;t+2<data.Triangles.Length;t+=3)
                    {
                        var p0=data.Vertices[data.Triangles[t]];var p1=data.Vertices[data.Triangles[t+1]];var p2=data.Vertices[data.Triangles[t+2]];
                        var dmean=(data.Uv[data.Triangles[t]].Y+data.Uv[data.Triangles[t+1]].Y+data.Uv[data.Triangles[t+2]].Y)/3;var px=strip[Math.Clamp((int)(dmean*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                        var n=Vector3.Cross(p1-p0,p2-p0);if(n.LengthSquared()<1e-16f)continue;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                        tris.Add(new(p0,p1,p2,n,Raster.Pack((byte)Math.Min(255,(px>>24)*l+20),(byte)Math.Min(255,((px>>16)&255)*l+20),(byte)Math.Min(255,((px>>8)&255)*l+20),255)));
                    }
                    File.WriteAllBytes(Path.Combine(preview,$"robust-{vi}-{view}.png"),Render(tris,new Camera(b2,dir)));
                }
            }
        }
        Console.WriteLine($"FEAT68 PASS: {checks} checks, {failures} failures");
    }
}
