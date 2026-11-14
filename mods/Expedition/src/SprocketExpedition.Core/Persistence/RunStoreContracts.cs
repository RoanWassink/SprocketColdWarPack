using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence;

// Public storage result surface. Storage never reports corruption as an empty new
// run: every failure carries an explicit status and issue list.
public enum RunStoreLoadStatus
{
    // The active run was verified and may be resumed by the caller.
    Loaded,

    // No active run is registered. Existing run folders are not treated as new runs.
    NoActiveRun,

    // A recorded Lost/Finished run was read for viewing/export. It cannot resume.
    CannotResumeTerminal,

    // Missing/mismatched mods (or caller-declared content) block resume. No files were
    // written or deleted; the snapshot is exposed for diagnostics only.
    ResumeBlocked,

    // The checkpoint/index transaction is incomplete or a checkpoint failed
    // verification. The caller must select an earlier/current checkpoint explicitly.
    RecoveryRequired,

    // The stored schema is not v1. Bytes are preserved unchanged and are never
    // reinterpreted. No fallback occurs implicitly.
    SchemaUnsupported,

    // I/O, malformed index or an explicit lookup of a missing run.
    Error
}

public static class RunStoreIssueCodes
{
    public const string InvalidRoot = "invalid-root";
    public const string InvalidRunId = "invalid-run-id";
    public const string InvalidSnapshot = "invalid-snapshot";
    public const string InvalidBlueprintHash = "invalid-blueprint-hash";
    public const string BlueprintHashMismatch = "blueprint-hash-mismatch";
    public const string BlueprintMissing = "blueprint-missing";
    public const string BlueprintConflict = "blueprint-conflict";
    public const string BlueprintTooLarge = "blueprint-too-large";
    public const string CheckpointCorrupt = "checkpoint-corrupt";
    public const string CheckpointMissing = "checkpoint-missing";
    public const string CheckpointTooLarge = "checkpoint-too-large";
    public const string CheckpointHashMismatch = "checkpoint-hash-mismatch";
    public const string PreviousCheckpointCorrupt = "previous-checkpoint-corrupt";
    public const string IndexMissing = "index-missing";
    public const string IndexCorrupt = "index-corrupt";
    public const string IndexRunMissing = "index-run-missing";
    public const string ActiveRunExists = "active-run-exists";
    public const string RecoveryRequired = "recovery-required";
    public const string SchemaUnsupported = "schema-unsupported";
    public const string RunTerminal = "run-terminal";
    public const string TerminalImmutable = "terminal-immutable";
    public const string RunIdentityChanged = "run-identity-changed";
    public const string OlderGeneration = "older-generation";
    public const string RemovesProcessedAttempt = "removes-processed-attempt";
    public const string RemovesConsumedOffer = "removes-consumed-offer";
    public const string StaleRevision = "stale-revision";
    public const string ModMissing = "mod-missing";
    public const string ModVersionMismatch = "mod-version-mismatch";
    public const string RecordMissing = "record-missing";
    public const string RecordCorrupt = "record-corrupt";
    public const string IoFailure = "io-failure";
    public const string UnrecognizedRunDirectory = "unrecognized-run-directory";
}

public sealed record RunStoreIssue(string Code, string Message);

public sealed record RunRecoveryInfo(
    string RunId,
    string? IndexedCheckpointHash,
    string? CurrentCheckpointHash,
    string? PreviousCheckpointHash,
    string Reason);

public sealed record RunLoadResult(
    RunStoreLoadStatus Status,
    string? RunId,
    RunSnapshot? Snapshot,
    IReadOnlyList<RunStoreIssue> Issues,
    RunRecoveryInfo? Recovery,
    bool IsActiveRun,
    string? CheckpointHash)
{
    // Only a verified checkpoint of the active run is resumable. A terminal run is
    // reported as CannotResumeTerminal even though its snapshot is returned.
    public bool CanResume => Status == RunStoreLoadStatus.Loaded && IsActiveRun;
}

public enum RunCheckpointSelection
{
    Current,
    Previous
}

public enum RunStoreWriteStatus
{
    Committed,
    Unchanged,
    Rejected,
    Failed
}

public sealed record RunStoreWriteResult(
    RunStoreWriteStatus Status,
    string? RunId,
    long Revision,
    string? CheckpointHash,
    bool CheckpointCommitted,
    IReadOnlyList<RunStoreIssue> Issues)
{
    public bool IsCommitted =>
        Status == RunStoreWriteStatus.Committed || Status == RunStoreWriteStatus.Unchanged;
}

// Caller-supplied native serialization output. SnapshotHash must be the lowercase
// or uppercase hex SHA-256 of exactly these bytes.
public sealed record BlueprintSnapshot(string SnapshotHash, byte[] Bytes);

// Caller-supplied availability of locally installed mods. Matching is ordinal for
// both id and version; storage does not guess, normalize or assume semantics.
public sealed class ModAvailability
{
    private readonly Dictionary<string, string> _versions;
    private readonly IReadOnlyList<ModRequirement> _installed;

    public ModAvailability(IEnumerable<ModRequirement> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        var list = new List<ModRequirement>();
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mod in installed)
        {
            if (mod is null)
            {
                throw new ArgumentException("Installed mod list contains null.", nameof(installed));
            }

            if (string.IsNullOrEmpty(mod.Id))
            {
                throw new ArgumentException("Installed mod id is missing.", nameof(installed));
            }

            if (mod.Version is null)
            {
                throw new ArgumentException(
                    $"Installed mod '{mod.Id}' has a null version.", nameof(installed));
            }

            if (!versions.TryAdd(mod.Id, mod.Version))
            {
                throw new ArgumentException(
                    $"Duplicate installed mod id '{mod.Id}'.", nameof(installed));
            }

            list.Add(mod);
        }

        _versions = versions;
        _installed = list.AsReadOnly();
    }

    public static ModAvailability None { get; } = new(Array.Empty<ModRequirement>());

    public IReadOnlyList<ModRequirement> Installed => _installed;

    public bool TryGetVersion(string id, out string? version)
    {
        if (_versions.TryGetValue(id, out var installedVersion))
        {
            version = installedVersion;
            return true;
        }

        version = null;
        return false;
    }
}

public sealed record RunSummary(
    string RunId,
    int SchemaVersion,
    RunPhase? Phase,
    int Round,
    int Victories,
    int Defeats,
    bool IsTerminal,
    string? CheckpointHash)
{
    public decimal Score { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
    public EraReference? Era { get; init; }
}

public sealed record RunListResult(
    IReadOnlyList<RunSummary> Runs,
    IReadOnlyList<RunStoreIssue> Issues);

// Terminal runs are recorded separately from the mutable checkpoint so a lost run
// and its design can be listed/exported after the active index is cleared.
public sealed record RunRecord(
    int SchemaVersion,
    string RunId,
    long Seed,
    RunPhase Phase,
    int Round,
    int BlockIndex,
    int Victories,
    int Defeats,
    EncounterOutcome? LastOutcome,
    string? LastOutcomeDetail,
    RunDesignState? Design,
    long CheckpointRevision,
    string CheckpointHash)
{
    // Schema-6 cumulative totals. Zero on older records, so their decoded values
    // and newly encoded bytes remain unchanged.
    public int EnemiesDestroyed { get; init; }
    public int FriendliesLost { get; init; }
    public int Attempts { get; init; }
    public int RoundAttempts { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
    public decimal Score { get; init; }
    public EraReference? Era { get; init; }
}

public sealed record RunRecordResult(
    RunRecord? Record,
    IReadOnlyList<RunStoreIssue> Issues);

public sealed record BlueprintReadResult(
    byte[]? Bytes,
    IReadOnlyList<RunStoreIssue> Issues);
