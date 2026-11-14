using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class RandomAndHashTests
{
    public static void StableRandom_IsDeterministicForSeedRoundCard()
    {
        var first = new StableRandom(123456789L, 7, 2);
        var second = new StableRandom(123456789L, 7, 2);

        for (int index = 0; index < 128; index++)
        {
            TestAssert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    public static void StableRandom_DiffersAcrossSeedRoundCardAndRoundStream()
    {
        var baseSequence = Sequence(new StableRandom(10L, 2, 1), 32);

        TestAssert.False(baseSequence.SequenceEqual(
            Sequence(new StableRandom(11L, 2, 1), 32)),
            "different seeds must produce different streams");
        TestAssert.False(baseSequence.SequenceEqual(
            Sequence(new StableRandom(10L, 3, 1), 32)),
            "different rounds must produce different streams");
        TestAssert.False(baseSequence.SequenceEqual(
            Sequence(new StableRandom(10L, 2, 2), 32)),
            "different card indices must produce different streams");
        TestAssert.False(baseSequence.SequenceEqual(
            Sequence(StableRandom.ForRound(10L, 2), 32)),
            "round stream must be domain-separated from card streams");
    }

    public static void StableRandom_NextIntIsInRange()
    {
        var random = new StableRandom(99L, 5, 0);
        for (int index = 0; index < 5000; index++)
        {
            TestAssert.InRange(0, 6, random.NextInt(7));
        }

        TestAssert.Equal(0, random.NextInt(1));
        TestAssert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
        TestAssert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(-1));
    }

    public static void StableRandom_RejectsInvalidRoundAndCard()
    {
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new StableRandom(1L, 0, 0));
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => new StableRandom(1L, 1, -1));
    }

    public static void PlanHasher_IsCanonicalForReorderedEnemiesAndMods()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var request = TestCatalogueFactory.Request(
            round: 1, seed: 5, budget: 400m,
            era: TestCatalogueFactory.WwiEra);
        var offers = catalogue.GenerateOffers(request);
        var plan = offers[1];

        var reordered = plan with
        {
            Enemies = plan.Enemies.Reverse().ToArray(),
            RequiredMods = plan.RequiredMods.Reverse().ToArray()
        };

        string contentHash = CatalogueHasher.Compute(definition);
        TestAssert.Equal(
            PlanHasher.ComputeContentHash(contentHash, plan),
            PlanHasher.ComputeContentHash(contentHash, reordered));
        TestAssert.Equal(
            PlanHasher.ComputePlanHash(contentHash, plan),
            PlanHasher.ComputePlanHash(contentHash, reordered));
    }

    public static void PlanHasher_OfferIdIsStableAndRunScoped()
    {
        var definition = TestCatalogueFactory.CreateExample();
        var catalogue = TestCatalogueFactory.CreateCatalogue(definition);
        var request = TestCatalogueFactory.Request(
            round: 4, seed: 11, runId: "run-alpha", budget: 500m,
            era: TestCatalogueFactory.InterwarEra);
        var plan = catalogue.GenerateOffers(request).Single(
            offer => offer.Risk == EncounterRisk.Standard);

        string contentHash = CatalogueHasher.Compute(definition);
        string repeat = PlanHasher.CreateOfferId("run-alpha", contentHash, plan);
        string otherRun = PlanHasher.CreateOfferId("run-beta", contentHash, plan);

        TestAssert.Equal(plan.OfferId, repeat);
        TestAssert.NotEqual(plan.OfferId, otherRun);
        TestAssert.True(PlanHasher.IsSafeOfferId(plan.OfferId));
        TestAssert.Contains(".04.standard.", plan.OfferId);
        TestAssert.False(PlanHasher.IsSafeOfferId(plan.OfferId.ToUpperInvariant()));
        TestAssert.False(PlanHasher.IsSafeOfferId("../enc.injected"));
        TestAssert.False(PlanHasher.IsSafeOfferId(string.Empty));
    }

    public static void CatalogueHasher_ChangesWithCheckedContent()
    {
        var definition = TestCatalogueFactory.CreateExample();
        string original = CatalogueHasher.Compute(definition);

        var era = definition.Eras[0];
        var changedEnemy = era.Enemies[0] with
        {
            ThreatScore = era.Enemies[0].ThreatScore + 1m
        };
        var changedEra = era with
        {
            Enemies = era.Enemies
                .Select((enemy, index) => index == 0 ? changedEnemy : enemy)
                .ToArray()
        };
        var changed = definition with
        {
            Eras = definition.Eras
                .Select((entry, index) => index == 0 ? changedEra : entry)
                .ToArray()
        };

        TestAssert.NotEqual(original, CatalogueHasher.Compute(changed));

        var changedMap = definition.Eras[0].Maps[0] with
        {
            ContentHash = TestCatalogueFactory.Hash(9999)
        };
        var mapChangedEra = definition.Eras[0] with
        {
            Maps = new[] { changedMap }
                .Concat(definition.Eras[0].Maps.Skip(1))
                .ToArray()
        };
        var mapChanged = definition with
        {
            Eras = new[] { mapChangedEra }
                .Concat(definition.Eras.Skip(1))
                .ToArray()
        };

        TestAssert.NotEqual(original, CatalogueHasher.Compute(mapChanged));
    }

    public static void ContentSafety_RejectsPathsAndTraversal()
    {
        TestAssert.True(ContentSafety.IsSafeLocalReference("fixture.map.a"));
        TestAssert.True(ContentSafety.IsSafeLocalReference("a"));
        TestAssert.True(ContentSafety.IsSafeLocalReference("a-b_c.1"));

        TestAssert.False(ContentSafety.IsSafeLocalReference("../escape"));
        TestAssert.False(ContentSafety.IsSafeLocalReference("/absolute/path"));
        TestAssert.False(ContentSafety.IsSafeLocalReference("C:\\absolute"));
        TestAssert.False(ContentSafety.IsSafeLocalReference("has space"));
        TestAssert.False(ContentSafety.IsSafeLocalReference(".."));
        TestAssert.False(ContentSafety.IsSafeLocalReference(string.Empty));
        TestAssert.False(ContentSafety.IsSafeLocalReference(null));
    }

    public static void ContentSafety_ValidatesHashesAndVersions()
    {
        TestAssert.True(ContentSafety.IsHexHash(TestCatalogueFactory.Hash(1)));
        TestAssert.False(ContentSafety.IsHexHash(new string('0', 63)));
        TestAssert.False(ContentSafety.IsHexHash(new string('z', 64)));
        TestAssert.False(ContentSafety.IsHexHash(null));

        TestAssert.True(ContentSafety.IsSafeVersion("1.0.0+build"));
        TestAssert.False(ContentSafety.IsSafeVersion("1 0"));
        TestAssert.False(ContentSafety.IsSafeVersion("../1"));
        TestAssert.False(ContentSafety.IsSafeVersion(string.Empty));
    }

    public static void EncounterRules_ThreatTargetRoundsDownAndSuppliesMatchRisk()
    {
        TestAssert.Equal(58m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(100m), 1, EncounterRisk.Cautious));
        TestAssert.Equal(59m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(101m), 1, EncounterRisk.Cautious));
        TestAssert.Equal(90m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(100m), 2, EncounterRisk.Standard));
        TestAssert.Equal(103m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(100m), 2, EncounterRisk.Dangerous));
        TestAssert.Equal(103m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(100m), 3, EncounterRisk.Cautious));
        TestAssert.Equal(132m, EncounterRules.ThreatTarget(
            TestCatalogueFactory.Budget(100m), 3, EncounterRisk.Dangerous));

        TestAssert.Equal(0.65m, EncounterRules.WithinBlockMultiplier(1));
        TestAssert.Equal(0.90m, EncounterRules.WithinBlockMultiplier(2));
        TestAssert.Equal(1.15m, EncounterRules.WithinBlockMultiplier(3));
        TestAssert.Equal(0.65m, EncounterRules.WithinBlockMultiplier(4));

        TestAssert.Equal(1, EncounterRules.Profile(EncounterRisk.Cautious).SupplyReward);
        TestAssert.Equal(2, EncounterRules.Profile(EncounterRisk.Standard).SupplyReward);
        TestAssert.Equal(3, EncounterRules.Profile(EncounterRisk.Dangerous).SupplyReward);

        TestAssert.Equal(0, EncounterRules.BlockForRound(1));
        TestAssert.Equal(0, EncounterRules.BlockForRound(3));
        TestAssert.Equal(1, EncounterRules.BlockForRound(4));
        TestAssert.Equal(5, EncounterRules.BlockForRound(18));
        TestAssert.Equal(6, EncounterRules.BlockForRound(19));
        TestAssert.True(EncounterRules.IsEndlessBlock(6));
        TestAssert.False(EncounterRules.IsEndlessBlock(5));
        TestAssert.Throws<ArgumentOutOfRangeException>(
            () => EncounterRules.BlockForRound(0));
    }

    private static ulong[] Sequence(StableRandom random, int count)
    {
        var values = new ulong[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = random.NextUInt64();
        }

        return values;
    }
}
