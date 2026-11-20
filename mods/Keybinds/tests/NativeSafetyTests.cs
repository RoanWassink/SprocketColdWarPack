using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class NativeSafetyTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var plugin = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "ModSettings.cs"));
        var panel = File.ReadAllText(Path.Combine(root, "ModPanel.cs"));
        check(!settings.Contains("UnityEngine") && !settings.Contains("Il2Cpp") && !settings.Contains("Keybinds.Ready"), "ModSettings uses only managed config and metadata");
        var protectedFiles = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "tests", "unchanged-source-sha256.json")))!;
        foreach (var file in protectedFiles)
            check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file.Key)))) == file.Value, "unchanged protected source " + file.Key);
        check(Regex.IsMatch(plugin, @"internal static class CancelPatch\s*\{\s*static void Prefix\(\) => Plugin.Safe\(\(\) => \{ VanillaMenu.StopRebinding\(\); Keybinds.End\(\); \}\);\s*\}"), "CancelPatch remains unchanged");
        foreach (string patch in new[] { "CapturePatch", "ResetPatch" })
            check(Regex.IsMatch(plugin, @"internal static class " + patch + @"\s*\{\s*static bool Prefix\(RebindActionUI __instance\)\s*\{\s*try \{ return !VanillaMenu\.\w+\(__instance\); \}\s*catch \(Exception e\) \{ Plugin.Logger.LogError\([^\r\n]+; return true; \}"), patch + " catches one error and falls back to vanilla");
        check(plugin.Contains("Config.Bind(\"Diagnostics\", \"DetailedLogging\", false, \"Verbose trace lines for testing. Warnings and errors are always logged.\")"), "DetailedLogging exact cfg contract");
        check(plugin.Contains("if (Plugin.DetailedLogging?.Value == true) Plugin.LogInfo(\"[Input lifetime] Scenes=\""), "scene trace uses live null-safe DetailedLogging");
        check(plugin.Contains("\"open-mod-settings\", \"Open mod settings\", \"\")")
            && plugin.IndexOf("Keybinds.Initialize(", StringComparison.Ordinal) < plugin.IndexOf("OpenModSettings = Keybinds.RegisterButton", StringComparison.Ordinal), "own action registered unbound after store initialization");
        check(plugin.Contains("Plugin.OpenModSettings?.WasPressedThisFrame == true && ModSettings.HasPresenter"), "existing Update polls nullable action with presenter guard");
        check(panel.Contains("\"ModKeybinds.Open\", \"Mod keybinds\", Open, out _") && panel.Contains("() => Plugin.Safe(OpenSettings)")
            && panel.Contains("enabled ? \"Open\" : \"Needs UI Kit\"") && panel.Contains("if (panel != null) { RefreshSettingsLauncher(); return; }"), "launcher helper preserves old click path and refreshes presenter state");
        check(Regex.IsMatch(panel, @"private static void OpenSettings\(\)\s*\{\s*if \(!ModSettings.HasPresenter\) \{ RefreshSettingsLauncher\(\); return; \}\s*ModSettings.Open\(\);\s*if \(settingsValue != null\) settingsValue.text = ""Opens after Settings"";\s*\}"), "row click opens through presenter then shows deferred status");
        LoadSafety.Run(plugin, check);
        var keybinds = File.ReadAllText(Path.Combine(root, "Keybinds.cs"));
        check(Regex.IsMatch(keybinds, @"defaults = paths.Select\(ButtonBinding.Parse\).ToArray\(\);")
            && keybinds.Contains("path = p.Path, action = actionName") && keybinds.Contains("Asset.Enable();"), "ModKeybind constructor serializes the parsed empty binding and enables the action");
        var unbound = SprocketKeybinds.ButtonBinding.Parse("");
        check(unbound.Path == "" && unbound.Canonical == "" && unbound.Modifiers == SprocketKeybinds.Modifiers.None
            && unbound.Display == "Unbound", "empty defaultPath is the existing valid unbound representation");
        var attributes = Regex.Matches(plugin, @"\[HarmonyPatch\(typeof\((\w+)\), nameof\(\w+\.(\w+)\)\)\]");
        int allAttributes = Directory.GetFiles(root, "*.cs").Sum(f => Regex.Matches(File.ReadAllText(f), @"\[HarmonyPatch\(").Count);
        check(attributes.Count == 10 && allAttributes == 10, "exactly 10 Harmony patch targets across all plugin sources");
        var dump = Environment.GetEnvironmentVariable("SPROCKET_NATIVE_METHODS")
            ?? Path.GetFullPath(Path.Combine(root, "..", "..", "..", "2026-10-04", "dez", "work", "ammo-switch-research", "all-native-methods.json"));
        if (!File.Exists(dump)) { Console.WriteLine("SKIP ICF: native method dump unavailable (SPROCKET_NATIVE_METHODS)"); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(dump));
        var methods = doc.RootElement.EnumerateArray().Select(m => new { Name = m.GetProperty("Name").GetString()!,
            Rva = Convert.ToUInt64(m.GetProperty("RVA").GetString()![2..], 16), Length = m.GetProperty("Length").GetString() }).ToArray();
        var rvaCounts = methods.GroupBy(m => m.Rva).ToDictionary(g => g.Key, g => g.Count());
        var report = new List<string> { "| Target | RVA | Length | Occurrences at RVA |", "|---|---|---|---|" };
        foreach (Match target in attributes)
        {
            string prefix = "System.Void Sprocket.SettingConfiguration." + target.Groups[1].Value + "::" + target.Groups[2].Value + "(";
            var found = methods.Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            check(found.Length == 1, "ICF target occurs exactly once: " + prefix);
            var method = found.Single();
            check(method.Rva != 0 && rvaCounts[method.Rva] == 1, "ICF RVA unshared: " + target.Groups[1].Value + "." + target.Groups[2].Value);
            report.Add($"| {method.Name} | 0x{method.Rva:X} | {method.Length} | {rvaCounts[method.Rva]} |");
        }
        File.WriteAllLines(Path.Combine(root, "logs", "native-targets.md"), report);
    }
}
