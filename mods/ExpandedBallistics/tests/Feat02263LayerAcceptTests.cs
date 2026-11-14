using SprocketUiKit;
using System.Globalization;
using System.Numerics;
using System.Text;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.63: an ordinary kneaded box is converted. The new box of the user (T-84 Oplot-M, object 279, mesh 27: 10 faces, 12 points, 5 mm on Auto, 2868 x 250 x 974 mm)
/// is embedded below exactly as saved; the three-piece beam is rebuilt in code. Pictures: the beam before and after, and the overlay from the side without the side walls.
/// </summary>
internal static partial class ComposerFillBlueprintEvidence
{
    private const string NewUserBox="""{"majorVersion":0,"minorVersion":3,"vertices":[1.697,0.0,-0.599000037,1.697,0.0,-0.849000037,1.697,0.25,-0.599000037,1.697,0.25,-0.849000037,1.0250001,0.25,-0.125,1.0250001,0.0,-0.125,1.0250001,0.0,0.125,1.0250001,0.25,0.125,-1.171,0.25,-0.133,-1.171,0.25,0.117000006,-1.171,0.0,0.117000006,-1.171,0.0,-0.133],"edges":[5,1,1,0,7,2,2,3,0,2,1,3,3,4,11,5,0,6,9,7,5,4,5,6,6,7,7,4,4,8,6,10,9,8,10,9,11,10,11,8],"edgeFlags":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],"faces":[{"v":[5,1,0,6],"t":[5,5,5,5],"tm":16843009,"te":3377716900724746},{"v":[4,7,2,3],"t":[5,5,5,5],"tm":16843009,"te":1407392064208906},{"v":[6,0,2,7],"t":[5,5,5,5],"tm":16843009,"te":2533287675363343},{"v":[0,1,3,2],"t":[5,5,5,5],"tm":16843009,"te":562975723225096},{"v":[1,5,4,3],"t":[5,5,5,5],"tm":16843009,"te":844480765427713},{"v":[11,5,6,10],"t":[5,5,5,5],"tm":16843009,"te":4785126144344083},{"v":[10,6,7,9],"t":[5,5,5,5],"tm":16843009,"te":4503608217829394},{"v":[8,9,7,4],"t":[5,5,5,5],"tm":16843009,"te":2814801307828243},{"v":[5,11,8,4],"t":[5,5,5,5],"tm":16843009,"te":3659243417894923},{"v":[11,10,9,8],"t":[5,5,5,5],"tm":16843009,"te":3940688329637895}]}""";

    internal static void RunLayerAccept(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"Layer accept: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),preview=Path.Combine(output,"preview");Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        double Density(string id)=>materials[id].Density;
        var rows=new StringBuilder("model\tforward\tstatus\tlayers\tfaces\twalls kept\twalls whole\twalls left out\tworst layer point mm\tworst wall point mm\n");
        double worstLayer=0,worstWall=0;
        void Model(string tag,string json,Vector3 fwd,CassetteModel recipe)
        {
            LayerConversion? r=null;string refused="";
            try{r=LayerFaces.Convert(json,recipe,null,Density,fwd);}catch(InvalidOperationException ex){refused=ex.Message;}
            if(r==null){Check(false,$"{tag}: must be converted, but was refused: {refused}");rows.AppendLine($"{tag}\t{fwd}\trefused\t\t\t\t\t\t\t{refused}");return;}
            var (mmL,whereL)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank>=0);var (mmS,whereS)=ShapeDeviation(json,r.Plan.Json,i=>r.Faces[i].Rank<0);
            double wall=r.Faces.Where(f=>f.Rank==LayerHit.SideWallRank).Select(f=>(double)f.Thickness.Max()).DefaultIfEmpty(5.0).Max();
            worstLayer=Math.Max(worstLayer,mmL);worstWall=Math.Max(worstWall,mmS);
            Check(mmL<=1,$"{tag}, forward {fwd}: layer points within 1 mm of the old shape (worst {mmL:0.###} mm, {whereL}; {r.Faces.Length} faces)");
            Check(mmS<=wall+.5,$"{tag}, forward {fwd}: side wall points within their {wall:0} mm of the old shape (worst {mmS:0.###} mm, {whereS}); {r.Box?.SideWallsKept} walls kept, {r.Box?.SideWallsWhole} whole, {r.Box?.SideWallsDropped} left out");
            var box=r.Box!;
            Check((box.SideWallsDropped>0)==r.Inspection.Contains($"{box.SideWallsDropped} side walls could not be kept."),$"{tag}: the window counts the walls that were left out");
            rows.AppendLine(string.Join("\t",tag,fwd.ToString(),"converted",r.Layout.Groups.Length,r.Faces.Length,box.SideWallsKept,box.SideWallsWhole,box.SideWallsDropped,mmL.ToString("0.###",CultureInfo.InvariantCulture),mmS.ToString("0.###",CultureInfo.InvariantCulture)));
        }
        foreach(var (id,label) in new[]{("leopard2a4","Leopard 2A4 recipe"),("t90","T-90 recipe")})
        {
            var recipe=new CassetteModel(presets.First(p=>p.Id==id),materials,reactions);
            // The new box of the user: the designer's forward axis tilted a little about the plate's x axis (the plate is turned 10 degrees), both ways, and straight.
            foreach(var fwd in new[]{Vector3.Normalize(new Vector3(0,.17f,.985f)),Vector3.Normalize(new Vector3(0,-.17f,.985f)),Vector3.UnitZ})
                Model($"user box (object 279, {label})",NewUserBox,fwd,recipe);
            Model($"box of 0.22.61 (object 280, {label})",UserBoxBefore(),Vector3.UnitZ,recipe);
            foreach(var turn in new[]{0f,15f,-15f})foreach(var mm in new[]{5,854})
                Model($"three pieces, turned {turn}, faces {mm} mm ({label})",ThreePieces(turn,mm),Vector3.UnitZ,recipe);
        }
        File.WriteAllText(Path.Combine(output,"ACCEPT-MODELS.tsv"),rows.ToString());
        File.WriteAllText(Path.Combine(output,"THREE-PIECES.json"),ThreePieces(0,5));
        // ---------- pictures: the three-piece beam before and after (native back points), and the overlay from the side without the side walls ----------
        {
            var t90=new CassetteModel(presets.First(p=>p.Id=="t90"),materials,reactions);var strip=CassetteDepthStrip.Build(t90.Preset);
            var before=ThreePieces(0,5);var r=LayerFaces.Convert(before,t90,null,Density,Vector3.UnitZ);
            foreach(var (stem,js) in new[]{("before",before),("after",r.Plan.Json)})
            {
                var tris=NativeTriangles(js);var pts=tris.SelectMany(q=>new[]{q.A,q.B,q.C}).ToArray();var pad=new Vector3(.05f);var bounds=(pts.Aggregate(Vector3.Min)-pad,pts.Aggregate(Vector3.Max)+pad);
                foreach(var (view,dir) in new[]{("oblique",new Vector3(-.55f,.6f,1)),("top",new Vector3(0,1,.001f))})File.WriteAllBytes(Path.Combine(preview,$"accept-{stem}-{view}.png"),Render(tris,new Camera(bounds,dir)));
            }
            // The overlay: the synthetic native copy re-mapped; side walls are collapsed, so only the layers remain.
            var (copy,owners)=SyntheticOverlay(r,true);var mapped=LayerOverlay.Remap(copy,LayerMesh.Read(r.Plan.Json),LayerRanks.FromJson(r.Plan.Json),r.Layout);
            Check(mapped.Triangles.Length==copy.Triangles.Length,"overlay: as many triangles out as in (zero-area triangles for the side walls), so the native coverage check passes");
            // A wall triangle may stay only where a face that is not a wall lies at the same place (the end of a wall lies in the plane of the end of the layers); then it shows that layer.
            bool NearLayer(int tri)
            {
                var ia=copy.Triangles[tri*3];var ib=copy.Triangles[tri*3+1];var ic=copy.Triangles[tri*3+2];float a0=copy.Uv[ia].Y,b0=copy.Uv[ib].Y,c0=copy.Uv[ic].Y;int part=a0==0&&b0==0&&c0==0?0:a0==1&&b0==1&&c0==1?1:2;
                var centre=(copy.Vertices[ia]+copy.Vertices[ib]+copy.Vertices[ic])/3;
                for(int i=0;i<r.Faces.Length;i++)
                {
                    if(r.Faces[i].Rank==LayerHit.SideWallRank)continue;var fr=r.Faces[i].Front;var bk=r.Faces[i].Back;
                    if(part==0&&LayerOverlay.Distance(centre,fr)<LayerOverlay.ToleranceM)return true;if(part==1&&LayerOverlay.Distance(centre,bk)<LayerOverlay.ToleranceM)return true;
                    if(part==2)for(int j=0;j<fr.Length;j++)if(LayerOverlay.Distance(centre,new[]{fr[j],fr[(j+1)%fr.Length],bk[(j+1)%fr.Length],bk[j]})<LayerOverlay.ToleranceM)return true;
                }
                return false;
            }
            int visibleWall=0,lonelyWall=0,wallTris=0;
            for(int t=0;t+2<mapped.Triangles.Length;t+=3)
            {
                if(r.Faces[owners[t/3]].Rank!=LayerHit.SideWallRank)continue;wallTris++;
                var m0=mapped.Vertices[mapped.Triangles[t]];bool collapsed=m0==mapped.Vertices[mapped.Triangles[t+1]]&&m0==mapped.Vertices[mapped.Triangles[t+2]];
                if(!collapsed){visibleWall++;if(!NearLayer(t/3))lonelyWall++;}
                else if(NearLayer(t/3)){}
            }
            Check(wallTris>0&&lonelyWall==0,$"overlay: no side wall is drawn by itself ({wallTris} wall triangles, {visibleWall} stay only because a layer lies at the same place and shows that layer, {lonelyWall} drawn alone)");
            var allPts=mapped.Vertices;var b2=(allPts.Aggregate(Vector3.Min)-new Vector3(.05f),allPts.Aggregate(Vector3.Max)+new Vector3(.05f));
            foreach(var (view,dir) in new[]{("side",new Vector3(1,.001f,0)),("below",new Vector3(0,-1,.001f)),("above",new Vector3(0,1,.001f))})
            {
                var tris=new List<SolidTriangle>();
                for(int t=0;t+2<mapped.Triangles.Length;t+=3)
                {
                    var p0=mapped.Vertices[mapped.Triangles[t]];var p1=mapped.Vertices[mapped.Triangles[t+1]];var p2=mapped.Vertices[mapped.Triangles[t+2]];
                    var dmean=(mapped.Uv[mapped.Triangles[t]].Y+mapped.Uv[mapped.Triangles[t+1]].Y+mapped.Uv[mapped.Triangles[t+2]].Y)/3;var px=strip[Math.Clamp((int)(dmean*CassetteDepthStrip.Height),0,CassetteDepthStrip.Height-1)];
                    var n=Vector3.Cross(p1-p0,p2-p0);if(n.LengthSquared()<1e-16f)continue;n=Vector3.Normalize(n);float l=.6f+.4f*MathF.Abs(Vector3.Dot(n,Vector3.Normalize(new Vector3(.4f,.7f,.5f))));
                    tris.Add(new(p0,p1,p2,n,Raster.Pack((byte)Math.Min(255,(px>>24)*l+20),(byte)Math.Min(255,((px>>16)&255)*l+20),(byte)Math.Min(255,((px>>8)&255)*l+20),255)));
                }
                File.WriteAllBytes(Path.Combine(preview,$"accept-overlay-{view}.png"),Render(tris,new Camera(b2,dir)));
            }
        }
        Console.WriteLine($"FEAT63 PASS: {checks} checks, {failures} failures; worst layer point {worstLayer:0.###} mm, worst wall point {worstWall:0.###} mm");
    }
    /// <summary>The user's beam of three pieces: a short piece left and behind, a long straight piece, a short piece on the right (14 faces).</summary>
    private static string ThreePieces(float turn,int mm)=>ChainJson(new[]{new Vector2(-1.0f,-.3f),new Vector2(-.6f,-.1f),new Vector2(.6f,-.1f),new Vector2(1.0f,-.35f)},new[]{new Vector2(-1.0f,-.7f),new Vector2(-.6f,-.5f),new Vector2(.6f,-.5f),new Vector2(1.0f,-.75f)},.3f,turn,mm);
}
