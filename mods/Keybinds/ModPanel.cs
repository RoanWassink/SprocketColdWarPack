using Il2CppInterop.Runtime;
using Sprocket.SettingConfiguration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SprocketKeybinds;

// Owns presentation only: existing mod registrations, IDs and save data are unchanged.
internal static class ModPanel
{
    private static GameObject? panel, launcher;
    private static RectTransform? content, titleBar, footer, scrollArea, freeBlock, warningRow;
    private static TMP_Text? help, freeTitle, freeNote, warningText;
    private static RectTransform? escapeHint;
    private static readonly List<(TMP_Text Name, TMP_Text Keys)> freeGroups = new();
    private static Button? authorButton, packButton;
    private static Image? authorBorder, packBorder;
    private static bool byPack;
    private static bool freeKeyLayoutPending;
    private static float lastContentWidth, lastCanvasScale;
    private static int lastScreenWidth, lastScreenHeight;
    private static readonly HashSet<string> expandedParents = new(StringComparer.Ordinal);
    private static readonly HashSet<string> expandedMods = new(StringComparer.Ordinal);
    private sealed record Header(GameObject Root, Button Button, TMP_Text Label, TMP_Text Indicator, TMP_Text Count, TMP_Text? Detail, Image? Border);
    private static readonly Dictionary<string, Header> parentHeaders = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Header> headers = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, GameObject> clearButtons = new();
    internal static Transform Content => content!;
    private static float S(float value) => value * PanelStyle.Scale;
    private static RectTransform Rect(string name, Transform parent) => PanelStyle.Node(name, parent);
    private static void Fill(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(S(left), S(bottom)); rect.offsetMax = new Vector2(-S(right), -S(top));
    }
    private static TMP_Text Text(Transform parent, string name, string value, float size, Color color, bool caps = false, float spacing = 0)
    {
        var rect = Rect(name, parent); Fill(rect);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value; PanelStyle.Label(text, size, color, caps, spacing);
        return text;
    }
    private static Button Button(RectTransform rect, string label, Action click, out Image border, bool quiet = false, bool section = false, float fontSize = 14)
    {
        var image = PanelStyle.Box(rect, Color.white, 4, raycast: true);
        border = PanelStyle.Outline(rect, section ? Color.clear : PanelStyle.Border);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => Plugin.Safe(click))));
        var text = Text(rect, "Label", label, fontSize, quiet ? PanelStyle.Text2 : PanelStyle.Text);
        Fill(text.GetComponent<RectTransform>(), quiet ? 10 : 16, 0, quiet ? 10 : 16);
        text.alignment = TextAlignmentOptions.Center;
        PanelStyle.StyleButton(button, border, text, quiet);
        PanelStyle.ConfigureButton(button, quiet: quiet, section: section);
        return button;
    }
    private static void Grouping()
    {
        VanillaMenu.StopRebinding(); byPack = !byPack; Layout();
    }
    internal static void Ensure(SettingsMenu menu, GameObject templateRoot, RebindActionUI template)
    {
        if (panel != null) return;
        PanelStyle.Initialize(template.ActionLabel.font);
        PanelStyle.SetScale(PanelStyle.ReferenceScale(Screen.height, PanelStyle.CanvasScale(menu.keymappingGUI.transform)));
        var panelRect = Rect("ModKeybinds.Panel", menu.keymappingGUI.transform);
        panelRect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Fill(panelRect, 18, 18, 18, 18); panel = panelRect.gameObject;
        PanelStyle.Shadow(panelRect);
        // A child's shadow draws above a Graphic on its parent. Keep both as ordered children.
        var background = Rect("Style.Background", panelRect); Fill(background);
        PanelStyle.Box(background, PanelStyle.Panel, 6, raycast: true);
        titleBar = Rect("TitleBar", panelRect); PanelStyle.Box(titleBar, PanelStyle.Block, 6);
        Text(titleBar, "Title", "MOD KEYBINDS", 20, PanelStyle.Text, true, 6);
        Text(titleBar, "Subtitle", "Sprocket Keybinds " + Plugin.PluginVersion, 13, PanelStyle.Text2);
        var author = Rect("ByAuthor", titleBar);
        authorButton = Button(author, "By author", Grouping, out var aBorder, fontSize: 13); authorBorder = aBorder;
        var pack = Rect("ByPack", titleBar);
        packButton = Button(pack, "By pack", Grouping, out var pBorder, fontSize: 13); packBorder = pBorder;
        var close = Rect("Close", titleBar);
        Button(close, PanelStyle.Glyph('\u2715', "X"), () => { VanillaMenu.StopRebinding(); panel?.SetActive(false); }, out _);
        var divider = Rect("Divider", titleBar); PanelStyle.Box(divider, PanelStyle.Divider, 0);
        footer = Rect("Footer", panelRect); PanelStyle.Box(footer, PanelStyle.Block, 6);
        var footerDivider = Rect("Divider", footer); PanelStyle.Box(footerDivider, PanelStyle.Divider, 0);
        help = Text(footer, "Help", "Expand a mod. Clear removes a key; Reset restores its default. Apply / Accept saves.", 12, PanelStyle.Text2);
        escapeHint = Rect("CaptureHint", footer);
        var cap = Rect("KeyCap", escapeHint); PanelStyle.Box(cap, PanelStyle.Button, 3); PanelStyle.Outline(cap, PanelStyle.Border, 3);
        Text(cap, "Key", "Esc", 11, PanelStyle.Text2).alignment = TextAlignmentOptions.Center;
        Text(escapeHint, "Hint", "cancel capture", 12, PanelStyle.Text2);
        scrollArea = Rect("Scroll", panelRect);
        var scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = S(35);
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollArea); Fill(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var viewportImage = viewport.gameObject.AddComponent<Image>(); viewportImage.color = new Color(0, 0, 0, .01f);
        content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content;
        PanelStyle.Outline(panelRect, PanelStyle.Frame, 6);
        CreateFreeKeys(); Chrome(); panel.SetActive(false);

        var staging = new GameObject("ModKeybinds.LauncherStaging"); staging.SetActive(false);
        try
        {
            launcher = Object.Instantiate(templateRoot, staging.transform, false).Cast<GameObject>();
            launcher.name = "ModKeybinds.Open";
            var ui = launcher.GetComponentInChildren<RebindActionUI>(true);
            ui.enabled = false;
            ui.ActionLabel.text = "Mod keybinds";
            ui.BindingText.text = "Open";
            foreach (var tab in launcher.GetComponentsInChildren<Sprocket.UI.Tab>(true))
            {
                bool opens = ui.BindingText.transform.IsChildOf(tab.transform);
                if (!opens) { tab.gameObject.SetActive(false); continue; }
                tab.Clicked = null; tab.onClick = new UnityEvent();
                tab.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)Open));
                tab.enabled = true; tab.interactable = true; tab.SetState(Sprocket.Selection.TabState.Normal);
            }
            foreach (var button in launcher.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.GetComponent<Sprocket.UI.Tab>() != null) continue;
                if (!ui.BindingText.transform.IsChildOf(button.transform)) { button.gameObject.SetActive(false); continue; }
                button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)Open));
                button.interactable = true;
            }
            Object.Destroy(ui);
            launcher.transform.SetParent(templateRoot.transform.parent, false);
            launcher.transform.SetAsLastSibling(); launcher.SetActive(true);
        }
        finally { Object.Destroy(staging); }
    }
    private static void Chrome() => PanelStyle.Guard(() =>
    {
        if (panel == null || titleBar == null || footer == null || scrollArea == null || escapeHint == null) return;
        var rect = panel.GetComponent<RectTransform>(); Fill(rect, 18, 18, 18, 18);
        var shadow = rect.Find("Style.Shadow").GetComponent<RectTransform>();
        Fill(shadow, -24, -42, -24, -6); // 0 18 48: 24px blur support, 18px downward offset.
        Place(titleBar, 0, 44);
        Fill(titleBar.Find("Title").GetComponent<RectTransform>(), 14, 0, 490);
        Fill(titleBar.Find("Subtitle").GetComponent<RectTransform>(), 220, 0, 296);
        Right(titleBar.Find("ByAuthor").GetComponent<RectTransform>(), 166, 5, 110, 34);
        Right(titleBar.Find("ByPack").GetComponent<RectTransform>(), 56, 5, 110, 34);
        Right(titleBar.Find("Close").GetComponent<RectTransform>(), 12, 6, 32, 32);
        Bottom(titleBar.Find("Divider").GetComponent<RectTransform>(), 1);
        footer.anchorMin = footer.anchorMax = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(.5f, 0); footer.sizeDelta = new Vector2(0, S(32)); footer.anchoredPosition = Vector2.zero;
        Place(footer.Find("Divider").GetComponent<RectTransform>(), 0, 1);
        Fill(help!.GetComponent<RectTransform>(), 14, 0, VanillaMenu.Rebinding ? 170 : 14);
        Right(escapeHint, 14, 5, 146, 22);
        var cap = escapeHint.Find("KeyCap").GetComponent<RectTransform>();
        cap.anchorMin = cap.anchorMax = new Vector2(0, .5f); cap.pivot = new Vector2(0, .5f);
        cap.sizeDelta = new Vector2(S(34), S(22)); cap.anchoredPosition = Vector2.zero;
        Fill(escapeHint.Find("Hint").GetComponent<RectTransform>(), 42);
        Fill(scrollArea, 14, 46, 14, 58);
        scrollArea.GetComponent<ScrollRect>().scrollSensitivity = S(35);
        escapeHint.gameObject.SetActive(VanillaMenu.Rebinding);
        if (authorButton != null) PanelStyle.ConfigureButton(authorButton, !byPack);
        if (packButton != null) PanelStyle.ConfigureButton(packButton, byPack);
        PanelStyle.OutlineColor(authorBorder, byPack ? PanelStyle.Border : PanelStyle.Accent);
        PanelStyle.OutlineColor(packBorder, byPack ? PanelStyle.Accent : PanelStyle.Border);
        // All own labels have explicit padding reapplied when the screen/canvas scale changes.
        foreach (var button in panel.GetComponentsInChildren<Button>(true))
        {
            if (VanillaMenu.Rows.Any(r => button.transform.IsChildOf(r.Root.transform))) continue;
            var label = button.transform.Find("Label");
            if (label != null) Fill(label.GetComponent<RectTransform>(), button.name.StartsWith("Clear.") ? 10 : 16, 0, button.name.StartsWith("Clear.") ? 10 : 16);
        }
        Fill(titleBar.Find("Close/Label").GetComponent<RectTransform>());
    });
    private static void Bottom(RectTransform rect, float height)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0);
        rect.sizeDelta = new Vector2(0, S(height)); rect.anchoredPosition = Vector2.zero;
    }
    private static void Right(RectTransform rect, float right, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(S(width), S(height)); rect.anchoredPosition = new Vector2(-S(right), -S(y));
    }
    private static void Open() { if (panel == null) return; Layout(); panel.transform.SetAsLastSibling(); panel.SetActive(true); }
    private static void Place(RectTransform rect, float y, float height, float left = 0, float right = 0)
    {
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(-S(left + right), S(height)); rect.anchoredPosition = new Vector2(S((left - right) / 2), -S(y));
    }
    private static Header CreateHeader(string name, string key, bool parent)
    {
        var rect = Rect(name, content!);
        var button = Button(rect, "", () =>
        {
            VanillaMenu.StopRebinding();
            var expanded = parent ? expandedParents : expandedMods;
            if (!expanded.Remove(key)) expanded.Add(key); Layout();
        }, out var border, section: parent);
        var label = rect.Find("Label").GetComponent<TMP_Text>();
        // Button's existing label is reused; no clickable overlay or additional event path.
        PanelStyle.Label(label, parent ? 15 : 14, parent ? PanelStyle.Heading : PanelStyle.Text, parent, parent ? 10 : 0);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        var indicator = Text(rect, "Indicator", "+", 14, parent ? PanelStyle.Heading : PanelStyle.Text);
        var tag = Rect("Tag", rect);
        var count = Text(tag, "Count", "", 12, PanelStyle.Faint);
        count.alignment = TextAlignmentOptions.MidlineRight;
        if (!parent)
        {
            PanelStyle.Box(tag, PanelStyle.Button, 4);
            count.transform.SetAsLastSibling();
        }
        var detail = parent ? null : Text(rect, "OwnerDetail", key, 12, PanelStyle.Text2);
        return new(rect.gameObject, button, label, indicator, count, detail, border);
    }
    private static void HeaderLayout(Header header, bool parent, bool open, bool duplicate, string name, int count) => PanelStyle.Guard(() =>
    {
        header.Indicator.text = open ? PanelStyle.Glyph('\u25be', "-") : PanelStyle.Glyph('\u25b8', "+");
        header.Label.text = parent ? name.ToUpperInvariant() : name;
        header.Count.text = count + (parent ? " MODS" : " ACTIONS");
        Fill(header.Indicator.GetComponent<RectTransform>(), 10, 0, Math.Max(0, header.Root.GetComponent<RectTransform>().rect.width / PanelStyle.Scale - 26));
        Fill(header.Label.GetComponent<RectTransform>(), 30, duplicate ? 16 : 0, 108);
        Right(header.Count.transform.parent.GetComponent<RectTransform>(), 10, duplicate ? 4 : 9.5f, 94, 17);
        Fill(header.Count.GetComponent<RectTransform>(), 4, 0, 4);
        if (header.Detail != null)
        {
            header.Detail.gameObject.SetActive(duplicate);
            Fill(header.Detail.GetComponent<RectTransform>(), 30, 2, 108, 22);
        }
        PanelStyle.ConfigureButton(header.Button, chosen: open, section: parent);
        if (!parent)
        {
            var colors = header.Button.colors; colors.normalColor = open ? PanelStyle.Selected : PanelStyle.Row; header.Button.colors = colors;
            PanelStyle.OutlineColor(header.Border, open ? PanelStyle.Accent : PanelStyle.Divider);
        }
    });
    internal static void Layout()
    {
        if (content == null || panel == null) return;
        float canvasScale = PanelStyle.CanvasScale(panel.transform);
        PanelStyle.SetScale(PanelStyle.ReferenceScale(Screen.height, canvasScale));
        lastCanvasScale = canvasScale; lastScreenHeight = Screen.height; lastScreenWidth = Screen.width;
        Chrome();
        foreach (var item in clearButtons.Where(p => !VanillaMenu.Rows.Any(r => r.Root.Pointer == p.Key)).ToArray())
        {
            if (item.Value != null) Object.Destroy(item.Value); clearButtons.Remove(item.Key);
        }
        foreach (var item in headers.Where(p => !VanillaMenu.Rows.Any(r => r.Binding.Id.Split('/')[0] == p.Key)).ToArray())
        {
            if (item.Value.Root != null) Object.Destroy(item.Value.Root); headers.Remove(item.Key);
        }
        float y = 0;
        var rowGroups = VanillaMenu.Rows.GroupBy(r => r.Binding.Id.Split('/')[0]).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var presentation = Keybinds.Metadata.Order(rowGroups.Select(g => (g.Key, g.Value[0].Binding.ModName)), byPack);
        foreach (var item in parentHeaders.Where(h => !presentation.Any(p => p.GroupKey == h.Key)).ToArray())
        {
            if (item.Value.Root != null) Object.Destroy(item.Value.Root); parentHeaders.Remove(item.Key);
        }
        foreach (var parent in presentation.GroupBy(p => p.GroupKey))
        {
            var parentKey = parent.Key;
            if (!parentHeaders.TryGetValue(parentKey, out var parentHeader) || parentHeader.Root == null)
                parentHeaders[parentKey] = parentHeader = CreateHeader("Parent." + parentKey, parentKey, true);
            bool parentOpen = expandedParents.Contains(parentKey);
            Place(parentHeader.Root.GetComponent<RectTransform>(), y, 36);
            HeaderLayout(parentHeader, true, parentOpen, false, parent.First().GroupName, parent.Count()); y += 40;
            foreach (var info in parent)
            {
                var owner = info.Owner; var group = rowGroups[owner];
                if (!headers.TryGetValue(owner, out var header) || header.Root == null)
                    headers[owner] = header = CreateHeader("Group." + owner, owner, false);
                bool open = expandedMods.Contains(owner), show = parentOpen && open;
                bool duplicate = parent.Count(p => p.Mod == info.Mod) > 1;
                header.Root.SetActive(parentOpen);
                if (parentOpen)
                {
                    Place(header.Root.GetComponent<RectTransform>(), y, duplicate ? 40 : 36, 12);
                    HeaderLayout(header, false, open, duplicate, (info.Pack ? info.Mod.Author + "/" : "") + info.Mod.Name, group.Select(r => r.Binding.Id).Distinct().Count());
                    y += (duplicate ? 40 : 36) + 4;
                }
                foreach (var row in group)
                {
                    row.Root.SetActive(show);
                    if (!clearButtons.TryGetValue(row.Root.Pointer, out var clear) || clear == null)
                    {
                        var clearRect = Rect("Clear." + row.Binding.Id, content); clear = clearRect.gameObject; clearButtons[row.Root.Pointer] = clear;
                        int slot = Enumerable.Range(0, row.Binding.Action.bindings.Count).First(i => row.Binding.Action.bindings[i].id.ToString() == row.UI.BindingId);
                        Button(clearRect, "Clear", () => { VanillaMenu.StopRebinding(); row.Binding.Unbind(slot); }, out _, quiet: true, fontSize: 12);
                    }
                    clear.SetActive(show);
                    PanelStyle.StyleRow(row.Root);
                    if (!show) continue;
                    Place(row.Root.GetComponent<RectTransform>(), y, 36, 24, 72);
                    Right(clear.GetComponent<RectTransform>(), 0, y + 4, 64, 28); y += 40;
                }
            }
        }
        UpdateFreeKeys(); LayoutFreeKeys(y);
        content.sizeDelta = new Vector2(0, S(y + FreeBlockHeight + 4));
        lastContentWidth = content.rect.width; freeKeyLayoutPending = false;
    }
    private static void CreateFreeKeys()
    {
        freeBlock = Rect("FreeKeys", content!); PanelStyle.Box(freeBlock, PanelStyle.Block); PanelStyle.Outline(freeBlock, PanelStyle.Border);
        freeTitle = Text(freeBlock, "Title", "FREE SINGLE KEYS", 12, PanelStyle.Text2, true, 10);
        foreach (var name in new[] { "Letters", "Digits", "F keys", "Other" })
        {
            var row = Rect("Keys." + name, freeBlock);
            var label = Text(row, "Group", name, 12, PanelStyle.Faint);
            var keys = Text(row, "Keys", "", 13, PanelStyle.Value); PanelStyle.Wrapped(keys);
            freeGroups.Add((label, keys));
        }
        freeNote = Text(freeBlock, "Note", "Keys used only in combos are free for single use.", 12, PanelStyle.Faint); PanelStyle.Wrapped(freeNote);
        warningRow = Rect("Warning", freeBlock); PanelStyle.Box(warningRow, PanelStyle.Warning); PanelStyle.Outline(warningRow, PanelStyle.WarningBorder);
        Text(warningRow, "Icon", PanelStyle.Glyph('\u26a0', "!"), 14, PanelStyle.DangerText);
        warningText = Text(warningRow, "Message", "vanilla onbekend. No verified free keys available.", 12, PanelStyle.DangerText); PanelStyle.Wrapped(warningText);
    }
    private static float FreeBlockHeight = 140;
    private static void LayoutFreeKeys(float y) => PanelStyle.Guard(() =>
    {
        if (content == null || freeBlock == null || freeTitle == null || freeNote == null || warningRow == null || warningText == null) return;
        float width = Math.Max(1, content.rect.width / PanelStyle.Scale - 24);
        Place(freeTitle.GetComponent<RectTransform>(), 12, 16, 12, 12);
        float rowY = 36;
        if (warningRow.gameObject.activeSelf)
        {
            float height = Math.Max(32, PanelStyle.Height(warningText, S(Math.Max(1, width - 42))) / PanelStyle.Scale + 16);
            Place(warningRow, rowY, height, 12, 12);
            Fill(warningRow.Find("Icon").GetComponent<RectTransform>(), 10, 0, Math.Max(0, width - 28));
            Fill(warningText.GetComponent<RectTransform>(), 32, 8, 10, 8); rowY += height + 8;
        }
        else foreach (var group in freeGroups)
        {
            float height = Math.Max(17, PanelStyle.Height(group.Keys, S(Math.Max(1, width - 80))) / PanelStyle.Scale);
            Place(group.Name.transform.parent.GetComponent<RectTransform>(), rowY, height, 12, 12);
            Fill(group.Name.GetComponent<RectTransform>(), 0, 0, Math.Max(0, width - 72));
            Fill(group.Keys.GetComponent<RectTransform>(), 80); rowY += height + 8;
        }
        float noteHeight = PanelStyle.Height(freeNote, S(width)) / PanelStyle.Scale;
        Place(freeNote.GetComponent<RectTransform>(), rowY, noteHeight, 12, 12);
        FreeBlockHeight = rowY + noteHeight + 12;
        Place(freeBlock, y, FreeBlockHeight);
    });
    internal static void UpdateFreeKeys()
    {
        if (freeBlock == null || panel == null || warningRow == null) return;
        var vanilla = VanillaBindings.Read();
        var available = FreeKeys.Calculate(vanilla.Paths, Keybinds.Entries.SelectMany(e => e.Bindings), vanilla.Known);
        if (warningRow.gameObject.activeSelf == vanilla.Known) { warningRow.gameObject.SetActive(!vanilla.Known); freeKeyLayoutPending = true; }
        for (int i = 0; i < freeGroups.Count; i++)
        {
            var group = freeGroups[i]; group.Name.transform.parent.gameObject.SetActive(vanilla.Known);
            var keys = FreeKeys.Groups[i].Where(k => available.Contains(k)).ToArray();
            var value = keys.Length == 0 ? "none" : string.Join(", ", keys.Take(8).Select(k => ButtonBinding.Parse("<Keyboard>/" + k).Display)) + (keys.Length > 8 ? " ..." : "");
            if (group.Keys.text != value) { group.Keys.text = value; freeKeyLayoutPending = true; }
        }
    }
    internal static void TickLayout()
    {
        if (content == null || panel == null || !panel.activeInHierarchy) return;
        PanelStyle.Tick();
        if (escapeHint != null)
        {
            escapeHint.gameObject.SetActive(VanillaMenu.Rebinding);
            if (help != null) Fill(help.GetComponent<RectTransform>(), 14, 0, VanillaMenu.Rebinding ? 170 : 14);
        }
        float canvasScale = PanelStyle.CanvasScale(panel.transform);
        if (freeKeyLayoutPending || Math.Abs(lastContentWidth - content.rect.width) > 1 || lastScreenHeight != Screen.height || lastScreenWidth != Screen.width || Math.Abs(lastCanvasScale - canvasScale) > .0001f)
            Layout();
    }
    internal static void Cleanup()
    {
        if (panel != null) Object.Destroy(panel);
        if (launcher != null) Object.Destroy(launcher);
        panel = null; launcher = null; content = null; titleBar = footer = scrollArea = freeBlock = warningRow = escapeHint = null;
        help = freeTitle = freeNote = warningText = null; authorButton = packButton = null; authorBorder = packBorder = null;
        freeKeyLayoutPending = false; freeGroups.Clear(); headers.Clear(); parentHeaders.Clear(); clearButtons.Clear();
        PanelStyle.Cleanup();
    }
}
