using System.Reflection;
namespace SprocketVisualEffects;

// Reflection-only optional reader for SprocketExpandedBallistics.BallisticsEffectApi.
// Allows Visual Effects to distinguish ERA, HEAT, HESH and internal APHE impacts
// without any compile-time or hard assembly dependency on ExpandedBallistics.
internal static class BallisticsApiClient
{
    private static Func<string>? readKind;
    private static long nextSearchMs = long.MinValue;

    internal static string ReadExplosionKind()
    {
        try
        {
            if (readKind == null)
            {
                var ticks = Environment.TickCount64;
                if (ticks < nextSearchMs) return "";
                nextSearchMs = ticks + 2000;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var api = asm.GetType("SprocketExpandedBallistics.BallisticsEffectApi", false);
                    if (api == null) continue;
                    var getter = api.GetProperty("CurrentExplosionKind", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                    if (getter != null)
                        readKind = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), getter);
                    break;
                }
            }
            return readKind != null ? (readKind() ?? "").Trim().ToLowerInvariant() : "";
        }
        catch
        {
            return "";
        }
    }
}
