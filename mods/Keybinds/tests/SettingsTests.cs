using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Configuration;
using SprocketKeybinds;
using System.Collections;

// Registry is tested without loading the plugin or any Unity object.
namespace SprocketKeybinds
{
    internal static class Keybinds { internal static readonly ModMetadata Metadata = new(); }
}
internal static class SettingsTests
{
    private enum Mode { First, Second, Third }
    private enum Aliases { Original = 1, Alias = 1, Other = 2 }
    internal static void Run(string temp, Action<bool, string> check)
    {
        var properties = new[] { "OwnerId", "Author", "ModName", "Group", "Key", "Label", "Description", "Kind", "Min", "Max", "Step", "Choices", "RequiresRestart", "Advanced" };
        check(typeof(ModSetting).GetFields().Length == 0, "ModSetting exposes no public fields");
        foreach (var name in properties)
        {
            var property = typeof(ModSetting).GetProperty(name);
            check(property != null && property.CanRead && property.GetSetMethod(true) == null, "ModSetting getter-only property " + name);
        }
        check(typeof(SettingOptions).GetFields().Length == 0, "SettingOptions exposes no public fields");
        foreach (var name in new[] { "Label", "Description", "Group", "RequiresRestart", "Advanced", "Step", "VisibleWhen" })
        {
            var property = typeof(SettingOptions).GetProperty(name);
            check(property?.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true, "SettingOptions read/write property " + name);
        }
        var listener = new Listener(); Logger.Listeners.Add(listener);
        var cfgPath = Path.Combine(temp, "settings.cfg");
        var file = new ConfigFile(cfgPath, true) { SaveOnConfigSet = true };
        ConfigEntry<T> Bind<T>(string key, T value, AcceptableValueBase? range = null, string section = "General") =>
            file.Bind(section, key, value, new ConfigDescription("Description " + key, range));
        var toggle = Bind("Toggle", true);
        check(ModSettings.All.Count == 0 && !ModSettings.HasPresenter && !ModSettings.IsOpen, "settings usable before plugin Load");
        check(ModSettings.Describe(toggle)!.Kind == SettingKind.Toggle, "bool maps to Toggle");
        var integer = Bind("Integer", 3, new AcceptableValueRange<int>(-10, 10));
        var wide = Bind("Long", 3L, new AcceptableValueRange<long>(-9007199254740999L, 9007199254740999L));
        var single = Bind("Float", 1f, new AcceptableValueRange<float>(-2f, 2f));
        var number = Bind("Double", 1d, new AcceptableValueRange<double>(-2d, 2d));
        foreach (var entry in new ConfigEntryBase[] { integer, wide, single, number })
        {
            var setting = ModSettings.Describe(entry)!;
            check(setting.Kind == SettingKind.Number, "ranged " + entry.SettingType.Name + " maps to Number");
            check(setting.Min == Convert.ToDouble(entry.Description.AcceptableValues!.GetType().GetProperty("MinValue")!.GetValue(entry.Description.AcceptableValues))
                && setting.Max == Convert.ToDouble(entry.Description.AcceptableValues.GetType().GetProperty("MaxValue")!.GetValue(entry.Description.AcceptableValues)), "numeric bounds " + entry.SettingType.Name);
            check(setting.Step == (entry.SettingType == typeof(int) || entry.SettingType == typeof(long) ? 1f : null), "default numeric step " + entry.SettingType.Name);
        }
        foreach (var entry in new ConfigEntryBase[] { Bind("BareInt", 1), Bind("BareLong", 1L), Bind("BareFloat", 1f), Bind("BareDouble", 1d) })
            check(ModSettings.Describe(entry) == null, "unranged " + entry.SettingType.Name + " rejected");
        foreach (var entry in new ConfigEntryBase[] { Bind("Decimal", 1m), Bind("Short", (short)1), Bind("Unsigned", 1u),
            new RawEntry(file, typeof(object), new object()), new RawEntry(file, typeof(KeyboardShortcut), default(KeyboardShortcut)) })
            check(ModSettings.Describe(entry) == null, "unsupported " + entry.SettingType.Name + " rejected");
        var text = Bind("Text", "hello"); var mode = Bind("Enum", Mode.First);
        bool enumListConstraint = false;
        try { typeof(AcceptableValueList<>).MakeGenericType(typeof(Mode)); } catch (ArgumentException) { enumListConstraint = true; }
        check(enumListConstraint, "reference BepInEx rejects enum acceptable-list type constraint");
        Console.WriteLine("SKIP enum-list subset instantiation: reference BepInEx requires IEquatable<T>, which enums do not implement.");
        var list = Bind("List", "red", new AcceptableValueList<string>("red", "blue"));
        var numbers = Bind("NumbersList", 2, new AcceptableValueList<int>(2, 4));
        check(ModSettings.Describe(text)!.Kind == SettingKind.Text, "string maps to Text");
        check(ModSettings.Describe(mode)!.Choices.SequenceEqual(new[] { "First", "Second", "Third" }), "enum exposes names");
        check(ModSettings.Describe(Bind("Aliases", Aliases.Original))!.Choices.SequenceEqual(Enum.GetNames<Aliases>()), "enum aliases retain names");
        check(ModSettings.Describe(list)!.Kind == SettingKind.Choice && ModSettings.Describe(list)!.Choices.SequenceEqual(new[] { "red", "blue" }), "string list maps to Choice");
        check(ModSettings.Describe(numbers)!.Kind == SettingKind.Choice, "other acceptable lists map to Choice");
        int visibilityCalls = 0;
        var opts = new SettingOptions { Label = "Custom", Description = "Help", Group = "First section", Step = .25f,
            Advanced = true, RequiresRestart = true, VisibleWhen = () => { visibilityCalls++; return false; } };
        var described = ModSettings.Describe(integer, opts)!;
        check(described.Label == "Custom" && described.Description == "Help" && described.Group == "First section" && described.Step == .25f
            && described.Advanced && described.RequiresRestart && described.Entry == integer && (int)described.DefaultValue == 3, "setting options and config source retained");
        check(visibilityCalls == 0 && !described.IsVisible && visibilityCalls == 1, "VisibleWhen runs only via IsVisible");
        ModSettings.Register("test.visible", toggle, opts);
        check(visibilityCalls == 1, "Register does not evaluate VisibleWhen");
        var defaults = ModSettings.Describe(toggle)!;
        check(defaults.Key == "Toggle" && defaults.Label == "Toggle" && defaults.Description == "Description Toggle" && defaults.Group == "General" && defaults.IsVisible, "default presentation from cfg");
        check(((IList<string>)ModSettings.Describe(list)!.Choices).IsReadOnly, "choices are immutable");
        var value = ModSettings.Register("test.values", integer)!;
        int changes = 0; integer.SettingChanged += (_, _) => changes++;
        var stable = ModSettings.All; var stableSnapshot = ModSettings.Snapshot();
        value.Value = 6.6d; check(integer.Value == 7 && changes == 1, "number rounds and fires native SettingChanged");
        value.Value = -2.5m; check(integer.Value == -3, "integral midpoint rounds away from zero");
        value.Value = 500L; check(integer.Value == 10, "BepInEx clamps numeric values");
        check(File.ReadAllText(cfgPath).Contains("Integer = 10"), "BepInEx writes the mod cfg immediately");
        value.Reset(); check(integer.Value == 3 && File.ReadAllText(cfgPath).Contains("Integer = 3"), "Reset restores persisted default");
        var floatSetting = ModSettings.Describe(single)!; floatSetting.Value = 1.25d;
        check(single.Value == 1.25f, "double converts to single entry type");
        ModSettings.Describe(number)!.Value = 2m; check(number.Value == 2d, "decimal converts to double entry type");
        ModSettings.Describe(wide)!.Value = 9007199254740993L;
        check(wide.Value == 9007199254740993L, "Int64 precision preserved above double exact range");
        ModSettings.Describe(mode)!.Value = "Third"; check(mode.Value == Mode.Third, "enum name accepted");
        ModSettings.Describe(mode)!.Value = "1"; check(mode.Value == Mode.Second, "enum numeric string accepted");
        ModSettings.Describe(mode)!.Value = Mode.First; check(mode.Value == Mode.First, "enum own type accepted");
        ModSettings.Describe(list)!.Value = "blue"; check(list.Value == "blue", "list string accepted");
        ModSettings.Describe(numbers)!.Value = "4"; check(numbers.Value == 4, "list value string accepted");
        ModSettings.Describe(numbers)!.Value = 2; check(numbers.Value == 2, "list own type accepted");
        ModSettings.Describe(text)!.Value = "changed"; check(text.Value == "changed", "text value writes cfg source");
        ModSettings.Describe(toggle)!.Value = false; check(!toggle.Value, "toggle value writes cfg source");
        foreach (var bad in new object[] { "oops", "8", null!, double.NaN, double.PositiveInfinity, new object(), true, double.MaxValue, Mode.First }) value.Value = bad;
        check(integer.Value == 3, "invalid number inputs never escape or alter value");
        floatSetting.Value = double.MaxValue; check(single.Value == 1.25f, "float overflow ignored");
        ModSettings.Describe(list)!.Value = "missing"; check(list.Value == "blue", "invalid choice ignored");
        ModSettings.Describe(text)!.Value = 3; check(text.Value == "changed", "invalid text ignored");
        ModSettings.Describe(toggle)!.Value = "true"; check(!toggle.Value, "invalid toggle ignored");
        check(ReferenceEquals(stable, ModSettings.All) && ReferenceEquals(stableSnapshot, ModSettings.Snapshot()), "value edits preserve registry caches");
        ModSettings.Register("test.values", toggle); integer.Value = 8; toggle.Value = false; ModSettings.ResetOwner("test.values");
        check(integer.Value == 3 && toggle.Value && File.ReadAllText(cfgPath).Contains("Toggle = true"), "ResetOwner persists every owner entry");
        var reload = new ConfigFile(cfgPath, false);
        check(reload.Bind("General", "Integer", 0).Value == 3 && reload.Bind("General", "Toggle", false).Value, "fresh ConfigFile reload sees changes");
        int warnings = listener.Warnings.Count;
        for (int repeat = 0; repeat < 2; repeat++)
        {
            check(ModSettings.Register(null!, toggle) == null, "null owner safely rejected");
            check(ModSettings.Register("bad/id", toggle) == null && ModSettings.Register("", toggle) == null, "invalid and empty owner safely rejected");
            check(ModSettings.Register("valid.owner", null!) == null && ModSettings.Describe(null!) == null, "null entry safely rejected");
            check(ModSettings.RegisterSection("valid.owner", null!, "Absent") == 0, "null config safely rejected");
            check(ModSettings.RegisterSection("valid.owner", file, "Empty") == 0, "empty section safely rejected");
        }
        check(listener.Warnings.Count == warnings + 5, "one warning per invalid owner, null entry and empty section cause");
        var visible = ModSettings.Describe(text, new SettingOptions { VisibleWhen = () => throw new Exception("predicate") })!;
        warnings = listener.Warnings.Count;
        check(visible.IsVisible && visible.IsVisible && listener.Warnings.Count == warnings + 1, "throwing visibility fails visible and warns once");
        int eventCalls = 0; Action throwing = () => throw new Exception("handler");
        Action healthy = () => { eventCalls++; check(!ReferenceEquals(stable, ModSettings.All), "caches replaced before Changed"); };
        ModSettings.Changed += throwing; ModSettings.Changed += healthy; ModSettings.Register("test.values", text);
        check(eventCalls == 1, "throwing Changed handler cannot stop later handlers or Register");
        var cachedAll = ModSettings.All; var cachedAuthors = ModSettings.Snapshot(); var cachedPacks = ModSettings.Snapshot(true);
        check(ReferenceEquals(ModSettings.Register("test.values", integer, new SettingOptions { Label = "Ignored" }), value)
            && ReferenceEquals(cachedAll, ModSettings.All) && eventCalls == 1, "duplicate returns original without Changed");
        var otherFile = new ConfigFile(Path.Combine(temp, "duplicate.cfg"), false);
        check(ReferenceEquals(ModSettings.Register("test.values", otherFile.Bind("General", "Integer", 99)), value), "duplicate definition across files returns original");
        check(!ModSettings.Unregister("test.values", null!) && !ModSettings.Unregister("bad/id", integer.Definition)
            && !ModSettings.Unregister("absent.owner", integer.Definition), "invalid and missing unregister safe");
        ModSettings.ResetOwner(null!); ModSettings.ResetOwner("bad/id");
        ModSettings.Changed -= throwing; ModSettings.Changed -= healthy;
        var sectionFile = new ConfigFile(Path.Combine(temp, "section.cfg"), false);
        sectionFile.Bind("Diagnostics", "First", true); sectionFile.Bind("Diagnostics", "Unsupported", 2);
        check(ModSettings.RegisterSection("test.section", sectionFile, "Diagnostics") == 1, "RegisterSection counts supported current entries");
        sectionFile.Bind("Diagnostics", "Late", false);
        check(!ModSettings.All.Any(s => s.OwnerId == "test.section" && s.Key == "Late"), "RegisterSection does not grow automatically");
        check(ModSettings.RegisterSection("test.section", sectionFile, "Diagnostics") == 2, "RegisterSection repeat includes current entries idempotently");
        ModSettings.Register("test.order.z", toggle, new SettingOptions { Group = "Second" });
        ModSettings.Register("test.order.z", integer, new SettingOptions { Group = "First" });
        ModSettings.Register("test.order.z", text, new SettingOptions { Group = "Second" });
        ModSettings.Register("test.order.a", toggle); ModSettings.Register("test.order.other", toggle);
        cachedAll = ModSettings.All; cachedAuthors = ModSettings.Snapshot(); cachedPacks = ModSettings.Snapshot(true);
        Keybinds.Metadata.Register("test.order.z", "A", "Same"); Keybinds.Metadata.Register("test.order.a", "A", "Same");
        Keybinds.Metadata.Register("test.order.other", "B", "Same");
        var handle = ModSettings.All.Single(s => s.OwnerId == "test.order.z" && s.Key == "Toggle");
        check(handle.OwnerId == "test.order.z" && handle.Author == "A" && handle.ModName == "Same", "read-only metadata properties reflect late metadata");
        Keybinds.Metadata.RegisterPack("test.order.z", "example.pack", "A pack"); Keybinds.Metadata.RegisterPack("test.order.a", "example.pack", "A pack");
        check(!ReferenceEquals(cachedAll, ModSettings.All) && !ReferenceEquals(cachedAuthors, ModSettings.Snapshot())
            && !ReferenceEquals(cachedPacks, ModSettings.Snapshot(true)), "new metadata replaces all caches");
        foreach (bool byPack in new[] { false, true })
        {
            var snapshot = ModSettings.Snapshot(byPack);
            var expected = Keybinds.Metadata.Order(ModSettings.All.Select(s => s.OwnerId).Distinct().Select(o => (o, o)), byPack);
            check(snapshot.SelectMany(g => g.Mods).Select(m => m.OwnerId).SequenceEqual(expected.Select(p => p.Owner)), "Snapshot order equals metadata Order, byPack=" + byPack);
            check(ReferenceEquals(snapshot, ModSettings.Snapshot(byPack)), "Snapshot cached by mode " + byPack);
            check(((IList)snapshot).IsReadOnly && snapshot.All(g => ((IList)g.Mods).IsReadOnly
                && g.Mods.All(m => ((IList)m.Sections).IsReadOnly && m.Sections.All(s => ((IList)s.Settings).IsReadOnly))), "snapshot collections immutable " + byPack);
            check(snapshot.SelectMany(g => g.Mods).All(m => { var p = Keybinds.Metadata.Resolve(m.OwnerId, m.OwnerId, byPack);
                return m.Name == p.Mod.Name && m.Author == p.Mod.Author; }), "Snapshot names and authors use Resolve " + byPack);
        }
        var mod = ModSettings.Snapshot().SelectMany(g => g.Mods).Single(m => m.OwnerId == "test.order.z");
        check(mod.Sections.Select(s => s.Name).SequenceEqual(new[] { "Second", "First" })
            && mod.Sections[0].Settings.Select(s => s.Key).SequenceEqual(new[] { "Toggle", "Text" }), "section and setting registration order retained");
        cachedAll = ModSettings.All; Keybinds.Metadata.Register("test.order.z", "A", "Same");
        check(ReferenceEquals(cachedAll, ModSettings.All), "identical metadata preserves cache");
        check(ModSettings.Unregister("test.order.z", toggle.Definition), "Unregister removes owner definition");
        ModSettings.Register("test.order.z", toggle, new SettingOptions { Group = "Second" });
        check(ModSettings.Snapshot().SelectMany(g => g.Mods).Single(m => m.OwnerId == "test.order.z").Sections[0].Settings.Last().Key == "Toggle", "re-registration appends after removal");
        check(((IList)ModSettings.All).IsReadOnly && !ReferenceEquals(cachedAll, ModSettings.All), "All immutable and replaced after mutation");
        check(cachedAll.Count > 0 && cachedAuthors.Count > 0, "old cache instances remain readable");
        int infos = listener.Infos; ModSettings.Open(); ModSettings.Open();
        check(listener.Infos == infos + 1, "missing presenter logs info once");
        int opened = 0; ModSettings.SetPresenter(() => opened++, () => true); ModSettings.Open();
        check(ModSettings.HasPresenter && ModSettings.IsOpen && opened == 1, "presenter opens and reports live state");
        var beforePresenter = ModSettings.All; warnings = listener.Warnings.Count;
        ModSettings.SetPresenter(() => throw new Exception("open"), () => throw new Exception("state"));
        ModSettings.Open(); ModSettings.Open(); check(!ModSettings.IsOpen && !ModSettings.IsOpen, "throwing presenter never escapes");
        ModSettings.SetPresenter(() => opened += 10, () => false); ModSettings.Open();
        check(opened == 11 && listener.Warnings.Count == warnings + 3, "replacement, open and state each warn once");
        ModSettings.SetPresenter(null, null);
        check(!ModSettings.HasPresenter && !ModSettings.IsOpen && ReferenceEquals(beforePresenter, ModSettings.All), "clearing presenter preserves caches");
        check(visibilityCalls == 1, "registry never invokes visibility during mutations or snapshots");
        Logger.Listeners.Remove(listener);
    }
    private sealed class RawEntry : ConfigEntryBase
    {
        internal RawEntry(ConfigFile file, Type type, object value) : base(file, new ConfigDefinition("Raw", type.Name), type, value, new ConfigDescription("unsupported")) { BoxedValue = value; }
        public override object BoxedValue { get; set; } = null!;
    }
    private sealed class Listener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;
        internal readonly List<string> Warnings = new(); internal int Infos;
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            if (eventArgs.Source.SourceName != "Sprocket Mod Settings") return;
            if (eventArgs.Level == LogLevel.Warning) Warnings.Add(eventArgs.Data.ToString()!);
            if (eventArgs.Level == LogLevel.Info) Infos++;
        }
        public void Dispose() { }
    }
}
