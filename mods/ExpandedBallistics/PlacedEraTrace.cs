using System.Runtime.CompilerServices;
namespace SprocketExpandedBallistics;

// The compiler checks Building before evaluating any interpolation, including native reads.
// Managed counters are test hooks; no Harmony or native state is involved.
internal static class PlacedEraTrace
{
    internal static Func<bool> Enabled=()=>false,Live=()=>false;
    internal static Action<string>? Sink;
    internal static int LiveBudget=240,PreviewBudget=80;
    internal static long Built,Written;
    internal static bool ShouldBuild(out bool live)
    {live=Live();return Enabled()&&(live?LiveBudget:PreviewBudget)>0;}
    internal static void Write(ref PlacedEraTraceHandler detail)
    {
        if(!detail.Building)return;
        if(!Enabled()){detail.Discard();return;}
        if(detail.Live){if(LiveBudget<=0){detail.Discard();return;}LiveBudget--;}else{if(PreviewBudget<=0){detail.Discard();return;}PreviewBudget--;}
        try{Sink?.Invoke("[Placed ERA trace] "+detail.Text());Written++;}catch{/* Diagnostics cannot alter simulation. */}
    }
}
[InterpolatedStringHandler]
internal ref struct PlacedEraTraceHandler
{
    private DefaultInterpolatedStringHandler text;
    internal bool Building {get;}
    internal bool Live {get;}
    public PlacedEraTraceHandler(int literalLength,int formattedCount,out bool shouldAppend)
    {
        Building=shouldAppend=PlacedEraTrace.ShouldBuild(out var live);Live=live;
        text=Building?new(literalLength,formattedCount):default;
        if(Building)PlacedEraTrace.Built++;
    }
    public void AppendLiteral(string value)=>text.AppendLiteral(value);
    public void AppendFormatted<T>(T value)=>text.AppendFormatted(value);
    public void AppendFormatted<T>(T value,string? format)=>text.AppendFormatted(value,format);
    internal void Prepend(string prefix)
    {var body=text.ToStringAndClear();text=new(prefix.Length+body.Length,0);text.AppendLiteral(prefix);text.AppendLiteral(body);}
    internal string Text()=>text.ToStringAndClear();
    internal void Discard(){if(Building)_=text.ToStringAndClear();}
}
