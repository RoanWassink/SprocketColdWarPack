using System.Reflection;

namespace SprocketExpandedBallistics;

/// <summary>
/// Late-bound read of the authoritative Expedition session owner. No plugin-presence
/// check, title/name heuristic or saved config is used. Missing/unknown state returns
/// null so the picker filter fails closed.
/// </summary>
internal static class ExpeditionState
{
    private static Type? flowType;
    private static PropertyInfo? ownsSession;

    internal static bool? OwnsSession()
    {
        try
        {
            flowType ??= AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("SprocketExpedition.Game.CampaignNativeFlow", false))
                .FirstOrDefault(t => t != null);
            if (flowType == null) return null;
            ownsSession ??= flowType.GetProperty("OwnsSession",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ownsSession == null || ownsSession.PropertyType != typeof(bool)) return null;
            return ownsSession.GetValue(null) is bool owned ? owned : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool ShouldHidePickerEntry() => SightMenuPolicy.ShouldHide(OwnsSession());

    internal static void Reset()
    {
        flowType = null;
        ownsSession = null;
    }
}
