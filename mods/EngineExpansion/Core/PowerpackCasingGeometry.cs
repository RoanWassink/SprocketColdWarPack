namespace SprocketEngineExpansion.Core;

/// <summary>Shared decoder: runtime and the exact-geometry preview consume the same binary vertices.</summary>
public static class MeshPayload
{
    public static PowerpackBridgeMesh Read(Stream stream)
    {
        using var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
        int nv=reader.ReadInt32(),ni=reader.ReadInt32();
        if(nv<1||nv>1000000||ni<3||ni>6000000||ni%3!=0)throw new InvalidDataException("Invalid mesh dimensions");
        var vertices=new Vec3[nv];var triangles=new int[ni];
        for(int i=0;i<nv;i++)
        {vertices[i]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());if(!double.IsFinite(vertices[i].X+vertices[i].Y+vertices[i].Z))throw new InvalidDataException("Non-finite mesh vertex");}
        for(int i=0;i<ni;i++){triangles[i]=reader.ReadInt32();if(triangles[i]<0||triangles[i]>=nv)throw new InvalidDataException("Invalid mesh index");}
        if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing mesh data");
        return new(vertices,triangles);
    }
}
public readonly record struct CasingSection(double CentreX,double Bottom,double Width,double Height,double Z);
public sealed record CasingFit(CasingSection Motor,CasingSection Gearbox)
{
    public const double SkinClearance=.008,SeamCover=.020,BridgeClearance=.012;
    public static CasingFit FromCorners(IEnumerable<Vec3> engine,IEnumerable<Vec3> gearboxes)
    {
        var e=engine.ToArray();var g=gearboxes.ToArray();
        CasingSection Section(Vec3[] a,double z)=>new((a.Min(p=>p.X)+a.Max(p=>p.X))*.5,
            a.Min(p=>p.Y)-SkinClearance,a.Max(p=>p.X)-a.Min(p=>p.X)+SkinClearance*2,
            a.Max(p=>p.Y)-a.Min(p=>p.Y)+SkinClearance*2,z);
        var motor=Section(e,e.Max(p=>p.Z)-SeamCover);
        var gearbox=Section(g,g.Max(p=>p.Z)+SeamCover);
        if(gearbox.Z<=motor.Z+.001||motor.Width<=0||gearbox.Width<=0||motor.Height<=0||gearbox.Height<=0)
            throw new InvalidOperationException("No valid casing span between motor and gearbox");
        return new(motor,gearbox);
    }
    private static double Mix(double a,double b,double t)=>a+(b-a)*t;
    public Vec3 Warp(Vec3 unit)
    {
        double t=Math.Clamp(unit.Z,0,1);
        return new(Mix(Motor.CentreX,Gearbox.CentreX,t)+unit.X*Mix(Motor.Width,Gearbox.Width,t),
            Mix(Motor.Bottom,Gearbox.Bottom,t)+unit.Y*Mix(Motor.Height,Gearbox.Height,t),Mix(Motor.Z,Gearbox.Z,t));
    }
    public PowerpackBridgeMesh Warp(PowerpackBridgeMesh unit)=>new(unit.Vertices.Select(Warp).ToArray(),unit.Triangles);
    // Approved hood occupies normalised longitudinal 0..0.345; .746 is its LOWEST underside.
    // Keeping the bridge 12 mm below this conservative roof avoids coplanar roof faces.
    public double? BridgeRoof(double z)
    {
        double t=(z-Motor.Z)/(Gearbox.Z-Motor.Z);
        return t>=0&&t<=.345?Mix(Motor.Bottom,Gearbox.Bottom,t)+.746*Mix(Motor.Height,Gearbox.Height,t)-BridgeClearance:null;
    }
    public PowerpackBridgeMesh ClearBridge(PowerpackBridgeMesh bridge)=>new(bridge.Vertices.Select(v=>
        BridgeRoof(v.Z) is {} limit?v with {Y=Math.Min(v.Y,limit)}:v).ToArray(),bridge.Triangles);
}
public static class CasingVisibility
{
    public static bool Visible(bool modelEnabled,bool casingEnabled,bool effective,bool marked)=>modelEnabled&&casingEnabled&&effective&&marked;
}
