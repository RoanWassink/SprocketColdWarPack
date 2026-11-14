using SprocketExpandedBallistics;
internal static class LongRodTests
{
 internal static void Run(Action<bool,string> check)
 {
  var reference=LongRodPenetration.EnvelopeMm(1.25,17500,1986);
  check(Math.Abs(reference-1246.613)<.02,"independent AT reference1250mm1986m/s");
  check(Math.Abs(LongRodPenetration.EnvelopeMm(.625,17500,1650)-519.607)<.02,"independent reference625mm1650m/s");
  check(Math.Abs(reference-LongRodPenetration.EnvelopeMm(1.25,17500,1986,8192))<.01,"log remaining-length integration converges");
  check(Math.Abs(reference/2-LongRodPenetration.EnvelopeMm(.625,17500,1986))<1e-6,"physical length scales envelope linearly");
  check(LongRodPenetration.EnvelopeMm(1.25,25000,1986)>reference,"higher density increases finite rod reach");
  var high=ShellBallistics.Calculate(125,1986,1800,new DartSettings(LengthInCalibres:10,PenetrationQuality:4,MaxVelocityFactor:1));
  var bounded=LongRodPenetration.Bound(high,17500,high.Velocity);
  check(LongRodPenetration.NativeBudgetMm(bounded,bounded.Velocity)<=reference+.001,"native float budget cannot exceed physical envelope");
  check(bounded with {PenetratorConstant=high.PenetratorConstant}==high,"bound preserves speed geometry mass and damage");
  var low=high with {PenetratorConstant=65000};
  check(LongRodPenetration.Bound(low,17500,low.Velocity)==low,"weaker existing native budget not strengthened");
  bool rejected=false;try{LongRodPenetration.EnvelopeMm(1.25,17500,200);}catch(InvalidOperationException){rejected=true;}
  check(rejected,"unsupported low speed is explicit rather than fake zero penetration");
  for(var length=1;length<=10;length++)for(var quality=1;quality<=4;quality++)
  {
   var dart=ShellBallistics.Calculate(125,1986,1800,new DartSettings(LengthInCalibres:length,PenetrationQuality:quality,MaxVelocityFactor:1));
   var b=LongRodPenetration.Bound(dart,17500,dart.Velocity);
   check(LongRodPenetration.NativeBudgetMm(b,b.Velocity)<=LongRodPenetration.EnvelopeMm(b.Length,17500,b.Velocity),"length/quality sweep always bounded");
  }
  var storage=ShellStorageGeometry.Calculate(125,500,new DartSettings(LengthInCalibres:10));
  check(storage.ProjectileLengthMm==1250 && storage.ProjectileLengthMm>860,"1250mm rod exceeds860mm carousel physical limit");
  check(storage.CombinedLengthMm==1750 && storage.PropellantLengthMm==500,"complete round includes unchanged native charge");
  check(storage.DiameterMm==125,"packing uses full sabot/bore diameter");
  check(ShellStorageGeometry.Calculate(125,500,new DartSettings(LengthInCalibres:2)).ProjectileLengthMm==375,"short custom shell preserves native minimum packaging");
 }
}
