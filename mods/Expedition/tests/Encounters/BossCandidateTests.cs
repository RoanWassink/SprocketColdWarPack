using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Game;

namespace EncountersTests;
public static class BossCandidateTests
{
    private static readonly BossCandidateDesign[] Designs = {
        new("a", "A", "next"), new("b", "B", "next"), new("old", "Old", "current") };
    public static void ParsesMultipleNamesAndIds()
    {
        var warnings = new List<string>();
        var ids = CampaignBossCandidatePolicy.Resolve(" A |b|A ", "current", "next", Designs, warnings.Add);
        TestAssert.Equal("a|b", string.Join("|", ids));
        TestAssert.Equal(0, warnings.Count);
    }
    public static void InvalidAndWrongEraWarnOnceEachAndDrop()
    {
        var warnings = new List<string>();
        var ids = CampaignBossCandidatePolicy.Resolve("A|Missing|Missing|Old|B", "current", "next", Designs, warnings.Add);
        TestAssert.Equal("a|b", string.Join("|", ids));
        TestAssert.Equal(2, warnings.Count);
    }
    public static void SingleAndNoValidCandidates()
    {
        TestAssert.Equal("a", CampaignBossCandidatePolicy.Resolve("A", "current", "next", Designs).Single());
        TestAssert.Equal(0, CampaignBossCandidatePolicy.Resolve("Old|Missing", "current", "next", Designs).Count);
    }
    public static void AmbiguousNameIsDroppedWithOneWarning()
    {
        var warnings = new List<string>();
        var ids = CampaignBossCandidatePolicy.Resolve("A|B", "current", "next",
            Designs.Append(new BossCandidateDesign("duplicate", "A", "next")), warnings.Add);
        TestAssert.Equal("b", ids.Single()); TestAssert.Equal(1, warnings.Count);
    }
    private static CatalogueDefinition Definition(bool multiple = true)
    {
        var d = TestCatalogueFactory.CreateExample();
        var ids = d.Eras[1].Enemies.Take(2).Select(e => e.Blueprint.LocalId).ToArray();
        return d with {
            BossBlueprints = new Dictionary<string,string> { [d.Eras[0].Era.NativeId] = ids[0] },
            BossCandidates = multiple ? new Dictionary<string,IReadOnlyList<string>> { [d.Eras[0].Era.NativeId] = ids } : null };
    }
    public static void StableAndBothCandidatesAcross256Seeds()
    {
        var d = Definition(); var seen = new Dictionary<string,int>();
        for (var seed = 0; seed < 256; seed++)
        {
            var a = BossSelection.Select(d, 0, ContentQualification.Fixture, seed, 4, 7);
            var b = BossSelection.Select(d, 0, ContentQualification.Fixture, seed, 4, 7);
            TestAssert.Equal(a.Blueprint.LocalId, b.Blueprint.LocalId);
            seen[a.Blueprint.LocalId] = seen.GetValueOrDefault(a.Blueprint.LocalId) + 1;
        }
        TestAssert.Equal(2, seen.Count);
        TestAssert.True(seen.Values.All(n => n > 80 && n < 176));
        Console.WriteLine("boss distribution (256 seeds): " + string.Join("; ", seen.Select(p => p.Key + "=" + p.Value)));
    }
    public static void PlannerUsesSameBossAcrossRollsAndRestore()
    {
        var d = Definition();
        var expected = BossSelection.Select(d, 0, ContentQualification.Fixture, 77, 4, 7).Blueprint.LocalId;
        for (var roll = 0; roll < 4; roll++)
        {
            var request = TestCatalogueFactory.Request(4, seed:77, block:0) with { RulesVersion=7, OfferRoll=roll };
            var plans = new SeededEncounterCatalogue(d, CatalogueOptions.Fixture).GenerateOffers(request);
            TestAssert.True(plans.All(p => p.Extras?.BossBlueprintId == expected && p.Extras.PlaceholderBoss == false));
            TestAssert.True(new SeededEncounterCatalogue(d, CatalogueOptions.Fixture).RestoreOffers(request, plans).IsSuccess);
        }
    }
    public static void V6IgnoresCandidatesAndSingleRetainsBoss()
    {
        var d = Definition(); var old = Definition(false);
        for (var seed=0; seed<20; seed++)
        {
            TestAssert.Equal(BossSelection.Select(old,0,ContentQualification.Fixture).Blueprint.LocalId,
                BossSelection.Select(d,0,ContentQualification.Fixture,seed,4,6).Blueprint.LocalId);
            TestAssert.Equal(BossSelection.Select(old,0,ContentQualification.Fixture).Blueprint.LocalId,
                BossSelection.Select(old,0,ContentQualification.Fixture,seed,4,7).Blueprint.LocalId);
        }
    }
    public static void AbsentFieldHasLegacyBytesAndHashAndArchiveRoundTrips()
    {
        var old = Definition(false);
        var bytes = JsonSerializer.Serialize(old);
        TestAssert.False(bytes.Contains("BossCandidates"));
        var legacy = JsonSerializer.Serialize(new { old.Version, old.Label, old.IsFixture,
            old.PriceUnitId, old.Eras, old.BossBlueprints });
        TestAssert.Equal(legacy, bytes);
        var restored = JsonSerializer.Deserialize<CatalogueDefinition>(bytes)!;
        TestAssert.Equal(CatalogueHasher.Compute(old), CatalogueHasher.Compute(restored));
        TestAssert.Equal(ThematicFormationTests.LegacyCatalogueHash(old), CatalogueHasher.Compute(old));
        var multi = Definition();
        TestAssert.False(CatalogueHasher.Compute(multi) == CatalogueHasher.Compute(old));
        var saved = JsonSerializer.Deserialize<CatalogueDefinition>(JsonSerializer.Serialize(multi))!;
        TestAssert.Equal(CatalogueHasher.Compute(multi), CatalogueHasher.Compute(saved));
        TestAssert.Equal(BossSelection.Select(multi,0,ContentQualification.Fixture,77,4,7).Blueprint.LocalId,
            BossSelection.Select(saved,0,ContentQualification.Fixture,77,4,7).Blueprint.LocalId);
    }
}
