using DynamicGUI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketExpandedBallistics;

// Modify the template only while the simulator's native dropdown clones it.
// Restoring it also avoids leaking changes into pooled cannon/other UI controls.
[HarmonyPatch]
internal static class RuntimeSimulatorScroll
{
    private static readonly HashSet<IntPtr> SimulatorDropdowns=new();
    [HarmonyPostfix,HarmonyPatch(typeof(Sprocket.VehicleDesigner.UI.OverlayConfigPlugin),nameof(Sprocket.VehicleDesigner.UI.OverlayConfigPlugin.Draw))]
    private static void SettlePanel(Sprocket.VehicleDesigner.UI.OverlayConfigPlugin __instance)
    {
        if(!RuntimeShellSelection.Enabled||__instance.overlayDrawer?.TryCast<Sprocket.VehicleDesigner.UI.ArmourOverlayConfig>()==null)return;
        try
        {
            var ui=__instance.configUI;
            var panel=ui?.configPanel;var layout=ui?.configLayout;
            if(ui==null||panel==null||layout==null)return;
            // Native Apply lays out children, then resizes configPanel. On the
            // first construction child widths can therefore still be inherited
            // from the pooled prefab. Resolve anchors at the final panel size,
            // then use the game's own ListLayout sizing callbacks bottom-up.
            panel.ForceUpdateRectTransforms();
            var lists=panel.GetComponentsInChildren<UnityEngine.UI.ListLayout>(true);
            for(var i=lists.Length-1;i>=0;i--){lists[i].ForceRelayout();lists[i].Relayout();}
            layout.Refresh();
            ui.Apply();
            panel.ForceUpdateRectTransforms();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Armour Simulator] Panel layout unavailable: "+ex.Message);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(DynamicDropdown),nameof(DynamicDropdown.SetOptions))]
    private static void Register(DynamicDropdown __instance)
    {
        var dropdown=__instance.dropdown;if(dropdown==null)return;
        if(RuntimeArmourSimulator.DrawingShellChoice)SimulatorDropdowns.Add(dropdown.Pointer);
        else SimulatorDropdowns.Remove(dropdown.Pointer);
    }
    private sealed record ScrollState(RectTransform Template,Vector2 Size,ScrollRect Scroll,bool Vertical,bool Horizontal,
        Scrollbar? Bar,ScrollRect.ScrollbarVisibility Visibility,GameObject? TemporaryBar);
    [HarmonyPrefix,HarmonyPatch(typeof(TMP_Dropdown),nameof(TMP_Dropdown.Show))]
    private static void Begin(TMP_Dropdown __instance,out ScrollState? __state)
    {
        __state=null;
        if(!SimulatorDropdowns.Contains(__instance.Pointer)||__instance.options.Count<=6)return;
        if(!__instance.IsActive()||!__instance.IsInteractable()||__instance.m_Dropdown!=null)return;
        // Show initializes this only on its first opening, then immediately reads the
        // item/content rects. Finish that native initialization before changing layout.
        try
        {
            if(!__instance.validTemplate)__instance.SetupTemplate();
            if(!__instance.validTemplate)return;
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Simulator scroll] Native template preparation unavailable: "+ex.Message);return;}
        var template=__instance.template;if(template==null)return;
        var scroll=template.GetComponentInChildren<ScrollRect>(true);
        if(scroll==null){Plugin.ModLog.LogWarning("[Simulator scroll] Native dropdown template has no ScrollRect.");return;}
        __state=new(template,template.sizeDelta,scroll,scroll.vertical,scroll.horizontal,scroll.verticalScrollbar,scroll.verticalScrollbarVisibility,null);
        try
        {
            template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Math.Clamp(Screen.height*.4f,160,320));
            scroll.vertical=true;scroll.horizontal=false;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            if(scroll.verticalScrollbar==null)
            {
            var barObject=new GameObject("Expanded Ballistics scrollbar");
            __state=__state with{TemporaryBar=barObject};
            var rect=barObject.AddComponent<RectTransform>();rect.SetParent(template,false);
            rect.anchorMin=new Vector2(1,0);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(1,.5f);
            rect.sizeDelta=new Vector2(12,-8);rect.anchoredPosition=new Vector2(-2,0);
            var track=barObject.AddComponent<Image>();track.color=new Color(.08f,.08f,.08f,.95f);
            var handleObject=new GameObject("Handle");var handle=handleObject.AddComponent<RectTransform>();handle.SetParent(rect,false);
            handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=new Vector2(2,2);handle.offsetMax=new Vector2(-2,-2);
            var handleImage=handleObject.AddComponent<Image>();handleImage.color=new Color(.75f,.65f,.4f,1);
            var bar=barObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handleImage;bar.direction=Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar=bar;
            }
            PrepareTemplateLayout(__instance,template);
        }
        catch(Exception ex){Plugin.ModLog.LogError("[Simulator scroll] "+ex);}
    }
    private static void PrepareTemplateLayout(TMP_Dropdown dropdown,RectTransform template)
    {
        // Layout dirtied by activation/scrollbar creation is normally processed at
        // frame end. Native Show needs these measurements now, before cloning.
        var wasActive=template.gameObject.activeSelf;
        try
        {
            dropdown.GetComponent<RectTransform>()?.ForceUpdateRectTransforms();
            template.gameObject.SetActive(true);
            template.ForceUpdateRectTransforms();
            LayoutRebuilder.ForceRebuildLayoutImmediate(template);
            template.ForceUpdateRectTransforms();
        }
        finally{template.gameObject.SetActive(wasActive);}
    }
    [HarmonyFinalizer,HarmonyPatch(typeof(TMP_Dropdown),nameof(TMP_Dropdown.Show))]
    private static void End(ScrollState? __state)
    {
        if(__state==null)return;
        __state.Template.sizeDelta=__state.Size;
        __state.Scroll.vertical=__state.Vertical;__state.Scroll.horizontal=__state.Horizontal;
        __state.Scroll.verticalScrollbar=__state.Bar;__state.Scroll.verticalScrollbarVisibility=__state.Visibility;
        if(__state.TemporaryBar!=null){__state.TemporaryBar.SetActive(false);UnityEngine.Object.Destroy(__state.TemporaryBar);}
    }
}

