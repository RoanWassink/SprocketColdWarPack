using SprocketKeybinds;
using UnityEngine;
namespace SprocketUiKit;

/// <summary>
/// Demo panel: every component and state from Components.dc.html, a mini I/O example with fake
/// data (Main.dc.html), inline rule rows (Rules.dc.html) and a click log. Opened with the Keybinds
/// API action "UI Kit / Demo" (factory default unbound since 0.1.2; bind it in Settings).
/// </summary>
internal static class Demo
{
    internal const string ActionId = "demo", ActionLabel = "UI Kit / Demo", DefaultKey = "";
    private static ModKeybind? key;
    private static Panel? main, second;
    private static int tab, segment = 3, demoTab, dropdown = 1, command = 0;
    private static double stepper = 2, progressPct = 62, delay = .5, duration = 2;
    private static bool toggleA = true, toggleB;
    private static string name = "Case flap";
    private static readonly List<string> log = new();

    private sealed class FakeInput { public string Title = "", Sub = "", Kind = "EVENT"; public float Period, Phase; public bool Watched = true; }
    private sealed class FakeLink { public FakeInput Input = null!; public string Command = "OPEN"; public double Delay; public string Hinge = ""; }
    private sealed class FakeHinge { public string Name = "", Kind = "LEAF"; public double Open = 90, Closed = 0, Speed = 180; public float Target; public float Angle; }
    private sealed class Rule { public int Source, Event, Command; public double Delay; }

    private static readonly List<FakeInput> inputs = new()
    {
        new() { Title = "Cannon fired", Sub = "Cannon 1 · 75 mm", Period = 3.1f },
        new() { Title = "Reload done", Sub = "Cannon 1 · 75 mm", Period = 3.1f, Phase = 1.6f },
        new() { Title = "Key", Sub = "Hinge / Action 1 · Numpad 1", Period = 5.3f, Phase = .7f },
    };
    private static readonly List<FakeHinge> hinges = new()
    {
        new() { Name = "Case flap", Kind = "MOUNT", Target = 1 },
        new() { Name = "Commander hatch", Kind = "LEAF" },
    };
    private static readonly List<FakeLink> links = new();
    private static readonly List<Rule> rules = new() { new Rule { Source = 0, Event = 0, Command = 0, Delay = .5 }, new Rule { Source = 0, Event = 2, Command = 1 } };
    private static FakeInput? selectedInput;
    private static FakeHinge? selectedHinge;
    private static int newType;
    private static int newCannon;
    private static Rule? editing;

    private static readonly string[] Commands = { "Open", "Close", "Toggle", "Pulse", "Follow" };
    private static readonly Choice[] Sources =
    {
        new("Cannon 1 · 75 mm", "Cannons"), new("Cannon 2 · 7.92 mm MG", "Cannons"),
        new("Action 1 (Numpad 1)", "Keys"), new("Action 2 (Numpad 2)", "Keys"),
        new("Case flap", "Hinges"), new("Commander hatch", "Hinges"), new("Mast", "Other"),
    };
    private static readonly string[] Events = { "fires", "starts reloading", "finished reloading", "is loaded (while)", "is reloading (while)" };
    private static readonly string[] RuleCommands = { "open", "close", "toggle", "pulse", "follow" };

    internal static void Register()
    {
        key = Keybinds.RegisterButton(UiKit.PluginGuid, "UI Kit", ActionId, ActionLabel, DefaultKey);
        main = UiKit.CreatePanel(UiKit.PluginGuid, "demo", "UI Kit demo", "Sprocket UI Kit " + UiKit.Version, 1240, 640);
        main.TitleIcon = Icon.Hinge;
        main.AllowedContexts = UiContexts.All; // the demo may also be opened in the main menu to check the look
        UiKit.BindToggle(main, key);
        main.OnClose += () => { second?.Close(); Demo014.Close(); Log("demo closed"); };
        main.OnOpen += () => Log("demo opened in " + UiKit.Context);
        BuildMain(main);
        second = UiKit.CreatePanel(UiKit.PluginGuid, "demo2", "Second panel", "focus and z-order", 420, 300);
        second.AllowedContexts = UiContexts.All;
        BuildSecond(second);
    }
    internal static void Unregister()
    {
        if (key != null) { Keybinds.Unregister(UiKit.PluginGuid, ActionId); key = null; }
        Demo012.Unregister();
    }
    private static void Log(string what)
    {
        log.Insert(0, $"{Time.unscaledTime,7:0.0}s  {what}");
        if (log.Count > 200) log.RemoveAt(log.Count - 1);
    }
    private static Action Logged(string what, Action? then = null) => () => { then?.Invoke(); Log(what); main?.SetStatus(what, StatusKind.Info); };

    private static void BuildMain(Panel p)
    {
        var bar = p.Body.Stack(0); bar.Padding(6, 16, 0, 16);
        bar.Tabs(new[] { "Components", "I/O example", "Rules", "Log" }.Concat(Demo012.Tabs).Concat(Demo014.Tabs).Concat(Demo016.Tabs).Concat(Demo017.Tabs).Concat(Demo019.Tabs).ToArray(), () => tab, i => { tab = i; Log("tab " + i); });
        // ---------- Tab 0: components (Components.dc.html) ----------
        var c = p.Body.Columns(0, 0, 0);
        c[0].Parent!.VisibleWhen(() => tab == 0);
        var buttons = c[0].Section("Buttons");
        var r1 = buttons.Row();
        r1.Button("Primary", Logged("primary"), ButtonStyle.Primary);
        r1.Button("Normal", Logged("normal"));
        r1.Button("Quiet", Logged("quiet"), ButtonStyle.Quiet);
        var r2 = buttons.Row();
        r2.Button("Delete", Logged("delete"), ButtonStyle.Danger);
        r2.Button("Disabled", Logged("never")).EnabledWhen(() => false).Tooltip("Disabled buttons ignore clicks but still show tooltips.");
        r2.Button("Rename", Logged("compact")).Compact();
        var r3 = buttons.Row();
        r3.IconButton(Icon.Close, Logged("icon close"), "Close icon");
        r3.IconButton(Icon.Trash, Logged("icon trash"), "Delete icon (hover turns it red)");
        r3.IconButton(Icon.Plus, Logged("icon plus"), "Add");
        r3.IconButton(Icon.Help, Logged("help"), "Follow: open while the input is on, closed as soon as it goes off.");
        r3.Button("Add input", Logged("add with icon"), ButtonStyle.Primary).WithIcon(Icon.Plus);
        var input = c[0].Section("Input");
        input.FieldRow("Stepper").Stepper(() => stepper, v => { stepper = v; Log("stepper " + v); }, 0, 10, .1, "s").Tooltip("Click, or hold to repeat. Snaps to the 0.1 step, limits 0-10.");
        input.FieldRow("Switch on").Toggle(() => toggleA, v => { toggleA = v; Log("switch A " + v); });
        input.FieldRow("Switch off").Toggle(() => toggleB, v => { toggleB = v; Log("switch B " + v); });
        input.Labeled("Dropdown").Dropdown(new[] { "Any cannon in group...", "Cannon 1 · 75 mm (group 1)", "Cannon 2 · 7.92 mm MG (group 2)" }, () => dropdown, i => { dropdown = i; Log("dropdown " + i); });
        input.Labeled("Text field (type, Ctrl+A, Enter, Esc)").TextField(() => name, v => { name = v; Log("name '" + v + "'"); main?.SetStatus("Renamed to " + v, StatusKind.Success); }, "Name");

        c[1].Section("Segmented").Segmented(Commands, () => segment, i => { segment = i; Log("segment " + Commands[i]); });
        c[1].Section("Tabs").Tabs(new[] { "I/O", "Hinges", "Log" }, () => demoTab, i => demoTab = i);
        var chips = c[1].Section("Chips").Row();
        chips.Chip("Designer", ChipStyle.Context); chips.Chip("Mount", ChipStyle.Tag); chips.Chip("Open", ChipStyle.Accent); chips.Chip("Close", ChipStyle.Neutral);
        var prog = c[1].Section("Progress", () => $"{progressPct:0}%");
        prog.Progress(() => (float)(progressPct / 100));
        prog.FieldRow("Value").Stepper(() => progressPct, v => progressPct = v, 0, 100, 5, "%").Compact();
        var radial = prog.Row(10);
        radial.Radial(() => (float)(progressPct / 100), 56).Thickness(7)
            .Tooltip("Radial progress (0.1.8): same value as the bar above, fills clockwise from 12 o'clock.");
        radial.Label("Reload progress", TextStyle.Value);
        var status = c[1].Section("Status line");
        var sr = status.Row(6);
        sr.Button("Info", () => main!.SetStatus("Information line", StatusKind.Info)).Compact();
        sr.Button("Success", () => main!.SetStatus("Link added: Cannon fired -> Open -> Case flap", StatusKind.Success)).Compact();
        sr.Button("Warning", () => main!.SetStatus("Cannon not found: falls back to group 1", StatusKind.Warning)).Compact();
        sr.Button("Error", () => main!.SetStatus("Could not save: vehicle is read-only", StatusKind.Error)).Compact();

        var list = c[2].Section("List and status", () => inputs.Count(i => i.Watched).ToString());
        list.List(() => inputs.Where(i => i.Watched).ToList(), (i, row) =>
        {
            row.Title = i.Title; row.Tag = i.Kind; row.Lamp = Blink(i); row.Removable = true; row.Key = i.Title;
        }).LampHold(.3f).OnRemove(i => { i.Watched = false; Log("removed " + i.Title); }).Empty("All removed: use Restore.");
        list.Button("Restore list", () => { foreach (var i in inputs) i.Watched = true; Log("restored"); }).Compact();
        c[2].Section("Warning").Warning("Cannon not found: falls back to group 1");
        var tip = c[2].Section("Tooltip and lamps").Row(10);
        tip.IconButton(Icon.Help, () => { }, "Hover any control with a tooltip for half a second.");
        tip.Lamp(() => Time.unscaledTime % 1.2f < .3f, "Blinking lamp");
        tip.Lamp(() => false, "Off");
        var more = c[2].Collapsible("More (collapsible)");
        more.Body.Label("Expandable section for settings that are not needed all the time.", TextStyle.Small).Wrap();
        more.Body.Button("Open second panel", () => { second!.Open(); Log("second panel"); });
        c[2].Footer("Two panels").Button("Open second panel", () => { second!.Open(); Log("second panel"); }, ButtonStyle.Primary);

        BuildIo(p);
        BuildRules(p);
        Demo012.Build(p, () => tab, Log);
        Demo014.Build(p, () => tab, Log);
        Demo016.Build(p, () => tab, Log);
        Demo017.Build(p, () => tab, Log);
        Demo019.Build(p, () => tab);
        // ---------- Tab 3: log ----------
        var lc = p.Columns(0);
        lc[0].Parent!.VisibleWhen(() => tab == 3);
        lc[0].Section("Click log", () => log.Count).List(() => log, (s, row) => row.Title = s).Height(420).Empty("Click something.");
        lc[0].Button("Clear log", () => log.Clear()).Compact();
    }

    private static bool Blink(FakeInput i) => (Time.unscaledTime + i.Phase) % i.Period < .12f;

    // ---------- Tab 1: mini I/O (Main.dc.html) with fake data ----------
    private static void BuildIo(Panel p)
    {
        var c = p.Columns(340, 0, 360);
        c[0].Parent!.VisibleWhen(() => tab == 1);
        if (links.Count == 0) links.Add(new FakeLink { Input = inputs[0], Command = "OPEN", Delay = .5, Hinge = "Case flap" });
        c[0].Section("Inputs", () => inputs.Count).List(() => inputs, (i, row) =>
        {
            row.Title = i.Title; row.Subtitle = i.Sub; row.Tag = i.Kind; row.Lamp = Blink(i); row.Selected = i == selectedInput; row.Key = i.Title;
        }).LampHold(.3f).OnSelect(i => { selectedInput = i; Log("input " + i.Title); });
        var ni = c[0].Footer("New input");
        ni.Labeled("Type").Dropdown(new[] { "Cannon fired (event)", "Cannon reloading (level)", "Reload done (event)", "Key (event)" }, () => newType, i => newType = i);
        ni.Labeled("Cannon").Dropdown(() => new[] { "Cannon 1 · 75 mm (group 1)", "Cannon 2 · 7.92 mm MG (group 2)" }, () => newCannon, i => newCannon = i)
            .VisibleWhen(() => newType < 3);
        ni.Button("Add input", () =>
        {
            var t = new[] { "Cannon fired", "Cannon reloading", "Reload done", "Key" }[newType];
            inputs.Add(new FakeInput { Title = t, Sub = newType < 3 ? (newCannon == 0 ? "Cannon 1 · 75 mm" : "Cannon 2 · 7.92 mm MG") : "Hinge / Action 2", Kind = newType == 1 ? "LEVEL" : "EVENT", Period = 2.5f + inputs.Count });
            main!.SetStatus("Input added: " + t, StatusKind.Success); Log("add input " + t);
        }, ButtonStyle.Primary).WithIcon(Icon.Plus);

        c[1].Section("Links", () => "input -> command -> hinge").List(() => links, (l, row) =>
        {
            row.Title = l.Input.Title; row.Subtitle = l.Input.Sub; row.Tag = l.Command + (l.Delay > 0 ? $" after {l.Delay:0.0} s" : " now"); row.Value = l.Hinge; row.Removable = true;
        }).OnRemove(l => { links.Remove(l); main!.SetStatus("Link removed.", StatusKind.Info); });
        var nl = c[1].Footer("New link");
        var pick = nl.Row(8);
        pick.Chip(() => selectedInput?.Title ?? "click an input", ChipStyle.Accent);
        pick.Label("->", TextStyle.Value);
        pick.Chip(() => selectedHinge?.Name ?? "click a hinge", ChipStyle.Accent);
        nl.Segmented(Commands, () => command, i => command = i);
        var st = nl.Row(12);
        st.Label("Delay", TextStyle.Value).Width(70);
        st.Stepper(() => delay, v => delay = v, 0, 60, .1, "s").ValueWidth(64);
        st.Label("Duration", TextStyle.Value).VisibleWhen(() => command == 3);
        st.Stepper(() => duration, v => duration = v, .1, 60, .5, "s").ValueWidth(64).VisibleWhen(() => command == 3);
        st.Spacer();
        st.Button("Add link", () =>
        {
            if (selectedInput == null || selectedHinge == null) { main!.SetStatus("Select an input (left) and a hinge (right) first.", StatusKind.Warning); return; }
            links.Add(new FakeLink { Input = selectedInput, Command = Commands[command].ToUpperInvariant(), Delay = delay, Hinge = selectedHinge.Name });
            main!.SetStatus($"Link added: {selectedInput.Title} -> {Commands[command]} -> {selectedHinge.Name}", StatusKind.Success);
        }, ButtonStyle.Primary).WithIcon(Icon.Plus);

        c[2].Section("Outputs", () => $"{hinges.Count} hinges").List(() => hinges, (h, row) =>
        {
            float t = h.Target; h.Angle += (t * (float)h.Open - h.Angle) * MathF.Min(1, Time.unscaledDeltaTime * 3);
            row.Title = h.Name; row.Tag = h.Kind; row.TagChip = true; row.Value = $"{h.Angle:0}°" + (h.Angle > 1 ? " open" : " closed"); row.ValueAccent = h.Angle > 1;
            row.Progress = (float)(h.Angle / Math.Max(1, h.Open)); row.Selected = h == selectedHinge;
        }).OnSelect(h => { selectedHinge = h; Log("hinge " + h.Name); });
        var sel = c[2].Footer();
        sel.Label(() => selectedHinge == null ? "SELECTED HINGE" : selectedHinge.Name, TextStyle.BlockTitle);
        sel.Label("Click a hinge above.", TextStyle.Small).VisibleWhen(() => selectedHinge == null);
        var details = sel.Stack(8); details.VisibleWhen(() => selectedHinge != null);
        details.Labeled("Name").TextField(() => selectedHinge?.Name ?? "", v => { if (selectedHinge != null && v.Trim().Length > 0) selectedHinge.Name = v.Trim(); }, "Hinge name").MaxChars(24);
        details.FieldRow("Open angle").Stepper(() => selectedHinge?.Open ?? 0, v => selectedHinge!.Open = v, 0, 180, 5, "°").Compact();
        details.FieldRow("Closed angle").Stepper(() => selectedHinge?.Closed ?? 0, v => selectedHinge!.Closed = v, 0, 180, 5, "°").Compact();
        details.FieldRow("Speed").Stepper(() => selectedHinge?.Speed ?? 0, v => selectedHinge!.Speed = v, 10, 720, 10, "°/s").Compact();
        var tests = details.Row(8);
        tests.Button("Test open", () => { selectedHinge!.Target = 1; Log("test open"); }).Flex();
        tests.Button("Test close", () => { selectedHinge!.Target = 0; Log("test close"); }).Flex();
    }

    // ---------- Tab 2: rule rows (Rules.dc.html): IF [source] [event] THEN [command] after [stepper] ----------
    private static void BuildRules(Panel p)
    {
        var c = p.Columns(280, 0);
        c[0].Parent!.VisibleWhen(() => tab == 2);
        c[0].Section("Hinges").List(() => hinges, (h, row) =>
        {
            row.Title = h.Name; row.Tag = h.Kind; row.TagChip = true; row.Value = $"{h.Angle:0}°"; row.Progress = (float)(h.Angle / Math.Max(1, h.Open));
            row.Note = rules.Count == 0 ? "No rules yet" : $"{rules.Count} rules"; row.Selected = h == (selectedHinge ?? hinges[0]);
        }).OnSelect(h => selectedHinge = h);
        var body = c[1].Section("Rules", () => "scrolls with the mouse wheel");
        var scroll = body.ScrollView(); scroll.Height(330);
        scroll.Repeat(() => rules, (rule, block) =>
        {
            var card = block.Card().Highlight(() => editing == rule);
            card.Lamp(() => (Time.unscaledTime + rule.Source) % 2.7f < .25f).Width(10);
            card.Label("If", TextStyle.Keyword).Width(34);
            card.Dropdown(() => Sources, () => rule.Source, i => { rule.Source = i; editing = rule; Log("source " + Sources[i].Label); }).Inline();
            card.Dropdown(Events, () => rule.Event, i => { rule.Event = i; editing = rule; Log("event " + Events[i]); }).Inline();
            card.Label("Then", TextStyle.Keyword);
            card.Dropdown(RuleCommands, () => rule.Command, i => { rule.Command = i; editing = rule; Log("command " + RuleCommands[i]); }).Inline().Accent(() => rule.Command == 0);
            card.Label(() => rule.Delay > 0 ? "after" : "now", TextStyle.Value);
            card.Stepper(() => rule.Delay, v => rule.Delay = v, 0, 30, .1, "s").Compact();
            card.Spacer();
            card.IconButton(Icon.Trash, () => { rules.Remove(rule); Log("rule removed"); }, "Delete rule");
        });
        var add = body.Row(8);
        add.Button("Add rule", () => { rules.Add(new Rule()); Log("rule added"); main!.SetStatus("Rule added", StatusKind.Success); }, ButtonStyle.Primary).WithIcon(Icon.Plus);
        add.Label("A new rule starts filled in (first cannon, fires -> open). Add 8+ to test scrolling; dropdowns near the bottom open upwards.", TextStyle.Faint).Wrap();
        var templates = c[1].Footer("Templates for this hinge").Row(8);
        templates.Button("Case flap: shot -> open, reloaded -> close", Logged("template 1")).Compact();
        templates.Button("Hatch on key (toggle)", Logged("template 2")).Compact();
    }

    private static void BuildSecond(Panel p)
    {
        p.SetContext(() => "PANEL 2");
        var c = p.Columns(0);
        var s = c[0].Section("Two panels");
        s.Label("Click a panel to bring it to the front. Esc closes the top one; drag either title bar.", TextStyle.Small).Wrap();
        s.FieldRow("Shared switch").Toggle(() => toggleA, v => { toggleA = v; Log("switch A from panel 2 " + v); });
        s.Labeled("Shared dropdown").Dropdown(new[] { "Any cannon in group...", "Cannon 1 · 75 mm (group 1)", "Cannon 2 · 7.92 mm MG (group 2)" }, () => dropdown, i => dropdown = i);
        c[0].Footer().Button("Close", () => p.Close());
    }
}
