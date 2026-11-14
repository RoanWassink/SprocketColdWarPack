using System.Numerics;
namespace SprocketExpandedBallistics;

// Native .56.2 successful-perforation continuation only. Never a ricochet override.
internal static class OriginalTrajectoryPolicy
{
    internal static bool Behavior(string? behavior)=>behavior is "heat" or "apfsds";
    internal static bool Eligible(ushort parentFlags,ushort childFlags,short parentIndex,short expectedParent,
        byte parentProfile,byte childProfile,float parentMass,float childMass,float parentDiameter,float childDiameter,
        float speed,Vector3 incoming,Vector3 outgoing,Vector3 spawn)
    {
        const ushort original=1,killed=8,deflected=16,embedded=32,penetrated=64,continuation=0x180;
        if((parentFlags&(original|penetrated))!=(original|penetrated)||(parentFlags&(killed|deflected|embedded))!=0||
            (childFlags&(original|continuation))!=(original|continuation)||(childFlags&(killed|deflected|embedded))!=0||
            parentIndex!=expectedParent||parentProfile!=childProfile)return false;
        return float.IsFinite(parentMass)&&parentMass>0&&parentMass==childMass&&
            float.IsFinite(parentDiameter)&&parentDiameter>0&&parentDiameter==childDiameter&&
            float.IsFinite(speed)&&speed>0&&Finite(spawn)&&Unit(incoming)&&Unit(outgoing);
    }
    internal static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Unit(Vector3 v)=>Finite(v)&&v.LengthSquared()>.99f&&v.LengthSquared()<1.01f;
}
