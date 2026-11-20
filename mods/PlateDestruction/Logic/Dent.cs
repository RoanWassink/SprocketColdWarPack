namespace PlateDestructionP0;

// ================================================================================================
// 0.4.0: dents (visual vertex displacement) and flying debris (own ballistic integration).
// Pure managed code (no Unity / Il2Cpp types) so the managed tests can run without the game.
// ================================================================================================

/// <summary>Small vector helpers on the pure V3 struct (the logic layer has no UnityEngine types).</summary>
public static class VM
{
    public static V3 Add(V3 a,V3 b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static V3 Sub(V3 a,V3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static V3 Mul(V3 a,double s)=>new((float)(a.X*s),(float)(a.Y*s),(float)(a.Z*s));
    public static double Dot(V3 a,V3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
    public static V3 Cross(V3 a,V3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    public static double Len(V3 a)=>Math.Sqrt(Dot(a,a));
    /// <summary>Unit vector; the fallback when the length is (almost) zero.</summary>
    public static V3 Norm(V3 a,V3 fallback){double l=Len(a);return l<1e-9?fallback:Mul(a,1.0/l);}
}

public enum DentStyle{Smooth=0,Kinetic=1,Blast=2,TearEdge=3,Ricochet=4,Embedded=5,ExitBlowout=6}

/// <summary>One dent in the local space of a renderer: centre, unit displacement direction, depth and radius (all in mesh units = metres), and the half it belongs to.</summary>
public readonly struct DentEvent
{
    public readonly V3 Center,Normal,ShotDir;public readonly double DepthM,RadiusM,ThicknessM,LipRatio,Buckling;
    public readonly PlateSide Side;public readonly DentStyle Style;public readonly uint Seed;
    public DentEvent(V3 center,V3 normal,double depthM,double radiusM,PlateSide side)
        :this(center,normal,depthM,radiusM,side,DentStyle.Smooth,normal,0,0,0,0){}
    public DentEvent(V3 center,V3 normal,double depthM,double radiusM,PlateSide side,DentStyle style,V3 shotDir,double thicknessM,double lipRatio,double buckling,uint seed)
    {Center=center;Normal=normal;DepthM=depthM;RadiusM=radiusM;Side=side;Style=style;ShotDir=shotDir;ThicknessM=thicknessM;LipRatio=lipRatio;Buckling=buckling;Seed=seed;}
}

/// <summary>Dent sizes and realistic physical deformation profiles (0.5.0).</summary>
public static class DentMath
{
    /// <summary>Smooth falloff (1-(d/R)^2)^2 inside the radius, 0 at and beyond it.</summary>
    public static double Falloff(double d,double r){if(!(r>0)||double.IsNaN(d)||d>=r)return 0;double x=Math.Max(0,d)/r;double a=1-x*x;return a*a;}
    public static double BlastRatio(double blastDamage,double strength){if(!(strength>0)||double.IsNaN(blastDamage)||double.IsInfinity(strength))return 0;return Math.Min(1.0,Math.Max(0.0,blastDamage/strength));}
    public static double BlastDepthMm(double q,double maxDepthMm)=>Math.Max(0,maxDepthMm)*Math.Min(1.0,Math.Max(0.0,q));
    public static double BlastRadiusM(double blastRadiusM,double factor,double min=0.10,double max=1.0)
    {
        double r=factor*blastRadiusM;if(double.IsNaN(r))r=min;
        return Math.Min(max,Math.Max(min,r));
    }
    /// <summary>A dent only reaches vertices this far from the dent centre along the normal: both shells of a plate, but not the mirrored half metres away.</summary>
    public static double NormalReach(double radiusM)=>Math.Max(0.25,radiusM);
    public static V3 Orient(V3 n,V3 away)=>VM.Dot(n,away)>=0?n:VM.Mul(n,-1);

    /// <summary>0.8.1: Ensures a triangle geometric normal matches the mesh's vertex normals (pointing outward into free space).</summary>
    public static V3 FaceOutwardNormal(SubdivMesh sm,int tri,V3 geoNormal)
    {
        var n=VM.Norm(geoNormal,new V3(0,1,0));
        if(tri>=0&&tri*3+2<sm.FlatTris.Length&&sm.Normals.Count==sm.Basis.Count)
        {
            int ia=sm.FlatTris[tri*3],ib=sm.FlatTris[tri*3+1],ic=sm.FlatTris[tri*3+2];
            if(ia>=0&&ib>=0&&ic>=0&&ia<sm.Normals.Count&&ib<sm.Normals.Count&&ic<sm.Normals.Count)
            {
                var avg=VM.Add(VM.Add(sm.Normals[ia],sm.Normals[ib]),sm.Normals[ic]);
                if(VM.Len(avg)>=1e-4&&VM.Dot(n,avg)<0)n=VM.Mul(n,-1.0);
            }
        }
        return n;
    }

    /// <summary>0.8.1: Resolves inward dent push direction when a surface/contact blast lies within a narrow surface band of the mesh skin.</summary>
    public static V3 ResolveBlastAway(V3 blastLocal,V3 center,V3 faceOutN,V3 fallbackDirL,double surfaceBandM=0.04)
    {
        var diff=VM.Sub(center,blastLocal);
        double dist=VM.Len(diff);
        var outN=VM.Norm(faceOutN,new V3(0,0,0));
        bool hasOut=VM.Len(outN)>0.5;
        if(dist>surfaceBandM)return VM.Norm(diff,new V3(0,-1,0));
        if(dist>1e-4&&(!hasOut||VM.Dot(diff,outN)<0))return VM.Norm(diff,new V3(0,-1,0));
        if(hasOut)
        {
            var inN=VM.Mul(outN,-1.0);
            if(VM.Len(fallbackDirL)>0.5)
            {
                var fb=VM.Norm(fallbackDirL,inN);
                if(VM.Dot(fb,inN)>0.15)return fb;
            }
            return inN;
        }
        return VM.Norm(fallbackDirL,new V3(0,-1,0));
    }

    /// <summary>0.8.2: True when a blast thickness probe travels far along a plate's in-plane length (&gt;250 mm and &gt;4x mean thickness), indicating an edge-on rim hit rather than a broadside hit.</summary>
    public static bool IsEdgeOnRim(double spanMm,double plateMeanThMm)
        =>!double.IsNaN(spanMm)&&!double.IsNaN(plateMeanThMm)&&spanMm>250.0&&plateMeanThMm>0&&spanMm>plateMeanThMm*4.0;

    /// <summary>Crater profile in normalised radius x in [0,1): deep inward core for x &lt; 0.65 minus outward raised lip around x = 0.725.</summary>
    public static double CraterProfile(double x,double lipRatio)
    {
        if(double.IsNaN(x)||x<0||x>=1.0)return 0;
        double core=0;
        if(x<0.65){double u=x/0.65;double a=1.0-u*u;core=a*a;}
        double lip=0;
        if(x>0.45&&lipRatio>0){double s=Math.Sin(Math.PI*(x-0.45)/0.55);lip=lipRatio*s*s;}
        return core-lip;
    }

    /// <summary>Kinetic crater depth scaled by shot energy (MJ) and inverse square root of plate thickness (mm).</summary>
    public static double KineticDepthMm(double baseDepthMm,double energyMJ,double thicknessMm)
    {
        if(baseDepthMm<=0||double.IsNaN(baseDepthMm))return 0;
        double e=double.IsNaN(energyMJ)||energyMJ<=0?0.5:energyMJ;
        double th=double.IsNaN(thicknessMm)||thicknessMm<=0?20.0:thicknessMm;
        double eFactor=Math.Clamp(Math.Sqrt(Math.Max(e,0.5)/6.0),0.5,1.5);
        double tFactor=Math.Clamp(Math.Sqrt(20.0/Math.Max(th,3.0)),0.45,1.45);
        return baseDepthMm*eFactor*tFactor;
    }

    /// <summary>0.6.0: Kinetic crater/ricochet depth scaled by impact style and visual scale (hull/turret multiplier).</summary>
    public static double KineticDepthMm(double baseDepthMm,double energyMJ,double thicknessMm,DentStyle style,double visualScale=1.0)
    {
        if(baseDepthMm<=0||double.IsNaN(baseDepthMm))return 0;
        double e=double.IsNaN(energyMJ)||energyMJ<=0?0.5:energyMJ;
        double th=double.IsNaN(thicknessMm)||thicknessMm<=0?20.0:thicknessMm;
        double eFactor=Math.Clamp(Math.Sqrt(Math.Max(e,0.5)/6.0),0.55,1.55);
        double tFactor=Math.Clamp(Math.Sqrt(20.0/Math.Max(th,3.0)),0.55,1.45);
        double styleF=style==DentStyle.Embedded?1.35:style==DentStyle.ExitBlowout?1.15:style==DentStyle.Ricochet?0.95:1.0;
        double sc=double.IsNaN(visualScale)?1.0:Math.Max(0.0,visualScale);
        return baseDepthMm*eFactor*tFactor*styleF*sc;
    }

    /// <summary>0.6.0: Calibre- and energy-scaled crater/gouge radius (m).</summary>
    public static double KineticRadiusM(double baseRadiusM,int calibreMm,double energyMJ,DentStyle style)
    {
        if(!(baseRadiusM>0)||double.IsNaN(baseRadiusM))return 0.06;
        double e=double.IsNaN(energyMJ)||energyMJ<=0?0.5:energyMJ;
        double cal=calibreMm>0?calibreMm:40.0;
        double calF=Math.Clamp(Math.Pow(Math.Max(cal,12.0)/75.0,0.35),0.72,1.55);
        double eF=Math.Clamp(Math.Pow(Math.Max(e,0.5)/4.0,0.22),0.75,1.45);
        double styleF=style==DentStyle.Ricochet?1.15:style==DentStyle.Embedded?1.10:style==DentStyle.ExitBlowout?1.05:0.95;
        return Math.Clamp(baseRadiusM*Math.Max(calF,eF)*styleF,0.06,0.40);
    }

    /// <summary>Thin plates (&lt;=20 mm) bend as one sheet; thicker plates attenuate deformation and suppress outward lip on the interior/back face.</summary>
    public static double BackFaceAttenuation(double dn,double thicknessM,out bool allowLip)
    {
        if(thicknessM<=0.020||double.IsNaN(thicknessM)){allowLip=true;return 1.0;}
        if(dn<=0.015){allowLip=true;return 1.0;}
        allowLip=false;
        return Math.Exp(-(dn-0.015)/Math.Max(0.04,thicknessM));
    }

    public static double BackFaceAttenuationAbs(double dn,double thicknessM,out bool allowLip)
        =>BackFaceAttenuation(Math.Abs(dn),thicknessM,out allowLip);

    /// <summary>0.6.0: Elongated ricochet gouge in normalized graze coords u in (-0.75, 1.25), v in [0, 1): inward scoop plus plowed exit bow-wave lip and sheared side shoulders.</summary>
    public static double RicochetProfile(double u,double v,double lipRatio)
    {
        if(double.IsNaN(u)||double.IsNaN(v)||u<=-0.75||u>=1.25||v<0||v>=1.0)return 0;
        double groove=0;
        if(u>-0.70&&u<0.65&&v<0.75)
        {
            double t=(u+0.70)/1.35;
            double env=Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),1.3)*(0.75+0.55*t);
            double tv=v/0.75;double a=1.0-tv*tv;
            groove=a*a*env;
        }
        double exitLip=0;
        if(u>0.40&&u<1.25&&v<0.95&&lipRatio>0)
        {
            double su=Math.Sin(Math.PI*(u-0.40)/0.85);
            double tv=v/0.95;double b=1.0-tv*tv;
            exitLip=-1.65*lipRatio*su*su*Math.Pow(Math.Max(0,b),1.5);
        }
        double sideLip=0;
        if(u>-0.45&&u<0.75&&v>0.50&&v<1.0&&lipRatio>0)
        {
            double sv=Math.Sin(Math.PI*(v-0.50)/0.50);
            double su=Math.Sin(Math.PI*(u+0.45)/1.20);
            sideLip=-0.55*lipRatio*sv*sv*su*su;
        }
        return groove+exitLip+sideLip;
    }

    /// <summary>0.6.0: Deep stopped/embedded crater with pronounced outward rolled-steel lip.</summary>
    public static double EmbeddedProfile(double x,double lipRatio)
    {
        if(double.IsNaN(x)||x<0||x>=1.0)return 0;
        double core=0;
        if(x<0.62){double u=x/0.62;double a=1.0-u*u;core=1.10*a*a;}
        double lip=0;
        if(x>0.40&&lipRatio>0){double s=Math.Sin(Math.PI*(x-0.40)/0.60);lip=1.50*lipRatio*s*s;}
        return core-lip;
    }

    /// <summary>0.6.0: Outward petaled exit blowout profile on the far face of a penetrated plate.</summary>
    public static double ExitBlowoutProfile(double x,double petalNoise)
    {
        if(double.IsNaN(x)||x<0||x>=1.0)return 0;
        double a=1.0-x*x;
        double cone=a*a*(0.45+0.55*Math.Sin(Math.PI*Math.Clamp(x/0.85,0.0,1.0)));
        return cone*Math.Max(0.25,1.0+0.45*petalNoise);
    }

    private static uint Hash1(uint seed,int k)
    {
        unchecked
        {
            uint h=seed*747796405u+(uint)k*2891336453u+1442695041u;
            h^=h>>15;h*=2246822519u;h^=h>>13;h*=3266489917u;h^=h>>16;return h;
        }
    }
    private static uint Hash3(uint seed,int x,int y,int z)
    {
        unchecked
        {
            uint h=seed*747796405u+(uint)x*2891336453u+(uint)y*3266489917u+(uint)z*668265263u+1442695041u;
            h^=h>>15;h*=2246822519u;h^=h>>13;h*=3266489917u;h^=h>>16;return h;
        }
    }
    private static double SmoothStep(double t)=>t*t*(3.0-2.0*t);

    /// <summary>Periodic smooth value noise in [-1, 1] over angle [0, 2*pi) for irregular forged crater rims.</summary>
    public static double AngularNoise(uint seed,double angle)
    {
        const int Bins=12;
        double twoPi=2.0*Math.PI;
        double a=((angle%twoPi)+twoPi)%twoPi;
        double u=a/twoPi*Bins;
        int i0=(int)Math.Floor(u)%Bins;
        int i1=(i0+1)%Bins;
        double t=SmoothStep(u-Math.Floor(u));
        double v0=Hash1(seed,i0)/4294967295.0*2.0-1.0;
        double v1=Hash1(seed,i1)/4294967295.0*2.0-1.0;
        return v0+(v1-v0)*t;
    }

    /// <summary>Deterministic 3D ridged value noise in [0, 1] for sheet-metal buckling and edge tearing.</summary>
    public static double RidgeNoise3D(uint seed,V3 p,double freq)
    {
        if(freq<=0)return 0.5;
        double fx=p.X/freq,fy=p.Y/freq,fz=p.Z/freq;
        int x0=(int)Math.Floor(fx),y0=(int)Math.Floor(fy),z0=(int)Math.Floor(fz);
        double tx=SmoothStep(fx-x0),ty=SmoothStep(fy-y0),tz=SmoothStep(fz-z0);
        double V(int dx,int dy,int dz)=>Hash3(seed,x0+dx,y0+dy,z0+dz)/4294967295.0*2.0-1.0;
        double Lerp(double a,double b,double t)=>a+(b-a)*t;
        double c00=Lerp(V(0,0,0),V(1,0,0),tx),c10=Lerp(V(0,1,0),V(1,1,0),tx);
        double c01=Lerp(V(0,0,1),V(1,0,1),tx),c11=Lerp(V(0,1,1),V(1,1,1),tx);
        double val=Lerp(Lerp(c00,c10,ty),Lerp(c01,c11,ty),tz);
        return Math.Clamp(1.0-Math.Abs(val),0.0,1.0);
    }
}

/// <summary>The vertices of one mesh and the displacement every dent has given them. All vertices within R of the centre (distance measured in the plate plane) move by
/// the SAME vector times a smooth falloff, so both shells of a plate keep their thickness. The total per vertex is capped at MaxDepth.</summary>
public sealed class DentField
{
    public V3[] Basis{get;}
    public V3[] Disp{get;}
    private readonly byte[]? side;
    /// <summary>0.4.1: vertices that belong only to broken cells do not move (null = all move).</summary>
    public bool[]? Alive{get;set;}
    public double MaxDepthM{get;}
    /// <param name="vertexSide">PlateSide value per vertex (0 = no side information), or null.</param>
    public DentField(V3[] basis,byte[]? vertexSide,double maxDepthM)
    {
        Basis=basis;Disp=new V3[basis.Length];side=vertexSide!=null&&vertexSide.Length==basis.Length?vertexSide:null;MaxDepthM=Math.Max(0,maxDepthM);
    }
    public V3 Position(int i)=>VM.Add(Basis[i],Disp[i]);
    public double MaxDisplacement(){double m=0;for(int i=0;i<Disp.Length;i++)m=Math.Max(m,VM.Len(Disp[i]));return m;}
    /// <summary>Applies one dent. Returns the number of vertices that moved. An event with a side only moves vertices of that side (vertex side 0 = unknown: moves).</summary>
    public int Apply(V3 center,V3 normal,double depthM,double radiusM,PlateSide eventSide)
    {
        if(!(depthM>0)||!(radiusM>0))return 0;
        var n=VM.Norm(normal,new V3(0,0,0));if(VM.Len(n)<0.5)return 0;
        double reach=DentMath.NormalReach(radiusM);int moved=0;
        for(int i=0;i<Basis.Length;i++)
        {
            if(eventSide!=PlateSide.Whole&&side!=null&&side[i]!=0&&side[i]!=(byte)eventSide)continue;
            if(Alive!=null&&i<Alive.Length&&!Alive[i])continue;
            var w=VM.Sub(Basis[i],center);double dn=VM.Dot(w,n);
            if(Math.Abs(dn)>reach)continue;
            var wp=VM.Sub(w,VM.Mul(n,dn));double d=VM.Len(wp);
            double f=DentMath.Falloff(d,radiusM);if(f<=0)continue;
            var nd=VM.Add(Disp[i],VM.Mul(n,depthM*f));
            double l=VM.Len(nd);if(l>MaxDepthM&&l>0)nd=VM.Mul(nd,MaxDepthM/l);
            Disp[i]=nd;moved++;
        }
        return moved;
    }
    public int Apply(DentEvent e)=>e.Style==DentStyle.Smooth
        ?Apply(e.Center,e.Normal,e.DepthM,e.RadiusM,e.Side)
        :ApplyRich(e,Basis,Disp,side,Alive,MaxDepthM,null);

    public int ApplyRich(DentEvent e,List<int>? movedOut=null)=>ApplyRich(e,Basis,Disp,side,Alive,MaxDepthM,movedOut);

    public static int ApplyRich(DentEvent e,SubdivMesh sm,List<int>? movedOut=null)
    {
        int vc=sm.Basis.Count;
        if(!(e.DepthM>0)||!(e.RadiusM>0)||vc==0)return 0;
        var n=VM.Norm(e.Normal,new V3(0,0,0));if(VM.Len(n)<0.5)return 0;
        if(e.Style==DentStyle.Smooth)
        {
            double reach=DentMath.NormalReach(e.RadiusM);int moved=0;
            for(int i=0;i<vc;i++)
            {
                if(e.Side!=PlateSide.Whole&&sm.VertSide!=null&&sm.VertSide[i]!=0&&sm.VertSide[i]!=(byte)e.Side)continue;
                if(sm.Alive!=null&&i<sm.Alive.Count&&!sm.Alive[i])continue;
                var w=VM.Sub(sm.Basis[i],e.Center);double dn=VM.Dot(w,n);
                if(Math.Abs(dn)>reach)continue;
                var wp=VM.Sub(w,VM.Mul(n,dn));double d=VM.Len(wp);
                double f=DentMath.Falloff(d,e.RadiusM);if(f<=0)continue;
                var nd=VM.Add(sm.Disp[i],VM.Mul(n,e.DepthM*f));
                double l=VM.Len(nd);if(l>sm.MaxDepthM&&l>0)nd=VM.Mul(nd,sm.MaxDepthM/l);
                sm.Disp[i]=nd;moved++;movedOut?.Add(i);
            }
            return moved;
        }
        return ApplyRichCore(e,i=>sm.Basis[i],i=>sm.Disp[i],(i,v)=>sm.Disp[i]=v,vc,i=>sm.VertSide!=null?sm.VertSide[i]:(byte)0,i=>sm.Alive==null||i>=sm.Alive.Count||sm.Alive[i],sm.MaxDepthM,movedOut);
    }

    public static int ApplyRich(DentEvent e,V3[] basis,V3[] disp,byte[]? side,bool[]? alive,double maxDepthM,List<int>? movedOut=null)
    {
        if(!(e.DepthM>0)||!(e.RadiusM>0)||basis.Length==0)return 0;
        var n=VM.Norm(e.Normal,new V3(0,0,0));if(VM.Len(n)<0.5)return 0;
        return ApplyRichCore(e,i=>basis[i],i=>disp[i],(i,v)=>disp[i]=v,basis.Length,i=>side!=null&&i<side.Length?side[i]:(byte)0,i=>alive==null||i>=alive.Length||alive[i],maxDepthM,movedOut);
    }

    private static void OrthonormalBasis(V3 n,out V3 u,out V3 v)
    {
        var up=Math.Abs(n.Y)<0.9?new V3(0,1,0):new V3(1,0,0);
        u=VM.Norm(VM.Cross(up,n),new V3(1,0,0));
        v=VM.Norm(VM.Cross(n,u),new V3(0,0,1));
    }

    private static int ApplyRichCore(
        DentEvent e,
        Func<int,V3> getBasis,
        Func<int,V3> getDisp,
        Action<int,V3> setDisp,
        int count,
        Func<int,byte> getSide,
        Func<int,bool> isAlive,
        double maxDepthM,
        List<int>? movedOut)
    {
        var n=VM.Norm(e.Normal,new V3(0,1,0));
        double reach=DentMath.NormalReach(e.RadiusM);
        OrthonormalBasis(n,out var basisU,out var basisV);

        // Tangential shot direction for oblique kinetic craters
        var sd=VM.Norm(e.ShotDir,n);
        var sdProj=VM.Sub(sd,VM.Mul(n,VM.Dot(sd,n)));
        double sinTheta=Math.Min(0.95,VM.Len(sdProj));
        var tDir=sinTheta>1e-4?VM.Mul(sdProj,1.0/sinTheta):basisU;

        int moved=0;
        for(int i=0;i<count;i++)
        {
            if(e.Side!=PlateSide.Whole){byte sv=getSide(i);if(sv!=0&&sv!=(byte)e.Side)continue;}
            if(!isAlive(i))continue;
            var bp=getBasis(i);
            var w=VM.Sub(bp,e.Center);
            double dn=VM.Dot(w,n);
            if(Math.Abs(dn)>reach)continue;
            var wp=VM.Sub(w,VM.Mul(n,dn));
            double dist=VM.Len(wp);

            V3 delta;
            if(e.Style==DentStyle.Kinetic)
            {
                double maxExtent=e.RadiusM*(1.0+0.85*sinTheta);
                if(dist>=maxExtent)continue;
                double alongT=VM.Dot(wp,tDir);
                double perpSq=Math.Max(0,dist*dist-alongT*alongT);
                double stretch=1.0+0.85*sinTheta;
                double effAlong=alongT/stretch;
                double effDist=Math.Sqrt(effAlong*effAlong+perpSq);
                double x=effDist/e.RadiusM;
                if(x>=1.0)continue;

                double backAtten=DentMath.BackFaceAttenuation(dn,e.ThicknessM,out bool allowLip);
                double prof=DentMath.CraterProfile(x,allowLip?e.LipRatio:0.0);
                if(Math.Abs(prof)<1e-9)continue;

                double angle=Math.Atan2(VM.Dot(wp,basisV),VM.Dot(wp,basisU));
                double angMod=1.0+0.22*DentMath.AngularNoise(e.Seed,angle);
                double downDot=dist>1e-6?alongT/dist:0;
                double asym=1.0+(prof<0?0.45*sinTheta*Math.Max(0,downDot):0.0);
                double dLen=e.DepthM*prof*angMod*asym*backAtten;
                delta=VM.Mul(n,dLen);
            }
            else if(e.Style==DentStyle.Ricochet)
            {
                double alongT=VM.Dot(wp,tDir);
                var perpVec=VM.Sub(wp,VM.Mul(tDir,alongT));
                double perpDist=VM.Len(perpVec);
                double halfLen=e.RadiusM*(1.10+0.65*sinTheta);
                double width=e.RadiusM*0.78;
                double u=alongT/halfLen;
                double v=perpDist/width;
                if(u<=-0.75||u>=1.25||v>=1.0)continue;

                double backAtten=DentMath.BackFaceAttenuation(dn,e.ThicknessM,out bool allowLip);
                double prof=DentMath.RicochetProfile(u,v,allowLip?e.LipRatio:0.0);
                if(Math.Abs(prof)<1e-9)continue;

                double ridge=DentMath.RidgeNoise3D(e.Seed,bp,Math.Max(0.03,e.RadiusM*0.25));
                double mod=0.82+0.36*ridge;
                delta=VM.Mul(n,e.DepthM*prof*mod*backAtten);
                if(prof<0&&u>0.40)
                    delta=VM.Add(delta,VM.Mul(tDir,0.18*Math.Abs(prof)*e.DepthM*backAtten));
            }
            else if(e.Style==DentStyle.Embedded)
            {
                double maxExtent=e.RadiusM*(1.0+0.35*sinTheta);
                if(dist>=maxExtent)continue;
                double alongT=VM.Dot(wp,tDir);
                double perpSq=Math.Max(0,dist*dist-alongT*alongT);
                double stretch=1.0+0.35*sinTheta;
                double effAlong=alongT/stretch;
                double effDist=Math.Sqrt(effAlong*effAlong+perpSq);
                double x=effDist/e.RadiusM;
                if(x>=1.0)continue;

                double backAtten=DentMath.BackFaceAttenuation(dn,e.ThicknessM,out bool allowLip);
                double prof=DentMath.EmbeddedProfile(x,allowLip?e.LipRatio:0.0);
                if(Math.Abs(prof)<1e-9)continue;

                double angle=Math.Atan2(VM.Dot(wp,basisV),VM.Dot(wp,basisU));
                double angMod=1.0+0.26*DentMath.AngularNoise(e.Seed,angle)+0.12*DentMath.AngularNoise(e.Seed^0x55AA55AAu,2.0*angle);
                delta=VM.Mul(n,e.DepthM*prof*angMod*backAtten);
                if(prof<0&&dist>1e-5)
                {
                    var radDir=VM.Mul(wp,1.0/dist);
                    delta=VM.Add(delta,VM.Mul(radDir,0.16*Math.Abs(prof)*e.DepthM*backAtten));
                }
            }
            else if(e.Style==DentStyle.ExitBlowout)
            {
                double x=dist/e.RadiusM;
                if(x>=1.0)continue;
                double backAtten=DentMath.BackFaceAttenuationAbs(dn,e.ThicknessM,out _);
                double angle=Math.Atan2(VM.Dot(wp,basisV),VM.Dot(wp,basisU));
                double petalNoise=DentMath.AngularNoise(e.Seed,angle*1.5);
                double prof=DentMath.ExitBlowoutProfile(x,petalNoise);
                if(Math.Abs(prof)<1e-9)continue;

                delta=VM.Mul(n,e.DepthM*prof*backAtten);
                if(dist>1e-5)
                {
                    var radDir=VM.Mul(wp,1.0/dist);
                    delta=VM.Add(delta,VM.Mul(radDir,0.22*e.DepthM*prof*backAtten));
                }
            }
            else if(e.Style==DentStyle.Blast)
            {
                double x=dist/e.RadiusM;
                if(x>=1.0)continue;
                double baseF=DentMath.Falloff(dist,e.RadiusM);
                if(baseF<=0)continue;
                double thinW=e.ThicknessM<=0?1.0:Math.Clamp(0.015/Math.Max(e.ThicknessM,0.003),0.25,1.0);
                double ridge1=DentMath.RidgeNoise3D(e.Seed,bp,Math.Max(0.08,e.RadiusM*0.42));
                double ridge2=DentMath.RidgeNoise3D(e.Seed^0xA5A55A5Au,bp,Math.Max(0.04,e.RadiusM*0.18));
                double buckle=1.0+e.Buckling*thinW*(0.65*ridge1+0.35*ridge2-0.45);
                double backAtten=DentMath.BackFaceAttenuation(dn,e.ThicknessM,out _);
                delta=VM.Mul(n,e.DepthM*baseF*buckle*backAtten);
                if(dist>1e-5)
                {
                    var radDir=VM.Mul(wp,1.0/dist);
                    double pull=0.10*e.DepthM*Math.Sin(Math.PI*x)*baseF*thinW*backAtten;
                    delta=VM.Sub(delta,VM.Mul(radDir,pull));
                }
            }
            else
            {
                double f=DentMath.Falloff(dist,e.RadiusM);
                if(f<=0)continue;
                delta=VM.Mul(n,e.DepthM*f);
            }

            if(VM.Len(delta)<1e-7)continue;
            var nd=VM.Add(getDisp(i),delta);
            double l=VM.Len(nd);if(l>maxDepthM&&l>0)nd=VM.Mul(nd,maxDepthM/l);
            setDisp(i,nd);
            moved++;
            movedOut?.Add(i);
        }
        return moved;
    }
}

/// <summary>0.5.0: Curls and buckles surviving mesh border vertices away from a blast tear-off.</summary>
public static class TearCurl
{
    private static double DistSqToSegment(V3 p,V3 a,V3 b)
    {
        double abx=b.X-a.X,aby=b.Y-a.Y,abz=b.Z-a.Z;
        double lenSq=abx*abx+aby*aby+abz*abz;
        double t=lenSq<1e-12?0.0:Math.Clamp(((p.X-a.X)*abx+(p.Y-a.Y)*aby+(p.Z-a.Z)*abz)/lenSq,0.0,1.0);
        double dx=p.X-(a.X+abx*t),dy=p.Y-(a.Y+aby*t),dz=p.Z-(a.Z+abz*t);
        return dx*dx+dy*dy+dz*dz;
    }

    public static int Apply(
        SubdivMesh sm,
        IReadOnlySet<int> newlyBrokenCells,
        Func<int,bool> isCellBroken,
        V3 blastLocal,
        double curlDepthM,
        double curlWidthM,
        double thicknessM,
        double maxDepthM,
        uint seed,
        List<int>? movedOut=null)
    {
        if(newlyBrokenCells.Count==0||!(curlDepthM>0)||!(curlWidthM>0)||sm.SubTriCell==null)return 0;
        int vc=sm.Basis.Count;
        var survivingKeys=new HashSet<SpatialPosKey>();
        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int n=st.Count/3;
            for(int ti=0;ti<n&&ti<sc.Count;ti++)
            {
                int cell=sc[ti];if(cell<0||isCellBroken(cell))continue;
                int a=st[ti*3],b=st[ti*3+1],c=st[ti*3+2];
                if(a>=0&&a<vc)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[a]));
                if(b>=0&&b<vc)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[b]));
                if(c>=0&&c<vc)survivingKeys.Add(SpatialPosKey.Quantize(sm.Basis[c]));
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
            if(u<0||v<0||u>=vc||v>=vc)return;
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
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];int n=st.Count/3;
            for(int ti=0;ti<n&&ti<sc.Count;ti++)
            {
                if(!newlyBrokenCells.Contains(sc[ti]))continue;
                int a=st[ti*3],b=st[ti*3+1],c=st[ti*3+2];
                CheckEdge(a,b);CheckEdge(b,c);CheckEdge(c,a);
            }
        }
        if(borderPts.Count==0)return 0;

        if(borderSegs.Count>256)
        {
            int stride=borderSegs.Count/256+1;
            var sub=new List<(V3,V3)>(256);
            for(int i=0;i<borderSegs.Count;i+=stride)sub.Add(borderSegs[i]);
            borderSegs=sub;
        }
        if(borderPts.Count>256)
        {
            int stride=borderPts.Count/256+1;
            var sub=new List<V3>(256);
            for(int i=0;i<borderPts.Count;i+=stride)sub.Add(borderPts[i]);
            borderPts=sub;
        }

        float w=(float)curlWidthM;
        x0-=w;y0-=w;z0-=w;x1+=w;y1+=w;z1+=w;
        double wSq=curlWidthM*curlWidthM;
        double thMm=thicknessM>0?thicknessM*1000.0:10.0;
        double thinScale=Math.Clamp(Math.Sqrt(10.0/Math.Max(thMm,3.0)),0.35,1.35);

        int moved=0;
        for(int i=0;i<vc;i++)
        {
            if(sm.Alive!=null&&i<sm.Alive.Count&&!sm.Alive[i])continue;
            var bp=sm.Basis[i];
            if(bp.X<x0||bp.X>x1||bp.Y<y0||bp.Y>y1||bp.Z<z0||bp.Z>z1)continue;
            double bestSq=double.MaxValue;
            if(borderSegs.Count>0)
            {
                for(int si=0;si<borderSegs.Count;si++){double d2=DistSqToSegment(bp,borderSegs[si].A,borderSegs[si].B);if(d2<bestSq)bestSq=d2;}
            }
            else
            {
                for(int bi=0;bi<borderPts.Count;bi++){var q=borderPts[bi];double dx=bp.X-q.X,dy=bp.Y-q.Y,dz=bp.Z-q.Z;double d2=dx*dx+dy*dy+dz*dz;if(d2<bestSq)bestSq=d2;}
            }
            if(bestSq>=wSq)continue;
            double d=Math.Sqrt(bestSq);
            double t=1.0-d/curlWidthM;
            double falloff=t*t;
            double wave=0.65+0.70*DentMath.RidgeNoise3D(seed,bp,0.14);
            var dir=VM.Norm(VM.Sub(bp,blastLocal),new V3(0,1,0));
            var delta=VM.Mul(dir,curlDepthM*falloff*wave*thinScale);
            if(VM.Len(delta)<1e-7)continue;
            var nd=VM.Add(sm.Disp[i],delta);
            double l=VM.Len(nd);if(l>maxDepthM&&l>0)nd=VM.Mul(nd,maxDepthM/l);
            sm.Disp[i]=nd;
            moved++;
            movedOut?.Add(i);
        }
        return moved;
    }
}

/// <summary>0.5.0: Bends, buckles and slightly twists detached debris vertices so torn pieces look mangled rather than flat cutouts.</summary>
public static class DebrisWarp
{
    public static int Apply(V3[] verts,V3 blastLocal,double warpDepthM,uint seed)
    {
        if(verts.Length==0||!(warpDepthM>0))return 0;
        double cx=0,cy=0,cz=0;
        for(int i=0;i<verts.Length;i++){cx+=verts[i].X;cy+=verts[i].Y;cz+=verts[i].Z;}
        var centroid=new V3((float)(cx/verts.Length),(float)(cy/verts.Length),(float)(cz/verts.Length));
        double maxR=0.01;
        for(int i=0;i<verts.Length;i++){double l=VM.Len(VM.Sub(verts[i],centroid));if(l>maxR)maxR=l;}
        var bendDir=VM.Norm(VM.Sub(centroid,blastLocal),new V3(0,1,0));
        var twistAxis=VM.Norm(new V3(bendDir.Z,0.25f,-bendDir.X),new V3(1,0,0));

        for(int i=0;i<verts.Length;i++)
        {
            var rel=VM.Sub(verts[i],centroid);
            double rNorm=Math.Clamp(VM.Len(rel)/maxR,0.0,1.0);
            double bow=rNorm*rNorm*warpDepthM;
            double ridge=DentMath.RidgeNoise3D(seed,verts[i],Math.Max(0.08,maxR*0.35));
            double buckle=(ridge-0.35)*0.65*warpDepthM;
            var twistVec=VM.Cross(twistAxis,rel);
            double twistAmt=0.35*rNorm*(warpDepthM/Math.Max(0.15,maxR));
            var disp=VM.Add(VM.Mul(bendDir,bow+buckle),VM.Mul(twistVec,twistAmt));
            verts[i]=VM.Add(verts[i],disp);
        }
        return verts.Length;
    }
}

/// <summary>Queries on triangle meshes (local space): first ray hit and closest surface point. Both give the unit triangle normal (orientation arbitrary).</summary>
public static class MeshQuery
{
    public static bool RayFirst(V3[] v,int[] t,V3 o,V3 dir,double maxDist,Func<int,bool>? ok,out double dist,out V3 normal,out int tri)
    {
        dist=double.MaxValue;normal=new V3(0,0,0);tri=-1;
        var d=VM.Norm(dir,new V3(0,0,0));if(VM.Len(d)<0.5)return false;
        for(int i=0;i+2<t.Length;i+=3)
        {
            int ia=t[i],ib=t[i+1],ic=t[i+2];if(ia<0||ib<0||ic<0||ia>=v.Length||ib>=v.Length||ic>=v.Length)continue;
            if(ok!=null&&!ok(i/3))continue;
            var a=v[ia];var e1=VM.Sub(v[ib],a);var e2=VM.Sub(v[ic],a);
            var pv=VM.Cross(d,e2);double det=VM.Dot(e1,pv);if(Math.Abs(det)<1e-14)continue;
            double inv=1.0/det;var tv=VM.Sub(o,a);double u=VM.Dot(tv,pv)*inv;if(u<0||u>1)continue;
            var qv=VM.Cross(tv,e1);double w=VM.Dot(d,qv)*inv;if(w<0||u+w>1)continue;
            double tt=VM.Dot(e2,qv)*inv;if(tt<0||tt>maxDist||tt>=dist)continue;
            dist=tt;tri=i/3;normal=VM.Norm(VM.Cross(e1,e2),new V3(0,1,0));
        }
        return tri>=0;
    }
    public static V3 ClosestOnTriangle(V3 p,V3 a,V3 b,V3 c)
    {
        var ab=VM.Sub(b,a);var ac=VM.Sub(c,a);var ap=VM.Sub(p,a);
        double d1=VM.Dot(ab,ap),d2=VM.Dot(ac,ap);if(d1<=0&&d2<=0)return a;
        var bp=VM.Sub(p,b);double d3=VM.Dot(ab,bp),d4=VM.Dot(ac,bp);if(d3>=0&&d4<=d3)return b;
        double vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0){double s=d1/(d1-d3);return VM.Add(a,VM.Mul(ab,s));}
        var cp=VM.Sub(p,c);double d5=VM.Dot(ab,cp),d6=VM.Dot(ac,cp);if(d6>=0&&d5<=d6)return c;
        double vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0){double s=d2/(d2-d6);return VM.Add(a,VM.Mul(ac,s));}
        double va=d3*d6-d5*d4;if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0){double s=(d4-d3)/((d4-d3)+(d5-d6));return VM.Add(b,VM.Mul(VM.Sub(c,b),s));}
        double den=1.0/(va+vb+vc);double vv=vb*den,ww=vc*den;
        return VM.Add(a,VM.Add(VM.Mul(ab,vv),VM.Mul(ac,ww)));
    }
    public static bool Closest(V3[] v,int[] t,V3 p,Func<int,bool>? ok,out V3 point,out V3 normal)
    {
        point=p;normal=new V3(0,1,0);double best=double.MaxValue;bool have=false;
        for(int i=0;i+2<t.Length;i+=3)
        {
            int ia=t[i],ib=t[i+1],ic=t[i+2];if(ia<0||ib<0||ic<0||ia>=v.Length||ib>=v.Length||ic>=v.Length)continue;
            if(ok!=null&&!ok(i/3))continue;
            var q=ClosestOnTriangle(p,v[ia],v[ib],v[ic]);var dv=VM.Sub(q,p);double dd=VM.Dot(dv,dv);
            if(dd<best){best=dd;point=q;normal=VM.Norm(VM.Cross(VM.Sub(v[ib],v[ia]),VM.Sub(v[ic],v[ia])),new V3(0,1,0));have=true;}
        }
        return have;
    }
}

/// <summary>Ground for the debris integrator: the cast from the old to the new position. Point = where the segment meets the ground, Fraction = 0..1 along the segment.</summary>
public interface IGround{bool Cast(V3 from,V3 to,out V3 point,out V3 normal,out double fraction);}

/// <summary>Ballistic rules of the debris (own integration, no physics objects). Speed = clamp(SpeedMin + SpeedPerRatio x D/strength, SpeedMin, SpeedMax).</summary>
public static class Ballistics
{
    public const double Gravity=9.81;
    public static double Speed(double min,double max,double perRatio,double ratio)
    {
        double r=double.IsNaN(ratio)||ratio<0?0:ratio;
        double hi=Math.Max(min,max);
        return Math.Min(hi,Math.Max(min,min+perRatio*r));
    }
    /// <summary>From the blast towards the centroid, normalised, plus an upward share, normalised again.</summary>
    public static V3 Direction(V3 blast,V3 centroid,double up=0.3)
    {
        var h=VM.Norm(VM.Sub(centroid,blast),new V3(0,0,0));
        return VM.Norm(new V3(h.X,(float)(h.Y+up),h.Z),new V3(0,1,0));
    }
    /// <summary>Random but repeatable spin: unit axis and degrees per second (0..maxDegPerS).</summary>
    public static (V3 Axis,double DegPerS) Spin(int seed,double maxDegPerS=360)
    {
        var r=new Random(seed);
        double x=r.NextDouble()*2-1,y=r.NextDouble()*2-1,z=r.NextDouble()*2-1;
        var axis=VM.Norm(new V3((float)x,(float)y,(float)z),new V3(0,1,0));
        return (axis,r.NextDouble()*maxDegPerS);
    }
}

public enum DebrisState{Flying,Landed,Lost}

/// <summary>One flying object: exact constant-gravity kinematics per step, a ground cast per step (ignored during the first IgnoreFirstS), a safety net below the start height.</summary>
public sealed class DebrisBody
{
    public V3 Pos{get;private set;}
    public V3 Vel{get;private set;}
    public V3 Start{get;}
    public double FlightTime{get;private set;}
    public double LandedAt{get;private set;}
    public DebrisState State{get;private set;}=DebrisState.Flying;
    public double IgnoreFirstS{get;set;}=0.15;
    public double LostBelowM{get;set;}=50;
    public DebrisBody(V3 pos,V3 vel){Pos=pos;Vel=vel;Start=pos;}
    public DebrisState Step(double dt,IGround? ground)
    {
        if(State!=DebrisState.Flying||!(dt>0))return State;
        var old=Pos;double t0=FlightTime;FlightTime+=dt;
        var np=new V3((float)(old.X+Vel.X*dt),(float)(old.Y+Vel.Y*dt-0.5*Ballistics.Gravity*dt*dt),(float)(old.Z+Vel.Z*dt));
        var nv=new V3(Vel.X,(float)(Vel.Y-Ballistics.Gravity*dt),Vel.Z);
        if(ground!=null&&FlightTime>IgnoreFirstS&&ground.Cast(old,np,out var pt,out _,out double frac))
        {
            Pos=pt;Vel=new V3(0,0,0);LandedAt=t0+Math.Min(1.0,Math.Max(0.0,frac))*dt;State=DebrisState.Landed;return State;
        }
        Pos=np;Vel=nv;
        if(Pos.Y<Start.Y-LostBelowM)State=DebrisState.Lost;
        return State;
    }
}

/// <summary>The set of live debris objects: at most Max (the oldest goes first), each at most Lifetime seconds. Item = whatever the caller keeps per object.</summary>
public sealed class DebrisBook<T> where T:class
{
    private readonly List<(T Item,double Born)> items=new();
    public int Max{get;}
    public double LifetimeS{get;}
    public DebrisBook(int max,double lifetimeS){Max=Math.Max(1,max);LifetimeS=lifetimeS;}
    public int Count=>items.Count;
    public IEnumerable<T> Items=>items.Select(x=>x.Item);
    /// <summary>Adds an object; returns the objects that had to go to stay within Max.</summary>
    public List<T> Add(T item,double now)
    {
        var removed=new List<T>();
        while(items.Count>=Max){removed.Add(items[0].Item);items.RemoveAt(0);}
        items.Add((item,now));return removed;
    }
    /// <summary>Removes and returns the objects older than the lifetime.</summary>
    public List<T> Expire(double now)
    {
        var gone=new List<T>();
        for(int i=items.Count-1;i>=0;i--)if(now-items[i].Born>=LifetimeS){gone.Insert(0,items[i].Item);items.RemoveAt(i);}
        return gone;
    }
    public bool Remove(T item){int i=items.FindIndex(x=>ReferenceEquals(x.Item,item));if(i<0)return false;items.RemoveAt(i);return true;}
    public List<T> Clear(){var all=items.Select(x=>x.Item).ToList();items.Clear();return all;}
}

/// <summary>0.7.0: Localized ragged blast hole sizing and perimeter noise.</summary>
public static class HoleMath
{
    public const int HoleIdBase=0x10000000;
    public static double HoleRadiusM(double dmgRatio,double blastRadiusM,double maxHoleRadiusM=0.45)
    {
        if(!(dmgRatio>=1.0)||double.IsNaN(dmgRatio))return 0;
        double r=0.08+0.095*Math.Sqrt(Math.Max(0.0,dmgRatio-1.0));
        double cap=Math.Min(Math.Max(0.12,maxHoleRadiusM),Math.Max(0.18,blastRadiusM*0.65));
        return Math.Clamp(r,0.10,cap);
    }
    public static double RaggedRadius(double baseRadiusM,double angle,uint seed)
    {
        double n1=DentMath.AngularNoise(seed,angle);
        double n2=DentMath.AngularNoise(seed^0x9E3779B9u,angle*2.5);
        return baseRadiusM*Math.Clamp(1.0+0.26*n1+0.13*n2,0.65,1.38);
    }
}

/// <summary>0.7.0: Punches localized ragged holes through a subdivided plate mesh by assigning hole IDs to triangles inside the jagged blast radius.</summary>
public static class HolePunch
{
    public static int Punch(
        SubdivMesh sm,
        V3 center,
        V3 normal,
        double holeRadiusM,
        PlateSide eventSide,
        int holeId,
        IReadOnlySet<int> alreadyBroken,
        uint seed,
        out int remainingSurvivingTris,
        out int totalSideTris)
    {
        remainingSurvivingTris=0;totalSideTris=0;
        if(!(holeRadiusM>0)||sm.Basis.Count==0)return 0;
        if(sm.SubTriCell==null)
        {
            sm.SubTriCell=new List<int>[sm.SubTris.Length];
            for(int s=0;s<sm.SubTris.Length;s++)
            {
                int nt=sm.SubTris[s].Count/3;
                var list=new List<int>(nt);
                for(int i=0;i<nt;i++)list.Add(0);
                sm.SubTriCell[s]=list;
            }
        }
        var n=VM.Norm(normal,new V3(0,1,0));
        var up=Math.Abs(n.Y)<0.9?new V3(0,1,0):new V3(1,0,0);
        var basisU=VM.Norm(VM.Cross(up,n),new V3(1,0,0));
        var basisV=VM.Norm(VM.Cross(n,basisU),new V3(0,0,1));
        double reach=DentMath.NormalReach(holeRadiusM);
        int punched=0,vc=sm.Basis.Count;

        for(int s=0;s<sm.SubTris.Length;s++)
        {
            var st=sm.SubTris[s];var sc=sm.SubTriCell[s];var ss=sm.SubTriSide?[s];
            int nt=st.Count/3;
            while(sc.Count<nt)sc.Add(0);
            for(int ti=0;ti<nt;ti++)
            {
                if(eventSide!=PlateSide.Whole&&ss!=null&&ti<ss.Count&&ss[ti]!=(byte)eventSide)continue;
                totalSideTris++;
                int curCell=sc[ti];
                if(curCell>=0&&alreadyBroken.Contains(curCell))continue;
                int ia=st[ti*3],ib=st[ti*3+1],ic=st[ti*3+2];
                if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc){remainingSurvivingTris++;continue;}
                V3 a=sm.Basis[ia],b=sm.Basis[ib],c=sm.Basis[ic];
                var cen=new V3((a.X+b.X+c.X)/3f,(a.Y+b.Y+c.Y)/3f,(a.Z+b.Z+c.Z)/3f);
                var w=VM.Sub(cen,center);
                double dn=VM.Dot(w,n);
                if(Math.Abs(dn)<=reach)
                {
                    var wp=VM.Sub(w,VM.Mul(n,dn));
                    double d=VM.Len(wp);
                    double angle=Math.Atan2(VM.Dot(wp,basisV),VM.Dot(wp,basisU));
                    if(d<=HoleMath.RaggedRadius(holeRadiusM,angle,seed))
                    {
                        sc[ti]=holeId;
                        punched++;
                        continue;
                    }
                }
                remainingSurvivingTris++;
            }
        }
        if(punched>0)sm.SyncFlat();
        return punched;
    }
}

/// <summary>0.7.1: Maps a pre-dent surface point onto the deformed mesh using barycentric interpolation of vertex displacements.</summary>
public static class SurfaceSnap
{
    public static bool DeformedPoint(SubdivMesh sm,TriAccel accel,V3 queryLocal,double maxDistLocal,Func<int,bool>? ok,out V3 snappedLocal,out V3 dispLocal)
    {
        snappedLocal=queryLocal;dispLocal=default;
        if(!accel.ClosestTri(sm.FlatBasis,sm.FlatTris,sm.TopologyVersion,queryLocal,maxDistLocal,ok,out V3 cp,out int tri))
            return false;
        int ia=sm.FlatTris[tri*3],ib=sm.FlatTris[tri*3+1],ic=sm.FlatTris[tri*3+2];
        int vc=Math.Min(sm.Basis.Count,sm.Disp.Count);
        if(ia<0||ib<0||ic<0||ia>=vc||ib>=vc||ic>=vc)return false;

        V3 a=sm.Basis[ia],b=sm.Basis[ib],c=sm.Basis[ic];
        V3 v0=VM.Sub(b,a),v1=VM.Sub(c,a),v2=VM.Sub(cp,a);
        double d00=VM.Dot(v0,v0),d01=VM.Dot(v0,v1),d11=VM.Dot(v1,v1),d20=VM.Dot(v2,v0),d21=VM.Dot(v2,v1);
        double denom=d00*d11-d01*d01;
        double w0=0,w1=0,w2=0;
        bool useClosest=true;
        if(Math.Abs(denom)>1e-16)
        {
            w1=(d11*d20-d01*d21)/denom;
            w2=(d00*d21-d01*d20)/denom;
            w0=1.0-w1-w2;
            w0=Math.Max(0.0,Math.Min(1.0,w0));
            w1=Math.Max(0.0,Math.Min(1.0,w1));
            w2=Math.Max(0.0,Math.Min(1.0,w2));
            double sum=w0+w1+w2;
            if(sum>1e-12){w0/=sum;w1/=sum;w2/=sum;useClosest=false;}
        }
        if(useClosest)
        {
            var da=VM.Sub(cp,a);var db=VM.Sub(cp,b);var dc=VM.Sub(cp,c);
            double la=VM.Dot(da,da),lb=VM.Dot(db,db),lc=VM.Dot(dc,dc);
            if(la<=lb&&la<=lc){w0=1;w1=0;w2=0;}
            else if(lb<=lc){w0=0;w1=1;w2=0;}
            else{w0=0;w1=0;w2=1;}
        }

        V3 dA=sm.Disp[ia],dB=sm.Disp[ib],dC=sm.Disp[ic];
        dispLocal=new V3(
            (float)(dA.X*w0+dB.X*w1+dC.X*w2),
            (float)(dA.Y*w0+dB.Y*w1+dC.Y*w2),
            (float)(dA.Z*w0+dB.Z*w1+dC.Z*w2));
        if(VM.Len(dispLocal)<1e-5)return false;
        snappedLocal=VM.Add(queryLocal,dispLocal);
        return true;
    }
}

/// <summary>0.8.2: Decision for filtering visual-only explosions (HEAT jet / ERA flash / internal APHE) from full HE blast denting.</summary>
public readonly struct BlastDentDecision
{
    public readonly bool SkipBlast;
    public readonly double DamageScale;
    public readonly int MaxNonDestructibleDents;
    public BlastDentDecision(bool skipBlast,double damageScale,int maxNonDestructibleDents)
    {SkipBlast=skipBlast;DamageScale=damageScale;MaxNonDestructibleDents=maxNonDestructibleDents;}
}

public static class BlastEffectFilter
{
    public static BlastDentDecision Classify(string? kind,bool visualOnly,double heatScale,int maxHeDents)
    {
        string k=(kind??"").Trim().ToLowerInvariant();
        double hScale=double.IsFinite(heatScale)?Math.Clamp(heatScale,0.0,1.0):0.35;
        int heCap=Math.Clamp(maxHeDents,0,16);
        if(k=="era"||k=="aphe")return new BlastDentDecision(true,0.0,0);
        if(k=="heat")return new BlastDentDecision(false,hScale,hScale>0&&heCap>0?1:0);
        if(visualOnly&&k!="hesh"&&k!="he"&&k!="")return new BlastDentDecision(true,0.0,0);
        return new BlastDentDecision(false,1.0,heCap);
    }
}

public static class DentQueuePolicy
{
    public const int MaxQueueSize=48;
    public static bool ShouldContinueFrame(int processed,int maxPerFrame,double elapsedMs,double budgetMs)
    {
        if(processed<=0)return true;
        int cap=Math.Clamp(maxPerFrame,1,16);
        double budget=double.IsFinite(budgetMs)?Math.Clamp(budgetMs,0.5,50.0):4.0;
        return processed<cap&&elapsedMs<budget;
    }
}
