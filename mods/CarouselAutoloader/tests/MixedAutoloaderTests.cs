using SprocketCarouselAutoloader;

internal static class MixedAutoloaderTests
{
    internal static void Run(Action<bool, string> check)
    {
        // 1. Complete 16-case truth table for RefillPolicy.CanTransferFromReserve
        foreach (var revolver in new[] { false, true })
        foreach (var full in new[] { false, true })
        foreach (var hasCrew in new[] { false, true })
        foreach (var hasReserve in new[] { false, true })
        {
            var expected = !full && hasReserve && (revolver || hasCrew);
            var actual = RefillPolicy.CanTransferFromReserve(revolver, full, hasCrew, hasReserve);
            check(actual == expected,
                $"CanTransferFromReserve(revolver={revolver}, full={full}, crew={hasCrew}, reserve={hasReserve}) == {expected}");
        }

        // 2. ReadyMagazineOnly truth table
        check(!RefillPolicy.ReadyMagazineOnly(false, true, false, false, false),
            "No autoloader: never restricted to ready magazine");
        check(!RefillPolicy.ReadyMagazineOnly(false, false, false, true, false),
            "Ordinary gun even with full rack flag stays unrestricted");
        check(RefillPolicy.ReadyMagazineOnly(true, true, false, false, false) &&
              !RefillPolicy.ReadyMagazineOnly(true, true, false, false, true),
            "Carousel restricted to carousel only when crew loader is absent");
        check(RefillPolicy.ReadyMagazineOnly(true, false, false, false, false) &&
              !RefillPolicy.ReadyMagazineOnly(true, false, false, false, true) &&
              RefillPolicy.ReadyMagazineOnly(true, false, false, true, true),
            "Bustle/Belt restricted when full or crewless, open when non-full with crew");
        check(!RefillPolicy.ReadyMagazineOnly(true, false, true, false, false) &&
              RefillPolicy.ReadyMagazineOnly(true, false, true, true, false),
            "Revolver restricted when full, unrestricted while non-full even without crew");

        // 3. BeltSourceCache switching between distinct mixed type IDs (5 and 6) on one aggregate
        var cache = new BeltSourceCache<string>();
        var aggPtr = new IntPtr(0x12345);
        check(!cache.TryGet(aggPtr, 5, out _), "empty BeltSourceCache misses type 5");
        cache.Store(aggPtr, 5, "view-type-5");
        check(cache.TryGet(aggPtr, 5, out var hit5) && hit5 == "view-type-5", "BeltSourceCache hits stored type 5");
        check(!cache.TryGet(aggPtr, 6, out _), "switching to type 6 misses cached type 5");
        cache.Store(aggPtr, 6, "view-type-6");
        check(cache.TryGet(aggPtr, 6, out var hit6) && hit6 == "view-type-6", "BeltSourceCache hits stored type 6");
        check(!cache.TryGet(aggPtr, 5, out _), "new type replaces previous cached entry cleanly");

        // 4. Source contract checks (v2 API, Belt no-LINQ/no-TakeShell invariant, exact 41 Harmony targets)
        var root = FindRoot();
        var apiSrc = File.ReadAllText(Path.Combine(root, "AmmoStorageApi.cs"));
        check(apiSrc.Contains("MixedStorageApiVersion=>2", StringComparison.Ordinal) &&
              apiSrc.Contains("GetRackCapacityForGeometry(", StringComparison.Ordinal) &&
              apiSrc.Contains("IsReadyMagazine(", StringComparison.Ordinal) &&
              apiSrc.Contains("GetReachableStorage(", StringComparison.Ordinal),
            "AmmoStorageApi exposes v2 mixed loader API");
        var beltSrc = File.ReadAllText(Path.Combine(root, "BeltRuntime.cs"));
        check(!beltSrc.Contains("OrderBy", StringComparison.Ordinal) &&
              !beltSrc.Contains(".ToList(", StringComparison.Ordinal) &&
              !beltSrc.Contains("TakeShell(", StringComparison.Ordinal),
            "BeltRuntime retains zero LINQ/sort/TakeShell invariants");
        var hooks = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => { var r = f.Replace('\\', '/'); return !r.Contains("/tests/") && !r.Contains("/bin/") && !r.Contains("/obj/"); })
            .Sum(f => File.ReadAllLines(f).Count(l => l.Contains("HarmonyPatch", StringComparison.Ordinal)));
        check(hooks == 52, $"Carousel exact 52 HarmonyPatch annotations preserved (actual={hooks})");

        Console.WriteLine("0.3.4 mixed autoloader regression tests PASS.");
    }

    private static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SprocketCarouselAutoloader.csproj"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }
}
