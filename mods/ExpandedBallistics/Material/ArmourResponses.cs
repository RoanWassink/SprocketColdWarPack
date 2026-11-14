namespace SprocketExpandedBallistics.Materials;
internal static class ArmourResponses
{
    internal static ArmourResponseCatalogue Parse(string json)=>SprocketExpandedBallistics.ArmourResponses.Parse(json);
    internal static bool PassiveMatches(ArmourResponse response,float density,float rha,float spall)=>SprocketExpandedBallistics.ArmourResponses.PassiveMatches(response,density,rha,spall);
    internal static double ArealMass(double density,double thicknessMm)=>SprocketExpandedBallistics.ArmourResponses.ArealMass(density,thicknessMm);
    internal static float RequestedPrice(float requested,ArmourResponse? response)=>response==null?requested:Math.Max(requested,(float)response.PassiveMaterial.RequestedCostMultiplier);
}
