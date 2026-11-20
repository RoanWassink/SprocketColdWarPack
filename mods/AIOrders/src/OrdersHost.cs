using System.Reflection;
using Il2CppInterop.Runtime.Attributes;
using Sprocket;
using Sprocket.ArtificialIntelligence;
using Sprocket.Formations;
using Sprocket.Gameplay;
using Sprocket.Orders;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Control;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace SprocketAIOrders;

public sealed partial class OrdersHost : MonoBehaviour
{
    public OrdersHost(IntPtr pointer) : base(pointer) { }
    internal static OrdersHost? Current;
    internal static bool Stopped;
    private enum Kind { Follow, Halt, Move, Attack, Retreat }
    private sealed class Unit
    {
        public VehicleBehaviour Tank = null!;
        public CommanderAI Commander = null!;
        public Kind Order;
        public bool Column, Native;
        public VehicleBehaviour? Target;
        public Vector3 Goal, Face;
        public readonly List<Vector3> Queue = new();
        public float SentAt;
        public bool Moving;
        public HaltTask? Hold;
        public Il2CppSystem.Threading.CancellationTokenSource? MoveToken, AttackToken;
        public int FollowSlot, Failures, MoveRetries;
        public float RetryAt, HeldAt, NextDiagnostic, CheckAt, BestDistance = float.PositiveInfinity;
        public Vector3 ProgressPos;
        public float ProgressAt, RecoverUntil;
        public int Stucks;
        public bool Approaching;
    }
    private sealed class Contact { public VehicleBehaviour Tank = null!; public Vector3 Position; public float Seen; public bool Visible; }
    private readonly Dictionary<IntPtr, Unit> owned = new();
    private readonly HashSet<IntPtr> selected = new();
    private readonly List<VehicleBehaviour> allies = new(), enemies = new();
    private readonly Dictionary<IntPtr, Contact> contacts = new();
    private readonly Dictionary<IntPtr, int> numbers = new();
    private readonly Dictionary<IntPtr, string> names = new();
    private readonly HashSet<IntPtr> gameOrdered = new();
    private DeathmatchGameMode? mode;
    private VehicleBehaviour? player, target;
    private IntPtr issuing;
    private float nextScan, nextBoundsCapture;
    private int mapKeyCheckFrame = -1, mapClosedFrame = -1;
    private bool boundsDiagnosed;
    private readonly QuickMenu quick = new();
    private NumberMenu menu => quick.Menu;
    private VehicleBehaviour[] menuUnits = Array.Empty<VehicleBehaviour>(), menuTargets = Array.Empty<VehicleBehaviour>();
    private WorldBounds? bounds;
    private string status = "Select allies and an order.";
    private string? pending;
    private MethodInfo? expeditionHandoff;
    private bool bridgeResolved;
    private Vector3 followForward, lastLeaderPosition;
    private float leaderReformAt;

    public void Update()
    {
        if (Stopped) return;
        Current = this;
        try
        {
            if (mapKeyCheckFrame >= 0 && Time.frameCount > mapKeyCheckFrame)
            {
                if (!mapOpen && player != null && Plugin.Detailed) Plugin.Info("AI Orders map key pressed but the panel did not open");
                mapKeyCheckFrame = -1;
            }
            if (Plugin.MapKey.WasPressedThisFrame && Plugin.Detailed)
            {
                Plugin.Info($"AI Orders map key: panelOpen={mapOpen}; player={player != null}; context={SprocketUiKit.UiKit.Context}");
                // The Kit may already have toggled this frame. Only check presses intended to open.
                if (!mapOpen && mapClosedFrame != Time.frameCount) mapKeyCheckFrame = Time.frameCount;
            }
            ExpireMapFeedback();
            if (player != null && Time.timeScale > 0f && !SprocketKeybinds.Keybinds.IsConfiguring && !SprocketUiKit.UiKit.TextInputActive)
            {
                // BindToggle only works for Kit panels, so poll the existing F9 keybind here.
                if (!mapOpen && Plugin.Toggle.WasPressedThisFrame)
                {
                    quick.Toggle();
                    if (Plugin.Detailed) Plugin.Info($"AI Orders quick HUD {(quick.Visible ? "open" : "closed")}.");
                }
                if (mapOpen)
                {
                    for (var n = 0; n <= 9; n++)
                        if (Plugin.Digits[n].WasPressedThisFrame) { MapDigit(n); break; }
                }
                else if (quick.AcceptsDigits)
                    for (var n = 0; n <= 9; n++)
                        if (Plugin.Digits[n].WasPressedThisFrame) { MenuPress(n); break; }
            }
            BuildMapStep();
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .2f;
            var active = UObject.FindObjectOfType<DeathmatchGameMode>();
            if (active == null || active.WasCollected || active.gameState != DeathmatchGameMode.ModeState.Running)
            { Reset(); mode = null; return; }
            var controlled = active.vehicleControlState?.TryCast<VehicleControlPlayerState>()?.vehicleController
                ?.ControlledVehicle?.TryCast<VehicleBehaviour>();
            if (!Usable(controlled) || controlled!.ControlType != ControlType.Player)
            { Reset(); mode = active; return; }
            if (mode?.Pointer != active.Pointer || player?.Pointer != controlled.Pointer)
            {
                var sceneChanged = mode?.Pointer != active.Pointer;
                Reset(); mode = active; player = controlled;
                followForward = player.transform.forward; followForward.y = 0; followForward = followForward.normalized;
                if (followForward.sqrMagnitude < .1f) followForward = Vector3.forward;
                lastLeaderPosition = player.Position;
                if (sceneChanged) { numbers.Clear(); names.Clear(); contacts.Clear(); gameOrdered.Clear(); DiscardMapTexture(); }
                CaptureBounds();
                // Build the topo map in the background so it is ready when opened.
                if (mapTexture == null && heightRow < 0) StartMapBuild();
            }
            if (Time.unscaledTime >= nextBoundsCapture) CaptureBounds();
            else RefreshBattleArea();
            if (Time.timeScale <= 0f) { pending = null; return; }
            Scan();
            var command = pending; pending = null;
            if (command != null) Execute(command);
            TrackLeaderHeading();
            KeepLeaderFormed();
            foreach (var unit in owned.Values.ToArray())
            {
                Tick(unit);
                if (owned.ContainsKey(unit.Tank.Pointer)) Diagnose(unit);
            }
        }
        catch (Exception ex)
        {
            Reset(); status = "Orders released after error; see BepInEx log.";
            Plugin.Warn("AI Orders released: " + ex);
        }
    }

    public void LateUpdate()
    {
        if (Stopped || !mapOpen || player == null || Time.timeScale <= 0f || !Application.isFocused || SprocketKeybinds.Keybinds.IsConfiguring || SprocketUiKit.UiKit.TextInputActive) return;
        try { ObserveMapDraw(); }
        catch (Exception ex) { CloseMap(); Plugin.Warn("AI Orders map diagnostics: " + ex); }
    }

    [HideFromIl2Cpp]
    private void MenuPress(int number)
    {
        if (pending != null) return;
        var action = quick.Press(number, menu.Screen == "units" ? menuUnits.Length : menuTargets.Length);
        if (action == "all") { Execute(action); return; }
        if (action == "close") { if (Plugin.Detailed) Plugin.Info("AI Orders quick HUD closed (0)."); return; }
        if (action == "map") { OpenMap(); return; }
        if (action == "units") { menuUnits = allies.ToArray(); return; }
        if (action == "targets") { menuTargets = enemies.ToArray(); return; }
        if (action?.StartsWith("unit:") == true)
        {
            var tank = menuUnits[int.Parse(action.Substring(5))];
            if (!Usable(tank) || !allies.Any(a => a.Pointer == tank.Pointer)) { status = "Unit no longer available."; return; }
            if (!selected.Add(tank.Pointer)) selected.Remove(tank.Pointer);
            return;
        }
        if (action?.StartsWith("target:") == true)
        {
            var enemy = menuTargets[int.Parse(action.Substring(7))];
            if (!Usable(enemy) || !enemies.Any(e => e.Pointer == enemy.Pointer)) { status = "Target no longer visible. Go back and reopen targets."; return; }
            target = enemy; pending = menu.TargetOrder; return;
        }
        if (action != null) pending = action;
    }

    [HideFromIl2Cpp]
    private void Scan()
    {
        allies.Clear(); enemies.Clear();
        var hostile = new List<VehicleBehaviour>();
        var spawns = mode!.vehicleSpawns;
        if (spawns == null) return;
        for (var i = 0; i < Math.Min(spawns.Count, 256); i++)
        {
            var gateway = spawns[i]?.Vehicle?.TryCast<IVehicleGateway>();
            var tank = gateway?.Behaviour?.TryCast<VehicleBehaviour>() ?? gateway?.transform?.GetComponentInChildren<VehicleBehaviour>();
            if (!Usable(tank) || tank!.Pointer == player!.Pointer) continue;
            if (tank.ID.TeamID.Equals(player.ID.TeamID))
            {
                if (tank.ControlType != ControlType.Player && tank.OrderReciever?.TryCast<CommanderAI>() != null)
                {
                    allies.Add(tank);
                    if (!numbers.ContainsKey(tank.Pointer)) numbers[tank.Pointer] = numbers.Count + 1;
                }
            }
            else hostile.Add(tank);
        }
        allies.Sort((a, b) => Number(a).CompareTo(Number(b)));
        // Resolve shared sight only after every friendly observer is known.
        foreach (var contact in contacts.Values) contact.Visible = false;
        foreach (var enemy in hostile)
        {
            if (!Visible(enemy)) continue;
            enemies.Add(enemy);
            if (!contacts.TryGetValue(enemy.Pointer, out var seen)) contacts[enemy.Pointer] = seen = new Contact { Tank = enemy };
            seen.Position = enemy.Position; seen.Seen = Time.time; seen.Visible = true;
        }
        foreach (var stale in contacts.Where(c => !c.Value.Visible && (Time.time - c.Value.Seen > 90f || !Usable(c.Value.Tank))).Select(c => c.Key).ToArray())
            contacts.Remove(stale);
        selected.RemoveWhere(p => !allies.Any(t => t.Pointer == p));
        foreach (var u in owned.Values.ToArray())
            if (!allies.Any(t => t.Pointer == u.Tank.Pointer)) Release(u, false);
        if (target != null && !enemies.Any(t => t.Pointer == target.Pointer)) target = null;
    }

    [HideFromIl2Cpp]
    private bool Visible(VehicleBehaviour tank)
    {
        foreach (var observer in allies.Append(player!))
            if (observer.OrderReciever?.TryCast<CommanderAI>() is { } commander && SeesTarget(commander, tank)) return true;
        return false;
    }

    [HideFromIl2Cpp]
    private int Number(VehicleBehaviour tank) => numbers.TryGetValue(tank.Pointer, out var n) ? n : 999;
    [HideFromIl2Cpp]
    private string Name(VehicleBehaviour tank)
    {
        if (names.TryGetValue(tank.Pointer, out var cached)) return cached;
        string? design = null;
        try { design = tank.VehicleInfo?.TryCast<Vehicle>()?.DesignInfo?.Name; } catch { }
        if (string.IsNullOrWhiteSpace(design)) design = tank.name;
        if (design!.Length > 22) design = design.Substring(0, 22);
        return names[tank.Pointer] = design;
    }

    // The installed Expedition already exposes this managed ingress internally.
    // Pin its exact bytes; refuse handoff on an unknown implementation.
    [HideFromIl2Cpp]
    private bool Handoff(VehicleBehaviour tank)
    {
        if (!bridgeResolved)
        {
            bridgeResolved = true;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType("SprocketExpedition.Game.CampaignEnemyHunt") != null);
            if (assembly != null)
            {
                // Check the signature, not a file hash: Expedition is rebuilt often and a pinned
                // hash silently blocked every order (0.4.0 in-game test, 2026-10-31).
                var handoff = assembly.GetType("SprocketExpedition.Game.CampaignEnemyHunt")!
                    .GetMethod("PlayerCommand", BindingFlags.Static | BindingFlags.NonPublic);
                var args = handoff?.GetParameters();
                if (handoff != null && handoff.ReturnType == typeof(void) && args!.Length == 2
                    && args[0].ParameterType == typeof(IVehicleBehaviour)
                    && args[1].ParameterType == typeof(Il2CppSystem.Collections.Generic.List<IOrderReceiver>))
                    expeditionHandoff = handoff;
                else
                {
                    Plugin.Warn("Expedition PlayerCommand(IVehicleBehaviour, List<IOrderReceiver>) not found; Expedition AI may override AI Orders in Expedition battles.");
                }
            }
        }
        if (expeditionHandoff != null)
        {
            var receivers = new Il2CppSystem.Collections.Generic.List<IOrderReceiver>();
            receivers.Add(tank.OrderReciever);
            expeditionHandoff.Invoke(null, new object?[] { player!.TryCast<IVehicleBehaviour>(), receivers });
        }
        return true;
    }

    [HideFromIl2Cpp]
    private VehicleBehaviour[] SelectedTanks() => allies.Where(a => selected.Contains(a.Pointer)).ToArray();

    // Takes (or keeps) ownership of one ally for a new order. Previous tasks,
    // tokens and formation membership are dropped; the queue is cleared.
    [HideFromIl2Cpp]
    private Unit? Claim(VehicleBehaviour tank, Kind kind)
    {
        var commander = tank.OrderReciever?.TryCast<CommanderAI>();
        if (commander == null || !commander.CanReceiveOrder)
        {
            Plugin.Warn($"Order refused for {Name(tank)}: {(commander == null ? "no CommanderAI" : "CanReceiveOrder=false")}.");
            return null;
        }
        gameOrdered.Remove(commander.Pointer);
        if (!owned.TryGetValue(tank.Pointer, out var unit))
        {
            if (!Handoff(tank)) { Plugin.Warn($"Order refused for {Name(tank)}: handoff failed."); return null; }
            unit = new Unit { Tank = tank, Commander = commander };
            owned.Add(tank.Pointer, unit);
        }
        Cancel(unit);
        LeaveFormation(unit);
        unit.Order = kind; unit.Queue.Clear(); unit.Failures = 0; unit.MoveRetries = 0; unit.BestDistance = float.PositiveInfinity; unit.Native = false; unit.RetryAt = 0;
        return unit;
    }

    [HideFromIl2Cpp]
    private void Execute(string command)
    {
        if (command == "all")
        {
            if (allies.Count > 0 && allies.All(a => selected.Contains(a.Pointer))) selected.Clear();
            else foreach (var a in allies) selected.Add(a.Pointer);
            return;
        }
        if (command == "none") { selected.Clear(); return; }
        var tanks = SelectedTanks();
        if (tanks.Length == 0) { status = "Select allies first."; return; }
        if (command == "Release")
        {
            var released = 0;
            foreach (var tank in tanks) if (owned.TryGetValue(tank.Pointer, out var u)) { Release(u, true); released++; }
            status = $"Released {released} unit(s) to their own AI.";
            Plugin.Info(status); return;
        }
        if (command == "FollowColumn" || command == "FollowLine") { Follow(tanks, command == "FollowColumn"); return; }
        if (!Enum.TryParse<Kind>(command, out var kind)) return;
        if ((kind == Kind.Attack || kind == Kind.Retreat) && target == null)
        { status = "Select a visible threat first."; return; }
        var count = 0;
        foreach (var tank in tanks)
        {
            if (kind == Kind.Retreat)
            {
                var threatDistance = FlatDistance(tank.Position, target!.Position);
                if (!float.IsFinite(threatDistance) || threatDistance < 1f)
                { Plugin.Warn("Retreat skipped: no valid threat direction for " + Name(tank)); continue; }
            }
            var unit = Claim(tank, kind);
            if (unit == null) continue;
            unit.Target = target;
            if (kind == Kind.Halt) { NativeHalt(unit); Hold(unit); }
            if (kind == Kind.Attack) Attack(unit, target!);
            if (kind == Kind.Retreat)
            {
                var p = OrderPolicy.Retreat(tank.Position.x, tank.Position.z, target!.Position.x, target.Position.z);
                unit.Order = Kind.Move;
                var away = new Vector3(p.X, tank.Position.y, p.Z) - tank.Position; away.y = 0;
                unit.Face = -away.normalized; // back up the slope facing the threat
                Move(unit, new Vector3(p.X, tank.Position.y, p.Z));
            }
            count++;
        }
        status = $"{kind}: {count} unit(s).";
        if (count > 0) quick.Complete();
        Plugin.Info(status);
    }

    [HideFromIl2Cpp]
    private void Attack(Unit unit, VehicleBehaviour enemy)
    {
        unit.Order = Kind.Attack; unit.Target = enemy;
        var info = new AttackOrderInfo(enemy.ID, AttackApproachType.Aggressive, AttackEngageMode.FireOnTheMove, 1f);
        Send(unit, new AttackOrder(ref info, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder));
        unit.Commander.FireMode = FireMode.FireAtWill;
        unit.Approaching = false; unit.CheckAt = 0;
    }

    // Native AttackOrder only engages a target the unit can already see. Like
    // GHPC, an attacked target that is out of sight is closed on: drive towards
    // its last known position until this unit sees it, then hand back to the
    // native engagement.
    [HideFromIl2Cpp]
    private void TickAttack(Unit unit)
    {
        if (!Usable(unit.Target)) { Halt(unit); return; }
        if (unit.Approaching && Unstick(unit)) return;
        if (Time.time < unit.CheckAt) return;
        unit.CheckAt = Time.time + 1.5f;
        var enemy = unit.Target!;
        var range = FlatDistance(unit.Tank.Position, enemy.Position);
        if (SeesTarget(unit.Commander, enemy) && range < 1800f)
        {
            if (!unit.Approaching) return;
            // In sight: stop the approach drive and re-arm the native engagement.
            CancelToken(unit.Commander.oneOffMoveOrderCts);
            unit.Moving = false; unit.Approaching = false;
            Attack(unit, enemy);
            unit.CheckAt = Time.time + 4f; // let the engagement settle before re-checking
            return;
        }
        var known = contacts.TryGetValue(enemy.Pointer, out var contact) ? contact.Position : enemy.Position;
        var toward = known - unit.Tank.Position; toward.y = 0;
        var gap = toward.magnitude;
        if (gap < 60f) return; // at the last known spot; keep searching with the native attack
        var goal = known - toward / gap * Math.Min(150f, gap * .3f);
        var nativeMoving = unit.Commander.driver?.CurrentTask?.TryCast<MoveToPositionTask>() != null
            && unit.Commander.oneOffMoveOrderCts is { IsCancellationRequested: false };
        if (!unit.Approaching || FlatDistance(goal, unit.Goal) > 50f || OrderPolicy.RetryEndedMove(Time.time - unit.SentAt, nativeMoving))
        {
            unit.Face = toward / gap;
            if (Move(unit, goal, keepAttack: true)) unit.Approaching = true;
        }
    }

    [HideFromIl2Cpp]
    private static bool SeesTarget(CommanderAI commander, VehicleBehaviour tank)
    {
        var buffer = commander?.visibleTargets;
        if (buffer?.visible == null) return false;
        for (var i = 0; i < Math.Min(buffer.visibleCount, buffer.visible.Length); i++)
        {
            var contact = buffer.visible[i];
            if (contact != null && contact.Valid && EntityID.FilterMatch(contact.EntityID, tank.ID)) return true;
        }
        return false;
    }

    // Detects a drive that has made no progress for 8 s (bogged, hung on an
    // obstacle, wedged against another tank) and backs up 15 m before the
    // caller re-paths. Returns true while the recovery manoeuvre runs.
    [HideFromIl2Cpp]
    private bool Unstick(Unit unit)
    {
        if (Time.time < unit.RecoverUntil) return true;
        var position = unit.Tank.Position;
        if (!unit.Moving || FlatDistance(position, unit.ProgressPos) > 4f)
        {
            if (unit.Moving && FlatDistance(position, unit.ProgressPos) > 15f) unit.Stucks = 0;
            unit.ProgressPos = position; unit.ProgressAt = Time.time; return false;
        }
        if (Time.time - unit.ProgressAt < 8f) return false;
        // Stopping to shoot is not being stuck.
        try { if (unit.Commander.HaltReason == HaltReason.Firing) { unit.ProgressAt = Time.time; return false; } } catch { }
        if (++unit.Stucks > 4)
        {
            Plugin.Warn($"{Name(unit.Tank)} stuck repeatedly; holding.");
            status = $"{Name(unit.Tank)} is stuck and holds position.";
            Halt(unit); return true;
        }
        var forward = unit.Tank.transform.forward; forward.y = 0; forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);
        var back = position - forward * 15f + right * (unit.Stucks % 2 == 1 ? 6f : -6f);
        if (Plugin.Detailed) Plugin.Info($"{Name(unit.Tank)} stuck at {position}; reversing (attempt {unit.Stucks}).");
        var keepAttack = unit.Order == Kind.Attack;
        if (keepAttack) { CancelToken(unit.Commander.oneOffMoveOrderCts); unit.Commander.driver?.ClearPath(); }
        else Cancel(unit);
        unit.Moving = false;
        try
        {
            var target = new PositionTarget(back, 4f, 0f);
            var task = new MoveToPositionTask(ref target) { ForceReverse = true, Priority = AITask.TopPriority };
            unit.Commander.driver?.AddTask(task);
        }
        catch (Exception ex) { Plugin.Warn("Reverse manoeuvre unavailable: " + ex.Message); }
        unit.RecoverUntil = Time.time + 5f;
        unit.ProgressPos = position; unit.ProgressAt = Time.time + 5f;
        if (keepAttack) unit.Approaching = false;
        return true;
    }

    // Native follow: the player's commander leads a native Line/Convoy
    // formation and each follower runs Sprocket's own FollowFormationDriveTask,
    // which steers continuously and matches the leader's speed. When the native
    // formation does not attach, the unit falls back to slot waypoints.
    [HideFromIl2Cpp]
    private void Follow(VehicleBehaviour[] tanks, bool column)
    {
        var leader = LeaderCommander();
        var native = false;
        if (leader?.formation != null)
        {
            try
            {
                var info = new JoinFormationOrderInfo(column ? FormationType.Convoy : FormationType.Line, player!.OrderReciever, JoinFormationOrderFlags.None);
                leader.Order(new JoinFormationOrder(ref info, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder));
                native = leader.formation.Leading || leader.formation.FormationInstructions != null;
                leaderReformAt = Time.time + .5f;
            }
            catch (Exception ex) { Plugin.Warn("Native formation leader unavailable: " + ex.Message); }
        }
        var count = 0;
        var slot = 0;
        foreach (var tank in tanks.OrderBy(t => FlatDistance(t.Position, player!.Position)))
        {
            var unit = Claim(tank, Kind.Follow);
            if (unit == null) continue;
            unit.Column = column; unit.FollowSlot = slot++;
            if (native)
            {
                var info = new JoinFormationOrderInfo(column ? FormationType.Convoy : FormationType.Line, player!.OrderReciever, JoinFormationOrderFlags.None);
                Send(unit, new JoinFormationOrder(ref info, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder));
                unit.Native = true; unit.CheckAt = Time.time + 2.5f;
            }
            count++;
        }
        status = $"Follow ({(column ? "column" : "line")}): {count} unit(s){(native ? "" : ", waypoint mode")}.";
        if (count > 0) quick.Complete();
        Plugin.Info(status);
    }

    [HideFromIl2Cpp]
    private CommanderAI? LeaderCommander()
    {
        try { return player?.OrderReciever?.TryCast<CommanderAI>(); }
        catch { return null; }
    }

    // A player-driven leader is not ticked like an AI leader; reform its slots
    // here so followers never target the leader's own position.
    [HideFromIl2Cpp]
    private void KeepLeaderFormed()
    {
        if (!owned.Values.Any(u => u.Order == Kind.Follow && u.Native) || Time.time < leaderReformAt) return;
        leaderReformAt = Time.time + 1f;
        try
        {
            var formation = LeaderCommander()?.formation;
            if (formation != null && formation.ReformRequired) formation.Reform();
        }
        catch (Exception ex) { Plugin.Warn("Leader reform: " + ex.Message); }
    }

    // Map/right-click move. Several units spread into a line abreast at the
    // goal; Shift appends a waypoint to each unit's queue instead.
    [HideFromIl2Cpp]
    private void MoveSelected(Vector3 point, bool queue)
    {
        var tanks = SelectedTanks();
        if (RefuseOrder(point, tanks.Length, queue ? MapClickAction.Waypoint : MapClickAction.Move)) return;
        var from = Vector3.zero;
        foreach (var t in tanks) from += t.Position;
        from /= tanks.Length;
        var spread = OrderPolicy.SpreadLine(point.x, point.z, from.x, from.z, tanks.Length);
        // Keep left-to-right order so paths do not cross.
        var right = Vector3.Cross(Vector3.up, (point - from).normalized);
        var ordered = tanks.OrderBy(t => Vector3.Dot(t.Position - from, right)).ToArray();
        var count = 0;
        for (var i = 0; i < ordered.Length; i++)
        {
            var tank = ordered[i];
            var goal = new Vector3(spread[i].X, point.y, spread[i].Z);
            if (queue && owned.TryGetValue(tank.Pointer, out var existing) && existing.Order == Kind.Move && existing.Moving)
            { existing.Queue.Add(goal); count++; continue; }
            var unit = Claim(tank, Kind.Move);
            if (unit == null) continue;
            var face = goal - tank.Position; face.y = 0;
            unit.Face = face.sqrMagnitude > 1f ? face.normalized : tank.transform.forward;
            Move(unit, goal);
            count++;
        }
        status = $"{(queue ? "Waypoint added" : "Move")}: {count} unit(s).";
        Plugin.Info(status);
        if (count > 0) ShowMapFeedback(point, OrderRefusal.None);
    }

    [HideFromIl2Cpp]
    private void AttackSelected(VehicleBehaviour enemy, Vector3? clicked = null)
    {
        var tanks = SelectedTanks();
        if (RefuseOrder(clicked ?? enemy.Position, tanks.Length, MapClickAction.Attack)) return;
        var count = 0;
        foreach (var tank in tanks)
        {
            var unit = Claim(tank, Kind.Attack);
            if (unit == null) continue;
            Attack(unit, enemy); count++;
        }
        target = enemy;
        status = count > 0 ? $"Attack {Name(enemy)}: {count} unit(s)." : "Select allies first.";
        Plugin.Info(status);
    }

    [HideFromIl2Cpp]
    private void Tick(Unit unit)
    {
        if (!Usable(unit.Tank) || unit.Tank.ControlType == ControlType.Player) { Release(unit, false); return; }
        switch (unit.Order)
        {
            case Kind.Attack:
                TickAttack(unit); return;
            case Kind.Halt:
                EnsureHold(unit); return;
            case Kind.Move:
                TickMove(unit); return;
            case Kind.Follow:
                if (unit.Native) TickNativeFollow(unit); else TickWaypointFollow(unit);
                return;
        }
    }

    [HideFromIl2Cpp]
    private void Halt(Unit unit)
    {
        Cancel(unit); LeaveFormation(unit);
        unit.Order = Kind.Halt; unit.Queue.Clear(); NativeHalt(unit); Hold(unit);
    }

    [HideFromIl2Cpp]
    private void TickMove(Unit unit)
    {
        if (Time.time < unit.RetryAt) { EnsureHold(unit); return; }
        if (Unstick(unit)) return;
        if (!unit.Moving) { Move(unit, unit.Goal); return; }
        var distance = FlatDistance(unit.Tank.Position, unit.Goal);
        if (distance < 12f)
        {
            if (unit.Queue.Count > 0) { var next = unit.Queue[0]; unit.Queue.RemoveAt(0); unit.BestDistance = float.PositiveInfinity; unit.MoveRetries = 0; var face = next - unit.Goal; face.y = 0; if (face.sqrMagnitude > 1f) unit.Face = face.normalized; Move(unit, next); }
            else Halt(unit);
            return;
        }
        var nativeMoving = unit.Commander.driver?.CurrentTask?.TryCast<MoveToPositionTask>() != null
            && unit.Commander.oneOffMoveOrderCts is { IsCancellationRequested: false };
        if (OrderPolicy.RetryEndedMove(Time.time - unit.SentAt, nativeMoving))
        {
            // Native task ended away from the goal (blocked path or stuck).
            if (distance < 30f && unit.Queue.Count == 0) { Halt(unit); return; }
            // Combat tasks pre-empt the drive task; only count retries without progress.
            if (distance < unit.BestDistance - 10f) unit.MoveRetries = 0;
            unit.BestDistance = Math.Min(unit.BestDistance, distance);
            if (++unit.MoveRetries > 3)
            {
                Plugin.Warn($"Move for {Name(unit.Tank)} gave up {distance:F0} m from goal.");
                status = $"{Name(unit.Tank)} cannot reach the waypoint.";
                Halt(unit); return;
            }
            Move(unit, unit.Goal);
        }
    }

    [HideFromIl2Cpp]
    private void TickNativeFollow(Unit unit)
    {
        if (Time.time < unit.CheckAt) return;
        unit.CheckAt = Time.time + 2f;
        var leaderFormation = LeaderCommander()?.formation;
        var formation = unit.Commander.formation;
        var attached = false;
        try
        {
            attached = leaderFormation != null && formation != null && formation.Following
                && FormationUtils.GetFormationLeader(formation)?.Pointer == leaderFormation.Pointer;
        }
        catch { }
        if (attached) { unit.Failures = 0; return; }
        if (++unit.Failures < 2) return;
        Plugin.Warn($"Native formation did not attach for {Name(unit.Tank)}; using waypoint follow.");
        Cancel(unit); LeaveFormation(unit);
        unit.Native = false;
    }

    [HideFromIl2Cpp]
    private void TickWaypointFollow(Unit unit)
    {
        if (Time.time < unit.RetryAt) { EnsureHold(unit); return; }
        if (Unstick(unit)) return;
        var velocity = player!.Velocity;
        var slot = OrderPolicy.FollowSlot(player.Position.x, player.Position.z, followForward.x, followForward.z,
            velocity.x, velocity.z, unit.FollowSlot, unit.Column);
        var goal = new Vector3(slot.X, unit.Tank.Position.y, slot.Z);
        var distance = FlatDistance(unit.Tank.Position, goal);
        var leaderMoving = velocity.sqrMagnitude > 1f;
        if (!leaderMoving && !OrderPolicy.FollowNeedsMove(distance, unit.Moving))
        { if (unit.Moving) { Cancel(unit); Hold(unit); } else EnsureHold(unit); return; }
        var nativeMoving = unit.Commander.driver?.CurrentTask?.TryCast<MoveToPositionTask>() != null
            && unit.Commander.oneOffMoveOrderCts is { IsCancellationRequested: false };
        if (!unit.Moving || OrderPolicy.RefreshMove(Time.time - unit.SentAt, FlatDistance(goal, unit.Goal)) || OrderPolicy.RetryEndedMove(Time.time - unit.SentAt, nativeMoving))
        { unit.Face = followForward; Move(unit, goal); }
    }

    [HideFromIl2Cpp]
    private void TrackLeaderHeading()
    {
        var moved = player!.Position - lastLeaderPosition; moved.y = 0;
        if (moved.sqrMagnitude < 4f) return;
        var forward = player.transform.forward; forward.y = 0;
        if (forward.sqrMagnitude > .01f)
        {
            var heading = OrderPolicy.BlendHeading(followForward.x, followForward.z, forward.x, forward.z);
            followForward = new Vector3(heading.X, 0, heading.Z);
        }
        lastLeaderPosition = player.Position;
    }

    [HideFromIl2Cpp]
    private bool Move(Unit unit, Vector3 point, bool keepAttack = false)
    {
        // Resolve actual ground before handing native pathfinding the waypoint.
        // A vehicle/tree above the ground must not conceal the terrain sample.
        var hits = Physics.RaycastAll(point + Vector3.up * 500f, Vector3.down, 1500f, ~0, QueryTriggerInteraction.Ignore);
        var terrain = hits.Where(h => h.collider != null && h.collider.TryCast<TerrainCollider>() != null).OrderBy(h => h.distance).ToArray();
        var reason = !InsideBounds(point) ? "map bounds" : terrain.Length == 0 ? "no terrain" : Vector3.Angle(terrain[0].normal, Vector3.up) > 25f ? "steep terrain" : null;
        if (reason != null)
        {
            Plugin.Warn($"{unit.Order} waypoint refused for {Name(unit.Tank)}: {reason}; point={point}; boundsFound={bounds != null}.");
            if (keepAttack) return false; // approach point unusable; the attack itself stays
            Cancel(unit);
            if (unit.Order == Kind.Follow) { unit.RetryAt = Time.time + 3f; Hold(unit); return false; }
            status = $"Waypoint refused for {Name(unit.Tank)}: {reason}.";
            unit.Order = Kind.Halt; unit.Queue.Clear(); NativeHalt(unit); Hold(unit); return false;
        }
        point = terrain[0].point;
        if (!keepAttack) Cancel(unit);
        else { if (unit.Hold != null && !unit.Hold.WasCollected) unit.Hold.Finished = true; unit.Hold = null; CancelToken(unit.Commander.oneOffMoveOrderCts); }
        var face = unit.Face.sqrMagnitude > .5f ? unit.Face : unit.Tank.transform.forward;
        face.y = 0;
        // Only a deliberate move ends facing its approach direction.
        var position = unit.Order == Kind.Move ? new PositionTarget(point, face.normalized, 10f, 0f) : new PositionTarget(point, 8f, 0f);
        Send(unit, new MoveToPositionOrder(ref position, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder));
        unit.Goal = point; unit.SentAt = Time.time; unit.Moving = true;
        unit.ProgressPos = unit.Tank.Position; unit.ProgressAt = Time.time;
        return true;
    }

    // A driver HaltTask alone does not stop the commander's own combat movement: in the 0.5.1
    // Expedition test a halted tank kept charging visible enemies at 10 m/s with HaltTask current.
    // The game's own Halt is a CombatOrder that sets the commander's HaltReason; send that too.
    [HideFromIl2Cpp]
    private void NativeHalt(Unit unit)
    {
        try { Send(unit, new CombatOrder(CombatOrderType.Halt, Order.HighestPriority, OrderCancellationType.CancelOnNextOrder)); }
        catch (Exception ex) { Plugin.Warn($"Native halt failed for {Name(unit.Tank)}: {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    private void Send(Unit unit, Order order)
    {
        issuing = unit.Commander.Pointer;
        try
        {
            unit.Commander.Order(order);
        }
        finally
        {
            issuing = IntPtr.Zero;
            if (!unit.Commander.WasCollected)
            {
                unit.MoveToken = unit.Commander.oneOffMoveOrderCts;
                unit.AttackToken = unit.Commander.attackOrderCts;
            }
        }
    }
    [HideFromIl2Cpp]
    private static void Cancel(Unit unit)
    {
        if (unit.Hold != null && !unit.Hold.WasCollected) unit.Hold.Finished = true;
        unit.Hold = null; unit.Moving = false;
        if (unit.Commander.WasCollected) return;
        CancelToken(unit.Commander.oneOffMoveOrderCts);
        CancelToken(unit.Commander.attackOrderCts);
        unit.Commander.driver?.ClearTasks(); unit.Commander.driver?.ClearPath();
    }
    // Native MoveToPosition only leaves a formation for a higher priority than
    // the join; ours are equal, so detach explicitly.
    [HideFromIl2Cpp]
    private static void LeaveFormation(Unit unit)
    {
        try
        {
            var formation = unit.Commander.WasCollected ? null : unit.Commander.formation;
            if (formation != null && formation.Lead != null) formation.SetLead(null);
        }
        catch (Exception ex) { Plugin.Warn("Leave formation: " + ex.Message); }
    }
    [HideFromIl2Cpp]
    private static void CancelToken(Il2CppSystem.Threading.CancellationTokenSource? token)
    {
        try { if (token != null && !token.WasCollected && !token.IsCancellationRequested) token.Cancel(); }
        catch (Exception ex) { Plugin.Warn("Cancellation channel unavailable: " + ex.Message); }
    }
    [HideFromIl2Cpp]
    private static void Hold(Unit unit)
    {
        unit.HeldAt = Time.time;
        unit.Hold = new HaltTask(3600f) { Priority = AITask.TopPriority };
        unit.Commander.driver?.AddTask(unit.Hold);
    }
    [HideFromIl2Cpp]
    private static void EnsureHold(Unit unit)
    {
        if (Time.time - unit.HeldAt < 1f) return;
        var current = unit.Commander.driver?.CurrentTask;
        if (unit.Hold == null || unit.Hold.WasCollected || unit.Hold.Finished || unit.Hold.CancellationRequested || current?.Pointer != unit.Hold.Pointer)
        { Cancel(unit); Hold(unit); }
    }
    [HideFromIl2Cpp]
    private void Release(Unit unit, bool leaveFormation)
    {
        owned.Remove(unit.Tank.Pointer);
        try
        {
            if (unit.Hold != null && !unit.Hold.WasCollected) unit.Hold.Finished = true;
            CancelToken(unit.MoveToken);
            CancelToken(unit.AttackToken);
            if (leaveFormation) LeaveFormation(unit);
        }
        catch (Exception ex) { Plugin.Warn("Cleanup: " + ex.Message); }
    }
    [HideFromIl2Cpp]
    internal void Reset()
    {
        if (owned.Count > 0) Plugin.Info($"Order session reset: releasing {owned.Count} unit(s); player or battle context changed.");
        foreach (var u in owned.Values.ToArray()) Release(u, true);
        CloseMap();
        ClearMapFeedback();
        boundsDiagnosed = false;
        mapKeyCheckFrame = -1;
        quick.MapClosed();
        selected.Clear(); allies.Clear(); enemies.Clear(); player = null; target = null; pending = null; quick.Close(); menuUnits = Array.Empty<VehicleBehaviour>(); menuTargets = Array.Empty<VehicleBehaviour>(); bounds = null;
    }
    [HideFromIl2Cpp]
    private void CaptureBounds()
    {
        nextBoundsCapture = Time.unscaledTime + 1f;
        var candidates = UObject.FindObjectsOfType<WorldBounds>()
            .Where(c => c != null && !c.WasCollected && c.gameObject.activeInHierarchy && c.gameObject.scene.name == mode!.gameObject.scene.name).ToArray();
        var smallest = float.PositiveInfinity;
        WorldBounds? best = null;
        foreach (var candidate in candidates)
        {
            var area = candidate.size.x * candidate.size.z;
            if (float.IsFinite(area) && area > 0 && area < smallest && ReadBattleArea(candidate).Contains(player!.Position.x, player.Position.y, player.Position.z, 16))
            { smallest = area; best = candidate; }
        }
        bounds = best;
        var changed = RefreshBattleArea();
        if (!Plugin.Detailed || (!changed && boundsDiagnosed)) return;
        boundsDiagnosed = true;
        Plugin.Info($"AI Orders map bounds: candidates={candidates.Length}; found={bounds != null}; scene={mode!.gameObject.scene.name}.");
        foreach (var candidate in candidates)
        {
            var t = candidate.transform; var centre = t.TransformPoint(candidate.centre); var scale = t.lossyScale;
            var size = candidate.size;
            var contains = ReadBattleArea(candidate).Contains(player!.Position.x, player.Position.y, player.Position.z, 16);
            Plugin.Info(FormattableString.Invariant($"AI Orders map bounds candidate: chosen={candidate.Pointer == bounds?.Pointer}; centre=({centre.x:F1}, {centre.y:F1}, {centre.z:F1}); size=({Math.Abs(size.x * scale.x):F1}, {Math.Abs(size.z * scale.z):F1}) m; yaw={t.eulerAngles.y:F1}; containsPlayer={contains}."));
        }
    }
    [HideFromIl2Cpp]
    private static BattleArea ReadBattleArea(WorldBounds value)
    {
        var m = value.transform.worldToLocalMatrix; var c = value.centre;
        var s = value.size; var scale = value.transform.lossyScale;
        return new BattleArea(value.Pointer, m.m00, m.m01, m.m02, m.m03 - c.x,
            m.m20, m.m21, m.m22, m.m23 - c.z, s.x, s.z, scale.x, scale.z);
    }
    [HideFromIl2Cpp]
    private bool RefreshBattleArea()
    {
        BattleArea? current = bounds == null || bounds.WasCollected ? null : ReadBattleArea(bounds);
        if (!MapPolicy.NeedsRebuild(battleArea, current)) return false;
        battleArea = current;
        DiscardMapTexture();
        if (player != null) StartMapBuild();
        return true;
    }
    [HideFromIl2Cpp]
    private void Diagnose(Unit unit)
    {
        if (!Plugin.Detailed || Time.time < unit.NextDiagnostic) return;
        unit.NextDiagnostic = Time.time + 10f;
        if (unit.Tank.WasCollected || unit.Commander.WasCollected) return;
        try
        {
            var driver = unit.Commander.driver;
            var task = driver?.CurrentTask?.GetIl2CppType()?.Name ?? "none";
            var speed = driver?.body?.linearVelocity.magnitude ?? 0f;
            var following = unit.Commander.formation?.Following ?? false;
            Plugin.Info($"Order state: unit={Name(unit.Tank)}; order={unit.Order}; native={unit.Native}; formationFollowing={following}; nativeTask={task}; haltReason={unit.Commander.HaltReason}; speed={speed:F1}; pos={unit.Tank.Position}; goal={unit.Goal}; queue={unit.Queue.Count}; moving={unit.Moving}.");
        }
        catch (Exception ex) { Plugin.Warn("Order diagnostics unavailable: " + ex.Message); }
    }
    [HideFromIl2Cpp]
    private bool InsideBounds(Vector3 point)
    {
        return battleArea?.Contains(point.x, point.y, point.z, 16) ?? false;
    }
    [HideFromIl2Cpp]
    internal bool Allows(CommanderAI commander, Order order) => !Controls(commander) || commander.Pointer == issuing;
    [HideFromIl2Cpp]
    internal bool Controls(CommanderAI commander) => owned.Values.Any(u => u.Commander.Pointer == commander.Pointer && Usable(u.Tank) && u.Tank.ControlType != ControlType.Player);
    [HideFromIl2Cpp]
    internal bool SuppressesFormation(CommanderAI commander) =>
        owned.Values.Any(u => u.Commander.Pointer == commander.Pointer && !(u.Order == Kind.Follow && u.Native) && Usable(u.Tank) && u.Tank.ControlType != ControlType.Player);
    [HideFromIl2Cpp]
    internal void NativeCommand(Il2CppSystem.Collections.Generic.List<IOrderReceiver>? receivers)
    {
        if (receivers == null) return;
        for (var i = 0; i < Math.Min(receivers.Count, 256); i++) if (receivers[i] != null) gameOrdered.Add(receivers[i].Pointer);
        foreach (var u in owned.Values.ToArray())
            for (var i = 0; i < Math.Min(receivers.Count, 256); i++)
                if (receivers[i]?.Pointer == u.Commander.Pointer) { Plugin.Info("Native player command takes over " + Name(u.Tank)); Release(u, false); break; }
    }
    [HideFromIl2Cpp]
    private static bool Usable(VehicleBehaviour? tank)
    {
        if (tank == null || tank.WasCollected) return false;
        try
        {
            if (!tank.gameObject.activeInHierarchy || !tank.enabled || (tank.Flags & VehicleFlags.Enabled) == 0) return false;
            if (tank.TryGetModule<VehicleCrewWorkspaceModule>(out var m) && m?.operators != null && m.operators.Count > 0)
            {
                for (var i = 0; i < m.operators.Count; i++)
                    if (m.operators[i]?.internalOperator == null || m.operators[i].internalOperator.HealthFraction > 0f) return true;
                return false;
            }
            return true;
        }
        catch { return false; }
    }
    [HideFromIl2Cpp]
    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
}
