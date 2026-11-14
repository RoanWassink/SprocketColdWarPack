#if CAMPAIGN
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.GameControl;
using Sprocket.SceneManagement;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles.Serialization;
using SprocketExpedition.Core.Presentation;
using UnityEngine;
using NativeTask = Il2CppSystem.Threading.Tasks.Task;
using UObject = UnityEngine.Object;

namespace SprocketExpedition.Game;

// Session ownership survives scenes. Battles spawn fresh copies; the editable
// tank travels as opaque blueprint bytes. Native scene tasks own all cleanup.
internal static class CampaignNativeFlow
{
    private enum Transfer { None, Preparing, MainMenu, Loading, Design }
    private static Transfer transfer;
    private static NativeTask? loading;
    private static SupportedGameModes nextMode;
    private static BattleConfig? battle;
    private static TaskCompletionSource<bool>? battleReady;
    private static TaskCompletionSource<bool>? editorReady;
    private static byte[]? editorBytes;
    private static object? lastView;
    private static float since;
    private static bool editorSeen;
    private static bool handoffFailed;
    private static long sessionGeneration;
    private static string? designerEra;
    private static CampaignFrameWork? preparation;
    private static float nextPreparationNotice;
    internal static bool NewGameSession { get; private set; }
    internal static int NewGameStartingEraIndex { get; private set; }
    internal static bool NewGameIronman { get; private set; }
    internal static int NewGameSlot { get; private set; }
    internal static void CommitNewGame() => NewGameSession = false;
    internal static bool OwnsSession { get; private set; }
    internal static long SessionGeneration => sessionGeneration;
    internal static bool IsTransferring => transfer != Transfer.None;
    internal static bool Editing => OwnsSession && !IsTransferring && DesignerReady();
    // First bridge uses the map on which the Custom Battle spike was tested.
    // Synthetic catalogue map IDs are not real map selections.
    internal const string BattleMap = "Defence";

    internal static void BeginNewGame(int startingEraIndex = 0, bool ironman = false, int slot = 0)
    {
        if (transfer == Transfer.Preparing) return;
        if (!CampaignEraAvailability.Ready)
            throw new InvalidOperationException("Wait for the available-tank check to finish.");
        if (CampaignEraAvailability.ScheduleEraNames.Count == 0)
            throw new InvalidOperationException(CampaignEraAvailability.Status);
        if (startingEraIndex < 0
            || startingEraIndex >= CampaignEraAvailability.ScheduleEraNames.Count)
            throw new ArgumentOutOfRangeException(nameof(startingEraIndex));
        EndSession();
        NewGameSlot = slot;
        NewGameStartingEraIndex = startingEraIndex;
        NewGameIronman = ironman;
        OwnsSession = true;
        NewGameSession = true;
        CampaignControllerHost.BeginSession();
        CampaignMusic.BeginPhase(CampaignMusicCue.Build);
        try
        {
            transfer = Transfer.Preparing;
            since = Time.realtimeSinceStartup;
            nextPreparationNotice = 0;
            preparation = new CampaignFrameWork(CampaignNativeCatalogue.PrepareInFrames());
            CampaignNativeMenu.ShowPreparation("Reading blueprint cards...");
        }
        catch (Exception ex)
        {
            preparation?.Dispose(); preparation = null;
            transfer = Transfer.None;
            CampaignNativeMenu.ClearPreparation();
            CampaignControllerHost.ShowError("Cannot start the native-era campaign: " + ex.Message);
            CampaignNativeMenu.Open();
        }
        Plugin.Logger.LogInfo("Expedition: New Game -> native tank editor; Custom Battle is the encounter host.");
    }

    internal static void RememberDesign(byte[] bytes) => editorBytes = bytes.ToArray();

    internal static void BeginLoadGame()
    {
        EndSession();
        OwnsSession = true;
        CampaignControllerHost.BeginSession();
        CampaignMusic.BeginPhase(CampaignMusicCue.Background);
        CampaignNativeMenu.Open();
    }

    internal static Task OpenSavedDesignerAsync(byte[] bytes, CancellationToken cancellationToken, string? era = null)
    {
        if (!OwnsSession || IsTransferring)
            throw new InvalidOperationException("The Expedition editor handoff is not ready.");
        RememberDesign(bytes);
        designerEra = era;
        CampaignMusic.BeginPhase(CampaignMusicCue.Build);
        var completion = new TaskCompletionSource<bool>();
        editorReady = completion;
        QueueScene(SupportedGameModes.Sandbox);
        return completion.Task.WaitAsync(cancellationToken);
    }

    internal static Task OpenBattleAsync(BattleConfig config, CancellationToken cancellationToken)
    {
        if (!OwnsSession || IsTransferring)
            throw new InvalidOperationException("The Expedition scene handoff is not ready.");
        CampaignMusic.BeginPhase(CampaignMusicCue.Background);
        battle = config;
        var completion = new TaskCompletionSource<bool>();
        battleReady = completion;
        QueueScene(SupportedGameModes.Deathmatch);
        return completion.Task.WaitAsync(cancellationToken);
    }

    internal static void ReturnToDesigner()
    {
        if (!OwnsSession || IsTransferring) return;
        CampaignMusic.BeginPhase(CampaignMusicCue.Build);
        designerEra = CampaignControllerHost.DesignerEra;
        CampaignNativeMenu.Close();
        if (DesignerReady()) return;
        QueueScene(SupportedGameModes.Sandbox);
    }

    internal static void ReturnToMainMenu()
    {
        var game = UObject.FindObjectOfType<GameController>();
        var scenes = ISceneManager.Instance;
        if (game == null || scenes == null || !scenes.TryGetFirstScene(SceneFlags.MainMenu, out var menu))
        {
            CampaignControllerHost.ShowError("The native main menu is unavailable. Your checkpoint is preserved.");
            return;
        }
        CampaignEditorBudget.Release();
        game.RequestSceneChange(menu);
        EndSession();
        CampaignControllerHost.BeginSession();
    }

    private static bool DesignerReady()
    {
        var core = UObject.FindObjectOfType<VehicleDesignerCore>();
        return core != null && core.HasEditor && core.Target != null
            && core.editorState == VehicleDesignerCore.EditorState.Running;
    }

    private static void QueueScene(SupportedGameModes mode)
    {
        CampaignEditorBudget.Release();
        CampaignDesignerPlayHook.Release();
        CampaignNativeMenu.Close();
        nextMode = mode;
        transfer = Transfer.MainMenu;
        loading = null;
        since = Time.realtimeSinceStartup;
        handoffFailed = false;
        var game = UObject.FindObjectOfType<GameController>();
        if (game == null) return;
        if (UObject.FindObjectOfType<DeathmatchGameMode>() != null)
        {
            CustomBattleHostSpike.RequestCampaignReturn();
            return;
        }
        var scenes = ISceneManager.Instance
            ?? throw new InvalidOperationException("The native scene manager is unavailable.");
        if (!scenes.TryGetFirstScene(SceneFlags.MainMenu, out var menu))
            throw new InvalidOperationException("The native main menu scene is unavailable.");
        game.RequestSceneChange(menu);
    }

    internal static void Tick()
    {
        if (!OwnsSession) return;
        try
        {
            if (IsTransferring)
            {
                CampaignDesignerPlayHook.Release();
                if (Time.realtimeSinceStartup - since > 120f)
                    throw new TimeoutException("The native editor/battle transition did not finish.");
                if (transfer == Transfer.Preparing)
                {
                    var pending = preparation ?? throw new InvalidOperationException("Catalogue preparation is unavailable.");
                    if (!pending.Advance(maximumSteps: 32))
                    {
                        if (Time.unscaledTime >= nextPreparationNotice)
                        {
                            nextPreparationNotice = Time.unscaledTime + .15f;
                            CampaignNativeMenu.ShowPreparation($"Reading blueprint cards... {Math.Max(0, pending.Steps - 1)} checked");
                        }
                        return;
                    }
                    Plugin.Logger.LogInfo($"Expedition New Game preparation: {pending.Steps} file boundaries across {pending.Frames} frames; "
                        + $"elapsed={(Time.realtimeSinceStartup - since) * 1000f:N0} ms; peak native step={pending.PeakStepMilliseconds:N1} ms.");
                    preparation = null;
                    var content = CampaignNativeCatalogue.Get(); // Completed cache only; no second scan.
                    if (NewGameStartingEraIndex < 0
                        || NewGameStartingEraIndex >= content.Eras.Count)
                    {
                        transfer = Transfer.None;
                        CampaignNativeMenu.ClearPreparation();
                        CampaignControllerHost.ShowError(
                            "The selected starting era is not available in the prepared content. Choose another era.");
                        CampaignNativeMenu.Open();
                        return;
                    }
                    content.ConfigureStartingEra(NewGameStartingEraIndex);
                    RememberDesign(content.Starter);
                    designerEra = content.Eras[NewGameStartingEraIndex].NativeId;
                    QueueScene(SupportedGameModes.Sandbox);
                    return;
                }
                if (transfer == Transfer.MainMenu)
                {
                    // The host retains suppression until the old native game
                    // controller has gone. Do not create the next one first.
                    if (CustomBattleHostSpike.MenuReturnPending) return;
                    var menu = UObject.FindObjectOfType<Sprocket.MainMenu>();
                    if (menu == null || !menu.sceneStarted
                        || UObject.FindObjectOfType<GameController>() != null) return;
                    var scenes = ISceneManager.Instance ?? menu.context?.SceneManager
                        ?? throw new InvalidOperationException("The native scene manager is unavailable.");
                    var setup = new GameSetupContext { Mode = nextMode };
                    if (nextMode == SupportedGameModes.Deathmatch)
                        setup.SetupConfig = battle!.Cast<IBattleConfig>();
                    var map = nextMode == SupportedGameModes.Deathmatch ? battle!.MapName : BattleMap;
                    loading = scenes.Load(map,
                        new Il2CppReferenceArray<Il2CppSystem.Object>(new Il2CppSystem.Object[] { setup }),
                        SceneLoadOptions.None, Il2CppSystem.Threading.CancellationToken.None);
                    if (loading == null) throw new InvalidOperationException("Scene loading returned no native task.");
                    transfer = Transfer.Loading;
                    return;
                }
                if (loading == null || !loading.IsCompleted) return;
                if (loading.IsFaulted || loading.IsCanceled)
                {
                    var stage = transfer == Transfer.Design ? "saved-design" : "scene";
                    var detail = loading.IsCanceled
                        ? "The native task was cancelled."
                        : DescribeFault(loading.Exception);
                    Plugin.Logger.LogError($"Expedition native {stage} load faulted: {detail}");
                    throw new InvalidOperationException(
                        $"The native {stage} load failed or was cancelled. {FirstLine(detail)}");
                }
                if (nextMode == SupportedGameModes.Deathmatch)
                {
                    var mode = UObject.FindObjectOfType<DeathmatchGameMode>();
                    if (mode == null || mode.gameState != DeathmatchGameMode.ModeState.Running) return;
                    transfer = Transfer.None;
                    loading = null;
                    var completion = battleReady;
                    battleReady = null;
                    completion?.TrySetResult(true);
                    return;
                }
                if (!DesignerReady()) return;
                if (transfer == Transfer.Loading && editorBytes is { Length: > 0 })
                {
                    var serializer = IVehicleBlueprintSerializer.Serializer
                        ?? throw new InvalidOperationException("The native blueprint serializer is unavailable.");
                    var blueprint = serializer.DeserializeJSON(Encoding.UTF8.GetString(editorBytes));
                    if (blueprint == null) throw new InvalidOperationException("The captured tank could not be read.");
                    var core = UObject.FindObjectOfType<VehicleDesignerCore>();
                    if (designerEra != null)
                    {
                        var date = CampaignNativeCatalogue.EndDate(designerEra);
                        core.TechnologyLimit = date;
                        core.TechnologyDate = date;
                        // Only the player's run-owned copy advances at a build
                        // boundary. Enemy bytes and named personal designs stay intact.
                        var savedDate = blueprint.Header.CreationDate;
                        var originalEra = CampaignNativeCatalogue.EraOf(savedDate);
                        var originalIndex = originalEra == null ? -1 : Sprocket.Vehicles.VehicleClassifications.GetEraIndexByName(originalEra);
                        var desiredIndex = Sprocket.Vehicles.VehicleClassifications.GetEraIndexByName(designerEra);
                        var action = CampaignEraDatePolicy.Decide(originalIndex, desiredIndex,
                            (originalIndex < 0 || originalIndex > desiredIndex)
                            && CampaignNativeCatalogue.IsExpeditionDate(savedDate));
                        if (action == RunCopyDateAction.Normalise)
                        {
                            // The run copy carries the open-ended date (TechDate.MaxValue)
                            // or an end date Expedition wrote while this era was the last
                            // one. A newer native era now lies after it, so the game files
                            // that date in a later era. Move the run-owned copy back to the
                            // end of its current era. Personal designs are never touched.
                            Plugin.Logger.LogInfo($"Expedition normalised the run copy's date {savedDate} "
                                + $"(classified {originalEra ?? "nowhere"}) to the end of {designerEra} ({date}).");
                            blueprint.Header.CreationDate = date;
                        }
                        else if (action == RunCopyDateAction.Reject)
                            throw new InvalidOperationException(
                                $"The saved tank (date {savedDate}, era {originalEra ?? "unknown"}) belongs to a later era than the current build era {designerEra}.");
                        else if (action == RunCopyDateAction.Advance) blueprint.Header.CreationDate = date;
                    }
                    loading = core.Load(
                        blueprint, Il2CppSystem.Threading.CancellationToken.None);
                    if (loading == null) throw new InvalidOperationException("Design loading returned no native task.");
                    Plugin.Logger.LogInfo(
                        $"Expedition saved-design load started: bytes={editorBytes.Length}; "
                        + $"era={designerEra ?? "unspecified"}; creation={blueprint.Header.CreationDate}; "
                        + $"technology={core.TechnologyLimit}; task={loading.Status}.");
                    transfer = Transfer.Design;
                    return;
                }
                transfer = Transfer.None;
                loading = null;
                if (NewGameSession)
                {
                    var target = UObject.FindObjectOfType<VehicleDesignerCore>().Target
                        .TryCast<Sprocket.Vehicles.IVehicleGateway>();
                    if (target == null || !float.IsFinite(target.Cost) || target.Cost <= 0f)
                        throw new InvalidOperationException("The native starter price is unavailable.");
                    CampaignNativeCatalogue.Get().MeasureStarterPrice((decimal)target.Cost);
                }
                editorSeen = true;
                Plugin.Logger.LogInfo("Expedition: editor ready; personal blueprint files were not changed.");
                var editorCompletion = editorReady;
                editorReady = null;
                editorCompletion?.TrySetResult(true);
            }

            var editing = DesignerReady();
            CampaignDesignerPlayHook.Tick(editing);
            if (editorSeen && !handoffFailed && !editing && UObject.FindObjectOfType<DeathmatchGameMode>() == null
                && UObject.FindObjectOfType<Sprocket.MainMenu>() != null)
            {
                EndSession();
                CampaignControllerHost.BeginSession();
                return;
            }
            var view = CampaignControllerHost.View;
            var show = view is ResultView or RunFinishedView or ErrorView or BlockedView or EndlessChoiceView or ArmoryView
                || view is OffersView offers && (!editing || offers.SelectedOfferId == null);
            if (show && !ReferenceEquals(view, lastView) && !CampaignNativeMenu.IsOpen)
                CampaignNativeMenu.Open();
            lastView = view;
            CampaignEnemyHunt.Tick();
        }
        catch (Exception ex)
        {
            preparation?.Dispose(); preparation = null;
            CampaignNativeMenu.ClearPreparation();
            handoffFailed = true;
            var completion = battleReady;
            battleReady = null;
            transfer = Transfer.None;
            loading = null;
            CampaignDesignerPlayHook.Release();
            completion?.TrySetException(ex);
            var editorCompletion = editorReady;
            editorReady = null;
            editorCompletion?.TrySetException(ex);
            CampaignControllerHost.ShowError("Expedition scene handoff failed: " + ex.Message);
            CampaignNativeMenu.Open();
            lastView = CampaignControllerHost.View;
            Plugin.Logger.LogError("Expedition scene handoff failed: " + ex);
        }
    }

    private static string DescribeFault(Il2CppSystem.AggregateException? exception)
    {
        if (exception == null) return "No native exception was attached to the faulted task.";
        try
        {
            var text = exception.ToString();
            var inner = exception.InnerExceptions;
            if (inner != null)
            {
                for (var i = 0; i < inner.Count; i++)
                {
                    var item = inner[i];
                    if (item != null) text += "\n--- inner " + (i + 1) + " ---\n" + item;
                }
            }
            return text;
        }
        catch
        {
            return exception.Message ?? "The native faulted task did not expose a message.";
        }
    }

    private static string FirstLine(string text)
    {
        var first = text.Split('\n')[0].Trim();
        return first.Length <= 300 ? first : first[..300];
    }

    internal static void EndSession()
    {
        preparation?.Dispose(); preparation = null;
        CampaignNativeMenu.ClearPreparation();
        CampaignPauseMenu.Release();
        CampaignMusic.Release();
        CampaignSpawnTrial.Release();
        CampaignEnemyHunt.Release();
        CampaignBattleHud.Release();
        CampaignEditorBudget.Release();
        sessionGeneration++;
        OwnsSession = false;
        NewGameSession = false;
        NewGameSlot = 0;
        NewGameStartingEraIndex = 0;
        NewGameIronman = false;
        designerEra = null;
        CampaignNativeCatalogue.Reset();
        transfer = Transfer.None;
        loading = null;
        battle = null;
        editorBytes = null;
        editorSeen = false;
        handoffFailed = false;
        lastView = null;
        var completion = battleReady;
        battleReady = null;
        completion?.TrySetCanceled();
        var editorCompletion = editorReady;
        editorReady = null;
        editorCompletion?.TrySetCanceled();
        CampaignDesignerPlayHook.Release();
        CampaignNativeMenu.Close();
    }
}
#endif
