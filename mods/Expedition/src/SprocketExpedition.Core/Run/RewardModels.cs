namespace SprocketExpedition.Core.Run;

public enum RewardKind
{
    MassProduction, AssemblyLine, EliteWorkshop, SwarmDoctrine, FieldReinforcements,
    LendLease, SalvageRights, VeteranCrew, WarBonds, EmergencyReserve,
    OperationalFreedom, LastStand, CapturedPrototype, ExperimentalContract, EmergencyRecovery
}
public enum RewardRarity { Common, Rare, UltraRare, Special }

public sealed record RewardItem(string Id, RewardKind Kind, RewardRarity Rarity,
    decimal Magnitude, int Duration, int RemainingRounds, string? BlueprintId = null);

public sealed record RewardState
{
    public int Version { get; init; } = 2;
    public long Revision { get; init; }
    public IReadOnlyList<RewardItem> Inventory { get; init; } = Array.Empty<RewardItem>();
    public IReadOnlyList<RewardItem> Active { get; init; } = Array.Empty<RewardItem>();
    public IReadOnlyList<RewardItem> Choices { get; init; } = Array.Empty<RewardItem>();
    public IReadOnlyList<string> GrantedOffers { get; init; } = Array.Empty<string>();
    public decimal EarnedFunds { get; init; }
    public int OfferRoll { get; init; }
    public int VeteranAllies { get; init; }
}

public sealed record RewardFleet(int PlayerCopies, int FreeReinforcements, decimal TotalCost,
    decimal EffectiveBudget, int MaximumAllies)
{
    public int Total => PlayerCopies + FreeReinforcements;
}

public sealed record EncounterExtras(int RulesVersion, int OfferRoll, RewardItem Reward,
    string? BossBlueprintId = null, string? BossEra = null, string? BossCharacter = null,
    bool PlaceholderBoss = false, decimal EnemyBudgetMultiplier = 1m)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool CuratedBoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Encounters.SpecialBattle? Special { get; init; }
    // Honest, pinned policy-5 target for the checked enemy-price planner. It is
    // absent on legacy plans so their serialized bytes and hashes are unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? EnemyBudgetTarget { get; init; }
    // Optional policy overlay for offers generated inside an older-schema run.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? GenerationPolicy { get; init; }
}

// Display parts of one reward (see RewardRules.DescribeParts). Note is empty when
// the reward has no extra rule.
public sealed record RewardParts(string Name, string Value, string RarityName, string Timing, string Note);
