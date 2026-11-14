namespace BepInEx
{
    internal static class Paths { internal static string ConfigPath = ""; }
}
namespace SprocketExpedition.Game
{
    internal static class Plugin { internal static readonly TestLogger Logger = new(); }
    internal sealed class TestLogger
    {
        internal void LogInfo(string message) => Console.WriteLine(message);
        internal void LogError(string message) => Console.WriteLine(message);
    }
}
