using System.Numerics;
namespace SprocketEmitter;

// Value-only decisions shared by the native reader and the offline tests.
internal readonly record struct GunCandidate(int CaliberMm,int Vuid,bool Installed,bool Released)
{
    internal bool Eligible=>Installed&&!Released&&CaliberMm>0;
    internal bool Beats(GunCandidate? best)=>Eligible&&(!best.HasValue||CaliberMm>best.Value.CaliberMm||
        (CaliberMm==best.Value.CaliberMm&&Vuid<best.Value.Vuid));
}

internal readonly record struct GunSize(bool Valid,int Vuid,int DiameterMm,int PropellantMm,string Reason)
{
    internal static GunSize Invalid(string reason)=>new(false,0,0,0,reason);
    internal static GunSize Validate(GunCandidate gun,bool hasShell,int diameter,int propellant) {
        if(!gun.Eligible)return Invalid("no installed cannon");
        if(!hasShell||diameter<=0||propellant<=0)return Invalid("ShellBlueprint empty or non-positive dimensions");
        if(diameter!=gun.CaliberMm)return Invalid("ShellBlueprint diameter differs from cannon caliber");
        if(diameter is <5 or >200||propellant is <10 or >2000)return Invalid("dimensions outside safe range (diameter 5-200 mm, length 10-2000 mm)");
        return new(true,gun.Vuid,diameter,propellant,"");
    }
}

internal readonly record struct EjectionSize(float Diameter,float Length,float CaliberMm,int PropellantMm,string Source)
{
    internal static EjectionSize Resolve(Settings settings,GunSize gun,float manualFactor) {
        if(settings.FollowCaliber&&gun.Valid)
            return new(gun.DiameterMm*.001f,(settings.Item=="casing"?gun.PropellantMm:3*gun.DiameterMm)*.001f,
                gun.DiameterMm,gun.PropellantMm,"cannon:"+gun.Vuid);
        float caliber=Settings.Finite(settings.Caliber,30,5,155);
        float diameter=caliber*.001f;
        float factor=Settings.Finite(manualFactor,settings.Item=="casing"?6.08f:3,.1f,20);
        return new(diameter,diameter*factor,caliber,0,settings.FollowCaliber?"fallback":"manual");
    }
    // Keep the existing longest-axis/bounds route for stock and embedded meshes alike.
    internal Vector3 Scale(float x,float y,float z,int longAxis)=>new(
        (longAxis==0?Length:Diameter)/x,(longAxis==1?Length:Diameter)/y,(longAxis==2?Length:Diameter)/z);
}
