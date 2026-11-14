namespace SprocketExpandedBallistics;

// Session-only ownership of native camera limits. Settings reloads and another
// camera owner may replace either value independently; never restore over them.
internal sealed class AtgmSightElevationState
{
    internal const float Minimum=-60,Maximum=60;
    private bool written;
    private float previousMin,previousMax,writtenMin,writtenMax;
    internal void BeforeWrite(float currentMin,float currentMax,float targetMin=Minimum,float targetMax=Maximum)
    {
        if(!written||currentMin!=writtenMin)previousMin=currentMin;
        if(!written||currentMax!=writtenMax)previousMax=currentMax;
        written=true;writtenMin=targetMin;writtenMax=targetMax;
    }
    internal float RestoreMin(float current)=>written&&current==writtenMin?previousMin:current;
    internal float RestoreMax(float current)=>written&&current==writtenMax?previousMax:current;
}
