using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

public static class DesignCapTests
{
    public static void CatalogueJsonAcceptsPositiveCapsAndRejectsInvalidCaps()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "example.catalogue.json"));
        var capped = json.Replace("\"threatScore\": 20,", "\"threatScore\": 20, \"maxPerBattle\": 2,");
        var parsed = CatalogueJson.Load(capped);
        TestAssert.Equal<int?>(2, parsed.Eras[0].Enemies[0].MaxPerBattle);
        foreach (var value in new[] { "0", "-1", "2.5", "2147483648", "\"two\"" })
            TestAssert.Throws<CatalogueJsonException>(() => CatalogueJson.Load(
                capped.Replace("\"maxPerBattle\": 2", "\"maxPerBattle\": " + value)));
        var invalid = Definition() with { Eras = Definition().Eras.Select(e => e with
            { Enemies = e.Enemies.Select(d => d with { MaxPerBattle = 0 }).ToArray() }).ToArray() };
        TestAssert.False(CatalogueValidator.Validate(invalid, CatalogueOptions.Fixture).IsValid);
    }

    public static void ConfigParsingAndAutosaveFilter()
    {
        var warnings = new List<string>();
        var caps = DesignCapPolicy.Parse(" T-84 Oplot-M Duplet (WT-maten)=2 ; Other=12;bad;=2;Zero=0;Neg=-1;Huge=2147483648;Float=2.5;Other=3;A=B=2;;", warnings.Add);
        TestAssert.Equal(2, caps.Count);
        TestAssert.Equal(2, caps["T-84 Oplot-M Duplet (WT-maten)"]);
        TestAssert.Equal(12, caps["Other"]);
        TestAssert.Equal(8, warnings.Count, "one warning per invalid entry");
        TestAssert.False(caps.ContainsKey("other"), "native names are ordinal");
        TestAssert.Equal(0, DesignCapPolicy.Parse(null).Count);
        TestAssert.Equal(2, DesignCapPolicy.Parse(DesignCapPolicy.DefaultConfig).Single().Value);
        foreach (var path in new[] { "Autosave.blueprint", "Autosave-1.blueprint", "C:/pool/autosave2.BLUEPRINT" })
            TestAssert.True(DesignCapPolicy.SkipFactionBlueprint(path), path);
        foreach (var path in new[] { "My Autosave.blueprint", "Tank.blueprint", "Autosave.txt" })
            TestAssert.False(DesignCapPolicy.SkipFactionBlueprint(path), path);
    }

    public static void CapsCover480CombinationsAllRisksSpecialsAndBossCharacters()
    {
        var definition = Definition();
        var presets = new HashSet<SpecialPreset>();
        var bosses = new HashSet<string>();
        var risks = new HashSet<EncounterRisk>();
        var count = 0;
        for (var seed = 0; seed < 30; seed++)
        for (var era = 0; era < 4; era++)
        for (var step = 1; step <= 4; step++)
        {
            var request = Request(definition, era, step, seed * 7919 + 9917);
            var catalogue = new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture);
            var plans = catalogue.GenerateOffers(request);
            foreach (var plan in plans)
            {
                risks.Add(plan.Risk);
                if (plan.Extras?.BossCharacter is { } boss) bosses.Add(boss);
                if (plan.Extras?.Special is { } special) presets.Add(special.Preset);
                TestAssert.True(plan.Enemies.Sum(e => e.Count) <= 12, "native slot cap");
                foreach (var unit in plan.Enemies.GroupBy(e => e.BlueprintId))
                    TestAssert.True(unit.Sum(e => e.Count) <= 2, "design cap: " + unit.Key);
                TestAssert.True(SpecialBattleRules.TryValidatePlan(plan.Extras, plan.Enemies, out var error), error);
                foreach (var unit in plan.Enemies)
                {
                    var design = definition.Eras.SelectMany(e => e.Enemies).Single(d => d.Blueprint.LocalId == unit.BlueprintId);
                    TestAssert.True(plan.Risk == EncounterRisk.Dangerous || !DangerousDesignPolicy.IsDangerousOnly(design), "same allowed pool");
                }
            }
            var restored = new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture)
                .RestoreOffers(request, JsonSerializer.Deserialize<EncounterPlan[]>(JsonSerializer.Serialize(plans)));
            TestAssert.True(restored.IsSuccess, string.Join(";", restored.Diagnostics));
            count++;
        }
        TestAssert.Equal(480, count);
        TestAssert.Equal(3, risks.Count);
        TestAssert.Equal(4, bosses.Count);
        foreach (var preset in Enum.GetValues<SpecialPreset>())
            TestAssert.True(presets.Contains(preset), "covered " + preset);
        Console.WriteLine("Design caps: 480 combinations, 1200 plans, all risks / 3 yellow specials / 4 boss characters; save/restore checked.");
    }

    public static void OverflowRefillsAndExhaustedPoolStaysCapped()
    {
        var pool = Definition().Eras[0].Enemies.Take(2).ToArray();
        var cheap = pool[0];
        var other = pool[1] with { MaxPerBattle = null };
        var source = new[] { new EnemyUnit(cheap.Blueprint.LocalId, 10) };
        var filled = DesignCapPolicy.Apply(source, new[] { cheap, other }, 200m);
        TestAssert.Equal(2, filled.Single(e => e.BlueprintId == cheap.Blueprint.LocalId).Count);
        TestAssert.True(filled.Any(e => e.BlueprintId == other.Blueprint.LocalId), "replacement from same pool");
        TestAssert.True(filled.Sum(e => e.Count * (e.BlueprintId == cheap.Blueprint.LocalId ? cheap.CheckedPrice.Value : other.CheckedPrice.Value)) <= 200m, "target ceiling");
        var exhausted = DesignCapPolicy.Apply(source, new[] { cheap }, 200m);
        TestAssert.Equal(2, exhausted.Single().Count);
    }

    public static void OptionalFieldHashesSerializesAndOlderRulesIgnoreIt()
    {
        var flagged = Definition();
        var neutral = flagged with { Eras = flagged.Eras.Select(e => e with
            { Enemies = e.Enemies.Select(d => d with { MaxPerBattle = null }).ToArray() }).ToArray() };
        var json = JsonSerializer.Serialize(neutral);
        TestAssert.False(json.Contains("MaxPerBattle"), "absent field omitted");
        TestAssert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<CatalogueDefinition>(json)!));
        TestAssert.NotEqual(CatalogueHasher.Compute(neutral), CatalogueHasher.Compute(flagged));
        foreach (var version in new[] { 2, 3, 4, 5, 6 })
        foreach (var step in new[] { 1, 4 })
        {
            var request = Request(neutral, 0, step, 9917) with { RulesVersion = version };
            var left = new SeededEncounterCatalogue(neutral, CatalogueOptions.Fixture).TryGenerateOffers(request);
            var right = new SeededEncounterCatalogue(flagged, CatalogueOptions.Fixture).TryGenerateOffers(request);
            TestAssert.Equal(left.Status, right.Status, "older rules keep availability too");
            TestAssert.Equal(string.Join(";", left.Diagnostics), string.Join(";", right.Diagnostics));
            var a = left.Offers;
            var b = right.Offers;
            // Catalogue identity intentionally differs; roster and extras must not.
            TestAssert.Equal(JsonSerializer.Serialize(a.Select(p => p.Enemies)), JsonSerializer.Serialize(b.Select(p => p.Enemies)));
            TestAssert.Equal(JsonSerializer.Serialize(a.Select(p => p.Extras)), JsonSerializer.Serialize(b.Select(p => p.Extras)));
        }
    }

    private static OfferRequest Request(CatalogueDefinition d, int era, int step, long seed) =>
        new("caps-run", seed, era * 4 + step, era, d.Eras[era].Era,
            new ScenarioValue(300m, d.PriceUnitId), 0, d.Version)
        { RulesVersion = 7, EraProgressionEnabled = false };

    private static CatalogueDefinition Definition()
    {
        var eras = Enumerable.Range(0, 4).Select(index =>
        {
            var era = new EraReference("caps.era." + index);
            var maps = Enumerable.Range(0, 3).Select(m => new MapEntry(era, $"caps.map.{index}.{m}",
                TestCatalogueFactory.Hash(1000 + index * 10 + m), new[] { "caps.layout" },
                Array.Empty<ModRequirement>(), ContentQualification.Fixture)).ToArray();
            var enemies = Enumerable.Range(0, 8).Select(i => new EnemyDesignEntry(
                new BlueprintReference($"caps.design.{index}.{i}", TestCatalogueFactory.Hash(2000 + index * 10 + i), era, Array.Empty<ModRequirement>()),
                (EnemyRole)(i % 5), 20m + i * 10m, new ScenarioValue(20m + i * 10m, "caps.unit"), ContentQualification.Fixture)
                { MaxPerBattle = 2, MinimumRisk = i == 7 ? EncounterRisk.Dangerous : null }).ToArray();
            return new CatalogueEraContent(era, maps, enemies);
        }).ToArray();
        return new("caps.fixture.1", CatalogueDefinition.FixtureLabel, true, "caps.unit", eras);
    }
}
