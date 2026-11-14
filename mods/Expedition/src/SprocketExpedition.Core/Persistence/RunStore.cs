using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence;

// Explicit, caller-rooted local persistence for one active run.
//
// On-disk layout (all names fixed; ids are validated before becoming path parts):
//   <root>/active-run.json                      single active-run index
//   <root>/runs/<guid>/checkpoint.json          current checkpoint
//   <root>/runs/<guid>/checkpoint.previous.json last-good backup
//   <root>/runs/<guid>/blueprints/<sha256>.bin   content-addressed opaque bytes
//   <root>/runs/<guid>/record.json              terminal record for viewing/export
//
// Commit order is blueprint bytes -> previous <- current <- new checkpoint -> record
// -> active index. Every file is written to a temp file, made durable, verified and
// renamed. A crash therefore leaves either the old checkpoint or a complete new one,
// and an index/checkpoint mismatch is reported for explicit recovery, never hidden.
public sealed class RunStore
{
    public const int CurrentSchemaVersion = 7;

    private readonly string _root;
    private readonly string _rootWithSeparator;

    public RunStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException(
                "A run storage root directory is required.",
                nameof(rootDirectory));
        }

        if (!Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException(
                "The run storage root must be absolute and caller-supplied; it is never "
                + "resolved against the current directory.",
                nameof(rootDirectory));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rootDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new ArgumentException(
                "The run storage root is not a valid path.",
                nameof(rootDirectory),
                exception);
        }

        _root = Path.TrimEndingDirectorySeparator(fullPath);
        _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
    }

    public string RootDirectory => _root;

    private string ActiveIndexPath =>
        EnsureWithinRoot(Path.Combine(_root, RunStoreNames.ActiveIndexFileName));

    public RunStoreWriteResult SaveCheckpoint(
        RunSnapshot snapshot,
        IReadOnlyList<BlueprintSnapshot>? blueprints = null,
        long? expectedRevision = null)
    {
        try
        {
            return SaveCore(
                snapshot,
                blueprints ?? Array.Empty<BlueprintSnapshot>(),
                expectedRevision);
        }
        catch (IOException exception)
        {
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Failed,
                snapshot?.RunId,
                0,
                null,
                false,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
        catch (UnauthorizedAccessException exception)
        {
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Failed,
                snapshot?.RunId,
                0,
                null,
                false,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
    }

    public RunLoadResult LoadActiveRun(ModAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);

        try
        {
            return LoadActiveCore(availability);
        }
        catch (IOException exception)
        {
            return Error(null, RunStoreIssueCodes.IoFailure, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(null, RunStoreIssueCodes.IoFailure, exception.Message);
        }
    }

    // Reads a specific run without adopting it. A non-active non-terminal run is
    // returned for viewing only (CanResume is false).
    public RunLoadResult LoadRun(string runId, ModAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (!RunStoreIds.TryCanonicalize(runId, out var canonicalRunId, out var error))
        {
            return Error(null, RunStoreIssueCodes.InvalidRunId, error!);
        }

        try
        {
            var runDirectory = GetRunDirectory(canonicalRunId);
            if (!Directory.Exists(runDirectory))
            {
                return Error(
                    canonicalRunId,
                    RunStoreIssueCodes.CheckpointMissing,
                    $"Run '{canonicalRunId}' has no stored checkpoint.");
            }

            var indexSlot = ReadIndex();
            var index = indexSlot.Issue is null && !indexSlot.SchemaUnsupported
                ? indexSlot.Index
                : null;
            return LoadRunCore(canonicalRunId, runDirectory, index, availability, activeLookup: false);
        }
        catch (IOException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }
    }

    // Explicit recovery: verifies the chosen checkpoint and its blueprint bytes, then
    // rewrites the active index. Selecting the backup restores it into checkpoint.json;
    // a superseded valid checkpoint is retained in the backup slot when possible.
    // Mods are checked before any write.
    public RunLoadResult RecoverRun(
        string runId,
        RunCheckpointSelection selection,
        ModAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (!RunStoreIds.TryCanonicalize(runId, out var canonicalRunId, out var error))
        {
            return Error(null, RunStoreIssueCodes.InvalidRunId, error!);
        }

        try
        {
            return RecoverCore(canonicalRunId, selection, availability);
        }
        catch (IOException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }
    }

    public RunListResult ListRuns()
    {
        var summaries = new List<RunSummary>();
        var issues = new List<RunStoreIssue>();

        try
        {
            if (!Directory.Exists(_root))
            {
                return new RunListResult(Array.Empty<RunSummary>(), Array.Empty<RunStoreIssue>());
            }

            if (File.Exists(_root))
            {
                issues.Add(new RunStoreIssue(
                    RunStoreIssueCodes.InvalidRoot,
                    "The configured run storage root is a file, not a directory."));
                return new RunListResult(Array.Empty<RunSummary>(), issues);
            }

            var runsDirectory = RunStoreNames.GetRunsDirectory(_root);
            if (!Directory.Exists(runsDirectory))
            {
                return new RunListResult(Array.Empty<RunSummary>(), Array.Empty<RunStoreIssue>());
            }

            var runDirectories = Directory.GetDirectories(runsDirectory);
            Array.Sort(runDirectories, StringComparer.Ordinal);

            foreach (var runDirectory in runDirectories)
            {
                var directoryName = Path.GetFileName(runDirectory);
                if (!RunStoreIds.TryCanonicalize(directoryName, out var canonicalRunId, out _)
                    || !string.Equals(canonicalRunId, directoryName, StringComparison.Ordinal))
                {
                    issues.Add(new RunStoreIssue(
                        RunStoreIssueCodes.UnrecognizedRunDirectory,
                        $"Ignored unrecognized run directory '{directoryName}'."));
                    continue;
                }

                var current = ReadCheckpointSlot(
                    RunStoreNames.GetCheckpointPath(runDirectory));
                var slot = current;
                if (!slot.Exists || slot.Issue is not null)
                {
                    if (slot.SchemaUnsupported)
                    {
                        issues.Add(slot.Issue!);
                        continue;
                    }

                    var previous = ReadCheckpointSlot(
                        RunStoreNames.GetPreviousCheckpointPath(runDirectory));
                    if (previous.Exists && previous.Issue is null)
                    {
                        issues.Add(new RunStoreIssue(
                            RunStoreIssueCodes.RecoveryRequired,
                            $"Run '{canonicalRunId}' current checkpoint is unavailable; "
                            + "listing its last-good backup."));
                        slot = previous;
                    }
                }

                if (!slot.Exists)
                {
                    issues.Add(new RunStoreIssue(
                        RunStoreIssueCodes.CheckpointMissing,
                        $"Run '{canonicalRunId}' has no checkpoint."));
                    continue;
                }

                if (slot.Issue is not null || slot.Envelope is null)
                {
                    issues.Add(slot.Issue!);
                    continue;
                }

                var snapshot = slot.Envelope.Snapshot;
                summaries.Add(new RunSummary(
                    canonicalRunId,
                    snapshot.SchemaVersion,
                    snapshot.Phase,
                    snapshot.Round,
                    snapshot.Victories,
                    snapshot.Defeats,
                    IsTerminal(snapshot.Phase),
                    slot.Hash)
                {
                    Score = snapshot.Score,
                    TestPool = snapshot.TestPool, Ironman = snapshot.Ironman,
                    Era = snapshot.EraSchedule[snapshot.EraIndex]
                });
            }

            return new RunListResult(summaries.AsReadOnly(), issues.AsReadOnly());
        }
        catch (IOException exception)
        {
            issues.Add(new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message));
            return new RunListResult(summaries.AsReadOnly(), issues.AsReadOnly());
        }
        catch (UnauthorizedAccessException exception)
        {
            issues.Add(new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message));
            return new RunListResult(summaries.AsReadOnly(), issues.AsReadOnly());
        }
    }

    public RunRecordResult ReadRecord(string runId)
    {
        if (!RunStoreIds.TryCanonicalize(runId, out var canonicalRunId, out var error))
        {
            return new RunRecordResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.InvalidRunId, error!) });
        }

        try
        {
            var runDirectory = GetRunDirectory(canonicalRunId);
            var path = RunStoreNames.GetRecordPath(runDirectory);
            if (!Directory.Exists(runDirectory) || !File.Exists(path))
            {
                return new RunRecordResult(
                    null,
                    new[]
                    {
                        new RunStoreIssue(
                            RunStoreIssueCodes.RecordMissing,
                            $"Run '{canonicalRunId}' has no terminal record.")
                    });
            }

            var bytes = ReadBounded(path, RunStoreNames.MaxCheckpointBytes);
            var record = RunStoreFormats.DecodeRecord(bytes);
            if (!string.Equals(record.RunId, canonicalRunId, StringComparison.Ordinal))
            {
                return new RunRecordResult(
                    null,
                    new[]
                    {
                        new RunStoreIssue(
                            RunStoreIssueCodes.RecordCorrupt,
                            "Record runId does not match its run directory.")
                    });
            }

            return new RunRecordResult(record, Array.Empty<RunStoreIssue>());
        }
        catch (RunStoreFormatException exception)
        {
            return new RunRecordResult(
                null,
                new[] { new RunStoreIssue(exception.Code, exception.Message) });
        }
        catch (IOException exception)
        {
            return new RunRecordResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
        catch (UnauthorizedAccessException exception)
        {
            return new RunRecordResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
    }

    // Reads one design snapshot blob for viewing/export. The path is derived from a
    // validated hash; a caller-supplied path or reference is never followed.
    public BlueprintReadResult ReadBlueprint(string runId, string snapshotHash)
    {
        if (!RunStoreIds.TryCanonicalize(runId, out var canonicalRunId, out var runIdError))
        {
            return new BlueprintReadResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.InvalidRunId, runIdError!) });
        }

        if (!RunStoreIds.TryNormalizeHash(snapshotHash, out var hash, out var hashError))
        {
            return new BlueprintReadResult(
                null,
                new[]
                {
                    new RunStoreIssue(RunStoreIssueCodes.InvalidBlueprintHash, hashError!)
                });
        }

        try
        {
            var runDirectory = GetRunDirectory(canonicalRunId);
            var path = EnsureWithinRoot(RunStoreNames.GetBlueprintPath(runDirectory, hash));
            if (!File.Exists(path))
            {
                return new BlueprintReadResult(
                    null,
                    new[]
                    {
                        new RunStoreIssue(
                            RunStoreIssueCodes.BlueprintMissing,
                            $"Blueprint '{hash}' is not stored for run '{canonicalRunId}'.")
                    });
            }

            var bytes = ReadBounded(path, RunStoreNames.MaxBlueprintBytes);
            if (!string.Equals(Sha256Hex.Compute(bytes), hash, StringComparison.Ordinal))
            {
                return new BlueprintReadResult(
                    null,
                    new[]
                    {
                        new RunStoreIssue(
                            RunStoreIssueCodes.BlueprintHashMismatch,
                            $"Blueprint '{hash}' bytes do not match their hash.")
                    });
            }

            return new BlueprintReadResult(bytes, Array.Empty<RunStoreIssue>());
        }
        catch (RunStoreFormatException exception)
        {
            return new BlueprintReadResult(
                null,
                new[] { new RunStoreIssue(exception.Code, exception.Message) });
        }
        catch (IOException exception)
        {
            return new BlueprintReadResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
        catch (UnauthorizedAccessException exception)
        {
            return new BlueprintReadResult(
                null,
                new[] { new RunStoreIssue(RunStoreIssueCodes.IoFailure, exception.Message) });
        }
    }

    private RunStoreWriteResult SaveCore(
        RunSnapshot snapshot,
        IReadOnlyList<BlueprintSnapshot> blueprints,
        long? expectedRevision)
    {
        if (snapshot is null)
        {
            return Rejected(
                null,
                0,
                RunStoreIssueCodes.InvalidSnapshot,
                "Snapshot is null.");
        }

        if (!RunStoreIds.TryCanonicalize(snapshot.RunId, out var canonicalRunId, out var runIdError))
        {
            return Rejected(
                snapshot.RunId,
                0,
                RunStoreIssueCodes.InvalidRunId,
                runIdError!);
        }

        if (snapshot.SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7))
        {
            return Rejected(
                canonicalRunId,
                0,
                RunStoreIssueCodes.SchemaUnsupported,
                $"Snapshot schemaVersion {snapshot.SchemaVersion} is not supported; "
                + "bytes on disk are left unchanged.");
        }

        var validationIssue = RunSnapshotValidation.Validate(snapshot);
        if (validationIssue is not null)
        {
            return Rejected(
                canonicalRunId,
                0,
                validationIssue.Code,
                validationIssue.Message);
        }

        string? designHash = null;
        if (snapshot.ConfirmedDesign is not null)
        {
            if (!RunStoreIds.TryNormalizeHash(
                    snapshot.ConfirmedDesign.Blueprint.SnapshotHash,
                    out var normalizedDesignHash,
                    out var designHashError))
            {
                return Rejected(
                    canonicalRunId,
                    0,
                    RunStoreIssueCodes.InvalidBlueprintHash,
                    designHashError!);
            }

            designHash = normalizedDesignHash;
        }

        if (!TryValidateSuppliedBlueprints(
                blueprints,
                out var supplied,
                out var blueprintIssue))
        {
            return Rejected(
                canonicalRunId,
                0,
                blueprintIssue!.Code,
                blueprintIssue.Message);
        }

        var runDirectory = GetRunDirectory(canonicalRunId);
        var checkpointPath = EnsureWithinRoot(
            RunStoreNames.GetCheckpointPath(runDirectory));
        var previousCheckpointPath = EnsureWithinRoot(
            RunStoreNames.GetPreviousCheckpointPath(runDirectory));
        var recordPath = EnsureWithinRoot(RunStoreNames.GetRecordPath(runDirectory));

        var indexSlot = ReadIndex();
        if (indexSlot.Issue is not null)
        {
            return Rejected(
                canonicalRunId,
                0,
                indexSlot.Issue.Code,
                indexSlot.Issue.Message);
        }

        var index = indexSlot.Index;
        var current = ReadCheckpointSlot(checkpointPath);
        var previous = ReadCheckpointSlot(previousCheckpointPath);

        if (current.SchemaUnsupported)
        {
            return Rejected(
                canonicalRunId,
                0,
                RunStoreIssueCodes.SchemaUnsupported,
                "The stored checkpoint has a newer schema. Bytes are preserved and will "
                + "not be reinterpreted or overwritten.");
        }

        if (current.Exists && current.Issue is not null)
        {
            return Rejected(
                canonicalRunId,
                0,
                RunStoreIssueCodes.RecoveryRequired,
                $"The stored checkpoint cannot be verified ({current.Issue.Message}). "
                + "Run explicit recovery before saving.");
        }

        var existing = current.Envelope;
        var existingBytes = current.Bytes;
        var existingHash = current.Hash;
        var existingRevision = existing?.Revision ?? 0;

        // Exact terminal duplicate: fully inert. It must not be turned into a
        // terminal-immutability rejection and must not rewrite the checkpoint, the
        // active index or the terminal record. Different terminal state is rejected
        // by the TerminalImmutable check below. Duplicate identity is the stored raw
        // snapshot element, not a decode/re-encode of it.
        if (existing is not null
            && existingBytes is not null
            && IsTerminal(existing.Snapshot.Phase)
            && VerifyBlueprints(runDirectory, existing) is null
            && IsExactSnapshotDuplicate(existingBytes, existing.Snapshot, snapshot))
        {
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Unchanged,
                canonicalRunId,
                existing.Revision,
                existingHash,
                true,
                Array.Empty<RunStoreIssue>());
        }

        if (index is null
            && EnumerateRunIds().Any(
                existingRunId => !string.Equals(
                    existingRunId,
                    canonicalRunId,
                    StringComparison.Ordinal)))
        {
            return Rejected(
                canonicalRunId,
                0,
                RunStoreIssueCodes.RecoveryRequired,
                "The active-run index is missing while stored runs exist. No new run was "
                + "started; recover the intended run explicitly.");
        }

        if (index is not null
            && index.ActiveRunId is null
            && TryFindUnresolvedRun(out var unresolvedRunId)
            && !string.Equals(unresolvedRunId, canonicalRunId, StringComparison.Ordinal))
        {
            return Rejected(
                canonicalRunId,
                0,
                RunStoreIssueCodes.RecoveryRequired,
                $"Run '{unresolvedRunId}' has no committed terminal index and cannot be "
                + "proven finished. No new run was started; recover it explicitly.");
        }

        var manifestIssue = BuildManifest(
            runDirectory,
            designHash,
            supplied,
            out var manifest);
        if (manifestIssue is not null)
        {
            return Rejected(
                canonicalRunId,
                existingRevision,
                manifestIssue.Code,
                manifestIssue.Message);
        }

        // Idempotent duplicate: compare the persisted raw snapshot element rather than
        // the envelope bytes, because a retry may legitimately rebuild the same
        // manifest. Referenced blueprint bytes must already be present and verified.
        var existingBlueprintsValid = existing is not null
            && VerifyBlueprints(runDirectory, existing) is null;
        if (existing is not null
            && existingBytes is not null
            && existingBlueprintsValid
            && IsExactSnapshotDuplicate(existingBytes, existing.Snapshot, snapshot))
        {
            // An exact terminal duplicate is fully inert: no checkpoint, index or
            // record rewrite. A changed terminal snapshot remains rejected below.
            if (IsTerminal(existing.Snapshot.Phase))
            {
                return new RunStoreWriteResult(
                    RunStoreWriteStatus.Unchanged,
                    canonicalRunId,
                    existing.Revision,
                    existingHash,
                    true,
                    Array.Empty<RunStoreIssue>());
            }

            var warning = TryRepairIndexForExistingCheckpoint(
                canonicalRunId,
                index,
                existing,
                existingHash!);
            var duplicateIssues = warning is null
                ? (IReadOnlyList<RunStoreIssue>)Array.Empty<RunStoreIssue>()
                : (IReadOnlyList<RunStoreIssue>)new[] { warning! };
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Unchanged,
                canonicalRunId,
                existing.Revision,
                existingHash,
                true,
                duplicateIssues);
        }

        if (expectedRevision.HasValue && expectedRevision.Value != existingRevision)
        {
            return Rejected(
                canonicalRunId,
                existingRevision,
                RunStoreIssueCodes.StaleRevision,
                $"Expected revision {expectedRevision.Value} but the stored revision is "
                + $"{existingRevision}.");
        }

        if (existing is not null)
        {
            if (existing.Snapshot.SchemaVersion != snapshot.SchemaVersion
                || ((existing.Snapshot.Rewards == null) != (snapshot.Rewards == null)))
                return Rejected(canonicalRunId, existingRevision, RunStoreIssueCodes.InvalidSnapshot, "A run cannot change its rules version.");
            if (existing.Snapshot.Rewards is { } previousRewards && snapshot.Rewards is { } rewards)
            {
                var differs = System.Text.Json.JsonSerializer.Serialize(previousRewards) != System.Text.Json.JsonSerializer.Serialize(rewards);
                if (rewards.Revision < previousRewards.Revision || (rewards.Revision == previousRewards.Revision && differs)
                    || rewards.EarnedFunds < previousRewards.EarnedFunds
                    || previousRewards.GrantedOffers.Any(id => !rewards.GrantedOffers.Contains(id, StringComparer.Ordinal)))
                    return Rejected(canonicalRunId, existingRevision, RunStoreIssueCodes.StaleRevision, "Reward inventory is older than or conflicts with the stored checkpoint.");
            }
            var identityIssue = CheckRunIdentity(existing.Snapshot, snapshot);
            if (identityIssue is not null)
            {
                return Rejected(
                    canonicalRunId,
                    existingRevision,
                    identityIssue.Code,
                    identityIssue.Message);
            }

            if (snapshot.SchemaVersion >= 6 && (snapshot.Attempts < existing.Snapshot.Attempts
                || snapshot.EnemiesDestroyed < existing.Snapshot.EnemiesDestroyed
                || snapshot.FriendliesLost < existing.Snapshot.FriendliesLost
                || snapshot.Defeats < existing.Snapshot.Defeats || snapshot.Score < existing.Snapshot.Score))
                return Rejected(canonicalRunId, existingRevision, RunStoreIssueCodes.StaleRevision,
                    "Cumulative schema-6 totals and score cannot decrease.");

            if (IsTerminal(existing.Snapshot.Phase))
            {
                return Rejected(
                    canonicalRunId,
                    existingRevision,
                    RunStoreIssueCodes.TerminalImmutable,
                    "The run is recorded as Lost/Finished. A terminal checkpoint cannot be "
                    + "rewritten or resumed.");
            }

            if (snapshot.GenerationCounter < existing.Snapshot.GenerationCounter)
            {
                return Rejected(
                    canonicalRunId,
                    existingRevision,
                    RunStoreIssueCodes.OlderGeneration,
                    "The snapshot generation is older than the stored checkpoint.");
            }

            foreach (var processed in existing.Snapshot.ProcessedAttempts)
            {
                if (!snapshot.ProcessedAttempts.Any(candidate => SameAttempt(candidate, processed)))
                {
                    return Rejected(
                        canonicalRunId,
                        existingRevision,
                        RunStoreIssueCodes.RemovesProcessedAttempt,
                        "The snapshot omits an attempt that is already processed. "
                        + "Out-of-order/duplicate results are inert.");
                }
            }

            foreach (var consumedOfferId in existing.Snapshot.ConsumedOfferIds)
            {
                if (!snapshot.ConsumedOfferIds.Contains(consumedOfferId, StringComparer.Ordinal))
                {
                    return Rejected(
                        canonicalRunId,
                        existingRevision,
                        RunStoreIssueCodes.RemovesConsumedOffer,
                        "The snapshot omits an already consumed offer id.");
                }
            }
        }

        var indexIssue = CheckIndexAllowsWrite(
            canonicalRunId,
            index,
            existingHash,
            existing is not null,
            runDirectory,
            previous,
            recordPath);
        if (indexIssue is not null)
        {
            return Rejected(
                canonicalRunId,
                existingRevision,
                indexIssue.Code,
                indexIssue.Message);
        }

        var newRevision = existingRevision + 1;
        var checkpointBytes = RunStoreFormats.EncodeCheckpoint(
            new RunCheckpointEnvelope(
                newRevision,
                snapshot.RunId,
                snapshot,
                manifest));
        var newHash = Sha256Hex.Compute(checkpointBytes);

        // 1. Content-addressed blueprint bytes are committed first. Existing blobs are
        //    immutable and verified; a conflicting file is never overwritten.
        foreach (var pair in supplied.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var blueprintPath = EnsureWithinRoot(
                RunStoreNames.GetBlueprintPath(runDirectory, pair.Key));
            if (File.Exists(blueprintPath))
            {
                var existingIssue = VerifyBlueprintFile(runDirectory, pair.Key, out _);
                if (existingIssue is not null)
                {
                    return Rejected(
                        canonicalRunId,
                        existingRevision,
                        existingIssue.Code,
                        existingIssue.Message);
                }

                continue;
            }

            var blueprintsDirectory = RunStoreNames.GetBlueprintsDirectory(runDirectory);
            Directory.CreateDirectory(blueprintsDirectory);
            var blueprintTemp = AtomicFile.NewTempPath(blueprintsDirectory);
            try
            {
                AtomicFile.WriteDurable(blueprintTemp, pair.Value);
                var written = File.ReadAllBytes(blueprintTemp);
                if (!string.Equals(Sha256Hex.Compute(written), pair.Key, StringComparison.Ordinal))
                {
                    AtomicFile.TryDelete(blueprintTemp);
                    return Rejected(
                        canonicalRunId,
                        existingRevision,
                        RunStoreIssueCodes.BlueprintHashMismatch,
                        $"Staged blueprint '{pair.Key}' failed hash verification.");
                }

                AtomicFile.MoveReplace(blueprintTemp, blueprintPath);
            }
            catch
            {
                AtomicFile.TryDelete(blueprintTemp);
                throw;
            }
        }

        // 2. Preserve the current checkpoint as last-good, then atomically replace the
        //    current checkpoint. A failure before the final rename leaves the previous
        //    valid checkpoint in place.
        Directory.CreateDirectory(runDirectory);
        var checkpointTemp = AtomicFile.NewTempPath(runDirectory);
        try
        {
            AtomicFile.WriteDurable(checkpointTemp, checkpointBytes);
            var writtenCheckpoint = File.ReadAllBytes(checkpointTemp);
            if (!string.Equals(
                    Sha256Hex.Compute(writtenCheckpoint),
                    newHash,
                    StringComparison.Ordinal))
            {
                AtomicFile.TryDelete(checkpointTemp);
                return Failed(
                    canonicalRunId,
                    existingRevision,
                    RunStoreIssueCodes.IoFailure,
                    "Staged checkpoint failed hash verification.",
                    checkpointCommitted: false);
            }

            if (File.Exists(checkpointPath))
            {
                var currentBytes = File.ReadAllBytes(checkpointPath);
                if (existingHash is null
                    || !string.Equals(
                        Sha256Hex.Compute(currentBytes),
                        existingHash,
                        StringComparison.Ordinal))
                {
                    AtomicFile.TryDelete(checkpointTemp);
                    return Failed(
                        canonicalRunId,
                        existingRevision,
                        RunStoreIssueCodes.RecoveryRequired,
                        "The stored checkpoint changed during the write. Nothing was "
                        + "replaced; run explicit recovery.",
                        checkpointCommitted: false);
                }

                var previousTemp = AtomicFile.NewTempPath(runDirectory);
                try
                {
                    AtomicFile.WriteDurable(previousTemp, currentBytes);
                    AtomicFile.MoveReplace(previousTemp, previousCheckpointPath);
                }
                catch
                {
                    AtomicFile.TryDelete(previousTemp);
                    throw;
                }
            }

            AtomicFile.MoveReplace(checkpointTemp, checkpointPath);
        }
        catch (IOException exception)
        {
            AtomicFile.TryDelete(checkpointTemp);
            return Failed(
                canonicalRunId,
                existingRevision,
                RunStoreIssueCodes.IoFailure,
                exception.Message,
                checkpointCommitted: false);
        }
        catch (UnauthorizedAccessException exception)
        {
            AtomicFile.TryDelete(checkpointTemp);
            return Failed(
                canonicalRunId,
                existingRevision,
                RunStoreIssueCodes.IoFailure,
                exception.Message,
                checkpointCommitted: false);
        }

        var warnings = new List<RunStoreIssue>();

        // 3. Terminal records are separate, derived data for viewing/export. A record
        //    failure does not invalidate the committed loss; it is reported.
        if (IsTerminal(snapshot.Phase))
        {
            try
            {
                WriteRecordFile(
                    runDirectory,
                    BuildRecord(canonicalRunId, snapshot, newRevision, newHash));
            }
            catch (IOException exception)
            {
                warnings.Add(new RunStoreIssue(
                    RunStoreIssueCodes.IoFailure,
                    $"Checkpoint committed but the terminal record could not be written: "
                    + exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                warnings.Add(new RunStoreIssue(
                    RunStoreIssueCodes.IoFailure,
                    $"Checkpoint committed but the terminal record could not be written: "
                    + exception.Message));
            }
        }

        // 4. The single active-run index is updated last. A crash here leaves a complete
        //    checkpoint that explicit recovery can adopt.
        try
        {
            var terminal = IsTerminal(snapshot.Phase);
            WriteIndex(terminal
                ? new RunStoreIndex(null, null, newRevision)
                : new RunStoreIndex(canonicalRunId, newHash, newRevision));
        }
        catch (IOException exception)
        {
            warnings.Add(new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                "Checkpoint committed but the active-run index update failed: "
                + exception.Message));
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Failed,
                canonicalRunId,
                newRevision,
                newHash,
                true,
                warnings.AsReadOnly());
        }
        catch (UnauthorizedAccessException exception)
        {
            warnings.Add(new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                "Checkpoint committed but the active-run index update failed: "
                + exception.Message));
            return new RunStoreWriteResult(
                RunStoreWriteStatus.Failed,
                canonicalRunId,
                newRevision,
                newHash,
                true,
                warnings.AsReadOnly());
        }

        return new RunStoreWriteResult(
            RunStoreWriteStatus.Committed,
            canonicalRunId,
            newRevision,
            newHash,
            true,
            warnings.AsReadOnly());
    }

    private RunLoadResult LoadActiveCore(ModAvailability availability)
    {
        if (!Directory.Exists(_root))
        {
            return new RunLoadResult(
                RunStoreLoadStatus.NoActiveRun,
                null,
                null,
                Array.Empty<RunStoreIssue>(),
                null,
                false,
                null);
        }

        if (File.Exists(_root))
        {
            return Error(
                null,
                RunStoreIssueCodes.InvalidRoot,
                "The configured run storage root is a file, not a directory.");
        }

        var indexSlot = ReadIndex();
        if (indexSlot.Issue is not null)
        {
            return new RunLoadResult(
                indexSlot.SchemaUnsupported
                    ? RunStoreLoadStatus.SchemaUnsupported
                    : RunStoreLoadStatus.Error,
                null,
                null,
                new[] { indexSlot.Issue! },
                null,
                false,
                null);
        }

        if (indexSlot.Index is null)
        {
            var runIds = EnumerateRunIds();
            if (runIds.Count == 0)
            {
                return new RunLoadResult(
                    RunStoreLoadStatus.NoActiveRun,
                    null,
                    null,
                    Array.Empty<RunStoreIssue>(),
                    null,
                    false,
                    null);
            }

            var onlyRunId = runIds.Count == 1 ? runIds[0] : null;
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                onlyRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.IndexMissing,
                        "The active-run index is missing but stored runs exist. No run was "
                        + "started; choose a run and checkpoint explicitly.")
                },
                new RunRecoveryInfo(
                    onlyRunId ?? string.Empty,
                    null,
                    null,
                    null,
                    "active-run index missing"),
                false,
                null);
        }

        var index = indexSlot.Index;
        if (index.ActiveRunId is null)
        {
            if (TryFindUnresolvedRun(out var unresolvedRunId))
            {
                return new RunLoadResult(
                    RunStoreLoadStatus.RecoveryRequired,
                    unresolvedRunId,
                    null,
                    new[]
                    {
                        new RunStoreIssue(
                            RunStoreIssueCodes.RecoveryRequired,
                            $"Run '{unresolvedRunId}' is stored without a committed terminal "
                            + "index and cannot be proven finished. No new run was started; "
                            + "recover it explicitly.")
                    },
                    new RunRecoveryInfo(
                        unresolvedRunId ?? string.Empty,
                        null,
                        null,
                        null,
                        "active index cleared while a non-terminal run exists"),
                    false,
                    null);
            }

            return new RunLoadResult(
                RunStoreLoadStatus.NoActiveRun,
                null,
                null,
                Array.Empty<RunStoreIssue>(),
                null,
                false,
                null);
        }

        if (!RunStoreIds.TryCanonicalize(index.ActiveRunId, out var canonicalRunId, out var error))
        {
            return Error(null, RunStoreIssueCodes.IndexCorrupt, error!);
        }

        var runDirectory = GetRunDirectory(canonicalRunId);
        if (!Directory.Exists(runDirectory))
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.IndexRunMissing,
                        $"The active index references run '{canonicalRunId}', but its folder "
                        + "is missing. No new run was started.")
                },
                new RunRecoveryInfo(
                    canonicalRunId,
                    index.CheckpointHash,
                    null,
                    null,
                    "active index references a missing run folder"),
                false,
                null);
        }

        return LoadRunCore(
            canonicalRunId,
            runDirectory,
            index,
            availability,
            activeLookup: true);
    }

    private RunLoadResult LoadRunCore(
        string canonicalRunId,
        string runDirectory,
        RunStoreIndex? index,
        ModAvailability availability,
        bool activeLookup)
    {
        var checkpointPath = RunStoreNames.GetCheckpointPath(runDirectory);
        var current = ReadCheckpointSlot(checkpointPath);

        if (current.SchemaUnsupported)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.SchemaUnsupported,
                canonicalRunId,
                null,
                new[] { current.Issue! },
                null,
                false,
                current.Hash);
        }

        if (current.Exists && current.Issue is not null)
        {
            var previousHash = InspectPreviousCheckpoint(runDirectory);
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[] { current.Issue! },
                new RunRecoveryInfo(
                    canonicalRunId,
                    index?.CheckpointHash,
                    current.Hash,
                    previousHash,
                    "current checkpoint failed verification"),
                false,
                current.Hash);
        }

        if (!current.Exists)
        {
            var previousHash = InspectPreviousCheckpoint(runDirectory);
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.CheckpointMissing,
                        "The current checkpoint is missing. No new run was started.")
                },
                new RunRecoveryInfo(
                    canonicalRunId,
                    index?.CheckpointHash,
                    null,
                    previousHash,
                    "current checkpoint missing"),
                false,
                null);
        }

        var envelope = current.Envelope!;
        var snapshot = envelope.Snapshot;

        if (!RunStoreIds.TryCanonicalize(snapshot.RunId, out var snapshotRunId, out _)
            || !string.Equals(snapshotRunId, canonicalRunId, StringComparison.Ordinal))
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.CheckpointCorrupt,
                        "Checkpoint runId does not match its run directory.")
                },
                new RunRecoveryInfo(
                    canonicalRunId,
                    index?.CheckpointHash,
                    current.Hash,
                    InspectPreviousCheckpoint(runDirectory),
                    "checkpoint run identity mismatch"),
                false,
                current.Hash);
        }

        var isActive = false;
        if (activeLookup)
        {
            if (index is null)
            {
                return RecoveryForIndexMismatch(
                    canonicalRunId,
                    runDirectory,
                    index,
                    current.Hash,
                    "active index is missing");
            }

            if (!RunStoreIds.TryCanonicalize(index.ActiveRunId, out var indexRunId, out _)
                || !string.Equals(indexRunId, canonicalRunId, StringComparison.Ordinal))
            {
                return RecoveryForIndexMismatch(
                    canonicalRunId,
                    runDirectory,
                    index,
                    current.Hash,
                    "active index references another run");
            }

            if (!string.Equals(index.CheckpointHash, current.Hash, StringComparison.Ordinal))
            {
                return RecoveryForIndexMismatch(
                    canonicalRunId,
                    runDirectory,
                    index,
                    current.Hash,
                    "active index checkpoint hash does not match the current checkpoint");
            }

            isActive = true;
        }
        else
        {
            isActive = index is not null
                && index.ActiveRunId is not null
                && RunStoreIds.TryCanonicalize(index.ActiveRunId, out var indexRunId, out _)
                && string.Equals(indexRunId, canonicalRunId, StringComparison.Ordinal)
                && string.Equals(index.CheckpointHash, current.Hash, StringComparison.Ordinal);

            if (index is not null
                && index.ActiveRunId is not null
                && RunStoreIds.TryCanonicalize(index.ActiveRunId, out var activeRunId, out _)
                && string.Equals(activeRunId, canonicalRunId, StringComparison.Ordinal)
                && !isActive)
            {
                return RecoveryForIndexMismatch(
                    canonicalRunId,
                    runDirectory,
                    index,
                    current.Hash,
                    "active index checkpoint hash does not match the current checkpoint");
            }
        }

        var blueprintIssue = VerifyBlueprints(runDirectory, envelope);
        if (blueprintIssue is not null)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[] { blueprintIssue! },
                new RunRecoveryInfo(
                    canonicalRunId,
                    index?.CheckpointHash,
                    current.Hash,
                    InspectPreviousCheckpoint(runDirectory),
                    "committed blueprint bytes are missing or do not match their hash"),
                isActive,
                current.Hash);
        }

        if (IsTerminal(snapshot.Phase))
        {
            return new RunLoadResult(
                RunStoreLoadStatus.CannotResumeTerminal,
                canonicalRunId,
                snapshot,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.RunTerminal,
                        $"Run phase is {snapshot.Phase}; a recorded Loss/Finish cannot resume.")
                },
                null,
                isActive,
                current.Hash);
        }

        var modIssues = CheckMods(snapshot, availability);
        if (modIssues.Count > 0)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.ResumeBlocked,
                canonicalRunId,
                snapshot,
                modIssues,
                null,
                isActive,
                current.Hash);
        }

        return new RunLoadResult(
            RunStoreLoadStatus.Loaded,
            canonicalRunId,
            snapshot,
            Array.Empty<RunStoreIssue>(),
            null,
            isActive,
            current.Hash);
    }

    private RunLoadResult RecoverCore(
        string canonicalRunId,
        RunCheckpointSelection selection,
        ModAvailability availability)
    {
        var runDirectory = GetRunDirectory(canonicalRunId);
        if (!Directory.Exists(runDirectory))
        {
            return Error(
                canonicalRunId,
                RunStoreIssueCodes.CheckpointMissing,
                $"Run '{canonicalRunId}' has no stored checkpoint folder.");
        }

        var currentPath = RunStoreNames.GetCheckpointPath(runDirectory);
        var current = ReadCheckpointSlot(currentPath);

        // A newer schema is never silently rolled back or reinterpreted, even when the
        // caller selects the older backup.
        if (current.SchemaUnsupported)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.SchemaUnsupported,
                canonicalRunId,
                null,
                new[] { current.Issue! },
                null,
                false,
                current.Hash);
        }

        var selectedPath = selection == RunCheckpointSelection.Current
            ? currentPath
            : RunStoreNames.GetPreviousCheckpointPath(runDirectory);
        var selected = selection == RunCheckpointSelection.Current
            ? current
            : ReadCheckpointSlot(selectedPath);

        if (!selected.Exists)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.CheckpointMissing,
                        $"The selected {selection} checkpoint does not exist.")
                },
                new RunRecoveryInfo(
                    canonicalRunId,
                    null,
                    current.Hash,
                    InspectPreviousCheckpoint(runDirectory),
                    "selected checkpoint missing"),
                false,
                null);
        }

        if (selected.SchemaUnsupported)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.SchemaUnsupported,
                canonicalRunId,
                null,
                new[] { selected.Issue! },
                null,
                false,
                selected.Hash);
        }

        if (selected.Issue is not null)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[] { selected.Issue! },
                new RunRecoveryInfo(
                    canonicalRunId,
                    null,
                    current.Hash,
                    InspectPreviousCheckpoint(runDirectory),
                    "selected checkpoint failed verification"),
                false,
                selected.Hash);
        }

        var envelope = selected.Envelope!;
        var blueprintIssue = VerifyBlueprints(runDirectory, envelope);
        if (blueprintIssue is not null)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[] { blueprintIssue! },
                new RunRecoveryInfo(
                    canonicalRunId,
                    null,
                    current.Hash,
                    InspectPreviousCheckpoint(runDirectory),
                    "selected checkpoint blueprint verification failed"),
                false,
                selected.Hash);
        }

        if (!RunStoreIds.TryCanonicalize(envelope.RunId, out var envelopeRunId, out _)
            || !string.Equals(envelopeRunId, canonicalRunId, StringComparison.Ordinal))
        {
            return new RunLoadResult(
                RunStoreLoadStatus.RecoveryRequired,
                canonicalRunId,
                null,
                new[]
                {
                    new RunStoreIssue(
                        RunStoreIssueCodes.CheckpointCorrupt,
                        "Selected checkpoint runId does not match its run directory.")
                },
                new RunRecoveryInfo(
                    canonicalRunId,
                    null,
                    current.Hash,
                    selected.Hash,
                    "selected checkpoint run identity mismatch"),
                false,
                selected.Hash);
        }

        var snapshot = envelope.Snapshot;
        var terminal = IsTerminal(snapshot.Phase);

        // Missing content blocks recovery before any index write. Terminal runs are
        // read for viewing/export and are not resumed.
        if (!terminal)
        {
            var modIssues = CheckMods(snapshot, availability);
            if (modIssues.Count > 0)
            {
                return new RunLoadResult(
                    RunStoreLoadStatus.ResumeBlocked,
                    canonicalRunId,
                    snapshot,
                    modIssues,
                    null,
                    false,
                    selected.Hash);
            }
        }

        var indexSlot = ReadIndex();
        if (indexSlot.SchemaUnsupported)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.SchemaUnsupported,
                canonicalRunId,
                null,
                new[] { indexSlot.Issue! },
                null,
                false,
                selected.Hash);
        }

        RunStoreIssue? issuesWarning = null;
        try
        {
            // Selecting the backup restores it as the active checkpoint. The bytes of a
            // still-valid superseded checkpoint are retained in the backup slot when
            // possible; unsupported-future bytes were rejected above and never touched.
            if (selection == RunCheckpointSelection.Previous)
            {
                var restoreIssue = RestorePreviousCheckpoint(
                    runDirectory,
                    current,
                    selected,
                    out var preserveIssue);
                if (restoreIssue is not null)
                {
                    return Error(
                        canonicalRunId,
                        RunStoreIssueCodes.IoFailure,
                        restoreIssue);
                }

                if (preserveIssue is not null)
                {
                    issuesWarning = preserveIssue;
                }
            }

            WriteIndex(new RunStoreIndex(
                terminal ? null : canonicalRunId,
                selected.Hash!,
                envelope.Revision));
        }
        catch (IOException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Error(canonicalRunId, RunStoreIssueCodes.IoFailure, exception.Message);
        }

        var recoveryIssues = new List<RunStoreIssue>();
        if (issuesWarning is not null)
        {
            recoveryIssues.Add(issuesWarning);
        }

        if (terminal)
        {
            var recordIssue = TryEnsureTerminalRecord(
                runDirectory,
                canonicalRunId,
                envelope,
                selected.Hash!);
            if (recordIssue is not null)
            {
                recoveryIssues.Add(recordIssue);
            }
        }

        if (terminal)
        {
            return new RunLoadResult(
                RunStoreLoadStatus.CannotResumeTerminal,
                canonicalRunId,
                snapshot,
                recoveryIssues,
                null,
                false,
                selected.Hash);
        }

        return new RunLoadResult(
            RunStoreLoadStatus.Loaded,
            canonicalRunId,
            snapshot,
            recoveryIssues,
            null,
            true,
            selected.Hash);
    }

    // Restores the verified backup into checkpoint.json. Ordering: restore current
    // first, then best-effort retain the superseded valid checkpoint, then the caller
    // updates the index. Any crash before the index update is still visible as an
    // index/checkpoint mismatch and can be recovered explicitly.
    private static string? RestorePreviousCheckpoint(
        string runDirectory,
        CheckpointSlot current,
        CheckpointSlot selected,
        out RunStoreIssue? preserveIssue)
    {
        preserveIssue = null;
        var selectedBytes = selected.Bytes
            ?? throw new IOException("Selected backup checkpoint bytes are unavailable.");
        var currentPath = RunStoreNames.GetCheckpointPath(runDirectory);
        var previousPath = RunStoreNames.GetPreviousCheckpointPath(runDirectory);

        var restoreTemp = AtomicFile.NewTempPath(runDirectory);
        try
        {
            AtomicFile.WriteDurable(restoreTemp, selectedBytes);
            var written = File.ReadAllBytes(restoreTemp);
            if (!string.Equals(
                    Sha256Hex.Compute(written),
                    selected.Hash,
                    StringComparison.Ordinal))
            {
                AtomicFile.TryDelete(restoreTemp);
                return "Staged restore checkpoint failed hash verification.";
            }

            AtomicFile.MoveReplace(restoreTemp, currentPath);
        }
        catch
        {
            AtomicFile.TryDelete(restoreTemp);
            throw;
        }

        if (current.Envelope is not null
            && current.Bytes is not null
            && !current.Bytes.AsSpan().SequenceEqual(selectedBytes))
        {
            var preserveTemp = AtomicFile.NewTempPath(runDirectory);
            try
            {
                AtomicFile.WriteDurable(preserveTemp, current.Bytes);
                AtomicFile.MoveReplace(preserveTemp, previousPath);
            }
            catch (IOException exception)
            {
                AtomicFile.TryDelete(preserveTemp);
                preserveIssue = new RunStoreIssue(
                    RunStoreIssueCodes.IoFailure,
                    "Recovered the selected checkpoint, but could not retain the "
                    + "superseded checkpoint: " + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                AtomicFile.TryDelete(preserveTemp);
                preserveIssue = new RunStoreIssue(
                    RunStoreIssueCodes.IoFailure,
                    "Recovered the selected checkpoint, but could not retain the "
                    + "superseded checkpoint: " + exception.Message);
            }
        }

        return null;
    }

    private RunLoadResult RecoveryForIndexMismatch(
        string canonicalRunId,
        string runDirectory,
        RunStoreIndex? index,
        string? currentHash,
        string reason)
    {
        var previousHash = InspectPreviousCheckpoint(runDirectory);
        return new RunLoadResult(
            RunStoreLoadStatus.RecoveryRequired,
            canonicalRunId,
            null,
            new[]
            {
                new RunStoreIssue(
                    RunStoreIssueCodes.RecoveryRequired,
                    reason + ". The checkpoint and active index are not a committed pair; "
                    + "choose Current or Previous explicitly.")
            },
            new RunRecoveryInfo(
                canonicalRunId,
                index?.CheckpointHash,
                currentHash,
                previousHash,
                reason),
            false,
            currentHash);
    }

    private RunStoreIssue? TryRepairIndexForExistingCheckpoint(
        string canonicalRunId,
        RunStoreIndex? index,
        RunCheckpointEnvelope envelope,
        string existingHash)
    {
        var terminal = IsTerminal(envelope.Snapshot.Phase);
        if (index is not null && index.ActiveRunId is not null)
        {
            if (!RunStoreIds.TryCanonicalize(index.ActiveRunId, out var activeRunId, out _)
                || !string.Equals(activeRunId, canonicalRunId, StringComparison.Ordinal))
            {
                // Another run is active; an inert duplicate must not disturb it.
                return null;
            }
        }

        var needsRepair = index is null
            || (terminal
                ? index.ActiveRunId is not null
                : index.ActiveRunId is null
                  || !string.Equals(
                      index.CheckpointHash,
                      existingHash,
                      StringComparison.Ordinal));
        if (!needsRepair)
        {
            return null;
        }

        try
        {
            WriteIndex(terminal
                ? new RunStoreIndex(null, null, envelope.Revision)
                : new RunStoreIndex(canonicalRunId, existingHash, envelope.Revision));
            return null;
        }
        catch (IOException exception)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                "The checkpoint is already durable but the active-run index could not be "
                + "repaired: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                "The checkpoint is already durable but the active-run index could not be "
                + "repaired: " + exception.Message);
        }
    }

    private RunStoreIssue? CheckIndexAllowsWrite(
        string canonicalRunId,
        RunStoreIndex? index,
        string? existingHash,
        bool hasExistingCheckpoint,
        string runDirectory,
        CheckpointSlot previous,
        string recordPath)
    {
        if (hasExistingCheckpoint)
        {
            if (index is null)
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.RecoveryRequired,
                    "The active-run index is missing. Run explicit recovery before saving.");
            }

            if (index.ActiveRunId is null)
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.RecoveryRequired,
                    "The active-run index is cleared while a checkpoint exists. Run explicit "
                    + "recovery before saving.");
            }

            if (!RunStoreIds.TryCanonicalize(index.ActiveRunId, out var activeRunId, out _)
                || !string.Equals(activeRunId, canonicalRunId, StringComparison.Ordinal))
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.ActiveRunExists,
                    $"Another run ('{index.ActiveRunId}') is active. Starting another run "
                    + "requires an explicit abandon decision that is not part of v1 storage.");
            }

            if (!string.Equals(index.CheckpointHash, existingHash, StringComparison.Ordinal))
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.RecoveryRequired,
                    "The active index checkpoint hash does not match the current checkpoint. "
                    + "Run explicit recovery before saving.");
            }

            return null;
        }

        var runDirectoryExists = Directory.Exists(runDirectory);
        if (index is not null && index.ActiveRunId is not null)
        {
            if (!RunStoreIds.TryCanonicalize(index.ActiveRunId, out var activeRunId, out _)
                || !string.Equals(activeRunId, canonicalRunId, StringComparison.Ordinal))
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.ActiveRunExists,
                    $"Another run ('{index.ActiveRunId}') is active. NewRun cannot overwrite "
                    + "a live or suspended run.");
            }

            // The index already names this run but there is no current checkpoint:
            // recovering/adopting that state is explicit, never an implicit first save.
            return new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                runDirectoryExists
                    ? "The active index references this run but no current checkpoint exists. "
                      + "Run explicit recovery before saving."
                    : "The active index references this run but its folder is missing. "
                      + "Run explicit recovery before saving.");
        }

        if (runDirectoryExists
            && (previous.Exists || File.Exists(recordPath))
            && (index is null || index.ActiveRunId is null))
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.RecoveryRequired,
                "A run folder exists without a current checkpoint/index pair. No run was "
                + "started; run explicit recovery.");
        }

        return null;
    }

    private static RunStoreIssue? CheckRunIdentity(
        RunSnapshot existing,
        RunSnapshot candidate)
    {
        if (!string.Equals(existing.RunId, candidate.RunId, StringComparison.Ordinal)
            || existing.Seed != candidate.Seed
            || existing.StartingEraIndex != candidate.StartingEraIndex
            || existing.TestPool != candidate.TestPool
            || existing.Ironman != candidate.Ironman
            || !SameValue(existing.InitialBaseBudget, candidate.InitialBaseBudget)
            || !string.Equals(existing.CostUnitId, candidate.CostUnitId, StringComparison.Ordinal))
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.RunIdentityChanged,
                "Run id, seed, initial base budget and cost unit are immutable for a run.");
        }

        if (existing.EraSchedule.Count != candidate.EraSchedule.Count)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.RunIdentityChanged,
                "The era schedule is immutable for a run.");
        }

        for (var index = 0; index < existing.EraSchedule.Count; index++)
        {
            if (!string.Equals(
                    existing.EraSchedule[index].NativeId,
                    candidate.EraSchedule[index].NativeId,
                    StringComparison.Ordinal))
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.RunIdentityChanged,
                    "The era schedule is immutable for a run.");
            }
        }

        return null;
    }

    private static RunStoreIssue? BuildManifest(
        string runDirectory,
        string? designHash,
        IReadOnlyDictionary<string, byte[]> supplied,
        out List<RunBlueprintEntry> manifest)
    {
        manifest = new List<RunBlueprintEntry>();

        foreach (var pair in supplied.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var path = RunStoreNames.GetBlueprintPath(runDirectory, pair.Key);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Length > RunStoreNames.MaxBlueprintBytes)
                {
                    return new RunStoreIssue(
                        RunStoreIssueCodes.BlueprintTooLarge,
                        $"Stored blueprint '{pair.Key}' exceeds the storage size limit.");
                }

                var bytes = File.ReadAllBytes(path);
                if (!string.Equals(
                        Sha256Hex.Compute(bytes),
                        pair.Key,
                        StringComparison.Ordinal))
                {
                    return new RunStoreIssue(
                        RunStoreIssueCodes.BlueprintConflict,
                        $"Stored blueprint '{pair.Key}' does not match its hash and will not "
                        + "be overwritten.");
                }

                manifest.Add(new RunBlueprintEntry(pair.Key, bytes.LongLength));
            }
            else
            {
                manifest.Add(new RunBlueprintEntry(pair.Key, pair.Value.LongLength));
            }
        }

        if (designHash is not null && !supplied.ContainsKey(designHash))
        {
            var issue = VerifyBlueprintFile(runDirectory, designHash, out var length);
            if (issue is not null)
            {
                return issue;
            }

            manifest.Add(new RunBlueprintEntry(designHash, length));
        }

        manifest.Sort((left, right) =>
            string.CompareOrdinal(left.Hash, right.Hash));
        return null;
    }

    private static bool TryValidateSuppliedBlueprints(
        IReadOnlyList<BlueprintSnapshot> blueprints,
        out Dictionary<string, byte[]> supplied,
        out RunStoreIssue? issue)
    {
        supplied = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        issue = null;

        foreach (var blueprint in blueprints)
        {
            if (blueprint is null)
            {
                issue = new RunStoreIssue(
                    RunStoreIssueCodes.InvalidSnapshot,
                    "Blueprint list contains null.");
                return false;
            }

            if (!RunStoreIds.TryNormalizeHash(
                    blueprint.SnapshotHash,
                    out var hash,
                    out var error))
            {
                issue = new RunStoreIssue(RunStoreIssueCodes.InvalidBlueprintHash, error!);
                return false;
            }

            if (blueprint.Bytes is null)
            {
                issue = new RunStoreIssue(
                    RunStoreIssueCodes.InvalidSnapshot,
                    $"Blueprint '{hash}' has null bytes.");
                return false;
            }

            if (blueprint.Bytes.LongLength > RunStoreNames.MaxBlueprintBytes)
            {
                issue = new RunStoreIssue(
                    RunStoreIssueCodes.BlueprintTooLarge,
                    $"Blueprint '{hash}' exceeds the storage size limit.");
                return false;
            }

            var computedHash = Sha256Hex.Compute(blueprint.Bytes);
            if (!string.Equals(computedHash, hash, StringComparison.Ordinal))
            {
                issue = new RunStoreIssue(
                    RunStoreIssueCodes.BlueprintHashMismatch,
                    $"Supplied blueprint hash '{hash}' does not match the SHA-256 of its "
                    + $"bytes ('{computedHash}').");
                return false;
            }

            if (supplied.TryGetValue(hash, out var existing))
            {
                if (!existing.AsSpan().SequenceEqual(blueprint.Bytes))
                {
                    issue = new RunStoreIssue(
                        RunStoreIssueCodes.BlueprintConflict,
                        $"Blueprint '{hash}' was supplied twice with different bytes.");
                    return false;
                }

                continue;
            }

            supplied.Add(hash, (byte[])blueprint.Bytes.Clone());
        }

        return true;
    }

    private static IReadOnlyList<RunStoreIssue> CheckMods(
        RunSnapshot snapshot,
        ModAvailability availability)
    {
        var issues = new List<RunStoreIssue>();
        foreach (var required in CollectRequiredMods(snapshot))
        {
            if (!availability.TryGetVersion(required.Id, out var installedVersion))
            {
                issues.Add(new RunStoreIssue(
                    RunStoreIssueCodes.ModMissing,
                    $"Required mod '{required.Id}' version '{required.Version}' is not "
                    + "installed. Resume is blocked; stored data is untouched."));
                continue;
            }

            if (!string.Equals(installedVersion, required.Version, StringComparison.Ordinal))
            {
                issues.Add(new RunStoreIssue(
                    RunStoreIssueCodes.ModVersionMismatch,
                    $"Required mod '{required.Id}' version '{required.Version}' does not "
                    + $"match installed version '{installedVersion}'. Resume is blocked; "
                    + "stored data is untouched."));
            }
        }

        return issues.AsReadOnly();
    }

    private static IReadOnlyList<ModRequirement> CollectRequiredMods(RunSnapshot snapshot)
    {
        var seen = new HashSet<(string Id, string Version)>();
        var required = new List<ModRequirement>();

        void Add(IReadOnlyList<ModRequirement> mods)
        {
            foreach (var mod in mods)
            {
                if (seen.Add((mod.Id, mod.Version)))
                {
                    required.Add(mod);
                }
            }
        }

        if (snapshot.ConfirmedDesign is not null)
        {
            Add(snapshot.ConfirmedDesign.Blueprint.RequiredMods);
        }

        foreach (var offer in snapshot.Offers)
        {
            Add(offer.RequiredMods);
        }

        if (snapshot.LockedPlan is not null)
        {
            Add(snapshot.LockedPlan.RequiredMods);
        }

        required.Sort((left, right) =>
        {
            var idComparison = string.CompareOrdinal(left.Id, right.Id);
            return idComparison != 0
                ? idComparison
                : string.CompareOrdinal(left.Version, right.Version);
        });
        return required.AsReadOnly();
    }

    private static RunStoreIssue? VerifyBlueprints(
        string runDirectory,
        RunCheckpointEnvelope envelope)
    {
        foreach (var entry in envelope.Blueprints)
        {
            var issue = VerifyBlueprintFile(runDirectory, entry.Hash, out var length);
            if (issue is not null)
            {
                return issue;
            }

            if (length != entry.Length)
            {
                return new RunStoreIssue(
                    RunStoreIssueCodes.BlueprintHashMismatch,
                    $"Blueprint '{entry.Hash}' length does not match the committed manifest.");
            }
        }

        return null;
    }

    private static RunStoreIssue? VerifyBlueprintFile(
        string runDirectory,
        string hash,
        out long length)
    {
        length = 0;
        var path = RunStoreNames.GetBlueprintPath(runDirectory, hash);
        if (!File.Exists(path))
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.BlueprintMissing,
                $"Committed blueprint '{hash}' is missing.");
        }

        var info = new FileInfo(path);
        if (info.Length > RunStoreNames.MaxBlueprintBytes)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.BlueprintTooLarge,
                $"Committed blueprint '{hash}' exceeds the storage size limit.");
        }

        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(Sha256Hex.Compute(bytes), hash, StringComparison.Ordinal))
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.BlueprintHashMismatch,
                $"Committed blueprint '{hash}' does not match its stored bytes.");
        }

        length = bytes.LongLength;
        return null;
    }

    private string? InspectPreviousCheckpoint(string runDirectory)
    {
        var previous = ReadCheckpointSlot(
            RunStoreNames.GetPreviousCheckpointPath(runDirectory));
        if (!previous.Exists || previous.Issue is not null || previous.Envelope is null)
        {
            return null;
        }

        return VerifyBlueprints(runDirectory, previous.Envelope) is null
            ? previous.Hash
            : null;
    }

    private CheckpointSlot ReadCheckpointSlot(string path)
    {
        var fullPath = EnsureWithinRoot(path);
        if (!File.Exists(fullPath))
        {
            return CheckpointSlot.Missing;
        }

        var info = new FileInfo(fullPath);
        if (info.Length > RunStoreNames.MaxCheckpointBytes)
        {
            return new CheckpointSlot(
                true,
                null,
                null,
                null,
                new RunStoreIssue(
                    RunStoreIssueCodes.CheckpointTooLarge,
                    "Checkpoint exceeds the storage size limit."),
                false);
        }

        var bytes = File.ReadAllBytes(fullPath);
        var hash = Sha256Hex.Compute(bytes);
        try
        {
            var envelope = RunStoreFormats.DecodeCheckpoint(bytes);
            return new CheckpointSlot(true, envelope, bytes, hash, null, false);
        }
        catch (RunStoreFormatException exception)
        {
            return new CheckpointSlot(
                true,
                null,
                bytes,
                hash,
                new RunStoreIssue(exception.Code, exception.Message),
                exception.SchemaUnsupported);
        }
    }

    private IndexSlot ReadIndex()
    {
        var path = ActiveIndexPath;
        if (!File.Exists(path))
        {
            return IndexSlot.Missing;
        }

        var info = new FileInfo(path);
        if (info.Length > RunStoreNames.MaxCheckpointBytes)
        {
            return new IndexSlot(
                true,
                null,
                new RunStoreIssue(
                    RunStoreIssueCodes.IndexCorrupt,
                    "Active index exceeds the storage size limit."),
                false);
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new IndexSlot(true, RunStoreFormats.DecodeIndex(bytes), null, false);
        }
        catch (RunStoreFormatException exception)
        {
            return new IndexSlot(
                true,
                null,
                new RunStoreIssue(exception.Code, exception.Message),
                exception.SchemaUnsupported);
        }
    }

    private void WriteIndex(RunStoreIndex index)
    {
        Directory.CreateDirectory(_root);
        var path = ActiveIndexPath;
        var tempPath = AtomicFile.NewTempPath(_root);
        try
        {
            var bytes = RunStoreFormats.EncodeIndex(index);
            AtomicFile.WriteDurable(tempPath, bytes);
            var written = File.ReadAllBytes(tempPath);
            if (!string.Equals(
                    Sha256Hex.Compute(written),
                    Sha256Hex.Compute(bytes),
                    StringComparison.Ordinal))
            {
                throw new IOException("Staged active index failed verification.");
            }

            AtomicFile.MoveReplace(tempPath, path);
        }
        finally
        {
            AtomicFile.TryDelete(tempPath);
        }
    }

    private static void WriteRecordFile(string runDirectory, RunRecord record)
    {
        var path = RunStoreNames.GetRecordPath(runDirectory);
        var tempPath = AtomicFile.NewTempPath(runDirectory);
        try
        {
            AtomicFile.WriteDurable(tempPath, RunStoreFormats.EncodeRecord(record));
            AtomicFile.MoveReplace(tempPath, path);
        }
        finally
        {
            AtomicFile.TryDelete(tempPath);
        }
    }

    private static RunStoreIssue? TryEnsureTerminalRecord(
        string runDirectory,
        string canonicalRunId,
        RunCheckpointEnvelope envelope,
        string checkpointHash)
    {
        if (File.Exists(RunStoreNames.GetRecordPath(runDirectory)))
        {
            return null;
        }

        try
        {
            WriteRecordFile(
                runDirectory,
                BuildRecord(
                    canonicalRunId,
                    envelope.Snapshot,
                    envelope.Revision,
                    checkpointHash));
            return null;
        }
        catch (IOException exception)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.IoFailure,
                "Recovered the terminal checkpoint but could not write its record: "
                + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new RunStoreIssue(
                RunStoreIssueCodes.IoFailure,
                "Recovered the terminal checkpoint but could not write its record: "
                + exception.Message);
        }
    }

    private static RunRecord BuildRecord(
        string canonicalRunId,
        RunSnapshot snapshot,
        long revision,
        string checkpointHash) =>
        new(
            RunStoreNames.SchemaVersion,
            canonicalRunId,
            snapshot.Seed,
            snapshot.Phase,
            snapshot.Round,
            snapshot.BlockIndex,
            snapshot.Victories,
            snapshot.Defeats,
            snapshot.LastOutcome,
            snapshot.LastOutcomeDetail,
            snapshot.ConfirmedDesign,
            revision,
            checkpointHash)
        {
            EnemiesDestroyed = snapshot.EnemiesDestroyed,
            FriendliesLost = snapshot.FriendliesLost,
            Attempts = snapshot.Attempts, RoundAttempts = snapshot.RoundAttempts,
            TestPool = snapshot.TestPool, Ironman = snapshot.Ironman, Score = snapshot.Score,
            Era = snapshot.SchemaVersion >= 6 ? snapshot.EraSchedule[snapshot.EraIndex] : null
        };

    private List<string> EnumerateRunIds()
    {
        var runIds = new List<string>();
        var runsDirectory = RunStoreNames.GetRunsDirectory(_root);
        if (!Directory.Exists(runsDirectory))
        {
            return runIds;
        }

        foreach (var directory in Directory.GetDirectories(runsDirectory))
        {
            var name = Path.GetFileName(directory);
            if (RunStoreIds.TryCanonicalize(name, out var canonicalRunId, out _)
                && string.Equals(canonicalRunId, name, StringComparison.Ordinal))
            {
                runIds.Add(canonicalRunId);
            }
        }

        runIds.Sort(StringComparer.Ordinal);
        return runIds;
    }

    // Used only when the active index does not name a run (missing or cleared). A run
    // is considered resolved only when a readable checkpoint/backup proves Lost or
    // Finished; anything unreadable or non-terminal must be recovered explicitly.
    private bool TryFindUnresolvedRun(out string? unresolvedRunId)
    {
        foreach (var candidateRunId in EnumerateRunIds())
        {
            var runDirectory = GetRunDirectory(candidateRunId);
            var current = ReadCheckpointSlot(
                RunStoreNames.GetCheckpointPath(runDirectory));
            if (current.SchemaUnsupported)
            {
                unresolvedRunId = candidateRunId;
                return true;
            }

            if (current.Envelope is not null)
            {
                if (!IsTerminal(current.Envelope.Snapshot.Phase))
                {
                    unresolvedRunId = candidateRunId;
                    return true;
                }

                continue;
            }

            var previous = ReadCheckpointSlot(
                RunStoreNames.GetPreviousCheckpointPath(runDirectory));
            if (previous.SchemaUnsupported)
            {
                unresolvedRunId = candidateRunId;
                return true;
            }

            if (previous.Envelope is not null)
            {
                if (!IsTerminal(previous.Envelope.Snapshot.Phase))
                {
                    unresolvedRunId = candidateRunId;
                    return true;
                }

                continue;
            }

            // Neither slot is readable: terminal state cannot be proven.
            unresolvedRunId = candidateRunId;
            return true;
        }

        unresolvedRunId = null;
        return false;
    }

    private byte[] ReadBounded(string path, long maxBytes)
    {
        var fullPath = EnsureWithinRoot(path);
        var info = new FileInfo(fullPath);
        if (info.Length > maxBytes)
        {
            throw new RunStoreFormatException(
                "File exceeds the storage size limit.",
                RunStoreIssueCodes.CheckpointTooLarge);
        }

        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.LongLength > maxBytes)
        {
            throw new RunStoreFormatException(
                "File exceeds the storage size limit.",
                RunStoreIssueCodes.CheckpointTooLarge);
        }

        return bytes;
    }

    private string GetRunDirectory(string canonicalRunId) =>
        EnsureWithinRoot(RunStoreNames.GetRunDirectory(_root, canonicalRunId));

    private string EnsureWithinRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(fullPath, _root, comparison)
            && !fullPath.StartsWith(_rootWithSeparator, comparison))
        {
            throw new InvalidOperationException(
                "Resolved path escapes the configured run storage root.");
        }

        return fullPath;
    }

    private static bool IsTerminal(RunPhase phase) =>
        phase is RunPhase.Lost or RunPhase.Finished or RunPhase.Retired;

    private static bool SameAttempt(AttemptIdentity left, AttemptIdentity right) =>
        string.Equals(left.RunId, right.RunId, StringComparison.Ordinal)
        && string.Equals(left.EncounterId, right.EncounterId, StringComparison.Ordinal)
        && string.Equals(left.PlanHash, right.PlanHash, StringComparison.Ordinal)
        && left.Generation == right.Generation;

    private static bool SameValue(ScenarioValue left, ScenarioValue right) =>
        left.Value == right.Value
        && string.Equals(left.UnitId, right.UnitId, StringComparison.Ordinal);

    private static bool IsExactSnapshotDuplicate(
        byte[] existingCheckpointBytes,
        RunSnapshot existingSnapshot,
        RunSnapshot candidate) =>
        RunStoreFormats.StoredSnapshotEquals(existingCheckpointBytes, candidate)
        || RunStoreFormats.SnapshotsEqual(existingSnapshot, candidate);

    private static RunStoreWriteResult Rejected(
        string? runId,
        long revision,
        string code,
        string message) =>
        new(
            RunStoreWriteStatus.Rejected,
            runId,
            revision,
            null,
            false,
            new[] { new RunStoreIssue(code, message) });

    private static RunStoreWriteResult Failed(
        string? runId,
        long revision,
        string code,
        string message,
        bool checkpointCommitted) =>
        new(
            RunStoreWriteStatus.Failed,
            runId,
            revision,
            null,
            checkpointCommitted,
            new[] { new RunStoreIssue(code, message) });

    private static RunLoadResult Error(string? runId, string code, string message) =>
        new(
            RunStoreLoadStatus.Error,
            runId,
            null,
            new[] { new RunStoreIssue(code, message) },
            null,
            false,
            null);

    private sealed record CheckpointSlot(
        bool Exists,
        RunCheckpointEnvelope? Envelope,
        byte[]? Bytes,
        string? Hash,
        RunStoreIssue? Issue,
        bool SchemaUnsupported)
    {
        public static CheckpointSlot Missing { get; } =
            new(false, null, null, null, null, false);
    }

    private sealed record IndexSlot(
        bool Exists,
        RunStoreIndex? Index,
        RunStoreIssue? Issue,
        bool SchemaUnsupported)
    {
        public static IndexSlot Missing { get; } = new(false, null, null, false);
    }
}
