namespace SprocketExpedition.Core;

// Frozen Core contract v1.0 — 2026-10-20.
// Coordinator amendment 2026-10-24: additive adventure schema 2; existing v1
// fields and enum values retained. See docs/adventure-rewards-2026-10-24.md.
// Native vertical-slice gate passed for player/ally/enemy spawning, one win and one
// loss, native editor return, repeated restart and editor restoration.
// This file is coordinator-owned. Workers must not change public contracts.
public enum RunPhase
{
    Building, ChoosingEncounter, LoadingEncounter, Fighting, Resolving,
    AwaitingEndlessChoice, Lost, Finished, Suspended, AwaitingRecovery, Retired
}

public enum EncounterRisk { Cautious, Standard, Dangerous }

// CombatRetreat is a deliberate exit during live combat (loss).
// PrecombatAborted is a loading/cancellation abort (no reward, no loss).
public enum EncounterOutcome
{
    Victory, FriendlyForceDestroyed, CombatRetreat, PrecombatAborted, TechnicalFailure
}

public sealed record ModRequirement(string Id, string Version);
public sealed record EraReference(string NativeId);

public sealed record BlueprintReference(
    string LocalId,
    string SnapshotHash,
    EraReference Era,
    IReadOnlyList<ModRequirement> RequiredMods);

// Value is expressed in the verified scenario unit. UnitId prevents accidental
// comparison with CustomBattles' multi-slot Cost. No conversion is defined here.
public sealed record ScenarioValue(decimal Value, string UnitId);

public sealed record DesignQuote(
    BlueprintReference Blueprint,
    ScenarioValue Cost,
    bool NativeDesignValid,
    bool NativeEraValid,
    string? RejectionReason);

// Opaque native serialization output for the locked design. The bytes never enter
// the presenter or UI; the controller passes them to RunStore for hash verification.
public sealed record DesignCapture(
    DesignQuote Quote,
    byte[] SnapshotBytes);

public sealed record EnemyUnit(string BlueprintId, int Count);

// OfferId is unique within a run and is never reused after resolution.
// PlanHash covers every field that can affect the generated encounter.
public sealed record EncounterPlan(
    string OfferId,
    string PlanHash,
    int Round,
    string MapId,
    string SpawnLayoutId,
    EraReference Era,
    EncounterRisk Risk,
    IReadOnlyList<EnemyUnit> Enemies,
    int SupplyReward,
    IReadOnlyList<ModRequirement> RequiredMods,
    string CatalogueVersion)
{
    public Run.EncounterExtras? Extras { get; init; }
}

public sealed record OfferRequest(
    string RunId,
    long Seed,
    int Round,
    int Block,
    EraReference Era,
    ScenarioValue AvailableBudget,
    int PendingSupplyPoints,
    string CatalogueVersion)
{
    public int RulesVersion { get; init; } = 1;
    public int OfferRoll { get; init; }
    public decimal EnemyBudgetMultiplier { get; init; } = 1m;
    // Adventure policy 5 pins this per generated offer set. Legacy v2-v4
    // requests ignore it so their producer bytes and hashes stay unchanged.
    public bool EraProgressionEnabled { get; init; }
    // Optional generation policy overlay. 5 applies policy-5 difficulty and
    // ambush planning to new uncommitted offers even when an existing saved run
    // keeps its original schema/reward version. Absent/0 means legacy.
    public int GenerationPolicy { get; init; }
    public bool TestPool { get; init; }
    public int TestPoolOffset { get; init; }
}

// A retry keeps the exact plan and uses a new generation.
public sealed record AttemptIdentity(
    string RunId,
    string EncounterId,
    string PlanHash,
    long Generation);

public sealed record EncounterRequest(
    AttemptIdentity Attempt,
    EncounterPlan Plan,
    BlueprintReference PlayerBlueprint,
    int FriendlyCountIncludingPlayer)
{
    public bool TestPool { get; init; }
    public IReadOnlyList<EnemyUnit> Reinforcements { get; init; } = Array.Empty<EnemyUnit>();
}

public sealed record EncounterResult(
    AttemptIdentity Attempt,
    EncounterPlan Plan,
    EncounterOutcome Outcome,
    string? Detail)
{
    public int? ConfirmedEnemyKills { get; init; }
    public int? SurvivingAllies { get; init; }
    // Schema-6 battle log metrics measured from the same native roster the HUD
    // shows. Null means the adapter could not measure the value reliably.
    public int? EnemiesDestroyed { get; init; }
    public int? FriendliesLost { get; init; }
    public bool? PlayerTankSurvived { get; init; }
}

public sealed record AdapterReadiness(bool Ready, string Reason);

public interface IGameAdapter
{
    AdapterReadiness CheckReadiness();

    // Cost and validation must describe the final native editor design, not a
    // cached/imported nominal value. Caller cannot start without a valid quote.
    // The returned opaque bytes are the mod-preserving native snapshot consumed by
    // storage; they never enter the presenter. era is the native era of the current
    // build block; it must be used for native era validation, not invented.
    Task<DesignCapture> CaptureDesignAsync(EraReference era,
        CancellationToken cancellationToken);

    // Opens the build boundary and returns only after the native editor is settled.
    Task OpenEditorAsync(BlueprintReference blueprint, EraReference era,
        ScenarioValue budget, CancellationToken cancellationToken);

    // The adapter owns every native object, AI/order setup, outcome observation,
    // editor return, cleanup and exact-once result delivery for the attempt.
    // combatStarted is called exactly once when the native combat state is actually
    // entered; the controller uses it to persist the Fighting boundary and to
    // distinguish a precombat abort from a combat retreat. It must not be called
    // for a load/spawn failure that never entered combat.
    Task<EncounterResult> RunEncounterAsync(EncounterRequest request,
        Func<Task> combatStarted, CancellationToken cancellationToken);

    Task CleanupAsync(CancellationToken cancellationToken);
}

// 3C owns the implementation; this is the frozen input/result boundary.
// Missing or unqualified content is an explicit availability error, never a
// wrong-era fallback or a synthetic fixture presented as playable content.
public interface IEncounterCatalogue
{
    IReadOnlyList<EncounterPlan> GenerateOffers(OfferRequest request);
    EncounterPlan? Find(string offerId);
}
