using BepInEx.Configuration;
using Sprocket.Vehicles.Cannons;
using SprocketUiKit;
using UnityEngine;

namespace SprocketExpandedBallistics;

/// <summary>
/// Ammunition HUD on a Sprocket UI Kit <see cref="HudPanel"/> (0.22.3; replaces the TMP panel). Always
/// shows the loaded shell of the controlled vehicle's active cannon (name, type tag, rounds, gun state),
/// with or without a carried mix; with a mix also one row per type. Play only, also in the scope; hidden
/// only while a kit panel or the pause is open. Data is refreshed at most ten times a second and the
/// HUD only re-lays out when a shown value changes.
/// </summary>
internal static class AmmoLoadoutHud
{
    private static ConfigEntry<string>? corner;
    private static ConfigEntry<float>? offsetX,offsetY;
    private static HudPanel? hud;
    private static AmmoHudView? view;
    private static float playUntil,nextData,retryAt;
    private static int failures;
    private static (string Corner,float X,float Y) applied=("",-1,-1);
    private const int MaxRows=AmmoLoadoutRules.MaximumTypes;

    internal static void Configure(ConfigFile config)
    {
        const string section="Ammunition HUD";
        corner=config.Bind(section,"Corner","BottomRight",new ConfigDescription("Screen corner of the ammunition HUD (kept apart from the hit panel at the top).",
            new AcceptableValueList<string>("TopLeft","TopRight","BottomLeft","BottomRight")));
        offsetX=config.Bind(section,"Offset X",24f,new ConfigDescription("Inward distance from the screen edge (1080p px).",new AcceptableValueRange<float>(0f,1200f)));
        offsetY=config.Bind(section,"Offset Y",230f,new ConfigDescription("Inward distance from the top or bottom edge (1080p px).",new AcceptableValueRange<float>(0f,900f)));
    }
    internal static void Failed(Exception ex)
    {
        if(failures++<4)Plugin.ModLog.LogWarning("[Ammo HUD] "+ex);
        Destroy();retryAt=Time.unscaledTime+5;
    }
    internal static void Hide()=>view=null;
    internal static void RefreshNow()=>nextData=0;
    internal static void Destroy()
    {
        try{if(hud!=null)UiKit.DestroyHud(hud);}catch(Exception){}
        hud=null;view=null;applied=("",-1,-1);
    }
    internal static void Clear(){Destroy();failures=0;retryAt=0;}

    /// <summary>Per frame in Play: true when the shown data should be refreshed now (10 Hz).</summary>
    internal static bool Tick()
    {
        playUntil=Time.unscaledTime+.5f;
        if(Time.unscaledTime<nextData||Time.unscaledTime<retryAt)return false;
        nextData=Time.unscaledTime+.1f;
        return true;
    }
    internal static void Update(Cannon? cannon)
    {
        try
        {
            var next=cannon==null?null:RuntimeAmmoLoadout.HudView(cannon);
            if(next?.Key!=view?.Key)view=next;
            if(view==null)return;
            hud??=Create();
            var c=corner?.Value??"BottomRight";var x=offsetX?.Value??24;var y=offsetY?.Value??230;
            if(applied!=(c,x,y)){applied=(c,x,y);hud.Anchor(Enum.TryParse<HudCorner>(c,out var hc)?hc:HudCorner.BottomRight,x,y);}
        }
        catch(Exception ex){Failed(ex);}
    }
    private static HudPanel Create()
    {
        var h=UiKit.CreateHud(BallisticsKeybindMetadata.Owner,"ammo",300);
        h.AllowedContexts=UiContexts.All;
        // Scoped visibility: Play, no kit panel open, not paused. A free cursor alone (scope, free look) does not hide it.
        h.HideWhenCursorFree=false;
        h.Visible(()=>view!=null&&Time.unscaledTime<playUntil&&!UiKit.AnyPanelOpen&&Time.timeScale>0);
        var body=h.Body;body.Spacing(4);
        var head=body.Row(8);
        head.Label("AMMO",TextStyle.Section);
        head.Chip(()=>view?.Code??"",ChipStyle.Tag);
        head.Spacer();
        head.Label(()=>view?.State??"",TextStyle.Small).Colour(()=>view?.StateColour).Outline();
        var main=body.Row(8);
        main.Label(()=>view?.Name??"",TextStyle.Label).Outline().Flex();
        main.Label(()=>view?.Rounds??"",TextStyle.Value).Outline();
        for(int i=0;i<MaxRows;i++)
        {
            var index=i;var row=body.Row(6);
            row.VisibleWhen(()=>view!=null&&index<view.Rows.Count);
            row.Label(()=>Row(index)?.Marker??"",TextStyle.Small).Colour("#f3cf86").Width(14);
            row.Label(()=>Row(index)?.Label??"",TextStyle.Small).Outline().Flex();
            row.Label(()=>Row(index)?.Count??"",TextStyle.Small).Outline();
        }
        body.Label(()=>view?.Notice??"",TextStyle.Small).Colour("#f3cf86").Outline().Wrap().VisibleWhen(()=>view!=null&&view.Notice.Length>0);
        Plugin.ModLog.LogInfo("[Ammo HUD] UI Kit HUD created.");
        return h;
    }
    private static AmmoHudRow? Row(int index)=>view!=null&&index<view.Rows.Count?view.Rows[index]:null;
}
