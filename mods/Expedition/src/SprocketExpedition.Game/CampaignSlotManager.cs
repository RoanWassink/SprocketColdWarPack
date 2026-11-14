using System.Text.Json;
using BepInEx;
using SprocketExpedition.Core.Persistence;

namespace SprocketExpedition.Game;

// Configurable local autosave slots. RunStore remains the authoritative checkpoint and
// blueprint store; slot-summary.json is a small UI/load summary with the fields
// the campaign menu needs (design reference, score, build credits, round, map).
internal static class CampaignSlotManager
{
    internal static int SlotCount { get; private set; } = 8;
    internal static void Configure(int count) => SlotCount = CampaignSlotPolicy.NormalizeCount(count);

    internal static bool IsEmpty(int slot)
    {
        var root = Root(slot);
        if (File.Exists(root)) return false;
        var candidate = new RunStore(root);
        var load = candidate.LoadActiveRun(ModAvailability.None);
        var records = candidate.ListRuns();
        return load.Status == RunStoreLoadStatus.NoActiveRun && load.Issues.Count == 0
            && records.Issues.Count == 0 && records.Runs.Count == 0;
    }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static string Root(int slot)
    {
        if (slot < 1 || slot > SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(
            Paths.ConfigPath, "SprocketExpedition", "slots", "slot-" + slot);
    }

    internal static RunStore Store(int slot)
    {
        var root = Root(slot);
        Directory.CreateDirectory(root);
        return new RunStore(root);
    }

    internal static bool HasPreEncounter(int slot)
        => Directory.Exists(PreEncounterRoot(slot));

    internal static void SavePreEncounter(int slot)
    {
        try
        {
            var source = Root(slot);
            if (!Directory.Exists(source)) return;
            var destination = PreEncounterRoot(slot);
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            CopyDirectory(source, destination);
            Plugin.Logger.LogInfo(
                $"Expedition slot {slot}: pre-encounter checkpoint copied for interrupted-run recovery.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(
                $"Expedition slot {slot}: pre-encounter copy failed. " + ex.Message);
        }
    }

    internal static bool RestorePreEncounter(int slot)
    {
        try
        {
            var pre = PreEncounterRoot(slot);
            if (!Directory.Exists(pre))
            {
                Plugin.Logger.LogError(
                    $"Expedition slot {slot}: no pre-encounter checkpoint is available.");
                return false;
            }

            var root = Root(slot);
            if (Directory.Exists(root))
            {
                var archiveRoot = Path.Combine(
                    Paths.ConfigPath, "SprocketExpedition", "slots", "archive");
                Directory.CreateDirectory(archiveRoot);
                var interrupted = Path.Combine(
                    archiveRoot,
                    $"slot-{slot}-interrupted-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
                Directory.Move(root, interrupted);
            }

            CopyDirectory(pre, root);
            Plugin.Logger.LogInfo(
                $"Expedition slot {slot}: restored the pre-encounter checkpoint; the interrupted attempt was archived.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(
                $"Expedition slot {slot}: pre-encounter restore failed. " + ex.Message);
            return false;
        }
    }

    private static string PreEncounterRoot(int slot)
        => Root(slot) + ".pre-encounter";

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    internal static bool ArchiveSlot(int slot)
    {
        try
        {
            var root = Root(slot);
            if (!Directory.Exists(root)) return true;
            var archiveRoot = Path.Combine(
                Paths.ConfigPath, "SprocketExpedition", "slots", "archive");
            Directory.CreateDirectory(archiveRoot);
            var destination = Path.Combine(
                archiveRoot,
                $"slot-{slot}-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            Directory.Move(root, destination);
            Plugin.Logger.LogInfo(
                $"Expedition slot {slot}: archived terminal run to {destination}.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(
                $"Expedition slot {slot}: archive failed; a new run was not started. " + ex.Message);
            return false;
        }
    }

    internal static SlotSummary? Read(int slot)
    {
        try
        {
            var path = Path.Combine(Root(slot), "slot-summary.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<SlotSummary>(
                File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    internal static void Write(int slot, RunStore store)
    {
        try
        {
            var load = store.LoadActiveRun(ModAvailability.None);
            var snapshot = load.Snapshot;
            if (snapshot == null && load.Status == RunStoreLoadStatus.NoActiveRun)
            {
                var terminal = store.ListRuns().Runs.LastOrDefault(run => run.IsTerminal);
                if (terminal != null) snapshot = store.LoadRun(terminal.RunId, ModAvailability.None).Snapshot;
            }
            SlotSummary summary;
            if (snapshot is null)
            {
                summary = new SlotSummary(
                    slot, null, "Empty", 0, 0, 0, 0, 0m, string.Empty,
                    null, null, null, DateTime.UtcNow);
            }
            else
            {
                var mapId = snapshot.LockedPlan?.MapId
                    ?? snapshot.Offers.FirstOrDefault()?.MapId;
                var design = snapshot.ConfirmedDesign?.Blueprint;
                summary = new SlotSummary(
                    slot,
                    snapshot.RunId,
                    snapshot.Phase.ToString(),
                    snapshot.Round,
                    snapshot.Victories,
                    snapshot.Defeats,
                    snapshot.Score,
                    snapshot.AvailableBudget.Value,
                    snapshot.AvailableBudget.UnitId,
                    mapId,
                    design?.LocalId,
                    design?.SnapshotHash,
                    DateTime.UtcNow)
                    { Ironman = snapshot.Ironman, Era = snapshot.EraSchedule[snapshot.EraIndex].NativeId };
            }

            var root = Root(slot);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "slot-summary.json");
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(summary, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError(
                $"Expedition slot {slot}: summary write failed. " + ex.Message);
        }
    }
}

internal sealed record SlotSummary(
    int Slot,
    string? RunId,
    string Phase,
    int Round,
    int Victories,
    int Defeats,
    decimal Score,
    decimal BuildCredits,
    string UnitId,
    string? MapId,
    string? DesignLocalId,
    string? DesignHash,
    DateTime UpdatedUtc)
{
    public bool Ironman { get; init; }
    public string? Era { get; init; }
    internal string Describe()
    {
        if (Phase == "Empty" || RunId is null)
            return $"Slot {Slot} — empty";
        return $"Slot {Slot} — round {Round} · {Victories} wins / {Defeats} losses\n"
            + $"{Phase} · score {Score:N0} · {(Ironman ? "Ironman" : "Normal")} · {Era} | {UpdatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n"
            + $"fleet budget {BuildCredits:N0} {UnitId}";
    }
}
