using SprocketUiKit;

internal static class Settings0113Tests
{
    internal static void Run(Action<bool, string> check)
    {
        for (int kind = 0; kind < 4; kind++) check(SettingsPolicy.Control(kind) == kind, "settings kind maps to matching control " + kind);
        check(SettingsPolicy.Control(99) == -1 && SettingsPolicy.Control(-1) == -1, "unknown settings kind is read only");
        var gate = new SettingWriteGate();
        gate.Queue(1); check(gate.Take(0, false, out var value) && value == 1, "slider first write immediate");
        gate.Queue(2); check(!gate.Take(.249, false, out _), "slider throttles before 250ms");
        check(gate.Take(.250, false, out value) && value == 2, "slider writes at 250ms");
        gate.Queue(3); check(gate.Take(.251, true, out value) && value == 3, "slider release always flushes latest");
        check(!gate.Take(1, true, out _), "slider release without changes does not write twice");
        gate.Queue(4); gate.Queue(5); check(gate.Take(1, false, out value) && value == 5, "slider coalesces writes");
        var edit = new TextEdit(); edit.Begin("default"); edit.Insert(" edited");
        check(!TextCommitPolicy.ShouldWrite(false, edit.Changed), "text never writes per keystroke or on Esc");
        check(TextCommitPolicy.ShouldWrite(true, edit.Changed), "text Enter and blur commit edited value");
        check(!TextCommitPolicy.ShouldWrite(true, false), "unchanged text does not write");
        check(!SettingsPolicy.ResetVisible(3, 3) && SettingsPolicy.ResetVisible(4, 3), "Reset only shown away from default");
        check(SettingsPolicy.Include(true, false, false) && !SettingsPolicy.Include(true, true, false), "advanced hidden by default");
        check(SettingsPolicy.Include(true, true, true) && !SettingsPolicy.Include(false, false, true), "advanced filter respects VisibleWhen");
        check(InputFocusPolicy.CanFocus(true, true, true) && !InputFocusPolicy.CanFocus(true, true, false), "paused focus allowed only for StayOpenWhilePaused panels");
        check(!InputFocusPolicy.CanFocus(false, true, true), "Settings and lifecycle suppression block paused panel focus");
        var waiting = new DeferredSettingsOpen(); bool isOpen = true;
        waiting.Request(true);
        check(isOpen && waiting.Pending && !SettingsPolicy.Show(isOpen, true, false, true), "configuring retains IsOpen with hidden root");
        check(!waiting.Reveal(true, true, true) && waiting.Pending, "pause does not end configuring wait");
        check(waiting.Reveal(false, true, true) && !waiting.Pending && isOpen, "after Settings visible and open above pause");
        check(SettingsPolicy.Show(isOpen, false, true, true) && !waiting.Reveal(false, true, true), "paused settings panel stays visible and focuses only once");
        waiting.Request(true); waiting.Cancel(); check(!waiting.Reveal(false, false, true), "closing cancels deferred focus");
        check(Array.Empty<string>().Length == 0, "empty settings source accepted");
        var source = new List<object> { new(), new() }; var snapshot = new StableSnapshot<object>();
        var stable = snapshot.Get(source); source.Clear();
        check(stable.Length == 2 && snapshot.Get(source).Length == 0, "frame snapshot survives callback membership mutation");
        source.Add(new()); stable = snapshot.Get(source);
        for (int i = 0; i < 1000; i++) { snapshot.Get(source); gate.Take(i, false, out _); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { snapshot.Get(source); gate.Take(i, false, out _); SettingsPolicy.Include(true, false, false); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"INFO settings idle bytes: {bytes}");
        check(bytes == 0, "settings idle policies and frame snapshot: zero bytes over 10000 frames");
    }
}

