using BepInEx;
using BepInEx.Unity.IL2CPP;
using SprocketKeybinds;
using SprocketUiKit;

[BepInPlugin("example.hatches", "Example Hatches", "1.0.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.3 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.0 <0.2.0")]
public sealed class ExamplePlugin : BasePlugin
{
    private sealed class Hatch { public string Name = ""; public bool Open; public double Speed = 90; }
    private readonly List<Hatch> hatches = new() { new() { Name = "Commander hatch" }, new() { Name = "Loader hatch" } };
    private Hatch? selected;

    public override void Load()
    {
        var key = Keybinds.RegisterButton("example.hatches", "Example Hatches", "panel", "Hatches / Panel", "");
        var panel = UiKit.CreatePanel("example.hatches", "main", "Hatches", "Example Hatches 1.0", 760, 460);
        UiKit.BindToggle(panel, key);                       // open/close with the player's keybind

        var cols = panel.Columns(320, 0);                   // fixed 320 px + flexible
        cols[0].Section("Hatches", () => hatches.Count)
            .List(() => hatches, (h, row) =>
            {
                row.Title = h.Name; row.Lamp = h.Open; row.Selected = h == selected;
                row.Tag = h.Open ? "OPEN" : "CLOSED";
            })
            .OnSelect(h => selected = h);

        var edit = cols[1].Section("Selected hatch");
        edit.Label(() => selected?.Name ?? "Click a hatch on the left.", TextStyle.Label);
        edit.FieldRow("Open").Toggle(() => selected?.Open ?? false, v => { if (selected != null) selected.Open = v; })
            .EnabledWhen(() => selected != null);
        edit.FieldRow("Speed").Stepper(() => selected?.Speed ?? 0, v => selected!.Speed = v, 10, 360, 10, "°/s")
            .EnabledWhen(() => selected != null).Tooltip("Degrees per second.");

        var add = cols[1].Footer("New hatch");
        add.Labeled("Name").TextField(() => "", name => newName = name, "Hatch name");
        add.Button("Add hatch", () =>
        {
            if (newName.Trim().Length == 0) { panel.SetStatus("Type a name first.", StatusKind.Warning); return; }
            hatches.Add(new Hatch { Name = newName.Trim() });
            panel.SetStatus("Added " + newName.Trim(), StatusKind.Success);
        }, ButtonStyle.Primary).WithIcon(Icon.Plus);
    }
    private string newName = "";
}
