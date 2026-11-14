#if CAMPAIGN
using System.Runtime.CompilerServices;
using System.Text;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game.UI;
using SprocketUiKit;
using UnityEngine;
using NativePauseMenu = Sprocket.Core.UI.PauseMenu;

namespace SprocketExpedition.Game;

// Sprocket UI Kit view of the Expedition menu. The panel is created once (lazily,
// on the first Open, because the TMP font must exist) and built once; the screens
// fill a MenuPage that the Kit's getters and repeaters read every frame. Sub-screens
// (difficulty, archive confirmation, armory management) are Kit pages, so the title
// bar shows the back button and Esc goes back. Input, click-through, Esc, the dim
// backdrop and the in-game cursor are owned by the Kit.
internal static partial class CampaignNativeMenu
{
    private const string KitPanelId = "menu";
    private const float KitPanelWidth = 1600f;
    private const float KitPanelHeight = 920f;
    private const float KitBackdrop = .97f;
    private static string DevelopmentNote => CampaignControllerHost.UsesNativeContent
        ? "EXPEDITION - CONTENT AND BALANCE IN DEVELOPMENT"
        : "TEST CAMPAIGN - CONTENT AND BALANCE IN DEVELOPMENT";
    private const string PageDifficulty = "difficulty";
    private const string PageArchive = "archive";
    private const string PageManage = "manage";

    private static Panel? kitPanel;
    private static bool kitPanelFailed;
    private static MenuPage page = MenuPage.Empty;
    private static string pageSignature = "";
    private static string appliedStatus = "";
    private static StatusKind appliedStatusKind;
    private static string appliedSubtitle = "";

    private static bool closingByExpedition;
    private static bool syncingKitPage;
    // Requests from the Kit (Esc / close button / title-bar back), handled on the next
    // Tick instead of re-entrantly inside the Kit's input pass.
    private static bool closeRequested;
    private static bool backRequested;
    // The game's pause menu closed the panel; reopen it once the pause menu is gone.
    private static bool closedByPause;
    private static float pauseMenuGoneSince = -1f;

    private sealed class MenuButton
    {
        public MenuButton(string label, Action onClick, bool primary = false, bool enabled = true)
        { Label = label; OnClick = onClick; Primary = primary; Enabled = enabled; }
        public string Label { get; }
        public Action OnClick { get; }
        public bool Primary { get; }
        public bool Enabled { get; }
    }

    private sealed class MenuCard
    {
        public string Title = "";
        public string Body = "";
        // Reward rarity: readable chip text plus the rarity colour (card accent and chip).
        public string? Tag;
        public Color? Colour;
        // Inline: one horizontal row (slot list); otherwise a stacked card.
        public bool Inline;
        // Structured encounter offer (visual card) and its cached map picture; null = text card.
        public OfferCardModel? Offer;
        public Texture2D? Thumb;
        public readonly List<MenuButton> Buttons = new();
    }

    // Result banner: outcome, numbers, optional veteran notice and received item, details.
    private sealed class MenuBanner
    {
        public ResultTone Tone;
        public string Headline = "";
        public string Subline = "";
        public string? OutcomeText;
        public string? Message;
        public readonly List<ResultTile> Tiles = new();
        public readonly List<ResultBlock> Blocks = new();
        public string? Notice;
        public RewardBlock? Item;
        public readonly List<string> Details = new();
    }

    private sealed class MenuPage
    {
        public static readonly MenuPage Empty = new();
        public string Title = "";
        public readonly List<string> Lines = new();
        public string? ChoicesHeading;
        public readonly List<List<MenuButton>> Choices = new();
        public readonly List<string> Notes = new();
        public readonly List<List<MenuCard>> CardRows = new();
        public readonly List<MenuButton> Header = new();
        public readonly List<MenuButton> Actions = new();
        // Single-element wrappers so a Repeat can build one horizontal Row.
        public readonly List<List<MenuButton>> HeaderRow = new();
        public readonly List<List<MenuButton>> ActionRow = new();
        // Visual screens: chips under the title, one instruction line, result banner.
        public readonly List<ScreenFact> Facts = new();
        public readonly List<List<ScreenFact>> FactsRow = new();
        public string Hint = "";
        public readonly List<MenuBanner> Banners = new();
        // Kit sub-page that shows this screen (null = root) and what going back does.
        public string? SubPage;
        public Action? Back;
        // What the old CLOSE/CANCEL button did; null = Esc and the close button do nothing.
        public Action? CloseAction;
        public string Status = "";
        public StatusKind StatusKind = StatusKind.Info;

        public void Seal()
        {
            HeaderRow.Clear();
            HeaderRow.Add(Header);
            ActionRow.Clear();
            if (Actions.Count > 0) ActionRow.Add(Actions);
            FactsRow.Clear();
            if (Facts.Count > 0) FactsRow.Add(Facts);
        }

        public string Signature(object? source)
        {
            var text = new StringBuilder();
            text.Append(source == null ? 0 : RuntimeHelpers.GetHashCode(source)).Append('\u001f');
            text.Append(Title).Append('\u001f').Append(ChoicesHeading).Append('\u001f')
                .Append(SubPage).Append('\u001f').Append(Back == null ? '-' : '+')
                .Append(CloseAction == null ? '-' : '+').Append(Status).Append((int)StatusKind).Append('\u001e');
            foreach (var line in Lines) text.Append(line).Append('\u001f');
            text.Append('\u001e');
            text.Append(Hint).Append('\u001f');
            foreach (var fact in Facts) text.Append(fact.Text).Append(fact.Emphasis ? '+' : '-').Append(fact.Tooltip).Append('\u001f');
            text.Append('\u001e');
            foreach (var banner in Banners) BannerSignature(text, banner);
            text.Append('\u001e');
            foreach (var line in Notes) text.Append(line).Append('\u001f');
            text.Append('\u001e');
            foreach (var row in Choices) { Buttons(text, row); text.Append('\u001d'); }
            text.Append('\u001e');
            Buttons(text, Header);
            text.Append('\u001e');
            Buttons(text, Actions);
            text.Append('\u001e');
            foreach (var row in CardRows)
            {
                foreach (var card in row)
                {
                    text.Append(card.Title).Append('\u001f').Append(card.Body).Append('\u001f')
                        .Append(card.Tag).Append(card.Colour?.ToString() ?? "-").Append(card.Inline ? '+' : '-');
                    if (card.Offer != null) OfferSignature(text, card.Offer, card.Thumb != null);
                    Buttons(text, card.Buttons);
                }
                text.Append('\u001d');
            }
            return text.ToString();
        }

        private static void BannerSignature(StringBuilder text, MenuBanner banner)
        {
            text.Append((int)banner.Tone).Append(banner.Headline).Append('\u001f').Append(banner.Subline).Append('\u001f')
                .Append(banner.OutcomeText).Append('\u001f').Append(banner.Message).Append('\u001f').Append(banner.Notice).Append('\u001f');
            foreach (var tile in banner.Tiles) text.Append(tile.Caption).Append('=').Append(tile.Value).Append('\u001f');
            if (banner.Item is { } item)
                text.Append(item.Name).Append(item.Effect).Append(item.Extra).Append(item.RarityText).Append('\u001f');
            foreach (var block in banner.Blocks)
            {
                text.Append(block.Title).Append('\u001f');
                foreach (var line in block.Lines) text.Append(line).Append('\u001f');
            }
            foreach (var detail in banner.Details) text.Append(detail).Append('\u001f');
        }

        private static void OfferSignature(StringBuilder text, OfferCardModel offer, bool hasThumb)
        {
            text.Append(offer.Title).Append('\u001f').Append(offer.Tagline).Append('\u001f')
                .Append(string.Join("|", offer.Kinds)).Append('\u001f')
                .Append(offer.RarityText).Append(offer.MapId).Append(hasThumb ? '+' : '-').Append(offer.EnemyCount)
                .Append('/').Append(offer.Supply).Append('\u001f').Append(offer.Cost?.Label).Append(offer.Cost?.Over).Append('\u001f');
            foreach (var line in offer.Composition) text.Append(line.Text).Append(line.Role).Append('\u001f');
            if (offer.Reward is { } reward)
                text.Append(reward.Name).Append(reward.Effect).Append(reward.Extra).Append('\u001f');
            foreach (var detail in offer.Details) text.Append(detail).Append('\u001f');
        }

        private static void Buttons(StringBuilder text, List<MenuButton> buttons)
        {
            foreach (var button in buttons)
                text.Append(button.Label).Append(button.Primary ? '+' : '-').Append(button.Enabled ? '+' : '-').Append('\u001f');
        }
    }

    private static bool EnsurePanel()
    {
        if (kitPanel != null) return true;
        if (kitPanelFailed) return false;
        try
        {
            var panel = UiKit.CreatePanel("sprocket.expedition", KitPanelId, "Expedition",
                MenuSubtitle(), KitPanelWidth, KitPanelHeight);
            // The main menu is UiContexts.Other; Expedition opens and closes the menu itself.
            panel.AllowedContexts = UiContexts.All;
            panel.CloseOnSceneChange = false;
            panel.BlockGameplayInput = true;
            panel.Backdrop(KitBackdrop);
            // Esc / the close button never close the panel themselves. Closable follows the
            // screen (PublishPage): on with the old CLOSE/CANCEL button, whose action
            // OnCloseRequest runs; off elsewhere, so Esc does nothing there.
            panel.Closable = false;
            panel.OnCloseRequest = OnKitCloseRequest;
            panel.OnClose += OnKitPanelClosed;
            panel.PageChanged += OnKitPageChanged;
            BuildKitContent(panel.Body);
            BuildKitContent(panel.Page(PageDifficulty, "Difficulty"));
            BuildKitContent(panel.Page(PageArchive, "Archive slot"));
            BuildKitContent(panel.Page(PageManage, "Manage items"));
            kitPanel = panel;
            appliedSubtitle = panel.Subtitle;
            Plugin.Logger.LogInfo($"Expedition menu: UI Kit panel created (built against Kit {UiKit.Version}).");
            return true;
        }
        catch (Exception ex)
        {
            kitPanelFailed = true;
            Plugin.Logger.LogError("Expedition menu: UI Kit panel creation failed; the menu cannot be shown. " + ex);
            return false;
        }
    }

    // One screen template per Kit page; every copy reads the current MenuPage.
    private static void BuildKitContent(Stack root)
    {
        var column = root.Columns(0)[0];

        // Header: development note on the left, ITEMS / DIFFICULTY / CLOSE on the right.
        column.Repeat(() => page.HeaderRow, (buttons, stack) =>
        {
            var row = stack.Row(10);
            row.Label(DevelopmentNote, TextStyle.Faint);
            row.Spacer();
            foreach (var button in buttons) AddKitButton(row, button);
        }, 0);

        var scroll = column.ScrollView().Fill();
        // A result screen shows its banner instead of the plain title.
        scroll.Label(() => page.Title, TextStyle.Title).VisibleWhen(() => page.Banners.Count == 0);
        scroll.Repeat(() => page.Banners, (banner, stack) => AddResultBanner(stack, banner), 8);
        scroll.Repeat(() => page.FactsRow, (facts, stack) => AddFactChips(stack, facts), 0);
        scroll.Label(() => page.Hint, TextStyle.Faint).Wrap().VisibleWhen(() => page.Hint.Length > 0);
        scroll.Repeat(() => page.Lines, (line, stack) => AddKitLine(stack, line), 4);
        scroll.Label(() => page.ChoicesHeading ?? "", TextStyle.Section)
            .VisibleWhen(() => page.ChoicesHeading != null);
        scroll.Repeat(() => page.Choices, (buttons, stack) =>
        {
            var row = stack.Row(12);
            foreach (var button in buttons) AddKitButton(row, button).Flex();
        }, 8);
        scroll.Repeat(() => page.Notes, (line, stack) => AddKitLine(stack, line), 4);
        scroll.Repeat(() => page.CardRows, (cards, stack) =>
        {
            var row = stack.Row(12);
            foreach (var card in cards) AddKitCard(row, card);
        }, 10);

        var footer = column.Footer();
        footer.VisibleWhen(() => page.ActionRow.Count > 0);
        footer.Repeat(() => page.ActionRow, (buttons, stack) =>
        {
            var row = stack.Row(12);
            foreach (var button in buttons) AddKitButton(row, button);
            row.Spacer();
        }, 0);
    }

    private static void AddKitLine(Stack stack, string line)
    {
        if (line.Length == 0) stack.Spacer(6);
        else stack.Label(line, TextStyle.Value).Wrap();
    }

    private static Button AddKitButton(Container parent, MenuButton button) =>
        parent.Button(button.Label, button.OnClick, button.Primary ? ButtonStyle.Primary : ButtonStyle.Normal)
            .EnabledWhen(() => button.Enabled && !CampaignControllerHost.Busy);

    private static void AddKitCard(Row row, MenuCard card)
    {
        if (card.Offer != null) { AddOfferCard(row, card); return; }
        var box = row.Card().Flex().Fill();
        if (card.Colour is { } accent) box.Accent(accent);
        if (card.Inline)
        {
            box.Label(card.Body, TextStyle.Value).Wrap();
            foreach (var button in card.Buttons) AddKitButton(box, button);
            return;
        }
        var stack = box.Stack(6);
        stack.Label(card.Title, TextStyle.Section).Wrap();
        if (card.Tag != null)
        {
            var tags = stack.Row(6);
            if (card.Colour is { } colour) tags.Chip(card.Tag, colour);
            else tags.Chip(card.Tag, ChipStyle.Tag);
            tags.Spacer();
        }
        stack.Label(card.Body, TextStyle.Value).Wrap();
        if (card.Buttons.Count == 0) return;
        var buttons = stack.Row(8);
        foreach (var button in card.Buttons) AddKitButton(buttons, button).Flex();
    }

    private static string RarityText(RewardRarity rarity) => MenuText.RarityText(rarity);

    private static string MenuSubtitle()
    {
        var slot = "Slot " + CampaignControllerHost.CurrentSlot;
        return CampaignControllerHost.RunSnapshot is { } run ? $"{slot} · round {run.Round}" : slot;
    }

    // Replace the visible page only when its content changed, so the Kit's
    // repeaters do not rebuild their elements for an identical screen.
    private static void PublishPage(MenuPage next, object? source)
    {
        next.Seal();
        var signature = next.Signature(source);
        if (signature != pageSignature)
        {
            var previousTitle = page.Title;
            page = next;
            pageSignature = signature;
            if (Plugin.Detailed && previousTitle != next.Title)
                Plugin.Trace($"[Expedition menu] screen '{previousTitle}' -> '{next.Title}' "
                    + $"(view {CampaignControllerHost.View?.GetType().Name ?? "slots"}, page {next.SubPage ?? "root"}).");
        }
        if (kitPanel == null) return;
        SyncKitPage();
        // Esc / ✕ are only offered where the old screen had CLOSE or CANCEL.
        var closable = page.CloseAction != null;
        if (kitPanel.Closable != closable) kitPanel.Closable = closable;
        if (page.Status != appliedStatus || page.StatusKind != appliedStatusKind)
        {
            appliedStatus = page.Status;
            appliedStatusKind = page.StatusKind;
            kitPanel.SetStatus(appliedStatus, appliedStatusKind);
        }
        var subtitle = MenuSubtitle();
        if (subtitle != appliedSubtitle)
        {
            appliedSubtitle = subtitle;
            kitPanel.Subtitle = subtitle;
        }
    }

    // Show the Kit page that belongs to the current screen.
    private static void SyncKitPage()
    {
        if (kitPanel == null || kitPanel.CurrentPage == page.SubPage) return;
        syncingKitPage = true;
        try
        {
            if (kitPanel.CurrentPage != null) kitPanel.PopToRoot();
            if (page.SubPage != null) kitPanel.Push(page.SubPage);
        }
        finally { syncingKitPage = false; }
    }

    private static void ResetPage()
    {
        page = MenuPage.Empty;
        pageSignature = "";
        SyncKitPage();
    }

    private static void ShowKitPanel(string reason)
    {
        if (kitPanel == null || kitPanel.IsOpen) return;
        var opened = kitPanel.Open();
        if (Plugin.Detailed)
            Plugin.Trace($"[Expedition menu] Kit panel open ({reason}): {(opened ? "shown" : "refused")} "
                + $"in context {UiKit.Context}.");
    }

    private static void HideKitPanel(string reason)
    {
        if (kitPanel == null || !kitPanel.IsOpen) return;
        closingByExpedition = true;
        try { kitPanel.Close(); }
        finally { closingByExpedition = false; }
        if (Plugin.Detailed) Plugin.Trace($"[Expedition menu] Kit panel closed by Expedition ({reason}).");
    }

    // Esc on the root page or the close button (only while Closable, i.e. on a screen
    // with CLOSE/CANCEL). Always refused; the old button's action runs on the next Tick.
    private static bool OnKitCloseRequest()
    {
        if (page.CloseAction != null && !CampaignControllerHost.Busy) closeRequested = true;
        return false;
    }

    // The title-bar back button or Esc left a sub-page.
    private static void OnKitPageChanged()
    {
        if (syncingKitPage || kitPanel == null) return;
        if (kitPanel.CurrentPage != page.SubPage) backRequested = true;
    }

    // With Closable = false and CloseOnSceneChange = false only the game's pause menu
    // (or an unexpected Kit rule) closes the panel without Expedition asking.
    private static void OnKitPanelClosed()
    {
        if (closingByExpedition || !wantOpen) return;
        closedByPause = true;
        pauseMenuGoneSince = -1f;
        if (Plugin.Detailed)
            Plugin.Trace($"[Expedition menu] Kit closed the panel while the flow wants it (pause menu?); screen '{page.Title}'.");
    }

    private static void HandleKitRequests()
    {
        if (backRequested)
        {
            backRequested = false;
            if (page.Back is { } back && !CampaignControllerHost.Busy)
            {
                if (Plugin.Detailed) Plugin.Trace($"[Expedition menu] back from '{page.Title}'.");
                back();
            }
            if (IsOpen) Build(); // the Kit page always follows the screen state
        }
        if (closeRequested)
        {
            closeRequested = false;
            if (page.CloseAction is { } close && !CampaignControllerHost.Busy)
            {
                if (Plugin.Detailed) Plugin.Trace($"[Expedition menu] Esc/close on '{page.Title}'.");
                close();
            }
        }
    }

    // Reopen once after the game's pause menu is gone (kept closed while it shows).
    private static void ReopenAfterPause()
    {
        if (!closedByPause || kitPanel == null) return;
        if (kitPanel.IsOpen) { closedByPause = false; return; }
        var pauseMenu = UnityEngine.Object.FindObjectOfType<NativePauseMenu>();
        if (pauseMenu != null && pauseMenu.gameObject.activeInHierarchy) { pauseMenuGoneSince = -1f; return; }
        if (pauseMenuGoneSince < 0f) { pauseMenuGoneSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - pauseMenuGoneSince < .3f) return;
        closedByPause = false;
        // Resuming from the native pause menu may restore its own time scale; keep the battle frozen.
        if (timeScaleApplied && Time.timeScale > 0f)
        {
            Time.timeScale = 0f;
            Plugin.Logger.LogInfo("Expedition native menu: battle re-frozen after the pause menu.");
        }
        ShowKitPanel("reopen after the pause menu");
        Build();
    }

    // The Kit frees the cursor only in its Game context (VehicleControlUI loaded) and
    // never touches it in the designer or the main menu, where the game leaves it free.
    // A campaign battle scene without that UI scene would otherwise keep a locked cursor
    // under the result screen, so only that case is handled here.
    // CURSOR-FALLBACK-BEGIN
    private static void FreeCursorOutsideKitGameContext()
    {
        if (!inBattle || UiKit.Context == UiContexts.Game) return;
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
    }
    // CURSOR-FALLBACK-END
}
#endif
