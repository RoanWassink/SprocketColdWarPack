using Sprocket.TechTrees;
namespace SprocketStabilization;
internal sealed record StabilizationSettings(float Scale,float MaxStrength);
internal static class StabilizationTechnology
{
    private static bool dateWarned;
    private static DateTime? Date(TechDate value) =>
        !value.Valid ? null :
        value.Year == 0 ? DateTime.MinValue : new DateTime(value.Year, value.Month, value.Day);
    internal static bool TryRead(ITechFrame? frame,out StabilizationSettings settings)
    {
        settings=null!;
        if(frame==null)return false;
        var found=frame.TryGetTech("sprocketStabilization",out var tech);
        if(!found||tech==null||!tech.GetBool("enabled",false))return false;
        // The native frame date is the same gate the picker uses. Do not trust a
        // technology entry in an imported/saved frame that predates its JSON date.
        if (!StabilizationDatePolicy.Allows(Date(frame.Date)))
        {
            if (!dateWarned)
            {
                dateWarned = true;
                var d = frame.Date;
                Plugin.ModLog.LogWarning(
                    $"[Stabilizer] sprocketStabilization needs 1948-01-01; frame date {d.Year:0000}-{d.Month:00}-{d.Day:00} keeps " +
                    "the checkbox saved but inactive.");
            }
            return false;
        }
        var scale=tech.GetFloat("strengthScale",100f);var max=tech.GetFloat("maxElevationStrength",10000f);
        if(!DriveSetupRules.ValidTechnology(scale,max))return false;
        settings=new(scale,max);return true;
    }
}
