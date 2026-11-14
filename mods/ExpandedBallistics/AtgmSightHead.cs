namespace SprocketExpandedBallistics;

// Transient angles in the part frame; positive pitch looks up. A bounded yaw
// drive must go through the front rather than cross its forbidden rear sector.
internal readonly record struct AtgmHeadAngles(float Yaw,float Pitch);
internal readonly record struct AtgmHeadLimits(float MinYaw,float MaxYaw,float MinPitch,float MaxPitch)
{
    public AtgmHeadLimits():this(-170,170,-20,60){}
}
internal static class AtgmSightHead
{
    internal static bool CanMove(bool assigned,bool valid)=>assigned&&valid;
    internal static float Wrap(float angle)=>float.IsFinite(angle)?(angle%360+540)%360-180:0;
    internal static AtgmHeadAngles Slew(AtgmHeadAngles head,AtgmHeadAngles command,float yawRate,float pitchRate,float dt,bool gunner,AtgmHeadLimits limits)
    {
        if(!gunner||!float.IsFinite(dt)||dt<=0)return head;
        var yaw=Math.Clamp(Wrap(command.Yaw),limits.MinYaw,limits.MaxYaw);
        var pitch=Math.Clamp(float.IsFinite(command.Pitch)?command.Pitch:head.Pitch,limits.MinPitch,limits.MaxPitch);
        return new(Move(head.Yaw,yaw,yawRate,dt),Move(head.Pitch,pitch,pitchRate,dt));
    }
    private static float Move(float from,float to,float rate,float dt)
    {
        var step=float.IsFinite(rate)?Math.Max(0,rate)*dt:0;
        return from+Math.Clamp(to-from,-step,step);
    }
}
