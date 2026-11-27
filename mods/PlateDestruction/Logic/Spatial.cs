namespace PlateDestructionP0;

// ================================================================================================
// 0.5.0: Fast triangle AABB acceleration, in-place armour degeneration, rebuild coalescing,
// and attached-part filtering. Pure managed code (no Unity / Il2Cpp types).
// ================================================================================================

/// <summary>Per-triangle AABB cache for fast RayFirst and Closest queries without scanning distant triangles.</summary>
public sealed class TriAccel
{
    private int builtVersion=-1;
    private int triCount;
    private float meshMinX,meshMinY,meshMinZ,meshMaxX,meshMaxY,meshMaxZ;
    private float[] minX=Array.Empty<float>(),minY=Array.Empty<float>(),minZ=Array.Empty<float>();
    private float[] maxX=Array.Empty<float>(),maxY=Array.Empty<float>(),maxZ=Array.Empty<float>();
    private bool[] valid=Array.Empty<bool>();

    public void Invalidate(){builtVersion=-1;}

    public void EnsureBuilt(V3[] v,int[] t,int version)
    {
        int n=t.Length/3;
        if(builtVersion==version&&triCount==n)return;
        builtVersion=version;
        triCount=n;
        if(minX.Length<n)
        {
            minX=new float[n];minY=new float[n];minZ=new float[n];
            maxX=new float[n];maxY=new float[n];maxZ=new float[n];
            valid=new bool[n];
        }
        float mx0=float.MaxValue,my0=float.MaxValue,mz0=float.MaxValue;
        float mx1=float.MinValue,my1=float.MinValue,mz1=float.MinValue;
        int vc=v.Length;
        for(int i=0;i<n;i++)
        {
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc){valid[i]=false;continue;}
            valid[i]=true;
            V3 a=v[ia],b=v[ib],c=v[ic];
            float x0=Math.Min(a.X,Math.Min(b.X,c.X)),y0=Math.Min(a.Y,Math.Min(b.Y,c.Y)),z0=Math.Min(a.Z,Math.Min(b.Z,c.Z));
            float x1=Math.Max(a.X,Math.Max(b.X,c.X)),y1=Math.Max(a.Y,Math.Max(b.Y,c.Y)),z1=Math.Max(a.Z,Math.Max(b.Z,c.Z));
            minX[i]=x0;minY[i]=y0;minZ[i]=z0;maxX[i]=x1;maxY[i]=y1;maxZ[i]=z1;
            if(x0<mx0)mx0=x0;if(y0<my0)my0=y0;if(z0<mz0)mz0=z0;
            if(x1>mx1)mx1=x1;if(y1>my1)my1=y1;if(z1>mz1)mz1=z1;
        }
        meshMinX=mx0;meshMinY=my0;meshMinZ=mz0;meshMaxX=mx1;meshMaxY=my1;meshMaxZ=mz1;
    }

    private static bool Slab(double p,double d,double lo,double hi,ref double t0,ref double t1)
    {
        if(Math.Abs(d)<1e-12)return p>=lo&&p<=hi;
        double inv=1.0/d;
        double u=(lo-p)*inv,v=(hi-p)*inv;
        if(u>v){double tmp=u;u=v;v=tmp;}
        if(u>t0)t0=u;if(v<t1)t1=v;
        return t0<=t1;
    }

    private static bool RayAabb(V3 o,V3 d,double maxD,float x0,float y0,float z0,float x1,float y1,float z1)
    {
        const double Eps=1e-5;
        double t0=0,t1=maxD;
        return Slab(o.X,d.X,x0-Eps,x1+Eps,ref t0,ref t1)&&Slab(o.Y,d.Y,y0-Eps,y1+Eps,ref t0,ref t1)&&Slab(o.Z,d.Z,z0-Eps,z1+Eps,ref t0,ref t1);
    }

    public bool RayFirst(V3[] v,int[] t,int version,V3 o,V3 dir,double maxDist,Func<int,bool>? ok,out double dist,out V3 normal,out int tri)
    {
        dist=double.MaxValue;normal=new V3(0,0,0);tri=-1;
        var d=VM.Norm(dir,new V3(0,0,0));if(VM.Len(d)<0.5)return false;
        EnsureBuilt(v,t,version);
        if(triCount==0||!RayAabb(o,d,maxDist,meshMinX,meshMinY,meshMinZ,meshMaxX,meshMaxY,meshMaxZ))return false;
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            double cap=Math.Min(maxDist,dist);
            if(!RayAabb(o,d,cap,minX[i],minY[i],minZ[i],maxX[i],maxY[i],maxZ[i]))continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var a=v[ia];var e1=VM.Sub(v[ib],a);var e2=VM.Sub(v[ic],a);
            var pv=VM.Cross(d,e2);double det=VM.Dot(e1,pv);if(Math.Abs(det)<1e-14)continue;
            double inv=1.0/det;var tv=VM.Sub(o,a);double u=VM.Dot(tv,pv)*inv;if(u<0||u>1)continue;
            var qv=VM.Cross(tv,e1);double w=VM.Dot(d,qv)*inv;if(w<0||u+w>1)continue;
            double tt=VM.Dot(e2,qv)*inv;if(tt<0||tt>maxDist||tt>=dist)continue;
            dist=tt;tri=i;normal=VM.Norm(VM.Cross(e1,e2),new V3(0,1,0));
        }
        return tri>=0;
    }

    /// <summary>1.0.1: Returns the farthest ray-triangle intersection within maxDist (used to measure full through-block depth on EB [Solid block] plates whose visual mesh is a hollow thin-walled shell).</summary>
    public bool RayFarthest(V3[] v,int[] t,int version,V3 o,V3 dir,double maxDist,Func<int,bool>? ok,out double dist,out int tri)
    {
        dist=-1.0;tri=-1;
        var d=VM.Norm(dir,new V3(0,0,0));if(VM.Len(d)<0.5)return false;
        EnsureBuilt(v,t,version);
        if(triCount==0||!RayAabb(o,d,maxDist,meshMinX,meshMinY,meshMinZ,meshMaxX,meshMaxY,meshMaxZ))return false;
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            if(!RayAabb(o,d,maxDist,minX[i],minY[i],minZ[i],maxX[i],maxY[i],maxZ[i]))continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var a=v[ia];var e1=VM.Sub(v[ib],a);var e2=VM.Sub(v[ic],a);
            var pv=VM.Cross(d,e2);double det=VM.Dot(e1,pv);if(Math.Abs(det)<1e-14)continue;
            double inv=1.0/det;var tv=VM.Sub(o,a);double u=VM.Dot(tv,pv)*inv;if(u<0||u>1)continue;
            var qv=VM.Cross(tv,e1);double w=VM.Dot(d,qv)*inv;if(w<0||u+w>1)continue;
            double tt=VM.Dot(e2,qv)*inv;if(tt<0||tt>maxDist||tt<=dist)continue;
            dist=tt;tri=i;
        }
        return tri>=0;
    }

    public bool Closest(V3[] v,int[] t,int version,V3 p,Func<int,bool>? ok,out V3 point,out V3 normal)
        =>Closest(v,t,version,p,ok,out point,out normal,out _);

    public bool Closest(V3[] v,int[] t,int version,V3 p,Func<int,bool>? ok,out V3 point,out V3 normal,out int tri)
    {
        point=p;normal=new V3(0,1,0);tri=-1;double best=double.MaxValue;bool have=false;
        EnsureBuilt(v,t,version);
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            double dx=Math.Max(Math.Max(minX[i]-p.X,0),p.X-maxX[i]);
            double dy=Math.Max(Math.Max(minY[i]-p.Y,0),p.Y-maxY[i]);
            double dz=Math.Max(Math.Max(minZ[i]-p.Z,0),p.Z-maxZ[i]);
            if(dx*dx+dy*dy+dz*dz>=best)continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var q=MeshQuery.ClosestOnTriangle(p,v[ia],v[ib],v[ic]);
            var dv=VM.Sub(q,p);double dd=VM.Dot(dv,dv);
            if(dd<best){best=dd;tri=i;point=q;normal=VM.Norm(VM.Cross(VM.Sub(v[ib],v[ia]),VM.Sub(v[ic],v[ia])),new V3(0,1,0));have=true;}
        }
        return have;
    }

    public bool ClosestTri(V3[] v,int[] t,int version,V3 p,double maxDist,Func<int,bool>? ok,out V3 point,out int tri)
    {
        point=p;tri=-1;double best=maxDist*maxDist;
        EnsureBuilt(v,t,version);
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            double dx=Math.Max(Math.Max(minX[i]-p.X,0),p.X-maxX[i]);
            double dy=Math.Max(Math.Max(minY[i]-p.Y,0),p.Y-maxY[i]);
            double dz=Math.Max(Math.Max(minZ[i]-p.Z,0),p.Z-maxZ[i]);
            if(dx*dx+dy*dy+dz*dz>=best)continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var q=MeshQuery.ClosestOnTriangle(p,v[ia],v[ib],v[ic]);
            var dv=VM.Sub(q,p);double dd=VM.Dot(dv,dv);
            if(dd<best){best=dd;point=q;tri=i;}
        }
        return tri>=0;
    }

    /// <summary>0.8.2: Closest point for blast denting: within tieTol of the true minimum distance, prefers the triangle most facing the blast origin.</summary>
    public bool ClosestBlast(V3[] v,int[] t,int version,IReadOnlyList<V3>? vertNormals,V3 p,Func<int,bool>? ok,out V3 point,out V3 faceOutN,out int tri,out double frontCos,double tieTol=0.005)
    {
        point=p;faceOutN=new V3(0,1,0);tri=-1;frontCos=0;
        EnsureBuilt(v,t,version);
        double minDd=double.MaxValue;
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            double dx=Math.Max(Math.Max(minX[i]-p.X,0),p.X-maxX[i]);
            double dy=Math.Max(Math.Max(minY[i]-p.Y,0),p.Y-maxY[i]);
            double dz=Math.Max(Math.Max(minZ[i]-p.Z,0),p.Z-maxZ[i]);
            if(dx*dx+dy*dy+dz*dz>=minDd)continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var q=MeshQuery.ClosestOnTriangle(p,v[ia],v[ib],v[ic]);
            var dv=VM.Sub(q,p);double dd=VM.Dot(dv,dv);
            if(dd<minDd)minDd=dd;
        }
        if(minDd==double.MaxValue)return false;
        double band=Math.Sqrt(minDd)+Math.Max(0.0,tieTol);
        double bandSq=band*band;
        double bestCos=double.NegativeInfinity,bestDd=double.MaxValue;
        int vnCount=vertNormals?.Count??0;
        for(int i=0;i<triCount;i++)
        {
            if(!valid[i]||(ok!=null&&!ok(i)))continue;
            double dx=Math.Max(Math.Max(minX[i]-p.X,0),p.X-maxX[i]);
            double dy=Math.Max(Math.Max(minY[i]-p.Y,0),p.Y-maxY[i]);
            double dz=Math.Max(Math.Max(minZ[i]-p.Z,0),p.Z-maxZ[i]);
            if(dx*dx+dy*dy+dz*dz>bandSq)continue;
            int ia=t[i*3],ib=t[i*3+1],ic=t[i*3+2];
            var q=MeshQuery.ClosestOnTriangle(p,v[ia],v[ib],v[ic]);
            var toBlast=VM.Sub(p,q);double dd=VM.Dot(toBlast,toBlast);
            if(dd>bandSq)continue;
            var outN=VM.Norm(VM.Cross(VM.Sub(v[ib],v[ia]),VM.Sub(v[ic],v[ia])),new V3(0,1,0));
            if(vertNormals!=null&&ia<vnCount&&ib<vnCount&&ic<vnCount)
            {
                var avg=VM.Add(VM.Add(vertNormals[ia],vertNormals[ib]),vertNormals[ic]);
                if(VM.Len(avg)>=1e-4&&VM.Dot(outN,avg)<0)outN=VM.Mul(outN,-1.0);
            }
            double dist=Math.Sqrt(dd);
            double fc=dist>1e-6?VM.Dot(toBlast,outN)/dist:1.0;
            if(tri<0||fc>bestCos+1e-4||(Math.Abs(fc-bestCos)<=1e-4&&dd<bestDd))
            {
                tri=i;point=q;faceOutN=outN;frontCos=fc;bestCos=fc;bestDd=dd;
            }
        }
        return tri>=0;
    }
}

/// <summary>0.8.2: Segment occlusion test against another plate's mesh (with endpoint margins so co-planar touching plates do not self-block).</summary>
public static class BlastOcclusion
{
    public static bool SegmentBlocked(TriAccel accel,V3[] v,int[] t,int version,V3 startLocal,V3 endLocal,double startMarginLocal,double endMarginLocal,Func<int,bool>? ok=null)
    {
        var seg=VM.Sub(endLocal,startLocal);
        double len=VM.Len(seg);
        double sM=Math.Max(0.0,startMarginLocal);
        double eM=Math.Max(0.0,endMarginLocal);
        if(len<=sM+eM+1e-4)return false;
        var dir=VM.Mul(seg,1.0/len);
        var origin=VM.Add(startLocal,VM.Mul(dir,sM));
        double maxD=len-sM-eM;
        return accel.RayFirst(v,t,version,origin,dir,maxD,ok,out _,out _,out _);
    }
}

public readonly struct TriBackup
{
    public readonly int MeshIndex,Offset,A,B,C;
    public TriBackup(int meshIndex,int offset,int a,int b,int c){MeshIndex=meshIndex;Offset=offset;A=a;B=b;C=c;}
}

/// <summary>Zero-rebuild in-place armour triangle degeneration (sets all 3 indices of a broken triangle to index a so native raycast/BVH ignores it) with full restore support.</summary>
public static class InPlaceArmour
{
    public static bool IsDegenerate(int a,int b,int c)=>a==b&&b==c;

    public static bool TryDegenerate(int[] tris,int offset,int meshIndex,out TriBackup backup)
    {
        backup=default;
        if(tris==null||offset<0||offset+2>=tris.Length)return false;
        int a=tris[offset],b=tris[offset+1],c=tris[offset+2];
        if(a==b&&b==c)return false;
        backup=new TriBackup(meshIndex,offset,a,b,c);
        tris[offset+1]=a;tris[offset+2]=a;
        return true;
    }

    public static int Restore(int[] tris,IEnumerable<TriBackup> backups,int meshIndex)
    {
        if(tris==null)return 0;
        int n=0;
        foreach(var b in backups)
        {
            if(b.MeshIndex!=meshIndex||b.Offset<0||b.Offset+2>=tris.Length)continue;
            tris[b.Offset]=b.A;tris[b.Offset+1]=b.B;tris[b.Offset+2]=b.C;n++;
        }
        return n;
    }
}

/// <summary>Batches delayed DamageModel rebuild requests per vehicle without resetting the earliest deadline under rapid fire.</summary>
public sealed class RebuildCoalescer
{
    private readonly Dictionary<long,double> due=new();
    public int Count=>due.Count;

    public void Request(long root,double now,double windowS)
    {
        if(!due.ContainsKey(root))due[root]=now+Math.Max(0,windowS);
    }

    public List<long> TakeReady(double now,IReadOnlySet<long>? busyRoots)
    {
        var ready=new List<long>();
        foreach(var kv in due)
        {
            if(kv.Value<=now&&(busyRoots==null||!busyRoots.Contains(kv.Key)))ready.Add(kv.Key);
        }
        for(int i=0;i<ready.Count;i++)due.Remove(ready[i]);
        ready.Sort();
        return ready;
    }

    public void ClearRoot(long root)=>due.Remove(root);
    public void Clear()=>due.Clear();
}

/// <summary>Filters out core vehicle/chassis/drivetrain/turret/wheel parts when adopting attached parts on a structural plate, and guarantees unique debris part keys.</summary>
public static class AttachedFilter
{
    private static readonly HashSet<string> CoreTypes=new(StringComparer.Ordinal)
    {
        "PlateStructure","TurretRing","TurretBasket","PartDecoupler","WheelMount","Axle","AxleRod","WheelArray",
        "Suspension","VoluteSuspensionSpring","SuspensionArm","TorsionBar","ChristieCoilSpring",
        "TrackAssembly","CombustionEngine","TransmissionBlock","CrewSeat","Cannon",
        "TraverseMotor","TraverseMotorModelComponent","LayingDrive","FuelTank","AmmoRack","AmmoRackModel",
        "PowertrainComponent","DriveSprocket","IdlerWheel","GunnerSight","Hatch","Viewport"
    };

    public static bool IsCoreStructuralPart(string? typeName)=>!string.IsNullOrEmpty(typeName)&&CoreTypes.Contains(typeName!);
}

public static class PartKeyRule
{
    public static string Make(int partVuid,long groupPtr,int parentId,string rendererName)
    {
        if(groupPtr!=0)return $"v{partVuid}:g{groupPtr}";
        string n=(rendererName??"").ToLowerInvariant().Replace("highdetail","").Replace("lowdetail","").Replace("lod0","").Replace("lod1","");
        return $"v{partVuid}:n{n}@{parentId}";
    }
}
