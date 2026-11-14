namespace SprocketExpandedBallistics;

// Flight geometry belongs to this registered shot; never use a later edited profile.
internal sealed record ImpactGeometry(double LengthM,double CoreDiameterM,double MassKg,double BoreDiameterM)
{
    internal double LengthToDiameter=>LengthM/CoreDiameterM;
    internal double BoreLengthRatio=>LengthM/BoreDiameterM;
    internal bool Valid=>double.IsFinite(LengthM)&&LengthM>0&&double.IsFinite(CoreDiameterM)&&CoreDiameterM>0&&
        double.IsFinite(MassKg)&&MassKg>0&&double.IsFinite(BoreDiameterM)&&BoreDiameterM>0&&double.IsFinite(LengthToDiameter);
}
