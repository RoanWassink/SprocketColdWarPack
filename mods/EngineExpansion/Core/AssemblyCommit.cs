namespace SprocketEngineExpansion.Core;
// One completed assembly/selection decision shared by runtime torque-table and fuel consumers.
internal sealed record AssemblyCommit(EngineSelection Selection,long Revision,bool Active)
{
    internal bool Matches(EngineSelection selection,long revision,bool active)=>Selection==selection&&Revision==revision&&Active==active;
    internal double Fuel(EnginePlan plan,double rpm)
    {if(plan.Selection!=Selection)throw new InvalidOperationException("Engine plan differs from committed selection.");return plan.FuelCoefficient(rpm,Active);}
}
