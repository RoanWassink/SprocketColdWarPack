using System.Globalization;
using System.Net;
using System.Text;
namespace SprocketExpandedBallistics;

/// <summary>0.22.28: report texts of layers that no longer exist (0.22.17-0.22.27), still recognised in saved sessions (last-session.json).</summary>
internal static class LegacyReportText
{
    internal const string ObliquityPrefix="Obliquity (LOS) correction",NativeBlockPrefix="Native block";
    internal static bool IsObliquityCorrection(string? text)=>text!=null&&text.StartsWith(ObliquityPrefix,StringComparison.Ordinal);
}
/// <summary>One bar of the penetration budget: a layer or a block step that consumed (negative) penetration.</summary>
internal sealed record WaterfallStep(string Label,double Delta,string Tooltip,string Colour);
/// <summary>
/// The penetration budget of one impact (0.22.23): the start (initial penetration of the main charge), the steps in order
/// (ERA, cassette, overlap, native block, steel, air ...) and the rest. <c>Start + sum(Delta) = End</c> always holds, and the
/// steps stop where the budget reaches 0.
/// </summary>
internal sealed record WaterfallModel(double Start,IReadOnlyList<WaterfallStep> Steps,double End,string EndLabel,bool Stopped);

/// <summary>
/// 0.22.23: the penetration waterfall of the hit panel, the Kit combat report and the HTML export. Pure and shared: the layers come from
/// <see cref="CombatHitProjection"/> (entry and exit per block), and inside a block the steps come from the same events the table lists
/// (ERA, the 0.22.28 protection line "path × factor = L", overlap, spaced plate), so what the chart shows is what the table lists.
/// Whatever the named steps do not explain is the plate itself (steel); a mismatch between layers is shown as "Unattributed",
/// never hidden, so the deltas always add up to the block progression.
/// </summary>
internal static class PenetrationWaterfall
{
    internal const string ColourEra="#e07a4f",ColourCassette="#c8a3f3",ColourOverlap="#8fd99c",ColourNative="#b8ad94",ColourSteel="#dc9e33",ColourOther="#79d8e8",ColourGap="#7c7566";
    // 0.22.25: the line-of-sight correction that native did not charge on a thick oblique plate.
    internal const string ColourObliquity="#f0c75e";
    internal const double Epsilon=.05;
    internal static WaterfallModel? Build(CombatReportShot shot)=>Build(shot,CombatHitProjection.Build(shot));
    internal static WaterfallModel? Build(CombatReportShot shot,IReadOnlyList<CombatHitView> views)
    {
        var main=views.Where(v=>v.Stage!="Precursor"&&v.Status!="Diagnostic only"&&(v.Layer?.Parts.Any(p=>p.Armour)==true||v.Status is "Stopped" or "Deflected" or "Passed"||v.Layer?.AirGapMm!=null&&v.Before!=null&&v.After!=null)).ToArray();
        if(main.Length==0)return null;
        double? first=shot.InitialPenetrationMm??main[0].Before;
        if(first is not {} start||!double.IsFinite(start)||start<=0)return null;
        var steps=new List<WaterfallStep>();double running=start;string endLabel="Left";bool zero=false;
        void Add(string label,double delta,string tooltip,string colour)
        {
            if(zero||!double.IsFinite(delta)||Math.Abs(delta)<Epsilon)return;
            delta=Math.Max(delta,-running);
            steps.Add(new(Fit(label),delta,tooltip,colour));running+=delta;
            if(running<=1e-9){running=0;zero=true;}
        }
        foreach(var v in main)
        {
            if(zero)break;
            var name=v.Layer?.AirGapMm!=null?"Air gap":HitBlockBuilder.Part(v);
            // 0.22.91 (part C, owner review 16-11): a layer the hit list does not show (an engine, a fuel tank: no armour plate, so no row of its own) keeps its own
            // measured reduction here, from its exit line that the projection moved into this row, instead of "Between layers". Only a reduction that lies within the
            // gap (in order) is booked; what lies between two of them, or is left at the end, stays "Between layers" (an air gap, a spaced-armour effect). Presentation only.
            HashSet<string>? hiddenIds=null;
            if(v.Before is {} gapEnd&&double.IsFinite(gapEnd)&&gapEnd<running-Epsilon)
                foreach(var r in v.Details.Where(e=>e.Kind is "componentResidual" or "plateResidual"&&e.HitId!=null&&e.HitId!=v.Id&&e.BeforeMm is {} b&&e.AfterMm is {} a&&double.IsFinite(b)&&double.IsFinite(a)&&a<b))
                {
                    if(r.AfterMm!.Value<=gapEnd+Epsilon)(hiddenIds??=new(StringComparer.Ordinal)).Add(r.HitId!);
                    double from=r.BeforeMm!.Value,down=Math.Max(r.AfterMm!.Value,gapEnd);
                    if(zero||from>running+Epsilon||down>=from)continue;
                    (hiddenIds??=new(StringComparer.Ordinal)).Add(r.HitId!);
                    if(running-from>Epsilon)Add("Between layers",from-running,$"Penetration changed by {(from-running).ToString("0.#",CultureInfo.InvariantCulture)} mm between the previous layer and {HiddenName(r)}.",ColourGap);
                    if(zero)break;
                    var hidden=HiddenName(r);
                    Add(hidden,down-running,$"{hidden}: {(running-down).ToString("0.#",CultureInfo.InvariantCulture)} mm of penetration (measured in this layer; it is no armour plate and has no row of its own).",ColourOther);
                }
            if(v.Before is {} entry&&double.IsFinite(entry))Add("Between layers",entry-running,$"Penetration changed by {(entry-running).ToString("0.#",CultureInfo.InvariantCulture)} mm between the previous layer and {name}.",ColourGap);
            if(zero)break;
            if(v.Status=="Deflected"){endLabel="Ricochet";break;}
            double? exit=v.Status.StartsWith("Stopped",StringComparison.Ordinal)?0:v.After;
            if(exit is not {} to||!double.IsFinite(to)){endLabel="Unmeasured";break;}
            double remaining=Math.Max(0,running-to);        // what this layer still has to take
            double gain=to-running;                          // a layer that returns penetration is shown as it is
            if(gain>Epsilon){steps.Add(new(Fit(name),gain,"Measured exit is higher than the entry of "+name+".",ColourGap));running+=gain;continue;}
            foreach(var e in v.Details)
            {
                if(remaining<=Epsilon||zero)break;
                if(e.HitId!=null&&hiddenIds!=null&&hiddenIds.Contains(e.HitId))continue;
                if(e.HitId!=null&&e.HitId!=v.Id&&v.Before is {} vb&&e.BeforeMm is {} eb&&eb>vb+Epsilon)continue;
                var kind=Kind(e,v);
                if(kind==null||e.BeforeMm is not {} b||e.AfterMm is not {} a||!double.IsFinite(b)||!double.IsFinite(a))continue;
                var loss=Math.Min(remaining,Math.Max(0,b-a));
                if(loss<Epsilon)continue;
                string tip=e.PaidIntervals is {Length:>0} paid
                    ?CombatReportPresentation.Escape(CombatReportLanguage.Scrub(string.Join("; ",paid.Select(p=>MaterialIntervalDisplay.Piece(p)))))
                    :CombatReportPresentation.Escape(CombatReportLanguage.Reaction(e.Text));
                Add(kind.Value.Label,-loss,tip,kind.Value.Colour);remaining-=loss;
            }
            // 0.22.41: report wording; what the listed reactions do not explain is the plate's own passive resistance.
            if(remaining>Epsilon)Add(v.Status.StartsWith("Stopped",StringComparison.Ordinal)?"Stopped: "+name:name,-remaining,
                v.Layer?.AirGapMm!=null?$"Air gap: {remaining.ToString("0.#",CultureInfo.InvariantCulture)} mm of penetration.":$"Plate {name}: {remaining.ToString("0.#",CultureInfo.InvariantCulture)} mm of penetration (passive resistance of the plate).",v.Layer?.AirGapMm!=null?ColourGap:ColourSteel);
            if(v.Status.StartsWith("Stopped",StringComparison.Ordinal)&&!zero){var rest=-running;running=0;zero=true;if(Math.Abs(rest)>=Epsilon)steps.Add(new(Fit("Stopped: "+name),rest,"The projectile was arrested in "+name+".",ColourSteel));}
        }
        if(zero)endLabel="Stopped";
        return new WaterfallModel(start,steps,running,endLabel,zero);
    }
    private static (string Label,string Colour)? Kind(CombatReportEvent e,CombatHitView v)
    {
        if(e.BeforeMm==null||e.AfterMm==null)return null;
        if(e.Kind=="eraReaction")return ("ERA",ColourEra);
        if((e.Kind is "materialNote" or "materialStop")&&LegacyReportText.IsObliquityCorrection(e.Text))return ("Obliquity",ColourObliquity);
        if(e.Kind=="materialNote")
        {
            if(e.Text.StartsWith(CassetteImpactPath.OverlapPrefix,StringComparison.Ordinal))return ("Overlap",ColourOverlap);
            if(e.Text.StartsWith(LegacyReportText.NativeBlockPrefix,StringComparison.Ordinal))return (CombatReportLanguage.ArrayName,ColourNative);
            return null;
        }
        if(e.Kind is "materialReaction" or "materialStop")
        {
            if(e.Text.StartsWith("Cassette ",StringComparison.Ordinal))return ("Composite",ColourCassette);
            // 0.22.28: the protection line of a mod material ("Leopard 2A7 wedge: 552 mm × 0.51 (long rod @66°) = 281 mm RHA").
            if(ProtectionLine.Is(e.Text)){var label=CombatReportLanguage.Material(e.Text[..e.Text.IndexOf(':')].Trim());return (label.Length==0?"Protection":label,ColourCassette);}
            if(MaterialIntervalDisplay.Is(e))
            {
                if(e.PaidIntervals is {Length:>0} paid)
                {
                    var primary=paid.OrderByDescending(p=>p.RhaMm).First();
                    if(primary.Material=="rha")return (HitBlockBuilder.Part(v),ColourSteel);
                    var label=CombatReportLanguage.Scrub(MaterialIntervalDisplay.Name(primary));
                    var colour=primary.Material.StartsWith("ebCassette_",StringComparison.Ordinal)?ColourCassette
                        :primary.Material.StartsWith("native:",StringComparison.Ordinal)?ColourOther:ColourSteel;
                    return (label.Length==0?HitBlockBuilder.Part(v):label,colour);
                }
                const string prefix="Confirmed material intervals:";
                var rest=e.Text.StartsWith(prefix,StringComparison.Ordinal)?e.Text[prefix.Length..].Trim():e.Text;
                var cut=rest.IndexOfAny(new[]{'[',':',';'});
                var fallback=CombatReportLanguage.Scrub((cut>0?rest[..cut]:rest).Trim());
                return (fallback.Length==0?HitBlockBuilder.Part(v):fallback,ColourOther);
            }
            var name=CombatReportLanguage.Scrub(e.Text.Split(':',';')[0].Trim());
            return (name.Length==0?"Material":name,ColourOther);
        }
        return null;
    }
    /// <summary>0.22.91: the report name of a layer without a row, from its exit line ("Traversed fuelTank; ..." → "fuel tank").</summary>
    private static string HiddenName(CombatReportEvent residual)
    {
        var t=residual.Text.Split(';')[0].Trim();
        if(t.StartsWith("Traversed ",StringComparison.Ordinal))t=t["Traversed ".Length..].Trim();
        return t.Length==0?"Layer":CombatReportLanguage.Module(t);
    }
    private static string Fit(string label)=>label.Length<=18?label:label[..17]+"\u2026";

    /// <summary>One line of text for tooltips and logs: "530 mm: ERA -120, Cassette -200 ... = 60 mm left".</summary>
    internal static string Text(WaterfallModel m)
    {
        var c=CultureInfo.InvariantCulture;
        var sb=new StringBuilder(m.Start.ToString("0",c)+" mm");
        foreach(var s in m.Steps)sb.Append(" \u2192 "+s.Label+" "+s.Delta.ToString("0",c));
        return sb.Append(" = "+m.End.ToString("0",c)+" mm "+(m.Stopped?"(stopped)":m.EndLabel.ToLowerInvariant())).ToString();
    }

    /// <summary>
    /// The static replacement of the Kit chart in the HTML export (0.22.23): an inline SVG with the same bars (start, floating deltas, rest) and the
    /// same colours; every bar has a title (the hover text). No scripts and no external files.
    /// </summary>
    internal static string Svg(WaterfallModel m)
    {
        var c=CultureInfo.InvariantCulture;
        const int W=760,H=190,Left=48,Right=12,Top=20,Bottom=52;
        double plotW=W-Left-Right,plotH=H-Top-Bottom;
        int n=m.Steps.Count+2;double slot=plotW/n,barW=Math.Min(70,slot*.72);
        double max=Math.Max(m.Start,1);
        double Y(double value)=>Top+plotH*(1-Math.Max(0,Math.Min(value,max))/max);
        string F(double v)=>v.ToString("0.#",c);
        string Enc(string t)=>WebUtility.HtmlEncode(t);
        var sb=new StringBuilder($"<svg class='wf' role='img' aria-label='Penetration budget' viewBox='0 0 {W} {H}' width='{W}' height='{H}' xmlns='http://www.w3.org/2000/svg'>");
        // Axis: 0 and the start value, plus a dashed line at 0 when the core stopped.
        sb.Append($"<line x1='{Left}' y1='{F(Y(0))}' x2='{W-Right}' y2='{F(Y(0))}' stroke='{(m.Stopped?"#e5533d":"#3b434b")}' stroke-width='1'{(m.Stopped?" stroke-dasharray='5 3'":"")}/>");
        sb.Append($"<text x='{Left-6}' y='{F(Y(0)+4)}' text-anchor='end' fill='#b8ad94' font-size='11'>0</text><text x='{Left-6}' y='{F(Y(max)+4)}' text-anchor='end' fill='#b8ad94' font-size='11'>{F(max)}</text>");
        double running=m.Start;
        void Bar(int index,double from,double to,string colour,string label,string value,string title)
        {
            double cx=Left+slot*(index+.5),x=cx-barW/2,top=Y(Math.Max(from,to)),bottom=Y(Math.Min(from,to));
            var height=Math.Max(2,bottom-top);
            sb.Append($"<g><title>{Enc(title)}</title><rect x='{F(x)}' y='{F(top)}' width='{F(barW)}' height='{F(height)}' rx='2' fill='{colour}'/>");
            sb.Append($"<text x='{F(cx)}' y='{F(top-4)}' text-anchor='middle' fill='#e7edf2' font-size='11'>{Enc(value)}</text>");
            sb.Append($"<text x='{F(cx)}' y='{F(H-Bottom+14)}' text-anchor='middle' fill='#b8ad94' font-size='10'>{Enc(label.Length>14?label[..13]+"\u2026":label)}</text></g>");
        }
        Bar(0,0,m.Start,"#79d8e8","Start",F(m.Start),"Initial penetration "+F(m.Start)+" mm");
        for(int i=0;i<m.Steps.Count;i++)
        {
            var s=m.Steps[i];var next=running+s.Delta;
            Bar(i+1,running,next,s.Colour,s.Label,(s.Delta>=0?"+":"\u2212")+F(Math.Abs(s.Delta)),s.Label+": "+(s.Delta>=0?"+":"\u2212")+F(Math.Abs(s.Delta))+" mm, "+F(next)+" mm left"+(s.Tooltip.Length>0?" \u2014 "+s.Tooltip:""));
            running=next;
        }
        Bar(n-1,0,m.End,m.Stopped?"#e5533d":"#8fd99c",m.Stopped?"Stopped":m.EndLabel,F(m.End),m.Stopped?"The penetration reached 0 mm: stopped.":m.EndLabel+": "+F(m.End)+" mm");
        return sb.Append("</svg>").ToString();
    }
}
