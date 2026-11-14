namespace SprocketExpedition.Game;

// Managed decisions shared by the host, configuration and regression tests.
internal static class CampaignSlotPolicy
{
    internal static int NormalizeCount(int value) => Math.Clamp(value, 4, 12);

    internal static bool UseFixture(string? savedVersion, bool newGame, bool allowFixture)
        => !newGame && (savedVersion == "fixture.firstplay.1"
            || (savedVersion == null && allowFixture));

    internal static bool ArchiveAndStart(int slot, Func<int, bool> archive, Action<int> start)
    {
        if (!archive(slot)) return false;
        start(slot);
        return true;
    }
}
