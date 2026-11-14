using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence.Tests;

// Sanitized local fixtures only. Names belong to no shipped content and no fixture
// is presented as playable; hashes are SHA-256 of the exact fixture bytes.
internal static class Fixtures
{
    public const string CostUnit = "native-scenario-unit";
    public const string CatalogueVersion = "fixture-catalogue-v1";

    public const string RunId = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";
    public const string OtherRunId = "0f8fad5b-d9cb-469f-a165-70867728950e";

    public static readonly ModRequirement ModA = new("fixture.mod.a", "1.0.0");
    public static readonly ModRequirement ModB = new("fixture.mod.b", "2.0.0");

    public static readonly IReadOnlyList<EraReference> Eras = Array.AsReadOnly(
        new[]
        {
            new EraReference("era-fixture-wwi"),
            new EraReference("era-fixture-interwar"),
            new EraReference("era-fixture-earlywar"),
            new EraReference("era-fixture-midwar"),
            new EraReference("era-fixture-latewar"),
            new EraReference("era-fixture-coldwar")
        });

    public static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string RunDirectory(string storeRoot, string? runId = null) =>
        Path.Combine(storeRoot, "runs", Guid.Parse(runId ?? RunId).ToString("D"));

    public static string CheckpointPath(string storeRoot, string? runId = null) =>
        Path.Combine(RunDirectory(storeRoot, runId), "checkpoint.json");

    public static string PreviousCheckpointPath(string storeRoot, string? runId = null) =>
        Path.Combine(RunDirectory(storeRoot, runId), "checkpoint.previous.json");

    public static string RecordPath(string storeRoot, string? runId = null) =>
        Path.Combine(RunDirectory(storeRoot, runId), "record.json");

    public static string IndexPath(string storeRoot) =>
        Path.Combine(storeRoot, "active-run.json");

    public static string BlueprintPath(string storeRoot, string hash, string? runId = null) =>
        Path.Combine(RunDirectory(storeRoot, runId), "blueprints", hash + ".bin");

    public static BlueprintSnapshot Blob(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new BlueprintSnapshot(Sha(bytes), bytes);
    }

    public static RunDesignState Design(
        BlueprintSnapshot blob,
        params ModRequirement[] mods)
    {
        var blueprint = new BlueprintReference(
            "design-fixture-player",
            blob.SnapshotHash,
            Eras[0],
            mods);
        return new RunDesignState(
            blueprint,
            new ScenarioValue(10.000000m, CostUnit),
            2);
    }

    public static EncounterPlan Plan(
        string offerId = "offer-1",
        int round = 1,
        EncounterRisk risk = EncounterRisk.Standard,
        params ModRequirement[] mods)
    {
        return new EncounterPlan(
            offerId,
            "plan-" + offerId,
            round,
            "map-fixture-1",
            "layout-fixture-1",
            Eras[0],
            risk,
            new[] { new EnemyUnit("enemy-fixture-1", 1) },
            2,
            mods,
            CatalogueVersion);
    }

    public static AttemptIdentity Attempt(
        string encounterId,
        long generation,
        string? runId = null) =>
        new(runId ?? RunId, encounterId, "plan-" + encounterId, generation);

    public static RunSnapshot BaseSnapshot(string? runId = null)
    {
        var id = runId ?? RunId;
        return new RunSnapshot(
            1,
            id,
            1234567890123L,
            CatalogueVersion,
            CostUnit,
            new ScenarioValue(100.000000m, CostUnit),
            Eras,
            RunPhase.Building,
            null,
            1,
            0,
            0,
            0,
            0,
            0,
            new ScenarioValue(100.000000m, CostUnit),
            0.000000m,
            new ScenarioValue(100.000000m, CostUnit),
            0,
            null,
            false,
            Array.Empty<EncounterPlan>(),
            null,
            null,
            null,
            0,
            Array.Empty<AttemptIdentity>(),
            Array.Empty<string>(),
            null,
            null);
    }

    public static JsonDocument ReadJson(string path) =>
        JsonDocument.Parse(File.ReadAllBytes(path));

    public static int StoredSnapshotRound(string checkpointPath)
    {
        using var document = ReadJson(checkpointPath);
        return document.RootElement
            .GetProperty("snapshot")
            .GetProperty("round")
            .GetInt32();
    }

    public static string EnvelopeRunId(string checkpointPath)
    {
        using var document = ReadJson(checkpointPath);
        return document.RootElement.GetProperty("runId").GetString()!;
    }
}

internal static class SnapshotAssert
{
    public static void Equal(RunSnapshot expected, RunSnapshot actual)
    {
        AssertEx.Equal(expected.SchemaVersion, actual.SchemaVersion, "schemaVersion");
        AssertEx.Equal(expected.RunId, actual.RunId, "runId");
        AssertEx.Equal(expected.Seed, actual.Seed, "seed");
        AssertEx.Equal(expected.CatalogueVersion, actual.CatalogueVersion, "catalogueVersion");
        AssertEx.Equal(expected.CostUnitId, actual.CostUnitId, "costUnitId");

        AssertEx.Equal(
            expected.InitialBaseBudget.Value,
            actual.InitialBaseBudget.Value,
            "initialBaseBudget.value");
        AssertEx.Equal(
            expected.InitialBaseBudget.UnitId,
            actual.InitialBaseBudget.UnitId,
            "initialBaseBudget.unitId");

        AssertEx.SequenceEqual(
            expected.EraSchedule.Select(era => era.NativeId),
            actual.EraSchedule.Select(era => era.NativeId),
            "eraSchedule");

        AssertEx.Equal(expected.Phase, actual.Phase, "phase");
        AssertEx.Equal(expected.SuspendedFrom, actual.SuspendedFrom, "suspendedFrom");
        AssertEx.Equal(expected.Round, actual.Round, "round");
        AssertEx.Equal(expected.BlockIndex, actual.BlockIndex, "blockIndex");
        AssertEx.Equal(expected.EraIndex, actual.EraIndex, "eraIndex");
        AssertEx.Equal(expected.WinsInBlock, actual.WinsInBlock, "winsInBlock");
        AssertEx.Equal(expected.Victories, actual.Victories, "victories");
        AssertEx.Equal(expected.Defeats, actual.Defeats, "defeats");

        AssertEx.Equal(
            expected.BlockBaseBudget.Value,
            actual.BlockBaseBudget.Value,
            "blockBaseBudget.value");
        AssertEx.Equal(
            expected.BlockBaseBudget.UnitId,
            actual.BlockBaseBudget.UnitId,
            "blockBaseBudget.unitId");
        AssertEx.Equal(expected.SupplyBonus, actual.SupplyBonus, "supplyBonus");
        AssertEx.Equal(
            expected.AvailableBudget.Value,
            actual.AvailableBudget.Value,
            "availableBudget.value");
        AssertEx.Equal(
            expected.AvailableBudget.UnitId,
            actual.AvailableBudget.UnitId,
            "availableBudget.unitId");
        AssertEx.Equal(
            expected.PendingSupplyPoints,
            actual.PendingSupplyPoints,
            "pendingSupplyPoints");

        if (expected.ConfirmedDesign is null || actual.ConfirmedDesign is null)
        {
            AssertEx.Equal(
                expected.ConfirmedDesign is null,
                actual.ConfirmedDesign is null,
                "confirmedDesign presence");
        }
        else
        {
            AssertEx.Equal(
                expected.ConfirmedDesign.Blueprint.LocalId,
                actual.ConfirmedDesign.Blueprint.LocalId,
                "design.localId");
            AssertEx.Equal(
                expected.ConfirmedDesign.Blueprint.SnapshotHash,
                actual.ConfirmedDesign.Blueprint.SnapshotHash,
                "design.snapshotHash");
            AssertEx.Equal(
                expected.ConfirmedDesign.Blueprint.Era.NativeId,
                actual.ConfirmedDesign.Blueprint.Era.NativeId,
                "design.era");
            AssertEx.SequenceEqual(
                DescribeMods(expected.ConfirmedDesign.Blueprint.RequiredMods),
                DescribeMods(actual.ConfirmedDesign.Blueprint.RequiredMods),
                "design.requiredMods");
            AssertEx.Equal(
                expected.ConfirmedDesign.Cost.Value,
                actual.ConfirmedDesign.Cost.Value,
                "design.cost.value");
            AssertEx.Equal(
                expected.ConfirmedDesign.Cost.UnitId,
                actual.ConfirmedDesign.Cost.UnitId,
                "design.cost.unitId");
            AssertEx.Equal(
                expected.ConfirmedDesign.FriendlyCountIncludingPlayer,
                actual.ConfirmedDesign.FriendlyCountIncludingPlayer,
                "design.friendlyCount");
        }

        AssertEx.Equal(
            expected.DesignConfirmedForBlock,
            actual.DesignConfirmedForBlock,
            "designConfirmedForBlock");

        AssertEx.SequenceEqual(
            expected.Offers.Select(Describe),
            actual.Offers.Select(Describe),
            "offers");
        AssertEx.Equal(expected.SelectedOfferId, actual.SelectedOfferId, "selectedOfferId");
        AssertEx.Equal(
            DescribeNullable(expected.LockedPlan),
            DescribeNullable(actual.LockedPlan),
            "lockedPlan");
        AssertEx.Equal(
            DescribeNullable(expected.CurrentAttempt),
            DescribeNullable(actual.CurrentAttempt),
            "currentAttempt");
        AssertEx.Equal(
            expected.GenerationCounter,
            actual.GenerationCounter,
            "generationCounter");
        AssertEx.SequenceEqual(
            expected.ProcessedAttempts.Select(Describe),
            actual.ProcessedAttempts.Select(Describe),
            "processedAttempts");
        AssertEx.SequenceEqual(
            expected.ConsumedOfferIds,
            actual.ConsumedOfferIds,
            "consumedOfferIds");
        AssertEx.Equal(expected.LastOutcome, actual.LastOutcome, "lastOutcome");
        AssertEx.Equal(
            expected.LastOutcomeDetail,
            actual.LastOutcomeDetail,
            "lastOutcomeDetail");
    }

    public static string Describe(EncounterPlan plan) =>
        string.Join(
            "|",
            plan.OfferId,
            plan.PlanHash,
            plan.Round.ToString(CultureInfo.InvariantCulture),
            plan.MapId,
            plan.SpawnLayoutId,
            plan.Era.NativeId,
            plan.Risk.ToString(),
            string.Join(
                ",",
                plan.Enemies.Select(
                    enemy => enemy.BlueprintId + ":"
                        + enemy.Count.ToString(CultureInfo.InvariantCulture))),
            plan.SupplyReward.ToString(CultureInfo.InvariantCulture),
            string.Join(",", DescribeMods(plan.RequiredMods)),
            plan.CatalogueVersion);

    public static string Describe(AttemptIdentity attempt) =>
        string.Join(
            "|",
            attempt.RunId,
            attempt.EncounterId,
            attempt.PlanHash,
            attempt.Generation.ToString(CultureInfo.InvariantCulture));

    public static string? DescribeNullable(EncounterPlan? plan) =>
        plan is null ? null : Describe(plan);

    public static string? DescribeNullable(AttemptIdentity? attempt) =>
        attempt is null ? null : Describe(attempt);

    private static IEnumerable<string> DescribeMods(
        IReadOnlyList<ModRequirement> mods) =>
        mods.Select(mod => mod.Id + "@" + mod.Version);
}
