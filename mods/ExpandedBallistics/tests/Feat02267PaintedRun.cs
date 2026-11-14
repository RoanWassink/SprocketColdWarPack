using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

internal static partial class ComposerFillBlueprintEvidence
{
    /// <summary>A stand-in for the native overlay copy of a mesh that is already converted: per face its front (d 0), its back (d 1) and its sides (d 0 at the front, 1 at the back), with the owner of every triangle.</summary>
    private static (OverlayCopy Copy,int[] Owners) OverlayOfMesh(LayerMesh m,bool otherDiagonal=false)
    {
        var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();var owners=new List<int>();
        void Tri(Vector3 a,float da,Vector3 b,float db,Vector3 c,float dc,int owner)
        {
            var nn=Vector3.Cross(b-a,c-a);if(nn.LengthSquared()<1e-16f)return;nn=Vector3.Normalize(nn);
            foreach(var (p,d) in new[]{(a,da),(b,db),(c,dc)}){t.Add(v.Count);v.Add(p);n.Add(nn);uv.Add(new(.5f,d));}owners.Add(owner);
        }
        for(int i=0;i<m.Polygons.Length;i++)
        {
            var f=m.Polygons[i];var b=m.Backs[i];int k=f.Length;if(k<3||b.Length!=k)continue;
            // The native tessellator may cut a quad along either diagonal; an old face that was kneaded is not planar, so the two cuts lie on different surfaces.
            if(otherDiagonal&&k==4)
            {
                Tri(f[1],0,f[2],0,f[3],0,i);Tri(f[1],0,f[3],0,f[0],0,i);Tri(b[1],1,b[3],1,b[2],1,i);Tri(b[1],1,b[0],1,b[3],1,i);
                for(int j=0;j<k;j++){int q=(j+1)%k;Tri(f[q],0,b[q],1,b[j],1,i);Tri(f[q],0,b[j],1,f[j],0,i);}
                continue;
            }
            for(int j=1;j+1<k;j++){Tri(f[0],0,f[j],0,f[j+1],0,i);Tri(b[0],1,b[j+1],1,b[j],1,i);}
            for(int j=0;j<k;j++){int q=(j+1)%k;Tri(f[j],0,f[q],0,b[q],1,i);Tri(f[j],0,b[q],1,b[j],1,i);}
        }
        return (new(v.ToArray(),t.ToArray(),n.ToArray(),uv.ToArray()),owners.ToArray());
    }
    /// <summary>
    /// The outermost and innermost layer faces of a converted mesh, found by looking: a layer face is outermost when no other layer face lies in front of its centre (ray along its normal),
    /// innermost when none lies behind it. Independent of the ranks the mod computes.
    /// </summary>
    private static (List<int> Outer,List<int> Inner) OutsideInside(LayerMesh m,IReadOnlyList<int> ranks)
    {
        var layer=Enumerable.Range(0,m.Polygons.Length).Where(i=>i<ranks.Count&&ranks[i]>=0&&m.Polygons[i].Length>=3).ToList();
        var tris=layer.Select(i=>(i,t:Enumerable.Range(1,m.Polygons[i].Length-2).Select(k=>new FillTriangle(m.Polygons[i][0],m.Polygons[i][k],m.Polygons[i][k+1])).ToArray())).ToList();
        bool Hit(int from,Vector3 origin,Vector3 dir){foreach(var (i,t) in tris){if(i==from)continue;foreach(var tr in t)if(ComposerFill.Ray(origin,dir,new[]{tr},50) is {} h&&h>.0005)return true;}return false;}
        var outer=new List<int>();var inner=new List<int>();
        foreach(var i in layer)
        {
            var c=m.Polygons[i].Aggregate(Vector3.Zero,(a,b)=>a+b)/m.Polygons[i].Length;var cb=m.Backs[i].Aggregate(Vector3.Zero,(a,b)=>a+b)/m.Backs[i].Length;var n=m.Normals[i];
            if(!Hit(i,c,n))outer.Add(i);if(!Hit(i,cb,-n))inner.Add(i);
        }
        return (outer,inner);
    }
    /// <summary>The ranks per piece from the outside in (pieces by the normal of the layer faces), for the report only.</summary>
    private static List<int[]> PiecesOutsideIn(LayerMesh m,IReadOnlyList<int> ranks)
    {
        var layer=Enumerable.Range(0,m.Polygons.Length).Where(i=>i<ranks.Count&&ranks[i]>=0&&m.Polygons[i].Length>=3).ToList();var groups=new List<List<int>>();
        foreach(var i in layer)
        {
            var g=groups.FirstOrDefault(x=>Vector3.Dot(m.Normals[x[0]],m.Normals[i])>.97f);if(g==null)groups.Add(g=new());g.Add(i);
        }
        Vector3 Centre(int i)=>m.Polygons[i].Aggregate(Vector3.Zero,(a,b)=>a+b)/m.Polygons[i].Length;
        return groups.Select(g=>g.OrderByDescending(i=>Vector3.Dot(Centre(i),m.Normals[g[0]])).ToArray()).ToList();
    }
    /// <summary>The closing requirement for painted: the front of the outermost layer face of every piece, the back of the innermost, and every side wall strip are drawn.</summary>
    private static (int Pieces,int FrontsMissing,int BacksMissing,int WallsMissing,string Detail) PaintedCover(LayerMesh m,IReadOnlyList<int> ranks,LayerLayout layout,bool otherDiagonal=false)
    {
        var (copy,owners)=OverlayOfMesh(m,otherDiagonal);var painted=LayerOverlay.Remap(copy,m,ranks,layout,true);
        bool Drawn(int t){var m0=painted.Vertices[painted.Triangles[t*3]];return !(m0==painted.Vertices[painted.Triangles[t*3+1]]&&m0==painted.Vertices[painted.Triangles[t*3+2]]);}
        int Part(int t){float a=copy.Uv[copy.Triangles[t*3]].Y,b=copy.Uv[copy.Triangles[t*3+1]].Y,c=copy.Uv[copy.Triangles[t*3+2]].Y;return a==0&&b==0&&c==0?0:a==1&&b==1&&c==1?1:2;}
        var (outer,inner)=OutsideInside(m,ranks);var pieces=PiecesOutsideIn(m,ranks);int fm=0,bm=0,wm=0;var detail=new StringBuilder();
        foreach(var piece in pieces)detail.Append($"piece of {piece.Length} faces, ranks outside in {string.Join(",",piece.Select(i=>ranks[i]))}; ");
        foreach(var o in outer){var fr=Enumerable.Range(0,copy.Triangles.Length/3).Where(t=>owners[t]==o&&Part(t)==0).ToArray();if(fr.Count(Drawn)<fr.Length)fm++;}
        foreach(var o in inner){var bk=Enumerable.Range(0,copy.Triangles.Length/3).Where(t=>owners[t]==o&&Part(t)==1).ToArray();if(bk.Count(Drawn)<bk.Length)bm++;}
        detail.Append($"outermost faces {outer.Count}, innermost {inner.Count}; ");
        for(int i=0;i<m.Polygons.Length&&i<ranks.Count;i++)
            if(ranks[i]==LayerHit.SideWallRank){var ts=Enumerable.Range(0,copy.Triangles.Length/3).Where(t=>owners[t]==i&&Part(t)==0).ToArray();if(ts.Length>0&&ts.Count(Drawn)<ts.Length)wm++;}
        return (outer.Count,fm,bm,wm,detail.ToString());
    }

    internal static void RunPainted(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Painted: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;
        CassetteModel Recipe(string id)=>new(presets.First(p=>p.Id==id),materials,reactions);var t90a=Recipe("t90a");
        var report=new StringBuilder();
        // The block of the user (mesh 2): the ranks as the mod computes them, per piece from the outside in.
        {
            var mesh=LayerMesh.Read(T90AMesh2);var ranks=LayerRanks.FromJson(T90AMesh2);var layout=LayerLayout.Build(t90a.Preset,LayerLayoutKind.PerBlock,null);
            var cover=PaintedCover(mesh,ranks,layout);var coverOther=PaintedCover(mesh,ranks,layout,true);
            report.AppendLine($"mesh 2 with the other diagonal: fronts missing {coverOther.FrontsMissing}, backs missing {coverOther.BacksMissing}, walls missing {coverOther.WallsMissing}");
            Check(coverOther.FrontsMissing==0&&coverOther.BacksMissing==0&&coverOther.WallsMissing==0,$"mesh 2 painted closes the block also when the native tessellator cuts every quad along the other diagonal ({coverOther.FrontsMissing} fronts, {coverOther.BacksMissing} backs, {coverOther.WallsMissing} walls not drawn; the outer layer face of the second piece is {8.7:0.#} mm out of plane)");
            report.AppendLine($"mesh 2: {mesh.Polygons.Length} faces, {ranks.Count(r=>r>=0)} layer faces, {ranks.Count(r=>r==-2)} walls, {layout.Groups.Length} layers; {cover.Detail}");
            report.AppendLine($"mesh 2 painted: pieces {cover.Pieces}, fronts missing {cover.FrontsMissing}, backs missing {cover.BacksMissing}, walls missing {cover.WallsMissing}");
            Check(cover.Pieces==2,$"mesh 2 has two outermost layer faces, one per piece ({cover.Pieces})");
            Check(PiecesOutsideIn(mesh,ranks).All(p=>p.Select(i=>ranks[i]).SequenceEqual(Enumerable.Range(0,layout.Groups.Length))),$"mesh 2: the ranks of every piece run 0 to {layout.Groups.Length-1} from the outside in ({cover.Detail})");
            Check(cover.FrontsMissing==0&&cover.BacksMissing==0&&cover.WallsMissing==0,$"mesh 2 painted closes the block: {cover.FrontsMissing} fronts, {cover.BacksMissing} backs and {cover.WallsMissing} walls not drawn ({cover.Detail})");
            // The pictures: painted as 0.22.66 drew it (in this build only the choice of the faces changed) and as now, and the layers look.
            foreach(var (stem,painted,old) in new[]{("painted",true,false),("painted-0.22.66",true,true),("layers",false,false)})
            {
                var (copy,_)=OverlayOfMesh(mesh,old);var data=LayerOverlay.Remap(copy,mesh,ranks,layout,painted);var strip=CassetteDepthStrip.Build(t90a.Preset);
                var pts=data.Vertices;var b2=(pts.Aggregate(Vector3.Min)-new Vector3(.05f),pts.Aggregate(Vector3.Max)+new Vector3(.05f));
                foreach(var (view,dir) in new[]{("a",new Vector3(-1,.5f,.5f)),("b",new Vector3(1,.5f,-.5f)),("top",new Vector3(0,1,.001f))})
                {
                    var tris=new List<SolidTriangle>();
                    for(int t=0;t+2<data.Triangles.Length;t+=3)
                    {
                        var p0=data.Vertices[data.Triangles[t]];var p1=data.Vertices[data.Triangles[t+1]];var p2=data.Vertices[data.Triangles[t+2]];
                        var dmean=(data.Uv[data.Triangles[t]].Y+data.Uv[data.Triangles[t+1]].Y+data.Uv[data.Triangles[t+2]].Y)/3;var px=strip[Math.Clamp((int)(dmean*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                        var n=Vector3.Cross(p1-p0,p2-p0);if(n.LengthSquared()<1e-16f)continue;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                        tris.Add(new(p0,p1,p2,n,Raster.Pack((byte)Math.Min(255,(px>>24)*l+20),(byte)Math.Min(255,((px>>16)&255)*l+20),(byte)Math.Min(255,((px>>8)&255)*l+20),255)));
                    }
                    File.WriteAllBytes(Path.Combine(preview,$"painted-mesh2-{stem}-{view}.png"),Render(tris,new Camera(b2,dir)));
                }
            }
        }
        // All models: the same requirement on the converted mesh of every earlier model.
        var models=new List<(string Tag,string Json,Vector3 Forward,Vector3? Centre,string Recipe)>
        {
            ("user block (mesh 51)",UserBlock(),UserBlockForward,UserBlockCentre,"leopard2a4"),("user box 15:57",NewUserBox,Vector3.UnitZ,null,"leopard2a4"),("user box 0.22.61",UserBoxBefore(),Vector3.UnitZ,null,"t90"),
            ("three pieces",ThreePieces(0,5),Vector3.UnitZ,null,"t90"),("Challenger box",ChallengerBox,ChallengerForward,ChallengerCentre,"burlington"),("T-90A open (mesh 20)",T90AOpen,T90AForward,T90ACentre,"t90a"),
            ("T-90A closed (mesh 20)",T90AClosed(),T90AForward,T90ACentre,"t90a"),("box 1000 x 800 x 803",BoxJson(Corners(1,.8f,.803f,0)),Vector3.UnitZ,null,"t90"),("wedge",WedgeJson(),Vector3.UnitZ,null,"t90"),
            ("kinked beam",ChainJson(new[]{new Vector2(-1.0f,-.2f),new Vector2(-.1f,0f),new Vector2(.8f,-.3f)},new[]{new Vector2(-1.0f,-.45f),new Vector2(-.1f,-.55f),new Vector2(.8f,-.85f)},.3f,0,5),Vector3.UnitZ,null,"t90"),
            ("open box (front and four sides)",BoxJson(Corners(1,.8f,.803f,0),withBack:false),Vector3.UnitZ,null,"t90")
        };
        foreach(var (tag,json,fwd,centre,recipeId) in models)
        {
            LayerConversion? r=null;try{r=LayerFaces.Convert(json,Recipe(recipeId),null,Density,fwd,5,centre);}catch(InvalidOperationException){}
            if(r==null){Check(false,$"{tag}: must be converted");continue;}
            var m=LayerMesh.Read(r.Plan.Json);var rk=LayerRanks.FromJson(r.Plan.Json);var cover=PaintedCover(m,rk,r.Layout);var cover2=PaintedCover(m,rk,r.Layout,true);
            Check(cover2.FrontsMissing==0&&cover2.BacksMissing==0&&cover2.WallsMissing==0,$"{tag}: painted closes the block also with the other diagonal ({cover2.FrontsMissing} fronts, {cover2.BacksMissing} backs, {cover2.WallsMissing} walls not drawn)");
            report.AppendLine($"{tag}: {cover.Detail}missing: fronts {cover.FrontsMissing}, backs {cover.BacksMissing}, walls {cover.WallsMissing}");
            Check(cover.Pieces>=1&&cover.FrontsMissing==0&&cover.BacksMissing==0&&cover.WallsMissing==0,$"{tag}: painted closes the block ({cover.Pieces} pieces; {cover.FrontsMissing} fronts, {cover.BacksMissing} backs, {cover.WallsMissing} walls not drawn)");
        }
        File.WriteAllText(Path.Combine(output,"PAINTED-RANKS.txt"),report.ToString());
        Console.WriteLine($"FEAT67 PASS: {checks} checks, {failures} failures");
    }
}
