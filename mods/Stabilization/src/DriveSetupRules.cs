namespace SprocketStabilization;
internal static class DriveSetupRules
{
    internal static bool UniqueOwnership(int d,int m,int r)=>d==1&&m==1&&r==1;
    internal static (float Min,float Max) StrengthLimits(float current,float scale,float max)
    {
        if(!float.IsFinite(current)||current<=0||!ValidTechnology(scale,max))throw new ArgumentOutOfRangeException(nameof(current));
        return(Math.Min(1,current)/scale,Math.Max(max,current)/scale);
    }
    internal static bool ValidTechnology(float scale,float max)=>float.IsFinite(scale)&&scale>0&&float.IsFinite(max)&&max>=1&&max<=1000000;
    internal static bool ValidStrengthEdit(float value,float min,float max)=>float.IsFinite(value)&&value>0&&value>=min&&value<=max;
}
