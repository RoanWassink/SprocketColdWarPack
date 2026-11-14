using System.Globalization;
using System.Text.Json;

namespace SprocketExpandedBallistics.Materials;

// Presentation only. These values never change the material or the shell consumer.
internal sealed record TooltipPreconditioning(IReadOnlyList<string> MaterialIds,
    double MinimumThickness, double MinimumAngle, double MaximumAngle,
    double FullAngle, double FullThickness, double PerLayerCap, double TotalCap)
{
    internal static TooltipPreconditioning FromValidatedCatalogue(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement.GetProperty("preconditioning");
        double N(string name) => p.GetProperty(name).GetDouble();
        return new(p.GetProperty("steelMaterialIds").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            N("minSteelThicknessMm"), N("minObliquityDegrees"), N("maxObliquityDegrees"),
            N("angleFullDegrees"), N("thicknessFullMm"), N("disturbancePerLayerCap"), N("cumulativeDisturbanceCap"));
    }
}

internal sealed record TooltipPassive(double RhaFactor, double Density, double SpallFactor, double EffectiveCost);

internal static class MaterialTooltip
{
    private static string N(double value) => double.IsFinite(value)
        ? value.ToString("0.###", CultureInfo.InvariantCulture) : "unavailable";
    private static string Percent(double fraction) => N(fraction * 100) + "%";
    internal static string ThicknessInstruction(double minimum,double maximum)
    {
        if(!double.IsFinite(minimum)||!double.IsFinite(maximum)||minimum>maximum)
            return "Supported plate thickness unavailable.";
        var first=Math.Ceiling(minimum);var last=Math.Floor(maximum);
        if(first>last)return "No supported whole-mm plate thickness.";
        return "Use " + N(first) + (first==last?"":"-"+N(last)) + " mm thickness.";
    }
    private static string Curve(IReadOnlyList<CurvePoint> curve, string unit) =>
        string.Join(", ", curve.Select(p => N(p.Input) + unit + "=" + Percent(p.Weight)));

    private static string BaseSummary(TooltipPassive p)
    {
        if (!double.IsFinite(p.RhaFactor) || !double.IsFinite(p.Density)) return "Base armour protection.";
        if (p.Density < 7850 && p.RhaFactor < 1) return "Lighter than steel; needs more thickness than RHA.";
        if (p.RhaFactor > 1 && p.Density > 7850) return "More protection per thickness than RHA, but heavier.";
        if (p.RhaFactor > 1) return "More protection than RHA at the same thickness.";
        if (p.RhaFactor < 1) return "Less protection than RHA at the same thickness.";
        return "General-purpose armour protection.";
    }

    private static (double Heat, double Rod, bool AfterSteel, bool HeadOnZero) Strengths(
        ArmourResponse r, ArmourResponseCatalogue c, TooltipPreconditioning? p)
    {
        var heat = r.Geometry.Mode == "resolvedLayers" ? 0 : Math.Min(c.MaximumAdditionalHeatLoss,
            r.Geometry.AngularCurve.Max(x => x.Weight) * (1-r.Calibration.HeatRetention));
        if (r.Kind == "lightEra") return (heat,0,false,true);
        var cap = p?.TotalCap ?? 0;
        var disturbed = r.Calibration.IntactRodRetention - cap *
            (r.Calibration.IntactRodRetention-r.Calibration.DisturbedRodRetention);
        var afterSteel = disturbed < r.Calibration.IntactRodRetention;
        var curve = r.Geometry.KineticAngularCurve ?? r.Geometry.AngularCurve;
        var rod = Math.Min(c.MaximumAdditionalKineticLoss,curve.Max(x=>x.Weight)*
            (1-Math.Min(r.Calibration.IntactRodRetention,disturbed)));
        // Native curves interpolate. Check the actual head-on point, not a kind label.
        var headOn = curve[0].Input > 0 || curve[0].Input == 0 && curve[0].Weight == 0;
        return (heat,rod,afterSteel,headOn);
    }

    private static bool Disabled(ArmourResponseCatalogue? c, bool shell, bool eligible, bool compatible) =>
        c == null || !c.Enabled || !shell || !eligible || !compatible;

    internal static IReadOnlyList<string> Summary(string materialId, TooltipPassive passive,
        ArmourResponseCatalogue? catalogue, TooltipPreconditioning? pre,
        bool shellLoaded, bool nativeEligible, bool compatible)
    {
        var r = catalogue?.Responses.FirstOrDefault(x=>x.CompatibleMaterialIds.Contains(materialId));
        var primes = pre?.MaterialIds.Contains(materialId) == true;
        if (r == null && !primes) return new[] { BaseSummary(passive) };
        if (Disabled(catalogue,shellLoaded,nativeEligible,compatible)) return new[] { "Extra protection disabled." };
        if (r == null) return new[] { "Angled steel can help armour behind it stop APFSDS." };
        var s=Strengths(r,catalogue!,pre);
        var heat=s.Heat>0 ? "HEAT: up to " + Percent(s.Heat) + " less penetration." : "HEAT: base armour only.";
        var rod=s.Rod>0 ? "APFSDS: up to " + Percent(s.Rod) + " less penetration" +
            (s.AfterSteel ? " after steel." : " at favourable angles.") : "APFSDS: base armour only.";
        return new[] { heat + " " + rod,
            ThicknessInstruction(r.Geometry.MinNormalThicknessMm,r.Geometry.MaxNormalThicknessMm) };
    }

    internal static string Build(string materialId, string label, TooltipPassive passive,
        ArmourResponseCatalogue? catalogue, TooltipPreconditioning? pre,
        bool shellLoaded, bool nativeEligible, bool compatible)
    {
        var lines=new List<string> { label.Length>80 ? label[..77]+"..." : label };
        var r=catalogue?.Responses.FirstOrDefault(x=>x.CompatibleMaterialIds.Contains(materialId));
        var primes=pre?.MaterialIds.Contains(materialId)==true;
        if (r==null && !primes) { lines.Add(BaseSummary(passive)); lines.Add("HEAT and APFSDS use this material's base protection."); }
        else if (Disabled(catalogue,shellLoaded,nativeEligible,compatible))
        { lines.Add("Extra protection disabled."); lines.Add(BaseSummary(passive)); }
        else if (r==null)
        {
            lines.Add(BaseSummary(passive));
            lines.Add("Angled plates can help layers behind them stop APFSDS.");
            lines.Add("Use at least " + N(pre!.MinimumThickness) + " mm, angled " + N(pre.MinimumAngle) + "-" + N(pre.MaximumAngle) + " degrees.");
        }
        else
        {
            var s=Strengths(r,catalogue!,pre);
            lines.Add(s.Heat>0 ? "HEAT: up to " + Percent(s.Heat) + " less penetration." : "HEAT: base armour only.");
            lines.Add(s.Rod>0 ? "APFSDS: up to " + Percent(s.Rod) + " less penetration" +
                (s.AfterSteel ? " after steel." : " at favourable angles.") : "APFSDS: base armour only.");
            if (s.Rod>0 && s.HeadOnZero) lines.Add("No extra APFSDS protection head-on.");
            lines.Add(ThicknessInstruction(r.Geometry.MinNormalThicknessMm,r.Geometry.MaxNormalThicknessMm));
            if (r.Geometry.Mode=="resolvedLayers") lines.Add("Leave a " + N(r.Geometry.MinMeasuredGapMm) + "-" + N(r.Geometry.MaxMeasuredGapMm) + " mm gap behind steel.");
            if (r.Kind is "lightEra" or "heavyEra") lines.Add("ERA works once per area; afterwards only base armour remains.");
        }
        return string.Join("\n",lines);
    }

    internal static string Status(ArmourResponseCatalogue? catalogue, bool shellLoaded, bool nativeEligible, bool compatible)
    {
        if (catalogue == null) return "No valid response catalogue loaded; native passive armour only.";
        if (!catalogue.Enabled) return "Additional responses disabled in configuration; native passive armour only.";
        if (!shellLoaded) return "The shell module is not loaded; native passive armour only.";
        if (!nativeEligible) return "Material is unavailable in this native technology context; no additional response.";
        if (!compatible) return "Active material recipe differs from this response preset; native passive armour only.";
        return "Additional response configured; the shell module and eligible impacts required. Runtime activation is not verified here.";
    }

}


