using BepInEx;
using Sprocket.TechTrees;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles;

namespace SprocketExpandedBallistics.Materials;

internal static class ResponseUi
{
    internal static ArmourResponseCatalogue? Catalogue;
    internal static TooltipPreconditioning? TooltipPreconditioningData;
    private static readonly HashSet<string> warnings = new();
    private static readonly HashSet<string> CandidateIds = new(StringComparer.Ordinal)
    { "cwepGlassTextolite", "cwepNeraCassette", "cwepLightEraCassette", "cwepPassiveComposite", "cwepHeavyEraCassette" };

    internal static void Reload()
    {
        Catalogue = null;
        TooltipPreconditioningData = null;
        var snapshot=MaterialFeature.ArmourSnapshot;
        ExperimentalArmourUi.Reload(snapshot);
        Catalogue=snapshot.Catalogue;
        if(Catalogue!=null)
        {
            if(snapshot.ResponseJson!=null)TooltipPreconditioningData=TooltipPreconditioning.FromValidatedCatalogue(snapshot.ResponseJson);
            MaterialFeature.ModLog.LogInfo($"Armour responses: {Catalogue.Responses.Count}; shared impact/UI snapshot, enabled={Catalogue.Enabled}.");
        }
        else if(snapshot.Error!=null&&warnings.Add(snapshot.Error))
            MaterialFeature.ModLog.LogWarning("Shared armour catalogue unavailable: "+snapshot.Error);
    }

    private static bool ShellConsumerLoaded()=>MaterialFeature.ShellReady;

    internal static string Tooltip(PlateStructure plate, string materialId, string label)
    {
        if(Reaction(plate,materialId) is {} role)
            return label+"\n"+string.Join("\n",MaterialReactionPresentation.Summary(role,MaterialFeature.ArmourSnapshot.MaterialReactions!.Enabled&&ShellConsumerLoaded()));
        var response = Find(materialId);
        var compatible = response == null || ArmourResponses.PassiveMatches(response, plate.armourDensity,
            plate.damageModelParameters.RhaFactor, plate.damageModelParameters.SpallFactor);
        if(response is {Kind:"lightEra" or "heavyEra"} && Catalogue is {} catalogue)
        {
            var experimental=ExperimentalArmourUi.Catalogue;
            var dynamic=SprocketExpandedBallistics.ExperimentalRodModel.UsesDynamic(response,experimental);
            SprocketExpandedBallistics.ExperimentalArmour.ExperimentalRodResponse? rod=null;
            if(dynamic)experimental?.Recipes.TryGetValue(response.ResponseId,out rod);
            var enabled=catalogue.Enabled&&ShellConsumerLoaded()&&EraAllowed(plate,materialId)&&compatible&&(!dynamic||experimental?.Enabled==true&&rod!=null
                &&response.Geometry.Mode=="declaredCassette"&&response.Calibration.IntactRodRetention==1&&response.Calibration.DisturbedRodRetention==1&&response.Calibration.DisturbanceContribution==0);
            var kLoss=response.Kind=="lightEra"?0:rod!=null?Math.Min(rod.MaximumLoss,rod.BaseLoss*Math.Clamp(1,rod.MinimumSusceptibility,rod.MaximumSusceptibility)):1-response.Calibration.IntactRodRetention;
            var kCap=rod!=null?experimental!.EffectiveKineticCap(catalogue.MaximumAdditionalKineticLoss):catalogue.MaximumAdditionalKineticLoss;
            EraTooltip.Point[] Points(IReadOnlyList<CurvePoint> points)=>points.Select(p=>new EraTooltip.Point(p.Input,p.Weight)).ToArray();
            return EraTooltip.Build(label,null,1,Points(response.Geometry.KineticAngularCurve??Array.Empty<CurvePoint>()),Points(response.Geometry.AngularCurve),
                kLoss,1-response.Calibration.HeatRetention,kCap,catalogue.MaximumAdditionalHeatLoss,rod?.ReferenceLD,
                EraTooltip.Application(response.ResponseId),enabled,response.EligibleThreatClasses?.SequenceEqual(new[]{"unitaryAntitankGrenade"})==true);
        }
        return SimpleMaterialDescription(materialId)+MaterialTooltip.Build(materialId, label,
            new(plate.damageModelParameters.RhaFactor, plate.armourDensity,
                plate.damageModelParameters.SpallFactor, plate.armourCostMultiplier),
            Catalogue, TooltipPreconditioningData, ShellConsumerLoaded(), EraAllowed(plate, materialId), compatible)+ExperimentalArmourUi.Description(materialId);
    }

    private static string SimpleMaterialDescription(string id)=>id switch
    {
        "ebWood"=>"General solid wood. Density depends on species and moisture; the scalar resistance and splinter contribution are adjustable starting values rather than measurements.\n",
        "ebRubber"=>"Unreinforced rubber. No reactive sandwich effect is assigned; density, scalar resistance and fragment contribution are adjustable starting values rather than measurements.\n",
        "ebFabric"=>"Woven canvas with an effective bulk density including pores. Density, scalar resistance and fragment contribution are adjustable starting values rather than measurements.\n",
        "ebGlass"=>"Monolithic soda-lime glass with brittle fragmentation. Density, scalar resistance and fragment contribution are adjustable starting values rather than measurements.\n",
        _=>""
    };
    private static SprocketExpandedBallistics.MaterialReactionRecipe? Reaction(PlateStructure plate,string id)
    {
        var catalogue=MaterialFeature.ArmourSnapshot.MaterialReactions;
        if(catalogue==null||!catalogue.Bindings.TryGetValue(id,out var binding))return null;
        return SprocketExpandedBallistics.MaterialReactionModel.PassiveMatches(binding,plate.armourDensity,plate.damageModelParameters.RhaFactor,plate.damageModelParameters.SpallFactor)?catalogue.Recipes[binding.RecipeId]:null;
    }
    internal static ArmourResponse? Find(string id) => Catalogue?.Responses.FirstOrDefault(r => r.CompatibleMaterialIds.Contains(id));
    private sealed record Context(DateTime? Design, string DesignDate, string? NativeEra, string TechDate, bool HasTech, bool HasKnownVehicleContext);
    private static readonly HashSet<string> availabilitySnapshots = new(StringComparer.Ordinal);
    private static Context ReadContext(PlateStructure plate)
    {
        var designDate = "unavailable";
        DateTime? designValue = null;
        string? eraName = null;
        var techDate = "unavailable";
        var hasTech = false;
        var hasContext = false;
        try
        {
            var vehicle = plate.Vehicle;
            var design = vehicle?.DesignInfo;
            if (design != null)
            {
                var date = design.Date;
                if (!MaterialAvailability.TryCalendarDate(date.Year, date.Month, date.Day, out var parsedDesign))
                    throw new InvalidOperationException("Invalid native vehicle date.");
                designDate = date.ToString();
                designValue = parsedDesign;
                hasContext = true;
                try { eraName = VehicleClassifications.GetEra(date)?.Name; }
                catch (Exception ex) { if (warnings.Add("era-name-read")) MaterialFeature.ModLog.LogWarning("Armour diagnostic era name unavailable: " + ex.Message); }
            }
            var tech = vehicle?.Tech;
            if (tech != null)
            {
                var date = tech.Date;
                if (!MaterialAvailability.TryCalendarDate(date.Year, date.Month, date.Day, out _))
                    throw new InvalidOperationException("Invalid native technology frame date.");
                techDate = date.ToString();
                hasTech = true;
            }
        }
        catch (Exception ex)
        {
            designValue = null;
            hasContext = false;
            hasTech = false;
            if (warnings.Add("context-read")) MaterialFeature.ModLog.LogWarning("Armour native technology context unavailable: " + ex.Message);
        }
        return new(designValue, designDate, eraName, techDate, hasTech, hasContext);
    }

    internal static string NativeDensityLabel(PlateStructure plate, string materialId)
    {
        try
        {
            if (plate.Vehicle?.Tech?.TryGetTech(materialId, out var tech) == true && tech != null)
            {
                var density = tech.GetFloat("density", float.NaN);
                if (float.IsFinite(density) && density > 0) return density.ToString("0") + " kg/m\u00B3";
            }
        }
        catch (Exception ex) { if (warnings.Add("recipe-read")) MaterialFeature.ModLog.LogWarning("Armour native recipe display unavailable: " + ex.Message); }
        return "native recipe unavailable";
    }

    internal static float? NativeRequestedCost(PlateStructure plate, string materialId)
    {
        try
        {
            if (plate.Vehicle?.Tech?.TryGetTech(materialId, out var tech) == true && tech != null)
            {
                var cost = tech.GetFloat("costMultiplier", float.NaN);
                if (float.IsFinite(cost) && cost >= 0) return cost;
            }
        }
        catch (Exception ex) { if (warnings.Add("recipe-read")) MaterialFeature.ModLog.LogWarning("Armour native recipe display unavailable: " + ex.Message); }
        return null;
    }

    internal static bool EraAllowed(PlateStructure plate, string materialId)
    {
        var context = ReadContext(plate);
        try
        {
            var inFrame = context.HasTech && plate.Vehicle.Tech.TryGetTech(materialId, out _);
            if (MaterialAvailability.Evaluate(context.HasKnownVehicleContext, context.HasTech, inFrame) != MaterialAvailabilityStatus.Available)
                return false;
            var response = Find(materialId);
            // Native/passive materials keep their existing native technology gate.
            if (response == null) return true;
            // Candidate face materials additionally require the owning vehicle's
            // actual design era/date. A scenario-wide technology frame cannot
            // substitute for that restriction.
            return SprocketExpandedBallistics.VehicleEraAccess.AllowsMaterial(
                plate.Vehicle.DesignInfo.Date, response.MinimumDate, context.HasTech, inFrame);
        }
        catch (Exception ex)
        {
            if (warnings.Add("tech-read")) MaterialFeature.ModLog.LogWarning("Armour availability lookup unavailable: " + ex.Message);
            return false;
        }
    }

    internal static void DescribeAvailability(IGUIElementDrawer ui, PlateStructure plate)
    {
        var context = ReadContext(plate);
        var details = SprocketExpandedBallistics.ReleaseDiagnostics.Enabled ? new List<string>() : null;
        var missing = new List<string>();
        foreach (var id in CandidateIds)
        {
            var index = MaterialDatabase.Materials.FindIndex(m => m.Id == id);
            var inFrame = false;
            try { inFrame = context.HasTech && plate.Vehicle.Tech.TryGetTech(id, out _); }
            catch (Exception ex) { if (warnings.Add("tech-read")) MaterialFeature.ModLog.LogWarning("Armour availability lookup unavailable: " + ex.Message); }
            var status = MaterialAvailability.Evaluate(context.HasKnownVehicleContext, context.HasTech, inFrame);
            var response = Find(id);
            var vehicleEra = response == null || status != MaterialAvailabilityStatus.Available
                ? false
                : SprocketExpandedBallistics.VehicleEraAccess.AllowsMaterial(
                    plate.Vehicle.DesignInfo.Date, response.MinimumDate, context.HasTech, inFrame);
            details?.Add($"{id}:catalogIndex={index},nativeTech={inFrame},status={status},vehicleEra={vehicleEra}");
            if (index < 0 || status != MaterialAvailabilityStatus.Available || response != null && !vehicleEra)
                missing.Add(MaterialAvailability.Label(id, id));
        }
        if(details!=null)
        {
            var snapshot = $"plate={plate.Pointer},selected={plate.armourTechID},designDate={context.DesignDate},nativeEra={context.NativeEra ?? "unknown"},techDate={context.TechDate},knownVehicleContext={context.HasKnownVehicleContext},responseEnabled={Catalogue?.Enabled.ToString() ?? "no catalogue"}; " + string.Join("; ", details);
            if (availabilitySnapshots.Count < 64 && availabilitySnapshots.Add(snapshot))
                MaterialFeature.ModLog.LogInfo("[Armour availability] " + snapshot);
        }
        if (missing.Count == 0) return;
        if (!context.HasKnownVehicleContext || !context.HasTech)
            ui.InfoField("Armour choices are currently unavailable.", 1);
        else ui.InfoField("Some armour is unavailable for this era or installation.", 1);
    }

    internal static void Describe(IGUIElementDrawer ui, PlateStructure plate)
    {
        var id = plate.armourTechID;
        if(Reaction(plate,id) is {} role)
        {
            foreach(var line in MaterialReactionPresentation.Summary(role,MaterialFeature.ArmourSnapshot.MaterialReactions!.Enabled&&ShellConsumerLoaded()))ui.InfoField(line,line.Length>55?2:1);
            return;
        }
        var response = Find(id);
        var compatible = response == null || ArmourResponses.PassiveMatches(response, plate.armourDensity,
            plate.damageModelParameters.RhaFactor, plate.damageModelParameters.SpallFactor);
        var summary = MaterialTooltip.Summary(id,
            new(plate.damageModelParameters.RhaFactor, plate.armourDensity,
                plate.damageModelParameters.SpallFactor, plate.armourCostMultiplier),
            Catalogue, TooltipPreconditioningData, ShellConsumerLoaded(), EraAllowed(plate,id), compatible);
        var experimental=MaterialFeature.ArmourSnapshot.Experimental;
        var dynamic=response!=null&&SprocketExpandedBallistics.ExperimentalRodModel.UsesDynamic(response,experimental);
        foreach (var original in summary)
        {
            var line=original;
            if(dynamic&&compatible&&Catalogue?.Enabled==true&&ShellConsumerLoaded()&&EraAllowed(plate,id))
            {
                var description="APFSDS: ERA rod response disabled.";
                if(experimental is {Enabled:true}&&experimental.Recipes.TryGetValue(response!.ResponseId,out var rod))
                    description=$"APFSDS: geometry-dependent ERA response (reference L/D {rod.ReferenceLD:0.##}).";
                line=line.Replace("APFSDS: base armour only.",description);
            }
            ui.InfoField(line, line.Length > 55 ? 2 : 1);
        }
    }
}

