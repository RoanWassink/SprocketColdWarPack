using Il2CppInterop.Runtime;
using Sprocket.UI;
using UnityEngine;
using UnityEngine.Events;
namespace SprocketExpandedBallistics;

internal sealed record ArmourComparisonEntry(string Label,ArmourComparisonReference Reference,double Angle,bool Actual,
    ComparisonSearchResult Result,int Probes,string? FailureReason=null,IReadOnlyList<ArmourComparisonLayer>? Layers=null,bool Selected=false);

// Uses the same native layout/drawer as the shell-editor button and plate stacker.
internal static class ArmourComparisonMenu
{
    private sealed record Row(ArmourComparisonReference Reference,double Angle,bool Actual,ArmourComparisonSearch Search,ArmourComparisonSelectedInput? SelectedInput=null)
    {
        internal string? FailureReason;
        internal IReadOnlyList<ArmourComparisonLayer>? Layers;
        internal bool EvidencePassed;
        internal double EvidencePen=double.PositiveInfinity;
    }
    private static readonly List<Row> rows=new();
    private static ArmourComparisonTarget? target;
    private static ArmourComparisonEntry[]? baseline;
    private static int current;
    private static bool running,replayPending,open=true,materialsOpen,detailsOpen,overviewOpen=true,focused,redrawPending;
    internal static bool Focused=>focused;
    private static string selectedCheck="";
    private static double nextProbeAt;
    private static float nextUiAt;
    private static int materialPage,detailPage,overviewPage;
    private static string status="Sample your array, then compare its protection.";
    private static Action? redraw;
    internal static void Draw(IGUILayout layout,IGUIElementDrawer ui,Action refresh)
    {
        redraw=refresh;
        if(!RuntimeDamageFeed.Ready){ui.InfoField("Array comparison unavailable.",1);return;}
        ValidateTarget();
        layout.EndAllDropdowns();
        layout.BeginDropdown("Armour comparison",open,Ui.BoolCallback(value=>open=value));
        try
        {
            var tip=new UITooltip{Header="Array protection",Body="Compare the last sampled point against fixed APFSDS and HEAT references. Results use this native simulator menu. Preview ERA starts intact."};
            if(focused)ui.Button("Back to simulation",DelegateSupport.ConvertDelegate<UnityAction>((Action)(()=>{focused=false;Refresh();})),ref tip);
            ui.Button(running?"Cancel comparison":"Compare array",DelegateSupport.ConvertDelegate<UnityAction>((Action)Click),ref tip);
            if(focused&&selectedCheck.Length>0)NativeText(ui,selectedCheck);
            if(!running&&target!=null&&current==rows.Count&&rows.Count>0)
            {
                var display=ArmourComparisonPresentation.Build(Entries(),baseline);
                if(focused)ui.Button("Keep as baseline",DelegateSupport.ConvertDelegate<UnityAction>((Action)Pin),ref tip);
                else ui.Button("View comparison",DelegateSupport.ConvertDelegate<UnityAction>((Action)(()=>{focused=true;Refresh();})),ref tip);
            }
            else ui.InfoField(status,2);
        }
        finally{layout.EndAllDropdowns();}
        if(focused&&!running&&target!=null&&current==rows.Count&&rows.Count>0)
        {
            var display=ArmourComparisonPresentation.Build(Entries(),baseline);
            // Native foldouts manage their own visibility/scroll position. Forcing
            // a config rebuild inside their callback recreates the clicked control.
            layout.BeginDropdown("Protection overview",overviewOpen,Ui.BoolCallback(value=>Section(0,value)));
            try{PagedText(ui,display.Summary,false,true);}finally{layout.EndAllDropdowns();}
            layout.BeginDropdown("Armour materials",materialsOpen,Ui.BoolCallback(value=>Section(1,value)));
            try{PagedText(ui,display.Materials,false);}finally{layout.EndAllDropdowns();}
            layout.BeginDropdown("Comparison details",detailsOpen,Ui.BoolCallback(value=>Section(2,value)));
            try{PagedText(ui,RuntimeArmourComparisonParity.Details(target)+"\n"+display.Details,true);}finally{layout.EndAllDropdowns();}
        }
    }
    private static void Section(int section,bool value)
    {
        if(section==0)overviewOpen=value;else if(section==1)materialsOpen=value;else detailsOpen=value;
        if(value){overviewOpen=section==0;materialsOpen=section==1;detailsOpen=section==2;}
        // Apply sibling collapse on the next frame, after the native click finishes.
        redrawPending=true;
    }
    private static void NativeText(IGUIElementDrawer ui,string text)
    {
        foreach(var raw in text.Split('\n'))
        {
            var line=System.Text.RegularExpressions.Regex.Replace(raw,"</?(?:b|size)(?:=[0-9]+)?>","").Trim();
            if(line.Length>0)ui.InfoField(line,Math.Clamp((line.Length+43)/44,1,8));
        }
    }
    private static void PagedText(IGUIElementDrawer ui,string text,bool details,bool overview=false)
    {
        // Keep one native section short enough to navigate on a small screen.
        var lines=new List<string>();
        foreach(var raw in text.Split('\n'))
        {
            var line=System.Text.RegularExpressions.Regex.Replace(raw,"</?(?:b|size)(?:=[0-9]+)?>","").Trim();
            while(line.Length>64)
            {
                int end=line.LastIndexOf(' ',64,64);if(end<16)end=64;
                lines.Add(line[..end]);line=line[end..].TrimStart();
            }
            if(line.Length>0)lines.Add(line);
        }
        const int perPage=6;
        int pages=Math.Max(1,(lines.Count+perPage-1)/perPage);
        int page=Math.Clamp(overview?overviewPage:details?detailPage:materialPage,0,pages-1);
        if(overview)overviewPage=page;else if(details)detailPage=page;else materialPage=page;
        var label=overview?"Overview":details?"Details":"Materials";
        if(pages>1)
        {
            ui.InfoField($"{label} — page {page+1}/{pages}",1);
            var tip=new UITooltip{Header=label,Body="Show another page without opening a tall inspector section."};
            if(page>0)ui.Button("Previous "+label.ToLowerInvariant()+" page",DelegateSupport.ConvertDelegate<UnityAction>((Action)(()=>{if(overview)overviewPage--;else if(details)detailPage--;else materialPage--;Refresh();})),ref tip);
            if(page+1<pages)ui.Button("Next "+label.ToLowerInvariant()+" page",DelegateSupport.ConvertDelegate<UnityAction>((Action)(()=>{if(overview)overviewPage++;else if(details)detailPage++;else materialPage++;Refresh();})),ref tip);
        }
        foreach(var line in lines.Skip(page*perPage).Take(perPage))ui.InfoField(line,2);
    }
    private static void Refresh()=>redraw?.Invoke();
    private static void Click()
    {
        try{if(running)Cancel();else Start();}
        catch(Exception ex){running=false;status="Comparison unavailable: "+ex.Message;Plugin.ModLog.LogWarning("[Array comparison] Button: "+ex.Message);Refresh();}
    }
    private static void Unavailable(string reason)
    {running=replayPending=false;selectedCheck="";rows.Clear();target=null;current=0;status=reason;Plugin.ModLog.LogInfo("[Array comparison] "+reason);Refresh();}
    private static void Start()
    {
        // The active armour overlay owns this operation. The damage-feed flag
        // describes the last player-state event and is not an overlay lifetime gate.
        if(RuntimeArmourComparisonCapture.Latest is not {} latest){Unavailable("Sample a point on the armour first.");return;}
        if(!RuntimeArmourComparisonRunner.IsCurrent(latest,out var reason)){Unavailable(reason);return;}
        rows.Clear();target=latest;current=materialPage=detailPage=overviewPage=0;nextProbeAt=nextUiAt=0;
        focused=true;overviewOpen=true;materialsOpen=detailsOpen=false;
        replayPending=true;selectedCheck="Selected shell: checking the same shot input…";
        if(latest.SelectedInput is {} selected)
        {
            var reference=new ArmourComparisonReference("selected:"+selected.Profile.Id,selected.Profile.Label,ShellBalance.ImpactBehavior(selected.Profile),
                selected.GunDiameter,selected.Diameter,selected.Mass,(float)selected.Geometry.LengthM,selected.Density,selected.Constant,0);
            rows.Add(new(reference,latest.ActualAngleDegrees,true,new ArmourComparisonSearch(),selected));
        }
        foreach(var reference in RuntimeArmourComparisonRunner.References)
        {
            rows.Add(new(reference,latest.ActualAngleDegrees,true,new ArmourComparisonSearch()));
            foreach(var angle in new[]{0d,30d,60d,90d})rows.Add(new(reference,angle,false,new ArmourComparisonSearch(grazing:angle==90)));
        }
        running=true;open=true;status="Measuring protection…";Refresh();
        Plugin.ModLog.LogInfo($"[Array comparison] Started: element={latest.Entity}; rows={rows.Count}; maximum 20 trials per row.");
    }
    private static void Cancel()
    {
        running=replayPending=false;selectedCheck="";rows.Clear();target=null;current=0;
        status="Comparison cancelled. Sample again when ready.";
        Plugin.ModLog.LogInfo("[Array comparison] Cancelled.");Refresh();
    }
    private static bool ValidateTarget()
    {
        if(target==null)return true;
        if(RuntimeArmourComparisonRunner.IsCurrent(target,out _))return true;
        running=replayPending=false;selectedCheck="";rows.Clear();target=null;current=0;
        status="Array changed. Sample it again to compare. Your saved baseline is kept.";
        Refresh();return false;
    }
    private static ArmourComparisonEntry[] Entries()=>rows.Select(r=>new ArmourComparisonEntry(r.Reference.Label,r.Reference,r.Angle,r.Actual,
        r.Search.Result,r.Search.ProbeCount,r.FailureReason,r.Layers,r.SelectedInput!=null)).ToArray();
    private static void Pin()
    {
        if(running||target==null||current!=rows.Count)return;
        baseline=Entries();status="Baseline saved for this session.";Refresh();
    }
    internal static void Update()
    {
        try
        {
            if(redrawPending){redrawPending=false;Refresh();}
            if(!ValidateTarget())return;
            if(running&&target!=null)
            {
                if(replayPending)
                {
                    replayPending=false;selectedCheck=RuntimeArmourComparisonParity.Test(target);
                    nextProbeAt=Time.realtimeSinceStartupAsDouble+.04;Refresh();return;
                }
                while(current<rows.Count&&rows[current].Search.NextPenetrationMm==null)current++;
                if(current==rows.Count)
                {
                    running=false;status="Comparison ready.";Refresh();
                    Plugin.ModLog.LogInfo($"[Array comparison] Completed: {rows.Sum(r=>r.Search.ProbeCount)} trials; {rows.Count(r=>r.Search.Result.Status==ComparisonSearchStatus.Bracketed)} brackets.");
                }
                else if(Time.realtimeSinceStartupAsDouble>=nextProbeAt)
                {
                    var row=rows[current];var nominal=row.Search.NextPenetrationMm!.Value;
                    var probe=row.SelectedInput is {} selected
                        ?RuntimeArmourComparisonRunner.ProbeSelectedAtBudget(target,selected,nominal)
                        :RuntimeArmourComparisonRunner.Probe(target,row.Reference,row.Angle,nominal);
                    nextProbeAt=Time.realtimeSinceStartupAsDouble+.04;
                    if(probe.Outcome==ComparisonProbeOutcome.Indeterminate)row.FailureReason=probe.Reason;
                    if(probe.Layers is {Count:>0}&&(!row.EvidencePassed||probe.Outcome==ComparisonProbeOutcome.Perforated&&nominal<=row.EvidencePen))
                    {row.Layers=probe.Layers;row.EvidencePassed=probe.Outcome==ComparisonProbeOutcome.Perforated;row.EvidencePen=nominal;}
                    row.Search.Observe(probe.Outcome);
                    status=$"{(row.Reference.Behavior=="heat"?"HEAT":"APFSDS")} at {row.Angle:0.#}° • {current+1}/{rows.Count}\nTest {row.Search.ProbeCount}/20";
                    if(row.Search.NextPenetrationMm==null||Time.unscaledTime>=nextUiAt){nextUiAt=Time.unscaledTime+.25f;Refresh();}
                }
            }
        }
        catch(Exception ex)
        {running=false;rows.Clear();current=0;target=null;status="Comparison unavailable: "+ex.Message;Plugin.ModLog.LogWarning("[Array comparison] "+ex.Message);redraw?.Invoke();}
    }
    internal static void Clear()
    {running=replayPending=focused=redrawPending=false;selectedCheck="";rows.Clear();baseline=null;target=null;redraw=null;current=materialPage=detailPage=overviewPage=0;open=overviewOpen=true;materialsOpen=detailsOpen=false;}
}
