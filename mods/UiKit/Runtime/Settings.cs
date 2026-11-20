using SprocketKeybinds;
using System.Runtime.CompilerServices;
using System.Globalization;
namespace SprocketUiKit;

public abstract partial class Container
{
    /// <summary>Add a retained setting row (since 0.1.13): typed control, description tooltip, restart chip and conditional Reset. Slider writes are throttled to 250 ms and flushed on release; text commits on Enter or blur.</summary>
    public Row Setting(ModSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var state = SettingValueCache.Get(setting);
        var row = Add(new Row());
        row.SettingBinding = new SettingBinding(row, setting, state);
        return row;
    }
}

// ConfigEntry.BoxedValue boxes numeric types. Read it only on a change event, then poll the cache.
// The weak key and entry->cache cycle do not retain removed rows or their panels.
internal sealed class SettingValueCache
{
    private static readonly ConditionalWeakTable<BepInEx.Configuration.ConfigEntryBase, SettingValueCache> caches = new();
    internal object Value = null!;
    internal static SettingValueCache Get(ModSetting setting) => caches.GetValue(setting.Entry, entry =>
    {
        var cache = new SettingValueCache { Value = entry.BoxedValue };
        entry.ConfigFile.SettingChanged += (_, args) => { if (ReferenceEquals(args.ChangedSetting, entry)) cache.Value = entry.BoxedValue; };
        return cache;
    });
}

internal sealed class SettingBinding
{
    private readonly ModSetting setting;
    private readonly SettingValueCache cache;
    private readonly SettingWriteGate writes = new();
    private readonly object defaults;
    private readonly int kind;
    private object? seen;
    private string text = "";
    private double number;
    private bool toggle, reset;
    private int choice;
    private Slider? slider;
    internal SettingBinding(Row row, ModSetting setting, SettingValueCache cache)
    {
        this.setting = setting; this.cache = cache; defaults = setting.DefaultValue;
        kind = SettingsPolicy.Control((int)setting.Kind);
        row.Gap = 10;
        row.Label(setting.Label).Tooltip(setting.Description);
        row.Spacer();
        if (setting.RequiresRestart) row.Chip("Restart required");
        Read();
        switch (kind)
        {
            case 0: row.Toggle(() => toggle, v => setting.Value = v).Tooltip(setting.Description); break;
            case 1:
                slider = row.Slider(() => writes.Pending ? writes.Value : number, Queue, setting.Min, setting.Max,
                    setting.Step ?? (setting.Entry.SettingType == typeof(int) || setting.Entry.SettingType == typeof(long) ? 1 : 0))
                    .WithField().OnDragEnd(Flush).Width(260).Tooltip(setting.Description);
                break;
            case 2: row.Dropdown(setting.Choices, () => choice, i => setting.Value = setting.Choices[i]).Width(210).Tooltip(setting.Description); break;
            case 3: row.TextField(() => text, v => setting.Value = v).MaxChars(4096).Width(260).Tooltip(setting.Description); break;
            default:
                row.Label(() => text).Tooltip(setting.Description);
                UnknownSettingWarnings.Warn(setting);
                break;
        }
        row.Button("Reset", () => { writes.Take(0, true, out _); setting.Reset(); }).VisibleWhen(() => reset);
    }
    private void Read()
    {
        if (ReferenceEquals(seen, cache.Value)) return;
        seen = cache.Value;
        reset = SettingsPolicy.ResetVisible(seen, defaults);
        text = Convert.ToString(seen, CultureInfo.InvariantCulture) ?? "";
        if (kind == 0) toggle = seen is true;
        if (kind == 1) number = Convert.ToDouble(seen, CultureInfo.InvariantCulture);
        if (kind == 2) { choice = -1; for (int i = 0; i < setting.Choices.Count; i++) if (setting.Choices[i] == text) { choice = i; break; } }
    }
    private void Queue(double value)
    {
        if (setting.Entry.SettingType == typeof(int) || setting.Entry.SettingType == typeof(long)) value = Math.Round(value);
        writes.Queue(value);
        Commit(false);
    }
    private void Commit(bool release) { if (writes.Take(Kit.Now, release, out double value)) setting.Value = value; }
    internal void Flush() => Commit(true);
    internal static void FlushTree(Element element)
    {
        if (element is Row row) row.SettingBinding?.Flush();
        foreach (var child in element.Kids) FlushTree(child);
    }
    internal void Refresh(float now)
    {
        Commit(false); Read();
    }
}

internal static class UnknownSettingWarnings
{
    private static readonly ConditionalWeakTable<ModSetting, object> warned = new();
    internal static void Warn(ModSetting setting)
    {
        if (warned.TryGetValue(setting, out _)) return;
        warned.Add(setting, new object());
        Plugin.Instance.Log.LogWarning($"[UI Kit] Unsupported setting kind: {setting.OwnerId}/{setting.Group}/{setting.Key}; showing read-only value.");
    }
}

internal static class SettingsMenu
{
    private static Panel? panel;
    private static Column? navigation, content;
    private static string? owner;
    private static bool byPack, advanced;
    internal static void Register()
    {
        panel = UiKit.CreatePanel(UiKit.PluginGuid, "mod-settings", "Mod settings", width: 1040, height: 650);
        panel.AllowedContexts = UiContexts.All;
        panel.StayOpenWhilePaused = true;
        panel.CloseOnSceneChange = false;
        panel.OnOpen += Refresh;
        panel.OnClose += Flush;
        ModSettings.Changed += Changed;
        ModSettings.SetPresenter(Open, () => panel?.IsOpen == true);
    }
    private static void Open() { if (panel!.IsOpen) Refresh(); else panel.Open(); }
    private static void Changed() { if (panel?.IsOpen == true) Refresh(); }
    private static void Clear(Container container) { while (container.Children.Count > 0) container.Remove(container.Children[^1]); }
    private static void Flush()
    {
        if (content == null) return;
        SettingBinding.FlushTree(content);
    }
    private static void Refresh()
    {
        if (panel == null) return;
        Flush();
        Kit.LeftSubtree(panel);
        Clear(panel.Body);
        var toolbar = panel.Body.Row();
        toolbar.Segmented(new[] { "By author", "By pack" }, () => byPack ? 1 : 0, i => { byPack = i == 1; Refresh(); });
        toolbar.Spacer(); toolbar.Label("Show advanced");
        toolbar.Toggle(() => advanced, v => { advanced = v; Refresh(); });
        var columns = panel.Columns(270, 0); navigation = columns[0]; content = columns[1];
        var left = navigation.ScrollView().Fill();
        var right = content.ScrollView().Fill();
        var groups = ModSettings.Snapshot(byPack);
        ModSettingMod? selected = null, first = null;
        foreach (var group in groups)
        {
            left.Label(group.Name, TextStyle.Heading);
            foreach (var mod in group.Mods)
            {
                first ??= mod;
                if (mod.OwnerId == owner) selected = mod;
                left.Button(mod.Name, () => { owner = mod.OwnerId; Refresh(); });
            }
        }
        selected ??= first;
        if (selected == null) { right.Label("Mods can add global settings with ModSettings.Register").Wrap(); return; }
        owner = selected.OwnerId;
        var heading = right.Row(); heading.Label(selected.Author + " > " + selected.Name, TextStyle.Heading); heading.Spacer();
        var selectedOwner = owner;
        heading.Button("Reset all", () => panel.Confirm("Reset all settings?", "Restore defaults for " + selected.Name + "?", "Reset all", () => ModSettings.ResetOwner(selectedOwner)));
        foreach (var section in selected.Sections)
        {
            Stack? body = null;
            foreach (var setting in section.Settings)
            {
                if (!SettingsPolicy.Include(setting.IsVisible, setting.Advanced, advanced)) continue;
                body ??= right.Stack();
                if (body.Children.Count == 0) body.Label(section.Name, TextStyle.Heading);
                body.Setting(setting);
            }
        }
       
    }
    internal static void Unregister()
    {
        ModSettings.Changed -= Changed;
        ModSettings.SetPresenter(null, null);
        if (panel != null) UiKit.DestroyPanel(panel);
        panel = null;
    }
}
