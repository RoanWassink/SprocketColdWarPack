using SprocketExpandedBallistics.ExperimentalArmour;
namespace SprocketExpandedBallistics;
internal static class ExperimentalRodModel
{
 internal static bool Supported(string id)=>SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.IsSupportedResponseId(id);
 internal static bool UsesDynamic(ArmourResponse response,ExperimentalArmourCatalogue? catalogue)=>
  Supported(response.ResponseId)&&(!SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.LegacyMaterialResponseIds.Contains(response.ResponseId,StringComparer.Ordinal)||
   (response.Calibration.IntactRodRetention==1&&response.Calibration.DisturbedRodRetention==1&&response.Calibration.DisturbanceContribution==0&&catalogue is {Enabled:true}&&catalogue.Recipes.ContainsKey(response.ResponseId)));
 internal static double Susceptibility(ExperimentalRodResponse recipe,ImpactGeometry geometry)=>
  geometry.Valid?Math.Clamp(Math.Sqrt(geometry.LengthToDiameter/recipe.ReferenceLD),recipe.MinimumSusceptibility,recipe.MaximumSusceptibility):0;
 internal static double Loss(ExperimentalRodResponse recipe,ImpactGeometry? geometry,double angularWeight)=>
  geometry is {Valid:true}&&double.IsFinite(angularWeight)?Math.Clamp(recipe.BaseLoss*Susceptibility(recipe,geometry)*Math.Clamp(angularWeight,0,1),0,recipe.MaximumLoss):0;
 internal static bool ValidConversion(double incoming,double target,double outgoing,double speed,double originalSpeed)=>
  double.IsFinite(incoming)&&incoming>0&&double.IsFinite(target)&&target>0&&target<incoming&&
  double.IsFinite(outgoing)&&outgoing>0&&outgoing<incoming&&
  double.IsFinite(speed)&&speed>0&&double.IsFinite(originalSpeed)&&speed<originalSpeed&&
  Math.Abs(outgoing-target)<=Math.Max(.001,target*.00001);
 internal static double Next(double old,double loss,double cap)=>Math.Min(old,Math.Max(1-cap,old*(1-loss)));
}
