using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Turrets;

namespace SprocketExpandedBallistics;

// Resolve real mount ownership from the saved attachment tree, never proximity.
internal static class RuntimeAtgmSightMount
{
    internal const string ElevationGuid="31fa3dd0-0738-4d29-b6a5-c3d77c291ea2";
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> a)=>a.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    internal static bool Below(VehicleTransform? child,VehicleTransform? ancestor)
    {
        if(ancestor==null)return false;
        var seen=new HashSet<IntPtr>();
        for(var t=child;t!=null&&seen.Add(t.Pointer);t=t.Parent)
            if(t.Pointer==ancestor.Pointer)return true;
        return false;
    }
    // True when a dedicated ATGM guidance sight sits on the structure this ring drives.
    internal static bool CarriesGuidanceSight(TurretRing ring)
    {
        var controlled=ring.motor?.ControlledTransform??ring.structure?.VehicleTransform;
        if(controlled==null||ring.Vehicle==null)return false;
        var objects=ring.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
            if(objects[i].GUID==RuntimeAtgmGuidanceSight.Guid&&Below(objects[i].VehicleTransform,controlled))return true;
        return false;
    }
    internal static IEnumerable<TurretRing> Rings(IVehicleGateway vehicle)
    {
        var objects=vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)if(parts[j].TryCast<TurretRing>() is {} ring)yield return ring;
        }
    }
    private static VehicleTransform? Controlled(TurretRing ring)=>ring.motor?.ControlledTransform??ring.structure?.VehicleTransform;
    // The two drives of an ATGM sight mount: an elevation ring carrying the
    // guidance sight, and the nearest ring below it (its yaw foot). Main turret
    // rings further down keep native motor rules.
    internal static bool IsMountRing(TurretRing ring)
    {
        if(ring.Vehicle==null||!CarriesGuidanceSight(ring))return false;
        if(ring.VehicleObject.GUID==ElevationGuid)return true;
        var controlled=Controlled(ring);if(controlled==null)return false;
        var rings=Rings(ring.Vehicle).ToArray();
        var drives=rings.Select(Controlled).Where(t=>t!=null).Select(t=>t!.Pointer).ToHashSet();
        foreach(var pitch in rings.Where(r=>r.VehicleObject?.GUID==ElevationGuid&&CarriesGuidanceSight(r)))
        {
            var seen=new HashSet<IntPtr>();
            for(var t=pitch.VehicleObject.VehicleTransform.Parent;t!=null&&seen.Add(t.Pointer);t=t.Parent)
                if(drives.Contains(t.Pointer)){if(t.Pointer==controlled.Pointer)return true;break;}
        }
        return false;
    }
    internal static TurretRing? RingOf(TraverseMotor motor)=>motor.Vehicle==null?null:Rings(motor.Vehicle).FirstOrDefault(r=>r.motor?.Pointer==motor.Pointer);
    internal static bool IsSightAxis(GunnerSight sight,Cannon launcher,IntPtr axis)
    {
        if(axis==IntPtr.Zero||sight.Vehicle==null||launcher.Vehicle==null||sight.Vehicle.Pointer!=launcher.Vehicle.Pointer)return false;
        var objects=sight.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)
            {
                var ring=parts[j].TryCast<TurretRing>();
                if(ring==null||ring.motor?.Behaviour?.TryCast<IReadOnlyAimable>()?.Pointer!=axis)continue;
                var controlled=ring.motor.ControlledTransform;
                if(!Below(sight.VehicleTransform,controlled))return false;
                // The pitch stage follows the sight. A launcher on this stage
                // elevates with it; one on the yaw foot keeps its own angle.
                if(ring.VehicleObject.GUID==ElevationGuid)return true;
                // Ordinary native turret yaw can carry both tube and optic.
                return ring.VehicleObject.GUID=="99281776-6b29-4ffb-9d8b-04139ca7b6a2";
            }
        }
        return false;
    }
    // Diagnostic: every ring axis above the sight and why it is or is not a sight axis.
    internal static string Describe(GunnerSight sight,Cannon launcher)
    {
        var parts=new List<string>();var objects=sight.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var components=objects[i].Components;
            for(int j=0;j<Count(components);j++)
            {
                var ring=components[j].TryCast<TurretRing>();
                var axis=ring?.motor?.Behaviour?.TryCast<IReadOnlyAimable>()?.Pointer??IntPtr.Zero;
                var controlled=ring?.motor?.ControlledTransform;
                if(ring==null||axis==IntPtr.Zero||!Below(sight.VehicleTransform,controlled))continue;
                string kind=ring.VehicleObject.GUID==ElevationGuid?"pitch":"yaw";
                parts.Add($"{kind}:ring={ring.VUID.Value} axis={axis.ToInt64():X} sightAxis={IsSightAxis(sight,launcher,axis)} unshared={Unshared(launcher,axis)} launcherBelow={Below(launcher.VehicleTransform,controlled)} connected={ring.motor.Connected}");
            }
        }
        return parts.Count==0?"none":string.Join("; ",parts);
    }
    internal static HashSet<IntPtr> Axes(GunnerSight sight,Cannon launcher)
    {
        var result=new HashSet<IntPtr>();var objects=sight.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)
            {
                var axis=parts[j].TryCast<TurretRing>()?.motor?.Behaviour?.TryCast<IReadOnlyAimable>()?.Pointer??IntPtr.Zero;
                if(axis!=IntPtr.Zero&&IsSightAxis(sight,launcher,axis)&&Unshared(launcher,axis))result.Add(axis);
            }
        }
        return result;
    }
    internal static bool Unshared(Cannon owner,IntPtr axis)
    {
        var objects=owner.Vehicle.ObjectReader.Items;
        for(int i=0;i<Count(objects);i++)
        {
            var parts=objects[i].Components;
            for(int j=0;j<Count(parts);j++)
                if(parts[j].TryCast<Cannon>() is {} c&&c.Pointer!=owner.Pointer&&
                   (c.HorizontalAimingMechanism?.Pointer==axis||c.VerticalAimingMechanism?.Pointer==axis))return false;
        }
        return true;
    }
}
