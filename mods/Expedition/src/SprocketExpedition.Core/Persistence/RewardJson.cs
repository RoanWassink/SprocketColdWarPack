using System.Text.Json;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Core.Persistence;

// New v2 records use the same closed-object rules as the frozen v1 graph.
// The CLR serializer handles field types only after exact keys are checked.
internal abstract class RewardObjectConverter<T> : StrictObjectConverter<T> where T : class
{
    private readonly string[] keys;
    protected virtual string[] OptionalKeys => Array.Empty<string>();
    protected RewardObjectConverter(params string[] keys) => this.keys = keys;
    private JsonSerializerOptions Plain(JsonSerializerOptions options)
    {
        var plain = new JsonSerializerOptions(options);
        for (var i = plain.Converters.Count - 1; i >= 0; i--)
            if (plain.Converters[i].GetType() == GetType()) plain.Converters.RemoveAt(i);
        return plain;
    }
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, Plain(options));
    protected override T ReadObject(IReadOnlyDictionary<string, JsonElement> values, JsonSerializerOptions options)
    {
        RejectUnknown(values, keys.Concat(OptionalKeys).ToArray());
        foreach (var key in keys) _ = Require(values, key);
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(values), Plain(options))
            ?? throw new JsonException("Reward object must not be null.");
    }
}
internal sealed class RewardStateConverter : RewardObjectConverter<RewardState>
{
    public RewardStateConverter() : base("version", "revision", "inventory", "active", "choices", "grantedOffers", "earnedFunds", "offerRoll", "veteranAllies") { }
}
internal sealed class RewardItemConverter : RewardObjectConverter<RewardItem>
{
    public RewardItemConverter() : base("id", "kind", "rarity", "magnitude", "duration", "remainingRounds", "blueprintId") { }
}
internal sealed class EncounterExtrasConverter : RewardObjectConverter<EncounterExtras>
{
    protected override string[] OptionalKeys => new[] { "curatedBoss", "special", "enemyBudgetTarget", "generationPolicy" };
    public EncounterExtrasConverter() : base("rulesVersion", "offerRoll", "reward", "bossBlueprintId", "bossEra", "bossCharacter", "placeholderBoss", "enemyBudgetMultiplier") { }
}
internal sealed class SpecialBattleConverter : RewardObjectConverter<SpecialBattle>
{
    public SpecialBattleConverter() : base("preset", "title", "pattern", "enemies", "rewardFamily", "rewardMagnitude", "duration") { }
}
internal sealed class SpecialUnitConverter : RewardObjectConverter<SpecialUnit>
{
    protected override string[] OptionalKeys => new[] { "deploymentRole" };
    public SpecialUnitConverter() : base("blueprintId", "count") { }
}
