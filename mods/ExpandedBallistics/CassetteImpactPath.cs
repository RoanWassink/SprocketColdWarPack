using System.Numerics;
namespace SprocketExpandedBallistics;

// Pure cassette hit facts that remain after 0.22.28: the side of a drawn face (for a plate native skipped), the once-per-line award,
// and the passive resistance native did not charge (a skipped plate, an overlap). The cassette's own protection is the factor table.
internal readonly record struct CassetteFaceHit(CassetteSide Side,int FaceIndex,Vector3 FrontNormal,double Alignment,bool Edge);

internal static class CassetteImpactPath
{
    internal const double SideAlignment = 0.7;
    // One award per penetrator line and plate. Face -1 cannot collide with a
    // real declared-face award, so a front hit followed by a back hit on the
    // same plate still pays only once.
    internal const int AwardFace = -1;
    internal const int AwardCapacity = 16384;

    // 0.22.12: the side follows the shot direction against the authored
    // outward face normal, not against the normal of whichever native block
    // happens to be processed later. A penetrator that opposes the drawn face
    // entered the front; a shot travelling with the outward normal came from
    // behind. The most opposed face wins, so a bent or co-located plate still
    // picks its own entry face.
    internal static CassetteFaceHit ResolveSide(Vector3 shotDirection,IReadOnlyList<Vector3>? faceNormals)
    {
        var direction=Unit(shotDirection);
        if(direction.LengthSquared()<.5f)direction=Vector3.Zero;
        if(faceNormals==null||faceNormals.Count==0)
            return new(CassetteSide.Front,-1,direction.LengthSquared()>.5f?-direction:Vector3.Zero,0,true);
        int frontIndex=-1,backIndex=-1;double minDot=double.PositiveInfinity,maxDot=double.NegativeInfinity;
        var frontNormal=Vector3.Zero;var backNormal=Vector3.Zero;
        for(int i=0;i<faceNormals.Count;i++)
        {
            var normal=Unit(faceNormals[i]);
            if(normal.LengthSquared()<.5f)continue;
            var dot=Vector3.Dot(direction,normal);
            if(!double.IsFinite(dot))continue;
            if(dot<minDot){minDot=dot;frontIndex=i;frontNormal=normal;}
            if(dot>maxDot){maxDot=dot;backIndex=i;backNormal=normal;}
        }
        if(frontIndex<0||backIndex<0)
            return new(CassetteSide.Front,-1,direction.LengthSquared()>.5f?-direction:Vector3.Zero,0,true);
        // dot <= 0 means the face points toward the shooter (or exactly edge
        // on); only a face that points the same way as the shot is a back hit.
        if(minDot<=1e-6)
            return new(CassetteSide.Front,frontIndex,frontNormal,-minDot,-minDot<SideAlignment);
        return new(CassetteSide.Back,backIndex,backNormal,maxDot,maxDot<SideAlignment);
    }

    internal static bool Claim(HashSet<(IntPtr Sim,short Lineage,int Entity,int Face)> awarded,
        IntPtr sim,short root,int entity)
    {
        if(awarded.Count>=AwardCapacity)return false;
        return awarded.Add((sim,root,entity,AwardFace));
    }

    // 0.22.16: a composite plate that native skipped (air block ending on its
    // front face, next solid block another object) or cut short (overlap).
    internal enum AirGapOutcome{None,SamePlate,Skipped,Ended,Claimed}
    internal static AirGapOutcome AirGap(bool pending,bool claimed,bool nextBlock,bool nextHasPlate)=>
        !pending?AirGapOutcome.None:claimed?AirGapOutcome.Claimed:!nextBlock?AirGapOutcome.Ended:
        nextHasPlate?AirGapOutcome.SamePlate:AirGapOutcome.Skipped;
    // Native passive law measured in 0.22.15: P_out = P_in (1 - L/P_in)^0.715.
    internal static double NativePassive(double beforeMm,double resistanceMm)
    {
        if(!double.IsFinite(beforeMm)||beforeMm<=0)return 0;
        if(!double.IsFinite(resistanceMm)||resistanceMm<=0)return beforeMm;
        if(resistanceMm>=beforeMm)return 0;
        return beforeMm*Math.Pow(1-resistanceMm/beforeMm,.715);
    }
    // Remaining plate path after native stopped at the other component, minus what native charged it as that component.
    // 0.22.28: plateRha is the plate's table factor (×RHA per mm of path).
    internal static double OverlapMissingMm(double faceMm,double cosine,double nativeSpanMm,double plateRha,double otherRha)
    {
        if(!(faceMm>0)||!double.IsFinite(nativeSpanMm)||!(plateRha>0))return 0;
        var remaining=OverlapRemainingMm(faceMm,cosine,nativeSpanMm);
        return remaining*Math.Max(0,plateRha-(double.IsFinite(otherRha)?Math.Max(0,otherRha):0));
    }
    // The plate path (mm, along the shot) that the native block did not charge as plate.
    internal static double OverlapRemainingMm(double faceMm,double cosine,double nativeSpanMm)=>
        !(faceMm>0)||!double.IsFinite(faceMm)?0:Math.Max(0,faceMm/Math.Max(.1,Math.Abs(cosine))-(double.IsFinite(nativeSpanMm)?Math.Max(0,nativeSpanMm):0));
    internal static string SkipReason(double gapMm)=>
        "native skipped plate (air gap "+gapMm.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" mm); cassette and passive resistance applied by the mod";
    // 0.22.17 point 6: 'L' is a resistance length in RHA mm, not a penetration loss. The native law turns L into the actual loss.
    internal const string OverlapPrefix="overlapped by ";
    internal static string OverlapReason(string other,double nativeSpanMm,double remainingPathMm,double deltaRha,double missingMm,double lossMm)
    {
        var c=System.Globalization.CultureInfo.InvariantCulture;
        return $"{OverlapPrefix}{other} after {nativeSpanMm.ToString("0",c)} mm: native did not charge {remainingPathMm.ToString("0",c)} mm of plate path; {remainingPathMm.ToString("0",c)} mm × {deltaRha.ToString("0.##",c)} ΔRHA = L {missingMm.ToString("0.#",c)} mm RHA; actual loss −{lossMm.ToString("0.#",c)} mm by the native law";
    }

    private static Vector3 Unit(Vector3 value)
    {
        var length=value.Length();
        if(!float.IsFinite(length)||length<1e-6f)return Vector3.Zero;
        return value/length;
    }
}
