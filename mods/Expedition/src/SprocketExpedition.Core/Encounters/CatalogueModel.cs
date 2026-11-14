namespace SprocketExpedition.Core.Encounters;

// Qualification is deliberately ordered so that a caller can request a minimum
// evidence level. Fixture content can never satisfy a release requirement.
public enum ContentQualification
{
    Fixture = 0,
    Candidate = 1,
    Qualified = 2
}

public enum EnemyRole
{
    Line,
    Support,
    Heavy,
    Recon,
    AntiTank
}

// A map reference is local and opaque. ContentHash is the checked hash of the
// map/spawn-layout payload used for resume validation. No raw game assets are
// stored here and no path outside the local content store is permitted.
public sealed record MapEntry(
    EraReference Era,
    string MapId,
    string ContentHash,
    IReadOnlyList<string> SpawnLayoutIds,
    IReadOnlyList<ModRequirement> RequiredMods,
    ContentQualification Qualification);

// The blueprint is the frozen contract reference (local id, snapshot hash,
// native era and required mods). Legacy fixtures retain their historical score.
// The native cost profile requires ThreatScore == CheckedPrice.Value; formation
// strength is the sum of each saved native design price times its count.
public sealed record EnemyDesignEntry(
    BlueprintReference Blueprint,
    EnemyRole Role,
    decimal ThreatScore,
    ScenarioValue CheckedPrice,
    ContentQualification Qualification)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FormationFamily { get; init; }

    // Actual native VehicleCard.Mass in kilograms for newly indexed content.
    // Absent on older pinned catalogues; absence means unknown, never 0 kg.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? MassKg { get; init; }

    // Lowest encounter risk this design may appear in. Absent (null) keeps the
    // design available to every risk, exactly as older catalogues expect.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EncounterRisk? MinimumRisk { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxPerBattle { get; init; }
}

public sealed record TestPoolDesign(string Name, EnemyDesignEntry Design);

public sealed record CatalogueEraContent(
    EraReference Era,
    IReadOnlyList<MapEntry> Maps,
    IReadOnlyList<EnemyDesignEntry> Enemies);

public sealed record CatalogueDefinition(
    string Version,
    string Label,
    bool IsFixture,
    string PriceUnitId,
    IReadOnlyList<CatalogueEraContent> Eras)
{
    public const string FixtureLabel = "TEST FIXTURE NOT PLAYABLE";
    public IReadOnlyDictionary<string, string>? BossBlueprints { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? BossCandidates { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TestPoolDesign>? TestPool { get; init; }
}

// RequiredQualification is the minimum evidence level for content used by this
// catalogue instance. Release defaults to Qualified. EnforceMinimumContent
// applies the authored release minimum (3 maps, 4 enemy designs per native era).
public sealed record CatalogueOptions(
    ContentQualification RequiredQualification,
    bool EnforceMinimumContent,
    bool CostBasedDifficulty = false)
{
    public static CatalogueOptions Release { get; } =
        new(ContentQualification.Qualified, true);

    public static CatalogueOptions Fixture { get; } =
        new(ContentQualification.Fixture, true);
}

public sealed record CatalogueValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public enum EncounterAvailabilityStatus
{
    Success,
    InvalidRequest,
    CatalogueVersionMismatch,
    UnknownEra,
    NoQualifiedMaps,
    NoQualifiedEnemyDesigns,
    NoValidFormation,
    EndlessContentExhausted,
    InvalidContent,
    OfferAlreadyResolved,
    InvalidPersistedOffers
}

public sealed record OfferGenerationResult(
    EncounterAvailabilityStatus Status,
    IReadOnlyList<EncounterPlan> Offers,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsSuccess => Status == EncounterAvailabilityStatus.Success;

    public static OfferGenerationResult Success(IReadOnlyList<EncounterPlan> offers) =>
        new(EncounterAvailabilityStatus.Success, offers, Array.Empty<string>());

    public static OfferGenerationResult Failure(
        EncounterAvailabilityStatus status,
        params string[] diagnostics) =>
        new(status, Array.Empty<EncounterPlan>(), diagnostics);

    public static OfferGenerationResult Failure(
        EncounterAvailabilityStatus status,
        IReadOnlyList<string> diagnostics) =>
        new(status, Array.Empty<EncounterPlan>(), diagnostics);
}

// Thrown by the frozen IEncounterCatalogue.GenerateOffers method when content is
// missing, unqualified, exhausted or otherwise unavailable. Status carries the
// explicit reason; callers must never substitute a wrong-era or synthetic plan.
public sealed class EncounterAvailabilityException : Exception
{
    public EncounterAvailabilityException(
        EncounterAvailabilityStatus status,
        IReadOnlyList<string> diagnostics)
        : base(BuildMessage(status, diagnostics))
    {
        Status = status;
        Diagnostics = diagnostics;
    }

    public EncounterAvailabilityStatus Status { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    private static string BuildMessage(
        EncounterAvailabilityStatus status,
        IReadOnlyList<string> diagnostics)
    {
        var detail = diagnostics.Count == 0
            ? "no diagnostic supplied"
            : string.Join("; ", diagnostics);
        return $"{status}: {detail}";
    }
}

public sealed record RiskProfile(
    EncounterRisk Risk,
    decimal ThreatMultiplier,
    int SupplyReward);

// Rules shared by generation, validation, persistence checks and tools. These
// are authored starting coefficients (see docs/implementation.md), not native
// dates or native prices.
public static class EncounterRules
{
    public const int MaxEnemies = 12;
    public const int MinFriendlyCount = 1;
    public const int MaxFriendlyCount = 8;
    public const int OfferCount = 3;
    public const int RoundsPerBlock = 3;
    public const int FiniteBlocks = 6;
    public const int FiniteRounds = RoundsPerBlock * FiniteBlocks;

    public static IReadOnlyList<RiskProfile> All { get; } = new[]
    {
        new RiskProfile(EncounterRisk.Cautious, 0.90m, 1),
        new RiskProfile(EncounterRisk.Standard, 1.00m, 2),
        new RiskProfile(EncounterRisk.Dangerous, 1.15m, 3)
    };

    public static RiskProfile Profile(EncounterRisk risk) => risk switch
    {
        EncounterRisk.Cautious => All[0],
        EncounterRisk.Standard => All[1],
        EncounterRisk.Dangerous => All[2],
        _ => throw new ArgumentOutOfRangeException(
            nameof(risk), risk, "Unknown encounter risk.")
    };

    public static bool IsDefinedRisk(EncounterRisk risk) =>
        risk is EncounterRisk.Cautious
            or EncounterRisk.Standard
            or EncounterRisk.Dangerous;

    public static bool IsEndlessBlock(int block) => block >= FiniteBlocks;

    public static int BlockForRound(int round)
    {
        if (round < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(round), round, "Round must be at least one.");
        }

        // Zero-based to match RunSnapshot.BlockIndex and OfferRequest.Block.
        return (round - 1) / RoundsPerBlock;
    }

    public static int RoundInBlock(int round)
    {
        if (round < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(round), round, "Round must be at least one.");
        }

        return (round - 1) % RoundsPerBlock + 1;
    }

    // The supplied budget already includes the active supply bonus. Rounding
    // down is explicit and uses decimal arithmetic in the checked scenario unit.
    // Approved target = current fleet ceiling * within-block multiplier
    // * risk multiplier.
    public static decimal ThreatTarget(
        ScenarioValue budget,
        int round,
        EncounterRisk risk)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return decimal.Floor(
            budget.Value
            * WithinBlockMultiplier(round)
            * Profile(risk).ThreatMultiplier);
    }

    // Schema 6 curve. A finite era has four fights: 1/2/3 are 0.70/0.90/1.10
    // and the fourth (boss) is explicitly 1.15. Endless blocks also have four
    // wins, but the fourth card is not a boss; it uses the same 1.15 factor.
    public static decimal WithinBlockMultiplierV6(int roundInBlock) =>
        roundInBlock switch
        {
            1 => 0.70m,
            2 => 0.90m,
            3 => 1.10m,
            4 => 1.15m,
            _ => throw new ArgumentOutOfRangeException(
                nameof(roundInBlock), roundInBlock, "Round-in-block must be 1-4.")
        };

    public static decimal ThreatTargetV6(
        ScenarioValue budget,
        int roundInBlock,
        EncounterRisk risk)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return decimal.Floor(
            budget.Value
            * WithinBlockMultiplierV6(roundInBlock)
            * Profile(risk).ThreatMultiplier);
    }

    // Native prices are discrete: one available model must still allow three
    // honest offers (one/two/three copies), rather than relabel the same force.
    // Keep the authored ceiling unless it cannot distinguish those first three
    // attainable positive costs. Legacy catalogues keep ThreatTarget unchanged.
    public static decimal CostThreatTarget(ScenarioValue budget, int round,
        EncounterRisk risk, IEnumerable<EnemyDesignEntry> available) =>
        CostThreatTargetCore(budget, WithinBlockMultiplier(round), risk, available);

    public static decimal CostThreatTargetV6(ScenarioValue budget, int roundInBlock,
        EncounterRisk risk, IEnumerable<EnemyDesignEntry> available) =>
        CostThreatTargetCore(budget, WithinBlockMultiplierV6(roundInBlock), risk, available);

    private static decimal CostThreatTargetCore(ScenarioValue budget, decimal withinBlock,
        EncounterRisk risk, IEnumerable<EnemyDesignEntry> available)
    {
        var prices = available.Where(e => e.CheckedPrice.Value > 0m
                && e.CheckedPrice.UnitId == budget.UnitId)
            .Select(e => e.CheckedPrice.Value).Distinct().OrderBy(p => p).Take(3).ToArray();
        var costs = new SortedSet<decimal>();
        void Add(decimal a, decimal b)
        {
            try { costs.Add(checked(a + b)); } catch (OverflowException) { }
        }
        foreach (var a in prices)
        {
            costs.Add(a);
            foreach (var b in prices)
            {
                Add(a, b);
                foreach (var c in prices)
                {
                    try { Add(checked(a + b), c); } catch (OverflowException) { }
                }
            }
        }
        var first = costs.Take(3).ToArray();
        var authored = decimal.Floor(
            budget.Value * withinBlock * Profile(risk).ThreatMultiplier);
        return first.Length <= (int)risk ? authored : Math.Max(authored, first[(int)risk]);
    }

    public static decimal WithinBlockMultiplier(int round) =>
        RoundInBlock(round) switch
        {
            1 => 0.65m,
            2 => 0.90m,
            3 => 1.15m,
            _ => throw new ArgumentOutOfRangeException(
                nameof(round), round, "Round-in-block is not defined.")
        };
}
