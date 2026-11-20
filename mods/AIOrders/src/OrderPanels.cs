using Il2CppInterop.Runtime.Attributes;
using SprocketUiKit;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SprocketAIOrders;

public sealed partial class OrdersHost
{
    private HudPanel? quickHud;
    private Panel? mapPanel;

    [HideFromIl2Cpp]
    internal void CreatePanels()
    {
        // Non-modal HUD: Kit 0.1.2 has corner anchors only, so use the lower left and
        // lift it above the game's own speed/gear block at the bottom of the screen.
        quickHud = UiKit.CreateHud(Plugin.Guid, "quick", 340);
        quickHud.AllowedContexts = UiContexts.Game;
        quickHud.HideWhenCursorFree = false;
        quickHud.Anchor(HudCorner.BottomLeft, 24, 150);
        quickHud.MaxHeight(520);
        quickHud.Visible(() => quick.HudVisible && Time.timeScale > 0f);

        var body = quickHud.Body;
        body.Spacing(2);
        var head = body.Row(8);
        head.Label("AI Orders", TextStyle.Section).Outline();
        head.Label(() => $"Selected {selected.Count}/{allies.Count}", TextStyle.Small).Outline();
        var root = body.Stack(1).VisibleWhen(() => menu.Screen == "root");
        var labels = new[] { "All/none", "Choose…", "Follow column", "Follow line", "Halt", "Attack…", "Retreat…", "Release", "Map" };
        for (var n = 1; n <= 9; n++)
        {
            var key = n;
            root.Label(() => $"{key}  {labels[key - 1]}", TextStyle.Value).Outline();
        }
        root.Label("0  Close", TextStyle.Value).Outline();
        var sub = body.Stack(1).VisibleWhen(() => menu.Screen != "root");
        sub.Label(() => menu.Screen == "units" ? "Choose allies; 0 back" : $"{menu.TargetOrder}; choose a threat", TextStyle.Small).Outline();
        for (var n = 1; n <= 7; n++)
        {
            var key = n;
            sub.Label(() => MenuChoiceText(key), TextStyle.Small).Outline().Wrap()
                .VisibleWhen(() => MenuChoice(key) != null);
        }
        sub.Label("No available units or visible threats.", TextStyle.Small).Outline().Wrap()
            .VisibleWhen(() => MenuItems().Length == 0);
        sub.Label(() => $"8  Previous · 9  Next · Page {menu.Page + 1}/{MenuPages()}", TextStyle.Small).Outline().Wrap();
        sub.Label("0  Back", TextStyle.Value).Outline();
        body.Label(() => status, TextStyle.Small).Outline().Wrap();

        mapPanel = UiKit.CreatePanel(Plugin.Guid, "map", "AI Orders", "Tactical map", 1600, 900);
        mapPanel.AllowedContexts = UiContexts.Game;
        UiKit.BindToggle(mapPanel, Plugin.MapKey);
        mapPanel.OnOpen += () =>
        {
            if (player == null) { mapPanel.Close(); return; }
            quick.MapOpened();
            boundsDiagnosed = false;
            CaptureBounds();
            mapPanel.SetSize(Math.Min(1600, Screen.width / UiKit.Scale - 20), Math.Min(900, Screen.height / UiKit.Scale - 20));
            if (heights == null && heightRow < 0) StartMapBuild();
            FitMap();
            drawStats.Reset(); nextDrawLog = Time.unscaledTime + 5;
            status = "Map: left-click selects, right-click orders.";
            if (Plugin.Detailed) Plugin.Info("AI Orders map panel open; quick HUD hidden.");
            if (Plugin.Detailed && mapCanvas != null) Plugin.Info(FormattableString.Invariant($"AI Orders map open: side={mapSpace.SizeX:F1} m; min=({mapSpace.MinX:F1}, {mapSpace.MinZ:F1}); canvas=({mapCanvas.CanvasWidth:F1}, {mapCanvas.CanvasHeight:F1}); zoom={mapCanvas.Zoom:F5}."));
        };
        mapPanel.OnClose += () =>
        {
            mapClosedFrame = Time.frameCount;
            quick.MapClosed();
            ObserveMapDraw();
            if (Plugin.Detailed) Plugin.Info("AI Orders map panel closed.");
        };
        var columns = mapPanel.Columns(330, 0);
        var col = columns[0];
        var chart = columns[1];
        chart.Label("LMB select  |  Shift add  |  RMB move/attack  |  Shift+RMB waypoint  |  wheel zoom  |  MMB pan", TextStyle.Small).Wrap();
        mapCanvas = chart.Canvas(700, DrawMapCanvas).Fill()
            .PanZoom(panButton: 2, minZoom: .0001f, maxZoom: 4)
            .Fps(appliedFps = Plugin.MapRedrawFps).OnClick(MapClick)
            .OnDrag(_ => mapCanvas?.Invalidate()).OnWheel((_, _) => mapCanvas?.Invalidate());
        var units = col;
        units.Label(() => $"Friendly units ({allies.Count})", TextStyle.Section);
        units.Label(() => $"{selected.Count}/{allies.Count} selected", TextStyle.Small);
        var select = units.Row();
        select.Button("Select all", () => KitAction("Select all", () => { foreach (var a in allies) selected.Add(a.Pointer); })).Compact();
        select.Button("Clear", () => KitAction("Clear", selected.Clear)).Compact();
        units.List<VehicleBehaviour>(() => allies, FillAllyRow).Fill().OnSelect(SelectAlly)
            .Empty("No AI allies on your team.").Tooltip("Click to select; Shift+click to add/remove.");
        var orders = col.Footer("Orders for selected units");
        var follow = orders.Row();
        follow.Button("Follow column", () => KitOrder("FollowColumn")).Compact();
        follow.Button("Follow line", () => KitOrder("FollowLine")).Compact();
        var stop = orders.Row();
        stop.Button("Halt", () => KitOrder("Halt")).Compact();
        stop.Button("Release to AI", () => KitOrder("Release")).Compact();
        orders.Button("Attack nearest spotted", () => KitAction("Attack nearest spotted", () =>
        {
            var nearest = NearestSpotted();
            if (nearest != null) AttackSelected(nearest);
        })).Compact().Text(() => NearestSpotted() is { } t
            ? $"Attack nearest spotted ({FlatDistance(t.Position, player!.Position):F0} m)"
            : "Attack nearest spotted (none)").EnabledWhen(() => NearestSpotted() != null);
        var frame = orders.Row();
        frame.Button("Center on me", () => KitAction("Center on me", CenterMap)).Compact();
        frame.Button("Whole map", () => KitAction("Whole map", FitMap)).Compact();
        orders.Label("1-9 select; Shift adds; 0 all/none.\n* = waypoint follow", TextStyle.Small).Wrap();
        orders.Label(() => status, TextStyle.Small).Wrap();
    }

    [HideFromIl2Cpp]
    internal void DestroyPanels()
    {
        if (quickHud != null) UiKit.DestroyHud(quickHud);
        if (mapPanel != null) UiKit.DestroyPanel(mapPanel);
        quickHud = null; mapPanel = null; mapCanvas = null;
    }

    [HideFromIl2Cpp]
    private void KitAction(string label, Action action)
    {
        if (Stopped || player == null || Time.timeScale <= 0f) return;
        if (Plugin.Detailed) Plugin.Info($"AI Orders Kit button: {label}.");
        action();
        mapCanvas?.Invalidate();
    }

    [HideFromIl2Cpp]
    private void KitOrder(string command) => KitAction(command, () => Execute(command));
    [HideFromIl2Cpp]
    private VehicleBehaviour[] MenuItems() => menu.Screen == "units" ? menuUnits : menuTargets;
    [HideFromIl2Cpp]
    private int MenuPages() => Math.Max(1, (MenuItems().Length + 6) / 7);
    [HideFromIl2Cpp]
    private VehicleBehaviour? MenuChoice(int n)
    {
        var index = menu.Page * 7 + n - 1;
        var items = MenuItems();
        return n >= 1 && n <= 7 && index >= 0 && index < items.Length ? items[index] : null;
    }
    [HideFromIl2Cpp]
    private bool MenuChoiceLive(int n) => MenuChoice(n) is { } t && Usable(t)
        && (menu.Screen == "units" ? allies : enemies).Any(a => a.Pointer == t.Pointer);
    [HideFromIl2Cpp]
    private string MenuChoiceText(int n)
    {
        var t = MenuChoice(n);
        if (t == null) return n.ToString();
        if (!MenuChoiceLive(n)) return $"{n}  (unavailable)";
        return menu.Screen == "units"
            ? $"{n}  {(selected.Contains(t.Pointer) ? "[x] " : "")}{Number(t)} {Name(t)} ({OrderText(t)})"
            : $"{n}  {Name(t)} ({FlatDistance(t.Position, player!.Position):F0} m)";
    }
    [HideFromIl2Cpp]
    private void FillAllyRow(VehicleBehaviour tank, ListRow row)
    {
        row.Key = tank.Pointer.ToString(); row.Title = $"{Number(tank)}  {Name(tank)}";
        row.Tag = OrderText(tank); row.TagChip = true;
        row.Value = player == null ? "" : $"{FlatDistance(tank.Position, player.Position):F0} m";
        row.Selected = selected.Contains(tank.Pointer);
    }
    [HideFromIl2Cpp]
    private void SelectAlly(VehicleBehaviour tank)
    {
        if (!Usable(tank)) return;
        if (!(Keyboard.current?.shiftKey?.isPressed ?? false)) selected.Clear();
        if (!selected.Add(tank.Pointer)) selected.Remove(tank.Pointer);
        mapCanvas?.Invalidate();
    }
    [HideFromIl2Cpp]
    private VehicleBehaviour? NearestSpotted() => player == null ? null : enemies.Where(Usable)
        .OrderBy(t => FlatDistance(t.Position, player.Position)).FirstOrDefault();
}
