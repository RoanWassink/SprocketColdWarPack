using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence;

// UTF-8 JSON, schema version 1, explicit converters for every frozen contract
// record. No type names, polymorphic CLR deserialization or unknown-property
// tolerance is used: the stored graph is a closed, validated set of types.
internal static class RunStoreJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            WriteIndented = false,
            MaxDepth = RunStoreNames.MaxJsonDepth,
            Encoder = JavaScriptEncoder.Default
        };

        options.Converters.Add(new ScenarioValueJsonConverter());
        options.Converters.Add(new ModRequirementJsonConverter());
        options.Converters.Add(new EraReferenceJsonConverter());
        options.Converters.Add(new EnemyUnitJsonConverter());
        options.Converters.Add(new AttemptIdentityJsonConverter());
        options.Converters.Add(new BlueprintReferenceJsonConverter());
        options.Converters.Add(new EncounterPlanJsonConverter());
        options.Converters.Add(new RunDesignStateJsonConverter());
        options.Converters.Add(new RunSnapshotJsonConverter());
        options.Converters.Add(new RewardStateConverter());
        options.Converters.Add(new RewardItemConverter());
        options.Converters.Add(new EncounterExtrasConverter());
        options.Converters.Add(new SpecialBattleConverter());
        options.Converters.Add(new SpecialUnitConverter());

        return options;
    }
}

internal abstract class StrictObjectConverter<T> : JsonConverter<T>
    where T : class
{
    public sealed override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"{typeof(T).Name} must be a JSON object.");
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, property.Value))
            {
                throw new JsonException(
                    $"Duplicate property '{property.Name}' in {typeof(T).Name}.");
            }
        }

        return ReadObject(values, options);
    }

    protected abstract T ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options);

    protected static void RejectUnknown(
        IReadOnlyDictionary<string, JsonElement> values,
        params string[] allowed)
    {
        foreach (var key in values.Keys)
        {
            if (Array.IndexOf(allowed, key) < 0)
            {
                throw new JsonException($"Unknown property '{key}'.");
            }
        }
    }

    protected static JsonElement Require(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var element))
        {
            throw new JsonException($"Missing required property '{name}'.");
        }

        return element;
    }

    protected static string RequireString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{name}' must be a string.");
        }

        return element.GetString()
            ?? throw new JsonException($"Property '{name}' must not be null.");
    }

    protected static string? RequireNullableString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{name}' must be a string or null.");
        }

        return element.GetString()
            ?? throw new JsonException($"Property '{name}' must not be null.");
    }

    protected static int RequireInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new JsonException($"Property '{name}' must be a 32-bit integer.");
        }

        return value;
    }

    protected static long RequireInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            throw new JsonException($"Property '{name}' must be a 64-bit integer.");
        }

        return value;
    }

    protected static bool RequireBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            throw new JsonException($"Property '{name}' must be a boolean.");
        }

        return element.GetBoolean();
    }

    protected static decimal RequireDecimalNumber(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var number))
            throw new JsonException($"Property '{name}' must be a decimal number.");
        return number;
    }

    // Decimals are stored as invariant strings so native quote precision and scale
    // round-trip exactly instead of depending on JSON number formatting.
    protected static decimal RequireDecimalString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{name}' must be a decimal string.");
        }

        var text = element.GetString()
            ?? throw new JsonException($"Property '{name}' must not be null.");
        if (!decimal.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new JsonException($"Property '{name}' is not a valid invariant decimal.");
        }

        return value;
    }

    protected static TEnum RequireEnum<TEnum>(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
        where TEnum : struct, Enum
    {
        var element = Require(values, name);
        return ReadEnum<TEnum>(element, name);
    }

    protected static TEnum? RequireNullableEnum<TEnum>(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
        where TEnum : struct, Enum
    {
        var element = Require(values, name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadEnum<TEnum>(element, name);
    }

    protected static IReadOnlyList<TItem> RequireArray<TItem>(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        JsonSerializerOptions options)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Property '{name}' must be an array.");
        }

        var list = new List<TItem>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null)
            {
                throw new JsonException($"Property '{name}' contains null.");
            }

            var value = item.Deserialize<TItem>(options);
            if (value is null)
            {
                throw new JsonException($"Property '{name}' contains null.");
            }

            list.Add(value);
        }

        return list.AsReadOnly();
    }

    protected static IReadOnlyList<string> RequireStringArray(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var element = Require(values, name);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Property '{name}' must be an array.");
        }

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Property '{name}' must contain strings.");
            }

            list.Add(item.GetString()
                ?? throw new JsonException($"Property '{name}' contains null."));
        }

        return list.AsReadOnly();
    }

    protected static TItem? RequireNullableObject<TItem>(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        JsonSerializerOptions options)
        where TItem : class
    {
        var element = Require(values, name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var value = element.Deserialize<TItem>(options);
        return value ?? throw new JsonException($"Property '{name}' must not be null.");
    }

    protected static void WriteNullableString(
        Utf8JsonWriter writer,
        string name,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    protected static void WriteDecimalString(
        Utf8JsonWriter writer,
        string name,
        decimal value) =>
        writer.WriteString(name, value.ToString(CultureInfo.InvariantCulture));

    private static TEnum ReadEnum<TEnum>(JsonElement element, string name)
        where TEnum : struct, Enum
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Property '{name}' must be an enum name.");
        }

        var text = element.GetString()
            ?? throw new JsonException($"Property '{name}' must not be null.");
        if (Array.IndexOf(Enum.GetNames(typeof(TEnum)), text) < 0)
        {
            throw new JsonException(
                $"Property '{name}' has unknown {typeof(TEnum).Name} value '{text}'.");
        }

        return (TEnum)Enum.Parse(typeof(TEnum), text, ignoreCase: false);
    }
}

internal sealed class ScenarioValueJsonConverter : StrictObjectConverter<ScenarioValue>
{
    public override void Write(
        Utf8JsonWriter writer,
        ScenarioValue value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteDecimalString(writer, "value", value.Value);
        writer.WriteString("unitId", value.UnitId);
        writer.WriteEndObject();
    }

    protected override ScenarioValue ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "value", "unitId");
        return new ScenarioValue(
            RequireDecimalString(values, "value"),
            RequireString(values, "unitId"));
    }
}

internal sealed class ModRequirementJsonConverter : StrictObjectConverter<ModRequirement>
{
    public override void Write(
        Utf8JsonWriter writer,
        ModRequirement value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("version", value.Version);
        writer.WriteEndObject();
    }

    protected override ModRequirement ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "id", "version");
        return new ModRequirement(
            RequireString(values, "id"),
            RequireString(values, "version"));
    }
}

internal sealed class EraReferenceJsonConverter : StrictObjectConverter<EraReference>
{
    public override void Write(
        Utf8JsonWriter writer,
        EraReference value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("nativeId", value.NativeId);
        writer.WriteEndObject();
    }

    protected override EraReference ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "nativeId");
        return new EraReference(RequireString(values, "nativeId"));
    }
}

internal sealed class EnemyUnitJsonConverter : StrictObjectConverter<EnemyUnit>
{
    public override void Write(
        Utf8JsonWriter writer,
        EnemyUnit value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("blueprintId", value.BlueprintId);
        writer.WriteNumber("count", value.Count);
        writer.WriteEndObject();
    }

    protected override EnemyUnit ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "blueprintId", "count");
        return new EnemyUnit(
            RequireString(values, "blueprintId"),
            RequireInt32(values, "count"));
    }
}

internal sealed class AttemptIdentityJsonConverter : StrictObjectConverter<AttemptIdentity>
{
    public override void Write(
        Utf8JsonWriter writer,
        AttemptIdentity value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("runId", value.RunId);
        writer.WriteString("encounterId", value.EncounterId);
        writer.WriteString("planHash", value.PlanHash);
        writer.WriteNumber("generation", value.Generation);
        writer.WriteEndObject();
    }

    protected override AttemptIdentity ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "runId", "encounterId", "planHash", "generation");
        return new AttemptIdentity(
            RequireString(values, "runId"),
            RequireString(values, "encounterId"),
            RequireString(values, "planHash"),
            RequireInt64(values, "generation"));
    }
}

internal sealed class BlueprintReferenceJsonConverter
    : StrictObjectConverter<BlueprintReference>
{
    public override void Write(
        Utf8JsonWriter writer,
        BlueprintReference value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("localId", value.LocalId);
        writer.WriteString("snapshotHash", value.SnapshotHash);
        writer.WritePropertyName("era");
        JsonSerializer.Serialize(writer, value.Era, options);
        writer.WritePropertyName("requiredMods");
        WriteMods(writer, value.RequiredMods, options);
        writer.WriteEndObject();
    }

    protected override BlueprintReference ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "localId", "snapshotHash", "era", "requiredMods");
        var era = values.TryGetValue("era", out var eraElement)
            ? eraElement.Deserialize<EraReference>(options)
            : null;
        return new BlueprintReference(
            RequireString(values, "localId"),
            RequireString(values, "snapshotHash"),
            era ?? throw new JsonException("Property 'era' must not be null."),
            RequireArray<ModRequirement>(values, "requiredMods", options));
    }

    internal static void WriteMods(
        Utf8JsonWriter writer,
        IReadOnlyList<ModRequirement> mods,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var mod in mods)
        {
            JsonSerializer.Serialize(writer, mod, options);
        }

        writer.WriteEndArray();
    }
}

internal sealed class EncounterPlanJsonConverter : StrictObjectConverter<EncounterPlan>
{
    public override void Write(
        Utf8JsonWriter writer,
        EncounterPlan value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("offerId", value.OfferId);
        writer.WriteString("planHash", value.PlanHash);
        writer.WriteNumber("round", value.Round);
        writer.WriteString("mapId", value.MapId);
        writer.WriteString("spawnLayoutId", value.SpawnLayoutId);
        writer.WritePropertyName("era");
        JsonSerializer.Serialize(writer, value.Era, options);
        writer.WriteString("risk", value.Risk.ToString());
        writer.WritePropertyName("enemies");
        writer.WriteStartArray();
        foreach (var enemy in value.Enemies)
        {
            JsonSerializer.Serialize(writer, enemy, options);
        }

        writer.WriteEndArray();
        writer.WriteNumber("supplyReward", value.SupplyReward);
        writer.WritePropertyName("requiredMods");
        BlueprintReferenceJsonConverter.WriteMods(writer, value.RequiredMods, options);
        writer.WriteString("catalogueVersion", value.CatalogueVersion);
        if (value.Extras != null) { writer.WritePropertyName("extras"); JsonSerializer.Serialize(writer, value.Extras, options); }
        writer.WriteEndObject();
    }

    protected override EncounterPlan ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(
            values,
            "offerId",
            "planHash",
            "round",
            "mapId",
            "spawnLayoutId",
            "era",
            "risk",
            "enemies",
            "supplyReward",
            "requiredMods",
            "catalogueVersion", "extras");

        var era = values.TryGetValue("era", out var eraElement)
            ? eraElement.Deserialize<EraReference>(options)
            : null;
        return new EncounterPlan(
            RequireString(values, "offerId"),
            RequireString(values, "planHash"),
            RequireInt32(values, "round"),
            RequireString(values, "mapId"),
            RequireString(values, "spawnLayoutId"),
            era ?? throw new JsonException("Property 'era' must not be null."),
            RequireEnum<EncounterRisk>(values, "risk"),
            RequireArray<EnemyUnit>(values, "enemies", options),
            RequireInt32(values, "supplyReward"),
            RequireArray<ModRequirement>(values, "requiredMods", options),
            RequireString(values, "catalogueVersion")) { Extras = values.TryGetValue("extras", out var extras) ? extras.Deserialize<EncounterExtras>(options) : null };
    }
}

internal sealed class RunDesignStateJsonConverter
    : StrictObjectConverter<RunDesignState>
{
    public override void Write(
        Utf8JsonWriter writer,
        RunDesignState value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("blueprint");
        JsonSerializer.Serialize(writer, value.Blueprint, options);
        writer.WritePropertyName("cost");
        JsonSerializer.Serialize(writer, value.Cost, options);
        writer.WriteNumber("friendlyCountIncludingPlayer", value.FriendlyCountIncludingPlayer);
        writer.WriteEndObject();
    }

    protected override RunDesignState ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(values, "blueprint", "cost", "friendlyCountIncludingPlayer");
        var blueprint = values.TryGetValue("blueprint", out var blueprintElement)
            ? blueprintElement.Deserialize<BlueprintReference>(options)
            : null;
        var cost = values.TryGetValue("cost", out var costElement)
            ? costElement.Deserialize<ScenarioValue>(options)
            : null;
        return new RunDesignState(
            blueprint ?? throw new JsonException("Property 'blueprint' must not be null."),
            cost ?? throw new JsonException("Property 'cost' must not be null."),
            RequireInt32(values, "friendlyCountIncludingPlayer"));
    }
}

internal sealed class RunSnapshotJsonConverter : StrictObjectConverter<RunSnapshot>
{
    public override void Write(
        Utf8JsonWriter writer,
        RunSnapshot value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", value.SchemaVersion);
        writer.WriteString("runId", value.RunId);
        writer.WriteNumber("seed", value.Seed);
        writer.WriteString("catalogueVersion", value.CatalogueVersion);
        writer.WriteString("costUnitId", value.CostUnitId);

        writer.WritePropertyName("initialBaseBudget");
        JsonSerializer.Serialize(writer, value.InitialBaseBudget, options);

        writer.WritePropertyName("eraSchedule");
        writer.WriteStartArray();
        foreach (var era in value.EraSchedule)
        {
            JsonSerializer.Serialize(writer, era, options);
        }

        writer.WriteEndArray();
        writer.WriteString("phase", value.Phase.ToString());

        if (value.SuspendedFrom.HasValue)
        {
            writer.WriteString("suspendedFrom", value.SuspendedFrom.Value.ToString());
        }
        else
        {
            writer.WriteNull("suspendedFrom");
        }

        writer.WriteNumber("round", value.Round);
        writer.WriteNumber("blockIndex", value.BlockIndex);
        writer.WriteNumber("eraIndex", value.EraIndex);
        if (value.StartingEraIndex != 0) writer.WriteNumber("startingEraIndex", value.StartingEraIndex);
        if (value.OfferEconomy != null) { writer.WritePropertyName("offerEconomy"); JsonSerializer.Serialize(writer, value.OfferEconomy, options); }
        if (value.GenerationPolicy.HasValue) writer.WriteNumber("generationPolicy", value.GenerationPolicy.Value);
        writer.WriteNumber("winsInBlock", value.WinsInBlock);
        writer.WriteNumber("victories", value.Victories);
        writer.WriteNumber("defeats", value.Defeats);

        writer.WritePropertyName("blockBaseBudget");
        JsonSerializer.Serialize(writer, value.BlockBaseBudget, options);

        WriteDecimalString(writer, "supplyBonus", value.SupplyBonus);

        writer.WritePropertyName("availableBudget");
        JsonSerializer.Serialize(writer, value.AvailableBudget, options);

        writer.WriteNumber("pendingSupplyPoints", value.PendingSupplyPoints);

        writer.WritePropertyName("confirmedDesign");
        if (value.ConfirmedDesign is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.ConfirmedDesign, options);
        }

        writer.WriteBoolean("designConfirmedForBlock", value.DesignConfirmedForBlock);

        writer.WritePropertyName("offers");
        writer.WriteStartArray();
        foreach (var offer in value.Offers)
        {
            JsonSerializer.Serialize(writer, offer, options);
        }

        writer.WriteEndArray();

        WriteNullableString(writer, "selectedOfferId", value.SelectedOfferId);

        writer.WritePropertyName("lockedPlan");
        if (value.LockedPlan is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.LockedPlan, options);
        }

        writer.WritePropertyName("currentAttempt");
        if (value.CurrentAttempt is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.CurrentAttempt, options);
        }

        writer.WriteNumber("generationCounter", value.GenerationCounter);

        writer.WritePropertyName("processedAttempts");
        writer.WriteStartArray();
        foreach (var attempt in value.ProcessedAttempts)
        {
            JsonSerializer.Serialize(writer, attempt, options);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("consumedOfferIds");
        writer.WriteStartArray();
        foreach (var offerId in value.ConsumedOfferIds)
        {
            writer.WriteStringValue(offerId);
        }

        writer.WriteEndArray();

        if (value.LastOutcome.HasValue)
        {
            writer.WriteString("lastOutcome", value.LastOutcome.Value.ToString());
        }
        else
        {
            writer.WriteNull("lastOutcome");
        }

        WriteNullableString(writer, "lastOutcomeDetail", value.LastOutcomeDetail);
        // Schema-6 run totals are written only when nonzero, so schemas 1-5 and
        // fresh zero-valued schema-6 snapshots keep their exact previous bytes.
        if (value.EnemiesDestroyed != 0) writer.WriteNumber("enemiesDestroyed", value.EnemiesDestroyed);
        if (value.FriendliesLost != 0) writer.WriteNumber("friendliesLost", value.FriendliesLost);
        if (value.Attempts != 0) writer.WriteNumber("attempts", value.Attempts);
        if (value.RoundAttempts != 0) writer.WriteNumber("roundAttempts", value.RoundAttempts);
        if (value.TestPool) { writer.WriteBoolean("test_pool", true); writer.WriteNumber("testPoolOffset", value.TestPoolOffset); }
        if (value.Ironman) writer.WriteBoolean("ironman", true);
        if (value.Score != 0m) writer.WriteNumber("score", value.Score);
        if (value.Rewards != null) { writer.WritePropertyName("rewards"); JsonSerializer.Serialize(writer, value.Rewards, options); }
        writer.WriteEndObject();
    }

    protected override RunSnapshot ReadObject(
        IReadOnlyDictionary<string, JsonElement> values,
        JsonSerializerOptions options)
    {
        RejectUnknown(
            values,
            "schemaVersion",
            "runId",
            "seed",
            "catalogueVersion",
            "costUnitId",
            "initialBaseBudget",
            "eraSchedule",
            "phase",
            "suspendedFrom",
            "round",
            "blockIndex",
            "eraIndex",
            "winsInBlock",
            "victories",
            "defeats",
            "blockBaseBudget",
            "supplyBonus",
            "availableBudget",
            "pendingSupplyPoints",
            "confirmedDesign",
            "designConfirmedForBlock",
            "offers",
            "selectedOfferId",
            "lockedPlan",
            "currentAttempt",
            "generationCounter",
            "processedAttempts",
            "consumedOfferIds",
            "lastOutcome",
            "lastOutcomeDetail", "rewards", "startingEraIndex", "offerEconomy", "generationPolicy",
            "enemiesDestroyed", "friendliesLost", "attempts", "roundAttempts", "ironman", "score", "test_pool", "testPoolOffset");

        var schemaVersion = RequireInt32(values, "schemaVersion");
        if (schemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7))
        {
            throw new JsonException(
                $"Unsupported snapshot schemaVersion {schemaVersion}.");
        }

        var initialBaseBudget = RequireObject<ScenarioValue>(
            values, "initialBaseBudget", options);
        var blockBaseBudget = RequireObject<ScenarioValue>(
            values, "blockBaseBudget", options);
        var availableBudget = RequireObject<ScenarioValue>(
            values, "availableBudget", options);

        return new RunSnapshot(
            schemaVersion,
            RequireString(values, "runId"),
            RequireInt64(values, "seed"),
            RequireString(values, "catalogueVersion"),
            RequireString(values, "costUnitId"),
            initialBaseBudget,
            RequireArray<EraReference>(values, "eraSchedule", options),
            RequireEnum<RunPhase>(values, "phase"),
            RequireNullableEnum<RunPhase>(values, "suspendedFrom"),
            RequireInt32(values, "round"),
            RequireInt32(values, "blockIndex"),
            RequireInt32(values, "eraIndex"),
            RequireInt32(values, "winsInBlock"),
            RequireInt32(values, "victories"),
            RequireInt32(values, "defeats"),
            blockBaseBudget,
            RequireDecimalString(values, "supplyBonus"),
            availableBudget,
            RequireInt32(values, "pendingSupplyPoints"),
            RequireNullableObject<RunDesignState>(values, "confirmedDesign", options),
            RequireBoolean(values, "designConfirmedForBlock"),
            RequireArray<EncounterPlan>(values, "offers", options),
            RequireNullableString(values, "selectedOfferId"),
            RequireNullableObject<EncounterPlan>(values, "lockedPlan", options),
            RequireNullableObject<AttemptIdentity>(values, "currentAttempt", options),
            RequireInt64(values, "generationCounter"),
            RequireArray<AttemptIdentity>(values, "processedAttempts", options),
            RequireStringArray(values, "consumedOfferIds"),
            RequireNullableEnum<EncounterOutcome>(values, "lastOutcome"),
            RequireNullableString(values, "lastOutcomeDetail")) {
                Rewards = values.TryGetValue("rewards", out var rewards) ? rewards.Deserialize<RewardState>(options) : null,
                StartingEraIndex = values.ContainsKey("startingEraIndex") ? RequireInt32(values, "startingEraIndex") : 0,
                OfferEconomy = values.TryGetValue("offerEconomy", out var economy) ? economy.Deserialize<OfferEconomy>(options) : null,
                GenerationPolicy = values.ContainsKey("generationPolicy") ? RequireInt32(values, "generationPolicy") : null,
                EnemiesDestroyed = values.ContainsKey("enemiesDestroyed") ? RequireInt32(values, "enemiesDestroyed") : 0,
                FriendliesLost = values.ContainsKey("friendliesLost") ? RequireInt32(values, "friendliesLost") : 0,
                Attempts = values.ContainsKey("attempts") ? RequireInt32(values, "attempts") : 0,
                RoundAttempts = values.ContainsKey("roundAttempts") ? RequireInt32(values, "roundAttempts") : 0,
                TestPool = values.ContainsKey("test_pool") && RequireBoolean(values, "test_pool"),
                TestPoolOffset = values.ContainsKey("testPoolOffset") ? RequireInt32(values, "testPoolOffset") : 0,
                Ironman = values.ContainsKey("ironman") && RequireBoolean(values, "ironman"),
                Score = values.ContainsKey("score") ? RequireDecimalNumber(values, "score") : 0m };
    }

    private static TItem RequireObject<TItem>(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        JsonSerializerOptions options)
        where TItem : class
    {
        var element = Require(values, name);
        if (element.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException($"Property '{name}' must not be null.");
        }

        return element.Deserialize<TItem>(options)
            ?? throw new JsonException($"Property '{name}' must not be null.");
    }
}
