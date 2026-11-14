#if CAMPAIGN
using BepInEx;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Persistence;

namespace SprocketExpedition.Game;

internal static class CampaignVeteranUnlocks
{
    internal static string DirectoryPath => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "veterans");
    private static AttemptIdentity? lastAttempt;
    private static string? lastNotice;
    internal static string? NoticeFor(EncounterResult result) => result.Outcome == EncounterOutcome.Victory
        && result.Attempt == lastAttempt ? lastNotice : null;

    // Called only after reading an authoritative checkpoint, both after commands
    // and on resume so an interrupted promotion can be recovered at the boundary.
    internal static void CaptureBoundary(RunStore store, RunLoadResult load)
    {
        if (load.Status != RunStoreLoadStatus.Loaded || load.Issues.Count != 0
            || load.Snapshot is not { } snapshot || !snapshot.CatalogueVersion.StartsWith(CampaignNativeCatalogue.VersionPrefix, StringComparison.Ordinal)
            || !CampaignVeteranStore.Eligible(snapshot)) return;
        try
        {
            var design = snapshot.ConfirmedDesign!;
            var read = store.ReadBlueprint(snapshot.RunId, design.Blueprint.SnapshotHash);
            if (read.Bytes is not { Length: > 0 }) throw new InvalidDataException("The winning tank snapshot is missing or invalid.");
            var unit = CustomBattleBlueprintResolver.CreateLockedUnit(read.Bytes, out var error)
                ?? throw new InvalidDataException("The winning native tank cannot load: " + error);
            var era = CampaignNativeCatalogue.EraOf(unit.CreationDate)
                ?? throw new InvalidDataException("The winning tank has no native era.");
            // The run copy may carry the open-ended date written while its era was
            // the last one: it still belongs to the era the run recorded.
            if (era != design.Blueprint.Era.NativeId
                && CampaignNativeCatalogue.DateBelongsToEra(unit.CreationDate, design.Blueprint.Era.NativeId))
                era = design.Blueprint.Era.NativeId;
            var entry = CampaignVeteranStore.Create(snapshot, read.Bytes, unit.Name, era);
            var created = CampaignVeteranStore.Write(DirectoryPath, entry);
            if (created || lastAttempt != entry.Attempt)
            {
                lastAttempt = entry.Attempt;
                lastNotice = created
                    ? $"Era complete: {entry.Name} added to the {entry.Era} enemy pool for future runs."
                    : $"Era complete: this version of {entry.Name} is already in the {entry.Era} enemy pool.";
            }
            if (!created) return;
            CampaignNativeCatalogue.Reset();
            Plugin.Logger.LogInfo($"Expedition veteran unlocked: {entry.Name}; era={entry.Era}; saved native quote={entry.Price}; completed round={entry.CompletedRound}; hash={entry.Hash}. Available to future run catalogues.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Expedition winning-tank promotion could not finish; campaign checkpoint preserved: " + ex.Message);
        }
    }
}
#endif
