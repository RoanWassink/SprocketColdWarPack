using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.GameControl;
using Sprocket.MissionFramework;
using Sprocket.SceneManagement;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.CrewSystems;
using SprocketExpedition.Core;
using UnityEngine;
using NativeTask = Il2CppSystem.Threading.Tasks.Task;

namespace SprocketExpedition.Game;

/// <summary>
/// Native Custom Battle campaign host, roster observer and outcome/return owner.
/// </summary>
internal sealed class CustomBattleHostSpike : MonoBehaviour
{
    private static CustomBattleHostSpike? active;
    private static byte[]? lockedBlueprintBytes;

    // Campaign encounter bridge. The callback is
    // invoked on the Unity main thread when a native result is accepted.
    internal static Action<bool, string>? CampaignOutcome;
    internal static Action? CampaignCombatStarted;
    internal static Action<string>? CampaignStartFailed;
    internal static string CampaignPlayerDesignName = string.Empty;
    internal static int CampaignFriendlyCount = 2;
    internal static int CampaignEnemyCount = 1;
    internal static IReadOnlyList<(UnitDefinition Unit, int Count)>? CampaignEnemyLineup;
    internal static string CampaignEnemyDesignSource = string.Empty;
    internal static bool CampaignEncounterRunning => active is { observing: true };
    internal static bool HasLockedBlueprint => lockedBlueprintBytes is { Length: > 0 };
    internal static decimal? CombatDurationSeconds => active is { combatStartedAt: { } since } current
        && current.encounterSessionGeneration == CampaignNativeFlow.SessionGeneration
        ? (decimal)Math.Max(0f, Time.time - since) : null;
    internal static bool ContactMade
    {
        get
        {
            var current = active;
            if (current == null || !current.observing || current.encounterSessionGeneration != CampaignNativeFlow.SessionGeneration)
                return false;
            var known = current.rosterSeen && current.lastUnknown == 0
                && current.lastAllies >= 0 && current.lastEnemies >= 0;
            current.contactMade |= SprocketExpedition.Core.Run.ContactRules.HasContact(
                (double)(CombatDurationSeconds ?? 0m),
                known ? Math.Max(0, CampaignEnemyCount - current.lastEnemies) : null,
                known ? Math.Max(0, CampaignFriendlyCount - current.lastAllies) : null);
            return current.contactMade;
        }
    }
    internal sealed record RosterStatus(int Friendlies, int Enemies, int Unknown, int Spawned, int Expected,
        int ExpectedFriendlies, int ExpectedEnemies, float? InitialNearestEnemy)
    { internal int SpawnedEnemies { get; init; } }
    internal static RosterStatus? CurrentRoster => active is { observing: true } current
        ? new(current.lastAllies, current.lastEnemies, current.lastUnknown, Math.Max(0, current.lastSpawned),
            current.mode?.vehicleSpawns?.Count ?? 0, CampaignFriendlyCount, CampaignEnemyCount, current.initialNearestEnemy)
            { SpawnedEnemies = CampaignControllerHost.RunSnapshot?.TestPool == true ? current.CountSpawnedEnemies() : 0 }
        : null;
    internal static int LivingAiAllyCount
    {
        get
        {
            var current = active;
            try
            {
                if (!CampaignNativeFlow.OwnsSession || current == null
                    || current.encounterSessionGeneration != CampaignNativeFlow.SessionGeneration
                    || current.mode == null || !current.mode.gameObject.scene.isLoaded || current.editorReturn)
                    return 0;
                if (current.resolvedVictory && current.victoryAiAllyCount is { } frozen) return frozen;
                return current.observing ? current.CountLivingAiAllies() : 0;
            }
            catch { return 0; }
        }
    }

    // Battle-log metric: whether the hull the player last controlled still has
    // living crew. Null when the roster cannot identify that hull reliably.
    internal static bool? PlayerTankSurvived
    {
        get
        {
            var current = active;
            if (current is null || !current.observing || current.lastControlledEntityKey is not { } key)
                return null;
            try
            {
                var spawns = current.mode?.vehicleSpawns;
                if (spawns == null) return null;
                for (var i = 0; i < spawns.Count; i++)
                {
                    var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
                    var tank = gateway?.Behaviour?.TryCast<VehicleBehaviour>()
                        ?? gateway?.transform?.GetComponentInChildren<VehicleBehaviour>();
                    if (tank == null || tank.WasCollected) continue;
                    if ((int)tank.ID != key) continue;
                    var alive = AliveCrewCount(tank, out var readable);
                    return readable ? alive > 0 : null;
                }
                return null;
            }
            catch { return null; }
        }
    }

    private DeathmatchGameMode? mode;
    private TeamID playerTeamID;
    private Il2CppReferenceArray<TeamDefinition>? teams;
    private Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo>?[]? previous;
    private bool observing;
    private bool entered;
    private bool rosterSeen;
    private bool controlGuardDone;
    private bool combatStartedNotified;
    private float? combatStartedAt;
    private bool contactMade;
    private NativeTask? controlTask;
    private string expectedPlayerDesignName = string.Empty;
    private long encounterSessionGeneration;
    private int? lastControlledEntityKey;
    private int? victoryAiAllyCount;
    private bool resultLogged;
    private float nextCheck;
    private int lastAllies = -1;
    private int lastEnemies = -1;
    private int lastUnknown = -1;
    private int lastSpawned = -1;
    private readonly HashSet<int> observedEnemySpawns = new();
    private int CountSpawnedEnemies()
    {
        var spawns = mode?.vehicleSpawns;
        if (spawns != null)
            for (var i = 0; i < spawns.Count; i++)
            {
                try
                {
                    if (spawns[i]?.Spawned != true) continue;
                    var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
                    var tank = gateway?.Behaviour?.TryCast<VehicleBehaviour>()
                        ?? gateway?.transform?.GetComponentInChildren<VehicleBehaviour>();
                    if (tank != null && !tank.ID.TeamID.Equals(playerTeamID)) observedEnemySpawns.Add(i);
                }
                catch (Exception) { /* Optional telemetry cannot change battle outcomes. */ }
            }
        return observedEnemySpawns.Count;
    }
    private float? initialNearestEnemy;
    private int outcomeStage;
    private bool editorReturn;
    private Mission? lossMission;
    private Mission? resolvedMission;
    private bool resolvedVictory;
    private bool returnInProgress;
    private readonly CampaignReturnGate returnGate = new();
    private NativeTask? returnTask;
    private NativeTask? lossTask;
    private bool lossOverlay;
    private bool returnRequested;
    private bool retryRequested;
    private bool restoreCursorLock;
    private readonly HashSet<int> confirmedDeadSpawns = new();
    private readonly Dictionary<int, string> tankNames = new();

    private void Start() => active = this;

    internal static bool MenuReturnPending => active?.returnGate.Pending == true;

    internal static bool StartCampaignEncounter(bool restart = true)
    {
        var spike = active;
        if (spike == null)
        {
            spike = UnityEngine.Object.FindObjectOfType<CustomBattleHostSpike>();
            active = spike;
        }
        if (spike == null)
        {
            Plugin.Logger.LogError(
                "Expedition campaign loop: no host spike component exists; cannot start the encounter.");
            return false;
        }

        if (spike.observing)
        {
            if (!spike.resultLogged)
            {
                Plugin.Logger.LogInfo(
                    "Expedition campaign loop: the host spike is already observing an encounter.");
                return false;
            }

            // The previous result was already delivered to the controller. Its
            // observer is paused here so the next encounter can restart the host.
            spike.observing = false;
        }

        spike.StartSpike(restart);
        return spike.observing;
    }

    internal static void EndCampaignEncounter()
    {
        var spike = active;
        if (spike == null) return;
        spike.observing = false;
        Plugin.Logger.LogInfo(
            "Expedition campaign loop: result consumed by the controller; host observer paused for the next Begin.");
    }

    internal static void RequestCampaignRetry()
    {
        if (active != null) active.retryRequested = true;
    }

    internal static void RequestCampaignReturn()
    {
        if (active != null && active.returnGate.Request())
        {
            active.returnRequested = true;
            active.observing = false;
            CampaignEnemyHunt.Release();
        }
    }

    internal static void PrepareEditorReturn()
    {
        if (active == null) return;
        active.editorReturn = true;
        active.observing = false;
        // Close the native mission task on exit; the cancelled campaign callback
        // is a no-op, so native cleanup cannot count as a campaign defeat.
        CampaignOutcome = static (_, _) => { };
        CampaignCombatStarted = null;
        CampaignStartFailed = null;
    }

    internal static void ClearCampaignOutcome()
    {
        CampaignOutcome = null;
        CampaignCombatStarted = null;
        CampaignStartFailed = null;
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        observing = false;
        returnGate.Reset();
        controlTask = null;
        mode = null;
        teams = null;
        previous = null;
    }

    private void Update()
    {
        try
        {
            if (returnGate.SceneRequested)
            {
                var menu = UnityEngine.Object.FindObjectOfType<Sprocket.MainMenu>();
                if (returnGate.Complete(menu != null && menu.sceneStarted,
                        UnityEngine.Object.FindObjectOfType<GameController>() != null))
                {
                    mode = null; teams = null; previous = null;
                    resolvedMission = lossMission = null;
                    controlTask = returnTask = lossTask = null;
                    CampaignOutcome = null;
                    CampaignCombatStarted = null;
                    CampaignStartFailed = null;
                    Plugin.Logger.LogInfo("Expedition return: native main menu ready; unload/result guards released.");
                }
                return;
            }
            if (observing && !returnGate.Pending) Observe();
            if (!observing && controlTask != null && controlTask.IsCompleted)
            {
                Plugin.Logger.LogInfo("Expedition initial control transfer completed after observer stop; "
                    + "no start callback or further control correction is applied.");
                controlTask = null;
            }
            if (outcomeStage != 0 && !returnGate.Pending) TickLoss();

            if (lossOverlay)
            {
                // The game normally hides/locks the cursor for vehicle control.
                // The campaign result needs a normal pointer.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (retryRequested)
            {
                retryRequested = false;
                lossOverlay = false;
                observing = false;
                resultLogged = false;
                outcomeStage = 0;
                lossMission = null;
                Plugin.Logger.LogInfo(
                    "Expedition campaign loss: debug Retry selected; restarting the native encounter with the campaign lineup.");
                StartSpike();
                restoreCursorLock = true;
            }
            else if (returnRequested)
            {
                returnRequested = false;
                returnInProgress = true;
                returnTask = null;
                // Also covers any legacy/debug caller setting the request flag.
                returnGate.Request();
                observing = false;
                Plugin.Logger.LogInfo(
                    "Expedition return: finishing the native player mission before the main-menu scene change.");
                var mission = resultLogged || editorReturn ? (resolvedMission ?? FindPlayerMission()) : null;
                if (mission != null && mode != null)
                {
                    try
                    {
                        mode.missionRunning = false;
                        returnTask = !editorReturn && resolvedVictory ? mission.Complete() : mission.Fail();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning(
                            "Expedition return: native mission completion could not start; continuing with scene return. "
                            + ex.Message);
                    }
                }
            }

            if (returnInProgress)
            {
                if (returnTask != null && !returnTask.IsCompleted) return;
                if (returnTask != null && (returnTask.IsFaulted || returnTask.IsCanceled))
                {
                    Plugin.Logger.LogWarning(
                        "Expedition return: native mission completion did not complete successfully; continuing with scene return.");
                }
                returnTask = null;
                returnInProgress = false;
                if (RequestMainMenuScene())
                {
                    lossOverlay = false;
                    outcomeStage = 0;
                    observing = false;
                    editorReturn = false;
                    // Keep callback suppression and native references until the
                    // actual scene unload, rather than just the request above.
                }
                else returnGate.Reset(); // A rejected request must permit a later retry/native exit.
            }

            if (restoreCursorLock && !lossOverlay)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                restoreCursorLock = false;
                Plugin.Logger.LogInfo(
                    "Expedition campaign loss: cursor lock restored for vehicle control after debug Retry.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("Expedition campaign host spike failed: " + ex);
            observing = false;
        }
    }

    private void StartSpike(bool restart = true)
    {
        if (controlTask != null && !controlTask.IsCompleted)
        {
            Plugin.Logger.LogError("Expedition cannot overlap a previous pending native player-control transfer.");
            return;
        }
        if (observing)
        {
            Plugin.Logger.LogInfo("Expedition campaign host spike already active; waiting for its result or scene change.");
            return;
        }

        var found = UnityEngine.Object.FindObjectsOfType<DeathmatchGameMode>();
        if (found.Length != 1)
        {
            Plugin.Logger.LogError(
                $"Expedition campaign host spike needs exactly one loaded Custom Battle; found {found.Length}.");
            return;
        }

        var target = found[0];
        if (target.gameState != DeathmatchGameMode.ModeState.Running)
        {
            Plugin.Logger.LogError(
                "Expedition campaign host spike needs a Running Custom Battle before it can restart the lineup.");
            return;
        }

        var config = target.context?.SetupContext?.SetupConfig?.TryCast<BattleConfig>();
        var nativeTeams = config?.Teams;
        if (nativeTeams == null || nativeTeams.Length < 2)
        {
            Plugin.Logger.LogError(
                "Expedition campaign host spike needs a Custom Battle with at least two native team definitions.");
            return;
        }

        var playerIndex = -1;
        for (var i = 0; i < nativeTeams.Length; i++)
        {
            if (nativeTeams[i] != null
                && (nativeTeams[i].Flags & TeamDefinitionFlags.Player) != 0)
            {
                playerIndex = i;
                break;
            }
        }

        if (playerIndex < 0)
        {
            Plugin.Logger.LogError(
                "Expedition campaign host spike: no TeamDefinition is marked Player. "
                + "This Custom Battle does not expose a player team; use a normal player-vs-enemy battle.");
            return;
        }

        var enemyIndex = -1;
        for (var i = 0; i < nativeTeams.Length; i++)
        {
            if (i != playerIndex && nativeTeams[i] != null)
            {
                enemyIndex = i;
                break;
            }
        }

        if (enemyIndex < 0)
        {
            Plugin.Logger.LogError(
                "Expedition campaign host spike: no second team is available for the planned enemy.");
            return;
        }

        var playerTeam = nativeTeams[playerIndex];
        var enemyTeam = nativeTeams[enemyIndex];
        UnitDefinition? lockedUnit = null;
        if (lockedBlueprintBytes is { Length: > 0 })
        {
            lockedUnit = CustomBattleBlueprintResolver.CreateLockedUnit(
                lockedBlueprintBytes, out var lockedError);
            if (lockedUnit == null)
            {
                Plugin.Logger.LogError(
                    "Expedition campaign host spike: locked blueprint resolution failed; "
                    + "no existing unit was substituted. " + (lockedError ?? "unknown error"));
                return;
            }
        }

        var playerUnit = lockedUnit ?? FirstUnit(playerTeam);
        var enemyLineup = CampaignEnemyLineup;
        var hasFactionLineup = enemyLineup is { Count: > 0 };
        var enemyUnit = hasFactionLineup ? null : FirstUnit(enemyTeam);
        if (playerUnit == null)
        {
            Plugin.Logger.LogError(
                $"Expedition campaign host spike: player team index {playerIndex} has no existing UnitDefinition. "
                + "Place a tank on the player team in the Custom Battle screen first; no unit was invented.");
            return;
        }
        if (!hasFactionLineup && enemyUnit == null)
        {
            Plugin.Logger.LogError(
                $"Expedition campaign host spike: enemy team index {enemyIndex} has no existing UnitDefinition "
                + $"and no '{CampaignFactionDesigns.FactionName}' faction lineup was resolved. "
                + "Place a tank on the enemy team or add designs to the faction; no enemy was invented.");
            return;
        }

        var old = new Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo>?[nativeTeams.Length];
        for (var i = 0; i < nativeTeams.Length; i++)
        {
            old[i] = nativeTeams[i]?.Units;
        }

        // Custom team order is not native Team1/Team2 order. TeamDefinitionFlags.Player
        // identifies the player's team; the other team is the planned enemy.
        // Reuse the exact native UnitDefinition objects: player + one identical AI ally
        // on the player team, one planned enemy on the other team.
        var friendlyCount = Math.Max(1, CampaignFriendlyCount);
        var enemyCount = Math.Max(1, CampaignEnemyCount);
        playerTeam.Units = Lineup(playerUnit, friendlyCount);
        enemyTeam.Units = hasFactionLineup
            ? Lineup(enemyLineup!)
            : Lineup(enemyUnit!, enemyCount);

        mode = target;
        playerTeamID = target.playerTeamID;
        teams = nativeTeams;
        active = this;
        previous = old;
        observing = true;
        editorReturn = false;
        entered = false;
        rosterSeen = false;
        controlGuardDone = false;
        combatStartedNotified = false;
        combatStartedAt = null;
        contactMade = false;
        controlTask = null;
        expectedPlayerDesignName = string.IsNullOrWhiteSpace(CampaignPlayerDesignName)
            ? playerUnit.Name : CampaignPlayerDesignName;
        encounterSessionGeneration = CampaignNativeFlow.SessionGeneration;
        lastControlledEntityKey = null;
        victoryAiAllyCount = null;
        resultLogged = false;
        nextCheck = 0f;
        lastAllies = -1;
        lastEnemies = -1;
        lastUnknown = -1;
        lastSpawned = -1;
        observedEnemySpawns.Clear();
        initialNearestEnemy = null;
        outcomeStage = 0;
        lossMission = null;
        resolvedMission = null;
        resolvedVictory = false;
        returnInProgress = false;
        returnGate.Reset();
        returnTask = null;
        lossTask = null;
        lossOverlay = false;
        returnRequested = false;
        retryRequested = false;
        restoreCursorLock = false;
        confirmedDeadSpawns.Clear();
        tankNames.Clear();
        // A native restart (including the debug Retry) is a new battle: counters
        // start over while the encounter identity stays the same. The test-pool
        // ledger drops its per-slot evidence for the same reason.
        BattleStatsBridge.Restart();
        CampaignTestPool.Ledger.Restart();

        Plugin.Logger.LogInfo(
            "Expedition campaign host spike: native Custom Battle restart requested; "
            + $"playerCustomTeam={playerIndex} (flags={playerTeam.Flags}), "
            + $"enemyCustomTeam={enemyIndex} (flags={enemyTeam.Flags}), "
            + $"nativePlayerTeamID={(byte)target.playerTeamID}. "
            + $"Player team={friendlyCount} identical units, enemy team={enemyCount} requested enemy; "
            + $"enemy source={CampaignEnemyDesignSource}; "
            + (hasFactionLineup
                ? "faction designs resolved."
                : "loaded Custom Battle enemy fallback.")
            + " Actual roster is not yet proven.");
        if (restart) target.RetryAsyncVoid();
        else Plugin.Logger.LogInfo("Expedition: observing the fresh scene's native encounter; no Retry was requested.");
    }

    private void Observe()
    {
        var target = mode;
        if (target == null || !target.gameObject.scene.isLoaded)
        {
            observing = false;
            Plugin.Logger.LogInfo("Expedition campaign host spike: scene ended; observer stopped.");
            return;
        }

        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + .25f;

        if (target.gameState is DeathmatchGameMode.ModeState.Exiting
            or DeathmatchGameMode.ModeState.Exited)
        {
            observing = false;
            if (!resultLogged)
            {
                // A scene/battle exit is not a combat casualty. Use only the last
                // Running roster: both sides present means no outcome was observed.
                if (rosterSeen && lastAllies == 0)
                {
                    resultLogged = true;
                    Plugin.Logger.LogInfo(
                        "Expedition campaign host spike outcome: LOSS (last running roster had no friendlies), once.");
                }
                else if (rosterSeen && lastEnemies == 0)
                {
                    resultLogged = true;
                    Plugin.Logger.LogInfo(
                        "Expedition campaign host spike outcome: VICTORY (last running roster had no enemies), once.");
                }
                else
                {
                    Plugin.Logger.LogInfo(
                        "Expedition campaign host spike: battle exited without a friendly/enemy wipe; no outcome inferred.");
                }
            }
            return;
        }

        if (target.gameState != DeathmatchGameMode.ModeState.Running) return;
        var game = UnityEngine.Object.FindObjectOfType<GameController>();
        if (game == null || game.state != GameState.Running) return;

        var spawns = target.vehicleSpawns;
        if (spawns == null) return;
        var spawned = 0;
        for (var i = 0; i < spawns.Count; i++)
        {
            if (spawns[i] != null && spawns[i].Spawned) spawned++;
        }

        if (spawned < lastSpawned && lastSpawned > 0 && outcomeStage == 0)
        {
            ResetBattleState();
        }

        if (spawned != lastSpawned)
        {
            lastSpawned = spawned;
            Plugin.Logger.LogInfo(
                $"Expedition campaign host spike actual spawns: {spawned}/{spawns.Count} spawned; state={target.gameState}.");
        }

        if (!entered)
        {
            if (target.gameState == DeathmatchGameMode.ModeState.Running
                && spawns.Count > 0
                && spawned == spawns.Count)
            {
                entered = true;
                Plugin.Logger.LogInfo("Expedition campaign host spike: all requested native spawns are in.");
            }
            return;
        }

        if (!CountRoster(out var allies, out var enemies, out var unknown)) return;

        if (allies != lastAllies || enemies != lastEnemies || unknown != lastUnknown)
        {
            lastAllies = allies;
            lastEnemies = enemies;
            lastUnknown = unknown;
            Plugin.Logger.LogInfo(
                $"Expedition campaign host spike actual roster: friendlies={allies}; enemies={enemies}; "
                + $"unreadable={unknown}; playerTeamID={(byte)playerTeamID}. "
                + "All friendlies must have no living crew before a loss is inferred.");
        }

        if (!rosterSeen)
        {
            if (allies > 0 && enemies > 0 && unknown == 0)
            {
                rosterSeen = true;
                MeasureInitialEnemyGap();
                Plugin.Logger.LogInfo(
                    "Expedition campaign host spike: actual roster is registered; outcome observation armed.");
            }
            else
            {
                return;
            }
        }

        if (!EnsureFriendlyPlayerControl()) return;
        RememberControlledEntity();
        if (!combatStartedNotified)
        {
            combatStartedNotified = true;
            combatStartedAt = Time.time;
            var combatStarted = CampaignCombatStarted;
            CampaignCombatStarted = null;
            try { combatStarted?.Invoke(); }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("Expedition campaign loop: combat-started callback failed: " + ex);
            }
        }

        if (resultLogged) return;

        // All friendlies lost dominates any simultaneous enemy loss; losing only the
        // original player is not a loss while an ally remains. Unreadable crew is
        // never treated as death, so a partial native read cannot fabricate a loss.
        if (allies == 0 && unknown == 0)
        {
            BeginLossOutcome(null, "all friendly vehicles have no living crew");
        }
        else if (enemies == 0 && unknown == 0)
        {
            resultLogged = true;
            resolvedMission = FindPlayerMission();
            resolvedVictory = true;
            CaptureVictoryAllies();
            Plugin.Logger.LogInfo(
                "Expedition campaign host spike outcome: VICTORY (enemy formation destroyed), once.");
            if (CampaignOutcome != null)
            {
                CampaignOutcome(true, "Enemy formation destroyed.");
            }
        }
    }

    private bool EnsureFriendlyPlayerControl()
    {
        if (!CampaignNativeFlow.OwnsSession || encounterSessionGeneration != CampaignNativeFlow.SessionGeneration)
        {
            observing = false;
            return false;
        }
        // Once initial control is qualified, never undo later manual cycling.
        if (controlGuardDone) return true;
        if (mode == null) return false;
        try
        {
            var completingTransfer = controlTask != null;
            if (controlTask != null)
            {
                if (!controlTask.IsCompleted) return false;
                if (controlTask.IsFaulted || controlTask.IsCanceled)
                    return FailCampaignStart("Native transfer to the player's own design failed or was cancelled.");
                controlTask = null;
            }
            var currentTank = CurrentControlledTank();
            if (IsOwnDesign(currentTank) && currentTank!.ControlType == ControlType.Player)
            {
                controlGuardDone = true;
                RememberControlledEntity();
                Plugin.Logger.LogInfo($"Expedition initial player control verified: own native design '{expectedPlayerDesignName}'; "
                    + $"entity={(int)currentTank.ID}; spawn=({currentTank.Position.x:N0}, {currentTank.Position.z:N0}); transferCompleted={completingTransfer}. Later player switches remain native.");
                return true;
            }
            if (completingTransfer)
                return FailCampaignStart("Native control transfer completed without the player's own design being controlled.");
            var own = FindFriendlyTank();
            var target = own?.TryCast<IVehicleBehaviour>();
            if (target == null || mode.vehicleControlState == null)
                return FailCampaignStart("No living friendly hull matches the player's exact native design name: "
                    + expectedPlayerDesignName);
            controlTask = mode.vehicleControlState.SetControlTarget(target);
            if (controlTask == null)
                return FailCampaignStart("Native SetControlTarget returned no completion task.");
            Plugin.Logger.LogInfo($"Expedition initial player control: awaiting one native transfer to own design '{expectedPlayerDesignName}'; "
                + $"entity={(int)own!.ID}. No combat-start acknowledgement until completion.");
            return false;
        }
        catch (Exception ex)
        {
            return FailCampaignStart("Initial own-design control failed: " + ex.Message);
        }
    }

    private bool FailCampaignStart(string reason)
    {
        observing = false;
        controlTask = null;
        victoryAiAllyCount = null;
        CampaignCombatStarted = null;
        var failed = CampaignStartFailed;
        CampaignStartFailed = null;
        Plugin.Logger.LogError("Expedition native encounter start rejected: " + reason + " No combat result fabricated.");
        if (CampaignNativeFlow.OwnsSession && encounterSessionGeneration == CampaignNativeFlow.SessionGeneration)
        {
            try { failed?.Invoke(reason); }
            catch (Exception ex) { Plugin.Logger.LogError("Expedition start-failure callback: " + ex.Message); }
        }
        return false;
    }

    private VehicleBehaviour? CurrentControlledTank()
    {
        try
        {
            return mode?.vehicleControlState?.TryCast<VehicleControlPlayerState>()?.vehicleController
                ?.ControlledVehicle?.TryCast<VehicleBehaviour>();
        }
        catch { return null; }
    }

    private bool IsOwnDesign(VehicleBehaviour? tank)
    {
        try
        {
            if (tank == null || tank.WasCollected) return false;
            var alive = AliveCrewCount(tank, out var readable);
            var name = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name;
            var id = tank.ID;
            return CampaignPlayerControlPolicy.IsOwnDesign(expectedPlayerDesignName, name,
                id.TeamID.Equals(playerTeamID), id.Valid, alive, readable);
        }
        catch { return false; }
    }

    private void RememberControlledEntity()
    {
        var tank = CurrentControlledTank();
        try
        {
            if (tank != null && tank.ID.Valid && tank.ID.TeamID.Equals(playerTeamID))
            {
                lastControlledEntityKey = (int)tank.ID;
                BattleStatsBridge.NotePlayer(tank.Pointer);
            }
        }
        catch { }
    }

    private int CountLivingAiAllies()
    {
        RememberControlledEntity();
        var roster = new List<CampaignPlayerControlPolicy.Ally>();
        var spawns = mode?.vehicleSpawns;
        if (spawns == null) return 0;
        for (var i = 0; i < spawns.Count; i++)
        {
            try
            {
                var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
                var tank = gateway?.Behaviour?.TryCast<VehicleBehaviour>()
                    ?? gateway?.transform?.GetComponentInChildren<VehicleBehaviour>();
                if (tank == null || tank.WasCollected || !tank.gameObject.activeInHierarchy) continue;
                var id = tank.ID;
                if (!id.Valid) continue;
                var alive = AliveCrewCount(tank, out var readable);
                var control = tank.ControlType;
                roster.Add(new CampaignPlayerControlPolicy.Ally((int)id, id.TeamID.Equals(playerTeamID),
                    control == ControlType.AI, control == ControlType.Player, alive, readable));
            }
            catch { } // unreadable hulls do not manufacture a reward ally
        }
        return CampaignPlayerControlPolicy.CountLivingAiAllies(roster, lastControlledEntityKey);
    }

    private void CaptureVictoryAllies()
    {
        try { victoryAiAllyCount = CountLivingAiAllies(); }
        catch { victoryAiAllyCount = 0; }
        Plugin.Logger.LogInfo($"Expedition definitive victory roster: {victoryAiAllyCount} confirmed living AI allies; "
            + "actual player-controlled entity excluded.");
    }

    private void MeasureInitialEnemyGap()
    {
        var friends = new List<Vector3>();
        var enemies = new List<Vector3>();
        var spawns = mode?.vehicleSpawns;
        if (spawns == null) return;
        for (var i = 0; i < spawns.Count; i++)
        {
            var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
            var tank = gateway?.Behaviour?.TryCast<VehicleBehaviour>();
            if (tank == null) continue;
            Plugin.Logger.LogInfo($"Expedition initial spawn attitude: team={(byte)tank.ID.TeamID}; upDot={Vector3.Dot(tank.transform.up, Vector3.up):N3}; y={tank.transform.position.y:N2}; map={mode!.gameObject.scene.name}.");
            (tank.ID.TeamID.Equals(playerTeamID) ? friends : enemies).Add(tank.transform.position);
        }
        var nearest = float.PositiveInfinity;
        foreach (var friend in friends)
            foreach (var enemy in enemies)
            {
                var delta = enemy - friend; delta.y = 0f;
                nearest = Math.Min(nearest, delta.magnitude);
            }
        if (!float.IsFinite(nearest)) return;
        initialNearestEnemy = nearest;
        Plugin.Logger.LogInfo($"Expedition initial measured nearest enemy: {nearest:N1} m horizontally; {friends.Count} friendly / {enemies.Count} enemy positions; map={mode!.gameObject.scene.name}.");
    }

    private VehicleBehaviour? FindFriendlyTank()
    {
        var spawns = mode?.vehicleSpawns;
        if (spawns == null) return null;
        for (var i = 0; i < spawns.Count; i++)
        {
            var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
            if (gateway == null) continue;
            var tank = gateway.Behaviour?.TryCast<VehicleBehaviour>()
                ?? gateway.transform?.GetComponentInChildren<VehicleBehaviour>();
            if (IsOwnDesign(tank)) return tank;
        }
        return null;
    }

    private bool CountRoster(out int allies, out int enemies, out int unknown)
    {
        allies = 0;
        enemies = 0;
        unknown = 0;
        var target = mode;
        var spawns = target?.vehicleSpawns;
        if (spawns == null) return false;

        for (var i = 0; i < spawns.Count; i++)
        {
            var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
            if (gateway == null)
            {
                if (!confirmedDeadSpawns.Contains(i)) unknown++;
                continue;
            }
            var tank = gateway.Behaviour?.TryCast<VehicleBehaviour>()
                ?? gateway.transform?.GetComponentInChildren<VehicleBehaviour>();
            if (tank == null)
            {
                if (!confirmedDeadSpawns.Contains(i)) unknown++;
                continue;
            }

            if (spawns[i]?.Spawned == true && !tank.ID.TeamID.Equals(playerTeamID)) observedEnemySpawns.Add(i);
            var alive = AliveCrewCount(tank, out var readable, out var crewDamaged);
            var name = TankName(i, tank);
            if (CampaignTestPool.Ledger.Active && !tank.ID.TeamID.Equals(playerTeamID))
                RecordTestPoolTank(i, name, spawns[i]?.Spawned == true, readable && alive <= 0, crewDamaged);
            RegisterBattleStatsTank(tank, name);
            if (!readable)
            {
                // Missing/unreadable crew is not evidence of death. Count the
                // vehicle as present but flag that the roster cannot decide an
                // outcome from this vehicle alone.
                unknown++;
                if (tank.ID.TeamID.Equals(playerTeamID)) allies++;
                else enemies++;
                continue;
            }

            if (alive <= 0)
            {
                confirmedDeadSpawns.Add(i);
                CampaignBattleStats.Current.Destroyed(tank.Pointer, Time.time);
                continue;
            }
            confirmedDeadSpawns.Remove(i);
            if (tank.ID.TeamID.Equals(playerTeamID)) allies++;
            else enemies++;
        }

        return true;
    }

    // One native name read per spawn slot, cached until the next battle. Used by
    // the test-pool CSV and by the battle statistics; the ledger owns the slot
    // mapping so a stale slot name can never survive a new battle.
    private string? TankName(int index, VehicleBehaviour tank)
    {
        if (tankNames.TryGetValue(index, out var name)) return name;
        try { name = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name; } catch { name = null; }
        if (!string.IsNullOrWhiteSpace(name)) tankNames[index] = name;
        return name;
    }

    private void RegisterBattleStatsTank(VehicleBehaviour tank, string? name)
    {
        if (!CampaignBattleStats.Current.Active) return;
        try
        {
            var rigidBody = IntPtr.Zero;
            var applier = IntPtr.Zero;
            try { rigidBody = tank.Rigidbody?.Pointer ?? IntPtr.Zero; } catch { }
            try { applier = tank.DamageApplier?.Pointer ?? IntPtr.Zero; } catch { }
            BattleStatsBridge.Register(tank.Pointer, name, tank.ID.TeamID.Equals(playerTeamID),
                rigidBody, applier);
        }
        catch { }
    }

    // 0.3.4 test-pool CSV: per-design evidence for the enemy team. The ledger
    // maps spawn slots itself and starts empty for every battle.
    private void RecordTestPoolTank(int index, string? name, bool spawned, bool dead, bool crewDamaged)
    {
        if (spawned) CampaignTestPool.Ledger.Spawned(index, name);
        if (dead || crewDamaged) CampaignTestPool.Ledger.Contact(name);
    }

    private static int AliveCrewCount(VehicleBehaviour tank, out bool readable)
        => AliveCrewCount(tank, out readable, out _);

    private static int AliveCrewCount(VehicleBehaviour tank, out bool readable, out bool damaged)
    {
        readable = false;
        damaged = false;
        if (tank == null
            || !tank.TryGetModule<VehicleCrewWorkspaceModule>(out var module)
            || module?.operators == null)
        {
            return 0;
        }

        var alive = 0;
        var allReadable = true;
        for (var i = 0; i < module.operators.Count; i++)
        {
            var member = module.operators[i]?.internalOperator;
            if (member == null) { allReadable = false; continue; }
            try
            {
                var health = member.HealthFraction;
                if (!float.IsFinite(health)) { allReadable = false; continue; }
                readable = true;
                if (health > 0) alive++;
                if (health < 1f) damaged = true;
            }
            catch
            {
                // A single unreadable crew member never proves the vehicle dead.
                allReadable = false;
            }
        }

        readable &= allReadable;
        return alive;
    }

    private void ResetBattleState()
    {
        initialNearestEnemy = null;
        confirmedDeadSpawns.Clear();
        tankNames.Clear();
        BattleStatsBridge.Restart();
        CampaignTestPool.Ledger.Restart();
        resultLogged = false;
        rosterSeen = false;
        controlGuardDone = false;
        combatStartedNotified = false;
        combatStartedAt = null;
        contactMade = false;
        lastControlledEntityKey = null;
        victoryAiAllyCount = null;
        resolvedVictory = false;
        entered = false;
        lastAllies = -1;
        lastEnemies = -1;
        lastUnknown = -1;
        lastSpawned = -1;
        observedEnemySpawns.Clear();
        outcomeStage = 0;
        lossMission = null;
        lossTask = null;
        lossOverlay = false;
        returnRequested = false;
        retryRequested = false;
        restoreCursorLock = false;
        Plugin.Logger.LogInfo(
            "Expedition campaign host spike: a new native battle generation was detected; result observer reset.");
    }

    private Mission? FindPlayerMission()
    {
        var missions = mode?.missions;
        if (missions == null) return null;
        for (var i = 0; i < missions.Length; i++)
        {
            var mission = missions[i];
            if (mission != null && (((int)mission.Flags) & 1) != 0) return mission;
        }

        return null;
    }

    private bool BeginLossOutcome(Mission? mission, string reason)
    {
        if (outcomeStage != 0) return true;
        var playerMission = mission ?? FindPlayerMission();
        if (playerMission == null)
        {
            Plugin.Logger.LogError(
                "Expedition campaign loss: no native player mission was found; the original native result will be left untouched.");
            return false;
        }

        resultLogged = true;
        lossMission = playerMission;
        resolvedMission = playerMission;
        resolvedVictory = false;
        outcomeStage = 4;

        if (CampaignOutcome != null)
        {
            lossOverlay = false;
            Plugin.Logger.LogInfo(
                $"Expedition campaign loop outcome: LOSS accepted ({reason}); notifying the campaign hub. "
                + "The native Retry-only fail screen is bypassed; no result is fabricated.");
            CampaignOutcome(false, reason);
            return true;
        }

        lossOverlay = true;
        Plugin.Logger.LogInfo(
            $"Expedition campaign host spike outcome: LOSS accepted ({reason}); "
            + "campaign defeat overlay is active. The native Retry-only fail screen is bypassed, "
            + "the native mission is left untouched, and Return to Menu performs the scene transition. "
            + "No result is fabricated.");
        return true;
    }

    private void TickLoss()
    {
        var target = mode;
        var mission = lossMission;
        if (target == null || mission == null)
        {
            outcomeStage = 0;
            return;
        }

        if (outcomeStage == 1)
        {
            try
            {
                // Preserve the native result sequence while avoiding a second
                // native FailMission from running after our own loss decision.
                target.missionRunning = false;
                lossTask = mission.Fail();
                if (lossTask == null)
                {
                    Plugin.Logger.LogError(
                        "Expedition campaign loss: native Mission.Fail returned no task; no result delivered.");
                    outcomeStage = 0;
                    return;
                }

                outcomeStage = 2;
                Plugin.Logger.LogInfo(
                    $"Expedition campaign loss: native player mission Fail requested; mission={mission.name}; awaiting its actual task.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("Expedition campaign loss: native Mission.Fail could not start. " + ex);
                outcomeStage = 0;
            }
            return;
        }

        if (outcomeStage == 2)
        {
            if (lossTask == null || !lossTask.IsCompleted) return;
            if (lossTask.IsFaulted || lossTask.IsCanceled)
            {
                Plugin.Logger.LogError(
                    "Expedition campaign loss: native Mission.Fail did not complete successfully; no fail screen was claimed.");
                lossTask = null;
                outcomeStage = 0;
                return;
            }

            lossTask = null;
            // The native Custom Battle fail screen only offers Retry and has no
            // editor/menu return. Bypass it and let the campaign result overlay own
            // the visible result and the explicit Return action.
            lossOverlay = true;
            outcomeStage = 4;
            Plugin.Logger.LogInfo(
                "Expedition campaign loss: native Mission.Fail completed; campaign defeat overlay shown. "
                + "The native Retry-only fail screen is bypassed; Return to Menu is the campaign action.");
            return;
        }

        if (outcomeStage == 4)
        {
            // The campaign overlay now owns the visible result. Keep the observer
            // alive until the explicit Return action or the actual mode exit.
            if (target == null
                || !target.gameObject.scene.isLoaded
                || target.gameState is DeathmatchGameMode.ModeState.Exiting
                    or DeathmatchGameMode.ModeState.Exited)
            {
                outcomeStage = 0;
                observing = false;
                Plugin.Logger.LogInfo(
                    "Expedition campaign loss: native mode exit observed; loss ownership cleared.");
            }
        }
    }

    internal static void SetLockedBlueprint(byte[]? bytes)
    {
        lockedBlueprintBytes = bytes is { Length: > 0 } ? (byte[])bytes.Clone() : null;
        Plugin.Logger.LogInfo(
            lockedBlueprintBytes is null
                ? "Expedition campaign host spike: no locked blueprint set; existing native unit definitions will be used."
                : $"Expedition campaign host spike: locked blueprint set ({lockedBlueprintBytes.Length} bytes); it will be resolved for player + ally at the next F7.");
    }

    internal static bool TrySuppressNativeReset(DeathmatchGameMode mode, string source)
    {
        var spike = active;
        if (spike == null || spike.mode?.Pointer != mode.Pointer) return false;
        if (spike.returnGate.AllowsCleanup(source,
                mode.gameState is DeathmatchGameMode.ModeState.Exiting or DeathmatchGameMode.ModeState.Exited))
        {
            Plugin.Logger.LogInfo("Expedition return: allowing actual native ResetGame cleanup during exit.");
            return false;
        }
        if (spike.returnGate.Pending)
        {
            Plugin.Logger.LogInfo($"Expedition return: suppressed stale native {source} during unload.");
            return true;
        }

        // A campaign result is visible in the hub; no native reset/retry may
        // restart the battle behind it. The hub's debug Retry clears resultLogged
        // before calling the native retry, so that path is allowed through.
        if (spike.editorReturn || CampaignOutcome != null && spike.resultLogged)
        {
            Plugin.Logger.LogInfo(
                $"Expedition campaign loop: native {source} suppressed after the result was delivered to the hub.");
            return true;
        }

        if (!spike.lossOverlay || spike.lossMission == null) return false;

        // The campaign overlay owns the result. A native automatic reset or a
        // hidden Retry must not restart the battle behind it. The explicit
        // campaign Retry button clears lossOverlay first, so it is allowed through.
        Plugin.Logger.LogInfo(
            $"Expedition campaign loss: native {source} suppressed while the campaign defeat overlay owns the result.");
        return true;
    }

    private bool RequestMainMenuScene()
    {
        try
        {
            var scenes = ISceneManager.Instance;
            if (scenes == null)
            {
                Plugin.Logger.LogError(
                    "Expedition campaign return: the native scene manager is not available.");
                return false;
            }

            if (!scenes.TryGetFirstScene(SceneFlags.MainMenu, out var menu))
            {
                Plugin.Logger.LogError(
                    "Expedition campaign return: the native main-menu scene was not found.");
                return false;
            }

            var game = UnityEngine.Object.FindObjectOfType<GameController>();
            if (game == null)
            {
                Plugin.Logger.LogError(
                    "Expedition campaign return: the native GameController is not available.");
                return false;
            }

            // Mark before calling: RequestSceneChange can synchronously start
            // native ExitGameInternal, which must await its real ResetGame task.
            returnGate.SceneStarted();
            game.RequestSceneChange(menu);
            Plugin.Logger.LogInfo(
                "Expedition campaign return: native GameController.RequestSceneChange(main menu) requested; "
                + "the vanilla scene transition owns cleanup/return.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError("Expedition campaign return: main-menu scene request failed. " + ex);
            return false;
        }
    }

    internal static bool NativeResult(
        DeathmatchGameMode mode,
        Mission mission,
        bool complete)
    {
        var spike = active;
        if (spike == null || spike.mode?.Pointer != mode.Pointer) return false;
        if (spike.returnGate.Pending) return true;
        if (!spike.observing)
        {
            // A controller result is still visible; suppress stale native result
            // callbacks until the controller starts the next attempt or returns.
            return CampaignOutcome != null;
        }
        return spike.OnNativeResult(mission, complete);
    }

    // Returns true when the spike handled the result and the original native
    // result method must be skipped. False lets the native victory/result run.
    private bool OnNativeResult(Mission mission, bool complete)
    {
        if (editorReturn) return true;
        var game = UnityEngine.Object.FindObjectOfType<GameController>();
        if (game == null || game.state != GameState.Running) return true;
        var playerMission = mission != null && (((int)mission.Flags) & 1) != 0;
        if (!playerMission) return false;

        // Native CompleteMission can be called repeatedly after the first result.
        // Suppress without logging once this generation is resolved.
        if (resultLogged) return true;

        // Ignore results from the previous battle while the next host restart is
        // still loading. StartSpike resets entered=false before RetryAsyncVoid.
        if (!entered) return true;
        // The one initial handoff must be confirmed before a result can be
        // delivered for this attempt. Observe will re-evaluate the live roster.
        if (!controlGuardDone) return true;

        // Prefer a fresh crew-based read at the native result boundary. The 0.25 s
        // observer can be one frame stale exactly when the last crew member dies.
        var allies = lastAllies;
        var unknown = lastUnknown;
        if (CountRoster(out var currentAllies, out var currentEnemies, out var currentUnknown))
        {
            allies = currentAllies;
            unknown = currentUnknown;
            lastAllies = currentAllies;
            lastEnemies = currentEnemies;
            lastUnknown = currentUnknown;
        }

        Plugin.Logger.LogInfo(
            $"Expedition campaign host spike native {(complete ? "CompleteMission" : "FailMission")}: "
            + $"mission={mission?.name ?? "none"}; flags={(mission == null ? 0 : (int)mission.Flags)}; "
            + $"playerMission={playerMission}; lastFriendlies={lastAllies}; lastEnemies={lastEnemies}; "
            + $"rosterFriendliesNow={allies}; unreadableNow={unknown}.");

        if (complete)
        {
            // All-friendlies death dominates a simultaneous enemy elimination.
            if (allies == 0 && unknown == 0)
            {
                return BeginLossOutcome(mission, "native complete and no friendly has living crew");
            }

            if (resultLogged) return true;
            resultLogged = true;

            if (CampaignOutcome != null)
            {
                resolvedMission = mission;
                resolvedVictory = true;
                CaptureVictoryAllies();
                Plugin.Logger.LogInfo(
                    "Expedition campaign loop outcome: VICTORY (native player CompleteMission), once; "
                    + "notifying the campaign hub and suppressing the native success screen.");
                CampaignOutcome(true, "Enemy formation destroyed.");
                return true;
            }

            Plugin.Logger.LogInfo(
                "Expedition campaign host spike outcome: VICTORY (native player CompleteMission), once.");
            return false;
        }

        // Native FailMission is not automatically an expedition loss: an ally may
        // still be alive. The approved rule requires every friendly to be destroyed.
        if (allies == 0 && unknown == 0)
        {
            return BeginLossOutcome(mission, "native player FailMission and no friendly has living crew");
        }

        // Suppress the native fail while an ally still lives; the campaign must keep
        // the battle Running so the surviving friendly can be played.
        if (unknown > 0)
        {
            Plugin.Logger.LogInfo(
                "Expedition campaign host spike: native player FailMission but friendly crew state is unreadable; "
                + "native fail suppressed, no expedition loss inferred.");
        }
        else
        {
            Plugin.Logger.LogInfo(
                "Expedition campaign host spike: native player FailMission while friendlies remain; "
                + "native fail suppressed; no expedition loss inferred (all-friendlies rule).");
        }
        return true;
    }

    internal void RestoreOriginalLineup()
    {
        var currentTeams = teams;
        var old = previous;
        if (currentTeams == null || old == null || mode == null || !mode.gameObject.scene.isLoaded)
        {
            return;
        }

        for (var i = 0; i < currentTeams.Length && i < old.Length; i++)
        {
            if (currentTeams[i] != null && old[i] != null)
            {
                currentTeams[i].Units = old[i]!;
            }
        }

        previous = null;
        Plugin.Logger.LogInfo("Expedition campaign host spike: original team lineups restored for the next restart.");
    }

    private static UnitDefinition? FirstUnit(TeamDefinition team)
    {
        var units = team?.Units;
        if (units == null) return null;
        foreach (var entry in units)
        {
            if (entry.Key != null && entry.Value != null && entry.Value.Count > 0)
            {
                return entry.Key;
            }
        }

        return null;
    }

    internal static Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo> Lineup(
        UnitDefinition unit,
        int count)
    {
        var lineup = new Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo>();
        lineup.Add(unit, new UnitInstanceInfo { Count = count });
        return lineup;
    }

    internal static Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo> Lineup(
        IReadOnlyList<(UnitDefinition Unit, int Count)> entries)
    {
        var lineup = new Il2CppSystem.Collections.Generic.Dictionary<UnitDefinition, UnitInstanceInfo>();
        var unique = new Dictionary<IntPtr, (UnitDefinition Unit, int Count)>();
        foreach (var entry in entries)
        {
            if (entry.Unit == null || entry.Count <= 0) continue;
            var pointer = entry.Unit.Pointer;
            if (unique.TryGetValue(pointer, out var existing))
            {
                unique[pointer] = (entry.Unit, existing.Count + entry.Count);
            }
            else
            {
                unique[pointer] = (entry.Unit, entry.Count);
            }
        }

        foreach (var entry in unique.Values)
        {
            lineup.Add(entry.Unit, new UnitInstanceInfo { Count = entry.Count });
        }
        return lineup;
    }
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.CompleteMission))]
internal static class CampaignSpikeCompleteMissionHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance, Mission __0)
        => !CustomBattleHostSpike.NativeResult(__instance, __0, complete: true);
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.FailMission))]
internal static class CampaignSpikeFailMissionHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance, Mission __0)
        => !CustomBattleHostSpike.NativeResult(__instance, __0, complete: false);
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.RetryAsyncVoid))]
internal static class CampaignSpikeRetryVoidHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance)
        => !CustomBattleHostSpike.TrySuppressNativeReset(__instance, "RetryAsyncVoid");
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.Retry))]
internal static class CampaignSpikeRetryTaskHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance, ref NativeTask __result)
    {
        if (!CustomBattleHostSpike.TrySuppressNativeReset(__instance, "Retry")) return true;
        __result = NativeTask.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.ResetGame))]
internal static class CampaignSpikeResetGameHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance, ref NativeTask __result)
    {
        if (!CustomBattleHostSpike.TrySuppressNativeReset(__instance, "ResetGame")) return true;
        __result = NativeTask.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.SetupGame))]
internal static class CampaignSpikeSetupGameHook
{
    [HarmonyPrefix]
    private static bool Prefix(DeathmatchGameMode __instance, ref NativeTask __result)
    {
        if (!CustomBattleHostSpike.TrySuppressNativeReset(__instance, "SetupGame")) return true;
        __result = NativeTask.CompletedTask;
        return false;
    }
}
