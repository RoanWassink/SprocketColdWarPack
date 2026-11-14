using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class AtgmMotorCurveChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var tuning=new AtgmFlightProfile(SpeedCalibration:"motorSmooth");
        var settings=new AtgmSettings(FlightSpeed:278,Acceleration:180,MotorDelay:.1,MotorBurnTime:4,CoastDeceleration:0);
        double Speed(double t)=>AtgmFlightDynamics.FlightSpeed(settings,tuning,50,t);
        check(Speed(0)==50&&Speed(.1)==50,"motor preserves ejection speed until ignition");
        check(Speed(.11)<51&&Speed(.3)>60&&Speed(.3)<80,"ignition ramps thrust without a cruise-speed jump");
        check(Speed(.8)>145&&Speed(.8)<175&&Speed(1.6)==278,"SACLOS speed visibly builds over about one and a half seconds");
        foreach(var dt in new[]{.005,.01,.02,.04})
        {
            var monotonic=true;var bounded=true;var accelerationBounded=true;var prior=Speed(0);
            for(var t=dt;t<=4;t+=dt){var next=Speed(t);monotonic&=next>=prior-1e-8;bounded&=next>=50&&next<=278;accelerationBounded&=(next-prior)/dt<=180.00001;prior=next;}
            check(monotonic&&bounded&&accelerationBounded,"motor curve is finite, monotonic and respects acceleration across simulation steps");
        }
        const double epsilon=.0001;var duration=(278d-50)/180+.2+.1;
        check((Speed(.1+epsilon)-Speed(.1))/epsilon<.1&&(Speed(duration)-Speed(duration-epsilon))/epsilon<.1,
            "ignition and reaching cruise have a smooth acceleration boundary");
        var shortBurn=settings with{MotorBurnTime=.3};
        var cut=AtgmFlightDynamics.FlightSpeed(shortBurn,tuning,50,2);
        check(cut>50&&cut<100&&AtgmFlightDynamics.FlightSpeed(shortBurn,tuning,50,5)==cut,"burnout ends the boost without a hidden jump to cruise");
        var coast=shortBurn with{CoastDeceleration=5};
        check(AtgmFlightDynamics.FlightSpeed(coast,tuning,50,3)<AtgmFlightDynamics.FlightSpeed(coast,tuning,50,1),"configured post-burnout deceleration remains effective");
        check(AtgmFlightDynamics.FlightSpeed(settings with{Acceleration=0},tuning,50,2)==50,"zero acceleration does not create a speed jump");
        var defaults=ReleaseProfiles.Defaults();var json=JsonNode.Parse(defaults)!;
        foreach(var p in json["profiles"]!.AsArray().Where(p=>p!["id"]!.GetValue<string>() is "atgm_saclos_direct_v1" or "atgm_mclos_manual_v1" or "atgm_gun_hump_v1"))
            check(p!["speedCalibration"]!.GetValue<string>()=="motorSmooth"&&p["coastDeceleration"]!.GetValue<double>()==0,"new stock ATGM uses smooth boost and maintains calibrated cruise");
        var draft=new ShellEditorDraft(defaults);var index=json["profiles"]!.AsArray().Select((p,i)=>(p,i)).First(v=>v.p!["id"]!.GetValue<string>()=="atgm_saclos_direct_v1").i;
        draft.Set(index,"speedCalibration","motorSmooth");
        check(ShellProfiles.Parse(draft.Serialize()).First(p=>p.Id=="atgm_saclos_direct_v1").Atgm!.FlightProfile!.SpeedCalibration=="motorSmooth","smooth motor option roundtrips through the actual editor and parser");
    }
}
