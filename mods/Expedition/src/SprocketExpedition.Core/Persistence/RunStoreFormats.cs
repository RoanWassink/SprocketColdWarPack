using System.Text;
using System.Text.Json;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence;

internal sealed record RunBlueprintEntry(string Hash, long Length);

internal sealed record RunCheckpointEnvelope(
    long Revision,
    string RunId,
    RunSnapshot Snapshot,
    IReadOnlyList<RunBlueprintEntry> Blueprints);

internal sealed record RunStoreIndex(
    string? ActiveRunId,
    string? CheckpointHash,
    long Revision)
{
    public static RunStoreIndex Empty { get; } = new(null, null, 0);
}

internal sealed class RunStoreFormatException : Exception
{
    public RunStoreFormatException(
        string message,
        string code,
        bool schemaUnsupported = false)
        : base(message)
    {
        Code = code;
        SchemaUnsupported = schemaUnsupported;
    }

    public string Code { get; }

    public bool SchemaUnsupported { get; }
}

internal static class RunStoreFormats
{
    // Duplicate identity compares the candidate's canonical snapshot bytes with the
    // snapshot element exactly as persisted, or with a decoded copy of the stored
    // snapshot. The first covers an in-memory retry; the second covers a caller that
    // re-saves a snapshot it loaded from disk. Both are value-based and deterministic.
    public static bool StoredSnapshotEquals(byte[] checkpointBytes, RunSnapshot candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(
                checkpointBytes,
                new JsonDocumentOptions { MaxDepth = RunStoreNames.MaxJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("snapshot", out var storedSnapshot)
                || storedSnapshot.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var storedText = storedSnapshot.GetRawText();
            var candidateText = Encoding.UTF8.GetString(SnapshotBytes(candidate));
            return string.Equals(storedText, candidateText, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool SnapshotsEqual(RunSnapshot left, RunSnapshot right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.SchemaVersion == right.SchemaVersion
            && left.StartingEraIndex == right.StartingEraIndex
            && Equals(left.OfferEconomy, right.OfferEconomy)
            && left.GenerationPolicy == right.GenerationPolicy
            && string.Equals(left.RunId, right.RunId, StringComparison.Ordinal)
            && left.Seed == right.Seed
            && JsonSerializer.Serialize(left.Rewards, RunStoreJson.Options) == JsonSerializer.Serialize(right.Rewards, RunStoreJson.Options)
            && string.Equals(left.CatalogueVersion, right.CatalogueVersion, StringComparison.Ordinal)
            && string.Equals(left.CostUnitId, right.CostUnitId, StringComparison.Ordinal)
            && ValueEquals(left.InitialBaseBudget, right.InitialBaseBudget)
            && SequenceEqual(left.EraSchedule, right.EraSchedule, EraEquals)
            && left.Phase == right.Phase
            && left.SuspendedFrom == right.SuspendedFrom
            && left.Round == right.Round
            && left.BlockIndex == right.BlockIndex
            && left.EraIndex == right.EraIndex
            && left.WinsInBlock == right.WinsInBlock
            && left.Victories == right.Victories
            && left.Defeats == right.Defeats
            && left.EnemiesDestroyed == right.EnemiesDestroyed
            && left.FriendliesLost == right.FriendliesLost
            && left.Attempts == right.Attempts
            && left.RoundAttempts == right.RoundAttempts
            && left.TestPool == right.TestPool && left.TestPoolOffset == right.TestPoolOffset
            && left.Ironman == right.Ironman
            && left.Score == right.Score
            && ValueEquals(left.BlockBaseBudget, right.BlockBaseBudget)
            && left.SupplyBonus == right.SupplyBonus
            && ValueEquals(left.AvailableBudget, right.AvailableBudget)
            && left.PendingSupplyPoints == right.PendingSupplyPoints
            && DesignEquals(left.ConfirmedDesign, right.ConfirmedDesign)
            && left.DesignConfirmedForBlock == right.DesignConfirmedForBlock
            && SequenceEqual(left.Offers, right.Offers, PlanEquals)
            && string.Equals(left.SelectedOfferId, right.SelectedOfferId, StringComparison.Ordinal)
            && PlanEquals(left.LockedPlan, right.LockedPlan)
            && AttemptEquals(left.CurrentAttempt, right.CurrentAttempt)
            && left.GenerationCounter == right.GenerationCounter
            && SequenceEqual(left.ProcessedAttempts, right.ProcessedAttempts, AttemptEquals)
            && SequenceEqual(left.ConsumedOfferIds, right.ConsumedOfferIds,
                (a, b) => string.Equals(a, b, StringComparison.Ordinal))
            && left.LastOutcome == right.LastOutcome
            && string.Equals(left.LastOutcomeDetail, right.LastOutcomeDetail, StringComparison.Ordinal);
    }

    private static bool ValueEquals(ScenarioValue? left, ScenarioValue? right)
        => left is null ? right is null
            : right is not null
              && left.Value == right.Value
              && string.Equals(left.UnitId, right.UnitId, StringComparison.Ordinal);

    private static bool EraEquals(EraReference? left, EraReference? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.NativeId, right.NativeId, StringComparison.Ordinal);

    private static bool ModEquals(ModRequirement? left, ModRequirement? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
              && string.Equals(left.Version, right.Version, StringComparison.Ordinal);

    private static bool BlueprintEquals(BlueprintReference? left, BlueprintReference? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.LocalId, right.LocalId, StringComparison.Ordinal)
              && string.Equals(left.SnapshotHash, right.SnapshotHash, StringComparison.Ordinal)
              && EraEquals(left.Era, right.Era)
              && SequenceEqual(left.RequiredMods, right.RequiredMods, ModEquals);

    private static bool DesignEquals(RunDesignState? left, RunDesignState? right)
        => left is null ? right is null
            : right is not null
              && BlueprintEquals(left.Blueprint, right.Blueprint)
              && ValueEquals(left.Cost, right.Cost)
              && left.FriendlyCountIncludingPlayer == right.FriendlyCountIncludingPlayer;

    private static bool EnemyEquals(EnemyUnit? left, EnemyUnit? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.BlueprintId, right.BlueprintId, StringComparison.Ordinal)
              && left.Count == right.Count;

    private static bool PlanEquals(EncounterPlan? left, EncounterPlan? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.OfferId, right.OfferId, StringComparison.Ordinal)
              && string.Equals(left.PlanHash, right.PlanHash, StringComparison.Ordinal)
              && left.Round == right.Round
              && string.Equals(left.MapId, right.MapId, StringComparison.Ordinal)
              && string.Equals(left.SpawnLayoutId, right.SpawnLayoutId, StringComparison.Ordinal)
              && EraEquals(left.Era, right.Era)
              && left.Risk == right.Risk
              && SequenceEqual(left.Enemies, right.Enemies, EnemyEquals)
              && left.SupplyReward == right.SupplyReward
            && JsonSerializer.Serialize(left.Extras) == JsonSerializer.Serialize(right.Extras)
              && SequenceEqual(left.RequiredMods, right.RequiredMods, ModEquals)
              && string.Equals(left.CatalogueVersion, right.CatalogueVersion, StringComparison.Ordinal);

    private static bool AttemptEquals(AttemptIdentity? left, AttemptIdentity? right)
        => left is null ? right is null
            : right is not null
              && string.Equals(left.RunId, right.RunId, StringComparison.Ordinal)
              && string.Equals(left.EncounterId, right.EncounterId, StringComparison.Ordinal)
              && string.Equals(left.PlanHash, right.PlanHash, StringComparison.Ordinal)
              && left.Generation == right.Generation;

    private static bool SequenceEqual<T>(
        IReadOnlyList<T>? left, IReadOnlyList<T>? right, Func<T, T, bool> equal)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (!equal(left[i], right[i])) return false;
        return true;
    }

    private static byte[] SnapshotBytes(RunSnapshot snapshot)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = false }))
        {
            JsonSerializer.Serialize(writer, snapshot, RunStoreJson.Options);
        }

        return buffer.ToArray();
    }

    public static byte[] EncodeCheckpoint(RunCheckpointEnvelope envelope)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", RunStoreNames.SchemaVersion);
            writer.WriteString("kind", RunStoreNames.CheckpointKind);
            writer.WriteNumber("revision", envelope.Revision);
            writer.WriteString("runId", envelope.RunId);
            writer.WritePropertyName("snapshot");
            JsonSerializer.Serialize(writer, envelope.Snapshot, RunStoreJson.Options);
            writer.WritePropertyName("blueprints");
            writer.WriteStartArray();
            foreach (var blueprint in envelope.Blueprints)
            {
                writer.WriteStartObject();
                writer.WriteString("hash", blueprint.Hash);
                writer.WriteNumber("length", blueprint.Length);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static RunCheckpointEnvelope DecodeCheckpoint(byte[] bytes)
    {
        using var document = ParseDocument(
            bytes,
            RunStoreNames.CheckpointKind,
            RunStoreIssueCodes.CheckpointCorrupt);
        var values = ReadObject(
            document.RootElement,
            RunStoreNames.CheckpointKind,
            RunStoreIssueCodes.CheckpointCorrupt,
            "schemaVersion",
            "kind",
            "revision",
            "runId",
            "snapshot",
            "blueprints");

        ReadSchemaVersion(
            values,
            RunStoreNames.CheckpointKind,
            RunStoreIssueCodes.CheckpointCorrupt);

        if (RequireString(values, "kind", RunStoreIssueCodes.CheckpointCorrupt)
            != RunStoreNames.CheckpointKind)
        {
            throw Corrupt("Checkpoint kind is not 'runCheckpoint'.");
        }

        var revision = RequireInt64(values, "revision", RunStoreIssueCodes.CheckpointCorrupt);
        if (revision < 0)
        {
            throw Corrupt("Checkpoint revision is negative.");
        }

        var runId = RequireString(values, "runId", RunStoreIssueCodes.CheckpointCorrupt);

        var snapshotElement = RequireProperty(
            values,
            "snapshot",
            RunStoreIssueCodes.CheckpointCorrupt);
        if (snapshotElement.ValueKind != JsonValueKind.Object)
        {
            throw Corrupt("Checkpoint snapshot must be a JSON object.");
        }

        // Probe the nested schema before shape deserialization so an unsupported
        // future snapshot is never partially reinterpreted as v1.
        if (!snapshotElement.TryGetProperty("schemaVersion", out var snapshotSchema)
            || snapshotSchema.ValueKind != JsonValueKind.Number
            || !snapshotSchema.TryGetInt32(out var nestedSchemaVersion))
        {
            throw Corrupt("Checkpoint snapshot schemaVersion is missing or malformed.");
        }

        if (nestedSchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or 7))
        {
            throw new RunStoreFormatException(
                $"Unsupported snapshot schemaVersion {nestedSchemaVersion}.",
                RunStoreIssueCodes.SchemaUnsupported,
                schemaUnsupported: true);
        }

        var blueprints = ReadBlueprints(
            RequireProperty(values, "blueprints", RunStoreIssueCodes.CheckpointCorrupt));

        RunSnapshot snapshot;
        try
        {
            snapshot = snapshotElement.Deserialize<RunSnapshot>(RunStoreJson.Options)
                ?? throw Corrupt("Checkpoint snapshot is null.");
        }
        catch (JsonException exception)
        {
            throw Corrupt($"Checkpoint snapshot is invalid: {exception.Message}");
        }

        if (snapshot.SchemaVersion != nestedSchemaVersion)
        {
            throw Corrupt("Snapshot schemaVersion does not match the checkpoint schema.");
        }

        if (!string.Equals(snapshot.RunId, runId, StringComparison.Ordinal))
        {
            throw Corrupt("Checkpoint runId does not match the snapshot runId.");
        }

        if (snapshot.ConfirmedDesign is not null)
        {
            if (!RunStoreIds.TryNormalizeHash(
                    snapshot.ConfirmedDesign.Blueprint.SnapshotHash,
                    out var designHash,
                    out _))
            {
                throw Corrupt("Design blueprint snapshot hash is not a SHA-256 hex digest.");
            }

            if (!ContainsBlueprint(blueprints, designHash))
            {
                throw Corrupt("Design blueprint bytes are not committed by this checkpoint.");
            }
        }

        return new RunCheckpointEnvelope(revision, runId, snapshot, blueprints);
    }

    public static byte[] EncodeIndex(RunStoreIndex index)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", RunStoreNames.SchemaVersion);
            writer.WriteString("kind", RunStoreNames.ActiveIndexKind);
            WriteNullableString(writer, "activeRunId", index.ActiveRunId);
            WriteNullableString(writer, "checkpointHash", index.CheckpointHash);
            writer.WriteNumber("revision", index.Revision);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static RunStoreIndex DecodeIndex(byte[] bytes)
    {
        using var document = ParseDocument(
            bytes,
            RunStoreNames.ActiveIndexKind,
            RunStoreIssueCodes.IndexCorrupt);
        var values = ReadObject(
            document.RootElement,
            RunStoreNames.ActiveIndexKind,
            RunStoreIssueCodes.IndexCorrupt,
            "schemaVersion",
            "kind",
            "activeRunId",
            "checkpointHash",
            "revision");

        ReadSchemaVersion(
            values,
            RunStoreNames.ActiveIndexKind,
            RunStoreIssueCodes.IndexCorrupt);

        if (RequireString(values, "kind", RunStoreIssueCodes.IndexCorrupt)
            != RunStoreNames.ActiveIndexKind)
        {
            throw new RunStoreFormatException(
                "Active index kind is not 'activeRunIndex'.",
                RunStoreIssueCodes.IndexCorrupt);
        }

        var activeRunId = ReadNullableString(values, "activeRunId", RunStoreIssueCodes.IndexCorrupt);
        var checkpointHash = ReadNullableString(
            values,
            "checkpointHash",
            RunStoreIssueCodes.IndexCorrupt);
        var revision = RequireInt64(values, "revision", RunStoreIssueCodes.IndexCorrupt);

        if (revision < 0)
        {
            throw new RunStoreFormatException(
                "Active index revision is negative.",
                RunStoreIssueCodes.IndexCorrupt);
        }

        if (activeRunId is null)
        {
            if (checkpointHash is not null)
            {
                throw new RunStoreFormatException(
                    "Active index has a checkpoint hash but no run id.",
                    RunStoreIssueCodes.IndexCorrupt);
            }
        }
        else
        {
            if (!RunStoreIds.TryCanonicalize(activeRunId, out _, out _))
            {
                throw new RunStoreFormatException(
                    "Active index run id is not a GUID.",
                    RunStoreIssueCodes.IndexCorrupt);
            }

            if (!RunStoreIds.TryNormalizeHash(checkpointHash, out _, out _))
            {
                throw new RunStoreFormatException(
                    "Active index checkpoint hash is not a SHA-256 hex digest.",
                    RunStoreIssueCodes.IndexCorrupt);
            }
        }

        return new RunStoreIndex(activeRunId, checkpointHash, revision);
    }

    public static byte[] EncodeRecord(RunRecord record)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", RunStoreNames.SchemaVersion);
            writer.WriteString("kind", RunStoreNames.RecordKind);
            writer.WriteString("runId", record.RunId);
            writer.WriteNumber("seed", record.Seed);
            writer.WriteString("phase", record.Phase.ToString());
            writer.WriteNumber("round", record.Round);
            writer.WriteNumber("blockIndex", record.BlockIndex);
            writer.WriteNumber("victories", record.Victories);
            writer.WriteNumber("defeats", record.Defeats);

            if (record.LastOutcome.HasValue)
            {
                writer.WriteString("lastOutcome", record.LastOutcome.Value.ToString());
            }
            else
            {
                writer.WriteNull("lastOutcome");
            }

            WriteNullableString(writer, "lastOutcomeDetail", record.LastOutcomeDetail);

            if (record.Design is null)
            {
                writer.WriteNull("design");
            }
            else
            {
                writer.WritePropertyName("design");
                JsonSerializer.Serialize(writer, record.Design, RunStoreJson.Options);
            }

            writer.WriteNumber("checkpointRevision", record.CheckpointRevision);
            writer.WriteString("checkpointHash", record.CheckpointHash);
            if (record.EnemiesDestroyed != 0) writer.WriteNumber("enemiesDestroyed", record.EnemiesDestroyed);
            if (record.FriendliesLost != 0) writer.WriteNumber("friendliesLost", record.FriendliesLost);
            if (record.Attempts != 0) writer.WriteNumber("attempts", record.Attempts);
            if (record.RoundAttempts != 0) writer.WriteNumber("roundAttempts", record.RoundAttempts);
            if (record.TestPool) writer.WriteBoolean("test_pool", true);
            if (record.Ironman) writer.WriteBoolean("ironman", true);
            if (record.Score != 0m) writer.WriteNumber("score", record.Score);
            if (record.Era != null) writer.WriteString("era", record.Era.NativeId);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static RunRecord DecodeRecord(byte[] bytes)
    {
        using var document = ParseDocument(
            bytes,
            RunStoreNames.RecordKind,
            RunStoreIssueCodes.RecordCorrupt);
        var values = ReadObject(
            document.RootElement,
            RunStoreNames.RecordKind,
            RunStoreIssueCodes.RecordCorrupt,
            "schemaVersion",
            "kind",
            "runId",
            "seed",
            "phase",
            "round",
            "blockIndex",
            "victories",
            "defeats",
            "lastOutcome",
            "lastOutcomeDetail",
            "design",
            "checkpointRevision",
            "checkpointHash",
            "enemiesDestroyed",
            "friendliesLost",
            "attempts", "roundAttempts", "ironman", "score", "era", "test_pool");

        ReadSchemaVersion(
            values,
            RunStoreNames.RecordKind,
            RunStoreIssueCodes.RecordCorrupt);

        if (RequireString(values, "kind", RunStoreIssueCodes.RecordCorrupt)
            != RunStoreNames.RecordKind)
        {
            throw new RunStoreFormatException(
                "Record kind is not 'runRecord'.",
                RunStoreIssueCodes.RecordCorrupt);
        }

        var phaseText = RequireString(values, "phase", RunStoreIssueCodes.RecordCorrupt);
        if (Array.IndexOf(Enum.GetNames(typeof(RunPhase)), phaseText) < 0)
        {
            throw new RunStoreFormatException(
                $"Record phase '{phaseText}' is unknown.",
                RunStoreIssueCodes.RecordCorrupt);
        }

        EncounterOutcome? lastOutcome = null;
        var outcomeText = ReadNullableString(
            values,
            "lastOutcome",
            RunStoreIssueCodes.RecordCorrupt);
        if (outcomeText is not null)
        {
            if (Array.IndexOf(Enum.GetNames(typeof(EncounterOutcome)), outcomeText) < 0)
            {
                throw new RunStoreFormatException(
                    $"Record lastOutcome '{outcomeText}' is unknown.",
                    RunStoreIssueCodes.RecordCorrupt);
            }

            lastOutcome = (EncounterOutcome)Enum.Parse(
                typeof(EncounterOutcome),
                outcomeText,
                ignoreCase: false);
        }

        RunDesignState? design = null;
        var designElement = RequireProperty(
            values,
            "design",
            RunStoreIssueCodes.RecordCorrupt);
        if (designElement.ValueKind != JsonValueKind.Null)
        {
            try
            {
                design = designElement.Deserialize<RunDesignState>(RunStoreJson.Options);
            }
            catch (JsonException exception)
            {
                throw new RunStoreFormatException(
                    $"Record design is invalid: {exception.Message}",
                    RunStoreIssueCodes.RecordCorrupt);
            }

            if (design is null)
            {
                throw new RunStoreFormatException(
                    "Record design must not be null when present.",
                    RunStoreIssueCodes.RecordCorrupt);
            }
        }

        return new RunRecord(
            RunStoreNames.SchemaVersion,
            RequireString(values, "runId", RunStoreIssueCodes.RecordCorrupt),
            RequireInt64(values, "seed", RunStoreIssueCodes.RecordCorrupt),
            (RunPhase)Enum.Parse(typeof(RunPhase), phaseText, ignoreCase: false),
            RequireInt32(values, "round", RunStoreIssueCodes.RecordCorrupt),
            RequireInt32(values, "blockIndex", RunStoreIssueCodes.RecordCorrupt),
            RequireInt32(values, "victories", RunStoreIssueCodes.RecordCorrupt),
            RequireInt32(values, "defeats", RunStoreIssueCodes.RecordCorrupt),
            lastOutcome,
            ReadNullableString(values, "lastOutcomeDetail", RunStoreIssueCodes.RecordCorrupt),
            design,
            RequireInt64(values, "checkpointRevision", RunStoreIssueCodes.RecordCorrupt),
            RequireString(values, "checkpointHash", RunStoreIssueCodes.RecordCorrupt))
        {
            EnemiesDestroyed = values.ContainsKey("enemiesDestroyed")
                ? RequireInt32(values, "enemiesDestroyed", RunStoreIssueCodes.RecordCorrupt) : 0,
            FriendliesLost = values.ContainsKey("friendliesLost")
                ? RequireInt32(values, "friendliesLost", RunStoreIssueCodes.RecordCorrupt) : 0,
            Attempts = values.ContainsKey("attempts")
                ? RequireInt32(values, "attempts", RunStoreIssueCodes.RecordCorrupt) : 0,
            RoundAttempts = values.ContainsKey("roundAttempts")
                ? RequireInt32(values, "roundAttempts", RunStoreIssueCodes.RecordCorrupt) : 0,
            TestPool = values.ContainsKey("test_pool") && RequireBoolean(values, "test_pool", RunStoreIssueCodes.RecordCorrupt),
            Ironman = values.ContainsKey("ironman") && RequireBoolean(values, "ironman", RunStoreIssueCodes.RecordCorrupt),
            Score = values.ContainsKey("score") ? RequireDecimal(values, "score", RunStoreIssueCodes.RecordCorrupt) : 0m,
            Era = values.ContainsKey("era") ? new EraReference(RequireString(values, "era", RunStoreIssueCodes.RecordCorrupt)) : null
        };
    }

    private static JsonDocument ParseDocument(byte[] bytes, string kind, string code)
    {
        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = RunStoreNames.MaxJsonDepth });
        }
        catch (JsonException exception)
        {
            throw new RunStoreFormatException(
                $"{kind} JSON is malformed: {exception.Message}",
                code);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadObject(
        JsonElement root,
        string kind,
        string code,
        params string[] allowed)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new RunStoreFormatException($"{kind} must be a JSON object.", code);
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                throw new RunStoreFormatException(
                    $"{kind} contains unknown property '{property.Name}'.",
                    code);
            }

            if (!values.TryAdd(property.Name, property.Value))
            {
                throw new RunStoreFormatException(
                    $"{kind} contains duplicate property '{property.Name}'.",
                    code);
            }
        }

        return values;
    }

    private static void ReadSchemaVersion(
        IReadOnlyDictionary<string, JsonElement> values,
        string kind,
        string code)
    {
        var element = RequireProperty(values, "schemaVersion", code);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var version))
        {
            throw new RunStoreFormatException($"{kind} schemaVersion is malformed.", code);
        }

        if (version != RunStoreNames.SchemaVersion)
        {
            throw new RunStoreFormatException(
                $"{kind} schemaVersion {version} is not supported.",
                RunStoreIssueCodes.SchemaUnsupported,
                schemaUnsupported: true);
        }
    }

    private static JsonElement RequireProperty(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string code)
    {
        if (!values.TryGetValue(name, out var element))
        {
            throw new RunStoreFormatException($"Required property '{name}' is missing.", code);
        }

        return element;
    }

    private static string RequireString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new RunStoreFormatException($"Property '{name}' must be a string.", code);
        }

        return element.GetString()
            ?? throw new RunStoreFormatException($"Property '{name}' must not be null.", code);
    }

    private static string? ReadNullableString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new RunStoreFormatException(
                $"Property '{name}' must be a string or null.",
                code);
        }

        return element.GetString()
            ?? throw new RunStoreFormatException($"Property '{name}' must not be null.", code);
    }

    private static long RequireInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var value))
        {
            throw new RunStoreFormatException($"Property '{name}' must be an integer.", code);
        }

        return value;
    }

    private static int RequireInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new RunStoreFormatException($"Property '{name}' must be an integer.", code);
        }

        return value;
    }

    private static bool RequireBoolean(IReadOnlyDictionary<string, JsonElement> values, string name, string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new RunStoreFormatException($"Property '{name}' must be a boolean.", code);
        return element.GetBoolean();
    }

    private static decimal RequireDecimal(IReadOnlyDictionary<string, JsonElement> values, string name, string code)
    {
        var element = RequireProperty(values, name, code);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var value) || value < 0m)
            throw new RunStoreFormatException($"Property '{name}' must be a nonnegative decimal.", code);
        return value;
    }

    private static IReadOnlyList<RunBlueprintEntry> ReadBlueprints(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Corrupt("Checkpoint blueprints must be an array.");
        }

        var entries = new List<RunBlueprintEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Corrupt("Checkpoint blueprint entry must be an object.");
            }

            var values = ReadObject(
                item,
                "Checkpoint blueprint entry",
                RunStoreIssueCodes.CheckpointCorrupt,
                "hash",
                "length");

            var hashText = RequireString(
                values,
                "hash",
                RunStoreIssueCodes.CheckpointCorrupt);
            if (!RunStoreIds.TryNormalizeHash(hashText, out var hash, out _))
            {
                throw Corrupt("Checkpoint blueprint hash is not a SHA-256 hex digest.");
            }

            var length = RequireInt64(
                values,
                "length",
                RunStoreIssueCodes.CheckpointCorrupt);
            if (length < 0)
            {
                throw Corrupt("Checkpoint blueprint length is negative.");
            }

            if (length > RunStoreNames.MaxBlueprintBytes)
            {
                throw new RunStoreFormatException(
                    "Checkpoint blueprint length exceeds the storage limit.",
                    RunStoreIssueCodes.BlueprintTooLarge);
            }

            if (!seen.Add(hash))
            {
                throw Corrupt($"Checkpoint lists blueprint '{hash}' more than once.");
            }

            entries.Add(new RunBlueprintEntry(hash, length));
        }

        return entries.AsReadOnly();
    }

    private static bool ContainsBlueprint(
        IReadOnlyList<RunBlueprintEntry> blueprints,
        string hash)
    {
        foreach (var blueprint in blueprints)
        {
            if (string.Equals(blueprint.Hash, hash, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteNullableString(
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

    private static RunStoreFormatException Corrupt(string message) =>
        new(message, RunStoreIssueCodes.CheckpointCorrupt);
}
