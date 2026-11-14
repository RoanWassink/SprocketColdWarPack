using SprocketUiKit;

internal static class Focus0112Tests
{
    private sealed class PanelStub
    {
        private readonly KeyboardFocusState<PanelStub> state;
        internal PanelStub(KeyboardFocusState<PanelStub> state) => this.state = state;
        internal bool ModalOpen;
        // Same identity projection as the public Panel.HasFocus getter (IL checked by Verify).
        internal bool HasFocus => state.Current == this;
    }

    internal static void Run(Action<bool, string> check)
    {
        var state = new KeyboardFocusState<PanelStub>();
        var a = new PanelStub(state); var b = new PanelStub(state); var hud = new PanelStub(state);
        var order = new List<PanelStub> { a, b, hud };
        PanelStub? Target(bool popup = false) => InputFocusPolicy.ShortcutTarget(state.Current, popup);
        void Ownership(PanelStub? expected, string step)
        {
            check(state.Current == expected && a.HasFocus == (expected == a) &&
                b.HasFocus == (expected == b) && !hud.HasFocus, "0.1.12 focus API projection: " + step);
        }

        Ownership(null, "initially no focus");
        check(Target() == null, "0.1.12 shortcut and Enter: no focus means no recipient");
        state.Open(a, true); Ownership(a, "open A");
        state.Open(hud, false); Ownership(a, "HUD open does not take focus");
        state.Open(b, true); Ownership(b, "open B");
        state.Press(a); Ownership(a, "press inside A");
        check(Target() == a && order[1] == b, "0.1.12 shortcut and Enter: focused A wins while B is above A");
        a.ModalOpen = true;
        check(Target() == a && Target()!.ModalOpen && !b.ModalOpen,
            "0.1.12 Enter: selects focused A's modal, not top B's state");
        a.ModalOpen = false; b.ModalOpen = true;
        check(Target() == a && !Target()!.ModalOpen,
            "0.1.12 Enter: top B's modal cannot suppress focused A's default action");
        check(Target(true) == null, "0.1.12 shortcut and Enter: popup suppresses dispatch with focus");
        state.Press(null); Ownership(null, "press outside");
        check(Target() == null && Target(true) == null, "0.1.12 shortcut and Enter: designer owns keys after outside press");
        state.Press(b); state.Close(b, order, p => p != hud); Ownership(a, "close owner hands off");
        state.Close(b, order, p => p != hud); Ownership(a, "close non-owner preserves focus");
        state.Close(a, order, _ => false); Ownership(null, "close last panel");
        foreach (var reason in new[] { "pause", "scene", "alt-tab", "settings", "unload" })
        {
            state.Open(a, true); state.Clear(); Ownership(null, reason);
            check(Target() == null, "0.1.12 shortcut and Enter: no recipient after " + reason);
        }
        state.Open(a, true);
        for (int i = 0; i < 2000; i++) { Target(); Target(true); _ = a.HasFocus; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { Target(); Target(true); _ = a.HasFocus; }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"INFO 0.1.12 focus routing/projections: {bytes} bytes / 2000 polls (existing shortcut ToList retained)");
        check(bytes == 0, "0.1.12 recipient policy and focus projections allocate zero bytes");

        // Existing dispatch takes a snapshot, so additions/removals affect the next dispatch.
        var shortcuts = new List<Action>(); int calls = 0;
        Action later = () => calls += 10;
        shortcuts.Add(() => { calls++; shortcuts.Clear(); shortcuts.Add(later); });
        shortcuts.Add(() => calls++);
        foreach (var action in shortcuts.ToList()) action();
        check(calls == 2 && shortcuts.Count == 1, "0.1.12 retained snapshot: callback can add/remove without breaking iteration");
        foreach (var action in shortcuts.ToList()) action();
        check(calls == 12, "0.1.12 retained snapshot: mutation visible on next dispatch");
    }
}
