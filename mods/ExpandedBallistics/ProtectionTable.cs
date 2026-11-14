using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>One row of the composer protection table (0.22.23): the metric, what it is compared with, and the two value cells.</summary>
internal sealed record ProtectionRow(string Metric,string Basis,string With,string Against);

/// <summary>
/// 0.22.23: the content of the composer protection table, shared by the model and the tests. The layout itself (column widths, alignment,
/// scaling) is the UI Kit <c>Table&lt;T&gt;</c> since Kit 0.1.6; the fixed-width workaround of 0.22.19 (ProtectionTableLayout) is gone.
/// Columns: the measure (fills the row), "Compared with" (e.g. "vs 430 mm RHA"), then one value column, or two when a layer is tilted.
/// </summary>
internal static class ProtectionTable
{
    // 0.22.21: clear row names; the basis ("vs 430 mm RHA") has its own column, and Legend explains the factors.
    internal static readonly string[] Metrics={"mm RHA","\u00D7 RHA (same thickness)","\u00D7 RHA (same weight)"};
    internal const string Legend="Game uses the mean of with/against tilt \u00B7 1.00 = solid RHA \u00B7 higher is better";
    internal const string MeasureHeader="RHA comparison",BasisHeader="Compared with";
    internal static string BasisText(double? referenceMm)=>referenceMm is {} r?"vs "+r.ToString("0",CultureInfo.InvariantCulture)+" mm RHA":"";
    /// <summary>Headers of the value columns: "With tilt" and "Against tilt" when a layer is tilted, else the single "Protection".</summary>
    internal static string[] Headers(bool tilted)=>tilted?new[]{"With tilt","Against tilt"}:new[]{"Protection"};
    /// <summary>All column headers in order (measure, basis, values).</summary>
    internal static string[] Columns(bool tilted)=>new[]{MeasureHeader,BasisHeader}.Concat(Headers(tilted)).ToArray();
}
