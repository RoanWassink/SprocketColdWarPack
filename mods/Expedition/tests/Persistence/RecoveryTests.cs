using System.Text;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence.Tests;

internal static class RecoveryTests
{
    public static void Register()
    {
        TestRunner.Add(
            "truncated current checkpoint requires explicit recovery to the backup",
            TruncatedCheckpointRecovery);
        TestRunner.Add(
            "interrupted temp file and orphan blueprint bytes are inert",
            InterruptedTempWriteIsInert);
        TestRunner.Add(
            "crash between checkpoint replace and index update recovers Current explicitly",
            CrashBeforeIndexRecoversCurrent);
        TestRunner.Add(
            "crash between checkpoint replace and index update recovers Previous and keeps both",
            CrashBeforeIndexRecoversPrevious);
        TestRunner.Add("checkpoint.previous holds the immediate last-good checkpoint", PreviousBackupPreserved);
        TestRunner.Add("failed write keeps the last-good checkpoint", FailedWriteKeepsCheckpoint);
        TestRunner.Add(
            "blueprint hash mismatch on resume requires explicit recovery",
            BlueprintMismatchOnResume);
        TestRunner.Add(
            "unsupported future schema is preserved and never reinterpreted",
            UnknownSchemaPreserved);
        TestRunner.Add(
            "missing/mismatched mods block resume without writes",
            ModsBlockWithoutWrites);
        TestRunner.Add(
            "recorded Lost cannot resume and is preserved for export",
            RecordedLossCannotResume);
        TestRunner.Add(
            "recorded Finished is idempotent and preserved for export",
            FinishedTerminalDuplicateIsIdempotent);
        TestRunner.Add(
            "Fighting recovery replays the exact plan under a new generation",
            FightingRetryReplaysExactPlan);
        TestRunner.Add(
            "a second run cannot overwrite the active run",
            SecondRunCannotOverwrite);
        TestRunner.Add(
            "missing active index requires explicit recovery",
            MissingIndexRequiresRecovery);
        TestRunner.Add(
            "blueprint paths are derived from hashes, never followed",
            BlueprintPathsAreDerived);
    }

    private static void TruncatedCheckpointRecovery()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot() with { Round = 1 };
        var second = first with { Round = 2, GenerationCounter = 1 };

        var firstWrite = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, firstWrite.Status, "first write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var firstBytes = File.ReadAllBytes(checkpointPath);

        var secondWrite = store.SaveCheckpoint(second);
        AssertEx.Equal(RunStoreWriteStatus.Committed, secondWrite.Status, "second write");
        AssertEx.SequenceEqual(
            firstBytes,
            File.ReadAllBytes(Fixtures.PreviousCheckpointPath(temp.StoreRoot)),
            "backup is the first checkpoint");

        var secondBytes = File.ReadAllBytes(checkpointPath);
        File.WriteAllBytes(
            checkpointPath,
            secondBytes.Take(secondBytes.Length / 2).ToArray());

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.RecoveryRequired, load.Status, "truncation status");
        AssertEx.ContainsIssue(load, RunStoreIssueCodes.CheckpointCorrupt, "truncation");
        AssertEx.False(load.CanResume, "cannot resume corrupt checkpoint");
        AssertEx.NotNull(load.Recovery, "recovery info");
        AssertEx.Equal<string?>(
            firstWrite.CheckpointHash,
            load.Recovery!.PreviousCheckpointHash,
            "backup hash");

        var currentRecovery = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.RecoveryRequired,
            currentRecovery.Status,
            "current checkpoint cannot be recovered");

        var previousRecovery = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Previous,
            ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, previousRecovery.Status, "previous recovery");
        AssertEx.True(previousRecovery.CanResume, "previous recovery resumes");
        AssertEx.NotNull(previousRecovery.Snapshot, "previous snapshot");
        AssertEx.Equal(1, previousRecovery.Snapshot!.Round, "recovered round");
        AssertEx.Equal<string?>(
            firstWrite.CheckpointHash,
            previousRecovery.CheckpointHash,
            "recovered hash");

        AssertEx.SequenceEqual(
            firstBytes,
            File.ReadAllBytes(checkpointPath),
            "selected backup restored into checkpoint.json");
        AssertEx.True(
            Directory.Exists(Fixtures.RunDirectory(temp.StoreRoot)),
            "run folder preserved");

        var reload = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, reload.Status, "reload after recovery");
        AssertEx.Equal(1, reload.Snapshot!.Round, "reloaded round");
    }

    private static void InterruptedTempWriteIsInert()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot() with { Round = 1 };

        var write = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");
        var runDirectory = Fixtures.RunDirectory(temp.StoreRoot);
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var checkpointBytes = File.ReadAllBytes(checkpointPath);

        var interruptedTemp = Path.Combine(runDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(interruptedTemp, Encoding.UTF8.GetBytes("{\"truncated\":"));

        var orphanHash = new string('b', 64);
        var blueprintsDirectory = Path.Combine(runDirectory, "blueprints");
        Directory.CreateDirectory(blueprintsDirectory);
        var orphanPath = Path.Combine(blueprintsDirectory, orphanHash + ".bin");
        File.WriteAllBytes(orphanPath, Encoding.UTF8.GetBytes("orphan bytes from a crashed save"));

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "leftovers do not corrupt load");
        AssertEx.Equal(1, load.Snapshot!.Round, "loaded round");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "checkpoint untouched by load");

        var next = first with { Round = 2, GenerationCounter = 1 };
        var nextWrite = store.SaveCheckpoint(next);
        AssertEx.Equal(RunStoreWriteStatus.Committed, nextWrite.Status, "next write");
        AssertEx.True(File.Exists(interruptedTemp), "unrelated temp file is inert, not deleted");
        AssertEx.True(File.Exists(orphanPath), "unreferenced blueprint is inert, not deleted");
    }

    private static void CrashBeforeIndexRecoversCurrent()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var state = CreateInterruptedIndexState(temp);
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.RecoveryRequired, load.Status, "interrupted index");
        AssertEx.ContainsIssue(load, RunStoreIssueCodes.RecoveryRequired, "recovery code");
        AssertEx.NotNull(load.Recovery, "recovery info");
        AssertEx.Equal<string?>(state.SecondHash, load.Recovery!.CurrentCheckpointHash, "current hash");
        AssertEx.Equal<string?>(state.FirstHash, load.Recovery.PreviousCheckpointHash, "previous hash");
        AssertEx.Equal<string?>(state.FirstHash, load.Recovery.IndexedCheckpointHash, "indexed hash");
        AssertEx.False(load.CanResume, "cannot resume without explicit recovery");

        var recovered = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, recovered.Status, "recover current");
        AssertEx.Equal(2, recovered.Snapshot!.Round, "current round retained");
        AssertEx.Equal<string?>(state.SecondHash, recovered.CheckpointHash, "current hash retained");
        AssertEx.SequenceEqual(
            state.SecondBytes,
            File.ReadAllBytes(checkpointPath),
            "current checkpoint bytes untouched");

        var reload = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, reload.Status, "reload after recovery");
        AssertEx.Equal(2, reload.Snapshot!.Round, "reload round");
    }

    private static void CrashBeforeIndexRecoversPrevious()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var state = CreateInterruptedIndexState(temp);
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var previousPath = Fixtures.PreviousCheckpointPath(temp.StoreRoot);

        var recovered = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Previous,
            ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, recovered.Status, "recover previous");
        AssertEx.Equal(1, recovered.Snapshot!.Round, "previous round");
        AssertEx.Equal<string?>(state.FirstHash, recovered.CheckpointHash, "previous hash");

        AssertEx.SequenceEqual(
            state.FirstBytes,
            File.ReadAllBytes(checkpointPath),
            "backup restored as current");
        AssertEx.SequenceEqual(
            state.SecondBytes,
            File.ReadAllBytes(previousPath),
            "superseded valid checkpoint retained in backup slot");

        var reload = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, reload.Status, "reload after rollback");
        AssertEx.Equal(1, reload.Snapshot!.Round, "reload round");
    }

    private static void PreviousBackupPreserved()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var previousPath = Fixtures.PreviousCheckpointPath(temp.StoreRoot);

        var first = Fixtures.BaseSnapshot() with { Round = 1 };
        var second = first with { Round = 2, GenerationCounter = 1 };
        var third = second with { Round = 3, GenerationCounter = 2 };

        store.SaveCheckpoint(first);
        store.SaveCheckpoint(second);
        var secondBytes = File.ReadAllBytes(Fixtures.CheckpointPath(temp.StoreRoot));
        store.SaveCheckpoint(third);

        AssertEx.Equal(
            3,
            Fixtures.StoredSnapshotRound(Fixtures.CheckpointPath(temp.StoreRoot)),
            "current is third");
        AssertEx.Equal(2, Fixtures.StoredSnapshotRound(previousPath), "previous is second");
        AssertEx.SequenceEqual(
            secondBytes,
            File.ReadAllBytes(previousPath),
            "previous bytes are the immediate predecessor");
    }

    private static void FailedWriteKeepsCheckpoint()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot() with { Round = 1 };

        var write = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "baseline write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var bytesBefore = File.ReadAllBytes(checkpointPath);

        var stale = store.SaveCheckpoint(
            first with { Round = 2, GenerationCounter = 1 },
            expectedRevision: 9);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, stale.Status, "stale revision");
        AssertEx.ContainsIssue(stale, RunStoreIssueCodes.StaleRevision, "stale revision code");
        AssertEx.SequenceEqual(
            bytesBefore,
            File.ReadAllBytes(checkpointPath),
            "stale write left checkpoint unchanged");

        var conflicting = Fixtures.Blob("conflicting-blueprint");
        var conflictPath = Fixtures.BlueprintPath(
            temp.StoreRoot,
            conflicting.SnapshotHash);
        Directory.CreateDirectory(Path.GetDirectoryName(conflictPath)!);
        var tampered = Encoding.UTF8.GetBytes("wrong bytes already at the content address");
        File.WriteAllBytes(conflictPath, tampered);

        var conflict = store.SaveCheckpoint(
            first with { Round = 2, GenerationCounter = 1 },
            new[] { conflicting });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, conflict.Status, "blueprint conflict");
        AssertEx.ContainsIssue(
            conflict,
            RunStoreIssueCodes.BlueprintConflict,
            "blueprint conflict code");
        AssertEx.SequenceEqual(
            bytesBefore,
            File.ReadAllBytes(checkpointPath),
            "conflict left checkpoint unchanged");
        AssertEx.SequenceEqual(
            tampered,
            File.ReadAllBytes(conflictPath),
            "conflicting content-addressed bytes were not overwritten");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "last-good checkpoint still loads");
        AssertEx.Equal(1, load.Snapshot!.Round, "last-good round");
    }

    private static void BlueprintMismatchOnResume()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blobA = Fixtures.Blob("design-a-bytes");
        var designA = Fixtures.Design(blobA);
        var first = Fixtures.BaseSnapshot() with
        {
            ConfirmedDesign = designA,
            DesignConfirmedForBlock = true
        };

        var firstWrite = store.SaveCheckpoint(first, new[] { blobA });
        AssertEx.Equal(RunStoreWriteStatus.Committed, firstWrite.Status, "first write");

        var blobB = Fixtures.Blob("design-b-bytes");
        var second = first with
        {
            Round = 2,
            GenerationCounter = 1,
            ConfirmedDesign = Fixtures.Design(blobB)
        };
        var secondWrite = store.SaveCheckpoint(second, new[] { blobB });
        AssertEx.Equal(RunStoreWriteStatus.Committed, secondWrite.Status, "second write");
        var secondBytes = File.ReadAllBytes(Fixtures.CheckpointPath(temp.StoreRoot));

        var blobBPath = Fixtures.BlueprintPath(temp.StoreRoot, blobB.SnapshotHash);
        var tampered = Encoding.UTF8.GetBytes("tampered blueprint bytes");
        File.WriteAllBytes(blobBPath, tampered);

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.RecoveryRequired, load.Status, "corrupt blob");
        AssertEx.ContainsIssue(load, RunStoreIssueCodes.BlueprintHashMismatch, "blob mismatch");
        AssertEx.False(load.CanResume, "cannot resume with bad blueprint bytes");

        var recovered = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Previous,
            ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, recovered.Status, "recover previous design");
        AssertEx.Equal(
            blobA.SnapshotHash,
            recovered.Snapshot!.ConfirmedDesign!.Blueprint.SnapshotHash,
            "recovered design hash");

        AssertEx.SequenceEqual(
            tampered,
            File.ReadAllBytes(blobBPath),
            "corrupt blueprint bytes were preserved, not deleted");
        AssertEx.SequenceEqual(
            secondBytes,
            File.ReadAllBytes(Fixtures.PreviousCheckpointPath(temp.StoreRoot)),
            "superseded checkpoint retained after recovery");
    }

    private static void UnknownSchemaPreserved()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot() with { Round = 1 };
        var firstWrite = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, firstWrite.Status, "first write");

        var second = first with { Round = 2, GenerationCounter = 1 };
        var secondWrite = store.SaveCheckpoint(second);
        AssertEx.Equal(RunStoreWriteStatus.Committed, secondWrite.Status, "second write");

        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var baselinePrevious = File.ReadAllBytes(
            Fixtures.PreviousCheckpointPath(temp.StoreRoot));

        var futureJson = "{\"schemaVersion\":2,\"kind\":\"runCheckpoint\","
            + "\"revision\":9,\"runId\":\"" + Fixtures.RunId + "\","
            + "\"snapshot\":{},\"blueprints\":[]}";
        var futureBytes = Encoding.UTF8.GetBytes(futureJson);
        File.WriteAllBytes(checkpointPath, futureBytes);
        var futureHash = Fixtures.Sha(futureBytes);
        var futureIndex = "{\"schemaVersion\":1,\"kind\":\"activeRunIndex\","
            + "\"activeRunId\":\"" + Fixtures.RunId + "\","
            + "\"checkpointHash\":\"" + futureHash + "\",\"revision\":9}";
        var futureIndexBytes = Encoding.UTF8.GetBytes(futureIndex);
        File.WriteAllBytes(indexPath, futureIndexBytes);

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.SchemaUnsupported, load.Status, "future schema load");
        AssertEx.ContainsIssue(load, RunStoreIssueCodes.SchemaUnsupported, "future schema issue");
        AssertEx.False(load.CanResume, "future schema cannot resume");
        AssertEx.SequenceEqual(
            futureBytes,
            File.ReadAllBytes(checkpointPath),
            "future checkpoint bytes unchanged by load");

        var save = store.SaveCheckpoint(first with { Round = 3, GenerationCounter = 2 });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, save.Status, "future schema write");
        AssertEx.ContainsIssue(save, RunStoreIssueCodes.SchemaUnsupported, "future schema write issue");
        AssertEx.SequenceEqual(
            futureBytes,
            File.ReadAllBytes(checkpointPath),
            "future checkpoint bytes unchanged by save");

        var currentRecovery = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.SchemaUnsupported,
            currentRecovery.Status,
            "future schema current recovery");

        var previousRecovery = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Previous,
            ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.SchemaUnsupported,
            previousRecovery.Status,
            "future schema is never silently rolled back");
        AssertEx.SequenceEqual(
            futureBytes,
            File.ReadAllBytes(checkpointPath),
            "future checkpoint bytes unchanged by recovery");
        AssertEx.SequenceEqual(
            baselinePrevious,
            File.ReadAllBytes(Fixtures.PreviousCheckpointPath(temp.StoreRoot)),
            "v1 backup unchanged while the future checkpoint is present");
        AssertEx.SequenceEqual(
            futureIndexBytes,
            File.ReadAllBytes(indexPath),
            "index bytes unchanged by unsupported-schema attempts");
    }

    private static void ModsBlockWithoutWrites()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("mod-design-bytes");
        var design = Fixtures.Design(blob, Fixtures.ModA);
        var offer = Fixtures.Plan("offer-1", mods: new[] { Fixtures.ModB });
        var snapshot = Fixtures.BaseSnapshot() with
        {
            ConfirmedDesign = design,
            DesignConfirmedForBlock = true,
            Offers = new[] { offer },
            SelectedOfferId = "offer-1"
        };

        var write = store.SaveCheckpoint(snapshot, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var checkpointBefore = File.ReadAllBytes(checkpointPath);
        var indexBefore = File.ReadAllBytes(indexPath);

        var missing = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.ResumeBlocked, missing.Status, "missing mods");
        AssertEx.False(missing.CanResume, "missing mods cannot resume");
        AssertEx.NotNull(missing.Snapshot, "blocked snapshot available for diagnostics");
        AssertEx.ContainsIssue(missing, RunStoreIssueCodes.ModMissing, "missing mod issue");

        var wrongVersion = store.LoadActiveRun(
            new ModAvailability(
                new[]
                {
                    Fixtures.ModA,
                    new ModRequirement(Fixtures.ModB.Id, "9.0.0")
                }));
        AssertEx.Equal(
            RunStoreLoadStatus.ResumeBlocked,
            wrongVersion.Status,
            "mismatched mod version");
        AssertEx.ContainsIssue(
            wrongVersion,
            RunStoreIssueCodes.ModVersionMismatch,
            "version mismatch issue");

        AssertEx.SequenceEqual(
            checkpointBefore,
            File.ReadAllBytes(checkpointPath),
            "mod block did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBefore,
            File.ReadAllBytes(indexPath),
            "mod block did not rewrite the index");

        var fullAvailability = new ModAvailability(new[] { Fixtures.ModA, Fixtures.ModB });
        var loaded = store.LoadActiveRun(fullAvailability);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, loaded.Status, "matching mods resume");
        AssertEx.True(loaded.CanResume, "matching mods can resume");

        // Blocked recovery must also avoid rewriting the index.
        var second = snapshot with { Round = 2, GenerationCounter = 1 };
        var secondWrite = store.SaveCheckpoint(second);
        AssertEx.Equal(RunStoreWriteStatus.Committed, secondWrite.Status, "second write");
        File.WriteAllBytes(indexPath, indexBefore);
        var interruptedIndex = File.ReadAllBytes(indexPath);

        var blockedRecovery = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.ResumeBlocked,
            blockedRecovery.Status,
            "blocked recovery status");
        AssertEx.SequenceEqual(
            interruptedIndex,
            File.ReadAllBytes(indexPath),
            "blocked recovery did not rewrite the index");

        var recovered = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            fullAvailability);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, recovered.Status, "recovery after mods installed");
        AssertEx.Equal(2, recovered.Snapshot!.Round, "recovered second checkpoint");
    }

    private static void RecordedLossCannotResume()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("lost-design-bytes");
        var design = Fixtures.Design(blob, Fixtures.ModA);
        var offer = Fixtures.Plan("offer-9", round: 5, mods: Fixtures.ModA);
        var attempt = Fixtures.Attempt("offer-9", 5);
        var lost = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.Lost,
            Round = 5,
            BlockIndex = 1,
            EraIndex = 1,
            Victories = 4,
            Defeats = 1,
            ConfirmedDesign = design,
            DesignConfirmedForBlock = true,
            Offers = new[] { offer },
            SelectedOfferId = "offer-9",
            LockedPlan = offer,
            CurrentAttempt = attempt,
            GenerationCounter = 5,
            ProcessedAttempts = new[] { attempt },
            ConsumedOfferIds = new[] { "offer-9" },
            LastOutcome = EncounterOutcome.FriendlyForceDestroyed,
            LastOutcomeDetail = "fixture: all friendly vehicles destroyed"
        };

        var write = store.SaveCheckpoint(lost, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "terminal write");
        AssertEx.True(File.Exists(Fixtures.RecordPath(temp.StoreRoot)), "record written");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var recordPath = Fixtures.RecordPath(temp.StoreRoot);
        var checkpointBytes = File.ReadAllBytes(checkpointPath);
        var indexBytes = File.ReadAllBytes(indexPath);
        var recordBytes = File.ReadAllBytes(recordPath);

        using (var indexDocument = Fixtures.ReadJson(indexPath))
        {
            AssertEx.Equal(
                System.Text.Json.JsonValueKind.Null,
                indexDocument.RootElement.GetProperty("activeRunId").ValueKind,
                "terminal write clears the active index");
        }

        var duplicate = store.SaveCheckpoint(lost, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Unchanged, duplicate.Status, "terminal duplicate");
        AssertEx.True(duplicate.CheckpointCommitted, "terminal duplicate is durable");
        AssertEx.Equal(write.Revision, duplicate.Revision, "terminal duplicate revision");
        AssertEx.Equal<string?>(
            write.CheckpointHash,
            duplicate.CheckpointHash,
            "terminal duplicate hash");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "terminal duplicate did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "terminal duplicate did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "terminal duplicate did not rewrite the terminal record");

        // The duplicate check must not depend on re-supplying already-stored native
        // snapshot bytes: the verified blob in the run folder is sufficient.
        var duplicateWithoutBlob = store.SaveCheckpoint(lost);
        AssertEx.Equal(
            RunStoreWriteStatus.Unchanged,
            duplicateWithoutBlob.Status,
            "terminal duplicate without re-supplied blob");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "blob-less terminal duplicate did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "blob-less terminal duplicate did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "blob-less terminal duplicate did not rewrite the terminal record");

        // A changed terminal snapshot must still be rejected, and must not disturb the
        // recorded loss or its record/index bytes.
        var changedTerminal = store.SaveCheckpoint(
            lost with { Round = 6 },
            new[] { blob });
        AssertEx.Equal(
            RunStoreWriteStatus.Rejected,
            changedTerminal.Status,
            "changed terminal snapshot rejected");
        AssertEx.ContainsIssue(
            changedTerminal,
            RunStoreIssueCodes.TerminalImmutable,
            "changed terminal immutable code");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "changed terminal snapshot did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "changed terminal snapshot did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "changed terminal snapshot did not rewrite the terminal record");

        var noActive = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.NoActiveRun, noActive.Status, "no active run");

        var loaded = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.CannotResumeTerminal,
            loaded.Status,
            "lost run status");
        AssertEx.False(loaded.CanResume, "recorded loss cannot resume");
        AssertEx.False(loaded.IsActiveRun, "lost run is not active");
        SnapshotAssert.Equal(lost, loaded.Snapshot!);

        var record = store.ReadRecord(Fixtures.RunId);
        AssertEx.NotNull(record.Record, "terminal record");
        AssertEx.Equal(RunPhase.Lost, record.Record!.Phase, "record phase");
        AssertEx.Equal<EncounterOutcome?>(
            EncounterOutcome.FriendlyForceDestroyed,
            record.Record.LastOutcome,
            "record outcome");
        AssertEx.Equal<string?>(
            write.CheckpointHash,
            record.Record.CheckpointHash,
            "record checkpoint hash");
        AssertEx.Equal(write.Revision, record.Record.CheckpointRevision, "record revision");
        AssertEx.NotNull(record.Record.Design, "record design");
        AssertEx.Equal(
            design.Blueprint.SnapshotHash,
            record.Record.Design!.Blueprint.SnapshotHash,
            "record design snapshot");

        var list = store.ListRuns();
        AssertEx.Equal(1, list.Runs.Count, "one stored run");
        AssertEx.Equal<RunPhase?>(RunPhase.Lost, list.Runs[0].Phase, "listed phase");
        AssertEx.True(list.Runs[0].IsTerminal, "listed as terminal");

        var blueprint = store.ReadBlueprint(Fixtures.RunId, blob.SnapshotHash);
        AssertEx.SequenceEqual(blob.Bytes, blueprint.Bytes!, "lost design export bytes");

        var resurrect = store.SaveCheckpoint(
            lost with { Phase = RunPhase.Fighting },
            new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Rejected, resurrect.Status, "resurrect rejected");
        AssertEx.ContainsIssue(
            resurrect,
            RunStoreIssueCodes.TerminalImmutable,
            "terminal immutable code");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "lost checkpoint bytes unchanged");

        var newRun = store.SaveCheckpoint(Fixtures.BaseSnapshot(Fixtures.OtherRunId));
        AssertEx.Equal(RunStoreWriteStatus.Committed, newRun.Status, "new run after loss");
        AssertEx.True(File.Exists(checkpointPath), "lost checkpoint retained");
        AssertEx.True(File.Exists(Fixtures.RecordPath(temp.StoreRoot)), "lost record retained");

        using (var indexDocument = Fixtures.ReadJson(Fixtures.IndexPath(temp.StoreRoot)))
        {
            AssertEx.Equal<string?>(
                Fixtures.OtherRunId,
                indexDocument.RootElement.GetProperty("activeRunId").GetString(),
                "new run is active");
        }

        var lostStill = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.CannotResumeTerminal,
            lostStill.Status,
            "lost run remains non-resumable");
    }

    private static void FinishedTerminalDuplicateIsIdempotent()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var finished = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.Finished,
            Round = 18,
            BlockIndex = 5,
            EraIndex = 5,
            Victories = 18,
            Defeats = 0,
            DesignConfirmedForBlock = true,
            GenerationCounter = 20,
            LastOutcome = EncounterOutcome.Victory,
            LastOutcomeDetail = "fixture: run finished"
        };

        var write = store.SaveCheckpoint(finished);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "finished write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var recordPath = Fixtures.RecordPath(temp.StoreRoot);
        AssertEx.True(File.Exists(recordPath), "finished record written");
        var checkpointBytes = File.ReadAllBytes(checkpointPath);
        var indexBytes = File.ReadAllBytes(indexPath);
        var recordBytes = File.ReadAllBytes(recordPath);

        var duplicate = store.SaveCheckpoint(finished);
        AssertEx.Equal(RunStoreWriteStatus.Unchanged, duplicate.Status, "finished duplicate");
        AssertEx.Equal(write.Revision, duplicate.Revision, "finished duplicate revision");
        AssertEx.Equal<string?>(
            write.CheckpointHash,
            duplicate.CheckpointHash,
            "finished duplicate hash");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "finished duplicate did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "finished duplicate did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "finished duplicate did not rewrite the terminal record");

        // Re-saving the snapshot as loaded back from disk is also an exact duplicate.
        var loadedFinished = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.CannotResumeTerminal,
            loadedFinished.Status,
            "finished load for duplicate");
        var duplicateLoaded = store.SaveCheckpoint(loadedFinished.Snapshot!);
        AssertEx.Equal(
            RunStoreWriteStatus.Unchanged,
            duplicateLoaded.Status,
            "finished duplicate from loaded snapshot");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "loaded finished duplicate did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "loaded finished duplicate did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "loaded finished duplicate did not rewrite the terminal record");

        var changedTerminal = store.SaveCheckpoint(finished with { Victories = 17 });
        AssertEx.Equal(
            RunStoreWriteStatus.Rejected,
            changedTerminal.Status,
            "changed finished snapshot rejected");
        AssertEx.ContainsIssue(
            changedTerminal,
            RunStoreIssueCodes.TerminalImmutable,
            "changed finished immutable code");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "changed finished snapshot did not rewrite the checkpoint");
        AssertEx.SequenceEqual(
            indexBytes,
            File.ReadAllBytes(indexPath),
            "changed finished snapshot did not rewrite the active index");
        AssertEx.SequenceEqual(
            recordBytes,
            File.ReadAllBytes(recordPath),
            "changed finished snapshot did not rewrite the terminal record");

        var noActive = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.NoActiveRun, noActive.Status, "finished no active run");

        var loaded = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.Equal(
            RunStoreLoadStatus.CannotResumeTerminal,
            loaded.Status,
            "finished cannot resume");
        AssertEx.False(loaded.CanResume, "finished cannot resume");
        SnapshotAssert.Equal(finished, loaded.Snapshot!);

        var record = store.ReadRecord(Fixtures.RunId);
        AssertEx.NotNull(record.Record, "finished record");
        AssertEx.Equal(RunPhase.Finished, record.Record!.Phase, "finished record phase");
    }

    private static void FightingRetryReplaysExactPlan()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("retry-design-bytes");
        var design = Fixtures.Design(blob);
        var offer = Fixtures.Plan("offer-r", round: 3, risk: EncounterRisk.Dangerous);
        var attempt = Fixtures.Attempt("offer-r", 3);
        var fighting = Fixtures.BaseSnapshot() with
        {
            Phase = RunPhase.Fighting,
            Round = 3,
            WinsInBlock = 2,
            Victories = 2,
            ConfirmedDesign = design,
            DesignConfirmedForBlock = true,
            Offers = new[] { offer },
            SelectedOfferId = "offer-r",
            LockedPlan = offer,
            CurrentAttempt = attempt,
            GenerationCounter = 3,
            ProcessedAttempts = new[] { Fixtures.Attempt("offer-prev", 2) },
            ConsumedOfferIds = new[] { "offer-prev" }
        };

        var write = store.SaveCheckpoint(fighting, new[] { blob });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "fighting write");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "fighting recovery load");
        AssertEx.Equal(RunPhase.Fighting, load.Snapshot!.Phase, "phase retained");
        SnapshotAssert.Equal(fighting, load.Snapshot!);

        var retry = load.Snapshot! with
        {
            GenerationCounter = 4,
            CurrentAttempt = Fixtures.Attempt("offer-r", 4)
        };
        var retryWrite = store.SaveCheckpoint(retry);
        AssertEx.Equal(RunStoreWriteStatus.Committed, retryWrite.Status, "retry write");

        var reload = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, reload.Status, "retry reload");
        AssertEx.Equal(4L, reload.Snapshot!.GenerationCounter, "new generation");
        AssertEx.Equal(4L, reload.Snapshot.CurrentAttempt!.Generation, "attempt generation");
        AssertEx.Equal<string?>("offer-r", reload.Snapshot.SelectedOfferId, "selected offer");
        AssertEx.NotNull(reload.Snapshot.LockedPlan, "locked plan");
        AssertEx.Equal(
            SnapshotAssert.Describe(offer),
            SnapshotAssert.Describe(reload.Snapshot.LockedPlan!),
            "exact plan replayed");
        AssertEx.SequenceEqual(
            new[] { SnapshotAssert.Describe(offer) },
            reload.Snapshot.Offers.Select(SnapshotAssert.Describe),
            "offers retained");
    }

    private static void SecondRunCannotOverwrite()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot();
        var write = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "first run");
        var firstBytes = File.ReadAllBytes(Fixtures.CheckpointPath(temp.StoreRoot));

        var second = Fixtures.BaseSnapshot(Fixtures.OtherRunId);
        var secondWrite = store.SaveCheckpoint(second);
        AssertEx.Equal(RunStoreWriteStatus.Rejected, secondWrite.Status, "second run rejected");
        AssertEx.ContainsIssue(
            secondWrite,
            RunStoreIssueCodes.ActiveRunExists,
            "active run guard");
        AssertEx.False(
            Directory.Exists(Fixtures.RunDirectory(temp.StoreRoot, Fixtures.OtherRunId)),
            "second run folder was not created");

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, load.Status, "first run still active");
        AssertEx.Equal(Fixtures.RunId, load.Snapshot!.RunId, "active run id");
        AssertEx.SequenceEqual(
            firstBytes,
            File.ReadAllBytes(Fixtures.CheckpointPath(temp.StoreRoot)),
            "first checkpoint unchanged");
    }

    private static void MissingIndexRequiresRecovery()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var write = store.SaveCheckpoint(Fixtures.BaseSnapshot() with { Round = 4 });
        AssertEx.Equal(RunStoreWriteStatus.Committed, write.Status, "write status");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var checkpointBytes = File.ReadAllBytes(checkpointPath);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        File.Delete(indexPath);

        var load = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.RecoveryRequired, load.Status, "missing index");
        AssertEx.ContainsIssue(load, RunStoreIssueCodes.IndexMissing, "missing index code");
        AssertEx.False(load.CanResume, "cannot resume without index");
        AssertEx.NotNull(load.Recovery, "recovery info");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "checkpoint untouched by missing index");

        var view = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, view.Status, "explicit view load");
        AssertEx.False(view.IsActiveRun, "not active without index");
        AssertEx.False(view.CanResume, "cannot resume without being active");
        AssertEx.Equal(4, view.Snapshot!.Round, "view round");

        var recovered = store.RecoverRun(
            Fixtures.RunId,
            RunCheckpointSelection.Current,
            ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, recovered.Status, "index recovery");
        AssertEx.True(recovered.CanResume, "recovered active run resumes");
        AssertEx.True(File.Exists(indexPath), "index recreated");
        AssertEx.SequenceEqual(
            checkpointBytes,
            File.ReadAllBytes(checkpointPath),
            "checkpoint untouched by index recovery");

        var reload = store.LoadActiveRun(ModAvailability.None);
        AssertEx.Equal(RunStoreLoadStatus.Loaded, reload.Status, "reload after index recovery");
    }

    private static void BlueprintPathsAreDerived()
    {
        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var blob = Fixtures.Blob("derived-path-design");
        var snapshot = Fixtures.BaseSnapshot() with
        {
            ConfirmedDesign = Fixtures.Design(blob),
            DesignConfirmedForBlock = true
        };
        store.SaveCheckpoint(snapshot, new[] { blob });

        var traversal = store.ReadBlueprint(Fixtures.RunId, "../../checkpoint.json");
        AssertEx.True(traversal.Bytes is null, "path-like hash returns no bytes");
        AssertEx.HasIssue(
            traversal.Issues,
            RunStoreIssueCodes.InvalidBlueprintHash,
            "path-like hash rejected");

        var traversalRun = store.ReadBlueprint("../../escape", blob.SnapshotHash);
        AssertEx.True(traversalRun.Bytes is null, "path-like run id returns no bytes");
        AssertEx.HasIssue(
            traversalRun.Issues,
            RunStoreIssueCodes.InvalidRunId,
            "path-like run id rejected");
    }

    private static InterruptedIndexState CreateInterruptedIndexState(TempDir temp)
    {
        var store = new RunStore(temp.StoreRoot);
        var first = Fixtures.BaseSnapshot() with { Round = 1 };
        var firstWrite = store.SaveCheckpoint(first);
        AssertEx.Equal(RunStoreWriteStatus.Committed, firstWrite.Status, "first write");
        var checkpointPath = Fixtures.CheckpointPath(temp.StoreRoot);
        var indexPath = Fixtures.IndexPath(temp.StoreRoot);
        var firstBytes = File.ReadAllBytes(checkpointPath);
        var indexAfterFirst = File.ReadAllBytes(indexPath);

        var second = first with { Round = 2, GenerationCounter = 1 };
        var secondWrite = store.SaveCheckpoint(second);
        AssertEx.Equal(RunStoreWriteStatus.Committed, secondWrite.Status, "second write");
        var secondBytes = File.ReadAllBytes(checkpointPath);

        // Simulate a crash after the new checkpoint was committed but before the
        // active index was updated.
        File.WriteAllBytes(indexPath, indexAfterFirst);

        return new InterruptedIndexState(
            firstWrite.CheckpointHash!,
            secondWrite.CheckpointHash!,
            firstBytes,
            secondBytes);
    }

    private sealed record InterruptedIndexState(
        string FirstHash,
        string SecondHash,
        byte[] FirstBytes,
        byte[] SecondBytes);
}
