namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.23: the composer table and the charts need UI Kit 0.1.7 (Table 0.1.6, LineChart/BarChart 0.1.7). BepInEx already refuses the plugin through its
/// dependency range; this check adds a clear log line of our own (and is what the installer script tests on the installed Kit DLL).
/// </summary>
internal static class KitVersionGate
{
    internal const string Required="0.1.7";
    /// <summary>True when <paramref name="found"/> (a "major.minor.patch[.x]" text) is at least <see cref="Required"/> and below 0.2.0.</summary>
    internal static bool Supported(string? found)
    {
        if(string.IsNullOrWhiteSpace(found)||!Version.TryParse(Clean(found),out var v))return false;
        return v>=Version.Parse(Required)&&v<new Version(0,2,0);
    }
    private static string Clean(string text){var t=text.Trim();var plus=t.IndexOfAny(new[]{'+','-'});if(plus>0)t=t[..plus];return t;}
    internal static string Message(string? found)=>$"Expanded Ballistics 0.22.23 needs Sprocket UI Kit {Required} or newer (below 0.2.0) for the composer table and the charts; found {(string.IsNullOrWhiteSpace(found)?"none":found)}. The mod is not loaded: update SprocketUiKit.dll in BepInEx\\plugins\\SprocketUiKit.";
}
