using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using Sprocket.DamageModelling;
using Il2CppSystem.Runtime.Serialization;
using UnityEngine;

namespace SprocketCarouselAutoloader;

internal sealed class BustleState
{
    internal readonly AmmoRack Rack;
    internal readonly LoaderKind Kind;
    internal bool Semi => Kind == LoaderKind.Semi;
    internal bool Revolver => Kind == LoaderKind.Revolver;
    internal bool Enabled = true;
    internal bool MirrorFeedArm;
    internal int CannonId = -1;
    // The part JSON has the same date; this check covers imported/saved parts
    // that bypassed the native part picker.
    internal bool DateAllowed = true;
    internal int DateKey = int.MinValue;
    internal DateTime? VehicleDate;
    internal bool Active => Enabled && BustleRuntime.Allowed(this);
    internal VehicleComponentPath? Path;
    internal int PathCannonId = -1;
    internal bool Syncing;
    internal bool AutoAssign = true;
    internal float AddedMechanismMass;
    internal bool Refilling;
    internal double RefillProgress;
    internal IntPtr RefillSource;
    internal int RefillType=-1;
    internal int RefillFrame = -1;
    internal string RefillStatus = "";
    // 0.2.26: refill state for the log/inspector/HUD, the per-round time in use, and the revolver crew point.
    internal RefillReason RefillReason = RefillReason.None;
    internal int RefillDetail;
    internal double RefillSecondsPerRound;
    internal bool CrewPointApplied;
    internal float HudNext;
    internal float HudPlayerCheck;
    internal bool HudPlayer;
    internal AmmoRack? RefillRack;
    internal VehicleComponentPath? RefillPath;
    // Revolver-only refill without a crew loader. Kept apart from the crew path
    // so a loader appearing or disappearing cannot inherit partial progress.
    internal double NoLoaderProgress;
    internal int NoLoaderType = -1;
    internal AmmoRack? NoLoaderRack;
    internal bool NoLoaderActive;
    internal double StillSeconds;
    internal Rigidbody? BodyCache;
    internal int EligibleFrame = -1, DistanceFrame = -1, ReloadFrame = -1, RawFrame = -1;
    internal IntPtr CachedCannon;
    internal bool CachedEligible;
    internal int NativeCapacity=-1;
    internal double CachedDistance, CachedSeconds, CachedRawSeconds;
    // 0.2.24: two-piece ammunition (bustle and revolver, saved per rack) and the cached feed arm,
    // recomputed after a rack build, an assignment or a toggle; never per frame.
    internal bool TwoPiece;
    internal bool TwoPieceActive => TwoPiece && !Semi;
    internal bool FeedValid, FeedKnown;
    internal IntPtr FeedCannon;
    internal FeedArm Feed;
    internal Spec? FeedSpec;
    internal BustleState(AmmoRack rack)
    {
        Rack = rack;
        Kind = rack.VehicleObject?.GUID == SemiAutoloader.PartGuid ? LoaderKind.Semi :
            rack.VehicleObject?.GUID == BustleRuntime.RevolverPartGuid ? LoaderKind.Revolver : LoaderKind.Bustle;
    }
    internal Rigidbody? VehicleBody()
    {
        if (BodyCache != null) return BodyCache;
        try { BodyCache = Rack.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.Rigidbody; }
        catch { BodyCache = null; }
        return BodyCache;
    }
}

internal static class BustleRuntime
{
    internal const string PartGuid = "a4214ee4-45cf-42ab-80f7-5e6c389513a3";
    internal const string RevolverPartGuid = "3189ea9f-9b42-4204-977f-8cebdcf4f77c";
    private const string EnabledKey = "sprocketBustleEnabledV1", CannonKey = "sprocketBustleCannonV1";
    private const string MirrorKey = "sprocketBustleMirrorFeedArmV1";
    private const string TwoPieceKey = "sprocketBustleTwoPieceV1";
    private static readonly Dictionary<IntPtr, BustleState> States = new();
    private static readonly HashSet<string> Warnings = new();
    internal static bool Allowed(BustleState state)
    {
        try
        {
            var date = CarouselRuntime.Date(state.Rack);
            int key = date is { } d ? d.Year * 10000 + d.Month * 100 + d.Day : int.MinValue;
            if (state.DateKey == key) return state.DateAllowed;
            state.DateKey = key; state.VehicleDate = date; state.DateAllowed = true;
            if (date is { } vehicle)
            {
                var from = CarouselDatePolicy.For(state.Kind);
                state.DateAllowed = CarouselDatePolicy.Allows(vehicle, from);
                if (!state.DateAllowed && Warnings.Add("date " + state.Rack.Pointer + " " + key))
                {
                    var label = state.Semi ? "Semi-autoloader" : state.Revolver ? "Revolver" : "Bustle";
                    Plugin.ModLog.LogWarning(
                        $"[{label}] Needs {from:yyyy-MM-dd}; vehicle date is " +
                        $"{vehicle:yyyy-MM-dd}. Saved part retained; native manual loading used.");
                }
            }
            return state.DateAllowed;
        }
        catch (Exception ex)
        {
            if (Warnings.Add("date check " + state.Rack.Pointer))
                Plugin.ModLog.LogWarning("[Autoloader] Date check unavailable; loader disabled: " + ex.Message);
            return false;
        }
    }
    [HarmonyPostfix, HarmonyPatch(typeof(Sprocket.Vehicles.PartImporting.PartDefinitionIO),
        nameof(Sprocket.Vehicles.PartImporting.PartDefinitionIO.DeserializePartDefinitionJSON))]
    private static void PartImported(Sprocket.PartImporting.PartDefinition __result)
    {
        try
        {
            if (__result?.guid == PartGuid)
                Plugin.ModLog.LogDebug($"[Bustle] Native part importer recognized '{__result.name}', {__result.components.Length} components, GUID {PartGuid}.");
        }
        catch (Exception ex) { HookGuard.Warn("PartDefinitionIO.DeserializePartDefinitionJSON", -1, ex); }
    }
    private static int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list) =>
        list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
    internal static bool IsPart(AmmoRack rack) =>
        rack.VehicleObject?.GUID == PartGuid || rack.VehicleObject?.GUID == SemiAutoloader.PartGuid ||
        rack.VehicleObject?.GUID == RevolverPartGuid;
    internal static BustleState State(AmmoRack rack)
    {
        if (!States.TryGetValue(rack.Pointer, out var state)) States[rack.Pointer] = state = new(rack);
        return state;
    }
    internal static IEnumerable<BustleState> Racks(Cannon cannon)
    {
        return VehicleLoadIndex.Get(cannon).Bustles;
    }
    internal static void InvalidateRuntimeCaches()
    {
        foreach (var state in States.Values)
        {
            state.EligibleFrame = state.DistanceFrame = state.ReloadFrame = state.RawFrame = -1;
            state.RefillRack = null; state.RefillPath = null;
            state.NoLoaderRack = null; state.BodyCache = null;
            state.FeedValid = false;
        }
    }
    internal static bool Eligible(BustleState state, Cannon cannon)
    {
        if (state.Semi && !SemiAutoloader.Available) return false;
        // Live health/installation checks still precede cached spatial checks.
        if (!state.Active || state.CannonId != cannon.VUID.Value) return false;
        if (!state.Rack.IsInstalled || state.Rack.HealthFraction <= 0) return Refuse(state, cannon, 1);
        if(Capacity(state,cannon)<=0) return Refuse(state, cannon, 2);
        if (state.EligibleFrame == Time.frameCount && state.CachedCannon == cannon.Pointer) return state.CachedEligible;
        state.EligibleFrame = Time.frameCount; state.CachedCannon = cannon.Pointer;
        // Same checks and order as 0.2.23; the reason is only used for the one-time Info line.
        int refusal = !(state.Rack.Capacity > 0) ? 3 :
            state.Rack.ShellSizeBlueprintID != cannon.ShellSizeBlueprintID ? 4 :
            !CarouselRuntime.SameTurret(state.Rack, cannon) ? 5 :
            !(Distance(state, cannon) < FeedLimit(state)) ? 6 :
            state.Revolver && !RevolverFits(state, cannon) ? 7 : 0;
        state.CachedEligible = refusal == 0;
        if (refusal != 0) Refuse(state, cannon, refusal);
        return state.CachedEligible;
    }
    // 0.2.24: one Info line per rack (VUID) and reason when an assigned, enabled bustle or revolver
    // does not load (the BepInEx disk log does not keep Debug). Semi is unchanged and silent.
    private static readonly HashSet<(int Rack, int Reason)> Refusals = new();
    private static bool Refuse(BustleState state, Cannon cannon, int reason)
    {
        if (state.Semi || !Refusals.Add((state.Rack.VUID.Value, reason))) return false;
        try
        {
            string text = reason switch
            {
                1 => "the rack is not installed or is destroyed",
                2 => CapacityReason(state, cannon),
                3 => "the rack holds no rounds",
                4 => "its ammunition slot differs from the cannon's",
                5 => "it is not in the cannon's turret",
                6 => $"the feed arm is {state.CachedDistance:0.00} m from the breech, limit {FeedLimit(state):0.00} m (config Bustle.MaxFeedDistance)",
                _ => "the ammunition is outside the revolver limits",
            };
            Plugin.ModLog.LogInfo($"[{(state.Revolver ? "Revolver" : "Bustle")}] Rack {state.Rack.VUID.Value} does not load: {text}. " +
                "No manual fallback while this loader is assigned and enabled. Logged once per rack and reason.");
        }
        catch { /* Logging must never change loading. */ }
        return false;
    }
    internal static string CapacityReason(BustleState state, Cannon cannon)
    {
        var size = ShellLoaderGeometry.Read(cannon);
        if (!size.Valid) return "no valid shell geometry";
        var stored = StoredLengthMm(state, size);
        if (state.Revolver)
        {
            var bp = state.Rack.Blueprint;
            var drum = Drum(state.Rack, cannon, state.TwoPieceActive);
            return RevolverPolicy.Refusal(size.DiameterMm, stored, bp?.Length ?? 0, drum.Chambers, state.TwoPieceActive) ?? "no complete round fits";
        }
        return state.TwoPieceActive ? $"no pair of {stored:0} mm pieces fits" : $"no complete {stored:0} mm round fits";
    }
    // Complete round, or with two-piece ammunition the longest piece (mm, as the packer sees it).
    internal static double StoredLengthMm(BustleState state, LoaderShellSize size)
    {
        if (!state.TwoPieceActive) return Math.Ceiling(size.FullLengthMm);
        var (p, c) = TwoPiecePolicy.Pieces(size.Status, size.ProjectileMm, size.ChargeMm, size.DiameterMm);
        return TwoPiecePolicy.LongestMm(p, c);
    }
    // Bustle and Semi keep exactly the native fit (ShellLoaderGeometry.RackCapacity). The revolver is
    // limited to its single drum (0.2.23) and to its configurable limits (0.2.24: a refused revolver
    // holds nothing, so InvalidAssignedFeed blocks instead of a silent manual fallback). Two-piece
    // racks count complete rounds from pairs of pieces.
    internal static int Capacity(BustleState state, Cannon cannon)
    {
        if (state.TwoPieceActive) return TwoPieceCapacity(state, ShellLoaderGeometry.Read(cannon));
        var native = ShellLoaderGeometry.RackCapacity(state.Rack, cannon, state.NativeCapacity);
        if (!state.Revolver) return native;
        var size = ShellLoaderGeometry.Read(cannon);
        return size.Valid && RevolverPolicy.Fits(size.DiameterMm, size.FullLengthMm) &&
            LoaderPhysicalFit.NativePackDimensions(size.ProjectileMm, size.ChargeMm, size.DiameterMm, out var d, out var len)
            ? DrumCapacity(state.Rack, native, d, len) : 0;
    }
    // floor(pieces / 2) with the longest piece in the native packer (and, for the revolver, in the drum).
    // This may exceed the native full-round capacity: the rack stores complete rounds with its native
    // size/type identity, only the count follows the pieces.
    internal static int TwoPieceCapacity(BustleState state, LoaderShellSize size)
    {
        var bp = state.Rack.Blueprint;
        if (bp == null || !size.Valid || !double.IsFinite(size.DiameterMm) || size.DiameterMm <= 0 || size.DiameterMm > ushort.MaxValue) return 0;
        var (p, c) = TwoPiecePolicy.Pieces(size.Status, size.ProjectileMm, size.ChargeMm, size.DiameterMm);
        int longest = TwoPiecePolicy.LongestMm(p, c);
        if (longest <= 0 || longest > ushort.MaxValue) return 0;
        var d = (ushort)Math.Ceiling(size.DiameterMm);
        int pieces = AmmoRackUtils.CalculateCapacity(bp.Width, bp.Height, bp.Length, d, (ushort)longest);
        if (state.Revolver)
        {
            if (!RevolverPolicy.Fits(size.DiameterMm, longest)) return 0;
            pieces = Math.Min(pieces, RevolverDrum.Capacity(bp.Width, bp.Height, bp.Length, d, longest));
        }
        return TwoPiecePolicy.Shots(pieces);
    }
    internal static int DrumCapacity(AmmoRack rack, int native, double diameterMm, double roundLengthMm)
    {
        var bp = rack.Blueprint;
        return bp == null ? 0 : RevolverDrum.Limit(native, bp.Width, bp.Height, bp.Length, diameterMm, roundLengthMm);
    }
    internal static RevolverDrum.Layout Drum(AmmoRack rack, Cannon cannon, bool twoPiece = false)
    {
        var size = ShellLoaderGeometry.Read(cannon);
        var bp = rack.Blueprint;
        if (bp == null || !size.Valid || !LoaderPhysicalFit.NativePackDimensions(size.ProjectileMm, size.ChargeMm, size.DiameterMm, out var d, out var len))
            return RevolverDrum.Plan(0, 0, 0, 0, 0);
        if (twoPiece)
        {
            var (p, c) = TwoPiecePolicy.Pieces(size.Status, size.ProjectileMm, size.ChargeMm, size.DiameterMm);
            len = (ushort)Math.Min(ushort.MaxValue, TwoPiecePolicy.LongestMm(p, c));
        }
        return RevolverDrum.Plan(bp.Width, bp.Height, bp.Length, d, len);
    }
    internal static double MaximumDistance => CannonBreech.MinimumOperateHandDistance;
    // Feed reach: native crew hand reach, or config Bustle.MaxFeedDistance (> 0) for bustle and revolver.
    internal static double FeedLimit(BustleState state) =>
        !state.Semi && Plugin.MaxFeedDistance > 0 ? Plugin.MaxFeedDistance : MaximumDistance;
    private static bool RevolverFits(BustleState state, Cannon cannon)
    {
        var physical = ShellLoaderGeometry.Read(cannon);
        // An unknown or invalid physical envelope must not silently use a proxy.
        if (!physical.Valid || !double.IsFinite(physical.FullLengthMm) || physical.FullLengthMm <= 0) return false;
        var stored = StoredLengthMm(state, physical);
        if (RevolverPolicy.Fits(physical.DiameterMm, stored)) return true;
        // Keyed by VUID (0.2.24): the pointer changes on every spawn.
        if (Warnings.Add("revolver fit " + state.Rack.VUID.Value))
            Plugin.ModLog.LogWarning(
                $"[Revolver] Rack {state.Rack.VUID.Value}: limit {RevolverPolicy.MaxCaliberMm:0} mm calibre" +
                (RevolverPolicy.MaxRoundLengthMm > 0 ? $" and {RevolverPolicy.MaxRoundLengthMm:0} mm round length" : "") +
                $" (config Revolver.MaxCaliberMm / MaxRoundLengthMm); this cannon is {physical.DiameterMm:0.#} mm / {stored:0.#} mm. " +
                "The loader does not load and there is no manual fallback while it is assigned and enabled; disable or unassign it to load manually.");
        return false;
    }
    internal static bool HasDesign(Cannon cannon) => Racks(cannon).Any(s => !s.Semi && Eligible(s, cannon));
    // Load point of the cannon in rack-local metres (bounds centre), via the same transforms the native
    // path and the crew point use; the native path's B end is taken when its A end proves world space.
    internal static bool LoadPoint(AmmoRack rack, Cannon cannon, out UnityEngine.Vector3 local)
    {
        local = default;
        try
        {
            var centre = rack.boundsCollider?.Centre ?? UnityEngine.Vector3.zero;
            var world = cannon.transform.TransformPoint(cannon.LoadLocalPosition);
            var path = new VehicleComponentPath(rack, cannon);
            path.aLocalPosition = centre;
            path.bLocalPosition = cannon.LoadLocalPosition;
            path.Update();
            if ((path.APosition - rack.transform.TransformPoint(centre)).sqrMagnitude < 1e-4f) world = path.BPosition;
            local = rack.transform.InverseTransformPoint(world) - centre;
            return float.IsFinite(local.x) && float.IsFinite(local.y) && float.IsFinite(local.z) && local.magnitude < 50f;
        }
        catch { return false; }
    }
    // Model input shared by the visual and the feed arm: rack bounds, true round (or piece) sizes,
    // and the cannon's load point when it is known.
    internal static Spec? ModelSpec(BustleState state, Cannon? assigned, int capacity)
    {
        var rack = state.Rack;
        var shell = assigned == null ? new LoaderShellSize(0, rack.projectileSizeID.Caliber*3.0,
            rack.projectileSizeID.PropellantLength, rack.projectileSizeID.Caliber) : ShellLoaderGeometry.Read(assigned);
        if (!shell.Valid) return null;
        var (p, c) = state.TwoPieceActive ? TwoPiecePolicy.Pieces(shell.Status, shell.ProjectileMm, shell.ChargeMm, shell.DiameterMm)
            : (shell.ProjectileMm, shell.ChargeMm);
        var bp = rack.Blueprint; var d = rack.boundsSize;
        UnityEngine.Vector3 load = default;
        bool known = assigned != null && LoadPoint(rack, assigned, out load);
        return new Spec(state.Revolver ? "Revolver" : "Bustle", state.Revolver ? ModelKind.Revolver : ModelKind.Bustle,
            bp != null ? bp.Width / 1000.0 : d.x, bp != null ? bp.Height / 1000.0 : d.y, bp != null ? bp.Length / 1000.0 : d.z,
            shell.DiameterMm, p, c, capacity, state.MirrorFeedArm, state.TwoPieceActive,
            known ? load.x : double.NaN, known ? load.y : double.NaN, known ? load.z : double.NaN);
    }
    // Cached feed arm (bustle/revolver). The model is built from FeedSpec, so arm and measuring point coincide.
    internal static FeedArm Feed(BustleState state, Cannon? cannon)
    {
        var key = cannon?.Pointer ?? IntPtr.Zero;
        if (state.FeedValid && state.FeedCannon == key) return state.Feed;
        state.FeedValid = true; state.FeedCannon = key;
        try { state.FeedSpec = ModelSpec(state, cannon, 0); }
        catch { state.FeedSpec = null; }
        state.FeedKnown = state.FeedSpec != null;
        state.Feed = state.FeedKnown ? FeedArmGeometry.For(state.FeedSpec!) : default;
        return state.Feed;
    }
    internal static UnityEngine.Vector3 FeedLocalPosition(AmmoRack rack, Cannon? cannon = null)
    {
        var state = State(rack);
        if (!state.Semi && cannon != null)
        {
            var feed = Feed(state, cannon);
            if (state.FeedKnown && feed.Auto)
                return (rack.boundsCollider?.Centre ?? UnityEngine.Vector3.zero) +
                    new UnityEngine.Vector3((float)feed.MeasureX, (float)feed.MeasureY, (float)feed.MeasureZ);
        }
        var outlet = state.Semi ? SemiAssistPolicy.Outlet(rack.boundsSize.x,rack.boundsSize.z,rack.projectileSizeID.Caliber,state.MirrorFeedArm) :
            BustleFeedGeometry.Outlet(rack.boundsSize.x, rack.boundsSize.z, rack.projectileSizeID.Caliber, state.MirrorFeedArm);
        return (rack.boundsCollider?.Centre ?? UnityEngine.Vector3.zero) +
            new UnityEngine.Vector3((float)outlet.X, (float)outlet.Y, (float)outlet.Z);
    }
    internal static double Distance(BustleState state, Cannon cannon)
    {
        if (state.DistanceFrame == Time.frameCount && state.PathCannonId == cannon.VUID.Value) return state.CachedDistance;
        if (state.Path == null || state.PathCannonId != cannon.VUID.Value)
        {
            state.Path = new VehicleComponentPath(state.Rack, cannon);
            state.PathCannonId = cannon.VUID.Value;
        }
        state.Path.aLocalPosition = FeedLocalPosition(state.Rack, cannon);
        state.Path.bLocalPosition = cannon.LoadLocalPosition;
        state.Path.Update();
        state.DistanceFrame = Time.frameCount;
        return state.CachedDistance = state.Path.Distance;
    }
    // The plain bustle curve for the ammunition in the gun (no revolver or two-piece factor), cached per frame.
    private static double RawSeconds(BustleState state, Cannon cannon)
    {
        if (state.RawFrame == Time.frameCount) return state.CachedRawSeconds;
        state.RawFrame = Time.frameCount;
        var shell = cannon.ShellBlueprint;
        var task=cannon.Behaviour?.LoadTask?.TryCast<LoadTask>();
        var type=task?.rack?.TryCast<AmmoRackBehaviour>()?.StoredTypeID.Value??task?.Request.TypeID.Value;
        var physical=type.HasValue?ShellLoaderGeometry.ReadForType(cannon,type.Value):ShellLoaderGeometry.Read(cannon);
        return state.CachedRawSeconds = BustleTiming.ReloadSeconds(shell.Diameter, shell.Mass,
            physical.FullLengthMm, 0);
    }
    // 0.2.25: the bustle cycle without the revolver factor, with the two-piece factor. The revolver refill
    // is computed from this, so the faster revolver fire cycle never speeds up the refill.
    internal static double BaseSeconds(BustleState state, Cannon cannon) =>
        RevolverPolicy.BaseCycleSeconds(RawSeconds(state, cannon), state.TwoPieceActive);
    internal static double Seconds(BustleState state, Cannon cannon)
    {
        if (state.ReloadFrame == Time.frameCount) return state.CachedSeconds;
        state.ReloadFrame = Time.frameCount;
        // Revolver factor (0.2.25: 0.6 x bustle), then two ram strokes per complete round (0.2.24, x 1.25).
        return state.CachedSeconds = RevolverPolicy.FireCycleSeconds(RawSeconds(state, cannon), state.Revolver, state.TwoPieceActive);
    }

    internal static void Assign(BustleState state, Cannon? cannon)
    {
        VehicleLoadIndex.Invalidate();
        state.CannonId = cannon?.VUID.Value ?? -1;
        state.AutoAssign = false;
        state.Path = null;
        state.FeedValid = false;
        if (cannon != null)
        {
            // One automatic feed per cannon: make the designer's most recent selection authoritative.
            foreach (var other in Racks(cannon))
                if (other.Rack.Pointer != state.Rack.Pointer && other.CannonId == state.CannonId)
                { other.CannonId = -1; other.AutoAssign = false; }
            foreach (var basket in CarouselRuntime.Baskets(cannon))
            {
                var carousel = CarouselRuntime.State(basket);
                if (carousel.CannonId != state.CannonId) continue;
                carousel.CannonId = -1;
                CarouselRuntime.Invalidate(carousel);
                basket.RequestRebuild();
                CarouselInspector.Redraw(basket);
            }
            Sync(state, cannon);
        }
        state.Rack.RequestRebuild();
    }
    private static void Sync(BustleState state, Cannon cannon)
    {
        if (state.Syncing || !state.Active) return;
        state.Syncing = true;
        try
        {
            var shell = cannon.ShellBlueprint;
            if (shell == null || shell.GeneratedProjectileBlueprint == null || Count(shell.GeneratedProjectileBlueprint) == 0) return;
            state.Rack.Blueprint.ShellSlotBlueprintID = cannon.ShellSizeBlueprintID;
            state.Rack.Blueprint.ShellTypeGuid = shell.GeneratedProjectileBlueprint[0].ProjectileTypeGuid;
            if (state.Rack.ShellSizeBlueprintID != cannon.ShellSizeBlueprintID)
                state.Rack.shellSlotBlueprintSlot.Load(cannon.ShellSizeBlueprintID);
            state.Rack.SyncWithShellSlotBlueprint(state.Rack.Blueprint, shell);
        }
        finally { state.Syncing = false; }
    }
    [HarmonyPrefix, HarmonyPatch(typeof(AmmoRack), nameof(AmmoRack.Build))]
    private static void PrepareRack(AmmoRack __instance)
    {
        try { if (!IsPart(__instance)) return; }
        catch (Exception ex) { HookGuard.Warn("AmmoRack.Build prepare", -1, ex); return; }
        CarouselRuntime.Guard("Prepare bustle rack", () =>
        {
            var state = State(__instance);
            // Remove our previous contribution before vanilla rebuild, then add it to the resulting baseline.
            __instance.SetMass(Math.Max(0, __instance.GetMass(Sprocket.Vehicles.MassType.Mechanisms) - state.AddedMechanismMass),
                Sprocket.Vehicles.MassType.Mechanisms);
            state.AddedMechanismMass = 0;
            var cannons = CarouselRuntime.Cannons(__instance);
            if (state.AutoAssign && state.CannonId < 0 && cannons.Count == 1 &&
                !Racks(cannons[0]).Any(s => s.Rack.Pointer != __instance.Pointer && s.Enabled && s.CannonId == cannons[0].VUID.Value) &&
                !CarouselRuntime.Baskets(cannons[0]).Any(b => CarouselRuntime.State(b).Enabled && CarouselRuntime.State(b).CannonId == cannons[0].VUID.Value))
                state.CannonId = cannons[0].VUID.Value;
            if (cannons.Count > 0) state.AutoAssign = false;
            var cannon = cannons.FirstOrDefault(c => c.VUID.Value == state.CannonId);
            if (cannon != null) Sync(state, cannon);
        });
    }
    [HarmonyPostfix, HarmonyPatch(typeof(AmmoRack), nameof(AmmoRack.Build))]
    private static void RackMass(AmmoRack __instance)
    {
        try { if (!IsPart(__instance)) return; }
        catch (Exception ex) { HookGuard.Warn("AmmoRack.Build mass", -1, ex); return; }
        CarouselRuntime.Guard("Bustle mechanism mass", () =>
        {
            var state = State(__instance);
            state.NativeCapacity=__instance.Capacity;
            state.FeedValid=false;
            var assigned=CarouselRuntime.Cannons(__instance).FirstOrDefault(c=>c.VUID.Value==state.CannonId);
            if(assigned!=null)
            {
                var capacity=Capacity(state,assigned);
                __instance.capacity=capacity;
                if(__instance.behaviour!=null)
                {
                    __instance.behaviour.Capacity=(ushort)capacity;
                    if(__instance.behaviour.AmountStored>capacity) __instance.behaviour.amountStored=(ushort)capacity;
                }
            }
            else if(state.Revolver || state.TwoPieceActive)
            {
                // An unassigned drum (or two-piece rack) also holds only what it takes with the native
                // size encoding. A saved vehicle with more rounds is clamped; no rounds are created.
                var size=__instance.projectileSizeID;
                var capacity=state.TwoPieceActive
                    ? TwoPieceCapacity(state,new LoaderShellSize(0,size.Caliber*3.0,size.PropellantLength,size.Caliber))
                    : DrumCapacity(__instance,__instance.Capacity,size.Caliber,size.ShellLength);
                __instance.capacity=capacity;
                if(__instance.behaviour!=null)
                {
                    __instance.behaviour.Capacity=(ushort)capacity;
                    if(__instance.behaviour.AmountStored>capacity) __instance.behaviour.amountStored=(ushort)capacity;
                }
            }
            state.AddedMechanismMass = state.Active
                ? (float)(state.Revolver ? RevolverPolicy.MechanismMassKg(__instance.Capacity) : BustleTiming.MechanismMassKg(__instance.Capacity))
                : 0;
            __instance.SetMass(__instance.GetMass(Sprocket.Vehicles.MassType.Mechanisms) + state.AddedMechanismMass,
                Sprocket.Vehicles.MassType.Mechanisms);
        });
    }
    // 0.2.28 (M3): part of the merged Cannon.Build postfix (CarouselRuntime.CannonBuilt), second as before.
    internal static void CannonChanged(Cannon __instance)
    {
        CarouselRuntime.Guard("Refresh bustle ammunition", () =>
        {
            foreach (var state in Racks(__instance))
                if (state.Active && state.CannonId == __instance.VUID.Value)
                { Sync(state, __instance); state.Rack.RequestRebuild(); }
        });
    }
    internal static bool SelectSupply(LoadTask task, Cannon cannon)
    {
        if (task.State == LoadState.Loading || (task.State == LoadState.Loaded && task.currentRequest == LoadRequest.Unused)) return false;
        foreach (var state in Racks(cannon).OrderBy(s => s.Rack.VUID.Value))
        {
            if (!Eligible(state, cannon)) continue;
            var aggregate = state.Rack.behaviour;
            var supply=aggregate;
            if(aggregate!=null)
            {
                var result=AmmoLoadoutBridge.Supply(cannon,aggregate,task.Request.TypeID.Value,out var typed);
                if(result<0)continue;
                if(result==1)supply=typed;
            }
            if (supply == null || supply.AmountStored == 0 || supply.StoredTypeID.Value == ProjectileTypeID.Invalid.Value) continue;
            var request = task.Request;
            if (request.TypeID.Value == ProjectileTypeID.Invalid.Value || request.SizeID.Caliber == 0)
            {
                request.TypeID = supply.StoredTypeID;
                request.SizeID = supply.ShellSizeID;
                request.Destination = cannon.LoadPosition;
            }
            request.Amount = 1;
            if (!request.IsMatch(supply.Cast<IAmmoSource>())) continue;
            task.Request = request;
            if (task.State == LoadState.NeverLoaded) task.currentRequest = LoadRequest.Unused;
            if (state.Semi) SemiAutoloader.Track(state,supply);
            task.rack = supply.Cast<IAmmoSource>();
            task.currentLoadInfo = state.Rack.LoadInfo;
            state.ReloadFrame=-1; state.RawFrame=-1;
            if (state.Semi) SemiAutoloader.InitializeTimings(task,state,cannon);
            else SetTimings(task, state, cannon);
            return true;
        }
        return false;
    }
    internal static bool Mechanical(LoadTask task, Cannon? cannon, out BustleState state)
    {
        state = null!;
        if (cannon == null || task.rack == null) return false;
        foreach (var candidate in VehicleLoadIndex.Get(cannon).Bustles)
        {
            if (candidate.Semi || candidate.Rack.behaviour==null ||
                !AmmoLoadoutBridge.SameStorage(candidate.Rack.behaviour,task.rack.TryCast<AmmoRackBehaviour>()) || !Eligible(candidate, cannon)) continue;
            state = candidate;
            return true;
        }
        return false;
    }
    internal static bool Active(LoadTask task, Cannon? cannon) => Mechanical(task, cannon, out var state) &&
        (task.State == LoadState.Loading || task.State == LoadState.Loaded || state.Rack.behaviour.AmountStored > 0);
    internal static bool ApplySpeed(LoadTask task, ref float travelSpeed, ref float handlingSpeed, ref float ramTime)
    {
        var cannon = task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        if (!Mechanical(task, cannon, out var state)) return false;
        var seconds = (float)Seconds(state, cannon!);
        travelSpeed = 1; handlingSpeed = 1; ramTime = seconds * .2f;
        return true;
    }
    internal static void ApplyTimings(LoadTask task)
    {
        var cannon = task.target?.TryCast<WeaponBehaviour>()?.mount?.TryCast<Cannon>();
        if (task.State == LoadState.Loading && Mechanical(task, cannon, out var state)) SetTimings(task, state, cannon!);
    }
    private static void SetTimings(LoadTask task, BustleState state, Cannon cannon)
    {
        var seconds = (float)Seconds(state, cannon);
        // Distance gates reach only; the established 20/20/40/20 cycle is retained.
        task.pickupTime = seconds * .2f;
        task.totalDistance = seconds * .2f;
        task.dropoffTime = seconds * .4f;
    }
    // 0.2.28 (M3): part of the merged ToBlueprint postfix (CarouselRuntime.Saved), second as before.
    internal static void Save(VehicleObject __0, Sprocket.Vehicles.Serialization.VehicleObjectBlueprint __result)
    {
        CarouselRuntime.Guard("Save bustle rack", () =>
        {
            var parts = __0.Components;
            for (int i = 0; i < Count(parts); i++)
            {
                var rack = parts[i].TryCast<AmmoRack>();
                if (rack == null || !IsPart(rack)) continue;
                var state = State(rack);
                WriteSettings(__result.State, state.Enabled, state.CannonId, state.MirrorFeedArm);
                // Written only when set, so racks without two-piece save exactly as before.
                if (state.TwoPiece && !state.Semi) __result.State.AddValue(TwoPieceKey, true);
            }
        });
    }
    // 0.2.28 (M3/M4): part of the merged LoadData postfix (CarouselRuntime.Loaded), second as before.
    internal static void Load(VehicleComponent __instance, SerializationInfo __0)
    {
        var rack = __instance.TryCast<AmmoRack>();
        if (rack == null || !IsPart(rack)) return;
        CarouselRuntime.Guard("Restore bustle rack", () =>
        {
            var state = State(rack);
            var saved = ReadSettings(__0);
            state.Enabled = saved.Enabled;
            state.CannonId = saved.CannonId;
            state.AutoAssign = !saved.HasAssignment;
            state.MirrorFeedArm = ReadMirrorSetting(__0);
            state.TwoPiece = ReadBool(__0, TwoPieceKey);
            state.Path = null;
            state.FeedValid = false;
        });
    }
    internal static void WriteSettings(SerializationInfo info, bool enabled, int cannonId, bool mirrorFeedArm = false)
    { info.AddValue(EnabledKey, enabled); info.AddValue(CannonKey, cannonId); info.AddValue(MirrorKey, mirrorFeedArm); }
    internal static bool ReadBool(SerializationInfo info, string key)
    {
        var entries = info.GetEnumerator();
        while (entries.MoveNext())
            if (entries.Name == key) return info.GetBoolean(key);
        return false;
    }
    internal static bool ReadMirrorSetting(SerializationInfo info)
    {
        var entries = info.GetEnumerator();
        while (entries.MoveNext())
            if (entries.Name == MirrorKey) return info.GetBoolean(MirrorKey);
        return false;
    }
    internal static (bool Enabled, int CannonId, bool HasAssignment) ReadSettings(SerializationInfo info)
    {
        var keys = new HashSet<string>(); var entries = info.GetEnumerator();
        while (entries.MoveNext()) keys.Add(entries.Name);
        var enabledKey = keys.Contains(EnabledKey) ? EnabledKey : null;
        var cannonKey = keys.Contains(CannonKey) ? CannonKey : null;
        return (enabledKey == null || info.GetBoolean(enabledKey),
            cannonKey != null ? info.GetInt32(cannonKey) : -1, cannonKey != null);
    }
    // 0.2.28 (M4): the two ReleaseInternal prefixes at normal priority in one hook, in registration order: rack
    // state (core), then the rack visuals (visuals group, only while patched). The load-index prefix stays its own.
    [HarmonyPrefix, HarmonyPatch(typeof(VehicleComponent), nameof(VehicleComponent.ReleaseInternal))]
    private static void Release(VehicleComponent __instance)
    {
        try { SemiAutoloader.Forget(__instance.Pointer); States.Remove(__instance.Pointer); }
        catch (Exception ex) { HookGuard.Warn("VehicleComponent.ReleaseInternal rack state", -1, ex); }
        if (BustleVisuals.Available)
        {
            try { BustleVisuals.Release(__instance); }
            catch (Exception ex) { HookGuard.Warn("VehicleComponent.ReleaseInternal rack visual", -1, ex); }
        }
    }
}


