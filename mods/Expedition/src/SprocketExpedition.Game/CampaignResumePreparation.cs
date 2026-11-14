using SprocketExpedition.Core.Persistence;

namespace SprocketExpedition.Game;

// Read-only gate shared by the native host and managed integration checks.
// Nothing is restored to the editor until both checkpoint and blueprint verify.
internal static class CampaignResumePreparation
{
    internal static (RunLoadResult Load, BlueprintSnapshot? Blueprint, string? Error) Read(
        RunStore store, ModAvailability mods)
    {
        var load = store.LoadActiveRun(mods);
        if (!load.CanResume || load.Snapshot == null)
            return (load, null, string.Join("; ", load.Issues.Select(i => i.Message))
                is { Length: > 0 } reason ? reason : $"This slot cannot resume ({load.Status}).");
        var design = load.Snapshot.ConfirmedDesign;
        if (design == null)
            return (load, null, "This checkpoint has no saved tank to restore.");
        var read = store.ReadBlueprint(load.Snapshot.RunId, design.Blueprint.SnapshotHash);
        if (read.Bytes is not { Length: > 0 })
            return (load, null, "The saved tank is missing or invalid: "
                + string.Join("; ", read.Issues.Select(i => i.Message)));
        return (load, new BlueprintSnapshot(design.Blueprint.SnapshotHash, read.Bytes), null);
    }

    internal static bool CanReplaceWithNewRun(RunLoadResult load, RunListResult records)
        => load.Status == RunStoreLoadStatus.NoActiveRun
            && load.Issues.Count == 0 && records.Issues.Count == 0
            && records.Runs.All(run => run.IsTerminal);
}
