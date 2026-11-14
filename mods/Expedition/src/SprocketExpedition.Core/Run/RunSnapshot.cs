using SprocketExpedition.Core;

namespace SprocketExpedition.Core.Run;

// Persistent, immutable view of every value the deterministic run director needs.
// Frozen v1 shape retained. Coordinator amendment 2026-10-24 adds optional
// schema-2 Rewards; null remains the original three-fight/18-round rules.
// The shape deliberately contains only contract records, primitives, enums and
// read-only collections so it can be serialized by the storage layer without a
// dependency on Unity, the filesystem or native assemblies.
//
// Conventions:
//   Round           one-based battle number; finite total derives from the
//                   pinned five/six-era schedule and its battle rules
//   BlockIndex      zero-based block, also the finite era index
//   EraIndex        index into EraSchedule; remains the last finite era in endless
//   PendingSupply   supply points earned in the current block, not yet converted
public sealed record RunSnapshot(
    int SchemaVersion,
    string RunId,
    long Seed,
    string CatalogueVersion,
    string CostUnitId,
    ScenarioValue InitialBaseBudget,
    IReadOnlyList<EraReference> EraSchedule,
    RunPhase Phase,
    RunPhase? SuspendedFrom,
    int Round,
    int BlockIndex,
    int EraIndex,
    int WinsInBlock,
    int Victories,
    int Defeats,
    ScenarioValue BlockBaseBudget,
    decimal SupplyBonus,
    ScenarioValue AvailableBudget,
    int PendingSupplyPoints,
    RunDesignState? ConfirmedDesign,
    bool DesignConfirmedForBlock,
    IReadOnlyList<EncounterPlan> Offers,
    string? SelectedOfferId,
    EncounterPlan? LockedPlan,
    AttemptIdentity? CurrentAttempt,
    long GenerationCounter,
    IReadOnlyList<AttemptIdentity> ProcessedAttempts,
    IReadOnlyList<string> ConsumedOfferIds,
    EncounterOutcome? LastOutcome,
    string? LastOutcomeDetail)
{
    // Schema 1 stays byte-compatible. Schema 2 has four battles per era and
    // carries its complete inventory/timers in the same atomic checkpoint.
    public RewardState? Rewards { get; init; }
    // Optional new-run shortcut. Earlier rounds are skipped, never credited as wins.
    // Zero is omitted from checkpoint JSON to preserve existing run bytes.
    public int StartingEraIndex { get; init; }
    // Original economy of already selected offers when a later item changes the
    // friendly budget. Restore validates their exact plan against this context.
    public OfferEconomy? OfferEconomy { get; init; }
    // Optional policy-5 generation overlay for future uncommitted offers in an
    // existing older-schema run. Absent preserves old snapshot bytes.
    public int? GenerationPolicy { get; init; }
    // Schema-6 cumulative run statistics. Optional and zero on older snapshots,
    // so their serialized bytes stay unchanged.
    public int EnemiesDestroyed { get; init; }
    public int FriendliesLost { get; init; }
    public int Attempts { get; init; }
    public int RoundAttempts { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
    public int TestPoolOffset { get; init; }
    public decimal Score { get; init; }
}

public sealed record OfferEconomy(ScenarioValue Budget, decimal EnemyBudgetMultiplier)
{
    // Optional policy-5 metadata. Absent means legacy bytes and no era
    // progression; the pinned multiplier alone reproduces the old offers.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? EraProgression { get; init; }
    // Schema-6 only: the selected base difficulty before the contract multiplier.
    // Absent on older offers so their serialized economy bytes stay unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? BaseDifficulty { get; init; }
}

// The validated design and resulting fleet size for the current block. The price is
// the full native quote; FriendlyCountIncludingPlayer already includes the player.
public sealed record RunDesignState(
    BlueprintReference Blueprint,
    ScenarioValue Cost,
    int FriendlyCountIncludingPlayer);

// Inputs are explicit: no clock, seed generator, current directory or environment.
public sealed record RunConfiguration(
    string RunId,
    long Seed,
    ScenarioValue InitialBaseBudget,
    IReadOnlyList<EraReference> EraSchedule,
    string CatalogueVersion)
{
    public bool AdventureRules { get; init; }
    public int AdventureVersion { get; init; } = 2;
    public int StartingEraIndex { get; init; }
    public bool Ironman { get; init; }
    public bool TestPool { get; init; }
}

public enum RunActionStatus
{
    Accepted,
    Rejected,
    IgnoredStale,
    IgnoredDuplicate
}

public sealed record RunActionResult(RunActionStatus Status, string Reason)
{
    public bool IsAccepted => Status == RunActionStatus.Accepted;
}
