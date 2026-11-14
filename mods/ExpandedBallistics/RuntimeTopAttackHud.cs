using Il2CppInterop.Runtime;
using Sprocket.Vehicles.Cannons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace SprocketExpandedBallistics;

/// <summary>
/// Compact target-lock feedback; rendering only.
/// Uses the same isolated canvas pattern as the ammunition HUD.
/// </summary>
internal static class RuntimeTopAttackHud
{
    private static GameObject? root;
    private static TextMeshProUGUI? text;
    private static RectTransform? panel;
    private static bool fontReported;
    private static int failures;
    private static float retryAt;

    internal static void Failed(Exception ex)
    {
        if(failures++<4)Plugin.ModLog.LogWarning("[Top-attack HUD] "+ex);
        Destroy();
        retryAt=Time.unscaledTime+5;
    }

    private static GameObject RectObject(string name)=>new GameObject(name,new Il2CppSystem.Type[]{Il2CppType.Of<RectTransform>()});

    internal static void Hide()
    {
        if(root!=null)root.SetActive(false);
    }

    internal static void Destroy()
    {
        if(root!=null)Object.Destroy(root);
        root=null;text=null;panel=null;
    }

    internal static void Clear()
    {
        Destroy();
        failures=0;
        retryAt=0;
    }

    internal static void Update()
    {
        if(Time.unscaledTime<retryAt)return;
        var status=RuntimeTopAttackLock.Status();
        if(!Cursor.visible&&!RuntimeDamageFeed.IsEditor)status??=RuntimeAtgm.TopAttackStatus();
        if(status==null){Hide();return;}
        if(root==null&&!Build())return;
        root!.SetActive(true);
        var anchor=new Vector2(RuntimeDamageFeed.Left?0f:1f,1f);
        panel!.anchorMin=anchor;
        panel.anchorMax=anchor;
        panel.pivot=anchor;
        panel.anchoredPosition=new Vector2(RuntimeDamageFeed.Left?24f:-24f,-400f);
        text!.text=status;
        panel.sizeDelta=new Vector2(320f,Math.Clamp(text.GetPreferredValues(text.text,296f,1000f).y+20f,40f,240f));
    }

    private static bool Build()
    {
        var font=TMP_Settings.defaultFontAsset;
        if(font==null)
        {
            if(!fontReported){fontReported=true;Plugin.ModLog.LogInfo("[Top-attack HUD] TMP default font unavailable; waiting.");}
            return false;
        }
        root=RectObject("Top-attack lock status");
        Object.DontDestroyOnLoad(root);
        var canvas=root.AddComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder=81;
        var scaler=root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920f,1080f);
        scaler.matchWidthOrHeight=.5f;
        var child=RectObject("Target lock");
        child.transform.SetParent(root.transform,false);
        panel=child.GetComponent<RectTransform>();
        panel.sizeDelta=new Vector2(320f,150f);
        var background=child.AddComponent<Image>();
        background.color=new Color(0f,0f,0f,.55f);
        background.raycastTarget=false;
        var content=RectObject("Rows");
        content.transform.SetParent(child.transform,false);
        var rect=content.GetComponent<RectTransform>();
        rect.anchorMin=Vector2.zero;
        rect.anchorMax=Vector2.one;
        rect.offsetMin=new Vector2(12f,10f);
        rect.offsetMax=new Vector2(-12f,-10f);
        text=content.AddComponent<TextMeshProUGUI>();
        text.font=font;
        text.fontSize=16f;
        text.color=Color.white;
        text.richText=false;
        text.raycastTarget=false;
        text.enableWordWrapping=true;
        text.overflowMode=TextOverflowModes.Truncate;
        return true;
    }
}
