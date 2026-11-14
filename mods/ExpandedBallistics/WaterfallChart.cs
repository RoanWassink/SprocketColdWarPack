using SprocketUiKit;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.23: the Kit side of the penetration waterfall (UI Kit 0.1.7 <c>BarChart.Waterfall</c>). The data comes from
/// <see cref="PenetrationWaterfall"/> and is fixed per impact, so the getters return the same list every frame.
/// </summary>
internal static class WaterfallChart
{
    internal static BarChart? Add(Stack parent,WaterfallModel? model,float height=150)
    {
        if(model==null||model.Steps.Count==0)return null;
        var bars=model.Steps.Select(s=>new ChartBar(s.Label,(float)s.Delta,UiKit.Colour(s.Colour),s.Tooltip)).ToList();
        var chart=parent.BarChart(height).Bars(()=>bars).Waterfall(()=>(float)model.Start,"Start",model.Stopped?"Stopped":model.EndLabel)
            .YAxis("Penetration","mm",0).Empty("No measured layers.");
        // The marker at 0: the budget ran out in this impact.
        if(model.Stopped)chart.RefLine("Stopped at 0 mm",()=>0f,UiKit.Colour("#e5533d"));
        return chart;
    }
}
