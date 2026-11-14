using UnityEngine;
namespace SprocketUiKit;

/// <summary>
/// Demo tab for the 0.1.7 charts: protection against angle (KE/HEAT and an RHA reference), the
/// remaining protection along the shot line as a staircase, and the same layers as a waterfall
/// (including a case that stops at 0).
/// </summary>
internal static class Demo017
{
    internal const int FirstTab = Demo016.FirstTab + 1;
    internal static readonly string[] Tabs = { "Chart" };

    private static readonly List<Vector2> ke = new(), heat = new(), remaining = new();
    private static List<ChartBar> layers = new();
    private static bool built, boosted, stopped;
    private static float startValue = 400;

    private static void Rebuild(float thickness, bool stop)
    {
        ke.Clear(); heat.Clear(); remaining.Clear();
        for (int angle = 0; angle <= 80; angle++)
        {
            double radians = angle * Math.PI / 180.0;
            float lineOfSight = (float)(thickness / Math.Cos(radians));
            ke.Add(new Vector2(angle, lineOfSight * .85f));
            heat.Add(new Vector2(angle, lineOfSight * .60f));
        }
        float[] depths = { 25, 55, 90, 130 };
        float[] normal = { -60, -80, -90, -100 };
        float[] stopping = { -120, -150, -180, -60 };
        var deltas = stop ? stopping : normal;
        remaining.Add(new Vector2(0, startValue));
        float left = startValue;
        for (int i = 0; i < depths.Length; i++)
        {
            left = MathF.Max(0, left + deltas[i]);
            remaining.Add(new Vector2(depths[i], left));
        }
        layers = new List<ChartBar>
        {
            new("Front plate", deltas[0], tooltip: "25 mm steel: the first layer."),
            new("Spall liner", deltas[1], tooltip: "30 mm composite."),
            new("Backing", deltas[2], tooltip: "35 mm steel."),
            new("Frame", deltas[3], tooltip: "40 mm structure."),
        };
    }

    internal static void Build(Panel p, Func<int> tab, Action<string> log)
    {
        if (!built) { Rebuild(120, false); built = true; }
        var c = p.Columns(0, 0);
        c[0].Parent!.VisibleWhen(() => tab() == FirstTab);
        c[1].Parent!.VisibleWhen(() => tab() == FirstTab);

        var angle = c[0].Section("Protection against angle");
        angle.Label("KE and HEAT are line-of-sight: thickness / cos(angle), in mm RHA. The dashed line is the RHA reference.", TextStyle.Small).Wrap();
        angle.LineChart(260)
            .Series("KE", () => ke, UiKit.Colour("#dc9e33"))
            .Series("HEAT", () => heat, UiKit.Colour("#8fd99c"))
            .XAxis("Angle", "deg", 0, 80, 10)
            .YAxis("Protection", "mm RHA")
            .RefLine("RHA 200", () => 200f)
            .Legend()
            .Empty("No protection data.");
        var change = angle.Row(8);
        change.Button("Change data", () => { boosted = !boosted; Rebuild(boosted ? 180 : 120, stopped); log("chart data: thickness " + (boosted ? 180 : 120)); });
        change.Label(() => boosted ? "Thickness 180 mm: both series and the automatic range follow." : "Thickness 120 mm at 0 deg; the image follows the button.", TextStyle.Faint).Wrap();

        var step = c[1].Section("Remaining along the shot line");
        step.Label("Depth in mm on x, remaining protection in mm on y. Four layers, drawn as a staircase.", TextStyle.Small).Wrap();
        var stepScroll = step.ScrollView().Height(160);
        stepScroll.Label("The wheel over the chart scrolls this view; the chart itself has no pan or zoom.", TextStyle.Faint).Wrap();
        stepScroll.LineChart(130)
            .Series("Remaining", () => remaining, UiKit.Colour("#8fd99c"), SeriesStyle.Step)
            .XAxis("Depth", "mm")
            .YAxis("Remaining", "mm", 0)
            .Legend(false)
            .Empty("No layers.");
        stepScroll.Label("End of the shot line.", TextStyle.Faint);

        var waterfall = c[1].Section("The same layers as a waterfall");
        waterfall.Label("Each bar is a delta; the bars float from the old to the new remainder. A layer that reaches 0 is marked Stopped.", TextStyle.Small).Wrap();
        waterfall.BarChart(140)
            .Bars(() => layers)
            .Waterfall(() => startValue, "Start", "Left")
            .YAxis("Remaining", "mm", 0)
            .Empty("No layers.");
        var stop = waterfall.Row(8);
        stop.Button("Stop at 0", () => { stopped = !stopped; Rebuild(boosted ? 180 : 120, stopped); log(stopped ? "waterfall stops at 0" : "waterfall continues"); });
        stop.Label(() => stopped ? "The third layer reaches 0: it is clamped, marked Stopped and the waterfall ends there." : "All four layers leave some protection at the end.", TextStyle.Faint).Wrap();
    }
}
