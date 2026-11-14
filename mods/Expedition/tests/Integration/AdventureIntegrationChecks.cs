using System.Security.Cryptography;
using System.Text;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration.Tests;

internal static class AdventureIntegrationChecks
{
    internal static async Task Policy5ContractRoundtrip(string root)
    {
        const string unit = "fixture.policy5.contract.unit";
        var eras = Enumerable.Range(0, 6)
            .Select(index => new EraReference("fixture.policy5.contract.era." + index)).ToArray();
        var mods = Array.Empty<ModRequirement>();
        var definition = new CatalogueDefinition("fixture.policy5.contract", CatalogueDefinition.FixtureLabel,
            true, unit, eras.Select(era => new CatalogueEraContent(era,
                Enumerable.Range(0, 3).Select(index => new MapEntry(era,
                    "fixture.policy5.contract.map." + index, new string('c', 64),
                    new[] { "fixture.policy5.contract.layout." + index }, mods,
                    ContentQualification.Fixture)).ToArray(),
                new[]
                {
                    Design(era, "line", EnemyRole.Line, 100m),
                    Design(era, "heavy", EnemyRole.Heavy, 180m),
                    Design(era, "recon", EnemyRole.Recon, 20m),
                    Design(era, "support", EnemyRole.Support, 50m)
                })).ToArray());
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var catalogue = new SeededEncounterCatalogue(definition, options);
        var store = new RunStore(root);
        var blob = Blob("fixture policy5 contract design");
        var director = new RunDirector();
        Assert.True(director.NewRun(new RunConfiguration(
            Guid.NewGuid().ToString("D"), 77L, new ScenarioValue(400m, unit), eras,
            definition.Version) { AdventureRules = true, AdventureVersion = 5 }).IsAccepted,
            "policy-5 contract fixture starts");
        Assert.True(director.ConfirmDesign(new DesignQuote(
            new BlueprintReference("fixture.policy5.player", blob.SnapshotHash, eras[0], mods),
            new ScenarioValue(10m, unit), true, true, null)).IsAccepted,
            "policy-5 contract design confirms");
        var snapshot = director.Snapshot!;
        var contract = new RewardItem("fixture.policy5.contract", RewardKind.ExperimentalContract,
            RewardRarity.Rare, .35m, 4, 4);
        snapshot = snapshot with
        {
            Phase = RunPhase.ChoosingEncounter,
            AvailableBudget = new ScenarioValue(540m, unit),
            Rewards = snapshot.Rewards! with
            {
                Revision = snapshot.Rewards!.Revision + 1,
                Active = new[] { contract }
            }
        };
        Assert.True(store.SaveCheckpoint(snapshot, new[] { blob }).IsCommitted,
            "policy-5 contract checkpoint saves");

        var presenter = new RecordingPresenter();
        var controller = new ExpeditionController(catalogue, store, new FakeAdapter(unit), presenter,
            () => ModAvailability.None, () => Guid.NewGuid().ToString("D"), () => 77L,
            () => new DifficultySettings(1.5m, true));
        await controller.ResumeAsync();
        var generated = controller.Snapshot!;
        Assert.True(generated.OfferEconomy is { EnemyBudgetMultiplier: 1.8m },
            "active ExperimentalContract multiplies the selected difficulty instead of being dropped");
        Assert.True(generated.GenerationPolicy == 5 && generated.Offers.All(offer =>
                offer.Extras?.GenerationPolicy == 5),
            "existing v5 run pins its policy on the generated set");
        Assert.True(generated.Offers.All(offer =>
                offer.Extras?.EnemyBudgetTarget is { } target && target > 0m),
            "controller generation pins the honest policy target");
        var hashes = generated.Offers.Select(offer => offer.OfferId + ":" + offer.PlanHash).ToArray();

        var changedPresenter = new RecordingPresenter();
        var changed = new ExpeditionController(catalogue, store, new FakeAdapter(unit), changedPresenter,
            () => ModAvailability.None, () => Guid.NewGuid().ToString("D"), () => 78L,
            () => new DifficultySettings(2.0m, true));
        await changed.ResumeAsync();
        Assert.True(changed.Snapshot!.OfferEconomy is { EnemyBudgetMultiplier: 1.8m },
            "a later global setting cannot replace the pinned contract economy");
        Assert.True(changed.Snapshot.Offers.Select(offer => offer.OfferId + ":" + offer.PlanHash)
                .SequenceEqual(hashes),
            "disk reload preserves exact policy-5 offer identity");

        EnemyDesignEntry Design(EraReference era, string id, EnemyRole role, decimal price) =>
            new(new BlueprintReference(id, new string((char)('a' + (int)role), 64), era, Array.Empty<ModRequirement>()),
                role, price, new ScenarioValue(price, unit), ContentQualification.Fixture)
            { FormationFamily = id };
        BlueprintSnapshot Blob(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new BlueprintSnapshot(hash, bytes);
        }
    }

    internal static async Task BattleLogWritesOneRow(string root)
    {
        const string unit = "fixture.battlelog.unit";
        var eras = Enumerable.Range(0, 2)
            .Select(index => new EraReference("fixture.battlelog.era." + index)).ToArray();
        var mods = Array.Empty<ModRequirement>();
        var definition = new CatalogueDefinition("fixture.battlelog", CatalogueDefinition.FixtureLabel,
            true, unit, eras.Select(era => new CatalogueEraContent(era,
                Enumerable.Range(0, 3).Select(index => new MapEntry(era,
                    "fixture.battlelog.map." + index, new string('d', 64),
                    new[] { "fixture.battlelog.layout." + index }, mods,
                    ContentQualification.Fixture)).ToArray(),
                new[]
                {
                    Design(era, "line", EnemyRole.Line, 20m),
                    Design(era, "heavy", EnemyRole.Heavy, 40m),
                    Design(era, "recon", EnemyRole.Recon, 10m),
                    Design(era, "support", EnemyRole.Support, 30m)
                })).ToArray());
        var options = new CatalogueOptions(ContentQualification.Fixture, false, true);
        var store = new RunStore(root);
        var rows = new List<BattleLogRow>();
        var presenter = new RecordingPresenter();
        var adapter = new FakeAdapter(unit);
        var controller = new ExpeditionController(
            new SeededEncounterCatalogue(definition, options), store, adapter,
            presenter, () => ModAvailability.None, () => Guid.NewGuid().ToString("D"),
            () => 99L, () => new DifficultySettings(1.5m, true),
            rows.Add, _ => { }, slot: 2);
        await controller.StartNewRunAsync(new NewRunSetup(new ScenarioValue(200m, unit), eras,
            definition.Version) { AdventureRules = true, AdventureVersion = 6 });
        var offers = controller.Snapshot!.Offers;
        Assert.Equal(3, offers.Count, "battle log run generated three offers");
        await controller.SelectOfferAsync(offers.Single(o => o.Risk == EncounterRisk.Standard).OfferId);
        await controller.BeginEncounterAsync();
        Assert.Equal(1, rows.Count, "one completed attempt writes one row");
        var row = rows[0];
        Assert.Equal(2, row.Slot, "row carries the controller slot");
        Assert.Equal(6, row.Schema, "row carries schema 6");
        Assert.Equal("Victory", row.Outcome, "row carries the outcome");
        Assert.True(row.EnemyCount > 0 && row.EnemyCost is > 0m,
            "row carries the planned enemy count and pinned cost");
        Assert.True(row.AttemptGeneration >= 1, "row carries the attempt generation");

        var path = Path.Combine(root, "stats", "battles.csv");
        BattleLog.Append(path, row);
        BattleLog.Append(path, row);
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length, "CSV has one header and two rows");
        Assert.Equal(BattleLog.Header, lines[0], "CSV header is written once");
        Assert.True(lines[1].Contains(",Victory,", StringComparison.Ordinal),
            "CSV row contains the outcome");

        await controller.ContinueAsync();
        var saved = controller.Snapshot!;
        var fixedCards = System.Text.Json.JsonSerializer.Serialize(saved.Offers);
        var bonds = new RewardItem("cash-now", RewardKind.WarBonds, RewardRarity.Common, .15m, 2, 2);
        var seedBonds = store.SaveCheckpoint(saved with { Rewards = saved.Rewards! with
            { Inventory = new[] { bonds }, Revision = saved.Rewards!.Revision + 1 } });
        Assert.True(seedBonds.IsCommitted, "War Bonds fixture saved: " + string.Join(";", seedBonds.Issues));
        controller = new ExpeditionController(new SeededEncounterCatalogue(definition, options), store, adapter,
            presenter, () => ModAvailability.None, () => Guid.NewGuid().ToString("D"), () => 99L,
            () => new DifficultySettings(1.5m, true), rows.Add,
            _ => throw new IOException("warning sink failure"), slot: 2);
        await controller.ResumeAsync();
        await controller.ShowArmoryAsync();
        await controller.RewardCommandAsync("cash", "cash-now");
        await controller.LeaveArmoryAsync();
        // Check the production command name below, and verify disk restore uses
        // the generation budget rather than the now larger funds balance.
        Assert.Equal(fixedCards, System.Text.Json.JsonSerializer.Serialize(controller.Snapshot!.Offers), "funds keep cards");
        await controller.ResumeAsync();
        Assert.False(presenter.Has<BlockedView>() || presenter.Has<ErrorView>(), "cash/reload keeps valid pinned cards");
        await controller.SelectOfferAsync(controller.Snapshot!.Offers[1].OfferId);
        var items = System.Text.Json.JsonSerializer.Serialize(controller.Snapshot.Rewards!.Active);
        adapter.Outcome = EncounterOutcome.PrecombatAborted;
        await controller.BeginEncounterAsync();
        var failed = controller.Snapshot!;
        Assert.Equal(EncounterOutcome.PrecombatAborted, presenter.Last<ResultView>().Result.Outcome, "abort reaches results");
        Assert.Equal(fixedCards, System.Text.Json.JsonSerializer.Serialize(failed.Offers), "abort keeps exact cards");
        adapter.Outcome = EncounterOutcome.TechnicalFailure;
        await controller.ResumeAsync();
        await controller.RetryEncounterAsync();
        Assert.True(controller.Snapshot!.GenerationCounter > failed.GenerationCounter, "abort retry creates new generation");
        Assert.Equal(fixedCards, System.Text.Json.JsonSerializer.Serialize(controller.Snapshot.Offers), "technical failure keeps cards");
        Assert.Equal(items, System.Text.Json.JsonSerializer.Serialize(controller.Snapshot.Rewards!.Active), "abort/technical do not tick items");
        Assert.Equal(3, rows.Count, "victory, abort and technical each log once");
        Console.WriteLine("PASS schema 6 battle log, War Bonds reload, abort RETRY and technical failure retain cards/items");

        EnemyDesignEntry Design(EraReference era, string id, EnemyRole role, decimal price) =>
            new(new BlueprintReference("fixture.battlelog." + id,
                    new string((char)('a' + (int)role), 64), era, Array.Empty<ModRequirement>()),
                role, price, new ScenarioValue(price, unit), ContentQualification.Fixture)
            { FormationFamily = id };
    }

    internal static async Task Run(string root, int version = 2)
    {
        const string unit = "fixture.adventure.cost";
        var eras=Enumerable.Range(0,6).Select(i=>new EraReference("fixture.adventure.era."+i)).ToArray();
        var mods=Array.Empty<ModRequirement>();
        var def=new CatalogueDefinition("fixture.adventure.integration.2",CatalogueDefinition.FixtureLabel,true,unit,
            eras.Select((era,i)=>new CatalogueEraContent(era,
                Enumerable.Range(0,3).Select(n=>new MapEntry(era,$"fixture.map.{i}.{n}",new string('a',64),new[]{"layout"},mods,ContentQualification.Fixture)).ToArray(),
                Enumerable.Range(0,4).Select(n=>new EnemyDesignEntry(new BlueprintReference($"fixture.tank.{i}.{n}",new string('b',64),era,mods),EnemyRole.Line,
                    10m+n*5m,new ScenarioValue(10m+n*5m,unit),ContentQualification.Fixture)).ToArray())).ToArray());
        var options=new CatalogueOptions(ContentQualification.Fixture,true,true);
        for (var startingEra = 0; startingEra < eras.Length; startingEra++)
        {
        var eraPresenter=new RecordingPresenter();var eraStore=new RunStore(Path.Combine(root,"start-era-"+startingEra));
        var eraController=new ExpeditionController(new SeededEncounterCatalogue(def,options),eraStore,new FakeAdapter(unit),eraPresenter,
            ()=>ModAvailability.None,()=>Guid.NewGuid().ToString("D"),()=>112L);
        await eraController.StartNewRunAsync(new NewRunSetup(new ScenarioValue(100m,unit),eras,def.Version)
            {AdventureRules=true,AdventureVersion=version,StartingEraIndex=startingEra});
        Assert.True(eraController.Snapshot is {Victories:0} initial
            && initial.Round == 1 + startingEra * 4 && initial.EraIndex == startingEra
            && initial.StartingEraIndex == startingEra
            && initial.ConfirmedDesign!.Blueprint.Era==eras[startingEra],"controller captures initial design in each selected native era");
        var initialOffers = eraPresenter.Last<OffersView>();
        Assert.True(initialOffers.Offers.Count >= 3 && initialOffers.SelectedOfferId == null
            && eraController.Snapshot!.SelectedOfferId == null,
            "all six era starts, including Coldwar, wait for an explicit offer choice");
        await eraController.SelectOfferAsync(initialOffers.Offers[1].OfferId);
        Assert.Equal(initialOffers.Offers[1].OfferId, eraController.Snapshot!.SelectedOfferId,
            "the player can choose a non-default first challenge in every starting era");
        Assert.True(eraStore.LoadActiveRun(ModAvailability.None).CanResume,"selected-era controller start persists");
        }
        var store=new RunStore(root); var adapter=new FakeAdapter(unit); var presenter=new RecordingPresenter();
        ExpeditionController Controller(RecordingPresenter p)=>new(new SeededEncounterCatalogue(def,options),store,adapter,p,
            ()=>ModAvailability.None,()=>Guid.NewGuid().ToString("D"),()=>111L);
        var c=Controller(presenter);
        await c.StartNewRunAsync(new NewRunSetup(new ScenarioValue(100m,unit),eras,def.Version){AdventureRules=true,AdventureVersion=version});
        Assert.True(c.Snapshot?.Rewards!=null,"new controller uses adventure rules");
        // Editing items after choosing an offer must never reroll that challenge.
        await c.SelectOfferAsync(c.Snapshot!.Offers[1].OfferId);
        var selectedBeforeItems=c.Snapshot.SelectedOfferId;
        var plansBeforeItems=System.Text.Json.JsonSerializer.Serialize(c.Snapshot.Offers);
        var rollBeforeItems=c.Snapshot.Rewards!.OfferRoll;
        await c.ShowArmoryAsync(); var starter=c.Snapshot!.Rewards!.Inventory[0];
        await c.RewardCommandAsync("activate",starter.Id); await c.LeaveArmoryAsync();
        Assert.Equal(selectedBeforeItems,c.Snapshot!.SelectedOfferId,"armory keeps selected offer");
        Assert.Equal(plansBeforeItems,System.Text.Json.JsonSerializer.Serialize(c.Snapshot.Offers),"map, enemies and reward remain exact after activation");
        Assert.Equal(rollBeforeItems,c.Snapshot.Rewards!.OfferRoll,"ordinary item does not silently reroll");
        presenter=new RecordingPresenter();c=Controller(presenter);await c.ResumeAsync();
        Assert.Equal(selectedBeforeItems,c.Snapshot!.SelectedOfferId,"selected offer and original economy restore after reload");
        await c.RefreshEncounterDesignAsync();
        Assert.Equal(selectedBeforeItems,c.Snapshot!.SelectedOfferId,"Play-time native tank re-quote keeps challenge");
        // Include a budget-affecting item and an enemy-budget modifier in the same
        // pinned selection, then verify exact content after a second resume.
        var selectedRewards=c.Snapshot.Rewards! with { Revision=c.Snapshot.Rewards!.Revision+1,
            Inventory=new[]{ItemForSelection("selected-workshop",RewardKind.EliteWorkshop),ItemForSelection("selected-contract",RewardKind.ExperimentalContract)} };
        Assert.True(store.SaveCheckpoint(c.Snapshot with{Rewards=selectedRewards}).IsCommitted,"selected item fixtures saved");
        presenter=new RecordingPresenter();c=Controller(presenter);await c.ResumeAsync();
        await c.ShowArmoryAsync();await c.RewardCommandAsync("activate","selected-workshop");
        await c.RewardCommandAsync("activate","selected-contract");await c.LeaveArmoryAsync();
        Assert.Equal(plansBeforeItems,System.Text.Json.JsonSerializer.Serialize(c.Snapshot!.Offers),"friendly/enemy modifiers do not replace a selected challenge");
        presenter=new RecordingPresenter();c=Controller(presenter);await c.ResumeAsync();
        Assert.True(!presenter.Has<BlockedView>()&&!presenter.Has<ErrorView>()&&c.Snapshot!.SelectedOfferId==selectedBeforeItems,
            "pinned generation economy survives active budget modifiers and checkpoint validation");
        await c.RefreshEncounterDesignAsync();
        static RewardItem ItemForSelection(string id,RewardKind kind)=>new(id,kind,RewardRarity.Rare,.2m,4,4);
        for(var i=1;i<=4;i++)
        {
            await c.SelectOfferAsync(c.Snapshot!.Offers[Math.Min(1,c.Snapshot.Offers.Count-1)].OfferId); await c.BeginEncounterAsync();
            Assert.Equal(i,c.Snapshot!.Victories,"controller saved victory" );
            if(i<4) await c.ContinueAsync();
        }
        Assert.True(presenter.Has<ArmoryView>()&&c.Snapshot!.Rewards!.Choices.Count==3,"boss exposes three item choices");
        presenter=new RecordingPresenter(); c=Controller(presenter); await c.ResumeAsync();
        Assert.True(presenter.Has<ArmoryView>(),"reload preserves reward choice screen");
        await c.RewardCommandAsync("claim",c.Snapshot!.Rewards!.Choices[0].Id); await c.LeaveArmoryAsync();
        Assert.Equal(RunPhase.ChoosingEncounter,c.Snapshot!.Phase,"boss reward opens next-era offers before editor Play");
        Assert.True(presenter.Last<OffersView>().Era==eras[1]&&!c.Snapshot.DesignConfirmedForBlock
            &&c.Snapshot.Offers.Count==3,"next-era budget stages offers without prematurely approving the old tank");
        Assert.True(!RunDirector.Restore(c.Snapshot).BeginEncounter().IsAccepted,"unrefitted tank cannot start the next era");
        var afterBossChoice=c.Snapshot.Offers[1].OfferId;
        await c.SelectOfferAsync(afterBossChoice);
        presenter=new RecordingPresenter();c=Controller(presenter);await c.ResumeAsync();
        Assert.Equal(afterBossChoice,c.Snapshot!.SelectedOfferId,"post-boss choice survives reload before refit");
        await c.RefreshEncounterDesignAsync();
        Assert.True(c.Snapshot!.ConfirmedDesign!.Blueprint.Era==eras[1]&&c.Snapshot.SelectedOfferId==afterBossChoice,
            "Play re-quotes the next-era design and keeps the already selected post-boss challenge");
        var s=c.Snapshot!; var recovery=new RewardItem("fixture.recovery",RewardKind.EmergencyRecovery,RewardRarity.UltraRare,1m,6,6);
        var updated=s with{Rewards=s.Rewards! with{Revision=s.Rewards!.Revision+1,Active=new[]{recovery},
            Inventory=s.Rewards.Inventory.Where(i=>i.Kind!=RewardKind.EmergencyRecovery).ToArray()}};
        Assert.True(store.SaveCheckpoint(updated).IsCommitted,"synthetic active recovery saved");
        presenter=new RecordingPresenter(); c=Controller(presenter); await c.ResumeAsync();
        await c.SelectOfferAsync(c.Snapshot!.Offers[1].OfferId); adapter.Outcome=EncounterOutcome.FriendlyForceDestroyed; await c.BeginEncounterAsync();
        var locked=c.Snapshot!.SelectedOfferId;
        Assert.Equal(RunPhase.AwaitingRecovery,c.Snapshot.Phase,"loss presents recovery");
        presenter=new RecordingPresenter(); c=Controller(presenter); await c.ResumeAsync();
        Assert.True(presenter.Last<ArmoryView>().Recovery,"reload returns to recovery choice");
        await c.UseRecoveryAsync(); Assert.Equal(locked,c.Snapshot!.SelectedOfferId,"recovery keeps same selected card");
        adapter.Outcome=EncounterOutcome.Victory; await c.BeginEncounterAsync();
        Assert.Equal(5,c.Snapshot!.Victories,"retried victory awards exactly once");
        Assert.False(c.Snapshot.Rewards!.Active.Any(i=>i.Kind==RewardKind.EmergencyRecovery),"charge consumed permanently");
        // Full inventory/active slots are expected user choices, not fatal views.
        var capacitySnapshot=c.Snapshot;
        RewardItem Item(string id,RewardKind kind)=>new(id,kind,RewardRarity.Rare,.2m,4,4);
        var capacityRewards=capacitySnapshot.Rewards with{Revision=capacitySnapshot.Rewards.Revision+1,
            Active=new[]{Item("active-mass",RewardKind.MassProduction),Item("active-line",RewardKind.AssemblyLine),Item("active-veteran",RewardKind.VeteranCrew)},
            Inventory=new[]{Item("held-support",RewardKind.FieldReinforcements),Item("held-mass",RewardKind.MassProduction),
                Item("held-2",RewardKind.SalvageRights),Item("held-3",RewardKind.SalvageRights),Item("held-4",RewardKind.SalvageRights),Item("held-5",RewardKind.SalvageRights)},
            Choices=new[]{Item("pending-loot",RewardKind.SalvageRights)}};
        Assert.True(store.SaveCheckpoint(capacitySnapshot with{Rewards=capacityRewards}).IsCommitted,"full capacity fixture saved");
        presenter=new RecordingPresenter(); c=Controller(presenter); await c.ResumeAsync();
        var unchanged=System.Text.Json.JsonSerializer.Serialize(c.Snapshot);
        await c.RewardCommandAsync("claim","pending-loot");
        Assert.True(presenter.Last<ArmoryView>().Notice?.Contains("Inventory is full")==true&&!presenter.Has<ErrorView>(),
            "full inventory remains in armory with an actionable notice");
        Assert.Equal(unchanged,System.Text.Json.JsonSerializer.Serialize(c.Snapshot),"rejected claim preserves all loot/checkpoint state");
        await c.RewardCommandAsync("discard","held-5"); await c.RewardCommandAsync("claim","pending-loot");
        Assert.True(c.Snapshot!.Rewards!.Inventory.Count==6&&c.Snapshot.Rewards.Choices.Count==0,"discard then claim works without leaving armory");
        unchanged=System.Text.Json.JsonSerializer.Serialize(c.Snapshot);
        await c.RewardCommandAsync("activate","held-support");
        Assert.True(presenter.Last<ArmoryView>().Notice?.Contains("All 3 active slots")==true&&!presenter.Has<ErrorView>(),
            "full active slots retain inventory with guidance instead of Unexpected error");
        Assert.Equal(unchanged,System.Text.Json.JsonSerializer.Serialize(c.Snapshot),"rejected activation remains inert");
        await c.RewardCommandAsync("discard-active","active-line");
        Assert.True(c.Snapshot!.Rewards!.Active.Count==2&&!c.Snapshot.Rewards.Inventory.Any(i=>i.Id=="active-line"),
            "discarding an active bonus frees a slot without restoring or refreshing the item");
        await c.RewardCommandAsync("activate","held-support");
        await c.RewardCommandAsync("activate","held-mass");
        Assert.True(c.Snapshot!.Rewards!.Active.Count==3&&c.Snapshot.Rewards.Active.Any(i=>i.Id=="held-mass")
            &&!c.Snapshot.Rewards.Active.Any(i=>i.Id=="active-mass"),"full slots still permit same-family replacement");
        Assert.True(!presenter.Has<ErrorView>(),"all capacity management stays in ordinary item views");
        presenter=new RecordingPresenter(); c=Controller(presenter); await c.ResumeAsync();
        Assert.True(c.Snapshot!.Rewards!.Active.Any(i=>i.Id=="held-support")&&c.Snapshot.Rewards.Active.Any(i=>i.Id=="held-mass"),
            "capacity handling and replacements survive checkpoint reload");
        Console.WriteLine("PASS full inventory/active slots -> inline armory notice -> discard/claim/activate/replace -> saved reload");
        Console.WriteLine("PASS adventure controller item activation -> boss -> save/reload/claim -> era refit -> loss/reload/recovery -> same-plan victory");
    }
}
