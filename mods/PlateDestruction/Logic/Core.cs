using System.Text;
namespace PlateDestructionP0;

// Pure logic (no Unity / Il2Cpp types) so the managed tests can run without the game.

/// <summary>Hit-log budget: at most MaxLines detail lines per session, then only a summary every SummaryEvery shots.</summary>
public sealed class LogBudget
{
    public int MaxLines{get;}
    public int SummaryEvery{get;}
    public int Lines{get;private set;}
    public int Suppressed{get;private set;}
    public int Shots{get;private set;}
    public LogBudget(int maxLines=200,int summaryEvery=50){MaxLines=Math.Max(0,maxLines);SummaryEvery=Math.Max(1,summaryEvery);}
    /// <summary>True if one more detail line may be written (and counts it). False = suppressed (counted).</summary>
    public bool TryLine(){if(Lines<MaxLines){Lines++;return true;}Suppressed++;return false;}
    public bool Exhausted=>Lines>=MaxLines;
    /// <summary>Call once per shot. Returns true when a summary line should be written (only after the line limit is reached).</summary>
    public bool Shot(){Shots++;return Exhausted&&Shots%SummaryEvery==0;}
    public void Reset(){Lines=0;Suppressed=0;Shots=0;}
}

/// <summary>Which half of a plate: Whole = an unmirrored plate (or the whole pair), Original = the modelled half, Mirror = its mirror image. Values are bit flags.</summary>
public enum PlateSide{Whole=0,Original=1,Mirror=2}

/// <summary>Session-only state: excluded (route A) and hidden (visual) plates, keyed by vehicle root pointer + VUID. Never persisted.</summary>
public sealed class PlateState
{
    private readonly HashSet<(long Root,int Vuid)> excluded=new();
    private readonly HashSet<(long Root,int Vuid)> hidden=new();
    private readonly Dictionary<(long Root,int Vuid),int> sides=new();
    /// <summary>Per-side break flags of mirrored plates (bit 1 = original half, bit 2 = mirrored half). Session only.</summary>
    public int SideCount=>sides.Count;
    public bool IsSideBroken(long root,int vuid,PlateSide side)=>side!=PlateSide.Whole&&sides.TryGetValue((root,vuid),out var f)&&(f&(int)side)!=0;
    public bool AnySideBroken(long root,int vuid)=>sides.Count>0&&sides.TryGetValue((root,vuid),out var f)&&f!=0;
    public bool SetSideBroken(long root,int vuid,PlateSide side,bool on)
    {
        if(side==PlateSide.Whole)return false;
        sides.TryGetValue((root,vuid),out var f);int n=on?f|(int)side:f&~(int)side;
        if(n==f)return false;
        if(n==0)sides.Remove((root,vuid));else sides[(root,vuid)]=n;
        return true;
    }
    public (long Root,int Vuid)? Picked{get;private set;}
    public int ExcludedCount=>excluded.Count;
    public int HiddenCount=>hidden.Count;
    public bool IsExcluded(long root,int vuid)=>excluded.Count>0&&excluded.Contains((root,vuid));
    public bool IsHidden(long root,int vuid)=>hidden.Contains((root,vuid));
    public void Pick(long root,int vuid){Picked=(root,vuid);}
    /// <summary>Toggle; returns the new state (true = now excluded).</summary>
    public bool ToggleExcluded(long root,int vuid){var k=(root,vuid);if(!excluded.Remove(k)){excluded.Add(k);ClearPickIfIdle();return true;}ClearPickIfIdle();return false;}
    public bool ToggleHidden(long root,int vuid){var k=(root,vuid);if(!hidden.Remove(k)){hidden.Add(k);ClearPickIfIdle();return true;}ClearPickIfIdle();return false;}
    /// <summary>Idempotent set; returns true when the state changed.</summary>
    public bool SetExcluded(long root,int vuid,bool on){var k=(root,vuid);bool ch=on?excluded.Add(k):excluded.Remove(k);ClearPickIfIdle();return ch;}
    public bool SetHidden(long root,int vuid,bool on){var k=(root,vuid);bool ch=on?hidden.Add(k):hidden.Remove(k);ClearPickIfIdle();return ch;}
    private void ClearPickIfIdle(){if(excluded.Count==0&&hidden.Count==0)Picked=null;}
    public IReadOnlyCollection<(long Root,int Vuid)> HiddenItems=>hidden.ToArray();
    /// <summary>Vehicle released: forget everything of that root.</summary>
    public void ClearRoot(long root){excluded.RemoveWhere(k=>k.Root==root);hidden.RemoveWhere(k=>k.Root==root);foreach(var k in sides.Keys.Where(k=>k.Root==root).ToArray())sides.Remove(k);if(Picked is {} p&&p.Root==root)Picked=null;ClearPickIfIdle();}
    public void ClearAll(){excluded.Clear();hidden.Clear();sides.Clear();Picked=null;}
}

/// <summary>Detects Play-to-Edit and new-battle transitions from (playing, scene handle) samples.</summary>
public sealed class SessionTracker
{
    private bool playing;
    private int scene=int.MinValue;
    /// <summary>Returns a reset reason ("edit", "new battle") or null.</summary>
    public string? Update(bool nowPlaying,int sceneHandle)
    {
        string? reason=null;
        if(playing&&!nowPlaying)reason="edit";
        else if(nowPlaying&&playing&&sceneHandle!=scene)reason="new battle";
        playing=nowPlaying;scene=sceneHandle;
        return reason;
    }

    /// <summary>0.6.0: Photo Mode aware transition detector. Unloading VehicleControlUI during Photo Mode or async transition frames never resets an active session; only entering Designer/MainMenu or changing active scene resets.</summary>
    public string? Update(bool hasControlUI,bool hasDesignerUI,bool hasMainMenu,bool hasPhotoUI,int sceneHandle,out bool inSession,out bool inPhotomode)
    {
        string? reason=null;
        bool leaving=hasDesignerUI||hasMainMenu;
        if(playing)
        {
            if(hasDesignerUI){reason="edit";playing=false;}
            else if(hasMainMenu){reason="menu";playing=false;}
            else if(sceneHandle!=scene){reason="new battle";playing=hasControlUI&&!leaving;}
            else playing=true;
        }
        else
        {
            if(hasControlUI&&!leaving)playing=true;
        }
        scene=sceneHandle;
        inSession=playing;
        inPhotomode=playing&&(!hasControlUI||hasPhotoUI);
        return reason;
    }
}

/// <summary>Per-shot hit tally for the periodic summary.</summary>
public sealed class HitTally
{
    private readonly Dictionary<int,int> hits=new();
    public int Total{get;private set;}
    public void Add(int vuid){hits.TryGetValue(vuid,out var n);hits[vuid]=n+1;Total++;}
    public void Clear(){hits.Clear();Total=0;}
    public string Top(int count=5)
    {
        var sb=new StringBuilder();
        foreach(var kv in hits.OrderByDescending(k=>k.Value).ThenBy(k=>k.Key).Take(count)){if(sb.Length>0)sb.Append(", ");sb.Append("vuid ").Append(kv.Key).Append('=').Append(kv.Value);}
        return sb.ToString();
    }
}

public enum TargetMode{FirstPlate,LastPlate}

/// <summary>Chooses which eligible component of one shot's penetrator path is the target.</summary>
public static class TargetChooser
{
    /// <summary>Path = eligibility per hit object in hitObjects order (entry first). Returns the index or -1 when none is eligible.</summary>
    public static int Choose(IReadOnlyList<bool> eligible,TargetMode mode)
    {
        if(mode==TargetMode.LastPlate){for(int i=eligible.Count-1;i>=0;i--)if(eligible[i])return i;return -1;}
        for(int i=0;i<eligible.Count;i++)if(eligible[i])return i;
        return -1;
    }
}

/// <summary>The "[destructible]" token in the native structure description (the only storage of the Destructable checkbox).</summary>
public static class DestructibleFlag
{
    public const string Token="[destructible]";
    /// <summary>True when the description contains the token (case-insensitive).</summary>
    public static bool Has(string? description)=>!string.IsNullOrEmpty(description)&&description!.Contains(Token,StringComparison.OrdinalIgnoreCase);
    /// <summary>Tick: append the token after any existing text. Untick: remove only the token (and the spaces around it). Returns null when nothing is left.</summary>
    public static string? Set(string? description,bool on)
    {
        if(on)
        {
            if(Has(description))return description;
            return string.IsNullOrWhiteSpace(description)?Token:description!.TrimEnd()+" "+Token;
        }
        if(string.IsNullOrEmpty(description))return description;
        if(!Has(description))return description;
        var text=System.Text.RegularExpressions.Regex.Replace(description!,@"(\s*"+System.Text.RegularExpressions.Regex.Escape(Token)+@"\s*)+"," ",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return text.Length==0?null:text;
    }
}

/// <summary>Damage per hit and plate strength (DESIGN.md section 2). Pure arithmetic, tuned by config.</summary>
public static class DamageFormula
{
    public const double ReferenceAreaM2=0.0625;
    /// <summary>Kinetic energy in MJ from penetrator mass (kg) and speed (m/s).</summary>
    public static double KineticMJ(double massKg,double speedMs)=>massKg>0&&speedMs>0?0.5*massKg*speedMs*speedMs/1e6:0;
    public static double Kinetic(double massKg,double speedMs,double perMJ=10)=>perMJ*KineticMJ(massKg,speedMs);
    /// <summary>D_blast = base x (calibre/100 mm)^3 x weight. Weight 0 (unknown or kinetic shell) gives 0.</summary>
    public static double Blast(double calibreMm,double weight,double baseValue=100)
    {
        if(calibreMm<=0||weight<=0)return 0;
        var k=calibreMm/100.0;return baseValue*k*k*k*weight;
    }
    public static double AreaFactor(double areaM2)=>Math.Max(1.0,areaM2/ReferenceAreaM2);
    /// <summary>StrengthPerMm x thickness (mm) x area factor; the fallback when the thickness is unknown.</summary>
    public static double Strength(double strengthPerMm,double thicknessMm,double areaM2,double defaultStrength)
    {
        if(!(thicknessMm>0)||double.IsNaN(thicknessMm)||double.IsInfinity(thicknessMm)||!(strengthPerMm>0))return defaultStrength;
        double a=double.IsNaN(areaM2)||double.IsInfinity(areaM2)?0:areaM2;
        return strengthPerMm*thicknessMm*AreaFactor(a);
    }
    /// <summary>Mean vertex thickness (m, or mm if the value is above 1) and solid volume (m3) to average thickness (mm) and area (m2). Null when unusable.</summary>
    public static (double ThicknessMm,double AreaM2)? FromMesh(double meanThickness,double solidVolume)
    {
        if(!(meanThickness>0)||!(solidVolume>0)||double.IsInfinity(meanThickness)||double.IsInfinity(solidVolume))return null;
        double m=meanThickness>1.0?meanThickness/1000.0:meanThickness;
        double area=solidVolume/m;
        if(!(area>1e-4)||area>100)return null;
        return (m*1000.0,area);
    }
}

/// <summary>Damage counters per plate and side (vehicle root + VUID + side). Session memory only. Side Whole = unmirrored plate.</summary>
public sealed class DamageLedger
{
    private sealed class Entry{internal int Hits;internal double Total;internal bool Broken;}
    private readonly Dictionary<(long Root,int Vuid,int Side),Entry> map=new();
    public int BrokenCount=>map.Values.Count(e=>e.Broken);
    public bool IsBroken(long root,int vuid)=>IsBroken(root,vuid,PlateSide.Whole);
    public bool IsBroken(long root,int vuid,PlateSide side)=>map.TryGetValue((root,vuid,(int)side),out var e)&&e.Broken;
    /// <summary>True when the whole plate is gone: the Whole entry is broken, or both halves are.</summary>
    public bool IsGone(long root,int vuid)=>IsBroken(root,vuid,PlateSide.Whole)||(IsBroken(root,vuid,PlateSide.Original)&&IsBroken(root,vuid,PlateSide.Mirror));
    public int Hits(long root,int vuid)=>Hits(root,vuid,PlateSide.Whole);
    public int Hits(long root,int vuid,PlateSide side)=>map.TryGetValue((root,vuid,(int)side),out var e)?e.Hits:0;
    public double Total(long root,int vuid)=>map.TryGetValue((root,vuid,0),out var e)?e.Total:0;
    public bool Add(long root,int vuid,double damage,double strength,out int hits,out double total)=>Add(root,vuid,PlateSide.Whole,damage,strength,out hits,out total);
    /// <summary>Adds one hit to one side. Returns true when this hit takes that side over its strength (the side is then marked broken). Hits on a broken side are ignored (false).</summary>
    public bool Add(long root,int vuid,PlateSide side,double damage,double strength,out int hits,out double total)
    {
        var k=(root,vuid,(int)side);
        if(!map.TryGetValue(k,out var e)){e=new Entry();map[k]=e;}
        if(e.Broken){hits=e.Hits;total=e.Total;return false;}
        e.Hits++;e.Total+=Math.Max(0,damage);hits=e.Hits;total=e.Total;
        if(e.Total>=strength){e.Broken=true;return true;}
        return false;
    }
    /// <summary>Forces the broken state (test key). Returns false if it was already broken.</summary>
    public bool ForceBreak(long root,int vuid)=>ForceBreak(root,vuid,PlateSide.Whole);
    public bool ForceBreak(long root,int vuid,PlateSide side)
    {
        var k=(root,vuid,(int)side);
        if(!map.TryGetValue(k,out var e)){e=new Entry();map[k]=e;}
        if(e.Broken)return false;e.Broken=true;return true;
    }
    public void ClearRoot(long root){foreach(var k in map.Keys.Where(k=>k.Root==root).ToArray())map.Remove(k);}
    public void Clear()=>map.Clear();
}

public enum BreakStep{HideDecals,HidePlate,PlayExplosion,RemoveFromModel,Log,HideSideDecals,HideSideMesh,RaiseSide,HideAttached,HideCellDecals,HideCellMesh,RaiseCells}

/// <summary>The fixed order of a plate break. A failing step is reported and does not stop the later ones.</summary>
public static class BreakPipeline
{
    public static readonly IReadOnlyList<BreakStep> Order=new[]{BreakStep.HideDecals,BreakStep.HidePlate,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RemoveFromModel,BreakStep.Log};
    /// <summary>One half of a mirrored plate: the decals and mesh of that half only, then the explosion, then the rebuild of the damage model.</summary>
    public static readonly IReadOnlyList<BreakStep> SideOrder=new[]{BreakStep.HideSideDecals,BreakStep.HideSideMesh,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RaiseSide,BreakStep.Log};
    /// <summary>0.4.1: some cells of a side or plate break: their decals and triangles go, their attached parts, the explosion, then the rebuild of the damage model.</summary>
    public static readonly IReadOnlyList<BreakStep> CellOrder=new[]{BreakStep.HideCellDecals,BreakStep.HideCellMesh,BreakStep.HideAttached,BreakStep.PlayExplosion,BreakStep.RaiseCells,BreakStep.Log};
    /// <summary>Runs every step in order. Returns the steps that threw (with the message).</summary>
    public static List<(BreakStep Step,string Error)> Run(Action<BreakStep> execute)=>Run(execute,Order);
    public static List<(BreakStep Step,string Error)> Run(Action<BreakStep> execute,IReadOnlyList<BreakStep> order)
    {
        var failed=new List<(BreakStep,string)>();
        foreach(var s in order){try{execute(s);}catch(Exception e){failed.Add((s,e.Message));}}
        return failed;
    }
}

/// <summary>Which break a side hit leads to: only that half, or (non-mirrored, per-side not possible, or the other half already gone) the whole plate (route A).</summary>
public static class SideBreak
{
    public static PlateSide Resolve(bool perSide,PlateSide side,bool otherSideBroken)=>(!perSide||side==PlateSide.Whole||otherSideBroken)?PlateSide.Whole:side;
    public static PlateSide Other(PlateSide side)=>side==PlateSide.Original?PlateSide.Mirror:side==PlateSide.Mirror?PlateSide.Original:PlateSide.Whole;
}

public readonly struct V3
{
    public readonly float X,Y,Z;
    public V3(float x,float y,float z){X=x;Y=y;Z=z;}
}

/// <summary>4x4 matrix, row-major, column vectors (Unity convention: translation in column 3). Pure managed so it can be tested.</summary>
public sealed class Mat4
{
    public readonly float[] M;
    public Mat4(float[] m){if(m==null||m.Length!=16)throw new ArgumentException("16 values expected");M=(float[])m.Clone();}
    public static Mat4 Identity()=>new(new float[]{1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1});
    /// <summary>Mirror in the plane through (px,py,pz) with unit normal (nx,ny,nz).</summary>
    public static Mat4 Reflection(float nx,float ny,float nz,float px,float py,float pz)
    {
        float d=-(nx*px+ny*py+nz*pz);
        return new Mat4(new[]{1-2*nx*nx,-2*nx*ny,-2*nx*nz,-2*nx*d, -2*ny*nx,1-2*ny*ny,-2*ny*nz,-2*ny*d, -2*nz*nx,-2*nz*ny,1-2*nz*nz,-2*nz*d, 0,0,0,1});
    }
    public V3 Point(V3 p)=>new(M[0]*p.X+M[1]*p.Y+M[2]*p.Z+M[3],M[4]*p.X+M[5]*p.Y+M[6]*p.Z+M[7],M[8]*p.X+M[9]*p.Y+M[10]*p.Z+M[11]);
    public V3 Dir(V3 p)=>new(M[0]*p.X+M[1]*p.Y+M[2]*p.Z,M[4]*p.X+M[5]*p.Y+M[6]*p.Z,M[8]*p.X+M[9]*p.Y+M[10]*p.Z);
    /// <summary>Determinant of the upper 3x3 block (negative = reflection).</summary>
    public float Det3()=>M[0]*(M[5]*M[10]-M[6]*M[9])-M[1]*(M[4]*M[10]-M[6]*M[8])+M[2]*(M[4]*M[9]-M[5]*M[8]);
    public static int Sign(float det)=>det<0?-1:1;
}

/// <summary>The two halves of a mirrored plate in structure space: the original half (bounds of the source mesh) and its mirror image (those bounds times the mirror matrix).</summary>
public sealed class SideGeometry
{
    public V3 OrigMin{get;}public V3 OrigMax{get;}public V3 MirrMin{get;}public V3 MirrMax{get;}
    public Mat4 Mirror{get;}
    public SideGeometry(Mat4 mirror,V3 origMin,V3 origMax)
    {
        Mirror=mirror;OrigMin=origMin;OrigMax=origMax;
        float x0=float.MaxValue,y0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,y1=float.MinValue,z1=float.MinValue;
        for(int i=0;i<8;i++)
        {
            var c=mirror.Point(new V3((i&1)==0?origMin.X:origMax.X,(i&2)==0?origMin.Y:origMax.Y,(i&4)==0?origMin.Z:origMax.Z));
            x0=Math.Min(x0,c.X);y0=Math.Min(y0,c.Y);z0=Math.Min(z0,c.Z);x1=Math.Max(x1,c.X);y1=Math.Max(y1,c.Y);z1=Math.Max(z1,c.Z);
        }
        MirrMin=new V3(x0,y0,z0);MirrMax=new V3(x1,y1,z1);
    }
    private static float DistSq(V3 p,V3 min,V3 max)
    {
        float dx=Math.Max(Math.Max(min.X-p.X,0),p.X-max.X),dy=Math.Max(Math.Max(min.Y-p.Y,0),p.Y-max.Y),dz=Math.Max(Math.Max(min.Z-p.Z,0),p.Z-max.Z);
        return dx*dx+dy*dy+dz*dz;
    }
    private static float CenterSq(V3 p,V3 min,V3 max){float cx=(min.X+max.X)/2-p.X,cy=(min.Y+max.Y)/2-p.Y,cz=(min.Z+max.Z)/2-p.Z;return cx*cx+cy*cy+cz*cz;}
    /// <summary>The half whose bounds are closer to the point (structure space). A tie (both zero or equal) is decided by the distance to the bounds centres; a full tie is Original.</summary>
    public PlateSide Choose(V3 p)
    {
        float a=DistSq(p,OrigMin,OrigMax),b=DistSq(p,MirrMin,MirrMax);
        if(Math.Abs(a-b)<=1e-12f){a=CenterSq(p,OrigMin,OrigMax);b=CenterSq(p,MirrMin,MirrMax);}
        return b<a?PlateSide.Mirror:PlateSide.Original;
    }
    private static V3 Clamp(V3 p,V3 min,V3 max)=>new(Math.Min(Math.Max(p.X,min.X),max.X),Math.Min(Math.Max(p.Y,min.Y),max.Y),Math.Min(Math.Max(p.Z,min.Z),max.Z));
    /// <summary>Closest point of one half's bounds to the point (structure space). Whole is not a half: the original half is used.</summary>
    public V3 Closest(V3 p,PlateSide side)=>side==PlateSide.Mirror?Clamp(p,MirrMin,MirrMax):Clamp(p,OrigMin,OrigMax);
    /// <summary>Bounds (structure space) of one half.</summary>
    public (V3 Min,V3 Max) Bounds(PlateSide side)=>side==PlateSide.Mirror?(MirrMin,MirrMax):(OrigMin,OrigMax);
    /// <summary>Distance (m, structure space) from the point to the closest point of one half's bounds.</summary>
    public float Distance(V3 p,PlateSide side)=>(float)Math.Sqrt(DistSq(p,side==PlateSide.Mirror?MirrMin:OrigMin,side==PlateSide.Mirror?MirrMax:OrigMax));
    /// <summary>True when the box lies inside the union box of both halves (grown by a tolerance): the vertex space of a mesh matches the structure space.</summary>
    public bool Covers(V3 min,V3 max,float tolerance)
    {
        float x0=Math.Min(OrigMin.X,MirrMin.X)-tolerance,y0=Math.Min(OrigMin.Y,MirrMin.Y)-tolerance,z0=Math.Min(OrigMin.Z,MirrMin.Z)-tolerance;
        float x1=Math.Max(OrigMax.X,MirrMax.X)+tolerance,y1=Math.Max(OrigMax.Y,MirrMax.Y)+tolerance,z1=Math.Max(OrigMax.Z,MirrMax.Z)+tolerance;
        return min.X>=x0&&min.Y>=y0&&min.Z>=z0&&max.X<=x1&&max.Y<=y1&&max.Z<=z1;
    }
}

/// <summary>Triangle bookkeeping for the armour build and the managed mirror copy of a mesh.</summary>
public static class SideTriangles
{
    public static PlateSide Side(V3[] v,int[] t,int tri,SideGeometry g)
    {
        int a=t[tri*3],b=t[tri*3+1],c=t[tri*3+2];
        var p=new V3((v[a].X+v[b].X+v[c].X)/3f,(v[a].Y+v[b].Y+v[c].Y)/3f,(v[a].Z+v[b].Z+v[c].Z)/3f);
        return g.Choose(p);
    }
    private static bool Valid(V3[] v,int[] t,int tri){int a=t[tri*3],b=t[tri*3+1],c=t[tri*3+2];return a>=0&&b>=0&&c>=0&&a<v.Length&&b<v.Length&&c<v.Length;}
    public static (int Original,int Mirror) Count(V3[] v,int[] t,SideGeometry g)
    {
        int o=0,m=0;
        for(int i=0;i<t.Length/3;i++){if(!Valid(v,t,i))continue;if(Side(v,t,i,g)==PlateSide.Mirror)m++;else o++;}
        return (o,m);
    }
    /// <summary>Numbers (0-based) of the triangles whose centroid lies on the given side; triangles with invalid indices are dropped.</summary>
    public static List<int> KeepIndices(V3[] v,int[] t,SideGeometry g,PlateSide keep)
    {
        var res=new List<int>();
        for(int i=0;i<t.Length/3;i++)
        {
            if(!Valid(v,t,i))continue;
            if(Side(v,t,i,g)==keep)res.Add(i);
        }
        return res;
    }
    /// <summary>The index list of the given triangle numbers, in order.</summary>
    public static int[] Gather(int[] t,IReadOnlyList<int> tris)
    {
        var res=new int[tris.Count*3];
        for(int k=0;k<tris.Count;k++){int i=tris[k];res[k*3]=t[i*3];res[k*3+1]=t[i*3+1];res[k*3+2]=t[i*3+2];}
        return res;
    }
    /// <summary>Copy of the index list that keeps only the triangles whose centroid lies on the given side. Vertices are not touched.</summary>
    public static int[] Keep(V3[] v,int[] t,SideGeometry g,PlateSide keep)=>Gather(t,KeepIndices(v,t,g,keep));
    /// <summary>Reverses the winding of every triangle (swaps the 2nd and 3rd index). A mirrored mesh needs this to keep its faces pointing outward.</summary>
    public static int[] FlipWinding(int[] t)
    {
        var r=(int[])t.Clone();
        for(int i=0;i+2<r.Length;i+=3){(r[i+1],r[i+2])=(r[i+2],r[i+1]);}
        return r;
    }
    public static V3[] Transform(V3[] v,Mat4 m){var r=new V3[v.Length];for(int i=0;i<v.Length;i++)r[i]=m.Point(v[i]);return r;}
    public static V3[] TransformDirs(V3[] v,Mat4 m){var r=new V3[v.Length];for(int i=0;i<v.Length;i++)r[i]=m.Dir(v[i]);return r;}
}

public enum ArmourAction{Pass,Skip,Filter}

/// <summary>What to do with one AddMesh call of a mirrored plate that has a broken half: the armour of the broken half is dropped, the other half stays.</summary>
public static class ArmourRule
{
    /// <param name="mirrorHanded">The call's matrix determinant has the opposite sign to the plate's own: the native code adds the mirrored half as its own call.</param>
    /// <param name="origTris">Triangles of this call (by centroid) on the original half.</param>
    /// <param name="mirrTris">Triangles of this call on the mirrored half.</param>
    public static (ArmourAction Action,PlateSide Keep) Decide(bool mirrorHanded,int origTris,int mirrTris,bool brokenOrig,bool brokenMirror)
    {
        if(!brokenOrig&&!brokenMirror)return (ArmourAction.Pass,PlateSide.Whole);
        if(brokenOrig&&brokenMirror)return (ArmourAction.Skip,PlateSide.Whole);
        if(mirrorHanded)return brokenMirror?(ArmourAction.Skip,PlateSide.Whole):(ArmourAction.Pass,PlateSide.Whole);
        var drop=brokenOrig?PlateSide.Original:PlateSide.Mirror;
        int dropCount=drop==PlateSide.Original?origTris:mirrTris,keepCount=drop==PlateSide.Original?mirrTris:origTris;
        if(dropCount==0)return (ArmourAction.Pass,PlateSide.Whole);
        if(keepCount==0)return (ArmourAction.Skip,PlateSide.Whole);
        return (ArmourAction.Filter,SideBreak.Other(drop));
    }
}

/// <summary>How the optional marker arrays of AddMesh/9 relate to the mesh: one entry per triangle, one per vertex, an independent list, absent, or an inconsistent mixture (then no triangle filtering is done).</summary>
public enum MarkerLayout{None,PerTriangle,PerVertex,Independent,Unknown}

public static class MarkerFilter
{
    /// <summary>Decides from the array lengths (0 = absent). Every present array has indices/3 entries: PerTriangle (filtered with the triangles). Every present array has the vertex count: PerVertex.
    /// Every present array has a length that matches neither: Independent (a separate marker list, e.g. 6 points; triangles are filtered, the markers stay as they are).
    /// A mixture (one array per triangle, the other of another length) cannot be filtered consistently: Unknown.</summary>
    public static MarkerLayout Classify(int markers,int markerTypes,int vertices,int indices)
    {
        int tris=indices%3==0?indices/3:-1;
        if(markers<=0&&markerTypes<=0)return MarkerLayout.None;
        int Kind(int n)=>n<=0?0:(n==tris&&tris>0)?1:(n==vertices&&vertices>0)?2:3; // 0 absent, 1 per triangle, 2 per vertex, 3 other
        int a=Kind(markers),b=Kind(markerTypes);
        if((a==0||a==1)&&(b==0||b==1))return MarkerLayout.PerTriangle;
        if((a==0||a==2)&&(b==0||b==2))return MarkerLayout.PerVertex;
        if((a==0||a==3)&&(b==0||b==3))return MarkerLayout.Independent;
        return MarkerLayout.Unknown;
    }
    /// <summary>The entries of a per-triangle array for the kept triangle numbers (same order as the filtered index list).</summary>
    public static T[] Pick<T>(T[] values,IReadOnlyList<int> keptTriangles)
    {
        var r=new T[keptTriangles.Count];
        for(int k=0;k<r.Length;k++)r[k]=values[keptTriangles[k]];
        return r;
    }
}

/// <summary>Blast damage (DESIGN.md section 6). D = BlastBase x (calibre/100)^3 x falloff(d); falloff(d) = 1/(1+(d/r)^2); r = RadiusPer100mm x calibre/100.</summary>
public static class BlastMath
{
    /// <summary>Beyond this many radii a blast does nothing (falloff below 1/17); keeps far plates out of the bookkeeping.</summary>
    public const double ReachRadii=4.0;
    public static double Radius(double radiusPer100mm,double calibreMm)=>radiusPer100mm*calibreMm/100.0;
    public static double Falloff(double distanceM,double radiusM)
    {
        if(!(radiusM>0)||double.IsNaN(distanceM))return 0;
        double x=Math.Max(0,distanceM)/radiusM;return 1.0/(1.0+x*x);
    }
    public static double Reach(double radiusPer100mm,double calibreMm)=>ReachRadii*Radius(radiusPer100mm,calibreMm);
    /// <summary>Mean vertex thickness (m, or mm if above 1) to mm. 0 when unusable.</summary>
    public static double ThicknessMm(double meanThickness)
    {
        if(!(meanThickness>0)||double.IsInfinity(meanThickness))return 0;
        return meanThickness>1.0?meanThickness:meanThickness*1000.0;
    }
    /// <summary>Damage of one blast to one plate side at the given distance. 0 below MinCalibreMm, beyond the reach, or for invalid input.</summary>
    public static double Damage(double blastBase,double calibreMm,double distanceM,double radiusPer100mm,double minCalibreMm)
    {
        if(!(calibreMm>0)||calibreMm<minCalibreMm||!(radiusPer100mm>0)||double.IsNaN(distanceM))return 0;
        double r=Radius(radiusPer100mm,calibreMm);
        if(distanceM>ReachRadii*r)return 0;
        double k=calibreMm/100.0;
        return blastBase*k*k*k*Falloff(distanceM,r);
    }
    /// <summary>BlastStrengthPerMm x mean thickness (mm), no area factor. Unknown thickness gives the default.</summary>
    public static double Strength(double perMm,double thicknessMm,double defaultStrength)
    {
        if(!(thicknessMm>0)||double.IsNaN(thicknessMm)||double.IsInfinity(thicknessMm)||!(perMm>0))return defaultStrength;
        return perMm*thicknessMm;
    }
    /// <summary>Damage of one blast to both halves of a mirrored plate: each half uses its own distance (blast position in structure space to that half's bounds).</summary>
    public static (double Original,double Mirror,double DistOriginal,double DistMirror) Halves(SideGeometry g,V3 blastStructureSpace,double blastBase,double calibreMm,double radiusPer100mm,double minCalibreMm)
    {
        double dO=g.Distance(blastStructureSpace,PlateSide.Original),dM=g.Distance(blastStructureSpace,PlateSide.Mirror);
        return (Damage(blastBase,calibreMm,dO,radiusPer100mm,minCalibreMm),Damage(blastBase,calibreMm,dM,radiusPer100mm,minCalibreMm),dO,dM);
    }
}

/// <summary>Kinetic hits (AP/APFSDS) make holes and dents but never tear anything off (user decision 2026-11-15). The old formula stays in DamageFormula for later dents.</summary>
public static class KineticPolicy
{
    /// <summary>Break damage of a kinetic hit: always 0.</summary>
    public static double BreakDamage(double massKg,double speedMs)=>0;
    /// <summary>Applies a kinetic hit to the ledger: nothing is added, nothing breaks. Returns the energy in MJ for the log line.</summary>
    public static double Apply(DamageLedger led,long root,int vuid,PlateSide side,double massKg,double speedMs,out bool breaks)
    {
        breaks=false;
        double e=DamageFormula.KineticMJ(massKg,speedMs);
        double dmg=BreakDamage(massKg,speedMs);
        if(dmg>0)breaks=led.Add(root,vuid,side,dmg,double.PositiveInfinity,out _,out _);
        return e;
    }
}

/// <summary>Drops a second explosion event of the same blast in the same frame (several effect instances can play for one blast).</summary>
public sealed class BlastDedup
{
    private int frame=int.MinValue;private readonly List<(V3 P,int Cal)> seen=new();
    public bool IsDuplicate(int frameNumber,V3 p,int calibre,float toleranceM=0.05f)
    {
        if(frameNumber!=frame){frame=frameNumber;seen.Clear();}
        foreach(var s in seen){float dx=s.P.X-p.X,dy=s.P.Y-p.Y,dz=s.P.Z-p.Z;if(s.Cal==calibre&&dx*dx+dy*dy+dz*dz<=toleranceM*toleranceM)return true;}
        seen.Add((p,calibre));return false;
    }
}

/// <summary>Segment against axis-aligned box (slab test), for the "does this shot pass through that side" check.</summary>
public static class SegmentBox
{
    public static bool Intersects(V3 a,V3 b,V3 min,V3 max)
    {
        float t0=0,t1=1;
        return Slab(a.X,b.X,min.X,max.X,ref t0,ref t1)&&Slab(a.Y,b.Y,min.Y,max.Y,ref t0,ref t1)&&Slab(a.Z,b.Z,min.Z,max.Z,ref t0,ref t1);
    }
    private static bool Slab(float p,float q,float lo,float hi,ref float t0,ref float t1)
    {
        float d=q-p;
        if(Math.Abs(d)<1e-9f)return p>=lo&&p<=hi;
        float u=(lo-p)/d,v=(hi-p)/d;if(u>v){var tmp=u;u=v;v=tmp;}
        t0=Math.Max(t0,u);t1=Math.Min(t1,v);return t0<=t1;
    }
}

/// <summary>Test aid after a side break: a shot that passes through the torn side must register no hit on that side.</summary>
public static class TornCheck
{
    /// <summary>Hits counted: only those whose position lies on the torn side. Pass = none.</summary>
    public static (int Hits,bool Pass) Evaluate(IEnumerable<PlateSide> hitSidesOfThatPlate,PlateSide torn)
    {
        int n=hitSidesOfThatPlate.Count(s=>s==torn);
        return (n,n==0);
    }
}

/// <summary>0.7.0: Recent penetrating simulation hit recorded in world space so upstream HE explosion visuals can snap back to the actual plate impact point.</summary>
public readonly struct SimHitMark
{
    public readonly int Frame,CalibreMm,Vuid;
    public readonly long Root;
    public readonly V3 Pos,Dir;
    public SimHitMark(int frame,V3 pos,V3 dir,int calibreMm,long root,int vuid)
    {Frame=frame;Pos=pos;Dir=dir;CalibreMm=calibreMm;Root=root;Vuid=vuid;}
}

/// <summary>0.7.0: Snaps upstream HE detonation effects back onto the recent shot impact point along the incoming penetrator ray.</summary>
public static class BlastSnap
{
    public static bool TrySnap(IReadOnlyList<SimHitMark> recent,int blastFrame,V3 blastPos,int blastCalibreMm,out SimHitMark matched,out double shiftDistM)
    {
        matched=default;shiftDistM=0;
        if(recent==null||recent.Count==0)return false;
        bool found=false;
        int bestFrameDiff=int.MaxValue;
        double bestPerp=double.MaxValue;
        for(int i=recent.Count-1;i>=0;i--)
        {
            var h=recent[i];
            int fdiff=Math.Abs(blastFrame-h.Frame);
            if(fdiff>3)continue;
            int calTol=Math.Max(25,(int)(Math.Max(blastCalibreMm,h.CalibreMm)*0.25));
            if(Math.Abs(blastCalibreMm-h.CalibreMm)>calTol)continue;
            var dir=VM.Norm(h.Dir,new V3(0,0,1));
            var delta=VM.Sub(blastPos,h.Pos);
            double dist=VM.Len(delta);
            if(dist<=0.25)continue;
            double along=VM.Dot(delta,dir);
            if(along<-10.0||along>0.35)continue;
            double perp=VM.Len(VM.Sub(delta,VM.Mul(dir,along)));
            if(perp>0.60)continue;
            if(!found||fdiff<bestFrameDiff||(fdiff==bestFrameDiff&&perp<bestPerp))
            {
                found=true;bestFrameDiff=fdiff;bestPerp=perp;
                matched=h;shiftDistM=dist;
            }
        }
        return found;
    }

    /// <summary>0.8.1: Matches surface/contact explosions (HESH, HEAT, ERA, close HE) within 0.45 m of a recent SimulationRan hit so the blast inherits the incoming shot direction rather than guessing from a near-zero surface delta.</summary>
    public static bool TryMatchContact(IReadOnlyList<SimHitMark> recent,int blastFrame,V3 blastPos,int blastCalibreMm,out SimHitMark matched,out double distM,out double alongM)
    {
        matched=default;distM=0;alongM=0;
        if(recent==null||recent.Count==0||blastCalibreMm<=0)return false;
        bool found=false;
        int bestFrameDiff=int.MaxValue;
        double bestDist=double.MaxValue;
        for(int i=recent.Count-1;i>=0;i--)
        {
            var h=recent[i];
            int fdiff=Math.Abs(blastFrame-h.Frame);
            if(fdiff>3)continue;
            var dir=VM.Norm(h.Dir,new V3(0,0,1));
            var delta=VM.Sub(blastPos,h.Pos);
            double dist=VM.Len(delta);
            if(dist>0.45)continue;
            double along=VM.Dot(delta,dir);
            if(along>0.20)continue;
            if(!found||fdiff<bestFrameDiff||(fdiff==bestFrameDiff&&dist<bestDist))
            {
                found=true;bestFrameDiff=fdiff;bestDist=dist;
                matched=h;distM=dist;alongM=along;
            }
        }
        return found;
    }
}
