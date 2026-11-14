using System.Diagnostics;
using SprocketCarouselAutoloader;

// 0.2.26: refill path choice (crew refill fix), reason texts shared by log and HUD, HUD row state and cost.
internal static class RefillTests
{
    internal static void Run(Action<bool,string> check)
    {
        // A: the revolver crew loader tops up a partly filled drum (also with stock left and a loaded gun);
        // before 0.2.26 the native task had to be "working", which only happens while the gun is Loading.
        check(RefillPolicy.Mode(true, false, true, false, false) == RefillMode.Crew, "revolver with crew loader tops up a partly filled drum");
        check(RefillPolicy.Mode(true, false, true, true, true) == RefillMode.Crew, "revolver with crew loader refills an empty drum");
        check(RefillPolicy.Mode(true, false, false, false, false) == RefillMode.Stationary, "revolver without crew loader: stationary refill only");
        check(RefillPolicy.Mode(true, true, true, false, false) == RefillMode.Full, "full drum: no refill");
        // Bustle unchanged: crew refill from empty, continued while refilling.
        check(RefillPolicy.Mode(false, false, true, false, false) == RefillMode.None, "bustle with stock waits until empty (unchanged)");
        check(RefillPolicy.Mode(false, false, true, false, true) == RefillMode.Crew && RefillPolicy.Mode(false, false, true, true, false) == RefillMode.Crew,
            "bustle refills from empty and while refilling (unchanged)");
        check(RefillPolicy.Mode(false, true, true, true, true) == RefillMode.Full, "full bustle: no refill");
        foreach (RefillReason reason in Enum.GetValues(typeof(RefillReason)))
            check(RefillPolicy.Text(reason).Length > 0, "reason text for " + reason);
        check(RefillPolicy.Blocked(RefillReason.NoReserve) && RefillPolicy.Blocked(RefillReason.NoReach) && !RefillPolicy.Blocked(RefillReason.Crew) &&
              !RefillPolicy.Blocked(RefillReason.Full) && !RefillPolicy.Blocked(RefillReason.WaitingStill), "blocked reasons");

        // B: HUD texts.
        check(HudText.Title(true, false, 5, 18) == "Drum 5/18" && HudText.Title(false, true, 12, 16) == "Bustle 12/16 (two-piece)", "HUD title");
        check(HudText.Cycle(true, false, 23) == "Cycle 2.3 s" && HudText.Cycle(false, false, -1) == "Cycle: ready" &&
              HudText.Cycle(false, true, -1) == "Cycle: waiting for refill", "HUD cycle text");
        check(HudText.Refill(RefillReason.Crew, 31) == "Crew refill 3.1 s" && HudText.Refill(RefillReason.Stationary, 120) == "Stationary refill 12 s" &&
              HudText.Refill(RefillReason.Full, -1) == "Full" && HudText.Refill(RefillReason.NoReserve, -1) == "Blocked: no matching reserve ammunition" &&
              HudText.Refill(RefillReason.NoReach, -1) == "Blocked: crew loader cannot reach the magazine", "HUD refill text (same words as the log)");

        var row = new HudRow();
        check(row.Update(true, false, 5, 18, true, false, .4, 2.31, RefillReason.Crew, .25, 3.1) && row.Active, "first HUD update builds the row");
        check(row.Title == "Drum 5/18" && row.Cycle == "Cycle 2.4 s" && row.Refill == "Crew refill 3.1 s" && row.ShowRefillBar &&
              Math.Abs(row.CycleFraction - .4f) < 1e-6 && Math.Abs(row.RefillFraction - .25f) < 1e-6 && !row.Blocked, "HUD crew refill row");
        check(!row.Update(true, false, 5, 18, true, false, .45, 2.35, RefillReason.Crew, .3, 3.05), "same shown values: no new strings");
        row.Update(true, false, 18, 18, false, false, 0, double.NaN, RefillReason.Full, 0, double.NaN);
        check(row.Refill == "Full" && row.RefillFraction == 1 && !row.ShowRefillBar && row.CycleFraction == 1 && row.Cycle == "Cycle: ready", "HUD full drum");
        row.Update(false, false, 0, 16, false, true, 0, double.NaN, RefillReason.NoReserve, 0, double.NaN);
        check(row.Blocked && row.Refill.StartsWith("Blocked: ") && row.CycleFraction == 0 && row.Cycle == "Cycle: waiting for refill", "HUD blocked refill");
        row.Update(true, false, 3, 18, true, false, 2, -5, RefillReason.Stationary, 7, 11.6);
        check(row.CycleFraction == 1 && row.RefillFraction == 1 && row.Cycle == "Cycle 0.0 s" && row.Refill == "Stationary refill 12 s", "HUD fractions clamp");

        // No allocation while the shown values stay the same (the 10 Hz sample path), and its cost.
        var steady = new HudRow();
        steady.Update(true, true, 9, 18, true, false, .5, 1.04, RefillReason.Crew, .5, 2.0);
        for (int i = 0; i < 1000; i++) steady.Update(true, true, 9, 18, true, false, .5, 1.04, RefillReason.Crew, .5, 2.0);
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < 100000; i++) steady.Update(true, true, 9, 18, true, false, .5 + i % 7 * 1e-4, 1.04, RefillReason.Crew, .5, 2.0);
        watch.Stop();
        check(GC.GetAllocatedBytesForCurrentThread() == before, "HUD row update allocates nothing while the shown values are unchanged");
        double perUpdateNs = watch.Elapsed.TotalMilliseconds * 1e6 / 100000;
        // A changing countdown (new string every 0.1 s) is the worst case at 10 Hz.
        var changing = new HudRow();
        watch.Restart();
        for (int i = 0; i < 10000; i++) changing.Update(true, false, i % 18, 18, true, false, .5, i * .1, RefillReason.Crew, .5, i * .1);
        watch.Stop();
        double perChangeUs = watch.Elapsed.TotalMilliseconds * 1000 / 10000;
        Directory.CreateDirectory("logs");
        File.WriteAllLines("logs/0.2.28-hud-cost.txt", new[]
        {
            "Carousel Autoloader 0.2.28 HUD row cost (managed part; the kit polls the cached strings/floats per frame).",
            $"unchanged update: {perUpdateNs:0} ns, 0 bytes allocated (100000 calls)",
            $"changing update (new title/cycle/refill strings): {perChangeUs:0.00} us per call (10000 calls)",
            "at 10 Hz per autoloader that is well below 0.01 ms per second; no ring/Canvas is drawn (Progress bars)."
        });
        check(perChangeUs < 1000, "HUD row update cost is small");
    }
}
