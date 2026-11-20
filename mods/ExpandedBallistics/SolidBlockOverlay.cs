using System.Numerics;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.83: the layer view (Ctrl+L) of a plate marked [Solid block]. The native overlay copy keeps every point, normal and triangle (so the coverage test of the
/// native renderers sees the same count); only the depth coordinate changes and the triangles off the drawn surface get zero area:
/// <list type="bullet">
/// <item>every point gets the depth of the middle of the band of the thickest solid material of the recipe (the strip is point-sampled, so the whole block shows
/// that one colour; the shading comes from the native normals, as for every other plate);</item>
/// <item>a triangle whose centre lies farther than <see cref="SurfaceM"/> from every drawn face of the closed shape (either diagonal of a warped face) is not part of the
/// block's surface: its three corners become one point. Inside the shape that is the inside of the skin (its back and its side walls); outside the shape it is the skin
/// that the game puts on the outside of a face that winds the other way (Object 292: a 5 mm step with its own walls). A triangle on the drawn surface is drawn.</item>
/// </list>
/// A plate that is not a closed shape keeps all its triangles (inside is not defined); it still shows the one colour. Pure code: the runtime glue is
/// RuntimeLayerFaces.Overlay; a plate without the mark never comes here.
/// 0.22.84: the runtime calls <see cref="ApplyBands"/>: the same overlay, with the layers of the recipe as bands from the front to the back of the block along the front of the
/// vehicle; the one colour above stays as its fallback.
/// </summary>
internal static class SolidBlockOverlay
{
    /// <summary>The text the overlay signature gets for a marked plate only (RuntimeCassettePlateVisual); an unmarked plate's signature stays as it was.</summary>
    internal const string Mark="|[Solid block]";
    /// <summary>A triangle within this distance of a drawn face is part of the surface (the overlay tolerance of a plate in layers, LayerOverlay.ToleranceM).</summary>
    internal const double SurfaceM=.0006;
    internal sealed class Report
    {
        internal string Material="";internal float Depth;internal bool Closed;internal string? Refusal;
        internal int Triangles,Hidden,Surface,Inside,Outside;
        internal readonly List<bool> HiddenTriangles=new();
        // 0.22.84: the bands (ApplyBands)
        internal bool Bands;internal string? BandRefusal;internal double BandMs;internal int BandPoints,BandNeighbour,BandPieces,BandRim;
    }
    /// <summary>The recipe layer shown: the thickest solid (not air) layer by its depth in the strip (thickness / cos tilt, as CassetteDepthStrip), the first of equals;
    /// its index in the recipe, or 0 for a recipe without layers (the strip of the game's default, rha).</summary>
    internal static int ThickestLayer(CassettePreset? preset)
    {
        var layers=preset?.Layers;if(layers==null||layers.Length==0)return 0;
        int best=-1;double depth=double.NegativeInfinity;
        for(int i=0;i<layers.Length;i++)
        {
            if(layers[i].Material=="air")continue;
            var d=layers[i].ThicknessMm/Math.Cos(layers[i].TiltDegrees*Math.PI/180);
            if(d>depth){depth=d;best=i;}
        }
        return best<0?0:best;
    }
    /// <summary>The material of <see cref="ThickestLayer"/> ("rha" without a recipe).</summary>
    internal static string Material(CassettePreset? preset)=>preset?.Layers is {Length:>0} l?l[ThickestLayer(preset)].Material:"rha";
    /// <summary>The depth coordinate of the middle of that layer's band in the strip (CassetteDepthStrip.Boundaries).</summary>
    internal static float Depth(CassettePreset? preset)
    {
        var b=CassetteDepthStrip.Boundaries(preset);int i=ThickestLayer(preset);
        return (float)((b[i]+b[i+1])/2);
    }

    /// <summary>The overlay of a marked plate from the native copy and the saved mesh (PlateStructureMesh.ToJson: "vertices" and the corners "v" of every face, in the frame of the overlay).</summary>
    internal static OverlayCopy Apply(OverlayCopy copy,string? meshJson,CassettePreset? preset,Report? report=null)
    {
        float depth=Depth(preset);
        var uv=new Vector2[copy.Uv.Length];for(int i=0;i<uv.Length;i++)uv[i]=new(.5f,depth);
        var shape=meshJson==null?null:SolidShape.FromJson(meshJson);
        var triangles=(int[])copy.Triangles.Clone();
        if(report!=null){report.Material=Material(preset);report.Depth=depth;report.Closed=shape?.Closed==true;report.Refusal=shape==null?"the mesh could not be read now.":shape.Refusal;report.Triangles=triangles.Length/3;}
        if(shape?.Closed==true)
        {
            var (points,faces)=Read(meshJson!);
            var fan=new List<(Vector3d A,Vector3d B,Vector3d C)>();var any=new List<(Vector3d A,Vector3d B,Vector3d C)>();
            foreach(var f in faces)
            {
                for(int j=1;j+1<f.Length;j++)fan.Add((points[f[0]],points[f[j]],points[f[j+1]]));
                // every diagonal: a warped face is no plane, and the native tessellation may split it along the other diagonal
                for(int s=0;s<f.Length;s++)for(int j=1;j+1<f.Length;j++)any.Add((points[f[s]],points[f[(s+j)%f.Length]],points[f[(s+j+1)%f.Length]]));
            }
            var min=points.Aggregate(new Vector3d(double.MaxValue,double.MaxValue,double.MaxValue),Vector3d.Min);var max=points.Aggregate(new Vector3d(double.MinValue,double.MinValue,double.MinValue),Vector3d.Max);
            for(int t=0;t+2<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                var centre=(V(copy.Vertices[a])+V(copy.Vertices[b])+V(copy.Vertices[c]))/3;
                bool hide=!Near(centre,any,SurfaceM);
                if(hide){triangles[t+1]=a;triangles[t+2]=a;}
                if(report!=null){report.HiddenTriangles.Add(hide);if(!hide)report.Surface++;else{report.Hidden++;if(Inside(centre,fan,min,max))report.Inside++;else report.Outside++;}}
            }
        }
        else if(report!=null)for(int t=0;t+2<triangles.Length;t+=3)report.HiddenTriangles.Add(false);
        return copy with{Triangles=triangles,Uv=uv};
    }

    // ---------------- 0.22.84: the layers of the recipe as bands along the front of the vehicle ----------------
    /// <summary>How many times the band depth was really computed (one per rebuild of the overlay; a poll with the same signature never comes here).</summary>
    internal static int BandComputations;
    /// <summary>The eight lines through a point run this far beside it (a line exactly through the point would graze the side walls).</summary>
    internal const double BandOffsetM=.0003;
    /// <summary>The point must lie within this distance (along the front direction) of the stretch of a line through the shape.</summary>
    internal const double BandToleranceM=.002;
    /// <summary>A line whose entry or exit lies this close (along the front direction) to the point meets the point's own front or back.</summary>
    internal const double EndM=.0005;
    /// <summary>On a rim a point of the overlay is measured this far into its own triangles.</summary>
    internal const double RimM=.0005;
    /// <summary>
    /// 0.22.84: the overlay of <see cref="Apply"/> (the same points, normals, triangles and the same triangles off the drawn surface), with the depth coordinate per point
    /// d = (distance from the front of the block at that place) / (depth of the block at that place), both along the front direction of the vehicle (Convert to layers without a
    /// selection: RuntimeLayerFaces.Forward). The strip of the recipe (CassetteDepthStrip, as the look painted) then shows the layers as bands from front to back, air as air.
    /// Per point: eight lines parallel to the front direction, 0.3 mm beside the point, against the closed shape of its own piece (fan triangles of the drawn faces); on each line
    /// the crossings are paired (entry, exit) and the stretch that holds the point gives d; the median of the lines that hold the point is used (for a point on the front or
    /// the back: of the lines whose entry or exit lies at the point, within <see cref="EndM"/>). A point that no line holds
    /// (a seam, a skin outside the shape) takes the d of the nearest point that has one. When this fails (no mesh, not closed, no front direction, no point with a d, an error),
    /// the plate keeps the one colour of <see cref="Apply"/> and <see cref="Report.BandRefusal"/> says why; nothing is thrown.
    /// </summary>
    internal static OverlayCopy ApplyBands(OverlayCopy copy,string? meshJson,CassettePreset? preset,Vector3? forward,Report? report=null)
    {
        report??=new Report();
        var solid=Apply(copy,meshJson,preset,report);
        try
        {
            if(meshJson==null){report.BandRefusal="the mesh could not be read now";return solid;}
            if(!report.Closed){report.BandRefusal="not a closed shape";return solid;}
            if(forward is not {} f||!float.IsFinite(f.X)||!float.IsFinite(f.Y)||!float.IsFinite(f.Z)||f.LengthSquared()<1e-12f){report.BandRefusal="no front direction of the vehicle";return solid;}
            if(copy.Uv.Length!=copy.Vertices.Length){report.BandRefusal="the overlay has not one depth per point";return solid;}
            BandComputations++;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var d=Depths(copy.Vertices,copy.Triangles,meshJson,f,report);
            watch.Stop();report.BandMs=watch.Elapsed.TotalMilliseconds;
            if(d==null)return solid;
            var uv=new Vector2[copy.Uv.Length];
            for(int i=0;i<uv.Length;i++)
            {
                if(!float.IsFinite(d[i])||d[i]<0||d[i]>1){report.BandRefusal="a depth outside 0 to 1";return solid;}
                uv[i]=new(.5f,d[i]);
            }
            report.Bands=true;
            return solid with{Uv=uv};
        }
        catch(Exception ex){report.Bands=false;report.BandRefusal="the band depth failed: "+ex.Message;return solid;}
    }
    private sealed class BandTriangle{internal double Au,Av,Bu,Bv,Cu,Cv,Sa,Sb,Sc,Det;internal int Piece;}
    /// <summary>d per point of the overlay (null: refused, the reason in the report).</summary>
    internal static float[]? Depths(Vector3[] vertices,int[] triangles,string json,Vector3 forward,Report report)
    {
        var (points,faces)=Read(json);int n=faces.Length;
        // pieces: faces joined by an edge (as SolidShape)
        var parent=Enumerable.Range(0,n).ToArray();int Find(int i){while(parent[i]!=i)i=parent[i]=parent[parent[i]];return i;}
        var edges=new Dictionary<(int,int),int>();
        for(int fi=0;fi<n;fi++)for(int j=0;j<faces[fi].Length;j++)
        {
            int a=faces[fi][j],b=faces[fi][(j+1)%faces[fi].Length];var key=(Math.Min(a,b),Math.Max(a,b));
            if(edges.TryGetValue(key,out var other))parent[Find(fi)]=Find(other);else edges[key]=fi;
        }
        var pieceOf=new int[n];var ids=new Dictionary<int,int>();for(int fi=0;fi<n;fi++){var r=Find(fi);if(!ids.TryGetValue(r,out var id))ids[r]=id=ids.Count;pieceOf[fi]=id;}
        int pieces=ids.Count;report.BandPieces=pieces;
        var fw=Norm(new Vector3d(forward.X,forward.Y,forward.Z));
        var u=Norm(Vector3d.Cross(fw,Math.Abs(fw.X)<.6?new Vector3d(1,0,0):new Vector3d(0,1,0)));var v=Vector3d.Cross(fw,u);
        var lo=Enumerable.Repeat(new Vector3d(double.MaxValue,double.MaxValue,double.MaxValue),pieces).ToArray();var hi=Enumerable.Repeat(new Vector3d(double.MinValue,double.MinValue,double.MinValue),pieces).ToArray();
        var tris=new List<BandTriangle>();var fans=new List<(Vector3d A,Vector3d B,Vector3d C)>[pieces];for(int p=0;p<pieces;p++)fans[p]=new();
        for(int fi=0;fi<n;fi++)
        {
            int piece=pieceOf[fi];var face=faces[fi];
            foreach(var k in face){lo[piece]=Vector3d.Min(lo[piece],points[k]);hi[piece]=Vector3d.Max(hi[piece],points[k]);}
            for(int j=1;j+1<face.Length;j++)
            {
                var a=points[face[0]];var b=points[face[j]];var c=points[face[j+1]];fans[piece].Add((a,b,c));
                var t=new BandTriangle{Au=Vector3d.Dot(a,u),Av=Vector3d.Dot(a,v),Bu=Vector3d.Dot(b,u),Bv=Vector3d.Dot(b,v),Cu=Vector3d.Dot(c,u),Cv=Vector3d.Dot(c,v),Sa=-Vector3d.Dot(a,fw),Sb=-Vector3d.Dot(b,fw),Sc=-Vector3d.Dot(c,fw),Piece=piece};
                t.Det=(t.Bu-t.Au)*(t.Cv-t.Av)-(t.Cu-t.Au)*(t.Bv-t.Av);
                if(Math.Abs(t.Det)<1e-14)continue; // parallel to the front direction: no line crosses it
                tris.Add(t);
            }
        }
        if(tris.Count==0){report.BandRefusal="no face of the shape crosses the front direction";return null;}
        // a grid over the view from the front: each cell lists the triangles over it
        double minU=tris.Min(t=>Math.Min(t.Au,Math.Min(t.Bu,t.Cu)))-.01,maxU=tris.Max(t=>Math.Max(t.Au,Math.Max(t.Bu,t.Cu)))+.01;
        double minV=tris.Min(t=>Math.Min(t.Av,Math.Min(t.Bv,t.Cv)))-.01,maxV=tris.Max(t=>Math.Max(t.Av,Math.Max(t.Bv,t.Cv)))+.01;
        int G=Math.Clamp((int)Math.Ceiling(Math.Sqrt(tris.Count))*2,1,256);double cu=(maxU-minU)/G,cv=(maxV-minV)/G;
        var grid=new List<int>[G*G];
        int CellU(double x)=>Math.Clamp((int)Math.Floor((x-minU)/cu),0,G-1);int CellV(double y)=>Math.Clamp((int)Math.Floor((y-minV)/cv),0,G-1);
        for(int i=0;i<tris.Count;i++)
        {
            var t=tris[i];
            int u0=CellU(Math.Min(t.Au,Math.Min(t.Bu,t.Cu))),u1=CellU(Math.Max(t.Au,Math.Max(t.Bu,t.Cu))),v0=CellV(Math.Min(t.Av,Math.Min(t.Bv,t.Cv))),v1=CellV(Math.Max(t.Av,Math.Max(t.Bv,t.Cv)));
            for(int x=u0;x<=u1;x++)for(int y=v0;y<=v1;y++)(grid[x*G+y]??=new()).Add(i);
        }
        var offsets=new (double U,double V)[8];for(int k=0;k<8;k++){double angle=(k*45+13.7)*Math.PI/180;offsets[k]=(Math.Cos(angle)*BandOffsetM,Math.Sin(angle)*BandOffsetM);}
        // one d per distinct point
        var index=new Dictionary<(int,int,int),int>();var unique=new List<Vector3d>();var map=new int[vertices.Length];
        for(int i=0;i<vertices.Length;i++)
        {
            var p=vertices[i];var key=(BitConverter.SingleToInt32Bits(p.X),BitConverter.SingleToInt32Bits(p.Y),BitConverter.SingleToInt32Bits(p.Z));
            if(!index.TryGetValue(key,out var at)){at=unique.Count;index[key]=at;unique.Add(V(p));}
            map[i]=at;
        }
        report.BandPoints=unique.Count;
        var crossings=new List<double>();var samples=new List<double>();var ends=new List<double>();var candidates=new List<int>();
        // its own piece: the one whose box holds it, else the one with the nearest surface; -1: none
        int PieceOf(Vector3d p)
        {
            candidates.Clear();
            for(int k=0;k<pieces;k++)if(p.X>=lo[k].X-.001&&p.Y>=lo[k].Y-.001&&p.Z>=lo[k].Z-.001&&p.X<=hi[k].X+.001&&p.Y<=hi[k].Y+.001&&p.Z<=hi[k].Z+.001)candidates.Add(k);
            if(candidates.Count==0)return -1;
            int piece=candidates[0];
            if(candidates.Count>1){double best=double.PositiveInfinity;foreach(var k in candidates){double dk=Distance(p,fans[k]);if(dk<best){best=dk;piece=k;}}}
            return piece;
        }
        // d at p from the eight lines beside it (NaN: no line holds it). rim: some line enters right at p and some line leaves right at p (the edge where a front and a back meet).
        double Sample(Vector3d p,int piece,bool nearestEnds,out bool rim)
        {
            rim=false;double pu=Vector3d.Dot(p,u),pv=Vector3d.Dot(p,v),s0=-Vector3d.Dot(p,fw);bool enters=false,leaves=false;
            samples.Clear();ends.Clear();
            foreach(var (ou,ov) in offsets)
            {
                double qu=pu+ou,qv=pv+ov;var cell=grid[CellU(qu)*G+CellV(qv)];if(cell==null)continue;
                crossings.Clear();
                foreach(var ti in cell)
                {
                    var t=tris[ti];if(t.Piece!=piece)continue;
                    double wa=((t.Bu-qu)*(t.Cv-qv)-(t.Bv-qv)*(t.Cu-qu))/t.Det,wb=((t.Cu-qu)*(t.Av-qv)-(t.Cv-qv)*(t.Au-qu))/t.Det,wc=1-wa-wb;
                    if(wa<0||wb<0||wc<0)continue;
                    crossings.Add(wa*t.Sa+wb*t.Sb+wc*t.Sc);
                }
                if(crossings.Count<2)continue;
                crossings.Sort();
                int m=1;for(int k=1;k<crossings.Count;k++)if(crossings[k]-crossings[m-1]>1e-7)crossings[m++]=crossings[k]; // one crossing through a shared edge or corner
                if((m&1)==1)continue;
                double bestGap=double.PositiveInfinity,entry=0,exit=0;
                for(int k=0;k+1<m;k+=2)
                {
                    double a=crossings[k],b=crossings[k+1],gap=s0<a?a-s0:s0>b?s0-b:0;
                    if(gap<bestGap){bestGap=gap;entry=a;exit=b;}
                }
                if(bestGap>BandToleranceM||exit-entry<1e-6)continue;
                samples.Add(Math.Clamp((s0-entry)/(exit-entry),0,1));ends.Add(Math.Min(Math.Abs(s0-entry),Math.Abs(s0-exit)));
                if(Math.Abs(s0-entry)<=EndM)enters=true;if(Math.Abs(s0-exit)<=EndM)leaves=true;
            }
            if(samples.Count==0)return double.NaN;
            rim=enters&&leaves;
            // A point on the front or the back of the block lies on its own entry or exit: the lines that meet that surface right at the point decide (at an edge the other
            // lines run past it into a neighbouring wall). Otherwise (a side wall) every line runs through the inside: the median.
            double nearest=ends.Min();
            if(nearestEnds&&nearest<=EndM){var kept=new List<double>();for(int k=0;k<samples.Count;k++)if(ends[k]<=nearest+.0001)kept.Add(samples[k]);samples.Clear();samples.AddRange(kept);}
            samples.Sort();return samples.Count%2==1?samples[samples.Count/2]:(samples[samples.Count/2-1]+samples[samples.Count/2])/2;
        }
        var depth=new double[unique.Count];var rims=new bool[unique.Count];var pieceOfPoint=new int[unique.Count];
        for(int q=0;q<unique.Count;q++)
        {
            depth[q]=double.NaN;int piece=pieceOfPoint[q]=PieceOf(unique[q]);if(piece<0)continue;
            depth[q]=Sample(unique[q],piece,true,out rims[q]);
        }
        // On a rim (a thin edge where the front and the back of the block meet) d is 0 for the front and 1 for the back: each point of the overlay there is measured
        // 0.5 mm into the triangles it belongs to, so that a front triangle gets the d of the front and a back triangle that of the back.
        var own=new Dictionary<int,double>();
        if(rims.Any(x=>x))
        {
            var toward=new Vector3d[vertices.Length];var users=new int[vertices.Length];
            for(int t=0;t+2<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(a==b||b==c||a==c)continue;
                var centre=(V(vertices[a])+V(vertices[b])+V(vertices[c]))/3;
                foreach(var i in new[]{a,b,c})if(i>=0&&i<vertices.Length&&rims[map[i]]){toward[i]+=centre;users[i]++;}
            }
            for(int i=0;i<vertices.Length;i++)
            {
                if(users[i]==0)continue;
                var p=unique[map[i]];var w=toward[i]/users[i]-p;double len=Math.Sqrt(Vector3d.Dot(w,w));if(len<1e-9)continue;
                var probe=p+w*(Math.Min(RimM,len*.5)/len);
                double d=Sample(probe,pieceOfPoint[map[i]],false,out _);
                if(!double.IsNaN(d)){own[i]=d;report.BandRim++;}
            }
        }
        // a point that no line holds: the d of the nearest point that has one
        const double C=.02;var cells=new Dictionary<(int,int,int),List<int>>();int valid=0;
        (int,int,int) Cell(Vector3d p)=>((int)Math.Floor(p.X/C),(int)Math.Floor(p.Y/C),(int)Math.Floor(p.Z/C));
        for(int q=0;q<unique.Count;q++)if(!double.IsNaN(depth[q])){valid++;var c=Cell(unique[q]);if(!cells.TryGetValue(c,out var l))cells[c]=l=new();l.Add(q);}
        if(valid==0){report.BandRefusal="no point of the overlay lies on a line through the shape";return null;}
        var filled=(double[])depth.Clone();
        for(int q=0;q<unique.Count;q++)
        {
            if(!double.IsNaN(depth[q]))continue;
            var p=unique[q];var c=Cell(p);int best=-1;double bd=double.PositiveInfinity;
            bool done=false;
            for(int r=0;r<=30&&!done;r++)
            {
                // the shell of cells at distance r
                for(int x=-r;x<=r;x++)for(int y=-r;y<=r;y++)
                {
                    bool edge=Math.Abs(x)==r||Math.Abs(y)==r;
                    for(int z=-r;z<=r;z+=edge?1:Math.Max(1,2*r))
                    {
                        if(!cells.TryGetValue((c.Item1+x,c.Item2+y,c.Item3+z),out var l))continue;
                        foreach(var o in l){var w=unique[o]-p;double dd=Vector3d.Dot(w,w);if(dd<bd){bd=dd;best=o;}}
                    }
                }
                done=best>=0&&Math.Sqrt(bd)<=r*C;
            }
            if(!done)for(int o=0;o<unique.Count;o++)if(!double.IsNaN(depth[o])){var w=unique[o]-p;double dd=Vector3d.Dot(w,w);if(dd<bd){bd=dd;best=o;}} // far away: every point
            if(best<0){report.BandRefusal="a point without a neighbour that has a depth";return null;}
            filled[q]=depth[best];report.BandNeighbour++;
        }
        var result=new float[vertices.Length];for(int i=0;i<result.Length;i++)result[i]=(float)(own.TryGetValue(i,out var r)?r:filled[map[i]]);
        return result;
    }
    private static Vector3d Norm(Vector3d a)=>a/Math.Sqrt(Vector3d.Dot(a,a));
    private static double Distance(Vector3d p,List<(Vector3d A,Vector3d B,Vector3d C)> fan)
    {
        double best=double.PositiveInfinity;
        foreach(var (a,b,c) in fan){var q=Closest(p,a,b,c)-p;best=Math.Min(best,Vector3d.Dot(q,q));}
        return best;
    }

    // ---------------- geometry (double precision) ----------------
    internal readonly record struct Vector3d(double X,double Y,double Z)
    {
        public static Vector3d operator+(Vector3d a,Vector3d b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vector3d operator-(Vector3d a,Vector3d b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vector3d operator*(Vector3d a,double s)=>new(a.X*s,a.Y*s,a.Z*s);
        public static Vector3d operator/(Vector3d a,double s)=>new(a.X/s,a.Y/s,a.Z/s);
        internal static double Dot(Vector3d a,Vector3d b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        internal static Vector3d Cross(Vector3d a,Vector3d b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        internal static Vector3d Min(Vector3d a,Vector3d b)=>new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
        internal static Vector3d Max(Vector3d a,Vector3d b)=>new(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
    }
    private static Vector3d V(Vector3 v)=>new(v.X,v.Y,v.Z);
    private static (Vector3d[] Points,int[][] Faces) Read(string json)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(json);var root=doc.RootElement;
        var flat=root.GetProperty("vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var points=new Vector3d[flat.Length/3];for(int i=0;i<points.Length;i++)points[i]=new(flat[3*i],flat[3*i+1],flat[3*i+2]);
        var faces=root.GetProperty("faces").EnumerateArray().Select(f=>f.GetProperty("v").EnumerateArray().Select(x=>x.GetInt32()).ToArray()).ToArray();
        return (points,faces);
    }
    // Three fixed directions that no drawn edge or face of a hand-made mesh lines up with; the majority of the three parities decides (a ray through an edge or corner
    // can count a crossing twice or not at all; two such rays at once for the same point are not expected).
    private static readonly Vector3d[] rays={new(.5773502691896258,.5773502691896258,.5773502691896258),new(.2672612419124244,-.5345224838248488,.8017837257372732),new(-.8164965809277261,.4082482904638631,.4082482904638631)};
    internal static bool Inside(Vector3d p,IReadOnlyList<(Vector3d A,Vector3d B,Vector3d C)> fan,Vector3d min,Vector3d max)
    {
        if(p.X<min.X||p.Y<min.Y||p.Z<min.Z||p.X>max.X||p.Y>max.Y||p.Z>max.Z)return false;
        int votes=0;
        foreach(var d in rays)
        {
            int crossings=0;
            foreach(var (a,b,c) in fan)if(Crosses(p,d,a,b,c))crossings++;
            if((crossings&1)==1)votes++;
        }
        return votes>=2;
    }
    private static bool Crosses(Vector3d o,Vector3d d,Vector3d a,Vector3d b,Vector3d c)
    {
        var e1=b-a;var e2=c-a;var p=Vector3d.Cross(d,e2);double det=Vector3d.Dot(e1,p);if(Math.Abs(det)<1e-18)return false;
        double inv=1/det;var s=o-a;double u=Vector3d.Dot(s,p)*inv;if(u<0||u>1)return false;
        var q=Vector3d.Cross(s,e1);double v=Vector3d.Dot(d,q)*inv;if(v<0||u+v>1)return false;
        return Vector3d.Dot(e2,q)*inv>0;
    }
    internal static bool Near(Vector3d p,IReadOnlyList<(Vector3d A,Vector3d B,Vector3d C)> triangles,double limit)
    {
        double l2=limit*limit;
        foreach(var (a,b,c) in triangles)
        {
            var q=Closest(p,a,b,c)-p;if(Vector3d.Dot(q,q)<=l2)return true;
        }
        return false;
    }
    /// <summary>The closest point of triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static Vector3d Closest(Vector3d p,Vector3d a,Vector3d b,Vector3d c)
    {
        var ab=b-a;var ac=c-a;var ap=p-a;double d1=Vector3d.Dot(ab,ap),d2=Vector3d.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
        var bp=p-b;double d3=Vector3d.Dot(ab,bp),d4=Vector3d.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        double vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        var cp=p-c;double d5=Vector3d.Dot(ab,cp),d6=Vector3d.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        double vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        double va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
        double den=1/(va+vb+vc);return a+ab*(vb*den)+ac*(vc*den);
    }
}
