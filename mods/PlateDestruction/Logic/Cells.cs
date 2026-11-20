namespace PlateDestructionP0;

// ================================================================================================
// 0.4.1: cells. A destructible plate side is split into cells of about CellSizeM in its own 2D plane; a blast breaks cells, not whole sides.
// Pure managed code (no Unity / Il2Cpp types) so the managed tests can run without the game.
// The rule "structure-space centroid of a triangle -> cell" lives in CellPlan only; the render meshes (high, low), the armour triangles and the
// attached parts all go through it, so visual and armour agree.
// ================================================================================================

/// <summary>Smooth seeded 2D value noise. The same position gives the same offset in every mesh, so a ragged cell edge is one coherent line.</summary>
public static class CellNoise
{
    private static uint Hash(uint seed,int x,int y,uint ch)
    {
        unchecked
        {
            uint h=seed*747796405u+(uint)x*2891336453u+(uint)y*3266489917u+ch*668265263u+1442695041u;
            h^=h>>15;h*=2246822519u;h^=h>>13;h*=3266489917u;h^=h>>16;return h;
        }
    }
    private static double Val(uint seed,int x,int y,uint ch)=>Hash(seed,x,y,ch)/4294967295.0*2.0-1.0;
    private static double Smooth(double t)=>t*t*(3-2*t);
    /// <summary>Value in -1..1, continuous in (u,v), feature size about <paramref name="feature"/> metres.</summary>
    public static double Noise(uint seed,double u,double v,uint channel,double feature=0.15)
    {
        double fu=u/feature,fv=v/feature;int x0=(int)Math.Floor(fu),y0=(int)Math.Floor(fv);
        double tx=Smooth(fu-x0),ty=Smooth(fv-y0);
        double a=Val(seed,x0,y0,channel),b=Val(seed,x0+1,y0,channel),c=Val(seed,x0,y0+1,channel),d=Val(seed,x0+1,y0+1,channel);
        double top=a+(b-a)*tx,bot=c+(d-c)*tx;return top+(bot-top)*ty;
    }
    public static (double Du,double Dv) Offset(uint seed,double u,double v,double amplitude)=>(Noise(seed,u,v,1)*amplitude,Noise(seed,u,v,2)*amplitude);
}

/// <summary>The cell grid of one side (or the whole plate) in structure space: the two largest extents of its bounds, split evenly into cells of about the wanted size.</summary>
public sealed class CellFrame
{
    public PlateSide Side{get;}
    public int AxisU{get;}public int AxisV{get;}
    public float U0{get;}public float V0{get;}
    public float SizeU{get;}public float SizeV{get;}
    public int Nu{get;}public int Nv{get;}
    public V3 Min{get;}public V3 Max{get;}
    public const int MaxCells=4096;
    public static float Axis(V3 p,int a)=>a==0?p.X:a==1?p.Y:p.Z;
    public CellFrame(PlateSide side,V3 min,V3 max,float cellSizeM)
    {
        Side=side;Min=min;Max=max;
        float ex=Math.Max(0,max.X-min.X),ey=Math.Max(0,max.Y-min.Y),ez=Math.Max(0,max.Z-min.Z);
        var ext=new[]{ex,ey,ez};
        var order=new[]{0,1,2};
        Array.Sort(order,(a,b)=>{int c=ext[b].CompareTo(ext[a]);return c!=0?c:a.CompareTo(b);}); // largest first, ties by axis number
        AxisU=order[0];AxisV=order[1];
        float size=Math.Max(0.05f,cellSizeM);
        float eu=ext[AxisU],ev=ext[AxisV];
        int nu,nv;
        while(true)
        {
            nu=Math.Max(1,(int)Math.Round(eu/size));nv=Math.Max(1,(int)Math.Round(ev/size));
            if(nu*nv<=MaxCells)break;
            size*=1.5f;
        }
        Nu=nu;Nv=nv;
        U0=Axis(min,AxisU);V0=Axis(min,AxisV);
        SizeU=Math.Max(1e-4f,eu/nu);SizeV=Math.Max(1e-4f,ev/nv);
    }
    public int Count=>Nu*Nv;
}

/// <summary>Cell assignment of one plate. Cell id = (side &lt;&lt; 20) | (iu + iv x Nu); side 0 = Whole.</summary>
public sealed class CellPlan
{
    public CellFrame?[] Frames{get;}
    public SideGeometry? Geo{get;}
    public uint Seed{get;}
    public double Jitter{get;}
    public V3 AllMin{get;}public V3 AllMax{get;}
    public CellPlan(CellFrame?[] frames,SideGeometry? geo,uint seed,double jitterM)
    {
        Frames=frames;Geo=geo;Seed=seed;Jitter=Math.Max(0,jitterM);
        float x0=float.MaxValue,y0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,y1=float.MinValue,z1=float.MinValue;
        foreach(var f in frames){if(f==null)continue;x0=Math.Min(x0,f.Min.X);y0=Math.Min(y0,f.Min.Y);z0=Math.Min(z0,f.Min.Z);x1=Math.Max(x1,f.Max.X);y1=Math.Max(y1,f.Max.Y);z1=Math.Max(z1,f.Max.Z);}
        if(x0>x1){x0=y0=z0=0;x1=y1=z1=0;}
        AllMin=new V3(x0,y0,z0);AllMax=new V3(x1,y1,z1);
    }
    public static int Encode(PlateSide side,int index)=>((int)side<<20)|index;
    public static PlateSide SideOf(int cell)=>(PlateSide)(cell>>20);
    public static int IndexOf(int cell)=>cell&0xFFFFF;
    public IEnumerable<PlateSide> Sides{get{for(int i=0;i<Frames.Length;i++)if(Frames[i]!=null)yield return (PlateSide)i;}}
    /// <summary>The side a structure-space point belongs to: the closer half for a mirrored plate with a split, else Whole.</summary>
    public PlateSide SideOfPoint(V3 p)=>Geo!=null?Geo.Choose(p):PlateSide.Whole;
    /// <summary>Cell of a structure-space point on the given side, or -1 when that side has no frame.</summary>
    public int CellOfPoint(V3 p,PlateSide side)
    {
        var f=Frames[(int)side];if(f==null)return -1;
        double u=CellFrame.Axis(p,f.AxisU),v=CellFrame.Axis(p,f.AxisV);
        var (du,dv)=Jitter>0?CellNoise.Offset(Seed,u,v,Jitter):(0.0,0.0);
        int iu=(int)Math.Floor((u+du-f.U0)/f.SizeU),iv=(int)Math.Floor((v+dv-f.V0)/f.SizeV);
        iu=Math.Min(Math.Max(iu,0),f.Nu-1);iv=Math.Min(Math.Max(iv,0),f.Nv-1);
        return Encode(side,iu+iv*f.Nu);
    }
    public int CellOfPoint(V3 p)=>CellOfPoint(p,SideOfPoint(p));
    /// <summary>The ONE assignment rule: the structure-space centroid of the triangle (computed in double, so the vertex order does not matter) decides.</summary>
    public int CellOfTriangle(V3[] v,int[] t,int tri)
    {
        int a=t[tri*3],b=t[tri*3+1],c=t[tri*3+2];
        if(a<0||b<0||c<0||a>=v.Length||b>=v.Length||c>=v.Length)return -1;
        var p=new V3((float)(((double)v[a].X+v[b].X+v[c].X)/3.0),(float)(((double)v[a].Y+v[b].Y+v[c].Y)/3.0),(float)(((double)v[a].Z+v[b].Z+v[c].Z)/3.0));
        return CellOfPoint(p);
    }
    public int[] Assign(V3[] v,int[] t)
    {
        var r=new int[t.Length/3];
        for(int i=0;i<r.Length;i++)r[i]=CellOfTriangle(v,t,i);
        return r;
    }
    /// <summary>True when the box lies inside the union of the frames (grown by a tolerance): the vertex space of a mesh is the structure space.</summary>
    public bool Covers(V3 min,V3 max,float tolerance)
        =>min.X>=AllMin.X-tolerance&&min.Y>=AllMin.Y-tolerance&&min.Z>=AllMin.Z-tolerance&&max.X<=AllMax.X+tolerance&&max.Y<=AllMax.Y+tolerance&&max.Z<=AllMax.Z+tolerance;
    /// <summary>Plan from the vertices and triangles of the (high detail) mesh. With a split geometry each half gets its own frame from its own triangles.</summary>
    public static CellPlan Build(V3[] v,int[] t,SideGeometry? geo,float cellSizeM,double jitterM,uint seed)
    {
        var frames=new CellFrame?[3];
        var mins=new V3[3];var maxs=new V3[3];var have=new bool[3];
        for(int i=0;i<3;i++){mins[i]=new V3(float.MaxValue,float.MaxValue,float.MaxValue);maxs[i]=new V3(float.MinValue,float.MinValue,float.MinValue);}
        for(int i=0;i<t.Length/3;i++)
        {
            int a=t[i*3],b=t[i*3+1],c=t[i*3+2];
            if(a<0||b<0||c<0||a>=v.Length||b>=v.Length||c>=v.Length)continue;
            var cen=new V3((v[a].X+v[b].X+v[c].X)/3f,(v[a].Y+v[b].Y+v[c].Y)/3f,(v[a].Z+v[b].Z+v[c].Z)/3f);
            int s=geo!=null?(int)geo.Choose(cen):0;
            var pa=v[a];var pb=v[b];var pc=v[c];
            mins[s]=new V3(Math.Min(mins[s].X,Math.Min(pa.X,Math.Min(pb.X,pc.X))),Math.Min(mins[s].Y,Math.Min(pa.Y,Math.Min(pb.Y,pc.Y))),Math.Min(mins[s].Z,Math.Min(pa.Z,Math.Min(pb.Z,pc.Z))));
            maxs[s]=new V3(Math.Max(maxs[s].X,Math.Max(pa.X,Math.Max(pb.X,pc.X))),Math.Max(maxs[s].Y,Math.Max(pa.Y,Math.Max(pb.Y,pc.Y))),Math.Max(maxs[s].Z,Math.Max(pa.Z,Math.Max(pb.Z,pc.Z))));
            have[s]=true;
        }
        for(int s=0;s<3;s++)if(have[s])frames[s]=new CellFrame((PlateSide)s,mins[s],maxs[s],cellSizeM);
        return new CellPlan(frames,geo,seed,jitterM);
    }
}

public static class BoxMath
{
    /// <summary>Distance from a point to an axis-aligned box (0 inside).</summary>
    public static double Distance(V3 p,V3 min,V3 max)
    {
        double dx=Math.Max(Math.Max(min.X-p.X,0),p.X-max.X),dy=Math.Max(Math.Max(min.Y-p.Y,0),p.Y-max.Y),dz=Math.Max(Math.Max(min.Z-p.Z,0),p.Z-max.Z);
        return Math.Sqrt(dx*dx+dy*dy+dz*dz);
    }
    public static V3 Closest(V3 p,V3 min,V3 max)=>new(Math.Min(Math.Max(p.X,min.X),max.X),Math.Min(Math.Max(p.Y,min.Y),max.Y),Math.Min(Math.Max(p.Z,min.Z),max.Z));
}

/// <summary>Bounds and local thickness/strength of the triangles of one cell (structure space).</summary>
public sealed class CellInfo{public V3 Min,Max;public int Tris;public double ThicknessMm,Strength;}

/// <summary>What one blast did to the cells of one plate.</summary>
public sealed class BlastOutcome
{
    public readonly List<int> NewBroken=new();
    /// <summary>Damage of THIS blast per cell in reach.</summary>
    public readonly Dictionary<int,double> Dmg=new();
    /// <summary>Per side: q = max D_cell/strength over the cells in reach that did NOT break (0..1).</summary>
    public readonly Dictionary<PlateSide,double> DentQ=new();
    /// <summary>Per side: max D_cell/strength over the cells that broke in this blast.</summary>
    public readonly Dictionary<PlateSide,double> BrokenRatio=new();
    public int Reached;
    public double MaxDmg;
}

/// <summary>Damage and broken state per cell of one plate. Session memory only. Broken is replaced as a whole (copy on write), so the armour build can read it from any thread.</summary>
public sealed class PlateCells
{
    public CellPlan Plan{get;}
    public Dictionary<int,CellInfo> Info{get;}
    public double Strength{get;}
    public double MinStrength{get;}
    public double MaxStrength{get;}
    private readonly Dictionary<int,double> damage=new();
    private HashSet<int> broken=new();
    public PlateCells(CellPlan plan,Dictionary<int,CellInfo> info,double strength)
    {
        Plan=plan;Info=info;Strength=strength;
        if(info.Count==0){MinStrength=strength;MaxStrength=strength;}
        else
        {
            double lo=double.MaxValue,hi=0;
            foreach(var ci in info.Values){double s=ci.Strength>0?ci.Strength:strength;if(s<lo)lo=s;if(s>hi)hi=s;}
            MinStrength=lo<=hi?lo:strength;
            MaxStrength=hi>0?hi:strength;
        }
    }
    public double StrengthOf(int cell)=>Info.TryGetValue(cell,out var ci)&&ci.Strength>0?ci.Strength:Strength;
    public IReadOnlySet<int> Broken=>broken;
    public int BrokenCount=>broken.Count;
    public int Total=>Info.Count;
    public bool IsBroken(int cell)=>broken.Contains(cell);
    public double Damage(int cell)=>damage.TryGetValue(cell,out var d)?d:0;
    private void Mark(IEnumerable<int> cells){var n=new HashSet<int>(broken);foreach(var c in cells)n.Add(c);broken=n;}
    public int CountIn(PlateSide side)=>Info.Keys.Count(c=>CellPlan.SideOf(c)==side);
    public int BrokenIn(PlateSide side)=>Info.Keys.Count(c=>CellPlan.SideOf(c)==side&&broken.Contains(c));
    public bool AllBroken(PlateSide side){int n=CountIn(side);return n>0&&BrokenIn(side)==n;}
    public List<int> Unbroken(PlateSide side)=>Info.Keys.Where(c=>CellPlan.SideOf(c)==side&&!broken.Contains(c)).OrderBy(c=>c).ToList();
    /// <summary>Test key / forced tear-off: marks every unbroken cell of a side as broken and returns them.</summary>
    public List<int> BreakAll(PlateSide side){var l=Unbroken(side);Mark(l);return l;}
    /// <summary>One blast at a structure-space point. D_cell = BlastBase x (cal/100)^3 x falloff(d_cell), d_cell = distance to the bounds of the cell's triangles. Damage accumulates per cell; a cell with damage >= cell strength breaks.</summary>
    public BlastOutcome Blast(V3 p,double calibreMm,double blastBase,double radiusPer100,double minCalibreMm,bool breakEnabled)
    {
        var o=new BlastOutcome();var newly=new List<int>();
        foreach(var kv in Info)
        {
            int c=kv.Key;if(broken.Contains(c))continue;
            double d=BoxMath.Distance(p,kv.Value.Min,kv.Value.Max);
            double dmg=BlastMath.Damage(blastBase,calibreMm,d,radiusPer100,minCalibreMm);
            if(dmg<=0)continue;
            o.Reached++;o.Dmg[c]=dmg;o.MaxDmg=Math.Max(o.MaxDmg,dmg);
            double total=Damage(c)+dmg;damage[c]=total;
            double cStr=kv.Value.Strength>0?kv.Value.Strength:Strength;
            var side=CellPlan.SideOf(c);
            if(breakEnabled&&cStr>0&&total>=cStr)
            {
                newly.Add(c);
                double r=dmg/cStr;o.BrokenRatio[side]=o.BrokenRatio.TryGetValue(side,out var old)?Math.Max(old,r):r;
            }
            else
            {
                double q=DentMath.BlastRatio(dmg,cStr);
                o.DentQ[side]=o.DentQ.TryGetValue(side,out var oq)?Math.Max(oq,q):q;
            }
        }
        newly.Sort();Mark(newly);o.NewBroken.AddRange(newly);
        return o;
    }
}

public static class CellMask
{
    /// <summary>A vertex is alive when at least one triangle that uses it is in an unbroken cell. A dent moves only alive vertices.</summary>
    public static bool[] AliveVertices(int vertexCount,int[] tris,int[] triCell,Func<int,bool> isBroken)
    {
        var alive=new bool[vertexCount];
        for(int i=0;i<tris.Length/3&&i<triCell.Length;i++)
        {
            int c=triCell[i];if(c>=0&&isBroken(c))continue;
            for(int k=0;k<3;k++){int ix=tris[i*3+k];if(ix>=0&&ix<vertexCount)alive[ix]=true;}
        }
        return alive;
    }
    /// <summary>Triangle numbers whose cell is NOT broken (invalid triangles, cell -1, are kept out).</summary>
    public static List<int> KeepUnbroken(int[] triCell,Func<int,bool> isBroken)
    {
        var r=new List<int>();for(int i=0;i<triCell.Length;i++){int c=triCell[i];if(c>=0&&!isBroken(c))r.Add(i);}return r;
    }
}

public static class ArmourCells
{
    /// <summary>The triangle numbers (0-based, in order) of <paramref name="candidates"/> (null = all) that stay: invalid triangles and those in a broken cell go.</summary>
    public static List<int> Keep(V3[] v,int[] t,CellPlan plan,IReadOnlySet<int> broken,IReadOnlyList<int>? candidates)
    {
        var res=new List<int>();
        int n=t.Length/3;
        if(candidates==null){for(int i=0;i<n;i++){int c=plan.CellOfTriangle(v,t,i);if(c>=0&&!broken.Contains(c))res.Add(i);}}
        else foreach(int i in candidates){int c=plan.CellOfTriangle(v,t,i);if(c>=0&&!broken.Contains(c))res.Add(i);}
        return res;
    }
}

public static class BoxCull
{
    /// <summary>Triangle numbers whose centroid, taken through <paramref name="toBox"/> (mesh space to the box space), lies inside the box grown by a tolerance. Used to find the triangles of a part inside a merged mesh.</summary>
    public static List<int> Inside(V3[] v,int[] t,Func<V3,V3> toBox,V3 min,V3 max,float tol)
    {
        var r=new List<int>();
        for(int i=0;i+2<t.Length;i+=3)
        {
            int a=t[i],b=t[i+1],c=t[i+2];if(a<0||b<0||c<0||a>=v.Length||b>=v.Length||c>=v.Length)continue;
            var cen=toBox(new V3((float)(((double)v[a].X+v[b].X+v[c].X)/3.0),(float)(((double)v[a].Y+v[b].Y+v[c].Y)/3.0),(float)(((double)v[a].Z+v[b].Z+v[c].Z)/3.0)));
            if(cen.X>=min.X-tol&&cen.X<=max.X+tol&&cen.Y>=min.Y-tol&&cen.Y<=max.Y+tol&&cen.Z>=min.Z-tol&&cen.Z<=max.Z+tol)r.Add(i/3);
        }
        return r;
    }
}
