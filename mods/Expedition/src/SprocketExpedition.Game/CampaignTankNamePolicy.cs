using System.Text.RegularExpressions;

namespace SprocketExpedition.Game;

// Conservative, explainable name filter for new catalogues only. Never deletes
// designs or rewrites a pinned save, and a 'gun' substring alone is insufficient.
internal static class CampaignTankNamePolicy
{
    private static readonly Regex Mobile = new(@"\b(self[ -]?propelled|spg|gun[ -]carrier|gun[ -]carriage|selbstfahrlafette|auf|haubitzen\s+au\d*|lefh\s*18\s*b2|rz\s*25)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Stationary = new(@"\b(stationary|stationair|towed|field[ -]gun|anti[ -]?tank[ -]gun|aa[ -]gun|at[ -]gun|pak|flak|feldhaubitze)\b|\b(lefh|sfh)\s*\d+|^\s*\d+(?:[.,]\d+)?\s*(?:mm|cm)\s+(?:(?:aa|at|field|mountain|infantry)\s+)?(?:gun|pack\s+howitzer|howitzer|haubica)\b|\b(?:canon|canone|cannone)\s+(?:de|d['’]|da)\b|\bbofors\s+\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static bool IsStationaryGun(string? name) => !string.IsNullOrWhiteSpace(name) && !Mobile.IsMatch(name) && Stationary.IsMatch(name);
}
