namespace PlateDestructionP0;

// ================================================================================================
// 0.5.0: Crack-free local adaptive mesh subdivision and crease-aware spatial normals.
// Pure managed code (no Unity / Il2Cpp types) so all managed tests can run without the game.
// ================================================================================================

public readonly struct V2
{
    public readonly float X,Y;
    public V2(float x,float y){X=x;Y=y;}
}

public readonly struct V4
{
    public readonly float X,Y,Z,W;
    public V4(float x,float y,float z,float w){X=x;Y=y;Z=z;W=w;}
}

public readonly struct Col32
{
    public readonly byte R,G,B,A;
    public Col32(byte r,byte g,byte b,byte a){R=r;G=g;B=b;A=a;}
}

/// <summary>Quantized 3D position key (0.25 mm buckets) to weld split UV/hard-edge vertices spatially while keeping thin plate front/back shells separate.</summary>
public readonly struct SpatialPosKey:IEquatable<SpatialPosKey>
{
    public readonly int X,Y,Z;
    public SpatialPosKey(int x,int y,int z){X=x;Y=y;Z=z;}
    public static SpatialPosKey Quantize(V3 p)
    {
        static int Q(float v)
        {
            double s=Math.Clamp((double)v*4000.0,-2e9,2e9);
            return double.IsNaN(s)?0:(int)Math.Round(s);
        }
        return new SpatialPosKey(Q(p.X),Q(p.Y),Q(p.Z));
    }
    public bool Equals(SpatialPosKey other)=>X==other.X&&Y==other.Y&&Z==other.Z;
    public override bool Equals(object? obj)=>obj is SpatialPosKey k&&Equals(k);
    public override int GetHashCode()=>HashCode.Combine(X,Y,Z);
    public static int Compare(SpatialPosKey a,SpatialPosKey b)
    {
        int c=a.X.CompareTo(b.X);if(c!=0)return c;
        c=a.Y.CompareTo(b.Y);if(c!=0)return c;
        return a.Z.CompareTo(b.Z);
    }
}

/// <summary>Canonical undirected edge key in quantized 3D basis space, so split-vertex seams split identically on both sides.</summary>
public readonly struct SpatialEdgeKey:IEquatable<SpatialEdgeKey>
{
    public readonly SpatialPosKey A,B;
    public SpatialEdgeKey(SpatialPosKey p0,SpatialPosKey p1)
    {
        if(SpatialPosKey.Compare(p0,p1)<=0){A=p0;B=p1;}
        else{A=p1;B=p0;}
    }
    public bool Equals(SpatialEdgeKey other)=>A.Equals(other.A)&&B.Equals(other.B);
    public override bool Equals(object? obj)=>obj is SpatialEdgeKey k&&Equals(k);
    public override int GetHashCode()=>HashCode.Combine(A,B);
}

/// <summary>Editable multi-submesh plate representation supporting crack-free local refinement, vertex displacement, cell filtering, and fast spatial queries.</summary>
public sealed class SubdivMesh
{
    public readonly List<V3> Basis;
    public readonly List<V3> Disp;
    public readonly List<V3> Normals;
    public readonly List<V4> Tangents;
    public readonly List<V2> UV;
    public readonly List<V2> UV2;
    public readonly List<Col32> Colors;
    public List<byte>? VertSide;
    public List<bool>? Alive;
    public List<V3>? ThickVec;
    public readonly List<int>[] SubTris;
    public List<int>[]? SubTriCell;
    public List<byte>[]? SubTriSide;
    public double MaxDepthM{get;}
    public int TopologyVersion{get;private set;}

    public V3[] FlatBasis{get;private set;}=Array.Empty<V3>();
    public int[] FlatTris{get;private set;}=Array.Empty<int>();
    public int[]? FlatTriCell{get;private set;}
    public byte[]? FlatTriSide{get;private set;}
    public int[] SubCounts{get;private set;}=Array.Empty<int>();

    private int indexedVersion=-1;
    private readonly Dictionary<SpatialPosKey,List<int>> weldGroups=new();
    private SpatialPosKey[] vertKeys=Array.Empty<SpatialPosKey>();
    private List<(int Sub,int TriIdx)>[] incident=Array.Empty<List<(int,int)>>();

    public SubdivMesh(
        V3[] basis,
        V3[]? normals,
        V4[]? tangents,
        V2[]? uv,
        V2[]? uv2,
        Col32[]? colors,
        int[][] subTris,
        byte[]? vertSide,
        int[][]? subTriCell,
        byte[][]? subTriSide,
        double maxDepthM,
        V3[]? thickVec=null)
    {
        int vc=basis.Length;
        Basis=new List<V3>(basis);
        Disp=new List<V3>(vc);for(int i=0;i<vc;i++)Disp.Add(new V3(0,0,0));
        if(normals!=null&&normals.Length==vc)Normals=new List<V3>(normals);
        else{Normals=new List<V3>(vc);for(int i=0;i<vc;i++)Normals.Add(new V3(0,1,0));}
        Tangents=tangents!=null&&tangents.Length==vc?new List<V4>(tangents):new List<V4>();
        UV=uv!=null&&uv.Length==vc?new List<V2>(uv):new List<V2>();
        UV2=uv2!=null&&uv2.Length==vc?new List<V2>(uv2):new List<V2>();
        Colors=colors!=null&&colors.Length==vc?new List<Col32>(colors):new List<Col32>();
        if(vertSide!=null&&vertSide.Length==vc)VertSide=new List<byte>(vertSide);
        if(thickVec!=null&&thickVec.Length==vc)ThickVec=new List<V3>(thickVec);
        int sc=Math.Max(1,subTris.Length);
        SubTris=new List<int>[sc];
        for(int s=0;s<sc;s++)SubTris[s]=s<subTris.Length?new List<int>(subTris[s]):new List<int>();
        if(subTriCell!=null&&subTriCell.Length==sc){SubTriCell=new List<int>[sc];for(int s=0;s<sc;s++)SubTriCell[s]=new List<int>(subTriCell[s]);}
        if(subTriSide!=null&&subTriSide.Length==sc){SubTriSide=new List<byte>[sc];for(int s=0;s<sc;s++)SubTriSide[s]=new List<byte>(subTriSide[s]);}
        MaxDepthM=Math.Max(0,maxDepthM);
        TopologyVersion=1;
        SyncFlat();
    }

    public V3 Position(int i)=>VM.Add(Basis[i],Disp[i]);

    public double MaxDisplacement()
    {
        double m=0;
        for(int i=0;i<Disp.Count;i++)m=Math.Max(m,VM.Len(Disp[i]));
        return m;
    }

    public void BumpTopology(){TopologyVersion++;SyncFlat();}

    public void SyncFlat()
    {
        int vc=Basis.Count;
        FlatBasis=Basis.ToArray();
        int totalTris=0;
        SubCounts=new int[SubTris.Length];
        for(int s=0;s<SubTris.Length;s++){int n=SubTris[s].Count/3;SubCounts[s]=n;totalTris+=n;}
        FlatTris=new int[totalTris*3];
        FlatTriCell=SubTriCell!=null?new int[totalTris]:null;
        FlatTriSide=SubTriSide!=null?new byte[totalTris]:null;
        int triOff=0;
        for(int s=0;s<SubTris.Length;s++)
        {
            var st=SubTris[s];int n=st.Count/3;
            for(int i=0;i<n*3;i++)FlatTris[triOff*3+i]=st[i];
            if(FlatTriCell!=null&&SubTriCell!=null)for(int i=0;i<n&&i<SubTriCell[s].Count;i++)FlatTriCell[triOff+i]=SubTriCell[s][i];
            if(FlatTriSide!=null&&SubTriSide!=null)for(int i=0;i<n&&i<SubTriSide[s].Count;i++)FlatTriSide[triOff+i]=SubTriSide[s][i];
            triOff+=n;
        }
    }

    public void EnsureSpatialIndex()
    {
        if(indexedVersion==TopologyVersion)return;
        indexedVersion=TopologyVersion;
        int vc=Basis.Count;
        weldGroups.Clear();
        vertKeys=new SpatialPosKey[vc];
        incident=new List<(int,int)>[vc];
        for(int i=0;i<vc;i++)
        {
            var k=SpatialPosKey.Quantize(Basis[i]);
            vertKeys[i]=k;
            if(!weldGroups.TryGetValue(k,out var g)){g=new List<int>(2);weldGroups[k]=g;}
            g.Add(i);
            incident[i]=new List<(int,int)>(4);
        }
        for(int s=0;s<SubTris.Length;s++)
        {
            var st=SubTris[s];int n=st.Count/3;
            for(int ti=0;ti<n;ti++)
            {
                int a=st[ti*3],b=st[ti*3+1],c=st[ti*3+2];
                if(a<0||b<0||c<0||a>=vc||b>=vc||c>=vc)continue;
                incident[a].Add((s,ti));incident[b].Add((s,ti));incident[c].Add((s,ti));
            }
        }
    }

    public SpatialPosKey VertKey(int vi)=>vertKeys[vi];
    public IReadOnlyList<int>? WeldGroup(SpatialPosKey key)=>weldGroups.TryGetValue(key,out var g)?g:null;
    public IReadOnlyList<(int Sub,int TriIdx)> Incident(int vi)=>incident[vi];
}

public static class LocalSubdiv
{
    private static long EdgePairKey(int u,int v)=>u<v?((long)u<<32)|(uint)v:((long)v<<32)|(uint)u;

    private static int GetMidpoint(SubdivMesh sm,Dictionary<long,int> cache,int u,int v)
    {
        long key=EdgePairKey(u,v);
        if(cache.TryGetValue(key,out int idx))return idx;
        idx=sm.Basis.Count;
        cache[key]=idx;
        V3 bu=sm.Basis[u],bv=sm.Basis[v];
        sm.Basis.Add(new V3((bu.X+bv.X)*0.5f,(bu.Y+bv.Y)*0.5f,(bu.Z+bv.Z)*0.5f));
        V3 du=sm.Disp[u],dv=sm.Disp[v];
        sm.Disp.Add(new V3((du.X+dv.X)*0.5f,(du.Y+dv.Y)*0.5f,(du.Z+dv.Z)*0.5f));
        V3 nu=sm.Normals[u],nv=sm.Normals[v];
        sm.Normals.Add(VM.Norm(new V3((nu.X+nv.X)*0.5f,(nu.Y+nv.Y)*0.5f,(nu.Z+nv.Z)*0.5f),nu));
        if(sm.Tangents.Count>0)
        {
            V4 tu=sm.Tangents[u],tv=sm.Tangents[v];
            V3 td=VM.Norm(new V3((tu.X+tv.X)*0.5f,(tu.Y+tv.Y)*0.5f,(tu.Z+tv.Z)*0.5f),new V3(tu.X,tu.Y,tu.Z));
            sm.Tangents.Add(new V4(td.X,td.Y,td.Z,tu.W));
        }
        if(sm.UV.Count>0){var a=sm.UV[u];var b=sm.UV[v];sm.UV.Add(new V2((a.X+b.X)*0.5f,(a.Y+b.Y)*0.5f));}
        if(sm.UV2.Count>0){var a=sm.UV2[u];var b=sm.UV2[v];sm.UV2.Add(new V2((a.X+b.X)*0.5f,(a.Y+b.Y)*0.5f));}
        if(sm.Colors.Count>0)
        {
            var a=sm.Colors[u];var b=sm.Colors[v];
            sm.Colors.Add(new Col32((byte)((a.R+b.R+1)>>1),(byte)((a.G+b.G+1)>>1),(byte)((a.B+b.B+1)>>1),(byte)((a.A+b.A+1)>>1)));
        }
        if(sm.VertSide!=null)sm.VertSide.Add(sm.VertSide[u]);
        if(sm.Alive!=null)sm.Alive.Add(sm.Alive[u]||sm.Alive[v]);
        if(sm.ThickVec!=null){V3 a=sm.ThickVec[u],b=sm.ThickVec[v];sm.ThickVec.Add(new V3((a.X+b.X)*0.5f,(a.Y+b.Y)*0.5f,(a.Z+b.Z)*0.5f));}
        return idx;
    }

    private static double DistSq(V3 a,V3 b){double dx=a.X-b.X,dy=a.Y-b.Y,dz=a.Z-b.Z;return dx*dx+dy*dy+dz*dz;}

    private static double TriAabbDistSq(V3 p,V3 a,V3 b,V3 c)
    {
        float x0=Math.Min(a.X,Math.Min(b.X,c.X)),y0=Math.Min(a.Y,Math.Min(b.Y,c.Y)),z0=Math.Min(a.Z,Math.Min(b.Z,c.Z));
        float x1=Math.Max(a.X,Math.Max(b.X,c.X)),y1=Math.Max(a.Y,Math.Max(b.Y,c.Y)),z1=Math.Max(a.Z,Math.Max(b.Z,c.Z));
        double dx=Math.Max(Math.Max(x0-p.X,0),p.X-x1),dy=Math.Max(Math.Max(y0-p.Y,0),p.Y-y1),dz=Math.Max(Math.Max(z0-p.Z,0),p.Z-z1);
        return dx*dx+dy*dy+dz*dz;
    }

    /// <summary>Adaptively refines triangles near an impact site using crack-free spatial edge matching (1->2, 1->3, 1->4 conforming templates). Returns new vertices added.</summary>
    public static int Refine(SubdivMesh sm,V3 center,double radiusM,V3 normal,double normalReachM,double targetEdgeM,int maxPasses,int maxVertices,PlateSide eventSide,Func<int,bool>? isCellBroken)
        =>RefineGraded(sm,center,radiusM,normal,normalReachM,targetEdgeM,targetEdgeM,maxPasses,maxVertices,eventSide,isCellBroken,default,0);

    /// <summary>0.6.0: Two-tier adaptive refinement with finer edge budget in the crater/ricochet core and optional stretched capsule for ricochet gouges.</summary>
    public static int RefineGraded(
        SubdivMesh sm,
        V3 center,
        double radiusM,
        V3 normal,
        double normalReachM,
        double outerTargetEdgeM,
        double coreTargetEdgeM,
        int maxPasses,
        int maxVertices,
        PlateSide eventSide,
        Func<int,bool>? isCellBroken,
        V3 stretchDir=default,
        double stretchHalfLenM=0)
    {
        if(outerTargetEdgeM<=1e-4||maxPasses<=0||radiusM<=0||sm.Basis.Count>=maxVertices)return 0;
        var n=VM.Norm(normal,new V3(0,1,0));
        double coreEdge=Math.Min(outerTargetEdgeM,Math.Max(1e-4,coreTargetEdgeM));
        double outerEdgeSq=outerTargetEdgeM*outerTargetEdgeM;
        double coreEdgeSq=coreEdge*coreEdge;
        double rCoreSq=(radiusM*0.88)*(radiusM*0.88);
        double rMarkSq=(radiusM*1.22)*(radiusM*1.22);
        double reach=normalReachM>0?normalReachM:DentMath.NormalReach(radiusM);

        V3 sDir=VM.Norm(stretchDir,new V3(0,0,0));
        bool useCapsule=stretchHalfLenM>1e-4&&VM.Len(sDir)>0.5;
        V3 segA=default,segB=default;
        double boundRadiusSq=rMarkSq;
        if(useCapsule)
        {
            var sOrtho=VM.Sub(sDir,VM.Mul(n,VM.Dot(sDir,n)));
            if(VM.Len(sOrtho)>1e-4)sDir=VM.Norm(sOrtho,sDir);
            segA=VM.Sub(center,VM.Mul(sDir,stretchHalfLenM*0.65));
            segB=VM.Add(center,VM.Mul(sDir,stretchHalfLenM*1.15));
            double totalHalf=stretchHalfLenM*1.15+radiusM*1.22;
            boundRadiusSq=totalHalf*totalHalf;
        }

        int startVerts=sm.Basis.Count;

        for(int pass=0;pass<maxPasses;pass++)
        {
            if(sm.Basis.Count>=maxVertices)break;
            int vc=sm.Basis.Count;
            var qKeys=new SpatialPosKey[vc];
            for(int i=0;i<vc;i++)qKeys[i]=SpatialPosKey.Quantize(sm.Basis[i]);

            var marked=new HashSet<SpatialEdgeKey>();
            for(int s=0;s<sm.SubTris.Length;s++)
            {
                var st=sm.SubTris[s];int nTris=st.Count/3;
                var scell=sm.SubTriCell?[s];
                var sside=sm.SubTriSide?[s];
                for(int ti=0;ti<nTris;ti++)
                {
                    if(eventSide!=PlateSide.Whole&&sside!=null&&ti<sside.Count&&sside[ti]!=(byte)eventSide)continue;
                    if(isCellBroken!=null&&scell!=null&&ti<scell.Count&&scell[ti]>=0&&isCellBroken(scell[ti]))continue;
                    int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                    if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;
                    V3 a=sm.Basis[ia],b=sm.Basis[ib],c=sm.Basis[ic];
                    if(TriAabbDistSq(center,a,b,c)>boundRadiusSq+reach*reach)continue;
                    V3 closest=MeshQuery.ClosestOnTriangle(center,a,b,c);
                    V3 w=VM.Sub(closest,center);
                    double dn=VM.Dot(w,n);
                    if(Math.Abs(dn)>reach)continue;

                    double dSq;
                    if(useCapsule)
                    {
                        V3 mid=new((a.X+b.X+c.X)/3f,(a.Y+b.Y+c.Y)/3f,(a.Z+b.Z+c.Z)/3f);
                        V3 cA=MeshQuery.ClosestOnTriangle(segA,a,b,c);
                        V3 cB=MeshQuery.ClosestOnTriangle(segB,a,b,c);
                        double d0=DistSqToSegment(closest,segA,segB);
                        double d1=DistSqToSegment(mid,segA,segB);
                        double d2=DistSqToSegment(cA,segA,segB);
                        double d3=DistSqToSegment(cB,segA,segB);
                        dSq=Math.Min(Math.Min(d0,d1),Math.Min(d2,d3));
                    }
                    else
                    {
                        V3 wp=VM.Sub(w,VM.Mul(n,dn));
                        dSq=VM.Dot(wp,wp);
                    }
                    if(dSq>rMarkSq)continue;
                    double edgeSq=dSq<=rCoreSq?coreEdgeSq:outerEdgeSq;

                    if(DistSq(a,b)>edgeSq)marked.Add(new SpatialEdgeKey(qKeys[ia],qKeys[ib]));
                    if(DistSq(b,c)>edgeSq)marked.Add(new SpatialEdgeKey(qKeys[ib],qKeys[ic]));
                    if(DistSq(c,a)>edgeSq)marked.Add(new SpatialEdgeKey(qKeys[ic],qKeys[ia]));
                }
            }
            if(marked.Count==0)break;
            SplitMarkedEdges(sm,qKeys,vc,marked);
        }

        int added=sm.Basis.Count-startVerts;
        if(added>0)sm.BumpTopology();
        return added;
    }

    private static double DistSqToSegment(V3 p,V3 a,V3 b)
    {
        double abx=b.X-a.X,aby=b.Y-a.Y,abz=b.Z-a.Z;
        double lenSq=abx*abx+aby*aby+abz*abz;
        double t=lenSq<1e-12?0.0:Math.Clamp(((p.X-a.X)*abx+(p.Y-a.Y)*aby+(p.Z-a.Z)*abz)/lenSq,0.0,1.0);
        double dx=p.X-(a.X+abx*t),dy=p.Y-(a.Y+aby*t),dz=p.Z-(a.Z+abz*t);
        return dx*dx+dy*dy+dz*dz;
    }

    /// <summary>Adaptively refines surviving triangles in a band around newly broken cells so torn borders can curl smoothly.</summary>
    public static int RefineBoundary(SubdivMesh sm,IReadOnlySet<int> newlyBrokenCells,Func<int,bool> isCellBroken,double bandWidthM,double targetEdgeM,int maxPasses,int maxVertices)
    {
        if(newlyBrokenCells.Count==0||bandWidthM<=0||targetEdgeM<=1e-4||maxPasses<=0||sm.SubTriCell==null||sm.Basis.Count>=maxVertices)return 0;
        int vc0=sm.Basis.Count;
        var survivingKeys=new HashSet<SpatialPosKey>();
        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var scell=sm.SubTriCell[s];int n=st.Count/3;
            for(int ti=0;ti<n&&ti<scell.Count;ti++)
            {
                int cell=scell[ti];if(cell<0||isCellBroken(cell))continue;
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                if(ia>=0&&ia<vc0)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[ia]));
                if(ib>=0&&ib<vc0)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[ib]));
                if(ic>=0&&ic<vc0)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[ic]));
            }
        }
        if(survivingKeys.Count==0)return 0;

        var segSeen=new HashSet<SpatialEdgeKey>();
        var borderSegs=new List<(V3 A,V3 B)>();
        var ptSeen=new HashSet<SpatialPosKey>();
        var borderPts=new List<V3>();
        float x0=float.MaxValue,y0=float.MaxValue,z0=float.MaxValue,x1=float.MinValue,y1=float.MinValue,z1=float.MinValue;
        void AddPt(V3 p,SpatialPosKey k)
        {
            if(ptSeen.Add(k))
            {
                borderPts.Add(p);
                x0=Math.Min(x0,p.X);y0=Math.Min(y0,p.Y);z0=Math.Min(z0,p.Z);
                x1=Math.Max(x1,p.X);y1=Math.Max(y1,p.Y);z1=Math.Max(z1,p.Z);
            }
        }
        void CheckEdge(int u,int v)
        {
            if(u<0||v<0||u>=vc0||v>=vc0)return;
            var ku=SpatialPosKey.Quantize(sm.Basis[u]);
            var kv=SpatialPosKey.Quantize(sm.Basis[v]);
            bool su=survivingKeys.Contains(ku),sv=survivingKeys.Contains(kv);
            if(su)AddPt(sm.Basis[u],ku);
            if(sv)AddPt(sm.Basis[v],kv);
            if(su&&sv&&segSeen.Add(new SpatialEdgeKey(ku,kv)))
                borderSegs.Add((sm.Basis[u],sm.Basis[v]));
        }
        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var scell=sm.SubTriCell[s];int n=st.Count/3;
            for(int ti=0;ti<n&&ti<scell.Count;ti++)
            {
                if(!newlyBrokenCells.Contains(scell[ti]))continue;
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                CheckEdge(ia,ib);CheckEdge(ib,ic);CheckEdge(ic,ia);
            }
        }
        if(borderPts.Count==0)return 0;

        float bw=(float)bandWidthM;
        x0-=bw;y0-=bw;z0-=bw;x1+=bw;y1+=bw;z1+=bw;
        double bwSq=bandWidthM*bandWidthM;
        double minEdgeSq=targetEdgeM*targetEdgeM;
        int startVerts=sm.Basis.Count;

        double BorderDistSq(V3 p)
        {
            double best=double.MaxValue;
            if(borderSegs.Count>0)
            {
                for(int i=0;i<borderSegs.Count;i++){double d=DistSqToSegment(p,borderSegs[i].A,borderSegs[i].B);if(d<best)best=d;if(best<=bwSq)return best;}
                return best;
            }
            for(int i=0;i<borderPts.Count;i++){double d=DistSq(p,borderPts[i]);if(d<best)best=d;if(best<=bwSq)return best;}
            return best;
        }

        for(int pass=0;pass<maxPasses;pass++)
        {
            if(sm.Basis.Count>=maxVertices)break;
            int vc=sm.Basis.Count;
            var qKeys=new SpatialPosKey[vc];
            for(int i=0;i<vc;i++)qKeys[i]=SpatialPosKey.Quantize(sm.Basis[i]);
            var marked=new HashSet<SpatialEdgeKey>();

            for(int s=0;s<sm.SubTris.Length;s++)
            {
                var st=sm.SubTris[s];var scell=sm.SubTriCell[s];int n=st.Count/3;
                for(int ti=0;ti<n&&ti<scell.Count;ti++)
                {
                    int cell=scell[ti];if(cell<0||isCellBroken(cell))continue;
                    int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                    if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;
                    V3 a=sm.Basis[ia],b=sm.Basis[ib],c=sm.Basis[ic];
                    float tx0=Math.Min(a.X,Math.Min(b.X,c.X)),ty0=Math.Min(a.Y,Math.Min(b.Y,c.Y)),tz0=Math.Min(a.Z,Math.Min(b.Z,c.Z));
                    float tx1=Math.Max(a.X,Math.Max(b.X,c.X)),ty1=Math.Max(a.Y,Math.Max(b.Y,c.Y)),tz1=Math.Max(a.Z,Math.Max(b.Z,c.Z));
                    if(tx1<x0||tx0>x1||ty1<y0||ty0>y1||tz1<z0||tz0>z1)continue;
                    var cen=new V3((a.X+b.X+c.X)/3f,(a.Y+b.Y+c.Y)/3f,(a.Z+b.Z+c.Z)/3f);
                    if(BorderDistSq(cen)>bwSq&&BorderDistSq(a)>bwSq&&BorderDistSq(b)>bwSq&&BorderDistSq(c)>bwSq)continue;
                    if(DistSq(a,b)>minEdgeSq)marked.Add(new SpatialEdgeKey(qKeys[ia],qKeys[ib]));
                    if(DistSq(b,c)>minEdgeSq)marked.Add(new SpatialEdgeKey(qKeys[ib],qKeys[ic]));
                    if(DistSq(c,a)>minEdgeSq)marked.Add(new SpatialEdgeKey(qKeys[ic],qKeys[ia]));
                }
            }
            if(marked.Count==0)break;
            SplitMarkedEdges(sm,qKeys,vc,marked);
        }

        int added=sm.Basis.Count-startVerts;
        if(added>0)sm.BumpTopology();
        return added;
    }

    private static void SplitMarkedEdges(SubdivMesh sm,SpatialPosKey[] qKeys,int vc,HashSet<SpatialEdgeKey> marked)
    {
        var midCache=new Dictionary<long,int>();
        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];int nTris=st.Count/3;
            var scell=sm.SubTriCell?[s];
            var sside=sm.SubTriSide?[s];
            var outTris=new List<int>(st.Count);
            var outCells=scell!=null?new List<int>(nTris):null;
            var outSides=sside!=null?new List<byte>(nTris):null;

            void Emit(int v0,int v1,int v2,int cellVal,byte sideVal,bool hasCell,bool hasSide)
            {
                outTris.Add(v0);outTris.Add(v1);outTris.Add(v2);
                if(hasCell)outCells!.Add(cellVal);
                if(hasSide)outSides!.Add(sideVal);
            }

            for(int ti=0;ti<nTris;ti++)
            {
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                int cellVal=scell!=null&&ti<scell.Count?scell[ti]:-1;
                byte sideVal=sside!=null&&ti<sside.Count?sside[ti]:(byte)0;
                bool hasCell=outCells!=null,hasSide=outSides!=null;

                if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)
                {
                    Emit(ia,ib,ic,cellVal,sideVal,hasCell,hasSide);
                    continue;
                }
                bool m0=marked.Contains(new SpatialEdgeKey(qKeys[ia],qKeys[ib]));
                bool m1=marked.Contains(new SpatialEdgeKey(qKeys[ib],qKeys[ic]));
                bool m2=marked.Contains(new SpatialEdgeKey(qKeys[ic],qKeys[ia]));
                int cnt=(m0?1:0)+(m1?1:0)+(m2?1:0);

                if(cnt==0)
                {
                    Emit(ia,ib,ic,cellVal,sideVal,hasCell,hasSide);
                }
                else if(cnt==3)
                {
                    int m01=GetMidpoint(sm,midCache,ia,ib);
                    int m12=GetMidpoint(sm,midCache,ib,ic);
                    int m20=GetMidpoint(sm,midCache,ic,ia);
                    Emit(ia,m01,m20,cellVal,sideVal,hasCell,hasSide);
                    Emit(m01,ib,m12,cellVal,sideVal,hasCell,hasSide);
                    Emit(m20,m12,ic,cellVal,sideVal,hasCell,hasSide);
                    Emit(m01,m12,m20,cellVal,sideVal,hasCell,hasSide);
                }
                else if(cnt==2)
                {
                    // Rotate triangle so (p0,p1) and (p2,p0) are the two marked edges; (p1,p2) is unmarked.
                    int p0,p1,p2;
                    if(!m1){p0=ia;p1=ib;p2=ic;}
                    else if(!m2){p0=ib;p1=ic;p2=ia;}
                    else{p0=ic;p1=ia;p2=ib;}
                    int mA=GetMidpoint(sm,midCache,p0,p1);
                    int mB=GetMidpoint(sm,midCache,p2,p0);
                    Emit(p0,mA,mB,cellVal,sideVal,hasCell,hasSide);
                    if(DistSq(sm.Basis[p1],sm.Basis[mB])<=DistSq(sm.Basis[p2],sm.Basis[mA]))
                    {
                        Emit(mA,p1,mB,cellVal,sideVal,hasCell,hasSide);
                        Emit(p1,p2,mB,cellVal,sideVal,hasCell,hasSide);
                    }
                    else
                    {
                        Emit(mA,p1,p2,cellVal,sideVal,hasCell,hasSide);
                        Emit(mA,p2,mB,cellVal,sideVal,hasCell,hasSide);
                    }
                }
                else
                {
                    // Rotate so (p0,p1) is the single marked edge.
                    int p0,p1,p2;
                    if(m0){p0=ia;p1=ib;p2=ic;}
                    else if(m1){p0=ib;p1=ic;p2=ia;}
                    else{p0=ic;p1=ia;p2=ib;}
                    int mA=GetMidpoint(sm,midCache,p0,p1);
                    Emit(p0,mA,p2,cellVal,sideVal,hasCell,hasSide);
                    Emit(mA,p1,p2,cellVal,sideVal,hasCell,hasSide);
                }
            }
            sm.SubTris[s]=outTris;
            if(sm.SubTriCell!=null&&outCells!=null)sm.SubTriCell[s]=outCells;
            if(sm.SubTriSide!=null&&outSides!=null)sm.SubTriSide[s]=outSides;
        }
    }
}

/// <summary>Crease-aware spatial normal and tangent recomputation across split-vertex seams.</summary>
public static class SmartNormals
{
    private static V3 RotateTangent(V3 t,V3 oldN,V3 newN)
    {
        double dot=Math.Clamp(VM.Dot(oldN,newN),-1.0,1.0);
        V3 rotated;
        if(dot>0.9999)
        {
            rotated=t;
        }
        else if(dot<-0.9999)
        {
            rotated=VM.Mul(t,-1);
        }
        else
        {
            var axis=VM.Cross(oldN,newN);
            double sinA=VM.Len(axis);
            if(sinA<1e-9)rotated=t;
            else
            {
                var k=VM.Mul(axis,1.0/sinA);
                double cosA=dot;
                // Rodrigues: t*cosA + (k x t)*sinA + k*(k.t)*(1 - cosA)
                var kxt=VM.Cross(k,t);
                double kdt=VM.Dot(k,t);
                rotated=new V3(
                    (float)(t.X*cosA+kxt.X*sinA+k.X*kdt*(1.0-cosA)),
                    (float)(t.Y*cosA+kxt.Y*sinA+k.Y*kdt*(1.0-cosA)),
                    (float)(t.Z*cosA+kxt.Z*sinA+k.Z*kdt*(1.0-cosA)));
            }
        }
        // Re-orthogonalize against newN
        double d=VM.Dot(rotated,newN);
        var ortho=VM.Sub(rotated,VM.Mul(newN,d));
        return VM.Norm(ortho,t);
    }

    public static void Compute(SubdivMesh sm,IReadOnlyList<int>? dirtyVerts,double smoothAngleDeg,Func<int,bool>? isCellBroken)
    {
        sm.EnsureSpatialIndex();
        int vc=sm.Basis.Count;
        if(vc==0)return;
        double cosCrease=Math.Cos(Math.Clamp(smoothAngleDeg,5.0,89.0)*Math.PI/180.0);

        // Determine spatial groups to recompute (dirty vertices + their 1-ring triangle neighbours)
        HashSet<SpatialPosKey>? affectedGroups=null;
        if(dirtyVerts!=null)
        {
            if(dirtyVerts.Count==0)return;
            affectedGroups=new HashSet<SpatialPosKey>();
            for(int i=0;i<dirtyVerts.Count;i++)
            {
                int vi=dirtyVerts[i];if(vi<0||vi>=vc)continue;
                affectedGroups.Add(sm.VertKey(vi));
                foreach(var (sub,ti) in sm.Incident(vi))
                {
                    var st=sm.SubTris[sub];
                    int a=st[ti*3],b=st[ti*3+1],c=st[ti*3+2];
                    if(a>=0&&a<vc)affectedGroups.Add(sm.VertKey(a));
                    if(b>=0&&b<vc)affectedGroups.Add(sm.VertKey(b));
                    if(c>=0&&c<vc)affectedGroups.Add(sm.VertKey(c));
                }
            }
            if(affectedGroups.Count==0)return;
        }

        // Compute deformed positions & face normals only for triangles touching affected groups (or all if full pass)
        var defPos=new V3[vc];
        if(affectedGroups==null)
        {
            for(int i=0;i<vc;i++)defPos[i]=sm.Position(i);
        }
        var faceAreaN=new Dictionary<(int Sub,int Tri),V3>();
        var faceUnitN=new Dictionary<(int Sub,int Tri),V3>();

        void EnsureTriNormal(int sub,int ti)
        {
            var tk=(sub,ti);
            if(faceAreaN.ContainsKey(tk))return;
            if(isCellBroken!=null&&sm.SubTriCell!=null&&ti<sm.SubTriCell[sub].Count)
            {
                int cell=sm.SubTriCell[sub][ti];
                if(cell>=0&&isCellBroken(cell))return;
            }
            var st=sm.SubTris[sub];
            int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
            if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)return;
            V3 p0=affectedGroups==null?defPos[ia]:sm.Position(ia);
            V3 p1=affectedGroups==null?defPos[ib]:sm.Position(ib);
            V3 p2=affectedGroups==null?defPos[ic]:sm.Position(ic);
            var awn=VM.Cross(VM.Sub(p1,p0),VM.Sub(p2,p0));
            double len=VM.Len(awn);
            if(len<1e-12)return;
            faceAreaN[tk]=awn;
            faceUnitN[tk]=VM.Mul(awn,1.0/len);
        }

        bool hasTangents=sm.Tangents.Count==vc;
        var seenGroups=new HashSet<SpatialPosKey>();
        for(int vi=0;vi<vc;vi++)
        {
            var key=sm.VertKey(vi);
            if(affectedGroups!=null&&!affectedGroups.Contains(key))continue;
            var group=sm.WeldGroup(key);if(group==null)continue;

            // Precompute face normals for every triangle touching any vertex in this weld group once
            if(seenGroups.Add(key))
            {
                for(int gi=0;gi<group.Count;gi++)
                    foreach(var (sub,ti) in sm.Incident(group[gi]))
                        EnsureTriNormal(sub,ti);
            }

            // Reference normal of this specific vertex's own incident triangles
            double rx=0,ry=0,rz=0;
            foreach(var (sub,ti) in sm.Incident(vi))
            {
                if(faceAreaN.TryGetValue((sub,ti),out var fn)){rx+=fn.X;ry+=fn.Y;rz+=fn.Z;}
            }
            var refSum=new V3((float)rx,(float)ry,(float)rz);
            if(VM.Len(refSum)<1e-12)continue;
            var refN=VM.Norm(refSum,sm.Normals[vi]);

            // Accumulate area-weighted face normals across the whole spatial weld group within smoothAngleDeg
            double sx=0,sy=0,sz=0;
            for(int gi=0;gi<group.Count;gi++)
            {
                foreach(var (sub,ti) in sm.Incident(group[gi]))
                {
                    var tk=(sub,ti);
                    if(!faceUnitN.TryGetValue(tk,out var u))continue;
                    if(VM.Dot(u,refN)<cosCrease)continue;
                    var aw=faceAreaN[tk];
                    sx+=aw.X;sy+=aw.Y;sz+=aw.Z;
                }
            }
            var sum=new V3((float)sx,(float)sy,(float)sz);
            if(VM.Len(sum)<1e-12)continue;
            var newN=VM.Norm(sum,refN);
            var oldN=sm.Normals[vi];
            sm.Normals[vi]=newN;
            if(hasTangents)
            {
                var ot=sm.Tangents[vi];
                var nt=RotateTangent(new V3(ot.X,ot.Y,ot.Z),oldN,newN);
                sm.Tangents[vi]=new V4(nt.X,nt.Y,nt.Z,ot.W);
            }
        }
    }

    /// <summary>Computes crease-aware spatial normals for an arbitrary standalone triangle mesh (e.g., debris).</summary>
    public static V3[] ComputeSimple(V3[] verts,int[] tris,double smoothAngleDeg)
    {
        int vc=verts.Length;
        var outN=new V3[vc];
        if(vc==0)return outN;
        double cosCrease=Math.Cos(Math.Clamp(smoothAngleDeg,5.0,89.0)*Math.PI/180.0);
        var groups=new Dictionary<SpatialPosKey,List<int>>();
        var keys=new SpatialPosKey[vc];
        var inc=new List<int>[vc];
        for(int i=0;i<vc;i++)
        {
            var k=SpatialPosKey.Quantize(verts[i]);keys[i]=k;
            if(!groups.TryGetValue(k,out var g)){g=new List<int>(2);groups[k]=g;}
            g.Add(i);inc[i]=new List<int>(4);
            outN[i]=new V3(0,1,0);
        }
        int tc=tris.Length/3;
        var areaN=new V3[tc];var unitN=new V3[tc];var valid=new bool[tc];
        for(int ti=0;ti<tc;ti++)
        {
            int ia=tris[ti*3],ib=tris[ti*3+1],ic=tris[ti*3+2];
            if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;
            inc[ia].Add(ti);inc[ib].Add(ti);inc[ic].Add(ti);
            var awn=VM.Cross(VM.Sub(verts[ib],verts[ia]),VM.Sub(verts[ic],verts[ia]));
            double l=VM.Len(awn);if(l<1e-12)continue;
            areaN[ti]=awn;unitN[ti]=VM.Mul(awn,1.0/l);valid[ti]=true;
        }
        for(int vi=0;vi<vc;vi++)
        {
            double rx=0,ry=0,rz=0;
            foreach(int ti in inc[vi])if(valid[ti]){rx+=areaN[ti].X;ry+=areaN[ti].Y;rz+=areaN[ti].Z;}
            var refSum=new V3((float)rx,(float)ry,(float)rz);
            if(VM.Len(refSum)<1e-12)continue;
            var refN=VM.Norm(refSum,outN[vi]);
            double sx=0,sy=0,sz=0;
            foreach(int vj in groups[keys[vi]])
            {
                foreach(int ti in inc[vj])
                {
                    if(!valid[ti]||VM.Dot(unitN[ti],refN)<cosCrease)continue;
                    sx+=areaN[ti].X;sy+=areaN[ti].Y;sz+=areaN[ti].Z;
                }
            }
            var sum=new V3((float)sx,(float)sy,(float)sz);
            if(VM.Len(sum)>=1e-12)outN[vi]=VM.Norm(sum,refN);
        }
        return outN;
    }
}

/// <summary>0.8.0: Solid sheared-steel cross-section wall geometry bridging outer and inner skins along torn borders.</summary>
public sealed class RimCapData
{
    public readonly List<V3> Verts=new(),Normals=new();
    public readonly List<V4> Tangents=new();
    public readonly List<V2> UV=new(),UV2=new();
    public readonly List<Col32> Colors=new();
    public readonly List<int> Tris=new();
}

/// <summary>0.8.0: Synthesizes flat-shaded steel thickness walls across newly opened outer/inner boundary edges.</summary>
public static class RimCapBuilder
{
    private readonly struct BoundEdge
    {
        public readonly int U,V,W;
        public readonly V3 NTri;
        public readonly SpatialEdgeKey Key;
        public BoundEdge(int u,int v,int w,V3 nTri,SpatialEdgeKey key){U=u;V=v;W=w;NTri=nTri;Key=key;}
    }

    private sealed class BoundVert
    {
        public int VertIdx;
        public SpatialPosKey Key;
        public V3 BasisPos,DefPos,TriNorm;
        public bool HasThick;
    }

    public static RimCapData? Build(SubdivMesh sm,Func<int,bool> isBroken,double fallbackThicknessM,int maxQuads=4000)
    {
        if(sm.SubTriCell==null||sm.Basis.Count==0)return null;
        int vc=sm.Basis.Count;
        var vKeys=new SpatialPosKey[vc];
        for(int i=0;i<vc;i++)vKeys[i]=SpatialPosKey.Quantize(sm.Basis[i]);

        var brokenEdges=new HashSet<SpatialEdgeKey>();
        var survEdgeCount=new Dictionary<SpatialEdgeKey,int>();

        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int nt=st.Count/3;
            for(int ti=0;ti<nt;ti++)
            {
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;
                int cell=ti<sc.Count?sc[ti]:-1;
                var e0=new SpatialEdgeKey(vKeys[ia],vKeys[ib]);
                var e1=new SpatialEdgeKey(vKeys[ib],vKeys[ic]);
                var e2=new SpatialEdgeKey(vKeys[ic],vKeys[ia]);
                if(cell>=0&&isBroken(cell))
                {
                    brokenEdges.Add(e0);brokenEdges.Add(e1);brokenEdges.Add(e2);
                }
                else
                {
                    survEdgeCount[e0]=survEdgeCount.TryGetValue(e0,out int c0)?c0+1:1;
                    survEdgeCount[e1]=survEdgeCount.TryGetValue(e1,out int c1)?c1+1:1;
                    survEdgeCount[e2]=survEdgeCount.TryGetValue(e2,out int c2)?c2+1:1;
                }
            }
        }
        if(brokenEdges.Count==0)return null;

        var boundEdges=new List<BoundEdge>();
        var vertMap=new Dictionary<int,BoundVert>();
        var boundVerts=new List<BoundVert>();

        void RecordVert(int vi,V3 nTri)
        {
            if(vertMap.ContainsKey(vi))return;
            bool ht=sm.ThickVec!=null&&vi<sm.ThickVec.Count&&VM.Len(sm.ThickVec[vi])>=0.0015;
            var bv=new BoundVert
            {
                VertIdx=vi,Key=vKeys[vi],
                BasisPos=sm.Basis[vi],DefPos=sm.Position(vi),
                TriNorm=nTri,HasThick=ht
            };
            vertMap[vi]=bv;boundVerts.Add(bv);
        }

        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int nt=st.Count/3;
            for(int ti=0;ti<nt;ti++)
            {
                int cell=ti<sc.Count?sc[ti]:-1;
                if(cell>=0&&isBroken(cell))continue;
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)continue;

                V3 pa=sm.Position(ia),pb=sm.Position(ib),pc=sm.Position(ic);
                var nTri=VM.Norm(VM.Cross(VM.Sub(pb,pa),VM.Sub(pc,pa)),new V3(0,1,0));

                void TryEdge(int u,int v,int w)
                {
                    var ek=new SpatialEdgeKey(vKeys[u],vKeys[v]);
                    if(!brokenEdges.Contains(ek)||!survEdgeCount.TryGetValue(ek,out int cnt)||cnt!=1)return;
                    boundEdges.Add(new BoundEdge(u,v,w,nTri,ek));
                    RecordVert(u,nTri);RecordVert(v,nTri);
                }
                TryEdge(ia,ib,ic);
                TryEdge(ib,ic,ia);
                TryEdge(ic,ia,ib);
            }
        }
        if(boundEdges.Count==0)return null;

        var data=new RimCapData();
        bool hasTan=sm.Tangents.Count==vc;
        bool hasUv=sm.UV.Count==vc;
        bool hasUv2=sm.UV2.Count==vc;
        bool hasCol=sm.Colors.Count==vc;

        void EmitQuad(V3 pA,V3 pB,V3 pInB,V3 pInA,V3 pC,V3 nTri,int srcA,int srcB)
        {
            var edge=VM.Sub(pB,pA);double eLen=VM.Len(edge);
            var dA=VM.Sub(pInA,pA);var dB=VM.Sub(pInB,pB);
            double tLen=0.5*(VM.Len(dA)+VM.Len(dB));
            if(eLen<1e-4||tLen<1e-4)return;

            var rawN=VM.Cross(edge,VM.Sub(pInB,pA));
            if(VM.Len(rawN)<1e-8)rawN=VM.Cross(edge,dA);
            if(VM.Len(rawN)<1e-8)return;

            var nWall=VM.Norm(rawN,new V3(0,1,0));
            var inwardDir=VM.Cross(nTri,edge);
            if(VM.Len(inwardDir)<1e-8)inwardDir=VM.Sub(pC,pA);
            bool flip=VM.Dot(nWall,inwardDir)>0;
            if(flip)nWall=VM.Mul(nWall,-1.0);

            int v0=data.Verts.Count;
            data.Verts.Add(pA);data.Verts.Add(pB);data.Verts.Add(pInB);data.Verts.Add(pInA);
            for(int k=0;k<4;k++)data.Normals.Add(nWall);

            if(hasUv)
            {
                float u1=(float)(eLen*2.0),v1=(float)(tLen*2.0);
                data.UV.Add(new V2(0,0));data.UV.Add(new V2(u1,0));
                data.UV.Add(new V2(u1,v1));data.UV.Add(new V2(0,v1));
            }
            if(hasUv2)for(int k=0;k<4;k++)data.UV2.Add(new V2(0,0));
            if(hasTan)
            {
                var eDir=VM.Norm(edge,new V3(1,0,0));
                var tan=new V4(eDir.X,eDir.Y,eDir.Z,1f);
                for(int k=0;k<4;k++)data.Tangents.Add(tan);
            }
            if(hasCol)
            {
                static Col32 Darken(Col32 c)=>new((byte)((c.R*166)>>8),(byte)((c.G*166)>>8),(byte)((c.B*166)>>8),c.A);
                Col32 cA=srcA<sm.Colors.Count?Darken(sm.Colors[srcA]):new Col32(150,145,140,255);
                Col32 cB=srcB<sm.Colors.Count?Darken(sm.Colors[srcB]):new Col32(150,145,140,255);
                data.Colors.Add(cA);data.Colors.Add(cB);data.Colors.Add(cB);data.Colors.Add(cA);
            }

            if(!flip)
            {
                data.Tris.Add(v0);data.Tris.Add(v0+1);data.Tris.Add(v0+2);
                data.Tris.Add(v0);data.Tris.Add(v0+2);data.Tris.Add(v0+3);
            }
            else
            {
                data.Tris.Add(v0);data.Tris.Add(v0+2);data.Tris.Add(v0+1);
                data.Tris.Add(v0);data.Tris.Add(v0+3);data.Tris.Add(v0+2);
            }
        }

        int quads=0;
        if(sm.ThickVec!=null)
        {
            var innerVerts=new List<BoundVert>();
            foreach(var bv in boundVerts)if(!bv.HasThick)innerVerts.Add(bv);

            V3 FindInner(V3 basisOuter,V3 tvInward,V3 nTri,V3 defOuter,double edgeLen)
            {
                var target=VM.Add(basisOuter,tvInward);
                double maxR=Math.Min(edgeLen*0.45,Math.Max(0.015,Math.Min(0.035,VM.Len(tvInward)*0.35)));
                double bestSq=maxR*maxR;
                BoundVert? best=null;
                for(int i=0;i<innerVerts.Count;i++)
                {
                    var iv=innerVerts[i];
                    if(VM.Dot(iv.TriNorm,nTri)>=-0.25)continue;
                    var d=VM.Sub(iv.BasisPos,target);
                    double sq=VM.Dot(d,d);
                    if(sq<bestSq){bestSq=sq;best=iv;}
                }
                return best!=null?best.DefPos:VM.Add(defOuter,tvInward);
            }

            for(int i=0;i<boundEdges.Count&&quads<maxQuads;i++)
            {
                var e=boundEdges[i];
                var tvU=sm.ThickVec[e.U];var tvV=sm.ThickVec[e.V];
                if(VM.Len(tvU)<0.0015||VM.Len(tvV)<0.0015)continue;
                if(VM.Dot(tvU,e.NTri)>0)tvU=VM.Mul(tvU,-1.0);
                if(VM.Dot(tvV,e.NTri)>0)tvV=VM.Mul(tvV,-1.0);
                V3 pA=sm.Position(e.U),pB=sm.Position(e.V),pC=sm.Position(e.W);
                var edgeVec=VM.Sub(pB,pA);
                double eLen=VM.Len(edgeVec);
                if(eLen<1e-4)continue;
                V3 pInA=FindInner(sm.Basis[e.U],tvU,e.NTri,pA,eLen);
                V3 pInB=FindInner(sm.Basis[e.V],tvV,e.NTri,pB,eLen);
                if(VM.Len(VM.Sub(pInB,pInA))<1e-4||VM.Dot(edgeVec,VM.Sub(pInB,pInA))<=1e-6)
                {
                    pInA=VM.Add(pA,tvU);pInB=VM.Add(pB,tvV);
                }
                EmitQuad(pA,pB,pInB,pInA,pC,e.NTri,e.U,e.V);
                quads++;
            }
        }
        else if(fallbackThicknessM>=0.002)
        {
            var emitted=new HashSet<SpatialEdgeKey>();
            var emittedMids=new List<V3>();

            BoundVert? FindPartner(V3 basisOuter,V3 target,V3 nTri,V3 negTri,double maxRSq)
            {
                double bestSq=maxRSq;BoundVert? best=null;
                for(int i=0;i<boundVerts.Count;i++)
                {
                    var cand=boundVerts[i];
                    if(VM.Dot(cand.TriNorm,nTri)>=-0.35)continue;
                    if(VM.Dot(VM.Sub(cand.BasisPos,basisOuter),negTri)<=0.001)continue;
                    var d=VM.Sub(cand.BasisPos,target);
                    double sq=VM.Dot(d,d);
                    if(sq<bestSq){bestSq=sq;best=cand;}
                }
                return best;
            }

            for(int i=0;i<boundEdges.Count&&quads<maxQuads;i++)
            {
                var e=boundEdges[i];
                if(emitted.Contains(e.Key))continue;
                V3 pA=sm.Position(e.U),pB=sm.Position(e.V),pC=sm.Position(e.W);
                var edgeVec=VM.Sub(pB,pA);
                double eLen=VM.Len(edgeVec);
                if(eLen<1e-4)continue;

                var negN=VM.Mul(e.NTri,-1.0);
                var inward=VM.Mul(negN,fallbackThicknessM);
                double maxR=Math.Min(eLen*0.45,Math.Max(0.015,Math.Min(0.035,fallbackThicknessM*0.45)));
                double maxRSq=maxR*maxR;
                var candA=FindPartner(sm.Basis[e.U],VM.Add(sm.Basis[e.U],inward),e.NTri,negN,maxRSq);
                var candB=FindPartner(sm.Basis[e.V],VM.Add(sm.Basis[e.V],inward),e.NTri,negN,maxRSq);
                bool usePartner=candA!=null&&candB!=null&&!candA.Key.Equals(candB.Key)&&VM.Dot(edgeVec,VM.Sub(candB.DefPos,candA.DefPos))>1e-6;
                if(!usePartner&&fallbackThicknessM>0.15)continue;

                V3 qInA=usePartner?candA!.DefPos:VM.Add(pA,inward);
                V3 qInB=usePartner?candB!.DefPos:VM.Add(pB,inward);
                var qMid=new V3((pA.X+pB.X+qInB.X+qInA.X)*0.25f,(pA.Y+pB.Y+qInB.Y+qInA.Y)*0.25f,(pA.Z+pB.Z+qInB.Z+qInA.Z)*0.25f);
                double dupTol=Math.Max(0.012,eLen*0.35);
                double dupTolSq=dupTol*dupTol;
                bool dup=false;
                for(int mi=0;mi<emittedMids.Count;mi++)
                {
                    var dm=VM.Sub(qMid,emittedMids[mi]);
                    if(VM.Dot(dm,dm)<=dupTolSq){dup=true;break;}
                }
                if(dup)continue;

                emitted.Add(e.Key);
                if(usePartner)emitted.Add(new SpatialEdgeKey(candA!.Key,candB!.Key));
                emittedMids.Add(qMid);
                EmitQuad(pA,pB,qInB,qInA,pC,e.NTri,e.U,e.V);
                quads++;
            }
        }

        return data.Tris.Count>0?data:null;
    }
}
