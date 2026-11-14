using SprocketCarouselAutoloader;

// 0.2.28 safety release: restore and time-out (A1), config switches and the Unity version check (A2/A3),
// early exit (M5) and the source guards for guards, merges and versions. Pure logic plus source reads.
internal static class SafetyTests
{
    // A load task as the magazine route sees it: the fields the snapshot records and writes back.
    sealed record FakeTask(int State, int Action, string? Rack, string Request, float Travel, float Pickup);

    internal static void Run(Action<bool, string> check)
    {
        Timeout(check);
        Ledger(check);
        Warnings(check);
        Switches(check);
        Presence(check);
        Config(check);
        Sources(check);
    }

    static void Timeout(Action<bool, string> check)
    {
        check(GuardPolicy.TimeoutSeconds(0) == 10 && GuardPolicy.TimeoutSeconds(2) == 10 && GuardPolicy.TimeoutSeconds(10d / 3) == 10,
            "time-out is at least 10 s");
        check(GuardPolicy.TimeoutSeconds(5) == 15 && GuardPolicy.TimeoutSeconds(11.4) == 3 * 11.4, "time-out is 3 x the expected cycle above 10 s");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0 })
            check(GuardPolicy.TimeoutSeconds(invalid) == 10, "unknown or invalid cycle gives the 10 s minimum");
        check(!GuardPolicy.Expired(0, 10, 2) && GuardPolicy.Expired(0, 10.001, 2), "expires only after the minimum");
        check(!GuardPolicy.Expired(100, 115, 5) && GuardPolicy.Expired(100, 115.01, 5), "expires only after 3 cycles");
        check(!GuardPolicy.Expired(double.NaN, 50, 1) && !GuardPolicy.Expired(0, double.NaN, 1), "invalid times never expire");
    }

    static void Ledger(Action<bool, string> check)
    {
        var ledger = new InterventionLedger<FakeTask>();
        var key = (IntPtr)0x1000;
        // Native state just before the route: fired (Loaded, Consumed) from reserve rack A, progress complete.
        var before = new FakeTask(0, 0, "rack A", "Consumed", 1.2f, .8f);
        check(ledger.Begin(key, before, 5) && ledger.Contains(key) && ledger.Count == 1, "route opens with its snapshot");
        var during = before with { State = 1, Rack = "magazine", Travel = 0, Pickup = 0 }; // the route's artificial state
        check(!ledger.Begin(key, during, 6), "a second begin keeps the first snapshot");
        check(ledger.Started(key) == 5 && double.IsNaN(ledger.Started((IntPtr)0x2000)), "start time recorded per task");
        check(ledger.Expired(key, 14.9, 2) == null && ledger.Contains(key), "no time-out before 10 s");
        var restored = ledger.Expired(key, 15.01, 2);
        check(ReferenceEquals(restored, before) && !ledger.Contains(key), "time-out returns the original snapshot once and closes the entry");
        check(restored == new FakeTask(0, 0, "rack A", "Consumed", 1.2f, .8f) && restored != during, "restore writes back every recorded field");
        check(ledger.Expired(key, 100, 2) == null, "a closed entry cannot be restored twice");

        // Error: abort returns the snapshot; normal end returns nothing to restore.
        ledger.Begin(key, before, 20);
        check(ReferenceEquals(ledger.Abort(key), before) && ledger.Count == 0 && ledger.Abort(key) == null, "error returns the snapshot once");
        ledger.Begin(key, before, 30);
        check(ledger.Complete(key) && ledger.Expired(key, 1000, 0) == null && ledger.Abort(key) == null, "resumed task: nothing to restore");
        // A slow refill (stationary revolver, 11.4 s per round) gets a longer wait before the time-out.
        ledger.Begin(key, before, 0);
        check(ledger.Expired(key, 34, 11.4) == null && ledger.Expired(key, 34.3, 11.4) != null, "slow refill: 3 x 11.4 s");
        // Independent tasks.
        ledger.Begin((IntPtr)1, before, 0); ledger.Begin((IntPtr)2, during, 0);
        check(ReferenceEquals(ledger.Abort((IntPtr)2), during) && ledger.Contains((IntPtr)1), "snapshots are kept per task");
    }

    static void Warnings(Action<bool, string> check)
    {
        var warn = new WarnOnce();
        int lines = 0;
        // 600 frames of three failing hooks on two cannons: one line per hook and VUID, never per frame.
        for (int frame = 0; frame < 600; frame++)
            foreach (var hook in new[] { "LoadTask.Update rapid cycle", "LoadTask.Update semi assistance", "VehicleWeaponLoadController.Update" })
                foreach (var vuid in new[] { 7, 12 })
                    if (warn.First(hook, vuid)) lines++;
        check(lines == 6 && warn.Count == 6, "one warning per hook and VUID over 600 frames");
        check(!warn.First("LoadTask.Update rapid cycle", 7) && warn.First("LoadTask.Update rapid cycle", 8), "a new VUID warns once");
    }

    static void Switches(Action<bool, string> check)
    {
        var route = new SessionSwitch();
        check(!route.Any && route.Cached((IntPtr)10) == null, "nothing switched off before a failure");
        check(route.Disable((IntPtr)10, "T-84|5") && !route.Disable((IntPtr)10, "T-84|5"), "one warning per cannon key");
        check(route.Any && route.Cached((IntPtr)10) == true, "the failed cannon is off");
        // Respawn of the same design: new pointer, same key -> still off for the session.
        check(route.Cached((IntPtr)20) == null && route.Resolve((IntPtr)20, "T-84|5") && route.Cached((IntPtr)20) == true, "respawn stays off");
        check(!route.Resolve((IntPtr)30, "T-84|6") && route.Cached((IntPtr)30) == false, "another cannon stays on");
        route.ForgetPointers();
        check(route.Cached((IntPtr)10) == null && route.Resolve((IntPtr)10, "T-84|5"), "pointer cache cleared on topology change, key kept");
    }

    static void Presence(Action<bool, string> check)
    {
        // Early exit: one resolve (the only allocation), then dictionary answers without allocation.
        var cache = new PresenceCache<string>();
        int resolves = 0;
        Func<int, string?> resolve = source => { resolves++; return source > 0 ? "bustle" : null; };
        check(cache.Get((IntPtr)1, 5, resolve) == "bustle" && resolves == 1, "first call resolves");
        for (int i = 0; i < 1000; i++) cache.Get((IntPtr)1, 5, resolve);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) cache.Get((IntPtr)1, 5, resolve);
        check(GC.GetAllocatedBytesForCurrentThread() == before, "cached early-exit lookups allocate nothing");
        check(resolves == 1 && cache.Resolves == 1, "no further resolves while cached");
        check(cache.Get((IntPtr)2, 0, resolve) == null && cache.Get((IntPtr)2, 0, resolve) == null && resolves == 3,
            "an unresolvable object is not cached (unknown: the 0.2.27 path runs)");
        cache.Clear();
        check(cache.Count == 0 && cache.Get((IntPtr)1, 5, resolve) == "bustle" && resolves == 4, "topology change resolves again");
        check(!EarlyExit.Candidate(0, 0), "no rack and no enabled basket: early exit");
        check(EarlyExit.Candidate(1, 0) && EarlyExit.Candidate(0, 1) && EarlyExit.Candidate(2, 3), "bustle/revolver/Semi rack or enabled basket: full path");
    }

    static void Config(Action<bool, string> check)
    {
        check(SafetySwitches.PatchNativeDamage(true) && !SafetySwitches.PatchNativeDamage(false), "Damage.NativeCookoff decides the native-damage group");
        check(SafetySwitches.AudioUnityVersion == "6000.3.21f1", "raw audio calls made for Unity 6000.3.21f1");
        check(SafetySwitches.AudioOff(true, "6000.3.21f1") == null, "audio on: enabled and the inspected Unity version");
        check(SafetySwitches.AudioOff(false, "6000.3.21f1") == "config Audio.Enabled = false", "Audio.Enabled = false: off");
        var other = SafetySwitches.AudioOff(true, "6000.3.22f1");
        check(other != null && other.Contains("6000.3.22f1") && other.Contains("6000.3.21f1"), "another Unity version: off, both versions in the Info line");
        check(SafetySwitches.AudioOff(true, "6000.4.0f1") != null && SafetySwitches.AudioOff(true, "6000.3.21f2") != null, "exact version match only");
        check(SafetySwitches.AudioOff(true, null) != null && SafetySwitches.AudioOff(true, " ") != null, "unreadable version: off");
        check(SafetySwitches.AudioOff(false, null) == "config Audio.Enabled = false", "the switch is reported before the version");
    }

    static void Sources(Action<bool, string> check)
    {
        var root = FindRoot();
        check(root != null, "project folder found for the safety source guards");
        if (root == null) return;
        string File(string name) => System.IO.File.ReadAllText(Path.Combine(root, name));
        var plugin = File("Plugin.cs");
        check(plugin.Contains("Config.Bind(\"Damage\",\"NativeCookoff\",true,") && plugin.Contains("Config.Bind(\"Audio\",\"Enabled\",true,"),
            "config [Damage] NativeCookoff and [Audio] Enabled, both default true");
        check(plugin.Contains("SafetySwitches.PatchNativeDamage(NativeCookoff)") && plugin.Contains("SafetySwitches.AudioOff(AudioEnabled, running)") &&
              plugin.Contains("UnityEngine.Application.unityVersion"), "Plugin uses the tested switch logic and Application.unityVersion");
        int audioCheck = plugin.IndexOf("SafetySwitches.AudioOff(", StringComparison.Ordinal), preload = plugin.IndexOf("AutoloaderAudio.PreloadClips()", StringComparison.Ordinal);
        check(audioCheck > 0 && preload > audioCheck && plugin.Split("AutoloaderAudio.PreloadClips()").Length == 2, "the raw audio upload runs only after the switch/version check");
        check(!plugin.Contains("typeof(NativeCycleDiagnostics)") && !plugin.Contains("typeof(RevolverCountdown)"), "merged classes are no longer patched on their own");
        var audio = File("AutoloaderAudio.cs");
        check(Body(audio, "Updated").TrimStart('{', ' ', '\r', '\n').StartsWith("if (!Active) return;") && Body(audio, "Release").Contains("if (!Active) return;"),
            "own audio and its cleanup only while active");

        // A1: restore through raw fields, never SetState; time-out and switch present.
        var refill = File("MagazineRefill.cs");
        check(!Body(refill, "Restore").Contains("SetState") && Body(refill, "Restore").Contains("task.state = snapshot.State"), "restore writes raw fields, never SetState");
        check(Body(refill, "Resume").Contains("Routes.Expired(") && Body(refill, "Abandon").Contains("RouteSwitch.Disable("), "magazine route time-out and session switch");
        var runtime = File("CarouselRuntime.cs");
        check(Body(runtime, "AttachNativeLoader").Contains("weapon.LoadTaskContributors = previous") &&
              Body(runtime, "RefreshCapturedLoaders").Contains("entry.Contributors = captured") && runtime.Split("ContributorSwitch.Disable(").Length == 3,
            "contributor writes restore the old array and switch off per cannon");

        // M5: the early exit comes before the first wrapper allocation of every per-frame hook.
        Early(check, refill, "Tick", "HasBustles(__instance)", "CannonFor(");
        Early(check, refill, "KeepMagazineRoute", "HasBustles(__instance)", "CannonFor(");
        Early(check, refill, "AfterUpdate", "HasBustles(__instance)", "CannonFor(");
        Early(check, runtime, "Speed", "HasLoader(__instance)", "TryCast");
        Early(check, runtime, "SelectSupply", "HasLoader(__instance)", "TryCast");
        Early(check, runtime, "RequestUpdated", "HasLoader(__instance)", "Timings(");
        Early(check, runtime, "RefreshNativeLoaders", "HasLoader(__instance)", "RefreshCapturedLoadersSafely(");
        Early(check, File("AutoloaderCycle.cs"), "RapidCycle", "HasLoader(__instance)", "Seconds(__instance)");
        Early(check, File("SemiAutoloader.cs"), "Assist", "HasBustles(__instance)", "TryCast");
        Early(check, audio, "Updated", "HasLoader(__instance)", "Observe(");
        Early(check, File("TurretAudioGuard.cs"), "PlaybackEligible", "AutoloaderVehicle(__instance)", "__instance.source");

        // A3: hooks that were unguarded or partly guarded in 0.2.27 now catch their own errors.
        foreach (var (file, method) in new[] {
            ("AutoloaderCycle.cs", "RapidCycle"), ("SemiAutoloader.cs", "Assist"), ("MagazineRefill.cs", "Tick"), ("MagazineRefill.cs", "AfterUpdate"),
            ("MagazineRefill.cs", "Resume"), ("SemiCountdown.cs", "Remaining"), ("SemiCountdown.cs", "Fraction"), ("CarouselRuntime.cs", "CannonDisabled"),
            ("CarouselRuntime.cs", "Released"), ("CarouselRuntime.cs", "Loaded"), ("CarouselRuntime.cs", "RestoreOperables"), ("CarouselRuntime.cs", "RefreshNativeLoaders"),
            ("BustleRuntime.cs", "Release"), ("BustleRuntime.cs", "PartImported"), ("BustleVisuals.cs", "CardCreated"), ("CarouselPlayBoundary.cs", "BeforeEditorPlay"),
            ("CarouselPlayBoundary.cs", "FinishedEditorPlay"), ("TurretAudioGuard.cs", "PlaybackEligible"), ("TurretAudioGuard.cs", "Execute"),
            ("VehicleLoadIndex.cs", "TopologyChanged"), ("CarouselNativeDamage.cs", "PoolReady"), ("CarouselNativeDamage.cs", "ModuleReady"),
            ("CarouselNativeDamage.cs", "ModuleReleased"), ("CarouselNativeDamage.cs", "RegisterReleased"), ("AutoloaderVisuals.cs", "RenderersApplied") })
            check(Body(File(file), method).Contains("catch"), $"{file} {method} catches its own errors");
    }

    static void Early(Action<bool, string> check, string source, string method, string exit, string firstCost)
    {
        var body = Body(source, method);
        int e = body.IndexOf(exit, StringComparison.Ordinal), c = body.IndexOf(firstCost, StringComparison.Ordinal);
        check(e >= 0 && (c < 0 || e < c), $"{method}: early exit before {firstCost}");
    }

    // Body of the first method declaration "Name(" in a C# source (braces matched; expression bodies up to ';').
    static string Body(string source, string name)
    {
        int at = -1;
        foreach (var prefix in new[] { "static void ", "static bool ", "static " })
        {
            at = source.IndexOf(prefix + name + "(", StringComparison.Ordinal);
            if (at < 0) at = source.IndexOf(prefix + "bool " + name + "(", StringComparison.Ordinal);
            if (at >= 0) break;
        }
        if (at < 0) return "";
        int open = source.IndexOf('{', at), arrow = source.IndexOf("=>", at, StringComparison.Ordinal);
        if (arrow >= 0 && arrow < open && source.IndexOf(';', at) < open) return source.Substring(arrow, source.IndexOf(';', arrow) - arrow);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        return "";
    }

    static string? FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (System.IO.File.Exists(Path.Combine(dir.FullName, "SprocketCarouselAutoloader.csproj"))) return dir.FullName;
        return null;
    }
}
