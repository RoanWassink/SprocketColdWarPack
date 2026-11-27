using SprocketCarouselAutoloader;

// 0.2.27: HUD below the AMMO panel, visibility "only while something runs", line choice, ring gate.
// Pure functions only; nothing here needs Unity, BepInEx or the UI Kit.
internal static class HudLayoutTests
{
    static HudCard Make() => new();

    // One sample of one autoloader at game time <paramref name="now"/>; the fractions and remaining times are fixed.
    static void Sample(HudCard card, float now, bool cycling = false, bool waiting = false, RefillReason reason = RefillReason.None,
        int stored = 5, int capacity = 18, bool revolver = true, bool twoPiece = false, float blockedSeconds = 5f,
        double cycleRemaining = 2.31, double refillRemaining = 3.1)
    {
        bool refill = reason is RefillReason.Crew or RefillReason.Stationary;
        card.Update(revolver, twoPiece, stored, capacity, cycling, waiting, cycling ? .4 : 0, cycling ? cycleRemaining : double.NaN,
            reason, refill ? .25 : 0, refill ? refillRemaining : double.NaN, now, blockedSeconds);
    }

    internal static void Run(Action<bool, string> check)
    {
        Placement(check);
        Height(check);
        Visibility(check);
        Blocked(check);
        Choice(check);
        Gate(check);
        Sources(check);
    }

    static void Placement(Action<bool, string> check)
    {
        var manual = new HudSpot(HudAnchor.BottomRight, 24, 230, HudSpotSource.Manual);
        // Bottom corner: same corner and X, Y = ammoY - gap - height.
        var s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, 64);
        check(s == new HudSpot(HudAnchor.BottomRight, 24, 158, HudSpotSource.BelowAmmo), "BottomRight AMMO panel: same corner and X, Y = 230 - 8 - 64");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomLeft", 40, 300, 8, 100);
        check(s == new HudSpot(HudAnchor.BottomLeft, 40, 192, HudSpotSource.BelowAmmo), "BottomLeft AMMO panel: same corner and X, Y = 300 - 8 - 100");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "bottomright", 24, 230, 12, 64);
        check(s.Corner == HudAnchor.BottomRight && s.Y == 154, "corner name is case-insensitive; the gap is the configured one");
        // The top of the HUD stays gap below the AMMO panel when the number of lines changes: bottom moves by the height difference.
        var one = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, 64);
        var two = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, 132);
        check(one.Y - two.Y == 68 && 230 - 8 - two.Y == 132, "more lines: anchored again, top edge still 8 below the AMMO panel");
        // Y never below 8.
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 60, 8, 100);
        check(s.Y == AutoloaderHudLayout.MinY && s.Source == HudSpotSource.BelowAmmo, "Y is clamped to at least 8");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 0, 8, 0);
        check(s.Y == 8, "Y clamp with the AMMO panel at the screen edge");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, 214);
        check(s.Y == 8, "Y exactly 8 when the HUD just fits");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, 215);
        check(s.Y == 8, "Y stays 8 when it would be 7");
        // Top corner or no AMMO panel: the manual spot, with the reason.
        var topManual = new HudSpot(HudAnchor.TopLeft, 16, 90, HudSpotSource.Manual);
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, topManual, true, "TopRight", 24, 140, 8, 64);
        check(s == new HudSpot(HudAnchor.TopLeft, 16, 90, HudSpotSource.AmmoHudTopCorner), "AMMO panel in a top corner: manual spot, reported as such");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "TopLeft", 24, 140, 8, 64);
        check(s.Source == HudSpotSource.AmmoHudTopCorner && s.Corner == HudAnchor.BottomRight && s.X == 24 && s.Y == 230, "top-left AMMO panel: default manual spot BottomRight 24/230");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, false, null, 0, 0, 8, 64);
        check(s == new HudSpot(HudAnchor.BottomRight, 24, 230, HudSpotSource.NoAmmoHud), "without Expanded Ballistics: manual spot, reported as such");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", float.NaN, 230, 8, 64);
        check(s.Source == HudSpotSource.NoAmmoHud, "a non-finite AMMO offset counts as not readable");
        s = AutoloaderHudLayout.Resolve(HudPlace.Manual, topManual, true, "BottomRight", 24, 230, 8, 64);
        check(s == new HudSpot(HudAnchor.TopLeft, 16, 90, HudSpotSource.Manual), "Placement = Manual always uses Corner/OffsetX/OffsetY");
        // Unreadable AMMO corner = BottomRight, like the AMMO panel itself; bad gap or height are repaired.
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "nonsense", 24, 230, 8, 64);
        check(s.Corner == HudAnchor.BottomRight && s.Source == HudSpotSource.BelowAmmo && s.Y == 158, "unknown AMMO corner name counts as BottomRight");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, float.NaN, 64);
        check(s.Y == 230 - AutoloaderHudLayout.DefaultGap - 64, "NaN gap falls back to 8");
        s = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, -5, float.NaN);
        check(s.Y == 222, "negative gap and NaN height are repaired");
        check(AutoloaderHudLayout.Width == 300f && AutoloaderHudLayout.DefaultGap == 8f && AutoloaderHudLayout.DefaultBlockedSeconds == 5f, "width 300, gap 8, blocked 5 s");
        // Config words.
        check(AutoloaderHudLayout.TryParseCorner("TopLeft", out var c1) && c1 == HudAnchor.TopLeft && AutoloaderHudLayout.TryParseCorner("topright", out var c2) &&
              c2 == HudAnchor.TopRight && AutoloaderHudLayout.TryParseCorner("BottomLeft", out var c3) && c3 == HudAnchor.BottomLeft &&
              AutoloaderHudLayout.TryParseCorner("BottomRight", out var c4) && c4 == HudAnchor.BottomRight &&
              !AutoloaderHudLayout.TryParseCorner("Middle", out _) && !AutoloaderHudLayout.TryParseCorner(null, out _), "corner names");
        check(AutoloaderHudLayout.ParsePlacement("Manual") == HudPlace.Manual && AutoloaderHudLayout.ParsePlacement("manual") == HudPlace.Manual &&
              AutoloaderHudLayout.ParsePlacement("BelowAmmo") == HudPlace.BelowAmmo && AutoloaderHudLayout.ParsePlacement(null) == HudPlace.BelowAmmo &&
              AutoloaderHudLayout.ParsePlacement("x") == HudPlace.BelowAmmo, "placement words (BelowAmmo by default)");
        check(AutoloaderHudLayout.IsBottom(HudAnchor.BottomLeft) && AutoloaderHudLayout.IsBottom(HudAnchor.BottomRight) &&
              !AutoloaderHudLayout.IsBottom(HudAnchor.TopLeft) && !AutoloaderHudLayout.IsBottom(HudAnchor.TopRight), "bottom corners");
    }

    static void Height(Action<bool, string> check)
    {
        // Numbers from the kit metrics: pad 10 x 2, head 20, small label 16, bar 4, ring 40, card gap 2, body gap 4.
        var cycleOnly = new HudCardShape(true, true, false, false);
        var refillOnly = new HudCardShape(false, false, true, true);
        var both = new HudCardShape(true, true, true, true);
        var blocked = new HudCardShape(false, false, true, false);
        var waitingBlocked = new HudCardShape(true, false, true, false);
        check(AutoloaderHudLayout.CardHeight(cycleOnly, false) == 44 && AutoloaderHudLayout.CardHeight(refillOnly, false) == 44, "bar card: head + label + bar = 44");
        check(AutoloaderHudLayout.CardHeight(both, false) == 68, "bar card with cycle and refill = 68");
        check(AutoloaderHudLayout.CardHeight(blocked, false) == 38 && AutoloaderHudLayout.CardHeight(waitingBlocked, false) == 62, "blocked card has no refill bar");
        check(AutoloaderHudLayout.CardHeight(cycleOnly, true) == 62 && AutoloaderHudLayout.CardHeight(both, true) == 104, "ring card: ring rows of 40");
        check(AutoloaderHudLayout.CardHeight(blocked, true) == 38 && AutoloaderHudLayout.CardHeight(waitingBlocked, true) == 56, "ring card: a label row of 16 while nothing runs");
        check(AutoloaderHudLayout.Height(ReadOnlySpan<HudCardShape>.Empty, false) == 20, "no card: only the padding");
        check(AutoloaderHudLayout.Height(new[] { cycleOnly }, false) == 64, "one bar card = 20 + 44");
        check(AutoloaderHudLayout.Height(new[] { cycleOnly, both }, false) == 20 + 44 + 4 + 68, "two cards: body gap 4");
        var four = new[] { both, both, both, both };
        check(AutoloaderHudLayout.Height(four, false) == 20 + 4 * 68 + 12 && AutoloaderHudLayout.Height(four, false) < AutoloaderHudLayout.MaxHeight, "four bar cards fit the HUD maximum");
        check(20 + 4 * 104 + 12 > AutoloaderHudLayout.MaxHeight && AutoloaderHudLayout.Height(four, true) == AutoloaderHudLayout.MaxHeight,
            "four full ring cards (448) are capped at the HUD maximum (420)");
        // Height change moves the anchor (Y) but not the top edge.
        var manual = new HudSpot(HudAnchor.BottomRight, 24, 230, HudSpotSource.Manual);
        float h1 = AutoloaderHudLayout.Height(new[] { cycleOnly }, false), h2 = AutoloaderHudLayout.Height(new[] { cycleOnly, refillOnly }, false);
        var s1 = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, h1);
        var s2 = AutoloaderHudLayout.Resolve(HudPlace.BelowAmmo, manual, true, "BottomRight", 24, 230, 8, h2);
        check(s1.Y + h1 + 8 == 230 && s2.Y + h2 + 8 == 230 && s2.Y < s1.Y, "top of the HUD stays 8 below the AMMO panel at one and at two cards");
    }

    static void Visibility(Action<bool, string> check)
    {
        var card = Make();
        // Fire cycle running: shown, cycle part only.
        Sample(card, 10, cycling: true, reason: RefillReason.None, stored: 5);
        check(card.Shown && card.ShowCycle && card.CycleRunning && !card.ShowRefill && !card.RefillRunning, "cycle running: card shown with the cycle part");
        check(card.Row.Cycle == "Cycle 2.4 s" && card.CycleName == "Cycle" && card.CycleCentre == "2.4", "cycle texts: bar label and ring centre");
        // Crew refill running (gun ready): shown, refill part only, never "Cycle: ready".
        Sample(card, 10.1f, reason: RefillReason.Crew);
        check(card.Shown && !card.ShowCycle && card.ShowRefill && card.RefillRunning && card.Row.ShowRefillBar, "refill running: card shown with the refill part, no ready line");
        check(card.Row.Refill == "Crew refill 3.1 s" && card.RefillName == "Crew refill" && card.RefillCentre == "3.1", "refill texts: bar label and ring centre");
        Sample(card, 10.2f, reason: RefillReason.Stationary, refillRemaining: 11.6);
        check(card.Shown && card.RefillRunning && card.Row.Refill == "Stationary refill 12 s" && card.RefillName == "Stationary refill" && card.RefillCentre == "12", "stationary refill running: whole seconds");
        // Both at once.
        Sample(card, 10.3f, cycling: true, reason: RefillReason.Crew);
        check(card.Shown && card.ShowCycle && card.ShowRefill && card.CycleRunning && card.RefillRunning, "cycle and refill both running: both parts");
        // Ready and full: hidden. Also "Full" while the cycle runs: the cycle only.
        Sample(card, 10.4f, reason: RefillReason.Full, stored: 18);
        check(!card.Shown && !card.ShowCycle && !card.ShowRefill && card.Row.Cycle == "Cycle: ready" && card.Row.Refill == "Full", "Cycle: ready and Full: hidden");
        Sample(card, 10.5f, cycling: true, reason: RefillReason.Full, stored: 18);
        check(card.Shown && card.ShowCycle && !card.ShowRefill, "Full is never shown, also while the cycle runs");
        Sample(card, 10.6f, reason: RefillReason.None, stored: 18);
        check(!card.Shown, "nothing known and not running: hidden");
        // Nothing runs although the drum is not full: waiting states are not "running".
        Sample(card, 11, reason: RefillReason.WaitingStill);
        check(!card.Shown && card.Row.Refill == "Refill: stop to refill (no crew loader)", "waiting for the vehicle to stand still: hidden");
        Sample(card, 11.1f, waiting: true, reason: RefillReason.WaitingStill);
        check(!card.Shown && !card.ShowCycle, "gun waits for a refill that is not running: hidden");
        Sample(card, 11.2f, reason: RefillReason.WaitingEmpty, revolver: false);
        check(!card.Shown, "bustle waiting for empty: hidden");
        // The gun waits for a refill that runs: the card shows the waiting cycle line and the refill.
        Sample(card, 12, waiting: true, reason: RefillReason.Crew, stored: 0);
        check(card.Shown && card.ShowCycle && !card.CycleRunning && card.ShowRefill && card.Row.Cycle == "Cycle: waiting for refill" &&
              card.CycleName == "Cycle: waiting for refill", "gun waiting while the refill runs: both parts, the cycle part as text only");
        // Heading, chip, shape.
        Sample(card, 13, cycling: true, revolver: false, twoPiece: true, stored: 12, capacity: 16);
        check(card.Heading == "Bustle 12/16" && card.TwoPiece && card.Row.Title == "Bustle 12/16 (two-piece)", "section label without suffix, two-piece as a chip flag");
        Sample(card, 13.1f, cycling: true, revolver: true, twoPiece: false, stored: 5, capacity: 6);
        check(card.Heading == "Drum 5/6" && !card.TwoPiece, "DRUM 5/6");
        check(card.Shape == new HudCardShape(true, true, false, false), "shape of a card with only the cycle running");
        card.Reset();
        check(!card.Shown && !card.ShowCycle && !card.ShowRefill && !card.Row.Active, "reset hides the card");
        // No allocation while the shown values stay the same (10 Hz sample path).
        var steady = Make();
        for (int i = 0; i < 1000; i++) Sample(steady, i * .1f, cycling: true, reason: RefillReason.Crew, twoPiece: true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20000; i++)
            steady.Update(true, true, 5, 18, true, false, .4 + i % 5 * 1e-4, 2.31, RefillReason.Crew, .25, 3.1, 100 + i * .1f, 5f);
        check(GC.GetAllocatedBytesForCurrentThread() == before && steady.Shown, "card update allocates nothing while the shown values are unchanged");
    }

    static void Blocked(Action<bool, string> check)
    {
        var card = Make();
        // Blocked: visible for 5 s after it starts, then hidden.
        Sample(card, 100, reason: RefillReason.NoReserve);
        check(card.Shown && card.ShowRefill && !card.RefillRunning && !card.ShowCycle && card.Row.Blocked && card.Row.Refill == "Blocked: no matching reserve ammunition" &&
              card.RefillName == "Blocked: no matching reserve ammunition", "blocked reason shown at its start, in red");
        Sample(card, 102.5f, reason: RefillReason.NoReserve);
        check(card.Shown, "still shown after 2.5 s");
        Sample(card, 104.9f, reason: RefillReason.NoReserve);
        check(card.Shown, "still shown just inside 5 s");
        Sample(card, 105f, reason: RefillReason.NoReserve);
        check(!card.Shown && !card.ShowRefill, "hidden at 5 s");
        Sample(card, 130, reason: RefillReason.NoReserve);
        check(!card.Shown, "stays hidden while the reason does not change");
        // A new reason starts a new window.
        Sample(card, 131, reason: RefillReason.NoReach);
        check(card.Shown && card.Row.Refill == "Blocked: crew loader cannot reach the magazine", "a changed reason is shown again");
        Sample(card, 135.9f, reason: RefillReason.NoReach);
        check(card.Shown, "new window runs 5 s from the change");
        Sample(card, 136f, reason: RefillReason.NoReach);
        check(!card.Shown, "new window ends");
        // Something runs again: shown; the blocked line stays out of it. Afterwards the same reason starts a new window.
        Sample(card, 140, cycling: true, reason: RefillReason.NoReach);
        check(card.Shown && card.ShowCycle && !card.ShowRefill, "cycle runs again while still blocked: the cycle only, no blocked line after the window");
        Sample(card, 141, reason: RefillReason.Crew);
        check(card.Shown && card.RefillRunning, "refill runs again: shown");
        Sample(card, 150, reason: RefillReason.NoReach);
        check(card.Shown && card.ShowRefill, "blocked again after running: a new window");
        Sample(card, 155, reason: RefillReason.NoReach);
        check(!card.Shown, "and it ends after 5 s");
        // Blocked while the cycle runs, inside the window: both parts; after it only the cycle.
        var run = Make();
        Sample(run, 200, cycling: true, reason: RefillReason.NoCrew);
        check(run.Shown && run.ShowCycle && run.ShowRefill, "blocked start while the cycle runs: both parts");
        Sample(run, 206, cycling: true, reason: RefillReason.NoCrew);
        check(run.Shown && run.ShowCycle && !run.ShowRefill, "after the window only the cycle");
        // A Blocked reason followed by Full and the same reason again counts as a new start.
        var again = Make();
        Sample(again, 300, reason: RefillReason.CrewIdle);
        Sample(again, 306, reason: RefillReason.CrewIdle);
        Sample(again, 307, reason: RefillReason.Full, stored: 18);
        Sample(again, 308, reason: RefillReason.CrewIdle);
        check(again.Shown, "same reason after a pause in between: new window");
        // Window length from the config; 0 means never shown; a non-finite value falls back to 5.
        var cfg = Make();
        Sample(cfg, 400, reason: RefillReason.NoBody, blockedSeconds: 2f);
        Sample(cfg, 401.9f, reason: RefillReason.NoBody, blockedSeconds: 2f);
        check(cfg.Shown, "BlockedSeconds = 2: shown at 1.9 s");
        Sample(cfg, 402f, reason: RefillReason.NoBody, blockedSeconds: 2f);
        check(!cfg.Shown, "BlockedSeconds = 2: hidden at 2 s");
        var zero = Make();
        Sample(zero, 500, reason: RefillReason.NotEligible, blockedSeconds: 0f);
        check(!zero.Shown, "BlockedSeconds = 0: never shown");
        var nan = Make();
        Sample(nan, 600, reason: RefillReason.NotEligible, blockedSeconds: float.NaN);
        Sample(nan, 604.5f, reason: RefillReason.NotEligible, blockedSeconds: float.NaN);
        check(nan.Shown, "non-finite BlockedSeconds uses 5 s");
        // The pure window directly.
        var w = new BlockedWindow();
        check(w.Open(RefillReason.NoReserve, 0, 5) && w.Open(RefillReason.NoReserve, 4.99f, 5) && !w.Open(RefillReason.NoReserve, 5, 5) &&
              !w.Open(RefillReason.Crew, 6, 5) && w.Open(RefillReason.NoReserve, 7, 5), "BlockedWindow: start, end, a non-blocked reason resets it");
        // Every blocked reason is covered by the window; the others never open it.
        foreach (RefillReason reason in Enum.GetValues(typeof(RefillReason)))
        {
            var one = new BlockedWindow();
            check(one.Open(reason, 0, 5) == RefillPolicy.Blocked(reason), "window opens exactly for blocked reasons: " + reason);
        }
    }

    static void Choice(Action<bool, string> check)
    {
        Span<int> picked = stackalloc int[AutoloaderHudLayout.MaxRows];
        int n = AutoloaderHudLayout.Select(new[] { false, true, false, true, true, false, true, true }, picked);
        check(AutoloaderHudLayout.MaxRows == 4 && n == 4 && picked[0] == 1 && picked[1] == 3 && picked[2] == 4 && picked[3] == 6, "at most four lines, the first four running in tracking order");
        n = AutoloaderHudLayout.Select(new[] { true, true, true, true, true }, picked);
        check(n == 4 && picked[3] == 3, "five running: only the first four");
        n = AutoloaderHudLayout.Select(new[] { false, false, true }, picked);
        check(n == 1 && picked[0] == 2, "one running: one line");
        n = AutoloaderHudLayout.Select(new[] { false, false, false }, picked);
        check(n == 0, "nothing running: no lines");
        n = AutoloaderHudLayout.Select(ReadOnlySpan<bool>.Empty, picked);
        check(n == 0, "no autoloaders: no lines");
        Span<int> two = stackalloc int[2];
        check(AutoloaderHudLayout.Select(new[] { true, true, true }, two) == 2, "the limit is the size of the result");
    }

    static void Gate(Action<bool, string> check)
    {
        check(!HudRingGate.Supported(null), "kit version unknown: bars");
        check(!HudRingGate.Supported(new Version(0, 1, 7)) && !HudRingGate.Supported(new Version(0, 1, 7, 9)) && !HudRingGate.Supported(new Version(0, 1, 5)) &&
              !HudRingGate.Supported(new Version(0, 1, 0)) && !HudRingGate.Supported(new Version(0, 0, 9)), "kit 0.1.7 and older: bars");
        check(HudRingGate.Supported(new Version(0, 1, 8)) && HudRingGate.Supported(new Version(0, 1, 8, 0)) && HudRingGate.Supported(new Version(0, 1, 9)) &&
              HudRingGate.Supported(new Version(0, 2, 0)) && HudRingGate.Supported(new Version(1, 0)), "kit 0.1.8 and newer: rings");
        check(HudRingGate.MinimumKit == new Version(0, 1, 8), "the gate is 0.1.8");
        check(!HudRingGate.Supported(0, 1, 7) && HudRingGate.Supported(0, 1, 8) && HudRingGate.Supported(0, 1, 12) && HudRingGate.Supported(0, 2, 0) &&
              !HudRingGate.Supported(0, 0, 99) && HudRingGate.Supported(1, 0, 0) && !HudRingGate.Supported(-1, -1, -1), "the gate from the parts of the plugin version");
        check(HudRingGate.Supported(Version.Parse("0.1.8")) && !HudRingGate.Supported(Version.Parse("0.1.7")), "parsed plugin version strings");
        check(HudCardText.Centre(-1, false) == "" && HudCardText.Centre(24, false) == "2.4" && HudCardText.Centre(0, false) == "0.0" &&
              HudCardText.Centre(120, true) == "12" && HudCardText.Centre(1236, false) == "123.6", "ring centre text");
    }

    // Source guards: the ring calls stay in one class, the HUD files stay ASCII and free of reflection, and the version is 0.2.29 (0.2.29 carousel models).
    static void Sources(Action<bool, string> check)
    {
        string? root = FindRoot();
        check(root != null, "project folder found for the source guards");
        if (root == null) return;
        var all = Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly);
        foreach (var file in all)
        {
            var text = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            if (name != "AutoloaderHudRing.cs")
                check(!text.Contains(".Radial("), name + " does not call Radial (only AutoloaderHudRing does, behind the 0.1.8 gate)");
            if (name is not ("AutoloaderHudView.cs" or "AutoloaderHudRing.cs"))
                check(!text.Contains("using SprocketUiKit"), name + " has no kit types");
        }
        foreach (var name in new[] { "AutoloaderHudView.cs", "AutoloaderHudRing.cs", "AutoloaderHudFeed.cs", "AutoloaderHudLayout.cs", "HudCard.cs", "AmmoHudAnchor.cs" })
        {
            var text = File.ReadAllText(Path.Combine(root, name));
            check(text.All(ch => ch < 128), name + " is ASCII");
            check(!text.Contains("System.Reflection") && !text.Contains("GetField(") && !text.Contains("GetMethod(") && !text.Contains("GetProperty(") &&
                  !text.Contains("BindingFlags") && !text.Contains("AccessTools"), name + " uses no reflection");
        }
        check(File.ReadAllText(Path.Combine(root, "AmmoHudAnchor.cs")).Contains("\"sprocket.expandedballistics\"") &&
              File.ReadAllText(Path.Combine(root, "AmmoHudAnchor.cs")).Contains("\"Ammunition HUD\""), "Expanded Ballistics GUID and config section");
        check(File.ReadAllText(Path.Combine(root, "Plugin.cs")).Contains("public const string PluginVersion = \"0.3.6\";") &&
              File.ReadAllText(Path.Combine(root, "SprocketCarouselAutoloader.csproj")).Contains("<Version>0.3.6</Version>") &&
              File.ReadAllText(Path.Combine(root, "CHANGELOG.md")).Contains("## 0.3.6"), "version 0.3.6 in Plugin.cs, csproj and CHANGELOG");
        var plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
        check(plugin.Contains("SoftDependency") && !plugin.Contains(">=0.1.8"), "the kit stays a soft dependency without a version requirement");
    }

    static string? FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SprocketCarouselAutoloader.csproj"))) return dir.FullName;
        return null;
    }
}
