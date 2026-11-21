namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.101: standalone 9999.12.31 final-era fallback in RuntimeShellEra/ShellEraPolicy
/// and clear diagnostic warnings for missing Sprocket_Data technology files or broken Eras.
/// </summary>
internal static class Feat022101ShellEraFallbackAndWarningsTests
{
    private static readonly string[] VanillaNames = { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar" };
    private static readonly ShellNativeDate[] VanillaStarts =
    {
        new(1914, 7, 28),
        new(1918, 11, 12),
        new(1939, 9, 2),
        new(1942, 1, 1),
        new(1944, 1, 1)
    };

    internal static void Run(Action<bool, string> sink)
    {
        int checks = 0, failures = 0;
        void Check(bool ok, string msg) { checks++; if (!ok) failures++; sink(ok, "FEAT101: " + msg); }

        // 1. Standalone 9999.12.31 fallback in ShellEraPolicy.ResolveIndex
        Check(ShellEraPolicy.ResolveIndex(ShellNativeDate.Maximum, 5, -1) == 4,
            "1 ResolveIndex maps 9999.12.31 to last era when native GetEraIndex returns -1");
        Check(ShellEraPolicy.ResolveIndex(ShellNativeDate.Maximum, 8, 99) == 7,
            "1 ResolveIndex maps 9999.12.31 to last era when native GetEraIndex is out of range");
        Check(ShellEraPolicy.ResolveIndex(ShellNativeDate.Maximum, 0, -1) == -1,
            "1 ResolveIndex returns -1 for empty era list");
        Check(ShellEraPolicy.ResolveIndex(new ShellNativeDate(1944, 6, 6), 5, 4) == 4,
            "1 ResolveIndex preserves native index for normal finite date");
        Check(ShellEraPolicy.ResolveIndex(new ShellNativeDate(1910, 1, 1), 5, -1) == -1,
            "1 ResolveIndex preserves -1 for pre-timeline finite date");

        var allBehaviors = new HashSet<string>(StringComparer.Ordinal) { "ap", "he", "aphe", "heat", "hesh", "apfsds", "atgm", "atgm_gun" };
        var allTechs = allBehaviors.Select(b => "expandedBallistics_" + b).ToHashSet(StringComparer.Ordinal);
        allTechs.Add("expandedBallistics_apfsds_gen4");

        int resolvedSentinel = ShellEraPolicy.ResolveIndex(ShellNativeDate.Maximum, VanillaNames.Length, -1);
        var sentinelContext = new ShellEraContext(ShellNativeDate.Maximum, VanillaNames, VanillaStarts, resolvedSentinel, allBehaviors, 5.0, allTechs);
        Check(ShellEraPolicy.Valid(sentinelContext),
            "1 9999.12.31 context with ResolveIndex is valid on vanilla 5-era timeline");

        var modernShell = new ShellProfile("apfsds_gen4", "APFSDS Gen 4", new DartSettings(), "apfsds",
            MinimumDate: new ShellNativeDate(2003, 3, 1),
            NativeTechnologyIds: new[] { "expandedBallistics_apfsds", "expandedBallistics_apfsds_gen4" });
        Check(ShellEraPolicy.Allowed(modernShell, sentinelContext),
            "1 9999.12.31 allows modern 2003 shell on standalone vanilla era timeline");
        var avail = ShellEraSelection.Availability(modernShell, sentinelContext, false);
        Check(avail.Selectable && avail.Reason == "",
            "1 ShellEraSelection.Availability marks modern shell selectable on 9999.12.31");

        // Invalid timeline still rejected even on 9999.12.31
        var brokenStarts = new ShellNativeDate[] { new(1914, 7, 28), new(1944, 1, 1), new(1942, 1, 1) };
        var brokenContext = new ShellEraContext(ShellNativeDate.Maximum, new[] { "A", "B", "C" }, brokenStarts, 2, allBehaviors, 5.0, allTechs);
        Check(!ShellEraPolicy.Valid(brokenContext),
            "1 Invalid non-ascending era timeline is still rejected on 9999.12.31");

        // 2. Technology directory diagnostics
        var root = Fix02228Tests.SourceRoot();
        var shippedTechDir = Path.Combine(root, "native-data", "Technology");
        var shippedStatus = ShellEraDiagnostics.CheckTechnologyDirectory(shippedTechDir);
        Check(shippedStatus.Ok && shippedStatus.Warning == null && shippedStatus.FoundCount >= ShellEraDiagnostics.CoreTechnologyFiles.Length,
            $"2 Shipped native-data/Technology passes CheckTechnologyDirectory (found={shippedStatus.FoundCount})");

        var tempDir = Path.Combine(Path.GetTempPath(), "eb-feat101-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            var missingDirStatus = ShellEraDiagnostics.CheckTechnologyDirectory(tempDir);
            Check(missingDirStatus.Kind == TechnologyFilesKind.MissingDirectory
                && missingDirStatus.Warning != null
                && missingDirStatus.Warning.Contains(@"Sprocket_Data\StreamingAssets\Technology\", StringComparison.Ordinal)
                && missingDirStatus.Warning.Contains("restart", StringComparison.OrdinalIgnoreCase),
                "2 Missing Technology directory produces clear actionable warning");

            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "RolledHomogeneousArmour.json"), "{}");
            var emptyTechStatus = ShellEraDiagnostics.CheckTechnologyDirectory(tempDir);
            Check(emptyTechStatus.Kind == TechnologyFilesKind.MissingAll
                && emptyTechStatus.Warning != null
                && emptyTechStatus.Warning.Contains("ExpandedBallistics-*.json", StringComparison.Ordinal)
                && emptyTechStatus.Warning.Contains("Sprocket_Data", StringComparison.Ordinal),
                "2 Directory without ExpandedBallistics-*.json produces MissingAll warning");

            foreach (var f in ShellEraDiagnostics.CoreTechnologyFiles.Take(5))
                File.WriteAllText(Path.Combine(tempDir, f), "{}");
            var partialStatus = ShellEraDiagnostics.CheckTechnologyDirectory(tempDir);
            Check(partialStatus.Kind == TechnologyFilesKind.MissingCore
                && partialStatus.MissingCoreFiles.Count == 3
                && partialStatus.Warning != null
                && partialStatus.Warning.Contains("ExpandedBallistics-apfsds.json", StringComparison.Ordinal),
                "2 Partial core technology files produces MissingCore warning naming missing files");

            foreach (var f in ShellEraDiagnostics.CoreTechnologyFiles)
                File.WriteAllText(Path.Combine(tempDir, f), "{}");
            var fullStatus = ShellEraDiagnostics.CheckTechnologyDirectory(tempDir);
            Check(fullStatus.Ok && fullStatus.Warning == null && fullStatus.FoundCount == 8,
                "2 Complete core technology files passes cleanly");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        // 3. Era timeline diagnostics
        Check(ShellEraDiagnostics.DiagnoseTimeline(new ShellNativeDate(1944, 6, 6), VanillaNames, VanillaStarts, 4) == null,
            "3 Valid timeline and finite date produces no warning");
        Check(ShellEraDiagnostics.DiagnoseTimeline(ShellNativeDate.Maximum, VanillaNames, VanillaStarts, 4) == null,
            "3 Valid timeline and 9999.12.31 sentinel produces no warning");

        var emptyTimeline = ShellEraDiagnostics.DiagnoseTimeline(new ShellNativeDate(1944, 6, 6), Array.Empty<string>(), Array.Empty<ShellNativeDate>(), -1);
        Check(emptyTimeline != null && emptyTimeline.Contains(@"Sprocket_Data\StreamingAssets\Eras\", StringComparison.Ordinal),
            "3 Empty era timeline warns with Eras path");

        var blankName = ShellEraDiagnostics.DiagnoseTimeline(null, new[] { "WWI", "" }, new[] { new ShellNativeDate(1914, 7, 28), new ShellNativeDate(1939, 9, 2) }, -1);
        Check(blankName != null && blankName.Contains("missing a name", StringComparison.OrdinalIgnoreCase),
            "3 Blank era name warns clearly");

        var dupName = ShellEraDiagnostics.DiagnoseTimeline(null, new[] { "WWI", "wwi" }, new[] { new ShellNativeDate(1914, 7, 28), new ShellNativeDate(1939, 9, 2) }, -1);
        Check(dupName != null && dupName.Contains("duplicate era name", StringComparison.OrdinalIgnoreCase),
            "3 Duplicate era name (case-insensitive) warns clearly");

        var nonAsc = ShellEraDiagnostics.DiagnoseTimeline(null, new[] { "A", "B" }, new[] { new ShellNativeDate(1944, 1, 1), new ShellNativeDate(1942, 1, 1) }, -1);
        Check(nonAsc != null && nonAsc.Contains("not strictly ascending", StringComparison.OrdinalIgnoreCase),
            "3 Non-ascending era start dates warn clearly");

        var maxStart = ShellEraDiagnostics.DiagnoseTimeline(null, new[] { "A", "B" }, new[] { new ShellNativeDate(1914, 7, 28), ShellNativeDate.Maximum }, -1);
        Check(maxStart != null && maxStart.Contains("9999.12.31", StringComparison.Ordinal),
            "3 Last era starting at 9999.12.31 warns clearly");

        var badVehicleDate = ShellEraDiagnostics.DiagnoseTimeline(new ShellNativeDate(1944, 13, 1), VanillaNames, VanillaStarts, -1);
        Check(badVehicleDate != null && badVehicleDate.Contains("invalid design date", StringComparison.OrdinalIgnoreCase),
            "3 Invalid vehicle design date warns clearly");

        var unmatchedDate = ShellEraDiagnostics.DiagnoseTimeline(new ShellNativeDate(1900, 1, 1), VanillaNames, VanillaStarts, -1);
        Check(unmatchedDate != null && unmatchedDate.Contains("1900.01.01", StringComparison.Ordinal),
            "3 Vehicle date before first era warns with date");

        // 4. Combined Diagnose & session-not-loaded check
        var okFiles = new TechnologyFilesStatus(TechnologyFilesKind.Ok, 29, Array.Empty<string>(), null);
        Check(ShellEraDiagnostics.Diagnose(okFiles, null, sentinelContext) == null,
            "4 Diagnose returns null when files, timeline, and loaded techs are healthy");

        var emptyLoadedContext = new ShellEraContext(new ShellNativeDate(1944, 6, 6), VanillaNames, VanillaStarts, 4,
            new HashSet<string>(StringComparer.Ordinal), null, new HashSet<string>(StringComparer.Ordinal));
        var notLoadedWarn = ShellEraDiagnostics.Diagnose(okFiles, null, emptyLoadedContext);
        Check(notLoadedWarn != null && notLoadedWarn.Contains("Restart Sprocket", StringComparison.OrdinalIgnoreCase),
            "4 Files on disk but 0 behaviors loaded on 1944 vehicle prompts restart");

        var preWwiStarts = new[] { new ShellNativeDate(1890, 1, 1), new ShellNativeDate(1914, 7, 28) };
        var preWwiContext = new ShellEraContext(new ShellNativeDate(1900, 1, 1), new[] { "Pre", "WWI" }, preWwiStarts, 0,
            new HashSet<string>(StringComparer.Ordinal), null, new HashSet<string>(StringComparer.Ordinal));
        Check(ShellEraDiagnostics.Diagnose(okFiles, null, preWwiContext) == null,
            "4 Pre-1914 vehicle with 0 loaded behaviors does not emit false-positive restart warning");

        var missingFiles = new TechnologyFilesStatus(TechnologyFilesKind.MissingAll, 0, ShellEraDiagnostics.CoreTechnologyFiles,
            "Warning: Missing ExpandedBallistics-*.json files.");
        var combined = ShellEraDiagnostics.Diagnose(missingFiles, "Warning: No eras are loaded.", null);
        Check(combined != null && combined.Contains("Missing ExpandedBallistics-*.json", StringComparison.Ordinal)
            && combined.Contains("Also: No eras are loaded.", StringComparison.Ordinal)
            && !combined.Contains("Also: Warning:", StringComparison.Ordinal),
            "4 Combined files + timeline warning avoids duplicate 'Warning:' prefix");

        // 5. Source invariants & version/hook count
        string Read(string rel) => File.ReadAllText(Path.Combine(root, rel)).Replace("\r\n", "\n");
        var runtimeEra = Read("RuntimeShellEra.cs");
        Check(runtimeEra.Contains("ShellEraPolicy.ResolveIndex(", StringComparison.Ordinal)
            && runtimeEra.Contains("internal static string? DiagnosticWarning(", StringComparison.Ordinal),
            "5 RuntimeShellEra uses ShellEraPolicy.ResolveIndex and exposes DiagnosticWarning");

        var editorPanel = Read("ShellEditorPanel.cs");
        int bannerPos = editorPanel.IndexOf("diagBanner.Warning(", StringComparison.Ordinal);
        int colsPos = editorPanel.IndexOf("var cols=p.Columns(", StringComparison.Ordinal);
        Check(bannerPos > 0 && colsPos > bannerPos
            && editorPanel.Contains("RuntimeShellEra.DiagnosticWarning(", StringComparison.Ordinal),
            "5 ShellEditorPanel displays diagnostic warning banner above panel columns");

        var selection = Read("RuntimeShellSelection.cs");
        Check(selection.Contains("RuntimeShellEra.DiagnosticWarning(", StringComparison.Ordinal)
            && selection.Contains("ui.InfoField(eraWarning,", StringComparison.Ordinal),
            "5 RuntimeShellSelection displays diagnostic warning in cannon inspector");

        Check(Read("Plugin.cs").Contains("\"0.22.101\"", StringComparison.Ordinal)
            && Read("SprocketExpandedBallistics.csproj").Contains("<Version>0.22.101</Version>", StringComparison.Ordinal)
            && Read("RELEASE-NOTES.md").Contains("## 0.22.101", StringComparison.Ordinal),
            "5 Plugin.cs, csproj, and RELEASE-NOTES.md carry version 0.22.101");

        int hooks = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => { var r = f.Replace('\\', '/'); return !r.Contains("/tests/") && !r.Contains("/bin/") && !r.Contains("/obj/"); })
            .Sum(f => File.ReadLines(f).Count(l => l.Contains("[HarmonyPatch", StringComparison.Ordinal)));
        Check(hooks == 48, $"5 Non-test [HarmonyPatch line count stays 48 (actual={hooks})");

        Console.WriteLine($"FEAT101: {checks} checks, {failures} failures");
    }
}
