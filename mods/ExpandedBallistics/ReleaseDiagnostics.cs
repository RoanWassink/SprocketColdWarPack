using BepInEx.Configuration;
namespace SprocketExpandedBallistics;
internal static class ReleaseDiagnostics
{
 private static ConfigEntry<bool>? detailed;
 internal static bool Enabled => detailed?.Value==true;
 internal static void Configure(ConfigFile config) => detailed=config.Bind("Diagnostics","Detailed logging",false,"Enable detailed impact, ERA and loader traces for troubleshooting. Disabled by default; enable only for troubleshooting.");
}
