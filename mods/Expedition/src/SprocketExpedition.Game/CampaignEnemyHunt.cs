#if CAMPAIGN
using HarmonyLib;
using Sprocket;
using Sprocket.ArtificialIntelligence;
using Sprocket.CustomBattles;
using Sprocket.Orders;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.CrewSystems;
using Sprocket.Vehicles.Spawning;
using Sprocket.Vehicles.Orders.Construction;
using UnityEngine;
using UObject = UnityEngine.Object;
using Numerics = System.Numerics;

namespace SprocketExpedition.Game;

// Owns only current Expedition AI commanders. Native detection, pathfinding,
// aiming and damage stay authoritative. The historical service/config name is
// retained; both teams now use the same visible-target and order-lifetime policy.
internal static class CampaignEnemyHunt
{
    private const float ScanInterval = .25f;
    private const float MinimumOrderSpacing = 2.5f;
    private const float SearchArriveRadius = 18f;
    private enum Mode { None, Advance, Search, Attack, Recover, BossRetreat }

    private sealed class TeamMemory
    {
        internal Vector3 Origin;
        internal Vector3 LastKnown;
        internal IntPtr Contact;
        internal float LastSeenAt = -999f;
        internal bool Seen;
    }

    private sealed class EnemySpawnOrigin
    {
        internal IntPtr Pointer;
        internal Vector3 Position;
        internal string Name = "unknown";
    }

    private sealed class Unit
    {
        internal VehicleBehaviour Tank = null!;
        internal CommanderAI Commander = null!;
        internal bool Friendly;
        internal int Slot;
        internal Vector3 Spawn;
        internal Vector3 LastOrderTarget;
        internal Vector3 SearchTarget;
        internal Vector3 SearchAnchor;
        internal Vector3 RecoveryTarget;
        internal CampaignHuntPlanner.GoalProgress Progress = new();
        internal float LastOrderAt = -999f;
        internal float LastOwnSightAt = -999f;
        internal float SearchUntil;
        internal float RecoveryUntil;
        internal float NextTryAt;
        internal float NextRecoveryAt;
        internal int SearchStep;
        internal int RecoveryCount;
        internal Mode Mode;
        internal IntPtr AttackTarget;
        internal bool StopToFire;
        internal bool Yielded;
        internal bool LoggedFirstMove;
        internal float NextUnsafeGoalLog;
        internal CampaignBossTactics.Character BossCharacter;
        internal bool BossEscort;
        internal CampaignBossTactics.Character EscortCharacter;
        internal float SpawnedAt;
        internal float LastContactAt;
        internal float InitialContactDistance = -1f;
        internal bool FortressSlowAdvanceUsed;
        internal float FortressSlowUntil;
        internal float NextBossRetreatAt;
        internal float BossRetreatUntil;
        internal CampaignCombatPolicy.Profile CombatProfile;
        internal CampaignCombatPolicy.Reaction Reaction = new();
        internal IntPtr ReactionTarget;
        internal string GroupRole = CampaignGroupDoctrine.Center;
        internal int LaneSide;
        internal float ContactDistance = float.PositiveInfinity;
        internal float NextDoctrineHoldLog;
        internal float DoctrineContactSince = -999f;
        internal IntPtr DoctrineContactTarget;
        internal float DoctrineLegStarted = -999f;
        internal float DoctrineLegUntil = -999f;
        internal Vector3 DoctrineLegTarget;
        internal float DoctrineLegStartDistance;
        internal float NextDoctrineLegAt = -999f;
        internal HaltTask? FireHold;
        internal FaceTargetTask? FireFacing;
        internal string DesignName = "unknown";
        internal bool ClearShot;
        internal float NextShotCheck;
        internal IntPtr ShotTarget;
        internal readonly List<(GunLayerAI Layer, AimAtDirectionTask Task)> PreAim = new();
        internal bool PreAimUnavailable;
        internal bool Ambusher;
        internal string AmbusherSource = "none";
        internal float NextAmbusherStepAt;
        internal FaceTargetTask? ReactionFacing;
        internal IntPtr ReactionFacingTarget;
        internal readonly CampaignTargetCommitment.Tracker TargetChoice = new();
        internal readonly Dictionary<IntPtr, (float Until, bool Clear)> FiringLines = new();
        internal readonly CampaignCombatPolicy.FireTracker FireTracker = new();
        internal readonly List<int> FireHeads = new();
        internal bool FireStallLogged;
        internal bool FireHasTarget;
        internal bool FireLoaded;
        internal float FireError = float.NaN;
        internal float FireHullAngle = float.NaN;
        internal bool Repositioning;
        internal IntPtr RepositionTarget;
        internal float RepositionUntil;
        internal Vector3 RepositionStart;
        internal int FlankSide = 1;
        internal float NextStallActionAt;
    }

    private static readonly List<Unit> units = new();
    private static readonly List<EnemySpawnOrigin> enemySpawnOrigins = new();
    private static readonly Dictionary<IntPtr, Unit> travelDrivers = new();
    private static readonly List<VehicleBehaviour> liveTanks = new();
    private static readonly HashSet<IntPtr> claimed = new();
    private static readonly HashSet<IntPtr> yieldedTanks = new();
    private static readonly HashSet<IntPtr> manualReceivers = new();
    private static readonly HashSet<string> blockedLogged = new();
    private static readonly List<CampaignHuntPlanner.VisibleTarget> candidates = new();
    private static readonly List<CampaignTargetCommitment.Candidate> targetCandidates = new();
    private static DeathmatchGameMode? mode;
    private static CampaignHuntPlanner.SpawnMemory spawnsAtStart = new();
    private static TeamMemory friendlyMemory = new();
    private static TeamMemory enemyMemory = new();
    private static float spawnCaptureAt = -1f;
    private static float spawnReferenceHeight;
    private static int expectedFriendlies;
    private static int expectedEnemies;
    private static float nextScan;
    private static float nextModeSearch;
    private static float nextSummary;
    private static bool prepared;
    private static bool hasFix;
    private static bool faulted;
    private static int enemySlots;
    private static int friendlySlots;
    private static int totalOrders;
    private static int blockedOrders;
    private static IntPtr issuing;
    private static IntPtr bossPointer;
    private static bool bossIdentityResolved;
    private static bool bossIdentityRejected;
    private static bool bossCharacterLogged;
    private static string bossStatus = string.Empty;
    private static string combatSeed = string.Empty;
    private static int combatEraIndex;
    private static decimal[] eraPrices = Array.Empty<decimal>();
    private static readonly Dictionary<string, decimal> designPrices = new(StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, string>? groupRoles;
    private static IReadOnlyDictionary<string, CampaignCasemateDetection.Decision>? ambusherDesigns;
    private static bool roleMappingRejected;
    private static bool spawnMappingVerified;
    internal static string Status { get; private set; } = "AI: native map orders";

    internal static void Prepare(BattleConfig battle, string planHash,
        IEnumerable<(string Name, decimal Price)>? prices = null, int eraIndex = 0,
        IReadOnlyDictionary<string, string>? tacticalRoles = null,
        IReadOnlyDictionary<string, CampaignCasemateDetection.Decision>? ambushers = null)
    {
        Release();
        if (Plugin.EnemyHunt != null && !Plugin.EnemyHunt.Value)
        {
            Status = "AI: native map orders (team AI disabled)";
            return;
        }
        groupRoles = tacticalRoles == null
            ? null
            : new Dictionary<string, string>(tacticalRoles, StringComparer.Ordinal);
        ambusherDesigns = ambushers == null
            ? null
            : new Dictionary<string, CampaignCasemateDetection.Decision>(ambushers, StringComparer.Ordinal);
        expectedFriendlies = CustomBattleHostSpike.CampaignFriendlyCount;
        expectedEnemies = CustomBattleHostSpike.CampaignEnemyCount;
        combatSeed = planHash;
        combatEraIndex = eraIndex;
        var measured = prices?.Where(p => p.Price > 0 && !string.IsNullOrWhiteSpace(p.Name)).ToArray()
            ?? Array.Empty<(string Name, decimal Price)>();
        eraPrices = measured.Select(p => p.Price).ToArray();
        foreach (var group in measured.GroupBy(p => p.Name, StringComparer.Ordinal))
            if (group.Select(p => p.Price).Distinct().Count() == 1) designPrices[group.Key] = group.First().Price;
        prepared = true;
        Status = "AI hunt: waiting for native team spawns";
        Plugin.Logger.LogInfo($"Expedition team AI: armed on {battle.MapName}; visible-target selection for both teams, "
            + $"both teams rally toward the opposing measured spawn; group roles={groupRoles?.Count ?? 0}; "
            + "native player command receivers take priority.");
    }

    internal static void Release()
    {
        var wasPrepared = prepared;
        ResetEncounter();
        designPrices.Clear(); eraPrices = Array.Empty<decimal>(); combatSeed = string.Empty;
        groupRoles = null;
        ambusherDesigns = null;
        CampaignBossTactics.Release();
        prepared = false;
        Status = "AI: native map orders";
        if (wasPrepared) Plugin.Logger.LogInfo("Expedition team AI: released; native ordering restored.");
    }

    internal static void Tick()
    {
        if (!prepared || faulted || !CampaignNativeFlow.OwnsSession) return;
        if (Plugin.EnemyHunt != null && !Plugin.EnemyHunt.Value) return;
        var now = Time.time;
        if (now < nextScan) return;
        nextScan = now + ScanInterval;
        try
        {
            var target = ResolveMode();
            if (target == null) { ResetEncounter(); nextScan = now + 1f; return; }
            if (target.gameState != DeathmatchGameMode.ModeState.Running) return;
            FindPlayer(target, out var friendlyTeam);
            if (!CampaignBattleArea.Ready)
            {
                PauseForNative("native battle bounds unavailable");
                return;
            }
            SyncUnits(target, friendlyTeam, now);
            if (!hasFix)
            {
                if (spawnCaptureAt < 0) spawnCaptureAt = now;
                if (!spawnsAtStart.Freeze(expectedFriendlies, expectedEnemies, now - spawnCaptureAt >= 2f))
                {
                    Status = "AI rally: measuring actual team spawns";
                    return;
                }
                var friendlyOrigin = Native(spawnsAtStart.Friendly, spawnReferenceHeight);
                var enemyOrigin = Native(spawnsAtStart.Enemy, spawnReferenceHeight);
                friendlyMemory.Origin = friendlyOrigin;
                friendlyMemory.LastKnown = enemyOrigin;
                enemyMemory.Origin = enemyOrigin;
                enemyMemory.LastKnown = friendlyOrigin;
                hasFix = true;
                Plugin.Logger.LogInfo($"Expedition team AI rally: FROZEN actual spawn centers; "
                    + $"friendly={Format(friendlyOrigin)} ({spawnsAtStart.FriendlyCount}/{expectedFriendlies}); "
                    + $"enemy={Format(enemyOrigin)} ({spawnsAtStart.EnemyCount}/{expectedEnemies}); "
                    + "each team advances toward the opposing spawn until real sightings update its own memory.");
            }
            UpdateSightings(friendlyMemory, true, friendlyTeam, now);
            UpdateSightings(enemyMemory, false, friendlyTeam, now);
            ResolveBossIdentity(friendlyTeam);
            var controlledPlayer = ControlledPlayer(target, friendlyTeam);
            CampaignElevatedVision.ObservePlayer(controlledPlayer);
            Drive(friendlyTeam, controlledPlayer, now);
            if (now >= nextSummary) { nextSummary = now + 15f; LogSummary(now); }
        }
        catch (Exception ex)
        {
            // A failing optional controller must not keep rejecting map orders.
            PauseForNative(ex.GetType().Name + ": " + ex.Message);
            nextScan = now + 1f;
        }
    }

    private static void PauseForNative(string reason)
    {
        claimed.Clear();
        travelDrivers.Clear();
        faulted = true;
        foreach (var unit in units) { ReleasePreAim(unit); ReleaseFireHold(unit); unit.Yielded = true; yieldedTanks.Add(unit.Tank.Pointer); }
        Status = "AI: native fallback (" + reason + ")";
        Plugin.Logger.LogWarning("Expedition team AI paused; native orders allowed: " + reason);
    }

    internal static bool Controls(CommanderAI? commander)
    {
        if (!prepared || !CampaignNativeFlow.OwnsSession || commander == null) return false;
        if (Plugin.EnemyHunt != null && !Plugin.EnemyHunt.Value) return false;
        try
        {
            if (mode == null || mode.gameState != DeathmatchGameMode.ModeState.Running) return false;
            if (commander.WasCollected) return false;
            if (!claimed.Contains(commander.Pointer)) return false;
            var unit = units.FirstOrDefault(item => item.Commander.Pointer == commander.Pointer);
            // Recheck the player handoff at the hook, not just at the 4 Hz scan.
            return unit != null && !unit.Yielded && !unit.Tank.WasCollected
                && unit.Tank.ControlType != ControlType.Player;
        }
        catch { return false; }
    }

    internal static VehicleBehaviour? VisionTank(CommanderAI commander)
    {
        if (mode == null || mode.gameState != DeathmatchGameMode.ModeState.Running || !Controls(commander)) return null;
        return units.FirstOrDefault(unit => unit.Commander.Pointer == commander.Pointer)?.Tank;
    }

    internal static bool Allows(Order? order)
    {
        try { return order != null && issuing != IntPtr.Zero && order.Pointer == issuing; }
        catch { return false; }
    }

    internal static bool ExternalOrder(CommanderAI commander, Order? order)
    {
        var unit = units.FirstOrDefault(item => item.Commander.Pointer == commander.Pointer);
        if (unit == null || !Controls(commander) || Allows(order)) return true;
        var kind = order?.GetIl2CppType()?.Name ?? "Order";
        if (unit.Friendly && manualReceivers.Contains(commander.Pointer))
        {
            Yield(unit, "native player command " + kind);
            return true;
        }
        blockedOrders++;
        if (blockedLogged.Add(commander.Pointer + "|" + kind))
            Plugin.Logger.LogInfo($"Expedition team AI: suppressed map {kind} for "
                + $"{(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1}.");
        return false;
    }

    // Verified native player-command ingress: single/team command selection
    // supplies the actual receivers before constructing/applying the order.
    // Observe only reference arguments; never patch the async value-type state.
    // External contract: AI Orders (>=0.4.1) calls this by reflection (static, name and
    // parameter types). Do not rename or change the signature without telling AI Orders.
    internal static void PlayerCommand(IVehicleBehaviour? sender,
        Il2CppSystem.Collections.Generic.List<IOrderReceiver>? receivers)
    {
        if (!prepared || faulted || !CampaignNativeFlow.OwnsSession || receivers == null) return;
        var player = sender?.TryCast<VehicleBehaviour>();
        if (player == null || player.ControlType != ControlType.Player || !IsUsable(player)) return;
        var battle = ResolveMode();
        if (battle?.gameState != DeathmatchGameMode.ModeState.Running || battle.vehicleSpawns == null) return;
        var count = Math.Min(receivers.Count, 64);
        for (var i = 0; i < count; i++)
        {
            var commander = receivers[i]?.TryCast<CommanderAI>();
            if (commander == null) continue;
            var friendly = false;
            for (var spawn = 0; spawn < battle.vehicleSpawns.Count; spawn++)
            {
                var tank = TankAt(battle.vehicleSpawns, spawn);
                if (tank != null && tank.ID.TeamID.Equals(player.ID.TeamID)
                    && tank.OrderReciever?.Pointer == commander.Pointer) { friendly = true; break; }
            }
            if (!friendly) continue;
            // Record before the first 4 Hz claim as well, so an early command
            // cannot be replaced by our later opening rally assignment.
            manualReceivers.Add(commander.Pointer);
            var unit = units.FirstOrDefault(item => item.Friendly && item.Commander.Pointer == commander.Pointer);
            if (unit != null && !unit.Yielded) Yield(unit, "native player command selection");
        }
    }

    private static void Yield(Unit unit, string reason)
    {
        ReleasePreAim(unit);
        ReleaseFireHold(unit);
        unit.Yielded = true;
        claimed.Remove(unit.Commander.Pointer);
        yieldedTanks.Add(unit.Tank.Pointer);
        Plugin.Logger.LogInfo($"Expedition team AI: ally slot {unit.Slot + 1} yielded to {reason}; "
            + "automatic rally will not replace its orders this battle.");
    }

    private static DeathmatchGameMode? ResolveMode()
    {
        try { if (mode != null && !mode.WasCollected && mode.gameObject.scene.isLoaded) return mode; }
        catch { }
        mode = null;
        if (Time.time < nextModeSearch) return null;
        nextModeSearch = Time.time + 1f;
        try
        {
            mode = UObject.FindObjectOfType<DeathmatchGameMode>();
            if (mode != null && (mode.WasCollected || !mode.gameObject.scene.isLoaded)) mode = null;
        }
        catch { mode = null; }
        return mode;
    }

    private static VehicleBehaviour? FindPlayer(DeathmatchGameMode target, out TeamID friendlyTeam)
    {
        friendlyTeam = target.playerTeamID;
        var spawns = target.vehicleSpawns;
        if (spawns == null) return null;
        for (var i = 0; i < spawns.Count; i++)
        {
            var tank = TankAt(spawns, i);
            if (!IsUsable(tank) || tank!.ControlType != ControlType.Player) continue;
            friendlyTeam = tank.ID.TeamID;
            return tank;
        }
        return null;
    }

    private static VehicleBehaviour? ControlledPlayer(DeathmatchGameMode target, TeamID friendlyTeam)
    {
        try
        {
            // Follow native handoffs too; a stale Player flag on a previous
            // hull must not keep the boss chasing that hull after a switch.
            var player = target.vehicleControlState?.TryCast<VehicleControlPlayerState>()?.vehicleController
                ?.ControlledVehicle?.TryCast<VehicleBehaviour>();
            return IsUsable(player) && player!.ControlType == ControlType.Player
                && player.ID.TeamID.Equals(friendlyTeam) ? player : null;
        }
        catch { return null; }
    }

    private static void ObserveEnemySpawn(VehicleBehaviour tank, string? name)
    {
        var existing = enemySpawnOrigins.FirstOrDefault(origin => origin.Pointer == tank.Pointer);
        if (existing == null)
        {
            enemySpawnOrigins.Add(new EnemySpawnOrigin
            {
                Pointer = tank.Pointer,
                Position = tank.Position,
                Name = string.IsNullOrWhiteSpace(name) ? "unknown" : name!
            });
        }
        else if (existing.Name == "unknown" && !string.IsNullOrWhiteSpace(name))
            existing.Name = name!;
    }

    private static void SampleSpawnSettling(Unit unit, float now)
    {
        if (unit.Yielded) return;
        var position = unit.Tank.Position;
        var up = unit.Tank.transform.up;
        var speed = 0f;
        try
        {
            var body = unit.Commander.driver?.body;
            if (body != null)
            {
                position = body.position;
                up = body.rotation * Vector3.up;
                speed = body.linearVelocity.magnitude;
            }
        }
        catch { }
        if (!CampaignSpawnSettleSampler.Observe(now, unit.Tank.Pointer.ToInt64(),
                unit.DesignName,
                new Numerics.Vector3(position.x, position.y, position.z),
                new Numerics.Vector3(up.x, up.y, up.z),
                speed, out var sample))
            return;
        Plugin.Logger.LogInfo($"Expedition spawn settle sample: {(unit.Friendly ? "ally" : "enemy")} "
            + $"slot {unit.Slot + 1}; design='{sample.Design}'; age={sample.AgeSeconds:N2} s; "
            + $"pos=({sample.Position.X:N0}, {sample.Position.Y:N0}, {sample.Position.Z:N0}); "
            + $"upDot={sample.UpDot:N3}; speed={sample.Speed:N1} m/s.");
    }

    private static void AssignGroupRole(Unit unit, bool allowAuthored)
    {
        if (!unit.Friendly && (!allowAuthored || roleMappingRejected))
        {
            unit.GroupRole = CampaignGroupDoctrine.Center;
            unit.LaneSide = CampaignGroupDoctrine.Side(unit.Slot);
            return;
        }
        string? authored = null;
        if (allowAuthored && !unit.Friendly && groupRoles != null
            && !string.IsNullOrWhiteSpace(unit.DesignName)
            && groupRoles.TryGetValue(unit.DesignName, out var mapped))
            authored = mapped;
        unit.GroupRole = CampaignGroupDoctrine.Normalize(authored
            ?? CampaignGroupDoctrine.DefaultRole(unit.Friendly, unit.Slot,
                unit.CombatProfile.Role == CampaignCombatPolicy.Role.FireSupport));
        unit.LaneSide = CampaignGroupDoctrine.Side(unit.Slot);
    }

    private static void SyncUnits(DeathmatchGameMode target, TeamID friendlyTeam, float now)
    {
        travelDrivers.Clear();
        for (var i = units.Count - 1; i >= 0; i--)
        {
            var unit = units[i];
            if (!IsUsable(unit.Tank))
            {
                ReleasePreAim(unit);
                claimed.Remove(unit.Commander.Pointer);
                units.RemoveAt(i);
                continue;
            }
            if (unit.Tank.ControlType == ControlType.Player && unit.Friendly && !unit.Yielded)
                Yield(unit, "player control handoff");
            if (unit.Tank.ControlType != ControlType.Player) continue;
            claimed.Remove(unit.Commander.Pointer);
            units.RemoveAt(i);
        }
        liveTanks.Clear();
        var spawns = target.vehicleSpawns;
        if (spawns == null) return;
        for (var i = 0; i < spawns.Count; i++)
        {
            var tank = TankAt(spawns, i);
            if (!IsUsable(tank)) continue;
            liveTanks.Add(tank!);
            var spawnFriendly = tank!.ID.TeamID.Equals(friendlyTeam);
            if (!spawnFriendly)
            {
                string? spawnName = null;
                try { spawnName = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name; } catch { }
                ObserveEnemySpawn(tank, spawnName);
            }
            if (!spawnsAtStart.Frozen)
            {
                if (spawnsAtStart.FriendlyCount + spawnsAtStart.EnemyCount == 0)
                    spawnReferenceHeight = tank!.Position.y;
                spawnsAtStart.Observe(tank!.Pointer.ToInt64(), tank.ID.TeamID.Equals(friendlyTeam), XZ(tank.Position));
            }
            if (tank!.ControlType == ControlType.Player || yieldedTanks.Contains(tank.Pointer)
                || units.Any(item => item.Tank.Pointer == tank.Pointer)) continue;
            var commander = tank.OrderReciever?.TryCast<CommanderAI>();
            if (commander == null) continue;
            if (manualReceivers.Contains(commander.Pointer)) { yieldedTanks.Add(tank.Pointer); continue; }
            var friendly = tank.ID.TeamID.Equals(friendlyTeam);
            var unit = new Unit { Tank = tank, Commander = commander, Friendly = friendly,
                Slot = friendly ? friendlySlots++ : enemySlots++, Spawn = tank.Position,
                SpawnedAt = now, LastContactAt = now, NextTryAt = now };
            string? name = null;
            try { name = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name; } catch { }
            unit.DesignName = name ?? "unknown";
            unit.FlankSide = CampaignGroupDoctrine.Side(unit.Slot);
            if (!friendly) CampaignTestPool.Ledger.Claimed(unit.DesignName);
            var isAmbusher = false;
            var ambusherSource = "none";
            if (!friendly && ambusherDesigns != null
                && ambusherDesigns.TryGetValue(unit.DesignName, out var ambusher))
            {
                isAmbusher = ambusher.IsAmbusher;
                ambusherSource = ambusher.SourceName;
            }
            unit.Ambusher = isAmbusher;
            unit.AmbusherSource = ambusherSource;
            decimal? price = !friendly && name != null && designPrices.TryGetValue(name, out var measured) ? measured : null;
            unit.CombatProfile = CampaignCombatPolicy.Create(combatSeed, unit.Slot, friendly, price, eraPrices,
                eraIndex: combatEraIndex, ambusher: isAmbusher);
            // An ambusher's 45 s clock starts at spawn, not at its first sighting.
            if (isAmbusher) unit.LastOwnSightAt = now;
            AssignGroupRole(unit, spawnMappingVerified && !roleMappingRejected);
            units.Add(unit);
            claimed.Add(commander.Pointer);
            Plugin.Logger.LogInfo($"Expedition team AI: {(friendly ? "ally" : "enemy")} slot {unit.Slot + 1} "
                + $"claimed at {Format(unit.Spawn)}; nativeTeam={(byte)tank.ID.TeamID}; "
                + $"insideBounds={CampaignBattleArea.Contains(tank.Position, 0f)}.");
            Plugin.Logger.LogInfo($"Expedition combat profile: {(friendly ? "ally" : "enemy")} slot {unit.Slot + 1}; "
                + $"role={unit.CombatProfile.Role}; stopRange={unit.CombatProfile.StopRange:N0} m; "
                + $"reaction={unit.CombatProfile.ReactionSeconds:N2} s; nativeCost={price?.ToString() ?? "unmeasured"}"
                + (isAmbusher ? $"; ambusherSource={ambusherSource}" : string.Empty) + ".");
        }
        foreach (var unit in units)
            if (!unit.Yielded && unit.Commander.driver is { } driver)
                travelDrivers[driver.Pointer] = unit;
        foreach (var unit in units) SampleSpawnSettling(unit, now);
        if (!spawnMappingVerified && expectedEnemies > 0
            && (enemySpawnOrigins.Count >= expectedEnemies || spawnsAtStart.Frozen)
            && enemySpawnOrigins.All(origin =>
                !string.IsNullOrWhiteSpace(origin.Name)
                && !string.Equals(origin.Name, "unknown", StringComparison.OrdinalIgnoreCase)))
        {
            spawnMappingVerified = true;
            var observed = enemySpawnOrigins
                .Select(origin => (origin.Position, origin.Name))
                .ToArray();
            if (!CampaignSpawnTrial.VerifySpawnedEnemies(observed))
            {
                roleMappingRejected = true;
                groupRoles = null;
                foreach (var unit in units.Where(item => !item.Friendly))
                    unit.GroupRole = CampaignGroupDoctrine.Center;
                Status = "AI rally: native spawn/design order mismatch; role lanes neutral";
                Plugin.Logger.LogWarning(
                    "Expedition group doctrine: planned marker/design order was not confirmed by the actual "
                    + "native spawn sequence; all enemy role lanes are neutral Center and no slot/role claim is retained.");
            }
            else
            {
                foreach (var unit in units.Where(item => !item.Friendly))
                    AssignGroupRole(unit, allowAuthored: true);
                Plugin.Logger.LogInfo(
                    "Expedition group doctrine: actual spawn layout verified; authored enemy role lanes activated.");
            }
        }
    }

    internal static bool ForwardTravelOnly(DriverAI driver, DriverUpdateFlags flags)
    {
        // The override lives only inside this native Step. No shared settings or
        // serialized blueprints are changed. Native forced reverse can still free
        // a blocked hull, and an explicit Battery withdrawal remains native.
        if (!prepared || faulted || !CampaignNativeFlow.OwnsSession
            || (Plugin.EnemyHunt != null && !Plugin.EnemyHunt.Value)
            || (flags & DriverUpdateFlags.ForceReverse) != 0
            || !travelDrivers.TryGetValue(driver.Pointer, out var unit)
            || unit.Yielded || !claimed.Contains(unit.Commander.Pointer)
            || unit.Tank.WasCollected || unit.Tank.ControlType == ControlType.Player
            || unit.Mode is not (Mode.Advance or Mode.Search or Mode.Recover)) return false;
        var move = unit.Commander.oneOffMoveOrderCts;
        var task = driver.CurrentTask?.TryCast<MoveToPositionTask>();
        if (move == null || move.IsCancellationRequested || task == null || task.ForceReverse
            || task.Finished || task.CancellationRequested) return false;
        var goal = task.Objective.Position;
        return Distance(goal, unit.LastOrderTarget) <= 1f
            && CampaignCombatPolicy.ForwardTravelOnly(Distance(unit.Tank.Position, goal));
    }

    private static void UpdateSightings(TeamMemory memory, bool friendlyObservers, TeamID friendlyTeam, float now)
    {
        // Select from actual detection buffers only. A hidden vehicle's live
        // position is not read here, even if it was the target last scan.
        candidates.Clear();
        var current = -1;
        for (var i = 0; i < liveTanks.Count; i++)
        {
            var target = liveTanks[i];
            if (target.ID.TeamID.Equals(friendlyTeam) == friendlyObservers) continue;
            var nearest = float.PositiveInfinity;
            foreach (var unit in units)
            {
                if (unit.Friendly != friendlyObservers || unit.Yielded || !Sees(unit.Commander, target.ID)) continue;
                nearest = Math.Min(nearest, Distance(unit.Tank.Position, target.Position));
            }
            if (!float.IsFinite(nearest)) continue;
            if (target.Pointer == memory.Contact) current = i;
            candidates.Add(new CampaignHuntPlanner.VisibleTarget(i, nearest, 0));
        }
        var selected = CampaignHuntPlanner.ChooseVisibleTarget(candidates, current);
        if (selected < 0) return;
        var observed = liveTanks[selected];
        memory.Contact = observed.Pointer;
        memory.LastKnown = observed.Position;
        memory.LastSeenAt = now;
        memory.Seen = true;
    }

    private static void ResolveBossIdentity(TeamID friendlyTeam)
    {
        if (!CampaignBossTactics.Enabled || bossIdentityRejected) return;
        var enemyCount = 0;
        var unknownName = false;
        var matches = new List<(VehicleBehaviour Tank, string Name)>();
        foreach (var tank in liveTanks)
        {
            if (tank.ID.TeamID.Equals(friendlyTeam)) continue;
            enemyCount++;
            string? name;
            try { name = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name; }
            catch { name = null; }
            if (string.IsNullOrWhiteSpace(name)) { unknownName = true; continue; }
            if (CampaignBossTactics.ExactEnemyName(name, true)) matches.Add((tank, name));
        }
        if (matches.Count > 1 || (bossIdentityResolved && matches.Any(match => match.Tank.Pointer != bossPointer)))
        {
            bossIdentityRejected = true;
            bossStatus = "Boss AI unavailable";
            ApplyBossRole(IntPtr.Zero, CampaignBossTactics.Character.None);
            Plugin.Logger.LogWarning("Expedition boss AI: native enemy design name is ambiguous or changed identity; "
                + "no hull receives a boss character. Expected exact name: " + CampaignBossTactics.NativeTankName);
            return;
        }
        if (!bossIdentityResolved)
        {
            // Do not select the first available matching hull while another
            // expected enemy or its native design name is still unreadable.
            if (enemyCount < expectedEnemies || unknownName) return;
            bossIdentityResolved = true;
            if (matches.Count != 1)
            {
                bossIdentityRejected = true;
                bossStatus = "Boss AI unavailable";
                Plugin.Logger.LogWarning("Expedition boss AI: no unique exact enemy design-name match; "
                    + "ordinary team AI retained. Expected: " + CampaignBossTactics.NativeTankName);
                return;
            }
            bossPointer = matches[0].Tank.Pointer;
            Plugin.Logger.LogInfo($"Expedition boss AI: exact native enemy design '{matches[0].Name}' "
                + $"matched uniquely; character={CampaignBossTactics.PreparedCharacter}. "
                + "Private live-player pursuit enabled; firing still requires native visibility.");
        }
        var character = !unknownName && matches.Count == 1
            ? CampaignBossTactics.Match(matches[0].Name, true, 1) : CampaignBossTactics.Character.None;
        ApplyBossRole(bossPointer, character);
        if (character != CampaignBossTactics.Character.None && !bossCharacterLogged)
        {
            bossCharacterLogged = true;
            Plugin.Logger.LogInfo($"Expedition boss character: {CampaignBossTactics.NativeTankName} "
                + $"-> {CampaignBossTactics.BehaviorName(character)}");
        }
        bossStatus = character == CampaignBossTactics.Character.None ? string.Empty : "Boss: " + character;
    }

    private static CampaignBossTactics.Character Behavior(Unit unit) =>
        unit.BossCharacter != CampaignBossTactics.Character.None
            ? unit.BossCharacter
            : unit.BossEscort ? unit.EscortCharacter : CampaignBossTactics.Character.None;

    private static void ApplyBossRole(IntPtr pointer, CampaignBossTactics.Character character)
    {
        foreach (var unit in units)
        {
            var desired = !unit.Friendly && unit.Tank.Pointer == pointer
                ? character : CampaignBossTactics.Character.None;
            // Veteran keeps its existing ordinary escorts; the three new
            // characters own their escorts as one tactical group.
            var escort = character is CampaignBossTactics.Character.Fortress
                    or CampaignBossTactics.Character.Hunter
                    or CampaignBossTactics.Character.Battery
                && !unit.Friendly && unit.Tank.Pointer != pointer;
            var escortCharacter = escort ? character : CampaignBossTactics.Character.None;
            if (unit.BossCharacter == desired && unit.BossEscort == escort
                && unit.EscortCharacter == escortCharacter) continue;
            unit.BossCharacter = desired;
            unit.BossEscort = escort;
            unit.EscortCharacter = escortCharacter;
            if (escort)
            {
                if (escortCharacter == CampaignBossTactics.Character.Hunter)
                    unit.GroupRole = CampaignGroupDoctrine.Flank;
                else if (escortCharacter == CampaignBossTactics.Character.Battery)
                    unit.GroupRole = CampaignGroupDoctrine.Support;
                else unit.GroupRole = CampaignGroupDoctrine.Center;
            }
            ReleaseFireHold(unit);
            unit.Reaction.Clear();
            if (desired != CampaignBossTactics.Character.None)
                unit.CombatProfile = CampaignCombatPolicy.Create(combatSeed, unit.Slot, unit.Friendly, null,
                    eraPrices, boss: true, eraIndex: combatEraIndex, ambusher: unit.Ambusher);
            // Boss identity may be gained or lost. In both directions the hull
            // may already be moving or attacking under ordinary lanes; cancel
            // both native order channels before the next scan installs policy.
            try
            {
                var attack = unit.Commander.attackOrderCts;
                if (attack != null && !attack.IsCancellationRequested) attack.Cancel();
                var move = unit.Commander.oneOffMoveOrderCts;
                if (move != null && !move.IsCancellationRequested) move.Cancel();
            }
            catch { }
            unit.Mode = Mode.None;
            unit.LastOrderAt = -999f;
            unit.Progress.Clear();
        }
    }

    private static bool TryBossRetreat(Unit unit, VehicleBehaviour visibleTarget,
        float currentDistance, float now)
    {
        var behavior = Behavior(unit);
        if (behavior != CampaignBossTactics.Character.Battery || now < unit.NextBossRetreatAt
            || now - unit.LastOrderAt < MinimumOrderSpacing || (unit.Tank.Flags & VehicleFlags.Mobile) == 0)
            return false;
        if (!CampaignBossTactics.TryBatteryRetreat(behavior, XZ(unit.Tank.Position),
                XZ(visibleTarget.Position), unit.InitialContactDistance, out var candidate)) return false;
        unit.NextBossRetreatAt = now + 8f;
        if (!TryGround(Native(candidate, unit.Tank.Position.y), out var grounded)) return false;
        // Reject obstructed short withdrawals before asking the native driver.
        // The first eight metres avoid the battery's own colliders. Endpoint
        // ground, slope and authored map bounds were already checked above.
        var delta = grounded - unit.Tank.Position;
        delta.y = 0;
        var distance = delta.magnitude;
        if (distance < 16f) return false;
        foreach (var height in new[] { 1.2f, 2.4f })
        {
            var from = unit.Tank.Position + delta.normalized * 8f + Vector3.up * height;
            if (Physics.Raycast(from, delta.normalized, out _, distance - 8f, ~0, QueryTriggerInteraction.Ignore))
                return false;
        }
        if (!IssueMove(unit, grounded, Mode.BossRetreat, now)) return false;
        EndReposition(unit);
        try { unit.Commander.FireMode = FireMode.FireAtWill; } catch { }
        unit.BossRetreatUntil = now + 24f;
        unit.NextBossRetreatAt = now + 36f;
        Plugin.Logger.LogInfo($"Expedition boss Battery: visible threat at {currentDistance:N0} m "
            + $"(initial {unit.InitialContactDistance:N0} m); "
            + $"safe bounded native withdrawal {Format(unit.Tank.Position)} -> {Format(grounded)}.");
        return true;
    }

    private static void Drive(TeamID friendlyTeam, VehicleBehaviour? player, float now)
    {
        var attacking = 0;
        var rallying = 0;
        foreach (var unit in units)
        {
            if (unit.Yielded || !Controls(unit.Commander) || now < unit.NextTryAt) continue;
            var ambusher = unit.Ambusher;
            if (unit.Mode == Mode.BossRetreat)
            {
                // The retreat endpoint was qualified from a real sighting.
                // Finish that fixed leg without sampling a hidden target.
                if (now < unit.BossRetreatUntil && Distance(unit.Tank.Position, unit.LastOrderTarget) > 10f)
                {
                    if (!MoveActive(unit) && now - unit.LastOrderAt >= CampaignHuntPlanner.MoveInterval)
                        IssueMove(unit, unit.LastOrderTarget, Mode.BossRetreat, now);
                    rallying++;
                    continue;
                }
                unit.Mode = Mode.None;
                unit.Progress.Clear();
            }
            var behavior = Behavior(unit);
            var trackingPlayer = CampaignBossTactics.TryPlayerPursuit(unit.BossCharacter, !unit.Friendly,
                player != null, player == null ? default : XZ(player.Position), out var playerGoal);
            // Only the identified boss prioritizes the actual player. Hidden
            // position knowledge never enters visibleTargets or TeamMemory.
            var target = trackingPlayer && Sees(unit.Commander, player!.ID)
                ? player : ChooseTarget(unit, friendlyTeam, now);
            if (unit.Mode != Mode.Attack)
            {
                // Sample only an actual sighting, a frozen/team last-known fix,
                // or the explicitly omniscient boss's private player objective.
                // A regular lost contact is never followed through a Transform.
                var known = target != null ? target.Position
                    : trackingPlayer ? Native(playerGoal, unit.Tank.Position.y)
                    : (unit.Friendly ? friendlyMemory : enemyMemory).LastKnown;
                PreAimAtKnownPosition(unit, known);
            }
            if (target != null)
            {
                var sustainedSight = unit.LastOwnSightAt > -900f
                    && now - unit.LastOwnSightAt < 1.5f;
                unit.LastOwnSightAt = now;
                unit.LastContactAt = now;
                unit.ReactionTarget = target.Pointer;
                if (unit.DoctrineContactTarget != target.Pointer)
                {
                    unit.DoctrineContactTarget = target.Pointer;
                    // Keep the team contact epoch stable while visibility is
                    // continuous. Reactive target choice must not re-anchor the
                    // alternating phase every scan.
                    if (!sustainedSight || unit.DoctrineContactSince < 0f)
                    {
                        unit.DoctrineContactSince = now;
                        unit.NextDoctrineLegAt = now;
                        unit.DoctrineLegUntil = -999f;
                    }
                }
                if (ambusher && unit.DoctrineLegUntil > 0f) EndDoctrineLeg(unit, now);
                if (!ambusher && TryContinueDoctrineLeg(unit, now)) { rallying++; continue; }
                if (!unit.Reaction.Ready(target.Pointer.ToInt64(), now, unit.CombatProfile.ReactionSeconds))
                {
                    // Casemates need the hull pointed at the target; start that
                    // turn immediately, while the fixed one-second timer runs.
                    if (ambusher)
                    {
                        if (unit.Mode == Mode.Attack)
                        {
                            EndCombatMovement(unit);
                            unit.Mode = Mode.None;
                            unit.LastOrderAt = -999f;
                            unit.AttackTarget = IntPtr.Zero;
                            unit.Progress.Clear();
                        }
                        FaceDuringReaction(unit, target);
                    }
                    rallying++;
                    continue;
                }
                unit.NextAmbusherStepAt = 0f;
                if (unit.Repositioning && unit.RepositionTarget != target.Pointer)
                    EndReposition(unit);
                if (unit.Repositioning)
                {
                    // The firing line for this target stays denied until the
                    // displacement finishes; then the normal stop/fire decision
                    // runs again with a fresh tracker.
                    unit.LastOwnSightAt = now;
                    unit.ReactionTarget = target.Pointer;
                    var arrived = Distance(unit.Tank.Position, unit.LastOrderTarget)
                        <= CampaignCombatPolicy.RepositionArriveMeters;
                    var traveled = Distance(unit.Tank.Position, unit.RepositionStart)
                        >= CampaignCombatPolicy.RepositionMeters;
                    if (arrived || traveled || now >= unit.RepositionUntil)
                    {
                        EndReposition(unit);
                    }
                    else
                    {
                        if (!MoveActive(unit) && now - unit.LastOrderAt >= CampaignHuntPlanner.MoveInterval)
                            IssueMove(unit, unit.LastOrderTarget, Mode.Advance, now);
                        rallying++;
                        continue;
                    }
                }
                var distance = Distance(unit.Tank.Position, target.Position);
                unit.ContactDistance = distance;
                if (behavior == CampaignBossTactics.Character.Battery
                    && unit.InitialContactDistance <= 0f)
                    unit.InitialContactDistance = distance;
                if (TryBossRetreat(unit, target, distance, now)) { rallying++; continue; }
                var clearShot = HasFiringLine(unit, target, now);
                if (!clearShot)
                {
                    // An ambusher or a dug-in Fortress does not abandon its
                    // prepared position for a blocked line; it keeps the hull
                    // on the visible contact and lets native visibility decide
                    // when firing is possible.
                    if (ambusher || behavior == CampaignBossTactics.Character.Fortress)
                    {
                        if (unit.Mode == Mode.Attack)
                        {
                            EndCombatMovement(unit);
                            unit.Mode = Mode.None;
                            unit.LastOrderAt = -999f;
                            unit.AttackTarget = IntPtr.Zero;
                            unit.Progress.Clear();
                        }
                        FaceDuringReaction(unit, target);
                        rallying++;
                        continue;
                    }
                    // Native detection may retain a contact behind a ridge or
                    // another hull. Do not brake indefinitely for that contact.
                    // This position is sampled only after native own detection.
                    var approach = unit.BossCharacter == CampaignBossTactics.Character.None
                        ? CampaignGroupDoctrine.ApproachWaypoint(XZ(target.Position), XZ(unit.Tank.Position),
                            unit.GroupRole, unit.LaneSide, unit.Slot)
                        : CampaignHuntPlanner.ApproachWaypoint(XZ(target.Position),
                            XZ(unit.Tank.Position), unit.Slot, 18f);
                    if (TryGround(Native(approach, target.Position.y), out var firingApproach))
                        Move(unit, firingApproach, Mode.Advance, now);
                    else if (unit.Mode == Mode.Attack)
                    {
                        EndCombatMovement(unit);
                        unit.Mode = Mode.None;
                        unit.LastOrderAt = -999f;
                    }
                    rallying++;
                    continue;
                }
                // 0.3.3 seek and destroy: a clear shot stops and fires; bounded
                // doctrine legs no longer move a hull away from a clear line.
                var stop = ambusher
                    ? true
                    : behavior != CampaignBossTactics.Character.None
                    ? CampaignBossTactics.StopToFire(behavior, distance,
                        unit.Mode == Mode.Attack && unit.StopToFire)
                    : CampaignCombatPolicy.HoldForShot(unit.CombatProfile, distance,
                        unit.Mode == Mode.Attack && unit.StopToFire, clearShot);
                var changed = unit.Mode != Mode.Attack || unit.AttackTarget != target.Pointer;
                if (changed || CampaignHuntPlanner.AttackDue(now, unit.LastOrderAt, AttackActive(unit), changed,
                        stop != unit.StopToFire)) IssueAttack(unit, target, stop, now);
                if (unit.Mode == Mode.Attack && unit.StopToFire)
                {
                    EnsureFireHold(unit, target);
                    ObserveFireProgress(unit, target, now);
                    if (unit.FireTracker.Stalled && now >= unit.NextStallActionAt)
                    {
                        if (ambusher)
                        {
                            // Ambushers keep position by decision; they only
                            // receive the stall line (once per stall episode).
                            if (!unit.FireStallLogged)
                            {
                                unit.FireStallLogged = true;
                                LogFireStall(unit, null, holding: true);
                            }
                        }
                        else if (TryReposition(unit, target, now))
                        {
                            rallying++;
                            continue;
                        }
                        else unit.NextStallActionAt = now + 2f;
                    }
                }
                attacking++;
                continue;
            }
            // Brief detector flicker must not repeatedly cancel a firing task.
            // No new attack order or hidden target position is used in this grace.
            if (unit.Mode == Mode.Attack && now - unit.LastOwnSightAt < 1.5f) continue;
            if (unit.Repositioning) EndReposition(unit);
            unit.ReactionTarget = IntPtr.Zero;
            unit.DoctrineContactTarget = IntPtr.Zero;
            unit.DoctrineContactSince = -999f;
            unit.DoctrineLegUntil = -999f;
            if (unit.Mode == Mode.Attack)
            {
                EndCombatMovement(unit);
                unit.Mode = Mode.None;
                unit.LastOrderAt = -999f;
                unit.AttackTarget = IntPtr.Zero;
                unit.Progress.Clear();
            }
            if (behavior == CampaignBossTactics.Character.Fortress)
            {
                // Hold until the one permitted 90 s slow step is over.
                if (unit.Mode == Mode.Advance && unit.FortressSlowAdvanceUsed
                    && now < unit.FortressSlowUntil)
                {
                    rallying++;
                    continue;
                }
                if (unit.Mode is Mode.Advance or Mode.Search or Mode.Recover) StopBossHold(unit);
                if (!unit.FortressSlowAdvanceUsed
                    && CampaignBossTactics.MaySlowAdvance(behavior, now, unit.LastContactAt))
                {
                    var bossMemory = unit.Friendly ? friendlyMemory : enemyMemory;
                    if (CampaignBossTactics.TrySlowAdvancePoint(XZ(unit.Tank.Position),
                            XZ(bossMemory.LastKnown), out var candidate)
                        && TryGround(Native(candidate, bossMemory.LastKnown.y), out var grounded)
                        && IssueMove(unit, grounded, Mode.Advance, now))
                    {
                        unit.FortressSlowAdvanceUsed = true;
                        unit.FortressSlowUntil = now + 20f;
                        Plugin.Logger.LogInfo("Expedition boss Fortress: 90 s without contact; "
                            + $"one slow step to the last known position {Format(grounded)}.");
                    }
                }
                rallying++;
                continue;
            }
            if (ambusher)
            {
                ReleaseReactionFacing(unit);
                if (CampaignAmbusherPlacement.HoldsPosition(true, false, now, unit.LastOwnSightAt))
                {
                    if (unit.Mode is Mode.Advance or Mode.Search or Mode.Recover) StopAmbusher(unit);
                    rallying++;
                    continue;
                }
            }
            rallying++;
            if (trackingPlayer)
            {
                // Keep the normal order throttle, recovery and native
                // pathfinder. Ground/bounds checks also apply to this boss.
                if (unit.Mode == Mode.Search) { unit.Mode = Mode.None; unit.Progress.Clear(); }
                // Sample beside the target hull, otherwise its own collider
                // would obscure the terrain ray directly below its position.
                for (var approachSlot = 0; approachSlot < 3; approachSlot++)
                {
                    var approach = CampaignHuntPlanner.ApproachWaypoint(playerGoal, XZ(unit.Tank.Position), approachSlot, 12f);
                    if (!TryGround(Native(approach, player!.Position.y), out var pursuit)) continue;
                    Move(unit, pursuit, Mode.Advance, now);
                    break;
                }
                continue;
            }
            var memory = unit.Friendly ? friendlyMemory : enemyMemory;
            if (unit.Mode == Mode.Search && Distance(unit.SearchAnchor, memory.LastKnown) > 30f)
                unit.Mode = Mode.None;
            if (unit.Mode == Mode.Search)
            {
                if (now >= unit.SearchUntil || Distance(unit.Tank.Position, unit.SearchTarget) <= SearchArriveRadius)
                    StartSearch(unit, memory, now);
                else Move(unit, unit.SearchTarget, Mode.Search, now);
                continue;
            }
            var goal = Vector3.zero;
            var safe = false;
            unit.ContactDistance = Distance(unit.Tank.Position, memory.LastKnown);
            foreach (var spacing in new[] { 24f, 12f, 6f })
            {
                var approach = CampaignGroupDoctrine.ApproachWaypoint(XZ(memory.LastKnown), XZ(memory.Origin),
                    unit.GroupRole, unit.LaneSide, unit.Slot, spacing);
                if (!TryGround(Native(approach, memory.LastKnown.y), out goal)) continue;
                safe = true;
                break;
            }
            if (!safe)
            {
                if (now >= unit.NextUnsafeGoalLog)
                {
                    unit.NextUnsafeGoalLog = now + 15f;
                    Plugin.Logger.LogInfo($"Expedition team AI: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1} "
                        + $"has no safe in-bounds rally endpoint; pos={Format(unit.Tank.Position)}; "
                        + $"contact={Format(memory.LastKnown)}; {NativeTask(unit)}.");
                }
                continue;
            }
            if (Distance(unit.Tank.Position, goal) <= SearchArriveRadius) StartSearch(unit, memory, now);
            else if (ambusher)
            {
                // No sight for 45 s: creep in bounded 40 m steps with a pause,
                // so the ambush cannot deadlock the battle.
                if (now < unit.NextAmbusherStepAt) continue;
                var delta = goal - unit.Tank.Position;
                delta.y = 0f;
                var stepGoal = goal;
                if (delta.magnitude > CampaignAmbusherPlacement.SlowStepMeters)
                    stepGoal = unit.Tank.Position + delta.normalized * CampaignAmbusherPlacement.SlowStepMeters;
                if (!TryGround(Native(XZ(stepGoal), stepGoal.y), out var groundedStep)) groundedStep = goal;
                if (Move(unit, groundedStep, Mode.Advance, now))
                    unit.NextAmbusherStepAt = now + CampaignAmbusherPlacement.SlowStepSeconds;
            }
            else Move(unit, goal, Mode.Advance, now);
        }
        var enemies = units.Count(unit => !unit.Friendly && !unit.Yielded);
        var manual = yieldedTanks.Count;
        Status = $"AI rally: {enemies} enemies · {attacking} engaging · {rallying} advancing/searching · "
            + $"{manual} native/manual · orders {totalOrders}"
            + (bossStatus.Length == 0 ? string.Empty : " · " + bossStatus);
    }

    private static VehicleBehaviour? ChooseTarget(Unit unit, TeamID friendlyTeam, float now)
    {
        targetCandidates.Clear();
        for (var i = 0; i < liveTanks.Count; i++)
        {
            var target = liveTanks[i];
            var targetFriendly = target.ID.TeamID.Equals(friendlyTeam);
            if (targetFriendly == unit.Friendly || !Sees(unit.Commander, target.ID)) continue;
            var assigned = units.Count(other => other != unit && other.Friendly == unit.Friendly
                && !other.Yielded && other.AttackTarget == target.Pointer && other.Mode == Mode.Attack);
            targetCandidates.Add(new CampaignTargetCommitment.Candidate(target.Pointer.ToInt64(),
                Distance(unit.Tank.Position, target.Position), assigned, HasFiringLine(unit, target, now)));
        }
        var selected = unit.TargetChoice.Choose(targetCandidates, now);
        if (selected == 0) return null;
        foreach (var target in liveTanks)
            if (target.Pointer.ToInt64() == selected) return target;
        return null;
    }

    private static void StartSearch(Unit unit, TeamMemory memory, float now)
    {
        if (now - unit.LastOrderAt < MinimumOrderSpacing) return;
        var step = CampaignHuntPlanner.NextSearchStep(unit.SearchStep);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var candidate = CampaignHuntPlanner.SearchWaypoint(XZ(memory.LastKnown), XZ(memory.Origin), unit.Slot, step + attempt);
            if (!TryGround(Native(candidate, memory.LastKnown.y), out var grounded)) continue;
            if (!Move(unit, grounded, Mode.Search, now)) continue;
            unit.SearchStep = step + attempt;
            unit.SearchTarget = grounded;
            unit.SearchAnchor = memory.LastKnown;
            unit.SearchUntil = now + CampaignHuntPlanner.SearchStepSeconds;
            return;
        }
    }

    private static bool DoctrineAllows(Unit unit, Mode desiredMode, float now)
    {
        // Boss identities and their escorts retain their own prepared tactics.
        if (Behavior(unit) != CampaignBossTactics.Character.None) return true;
        // Ambushers already gate every move behind the 45 s hold and slow-step
        // cadence; alternating bounds would immediately break that position.
        if (unit.Ambusher) return true;
        var coverage = TeamCoverage(unit, now);
        if (coverage.TeamSize <= 1) return true;
        var visible = unit.LastOwnSightAt > -900f && now - unit.LastOwnSightAt < 1.5f;
        var contactAt = unit.DoctrineContactSince >= 0f
            ? unit.DoctrineContactSince : unit.LastOwnSightAt;
        var allowed = CampaignGroupDoctrine.CanStartLeg(unit.GroupRole, unit.Slot,
            coverage.TeamSize, coverage.LeadSlot, visible, coverage.Cover, now,
            contactAt, unit.ContactDistance);
        if (!allowed && now >= unit.NextDoctrineHoldLog)
        {
            unit.NextDoctrineHoldLog = now + 12f;
            Plugin.Logger.LogInfo($"Expedition group doctrine: {(unit.Friendly ? "ally" : "enemy")} "
                + $"slot {unit.Slot + 1} holds role={unit.GroupRole} for the next bound; "
                + $"visibleContact={visible}; stationaryCover={coverage.Cover}; team={coverage.TeamSize}; "
                + $"leadSlot={coverage.LeadSlot + 1}; contactRange={unit.ContactDistance:N0} m; "
                + $"requestedMode={desiredMode}.");
        }
        return allowed;
    }

    private static (int TeamSize, int LeadSlot, bool Cover) TeamCoverage(Unit unit, float now)
    {
        var team = units.Where(other => other.Friendly == unit.Friendly
            && !other.Yielded && IsUsable(other.Tank)).ToArray();
        if (team.Length == 0) return (0, unit.Slot, false);
        var lead = CampaignGroupDoctrine.SelectLeadSlot(
            team.Select(other => (other.Slot, Mobile(other), IsUsable(other.Tank))).ToArray(),
            team.Min(other => other.Slot));
        var cover = team.Any(other => other != unit && IsStationaryCover(other, now));
        return (team.Length, lead, cover);
    }

    private static bool IsStationaryCover(Unit other, float now)
    {
        if (other.Yielded || other.Mode != Mode.Attack || !other.StopToFire
            || !AttackActive(other) || other.AttackTarget == IntPtr.Zero
            || other.ShotTarget != other.AttackTarget
            || !other.ClearShot || now - other.LastOwnSightAt >= 1.5f)
            return false;
        return CampaignGroupDoctrine.IsStationarySpeed(HorizontalSpeed(other));
    }

    private static bool TryContinueDoctrineLeg(Unit unit, float now)
    {
        if (unit.DoctrineLegUntil <= 0f) return false;
        var current = Distance(unit.Tank.Position, unit.DoctrineLegTarget);
        var stalled = unit.Progress.Stalled(XZ(unit.DoctrineLegTarget), XZ(unit.Tank.Position), now);
        if (unit.Mode != Mode.Advance
            || !CampaignGroupDoctrine.KeepEngagementLeg(now, unit.DoctrineLegUntil,
                unit.DoctrineLegStarted, unit.DoctrineLegStartDistance, current, stalled))
        {
            EndDoctrineLeg(unit, now);
            return false;
        }
        if (!MoveActive(unit) && now - unit.LastOrderAt >= CampaignHuntPlanner.MoveInterval)
            return IssueMove(unit, unit.DoctrineLegTarget, Mode.Advance, now);
        return true;
    }

    private static void EndDoctrineLeg(Unit unit, float now)
    {
        unit.DoctrineLegUntil = -999f;
        unit.DoctrineLegStarted = -999f;
        unit.NextDoctrineLegAt = now + CampaignGroupDoctrine.EngagementLegCooldown;
        unit.Progress.Clear();
    }

    private static bool TryStartEngagementLeg(Unit unit, VehicleBehaviour target, float now, float distance)
    {
        if (unit.BossCharacter != CampaignBossTactics.Character.None) return false;
        if (now < unit.NextDoctrineLegAt || !Mobile(unit)
            || now - unit.LastOrderAt < MinimumOrderSpacing) return false;
        var coverage = TeamCoverage(unit, now);
        if (coverage.Cover && CampaignGroupDoctrine.IsCoverSlot(
                unit.GroupRole, unit.Slot, coverage.TeamSize, coverage.LeadSlot))
            return false;
        var allowed = coverage.Cover
            ? CampaignGroupDoctrine.EngagementMoveAllowed(unit.GroupRole, unit.Slot,
                coverage.TeamSize, coverage.LeadSlot, coverage.Cover, now,
                unit.DoctrineContactSince, distance)
            : CampaignGroupDoctrine.EngagementNoCoverLeadAllowed(unit.GroupRole, unit.Slot,
                coverage.TeamSize, coverage.LeadSlot, distance);
        if (!allowed) return false;
        var endpoint = CampaignGroupDoctrine.EngagementWaypoint(XZ(target.Position),
            XZ(unit.Tank.Position), unit.GroupRole, unit.LaneSide, unit.Slot);
        if (!TryGround(Native(endpoint, target.Position.y), out var grounded)) return false;
        if (!IssueMove(unit, grounded, Mode.Advance, now)) return false;
        unit.DoctrineLegStarted = now;
        unit.DoctrineLegUntil = now + CampaignGroupDoctrine.EngagementLegSeconds;
        unit.DoctrineLegTarget = grounded;
        unit.DoctrineLegStartDistance = Distance(unit.Tank.Position, grounded);
        unit.Progress.Clear();
        Plugin.Logger.LogInfo($"Expedition group doctrine: {(unit.Friendly ? "ally" : "enemy")} "
            + $"slot {unit.Slot + 1} advances role={unit.GroupRole} in a bounded clear-contact leg; "
            + $"cover={coverage.Cover}; team={coverage.TeamSize}; leadSlot={coverage.LeadSlot + 1}; "
            + $"contactRange={distance:N0} m; legEnd={unit.DoctrineLegUntil:N1}.");
        return true;
    }

    private static bool Mobile(Unit unit)
    {
        try { return (unit.Tank.Flags & VehicleFlags.Mobile) != 0; }
        catch { return false; }
    }

    private static float HorizontalSpeed(Unit unit)
    {
        try
        {
            var velocity = unit.Commander.driver?.body?.linearVelocity;
            if (velocity == null) return float.NaN;
            return new Vector2(velocity.Value.x, velocity.Value.z).magnitude;
        }
        catch { return float.NaN; }
    }

    private static bool Move(Unit unit, Vector3 goal, Mode desiredMode, float now)
    {
        if ((unit.Tank.Flags & VehicleFlags.Mobile) == 0) return false;
        if (now - unit.LastOrderAt < MinimumOrderSpacing) return false;
        if (desiredMode is Mode.Advance or Mode.Search && unit.Mode != desiredMode
            && unit.Mode != Mode.Recover
            && !DoctrineAllows(unit, desiredMode, now))
            return false;
        if (unit.Mode == Mode.Recover)
        {
            if (now < unit.RecoveryUntil && Distance(unit.Tank.Position, unit.RecoveryTarget) > 8f) return false;
            unit.Mode = Mode.None;
            unit.Progress.GrantTurnTime(now);
        }
        var stuck = Distance(unit.Tank.Position, goal) > 18f
            && unit.Progress.Stalled(XZ(goal), XZ(unit.Tank.Position), now);
        var recoveryBlocked = now < unit.NextRecoveryAt;
        if (stuck && unit.Mode != Mode.None && !recoveryBlocked)
        {
            // Ask the same native driver/pathfinder for a bounded sidestep. No
            // physics override, reverse-throttle injection or live hull teleport.
            var candidate = CampaignHuntPlanner.RecoveryWaypoint(XZ(unit.Tank.Position), XZ(goal),
                unit.Slot, unit.RecoveryCount++);
            if (TryGround(new Vector3(candidate.X, unit.Tank.Position.y, candidate.Y), out var detour)
                && IssueMove(unit, detour, Mode.Recover, now))
            {
                unit.RecoveryTarget = detour;
                unit.RecoveryUntil = now + 24f;
                unit.NextRecoveryAt = now + CampaignHuntPlanner.MoveInterval;
                unit.Progress.GrantTurnTime(now);
                Plugin.Logger.LogInfo($"Expedition team AI: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1} "
                    + $"native path recovery: pos={Format(unit.Tank.Position)}; goal={Format(goal)}; "
                    + $"bestGoalRange={unit.Progress.BestDistance:N0} m; detour={Format(detour)}; {NativeTask(unit)}.");
                return true;
            }
            // An invalid edge/terrain detour must not cause the same main
            // order to be reissued every scan while the stall flag stays set.
            unit.NextRecoveryAt = now + CampaignHuntPlanner.MoveInterval;
            unit.Progress.GrantTurnTime(now);
            recoveryBlocked = true;
        }
        var changed = unit.Mode != desiredMode;
        var arrived = Distance(unit.Tank.Position, goal) <= 12f;
        if (!changed && arrived) return false;
        if (changed || CampaignHuntPlanner.ShouldRefresh(now, unit.LastOrderAt, MoveActive(unit),
                (goal - unit.LastOrderTarget).sqrMagnitude, stuck, CampaignHuntPlanner.MoveInterval,
                recoveryBlocked))
            return IssueMove(unit, goal, desiredMode, now);
        return false;
    }

    private static bool MoveActive(Unit unit)
    {
        try
        {
            var cts = unit.Commander.oneOffMoveOrderCts;
            if (cts != null && !cts.IsCancellationRequested) return true;
            var name = unit.Commander.driver?.CurrentTask?.GetIl2CppType()?.Name;
            return name != null && name.Contains("Move", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static bool AttackActive(Unit unit)
    {
        try { var cts = unit.Commander.attackOrderCts; return cts != null && !cts.IsCancellationRequested; }
        catch { return false; }
    }

    private static bool IssueMove(Unit unit, Vector3 point, Mode desiredMode, float now)
    {
        // Attack and one-off movement use separate native cancellation channels.
        // End our attack/hold before installing a new movement task.
        if (unit.Mode == Mode.Attack || unit.FireHold != null) EndCombatMovement(unit);
        var target = new PositionTarget(point, 8f, 0f);
        var order = new MoveToPositionOrder(ref target, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder);
        if (!Send(unit, order, now)) return false;
        if (!unit.LoggedFirstMove)
        {
            unit.LoggedFirstMove = true;
            Plugin.Logger.LogInfo($"Expedition team AI rally: first native move for "
                + $"{(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1}: "
                + $"{Format(unit.Tank.Position)} -> {Format(point)}; "
                + $"insideBounds={CampaignBattleArea.Contains(point, 16f)}; {NativeTask(unit)}.");
        }
        unit.LastOrderTarget = point;
        unit.LastOrderAt = now;
        unit.Mode = desiredMode;
        unit.AttackTarget = IntPtr.Zero;
        totalOrders++;
        return true;
    }

    private static void IssueAttack(Unit unit, VehicleBehaviour target, bool stop, float now)
    {
        ReleasePreAim(unit); // Actual native target tracking/firing owns the layers.
        // AttackOrder does not call GetMoveOrderCancellationToken. Merely
        // sending it leaves the previous highest-priority waypoint running.
        var move = unit.Commander.oneOffMoveOrderCts;
        if (move != null && !move.IsCancellationRequested) move.Cancel();
        ReleaseFireHold(unit);
        unit.Commander.driver?.ClearTasks(); // Synchronous native AI task stack, not a scene/async task.
        unit.Commander.driver?.ClearPath();
        var approach = stop ? AttackApproachType.Passive : unit.CombatProfile.Role == CampaignCombatPolicy.Role.Assault
            ? AttackApproachType.Aggressive : AttackApproachType.Balanced;
        var info = new AttackOrderInfo(target.ID, approach,
            stop ? AttackEngageMode.StopToFire : AttackEngageMode.FireOnTheMove, 1f);
        var order = new AttackOrder(ref info, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder);
        if (!Send(unit, order, now)) return;
        var continuesHold = stop && unit.Mode == Mode.Attack && unit.StopToFire
            && unit.AttackTarget == target.Pointer;
        if (!unit.Friendly) CampaignTestPool.Ledger.Contact(unit.DesignName);
        try { unit.Commander.FireMode = FireMode.FireAtWill; } catch { }
        if (unit.AttackTarget != target.Pointer)
            Plugin.Logger.LogInfo($"Expedition team AI: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1} "
                + $"engages visible team {(byte)target.ID.TeamID} hull at {Distance(unit.Tank.Position, target.Position):N0} m.");
        Plugin.Logger.LogInfo($"Expedition combat order: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1}; "
            + $"role={unit.CombatProfile.Role}; stop={stop}; approach={approach}; "
            + $"reaction={unit.CombatProfile.ReactionSeconds:N2} s; waypoint cancelled; stationary aim={stop}.");
        unit.LastOrderAt = now;
        unit.Mode = Mode.Attack;
        unit.AttackTarget = target.Pointer;
        unit.StopToFire = stop;
        if (stop)
        {
            if (!continuesHold) StartFireEpisode(unit, now);
            EnsureFireHold(unit, target);
        }
        else unit.FireTracker.Release();
        totalOrders++;
    }

    private static void EnsureFireHold(Unit unit, VehicleBehaviour target)
    {
        var ct = unit.Commander.attackOrderCts;
        var driver = unit.Commander.driver;
        if (ct == null || ct.IsCancellationRequested || driver == null) return;
        var forward = unit.Tank.transform.forward; forward.y = 0;
        var direction = target.Position - unit.Tank.Position; direction.y = 0;
        var turning = unit.FireFacing is { } facing && !facing.WasCollected && !facing.Finished && !facing.CancellationRequested;
        if (CampaignCombatPolicy.FaceToFire(Vector3.Angle(forward, direction), turning))
        {
            if (unit.FireHold is { } oldHold && !oldHold.WasCollected) oldHold.Finished = true;
            unit.FireHold = null;
            if (!turning)
            {
                driver.ClearPath();
                unit.FireFacing = new FaceTargetTask(target.transform, 6f, FaceTargetTask.CompletionMode.Finish)
                    { Priority = AITask.TopPriority, CancellationToken = ct.Token };
                driver.AddTask(unit.FireFacing);
            }
            return;
        }
        if (turning) unit.FireFacing!.Finished = true;
        unit.FireFacing = null;
        if (unit.FireHold is { } held && !held.WasCollected && !held.Finished && !held.CancellationRequested) return;
        // A native brake/hold task; gun laying and firing tasks keep running.
        driver.ClearPath();
        unit.FireHold = new HaltTask(3600f) { Priority = AITask.TopPriority, CancellationToken = ct.Token };
        driver.AddTask(unit.FireHold);
    }

    private static void StartFireEpisode(Unit unit, float now)
    {
        unit.FireHeads.Clear();
        unit.FireTracker.Reset(now);
        unit.FireStallLogged = false;
        unit.NextStallActionAt = 0f;
        unit.FireLoaded = false;
        unit.FireHasTarget = false;
        unit.FireError = float.NaN;
        unit.FireHullAngle = float.NaN;
    }

    // Native shot, reload, gun-error and hull-angle sampling. HasTarget is only
    // carried for the log line: the Cavalier fired with has=False.
    private static void ObserveFireProgress(Unit unit, VehicleBehaviour target, float now)
    {
        var shots = 0;
        var loaded = false;
        var readable = false;
        var hasTarget = false;
        var error = float.NaN;
        try
        {
            var gunners = unit.Commander.gunners;
            if (gunners == null || gunners.Length == 0) loaded = true;
            else
            {
                if (unit.FireHeads.Count != gunners.Length)
                {
                    while (unit.FireHeads.Count < gunners.Length) unit.FireHeads.Add(-1);
                    if (unit.FireHeads.Count > gunners.Length)
                        unit.FireHeads.RemoveRange(gunners.Length, unit.FireHeads.Count - gunners.Length);
                }
                for (var i = 0; i < Math.Min(gunners.Length, 32); i++)
                {
                    var ai = gunners[i];
                    var gun = ai?.Gunner;
                    if (gun == null) continue;
                    readable = true;
                    var head = 0;
                    try { head = ai!.fireBufferHead; } catch { }
                    var previous = unit.FireHeads[i];
                    if (previous >= 0 && head != previous) shots++;
                    unit.FireHeads[i] = head;
                    try { if (gun.ReloadFraction >= .99f) loaded = true; } catch { }
                    if (i != 0) continue;
                    try
                    {
                        var aim = gun.AimingMechanisms;
                        var count = aim?.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IReadOnlyAimable>>().Count ?? 0;
                        for (var j = 0; j < Math.Min(count, 4); j++)
                        {
                            var axis = aim![j];
                            try { if (axis.HasTarget) hasTarget = true; } catch { }
                            var deviation = axis.TargetAngleDeviation;
                            if (float.IsFinite(deviation)
                                && (!float.IsFinite(error) || Math.Abs(deviation) < Math.Abs(error)))
                                error = deviation;
                        }
                    }
                    catch { }
                }
                if (!readable) loaded = true;
            }
        }
        catch { loaded = true; }
        var forward = unit.Tank.transform.forward; forward.y = 0f;
        var direction = target.Position - unit.Tank.Position; direction.y = 0f;
        var hull = forward.sqrMagnitude > .01f && direction.sqrMagnitude > .01f
            ? Vector3.Angle(forward, direction) : float.NaN;
        var turning = unit.FireFacing is { } facing && !facing.WasCollected
            && !facing.Finished && !facing.CancellationRequested;
        unit.FireHasTarget = hasTarget;
        unit.FireLoaded = loaded;
        unit.FireError = error;
        unit.FireHullAngle = hull;
        if (unit.FireTracker.Observe(now, shots, loaded, error, hull, turning))
            unit.FireStallLogged = false;
    }

    // Move a stalled hull to a flank point. The chosen side alternates on every
    // stall, so the same lateral position is never tried twice in a row.
    private static bool TryReposition(Unit unit, VehicleBehaviour target, float now)
    {
        if (!CampaignCombatPolicy.Repositions(unit.Ambusher, unit.Yielded,
                unit.Tank.ControlType == ControlType.Player, Mobile(unit))) return false;
        var preferred = unit.FlankSide;
        var lateral = CampaignCombatPolicy.FlankLateral(unit.Slot);
        var forward = CampaignCombatPolicy.FlankForward(unit.Slot);
        var behavior = Behavior(unit);
        var sidestepOnly = CampaignBossTactics.SidestepsOnly(behavior);
        var goal = Vector3.zero;
        var found = false;
        var used = preferred;
        for (var attempt = 0; attempt < 2 && !found; attempt++)
        {
            var side = attempt == 0 ? preferred : -preferred;
            System.Numerics.Vector2 point;
            if (sidestepOnly)
            {
                if (!CampaignBossTactics.TrySidestep(behavior, XZ(unit.Tank.Position),
                        XZ(target.Position), side, out point)) continue;
            }
            else
            {
                point = CampaignCombatPolicy.FlankWaypoint(XZ(unit.Tank.Position), XZ(target.Position),
                    side, lateral, forward);
            }
            if (TryGround(Native(point, target.Position.y), out var grounded))
            {
                goal = grounded;
                used = side;
                found = true;
            }
        }
        // The following stall takes the other side than the one actually used.
        unit.FlankSide = CampaignCombatPolicy.NextFlankSide(used);
        if (!found)
        {
            // Fortress and Battery never use the forward fallback point.
            if (sidestepOnly) return false;
            // No valid flank ground on either side: use the same approach point
            // as the existing no-clear-shot branch.
            var approach = unit.BossCharacter == CampaignBossTactics.Character.None
                ? CampaignGroupDoctrine.ApproachWaypoint(XZ(target.Position), XZ(unit.Tank.Position),
                    unit.GroupRole, unit.LaneSide, unit.Slot)
                : CampaignHuntPlanner.ApproachWaypoint(XZ(target.Position),
                    XZ(unit.Tank.Position), unit.Slot, 18f);
            if (!TryGround(Native(approach, target.Position.y), out goal)) return false;
        }
        var stall = unit.FireTracker.StalledFor;
        var shots = unit.FireTracker.Shots;
        var loaded = unit.FireLoaded;
        var has = unit.FireHasTarget;
        var error = unit.FireError;
        var hull = unit.FireHullAngle;
        var end = now + CampaignCombatPolicy.RepositionSeconds;
        // The firing-line cache stays false for this target until the leg ends.
        unit.FiringLines[target.Pointer] = (end, false);
        unit.ClearShot = false;
        unit.Repositioning = true;
        unit.RepositionTarget = target.Pointer;
        unit.RepositionUntil = end;
        unit.RepositionStart = unit.Tank.Position;
        if (!IssueMove(unit, goal, Mode.Advance, now))
        {
            unit.Repositioning = false;
            unit.RepositionTarget = IntPtr.Zero;
            unit.FiringLines.Remove(target.Pointer);
            unit.FireTracker.Reset(now);
            return false;
        }
        unit.StopToFire = false;
        LogFireStall(unit, stall, shots, loaded, has, error, hull, goal, holding: false);
        return true;
    }

    private static void EndReposition(Unit unit)
    {
        if (!unit.Repositioning) return;
        var target = unit.RepositionTarget;
        unit.Repositioning = false;
        unit.RepositionTarget = IntPtr.Zero;
        unit.RepositionUntil = 0f;
        if (target != IntPtr.Zero) unit.FiringLines.Remove(target);
        unit.FireTracker.Release();
        unit.FireStallLogged = false;
        unit.Progress.Clear(); // the next approach gets its own progress anchor
    }

    private static void LogFireStall(Unit unit, Vector3? destination, bool holding) =>
        LogFireStall(unit, unit.FireTracker.StalledFor, unit.FireTracker.Shots, unit.FireLoaded,
            unit.FireHasTarget, unit.FireError, unit.FireHullAngle, destination, holding);

    private static void LogFireStall(Unit unit, float stallSeconds, int shots, bool loaded,
        bool has, float error, float hull, Vector3? destination, bool holding)
    {
        var errorText = float.IsFinite(error) ? error.ToString("N1") : "?";
        var hullText = float.IsFinite(hull) ? hull.ToString("N1") : "?";
        var suffix = holding
            ? "; holding (ambusher)"
            : destination.HasValue
            ? $"; repositioning to ({destination.Value.x:N0}, {destination.Value.z:N0})"
            : string.Empty;
        var ending = holding ? string.Empty : ".";
        Plugin.Logger.LogInfo($"Expedition team AI: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1} "
            + $"no firing progress for {Math.Max(0f, stallSeconds):N0} s (shots={shots}, "
            + $"loaded={(loaded ? "j" : "n")}, has={(has ? "j" : "n")}, error={errorText}, "
            + $"hullAngle={hullText}){suffix}{ending}");
    }

    // The one-second Ambusher reaction is not a wait-before-turning timer: a
    // visible contact immediately gets a native hull-facing task. The task is
    // owned by this controller and is released on target loss, attack handoff
    // and encounter cleanup.
    private static void FaceDuringReaction(Unit unit, VehicleBehaviour target)
    {
        if (unit.Yielded || unit.Tank.ControlType == ControlType.Player) return;
        try
        {
            var driver = unit.Commander.driver;
            if (driver == null) return;
            if (unit.ReactionFacing is { } existing && !existing.WasCollected
                && unit.ReactionFacingTarget == target.Pointer)
            {
                if (!existing.Finished && !existing.CancellationRequested) return;
                var forward = unit.Tank.transform.forward; forward.y = 0f;
                var direction = target.Position - unit.Tank.Position; direction.y = 0f;
                if (forward.sqrMagnitude > .01f && direction.sqrMagnitude > .01f
                    && Vector3.Angle(forward, direction) <= 8f) return;
            }
            ReleaseReactionFacing(unit);
            // Stop an old travel leg before turning; this is a hull order only,
            // not a detection or firing permission.
            var move = unit.Commander.oneOffMoveOrderCts;
            if (move != null && !move.IsCancellationRequested) move.Cancel();
            if (unit.Mode is Mode.Advance or Mode.Search or Mode.Recover)
            {
                unit.Mode = Mode.None;
                unit.LastOrderAt = -999f;
                unit.Progress.Clear();
            }
            driver.ClearPath();
            driver.ClearTasks();
            unit.ReactionFacingTarget = target.Pointer;
            unit.ReactionFacing = new FaceTargetTask(target.transform, 6f,
                FaceTargetTask.CompletionMode.DontApplyNextTasks)
            { Priority = AITask.TopPriority };
            // Attach only a verified live attack token. Before the first attack
            // order the default token is not cancelled and this controller owns
            // the task's Finished flag explicitly.
            if (unit.Commander.attackOrderCts is { } attack && !attack.IsCancellationRequested)
                unit.ReactionFacing.CancellationToken = attack.Token;
            driver.AddTask(unit.ReactionFacing);
        }
        catch { ReleaseReactionFacing(unit); }
    }

    private static void ReleaseReactionFacing(Unit unit)
    {
        try
        {
            if (unit.ReactionFacing is { } task && !task.WasCollected) task.Finished = true;
        }
        catch { /* Native vehicle release may already have cleared its task stack. */ }
        unit.ReactionFacing = null;
        unit.ReactionFacingTarget = IntPtr.Zero;
    }

    private static void StopAmbusher(Unit unit)
    {
        ReleaseReactionFacing(unit);
        EndCombatMovement(unit);
        var move = unit.Commander.oneOffMoveOrderCts;
        if (move != null && !move.IsCancellationRequested) move.Cancel();
        unit.Commander.driver?.ClearPath();
        unit.Mode = Mode.None;
        unit.LastOrderAt = -999f;
        unit.Progress.Clear();
    }

    // A Fortress stops a travel leg without discarding its facing/firing state.
    private static void StopBossHold(Unit unit)
    {
        ReleaseReactionFacing(unit);
        var move = unit.Commander.oneOffMoveOrderCts;
        if (move != null && !move.IsCancellationRequested) move.Cancel();
        unit.Commander.driver?.ClearPath();
        unit.Mode = Mode.None;
        unit.LastOrderAt = -999f;
        unit.Progress.Clear();
    }

    private static void PreAimAtKnownPosition(Unit unit, Vector3 position)
    {
        if (unit.PreAimUnavailable || unit.Yielded || unit.Tank.ControlType == ControlType.Player) return;
        try
        {
            var direction = position - unit.Tank.Position;
            direction.y = 0;
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.z) || direction.sqrMagnitude < 1f) return;
            direction = direction.normalized;
            for (var i = unit.PreAim.Count - 1; i >= 0; i--)
            {
                var (layer, task) = unit.PreAim[i];
                if (layer.WasCollected || task.WasCollected) { unit.PreAim.RemoveAt(i); continue; }
                var current = layer.CurrentTask;
                if (task.Finished || task.CancellationRequested || current != null && current.Pointer != task.Pointer)
                {
                    task.Finished = true;
                    layer.taskStack?.ClearEndedTasks();
                    unit.PreAim.RemoveAt(i);
                }
                else task.AimDirection = direction;
            }
            var layers = unit.Commander.gunLayers;
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null || layer.WasCollected || layer.CurrentTask != null
                    || unit.PreAim.Any(owned => owned.Layer.Pointer == layer.Pointer)) continue;
                // Verified native direction-only task: no fire flag or target
                // entity. Zero completion tolerance retains the bearing until
                // a real task/manual handoff/session cleanup replaces it.
                var task = new AimAtDirectionTask(direction, 0f) { Priority = AITask.NeutralPriority };
                unit.PreAim.Add((layer, task));
                layer.PushTask(task);
            }
        }
        catch (Exception ex)
        {
            ReleasePreAim(unit);
            unit.PreAimUnavailable = true;
            Plugin.Logger.LogWarning($"Expedition pre-aim unavailable for '{unit.DesignName}'; native aiming retained: {ex.GetType().Name}.");
        }
    }

    private static void ReleasePreAim(Unit unit)
    {
        foreach (var (layer, task) in unit.PreAim)
        {
            try
            {
                if (!task.WasCollected) task.Finished = true;
                if (!layer.WasCollected) layer.taskStack?.ClearEndedTasks();
            }
            catch { /* Native vehicle release may already have disposed its layers. */ }
        }
        unit.PreAim.Clear();
    }

    private static bool HasFiringLine(Unit unit, VehicleBehaviour target, float now)
    {
        unit.ShotTarget = target.Pointer;
        // A stalled hull keeps its denial until the displacement finishes, even
        // if a stale cache entry would otherwise expire mid-leg.
        if (unit.Repositioning && unit.RepositionTarget == target.Pointer)
        {
            unit.NextShotCheck = now + .75f;
            unit.ClearShot = false;
            return false;
        }
        if (unit.FiringLines.TryGetValue(target.Pointer, out var cached) && now < cached.Until)
        {
            unit.NextShotCheck = cached.Until;
            return unit.ClearShot = cached.Clear;
        }
        unit.NextShotCheck = now + .75f;
        unit.ClearShot = false;
        // Bounded hull/turret-height samples, not a replacement for native
        // detection or gun ballistics. Ignore only this shooter and its target.
        foreach (var height in new[] { 1.2f, 2.5f })
        {
            var start = unit.Tank.Position + Vector3.up * height;
            var delta = target.Position + Vector3.up * height - start;
            if (delta.sqrMagnitude < 1f) continue;
            var blocked = false;
            foreach (var hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                var collider = hit.collider;
                if (collider == null || collider.transform.IsChildOf(unit.Tank.transform)
                    || collider.transform.IsChildOf(target.transform)) continue;
                blocked = true;
                break;
            }
            if (!blocked) { unit.ClearShot = true; break; }
        }
        // Cache every visible candidate, so target selection does not invalidate
        // the selected target's cache and repeat all rays on every 250 ms tick.
        // Unit objects and their caches are discarded on encounter reset.
        if (unit.FiringLines.Count >= 64) unit.FiringLines.Clear();
        unit.FiringLines[target.Pointer] = (unit.NextShotCheck, unit.ClearShot);
        return unit.ClearShot;
    }

    private static void ReleaseFireHold(Unit unit)
    {
        ReleaseReactionFacing(unit);
        try
        {
            if (unit.FireHold is { } held && !held.WasCollected) held.Finished = true;
            if (unit.FireFacing is { } facing && !facing.WasCollected) facing.Finished = true;
        }
        catch { /* Native vehicle release may already have cleared its task stack. */ }
        unit.FireHold = null;
        unit.FireFacing = null;
    }

    private static void EndCombatMovement(Unit unit)
    {
        ReleaseFireHold(unit);
        unit.FireTracker.Release();
        unit.FireStallLogged = false;
        var attack = unit.Commander.attackOrderCts;
        if (attack != null && !attack.IsCancellationRequested) attack.Cancel();
        unit.Commander.driver?.ClearTasks();
    }

    private static bool Send(Unit unit, Order order, float now)
    {
        if (unit.Yielded || unit.Tank.ControlType == ControlType.Player) return false;
        var receiver = unit.Tank.OrderReciever;
        if (receiver == null || !receiver.CanReceiveOrder) { unit.NextTryAt = now + 2f; return false; }
        issuing = order.Pointer;
        try { receiver.Order(order); return !unit.Yielded; }
        catch (Exception ex)
        {
            unit.NextTryAt = now + 2f;
            Plugin.Logger.LogWarning("Expedition team AI native order refused: " + ex.Message);
            return false;
        }
        finally { issuing = IntPtr.Zero; }
    }

    private static bool Sees(CommanderAI commander, EntityID id)
    {
        try
        {
            var buffer = commander.visibleTargets;
            var visible = buffer?.visible;
            if (buffer == null || visible == null) return false;
            var count = Math.Min(buffer.visibleCount, visible.Length);
            for (var i = 0; i < count; i++)
            {
                var target = visible[i];
                if (target != null && target.Valid && EntityID.FilterMatch(target.EntityID, id)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsUsable(VehicleBehaviour? tank)
    {
        if (tank == null || tank.Pointer == IntPtr.Zero || tank.WasCollected) return false;
        try
        {
            if (!tank.gameObject.activeInHierarchy || !tank.enabled) return false;
            var flags = tank.Flags;
            if ((flags & VehicleFlags.Enabled) == 0 || (flags & (VehicleFlags.Mobile | VehicleFlags.Armed)) == 0)
                return false;
            if (!tank.TryGetModule<VehicleCrewWorkspaceModule>(out var module) || module?.operators == null)
                return true; // unreadable is not evidence of death
            var readable = module.operators.Count > 0;
            for (var i = 0; i < module.operators.Count; i++)
            {
                var member = module.operators[i]?.internalOperator;
                if (member == null) { readable = false; continue; }
                var health = member.HealthFraction;
                if (!float.IsFinite(health)) { readable = false; continue; }
                if (health > 0) return true;
            }
            return !readable;
        }
        catch { return true; }
    }

    private static VehicleBehaviour? TankAt(Il2CppSystem.Collections.Generic.List<VehicleSpawn> spawns, int index)
    {
        try
        {
            var gateway = spawns[index]?.Vehicle?.TryCast<IVehicleGateway>();
            return gateway?.Behaviour?.TryCast<VehicleBehaviour>()
                ?? gateway?.transform?.GetComponentInChildren<VehicleBehaviour>();
        }
        catch { return null; }
    }

    private static bool TryGround(Vector3 point, out Vector3 grounded)
    {
        grounded = point;
        try
        {
            if (!Physics.Raycast(point + Vector3.up * 250f, Vector3.down, out var hit, 500f, ~0,
                    QueryTriggerInteraction.Ignore) || hit.collider == null
                || hit.collider.TryCast<TerrainCollider>() == null
                || Vector3.Angle(hit.normal, Vector3.up) > 25f
                || !CampaignBattleArea.Contains(hit.point, 16f)) return false;
            grounded = hit.point + Vector3.up * .5f;
            return true;
        }
        catch { return false; }
    }

    private static void LogSummary(float now)
    {
        Plugin.Logger.LogInfo($"Expedition team AI summary: enemies={units.Count(unit => !unit.Friendly && !unit.Yielded)}; "
            + $"allies={units.Count(unit => unit.Friendly && !unit.Yielded)}; native/manual={yieldedTanks.Count}; "
            + $"attacking={units.Count(unit => !unit.Yielded && unit.Mode == Mode.Attack)}; "
            + $"searching={units.Count(unit => !unit.Yielded && unit.Mode == Mode.Search)}; "
            + $"orders={totalOrders}; blockedMapOrders={blockedOrders}; "
            + $"friendlyFix={Format(friendlyMemory.LastKnown)} ({SeenAge(friendlyMemory, now)}); "
            + $"enemyFix={Format(enemyMemory.LastKnown)} ({SeenAge(enemyMemory, now)}).");
        foreach (var unit in units.Where(item => !item.Yielded).Take(20))
        {
            var contact = liveTanks.FirstOrDefault(tank => tank.Pointer == unit.AttackTarget);
            var visible = contact != null && Sees(unit.Commander, contact.ID);
            var contactRange = visible ? Distance(unit.Tank.Position, contact!.Position).ToString("N0") : "unseen";
            Plugin.Logger.LogInfo($"Expedition AI path sample: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1}; "
                + $"design='{unit.DesignName}'; role={unit.GroupRole}; lane={unit.LaneSide}; mode={unit.Mode}; pos={Format(unit.Tank.Position)}; lastOrderTarget={Format(unit.LastOrderTarget)}; "
                + $"waypointRange={Distance(unit.Tank.Position, unit.LastOrderTarget):N0} m; targetRange={contactRange}; "
                + $"stopToFire={unit.StopToFire}; clearShot={unit.ClearShot}; fireStall={unit.FireTracker.StalledFor:N0}; "
                + $"preAimLayers={unit.PreAim.Count}; "
                + $"{TravelSample(unit)}; {NativeTask(unit)}.");
            if (unit.Mode == Mode.Attack)
                Plugin.Logger.LogInfo($"Expedition AI weapon sample: {(unit.Friendly ? "ally" : "enemy")} slot {unit.Slot + 1}; "
                    + $"design='{unit.DesignName}'; {WeaponSample(unit)}.");
        }
    }

    private static string WeaponSample(Unit unit)
    {
        try
        {
            var gunners = unit.Commander.gunners;
            if (gunners == null || gunners.Length == 0) return "gunners=0 (native weapon crew/control unavailable)";
            var samples = new List<string>();
            for (var i = 0; i < Math.Min(gunners.Length, 3); i++)
            {
                var ai = gunners[i];
                var gun = ai?.Gunner;
                if (gun == null) { samples.Add($"gun{i}=no control"); continue; }
                var aim = gun.AimingMechanisms;
                var angles = new List<string>();
                var count = aim?.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<IReadOnlyAimable>>().Count ?? 0;
                for (var j = 0; j < Math.Min(count, 4); j++)
                {
                    var axis = aim![j];
                    angles.Add($"axis{j}[target={axis.TargetID},has={axis.HasTarget},reachable={axis.TargetAchievable},error={axis.TargetAngleDeviation:N2}]");
                }
                samples.Add($"gun{i}[functioning={gun.Functioning},canFire={gun.CanFire},reload={gun.ReloadFraction:N2},"
                    + $"crew={gun.EnableFraction:N2},loader={gun.LoaderEnableFraction:N2},weaponFlags={gun.WeaponFlags},"
                    + $"sight={gun.Sight?.ID},task={ai!.currentTask?.GetIl2CppType()?.Name ?? "none"},"
                    + $"instructions={ai.currentTask?.Instructions},stopRequested={ai.StopRequested},fireBufferHead={ai.fireBufferHead};"
                    + string.Join(";", angles) + "]");
            }
            return "fireMode=" + unit.Commander.FireMode + "; " + string.Join("; ", samples);
        }
        catch (Exception ex) { return "weapon sample unavailable: " + ex.GetType().Name; }
    }

    private static string TravelSample(Unit unit)
    {
        try
        {
            var driver = unit.Commander.driver;
            if (driver?.body == null) return "travel=unavailable";
            var velocity = driver.body.linearVelocity; velocity.y = 0;
            var forward = unit.Tank.transform.forward; forward.y = 0;
            var direction = unit.LastOrderTarget - unit.Tank.Position; direction.y = 0;
            var task = driver.CurrentTask?.TryCast<MoveToPositionTask>();
            return $"hullSpeed={Vector3.Dot(velocity, forward.normalized):N1} m/s; "
                + $"goalAngle={Vector3.Angle(forward, direction):N0} deg; "
                + $"nativeReverseAllowed={driver.reverseAllowed}; nativeReverseRange={driver.maxReverseDistance:N0} m; "
                + $"forcedReverse={task?.ForceReverse}; forwardTravel={ForwardTravelOnly(driver, DriverUpdateFlags.None)}";
        }
        catch { return "travel=unreadable"; }
    }

    private static string NativeTask(Unit unit)
    {
        try
        {
            var task = unit.Commander.driver?.CurrentTask?.GetIl2CppType()?.Name ?? "none";
            var move = unit.Commander.oneOffMoveOrderCts;
            var attack = unit.Commander.attackOrderCts;
            return $"driver={task}; moveToken={(move == null ? "missing" : move.IsCancellationRequested ? "cancelled" : "active")}; "
                + $"attackToken={(attack == null ? "missing" : attack.IsCancellationRequested ? "cancelled" : "active")}";
        }
        catch { return "native task unreadable"; }
    }

    private static string SeenAge(TeamMemory memory, float now) => memory.Seen
        ? Math.Max(0, now - memory.LastSeenAt).ToString("N0") + " s since sighting" : "opposing spawn";

    private static void ResetEncounter()
    {
        CampaignElevatedVision.Reset();
        CampaignSpawnSettleSampler.Reset();
        foreach (var unit in units) { ReleasePreAim(unit); ReleaseFireHold(unit); }
        travelDrivers.Clear();
        units.Clear(); liveTanks.Clear(); claimed.Clear(); yieldedTanks.Clear(); manualReceivers.Clear();
        enemySpawnOrigins.Clear();
        blockedLogged.Clear(); candidates.Clear(); targetCandidates.Clear();
        mode = null; hasFix = false; faulted = false;
        spawnsAtStart = new(); friendlyMemory = new(); enemyMemory = new();
        spawnCaptureAt = -1f; spawnReferenceHeight = 0;
        nextScan = nextModeSearch = nextSummary = 0f;
        enemySlots = friendlySlots = totalOrders = blockedOrders = 0;
        issuing = IntPtr.Zero;
        roleMappingRejected = false;
        spawnMappingVerified = false;
        bossPointer = IntPtr.Zero;
        bossIdentityResolved = bossIdentityRejected = false;
        bossCharacterLogged = false;
        bossStatus = string.Empty;
        if (prepared) Status = "AI hunt: waiting for the next battle";
    }

    private static Numerics.Vector2 XZ(Vector3 value) => new(value.x, value.z);
    private static Vector3 Native(Numerics.Vector2 value, float height) => new(value.X, height, value.Y);
    private static float Distance(Vector3 a, Vector3 b) => Numerics.Vector2.Distance(XZ(a), XZ(b));
    private static string Format(Vector3 point) => $"({point.x:N0}, {point.z:N0})";
}

[HarmonyPatch(typeof(VehicleOrderConstruction), nameof(VehicleOrderConstruction.SelectConstructAndApply))]
internal static class CampaignPlayerCommandIngressPatch
{
    private static void Prefix(IVehicleBehaviour __0, Il2CppSystem.Collections.Generic.List<IOrderReceiver> __2)
    {
        try { CampaignEnemyHunt.PlayerCommand(__0, __2); }
        catch (Exception ex)
        {
            // This observer never cancels the native command UI or its task.
            Plugin.Logger.LogWarning("Expedition native player command observer: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(CommanderAI), nameof(CommanderAI.Order))]
internal static class CampaignEnemyHuntOrderPatch
{
    private static bool Prefix(CommanderAI __instance, Order __0)
    {
        try { return CampaignEnemyHunt.ExternalOrder(__instance, __0); }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(CommanderAI), nameof(CommanderAI.UpdateFormationTasks))]
internal static class CampaignEnemyHuntFormationPatch
{
    private static bool Prefix(CommanderAI __instance)
    {
        try { return !CampaignEnemyHunt.Controls(__instance); }
        catch { return true; }
    }
}

[HarmonyPatch(typeof(DriverAI), nameof(DriverAI.Step), new[] { typeof(float), typeof(DriverUpdateFlags) })]
internal static class CampaignForwardTravelPatch
{
    private static void Prefix(DriverAI __instance, DriverUpdateFlags __1, out bool? __state)
    {
        __state = null;
        try
        {
            if (!__instance.reverseAllowed || !CampaignEnemyHunt.ForwardTravelOnly(__instance, __1)) return;
            __state = __instance.reverseAllowed;
            __instance.reverseAllowed = false;
        }
        catch { /* Optional policy failure leaves native movement in charge. */ }
    }

    private static void Finalizer(DriverAI __instance, bool? __state)
    {
        // Harmony runs this after successful and exceptional native steps. Never
        // leave a changed driver setting on a manual/player handoff or unload.
        try { if (__state.HasValue && !__instance.WasCollected) __instance.reverseAllowed = __state.Value; }
        catch { /* The native driver may have been released during the step. */ }
    }
}
#endif
