using BepInEx.Configuration;
using SprocketKeybinds;
using SprocketUiKit;
using UnityEngine;
namespace SprocketExpandedBallistics;

/// <summary>
/// Play HUD for recent hits on a Sprocket UI Kit <see cref="HudPanel"/> (wave 2b; replaces the TMP feed).
/// Non-interactive: no cursor, no clicks, no input block, never closed by Esc. Blocks are built once per
/// shot (<see cref="HitBlockBuilder"/>); per frame only the buffer expiry check, the anchor/width/scale
/// comparisons and the kit's alpha getters run.
/// </summary>
internal static class HitPanelHud
{
    private static ConfigEntry<string>? mode;
    private static ConfigEntry<int>? maxHits;
    private static ConfigEntry<float>? seconds,scale;
    private static HudPanel? hud;
    private static bool? appliedLeft,appliedExpanded;private static float appliedScale=-1;
    private static int failures;private static float retryAt;
    internal static bool Expanded=>mode?.Value=="expanded";
    private static HitPanelBuffer Buffer=>RuntimeDamageFeed.Buffer;

    internal static void Configure(ConfigFile config)
    {
        const string section="Hit panel";
        mode=config.Bind(section,"Mode","compact",new ConfigDescription("compact = two lines per hit; expanded = also the per-plate breakdown of the pinned (else newest) hit. Switch with the Sprocket Keybinds action \"Hit panel: compact/expanded\" (unbound by default).",new AcceptableValueList<string>("compact","expanded")));
        maxHits=config.Bind(section,"Max hits",6,new ConfigDescription("Hits shown at once.",new AcceptableValueRange<int>(3,8)));
        seconds=config.Bind(section,"Seconds",10f,new ConfigDescription("A hit stays this long and fades over the last 2 s. A pinned hit never expires.",new AcceptableValueRange<float>(4f,30f)));
        scale=config.Bind(section,"Scale",1f,new ConfigDescription("Extra size factor on top of the UI Kit scale.",new AcceptableValueRange<float>(.8f,1.5f)));
        Apply();
    }
    private static void Apply()
    {
        if(maxHits!=null)Buffer.MaxBlocks=maxHits.Value;
        if(seconds!=null)Buffer.Lifetime=seconds.Value;
    }
    internal static void Failed(Exception ex)
    {
        if(failures++<4)Plugin.ModLog.LogWarning("[Hit panel] "+ex);
        Destroy(false);retryAt=Time.unscaledTime+5;
    }

    /// <summary>Per frame from <see cref="DamageFeedBehaviour"/>.</summary>
    internal static void Update()
    {
        if(Time.unscaledTime<retryAt)return;
        Apply();
        Buffer.Expire(Time.unscaledTime);
        if(RuntimeDamageFeed.Show)HitPanelKeys.Poll();
        if(hud==null)
        {
            if(!RuntimeDamageFeed.Show||Buffer.Count==0)return;
            Create();
        }
        var left=RuntimeDamageFeed.Left;
        if(appliedLeft!=left){appliedLeft=left;hud!.Anchor(left?HudCorner.TopLeft:HudCorner.TopRight,24,140);}
        var expanded=Expanded;
        if(appliedExpanded!=expanded){appliedExpanded=expanded;hud!.Width=expanded?640:520;}
        var s=scale?.Value??1f;
        if(appliedScale!=s){appliedScale=s;hud!.ScaleFactor=s;}
    }
    private static void Create()
    {
        hud=UiKit.CreateHud(BallisticsKeybindMetadata.Owner,"hits",520);
        // Visibility follows the existing feed gate (Play, feed enabled, cursor hidden); the kit adds pause/panel rules.
        hud.AllowedContexts=UiContexts.All;
        hud.Visible(()=>RuntimeDamageFeed.Show&&Buffer.Count>0);
        hud.MaxHeight(600);
        appliedLeft=null;appliedExpanded=null;appliedScale=-1;
        var body=hud.Body;body.Spacing(6);
        var title=body.Row(8);
        title.VisibleWhen(()=>Expanded);
        title.Label("HITS",TextStyle.Section);
        title.Chip(()=>Buffer.CountText,ChipStyle.Tag);
        title.Chip(()=>Buffer.PinnedText,ChipStyle.Accent).VisibleWhen(()=>Buffer.Pinned!=null);
        title.Spacer();
        title.Label(()=>Buffer.LifetimeText,TextStyle.Faint);
        body.Repeat<HitBlock>(()=>Buffer.Shown,Block,6);
        Plugin.ModLog.LogInfo("[Hit panel] UI Kit HUD created.");
    }
    /// <summary>One card per shot: stripe, number and result word share the hit colour; the word always names the outcome.</summary>
    private static void Block(HitBlock b,Stack s)
    {
        var card=s.Card().Stripe(()=>b.Colour).MinHeight(44);
        card.Alpha(()=>Buffer.Alpha(b,Time.unscaledTime));
        var stack=card.Stack(2);
        var head=stack.Row(6).ShrinkToFit();
        head.Label(b.NumberText,TextStyle.Label).Colour(b.Colour).Outline();
        if(b.Direction.Length>0)head.Chip(b.Direction,ChipStyle.Tag);
        head.Label(b.Ammo,TextStyle.Label).Outline().Flex();
        if(b.Tandem)head.Chip("TANDEM",ChipStyle.Tag);
        head.Label(b.Values,TextStyle.Value).Outline();
        head.Label(b.Result,TextStyle.Section).Colour(b.Colour).Outline().Width(b.Result.Length>12?176:104).AlignRight();
        if(b.Shooter.Length>0)stack.Label(CombatReportPresentation.Escape(b.Shooter),TextStyle.Small).Outline();
        var detail=stack.Row(6);
        detail.Label(b.Detail,TextStyle.Value).Outline().Flex();
        if(b.DamageShort.Length>0)detail.Label(()=>Expanded?b.DamageExpanded:b.DamageShort,TextStyle.Small).Colour("#ffcbb3").Outline();
        if(b.Breakdown.Length>0)
        {
            var breakdown=stack.Stack(2);
            breakdown.VisibleWhen(()=>Expanded&&Buffer.IsFocus(b));
            foreach(var line in b.Breakdown)breakdown.Label(line,TextStyle.Small).Outline();
            // 0.22.23: the penetration budget of the pinned (else newest) hit as a waterfall: start, one bar per layer or block step, the rest.
            WaterfallChart.Add(breakdown,b.Waterfall,140);
        }
    }
    internal static void Destroy(bool keys=true)
    {
        try{if(hud!=null)UiKit.DestroyHud(hud);}catch(Exception ex){Plugin.ModLog.LogWarning("[Hit panel] HUD removal: "+ex.Message);}
        hud=null;appliedLeft=null;appliedExpanded=null;appliedScale=-1;
        if(keys)HitPanelKeys.Clear();
    }
    internal static void ToggleMode()
    {
        if(mode==null)return;
        mode.Value=Expanded?"compact":"expanded";
        Plugin.ModLog.LogInfo("[Hit panel] Mode "+mode.Value+" from the global keybind.");
    }
}

/// <summary>Two new Sprocket Keybinds actions for the hit panel (unbound by default; the existing feed actions stay in <see cref="DamageFeedKeys"/>).</summary>
internal static class HitPanelKeys
{
    internal static readonly string[] Actions={"hit-panel-mode","hit-panel-pin"};
    internal static readonly string[] Labels={"Hit panel: compact/expanded","Hit panel: pin newest"};
    private static ModKeybind?[] keys=new ModKeybind?[2];
    private static float retryAt;private static int warnings;
    private static void Register()
    {
        if((keys[0]!=null&&keys[1]!=null)||Time.unscaledTime<retryAt)return;
        retryAt=Time.unscaledTime+2f;
        try{for(int i=0;i<Actions.Length;i++)keys[i]??=Keybinds.RegisterButton(BallisticsKeybindMetadata.Owner,BallisticsKeybindMetadata.Name,Actions[i],Labels[i],"");}
        catch(Exception ex){if(warnings++<3)Plugin.ModLog.LogWarning("[Hit panel keys] Keybinds unavailable: "+ex.Message);}
    }
    internal static void Poll()
    {
        Register();
        try
        {
            if(keys[0]?.WasPressedThisFrame==true)HitPanelHud.ToggleMode();
            if(keys[1]?.WasPressedThisFrame==true)
            {
                var pinned=RuntimeDamageFeed.Buffer.TogglePin();
                Plugin.ModLog.LogInfo("[Hit panel] "+(pinned?"Pinned "+RuntimeDamageFeed.Buffer.Pinned?.NumberText:"Unpinned")+" from the global keybind.");
            }
        }
        catch(Exception ex){if(warnings++<3)Plugin.ModLog.LogWarning("[Hit panel keys] "+ex.Message);}
    }
    internal static void Clear()
    {
        try{foreach(var action in Actions)Keybinds.Unregister(BallisticsKeybindMetadata.Owner,action);}catch(Exception){}
        keys=new ModKeybind?[2];retryAt=0;
    }
}
