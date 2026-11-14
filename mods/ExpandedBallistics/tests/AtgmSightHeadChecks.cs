namespace SprocketExpandedBallistics;
internal static class AtgmSightHeadChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var limits=new AtgmHeadLimits();var zero=new AtgmHeadAngles(0,0);
        AtgmHeadAngles Step(AtgmHeadAngles h,AtgmHeadAngles c,float dt,bool gunner=true)=>AtgmSightHead.Slew(h,c,60,40,dt,gunner,limits);
        bool Near(float a,float b)=>Math.Abs(a-b)<.0002f;
        foreach(var assigned in new[]{false,true})foreach(var valid in new[]{false,true})
            check(AtgmSightHead.CanMove(assigned,valid)==(assigned&&valid),"head requires both assigned and valid gunner");
        var first=Step(zero,new(150,50),.1f);
        check(Near(first.Yaw,6)&&Near(first.Pitch,4),"independent yaw/pitch rate caps in degrees per second");
        check(Step(zero,new(2,-1),1)==new AtgmHeadAngles(2,-1),"head reaches near command without overshoot");
        check(Step(new(23,-7),new(-150,60),10,false)==new AtgmHeadAngles(23,-7),"no gunner freezes both axes despite large command and dt");
        check(Step(first,new(-150,-20),1,false)==first,"losing crew freezes the current pose");
        check(Step(first,new(-150,-20),.1f).Yaw<first.Yaw,"restored crew resumes from frozen pose");
        check(Step(zero,new(179,89),100)==new AtgmHeadAngles(170,60),"positive mechanical stops");
        check(Step(zero,new(-179,-89),100)==new AtgmHeadAngles(-170,-20),"negative mechanical stops");
        check(Step(zero,new(359,0),1)==new AtgmHeadAngles(-1,0),"yaw command wraps359 to minus one");
        check(Step(zero,new(-359,0),1)==new AtgmHeadAngles(1,0),"yaw command wraps minus359 to one");
        check(Step(zero,new(721,0),1)==new AtgmHeadAngles(1,0),"multiple yaw revolutions normalize");
        check(Step(new(169,0),new(-169,0),.1f)==new AtgmHeadAngles(163,0),"rear wrap does not jump or cross forbidden yaw sector");
        check(Step(new(-169,0),new(169,0),.1f)==new AtgmHeadAngles(-163,0),"reverse rear wrap stays on mechanical travel");
        foreach(var dt in new[]{0f,-1,float.NaN,float.PositiveInfinity})
            check(Step(first,new(150,60),dt)==first,"invalid or paused clock cannot move head");
        check(AtgmSightHead.Slew(first,new(150,60),0,0,1,true,limits)==first,"zero configured rates lock head");
        check(AtgmSightHead.Slew(first,new(150,60),float.NaN,-1,1,true,limits)==first,"invalid rates cannot propagate or reverse motion");
        check(AtgmSightHead.Slew(zero,new(80,50),10,5,100,true,new(-30,30,-10,20))==new AtgmHeadAngles(30,20),"custom limits used by slew logic");
        var elevation=new AtgmSightElevationState();
        elevation.BeforeWrite(-10,20);
        check(elevation.RestoreMin(-60)==-10&&elevation.RestoreMax(60)==20,"legacy native-axis camera bounds restore");
        elevation.BeforeWrite(-60,60,-90,90);
        check(elevation.RestoreMin(-90)==-10&&elevation.RestoreMax(90)==20,"gimbal mode switch retains original camera bounds");
        elevation.BeforeWrite(-15,90,-90,90);
        check(elevation.RestoreMin(-90)==-15&&elevation.RestoreMax(90)==20,"native limit override becomes per-bound restore value");
        check(elevation.RestoreMax(30)==30,"cleanup never overwrites a later camera owner");
        foreach(var dt in new[]{.01f,.02f,.1f})
        {
            var pose=zero;var command=new AtgmHeadAngles(150,60);
            for(int i=0;i<(int)Math.Round(1/dt);i++)pose=Step(pose,command,dt);
            check(Near(pose.Yaw,60)&&Near(pose.Pitch,40),"one-second travel independent of frame partition");
        }
        var state=zero;
        for(int i=0;i<1000;i++)
        {
            var target=new AtgmHeadAngles(i%2==0?179:-179,i%3==0?89:-89);var before=state;
            state=Step(state,target,.016f);
            check(Math.Abs(state.Yaw-before.Yaw)<=.9601f&&Math.Abs(state.Pitch-before.Pitch)<=.6401f&&
                state.Yaw>=-170&&state.Yaw<=170&&state.Pitch>=-20&&state.Pitch<=60,"alternating extreme commands retain speed and mechanical limits");
        }
    }
}
