using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;

// Synthetic prices/designs only. No claim of native gameplay validation.
static class Program
{
    static readonly EraReference[] Eras = Enumerable.Range(0,6).Select(i => new EraReference("fixture.era."+i)).ToArray();
    static readonly EraReference[] ErasFive = Eras.Take(5).ToArray();
    const string Unit = "fixture.cost";
    static readonly ModRequirement[] Mods = Array.Empty<ModRequirement>();
    static readonly byte[] Bytes = Encoding.UTF8.GetBytes("synthetic player blueprint");
    static readonly string Hash = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    static readonly CatalogueOptions Options = new(ContentQualification.Fixture, true, true);
    static readonly CatalogueDefinition Definition = new("fixture.adventure.1", CatalogueDefinition.FixtureLabel, true, Unit,
        Eras.Select((era,i) => EraContent(era,i)).ToArray());
    static readonly CatalogueDefinition DefinitionFive = WithEndlessCapacity(
        new("fixture.adventure.5", CatalogueDefinition.FixtureLabel, true, Unit,
            ErasFive.Select((era,i) => EraContent(era,i)).ToArray()));
    static CatalogueDefinition WithEndlessCapacity(CatalogueDefinition definition)
    {
        var eras = definition.Eras.ToArray();
        var last = eras[^1];
        // Representative intermediate prices keep the synthetic final-era pool
        // usable by the real policy-5 cost/thematic planners.
        var prices = new[] { 45m, 75m, 150m, 2000m };
        var extras = prices.Select((price, index) => new EnemyDesignEntry(
            new BlueprintReference("fixture.tank.optional.endless." + index,
                new string((char)('a' + index),64), last.Era, Mods),
            index == prices.Length - 1 ? EnemyRole.Heavy : EnemyRole.Line,
            price, new ScenarioValue(price,Unit), ContentQualification.Fixture)).ToArray();
        eras[^1] = last with { Enemies = last.Enemies.Concat(extras).ToArray() };
        return definition with { Eras = eras };
    }
    static CatalogueEraContent EraContent(EraReference era, int i) => new(era,
        Enumerable.Range(0,3).Select(m => new MapEntry(era,$"fixture.map.{i}.{m}", new string((char)('a'+i),64),new[]{"layout"},Mods,ContentQualification.Fixture)).ToArray(),
        Enumerable.Range(0,4).Select(n => new EnemyDesignEntry(new BlueprintReference($"fixture.tank.{i}.{n}", new string((char)('a'+i),64),era,Mods),
            EnemyRole.Line,i==5&&n==3?2000m:10m+n*5m,new ScenarioValue(i==5&&n==3?2000m:10m+n*5m,Unit),ContentQualification.Fixture)).ToArray());
    static string TestBase() {
        var root=Path.Combine(AppContext.BaseDirectory,"test-temp");
        Directory.CreateDirectory(root);
        return root;
    }
    static void Check(bool value,string what) { if (!value) throw new Exception(what); }
    static void Ok(RunActionResult result) => Check(result.IsAccepted,result.Reason);
    static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
    static RunDirector New(int version=2) { var d=new RunDirector(); Ok(d.NewRun(new RunConfiguration(Guid.NewGuid().ToString("D"),17,new ScenarioValue(100m,Unit),Eras,Definition.Version){AdventureRules=true,AdventureVersion=version})); return d; }
    static void Confirm(RunDirector d,decimal price=20m) { var s=d.Snapshot!; var q=new DesignQuote(new BlueprintReference("fixture.player",Hash,Eras[s.EraIndex],Mods),new ScenarioValue(price,Unit),true,true,null); Ok(s.Phase==RunPhase.ChoosingEncounter ? d.ConfirmEncounterDesign(q) : d.ConfirmDesign(q)); }
    static EncounterRequest Fight(RunDirector d) => Fight(d, Definition);
    static EncounterRequest Fight(RunDirector d, CatalogueDefinition definition) {
        if (!d.Snapshot!.DesignConfirmedForBlock) Confirm(d);
        if(d.Snapshot!.Offers.Count==0) { var plans=new SeededEncounterCatalogue(definition,Options).GenerateOffers(d.CreateOfferRequest()); Ok(d.SetOffers(plans)); }
        if(d.Snapshot!.SelectedOfferId==null) Ok(d.SelectEncounter(d.Snapshot.Offers[1].OfferId));
        Ok(d.BeginEncounter()); var r=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(r.Attempt)); return r;
    }
    static void Win(RunDirector d) => Win(d, Definition);
    static void Win(RunDirector d, CatalogueDefinition definition) { var r=Fight(d,definition); Ok(d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.Victory,null){SurvivingAllies=2})); }
    static RewardItem Item(RewardKind kind,string id="item",int rounds=3,decimal magnitude=.25m,string? blueprint=null) => new(id,kind,rounds<3?RewardRarity.Common:rounds<5?RewardRarity.Rare:RewardRarity.UltraRare,magnitude,rounds,rounds,blueprint);
    static RunDirector WithRewards(RunDirector d,RewardState rewards) => RunDirector.Restore(d.Snapshot! with {Rewards=rewards});
    static void Main() {
        var tests=new (string,Action)[] {
            ("fleet discounts apply only after the native-priced player",FleetPricing),
            ("free support, caps, duplicate families and veteran eligibility",SupportCaps),
            ("deterministic rarity, duration and boss choice distribution",Determinism),
            ("storage, activation, timing and delayed bonds",Lifecycle),
            ("duplicate pickups replace active families with fresh timers, including full slots",Replacement),
            ("all 24 battles, boss eras, saved boundaries and endless",TwentyFour),
            ("reward checkpoint roundtrip and stale writes",Persistence),
            ("recovery is once, same plan, no clocks or duplicate reward",Recovery),
            ("technical failure and refit preserve timers",NonVictory),
            ("reroll and tampered offers cannot replace a pinned plan",Reroll),
            ("malformed rewards and pending choices reject atomically",Validation)
            ,("final budget bonus expiry remains resumable at endless choice",FinalExpiry)
            ,("named next-era bosses override placeholders",NamedBoss)
            ,("curated version3 campaign, max-price bosses and gold reward saves",CuratedCampaign)
            ,("special rules preserve budgets, count limits and deterministic identities",Specials)
            ,("single boss recovery stays locked and boss rerolls are refused",CuratedRecovery)
            ,("each starting era skips no rewards, persists and finishes through Coldwar",StartingEras)
            ,("selected challenge survives item budget loss and affordable refit",SelectedItemRefit)
            ,("optional five-era schedule keeps Latewar final boss and endless cadence",OptionalFiveEra)
            ,("schema 6 fixes the round offers through item changes and loss",Schema6FixedOffers)
            ,("schema 6 base budget ignores item multipliers and contract waits a round",Schema6BudgetAndContract)
            ,("schema 6 cumulative attempt and kill totals survive restore",Schema6RunTotals)
            ,("schema 6 normal defeats retry unchanged and reset round penalties after victory",Schema6Retries)
            ,("schema 6 Ironman defeats end the run without recovery",Schema6Ironman)
            ,("schema 6 Retire persists score, counters, mode and era",Schema6Retire)
            ,("schema 6 score formula covers risks, boss, flawless, difficulty and retry floor",Schema6Score)
            ,("schema 6 contact return is free before contact and penalized after",Schema6Contact)
            ,("schema 6 loot excludes Emergency Recovery while schema 5 retains it",Schema6Loot)
        };
        var failures=0;
        foreach(var (name,test) in tests) try { test(); Console.WriteLine("PASS "+name); } catch(Exception ex) { failures++; Console.WriteLine("FAIL "+name+": "+ex); }
        Console.WriteLine($"{tests.Length-failures}/{tests.Length} adventure scenarios passed");
        Environment.ExitCode=failures==0?0:1;
    }
    static void StartingEras() {
        foreach (var version in new[] { 2, 3 })
        for (var start = 0; start < 6; start++) {
            var d = new RunDirector();
            Ok(d.NewRun(new RunConfiguration(Guid.NewGuid().ToString("D"),17,new ScenarioValue(100m,Unit),Eras,Definition.Version)
                {AdventureRules=true,AdventureVersion=version,StartingEraIndex=start}));
            var s=d.Snapshot!;
            Check(s.EraIndex==start && s.BlockIndex==start && s.Round==start*4+1 && s.Victories==0,
                "canonical era/round with zero invented victories");
            Check(s.Rewards!.GrantedOffers.Count==0 && s.Rewards.EarnedFunds==0 && s.ProcessedAttempts.Count==0,
                "skipped eras give no loot, funds or attempts");
            Check(s.BlockBaseBudget.Value==RunDirector.FiniteEraBudget(100m,start),"era budget uses existing native-cost base multipliers");
            var store=new RunStore(Path.Combine(TestBase(),"ExpeditionStart-"+Guid.NewGuid().ToString("N")));
            for(var wins=0;wins<(6-start)*4;wins++) {
                if(!d.Snapshot!.DesignConfirmedForBlock) Confirm(d);
                if(d.Snapshot!.Offers.Count==0) Ok(d.SetOffers(new SeededEncounterCatalogue(Definition,Options).GenerateOffers(d.CreateOfferRequest())));
                var before=d.Snapshot!;
                Check(before.Offers.All(p=>p.Era==Eras[before.EraIndex]),"enemies/offers use current native era");
                Check(before.Offers.Count==(version==3&&before.Round%4==0?1:3),"curated boss remains every fourth era battle");
                Ok(d.SelectEncounter(before.Offers.First(p=>p.Risk==EncounterRisk.Standard).OfferId));
                Ok(d.BeginEncounter());var request=d.CreateEncounterRequest();Ok(d.ReportCombatStarted(request.Attempt));
                Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"save selected-era battle");
                var loaded=store.LoadActiveRun(ModAvailability.None);Check(loaded.CanResume&&loaded.Snapshot!.StartingEraIndex==start,"starting era persists");
                d=RunDirector.Restore(loaded.Snapshot!);var resumed=d.CreateEncounterRequest();Ok(d.ReportCombatStarted(resumed.Attempt));
                Check(!d.AcceptResult(new EncounterResult(request.Attempt,request.Plan,EncounterOutcome.Victory,null)).IsAccepted,"stale pre-load outcome refused");
                Ok(d.AcceptResult(new EncounterResult(resumed.Attempt,resumed.Plan,EncounterOutcome.Victory,null)));
                if(d.Snapshot!.Rewards!.Choices.Count>0) Ok(d.SkipRewardChoice());
                while(d.Snapshot!.Rewards!.Inventory.Count>3) Ok(d.DiscardReward(d.Snapshot.Rewards.Inventory[0].Id));
                d=RunDirector.Restore(d.Snapshot!);
                Check(d.Snapshot!.Victories==wins+1&&d.Snapshot.Rewards!.GrantedOffers.Count==wins+1,"only actual wins grant rewards");
                Check(store.SaveCheckpoint(d.Snapshot!).IsCommitted,"victory persists after era skip");
            }
            Check(d.Snapshot!.Phase==RunPhase.AwaitingEndlessChoice&&d.Snapshot.Round==25,"Coldwar ends at the normal final boundary");
            Ok(d.ContinueEndless());Check(d.Snapshot!.Round==25&&d.Snapshot.BlockIndex==6,"endless keeps canonical round after skipped eras");
            d=RunDirector.Restore(d.Snapshot!);
            var invalid=d.Snapshot! with{StartingEraIndex=(start+1)%6};
            Check(!store.SaveCheckpoint(invalid!).IsCommitted,"starting era is immutable in storage");
        }
        foreach(var bad in new[]{-1,6,int.MaxValue}) {
            var d=new RunDirector();Check(!d.NewRun(new RunConfiguration("bad",17,new ScenarioValue(100m,Unit),Eras,Definition.Version)
                {AdventureRules=true,StartingEraIndex=bad}).IsAccepted&&!d.HasRun,"invalid start rejected atomically");
        }
    }
    static void OptionalFiveEra() {
        var root=Path.Combine(TestBase(),"ExpeditionOptionalFive-"+Guid.NewGuid().ToString("N"));
        var store=new RunStore(root);
        var d=new RunDirector();
        Ok(d.NewRun(new RunConfiguration(Guid.NewGuid().ToString("D"),17,new ScenarioValue(100m,Unit),ErasFive,DefinitionFive.Version)
            {AdventureRules=true,AdventureVersion=5,StartingEraIndex=4}));
        var start=d.Snapshot!;
        Check(start.EraSchedule.Count==5&&start.EraSchedule[^1].NativeId==ErasFive[4].NativeId,
            "five-era schedule is the ordered prefix, not a renamed compression");
        Check(start.StartingEraIndex==4&&start.BlockIndex==4&&start.EraIndex==4&&start.Round==17,
            "Latewar start maps to round seventeen of twenty");
        Check(!new RunDirector().NewRun(new RunConfiguration("bad-coldwar",17,new ScenarioValue(100m,Unit),ErasFive,DefinitionFive.Version)
            {AdventureRules=true,AdventureVersion=5,StartingEraIndex=5}).IsAccepted,
            "a five-era run cannot start in an excluded Coldwar");

        for(var win=0;win<2;win++) { Win(d,DefinitionFive); ClearRewards(d); }
        var pre=d.Snapshot!;
        Check(pre.Round==19&&pre.BlockIndex==4&&pre.EraIndex==4&&pre.Victories==2,
            "two final-era battles leave an on-disk pre-final boundary");
        var saved=store.SaveCheckpoint(pre,new[]{new BlueprintSnapshot(Hash,Bytes)});
        Check(saved.IsCommitted,"five-era pre-final checkpoint commits: "+string.Join(";",saved.Issues));
        var loaded=store.LoadActiveRun(ModAvailability.None);
        var loadedSnapshot=loaded.Snapshot!;
        Check(loaded.CanResume&&loadedSnapshot.EraSchedule.Count==5
            &&loadedSnapshot.EraSchedule[^1].NativeId==ErasFive[4].NativeId,
            "five-era schedule is pinned across disk reload");
        d=RunDirector.Restore(loadedSnapshot);
        Check(d.Snapshot!.StartingEraIndex==4&&d.Snapshot.EraSchedule.Count==5,
            "restore keeps the saved five-era start and schedule");

        Win(d,DefinitionFive); ClearRewards(d);
        var boss=Fight(d,DefinitionFive);
        var bossExtras=boss.Plan.Extras;
        Check(boss.Plan.Round==20&&bossExtras?.CuratedBoss==true,
            "final finite battle uses the curated boss");
        var bossBlueprint=bossExtras?.BossBlueprintId;
        Check(bossBlueprint!=null&&bossExtras!.BossEra==ErasFive[4].NativeId
            &&DefinitionFive.Eras[4].Enemies.Any(enemy=>enemy.Blueprint.LocalId==bossBlueprint),
            "five-era final boss comes from Latewar, never a nonexistent Coldwar");
        Ok(d.AcceptResult(new EncounterResult(boss.Attempt,boss.Plan,EncounterOutcome.Victory,null){SurvivingAllies=2}));
        ClearRewards(d);
        var final=d.Snapshot!;
        Check(final.Phase==RunPhase.AwaitingEndlessChoice&&final.Victories==4&&final.Round==21
            &&final.BlockIndex==4&&final.EraIndex==4,
            "Latewar is the final finite era at the normal endless choice");
        Ok(d.ContinueEndless());
        var endless=d.Snapshot!;
        Check(endless.BlockIndex==5&&endless.EraIndex==4&&endless.Round==21,
            "first endless block follows five finite blocks and stays in Latewar");
        saved=store.SaveCheckpoint(endless,new[]{new BlueprintSnapshot(Hash,Bytes)});
        Check(saved.IsCommitted,"five-era endless checkpoint commits: "+string.Join(";",saved.Issues));
        loaded=store.LoadActiveRun(ModAvailability.None);
        loadedSnapshot=loaded.Snapshot!;
        Check(loaded.CanResume&&loadedSnapshot.BlockIndex==5&&loadedSnapshot.EraSchedule.Count==5,
            "endless boundary and pinned five-era schedule survive disk reload");
        d=RunDirector.Restore(loadedSnapshot);

        for(var endlessRound=0;endlessRound<4;endlessRound++) {
            var request=Fight(d,DefinitionFive);
            Check(request.Plan.Round==21+endlessRound,"endless round advances by one battle");
            Check(d.Snapshot!.Offers.Count==3,"endless cadence keeps three offers, not a curated boss");
            Check(request.Plan.Era.NativeId==ErasFive[4].NativeId,"endless stays in Latewar");
            Check(request.Plan.Extras!.BossBlueprintId==null&&!request.Plan.Extras.CuratedBoss,
                "endless does not turn every fourth battle into a finite curated boss");
            Ok(d.AcceptResult(new EncounterResult(request.Attempt,request.Plan,EncounterOutcome.Victory,null){SurvivingAllies=2}));
            ClearRewards(d);
        }
        var after=d.Snapshot!;
        Check(after.BlockIndex==6&&after.EraIndex==4&&after.Round==25&&after.Victories==8,
            "four endless battles advance one block without changing the pinned final era");
    }
    static void ClearRewards(RunDirector d) {
        if(d.Snapshot!.Rewards!.Choices.Count>0) Ok(d.SkipRewardChoice());
        while(d.Snapshot!.Rewards!.Inventory.Count>3) Ok(d.DiscardReward(d.Snapshot.Rewards.Inventory[0].Id));
    }
    static void SelectedItemRefit() {
        foreach(var version in new[]{2,3}) {
            var d=New(version);var s=d.Snapshot!;
            d=RunDirector.Restore(s with{Rewards=s.Rewards! with{Active=new[]{Item(RewardKind.LastStand,"solo-budget")}},
                AvailableBudget=new ScenarioValue(125m,Unit)});
            Confirm(d,120m);var request=d.CreateOfferRequest();
            var catalogue=new SeededEncounterCatalogue(Definition,Options);Ok(d.SetOffers(catalogue.GenerateOffers(request)));
            var chosen=d.Snapshot!.Offers.Single(p=>p.Risk==EncounterRisk.Standard);Ok(d.SelectEncounter(chosen.OfferId));
            var originalPlans=JsonSerializer.Serialize(d.Snapshot.Offers);
            Ok(d.DiscardActiveReward("solo-budget"));
            Check(d.Snapshot!.SelectedOfferId==chosen.OfferId&&!d.Snapshot.DesignConfirmedForBlock
                &&d.Snapshot.AvailableBudget.Value==100m,"lost budget requires cheaper tank without erasing selected challenge");
            d=RunDirector.Restore(d.Snapshot);Ok(d.Suspend());d=RunDirector.Restore(d.Snapshot!);Ok(d.Resume());
            Check(!d.BeginEncounter().IsAccepted,"unaffordable tank cannot bypass native re-quote");
            var store=new RunStore(Path.Combine(TestBase(),"ExpeditionSelected-"+Guid.NewGuid().ToString("N")));
            Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"unconfirmed selected refit saves");
            var loaded=store.LoadActiveRun(ModAvailability.None);Check(loaded.CanResume,"unconfirmed selected refit loads");
            d=RunDirector.Restore(loaded.Snapshot!);
            var restoreRequest=request with{AvailableBudget=d.Snapshot!.OfferEconomy!.Budget,
                EnemyBudgetMultiplier=d.Snapshot.OfferEconomy.EnemyBudgetMultiplier};
            Check(new SeededEncounterCatalogue(Definition,Options).RestoreOffers(restoreRequest,d.Snapshot.Offers).IsSuccess,
                "exact selected offers validate at original economy after the item budget is gone");
            Confirm(d,90m);Check(JsonSerializer.Serialize(d.Snapshot!.Offers)==originalPlans,"affordable refit preserves every plan byte");
            Ok(d.BeginEncounter());Check(d.CreateEncounterRequest().Plan.PlanHash==chosen.PlanHash,"Play starts the already selected challenge");
        }
    }
    static void FleetPricing() {
        var s=new RewardState{Active=new[]{Item(RewardKind.MassProduction)}};
        var f=RewardRules.Fleet(s,175m,100m); Check(f.Total==2&&f.TotalCost==175m,"second tank 25% cheaper");
        Check(RewardRules.Fleet(s,99m,100m).Total==0,"native player remains full price");
        var many=new RewardState{Active=new[]{Item(RewardKind.AssemblyLine,"line",3,.2m),Item(RewardKind.SwarmDoctrine,"swarm",3,.2m)}};
        f=RewardRules.Fleet(many,1000m,100m); Check(f.Total==8&&f.TotalCost>=520m,"max eight and cap discount40%");
    }
    static void SupportCaps() {
        var s=new RewardState{Active=new[]{Item(RewardKind.FieldReinforcements,"reinforce"),Item(RewardKind.LendLease,"lease",3,1m,"fixture.tank.0.0")}};
        var f=RewardRules.Fleet(s,100m,100m); Check(f.Total==3&&f.FreeReinforcements==2,"two free supports");
        f=RewardRules.Fleet(s with{Active=s.Active.Append(Item(RewardKind.LastStand,"last")).ToArray()},100m,100m); Check(f.Total==1&&f.EffectiveBudget==125m,"last stand suppresses allies");
        Throws(()=>RewardRules.Validate(s with {Active=new[]{s.Active[0],Item(RewardKind.EmergencyReserve)}}));
        Throws(()=>RewardRules.Validate(s with {Active=new[]{s.Active[1],Item(RewardKind.CapturedPrototype,blueprint:"fixture.tank.0.1")}}));
        var veteran=new RewardState{Active=new[]{Item(RewardKind.VeteranCrew,magnitude:.2m)},VeteranAllies=0};
        Check(RewardRules.Fleet(veteran,180m,100m).Total==1,"no discounts without surviving allies");
        Check(RewardRules.Fleet(veteran with{VeteranAllies=1},180m,100m).Total==2,"previous survivor eligible");
    }
    static void Determinism() {
        var seen=new HashSet<RewardKind>(); var rarities=new HashSet<RewardRarity>();
        for(var i=0;i<1000;i++) {
            var r=RewardRules.Create(i,1,"offer",i%3,blueprintId:"fixture.tank.0.0");
            Check(r==RewardRules.Create(i,1,"offer",i%3,blueprintId:"fixture.tank.0.0"),"deterministic"); seen.Add(r.Kind);rarities.Add(r.Rarity);
            Check(r.Duration is>=1 and<=6,"bounded duration");
            Check(r.Rarity!=RewardRarity.Common||r.Duration<=2,"blue1-2"); Check(r.Rarity!=RewardRarity.Rare||r.Duration is>=3 and<=4,"red3-4"); Check(r.Rarity!=RewardRarity.UltraRare||r.Duration>=5,"purple5-6");
        }
        Check(seen.Count==15&&rarities.Count==3,"all rewards and rarities reachable");
    }
    static void Lifecycle() {
        var bonds=Item(RewardKind.WarBonds,"bonds",4,.3m); var salvage=Item(RewardKind.SalvageRights,"salvage",2,.01m);
        var s=new RewardState{Inventory=new[]{bonds,salvage}};
        s=RewardRules.Activate(s,"bonds"); s=RewardRules.Activate(s,"salvage");
        var drop=Item(RewardKind.MassProduction,"drop",2);
        s=RewardRules.Victory(s,drop,"offer1",false,1,1,100m,3,2);
        Check(s.EarnedFunds==8m&&s.Active.Single(i=>i.Kind==RewardKind.WarBonds).RemainingRounds==1,"5%win+3%salvage, one bond tick");
        Check(s.Inventory.Single().RemainingRounds==2,"held item does not tick");
        var once=s; s=RewardRules.Victory(s,drop,"offer1",false,1,1,100m,3,2); Check(ReferenceEquals(once,s),"duplicate inert");
        s=RewardRules.Victory(s,drop with{Id="drop2"},"offer2",false,1,2,100m,3,2);
        Check(s.EarnedFunds==46m&&s.Active.Count==0,"delayed30%, base5%, salvage3%");
        var full=new RewardState{Inventory=Enumerable.Range(0,6).Select(i=>Item(RewardKind.AssemblyLine,"held"+i)).ToArray()};
        full=RewardRules.Victory(full,drop,"overflow",false,1,1,100m,0,0); Check(full.Choices.Count==1,"fullinventory retains offered reward");
        full=RewardRules.Discard(full,"held0"); full=RewardRules.Claim(full,"drop"); Check(full.Inventory.Count==6&&full.Choices.Count==0,"replace stored item");
    }
    static void Replacement() {
        var old=Item(RewardKind.CapturedPrototype,"old",6,1m,"fixture.tank.1.0") with{RemainingRounds=2};
        var pickup=Item(RewardKind.CapturedPrototype,"new",6,1m,"fixture.tank.2.0");
        var held=Item(RewardKind.AssemblyLine,"held");
        var s=new RewardState{Active=new[]{Item(RewardKind.MassProduction,"mass"),old,Item(RewardKind.SalvageRights,"salvage")},
            Inventory=new[]{pickup,held},EarnedFunds=15m};
        Check(RewardRules.ActiveReplacement(s,pickup)==old,"UI identifies exact support replacement before activation");
        var replaced=RewardRules.Activate(s,pickup.Id);
        RewardRules.Validate(replaced);
        Check(replaced.Active.Count==3&&replaced.Active[1]==pickup&&replaced.Inventory.Single()==held,
            "full active slots permit replacement in place and consume only the new item");
        Check(replaced.EarnedFunds==15m&&s.Active[1]==old&&s.Inventory.Count==2,"input state and unrelated funds remain intact");
        Check(RewardRules.Fleet(replaced,100m,100m).FreeReinforcements==1,"replacement does not stack free allies");
        var next=RewardRules.Victory(replaced,Item(RewardKind.VeteranCrew,"drop"),"replacement-win",false,1,1,100m,0,0);
        Check(next.Active.Single(i=>i.Id==pickup.Id).RemainingRounds==5,"fresh duration ticks only once on victory");
        var replay=RewardRules.Victory(next,Item(RewardKind.VeteranCrew,"drop"),"replacement-win",false,1,1,100m,0,0);
        Check(ReferenceEquals(next,replay),"replayed victory cannot consume another replacement round");
        Throws(()=>RewardRules.Activate(replaced,pickup.Id));
        Throws(()=>RewardRules.Activate(replaced,held.Id));
        foreach(var pair in new[]{(RewardKind.LendLease,RewardKind.CapturedPrototype),(RewardKind.CapturedPrototype,RewardKind.LendLease),
            (RewardKind.FieldReinforcements,RewardKind.EmergencyReserve),(RewardKind.EmergencyReserve,RewardKind.FieldReinforcements)}) {
            var a=Item(pair.Item1,"family-old",3,1m,"fixture.tank.1.0");
            var b=Item(pair.Item2,"family-new",4,1m,"fixture.tank.2.0");
            var result=RewardRules.Activate(new RewardState{Active=new[]{a},Inventory=new[]{b}},b.Id);
            Check(result.Active.Single()==b,"related support types replace in both directions"); RewardRules.Validate(result);
        }
        var weaker=Item(RewardKind.MassProduction,"weaker",2,.1m);
        var discount=RewardRules.Activate(new RewardState{Active=new[]{Item(RewardKind.MassProduction,"stronger",6,.3m)},Inventory=new[]{weaker}},weaker.Id);
        Check(discount.Active.Single()==weaker&&RewardRules.Fleet(discount,190m,100m).TotalCost==190m,
            "replacement takes the new magnitude and duration without combining effects");
        var bond=Item(RewardKind.WarBonds,"bond-new",6,.3m);
        var bonds=RewardRules.Activate(new RewardState{Active=new[]{Item(RewardKind.WarBonds,"bond-old",3,.3m) with{RemainingRounds=1}},Inventory=new[]{bond}},bond.Id);
        Check(bonds.Active.Single().RemainingRounds==2&&bonds.EarnedFunds==0m,"replaced bonds restart two victories without an early payout");
        Check(RewardRules.Describe(pickup).Contains("6 victories after activation")&&RewardRules.Describe(old,true).Contains("2 of 6 victories remaining"),
            "inventory and active descriptions explain duration timing");
        var d=WithRewards(New(),s with{EarnedFunds=0m}); var revision=d.Snapshot!.Rewards!.Revision;
        Ok(d.ActivateReward(pickup.Id));
        Check(d.Snapshot!.Rewards!.Revision==revision+1,"replacement advances checkpoint revision");
        d=RunDirector.Restore(d.Snapshot);
        Check(d.Snapshot!.Rewards!.Active[1]==pickup,"replacement blueprint and fresh timer survive restore");
        var before=JsonSerializer.Serialize(d.Snapshot);
        Check(!d.ActivateReward(pickup.Id).IsAccepted&&JsonSerializer.Serialize(d.Snapshot)==before,"repeated activation rejects atomically");
        Confirm(d);
        var store=new RunStore(Path.Combine(TestBase(),"ExpeditionReplacementTests-"+Guid.NewGuid().ToString("N")));
        Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"replacement checkpoint commits");
        var load=store.LoadActiveRun(ModAvailability.None);
        Check(load.CanResume&&load.Snapshot!.Rewards!.Active[1]==pickup,"replacement tank identity and full duration survive disk reload");
    }
    static void TwentyFour() {
        var d=New();
        for(var round=1;round<=24;round++) {
            var r=Fight(d); Check(r.Plan.Round==round,"real battle index");
            var boss=round%4==0;
            Check((r.Plan.Extras!.BossBlueprintId!=null)==boss,"fourth battle isboss");
            if(boss) { Check(r.Plan.Extras.BossEra==Eras[Math.Min((round-1)/4+1,5)].NativeId,"next nativeera boss"); Check(r.Plan.Enemies.Sum(e=>e.Count)>1,"minions"); }
            Ok(d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.Victory,null){SurvivingAllies=2}));
            var s=d.Snapshot!; Check(s.Victories==round&&s.Round==round+1,"single round advance");
            d=RunDirector.Restore(s);
            if(s.Rewards!.Choices.Count>0) { Check(!d.BeginEncounter().IsAccepted,"pending loot blocks fight"); Ok(d.SkipRewardChoice()); }
            while(d.Snapshot!.Rewards!.Inventory.Count>3) Ok(d.DiscardReward(d.Snapshot.Rewards.Inventory[0].Id));
            if(boss) Check(d.Snapshot!.Phase==(round==24?RunPhase.AwaitingEndlessChoice:RunPhase.Building),"era boundary");
        }
        Ok(d.ContinueEndless()); Check(d.Snapshot!.Round==25,"endless after24"); Confirm(d); Win(d); Check(d.Snapshot!.Round==26,"endless continues");
    }
    static void Persistence() {
        var root=Path.Combine(TestBase(),"ExpeditionAdventureTests-"+Guid.NewGuid().ToString("N"));
        var d=New(); Confirm(d); var store=new RunStore(root);
        var saved=store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}); Check(saved.IsCommitted,string.Join(";",saved.Issues));
        var before=d.Snapshot!; Ok(d.ActivateReward(before.Rewards!.Inventory[0].Id));
        saved=store.SaveCheckpoint(d.Snapshot!); Check(saved.IsCommitted,"save activated item: "+string.Join(";",saved.Issues));
        Check(!store.SaveCheckpoint(before).IsCommitted,"older inventory revision rejected");
        Win(d); saved=store.SaveCheckpoint(d.Snapshot!); Check(saved.IsCommitted,string.Join(";",saved.Issues));
        var load=store.LoadActiveRun(ModAvailability.None); Check(load.CanResume,"v2 resumes");
        Check(JsonSerializer.Serialize(load.Snapshot!.Rewards)==JsonSerializer.Serialize(d.Snapshot!.Rewards),"full item roundtrip");
        d=RunDirector.Restore(load.Snapshot); Win(d);
        Check(d.Snapshot!.Rewards!.Active.Count==0,"duration survives reload and expires once");
        // Fixtures are left in temp for failure diagnosis. No game or user directory writes.
    }
    static void Recovery() {
        var d=New(); d=WithRewards(d,new RewardState{Active=new[]{Item(RewardKind.EmergencyRecovery,"recover",6,1m),Item(RewardKind.MassProduction,"mass",2)}});
        var r=Fight(d); var funds=d.Snapshot!.AvailableBudget;
        Ok(d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.FriendlyForceDestroyed,null)));
        Check(d.Snapshot!.Phase==RunPhase.AwaitingRecovery&&d.Snapshot.Defeats==0,"await recovery rather than permadeath");
        d=RunDirector.Restore(d.Snapshot!); Ok(d.Suspend()); Ok(d.Resume()); Ok(d.UseRecovery());
        Check(d.Snapshot!.Rewards!.Active.Count==1&&d.Snapshot.Rewards.Active[0].RemainingRounds==2,"only charge consumed");
        Check(d.Snapshot.AvailableBudget==funds,"no reward or penalty");
        Check(!d.SelectEncounter(d.Snapshot.Offers[0].OfferId).IsAccepted,"same encounter locked");
        var retry=Fight(d); Check(retry.Plan.PlanHash==r.Plan.PlanHash&&retry.Attempt.Generation>r.Attempt.Generation,"same encounter fresh attempt");
        Check(!d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.Victory,null)).IsAccepted,"late original callback inert");
        Ok(d.AcceptResult(new EncounterResult(retry.Attempt,retry.Plan,EncounterOutcome.FriendlyForceDestroyed,null)));
        Check(d.Snapshot!.Phase==RunPhase.Lost&&d.Snapshot.Defeats==1,"second defeat terminal");
    }
    static void NonVictory() {
        var d=New(); Ok(d.ActivateReward(d.Snapshot!.Rewards!.Inventory[0].Id)); var r=Fight(d);
        Ok(d.ReturnToEditor(r.Attempt)); Check(d.Snapshot!.Rewards!.Active[0].RemainingRounds==2,"refit no tick");
        r=Fight(d); Ok(d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.TechnicalFailure,null)));
        var restored=RunDirector.Restore(d.Snapshot!); Ok(restored.Resume()); Check(restored.Snapshot!.Rewards!.Active[0].RemainingRounds==2,"technical retry no tick");
    }
    static void Reroll() {
        var d=New(); var r=Fight(d); Ok(d.ReturnToEditor(r.Attempt));
        var before=d.Snapshot!; Ok(d.ActivateReward(before.Rewards!.Inventory[0].Id)); Confirm(d);
        Check(d.Snapshot!.SelectedOfferId==r.Plan.OfferId&&d.Snapshot.Offers.Any(p=>p.PlanHash==r.Plan.PlanHash),
            "ordinary item keeps selected challenge and plan identity");
        var itemRewards=d.Snapshot.Rewards! with {Inventory=d.Snapshot.Rewards!.Inventory.Append(Item(RewardKind.OperationalFreedom,"actual-reroll")).ToArray()};
        d=WithRewards(d,itemRewards);Ok(d.ActivateReward("actual-reroll"));Confirm(d);
        var req=d.CreateOfferRequest(); var cat=new SeededEncounterCatalogue(Definition,Options); var plans=cat.GenerateOffers(req);
        Check(plans.All(p=>p.OfferId!=r.Plan.OfferId)&&d.Snapshot!.OfferEconomy==null,"explicit reroll produces new offers and clears old economy");
        var altered=plans.Select(p=>p with{Extras=p.Extras! with{Reward=p.Extras.Reward with{Duration=6,RemainingRounds=6}}}).ToArray();
        Check(!new SeededEncounterCatalogue(Definition,Options).RestoreOffers(req,altered).IsSuccess,"rehydration rejects modified rewards");
        Ok(d.SetOffers(plans)); var snap=d.Snapshot!; Check(new SeededEncounterCatalogue(Definition,Options).RestoreOffers(req,snap.Offers).IsSuccess,"exact restore");
    }
    static void Validation() {
        Throws(()=>RewardRules.Validate(new RewardState{Revision=-1}));
        Throws(()=>RewardRules.Validate(new RewardState{Active=new[]{Item(RewardKind.LendLease)}}));
        Throws(()=>RewardRules.Validate(new RewardState{Inventory=new[]{Item(RewardKind.EmergencyRecovery,"a"),Item(RewardKind.EmergencyRecovery,"b")}}));
        var d=New(); var r=Fight(d); var before=JsonSerializer.Serialize(d.Snapshot);
        var bad=r.Plan with{Extras=r.Plan.Extras! with{Reward=Item(RewardKind.MassProduction,rounds:7)}};
        Check(!d.AcceptResult(new EncounterResult(r.Attempt,bad,EncounterOutcome.Victory,null)).IsAccepted,"modified outcome rejected"); Check(before==JsonSerializer.Serialize(d.Snapshot),"rejection atomic");
    }
    static void FinalExpiry() {
        var d=New();
        for(var n=1;n<24;n++) {
            Win(d);
            if(d.Snapshot!.Rewards!.Choices.Count>0) Ok(d.SkipRewardChoice());
            while(d.Snapshot!.Rewards!.Inventory.Count>3) Ok(d.DiscardReward(d.Snapshot.Rewards.Inventory[0].Id));
        }
        var s=d.Snapshot!;var basis=s.BlockBaseBudget.Value+s.SupplyBonus+s.Rewards!.EarnedFunds;
        var rewards=s.Rewards with{Active=new[]{Item(RewardKind.LastStand,"last",1,.25m)}};
        d=RunDirector.Restore(s with{Rewards=rewards,Phase=RunPhase.Building,ConfirmedDesign=null,DesignConfirmedForBlock=false,
            AvailableBudget=new ScenarioValue(basis*1.25m,Unit)});
        Confirm(d,basis*1.2m);Win(d);
        Check(!d.Snapshot!.DesignConfirmedForBlock,"expired bonus makes old design unaffordable");
        d=RunDirector.Restore(d.Snapshot);Check(d.Snapshot!.Phase==RunPhase.AwaitingEndlessChoice,"end choice needs no affordable old tank");
        Check(!d.Finish().IsAccepted,"pending finalboss loot cannot be overwritten");
        Ok(d.SkipRewardChoice());Ok(d.Suspend());Ok(d.Resume());Ok(d.ContinueEndless());
        Check(d.Snapshot!.Phase==RunPhase.Building,"refit before endless");
    }
    static void NamedBoss() {
        var def=Definition with{BossBlueprints=new Dictionary<string,string>{{Eras[0].NativeId,"fixture.tank.1.0"}}};
        var request=new OfferRequest("named-boss",17,4,0,Eras[0],new ScenarioValue(100m,Unit),6,def.Version){RulesVersion=2};
        var plans=new SeededEncounterCatalogue(def,Options).GenerateOffers(request);
        Check(plans.All(p=>p.Extras!.BossBlueprintId=="fixture.tank.1.0"&&!p.Extras.PlaceholderBoss),"explicit boss identity");
        var invalid=def with{BossBlueprints=new Dictionary<string,string>{{Eras[0].NativeId,"fixture.tank.0.0"}}};
        Check(!new SeededEncounterCatalogue(invalid,Options).TryGenerateOffers(request).IsSuccess,"wrong native era never substituted");
    }
    static void CuratedCampaign() {
        var d=New(3); var store=new RunStore(Path.Combine(TestBase(),"ExpeditionV3-"+Guid.NewGuid().ToString("N")));
        var sawSpecial=false;
        for(var round=1;round<=24;round++) {
            var r=Fight(d); var s=d.Snapshot!; var boss=round%4==0;
            Check(s.SchemaVersion==3&&s.Offers.Count==(boss?1:3),"single curated boss / three normal cards");
            if(boss) {
                Check(r.Plan.Extras!.CuratedBoss&&r.Plan.Risk==EncounterRisk.Standard,"explicit curated fixed profile");
                var pool=Definition.Eras[Math.Min((round-1)/4+1,5)].Enemies;
                Check(pool.Single(e=>e.Blueprint.LocalId==r.Plan.Extras.BossBlueprintId).CheckedPrice.Value==pool.Max(e=>e.CheckedPrice.Value),"highest cost upcoming-era boss");
                Check(!d.SetOffers(s.Offers.Concat(s.Offers).ToArray()).IsAccepted,"two boss cards rejected");
            }
            var special=r.Plan.Extras!.Special;
            if(special!=null) {sawSpecial=true;Check(!boss&&r.Plan.Extras.Reward.Rarity==RewardRarity.Special,"yellow event and item match");}
            Check(store.SaveCheckpoint(s,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"save active v3 encounter");
            var loaded=store.LoadActiveRun(ModAvailability.None); Check(loaded.CanResume,"v3 checkpoint resumes");
            var req=new OfferRequest(s.RunId,s.Seed,s.Round,s.BlockIndex,Eras[s.EraIndex],s.AvailableBudget,s.PendingSupplyPoints,s.CatalogueVersion)
                {RulesVersion=3,OfferRoll=s.Rewards!.OfferRoll};
            Check(new SeededEncounterCatalogue(Definition,Options).RestoreOffers(req,loaded.Snapshot!.Offers).IsSuccess,"pinned curated/special restore");
            // Round-trip through storage clones special.Enemies, so equality must
            // compare content rather than record-list reference identity.
            d=RunDirector.Restore(loaded.Snapshot); var fresh=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(fresh.Attempt));
            Check(!d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.Victory,null)).IsAccepted,"pre-resume result stale");
            Ok(d.AcceptResult(new EncounterResult(fresh.Attempt,fresh.Plan,EncounterOutcome.Victory,null)));
            if(d.Snapshot!.Rewards!.Choices.Count>0) Ok(d.SkipRewardChoice());
            while(d.Snapshot!.Rewards!.Inventory.Count>3) Ok(d.DiscardReward(d.Snapshot.Rewards.Inventory[0].Id));
            Check(store.SaveCheckpoint(d.Snapshot!).IsCommitted,"save v3 victory once");
        }
        Check(sawSpecial,"v3 walk offered a special");
        var old=new OfferRequest("legacy2",17,4,0,Eras[0],new ScenarioValue(100m,Unit),6,Definition.Version){RulesVersion=2};
        var oldPlans=new SeededEncounterCatalogue(Definition,Options).GenerateOffers(old);
        Check(oldPlans.Count==3&&oldPlans.All(p=>!p.Extras!.CuratedBoss&&p.Extras.Special==null),"existing schema2 boss offers remain three");
        Check(!JsonSerializer.Serialize(oldPlans[0].Extras).Contains("CuratedBoss")&&!JsonSerializer.Serialize(oldPlans[0].Extras).Contains("Special"),"default metadata does not change old hashes");
        Check(new SeededEncounterCatalogue(Definition,Options).RestoreOffers(old,oldPlans).IsSuccess,"old boss exact restore");
    }
    static void Specials() {
        var seen=new HashSet<SpecialPreset>(); var opportunities=0;
        var designs=Enumerable.Range(1,18).Select(i=>new SpecialDesign("tank"+i,i*10m)).ToArray();
        for(var seed=-50;seed<350;seed++) {
            if(SpecialBattleRules.ShouldOffer(seed,3,0)) opportunities++;
            Check(!SpecialBattleRules.ShouldOffer(seed,4,0),"no boss specials");
            foreach(var budget in new[]{120m,400m,700m}) {
                var b=SpecialBattleRules.Create(seed,3,0,budget,designs); if(b==null)continue;
                seen.Add(b.Preset); var cost=b.Enemies.Sum(e=>designs.Single(d=>d.BlueprintId==e.BlueprintId).Cost*e.Count);
                Check(cost>=budget*.65m&&cost<=budget*1.25m,"native price budget window");
                var count=b.Enemies.Sum(e=>e.Count);Check(count>0&&count<=12&&b.Enemies.All(e=>e.Count>0),"native twelve-enemy cap");
                Check(b.Preset!=SpecialPreset.RatPlague||count is>=8 and<=12,"rat count");
                Check(b.Preset!=SpecialPreset.SteelWall||count<=3,"steel count");
                Check(b.Preset!=SpecialPreset.Crossfire||count is>=4 and<=8,"split count");
                Check(JsonSerializer.Serialize(b)==JsonSerializer.Serialize(SpecialBattleRules.Create(seed,3,0,budget,designs.Reverse().ToArray())),"stable across input ordering");
            }
        }
        Check(seen.Count==3&&opportunities is>70 and<170,"all presets and reasonable opportunity frequency");
        Check(SpecialBattleRules.Create(1,1,0,0m,designs)==null,"invalid budget safe");
        Check(SpecialBattleRules.Create(1,1,0,100m,new[]{new SpecialDesign("same",10m),new SpecialDesign("same",20m)})==null,"conflicting identity excluded");
        _=SpecialBattleRules.Create(long.MinValue,1,0,decimal.MaxValue,new[]{new SpecialDesign("huge",decimal.MaxValue)});
        var gold=Item(RewardKind.EliteWorkshop,"gold",4,.25m) with{Rarity=RewardRarity.Special};
        Check(RewardRules.Fleet(new RewardState{Version=3,Active=new[]{gold}},100m,10m).EffectiveBudget==125m,"gold magnitude implemented");
    }
    static IReadOnlyList<EncounterPlan> Generate(RunDirector d) =>
        new SeededEncounterCatalogue(Definition,Options).GenerateOffers(d.CreateOfferRequest());
    static string OffersJson(RunDirector d) => JsonSerializer.Serialize(d.Snapshot!.Offers);
    static EncounterPlan Standard(RunDirector d, IReadOnlyList<EncounterPlan>? plans = null) =>
        (plans ?? d.Snapshot!.Offers).Single(p => p.Risk == EncounterRisk.Standard);
    static void WinPlan(RunDirector d, EncounterPlan plan) {
        Ok(d.SelectEncounter(plan.OfferId)); Ok(d.BeginEncounter());
        var request=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(request.Attempt));
        Ok(d.AcceptResult(new EncounterResult(request.Attempt,request.Plan,EncounterOutcome.Victory,null){SurvivingAllies=1}));
        ClearRewards(d);
    }
    static void Schema6FixedOffers() {
        var d=New(6); Confirm(d);
        var first=Generate(d); Ok(d.SetOffers(first));
        var before=OffersJson(d); var ids=first.Select(p=>p.OfferId).ToHashSet(StringComparer.Ordinal);
        var rewards=d.Snapshot!.Rewards! with { Inventory=d.Snapshot.Rewards.Inventory.Concat(new[]{
            Item(RewardKind.EliteWorkshop,"elite-1",3,.2m), Item(RewardKind.EliteWorkshop,"elite-2",3,.2m),
            Item(RewardKind.ExperimentalContract,"contract",3,.35m), Item(RewardKind.MassProduction,"mass",3,.25m),
            Item(RewardKind.OperationalFreedom,"freedom",3,1m) }).ToArray() };
        d=WithRewards(d,rewards);
        Ok(d.ActivateReward("elite-1")); Check(OffersJson(d)==before,"schema 6 activation keeps offers");
        Ok(d.ActivateReward("elite-2")); Check(OffersJson(d)==before,"schema 6 replacement keeps offers");
        Ok(d.ActivateReward("contract")); Check(OffersJson(d)==before,"contract keeps the running round");
        Ok(d.ActivateReward("mass")); Check(OffersJson(d)==before,"ordinary item keeps offers");
        Ok(d.DiscardActiveReward("elite-2")); Check(OffersJson(d)==before,"schema 6 discard keeps offers");
        Check(Standard(d).Extras!.EnemyBudgetMultiplier==1m,"the running round keeps its pinned multiplier");
        var roll=d.Snapshot!.Rewards!.OfferRoll;
        Ok(d.ActivateReward("freedom"));
        Check(d.Snapshot!.Offers.Count==0&&d.Snapshot.Rewards!.OfferRoll==roll+1,"operational freedom rerolls the fixed offers");
        var second=Generate(d);
        Check(second.All(p=>!ids.Contains(p.OfferId)),"the reroll produced new offer ids");
        Check(Standard(d,second).Extras!.EnemyBudgetMultiplier==1.2m,"post-activation offers include the contract");
    }
    static void Schema6BudgetAndContract() {
        var plain=New(6); Confirm(plain); var plainTarget=Standard(plain,Generate(plain)).Extras!.EnemyBudgetTarget;
        var elite=New(6);
        elite=WithRewards(elite,elite.Snapshot!.Rewards! with { Inventory=elite.Snapshot.Rewards.Inventory
            .Append(Item(RewardKind.EliteWorkshop,"elite",3,.2m)).ToArray() });
        Ok(elite.ActivateReward("elite")); Confirm(elite);
        Check(Standard(elite,Generate(elite)).Extras!.EnemyBudgetTarget==plainTarget,
            "schema 6 enemy target ignores the item budget");
        var v5plain=New(5); Confirm(v5plain); var v5plainTarget=Standard(v5plain,Generate(v5plain)).Extras!.EnemyBudgetTarget;
        var v5elite=New(5);
        v5elite=WithRewards(v5elite,v5elite.Snapshot!.Rewards! with { Inventory=v5elite.Snapshot.Rewards.Inventory
            .Append(Item(RewardKind.EliteWorkshop,"elite",3,.2m)).ToArray() });
        Ok(v5elite.ActivateReward("elite")); Confirm(v5elite);
        Check(Standard(v5elite,Generate(v5elite)).Extras!.EnemyBudgetTarget>v5plainTarget,
            "schema 5 keeps its item-inclusive target");

        var timing=New(6); Confirm(timing);
        var running=Generate(timing); Ok(timing.SetOffers(running));
        Check(Standard(timing,running).Extras!.EnemyBudgetMultiplier==1m,"contract not active yet");
        timing=WithRewards(timing,timing.Snapshot!.Rewards! with { Inventory=timing.Snapshot.Rewards.Inventory
            .Append(Item(RewardKind.ExperimentalContract,"contract",3,.35m)).ToArray() });
        Ok(timing.ActivateReward("contract"));
        Check(Standard(timing).Extras!.EnemyBudgetMultiplier==1m,"the running round keeps its old multiplier");
        WinPlan(timing,Standard(timing));
        var next=Generate(timing);
        Check(Standard(timing,next).Extras!.EnemyBudgetMultiplier==1.2m,
            "the first offers generated after activation include x1.2");
    }
    static void Schema6RunTotals() {
        var root=Path.Combine(TestBase(),"ExpeditionV6Totals-"+Guid.NewGuid().ToString("N"));
        var store=new RunStore(root);
        var d=New(6); Confirm(d);
        var plans=Generate(d); Ok(d.SetOffers(plans));
        var first=Standard(d,plans); Ok(d.SelectEncounter(first.OfferId)); Ok(d.BeginEncounter());
        var r1=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(r1.Attempt));
        Ok(d.AcceptResult(new EncounterResult(r1.Attempt,r1.Plan,EncounterOutcome.Victory,null)
            {SurvivingAllies=1,EnemiesDestroyed=4,FriendliesLost=1,PlayerTankSurvived=true}));
        Check(d.Snapshot!.Attempts==1&&d.Snapshot.EnemiesDestroyed==4&&d.Snapshot.FriendliesLost==1,"first attempt totals");
        ClearRewards(d);
        var secondPlans=Generate(d); Ok(d.SetOffers(secondPlans));
        var second=Standard(d,secondPlans); Ok(d.SelectEncounter(second.OfferId)); Ok(d.BeginEncounter());
        var r2=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(r2.Attempt));
        Ok(d.AcceptResult(new EncounterResult(r2.Attempt,r2.Plan,EncounterOutcome.TechnicalFailure,null)
            {EnemiesDestroyed=0,FriendliesLost=0}));
        Check(d.Snapshot!.Attempts==2&&d.Snapshot.EnemiesDestroyed==4&&d.Snapshot.FriendliesLost==1,"aborted attempt counts, no invented losses");
        Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"suspended v6 totals save");
        var loaded=store.LoadActiveRun(ModAvailability.None);
        Check(loaded.Snapshot!.Attempts==2&&loaded.Snapshot.EnemiesDestroyed==4&&loaded.Snapshot.FriendliesLost==1,"v6 totals load");
        d=RunDirector.Restore(loaded.Snapshot); Ok(d.Resume());
        var idsBeforeLoss=d.Snapshot!.Offers.Select(p=>p.OfferId).ToArray();
        var r3=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(r3.Attempt));
        Ok(d.AcceptResult(new EncounterResult(r3.Attempt,r3.Plan,EncounterOutcome.FriendlyForceDestroyed,null)
            {EnemiesDestroyed=2,FriendliesLost=3}));
        Check(d.Snapshot!.Phase==RunPhase.ChoosingEncounter&&d.Snapshot.Offers.Select(p=>p.OfferId).SequenceEqual(idsBeforeLoss),
            "schema 6 loss keeps the fixed round offers");
        var restored=RunDirector.Restore(d.Snapshot!);
        Check(restored.Snapshot!.Offers.Count==3&&restored.Snapshot.Attempts==3,"lost offers and totals survive restore");
        Ok(d.Retire());
        Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"terminal v6 totals save");
        var record=store.ReadRecord(d.Snapshot!.RunId);
        Check(record.Record!=null&&record.Record.Attempts==3&&record.Record.EnemiesDestroyed==6
            &&record.Record.FriendliesLost==4,"record carries the schema 6 totals");
    }
    static void Schema6Retries() {
        var d=New(6); Confirm(d); var plans=Generate(d); Ok(d.SetOffers(plans));
        d=WithRewards(d,d.Snapshot!.Rewards! with { Inventory=new[]{Item(RewardKind.EliteWorkshop,"active",4,.2m)} });
        Ok(d.ActivateReward("active"));
        var cards=OffersJson(d); var before=d.Snapshot!;
        var timer=JsonSerializer.Serialize(before.Rewards!.Active);
        for(var i=1;i<=3;i++) {
            var plan=d.Snapshot!.Offers[i%3]; Ok(d.SelectEncounter(plan.OfferId)); Ok(d.BeginEncounter());
            var request=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(request.Attempt));
            Ok(d.AcceptResult(new(request.Attempt,request.Plan,i%2==0?EncounterOutcome.CombatRetreat:EncounterOutcome.FriendlyForceDestroyed,null)));
            Check(d.Snapshot!.Phase==RunPhase.ChoosingEncounter && d.Snapshot.RoundAttempts==i&&d.Snapshot.Defeats==i&&d.Snapshot.Attempts==i,"retry counters");
            Check(OffersJson(d)==cards&&d.Snapshot.SelectedOfferId==plan.OfferId,"same cards and selected offer");
            Check(JsonSerializer.Serialize(d.Snapshot.Rewards!.Active)==timer,"no item ticks on failure");
            Check(d.Snapshot.Rewards.EarnedFunds==before.Rewards.EarnedFunds&&d.Snapshot.PendingSupplyPoints==before.PendingSupplyPoints&&d.Snapshot.Rewards.GrantedOffers.Count==0,"no loss rewards");
            Check(!d.AcceptResult(new(request.Attempt,request.Plan,EncounterOutcome.Victory,null)).IsAccepted,"late victory rejected");
            d=RunDirector.Restore(d.Snapshot); Check(OffersJson(d)==cards,"retry restores exact cards");
        }
        Ok(d.SelectEncounter(plans[1].OfferId)); Ok(d.BeginEncounter()); var won=d.CreateEncounterRequest(); Ok(d.ReportCombatStarted(won.Attempt));
        Ok(d.AcceptResult(new(won.Attempt,won.Plan,EncounterOutcome.Victory,null){FriendliesLost=0}));
        Check(d.Snapshot!.RoundAttempts==0&&d.Snapshot.Defeats==3&&d.Snapshot.Attempts==4&&d.Snapshot.Victories==1,"victory resets current round only");
        Check(d.Snapshot.Score==ScoreRules.Battle(0,EncounterRisk.Standard,false,0,3,1.5m,false).Points,"retry score committed once");
    }
    static void Schema6Ironman() {
        var d=New(6); d=RunDirector.Restore(d.Snapshot! with{Ironman=true}); Confirm(d);
        d=WithRewards(d,d.Snapshot!.Rewards! with{Active=new[]{Item(RewardKind.EmergencyRecovery,"forbidden-charge",6,1m)}});
        var r=Fight(d); Ok(d.AcceptResult(new(r.Attempt,r.Plan,EncounterOutcome.FriendlyForceDestroyed,null)));
        Check(d.Snapshot!.Phase==RunPhase.Lost&&d.Snapshot.Ironman&&d.Snapshot.RoundAttempts==1,"Ironman death ends run");
        Check(!d.UseRecovery().IsAccepted&&!d.BeginEncounter().IsAccepted,"Ironman has no recovery or retry");
        var normal=New(6); Confirm(normal); normal=WithRewards(normal,normal.Snapshot!.Rewards! with{Active=new[]{Item(RewardKind.EmergencyRecovery,"old-charge",6,1m)}});
        r=Fight(normal); Ok(normal.AcceptResult(new(r.Attempt,r.Plan,EncounterOutcome.FriendlyForceDestroyed,null)));
        Check(normal.Snapshot!.Phase==RunPhase.ChoosingEncounter,"schema 6 never enters recovery, even with injected charge");
    }
    static void Schema6Retire() {
        var d=New(6); var root=Path.Combine(TestBase(),"Retire-"+Guid.NewGuid().ToString("N")); var store=new RunStore(root);
        var s=d.Snapshot! with{Score=1234.567890123456789m,Ironman=true,RoundAttempts=2,Defeats=2,Attempts=2};
        d=RunDirector.Restore(s); Check(store.SaveCheckpoint(d.Snapshot!).IsCommitted,"mode fields save");
        Check(!store.SaveCheckpoint(d.Snapshot! with{Ironman=false}).IsCommitted,"mode immutable after run starts");
        var loaded=store.LoadActiveRun(ModAvailability.None); Check(loaded.Snapshot!.Score==s.Score&&loaded.Snapshot.RoundAttempts==2&&loaded.Snapshot.Ironman,"decimal score/mode/penalty load");
        Check(store.SaveCheckpoint(loaded.Snapshot with{Score=0m}).Status==RunStoreWriteStatus.Rejected,"stale score cannot erase earned points");
        Check(store.SaveCheckpoint(loaded.Snapshot).Status==RunStoreWriteStatus.Unchanged,"byte-identical loaded checkpoint is inert");
        d=RunDirector.Restore(loaded.Snapshot); Ok(d.Retire()); Check(store.SaveCheckpoint(d.Snapshot!).IsCommitted,"retire commit");
        Check(store.LoadActiveRun(ModAvailability.None).Status==RunStoreLoadStatus.NoActiveRun,"retire clears active index");
        var record=store.ReadRecord(s.RunId).Record!; Check(record.Phase==RunPhase.Retired&&record.Score==s.Score&&record.Ironman&&record.RoundAttempts==2&&record.Era==s.EraSchedule[0],"retire records all fields");
        Check(!d.Retire().IsAccepted&&!d.Resume().IsAccepted,"retire terminal");
    }
    static void Schema6Score() {
        Check(ScoreRules.Battle(0,EncounterRisk.Cautious,false,1,0,1m,false).Points==56.25m,"Recruit cautious");
        Check(ScoreRules.Battle(2,EncounterRisk.Dangerous,true,0,1,1.25m,false).Points==843.75m,"era 2 dangerous boss flawless one retry");
        Check(ScoreRules.Battle(2,EncounterRisk.Dangerous,true,0,1,1.25m,true).Points==1096.875m,"Ironman x1.3");
        Check(ScoreRules.Battle(0,EncounterRisk.Standard,false,null,0,2m,false).Points==160m,"unknown losses no bonus, Ace");
        Check(ScoreRules.Battle(0,EncounterRisk.Standard,false,0,0,1.5m,false).Points==156.25m,"Veteran flawless");
        Check(ScoreRules.RetryFactor(1)==.75m&&ScoreRules.RetryFactor(2)==.5625m&&ScoreRules.RetryFactor(99)==.25m,"retry floor");
    }
    static void Schema6Contact() {
        Check(!ContactRules.HasContact(59.999,null,null)&&ContactRules.HasContact(60,null,null),"60-second boundary");
        Check(ContactRules.HasContact(0,1,null)&&ContactRules.HasContact(0,null,1)&&ContactRules.HasContact(0,null,null,true),"death or verified hit");
        var d=New(6); var r=Fight(d); var cards=OffersJson(d); Ok(d.ReturnToEditor(r.Attempt,new(false)));
        Check(d.Snapshot!.Attempts==1&&d.Snapshot.RoundAttempts==0&&d.Snapshot.Defeats==0&&OffersJson(d)==cards,"precontact free");
        Ok(d.BeginEncounter());r=d.CreateEncounterRequest();Ok(d.ReportCombatStarted(r.Attempt)); Ok(d.ReturnToEditor(r.Attempt,new(true,1,0,true,60m)));
        Check(d.Snapshot!.Attempts==2&&d.Snapshot.RoundAttempts==1&&d.Snapshot.Defeats==1&&OffersJson(d)==cards,"postcontact retry");
        Check(!d.ReturnToEditor(r.Attempt,new(true)).IsAccepted,"duplicate return inert");
        var iron=New(6);iron=RunDirector.Restore(iron.Snapshot! with{Ironman=true});r=Fight(iron);var before=JsonSerializer.Serialize(iron.Snapshot);
        Check(!iron.ReturnToEditor(r.Attempt,new(true)).IsAccepted&&JsonSerializer.Serialize(iron.Snapshot)==before,"Ironman postcontact denied atomically");
        Ok(iron.ReturnToEditor(r.Attempt,new(false)));
    }
    static void Schema6Loot() {
        var oldRecovery=0;
        for(var i=0;i<10000;i++) {
            Check(RewardRules.Create(i,1,"loot."+i,2,rulesVersion:6).Kind!=RewardKind.EmergencyRecovery,"no schema 6 recovery loot");
            if(RewardRules.Create(i,1,"loot."+i,2,rulesVersion:5).Kind==RewardKind.EmergencyRecovery) oldRecovery++;
        }
        Check(oldRecovery>0,"schema 5 still rolls recovery");
        var d=New(6);for(var i=0;i<4;i++)Win(d);
        Check(d.Snapshot!.Rewards!.Choices.All(i=>i.Kind!=RewardKind.EmergencyRecovery),"boss loot excludes recovery too");
    }
    static void CuratedRecovery() {
        var d=New(3);for(var i=0;i<3;i++)Win(d);
        var s=d.Snapshot!;
        d=RunDirector.Restore(s with{Rewards=s.Rewards! with{Revision=s.Rewards!.Revision+1,
            Active=new[]{Item(RewardKind.EmergencyRecovery,"boss-recovery",6,1m)},
            Inventory=s.Rewards.Inventory.Where(item=>item.Kind!=RewardKind.EmergencyRecovery)
                .Append(Item(RewardKind.OperationalFreedom,"boss-reroll")).ToArray()}});
        var before=JsonSerializer.Serialize(d.Snapshot);
        Check(!d.ActivateReward("boss-reroll").IsAccepted&&JsonSerializer.Serialize(d.Snapshot)==before,"boss cannot be rerolled; item retained");
        var r=Fight(d);Check(d.Snapshot!.Offers.Count==1,"single selected boss");
        Ok(d.AcceptResult(new EncounterResult(r.Attempt,r.Plan,EncounterOutcome.FriendlyForceDestroyed,null)));
        var store=new RunStore(Path.Combine(TestBase(),"ExpeditionBossRecovery-"+Guid.NewGuid().ToString("N")));
        Check(store.SaveCheckpoint(d.Snapshot!,new[]{new BlueprintSnapshot(Hash,Bytes)}).IsCommitted,"one-card recovery saved");
        var load=store.LoadActiveRun(ModAvailability.None);d=RunDirector.Restore(load.Snapshot!);Ok(d.UseRecovery());
        var retry=Fight(d);Check(retry.Plan.PlanHash==r.Plan.PlanHash&&retry.Attempt.Generation>r.Attempt.Generation,"same curated plan with fresh generation");
        Ok(d.AcceptResult(new EncounterResult(retry.Attempt,retry.Plan,EncounterOutcome.Victory,null)));
        Check(d.Snapshot!.Victories==4&&d.Snapshot.Rewards!.Choices.Count==3,"one boss win gives the normal purple choice");
    }
}
