using System.Numerics;
namespace SprocketExpandedBallistics;
internal static class KobraCloseRangeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var id in new[]{"atgm_gun_hump_v1","atgm_kobra_9m112m"})
        {
            var profile=ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id==id);var flight=profile.Atgm!;var tune=flight.FlightProfile!;
            check(AtgmFlightDynamics.EffectiveGuidanceDelay(flight.GuidanceDelay,tune,150)==0,"Kobra enables guidance before passing the 150m sandbox target");
            check(AtgmFlightDynamics.EffectiveGuidanceDelay(flight.GuidanceDelay,tune,null)==0&&AtgmFlightDynamics.SelectPattern(tune,null)==AtgmRangePattern.Direct,"unknown visible range uses direct guidance without blind one-second lockout");
            foreach(var bad in new[]{double.NaN,double.PositiveInfinity,-1d,0d})check(AtgmFlightDynamics.EffectiveGuidanceDelay(flight.GuidanceDelay,tune,bad)==0,"invalid range cannot leave Kobra blind in direct fallback");
            check(AtgmFlightDynamics.EffectiveGuidanceDelay(flight.GuidanceDelay,tune,2500)==1&&AtgmFlightDynamics.SelectPattern(tune,2500)==AtgmRangePattern.Hump,"measured long-range Kobra keeps its original hump and delay");
            double oldBlindDistance=0;for(int i=0;i<50;i++)oldBlindDistance+=AtgmFlightDynamics.FlightSpeed(flight,tune,176.6,i*.02)*.02;
            check(oldBlindDistance>150,"old lockout lasts beyond close target with actual gun launch speed");
        }
        check(AtgmFlightDynamics.EffectiveGuidanceDelay(.3,AtgmFlightProfile.Legacy,null)==.3,"ordinary custom guidance delay remains unchanged");
        var disabled=new AtgmFlightProfile(RangePattern:"hump",ShortRangeThresholdM:0);
        check(AtgmFlightDynamics.EffectiveGuidanceDelay(1,disabled,null)==1,"explicitly disabled short-range handling retains custom delay");
        var lens=new Vector3(0,2,0);var point=new Vector3(0,1.5f,150);var camera=new Vector3(0,6,-10);
        check(AtgmFlightDynamics.TryOpticalAim(lens,point,out var ray)&&Vector3.Distance(lens+ray*Vector3.Distance(lens,point),point)<.001,"cursor point is reprojected through physical lens rather than high third-person camera");
        var cameraRay=Vector3.Normalize(point-camera);
        check(Math.Abs(ray.Y)<Math.Abs(cameraRay.Y)*.2,"reprojection prevents excess downward command from camera parallax");
        check(!AtgmFlightDynamics.TryOpticalAim(lens,lens,out _)&&!AtgmFlightDynamics.TryOpticalAim(lens,new(float.NaN,0,1),out _),"malformed/degenerate optical aim is rejected");
        var p=ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id=="atgm_kobra_9m112m");var settings=p.Atgm!;var tuning=settings.FlightProfile!;
        var heading=Vector3.Normalize(new Vector3(0,-.01f,1));var position=lens;bool steeredBeforeTarget=false;
        for(int i=0;i<100&&position.Z<150;i++)
        {
            var age=i*.02;var speed=AtgmFlightDynamics.FlightSpeed(settings,tuning,176.6,age);
            Vector3? desired=null;
            if(age>=settings.MotorDelay&&age>=AtgmFlightDynamics.EffectiveGuidanceDelay(settings.GuidanceDelay,tuning,150)&&AtgmFlightDynamics.TryAimAlongSightRay(lens,ray,position,tuning.LeadDistanceM,out var target))desired=target-position;
            var command=AtgmGuidance.Step(heading,desired,settings with{FlightSpeed=speed},.02);
            if(desired!=null&&Vector3.Distance(heading,Vector3.Normalize(command))>.00001f)steeredBeforeTarget=true;
            heading=Vector3.Normalize(command);position+=command*.02f;
        }
        Console.WriteLine($"KOBRA CLOSE: atZ={position.Z:0.0} height={position.Y:0.00} targetHeight={point.Y:0.00} guidedBeforeTarget={steeredBeforeTarget}");
        check(steeredBeforeTarget&&Math.Abs(position.Y-point.Y)<.25,"actual Kobra motor/LOS model follows close optical target before the old lockout expired");
    }
}
