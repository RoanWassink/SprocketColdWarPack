using System.Numerics;
namespace SprocketExpandedBallistics;
internal static class AtgmLaunchCaptureIntegrationChecks
{
    private static double Angle(Vector3 a,Vector3 b)=>Math.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a),Vector3.Normalize(b)),-1,1))*180/Math.PI;
    private static Vector3 Launch(double degrees)=>new(0,(float)Math.Sin(degrees*Math.PI/180),(float)Math.Cos(degrees*Math.PI/180));
    internal static void Run(Action<bool,string> check)
    {
        var launch=Launch(60);var target=Vector3.UnitZ;
        var c=new AtgmLaunchCapture(launch,target);
        check(c.Enabled&&!c.Finished,"elevated independent tube enables capture");
        check(Angle(c.Step(launch,target,.1,100,.02),launch)<.05,"capture waits for tube clearance");
        var v=c.Step(launch,target,.2,100,.02);
        check(Angle(launch,v)>0&&Angle(launch,v)<=1.802&&Math.Abs(v.Length()-100)<.001,"capture obeys angular rate and preserves actual motor speed");
        var high=new AtgmLaunchCapture(launch,target).Step(launch,target,.2,375,.02);
        check(Angle(launch,high)<=250d/375*.02*180/Math.PI+.002,"capture lateral acceleration caps high-speed turn");
        check(Angle(c.Step(launch,null,.3,100,.02),launch)<.05,"missing owned sight gives no capture turn");
        c.Step(launch,target,5,100,.02);check(c.Finished&&c.Status=="expired","capture has absolute finite lifetime");
        check(Angle(c.Step(launch,target,.2,100,.02),launch)<.05,"expired capture cannot restart after clock rewind");
        foreach(var angle in new[]{45d,60d,90d})
        {
            var f=new AtgmLaunchCapture(Launch(angle),target);var heading=Launch(angle);
            for(int i=0;i<150&&!f.Finished;i++)
            {var next=f.Step(heading,target,.15+i*.02,100,.02);check(float.IsFinite(next.X)&&Math.Abs(next.Length()-100)<.001&&Angle(heading,next)<=1.802,"capture trajectory remains continuous and speed bounded");heading=Vector3.Normalize(next);}
            check(f.Finished&&f.Status=="captured"&&Angle(heading,target)<3.1,"low-speed elevated tube gathers toward sight heading");
        }
        foreach(var bad in new[]{Vector3.Zero,new Vector3(float.NaN,0,1),new Vector3(float.PositiveInfinity,0,1)})
            check(new AtgmLaunchCapture(bad,target).Finished,"invalid launch vector stays inert");
        check(new AtgmLaunchCapture(target,target).Finished&&new AtgmLaunchCapture(Launch(-60),target).Finished,"ordinary/inverted tubes have no capture boost");
        var rear=new AtgmLaunchCapture(launch,target);rear.Step(launch,-target,.2,100,.02);
        check(rear.Finished&&rear.Status=="rejected","capture cannot chase a rearward command");
        foreach(var dt in new[]{double.NaN,-1,0,.2})check(Angle(new AtgmLaunchCapture(launch,target).Step(launch,target,.2,100,dt),launch)<.05,"invalid/large timestep cannot jump heading");
        var noArrival=new AtgmLaunchCapture(launch,target);noArrival.Step(target,target,1,100,.02,false);
        check(!noArrival.Finished,"matching heading alone is not arrival at the physical sight ray");
        check(!AtgmIndependentSightPolicy.Allows(true,2,false,true)&&!AtgmIndependentSightPolicy.Allows(true,1,true,true)&&!AtgmIndependentSightPolicy.Allows(false,1,false,true)&&AtgmIndependentSightPolicy.Allows(true,1,false,true),"independent optics requires one owned launcher and rejects shared/foreign use");
        // Production motor and guidance trajectory: elevated launches must actually
        // gather back onto the sight line, not merely finish pointing horizontally.
        var profile=ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id=="atgm_saclos_direct_v1");
        foreach(var angle in new[]{45d,60d})
        {
            var heading=Launch(angle);var pos=new Vector3(0,2,0);var origin=pos;var gather=new AtgmLaunchCapture(heading,target);double closest=double.MaxValue;bool captured=false;
            for(int i=0;i<450;i++)
            {
                var age=i*.02;var speed=AtgmFlightDynamics.FlightSpeed(profile.Atgm!,profile.Atgm!.FlightProfile!,50,age);
                var delta=pos-origin;var along=Vector3.Dot(delta,target);var cross=delta-target*along;
                var aim=origin+target*AtgmLaunchCapture.GatherLead(along,speed,cross.Length())-pos;
                Vector3 command;
                if(gather.Enabled&&!gather.Finished){command=gather.Step(heading,aim,age,speed,.02,cross.Length()<4);captured|=gather.Status=="captured";}
                else {AtgmFlightDynamics.TryAimAlongSightRay(origin,target,pos,30,out var point);command=AtgmGuidance.Step(heading,point-pos,profile.Atgm! with{FlightSpeed=speed},.02);}
                heading=Vector3.Normalize(command);pos+=command*.02f;
                if(age>1)closest=Math.Min(closest,Math.Abs(pos.Y-origin.Y));
                if(pos.Z>1000)break;
            }
            Console.WriteLine($"CAPTURE TRAJECTORY angle={angle} captured={captured} closestLOS={closest:0.00} final={pos}");
            check(captured&&closest<4,"elevated production motor gathers back to physical sight ray");
        }
        // Swingfire: fixed box elevated 20 degrees forward, separated sight looking
        // 45/90 degrees to the side. The missile must swing onto the sight ray.
        var swing=ShellProfiles.Parse(ReleaseProfiles.Defaults()).Single(p=>p.Id=="atgm_swingfire");
        foreach(var side in new[]{45d,90d})
        {
            var sight=new Vector3((float)Math.Sin(side*Math.PI/180),0,(float)Math.Cos(side*Math.PI/180));
            var heading=Launch(20);var pos=new Vector3(0,2,0);var origin=new Vector3(3,2,0);var gather=new AtgmLaunchCapture(heading,sight);
            double late=double.MaxValue;bool captured=false;float capturedAlong=float.NaN,capturedAge=float.NaN;
            check(gather.Enabled,"Swingfire sideways gather accepted");
            for(int i=0;i<1500;i++)
            {
                var age=i*.02;var speed=AtgmFlightDynamics.FlightSpeed(swing.Atgm!,swing.Atgm!.FlightProfile!,50,age);
                var delta=pos-origin;var along=Vector3.Dot(delta,sight);var cross=delta-sight*along;
                var aim=origin+sight*AtgmLaunchCapture.GatherLead(along,speed,cross.Length())-pos;
                Vector3 command;
                if(gather.Enabled&&!gather.Finished){command=gather.Step(heading,aim,age,speed,.02,cross.Length()<4);if(!captured&&gather.Status=="captured"){captured=true;capturedAlong=along;capturedAge=(float)age;}}
                else {AtgmFlightDynamics.TryAimAlongSightRay(origin,sight,pos,30,out var point);command=AtgmGuidance.Step(heading,point-pos,swing.Atgm! with{FlightSpeed=speed},.02);}
                heading=Vector3.Normalize(command);pos+=command*.02f;
                if(along>1000)late=Math.Min(late,cross.Length());
                if(along>1500)break;
            }
            Console.WriteLine($"SWINGFIRE GATHER side={side} captured={captured} at={capturedAlong:0}m along sight after {capturedAge:0.00}s offRayAfter1km={late:0.00}");
            check(captured&&late<3,"Swingfire swings onto a separated sight's ray");
        }
    }
}
