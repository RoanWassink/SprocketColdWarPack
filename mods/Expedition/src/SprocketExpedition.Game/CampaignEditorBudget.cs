#if CAMPAIGN
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access;
using Sprocket.VehicleEditorModifier;
using Sprocket.Vehicles;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace SprocketExpedition.Game;

// The Defence editor contributes a vanilla 150000/16 criterion. Only this
// Expedition editor visit borrows it; Core's budget and spawn request stay the
// authority. Restore the borrowed values before leaving the visit.
internal static class CampaignEditorBudget
{
    private static ScenarioCostVehicleEditorPlugin? bound;
    private static float originalBudget;
    private static int originalLimit;
    private static GameObject? canvas;
    private static TextMeshProUGUI? label;
    private static RectTransform? itemsButtonRect;
    private static Button? itemsButton;
    private static float nextRefresh;
    private static bool failed;

    internal static void Tick()
    {
        var budget = CampaignControllerHost.DesignerBudget;
        if (budget == null && CampaignNativeFlow.NewGameSession && CampaignNativeFlow.Editing)
            budget = CampaignNativeCatalogue.Get().StartingBudget;
        if (!CampaignNativeFlow.Editing || budget == null)
        {
            Release();
            return;
        }
        // Like the campaign menu, dispatch pointer clicks from Update without
        // an injected native delegate. Sample every frame, not at the label's 5 Hz.
        if (canvas != null && canvas.activeInHierarchy && !CampaignNativeMenu.IsOpen
            && CampaignControllerHost.CanOpenDesignerItems && itemsButtonRect != null
            && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
            && RectTransformUtility.RectangleContainsScreenPoint(itemsButtonRect, Mouse.current.position.ReadValue(), null))
        {
            CampaignControllerHost.ShowItemsFromDesigner();
            return;
        }
        if (failed || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        try
        {
            var core = UnityEngine.Object.FindObjectOfType<VehicleDesignerCore>();
            if (core?.Editor == null) return;
            var editor = core.Cast<IVehicleEditorSource>().Editor;
            var plugins = editor.PluginReader.LoadedPlugins;
            var count = plugins.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IVehicleEditorPlugin>>().Count;
            ScenarioCostVehicleEditorPlugin? found = null;
            for (var index = 0; index < Math.Min(count, 128); index++)
            {
                var candidate = plugins[index]?.TryCast<ScenarioCostVehicleEditorPlugin>();
                if (candidate?.editor?.Pointer == editor.Pointer) { found = candidate; break; }
            }
            if (bound != null && bound.Pointer != found?.Pointer) RestoreCriterion();
            if (found != null)
            {
                var criterion = found.Criteria;
                if (criterion != null)
                {
                    if (bound == null)
                    {
                        bound = found;
                        originalBudget = criterion.CostBudget;
                        originalLimit = criterion.VehicleCountLimit;
                        Plugin.Logger.LogInfo($"Expedition editor fleet criterion bound; native={originalBudget}/{originalLimit}; campaign={budget.Value}; cap={EncounterRules.MaxFriendlyCount} including player.");
                    }
                    var nativeBudget = (float)budget.Value;
                    if (criterion.CostBudget != nativeBudget || criterion.VehicleCountLimit != EncounterRules.MaxFriendlyCount)
                    {
                        criterion.CostBudget = nativeBudget;
                        criterion.VehicleCountLimit = EncounterRules.MaxFriendlyCount;
                        found.UpdateCriterion();
                    }
                }
            }
            var price = core.Target.TryCast<IVehicleGateway>()?.Cost ?? 0f;
            EnsureLabel();
            canvas!.SetActive(!CampaignNativeMenu.IsOpen);
            itemsButton!.interactable = CampaignControllerHost.CanOpenDesignerItems;
            if (!float.IsFinite(price) || price <= 0f)
            {
                label!.text = $"EXPEDITION · Fleet budget {budget.Value:N2}\nWaiting for the native tank price.";
                return;
            }
            var exactPrice = (decimal)price;
            var snapshot = CampaignControllerHost.RunSnapshot;
            var fleet = snapshot?.Rewards is { } rewards ? RewardRules.Fleet(rewards,
                snapshot.BlockBaseBudget.Value + snapshot.SupplyBonus + rewards.EarnedFunds, exactPrice) : null;
            var total = fleet?.Total ?? (int)Math.Min(EncounterRules.MaxFriendlyCount, decimal.Floor(budget.Value / exactPrice));
            label!.text = $"EXPEDITION · Fleet budget {budget.Value:N2} · Tank {exactPrice:N2}\n"
                + (total < 1 ? "Over budget: this tank cannot start the encounter."
                    : $"Fleet: {total} total = you + {total - 1} AI allies · maximum {fleet?.MaximumAllies ?? EncounterRules.MaxFriendlyCount} total");
        }
        catch (Exception ex)
        {
            Release();
            failed = true;
            Plugin.Logger.LogWarning("Expedition optional editor fleet display disabled: " + ex.Message);
        }
    }

    private static void EnsureLabel()
    {
        if (canvas != null && label != null) return;
        canvas = new GameObject("Expedition editor fleet", new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        var display = canvas.AddComponent<Canvas>();
        display.renderMode = RenderMode.ScreenSpaceOverlay;
        display.sortingOrder = 100;
        canvas.AddComponent<GraphicRaycaster>();
        var scaler = canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var panel = new GameObject("Fleet summary", new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        panel.transform.SetParent(canvas.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(355, -170);
        rect.sizeDelta = new Vector2(820, 62);
        var background = panel.AddComponent<Image>();
        background.color = new Color(0.045f, 0.05f, 0.04f, 0.9f);
        background.raycastTarget = false;
        var textObject = new GameObject("Text", new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        textObject.transform.SetParent(panel.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 5);
        textRect.offsetMax = new Vector2(-10, -5);
        label = textObject.AddComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 20;
        label.color = new Color(0.96f, 0.85f, 0.63f);
        label.richText = false;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Left;
        var buttonObject = new GameObject("Items and bonuses", new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        buttonObject.transform.SetParent(canvas.transform, false);
        itemsButtonRect = buttonObject.GetComponent<RectTransform>();
        itemsButtonRect.anchorMin = itemsButtonRect.anchorMax = itemsButtonRect.pivot = new Vector2(0, 1);
        itemsButtonRect.anchoredPosition = new Vector2(355, -240);
        itemsButtonRect.sizeDelta = new Vector2(300, 42);
        var buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(.86f, .62f, .2f, .98f);
        buttonImage.raycastTarget = true;
        itemsButton = buttonObject.AddComponent<Button>();
        itemsButton.targetGraphic = buttonImage;
        var buttonTextObject = new GameObject("Items label", new Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        buttonTextObject.transform.SetParent(buttonObject.transform, false);
        var buttonTextRect = buttonTextObject.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero; buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero; buttonTextRect.offsetMax = Vector2.zero;
        var buttonText = buttonTextObject.AddComponent<TextMeshProUGUI>();
        buttonText.text = "ITEMS / BONUSES";
        buttonText.font = TMP_Settings.defaultFontAsset;
        buttonText.fontSize = 20;
        buttonText.color = new Color(.08f, .07f, .04f);
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.raycastTarget = false;
    }

    private static void RestoreCriterion()
    {
        try
        {
            if (bound?.Criteria is { } criterion)
            {
                criterion.CostBudget = originalBudget;
                criterion.VehicleCountLimit = originalLimit;
                bound.UpdateCriterion();
            }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Expedition editor criterion restoration: " + ex.Message); }
        finally { bound = null; }
    }

    internal static void Release()
    {
        RestoreCriterion();
        if (canvas != null) UnityEngine.Object.Destroy(canvas);
        canvas = null;
        label = null;
        itemsButtonRect = null;
        itemsButton = null;
    }
}
#endif
