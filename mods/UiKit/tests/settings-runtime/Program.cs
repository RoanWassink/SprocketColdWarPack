using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using SprocketKeybinds;
using SprocketUiKit;
using UIStack = SprocketUiKit.Stack;

var roots = args;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var root in roots) { var path = Path.Combine(root, name.Name + ".dll"); if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)); }
    return null;
};
try { Assembly.GetExecutingAssembly().GetType("RuntimeTests")!.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null); } catch (Exception e) { Console.WriteLine(e); Environment.Exit(1); }

static class RuntimeTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); count++; }
    static object Field(object obj, string name) => obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    static List<Element> Children(Container c) => (List<Element>)typeof(Container).GetField("Children", Private)!.GetValue(c)!;
    static object Binding(Row row) => Field(row, "SettingBinding");
    static void Poll(Row row) => Binding(row).GetType().GetMethod("Refresh", Private)!.Invoke(Binding(row), new object[] { 0f });
    static T Control<T>(Row row) where T : Element => Children(row).OfType<T>().Single();
    static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Kids) foreach (var descendant in Descendants(child)) yield return descendant;
    }
    static ModSetting Describe(ConfigEntryBase entry) => ModSettings.Describe(entry)!;
    enum Mode { One, Two, Three }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        Check(ModSettings.Snapshot().Count == 0, "real empty registry snapshot");
        SettingsMenu.Register(); ModSettings.Open();
        var menu = (Panel)typeof(SettingsMenu).GetField("panel", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(menu.IsOpen && ModSettings.IsOpen && Descendants(menu.Body).OfType<Label>().Any(l => l.Text == "Mods can add global settings with ModSettings.Register"), "empty presenter view explains registration and reports open");
        var configPath = Path.GetFullPath("logs/0.1.13/runtime-settings.cfg");
        if (File.Exists(configPath)) File.Delete(configPath);
        var file = new ConfigFile(configPath, false) { SaveOnConfigSet = true };
        var toggle = file.Bind("Test", "Toggle", false, "Toggle description");
        var number = file.Bind("Test", "Number", 5, new ConfigDescription("Range", new AcceptableValueRange<int>(0, 20)));
        var text = file.Bind("Test", "Text", "initial", "Text description");
        var choice = file.Bind("Test", "Choice", Mode.One, "Enum");
        ModSettings.Register("test.settings", toggle);
        ModSettings.Register("test.settings", number, new SettingOptions { Advanced = true, RequiresRestart = true });
        ModSettings.Open();
        Check(Descendants(menu.Body).OfType<Row>().Count(r => r.SettingBinding != null) == 1, "menu hides advanced rows by default");
        typeof(SettingsMenu).GetField("advanced", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        ModSettings.Open();
        Check(Descendants(menu.Body).OfType<Row>().Count(r => r.SettingBinding != null) == 2, "Show advanced reveals registered advanced row");
        Check(Descendants(menu.Body).OfType<Chip>().Count(c => c.Text == "Restart required") == 1, "restart metadata renders the chip");
        var hidden = file.Bind("Test", "Hidden", true, "Conditional"); int visibleCalls = 0;
        ModSettings.Register("test.settings", hidden, new SettingOptions { VisibleWhen = () => { visibleCalls++; return false; } });
        Check(Descendants(menu.Body).OfType<Row>().Count(r => r.SettingBinding != null) == 2 && visibleCalls > 0, "view refresh evaluates VisibleWhen and hides the row");
        var stack = new UIStack();
        var tr = stack.Setting(Describe(toggle));
        var nr = stack.Setting(Describe(number));
        var xr = stack.Setting(Describe(text));
        var cr = stack.Setting(Describe(choice));
        Check(Children(tr).OfType<Toggle>().Count() == 1 && Children(nr).OfType<Slider>().Count() == 1 && Children(xr).OfType<TextField>().Count() == 1 && Children(cr).OfType<Dropdown>().Count() == 1, "real Container.Setting maps all four kinds");
        var reset = Control<Button>(tr);
        var visible = (Func<bool>)typeof(Element).GetField("VisibleGetter", Private)!.GetValue(reset)!;
        Check(!visible(), "real Reset hidden at default");
        toggle.Value = true; Poll(tr);
        Check(((Func<bool>)Field(Control<Toggle>(tr), "value"))() && visible(), "external ConfigEntry change visible at next poll");
        ((Action)Field(reset, "onClick"))(); Poll(tr);
        Check(!toggle.Value && !visible(), "real Reset restores default and hides button");
        ((Action<bool>)Field(Control<Toggle>(tr), "set"))(true);
        Check(toggle.Value && File.ReadAllText(file.ConfigFilePath).Contains("Toggle = true"), "toggle writes cfg immediately");
        var field = Control<TextField>(xr);
        field.GetType().GetMethod("Changed", Private)!.Invoke(field, new object[] { "typed" });
        Check(text.Value == "initial", "linked text row only wires commit callback");
        field.GetType().GetMethod("Commit", Private)!.Invoke(field, new object[] { "typed" });
        Check(text.Value == "typed", "linked text row commit writes value");
        ((Action<int>)Field(Control<Dropdown>(cr), "select"))(2);
        Check(choice.Value == Mode.Three, "choice uses all enum names and writes immediately");
        var kit = typeof(UiKit).Assembly.GetType("SprocketUiKit.Kit")!;
        var now = kit.GetField("Now", BindingFlags.Static | BindingFlags.NonPublic)!;
        var slider = Control<Slider>(nr); var write = (Action<double>)Field(slider, "set");
        now.SetValue(null, 0f); write(7.6); Check(number.Value == 8, "integer slider rounds");
        now.SetValue(null, .1f); write(10);
        Check(number.Value == 8 && ((Func<double>)Field(slider, "value"))() == 10, "slider shows pending value without cfg write");
        now.SetValue(null, .25f); Poll(nr); Check(number.Value == 10, "real slider writes at 250ms");
        now.SetValue(null, .26f); write(12); ((Action)Field(slider, "dragEnd"))();
        Check(number.Value == 12, "real slider release flushes cfg");
        toggle.Value = true; number.Value = 18;
        ModSettings.ResetOwner("test.settings");
        Check(!toggle.Value && number.Value == 5, "ResetOwner resets every setting of the selected mod");
        var binding = Binding(nr); var poll = (Action<float>)binding.GetType().GetMethod("Refresh", Private)!.CreateDelegate(typeof(Action<float>), binding);
        for (int i = 0; i < 1000; i++) poll(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) poll(0);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, "real setting binding idle polling: zero bytes over 10000 frames");
        var unknown = Describe(text);
        typeof(ModSetting).GetField("<Kind>k__BackingField", Private)!.SetValue(unknown, (SettingKind)99);
        int warnings = SprocketUiKit.Plugin.Instance.Log.WarningCount;
        var readOnly = stack.Setting(unknown); stack.Setting(unknown);
        Check(!Children(readOnly).Any(e => e is TextField || e is Slider || e is Dropdown || e is Toggle) && SprocketUiKit.Plugin.Instance.Log.WarningCount == warnings + 1, "unknown kind is read only with one warning per setting");
        Console.WriteLine("ALL HEADLESS SETTINGS PASSED: " + count);
    }
}

