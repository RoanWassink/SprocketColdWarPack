#if CAMPAIGN
using Il2CppInterop.Runtime;
using Sprocket;
using Sprocket.UI;
using SprocketExpedition.Core.Presentation;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UObject = UnityEngine.Object;
using NativePauseMenu = Sprocket.Core.UI.PauseMenu;

namespace SprocketExpedition.Game;

// No native method detour, byref argument replacement or option-array mutation.
// Add an owned visual Tab after the native menu has finished building its buttons.
internal static class CampaignPauseMenu
{
    private static NativePauseMenu? menu;
    private static Tab? extra;
    private static UnityAction? clicked;
    private static RectTransform? resizedContent;
    private static Vector2 originalSize;
    private static float nextCheck;
    private static bool failed;

    internal static void Tick()
    {
        if (!CampaignControllerHost.CanShowReturnToEditor)
        {
            if (extra != null || menu != null) Release();
            return;
        }
        if (failed || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.1f;
        try
        {
            var found = UObject.FindObjectOfType<NativePauseMenu>();
            if (found == null || !found.gameObject.activeInHierarchy)
            { Release(); return; }
            if (menu != null && menu.Pointer != found.Pointer) Release();
            if (extra != null)
            {
                extra.Label = CampaignControllerHost.ReturnToEditorLabel;
                extra.interactable = CampaignControllerHost.CanReturnToEditor;
                return;
            }
            if (found.content == null || found.buttonPrefab == null || found.buttons == null
                || found.options == null || found.buttons.Length == 0
                || found.buttons.Length != found.options.Length) return;
            var resumeIndex = -1;
            for (var i = 0; i < found.options.Length; i++)
            {
                var name = found.options[i]?.Name ?? "";
                if (name.Equals("Resume", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Continue", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Return to Game", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Back to Game", StringComparison.OrdinalIgnoreCase))
                { resumeIndex = i; break; }
            }
            if (resumeIndex < 0)
            {
                failed = true;
                Plugin.Logger.LogWarning("Expedition pause: native resume option not recognized; "
                    + string.Join(" | ", found.options.Select(x => x?.Name ?? "null")));
                return;
            }
            var resume = found.buttons[resumeIndex];
            if (resume == null || !resume.interactable || CampaignControllerHost.View is not EncounterView view) return;
            var attempt = view.Attempt;
            var session = CampaignNativeFlow.SessionGeneration;
            menu = found;
            var clone = UObject.Instantiate(found.buttonPrefab.gameObject, found.content, false);
            // Clone the prefab, not a native button with active WaitForAny subscribers.
            extra = clone.GetComponent<Tab>();
            if (extra == null) { UObject.Destroy(clone); throw new InvalidOperationException("Native pause prefab has no Tab."); }
            extra.Clicked = null;
            extra.onClick = new UnityEvent();
            extra.Label = CampaignControllerHost.ReturnToEditorLabel;
            extra.interactable = CampaignControllerHost.CanReturnToEditor;
            clicked = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
            {
                try
                {
                    if (resume == null || !resume.interactable) return;
                    if (!CampaignControllerHost.RequestReturnToEditor(attempt, session)) return;
                    // The original native Resume button owns dismissal/unpausing.
                    resume.Click();
                }
                catch (Exception ex) { Plugin.Logger.LogWarning("Expedition pause refit click: " + ex.Message); }
            }))!;
            extra.onClick.AddListener(clicked);
            var rect = extra.GetComponent<RectTransform>();
            if (found.content.GetComponent<LayoutGroup>() != null)
                rect.SetSiblingIndex(Math.Min(1, found.content.childCount - 1));
            else
            {
                var nativeRects = found.buttons.Where(x => x != null).Select(x => x.GetComponent<RectTransform>()).ToArray();
                var last = nativeRects.OrderBy(x => x.anchoredPosition.y).First();
                var height = Math.Max(40f, last.rect.height);
                rect.anchorMin = last.anchorMin; rect.anchorMax = last.anchorMax; rect.pivot = last.pivot;
                rect.sizeDelta = last.sizeDelta;
                rect.anchoredPosition = last.anchoredPosition - new Vector2(0, height + 10f);
                resizedContent = found.content;
                originalSize = resizedContent.sizeDelta;
                resizedContent.sizeDelta = originalSize + new Vector2(0, height + 10f);
            }
            clone.SetActive(true);
            Plugin.Logger.LogInfo("Expedition pause: visual Return to Editor button added after native menu initialization; native options unchanged.");
        }
        catch (Exception ex)
        {
            Release(); failed = true;
            Plugin.Logger.LogWarning("Expedition optional pause button disabled: " + ex.Message);
        }
    }

    internal static void Release()
    {
        try
        {
            if (extra != null)
            {
                // Detach the managed thunk before releasing its root or asking
                // Unity to destroy the native button at the end of the frame.
                extra.interactable = false;
                if (clicked != null) extra.onClick?.RemoveListener(clicked);
                UObject.Destroy(extra.gameObject);
            }
            if (resizedContent != null) resizedContent.sizeDelta = originalSize;
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Expedition pause cleanup: " + ex.Message); }
        finally
        {
            menu = null; extra = null; clicked = null; resizedContent = null;
            nextCheck = 0f;
        }
    }
}
#endif
