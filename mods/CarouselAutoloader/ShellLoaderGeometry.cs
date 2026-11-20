using System.Reflection;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.AmmunitionStorage;

namespace SprocketCarouselAutoloader;

internal readonly record struct LoaderShellSize(int Status,double ProjectileMm,double ChargeMm,double DiameterMm)
{
    internal double FullLengthMm => ProjectileMm+ChargeMm;
    internal bool Valid => LoaderPhysicalFit.Valid(Status,ProjectileMm,ChargeMm,DiameterMm);
}

// Optional managed API: native ammo identity and request encoding are never changed.
internal static class ShellLoaderGeometry
{
    private delegate int GeometryReader(Cannon cannon,out double projectile,out double charge,out double diameter);
    private delegate int TypedGeometryReader(Cannon cannon,int type,out double projectile,out double charge,out double diameter);
    private static GeometryReader? reader;
    private static TypedGeometryReader? typedReader;
    private static long nextSearch;
    private static bool warned;
    internal static LoaderShellSize Read(Cannon cannon)
    {
        try
        {
            if(reader==null && Environment.TickCount64>=nextSearch)
            {
                nextSearch=Environment.TickCount64+1000;
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type=assembly.GetType("SprocketExpandedBallistics.ShellGeometryApi",false);
                    var method=type?.GetMethod("GetLoaderGeometry",BindingFlags.Public|BindingFlags.Static);
                    if(method==null) continue;
                    reader=method.CreateDelegate<GeometryReader>();
                    typedReader=type?.GetMethod("GetLoaderGeometryForType",BindingFlags.Public|BindingFlags.Static)?.CreateDelegate<TypedGeometryReader>();
                    break;
                }
            }
            if(reader!=null)
            {
                var status=reader(cannon,out var projectile,out var charge,out var diameter);
                if(status!=0) return new(status,projectile,charge,diameter);
            }
            var shell=cannon.ShellBlueprint;
            return shell==null ? new(-1,0,0,0) : new(0,shell.Diameter*3.0,shell.PropellantLength,shell.Diameter);
        }
        catch(Exception ex)
        {
            if(!warned) { warned=true; Plugin.ModLog.LogWarning($"[Autoloader] Shell geometry unavailable; custom feed blocked: {ex.Message}"); }
            return new(-1,0,0,0);
        }
    }
    internal static LoaderShellSize ReadForType(Cannon cannon,int type)
    {
        var envelope=Read(cannon); // Also resolves the optional typed API once.
        if(typedReader==null)return envelope;
        try
        {
            var status=typedReader(cannon,type,out var projectile,out var charge,out var diameter);
            return status==0?envelope:new(status,projectile,charge,diameter);
        }
        catch{return new(-1,0,0,0);}
    }
    internal static int RackCapacity(AmmoRack rack,Cannon cannon,int nativeCapacity=-1)
    {
        var size=Read(cannon);
        if(!size.Valid) return 0;
        if(nativeCapacity<0) nativeCapacity=rack.Capacity;
        if(size.Status==0) return nativeCapacity;
        if(!LoaderPhysicalFit.NativePackDimensions(size.ProjectileMm,size.ChargeMm,size.DiameterMm,out var diameter,out var length)) return 0;
        var bp=rack.Blueprint;
        if(bp==null) return 0;
        // Let the same native packer decide whether a complete custom round fits.
        // Never change ShellSizeID: it must still match the gun's native request.
        return Math.Max(0, Math.Min(nativeCapacity, Grid(rack, diameter, length).Capacity));
    }

    internal static RackGrid Grid(AmmoRack rack, int diameter, int length)
    {
        var bp = rack.Blueprint;
        if (bp == null) return default;
        var s = rack.spacing; var a = rack.minPadding; var b = rack.maxPadding;
        return RackGrid.Of(bp.Width / 1000d, bp.Height / 1000d, bp.Length / 1000d, diameter, length,
            new(s.x, s.y, s.z), new(a.x, a.y, a.z), new(b.x, b.y, b.z));
    }
}
