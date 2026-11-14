using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Persistence.Tests;

internal static class V7SpecialPersistenceTests
{
    internal static void Register()
    {
        TestRunner.Add("schema 7 special plan saves and reloads with its formation",
            SpecialPlanRoundtrip);
    }

    private static void SpecialPlanRoundtrip()
    {
        var reward = new RewardItem("v7.special", RewardKind.VeteranCrew,
            RewardRarity.Special, .30m, 4, 4);
        var units = new[]
        {
            new SpecialUnit("v7.enemy.a", 4) { DeploymentRole = "Center" },
            new SpecialUnit("v7.enemy.b", 2) { DeploymentRole = "Flank" }
        };
        var formation = new SpecialBattle(SpecialPreset.Crossfire, "Crossfire",
            SpecialSpawnPattern.Split, units, "VeteranCrew", .30m, 4);
        var plan = new EncounterPlan("v7.offer", "v7.plan", 1, "v7.map", "v7.layout",
            Fixtures.Eras[0], EncounterRisk.Standard,
            units.Select(unit => new EnemyUnit(unit.BlueprintId, unit.Count)).ToArray(),
            2, Array.Empty<ModRequirement>(), Fixtures.CatalogueVersion)
        {
            Extras = new EncounterExtras(7, 0, reward)
            {
                Special = formation,
                EnemyBudgetTarget = 400m
            }
        };
        var snapshot = Fixtures.BaseSnapshot() with
        {
            SchemaVersion = 7,
            Phase = RunPhase.ChoosingEncounter,
            Rewards = new RewardState { Version = 7, Inventory = new[] { reward } },
            Offers = new[] { plan },
            SelectedOfferId = plan.OfferId
        };

        using var temp = new TempDir();
        var store = new RunStore(temp.StoreRoot);
        var saved = store.SaveCheckpoint(snapshot);
        AssertEx.True(saved.IsCommitted, "schema 7 special checkpoint saves: "
            + string.Join(";", saved.Issues.Select(issue => issue.Code + ":" + issue.Message)));
        var loaded = store.LoadRun(Fixtures.RunId, ModAvailability.None);
        AssertEx.True(loaded.CanResume, "schema 7 special checkpoint loads");
        AssertEx.Equal(7, loaded.Snapshot!.SchemaVersion, "schema 7 survives disk");
        var offer = loaded.Snapshot.Offers.Single();
        AssertEx.NotNull(offer.Extras?.Special, "the saved special formation survives disk");
        AssertEx.Equal("Crossfire", offer.Extras!.Special!.Title, "special title survives disk");
        AssertEx.Equal(SpecialSpawnPattern.Split, offer.Extras.Special.Pattern,
            "special pattern survives disk");
        AssertEx.Equal(RewardRarity.Special, offer.Extras.Reward.Rarity,
            "yellow reward rarity survives disk");
        var reloaded = loaded.Snapshot.Offers.Concat(new[] { loaded.Snapshot.LockedPlan! })
            .Where(candidate => candidate is not null)
            .ToArray();
        AssertEx.True(reloaded.All(candidate => candidate.Extras?.Special is null
                || SpecialBattleRules.TryValidatePlan(candidate.Extras, candidate.Enemies, out _)),
            "every reloaded special plan validates structurally");
    }
}
