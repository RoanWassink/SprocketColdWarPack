using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SprocketExpandedBallistics;

internal sealed record ArmourComparisonDisplay(string Summary, string Materials, string Details);

// Summary rich text uses fixed markup; profile names are escaped as plain text.
// Material and diagnostic content is bounded plain text; it never supplies markup.
internal static class ArmourComparisonPresentation
{
    internal static ArmourComparisonDisplay Build(IReadOnlyList<ArmourComparisonEntry> current,
        IReadOnlyList<ArmourComparisonEntry>? baseline = null)
    {
        var summary = new StringBuilder();
        var selected=current.FirstOrDefault(r=>r.Selected);
        if(selected!=null)
        {
            summary.AppendLine("<b>SELECTED SHELL TEST</b>")
                .Append(Plain(selected.Label,64)).Append(" · ").Append(Number(selected.Reference.GunDiameterM*1000)).Append(" mm calibre\n")
                .Append("Tested stopping limit: <b>").Append(Value(selected)).Append("</b> at ").Append(Number(selected.Angle)).AppendLine("°")
                .AppendLine("Same shell geometry and profile as your sample; penetration varied by speed.");
            var saved=Match(selected,baseline);
            if(saved!=null)summary.Append("Compared with saved array: ").Append(Change(selected,saved)).AppendLine();
            summary.AppendLine();
        }
        summary.AppendLine("<b>FIXED REFERENCE TESTS</b>");
        var materials = new StringBuilder();
        var details = new StringBuilder("Angles from plate normal: 0° straight on; 90° grazing. ERA intact in each trial.\nNumbers are tested stop limits for fixed references, not universal RHA ratings.\n");
        foreach (var behavior in new[] { "apfsds", "heat" })
        {
            string label = TypeLabel(behavior);
            var actual = current.FirstOrDefault(r => r.Reference.Behavior == behavior && r.Actual&&!r.Selected);
            var referenceLabel=actual==null?" reference":behavior=="apfsds"
                ?" ref ("+Number(actual.Reference.DiameterM*1000)+" × "+Number(actual.Reference.LengthM*1000)+" mm rod)"
                :" ref ("+Number(actual.Reference.GunDiameterM*1000)+" mm)";
            summary.Append(label).Append(referenceLabel).Append("  <size=34><b>").Append(Value(actual)).Append("</b></size>");
            if (actual != null) summary.Append("  at ").Append(Number(actual.Angle)).Append('°');
            var previous = Match(actual, baseline);
            if (previous != null) summary.Append("  (").Append(Change(actual!, previous)).Append(')');
            summary.AppendLine();
        }
        summary.AppendLine("<size=16>Only these reference shells. A different shell with the same penetration can give a different result.</size>\n");
        summary.Append("Hit armour weight: ").Append(Weight(current)).AppendLine();
        if(baseline!=null)summary.Append("Saved array weight: ").Append(Weight(baseline)).AppendLine();
        details.AppendLine("Weight sums unique recorded armour parts at the sampled angle, across both references. Each part is counted once, including its whole geometry. A hull/turret is counted as a whole part; unreached parts are not included.");
        summary.AppendLine("<b>Angle      APFSDS       HEAT</b>");
        foreach (double angle in new[] { 0d, 30d, 60d })
        {
            var rod = At(current, "apfsds", angle);
            var heat = At(current, "heat", angle);
            summary.Append(Number(angle)).Append("°      ").Append(Value(rod)).Append("       ").Append(Value(heat)).AppendLine();
        }
        summary.Append("<size=16>0° = straight on. 90° = grazing — no finite value.</size>");
        if (baseline != null)
        {
            summary.AppendLine().AppendLine("<b>Compared with saved array</b>");
            foreach (double angle in new[] { 0d, 30d, 60d })
            {
                var rod = At(current, "apfsds", angle); var heat = At(current, "heat", angle);
                summary.Append(Number(angle)).Append("°      ").Append(Change(rod, Match(rod, baseline)))
                    .Append("       ").Append(Change(heat, Match(heat, baseline))).AppendLine();
            }
        }
        foreach (var row in current.Take(16))
        {
            details.Append(row.Selected?"Selected shell":TypeLabel(row.Reference.Behavior)).Append(' ').Append(Number(row.Angle)).Append("°: ")
                .Append(DetailValue(row)).Append("; ").Append(row.Probes).AppendLine(" trials.");
            var previous=Match(row,baseline);
            if(Bounds(row,out var lo,out var hi)&&Bounds(previous,out var oldLo,out var oldHi))
                details.Append("  Possible threshold difference: ").Append(Signed(Math.Floor(lo-oldHi))).Append(" to ").Append(Signed(Math.Ceiling(hi-oldLo))).AppendLine(" mm.");
        }
        AppendArrayMaterials(materials,current);
        foreach(var row in current.Where(r=>r.Actual&&r.Layers!=null))
            foreach(var layer in row.Layers!.Take(24))
                foreach(var effect in layer.Effects.Take(8))
                    if(details.Length<12000)
                    {
                        details.Append(row.Selected?"Selected shell":TypeLabel(row.Reference.Behavior)).Append(" / ").Append(Plain(layer.Label,64)).Append(": ").Append(Plain(effect.Label,240));
                        if(effect.BeforeMm is {} before&&effect.AfterMm is {} after&&double.IsFinite(before)&&double.IsFinite(after))
                            details.Append(" · ").Append(Number(before)).Append(" → ").Append(Number(after)).Append(" mm; ").Append(Number(Math.Max(0,before-after))).Append(" mm extra reduction");
                        details.AppendLine();
                    }
        foreach (var reference in current.Select(r => r.Reference).Distinct().Take(4))
        {
            details.Append(TypeLabel(reference.Behavior)).Append(": ").Append(Number(reference.GunDiameterM * 1000)).Append(" mm calibre; ");
            if (reference.Behavior == "heat") details.Append("single charge; fixed native jet proxy, not physical warhead dimensions.");
            else details.Append(Number(reference.DiameterM * 1000)).Append(" × ").Append(Number(reference.LengthM * 1000))
                .Append(" mm rod; ").Append(Number(reference.MassKg)).Append(" kg. Geometry fixed; nominal penetration varied via equivalent speed.");
            details.AppendLine();
        }
        details.Append("Search 10–2500 mm; bracket tolerance 5 mm. Changed angles can reach different downstream layers.\n")
            .Append("Observed material changes are not additive per-material protection ratings. Grouped materials prove membership, not their internal order.");
        if (materials.Length == 0) materials.Append("No reliable material observations for the sampled angle.");
        return new(summary.ToString().TrimEnd(), materials.ToString().TrimEnd(), details.ToString().TrimEnd());
    }

    private static string TypeLabel(string behavior) => behavior == "apfsds" ? "APFSDS" : behavior == "heat" ? "HEAT" : "Reference";
    private static string Weight(IReadOnlyList<ArmourComparisonEntry> rows)
    {
        var actual=rows.Where(r=>r.Actual&&r.Layers!=null).ToArray();
        var parts=actual.SelectMany(r=>r.Layers!).GroupBy(l=>l.Entity).ToArray();
        if(parts.Length==0)return "not measured";
        double total=0;int unknown=0;
        foreach(var part in parts)
        {
            var mass=part.Select(p=>p.PartMassKg).FirstOrDefault(m=>m is {} n&&double.IsFinite(n)&&n>=0);
            if(mass is {} value)total+=value;else unknown++;
        }
        if(!double.IsFinite(total))return "not measured";
        if(unknown==parts.Length)return "not measured";
        bool limited=actual.Any(r=>r.Layers!.Count>=24);
        return (unknown>0||limited?"≥":"")+Number(total)+" kg"+(unknown>0||limited?" (partial)":"");
    }
    private static ArmourComparisonEntry? At(IReadOnlyList<ArmourComparisonEntry> rows, string behavior, double angle) =>
        rows.FirstOrDefault(r => !r.Actual && r.Reference.Behavior == behavior && Math.Abs(r.Angle - angle) < .01);
    private static ArmourComparisonEntry? Match(ArmourComparisonEntry? row, IReadOnlyList<ArmourComparisonEntry>? baseline) =>
        row == null || baseline == null ? null : baseline.FirstOrDefault(old => old.Selected==row.Selected&&old.Reference == row.Reference && old.Actual == row.Actual && Math.Abs(old.Angle - row.Angle) <= .1);
    private static bool Bounds(ArmourComparisonEntry? row, out double lower, out double upper)
    {
        lower = upper = 0;
        if (row?.Result.Status != ComparisonSearchStatus.Bracketed || row.Result.LowerMm is not float lo || row.Result.UpperMm is not float hi ||
            !float.IsFinite(lo) || !float.IsFinite(hi) || lo < 0 || hi < lo) return false;
        lower = lo; upper = hi; return true;
    }
    private static string Value(ArmourComparisonEntry? row)
    {
        if (Bounds(row, out var lower, out _)) return Number(Math.Floor(lower)) + " mm";
        if (row?.Result.Status == ComparisonSearchStatus.AboveMaximum && row.Result.LowerMm is float lo && float.IsFinite(lo)) return "≥" + Number(Math.Floor(lo)) + " mm";
        if (row?.Result.Status == ComparisonSearchStatus.BelowMinimum && row.Result.UpperMm is float hi && float.IsFinite(hi)) return "<" + Number(Math.Ceiling(hi)) + " mm";
        return row?.Result.Status == ComparisonSearchStatus.Grazing ? "grazing" : "—";
    }
    private static string Change(ArmourComparisonEntry? row, ArmourComparisonEntry? old)
    {
        if (!Bounds(row, out var lo, out var hi) || !Bounds(old, out var oldLo, out var oldHi)) return "—";
        // Difference between tested stopping limits, not an exact difference in
        // unknown physical thresholds. The measurement interval stays in details.
        return "≈" + Signed(Math.Round(lo - oldLo)) + " mm";
    }
    private static string DetailValue(ArmourComparisonEntry row)
    {
        if (Bounds(row, out var lower, out var upper)) return "stop/perforation bracket " + Number(Math.Floor(lower)) + "–" + Number(Math.Ceiling(upper)) + " mm";
        if (row.Result.Status == ComparisonSearchStatus.AboveMaximum) return "stopped at search limit; " + Value(row);
        if (row.Result.Status == ComparisonSearchStatus.BelowMinimum) return "perforated at minimum test; " + Value(row);
        if (row.Result.Status == ComparisonSearchStatus.Grazing) return "grazing; no finite threshold";
        return "not determined: " + Plain(row.FailureReason ?? row.Result.Detail, 180);
    }
    private static void AppendArrayMaterials(StringBuilder text,IReadOnlyList<ArmourComparisonEntry> current)
    {
        // These are observations of the same physical parts, not a separate array
        // per projectile. Preserve different real plates even when materials match.
        var observations=current.Where(r=>r.Actual&&r.Layers is {Count:>0}).OrderByDescending(r=>r.Selected)
            .ThenByDescending(r=>r.Layers!.Count).SelectMany(r=>r.Layers!).ToArray();
        if(observations.Length==0)return;
        var parts=observations.Where(l=>l.MaterialId!=null||!observations.Any(other=>other.Entity==l.Entity&&other.MaterialId!=null))
            .GroupBy(l=>(l.Entity,l.MaterialId)).ToArray();
        text.AppendLine("Armour parts at the sampled point");
        int number=0;
        foreach(var part in parts.Take(32))
        {
            var layer=part.OrderByDescending(l=>l.ThicknessMm.HasValue).ThenByDescending(l=>l.AngleDegrees.HasValue).First();
            text.Append(++number).Append(". ").Append(Plain(layer.Label, 64));
            var thicknesses=part.Select(l=>l.ThicknessMm).Where(t=>t is {} v&&double.IsFinite(v)).Select(t=>t!.Value).ToArray();
            if(thicknesses.Length>0)
            {
                text.Append(" · ").Append(Number(thicknesses.Min()));
                if(thicknesses.Max()-thicknesses.Min()>.1)text.Append('–').Append(Number(thicknesses.Max()));
                text.Append(" mm");
            }
            if (layer.AngleDegrees is double angle && double.IsFinite(angle)) text.Append(" · ").Append(Number(angle)).Append('°');
            if (!string.IsNullOrWhiteSpace(layer.Role)) text.Append(" · ").Append(Plain(layer.Role, 48));
            if (part.Any(l=>l.Grouped)) text.Append(" · combined layer");
            text.AppendLine();
        }
        text.AppendLine("Each part is listed once. Shell-specific reactions are under Comparison details.");
        if(parts.Length>32||current.Any(r=>r.Actual&&r.Layers?.Count>=24))text.AppendLine("Part list may be incomplete.");
    }
    private static string Number(double value) => double.IsFinite(value) ? value.ToString("0.#", CultureInfo.InvariantCulture) : "—";
    private static string Signed(double value) => value > 0 ? "+" + Number(value) : Number(value);
    private static string Plain(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown";
        var text = new StringBuilder();
        foreach (char c in value)
        {
            if (text.Length >= limit) break;
            text.Append(c == '<' ? '‹' : c == '>' ? '›' : char.IsControl(c) ? ' ' : c);
        }
        return text.ToString().Trim();
    }
}

