namespace SprocketExpedition.Game;

// What to do with the creation date of the run-owned copy of the player's tank
// when the designer opens in a build era. Pure decision, no native types:
// indices are positions in the native era list (-1 = the date lies in no era).
internal enum RunCopyDateAction
{
    // Same era: the date stays as saved.
    Keep,
    // Earlier era: the copy moves forward to the end of the build era.
    Advance,
    // Later era (or no era) but the date is one Expedition wrote itself, the open
    // sentinel or an era end date: move the copy to the end of the build era.
    Normalise,
    // Later era with a date Expedition did not write: the tank is incompatible.
    Reject
}

internal static class CampaignEraDatePolicy
{
    internal static RunCopyDateAction Decide(int savedEraIndex, int buildEraIndex, bool writtenByExpedition)
    {
        if (savedEraIndex >= 0 && savedEraIndex == buildEraIndex) return RunCopyDateAction.Keep;
        if (savedEraIndex >= 0 && savedEraIndex < buildEraIndex) return RunCopyDateAction.Advance;
        return writtenByExpedition ? RunCopyDateAction.Normalise : RunCopyDateAction.Reject;
    }
}
