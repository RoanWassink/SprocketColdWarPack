namespace SprocketExpedition.Core.Encounters;

public enum SpecialPreset { RatPlague, SteelWall, Crossfire, TacticalGroup }
// Numeric values 0-3 are persisted in old saves; Ambush is appended for
// adventure policy 5 and rejected on older policy versions.
public enum SpecialSpawnPattern { Swarm, Line, Split, CombinedArms, Ambush, Defensive }
public sealed record SpecialDesign(string BlueprintId, decimal Cost);
public sealed record SpecialUnit(string BlueprintId, int Count)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? DeploymentRole { get; init; }
}
public sealed record SpecialBattle(SpecialPreset Preset, string Title, SpecialSpawnPattern Pattern,
    IReadOnlyList<SpecialUnit> Enemies, string RewardFamily, decimal RewardMagnitude, int Duration);
