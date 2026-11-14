using SprocketExpandedBallistics.ExperimentalArmour;






namespace SprocketExpandedBallistics.Materials;

internal static class ExperimentalArmourUi
{
    internal static ExperimentalArmourCatalogue? Catalogue;
    internal static string? Error;
    internal static void Reload(SprocketExpandedBallistics.ArmourCatalogueSnapshot? sharedSnapshot=null)
    {
        Catalogue=null;Error=null;
        var shared=sharedSnapshot??MaterialFeature.ArmourSnapshot;
        Catalogue=shared.Experimental;Error=shared.ExperimentalError;
    }
    internal static string Description(string materialId)
    {
        if(!SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.IsSupportedResponseId(materialId))return "";
        var legacy=ResponseUi.Find(materialId);
        if(legacy!=null&&SprocketExpandedBallistics.ExperimentalArmour.ExperimentalArmour.LegacyMaterialResponseIds.Contains(legacy.ResponseId,StringComparer.Ordinal)
            &&!SprocketExpandedBallistics.ExperimentalRodModel.UsesDynamic(legacy,Catalogue))return "";
        if(Error!=null)return "\nERA long-rod response disabled: invalid sidecar. Legacy/passive armour retained.";
        if(Catalogue?.Enabled!=true||ResponseUi.Catalogue?.Enabled!=true)return "\nERA long-rod response is off. Enable armour responses to use it.";
        if(!Catalogue.Recipes.TryGetValue(materialId,out var rod))return "\nNo supported rod-response recipe; legacy/passive armour only.";
        if(legacy==null||legacy.ResponseId!=materialId||legacy.Kind!="heavyEra")return "\nERA binding unavailable; use its matching recipe.";
        if(legacy.Geometry.Mode!="declaredCassette")return "\nERA long-rod response unavailable: cassette geometry is required.";
        var material=MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==materialId);
        if(material==null||!ArmourResponses.PassiveMatches(legacy,material.Density,material.RhaFactor,material.SpallFactor)||legacy.Calibration.IntactRodRetention!=1||legacy.Calibration.DisturbedRodRetention!=1||legacy.Calibration.DisturbanceContribution!=0)
            return "\nERA long-rod response unavailable: matching passive properties and neutral base KE (1/1/0) are required.";
        return $"\nAPFSDS response: L/D reference {rod.ReferenceLD:0.##}; base loss {rod.BaseLoss:P0}, local maximum {rod.MaximumLoss:P0}; shared KE cap {Catalogue.EffectiveKineticCap(ResponseUi.Catalogue!.MaximumAdditionalKineticLoss):P0}. Uses launched rod geometry and eligible angle. Single reaction replaces neutral base KE. Tandem outcome depends on the precursor and remaining cells.";
    }
}
