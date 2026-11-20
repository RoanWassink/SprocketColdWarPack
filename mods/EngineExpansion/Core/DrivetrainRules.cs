namespace SprocketEngineExpansion.Core;

public static class DrivetrainRules
{
    public static double LengthScale(double volume)
    {if(!double.IsFinite(volume)||volume<=0||volume>1)throw new ArgumentOutOfRangeException(nameof(volume));return Math.Pow(volume,1.0/3);}
    public static float Angle(float native,double multiplier)
    {if(!float.IsFinite(native)||native<0||native>180||!double.IsFinite(multiplier)||multiplier<1||multiplier>2)throw new ArgumentOutOfRangeException();return (float)Math.Min(180,native*multiplier);}
    public static float Angle(float native,double multiplier,bool unlimited)
    {if(!float.IsFinite(native)||native<0||native>180)throw new ArgumentOutOfRangeException(nameof(native));return unlimited?180f:Angle(native,multiplier);}
    public static (int Down,int Up) Shift(int down,int up,int maximum,int nativeMinimum,int gap,int customLimit)
    {
        int high=maximum/100*100-100,low=(int)Math.Ceiling(nativeMinimum/100.0)*100;
        if(gap<=0||low<0||high<low+gap)throw new ArgumentException("Native shift range is too small.");
        // Preserve every already-valid player choice, including a deliberate limiter override.
        if(up>high)
        {
            int upper=high;
            if(customLimit>0&&customLimit-50>=low+gap)upper=Math.Min(upper,customLimit-50);
            up=upper;
        }
        if(up<low+gap)up=low+gap;
        down=Math.Clamp(down,low,up-gap);
        return(down,up);
    }
}
