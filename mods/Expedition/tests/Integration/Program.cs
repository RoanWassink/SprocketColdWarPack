using System.Security.Cryptography;
using System.Text;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Integration;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "example.catalogue.json");
            var definition = CatalogueJson.Load(File.ReadAllText(path));
            var testBase = Path.Combine(AppContext.BaseDirectory, "test-temp");
            Directory.CreateDirectory(testBase);
            var root = Path.Combine(
                testBase,
                "expedition-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await Run(definition, root);
                await AdventureIntegrationChecks.Run(Path.Combine(root, "adventure"));
                await AdventureIntegrationChecks.Run(Path.Combine(root, "curated"), 3);
                await AdventureIntegrationChecks.Policy5ContractRoundtrip(
                    Path.Combine(root, "policy5-contract"));
                await AdventureIntegrationChecks.BattleLogWritesOneRow(
                    Path.Combine(root, "battle-log"));
                await Phase1Checks.NormalRetry(Path.Combine(root,"normal-retry"));
                await Phase1Checks.Ironman(Path.Combine(root,"ironman"));
                await Phase1Checks.Retire(Path.Combine(root,"retire"));
                await Phase1Checks.EditorReturn(Path.Combine(root,"editor-normal"),false);
                await Phase1Checks.EditorReturn(Path.Combine(root,"editor-ironman"),true);
                await Phase1Checks.BossFlow(Path.Combine(root,"boss-flow"));
                await Phase1Checks.TestPool(Path.Combine(root,"test-pool"));
                await Phase1Checks.TestPoolEditorReturn(Path.Combine(root,"test-pool-editor"));
                await Phase1Checks.TestPoolIronmanAndNormalLoss(Path.Combine(root,"test-pool-ironman"));
                await SaveFixtureChecks.Run(Path.Combine(root,"save-fixtures"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }

            Console.WriteLine("PASS controller new run, resume, victory, loss and technical retry");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + ex);
            return 1;
        }
    }

    private static async Task Run(CatalogueDefinition definition, string root)
    {
        var eras = new[]
        {
            definition.Eras[0].Era,
            new EraReference("fixture.era.test.2"),
            new EraReference("fixture.era.test.3"),
            new EraReference("fixture.era.test.4"),
            new EraReference("fixture.era.test.5"),
            new EraReference("fixture.era.test.6")
        };
        var unit = definition.PriceUnitId;
        var setup = new NewRunSetup(
            new ScenarioValue(200m, unit),
            eras,
            definition.Version);
        var mods = new ModAvailability(new[]
        {
            new ModRequirement("fixture.mod.core", "1.0"),
            new ModRequirement("fixture.mod.maps", "1.0"),
            new ModRequirement("fixture.mod.wwi", "2.1"),
            new ModRequirement("fixture.mod.interwar", "1.0"),
            new ModRequirement("fixture.mod.coldwar", "3.0")
        });
        var store = new RunStore(root);
        var adapter = new FakeAdapter(unit);

        var catalogue = new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture);
        var presenter = new RecordingPresenter();
        var controller = NewController(catalogue, store, adapter, presenter, mods);

        await controller.ShowStartAsync();
        var start = presenter.Last<StartView>();
        Assert.False(start.HasActiveRun, "fresh start has no active run");
        Assert.False(start.CanResume, "fresh start cannot resume");
        Assert.True(SprocketExpedition.Game.CampaignResumePreparation.CanReplaceWithNewRun(
            store.LoadActiveRun(mods), store.ListRuns()), "verified empty slot is available");

        await controller.StartNewRunAsync(setup);
        Assert.True(presenter.Has<BuildingView>(), "building screen shown; views=" + presenter.Describe());
        Assert.True(presenter.Has<OffersView>(), "offers shown; views=" + presenter.Describe());
        var offers = presenter.Last<OffersView>();
        Assert.Equal(3, offers.Offers.Count, "three offers");
        Assert.Equal(RunPhase.ChoosingEncounter, Snapshot(store, mods).Phase, "choosing phase");
        await CheckEditorReturn(definition, Path.Combine(root, "editor-return"), setup, mods);

        // A second new run cannot overwrite the active run.
        await controller.StartNewRunAsync(setup);
        Assert.True(presenter.Has<BlockedView>(), "active run blocks a new run");

        // Resume with a fresh catalogue instance to exercise persisted offers.
        var catalogue2 = new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture);
        var presenter2 = new RecordingPresenter();
        var controller2 = NewController(catalogue2, store, adapter, presenter2, mods);
        await controller2.ResumeAsync();
        var resumedOffers = presenter2.Last<OffersView>();
        Assert.Equal(3, resumedOffers.Offers.Count, "resumed offers");
        Assert.Equal(offers.Offers[0].OfferId, resumedOffers.Offers[0].OfferId, "exact resumed offer ids");

        var first = resumedOffers.Offers[0];
        await controller2.SelectOfferAsync(first.OfferId);
        Assert.Equal(first.OfferId, presenter2.Last<OffersView>().SelectedOfferId, "selection persisted");

        var prepared = SprocketExpedition.Game.CampaignResumePreparation.Read(store, mods);
        Assert.True(prepared.Error is null, "resume gate accepts verified saved tank");
        Assert.Equal(first.OfferId, prepared.Load.Snapshot!.SelectedOfferId, "resume retains selected challenge");
        Assert.Equal(Snapshot(store, mods).ConfirmedDesign!.Blueprint.SnapshotHash,
            Convert.ToHexString(SHA256.HashData(prepared.Blueprint!.Bytes)).ToLowerInvariant(),
            "editor restoration receives the actual verified saved tank bytes");
        Assert.False(SprocketExpedition.Game.CampaignResumePreparation.CanReplaceWithNewRun(
            prepared.Load, store.ListRuns()), "active slot cannot be reclaimed by New Game");
        var missingMods = SprocketExpedition.Game.CampaignResumePreparation.Read(store, ModAvailability.None);
        Assert.True(missingMods.Error is not null && missingMods.Blueprint is null,
            "missing mods block editor restore");
        var blockedPresenter = new RecordingPresenter();
        var blockedController = NewController(new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture),
            store, adapter, blockedPresenter, ModAvailability.None);
        await blockedController.ShowStartAsync();
        Assert.True(blockedPresenter.Has<BlockedView>(), "blocked start shows reason");
        Assert.False(blockedPresenter.Has<StartView>(), "start view cannot hide missing-mod block");
        var blueprintPath = Path.Combine(root, "runs", prepared.Load.Snapshot.RunId,
            "blueprints", prepared.Blueprint.SnapshotHash + ".bin");
        var originalBlueprint = File.ReadAllBytes(blueprintPath);
        try
        {
            File.WriteAllText(blueprintPath, "corrupt fixture bytes");
            var corrupted = SprocketExpedition.Game.CampaignResumePreparation.Read(store, mods);
            Assert.True(corrupted.Error is not null && corrupted.Blueprint is null,
                "hash-invalid blueprint never reaches native editor restoration");
        }
        finally { File.WriteAllBytes(blueprintPath, originalBlueprint); }

        adapter.Outcome = EncounterOutcome.Victory;
        await controller2.BeginEncounterAsync();
        var victory = presenter2.Last<ResultView>();
        Assert.Equal(EncounterOutcome.Victory, victory.Result.Outcome, "victory outcome");
        Assert.Equal(RunPhase.ChoosingEncounter, victory.NextPhase, "round advances to next choice");
        Assert.False(victory.IsTerminal, "victory is not terminal");
        Assert.True(adapter.CombatStarted, "adapter reported combat start");

        await controller2.ContinueAsync();
        var roundTwo = presenter2.Last<OffersView>();
        Assert.Equal(2, roundTwo.Round, "round two offers generated");
        Assert.Equal(3, roundTwo.Offers.Count, "three round-two offers");

        var second = roundTwo.Offers[1];
        await controller2.SelectOfferAsync(second.OfferId);
        adapter.DesignRevision = 2;
        await controller2.RefreshEncounterDesignAsync();
        var upgraded = Snapshot(store, mods);
        Assert.Equal(second.OfferId, upgraded.SelectedOfferId, "upgrade preserves selected challenge");
        Assert.Equal(second.PlanHash, upgraded.Offers[1].PlanHash, "upgrade preserves exact enemy plan");
        Assert.Equal(20m, upgraded.ConfirmedDesign!.Cost.Value, "new quote persisted");
        var upgradedHash = upgraded.ConfirmedDesign.Blueprint.SnapshotHash;
        var storedBlueprint = store.ReadBlueprint(upgraded.RunId, upgradedHash);
        Assert.True(storedBlueprint.Bytes is { Length: > 0 }, "upgraded tank snapshot persisted");
        adapter.Outcome = EncounterOutcome.FriendlyForceDestroyed;
        await controller2.BeginEncounterAsync();
        Assert.Equal(upgradedHash, adapter.LastPlayedBlueprintHash, "next battle uses upgraded tank");
        var loss = presenter2.Last<ResultView>();
        Assert.Equal(EncounterOutcome.FriendlyForceDestroyed, loss.Result.Outcome, "loss outcome");
        Assert.True(loss.IsLoss, "loss flagged");
        Assert.True(loss.IsTerminal, "loss is terminal");
        Assert.Equal(RunPhase.Lost, loss.NextPhase, "lost phase");

        var noActive = store.LoadActiveRun(mods);
        Assert.Equal(RunStoreLoadStatus.NoActiveRun, noActive.Status, "loss clears active index");
        var terminal = store.LoadRun(loss.Result.Attempt.RunId, mods);
        Assert.Equal(RunStoreLoadStatus.CannotResumeTerminal, terminal.Status, "terminal run cannot resume");
        Assert.False(terminal.CanResume, "terminal run not resumable");
        var terminalPreparation = SprocketExpedition.Game.CampaignResumePreparation.Read(store, mods);
        Assert.True(terminalPreparation.Blueprint is null && terminalPreparation.Error is not null,
            "terminal slot cannot restore into a playable session");
        Assert.True(SprocketExpedition.Game.CampaignResumePreparation.CanReplaceWithNewRun(
            noActive, store.ListRuns()), "verified terminal slot may be archived for new run");
        Assert.False(SprocketExpedition.Game.CampaignResumePreparation.CanReplaceWithNewRun(
            noActive, new RunListResult(store.ListRuns().Runs,
                new[] { new RunStoreIssue("test-corruption", "unverified records") })),
            "unverified slot records cannot be reclaimed");

        // A new run is allowed after a terminal loss.
        await controller2.StartNewRunAsync(setup);
        Assert.Equal(3, presenter2.Last<OffersView>().Offers.Count, "new run after loss has offers");

        // Technical failure after combat start suspends; resume retries the exact plan.
        var technicalOffer = presenter2.Last<OffersView>().Offers[2];
        await controller2.SelectOfferAsync(technicalOffer.OfferId);
        adapter.Outcome = EncounterOutcome.TechnicalFailure;
        await controller2.BeginEncounterAsync();
        var technical = presenter2.Last<ResultView>();
        Assert.Equal(EncounterOutcome.TechnicalFailure, technical.Result.Outcome, "technical outcome");
        Assert.Equal(RunPhase.Suspended, technical.NextPhase, "technical failure suspends");
        Assert.False(SprocketExpedition.Game.CampaignResumePreparation.CanReplaceWithNewRun(
            store.LoadActiveRun(mods), store.ListRuns()), "suspended run cannot be reclaimed");

        var catalogue3 = new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture);
        var presenter3 = new RecordingPresenter();
        var controller3 = NewController(catalogue3, store, adapter, presenter3, mods);
        var callsBeforeResume = adapter.EncounterCalls;
        await controller3.ResumeAsync();
        Assert.Equal(callsBeforeResume, adapter.EncounterCalls, "resume never starts combat automatically");
        var retryView = presenter3.Last<EncounterView>();
        Assert.False(retryView.CombatStarted, "resumed retry is not yet in combat");
        Assert.Equal(technical.Result.Attempt.PlanHash, retryView.Attempt.PlanHash, "retry keeps exact plan");

        adapter.Outcome = EncounterOutcome.Victory;
        await controller3.RetryEncounterAsync();
        var retried = presenter3.Last<ResultView>();
        Assert.Equal(EncounterOutcome.Victory, retried.Result.Outcome, "retry victory");
        Assert.Equal(technical.Result.Attempt.PlanHash, retried.Result.Attempt.PlanHash, "retry plan hash");
        Assert.Equal(0, Snapshot(store, mods).Offers.Count,
            "result checkpoint may precede generation of next offers");
        var boundaryPresenter = new RecordingPresenter();
        var boundaryResume = NewController(new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture),
            store, adapter, boundaryPresenter, mods);
        var callsBeforeBoundary = adapter.EncounterCalls;
        await boundaryResume.ResumeAsync();
        Assert.Equal(3, boundaryPresenter.Last<OffersView>().Offers.Count,
            "resume immediately after victory generates and saves next offers");
        Assert.Equal(3, Snapshot(store, mods).Offers.Count, "resumed boundary offers committed");
        Assert.Equal(callsBeforeBoundary, adapter.EncounterCalls, "boundary resume does not launch a battle");
    }

    private static async Task CheckEditorReturn(CatalogueDefinition definition, string root, NewRunSetup setup, ModAvailability mods)
    {
        var store = new RunStore(root);
        var adapter = new FakeAdapter(definition.PriceUnitId) { HeldResult = new TaskCompletionSource<bool>() };
        var presenter = new RecordingPresenter();
        var controller = NewController(new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture), store, adapter, presenter, mods);
        await controller.StartNewRunAsync(setup);
        var chosen = presenter.Last<OffersView>().Offers[1];
        await controller.SelectOfferAsync(chosen.OfferId);
        using var cancellation = new CancellationTokenSource();
        var fight = controller.BeginEncounterAsync(cancellation.Token);
        await adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = Snapshot(store, mods);
        Assert.True(controller.CanReturnToEditor(before.CurrentAttempt!), "live attempt can request refit");
        cancellation.Cancel();
        // Deliberately let an adapter return a late victory despite cancellation.
        adapter.HeldResult.SetResult(true);
        await fight;
        Assert.Equal(0, Snapshot(store, mods).Victories, "cancelled adapter result grants no victory");
        await controller.ReturnToEditorAsync(before.CurrentAttempt!);
        var saved = Snapshot(store, mods);
        Assert.Equal(RunPhase.ChoosingEncounter, saved.Phase, "refit checkpoint persisted");
        Assert.Equal(before.SelectedOfferId, saved.SelectedOfferId, "exact selection retained");
        Assert.Equal(before.AvailableBudget, saved.AvailableBudget, "refit budget retained");
        Assert.Equal(before.Round, saved.Round, "refit round retained");
        Assert.Equal(0, saved.Defeats, "no defeat for refit");
        Assert.Equal(before.PendingSupplyPoints, saved.PendingSupplyPoints, "no refit reward");
        Assert.False(controller.CanReturnToEditor(before.CurrentAttempt!), "duplicate request is no longer available");
        // Fresh controller proves restart after returning persists the selected challenge.
        var resumedPresenter = new RecordingPresenter();
        controller = NewController(new SeededEncounterCatalogue(definition, CatalogueOptions.Fixture), store, adapter, resumedPresenter, mods);
        await controller.ResumeAsync();
        Assert.Equal(chosen.OfferId, resumedPresenter.Last<OffersView>().SelectedOfferId, "refit survives reload");
        adapter.HeldResult = null;
        adapter.DesignRevision = 2;
        await controller.RefreshEncounterDesignAsync();
        Assert.Equal(chosen.PlanHash, resumedPresenter.Last<OffersView>().Offers[1].PlanHash, "edited tank keeps frozen enemy plan");
        await controller.BeginEncounterAsync();
        var result = resumedPresenter.Last<ResultView>();
        Assert.True(result.Result.Attempt.Generation > before.CurrentAttempt!.Generation, "replayed fight has a fresh generation");
        Assert.Equal(1, Snapshot(store, mods).Victories, "only replayed victory grants a result");
        Console.WriteLine("PASS cancelled combat -> persisted refit -> reload -> edited tank -> same encounter, with late result suppressed");
    }

    private static ExpeditionController NewController(
        SeededEncounterCatalogue catalogue,
        RunStore store,
        IGameAdapter adapter,
        IExpeditionPresenter presenter,
        ModAvailability mods)
        => new(
            catalogue,
            store,
            adapter,
            presenter,
            () => mods,
            () => Guid.NewGuid().ToString("D"),
            () => 4242424242L);

    private static RunSnapshot Snapshot(RunStore store, ModAvailability mods)
    {
        var load = store.LoadActiveRun(mods);
        Assert.True(load.CanResume, "active run is resumable");
        return load.Snapshot!;
    }
}

internal sealed class FakeAdapter : IGameAdapter
{
    internal TaskCompletionSource<bool>? HeldResult { get; set; }
    internal TaskCompletionSource<bool> Started { get; } = new();
    internal int EncounterCalls { get; private set; }
    internal EncounterRequest? LastRequest { get; private set; }
    private readonly string _unit;
    internal EncounterOutcome Outcome { get; set; } = EncounterOutcome.Victory;
    internal bool CombatStarted { get; private set; }
    internal int DesignRevision { get; set; } = 1;
    internal string? LastPlayedBlueprintHash { get; private set; }

    internal FakeAdapter(string unit)
    {
        _unit = unit;
    }

    public AdapterReadiness CheckReadiness() => new(true, "fixture adapter ready");

    public Task<DesignCapture> CaptureDesignAsync(
        EraReference era,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-native-blueprint-" + DesignRevision);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var quote = new DesignQuote(
            new BlueprintReference("fixture.player", hash, era, Array.Empty<ModRequirement>()),
            new ScenarioValue(10m * DesignRevision, _unit),
            true,
            true,
            null);
        return Task.FromResult(new DesignCapture(quote, bytes));
    }

    public Task OpenEditorAsync(
        BlueprintReference blueprint,
        EraReference era,
        ScenarioValue budget,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<EncounterResult> RunEncounterAsync(
        EncounterRequest request,
        Func<Task> combatStarted,
        CancellationToken cancellationToken)
    {
        EncounterCalls++;
        LastRequest = request;
        LastPlayedBlueprintHash = request.PlayerBlueprint.SnapshotHash;
        CombatStarted = Outcome != EncounterOutcome.PrecombatAborted;
        if (CombatStarted) await combatStarted();
        Started.TrySetResult(true);
        if (HeldResult != null) await HeldResult.Task;
        return new EncounterResult(request.Attempt, request.Plan, Outcome, "fixture result");
    }

    public Task CleanupAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class RecordingPresenter : IExpeditionPresenter
{
    private readonly List<object> _views = new();

    internal T Last<T>() => _views.OfType<T>().Last();
    internal bool Has<T>() => _views.OfType<T>().Any();
    internal string Describe() => string.Join(" | ", _views.Select(v => v switch
    {
        ErrorView e => "Error(" + e.Message + ")",
        BlockedView b => "Blocked(" + b.Reason + ")",
        _ => v.GetType().Name
    }));

    public void ShowStart(StartView view) => _views.Add(view);
    public void ShowBuilding(BuildingView view) => _views.Add(view);
    public void ShowOffers(OffersView view) => _views.Add(view);
    public void ShowEncounter(EncounterView view) => _views.Add(view);
    public void ShowResult(ResultView view) => _views.Add(view);
    public void ShowRunFinished(RunFinishedView view) => _views.Add(view);
    public void ShowEndlessChoice(EndlessChoiceView view) => _views.Add(view);
    public void ShowResumeBlocked(BlockedView view) => _views.Add(view);
    public void ShowError(ErrorView view) => _views.Add(view);
    public void ShowArmory(ArmoryView view) => _views.Add(view);
}

internal static class Assert
{
    internal static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void False(bool condition, string message) => True(!condition, message);

    internal static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}
