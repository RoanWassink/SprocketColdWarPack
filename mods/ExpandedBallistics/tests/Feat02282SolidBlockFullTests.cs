using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.82 "Solid block" complete: the choice under Apply (A), the mark through Apply, Undo/Redo, save and load (B), the weight (C), the overlay (D: stopped, see the
/// report), the odd line (E: in 0.22.81, confirmed), the diag line only with Composite fill / Layer hit log apart from the failure exits (F), and what the code does with
/// fragments (G). Tests on the user's plates: Object 292 (17:21, mesh 22, two closed cheeks), Object 293 (20:38, two cheeks with a gap of 7 edges each; the dump of 21:14,
/// closed), the Challenger 1 proof box and the T-80U proof box. The production pure code runs as it is (SolidShape, SolidBlock, SolidBlockApply, SolidDiagGate, SolidMarks,
/// CompositePlate names, CassetteComposerModel); the IL2CPP glue (TrySolidBlock, Weigh, EnsureName, SolidCavities) is replayed statement for statement or checked in the source.
/// The game's surfaces of a shot line are simulated from the saved mesh (front faces and the back faces of the mod's own thickness model, LayerMesh); the independent
/// path is a separate ray-polygon code on the drawn faces only.
/// </summary>
internal static class Feat02282SolidBlockFullTests
{
    /// <summary>The production files 0.22.82 may change on top of 0.22.81.</summary>
    internal static bool IsPlanned(string rel)=>rel is "CompositePlate.cs" or "RuntimeComposerFill.cs" or "RuntimeCompositePlateEdit.cs" or "CassetteComposerModel.cs" or "CassetteComposerPanel.cs" or "RuntimeGroupedProtection.cs" or "Plugin.cs" or "SprocketExpandedBallistics.csproj" or "RELEASE-NOTES.md"||Feat02283SolidOverlayTests.IsPlanned(rel); // 0.22.83: its planned files count too
    /// <summary>RuntimeComposerFill.cs without the 0.22.82 additions (one line in Weigh and the region of the solid weight), for the ledger checks of older versions.</summary>
    internal static string Without82Fill(string s)
    {
        const string begin="    // ---- 0.22.82 solid weight (begin) ----",end="    // ---- 0.22.82 solid weight (end) ----";
        var lines=Feat02283SolidOverlayTests.Without83Fill(s).Split("\r\n").ToList(); // 0.22.83: its additions first
        int b=lines.IndexOf(begin),e=lines.IndexOf(end);
        if(b>=0&&e>b)lines.RemoveRange(b,e-b+1);
        return string.Join("\r\n",lines.Where(l=>!l.EndsWith("// 0.22.82 solid",StringComparison.Ordinal)));
    }
    private static string F(double v,string f="0.###")=>v.ToString(f,CultureInfo.InvariantCulture);
    private static string Hex(uint c)=>"#"+(c>>8).ToString("x6",CultureInfo.InvariantCulture);

    // ---------------- blueprint and frames ----------------
    private static (string Json,string? Name) Mesh(JsonElement root,int vuid)
    {
        var m=root.GetProperty("meshes").EnumerateArray().First(x=>x.GetProperty("vuid").GetInt32()==vuid).GetProperty("meshData");
        return (m.GetProperty("mesh").GetRawText(),m.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString():null);
    }
    private static float[] Euler(float x,float y,float z)
    {
        float rx=x*MathF.PI/180,ry=y*MathF.PI/180,rz=z*MathF.PI/180;float cx=MathF.Cos(rx),sx=MathF.Sin(rx),cy=MathF.Cos(ry),sy=MathF.Sin(ry),cz=MathF.Cos(rz),sz=MathF.Sin(rz);
        return MM(MM(new[]{cy,0,sy,0,1,0,-sy,0,cy},new[]{1,0,0,0,cx,-sx,0,sx,cx}),new[]{cz,-sz,0,sz,cz,0,0,0,1});
    }
    private static float[] MM(float[] a,float[] b){var r=new float[9];for(int i=0;i<3;i++)for(int j=0;j<3;j++){float s=0;for(int k=0;k<3;k++)s+=a[3*i+k]*b[3*k+j];r[3*i+j]=s;}return r;}
    private static Vector3 Mul(float[] m,Vector3 v)=>new(m[0]*v.X+m[1]*v.Y+m[2]*v.Z,m[3]*v.X+m[4]*v.Y+m[5]*v.Z,m[6]*v.X+m[7]*v.Y+m[8]*v.Z);
    private static float[] Transpose(float[] m)=>new[]{m[0],m[3],m[6],m[1],m[4],m[7],m[2],m[5],m[8]};
    private static (float[] R,Vector3 T) World(Dictionary<int,JsonElement> objects,JsonElement o)
    {
        var ts=o.GetProperty("transform");var rot=ts.GetProperty("rot");var pos=ts.GetProperty("pos");
        var r=Euler(rot[0].GetSingle(),rot[1].GetSingle(),rot[2].GetSingle());var p=new Vector3(pos[0].GetSingle(),pos[1].GetSingle(),pos[2].GetSingle());
        int parent=o.GetProperty("pvuid").GetInt32();if(parent<0||!objects.TryGetValue(parent,out var po))return (r,p);
        var (pr,pt)=World(objects,po);return (MM(pr,r),Mul(pr,p)+pt);
    }
    private static (Vector3[] Points,int[][] Faces) Read(string json)
    {
        using var d=JsonDocument.Parse(json);var v=d.RootElement.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var p=new Vector3[v.Length/3];for(int i=0;i<p.Length;i++)p[i]=new(v[3*i],v[3*i+1],v[3*i+2]);
        return (p,d.RootElement.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray());
    }

    // ---------------- the independent path: a ray through the drawn faces only (each face as its fan triangles 0-j-j+1, the triangulation of the volume; ray-triangle test) ----------------
    // The drawn quads of Object 292 are warped by up to 183 mm, so a quad is no plane: the surface is the one of its fan triangles (the other diagonal is reported apart).
    private static List<(double T,int Face)> CrossDrawn(Vector3[] p,int[][] faces,Vector3 o,Vector3 d)
    {
        var hits=new List<(double T,int Face)>();
        for(int f=0;f<faces.Length;f++)
        {
            var v=faces[f];
            for(int j=1;j+1<v.Length;j++)if(Tri(o,d,p[v[0]],p[v[j]],p[v[j+1]]) is {} t){hits.Add((t,f));break;}
        }
        hits.Sort((a,b)=>a.T.CompareTo(b.T));
        var kept=new List<(double T,int Face)>();foreach(var h in hits)if(kept.Count==0||h.T-kept[^1].T>1e-7)kept.Add(h);
        return kept;
    }
    private static double InsideDrawn(List<(double T,int Face)> hits){double s=0;for(int i=0;i+1<hits.Count;i+=2)s+=hits[i+1].T-hits[i].T;return s;}

    // ---------------- the simulated surfaces of the game: front and back of every face (LayerMesh: the mod's own model of the native thickness) ----------------
    private static List<(double T,Vector3 N)> CrossSkins(LayerMesh m,Vector3 o,Vector3 d)
    {
        var hits=new List<(double T,Vector3 N)>();
        void Poly(Vector3[] poly,Vector3 n)
        {
            for(int j=1;j+1<poly.Length;j++)if(Tri(o,d,poly[0],poly[j],poly[j+1]) is {} t)hits.Add((t,n));
        }
        // every face as a closed slab: its front, its back and a side wall along each of its edges (assumption: the game builds each face as a solid of its own; only the game shows how it joins them)
        for(int i=0;i<m.Polygons.Length;i++)
        {
            var f=m.Polygons[i];if(f.Length<3)continue;Poly(f,m.Normals[i]);var b=m.Backs[i];if(b.Length!=f.Length)continue;Poly(b,m.Normals[i]);
            for(int j=0;j<f.Length;j++){int k=(j+1)%f.Length;var side=new[]{f[j],f[k],b[k],b[j]};var n=Vector3.Cross(f[k]-f[j],b[j]-f[j]);Poly(side,n.LengthSquared()>0?Vector3.Normalize(n):m.Normals[i]);}
        }
        hits.Sort((a,b)=>a.T.CompareTo(b.T));return hits;
    }
    private static double? Tri(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
    {
        double e1x=b.X-a.X,e1y=b.Y-a.Y,e1z=b.Z-a.Z,e2x=c.X-a.X,e2y=c.Y-a.Y,e2z=c.Z-a.Z;
        double px=d.Y*e2z-d.Z*e2y,py=d.Z*e2x-d.X*e2z,pz=d.X*e2y-d.Y*e2x;double det=e1x*px+e1y*py+e1z*pz;if(Math.Abs(det)<1e-14)return null;
        double inv=1/det,tx=o.X-a.X,ty=o.Y-a.Y,tz=o.Z-a.Z;double u=(tx*px+ty*py+tz*pz)*inv;if(u<0||u>1)return null;
        double qx=ty*e1z-tz*e1y,qy=tz*e1x-tx*e1z,qz=tx*e1y-ty*e1x;double v=(d.X*qx+d.Y*qy+d.Z*qz)*inv;if(v<0||u+v>1)return null;
        double t=(e2x*qx+e2y*qy+e2z*qz)*inv;return t>0?t:null;
    }
    private sealed record LineResult(double DrawnInsideMm,double Inside,double Cavity,double Skins,double CavityRha,double SkinRha,double Angle,double Factor,int Surfaces,int Passages,bool Odd,int OddLines,double DrawnRha);
    /// <summary>One shot line through a plate marked [Solid block] as the production pure code pays it: SolidBlock.Analyse on the surfaces, then per skin (the block of that skin) SolidBlock.Plan on the ledger of the ray, committed; the skins at the table factor of their entry angle; SolidLineSeen for the odd line (E).</summary>
    private static LineResult Shoot(LayerMesh skins,Vector3[] p,int[][] faces,Vector3 o,Vector3 d,ProtectionFactorTable table,ProtectionThreat threat,string material)
    {
        var hits=CrossSkins(skins,o,d);var drawn=CrossDrawn(p,faces,o,d);
        double Angle(double t){var h=hits.OrderBy(x=>Math.Abs(x.T-t)).First();var c=Math.Abs(Vector3.Dot(Vector3.Normalize(h.N),d));return Math.Acos(Math.Clamp(c,0,1))*180/Math.PI;}
        double DrawnAngle(){var v=faces[drawn[0].Face];var n=Vector3.Zero;for(int j=0;j<v.Length;j++)n+=Vector3.Cross(p[v[j]],p[v[(j+1)%v.Length]]);return Math.Acos(Math.Clamp(Math.Abs(Vector3.Dot(Vector3.Normalize(n),d)),0,1))*180/Math.PI;}
        var line=SolidBlock.Analyse(hits.Select(h=>h.T));var ledger=new MaterialIntervalLedger();var seen=new SolidLineSeen();seen.See(hits.Select(h=>h.T));
        double cavity=0,cavityRha=0,skinMm=0,skinRha=0;int odd=0;
        foreach(var skin in line.Skins)
        {
            foreach(var c in SolidBlock.Plan(line,skin.StartM,skin.EndM,ledger,22,material,table.Label,ps=>Angle(ps.Front.StartM),a=>table.Factor(threat,a))){ledger.Commit(c.Plan);seen.Paid(c.Passage);cavity+=c.CavityMm;cavityRha+=c.RhaMm;}
            skinMm+=skin.LengthMm;skinRha+=skin.LengthMm*table.Factor(threat,Angle(skin.StartM));
        }
        // E: the block of every skin lists its own surfaces; on a line that is even as a whole no odd line may be written (a false "odd" line); an odd whole line keeps its true line
        if(!line.Odd)foreach(var skin in line.Skins){var own=hits.Select(h=>h.T).Where(t=>t>=skin.StartM-1e-9&&t<=skin.EndM+1e-9).ToList();if(SolidBlock.Analyse(own).Odd&&seen.OddLine(own)!=null)odd++;}
        double inside=line.Passages.Sum(x=>x.Inside.LengthMm),angle=line.Passages.Length>0?Angle(line.Passages[0].Front.StartM):double.NaN;
        double drawnInside=InsideDrawn(drawn)*1000,drawnAngle=drawn.Count>0?DrawnAngle():double.NaN;
        return new(drawnInside,inside,cavity,skinMm,cavityRha,skinRha,angle,double.IsFinite(angle)?table.Factor(threat,angle):double.NaN,hits.Count,line.Passages.Length,line.Odd,odd,double.IsFinite(drawnAngle)?drawnInside*table.Factor(threat,drawnAngle):0);
    }

    /// <summary>The same drawn shape with every corner at <paramref name="mm"/> (as Thickness only would set it): only "t" changes.</summary>
    private static string Thin(string json,ushort mm)
    {
        var n=JsonNode.Parse(json)!;
        foreach(var f in n["faces"]!.AsArray()){var t=f!["t"]!.AsArray();for(int i=0;i<t.Count;i++)t[i]=mm;}
        return n.ToJsonString();
    }
    private static int[] Pieces(int[][] fcs)
    {
        var parentOf=Enumerable.Range(0,fcs.Length).ToArray();int Find(int i){while(parentOf[i]!=i)i=parentOf[i]=parentOf[parentOf[i]];return i;}
        var owner=new Dictionary<(int,int),int>();
        for(int f=0;f<fcs.Length;f++)for(int j=0;j<fcs[f].Length;j++){int a=fcs[f][j],b=fcs[f][(j+1)%fcs[f].Length];var k=(Math.Min(a,b),Math.Max(a,b));if(owner.TryGetValue(k,out var g))parentOf[Find(f)]=Find(g);else owner[k]=f;}
        var ids=new Dictionary<int,int>();var piece=new int[fcs.Length];for(int f=0;f<fcs.Length;f++){var r=Find(f);if(!ids.ContainsKey(r))ids[r]=ids.Count;piece[f]=ids[r];}
        return piece;
    }
    private sealed record LineStats(int Lines,int Within,int Unpaired,int Odd,double MinDiff,double MaxDiff,double PaidRha,double DrawnRha);
    /// <summary>Two cheeks x three heights (25, 50, 75 % of the cheek) x straight, +30 and -30 degrees about the vertical, in the vehicle frame (the plate's world transform from the blueprint).</summary>
    private static LineStats Lines(string label,string json,Dictionary<int,JsonElement> objects,int objectVuid,ProtectionFactorTable table,string material,StringBuilder tsv,double skinMm)
    {
        var (rot,pos)=World(objects,objects[objectVuid]);var inv=Transpose(rot);
        var (pts,fcs)=Read(json);var skins=LayerMesh.Read(json);var world=pts.Select(q=>Mul(rot,q)+pos).ToArray();var piece=Pieces(fcs);
        int lines=0,within=0,unpaired=0,odd=0;double minDiff=double.PositiveInfinity,maxDiff=double.NegativeInfinity,paid=0,drawn=0;
        for(int c=0;c<=piece.Max();c++)
        {
            var cp=fcs.Where((_,f)=>piece[f]==c).SelectMany(f=>f).Distinct().Select(i=>world[i]).ToArray();var lo=cp.Aggregate(Vector3.Min);var hi=cp.Aggregate(Vector3.Max);var mid=(lo+hi)/2;
            foreach(var h in new[]{.25f,.5f,.75f})foreach(var yaw in new[]{0f,30f,-30f})
            {
                var dirW=new Vector3(-MathF.Sin(yaw*MathF.PI/180),0,-MathF.Cos(yaw*MathF.PI/180));var originW=new Vector3(mid.X+.0037f,lo.Y+(hi.Y-lo.Y)*h+.0029f,mid.Z)-dirW*3; // a few mm off the centre: a line exactly along a seam of two faces is the known fallback (skins only), shown in the frontal view
                var r=Shoot(skins,pts,fcs,Mul(inv,originW-pos),Vector3.Normalize(Mul(inv,dirW)),table,ProtectionThreat.LongRod,material);
                double total=r.CavityRha+r.SkinRha,diff=r.Inside-r.DrawnInsideMm;
                tsv.AppendLine(string.Join("\t",label,"cheek "+(c+1),F(h*100,"0")+" %",F(yaw,"0"),r.Surfaces,r.Passages,F(r.Angle,"0.#"),F(r.Factor,"0.####"),F(r.DrawnInsideMm,"0.#"),F(r.Inside,"0.#"),F(r.Cavity,"0.#"),F(r.Skins,"0.#"),F(r.CavityRha,"0.#"),F(r.SkinRha,"0.#"),F(total,"0.#"),F(r.DrawnRha,"0.#"),r.OddLines));
                if(!(r.DrawnInsideMm>0))continue;
                lines++;odd+=r.OddLines;
                if(r.Passages==0||r.Odd){unpaired++;continue;}
                paid+=total;drawn+=r.DrawnRha;
                minDiff=Math.Min(minDiff,diff);maxDiff=Math.Max(maxDiff,diff);
                double allowance=double.IsFinite(skinMm)?2*skinMm/Math.Max(.2,Math.Cos(r.Angle*Math.PI/180))+1:1;
                if(diff>=-.5&&diff<=allowance)within++;
            }
        }
        return new(lines,within,unpaired,odd,minDiff,maxDiff,paid,drawn);
    }
    /// <summary>The frontal view in the vehicle frame: 20 mm cells, one straight shot each, paid as the production code pays a solid block (cavity x factor + skins).</summary>
    private static Panel Heat(string title,string what,string json,Dictionary<int,JsonElement> objects,int objectVuid,ProtectionFactorTable table,string material)
    {
        var (rot,pos)=World(objects,objects[objectVuid]);var inv=Transpose(rot);
        var (pts,fcs)=Read(json);var skins=LayerMesh.Read(json);var world=pts.Select(q=>Mul(rot,q)+pos).ToArray();
        var vlo=world.Aggregate(Vector3.Min);var vhi=world.Aggregate(Vector3.Max);
        const double cell=.02;int nx=(int)Math.Ceiling((vhi.X-vlo.X)/cell),ny=(int)Math.Ceiling((vhi.Y-vlo.Y)/cell);
        var values=new double[nx,ny];int hitCells=0,paired=0;double sum=0;const double top=700;
        for(int i=0;i<nx;i++)for(int j=0;j<ny;j++)
        {
            var o=new Vector3((float)(vlo.X+(i+.437)*cell),(float)(vlo.Y+(j+.461)*cell),vhi.Z+1);
            var r=Shoot(skins,pts,fcs,Mul(inv,o-pos),Vector3.Normalize(Mul(inv,-Vector3.UnitZ)),table,ProtectionThreat.LongRod,material);
            var v=r.CavityRha+r.SkinRha;values[i,j]=r.Surfaces>0?(r.Passages>0&&!r.Odd?v:-1):double.NaN;if(r.Surfaces>0){hitCells++;sum+=v;if(r.Passages>0&&!r.Odd)paired++;}
        }
        var hp=new Panel{title=title,explanation=$"{what} Each 20 mm cell is one straight 120 mm APFSDS line (long rod, the user's recipe); {paired} of {hitCells} cells pair their skins (grey: not paired, skins only); mean {F(sum/Math.Max(1,hitCells),"0")} mm RHA-eq.",width=980,height=500};
        hp.Rect(0,0,980,500,"#0d1115");
        double scale=Math.Min(900/((vhi.X-vlo.X)*1000),420/((vhi.Y-vlo.Y)*1000));double X(double x)=>40+(vhi.X-x)*1000*scale;double top0=20+(420-(vhi.Y-vlo.Y)*1000*scale)/2;double Y(double y)=>top0+(vhi.Y-y)*1000*scale; // seen from the front: the vehicle's left is on the right
        for(int i=0;i<nx;i++)for(int j=0;j<ny;j++){var v=values[i,j];if(double.IsNaN(v))continue;double x0=vlo.X+i*cell,y0=vlo.Y+j*cell;hp.Poly(new[]{(X(x0),Y(y0)),(X(x0+cell),Y(y0)),(X(x0+cell),Y(y0+cell)),(X(x0),Y(y0+cell))},v<0?"#5a5a5a":Ramp(v/top));}
        foreach(var f in fcs)for(int j=0;j<f.Length;j++){var a=world[f[j]];var b=world[f[(j+1)%f.Length]];hp.Line(X(a.X),Y(a.Y),X(b.X),Y(b.Y),"#ffffff38",.6);}
        for(int k=0;k<=10;k++){double t=k/10.0;hp.Rect(40+k*40,462,40,14,Ramp(t));if(k%2==0)hp.Text(40+k*40,486,F(t*top,"0"),"#c9d1d8","MiddleLeft",null,8);}
        hp.Rect(500,462,40,14,"#5a5a5a");hp.Text(546,469,"skins only (not paired)   |   scale: mm RHA-eq along the line","#c9d1d8");
        return hp;
    }

    // ---------------- the [Solid block] diag line: the glue of SolidCavities replayed (scan, diag, Guard, Allow) with the production gate ----------------
    private static string? DiagShot(SolidDiagGate gate,int entity,SolidMarkAnswer answer,bool verbose,List<double> surfaces,ProtectionFactorTable table,double budget,ISet<int> failed,List<string> logs,Func<double,double>? factorAt=null,double fromM=double.NegativeInfinity)
    {
        if(answer!=SolidMarkAnswer.Marked&&!gate.Wants(entity,verbose,answer))return null; // the scan: not collected
        var diag=gate.Wants(entity,verbose,answer)?new SolidDiag{Shot=gate.Id,Entity=entity,Name=answer==SolidMarkAnswer.Unknown?null:CompositePlate.SolidBlockName,Marked=answer==SolidMarkAnswer.Unknown?null:answer==SolidMarkAnswer.Marked,Source=answer==SolidMarkAnswer.Unknown?"unknown: no build entry":"Build",Surfaces=surfaces.Count}:null;
        if(answer==SolidMarkAnswer.Marked)
            SolidBlock.Guard(entity,failed,logs.Add,()=>
            {
                var line=SolidBlock.Analyse(surfaces);double s=Math.Max(surfaces.Min(),fromM),e=line.Skins.Length>0?line.Skins[0].EndM:s;
                if(fromM>double.NegativeInfinity){s=fromM;e=fromM+.001;}
                diag?.Line(line,s,e);
                if(line.Odd){if(diag!=null)diag.Exit="odd";return;}
                var charges=SolidBlock.Plan(line,s,e,new MaterialIntervalLedger(),entity,"ebCassette_t80u",table.Label,_=>{if(diag!=null)diag.Angle=0;return 0;},a=>{var f=(factorAt??(x=>table.Factor(ProtectionThreat.LongRod,x)))(a);if(diag!=null)diag.Factor=f;return f;});
                if(diag!=null){diag.Charges=charges.Count;diag.Exit=charges.Count==0?"notDue":"applyPassiveFalse";}
                foreach(var c in charges)
                {
                    var target=CassetteImpactPath.NativePassive(budget,c.RhaMm);
                    if(!(c.RhaMm>0)||!(budget>0)||!(target<budget)){diag?.Applied(c,false,budget>0?budget:0,0);continue;}
                    diag?.Applied(c,true,budget,target);budget=target;
                }
            });
        else if(diag!=null)diag.Exit=answer==SolidMarkAnswer.Unmarked?"unmarked":"markUnknown";
        return diag!=null&&gate.Allow(entity,diag.Exit,verbose)?diag.Text():null;
    }

    // ---------------- pictures (drawn by preview/Make-Solid-Block-Sheet.ps1) ----------------
    private sealed class Panel
    {
        public string title="",explanation="";public int width,height;public List<Dictionary<string,object?>> ops=new();
        public void Poly(IEnumerable<(double X,double Y)> xy,string fill,string? stroke=null,double strokePx=0)=>ops.Add(new(){["op"]="poly",["xy"]=xy.SelectMany(p=>new[]{Math.Round(p.X,2),Math.Round(p.Y,2)}).ToArray(),["fill"]=fill,["stroke"]=stroke,["strokePx"]=strokePx});
        public void Line(double x0,double y0,double x1,double y1,string colour,double w)=>ops.Add(new(){["op"]="line",["x0"]=Math.Round(x0,2),["y0"]=Math.Round(y0,2),["x1"]=Math.Round(x1,2),["y1"]=Math.Round(y1,2),["colour"]=colour,["widthPx"]=w});
        public void Text(double x,double y,string text,string colour="#e8e2d4",string anchor="MiddleLeft",string? background=null,double size=9)=>ops.Add(new(){["op"]="text",["x"]=Math.Round(x,1),["y"]=Math.Round(y,1),["text"]=text,["colour"]=colour,["anchor"]=anchor,["background"]=background,["size"]=size});
        public void Rect(double x,double y,double w,double h,string fill,string? stroke=null,double px=0)=>Poly(new[]{(x,y),(x+w,y),(x+w,y+h),(x,y+h)},fill,stroke,px);
        public double Wrapped(double x,double y,string text,int chars,string colour="#e8e2d4",double size=9,double step=14)
        {
            foreach(var para in text.Split('\n'))
            {
                var line=new StringBuilder();
                foreach(var word in para.Split(' '))
                {
                    if(line.Length>0&&line.Length+1+word.Length>chars){Text(x,y,line.ToString(),colour,"MiddleLeft",null,size);y+=step;line.Clear();}
                    if(line.Length>0)line.Append(' ');line.Append(word);
                }
                if(line.Length>0){Text(x,y,line.ToString(),colour,"MiddleLeft",null,size);y+=step;}
            }
            return y;
        }
    }
    private static string Ramp(double t)
    {
        t=Math.Clamp(t,0,1);var stops=new[]{(0.0,0x2b,0x1a,0x4f),(0.25,0x3b,0x52,0x8b),(0.5,0x21,0x91,0x8c),(0.75,0x5e,0xc9,0x62),(1.0,0xfd,0xe7,0x25)};
        for(int i=1;i<stops.Length;i++)if(t<=stops[i].Item1)
        {
            var a=stops[i-1];var b=stops[i];double u=(t-a.Item1)/(b.Item1-a.Item1);
            return "#"+((int)Math.Round(a.Item2+(b.Item2-a.Item2)*u)).ToString("x2")+((int)Math.Round(a.Item3+(b.Item3-a.Item3)*u)).ToString("x2")+((int)Math.Round(a.Item4+(b.Item4-a.Item4)*u)).ToString("x2");
        }
        return "#fde725";
    }

    internal static void Run(Action<bool,string> sink)
    {
        int checks=0,failures=0;void Check(bool ok,string message){checks++;if(!ok)failures++;sink(ok,"FEAT82: "+message);}
        string root=Fix02228Tests.SourceRoot(),output=Fix02228Tests.OutputRoot(),parent=Fix02228Tests.SessionRoot(),preview=Path.Combine(output,"preview");
        Directory.CreateDirectory(preview);
        var materials=CassetteTechnology.ReadDirectory(Path.Combine(AppContext.BaseDirectory,"NativeData"));
        var reactions=MaterialReactionCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,MaterialReactionCatalogue.FileName)));
        var presets=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,CassetteCatalogue.FileName))).Presets;
        var userPreset=CassetteCatalogue.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/object292-recipe.cassette.json"))).Presets.Single();
        CassetteModel Model(CassettePreset p)=>new(p,materials,reactions);
        var userModel=Model(userPreset);var burlington=Model(presets.First(p=>p.Id=="burlington"));var t80u=Model(presets.First(p=>p.Id=="t80u"));
        var userTable=ProtectionFactorTable.Build(userModel);
        var cases=new StringBuilder("case\tresult\tpieces\tvolume m3 (per piece)\tfaces turned\topen edges\tgaps\tclosest other point to an open edge mm\ttolerance gap\tmessage\n");
        var lines=new StringBuilder("plate\tcheek\theight\tyaw deg\tsurfaces on the line\tpassages\tangle deg\tfactor\tdrawn inside mm (independent)\tinside mm (solid path)\tcavity mm\tskins mm\tcavity mm RHA-eq\tskins mm RHA-eq\tpath x factor mm RHA-eq (solid path)\tdrawn inside x factor mm RHA-eq (independent)\todd lines\n");
        var weights=new StringBuilder("plate\tpiece\tvolume m3\trecipe\tdensity kg/m3 (incl. air)\tmass kg\tnote\n");
        var panels=new List<Panel>();
        try
        {
            using var bp292=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")));
            using var bp293=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-2038.blueprint")));
            using var dump293=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-293-follow-wall-plate-324.json")));
            using var challenger=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof - Challenger 1 Mk3.blueprint")));
            using var proof77=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/solid-block/Solid block proof.blueprint")));
            var (json292,name292)=Mesh(bp292.RootElement,22);var (json293,_)=Mesh(bp293.RootElement,2);var dumpJson=dump293.RootElement.GetProperty("mesh").GetRawText();
            var (boxJson,boxName)=Mesh(challenger.RootElement,4);

            // ================= A. Apply "Solid block": the shape =================
            var s292=SolidShape.FromJson(json292);
            cases.AppendLine(string.Join("\t","Object 292 mesh 22 (17:21)",s292.Closed?"closed":"refused",s292.Pieces,string.Join(" + ",s292.PieceVolumesM3.Select(v=>F(v,"0.######"))),s292.ReversedFaces,s292.OpenEdges,s292.Gaps.Length,"-","-",s292.Refusal??""));
            Check(s292.Closed&&s292.Pieces==2&&s292.PieceFaces.SequenceEqual(new[]{88,88})&&s292.PieceVolumesM3.All(v=>Math.Abs(v-.18)<.02),$"Object 292 mesh 22: two closed cheeks of 88 faces, {string.Join(" and ",s292.PieceVolumesM3.Select(v=>F(v,"0.#####")))} m³ (expected about 0.18 m³ each); {s292.ReversedFaces} faces turned for the volume");
            Check(Math.Abs(s292.PieceVolumesM3[0]-s292.PieceVolumesM3[1])<.01*s292.PieceVolumesM3[0],$"the two cheeks are mirror images; their stored quads start at another corner, so the fan diagonal of the warped quads differs: volumes within 1 % ({F(s292.PieceVolumesM3[0],"0.########")} and {F(s292.PieceVolumesM3[1],"0.########")} m³)");
            {
                // the drawn quads are warped (up to 183 mm out of plane): the volume with the other diagonal of every quad, as the uncertainty of the triangulation
                var (pts,fcs)=Read(json292);var other=SolidShape.Analyse(pts,fcs.Select(f=>f.Length==4?new[]{f[1],f[2],f[3],f[0]}:f).ToArray());
                double warp=0;foreach(var f in fcs.Where(f=>f.Length==4)){var n=Vector3.Normalize(Vector3.Cross(pts[f[1]]-pts[f[0]],pts[f[2]]-pts[f[0]]));warp=Math.Max(warp,Math.Abs(Vector3.Dot(pts[f[3]]-pts[f[0]],n))*1000);}
                cases.AppendLine(string.Join("\t","Object 292 mesh 22, the other diagonal of every quad",other.Closed?"closed":"refused",other.Pieces,string.Join(" + ",other.PieceVolumesM3.Select(v=>F(v,"0.######"))),other.ReversedFaces,0,0,"-","-",$"largest warp of a quad {F(warp,"0.#")} mm; volume difference {F(100*(other.VolumeM3-s292.VolumeM3)/s292.VolumeM3,"0.##")} %"));
                Check(other.Closed&&Math.Abs(other.VolumeM3-s292.VolumeM3)<.03*s292.VolumeM3,$"the triangulation matters little: with the other diagonal of every (warped, up to {F(warp,"0")} mm) quad the volume is {F(other.VolumeM3,"0.#####")} m³ ({F(100*(other.VolumeM3-s292.VolumeM3)/s292.VolumeM3,"0.##")} %)");
            }
            var snap292=SolidShape.FromSnapshot(ComposerFill.Read(json292,true));
            Check(snap292.Closed&&snap292.PieceVolumesM3.Zip(s292.PieceVolumesM3).All(z=>Math.Abs(z.First-z.Second)<1e-12)&&snap292.ReversedFaces==s292.ReversedFaces,"the composer note (gated snapshot) and Apply (the saved mesh) give the same pieces and volumes");
            // independent volume: the inside length of a grid of rays through the drawn faces only (no SolidShape code), 2 mm cells
            {
                var (pts,fcs)=Read(json292);var lo=new Vector3(float.MaxValue);var hi=new Vector3(float.MinValue);foreach(var q in pts){lo=Vector3.Min(lo,q);hi=Vector3.Max(hi,q);}
                double cell=.002,sum=0;for(double x=lo.X+cell*.5137;x<hi.X;x+=cell)for(double y=lo.Y+cell*.4731;y<hi.Y;y+=cell)sum+=InsideDrawn(CrossDrawn(pts,fcs,new((float)x,(float)y,lo.Z-1),Vector3.UnitZ))*cell*cell;
                Check(Math.Abs(sum-s292.VolumeM3)<.005*s292.VolumeM3,$"independent check: rays through the drawn faces on a 2 mm grid give {F(sum,"0.#####")} m³ for both cheeks, SolidShape {F(s292.VolumeM3,"0.#####")} m³ (difference {F(100*(sum-s292.VolumeM3)/s292.VolumeM3,"0.##")} %)");
            }
            var dump=SolidShape.FromJson(dumpJson);
            cases.AppendLine(string.Join("\t","Object 293 dump 21:14 (follow-wall-plate-324)",dump.Closed?"closed":"refused",dump.Pieces,string.Join(" + ",dump.PieceVolumesM3.Select(v=>F(v,"0.######"))),dump.ReversedFaces,dump.OpenEdges,dump.Gaps.Length,"-","-",dump.Refusal??""));
            Check(dump.Closed&&dump.Pieces==2&&dump.PieceVolumesM3.All(v=>v>.3&&v<.4),$"Object 293, the closed dump mesh of 21:14: Apply succeeds: 2 closed pieces, {string.Join(" and ",dump.PieceVolumesM3.Select(v=>F(v,"0.#####")))} m³");
            var open=SolidShape.FromJson(json293);
            cases.AppendLine(string.Join("\t","Object 293 mesh of 20:38",open.Closed?"closed":"refused",open.Pieces,"-",open.ReversedFaces,open.OpenEdges,open.Gaps.Length,F(open.ClosestOpenPointMm,"0.###"),open.ToleranceGap,open.Refusal??""));
            Check(!open.Closed&&open.OpenEdges==14&&open.Gaps.Length==2&&open.Gaps.All(g=>g.Edges==7)&&open.Gaps[0].Piece!=open.Gaps[1].Piece,$"Object 293 of 20:38: refused; two gaps of 7 open edges, one in each cheek ({open.OpenEdges} open edges)");
            Check(open.Refusal!.StartsWith(SolidShape.NotClosed+" 14 open edges: a gap of 7 edges at faces ",StringComparison.Ordinal)&&open.Refusal.Contains(" mm); a gap of 7 edges at faces ",StringComparison.Ordinal)&&open.Gaps.All(g=>open.Refusal.Contains("faces "+string.Join(", ",g.Faces),StringComparison.Ordinal)),"the refusal gives the reason and the advice, and names each gap: its edges, its faces and where it lies: "+open.Refusal);
            Check(!open.ToleranceGap&&open.ClosestOpenPointMm>=.1,$"the gap of Object 293 is not a matter of tolerance: the nearest other point to a point of an open edge lies {F(open.ClosestOpenPointMm,"0.###")} mm away (nothing is closed silently)");
            Check(SolidShape.FromSnapshot(ComposerFill.Read(json293,true)).Refusal==open.Refusal,"the composer note shows the same refusal before Apply");
            // synthetic shapes: a tolerance gap (two points 0.05 mm apart), an open box, an inner wall, a face with a hole is refused by the snapshot reader
            {
                var cube=Read(boxJson);var pts=cube.Points.ToList();int dup=pts.Count;pts.Add(pts[0]+new Vector3(.00005f,0,0));
                var fcs=cube.Faces.Select(f=>f.ToArray()).ToArray();int at=Array.FindIndex(fcs,f=>f.Contains(0));fcs[at]=fcs[at].Select(i=>i==0?dup:i).ToArray();
                var tol=SolidShape.Analyse(pts,fcs);
                cases.AppendLine(string.Join("\t","Challenger box with one corner split 0.05 mm",tol.Closed?"closed":"refused",tol.Pieces,"-",tol.ReversedFaces,tol.OpenEdges,tol.Gaps.Length,F(tol.ClosestOpenPointMm,"0.###"),tol.ToleranceGap,tol.Refusal??""));
                Check(!tol.Closed&&tol.ToleranceGap&&Math.Abs(tol.ClosestOpenPointMm-.05)<.002&&tol.Refusal!.Contains("lie 0.05 mm apart (less than 0.1 mm): merge them in the designer",StringComparison.Ordinal),"a corner split by 0.05 mm is refused and reported as a tolerance gap with its size: "+tol.Refusal);
                var five=SolidShape.Analyse(cube.Points,cube.Faces.Take(5).ToArray());
                Check(!five.Closed&&five.OpenEdges==4&&five.Gaps.Length==1,"an open box (5 of 6 faces) is refused: one gap of 4 edges");
                var wall=SolidShape.Analyse(cube.Points,cube.Faces.Append(new[]{0,1,7,6}).ToArray());
                Check(!wall.Closed&&wall.Refusal!.Contains("more than two faces",StringComparison.Ordinal)||!wall.Closed&&wall.OpenEdges>0,"a wall inside the box is refused: "+wall.Refusal);
                var flipped=cube.Faces.Select((f,i)=>i%2==0?f.Reverse().ToArray():f).ToArray();var turned=SolidShape.Analyse(cube.Points,flipped);
                Check(turned.Closed&&Math.Abs(turned.VolumeM3-.264)<1e-6&&turned.ReversedFaces==3,$"faces that wind the other way are turned for the volume: 3 of 6 faces turned, {F(turned.VolumeM3,"0.######")} m³ = 0.8 x 0.55 x 0.6");
            }

            // ================= B. the mark: names, Apply, Undo/Redo, a second Apply, Thickness only and Layers, save and load =================
            {
                Check(CompositePlate.NameForMode(ThicknessMode.SolidBlock)==CompositePlate.SolidBlockName&&CompositePlate.ModeFromName(CompositePlate.SolidBlockName)==ThicknessMode.SolidBlock&&CompositePlate.IsSolidBlock(CompositePlate.NameForMode(ThicknessMode.SolidBlock)),"Solid block is a mode of its own: NameForMode and ModeFromName know it (the mark of 0.22.77, the same text)");
                Check(CompositePlate.ModeFromName(CompositePlate.SolidBlockName+" [FW75:abc]")==ThicknessMode.SolidBlock&&CompositePlate.LayoutFromName(CompositePlate.SolidBlockName)==null&&!CompositePlate.IsName(CompositePlate.SolidBlockName),"a saved-front suffix is ignored; Solid block is no layout of layers; the identity of the plate stays the recipe (IsName unchanged)");
                foreach(var mode in new[]{ThicknessMode.Uniform,ThicknessMode.ToBackPlane,ThicknessMode.FillToWall,ThicknessMode.Layers,ThicknessMode.LayerBlocks})
                    Check(CompositePlate.ModeFromName(CompositePlate.NameForMode(mode))==mode,$"{mode}: name and mode unchanged");
                // EnsureName (RuntimeCompositePlate.cs, IL2CPP) names a mesh NameForMode(mode ?? ModeFromName(existing)); the replica is checked against the source below
                string Ensure(string? existing,ThicknessMode? mode)=>CompositePlate.NameForMode(mode??CompositePlate.ModeFromName(existing));
                var source=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlate.cs"));
                Check(source.Contains("var wanted=CompositePlate.NameForMode(mode??CompositePlate.ModeFromName(existing));",StringComparison.Ordinal)&&source.Contains("blueprint.Name=CompositePlate.NameForMode(mode);",StringComparison.Ordinal),"RuntimeCompositePlate.EnsureName and NameMesh name a mesh exactly as the replica (and are byte-identical to 0.22.81)");
                var edit=File.ReadAllText(Path.Combine(root,"RuntimeCompositePlateEdit.cs"));
                Check(edit.Contains("var ok=Run(editor,materialId,null,ThicknessMode.SolidBlock,wasComposite,SolidBlockApply.Label,\"\",out message);",StringComparison.Ordinal)&&System.Text.RegularExpressions.Regex.Matches(edit,@"creator\.CreateOp\(").Count==1,"TrySolidBlock runs the one existing CreateOp step (Run) with the mode Solid block and no depth: one Undo step, the copy keeps every point and thickness (the checks of Change)");
                Check(edit.Contains("preparedName=prepared?.Plan.MeshName??(CompositePlate.ModeFromName(originalName)==mode?originalName:null);",StringComparison.Ordinal)&&edit.Contains("try{component.blueprintRef.Load(oldId);Apply(oldMesh.Mesh,snapshot.OldMaterial,snapshot.NameOnRevert);Applied=false;}",StringComparison.Ordinal),"Execute names the copy by the mode of the step; Undo names the old mesh by its own name (thickness mode null)");
                // the thicknesses of the step: no depth, so every face keeps its loops (the shape stays as drawn)
                var before=ComposerFill.Read(json292,true);var plan=CompositeThicknessPlan.Create("ebCassette_x",before.ThicknessList(),null,null);
                Check(plan.Thicknesses.Select((t,i)=>t.SequenceEqual(before.Faces[i].Thickness)).All(x=>x)&&Enumerable.Range(0,before.FaceCount).All(i=>!plan.Changes(before.ThicknessList(),i)),"the step writes no thickness: all 176 faces keep their corner thicknesses (the geometry stays as the user drew it)");
                var modePlan=ThickenModePlan.Create(ThicknessMode.SolidBlock,before.Faces.Select(f=>f.Along).ToList(),null);
                Check(!modePlan.Any,"the step changes no thickness direction");
                // the sequence of the user (the base name of Object 292 is [To back plane])
                var n0=name292!;var applied=Ensure(n0,ThicknessMode.SolidBlock);var undone=Ensure(n0,null);var redone=Ensure(applied,ThicknessMode.SolidBlock);
                var second=Ensure(applied,ThicknessMode.SolidBlock);var undoSecond=Ensure(applied,null);
                var thickness=Ensure(applied,ThicknessMode.Uniform);var undoThickness=Ensure(applied,null);var layers=Ensure(applied,ThicknessMode.Layers);var flip=Ensure(applied,CompositePlate.ModeFromName(applied));
                Check(n0==CompositePlate.BackplaneName&&applied==CompositePlate.SolidBlockName,$"Apply Solid block on Object 292 ('{n0}'): the copy is named '{applied}'");
                Check(undone==n0&&redone==CompositePlate.SolidBlockName,"Undo gives the old name back, Redo the mark");
                Check(second==CompositePlate.SolidBlockName&&undoSecond==CompositePlate.SolidBlockName,"a second Apply keeps the mark, and its Undo too (0.22.81 lost it there: below)");
                Check(thickness==CompositePlate.Name&&undoThickness==CompositePlate.SolidBlockName,"Thickness only on a Solid block removes the mark in that step; its Undo brings it back");
                Check(layers==CompositePlate.LayersName&&!CompositePlate.IsSolidBlock(layers),"Layers (Convert to layers) names the plate [Layers]: the mark goes");
                Check(flip==CompositePlate.SolidBlockName,"Flip and Thicken inward keep the mode of the plate, so the mark stays");
                // save and load: the blueprint keeps the name of the mesh; Object 292 with the mark written as Apply leaves it, out and back in
                var node=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"tests/fixtures/Object-292-1721.blueprint")))!;
                var meshNode=node["meshes"]!.AsArray().First(m=>m!["vuid"]!.GetValue<int>()==22)!;meshNode["meshData"]!["name"]=applied;
                var saved=Path.Combine(output,"object292-solid-block-roundtrip.blueprint");File.WriteAllText(saved,node.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
                using(var back=JsonDocument.Parse(File.ReadAllText(saved)))
                {
                    var (j,nm)=Mesh(back.RootElement,22);
                    Check(nm==CompositePlate.SolidBlockName&&CompositePlate.ModeFromName(nm)==ThicknessMode.SolidBlock&&SolidShape.FromJson(j).PieceVolumesM3.SequenceEqual(s292.PieceVolumesM3),"save and load: the blueprint keeps the mark in meshData.name and the same geometry (same volumes)");
                    SolidMarks.Reset();var outcome=SolidMarks.Built(0x292,0x2922,true,false,false,()=>nm,"Build",false);
                    Check(outcome==SolidMarks.BuildOutcome.Noted&&SolidMarks.Answer(0x292,out var e,out _)==SolidMarkAnswer.Marked&&e.Name==CompositePlate.SolidBlockName,"after loading, the Build postfix notes the mark (the hit pays the cavity)");
                    SolidMarks.Reset();
                }
                // the proof blueprints keep their marks (read only)
                Check(boxName==CompositePlate.SolidBlockName&&Mesh(proof77.RootElement,proof77.RootElement.GetProperty("meshes").EnumerateArray().First(m=>m.GetProperty("meshData").TryGetProperty("name",out var x)&&x.GetString()==CompositePlate.SolidBlockName).GetProperty("vuid").GetInt32()).Name==CompositePlate.SolidBlockName,"the two proof blueprints carry the mark that NameForMode(SolidBlock) writes");
                // copying: the game copies a mesh with its blueprint (name); a linked copy shares the mesh. Only the game can show the copy menu itself.
                Check(CompositePlate.ModeFromName(JsonNode.Parse(JsonSerializer.Serialize(new{name=applied}))!["name"]!.GetValue<string>())==ThicknessMode.SolidBlock,"a copy of the mesh blueprint (name included) keeps the mode");
            }

            // ================= A. the window: the choice, the help text, the note, the refusals =================
            {
                var backend=new CassetteComposer(materials,reactions,"","",_=>false);
                CassetteComposerModel NewModel(CassettePreset p,bool load){var m=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>new[]{p});m.RecipeDensity=id=>id==p.MaterialId?Model(p).Passive.Density:null;if(load)m.LoadPreset(p.Id);return m;}
                Check(CassetteComposerModel.ApplyChoiceLabels.Length==10&&CassetteComposerModel.ApplyChoiceLabels[(int)ComposerApplyChoice.SolidBlock]=="Solid block"&&Enum.GetValues<ComposerApplyChoice>().Length==10,"Solid block is the tenth choice under Apply");
                var panelSource=File.ReadAllText(Path.Combine(root,"CassetteComposerPanel.cs"));
                Check(panelSource.Contains("for(int r=0;r<(CassetteComposerModel.ApplyChoiceLabels.Length+2)/3;r++)",StringComparison.Ordinal)&&panelSource.Contains("ApplyChoiceLabels.Skip(from).Take(3)",StringComparison.Ordinal),"the window shows four rows of choices (three, three, three, one)");
                Check(panelSource.Contains("if(choice==ComposerApplyChoice.SolidBlock)",StringComparison.Ordinal)&&panelSource.Contains("RuntimeCompositePlateEdit.TrySolidBlock(tracked!,recipe,converts,out var solidMessage)",StringComparison.Ordinal),"the button Apply runs TrySolidBlock with the recipe (preset or the plate's own cassette) and converts a plain plate in the same step");
                var m=NewModel(userPreset,true);
                bool prevNativeCollider=SolidBlockApply.NativeCollider;
                SolidBlockApply.NativeCollider=false;
                try
                {
                var thick292=SolidShape.ThickFaces(json292);
                Check(thick292==SolidShape.ThickFaces(ComposerFill.Read(json292,true).Faces.Select(f=>(IReadOnlyList<ushort>)f.Thickness))&&thick292.Faces==44&&thick292.MaxMm==642,$"Object 292 mesh 22 still has the thicknesses of To back plane: {thick292.Faces} faces thicker than {SolidShape.SkinWarningMm} mm, up to {thick292.MaxMm} mm (the same in the note and in Apply)");
                var info=SolidTargetInfo.Of(s292,thick292);
                var target=new ComposerTarget("Plate #324",176,userPreset.MaterialId,userPreset.Label,true,5,642,ConvertBlocker:null,Solid:info);
                m.SetTarget(target);m.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                double rho=userModel.Passive.Density,mass292=SolidBlockApply.MassKg(s292.VolumeM3,rho);
                Check(m.ApplyChoice==ComposerApplyChoice.SolidBlock&&m.CanApply&&m.ApplyButton=="Apply: Solid block","Solid block on a closed composite plate: Apply is possible; the button says Apply: Solid block");
                Check(m.ApplyChoiceHelp=="The whole closed shape counts as the recipe: the path of the shot through it × the factor. For cast or curved shapes.","the help text of the brief");
                var note=m.ApplyNote;
                Check(note.StartsWith($"Applies {userPreset.Label} to Plate #324 as a Solid block; the shape stays as drawn. One undo step. Solid block: 2 closed pieces, {F(s292.VolumeM3*1000,"0.#")} l, new weight {F(mass292,"0")} kg ({F(rho,"0")} kg/m³ incl. air). The game still sees a thin shell: fragments cross the inside freely.",StringComparison.Ordinal),"the note: closed pieces, litres, the new weight and the honest line: "+note);
                Check(note.EndsWith(SolidBlockApply.Thick(44,642),StringComparison.Ordinal)&&note.Contains("44 faces are thicker than 50 mm (up to 642 mm)",StringComparison.Ordinal),"the note warns that 44 faces are thicker than a skin and how to make them thin (nothing is changed by itself)");
                var thinTarget=target with{Solid=info with{ThickFaces=0,MaxThicknessMm=5}};var thinModel=NewModel(userPreset,true);thinModel.SetTarget(thinTarget);thinModel.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                Check(thinModel.ApplyNote.EndsWith("The game still sees a thin shell: fragments cross the inside freely.",StringComparison.Ordinal),"with thin skins the note ends on the honest line (no warning)");
                var plainModel=NewModel(userPreset,true);plainModel.SetTarget(new ComposerTarget("Plate #7",176,"rha","Steel",false,5,642,ConvertBlocker:null,Solid:info));plainModel.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                Check(plainModel.CanApply&&plainModel.ApplyButton=="Apply: Solid block (converts the plate first)"&&plainModel.ApplyNote.StartsWith("Converts Plate #7 to a composite plate, applies "+userPreset.Label+" and then: Solid block. One undo step. Solid block: 2 closed pieces",StringComparison.Ordinal),"a plain plate: converted to a composite plate in the same step (one undo step): "+plainModel.ApplyNote);
                var openModel=NewModel(userPreset,true);openModel.SetTarget(new ComposerTarget("Plate #324",248,"ebCassette_t80u","T-80U",true,5,500,ConvertBlocker:null,Solid:SolidTargetInfo.Of(open)));openModel.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                Check(!openModel.CanApply&&openModel.ApplyBlocker==open.Refusal&&openModel.ApplyNote==open.Refusal,"an open plate: Apply is refused with the reason, the advice and the gaps (the same text as Apply would give)");
                var layered=NewModel(userPreset,true);layered.SetTarget(new ComposerTarget("Plate #366",201,"ebCassette_t80u","T-80U",true,20,20,ConvertBlocker:null,LayerLine:"Layers: 26 faces assigned, 0 not assigned.",Solid:info));layered.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                Check(!layered.CanApply&&layered.ApplyBlocker==SolidBlockApply.InLayers,"a plate in layers: refused with the advice to choose Back to box first (the choice does not do it itself)");
                var marked=NewModel(userPreset,true);marked.SetTarget(target with{SolidMark=true});
                Check(marked.ApplyChoice==ComposerApplyChoice.SolidBlock&&marked.CanApply,"a plate that is a Solid block starts on Solid block (Apply with another recipe keeps the mark)");
                marked.SetApplyChoice(ComposerApplyChoice.ThicknessOnly);
                Check(marked.ApplyNote.StartsWith(SolidBlockApply.Leaves,StringComparison.Ordinal),"Thickness only on a Solid block: the note says the mark goes: "+marked.ApplyNote);
                marked.SetApplyChoice(ComposerApplyChoice.ConvertToLayers);Check(marked.ApplyNote.StartsWith(SolidBlockApply.Leaves,StringComparison.Ordinal),"Convert to layers on a Solid block: the note says the mark goes");
                marked.SetApplyChoice(ComposerApplyChoice.Flip);Check(!marked.ApplyNote.Contains(SolidBlockApply.Leaves,StringComparison.Ordinal),"Flip keeps the mark and says nothing about it");
                var own=new CassetteComposerModel(backend,materials.Keys.Select(id=>new ComposerMaterial(id,id,CassettePalette.Group(id))).ToArray(),Array.Empty<ComposerMaterial>(),()=>Array.Empty<CassettePreset>()){RecipeDensity=id=>id=="ebCassette_t80u"?t80u.Passive.Density:null};
                own.SetTarget(target with{MaterialId="ebCassette_t80u",MaterialLabel="T-80U"});own.SetApplyChoice(ComposerApplyChoice.SolidBlock);
                Check(own.CanApply&&own.ApplyRecipeId=="ebCassette_t80u"&&own.ApplyNote.Contains($"new weight {F(SolidBlockApply.MassKg(s292.VolumeM3,t80u.Passive.Density),"0")} kg",StringComparison.Ordinal),"no preset open: the plate keeps its own cassette and the weight uses its density");
                // the picture of the window (two states)
                foreach(var (title,model,explain) in new[]{("The window: Solid block on Object 292 (mesh 22, two closed cheeks)",m,"The model of the window (CassetteComposerModel) as the production code builds it: the four rows of choices, the help text, the note with the closed pieces, the volume and the new weight."),
                    ("The window: Solid block on Object 293 (20:38): refused, with the gaps",openModel,"The same choice on the mesh of 20:38: Apply is refused; the note names each gap (edges, faces, place) and the advice.")})
                {
                    var p=new Panel{title=title,explanation=explain,width=980,height=430};
                    p.Rect(0,0,980,430,"#141a20","#2b333c",1);p.Text(16,18,"TARGET / APPLY","#b8ad94");
                    p.Rect(16,32,560,24,"#26303a","#3b4652",1);p.Text(24,44,model.TargetChip,"#e8e2d4");
                    p.Text(16,72,"Apply also does","#b8ad94");
                    var labels=CassetteComposerModel.ApplyChoiceLabels;
                    for(int i=0;i<labels.Length;i++)
                    {
                        int row=i/3,col=i%3;double x=16+col*200,y=84+row*30;bool chosen=(int)model.ApplyChoice==i;
                        p.Rect(x,y,196,24,chosen?"#4f6d3a":"#222a32",chosen?"#9cc46e":"#3b4652",1);p.Text(x+98,y+12,labels[i],chosen?"#ffffff":"#c9d1d8","MiddleCenter");
                    }
                    double yy=p.Wrapped(16,212,model.ApplyChoiceHelp,150,"#b8ad94");
                    yy=p.Wrapped(16,yy+8,model.ApplyNote,150,"#e8e2d4");
                    p.Rect(16,392,600,28,model.CanApply?"#35587a":"#3a3a3a","#5d7da0",1);p.Text(316,406,model.ApplyButton+(model.CanApply?"":"  (refused)"),"#ffffff","MiddleCenter");
                    panels.Add(p);
                }
                File.WriteAllText(Path.Combine(output,"SOLID-BLOCK-WINDOW.json"),JsonSerializer.Serialize(new{labels=CassetteComposerModel.ApplyChoiceLabels,help=m.ApplyChoiceHelp,note292=m.ApplyNote,button292=m.ApplyButton,note293=openModel.ApplyNote,blocker293=openModel.ApplyBlocker,layers=layered.ApplyBlocker,leaves=SolidBlockApply.Leaves},new JsonSerializerOptions{WriteIndented=true}));
                }
                finally{SolidBlockApply.NativeCollider=prevNativeCollider;}
            }

            // ================= C. the weight =================
            {
                // the density of a recipe over its nominal depth, air included, computed here from the Technology files (independent of CassetteModel)
                double Density(CassettePreset p){double mass=0,depth=0;foreach(var l in p.Layers){var d=l.Material=="air"?l.ThicknessMm:l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180);depth+=d;if(l.Material!="air")mass+=materials[l.Material].Density*d;}return mass/depth;}
                Check(Math.Abs(Density(userPreset)-userModel.Passive.Density)<1e-9*userModel.Passive.Density&&Math.Abs(Density(burlington.Preset)-burlington.Passive.Density)<1e-9*burlington.Passive.Density,$"the density of the weight is that of the recipe over its nominal depth, air included (the density a plate in layers uses): user recipe {F(userModel.Passive.Density,"0.#")}, Burlington {F(burlington.Passive.Density,"0.#")} kg/m³");
                double rho=userModel.Passive.Density;
                for(int i=0;i<s292.Pieces;i++)weights.AppendLine(string.Join("\t","Object 292 mesh 22",i==0?"cheek 1":"cheek 2",F(s292.PieceVolumesM3[i],"0.######"),userPreset.Id,F(rho,"0.#"),F(SolidBlockApply.MassKg(s292.PieceVolumesM3[i],rho),"0.#"),""));
                double native292=0;foreach(var b in bp292.RootElement.GetProperty("blueprints").EnumerateArray())if(b.GetProperty("id").GetInt32()==72)native292=b.GetProperty("blueprint").GetProperty("armourVolume").GetDouble();
                weights.AppendLine(string.Join("\t","Object 292 mesh 22","both",F(s292.VolumeM3,"0.######"),userPreset.Id,F(rho,"0.#"),F(SolidBlockApply.MassKg(s292.VolumeM3,rho),"0.#"),$"the game's armourVolume of the plate (skins, with the thick front faces of To back plane) is {F(native292,"0.####")} m³: {F(native292*rho,"0")} kg at the same density (assumption: the tech density of the cassette is this density)"));
                var box=SolidShape.FromJson(boxJson);double rb=burlington.Passive.Density;
                weights.AppendLine(string.Join("\t","Solid block proof - Challenger 1 Mk3 (mesh 4)","box",F(box.VolumeM3,"0.######"),"burlington",F(rb,"0.#"),F(SolidBlockApply.MassKg(box.VolumeM3,rb),"0.#"),"800 x 550 x 600 mm"));
                Check(box.Closed&&box.Pieces==1&&Math.Abs(box.VolumeM3-.8*.55*.6)<1e-6,$"Challenger box: one closed piece of {F(box.VolumeM3,"0.######")} m³ = 0.8 x 0.55 x 0.6 m");
                Check(Math.Abs(SolidBlockApply.MassKg(box.VolumeM3,rb)-.264*rb)<1e-3,$"Challenger box weight {F(SolidBlockApply.MassKg(box.VolumeM3,rb),"0.#")} kg = 0.264 m³ x {F(rb,"0.#")} kg/m³");
                var fill=File.ReadAllText(Path.Combine(root,"RuntimeComposerFill.cs"));
                int b0=fill.IndexOf("    // ---- 0.22.82 solid weight (begin) ----",StringComparison.Ordinal),b1=fill.IndexOf("    // ---- 0.22.82 solid weight (end) ----",StringComparison.Ordinal);
                var region=b0>0&&b1>b0?fill[b0..b1]:"";
                Check(region.Contains("var mass=(float)SolidBlockApply.MassKg(shape.VolumeM3,model.Passive.Density);",StringComparison.Ordinal)&&region.Contains("var shape=SolidShape.FromJson(plate.Mesh.ToJson(true));",StringComparison.Ordinal),"Weigh: mass = the closed volume of SolidShape (as Apply measures it) x the density of the recipe");
                var writes=System.Text.RegularExpressions.Regex.Matches(region,@"plate\.(\w+)\s*=(?!=)|plate\.(SetMass|SetCost|RecalculateCenterOfMass|RaiseDirtyFlags|SetArmourMaterial)\(|plate\.Vehicle\.RaiseDirtyFlags").Select(x=>x.Value).Distinct().ToArray();
                Check(writes.OrderBy(x=>x).SequenceEqual(new[]{"plate.RecalculateCenterOfMass(","plate.SetCost(","plate.SetMass(","plate.cachedMass="}.OrderBy(x=>x))&&!region.Contains("armourDensity=",StringComparison.Ordinal),"the solid weight writes only SetMass (with cachedMass, as Build does), the centre of mass and the cost; armourDensity is not written: "+string.Join(", ",writes));
                var weigh=fill[fill.IndexOf("internal static void Weigh(PlateStructure plate)",StringComparison.Ordinal)..];weigh=weigh[..weigh.IndexOf("    private static int weightWarnings;",StringComparison.Ordinal)];
                Check(weigh.Contains("        try\r\n        {\r\n            NativeMaterialReactions.SolidMarkBuilt(plate);",StringComparison.Ordinal)&&weigh.IndexOf("SolidMarks.Answer(plate.Pointer.ToInt64(),out _,out _) is var solid&&solid!=SolidMarkAnswer.Unmarked&&WeighSolid(plate,solid==SolidMarkAnswer.Marked))return; // 0.22.82 solid",StringComparison.Ordinal)>weigh.IndexOf("NativeMaterialReactions.SolidMarkBuilt(plate);",StringComparison.Ordinal),"Weigh (the existing Build postfix, after every Build) notes the mark first and then weighs a marked plate; an unmarked plate takes the unchanged route");
                File.WriteAllText(Path.Combine(output,"SOLID-BLOCK-WEIGHTS.tsv"),weights.ToString());
            }

            // ================= the path through the shape x factor: straight and at +-30 degrees, three heights per cheek =================
            {
                var objects292=bp292.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
                var objects293=bp293.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
                var dumpTable=userTable; // the dump has no recipe of the user's file in the test catalogue: the same table, so the numbers compare
                var thin292=Thin(json292,5);
                var saved=Lines("Object 292 (17:21, as saved)",json292,objects292,323,userTable,userPreset.MaterialId,lines,double.NaN);
                var thin=Lines("Object 292, every face 5 mm (same drawn shape)",thin292,objects292,323,userTable,userPreset.MaterialId,lines,5);
                var dumpLines=Lines("Object 293 dump (21:14, as saved, 5 mm)",dumpJson,objects293,323,dumpTable,userPreset.MaterialId,lines,5);
                Check(saved.Lines==18&&saved.Within<saved.Lines&&saved.Unpaired>0,$"Object 292 as saved (44 faces of 417-642 mm from To back plane): on {saved.Unpaired} of 18 lines the surfaces of the thick faces cannot be paired (odd, or no passage): those lines pay only skins; the solid path equals the drawn path on {saved.Within} of 18. This is the warning of the note (finding; the calculation is not changed)");
                foreach(var (tag,s) in new[]{("Object 292 with 5 mm skins",thin),("Object 293 dump",dumpLines)})
                {
                    Check(s.Lines==18,$"{tag}: 18 shot lines (2 cheeks x 3 heights x straight, +30 and -30 degrees) cross the shape ({s.Lines})");
                    Check(s.Within==s.Lines-s.Unpaired&&s.Unpaired<=2,$"{tag}: on every paired line the solid path (front skin in to back skin out) is the path through the drawn faces of the independent code plus at most the two skins on the line ({s.Within} of {s.Lines-s.Unpaired}; differences {F(s.MinDiff,"0.##")} to {F(s.MaxDiff,"0.##")} mm); {s.Unpaired} line(s) cross a seam or graze a face (odd count: skins only, the known fallback)");
                    Check(s.Odd==0,$"{tag}: no line writes an odd line for the back skin of a paid passage (E)");
                    Check(Math.Abs(s.PaidRha-s.DrawnRha)<=.05*s.DrawnRha,$"{tag}: path x factor of the solid path {F(s.PaidRha,"0")} mm RHA-eq against the drawn path x factor {F(s.DrawnRha,"0")} mm RHA-eq, summed over the paired lines ({F(100*(s.PaidRha-s.DrawnRha)/s.DrawnRha,"0.#")} %: the skins, which the game puts on the outside of faces that wind the other way)");
                }
                File.WriteAllText(Path.Combine(output,"SOLID-BLOCK-LINES.tsv"),lines.ToString());
                File.WriteAllText(Path.Combine(output,"object292-mesh22-5mm.json"),thin292);
                panels.Insert(0,Heat("Object 292 as Solid block, as saved (17:21): path through the shape x factor from the front","mesh 22 with the 44 faces of 417-642 mm that To back plane left: where a cell is dark the thick faces make the line unpairable and only skins are paid.",json292,objects292,323,userTable,userPreset.MaterialId));
                panels.Insert(1,Heat("Object 292 as Solid block, every face a 5 mm skin (the same drawn shape): path through the shape x factor from the front","what the hit pays after Thickness only 5 mm and Solid block: the colour follows the depth of the cheek.",thin292,objects292,323,userTable,userPreset.MaterialId));
            }

            // ================= F. the diag line =================
            {
                var t80Table=ProtectionFactorTable.Build(t80u);var box=new List<double>{1,1.005,1.495,1.5};var failed=new HashSet<int>();var logs=new List<string>();var diagOut=new StringBuilder();
                string? One(SolidMarkAnswer answer,bool verbose,double budget=900,Func<double,double>? factorAt=null,List<double>? surfaces=null,double from=double.NegativeInfinity,int entity=501){var gate=new SolidDiagGate();var l=DiagShot(gate,entity,answer,verbose,surfaces??box,t80Table,budget,failed,logs,factorAt,from);if(l!=null)diagOut.AppendLine((verbose?"[on] ":"[off] ")+l);return l;}
                SolidDiagGate.ResetSession();
                var onPaid=One(SolidMarkAnswer.Marked,true);
                Check(onPaid!=null&&onPaid.EndsWith("exit paid.",StringComparison.Ordinal)&&onPaid.Contains("4 surfaces, 2 skins, 1 passages",StringComparison.Ordinal),"with Composite fill / Layer hit log on: the diag line as in 0.22.79 (paid)");
                Check(One(SolidMarkAnswer.Unmarked,true) is {} u&&u.EndsWith("exit unmarked.",StringComparison.Ordinal)&&One(SolidMarkAnswer.Marked,true,factorAt:_=>double.NaN,surfaces:new(){1,1.005,1.495})!.EndsWith("exit odd.",StringComparison.Ordinal),"with the setting on, every way out keeps its line (unmarked, odd)");
                SolidDiagGate.ResetSession();
                Check(One(SolidMarkAnswer.Marked,false)==null,"with the setting off, a paid solid block writes no diag line");
                Check(One(SolidMarkAnswer.Unmarked,false)==null&&One(SolidMarkAnswer.Marked,false,surfaces:new(){1,1.005,1.495})==null&&One(SolidMarkAnswer.Marked,false,from:1.1)==null,"off: unmarked, odd and notDue write nothing");
                var unknown=One(SolidMarkAnswer.Unknown,false);var thrown=One(SolidMarkAnswer.Marked,false,factorAt:_=>throw new InvalidOperationException("test"),entity:777);var refused=One(SolidMarkAnswer.Marked,false,budget:0);
                Check(unknown!=null&&unknown.EndsWith("exit markUnknown.",StringComparison.Ordinal)&&thrown!=null&&thrown.EndsWith("exit exception.",StringComparison.Ordinal)&&refused!=null&&refused.EndsWith("exit applyPassiveFalse.",StringComparison.Ordinal),"off: the ways out where a plate with the mark (or an unknown mark) does not pay always write one line: markUnknown, exception, applyPassiveFalse");
                Check(SolidDiagGate.Failures==3&&SolidDiagGate.Written==0,"those lines count against their own limit (16), not against the 64 of the setting");
                {var gate=new SolidDiagGate();var a=DiagShot(gate,600,SolidMarkAnswer.Unknown,false,box,t80Table,900,failed,logs);var b=DiagShot(gate,600,SolidMarkAnswer.Unknown,false,box,t80Table,900,failed,logs);
                 Check(a!=null&&b==null,"one line per shot per plate: the second block of the same shot with the same unknown mark writes nothing");}
                SolidDiagGate.ResetSession();int written=0;for(int i=0;i<40;i++)if(One(SolidMarkAnswer.Unknown,false)!=null)written++;
                Check(written==SolidDiagGate.FailureLimit&&SolidDiagGate.Failures==16,$"at most 16 such lines per session ({written} of 40 shots)");
                var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
                Check(gp.Contains("shot=solidShots.GetOrCreateValue(reached.Impact);verbose=RuntimeGroupedProtection.LayerHitLog;",StringComparison.Ordinal)&&gp.Contains("if(seen.Answer!=SolidMarkAnswer.Marked&&!shot.Diag.Wants(entity,verbose,seen.Answer))continue;",StringComparison.Ordinal)
                    &&gp.Contains("var diag=shot.Diag.Wants(pair.Key,verbose,seen.Answer)?new SolidDiag{",StringComparison.Ordinal)&&gp.Contains("if(diag!=null&&shot.Diag.Allow(pair.Key,diag.Exit,verbose))Plugin.ModLog.LogInfo(diag.Text());",StringComparison.Ordinal),"the glue of SolidCavities passes the setting Composite fill / Layer hit log to the gate (the replay above follows these lines)");
                File.WriteAllText(Path.Combine(output,"SOLID-DIAG-GATE.txt"),diagOut.ToString());
                SolidDiagGate.ResetSession();
            }

            // ================= E and G: what the code does =================
            {
                var f81=Path.Combine(output,"FEAT81-RESULT.json");using var doc=File.Exists(f81)?JsonDocument.Parse(File.ReadAllText(f81)):null;
                Check(doc!=null&&doc.RootElement.GetProperty("passed").GetBoolean()&&doc.RootElement.GetProperty("failures").GetInt32()==0&&File.GetLastWriteTimeUtc(f81)>DateTime.UtcNow.AddHours(-2),"E is in 0.22.81: FEAT81 (no odd line for the back skin of a paid passage) passed in this run");
                var ar=File.ReadAllText(Path.Combine(root,"RuntimeArmourResponses.cs"));var gp=File.ReadAllText(Path.Combine(root,"RuntimeGroupedProtection.cs"));
                Check(ar.Contains("if((sim.fragments[index].flags&FragmentFlag.OriginalPenetrator)==0)return;",StringComparison.Ordinal)&&gp.Contains("if((fragment.flags&(FragmentFlag.OriginalPenetrator|FragmentFlag.Killed))!=FragmentFlag.OriginalPenetrator){if(diag!=null)diag.Exit=\"notOriginalPenetrator\";return;}",StringComparison.Ordinal),"G (read, not changed): fragments are skipped by the mod's armour path (Begin) and never pay a cavity (SolidCavity): they cross the inside of a solid block as air and meet only the back skin");
            }

            // ================= D (stopped): the overlay as it would look, drawn from the mesh (not in the DLL) =================
            {
                var objects=bp292.RootElement.GetProperty("objects").EnumerateArray().ToDictionary(o=>o.GetProperty("vuid").GetInt32(),o=>o);
                var (rot,pos)=World(objects,objects[323]);var (pts,fcs)=Read(json292);var world=pts.Select(q=>Mul(rot,q)+pos).ToArray();
                var solid=userPreset.Layers.Where(l=>l.Material!="air").OrderByDescending(l=>l.ThicknessMm/Math.Cos(l.TiltDegrees*Math.PI/180)).First().Material;
                uint baseColour=CassetteDepthStrip.Colour(solid);var view=Vector3.Normalize(new Vector3(.55f,.45f,1));var up=Vector3.UnitY;var right=Vector3.Normalize(Vector3.Cross(up,view));var vup=Vector3.Cross(view,right);var light=Vector3.Normalize(new Vector3(.3f,.8f,.6f));
                var ov=new Panel{title="D (stopped, not in this DLL): the overlay of a Solid block as one dense block in the colour of the recipe",explanation=$"Drawn here from mesh 22 of Object 292: every face in the colour of the thickest material of the recipe ({solid}), shaded, no thin skin and no stripes. In the game this needs RuntimeCassettePlateVisual.cs (see the report).",width=980,height=420};
                ov.Rect(0,0,980,420,"#0d1115");
                var proj=world.Select(q=>(X:Vector3.Dot(q,right),Y:Vector3.Dot(q,vup),Z:Vector3.Dot(q,view))).ToArray();
                double minX=proj.Min(p=>p.X),maxX=proj.Max(p=>p.X),minY=proj.Min(p=>p.Y),maxY=proj.Max(p=>p.Y);double sc=Math.Min(920/(maxX-minX),360/(maxY-minY));
                var order=Enumerable.Range(0,fcs.Length).OrderBy(f=>fcs[f].Average(i=>proj[i].Z)).ToArray();
                foreach(var f in order)
                {
                    var v=fcs[f];var n=Vector3.Zero;for(int j=0;j<v.Length;j++)n+=Vector3.Cross(world[v[j]],world[v[(j+1)%v.Length]]);n=Vector3.Normalize(n);
                    double shade=.45+.55*Math.Abs(Vector3.Dot(n,light));int R=(int)((baseColour>>24&0xff)*shade),G=(int)((baseColour>>16&0xff)*shade),B=(int)((baseColour>>8&0xff)*shade);
                    ov.Poly(v.Select(i=>(30+(proj[i].X-minX)*sc,390-(proj[i].Y-minY)*sc)),$"#{R:x2}{G:x2}{B:x2}",$"#{R:x2}{G:x2}{B:x2}",.8);
                }
                panels.Add(ov);
            }
        }
        catch(Exception error){Check(false,"FEAT82 threw "+error.GetType().Name+": "+error.Message+" "+error.StackTrace?.Split('\n').FirstOrDefault());}
        File.WriteAllText(Path.Combine(output,"SOLID-BLOCK-CASES.tsv"),cases.ToString());
        File.WriteAllText(Path.Combine(preview,"solid-block-panels.json"),JsonSerializer.Serialize(panels,new JsonSerializerOptions{IncludeFields=true}));
        File.WriteAllText(Path.Combine(output,"FEAT82-RESULT.json"),JsonSerializer.Serialize(new{passed=failures==0,checks,failures},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"FEAT82 PASS: {checks} checks, {failures} failures");
    }
}
