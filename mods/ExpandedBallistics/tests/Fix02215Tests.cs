using System.Globalization;
namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.15 acceptance: the cassette composer fits on the screen also as the first kit panel of the session (UiKit.Scale
/// still 1 before the kit canvas exists), the re-fit rule while open (only beyond 4 units), and the fallback for an
/// invalid scale.
/// </summary>
internal static class Fix02215Tests
{
    private static string F(float v)=>v.ToString("0",CultureInfo.InvariantCulture);
    private static string S(float v)=>v.ToString("0.###",CultureInfo.InvariantCulture);

    internal static void Run(Action<bool,string> check)
    {
        Fits(check);
        Refit(check);
        InvalidScale(check);
        Console.WriteLine("0.22.15 checks PASS");
    }

    // Pixels on screen = kit units x the scale the kit canvas really draws with.
    private static bool OnScreen((float W,float H) size,int sw,int sh,float trueScale)=>size.W*trueScale<=sw+.5f&&size.H*trueScale<=sh+.5f;

    private static void Fits(Action<bool,string> check)
    {
        foreach(var (sw,sh) in new[]{(3440,1440),(2560,1440),(1920,1080)})
        {
            var trueScale=ComposerLayout.KitScale(sh,1f);
            foreach(var kit in new[]{1f,4f/3f})
            {
                var size=ComposerLayout.Size(sw,sh,kit,1f);
                check(OnScreen(size,sw,sh,trueScale),$"fix02215: {sw}x{sh}, kit scale {S(kit)}: {F(size.W)}x{F(size.H)} units = {F(size.W*trueScale)}x{F(size.H*trueScale)} px fits");
                // the kit factor unknown (config entry unreadable): still on screen at factor 1
                var unknown=ComposerLayout.Size(sw,sh,kit,null);
                check(OnScreen(unknown,sw,sh,trueScale),$"fix02215: {sw}x{sh}, kit scale {S(kit)}, factor unknown: fits");
            }
            // the composer still fills most of the screen (no overly cautious size)
            var full=ComposerLayout.Size(sw,sh,1f,1f);
            check(full.W*trueScale/sw>=.95f&&full.H*trueScale/sh>=.93f,$"fix02215: {sw}x{sh} uses {full.W*trueScale/sw:P0} x {full.H*trueScale/sh:P0} of the screen");
        }
        // the reported case: 3440x1440, first panel (kit scale 1) is no longer 4590x1920 px
        var first=ComposerLayout.Size(3440,1440,1f,1f);
        check(Math.Abs(first.W-(2580-2*ComposerLayout.Margin))<1&&Math.Abs(first.H-(1080-2*ComposerLayout.Margin))<1,$"fix02215: 3440x1440 first panel is {F(first.W)}x{F(first.H)} units (view 2580x1080)");
        check(first==ComposerLayout.Size(3440,1440,4f/3f,1f),"fix02215: the size before and after the kit canvas is the same at 3440x1440");
        // a kit factor other than 1: the config value is used before the canvas, the kit value after
        var lower=ComposerLayout.Size(2560,1440,1f,.75f);
        check(OnScreen(lower,2560,1440,1f)&&lower.W>2400,$"fix02215: factor 0.75 at 1440p (scale 1) sizes for scale 1: {F(lower.W)} units");
        var higher=ComposerLayout.Size(1920,1080,1.5f,null);
        check(OnScreen(higher,1920,1080,1.5f),"fix02215: a kit scale of 1.5 (factor unknown) is honoured");
        check(ComposerLayout.EffectiveScale(1f,1440,1f)>1.33f&&ComposerLayout.EffectiveScale(2f,1440,1f)==2f,"fix02215: kit scale 1 is not trusted; another kit value counts when larger");
    }

    private static void Refit(Action<bool,string> check)
    {
        check(!ComposerLayout.NeedsResize(2532,1032,2532,1032),"fix02215: same size: no resize");
        check(!ComposerLayout.NeedsResize(2535.9f,1028.1f,2532,1032),"fix02215: a difference of up to 4 units: no resize");
        check(!ComposerLayout.NeedsResize(2536,1032,2532,1032),"fix02215: exactly 4 units: no resize");
        check(ComposerLayout.NeedsResize(2536.5f,1032,2532,1032),"fix02215: width more than 4 units off: resize");
        check(ComposerLayout.NeedsResize(2532,1026,2532,1032),"fix02215: height more than 4 units off: resize");
        // the 3440x1440 report case: the old oversized panel is resized once, then stays put
        var wrong=ComposerLayout.PanelSize(3440,1440);var right=ComposerLayout.Size(3440,1440,4f/3f,1f);
        check(ComposerLayout.NeedsResize(right.W,right.H,wrong.W,wrong.H)&&!ComposerLayout.NeedsResize(right.W,right.H,right.W,right.H),"fix02215: the oversized first panel is resized once and then left alone");
        // another screen size or UI scale while open triggers a resize
        var big=ComposerLayout.Size(2560,1440,4f/3f,1f);var small=ComposerLayout.Size(3440,1440,4f/3f,1f);var ui=ComposerLayout.Size(2560,1440,2f,1.5f);
        check(ComposerLayout.NeedsResize(small.W,small.H,big.W,big.H),"fix02215: a new screen size resizes the open composer");
        check(ComposerLayout.NeedsResize(ui.W,ui.H,big.W,big.H)&&OnScreen(ui,2560,1440,2f),"fix02215: a new UI scale resizes the open composer and still fits");
    }

    private static void InvalidScale(Action<bool,string> check)
    {
        foreach(var (sw,sh) in new[]{(3440,1440),(2560,1440),(1920,1080),(1280,720)})
        {
            var trueScale=ComposerLayout.KitScale(sh,1f);
            foreach(var bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            {
                var s=ComposerLayout.EffectiveScale(bad,sh,null);
                var size=ComposerLayout.Size(sw,sh,bad,float.NaN);
                check(float.IsFinite(s)&&s>0&&OnScreen(size,sw,sh,trueScale),$"fix02215: {sw}x{sh}, kit scale {S(bad)}: fallback {F(size.W)}x{F(size.H)} units fits");
                var view=ComposerLayout.View(sw,sh,bad);
                check(OnScreen(ComposerLayout.PanelSize(view.W,view.H),sw,sh,trueScale),$"fix02215: {sw}x{sh}, View with scale {S(bad)} falls back to screen/1080");
            }
        }
        check(ComposerLayout.KitScale(1440,float.NaN)==ComposerLayout.KitScale(1440,null)&&ComposerLayout.KitScale(1440,0)==ComposerLayout.KitScale(1440,1)&&ComposerLayout.KitScale(0,1)==1,"fix02215: kit scale estimate ignores an invalid factor or screen");
        check(ComposerLayout.KitScale(1080,9)==2&&ComposerLayout.KitScale(1080,.1f)==.5f,"fix02215: kit factor clamped to 0.5-2 as in the kit");
        check(ComposerLayout.View(0,0,float.NaN)==(1920f,1080f)&&ComposerLayout.PanelSize(float.NaN,1080)==(1600f,900f),"fix02215: unknown screen keeps the old fallbacks");
    }
}
