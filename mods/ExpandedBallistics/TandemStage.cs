namespace SprocketExpandedBallistics;
internal enum TandemPhase {Ready,Simulating,Applying,Completed,Failed}
// Native simulation itself can consume ERA. Commit starts BEFORE entering that call.
internal sealed class TandemStage
{
 internal TandemPhase Phase {get;private set;}
 internal bool Committed {get;private set;}
 internal bool CanFallback=>!Committed;
 internal bool BeginSimulation(){if(Phase!=TandemPhase.Ready)return false;Committed=true;Phase=TandemPhase.Simulating;return true;}
 internal bool BeginApply(){if(Phase!=TandemPhase.Simulating)return false;Phase=TandemPhase.Applying;return true;}
 internal void Complete(){if(Phase is not(TandemPhase.Simulating or TandemPhase.Applying))throw new InvalidOperationException("Invalid stage completion.");Phase=TandemPhase.Completed;}
 internal void Fail()=>Phase=TandemPhase.Failed;
 internal static double Budget(double referencePen,double calibreMm,double referenceCalibreMm)=>
  double.IsFinite(referencePen)&&referencePen>0&&double.IsFinite(calibreMm)&&calibreMm>0&&double.IsFinite(referenceCalibreMm)&&referenceCalibreMm>0?Math.Clamp(referencePen*calibreMm/referenceCalibreMm,1,2000):0;
}
