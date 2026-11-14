using Sprocket.Vehicles.Cannons;

namespace SprocketExpandedBallistics;

/// <summary>Optional canonical storage geometry for physical loading adapters.</summary>
public static class ShellGeometryApi
{
    /// <returns>0 for native ammunition, 1 for custom geometry, -1 for invalid custom geometry.</returns>
    public static int GetLoaderGeometry(Cannon cannon,out double projectileLengthMm,out double propellantLengthMm,out double projectileDiameterMm)
    {
        projectileLengthMm=propellantLengthMm=projectileDiameterMm=0;
        try
        {
            if(RuntimeAmmoLoadout.HasLoadout(cannon))return GetLargestCarried(cannon,out projectileLengthMm,out propellantLengthMm,out projectileDiameterMm);
            var profile=RuntimeShellSelection.CannonProfile(cannon);
            if(profile==null)return 0;
            var dimensions=ShellStorageGeometry.Calculate(cannon.Blueprint.Caliber,cannon.ShellBlueprint.PropellantLength,profile.Settings);
            projectileLengthMm=dimensions.ProjectileLengthMm;propellantLengthMm=dimensions.PropellantLengthMm;projectileDiameterMm=dimensions.DiameterMm;
            return 1;
        }
        catch{return -1;}
    }
    public static int GetLoaderGeometryForProfile(Cannon cannon,string profileId,out double projectileLengthMm,out double propellantLengthMm,out double projectileDiameterMm)
    {
        projectileLengthMm=propellantLengthMm=projectileDiameterMm=0;
        try
        {
            if(profileId==ShellProfiles.Vanilla){projectileLengthMm=3*cannon.Blueprint.Caliber;propellantLengthMm=cannon.ShellBlueprint.PropellantLength;projectileDiameterMm=cannon.Blueprint.Caliber;return 1;}
            var profile=RuntimeShellSelection.Profiles.FirstOrDefault(p=>p.Id==profileId);
            if(profile==null||!RuntimeShellEra.Allowed(profile,cannon.Vehicle))return -1;
            var d=ShellStorageGeometry.Calculate(cannon.Blueprint.Caliber,cannon.ShellBlueprint.PropellantLength,profile.Settings);
            projectileLengthMm=d.ProjectileLengthMm;propellantLengthMm=d.PropellantLengthMm;projectileDiameterMm=d.DiameterMm;return 1;
        }
        catch{return -1;}
    }
    public static int GetLoaderGeometryForType(Cannon cannon,int typeId,out double projectileLengthMm,out double propellantLengthMm,out double projectileDiameterMm)
    {
        projectileLengthMm=propellantLengthMm=projectileDiameterMm=0;
        return RuntimeAmmoLoadout.TryProfileForType(cannon,typeId,out var id)?GetLoaderGeometryForProfile(cannon,id,out projectileLengthMm,out propellantLengthMm,out projectileDiameterMm):RuntimeAmmoLoadout.HasLoadout(cannon)?-1:GetLoaderGeometry(cannon,out projectileLengthMm,out propellantLengthMm,out projectileDiameterMm);
    }
    private static int GetLargestCarried(Cannon cannon,out double projectile,out double charge,out double diameter)
    {
        projectile=charge=diameter=0;
        foreach(var entry in RuntimeAmmoLoadout.DesignEntries(cannon).Where(e=>e.Count>0))
        {
            if(GetLoaderGeometryForProfile(cannon,entry.ProfileId,out var p,out var c,out var d)!=1)return -1;
            projectile=Math.Max(projectile,p);charge=Math.Max(charge,c);diameter=Math.Max(diameter,d);
        }
        return projectile>0&&diameter>0?1:-1;
    }
}
