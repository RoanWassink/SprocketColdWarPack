using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.UI;
using SprocketExpedition.Core.Presentation;
using UnityEngine;
using UnityEngine.Events;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

// Native main-menu integration without cloning MenuPanel (a clone is not
// initialized and Clear() throws). The existing game panel is temporarily
// repurposed as the native Expedition submenu, then its original native buttons
// are re-added on Back. The panel is shifted up one row so adding Expedition does
// not push the buttons against the bottom edge.
[HarmonyPatch]
internal static class CampaignMenuButton
{
    private sealed record MainButton(string Label, UnityAction Action, bool Interactable);

    private static readonly List<MainButton> mainButtons = new();
    private static UnityAction? sandbox;
    private static UnityAction? customBattle;
    private static UnityAction? openAction;
    private static UnityAction? newGameAction;
    private static UnityAction? loadGameAction;
    private static UnityAction? backAction;
    private static MenuPanel? mainPanel;
    private static IntPtr mainPointer;
    private static string mainTitle = string.Empty;
    private static Vector2 mainPosition;
    private static bool shifted;
    private static bool switching;
    internal static bool IsExpeditionMenuOpen => mainPanel != null
        && mainPanel.gameObject.activeInHierarchy && mainPanel.Title == "Expedition";

    [HarmonyPostfix, HarmonyPatch(typeof(MenuPanel), nameof(MenuPanel.Button))]
    private static void Added(
        MenuPanel __instance,
        string label,
        UnityAction onClick,
        bool interactable)
    {
        if (label is null || switching) return;

        if (label.IndexOf("sandbox", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            sandbox = onClick;
        }

        if (label.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0
            && label.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customBattle = onClick;
        }

        var gameMain = UnityEngine.Object.FindObjectOfType<Sprocket.MainMenu>()?.panel;
        if (gameMain == null || __instance.Pointer != gameMain.Pointer) return;

        mainPanel = gameMain;
        if (mainPointer != __instance.Pointer)
        {
            mainPointer = __instance.Pointer;
            mainButtons.Clear();
            mainTitle = __instance.Title;
            mainPosition = __instance.GetComponent<RectTransform>().anchoredPosition;
            shifted = false;
        }

        if (!string.Equals(label, "Expedition", StringComparison.OrdinalIgnoreCase)
            && !mainButtons.Any(button => button.Label == label))
        {
            mainButtons.Add(new MainButton(label, onClick, interactable));
        }

        if (label.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0
            && label.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AddExpeditionButton(__instance, interactable);
        }
    }

    private static void AddExpeditionButton(MenuPanel panel, bool interactable)
    {
        openAction ??= DelegateSupport.ConvertDelegate<UnityAction>(
            new Action(OpenExpeditionMenu))!;
        switching = true;
        try
        {
            panel.Button("Expedition", openAction, interactable);
        }
        finally
        {
            switching = false;
        }

        if (!shifted)
        {
            var rect = panel.GetComponent<RectTransform>();
            mainPosition = rect.anchoredPosition;
            var row = panel.height > 0 ? panel.height : 70f;
            rect.anchoredPosition = mainPosition + new Vector2(0, row);
            shifted = true;
        }
    }

    private static void OpenExpeditionMenu()
    {
        if (mainPanel == null)
        {
            Plugin.Logger.LogError(
                "Expedition native menu: the game's main MenuPanel was not found.");
            return;
        }

        try
        {
            newGameAction ??= DelegateSupport.ConvertDelegate<UnityAction>(
                new Action(NewGame))!;
            loadGameAction ??= DelegateSupport.ConvertDelegate<UnityAction>(
                new Action(LoadGame))!;
            backAction ??= DelegateSupport.ConvertDelegate<UnityAction>(
                new Action(CloseExpeditionMenu))!;

            switching = true;
            try
            {
                mainPanel.Clear();
                mainPanel.Title = "Expedition";
                mainPanel.Button("New Game", newGameAction, true);
                mainPanel.Button("Load Game", loadGameAction, true);
                mainPanel.Button("Back", backAction, true);
            }
            finally
            {
                switching = false;
            }
            mainPanel.Apply();
            Plugin.Logger.LogInfo(
                "Expedition native menu: opened the native New Game / Load Game submenu in the game panel.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("Expedition native menu: submenu creation failed. " + ex);
            switching = false;
        }
    }

    private static void CloseExpeditionMenu()
    {
        if (mainPanel == null) return;
        try
        {
            switching = true;
            try
            {
                mainPanel.Clear();
                mainPanel.Title = mainTitle;
                openAction ??= DelegateSupport.ConvertDelegate<UnityAction>(
                    new Action(OpenExpeditionMenu))!;
                var expeditionInserted = false;
                foreach (var button in mainButtons)
                {
                    mainPanel.Button(button.Label, button.Action, button.Interactable);
                    if (!expeditionInserted
                        && button.Label.IndexOf("custom", StringComparison.OrdinalIgnoreCase) >= 0
                        && button.Label.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Keep Expedition directly below Custom Battle, as it was
                        // when first added, instead of appending it after Quit.
                        mainPanel.Button("Expedition", openAction, true);
                        expeditionInserted = true;
                    }
                }
                if (!expeditionInserted)
                {
                    mainPanel.Button("Expedition", openAction, true);
                }
            }
            finally
            {
                switching = false;
            }
            mainPanel.Apply();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("Expedition native menu: Back failed. " + ex);
        }
    }

    private static void NewGame()
    {
        CloseExpeditionMenu();
        CampaignNativeMenu.ShowNewGameOptions();
    }

    private static void LoadGame()
    {
        CloseExpeditionMenu();
        CampaignNativeFlow.BeginLoadGame();
    }
}


