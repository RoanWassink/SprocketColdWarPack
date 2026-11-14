using System.Text.Json;

namespace SprocketExpandedBallistics;

internal sealed record ResponseMaterial(double Density, double RhaFactor, double SpallFactor, double RequestedCostMultiplier);
internal sealed record ResponseCalibration(double HeatRetention, double IntactRodRetention, double DisturbedRodRetention, double DisturbanceContribution);
internal sealed record CurvePoint(double Input, double Weight);
internal sealed record ResponseGeometry(string Mode, double MinNormalThicknessMm, double MaxNormalThicknessMm,
    double MinMeasuredGapMm, double MaxMeasuredGapMm, IReadOnlyList<CurvePoint> AngularCurve, IReadOnlyList<CurvePoint> MeasuredGapCurve,
    IReadOnlyList<CurvePoint>? KineticAngularCurve=null);
internal sealed record EraRodInteractionSettings(string Mechanism,double ReserveFraction,double YawDegrees,double BreakupFraction,double ErosionShare);
// 0.22.12: how much of the shared heavy-ERA rod recipe a threat bucket pays.
// Missing config keys keep the code default; the live responses file may
// override any bucket without touching the others.
internal sealed record EraThreatScale(double ChemicalJet,double LongRod,double ShortRod,double KineticFullBore,double Hesh,double He)
{
    internal static EraThreatScale DefaultFor(string kind)=>kind=="heavyEra"
        ?new(1,1,.5,0,0,0)
        :new(1,0,0,0,0,0);
    internal double For(string threat)=>threat switch
    {
        "chemicalJet"=>ChemicalJet,"longRod"=>LongRod,"shortRod"=>ShortRod,
        "kineticFullBore"=>KineticFullBore,"hesh"=>Hesh,"he"=>He,_=>1
    };
}
internal sealed record ArmourResponse(string ResponseId, string Kind, IReadOnlyList<string> CompatibleMaterialIds,
    DateTime MinimumDate, string HistoricalDate, string Status, ResponseMaterial PassiveMaterial,
    ResponseCalibration Calibration, ResponseGeometry Geometry, double CellPitchM,IReadOnlyList<string>? EligibleThreatClasses=null,EraStackSettings? Stack=null,
    EraRodInteractionSettings? RodInteraction=null,EraThreatScale? ThreatScale=null);
internal sealed record ArmourResponseCatalogue(bool Enabled, double MaximumAdditionalKineticLoss,
    double MaximumAdditionalHeatLoss, IReadOnlyList<ArmourResponse> Responses, RodPreconditioning Preconditioning,
    IReadOnlyDictionary<string,ArmourResponse>? ByResponseId=null,
    IReadOnlyDictionary<string,ArmourResponse>? ByMaterialId=null,double MinimumTriggerCalibreMm=20)
{
    // Built once by Parse. The loop is only a defensive fallback for manually
    // constructed catalogues; impact code never scans the list.
    internal ArmourResponse? ForResponse(string id)
    {
        if(ByResponseId!=null&&ByResponseId.TryGetValue(id,out var found))return found;
        for(var i=0;i<Responses.Count;i++)if(Responses[i].ResponseId==id)return Responses[i];
        return null;
    }
    internal ArmourResponse? ForMaterial(string material)
    {
        if(ByMaterialId!=null&&ByMaterialId.TryGetValue(material,out var found))return found;
        for(var i=0;i<Responses.Count;i++)if(Responses[i].CompatibleMaterialIds.Contains(material))return Responses[i];
        return null;
    }
}
internal sealed record RodPreconditioning(IReadOnlyList<string> SteelIds,double MinimumThickness,double MinimumAngle,double MaximumAngle,double FullAngle,double FullThickness,double PerLayerCap,double TotalCap);

// Static configuration only. No shell state, impact hooks, cell ledger or effects.
internal static class ArmourResponses
{
    // One-shot compatibility marker for the caller's log. The parser itself is
    // pure so the shared test host can still link it.
    internal static bool LegacyMinimumEraUpgraded { get; private set; }
    internal static void ClearLegacyMinimumEraFlag() => LegacyMinimumEraUpgraded = false;
    internal static bool PassiveMatches(ArmourResponse response,float density,float rha,float spall)
    {
        static bool Near(float actual,double expected)=>float.IsFinite(actual)&&Math.Abs(actual-expected)<=Math.Max(1e-7,Math.Abs(expected)*1e-6);
        return Near(density,response.PassiveMaterial.Density)&&Near(rha,response.PassiveMaterial.RhaFactor)&&Near(spall,response.PassiveMaterial.SpallFactor);
    }
    internal static ArmourResponseCatalogue Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rootFields=new List<string>{"schemaVersion","enabled","maximumAdditionalKineticLoss","maximumAdditionalHeatLoss","preconditioning","responses"};
        if(root.TryGetProperty("minimumTriggerCalibreMm",out _))rootFields.Add("minimumTriggerCalibreMm");
        Fields(root, rootFields.ToArray());
        var schemaVersion=root.GetProperty("schemaVersion").GetInt32();
        if(schemaVersion is not (1 or 2))throw new FormatException("Unsupported response schema.");
        var kinetic = Number(root, "maximumAdditionalKineticLoss", 0, schemaVersion==2?.95:.35);
        var heat = Number(root, "maximumAdditionalHeatLoss", 0, schemaVersion==2?.95:.6);
        var minimumTriggerCalibre=root.TryGetProperty("minimumTriggerCalibreMm",out _)?Number(root,"minimumTriggerCalibreMm",.1,1000):20;
        var pre = root.GetProperty("preconditioning");
        Fields(pre, "steelMaterialIds", "minSteelThicknessMm", "minObliquityDegrees", "maxObliquityDegrees",
            "angleFullDegrees", "thicknessFullMm", "disturbancePerLayerCap", "cumulativeDisturbanceCap");
        _ = Strings(pre.GetProperty("steelMaterialIds"));
        _ = Number(pre, "minSteelThicknessMm", 3, 1000);
        var angleMin = Number(pre, "minObliquityDegrees", 0, 89);
        var angleMax = Number(pre, "maxObliquityDegrees", angleMin, 89);
        _ = Number(pre, "angleFullDegrees", angleMin, angleMax);
        _ = Number(pre, "thicknessFullMm", 3, 1000);
        var perLayer = Number(pre, "disturbancePerLayerCap", 0, .35);
        _ = Number(pre, "cumulativeDisturbanceCap", perLayer, .6);
        var entries = root.GetProperty("responses");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() is < 1 or > 64)
            throw new FormatException("Expected 1 to 64 armour responses.");
        var result = new List<ArmourResponse>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var materials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in entries.EnumerateArray())
        {
            var legacyMinimumEra = item.TryGetProperty("minimumEra", out var legacyEraNode);
            var expectedFields = new List<string>{"responseId", "kind", "compatibleMaterialIds",
                legacyMinimumEra ? "minimumEra" : "minimumDate", "historicalDate", "status",
                "passiveMaterial", "calibration", "geometry", "era"};
            if(item.TryGetProperty("threatScale",out _))expectedFields.Add("threatScale");
            Fields(item, expectedFields.ToArray());
            IReadOnlyList<string>? eligibleClasses=null;
            if(item.TryGetProperty("eligibleThreatClasses",out var classesJson))
            {
                var classes=Strings(classesJson);
                if(classes.Any(c=>c!="unitaryAntitankGrenade"))throw new FormatException("Unsupported eligibleThreatClasses.");
                eligibleClasses=Array.AsReadOnly(classes.ToArray());
            }

            var id = Text(item, "responseId");
            if(id=="nizhLmInsert"&&eligibleClasses==null)throw new FormatException("nizhLmInsert requires eligibleThreatClasses.");
            if (!ValidId(id) || !ids.Add(id)) throw new FormatException("Invalid/duplicate responseId.");
            var kind = Text(item, "kind");
            if (kind is not ("glassTextolite" or "nera" or "lightEra" or "heavyEra" or "passiveComposite")) throw new FormatException("Unknown response kind.");
            EraThreatScale? threatScale=null;
            if(kind is "lightEra" or "heavyEra")
            {
                threatScale=EraThreatScale.DefaultFor(kind);
                if(item.TryGetProperty("threatScale",out var scaleJson))threatScale=ParseThreatScale(scaleJson,threatScale);
            }
            else if(item.TryGetProperty("threatScale",out _))throw new FormatException("threatScale is only valid on ERA responses.");
            var compatible = Strings(item.GetProperty("compatibleMaterialIds"));
            foreach (var material in compatible)
                if (!materials.Add(material) || material is "rha" or "sheetMetal") throw new FormatException("Duplicate or protected material binding.");
            DateTime minimumDate;
            if (legacyMinimumEra)
            {
                // 0.22.8 and older stored a literal era name. It maps to that
                // era's real start; the caller logs the one-time upgrade.
                var legacy = legacyEraNode.ValueKind == JsonValueKind.String ? legacyEraNode.GetString() ?? "" : "";
                if (!string.Equals(legacy.Trim(), "coldwar", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Unsupported legacy minimumEra; use minimumDate.");
                minimumDate = new DateTime(1945, 9, 3);
                LegacyMinimumEraUpgraded = true;
            }
            else
            {
                var minimum = Text(item, "minimumDate");
                if (!DateTime.TryParseExact(minimum, "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out minimumDate)) throw new FormatException("Invalid minimum date.");
            }
            var historical = Text(item, "historicalDate");
            if (!DateTime.TryParseExact(historical, "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _)) throw new FormatException("Invalid metadata date.");
            if (Text(item, "status") != "candidate") throw new FormatException("Unsupported response status.");
            var passive = item.GetProperty("passiveMaterial");
            Fields(passive, "density", "rhaFactor", "spallFactor", "requestedCostMultiplier");
            var physical = new ResponseMaterial(Number(passive, "density", 100, 25000), Number(passive, "rhaFactor", .001, 5),
                Number(passive, "spallFactor", 0, 1), Number(passive, "requestedCostMultiplier", 0, 1e12));
            if(!SprocketExpandedBallistics.Materials.MaterialBalance.Calculate((float)physical.RhaFactor,(float)physical.Density,(float)physical.RequestedCostMultiplier).IsValid)
                throw new FormatException("Unsupported native material price or physical values: "+id);
            // Price/mass enforcement belongs to the materials module.
            var cal = item.GetProperty("calibration");
            Fields(cal, "heatRetention", "intactRodRetention", "disturbedRodRetention", "disturbanceContribution");
            var calibration = new ResponseCalibration(Number(cal, "heatRetention", 0, 1),
                Number(cal, "intactRodRetention", 1-kinetic, 1), Number(cal, "disturbedRodRetention", 1-kinetic, 1),
                Number(cal, "disturbanceContribution", 0, .35));
            var geometry = item.GetProperty("geometry");
            var geometryFields=new[]{"mode", "minNormalThicknessMm", "maxNormalThicknessMm", "minMeasuredGapMm", "maxMeasuredGapMm", "angularCurve", "measuredGapCurve"};
            Fields(geometry,kind=="heavyEra"?geometryFields.Append("kineticAngularCurve").ToArray():geometryFields);
            var mode = Text(geometry, "mode");
            if (mode is not ("declaredCassette" or "resolvedLayers")) throw new FormatException("Unknown geometry mode.");
            var min = Number(geometry, "minNormalThicknessMm", .1, 1000);
            var max = Number(geometry, "maxNormalThicknessMm", min, 1000);
            var minGap = Number(geometry, "minMeasuredGapMm", 0, 1000);
            var maxGap = Number(geometry, "maxMeasuredGapMm", minGap, 1000);
            var angular = Curve(geometry.GetProperty("angularCurve"), "angleDegrees", 90, false);
            var kineticAngular=kind=="heavyEra"?Curve(geometry.GetProperty("kineticAngularCurve"),"angleDegrees",90,false):null;
            var gap = Curve(geometry.GetProperty("measuredGapCurve"), "distanceMm", 1000, mode == "declaredCassette");
            var era = item.GetProperty("era");
            Fields(era, "cellPitchM");
            var pitch = Number(era, "cellPitchM", 0, 10);
            if (kind is "lightEra" or "heavyEra" ? pitch < .01 : pitch != 0)
                throw new FormatException("Invalid ERA cell pitch.");
            EraStackSettings? stack=null;
            if(era.TryGetProperty("stack",out var stackJson))
            {
                if(schemaVersion!=2||kind is not ("lightEra" or "heavyEra")||mode!="declaredCassette")
                    throw new FormatException("Stack settings require schema2 declared ERA.");
                Fields(stackJson,"enabled","unitNormalThicknessMm","thicknessToleranceMm","maximumStages");
                var unit=Number(stackJson,"unitNormalThicknessMm",.1,1000);
                var tolerance=Number(stackJson,"thicknessToleranceMm",0,Math.Min(5,unit*.05));
                var stages=stackJson.GetProperty("maximumStages").GetInt32();
                if(stages is <1 or >16)throw new FormatException("Invalid maximum ERA stages.");
                stack=new(stackJson.GetProperty("enabled").GetBoolean(),unit,tolerance,stages);
                if(unit<min||unit>max)throw new FormatException("Stack unit must satisfy the configured normal-thickness gate.");
            }
            EraRodInteractionSettings? rodInteraction=null;
            if(item.TryGetProperty("rodInteraction",out var rodJson))
            {
                if(schemaVersion!=2||kind!="heavyEra"||mode!="declaredCassette")
                    throw new FormatException("Rod interaction requires schema2 declared heavy ERA.");
                Fields(rodJson,"mechanism","reserveFraction","yawDegrees","breakupFraction","erosionShare");
                var mechanism=Text(rodJson,"mechanism");
                if(mechanism is not ("movingPlate" or "linearCharge"))throw new FormatException("Unknown ERA rod mechanism.");
                var breakup=Number(rodJson,"breakupFraction",0,1);
                if(mechanism=="movingPlate"&&breakup!=0)throw new FormatException("Moving-plate ERA cannot declare linear-charge cutting.");
                rodInteraction=new(mechanism,Number(rodJson,"reserveFraction",0,1),Number(rodJson,"yawDegrees",0,30),breakup,
                    Number(rodJson,"erosionShare",0,1));
            }
            result.Add(new(id, kind, compatible, minimumDate, historical, "candidate", physical, calibration,
                new(mode, min, max, minGap, maxGap, angular, gap,kineticAngular), pitch,eligibleClasses,stack,rodInteraction,threatScale));
        }
        var byResponseId=new Dictionary<string,ArmourResponse>(StringComparer.Ordinal);
        var byMaterialId=new Dictionary<string,ArmourResponse>(StringComparer.Ordinal);
        foreach(var response in result)
        {
            byResponseId[response.ResponseId]=response;
            foreach(var material in response.CompatibleMaterialIds)byMaterialId.TryAdd(material,response); // first match wins, as FirstOrDefault did
        }
        return new(root.GetProperty("enabled").GetBoolean(), kinetic, heat, result, new(Strings(pre.GetProperty("steelMaterialIds")),
            Number(pre,"minSteelThicknessMm",3,1000),angleMin,angleMax,
            Number(pre,"angleFullDegrees",angleMin+.001,angleMax),Number(pre,"thicknessFullMm",3,1000),perLayer,
            Number(pre,"cumulativeDisturbanceCap",perLayer,.6)),byResponseId,byMaterialId,minimumTriggerCalibre);
    }

    private static EraThreatScale ParseThreatScale(JsonElement node,EraThreatScale defaults)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new FormatException("threatScale must be an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var scale=defaults;
        foreach(var property in node.EnumerateObject())
        {
            if(property.Name is not ("chemicalJet" or "longRod" or "shortRod" or "kineticFullBore" or "hesh" or "he")||!seen.Add(property.Name))
                throw new FormatException("Unknown or duplicate threatScale bucket: "+property.Name);
            if(!property.Value.TryGetDouble(out var value)||!double.IsFinite(value)||value<0||value>1)
                throw new FormatException("threatScale is outside 0-1: "+property.Name);
            scale=property.Name switch
            {
                "chemicalJet"=>scale with{ChemicalJet=value},"longRod"=>scale with{LongRod=value},
                "shortRod"=>scale with{ShortRod=value},"kineticFullBore"=>scale with{KineticFullBore=value},
                "hesh"=>scale with{Hesh=value},"he"=>scale with{He=value},_=>scale
            };
        }
        return scale;
    }
    private static IReadOnlyList<CurvePoint> Curve(JsonElement array, string field, double max, bool allowEmpty)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 16 ||
            array.GetArrayLength() < (allowEmpty ? 0 : 2)) throw new FormatException("Invalid response curve.");
        var points = new List<CurvePoint>();
        foreach (var point in array.EnumerateArray())
        {
            Fields(point, field, "weight");
            var value = Number(point, field, 0, max);
            if (points.Count > 0 && value <= points[^1].Input) throw new FormatException("Unordered response curve.");
            points.Add(new(value, Number(point, "weight", 0, 1)));
        }
        return points;
    }
    private static IReadOnlyList<string> Strings(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 64) throw new FormatException("Invalid material list.");
        var result = new List<string>();
        foreach (var element in array.EnumerateArray())
        {
            var value = element.GetString() ?? "";
            if (!ValidId(value) || result.Contains(value)) throw new FormatException("Invalid material ID.");
            result.Add(value);
        }
        return result;
    }
    private static bool ValidId(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => c <= 127 && (char.IsLetterOrDigit(c) || c is '_' or '-'));
    private static string Text(JsonElement item, string name)
    {
        var value = item.GetProperty(name).GetString() ?? "";
        if (value.Length is < 1 or > 80 || value.Any(char.IsControl)) throw new FormatException("Invalid response text.");
        return value;
    }
    private static double Number(JsonElement item, string name, double min, double max)
    {
        var value = item.GetProperty(name).GetDouble();
        if (!double.IsFinite(value) || value < min || value > max) throw new FormatException("Out-of-range response value: " + name);
        return value;
    }
    private static void Fields(JsonElement item, params string[] expected)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new FormatException("Expected response object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in item.EnumerateObject())
            if ((!expected.Contains(field.Name) && !(expected.Contains("responseId") && field.Name is "eligibleThreatClasses" or "rodInteraction") && !(expected.Contains("cellPitchM") && field.Name=="stack")) || !seen.Add(field.Name)) throw new FormatException("Unknown/duplicate response field: " + field.Name);
        if (expected.Any(name=>!seen.Contains(name))) throw new FormatException("Missing response fields.");
    }

    internal static double ArealMass(double density, double thicknessMm)
    {
        if (!double.IsFinite(density) || density <= 0 || !double.IsFinite(thicknessMm) || thicknessMm < 0) throw new ArgumentOutOfRangeException();
        return density * thicknessMm * .001;
    }
}
