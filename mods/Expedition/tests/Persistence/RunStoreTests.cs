using System.Text;
using System.Text.Json;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence.Tests;

internal static class RunStoreTests
{
    public static void Register()
    {
        TestRunner.Add(
            "roundtrip preserves every frozen snapshot field",
            RoundtripPreservesEveryFrozenField);
        TestRunner.Add(
            "checkpoint, blueprint and index use fixed UTF-8 JSON names without BOM",
            FixedUtf8JsonFileNames);
        TestRunner.Add("explicit absolute root is required", ExplicitAbsoluteRoot);
        TestRunner.Add("missing design blueprint blob is rejected", MissingDesignBlueprint);
        TestRunner.Add("supplied blueprint hash mismatch is rejected", BlueprintHashMismatchOnWrite);
        TestRunner.Add("invalid run id cannot become a path", InvalidRunIdRejected);
        TestRunner.Add("only schema 1 is stored and Resolving is not persisted", SchemaAndPhaseGuards);
        TestRunner.Add("duplicate checkpoint write is inert", DuplicateWriteIsInert);
        TestRunner.Add(
            "older generation, removed processed attempt and removed consumed offer are inert",
            OutOfOrderWritesAreInert);
        TestRunner.Add("run id, seed, budget, unit and era schedule are immutable", RunIdentityIsImmutable);
        TestRunner.Add("empty root reports no active run", EmptyRootHasNoActiveRun);
        TestRunner.Add("non-terminal run has no terminal record", NonTerminalHasNoRecord);
        TestRunner.Add(
            "guid run id forms map to one safe canonical folder",
            GuidRunIdMapsToCanonicalFolder);
        TestRunner.Add(
            "suspended ChoosingEncounter resumes exactly",
            SuspendedChoosingEncounterResumes);
        TestRunner.Add(
            "awaiting endless choice round 19 resumes exactly",
            AwaitingEndlessChoiceResumes);
        TestRunner.Add("schema 4 themed group persists exact roles",
            V4ThemedSnapshotRoundtrip);
        TestRunner.Add("legacy schema 3 themed snapshot still restores",
            V3ThemedSnapshotStillLoads);
        TestRunner.Add("schema 5 pinned difficulty survives disk save, discard and reload",
            V5PinnedDifficultyRoundtrip);
        TestRunner.Add("existing v4 run opts into policy 5 for future offers without rewriting old identity",
            V4PolicyOptInRoundtrip);
        TestRunner.Add("five-era pinned schedule survives pre-final and endless disk reload",
            FiveEraPinnedScheduleRoundtrip);
        TestRunner.Add("schema 6 run totals are optional in checkpoint and record",
            Schema6TotalsRoundtrip);
    }

    private static void Schema6TotalsRoundtrip()
    {
        using (var temp = new TempDir())
        {
            var store = new RunStore(temp.StoreRoot);
            var zero = Fixtures.BaseSnapshot() with
            {
                SchemaVersion = 6,
                Rewards = new RewardState { Version = 6 }
            };
            AssertEx.True(store.SaveCheckpoint(zero).IsCommitted, "zero-total v6 checkpoint");
            var json = Fixtures.ReadJson(Fixtures.CheckpointPath(temp.StoreRoot));
            var snapshot = json.RootElement.GetProperty("snapshot");
            AssertEx.False(snapshot.TryGetProperty("attempts", out _), "zero attempts omit the key");
            AssertEx.False(snapshot.TryGetProperty("enemiesDestroyed", out _), "zero kills omit the key");
            AssertEx.False(snapshot.TryGetProperty("friendliesLost", out _), "zero losses omit the key");
            var loaded = store.LoadActiveRun(ModAvailability.None);
            AssertEx.True(loaded.CanResume, "zero-total v6 checkpoint loads");
            AssertEx.Equal(0, loaded.Snapshot!.Attempts, "old/short checkpoints default attempts to 0");
            AssertEx.Equal(0, loaded.Snapshot.EnemiesDestroyed, "old/short checkpoints default kills to 0");
            AssertEx.Equal(0, loaded.Snapshot.FriendliesLost, "old/short checkpoints default losses to 0");
        }

        using (var temp = new TempDir())
        {
            var store = new RunStore(temp.StoreRoot);
            var terminal = Fixtures.BaseSnapshot() with
            {
                SchemaVersion = 6,
                Rewards = new RewardState { Version = 6 },
                Phase = RunPhase.Lost,
                Defeats = 1,
                Attempts = 7,
                EnemiesDestroyed = 23,
                FriendliesLost = 4
            };
            var saved = store.SaveCheckpoint(terminal);
            AssertEx.True(saved.IsCommitted, "terminal v6 totals save: "
                + string.Join(";", saved.Issues.Select(i => i.Code + ":" + i.Message)));
            var loaded = store.LoadRun(Fixtures.RunId, ModAvailability.None);
            AssertEx.Equal(7, loaded.Snapshot!.Attempts, "attempts survive disk");
            AssertEx.Equal(23, loaded.Snapshot.EnemiesDestroyed, "kills survive disk");
            AssertEx.Equal(4, loaded.Snapshot.FriendliesLost, "losses survive disk");
            var record = store.ReadRecord(Fixtures.RunId);
            AssertEx.NotNull(record.Record, "terminal record exists");
            AssertEx.Equal(7, record.Record!.Attempts, "record attempts");
            AssertEx.Equal(23, record.Record.EnemiesDestroyed, "record kills");
            AssertEx.Equal(4, record.Record.FriendliesLost, "record losses");
        }
    }

    private static void RoundtripPreservesEveryFrozenField()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var mods = new ModAvailability(new[] { Fixtures.ModA, Fixtures.ModB });

        var blob = Fixtures.Blob("roundtrip-design-bytes");
        var design = Fixtures.Design(blob, Fixtures.ModA);
        var offerA = Fixtures.Plan(
            "offer-a",
            round: 2,
            risk: EncounterRisk.Cautious,
            mods: new[] { Fixtures.ModA, Fixtures.ModB });
        var offerB = Fixtures.Plan(
            "offer-b",
            round: 2,
            risk: EncounterRisk.Dangerous,
            mods: new[] { Fixtures.ModB });
        var attempt = Fixtures.Attempt("offer-a", 3);

        var snapshot = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.Fighting,
            Round = 2,
            BlockIndex = 1,
            EraIndex = 1,
            WinsInBlock = 2,
            Victories = 3,
            Defeats = 1,
            BlockBaseBudget = new ScenarioValue(160.500000m, Fixtures.CostUnit),
            SupplyBonus = 3.250000m,
            AvailableBudget = new ScenarioValue(163.750000m, Fixtures.CostUnit),
            PendingSupplyPoints = 2,
            ConfirmedDesign = design,
            DesignConfirmedForBlock = true,
            Offers = new[] { offerA, offerB },
            SelectedOfferId = "offer-a",
            LockedPlan = offerA,
            CurrentAttempt = attempt,
            GenerationCounter = 3,
            ProcessedAttempts = new[]
            {
                new AttemptIdentity(Fixtures.RunId, "offer-0", "plan-offer-0", 2)
            },
            ConsumedOfferIds = new[] { "offer-0" },
            LastOutcome = EncounterOutcome.PrecombatAborted,
            LastOutcomeDetail = "fixture technical abort"
        };

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");
        AssertEx.True(write.CheckpointCommitted, "checkpoint committed");
        AssertEx.Equal(1L, write.Revision, "revision");

        var load = store.LoadActiveRun(mods);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "load status");
        AssertEx.True(load.CanResume, "can resume");
        AssertEx.True(load.IsActiveRun, "active run");
        AssertEx.NotNull(load.Snapshot, "snapshot");
        SnapshotAssert.Equal(snapshot, load.Snapshot!);

        AssertEx.Equal(
            "100.000000",
            AssertEx.DecimalText(load.Snapshot!.InitialBaseBudget.Value),
            "initial budget decimal scale");
        AssertEx.Equal(
            "3.250000",
            AssertEx.DecimalText(load.Snapshot.SupplyBonus),
            "supply bonus decimal scale");

        var blueprint = store.ReadBlueprint(Fixtures.RunId, blob.SnapshotHash);
        AssertEx.NotNull(blueprint.Bytes, "blueprint bytes");
        AssertEx.SequenceEqual(blob.Bytes, blueprint.Bytes!, "blueprint byte roundtrip");
    }

    private static void FixedUtf8JsonFileNames()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("file-layout-design");
        var snapshot = Fixtures.BaseSnapshot() with
        {
            ConfirmedDesign = Fixtures.Design(blob),
            DesignConfirmedForBlock = true
        };

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");

        var runDirectory = Fixtures.RunDirectory(temp.StoreRoot);
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var index = Fixtures.IndexPath(temp.StoreRoot);
        var blueprintPath = Fixtures.BlueprintPath(temp.StoreRoot, blob.SnapshotHash);

        AssertEx.True(File.Exists(checkpointPath), "checkpoint path exists");
        AssertEx.True(File.Exists(index), "active index exists");
        AssertEx.True(File.Exists(blueprintPath), "content-addressed blueprint exists");
        AssertEx.Equal(
            Path.GetFullPath(temp.StoreRoot),
            store.RootDirectory,
            "explicit root is preserved");

        var checkpointBytes = File.ReadAllBytes(checkpointPath);
        AssertEx.False(
            checkpointBytes.Length >= 3
                && checkpointBytes[0] == 0xEF
                && checkpointBytes[1] == 0xBB
                && checkpointBytes[2] == 0xBF,
            "checkpoint must be UTF-8 without BOM");
        AssertEx.Equal(
            Encoding.UTF8.GetString(checkpointBytes),
            new UTF8Encoding(false).GetString(checkpointBytes),
            "checkpoint decodes as UTF-8");

        using (var document = Fixtures.ReadJson(checkpointPath))
        {
            var root = document.RootElement;
            AssertEx.Equal(JsonValueKind.Object, root.ValueKind, "checkpoint JSON object");
            AssertEx.Equal<string?>(
                "runCheckpoint",
                root.GetProperty("kind").GetString(),
                "checkpoint kind");
            AssertEx.Equal(
                1,
                root.GetProperty("schemaVersion").GetInt32(),
                "checkpoint schema");
            AssertEx.Equal<string?>(
                Fixtures.RunId,
                root.GetProperty("runId").GetString(),
                "checkpoint run id");
        }

        using (var document = Fixtures.ReadJson(index))
        {
            AssertEx.Equal<string?>(
                "activeRunIndex",
                document.RootElement.GetProperty("kind").GetString(),
                "index kind");
            AssertEx.Equal<string?>(
                Fixtures.RunId,
                document.RootElement.GetProperty("activeRunId").GetString(),
                "index active run");
        }

        var leftoverTempFiles = Directory
            .GetFiles(runDirectory, "*", SearchOption.AllDirectories)
            .Where(
                path => (Path.GetFileName(path) ?? string.Empty).StartsWith(
                    ".tmp-",
                    StringComparison.Ordinal))
            .ToArray();
        AssertEx.Equal(0, leftoverTempFiles.Length, "no staging temp files remain");
    }

    private static void ExplicitAbsoluteRoot()
    {
        AssertEx.Throws<ArgumentException>(
            () => new RunStore("relative/store"),
            "relative root must be rejected");
        AssertEx.Throws<ArgumentException>(
            () => new RunStore("   "),
            "blank root must be rejected");
        AssertEx.Throws<ArgumentException>(
            () => new RunStore(null!),
            "null root must be rejected");
    }

    private static void MissingDesignBlueprint()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("required-design-bytes");
        var snapshot = Fixtures.BaseSnapshot() with
        {
            ConfirmedDesign = Fixtures.Design(blob),
            DesignConfirmedForBlock = true
        };

        var write = store.SaveCheckpoint(snapshot);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, write.Status, "write status");
        AssertEx.ContainsIssue(write, RunStoreIssueCodes.BlueprintMissing, "missing blob");
        AssertEx.False(write.CheckpointCommitted, "not committed");
        AssertEx.False(
            File.Exists(Fixtures.CheckpointPath(temp.StoreRoot)),
            "no checkpoint was written");
        AssertEx.False(
            File.Exists(Fixtures.IndexPath(temp.StoreRoot)),
            "no active index was written");
    }

    private static void BlueprintHashMismatchOnWrite()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var wrongHash = new string('a', 64);
        var blob = new BlueprintSnapshot(wrongHash, Encoding.UTF8.GetBytes("payload"));

        var write = store.SaveCheckpoint(Fixtures.BaseSnapshot(), new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, write.Status, "write status");
        AssertEx.ContainsIssue(
            write,
            RunStoreIssueCodes.BlueprintHashMismatch,
            "hash mismatch");
        AssertEx.False(
            File.Exists(Fixtures.CheckpointPath(temp.StoreRoot)),
            "no checkpoint was written");
    }

    private static void InvalidRunIdRejected()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var snapshot = Fixtures.BaseSnapshot("../../escape");

        var write = store.SaveCheckpoint(snapshot);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, write.Status, "write status");
        AssertEx.ContainsIssue(write, RunStoreIssueCodes.InvalidRunId, "invalid run id");
        AssertEx.False(
            Directory.Exists(temp.StoreRoot),
            "no storage root content was created for an invalid id");
        AssertEx.False(
            Directory.Exists(Path.Combine(temp.Root, "escape")),
            "no path escaped the store root");
    }

    private static void SchemaAndPhaseGuards()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);

        var future = Fixtures.BaseSnapshot() with { SchemaVersion = 99 };
        var futureWrite = store.SaveCheckpoint(future);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, futureWrite.Status, "future schema");
        AssertEx.ContainsIssue(
            futureWrite,
            RunStoreIssueCodes.SchemaUnsupported,
            "future schema code");

        var legacy = Fixtures.BaseSnapshot() with { SchemaVersion = 0 };
        var legacyWrite = store.SaveCheckpoint(legacy);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, legacyWrite.Status, "legacy schema");
        AssertEx.ContainsIssue(
            legacyWrite,
            RunStoreIssueCodes.SchemaUnsupported,
            "legacy schema code");

        var resolving = Fixtures.BaseSnapshot() with { Phase = RunPhase.Resolving };
        var resolvingWrite = store.SaveCheckpoint(resolving);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, resolvingWrite.Status, "resolving phase");
        AssertEx.ContainsIssue(
            resolvingWrite,
            RunStoreIssueCodes.InvalidSnapshot,
            "resolving phase code");
        var legacyPolicy = Fixtures.BaseSnapshot() with { GenerationPolicy = 5 };
        var legacyPolicyWrite = store.SaveCheckpoint(legacyPolicy);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, legacyPolicyWrite.Status,
            "legacy policy overlay");
        AssertEx.ContainsIssue(legacyPolicyWrite, RunStoreIssueCodes.InvalidSnapshot,
            "legacy policy overlay requires v4");
        AssertEx.False(
            File.Exists(Fixtures.CheckpointPath(temp.StoreRoot)),
            "guarded writes created no checkpoint");
    }

    private static void DuplicateWriteIsInert()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var snapshot = Fixtures.BaseSnapshot();

        var first = store.SaveCheckpoint(snapshot);
        AssertEx.Equal(RunStoreWriteStatus.Committed, first.Status, "first write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var checkpointBefore = File.ReadAllBytes(checkpointPath);
        var indexBefore = File.ReadAllBytes(indexPath);

        var second = store.SaveCheckpoint(snapshot);
        AssertEx.Equal(RunStoreWriteStatus.Unchanged, second.Status, "duplicate write");
        AssertEx.True(second.CheckpointCommitted, "duplicate checkpoint is durable");
        AssertEx.Equal(first.Revision, second.Revision, "revision unchanged");
        AssertEx.Equal(first.CheckpointHash, second.CheckpointHash, "hash unchanged");
        AssertEx.SequenceEqual(
            checkpointBefore,
            File.ReadAllBytes(checkpointPath),
            "checkpoint bytes unchanged");
        AssertEx.SequenceEqual(
            indexBefore,
            File.ReadAllBytes(indexPath),
            "index bytes unchanged");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "still loadable");
    }

    private static void OutOfOrderWritesAreInert()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var attempt = Fixtures.Attempt("offer-1", 5);
        var current = Fixtures.BaseSnapshot() with
        {
            GenerationCounter = 5,
            ProcessedAttempts = new[] { attempt },
            ConsumedOfferIds = new[] { "offer-1" }
        };

        var write = store.SaveCheckpoint(current);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "baseline write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var bytesBefore = File.ReadAllBytes(checkpointPath);

        var older = current with { GenerationCounter = 4 };
        var olderWrite = store.SaveCheckpoint(older);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, olderWrite.Status, "older generation");
        AssertEx.ContainsIssue(
            olderWrite,
            RunStoreIssueCodes.OlderGeneration,
            "older generation code");

        var withoutProcessed = current with
        {
            GenerationCounter = 6,
            ProcessedAttempts = Array.Empty<AttemptIdentity>()
        };
        var processedWrite = store.SaveCheckpoint(withoutProcessed);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, processedWrite.Status, "removed attempt");
        AssertEx.ContainsIssue(
            processedWrite,
            RunStoreIssueCodes.RemovesProcessedAttempt,
            "removed attempt code");

        var withoutConsumed = current with
        {
            GenerationCounter = 6,
            ConsumedOfferIds = Array.Empty<string>()
        };
        var consumedWrite = store.SaveCheckpoint(withoutConsumed);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, consumedWrite.Status, "removed consumed id");
        AssertEx.ContainsIssue(
            consumedWrite,
            RunStoreIssueCodes.RemovesConsumedOffer,
            "removed consumed id code");

        AssertEx.SequenceEqual(
            bytesBefore,
            File.ReadAllBytes(checkpointPath),
            "rejected writes left checkpoint bytes unchanged");

        var duplicate = store.SaveCheckpoint(current);
        AssertEx.Equal(
            RunStoreWriteStatus.Unchanged,
            duplicate.Status,
            "duplicate result checkpoint is inert");
    }

    private static void RunIdentityIsImmutable()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var baseline = Fixtures.BaseSnapshot();
        var write = store.SaveCheckpoint(baseline);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "baseline write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var bytesBefore = File.ReadAllBytes(checkpointPath);

        var changedSeed = baseline with { Seed = baseline.Seed + 1 };
        var seedWrite = store.SaveCheckpoint(changedSeed);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, seedWrite.Status, "changed seed");
        AssertEx.ContainsIssue(
            seedWrite,
            RunStoreIssueCodes.RunIdentityChanged,
            "changed seed code");

        var changedBudget = baseline with
        {
            InitialBaseBudget = new ScenarioValue(101m, Fixtures.CostUnit)
        };
        var budgetWrite = store.SaveCheckpoint(changedBudget);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, budgetWrite.Status, "changed budget");
        AssertEx.ContainsIssue(
            budgetWrite,
            RunStoreIssueCodes.RunIdentityChanged,
            "changed budget code");

        var changedUnit = baseline with { CostUnitId = "other-unit" };
        var unitWrite = store.SaveCheckpoint(changedUnit);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, unitWrite.Status, "changed unit");
        AssertEx.ContainsIssue(
            unitWrite,
            RunStoreIssueCodes.RunIdentityChanged,
            "changed unit code");

        var changedEras = baseline with { EraSchedule = Fixtures.Eras.Reverse().ToArray() };
        var eraWrite = store.SaveCheckpoint(changedEras);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, eraWrite.Status, "changed eras");
        AssertEx.ContainsIssue(
            eraWrite,
            RunStoreIssueCodes.RunIdentityChanged,
            "changed eras code");

        AssertEx.SequenceEqual(
            bytesBefore,
            File.ReadAllBytes(checkpointPath),
            "identity changes never rewrote the checkpoint");
    }

    private static void EmptyRootHasNoActiveRun()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.NoActiveRun, load.Status, "load status");
        AssertEx.False(load.CanResume, "cannot resume");
        AssertEx.Equal(0, load.Issues.Count, "no issues for an empty root");
        AssertEx.False(Directory.Exists(temp.StoreRoot), "load did not create the root");

        var list = store.ListRuns();
        AssertEx.Equal(0, list.Runs.Count, "no runs listed");
        AssertEx.Equal(0, list.Issues.Count, "no list issues");
    }

    private static void NonTerminalHasNoRecord()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var write = store.SaveCheckpoint(Fixtures.BaseSnapshot());
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");

        var record = store.ReadRecord(Fixtures.RunId);
        AssertEx.True(record.Record is null, "non-terminal run has no record");
        AssertEx.HasIssue(record.Issues, RunStoreIssueCodes.RecordMissing, "record missing");
        AssertEx.False(
            File.Exists(Fixtures.RecordPath(temp.StoreRoot)),
            "no record file was written");
    }

    private static void GuidRunIdMapsToCanonicalFolder()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var upperRunId = Fixtures.RunId.ToUpperInvariant();
        var snapshot = Fixtures.BaseSnapshot(upperRunId) with { Round = 3 };

        var write = store.SaveCheckpoint(snapshot);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "uppercase run id write");
        AssertEx.Equal<string?>(
            Fixtures.RunId,
            write.RunId,
            "write reports the canonical run id");

        var canonicalDirectory = Path.Combine(
            temp.StoreRoot,
            "runs",
            Fixtures.RunId);
        AssertEx.True(
            Directory.Exists(canonicalDirectory),
            "the canonical GUID names the run folder");

        var runDirectoryNames = Directory
            .GetDirectories(Path.Combine(temp.StoreRoot, "runs"))
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .ToArray();
        AssertEx.True(
            runDirectoryNames.Contains(Fixtures.RunId, StringComparer.Ordinal),
            "the stored folder name is the canonical GUID");
        AssertEx.False(
            runDirectoryNames.Contains(upperRunId, StringComparer.Ordinal),
            "the raw run id string is never used as a path");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "uppercase run id load");
        AssertEx.Equal<string?>(upperRunId, load.Snapshot!.RunId, "snapshot run id preserved");
        AssertEx.Equal(3, load.Snapshot.Round, "round preserved");
    }

    private static void SuspendedChoosingEncounterResumes()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var offer = Fixtures.Plan("offer-s", round: 4);
        var suspended = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.Suspended,
            SuspendedFrom = RunPhase.ChoosingEncounter,
            Round = 4,
            BlockIndex = 1,
            EraIndex = 1,
            WinsInBlock = 1,
            Victories = 3,
            Offers = new[] { offer },
            SelectedOfferId = "offer-s",
            GenerationCounter = 2,
            ProcessedAttempts = new[] { Fixtures.Attempt("offer-prev", 1) },
            ConsumedOfferIds = new[] { "offer-prev" }
        };

        var write = store.SaveCheckpoint(suspended);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "suspended write");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "suspended load");
        AssertEx.True(load.CanResume, "suspended run resumes");
        SnapshotAssert.Equal(suspended, load.Snapshot!);
        AssertEx.Equal(RunPhase.Suspended, load.Snapshot!.Phase, "suspended phase");
        AssertEx.Equal<RunPhase?>(
            RunPhase.ChoosingEncounter,
            load.Snapshot.SuspendedFrom,
            "suspended from");
        AssertEx.Equal<string?>("offer-s", load.Snapshot.SelectedOfferId, "selected offer");
    }

    private static void AwaitingEndlessChoiceResumes()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var awaiting = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.AwaitingEndlessChoice,
            Round = 19,
            BlockIndex = 5,
            EraIndex = 5,
            Victories = 18,
            Defeats = 2,
            DesignConfirmedForBlock = true,
            GenerationCounter = 30,
            ConsumedOfferIds = new[] { "offer-18" }
        };

        var write = store.SaveCheckpoint(awaiting);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "endless write");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "endless load");
        AssertEx.True(load.CanResume, "endless choice resumes");
        SnapshotAssert.Equal(awaiting, load.Snapshot!);
        AssertEx.Equal(RunPhase.AwaitingEndlessChoice, load.Snapshot!.Phase, "endless phase");
        AssertEx.Equal(19, load.Snapshot.Round, "round 19 with phase-keyed UI");
    }

    private static void V4ThemedSnapshotRoundtrip()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("schema-4-themed-design");
        var fixture = ThemedFixtures.Create(4);
        var snapshot = AdventureSnapshot(fixture, blob);

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "v4 write status");
        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "v4 load status");
        AssertEx.True(load.CanResume, "v4 run resumes");
        var loadedSnapshot = load.Snapshot!;
        AssertEx.Equal(4, loadedSnapshot.SchemaVersion, "v4 snapshot schema");
        AssertEx.Equal(4, loadedSnapshot.Rewards!.Version, "v4 reward rules");
        var expectedPlan = fixture.Offers.Single(offer => offer.Risk == EncounterRisk.Standard);
        AssertEx.True(expectedPlan.Extras?.Special is { Preset: SpecialPreset.TacticalGroup },
            "v4 fixture must contain a production TacticalGroup offer");
        AssertEx.True(expectedPlan.Extras!.Special!.Enemies.All(unit =>
                !string.IsNullOrWhiteSpace(unit.DeploymentRole)),
            "v4 production offer must carry explicit deployment roles");
        var loadedPlan = loadedSnapshot.LockedPlan!;
        var special = loadedPlan.Extras!.Special!;
        AssertEx.Equal(expectedPlan.PlanHash, loadedPlan.PlanHash, "v4 plan hash roundtrip");
        AssertEx.Equal(expectedPlan.Extras!.Special!.Enemies.Count, special.Enemies.Count,
            "special roster count");
        for (var index = 0; index < special.Enemies.Count; index++)
        {
            AssertEx.Equal(expectedPlan.Extras!.Special!.Enemies[index].BlueprintId,
                special.Enemies[index].BlueprintId, "special unit identity roundtrip");
            AssertEx.Equal(expectedPlan.Extras!.Special!.Enemies[index].DeploymentRole,
                special.Enemies[index].DeploymentRole, "deployment role roundtrip");
        }
        var restored = RunDirector.Restore(loadedSnapshot);
        AssertEx.NotNull(restored.Snapshot?.LockedPlan, "restored v4 locked plan");
        AssertEx.Equal(expectedPlan.PlanHash, restored.Snapshot!.LockedPlan!.PlanHash,
            "restored v4 plan hash");

        var restoreOptions = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var exactRestore = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(fixture.Request, loadedSnapshot.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.Success, exactRestore.Status,
            "exact stored v4 offers must restore under their generation options");
        var laterGlobalSetting = fixture.Request with
        {
            EnemyBudgetMultiplier = fixture.Request.EnemyBudgetMultiplier,
            EraProgressionEnabled = true
        };
        var legacyRestore = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(laterGlobalSetting, loadedSnapshot.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.Success, legacyRestore.Status,
            "a policy-5 setting change must not invalidate an existing v4 offer set");

        var tampered = loadedSnapshot.Offers.Select(plan =>
        {
            if (plan.Risk != EncounterRisk.Standard) return plan;
            var changed = plan.Extras!.Special! with
            {
                Enemies = plan.Extras.Special.Enemies.Select(unit => unit with
                {
                    DeploymentRole = unit.DeploymentRole == "Center" ? "Flank" : "Center"
                }).ToArray()
            };
            var draft = plan with
            {
                Extras = plan.Extras! with { Special = changed },
                OfferId = string.Empty,
                PlanHash = string.Empty
            };
            var contentHash = CatalogueHasher.Compute(fixture.Definition);
            var offerId = PlanHasher.CreateOfferId(
                fixture.Request.RunId, contentHash, draft);
            var identified = draft with { OfferId = offerId };
            return identified with
            {
                PlanHash = PlanHasher.ComputePlanHash(contentHash, identified)
            };
        }).ToArray();
        var rejected = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(fixture.Request, tampered);
        AssertEx.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, rejected.Status,
            "tampered deployment roles must not restore from the pinned catalogue");
        AssertEx.True(rejected.Diagnostics.Any(error =>
                error.Contains("saved reward, boss, formation or offer identity differs",
                    StringComparison.Ordinal)),
            "role-tamper rejection must come from the pinned-plan identity check");

        var duplicate = store.SaveCheckpoint(loadedSnapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Unchanged, duplicate.Status, "v4 exact duplicate is inert");

        var badPattern = loadedSnapshot.LockedPlan! with
        {
            Extras = loadedSnapshot.LockedPlan.Extras! with
            {
                Special = loadedSnapshot.LockedPlan.Extras!.Special! with
                {
                    Pattern = SpecialSpawnPattern.Line
                }
            }
        };
        var structural = store.SaveCheckpoint(
            loadedSnapshot with { LockedPlan = badPattern }, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, structural.Status,
            "v4 storage must reject a non-CombinedArms TacticalGroup");
        AssertEx.True(structural.Issues.Any(issue =>
                issue.Code == RunStoreIssueCodes.InvalidSnapshot
                && issue.Message.Contains("CombinedArms", StringComparison.Ordinal)),
            "v4 storage rejection must name the required spawn pattern");

        var badVersion = loadedSnapshot.LockedPlan! with
        {
            Extras = loadedSnapshot.LockedPlan.Extras! with { RulesVersion = 3 }
        };
        var mixedVersion = store.SaveCheckpoint(
            loadedSnapshot with { LockedPlan = badVersion }, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, mixedVersion.Status,
            "a v4 snapshot must reject a v3 plan extras version");
        AssertEx.True(mixedVersion.Issues.Any(issue =>
                issue.Code == RunStoreIssueCodes.InvalidSnapshot
                && issue.Message.Contains("rules version", StringComparison.Ordinal)),
            "mixed plan/snapshot rules versions must be rejected explicitly");
    }

    private static void V3ThemedSnapshotStillLoads()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("schema-3-themed-design");
        var fixture = ThemedFixtures.CreateV3WithSpecial();
        var actualSpecial = fixture.Offers
            .Select(offer => offer.Extras?.Special)
            .First(special => special is not null)!;
        AssertEx.True(actualSpecial.Preset != SpecialPreset.TacticalGroup,
            "the legacy v3 fixture must use a real pre-v4 preset, not TacticalGroup");
        var snapshot = AdventureSnapshot(fixture, blob);

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "v3 write status");
        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "v3 load status");
        var loadedSnapshot = load.Snapshot!;
        AssertEx.Equal(3, loadedSnapshot.SchemaVersion, "v3 schema retained");
        AssertEx.True(loadedSnapshot.LockedPlan!.Extras!.Special!.Enemies
            .All(unit => unit.DeploymentRole == null), "v3 optional roles stay absent");
        _ = RunDirector.Restore(loadedSnapshot);
    }

    private static void V4PolicyOptInRoundtrip()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("v4-policy-opt-in-design");
        var fixture = ThemedFixtures.Create(4, seed: 97531L, budget: 400m);
        var director = new RunDirector();
        var start = director.NewRun(new RunConfiguration(
            Fixtures.RunId, fixture.Request.Seed,
            new ScenarioValue(fixture.Request.AvailableBudget.Value, Fixtures.CostUnit),
            fixture.Eras, fixture.Definition.Version)
        {
            AdventureRules = true,
            AdventureVersion = 4
        });
        AssertEx.True(start.IsAccepted, "v4 opt-in run starts: " + start.Reason);
        var quote = new DesignQuote(
            new BlueprintReference("design-v4-policy", blob.SnapshotHash,
                fixture.Eras[0], Array.Empty<ModRequirement>()),
            new ScenarioValue(10m, Fixtures.CostUnit), true, true, null);
        AssertEx.True(director.ConfirmDesign(quote).IsAccepted, "v4 opt-in design confirms");
        AssertEx.Equal<int?>(null, director.Snapshot!.GenerationPolicy,
            "a v4 run starts without the policy-5 overlay");
        AssertEx.True(director.EnablePolicy5Generation().IsAccepted,
            "policy 5 can be queued at a safe uncommitted boundary");
        AssertEx.Equal<int?>(5, director.Snapshot!.GenerationPolicy,
            "opt-in policy is persisted on the run, not on old offers");
        var request = director.CreateOfferRequest();
        AssertEx.Equal(4, request.RulesVersion, "the saved run keeps its v4 reward/schema rules");
        AssertEx.Equal(5, request.GenerationPolicy, "future generation uses policy 5");

        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var offers = new SeededEncounterCatalogue(fixture.Definition, options)
            .GenerateOffers(request);
        AssertEx.True(offers.All(offer => offer.Extras?.GenerationPolicy == 5),
            "policy-5 generation marks each new offer with its policy overlay");
        var economy = new OfferEconomy(request.AvailableBudget, request.EnemyBudgetMultiplier)
        {
            EraProgression = request.EraProgressionEnabled
        };
        AssertEx.True(director.SetOffers(offers, economy).IsAccepted,
            "new policy-5 offers are accepted inside the v4 run");
        var write = store.SaveCheckpoint(director.Snapshot!, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status,
            "v4 opt-in checkpoint saves");

        var loaded = store.LoadActiveRun(ModAvailability.None).Snapshot!;
        AssertEx.Equal(4, loaded.SchemaVersion, "v4 schema remains");
        AssertEx.Equal(4, loaded.Rewards!.Version, "v4 reward rules remain");
        AssertEx.Equal<int?>(5, loaded.GenerationPolicy, "policy overlay survives disk");
        var pinnedRequest = request with
        {
            AvailableBudget = loaded.OfferEconomy!.Budget,
            EnemyBudgetMultiplier = loaded.OfferEconomy.EnemyBudgetMultiplier,
            EraProgressionEnabled = loaded.OfferEconomy.EraProgression ?? false,
            GenerationPolicy = loaded.GenerationPolicy ?? 0
        };
        var restored = new SeededEncounterCatalogue(fixture.Definition, options)
            .RestoreOffers(pinnedRequest, loaded.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.Success, restored.Status,
            "new policy-5 offers restore inside the old schema: "
            + string.Join(" | ", restored.Diagnostics));

        var v4Again = new SeededEncounterCatalogue(fixture.Definition, options)
            .GenerateOffers(request with { GenerationPolicy = 0 });
        AssertEx.True(v4Again.All(offer => offer.Extras?.GenerationPolicy is null),
            "the legacy v4 producer remains available and unmarked");
        AssertEx.True(v4Again.All(offer => offer.Extras?.EnemyBudgetTarget is null),
            "legacy v4 offers never acquire policy-5 target metadata");
    }

    private static void V5PinnedDifficultyRoundtrip()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("schema-5-difficulty-design");
        var fixture = ThemedFixtures.Create(5, seed: 2468L, budget: 700m,
            multiplier: 1.5m, progression: true);
        var snapshot = AdventureSnapshot(fixture, blob);
        var savedBytes = System.Text.Json.JsonSerializer.Serialize(snapshot.Offers);

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "v5 write status");

        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        using (var json = Fixtures.ReadJson(checkpointPath))
        {
            var root = json.RootElement.GetProperty("snapshot");
            var storedEconomy = root.GetProperty("offerEconomy");
            AssertEx.Equal(1.5m,
                storedEconomy.GetProperty("enemyBudgetMultiplier").GetDecimal(),
                "pinned base multiplier is stored exactly");
            AssertEx.True(storedEconomy.GetProperty("eraProgression").GetBoolean(),
                "pinned era progression is stored");
        }

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "v5 load status");
        var loaded = load.Snapshot!;
        AssertEx.Equal(5, loaded.SchemaVersion, "v5 snapshot schema");
        AssertEx.Equal(5, loaded.Rewards!.Version, "v5 reward rules");
        AssertEx.Equal<int?>(5, loaded.GenerationPolicy, "v5 run generation policy");
        var economy = loaded.OfferEconomy!;
        AssertEx.Equal(new ScenarioValue(700m, Fixtures.CostUnit), economy.Budget,
            "pinned offer budget survives disk");
        AssertEx.Equal(1.5m, economy.EnemyBudgetMultiplier,
            "pinned base multiplier survives disk");
        AssertEx.Equal<bool?>(true, economy.EraProgression,
            "pinned progression survives disk");
        AssertEx.Equal(savedBytes,
            System.Text.Json.JsonSerializer.Serialize(loaded.Offers),
            "loading does not rehash or alter saved offers");
        var standard = loaded.Offers.Single(offer => offer.Risk == EncounterRisk.Standard);
        AssertEx.True(standard.Extras?.Special is { Pattern: SpecialSpawnPattern.Ambush }
                || standard.Extras?.Special is { Pattern: SpecialSpawnPattern.CombinedArms },
            "schema 5 stores a supported policy-5 formation pattern");
        AssertEx.True(standard.Extras!.EnemyBudgetTarget is { } budgetTarget && budgetTarget > 0m,
            "schema 5 stores the honest scaled enemy target");
        var restoredDirector = RunDirector.Restore(loaded);
        AssertEx.NotNull(restoredDirector.Snapshot?.LockedPlan, "v5 locked plan restores");

        var restoreOptions = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var pinnedRequest = fixture.Request with
        {
            AvailableBudget = economy.Budget,
            EnemyBudgetMultiplier = economy.EnemyBudgetMultiplier,
            EraProgressionEnabled = economy.EraProgression ?? false,
            GenerationPolicy = loaded.GenerationPolicy ?? 0
        };
        var exact = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(pinnedRequest, loaded.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.Success, exact.Status,
            "exact v5 offers restore from their pinned economy: "
            + string.Join(" | ", exact.Diagnostics));

        var changedSetting = pinnedRequest with { EnemyBudgetMultiplier = 2.0m };
        var rejected = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(changedSetting, loaded.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, rejected.Status,
            "a later global difficulty change cannot silently alter pinned offers");

        var changedPattern = loaded.Offers.Select(plan =>
        {
            if (plan.Risk != EncounterRisk.Standard) return plan;
            var special = plan.Extras!.Special! with
            {
                Pattern = SpecialSpawnPattern.Line
            };
            var draft = plan with
            {
                Extras = plan.Extras! with { Special = special },
                OfferId = string.Empty,
                PlanHash = string.Empty
            };
            var contentHash = CatalogueHasher.Compute(fixture.Definition);
            var offerId = PlanHasher.CreateOfferId(pinnedRequest.RunId, contentHash, draft);
            var identified = draft with { OfferId = offerId };
            return identified with
            {
                PlanHash = PlanHasher.ComputePlanHash(contentHash, identified)
            };
        }).ToArray();
        var patternRejected = new SeededEncounterCatalogue(fixture.Definition, restoreOptions)
            .RestoreOffers(pinnedRequest, changedPattern);
        AssertEx.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, patternRejected.Status,
            "a rehashed pattern tamper cannot replace the pinned policy-5 plan identity");

        var era = fixture.Definition.Eras.Single();
        var massChanged = fixture.Definition with
        {
            Eras = new[]
            {
                era with
                {
                    Enemies = era.Enemies.Select(enemy => enemy with { MassKg = 1 }).ToArray()
                }
            }
        };
        var massRejected = new SeededEncounterCatalogue(massChanged, restoreOptions)
            .RestoreOffers(pinnedRequest, loaded.Offers);
        AssertEx.Equal(EncounterAvailabilityStatus.InvalidPersistedOffers, massRejected.Status,
            "changed measured-mass catalogue identity cannot accept old offer hashes");

        var duplicate = store.SaveCheckpoint(loaded, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Unchanged, duplicate.Status,
            "v5 exact duplicate checkpoint is inert");
    }

    private static void FiveEraPinnedScheduleRoundtrip()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var five = Fixtures.Eras.Take(5).ToArray();
        var preFinal = Fixtures.BaseSnapshot() with
        {
            EraSchedule = five,
            Phase = RunPhase.Building,
            Round = 13,
            BlockIndex = 4,
            EraIndex = 4,
            WinsInBlock = 0,
            Victories = 12,
            BlockBaseBudget = new ScenarioValue(500m, Fixtures.CostUnit),
            SupplyBonus = 0m,
            AvailableBudget = new ScenarioValue(500m, Fixtures.CostUnit),
            PendingSupplyPoints = 0
        };

        var write = store.SaveCheckpoint(preFinal);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status,
            "five-era pre-final write: " + string.Join("; ", write.Issues));
        var loaded = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, loaded.Status, "five-era pre-final load");
        AssertEx.True(loaded.CanResume, "five-era pre-final resumes");
        var preSnapshot = loaded.Snapshot!;
        AssertEx.Equal(5, preSnapshot.EraSchedule.Count, "pre-final schedule stays five");
        AssertEx.SequenceEqual(
            five.Select(era => era.NativeId),
            preSnapshot.EraSchedule.Select(era => era.NativeId),
            "pre-final schedule is pinned exactly");
        var restored = RunDirector.Restore(preSnapshot);
        AssertEx.Equal(4, restored.Snapshot!.EraIndex, "pre-final restores Latewar");

        var endless = preFinal with
        {
            Round = 16,
            BlockIndex = 5,
            EraIndex = 4,
            WinsInBlock = 0,
            Victories = 15,
            BlockBaseBudget = new ScenarioValue(575m, Fixtures.CostUnit),
            SupplyBonus = 0m,
            AvailableBudget = new ScenarioValue(575m, Fixtures.CostUnit)
        };
        write = store.SaveCheckpoint(endless);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status,
            "five-era endless write: " + string.Join("; ", write.Issues));
        loaded = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, loaded.Status, "five-era endless load");
        AssertEx.True(loaded.CanResume, "five-era endless resumes");
        var endlessSnapshot = loaded.Snapshot!;
        AssertEx.Equal(5, endlessSnapshot.BlockIndex, "endless block boundary retained");
        AssertEx.Equal(5, endlessSnapshot.EraSchedule.Count, "endless schedule stays five");
        AssertEx.Equal(4, endlessSnapshot.EraIndex, "endless stays in Latewar");
        restored = RunDirector.Restore(endlessSnapshot);
        AssertEx.Equal(5, restored.Snapshot!.BlockIndex, "endless restore keeps block");
        AssertEx.Equal(4, restored.Snapshot.EraIndex, "endless restore keeps Latewar");
    }

    private static RunSnapshot AdventureSnapshot(
        AdventureOfferFixture fixture, BlueprintSnapshot blob)
    {
        var director = new RunDirector();
        var configuration = new RunConfiguration(
            Fixtures.RunId, fixture.Request.Seed,
            new ScenarioValue(fixture.Request.AvailableBudget.Value, Fixtures.CostUnit),
            fixture.Eras,
            fixture.Definition.Version)
        {
            AdventureRules = true,
            AdventureVersion = fixture.Request.RulesVersion
        };
        var start = director.NewRun(configuration);
        AssertEx.True(start.IsAccepted, "themed run starts: " + start.Reason);

        var quote = new DesignQuote(
            new BlueprintReference("design-themed-player", blob.SnapshotHash,
                fixture.Eras[0], Array.Empty<ModRequirement>()),
            new ScenarioValue(10m, Fixtures.CostUnit), true, true, null);
        var design = director.ConfirmDesign(quote);
        AssertEx.True(design.IsAccepted, "themed design confirms: " + design.Reason);

        var set = director.SetOffers(fixture.Offers,
            fixture.Request.RulesVersion >= 5
                ? new OfferEconomy(fixture.Request.AvailableBudget,
                    fixture.Request.EnemyBudgetMultiplier)
                {
                    EraProgression = fixture.Request.EraProgressionEnabled
                }
                : (OfferEconomy?)null);
        AssertEx.True(set.IsAccepted, "production-generated themed offers set: " + set.Reason);
        var standard = fixture.Offers.Single(offer => offer.Risk == EncounterRisk.Standard);
        var select = director.SelectEncounter(standard.OfferId);
        AssertEx.True(select.IsAccepted, "themed offer selected: " + select.Reason);
        var begin = director.BeginEncounter();
        AssertEx.True(begin.IsAccepted, "themed encounter begins: " + begin.Reason);
        AssertEx.NotNull(director.Snapshot, "themed snapshot exists");
        return director.Snapshot!;
    }
}
