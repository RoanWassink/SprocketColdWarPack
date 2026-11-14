using System.Text.Json;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration.Tests;

internal static class Phase1Checks
{
    private const string Unit = "fixture.phase1";
    private sealed class Fixture
    {
        internal readonly RunStore Store;
        internal readonly FakeAdapter Adapter = new(Unit);
        internal RecordingPresenter Presenter = new();
        internal readonly List<BattleLogRow> Rows = new();
        internal readonly List<string> Logs = new();
        internal readonly CatalogueDefinition Definition;
        internal ExpeditionController Controller;
        internal Fixture(string root, bool testPool = false)
        {
            Store = new(root);
            var eras = Enumerable.Range(0,2).Select(i => new EraReference("fixture.phase1.era."+i)).ToArray();
            Definition = new("fixture.phase1", CatalogueDefinition.FixtureLabel, true, Unit,
                eras.Select((era,i)=>new CatalogueEraContent(era,
                    Enumerable.Range(0,3).Select(m=>new MapEntry(era,"map."+m,new string('a',64),new[]{"layout"},Array.Empty<ModRequirement>(),ContentQualification.Fixture)).ToArray(),
                    new[]{10m,20m,30m,40m}.Select((p,n)=>new EnemyDesignEntry(
                        new BlueprintReference($"tank.{i}.{n}",new string('b',64),era,Array.Empty<ModRequirement>()),
                        n==3?EnemyRole.Heavy:n==0?EnemyRole.Recon:EnemyRole.Line,p,new(p,Unit),ContentQualification.Fixture)
                        {FormationFamily="family."+n}).ToArray())).ToArray());
            if (testPool) Definition = Definition with { TestPool = Enumerable.Range(0, 7).Select(i =>
                new TestPoolDesign("Experimental " + i, new EnemyDesignEntry(
                    new BlueprintReference("test." + i, new string('c', 64), eras[^1], Array.Empty<ModRequirement>()),
                    EnemyRole.Heavy, 1_000_000m, new(1_000_000m, Unit), ContentQualification.Fixture))).ToArray() };
            Controller = Create();
        }
        internal ExpeditionController Create() => new(new(Definition,new(ContentQualification.Fixture,false,true)), Store, Adapter,
            Presenter, ()=>ModAvailability.None, ()=>Guid.NewGuid().ToString("D"), ()=>91234,
            ()=>new(1.5m,true), Rows.Add, _=>throw new IOException("unavailable diagnostics"), testPoolLog: Logs.Add);
        internal Task Start(bool ironman=false) => Controller.StartNewRunAsync(new(new(200m,Unit), Definition.Eras.Select(e=>e.Era).ToArray(),Definition.Version)
            {AdventureRules=true,AdventureVersion=6,Ironman=ironman,TestPool=Definition.TestPool!=null});
        internal async Task Battle(EncounterOutcome outcome=EncounterOutcome.Victory)
        {
            Adapter.Outcome=outcome;
            if(!Controller.Snapshot!.DesignConfirmedForBlock) await Controller.RefreshEncounterDesignAsync();
            await Controller.SelectOfferAsync(Controller.Snapshot!.Offers.Single(o=>o.Risk==EncounterRisk.Standard).OfferId);
            await Controller.BeginEncounterAsync();
        }
        internal async Task Reload()
        {
            Presenter=new(); Controller=Create(); await Controller.ResumeAsync();
            Assert.False(Presenter.Has<ErrorView>()||Presenter.Has<BlockedView>(),"resume is valid");
        }
    }

    // 0.3.4 (user: "Yes, after any fight"): a lost test round moves the cursor
    // and the same round is offered again from the next four tanks.
    internal static async Task TestPool(string root)
    {
        var f = new Fixture(root, true); await f.Start();
        Assert.True(f.Controller.Snapshot!.TestPool, "test flag fixed on run");
        Assert.Equal("test.0", f.Controller.Snapshot.Offers[0].Enemies[0].BlueprintId, "first page starts at first design");
        await f.Battle(EncounterOutcome.FriendlyForceDestroyed);
        Assert.True(f.Adapter.LastRequest!.TestPool, "native adapter receives test flag");
        Assert.True(f.Rows.Single().TestPool && f.Rows.Single().EnemyDesigns == "Experimental 0|Experimental 1|Experimental 2|Experimental 3", "test row names and flag");
        Assert.Equal(4_000_000m, f.Rows.Single().EnemyCost, "test price of all four tanks is measured even above budget");
        var s = f.Controller.Snapshot!;
        Assert.Equal(4, s.TestPoolOffset, "loss advances the cursor by the tanks fought");
        Assert.True(s.Phase == RunPhase.ChoosingEncounter && s.Round == 1 && s.Defeats == 1 && s.RoundAttempts == 1
            && s.Rewards!.GrantedOffers.Count == 0, "same round, defeat and attempt counted, no reward");
        Assert.True(s.Offers.Count == 3 && s.Offers.All(o => o.Enemies.Select(e => e.BlueprintId).SequenceEqual(new[] { "test.4", "test.5", "test.6", "test.0" })),
            "the round is offered again from the fifth design");
        Assert.Equal(EncounterRisk.Standard, s.Offers.Single(o => o.OfferId == s.SelectedOfferId).Risk, "lost card's risk stays selected for RETRY");
        Assert.True(f.Presenter.Last<ResultView>().CanRetryLoss, "loss still offers RETRY");
        Assert.Equal("Expedition test pool: offset 0 -> 4 after FriendlyForceDestroyed", f.Logs.Single(), "one cursor line per fought battle");
        var offers = JsonSerializer.Serialize(s.Offers);
        await f.Reload();
        Assert.True(f.Controller.Snapshot!.TestPool && f.Controller.Snapshot.TestPoolOffset == 4, "disk restores test mode and advanced cursor");
        Assert.Equal(offers, JsonSerializer.Serialize(f.Controller.Snapshot.Offers), "reload restores the re-pinned test offers");
        await f.Controller.PrepareLossRetryAsync();
        Assert.Equal(offers, JsonSerializer.Serialize(f.Presenter.Last<OffersView>().Offers), "RETRY shows the next tanks");
        await f.Battle();
        Assert.Equal(8, f.Controller.Snapshot!.TestPoolOffset, "victory commits next page");
        Assert.Equal("Expedition test pool: offset 4 -> 8 after Victory", f.Logs[^1], "victory cursor line");
        await f.Controller.ContinueAsync();
        Assert.Equal("test.1", f.Controller.Snapshot!.Offers[0].Enemies[0].BlueprintId, "next page wraps to the second design");
        await f.Controller.RetireAsync();
        Assert.True(f.Store.ReadRecord(f.Controller.Snapshot!.RunId).Record!.TestPool, "terminal record stores test mode");
        await f.Controller.ShowStartAsync();
        Assert.True(f.Presenter.Last<StartView>().Records.Single().TestPool, "records presenter receives TEST identity");
        Console.WriteLine("PASS test pool controller: loss advances cursor, re-pinned round, reload, RETRY, victory, retire and records");
    }

    internal static async Task TestPoolEditorReturn(string root)
    {
        var f = new Fixture(root, true); await f.Start(); var c = f.Controller;
        var cards = JsonSerializer.Serialize(c.Snapshot!.Offers);
        var active = await Held(f);
        await c.ReturnToEditorAsync(active, new EditorReturnContext(false, 0, 0, true, 10m));
        Assert.True(c.Snapshot!.TestPoolOffset == 0 && JsonSerializer.Serialize(c.Snapshot.Offers) == cards && f.Logs.Count == 0,
            "return before contact keeps cursor and cards");
        active = await Held(f);
        await c.ReturnToEditorAsync(active, new EditorReturnContext(true, 1, 0, true, 60m));
        var s = c.Snapshot!;
        Assert.True(s.TestPoolOffset == 4 && s.Defeats == 1 && s.RoundAttempts == 1, "retreat after contact advances the cursor");
        Assert.Equal("test.4", s.Offers[0].Enemies[0].BlueprintId, "retreat re-pins the round from the next tanks");
        Assert.Equal(EncounterRisk.Standard, s.Offers.Single(o => o.OfferId == s.SelectedOfferId).Risk, "retreated risk stays selected");
        Assert.Equal(s.SelectedOfferId, f.Presenter.Last<OffersView>().SelectedOfferId, "host sees a selected card to return to the editor");
        Assert.True(f.Rows[^1].Outcome == "Retreat" && f.Logs.Single() == "Expedition test pool: offset 0 -> 4 after Retreat", "retreat CSV and cursor line");
        await f.Reload(); Assert.Equal(4, f.Controller.Snapshot!.TestPoolOffset, "retreat cursor survives restart");
        Console.WriteLine("PASS test pool editor return: free abort keeps tanks, retreat advances and re-pins with selection");

        static async Task<AttemptIdentity> Held(Fixture f)
        {
            var c = f.Controller; f.Adapter.HeldResult = new();
            await c.SelectOfferAsync(c.Snapshot!.Offers.Single(o => o.Risk == EncounterRisk.Standard).OfferId);
            using var cancellation = new CancellationTokenSource();
            var task = c.BeginEncounterAsync(cancellation.Token);
            cancellation.Cancel(); f.Adapter.HeldResult.SetResult(true); await task;
            f.Adapter.HeldResult = null;
            return c.Snapshot!.CurrentAttempt!;
        }
    }

    internal static async Task TestPoolIronmanAndNormalLoss(string root)
    {
        var f = new Fixture(Path.Combine(root, "ironman"), true); await f.Start(true);
        await f.Battle(EncounterOutcome.FriendlyForceDestroyed);
        var s = f.Controller.Snapshot!;
        Assert.True(s.Phase == RunPhase.Lost && s.TestPoolOffset == 4 && !f.Presenter.Last<ResultView>().CanRetryLoss, "Ironman test loss still ends the run");
        var normal = new Fixture(Path.Combine(root, "normal")); await normal.Start();
        var cards = JsonSerializer.Serialize(normal.Controller.Snapshot!.Offers);
        await normal.Battle(EncounterOutcome.FriendlyForceDestroyed);
        var n = normal.Controller.Snapshot!;
        Assert.True(n.TestPoolOffset == 0 && !n.TestPool && JsonSerializer.Serialize(n.Offers) == cards
            && n.Offers.Single(o => o.OfferId == n.SelectedOfferId).Risk == EncounterRisk.Standard && normal.Logs.Count == 0,
            "normal campaign loss keeps the same cards and selection");
        Console.WriteLine("PASS test pool Ironman loss terminal; normal loss unchanged");
    }

    internal static async Task NormalRetry(string root)
    {
        var f=new Fixture(root);await f.Start();var c=f.Controller;
        var cards=JsonSerializer.Serialize(c.Snapshot!.Offers);var items=JsonSerializer.Serialize(c.Snapshot.Rewards!.Active);
        for(var i=1;i<=2;i++)
        {
            await f.Battle(EncounterOutcome.FriendlyForceDestroyed);
            Assert.True(f.Presenter.Last<ResultView>().CanRetryLoss,"normal loss exposes retry");
            Assert.Equal(cards,JsonSerializer.Serialize(c.Snapshot!.Offers),"loss keeps cards");
            Assert.Equal(items,JsonSerializer.Serialize(c.Snapshot.Rewards!.Active),"loss keeps item timers");
            await f.Reload();c=f.Controller;
            Assert.Equal(i,c.Snapshot!.RoundAttempts,"disk restores current-round failures");
            Assert.Equal(i,c.Snapshot.Defeats,"disk restores defeats");
            Assert.Equal(0,c.Snapshot.Rewards!.GrantedOffers.Count,"no loot after defeat");
            await c.PrepareLossRetryAsync();
        }
        await f.Battle();
        Assert.Equal(70.3125m,c.Snapshot!.Score,"same pinned Veteran difficulty, two retries, unknown-loss bonus withheld");
        Assert.Equal(0,c.Snapshot.RoundAttempts,"victory resets penalty");
        Assert.Equal(70.3125m,f.Presenter.Last<ResultView>().ScoreGained,"victory exposes actual score increment");
        Assert.Equal(3,f.Rows.Count,"one row per completed attempt");
        Assert.Equal(70.3125m,f.Rows[^1].ScoreGained,"CSV score matches state");
        await f.Reload();Assert.Equal(70.3125m,f.Controller.Snapshot!.Score,"exact decimal score reload");
        Console.WriteLine("PASS schema 6 repeated loss -> reload -> same-offer retry -> score -> reload");
    }

    internal static async Task Ironman(string root)
    {
        var f=new Fixture(root);await f.Start(true);await f.Battle(EncounterOutcome.FriendlyForceDestroyed);
        var s=f.Controller.Snapshot!;
        Assert.True(s.Phase==RunPhase.Lost&&s.Ironman,"Ironman loss terminal");
        Assert.False(f.Presenter.Last<ResultView>().CanRetryLoss,"no Ironman defeat retry");
        Assert.Equal(RunStoreLoadStatus.NoActiveRun,f.Store.LoadActiveRun(ModAvailability.None).Status,"terminal index cleared");
        var record=f.Store.ReadRecord(s.RunId).Record!;
        Assert.True(record.Ironman&&record.Phase==RunPhase.Lost&&record.Attempts==1,"separate mode retained in record");
        Console.WriteLine("PASS schema 6 Ironman loss -> terminal record, no recovery/retry");
    }

    internal static async Task Retire(string root)
    {
        var f=new Fixture(root);await f.Start();await f.Battle();await f.Controller.ContinueAsync();
        var s=f.Controller.Snapshot!;await f.Controller.RetireAsync();
        var record=f.Store.ReadRecord(s.RunId).Record!;
        Assert.True(record.Phase==RunPhase.Retired&&record.Score==125m&&record.Era==s.EraSchedule[0],"Retire writes score and furthest era");
        Assert.Equal(RunStoreLoadStatus.NoActiveRun,f.Store.LoadActiveRun(ModAvailability.None).Status,"Retire clears active index");
        Assert.False(f.Store.LoadRun(s.RunId,ModAvailability.None).CanResume,"retired cannot resume");
        await f.Controller.ShowStartAsync();var shown=f.Presenter.Last<StartView>().Records.Single();
        Assert.True(shown.Phase==RunPhase.Retired&&shown.Score==125m&&shown.Era==s.EraSchedule[0]&&!shown.Ironman,"records use real fields");
        Console.WriteLine("PASS schema 6 Retire -> record -> index cleared -> real records view");
    }

    internal static async Task EditorReturn(string root,bool ironman)
    {
        var f=new Fixture(root);await f.Start(ironman);
        var c=f.Controller;var cards=JsonSerializer.Serialize(c.Snapshot!.Offers);
        await CancelHeld();
        var active=c.Snapshot!.CurrentAttempt!;
        await c.ReturnToEditorAsync(active,new EditorReturnContext(false,0,0,true,10m));
        Assert.True(c.Snapshot!.RoundAttempts==0&&c.Snapshot.Defeats==0&&c.Snapshot.Attempts==1,"before contact free, attempt recorded");
        Assert.Equal("PrecombatAborted",f.Rows[^1].Outcome,"free abort logged once");
        await CancelHeld();active=c.Snapshot!.CurrentAttempt!;var before=JsonSerializer.Serialize(c.Snapshot);
        await c.ReturnToEditorAsync(active,new EditorReturnContext(true,1,0,true,60m));
        if(ironman)
        {
            Assert.Equal(before,JsonSerializer.Serialize(c.Snapshot),"Ironman postcontact denial changes nothing");
            Assert.Equal(1,f.Rows.Count,"blocked return writes no attempt row");
        }
        else
        {
            Assert.True(c.Snapshot!.RoundAttempts==1&&c.Snapshot.Defeats==1&&c.Snapshot.Attempts==2,"postcontact counts one retry");
            Assert.Equal("Retreat",f.Rows[^1].Outcome,"postcontact CSV is Retreat");
            Assert.Equal(cards,JsonSerializer.Serialize(c.Snapshot.Offers),"retreat keeps pinned cards");
            await f.Reload();Assert.Equal(1,f.Controller.Snapshot!.RoundAttempts,"retreat survives restart");
        }
        Console.WriteLine($"PASS schema 6 {(ironman?"Ironman":"normal")} editor return, cancellation/generation guards, before/after contact");

        async Task CancelHeld()
        {
            f.Adapter.HeldResult=new();
            await c.SelectOfferAsync(c.Snapshot!.Offers[1].OfferId);
            using var cancellation=new CancellationTokenSource();
            var task=c.BeginEncounterAsync(cancellation.Token);
            // FakeAdapter reports combat synchronously before awaiting its result.
            Assert.Equal(RunPhase.Fighting,c.Snapshot!.Phase,"held fight is live");
            cancellation.Cancel();f.Adapter.HeldResult.SetResult(true);await task;
            Assert.Equal(0,c.Snapshot!.Victories,"cancelled late victory rejected");
            f.Adapter.HeldResult=null;
        }
    }

    internal static async Task BossFlow(string root)
    {
        var f=new Fixture(root);await f.Start();
        Assert.True(f.Presenter.Last<OffersView>().EraBoss?.Contains("tank.1.3")==true,"first era preview uses encounter's max-price boss");
        for(var i=0;i<3;i++){await f.Battle();await f.Controller.ContinueAsync();}
        Assert.Equal(1,f.Controller.Snapshot!.Offers.Count,"boss remains a single fixed card");
        await f.Battle();var result=f.Presenter.Last<ResultView>();
        Assert.True(result.ScoreGained>0m&&result.ScoreBreakdown?.Contains("boss x3")==true,"boss victory shows its score before loot");
        await f.Controller.ContinueAsync();Assert.True(f.Presenter.Last<ArmoryView>().Rewards.Choices.Count==3,"continue opens boss loot");
        await f.Controller.RewardCommandAsync("skip",null);await f.Controller.LeaveArmoryAsync();
        Assert.True(f.Presenter.Last<OffersView>().EraBoss?.Contains("tank.1.3")==true,"next-era preview after boss loot");
        Console.WriteLine("PASS schema 6 deterministic boss preview -> victory score -> boss loot -> next-era preview");
    }
}
