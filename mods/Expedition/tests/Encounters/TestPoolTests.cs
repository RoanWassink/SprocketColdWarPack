using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Run;

namespace EncountersTests;

public static class TestPoolTests
{
    static readonly byte[] Bytes = Encoding.UTF8.GetBytes("test pool player fixture");
    static readonly string Hash = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    static CatalogueDefinition Definition(int count = 7)
    {
        var basis = TestCatalogueFactory.CreateExample();
        return basis with { TestPool = Enumerable.Range(0, count).Reverse().Select(i => new TestPoolDesign($"Tank {i:D2}",
            new EnemyDesignEntry(new BlueprintReference($"test.tank.{i:D2}", new string('a', 64), basis.Eras[^1].Era,
                Array.Empty<ModRequirement>()), EnemyRole.Heavy, 1_000_000m + i,
                new ScenarioValue(1_000_000m + i, basis.PriceUnitId), ContentQualification.Fixture))).ToArray() };
    }
    static OfferRequest Request(CatalogueDefinition d, int round = 1, int offset = 0, int era = 0) =>
        new("00000001-0000-4000-8000-000000000001", 987, round, (round - 1) / 4, d.Eras[era].Era, new ScenarioValue(1m, d.PriceUnitId), 0, d.Version)
        { RulesVersion = 6, TestPool = true, TestPoolOffset = offset };
    static IReadOnlyList<EncounterPlan> Offers(CatalogueDefinition d, OfferRequest r) =>
        new SeededEncounterCatalogue(d, CatalogueOptions.Fixture).GenerateOffers(r);
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void Ok(RunActionResult result) => Check(result.IsAccepted, result.Reason);
    static RunDirector Director(CatalogueDefinition definition, int start = 0)
    {
        var run = new RunDirector();
        Ok(run.NewRun(new RunConfiguration("00000001-0000-4000-8000-000000000001", 987, new ScenarioValue(100m, definition.PriceUnitId),
            definition.Eras.Select(e => e.Era).ToArray(), definition.Version)
            { AdventureRules = true, AdventureVersion = 6, TestPool = true, StartingEraIndex = start }));
        return run;
    }
    static void Prepare(RunDirector run, CatalogueDefinition d)
    {
        var s = run.Snapshot!;
        if (s.Phase == RunPhase.LoadingEncounter) return;
        var q = new DesignQuote(new BlueprintReference("player", Hash, s.EraSchedule[s.EraIndex], Array.Empty<ModRequirement>()),
            new ScenarioValue(10m, s.CostUnitId), true, true, null);
        if (!s.DesignConfirmedForBlock)
            Ok(s.Phase == RunPhase.ChoosingEncounter ? run.ConfirmEncounterDesign(q) : run.ConfirmDesign(q));
        if (run.Snapshot!.Offers.Count == 0) Ok(run.SetOffers(Offers(d, run.CreateOfferRequest())));
        Ok(run.SelectEncounter(run.Snapshot.Offers[0].OfferId));
    }
    static void Finish(RunDirector run, EncounterOutcome outcome)
    {
        if (run.Snapshot!.Phase != RunPhase.LoadingEncounter) Ok(run.BeginEncounter());
        var req = run.CreateEncounterRequest();
        Check(req.TestPool, "adapter must receive immutable run flag");
        if (outcome != EncounterOutcome.PrecombatAborted) Ok(run.ReportCombatStarted(req.Attempt));
        Ok(run.AcceptResult(new EncounterResult(req.Attempt, req.Plan, outcome, "fixture")));
    }

    public static void TestOffersRotateDistinctSortedAndIgnoreBudgetInEveryEra()
    {
        var d = Definition();
        foreach (var era in Enumerable.Range(0, d.Eras.Count))
        {
            var a = Offers(d, Request(d, era: era));
            Check(a.Count == 3 && a.All(p => p.Enemies.Select(e => e.BlueprintId).SequenceEqual(new[] { "test.tank.00", "test.tank.01", "test.tank.02", "test.tank.03" })), "every card holds the sorted first four");
            Check(a.All(p => p.Enemies.All(e => e.Count == 1)), "one copy per tank");
            Check(Offers(d, Request(d, 2, 4, era))[0].Enemies.Select(e => e.BlueprintId)
                .SequenceEqual(new[] { "test.tank.04", "test.tank.05", "test.tank.06", "test.tank.00" }), "second page wraps distinct");
            Check(Offers(Definition(3), Request(Definition(3), era: era))[0].Enemies.Count == 3, "small pool caps tanks per battle");
        }
    }
    public static void TestBossUsesNextTankOnlyAndKeepsBossPresentation()
    {
        var d = Definition(); var boss = Offers(d, Request(d, 4, 9)).Single();
        Check(boss.Enemies.Count == 4 && boss.Enemies[0] == new EnemyUnit("test.tank.02", 1), "boss cursor");
        Check(boss.Extras is { CuratedBoss: true, BossBlueprintId: "test.tank.02", BossCharacter: "Tank 02" }, "boss metadata");
        Check(Offers(d, Request(d, 16, 9, 2)).Count == 3, "endless fourth round has three test cards");
    }
    static string[] Tanks(EncounterPlan plan) => plan.Enemies.Select(e => e.BlueprintId).ToArray();
    static string[] Page(int offset) => Enumerable.Range(0, 4).Select(j => $"test.tank.{(offset + j) % 7:D2}").ToArray();

    // 0.3.4 (user: "Yes, after any fight"): Victory, defeat and retreat advance
    // the cursor by the tanks fought; aborted and technical attempts do not.
    public static void TestCursorAdvancesAfterEveryFoughtRoundAndSurvivesRestore()
    {
        var d = Definition(); var run = Director(d); Prepare(run, d);
        var first = JsonSerializer.Serialize(run.Snapshot!.Offers);
        foreach (var notFought in new[] { EncounterOutcome.PrecombatAborted, EncounterOutcome.TechnicalFailure })
        {
            Finish(run, notFought);
            Check(run.Snapshot!.TestPoolOffset == 0, notFought + " never advances the cursor");
            Check(JsonSerializer.Serialize(run.Snapshot.Offers) == first, notFought + " keeps identical offers");
            run = RunDirector.Restore(run.Snapshot);
            if (run.Snapshot!.Phase == RunPhase.Suspended) Ok(run.Resume());
            Prepare(run, d);
        }
        var offset = 0;
        foreach (var loss in new[] { EncounterOutcome.FriendlyForceDestroyed, EncounterOutcome.CombatRetreat })
        {
            var before = run.Snapshot!;
            Finish(run, loss); offset += 4;
            var s = run.Snapshot!;
            Check(s.TestPoolOffset == offset, loss + " advances by the four tanks fought");
            Check(s.Phase == RunPhase.ChoosingEncounter && s.Offers.Count == 0 && s.SelectedOfferId == null
                && s.OfferEconomy == null && s.LockedPlan == null, loss + " clears the old cursor's cards");
            Check(s.Round == before.Round && s.Defeats == before.Defeats + 1 && s.RoundAttempts == before.RoundAttempts + 1
                && s.Victories == 0 && s.Rewards!.GrantedOffers.Count == 0 && s.ConsumedOfferIds.Count == 0, loss + ": same round, no reward");
            run = RunDirector.Restore(s);
            Prepare(run, d);
            Check(run.Snapshot!.Offers.Count == 3 && run.Snapshot.Offers.All(p => Tanks(p).SequenceEqual(Page(offset))), loss + " re-offers the round from the next tanks");
        }
        Finish(run, EncounterOutcome.Victory);
        Check(run.Snapshot!.TestPoolOffset == 12, "normal victory advances four");
        for (var i = 0; i < 3; i++)
        {
            if (run.Snapshot!.Rewards!.Choices.Count > 0) Ok(run.SkipRewardChoice());
            Prepare(run, d); Finish(run, EncounterOutcome.Victory);
        }
        Check(run.Snapshot!.TestPoolOffset == 24, "boss victory advances four");
        Check(RunDirector.Restore(run.Snapshot).Snapshot!.TestPoolOffset == 24, "restored cursor");
    }
    public static void TestBossLossReoffersBossRoundFromNextTank()
    {
        var d = Definition(); var run = Director(d);
        for (var i = 0; i < 3; i++)
        {
            if (run.Snapshot!.Rewards!.Choices.Count > 0) Ok(run.SkipRewardChoice());
            Prepare(run, d); Finish(run, EncounterOutcome.Victory);
        }
        if (run.Snapshot!.Rewards!.Choices.Count > 0) Ok(run.SkipRewardChoice());
        Prepare(run, d); Finish(run, EncounterOutcome.FriendlyForceDestroyed);
        Prepare(run, d);
        var boss = run.Snapshot!.Offers.Single();
        Check(run.Snapshot.TestPoolOffset == 16 && run.Snapshot.Round == 4 && Tanks(boss).SequenceEqual(Page(16)), "boss round re-offered from offset 16");
        Check(boss.Extras is { CuratedBoss: true } && boss.Extras.BossBlueprintId == Page(16)[0] && run.Snapshot.SelectedOfferId == boss.OfferId, "next boss tank selected");
    }
    public static void TestIronmanTestLossEndsRunAndAdvances()
    {
        var d = Definition(); var run = new RunDirector();
        Ok(run.NewRun(new RunConfiguration("00000001-0000-4000-8000-000000000001", 987, new ScenarioValue(100m, d.PriceUnitId),
            d.Eras.Select(e => e.Era).ToArray(), d.Version) { AdventureRules = true, AdventureVersion = 6, TestPool = true, Ironman = true }));
        Prepare(run, d); Finish(run, EncounterOutcome.FriendlyForceDestroyed);
        Check(run.Snapshot!.Phase == RunPhase.Lost && run.Snapshot.TestPoolOffset == 4 && run.Snapshot.Defeats == 1, "Ironman loss is still terminal");
        Check(RunDirector.Restore(run.Snapshot).Snapshot!.Phase == RunPhase.Lost, "terminal test snapshot validates");
    }
    // Every fought round adds at most TanksPerBattle and one Victory or Defeat,
    // so offset <= 4 * (Victories + Defeats). The 0.3.1 formula stays accepted.
    public static void TestSnapshotCheckBoundsCursorByFoughtRounds()
    {
        var d = Definition(); var run = Director(d); Prepare(run, d);
        Finish(run, EncounterOutcome.FriendlyForceDestroyed); Prepare(run, d); Finish(run, EncounterOutcome.CombatRetreat);
        var s = run.Snapshot!;
        Check(s.TestPoolOffset == 8 && s.Defeats == 2 && s.Victories == 0, "two fought losses");
        static bool Valid(RunSnapshot snapshot)
        {
            try { RunDirector.Restore(snapshot); return true; }
            catch (ArgumentException) { return false; }
        }
        Check(Valid(s) && Valid(s with { TestPoolOffset = 5 }), "offset up to 4 x (V + D) accepted");
        Check(Valid(s with { TestPoolOffset = 0 }), "0.3.1/0.3.3 cursor without loss advance accepted (no migration)");
        Check(!Valid(s with { TestPoolOffset = 9 }), "offset above 4 x (V + D) rejected");
        Check(!Valid(s with { Defeats = 1 }), "defeats bound the cursor");
        Check(!Valid(s with { TestPoolOffset = -1 }), "negative cursor rejected");
    }
    public static void TestLateStartBeginsAtFirstTankAndRerollKeepsTanks()
    {
        var d = Definition(); var run = Director(d, 2); Prepare(run, d);
        Check(run.Snapshot!.Offers[0].Enemies[0].BlueprintId == "test.tank.00", "late start sees first tank");
        var request = Request(d) with { OfferRoll = 19, EnemyBudgetMultiplier = 2m };
        Check(Offers(d, request).Select(p => p.Enemies[0]).SequenceEqual(Offers(d, Request(d)).Select(p => p.Enemies[0])), "reroll/difficulty cannot skip designs");
    }
    public static void TestRecordAndCheckpointPersistFlagAndCursor()
    {
        var root = Path.Combine(Path.GetTempPath(), "expedition-testpool-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = Definition(); var run = Director(d); Prepare(run, d); Finish(run, EncounterOutcome.Victory);
            Ok(run.Retire());
            var store = new RunStore(root);
            var saved = store.SaveCheckpoint(run.Snapshot!, new[] { new BlueprintSnapshot(Hash, Bytes) });
            Check(saved.IsCommitted, "checkpoint committed: " + string.Join(";", saved.Issues));
            var loaded = store.ListRuns().Runs.Single();
            Check(loaded.TestPool && store.ReadRecord(run.Snapshot!.RunId).Record?.TestPool == true, "record flag readback");
            var text = File.ReadAllText(Path.Combine(root, "runs", run.Snapshot!.RunId, "record.json"));
            Check(text.Contains("test_pool"), "record serialized flag");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static void TestPoolOffPreservesCatalogueHashAndOfferBytes()
    {
        var d = TestCatalogueFactory.CreateExample(); var explicitOff = d with { TestPool = null };
        Check(CatalogueHasher.Compute(d) == CatalogueHasher.Compute(explicitOff), "normal hash");
        var r = Request(d) with { TestPool = false, AvailableBudget = new ScenarioValue(200m, d.PriceUnitId) };
        Check(JsonSerializer.Serialize(Offers(d, r)) == JsonSerializer.Serialize(Offers(explicitOff, r with { TestPool = false })), "normal offers exact bytes");
        Check(!JsonSerializer.Serialize(d).Contains("TestPool"), "optional catalogue section omitted");
    }
    public static void TestPinnedPoolRejectsTamperedOffersAndInvalidEntries()
    {
        var d = Definition(); var r = Request(d); var plans = Offers(d, r);
        Check(new SeededEncounterCatalogue(d, CatalogueOptions.Fixture).RestoreOffers(r, plans).IsSuccess, "restore exact pool");
        var changed = d with { TestPool = d.TestPool!.Select(t => t with { Name = "changed " + t.Name }).ToArray() };
        Check(!new SeededEncounterCatalogue(changed, CatalogueOptions.Fixture).RestoreOffers(r, plans).IsSuccess, "tampered pinned pool rejected");
        Check(!CatalogueValidator.Validate(d with { TestPool = new[] { d.TestPool![0], d.TestPool[0] } }, CatalogueOptions.Fixture).IsValid, "duplicates rejected");
        var invalidPrice = d.TestPool![0] with { Design = d.TestPool[0].Design with { CheckedPrice = null! } };
        Check(!CatalogueValidator.Validate(d with { TestPool = new[] { invalidPrice } }, CatalogueOptions.Fixture).IsValid, "missing test price rejected without throwing");
        try { Offers(Definition(2), Request(Definition(2))); throw new Exception("short pool accepted"); }
        catch (EncounterAvailabilityException) { }
    }
}

