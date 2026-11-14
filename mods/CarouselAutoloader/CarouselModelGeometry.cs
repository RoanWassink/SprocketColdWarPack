using System;
using System.Collections.Generic;
using System.Linq;

namespace SprocketCarouselAutoloader;

// 0.2.29: config [Visuals] CarouselModel. Simple = exactly the procedural model of 0.2.28.
public enum CarouselVisualModel { Detailed, Simple }
/// <summary>0.2.30: the cannon load point in the carousel model frame (m; origin basket centre-bottom, Y up, Z forward). Read once at build time.</summary>
public readonly record struct CarouselLiftTarget(double X, double Y, double Z);
/// <summary>0.2.33: where the ring parts are in Play: all under the Ring object, found elsewhere (moved or cloned by the game), or not found (baked).</summary>
public enum CarouselRingPartPath { UnderRing, Moved, Baked, None }
/// <summary>0.2.33: where the lift target of a (re)build came from.</summary>
public enum CarouselLiftTargetSource { Fresh, Cached, None }
public enum CarouselModelLevel { Detailed, Reduced, Simple }

public readonly record struct CarouselModelChoice(CarouselModelLevel Level, string Style, string Reason, int Triangles)
{
    /// <summary>Text for the one Info line per basket; null when there is nothing to explain
    /// (detailed model, Simple chosen in the config, or a basket without slots).</summary>
    public string? Info => Level switch
    {
        CarouselModelLevel.Reduced => $"detailed {Style} carousel would have {Reason}; simple cassettes with the detailed hub ({Triangles} triangles)",
        CarouselModelLevel.Simple when Reason.Length > 0 => $"detailed {Style} carousel does not fit ({Reason}); simple model used",
        _ => null,
    };
}

// 0.2.29 (BESLUITEN 2026-11-05 "Goed, bouw ze in"): the approved T-72 and T-80 carousel models
// (CarouselModelData, from Models/carousel_t72.obj and carousel_t80.obj) placed per slot. Decorative
// geometry only: no Unity objects, colliders, ammunition or capacity logic. CarouselVisualGeometry.Build
// stays the 0.2.28 model; it is the Simple model here and it still feeds the native damage mesh.
public static class CarouselModelGeometry
{
    public static CarouselVisualModel Model { get; set; } = CarouselVisualModel.Detailed;
    /// <summary>Config [Visuals] CarouselLift: the cassette lift (0.2.30: on the rim behind the assigned cannon).</summary>
    public static bool Lift { get; set; } = true;
    public const int TriangleBudget = 25000;
    public const double ReferenceCalibre = .125, ReferenceProjectile = .680, ReferenceCharge = .408;
    /// <summary>Distance every slot part keeps from its wedge boundary: neighbours stay at least 3 mm apart.</summary>
    public const double WedgeMargin = .0015;
    /// <summary>The cassette may be narrowed to its wedge, but not below this share of the calibre scale.</summary>
    public const double MinimumCassetteScale = .75;
    public const double HubMargin = .005, MinimumHubRoom = .05;
    /// <summary>0.2.30: the lift foot centre stands this far inside the slot outer edge, per calibre scale: the foot plate (half depth
    /// 0.15 m in the part) then ends on the outer edge of the slot, so foot, guides and claw stand over the outer end of the cassette.</summary>
    public const double LiftInset = .150;
    /// <summary>Margin (m) the lift body below the ram head may stand beyond the basket radius (test limit; the placement itself ends at the slot outer edge).</summary>
    public const double LiftRimMargin = .020;
    /// <summary>The height of the lift (Y only) is scaled so that the ram reaches the load point, within these factors of the reference ram
    /// height. 0.2.34: a load point outside them gets the nearest factor (not the default height, which stuck far above a basket whose cassettes
    /// reach almost up to the breech); only an unknown load point gives the default height x k.</summary>
    public const double MinHeightFactor = .3, MaxHeightFactor = 1.5;
    /// <summary>Lift part landmarks (m, reference calibre): top of the claw/slide zone and the underside of the ram head (test and report use).</summary>
    internal const double LiftClawTop = .45, LiftHeadBottomT72 = .90, LiftHeadBottomT80 = .99;

    // Fixed points of the approved parts (claude-fix geom.py), slot frame of the OBJ: z = 0 outer edge, -z inward.
    internal const double ProjectileBase = -.028, HeadEnd = -.028, UpperTroughEnd = -.445, NoseFace = -.709, CassetteEnd = -.738,
        ChargeBaseT72 = -.032, ChargeTipGap = .005, CageEnd = -.205, ChargeFootT80 = .193, ChargeTopT80 = .601, SpineTop = .620;
    private static readonly CarouselVisualVector Up = new(0, 1, 0);

    internal sealed class StyleInfo
    {
        internal readonly CarouselStyleData Data;
        internal readonly double HubRadius, HubHeight, LiftHeadBottom, LiftRamY, LiftTopY;
        internal readonly bool[] Gear;
        internal StyleInfo(CarouselStyleData data)
        {
            Data = data;
            var lift = data.Lift;
            LiftHeadBottom = data.Name == "t80" ? LiftHeadBottomT80 : LiftHeadBottomT72;
            // Ram end = the front face of the ram head (all vertices at the largest z); its height is their mean (the ram axis).
            double front = double.NegativeInfinity, sum = 0;
            int count = 0;
            for (int i = 0; i < lift.Vertices; i++)
            {
                LiftTopY = Math.Max(LiftTopY, lift.Positions[3 * i + 1]);
                double z = lift.Positions[3 * i + 2];
                if (z > front + 1e-6) { front = z; sum = 0; count = 0; }
                if (z >= front - 1e-6) { sum += lift.Positions[3 * i + 1]; count++; }
            }
            LiftRamY = count > 0 ? sum / count : LiftTopY;
            var hub = data.Hub;
            for (int i = 0; i < hub.Vertices; i++)
            {
                HubRadius = Math.Max(HubRadius, Math.Sqrt(hub.Positions[3 * i] * hub.Positions[3 * i] + hub.Positions[3 * i + 2] * hub.Positions[3 * i + 2]));
                HubHeight = Math.Max(HubHeight, hub.Positions[3 * i + 1]);
            }
            // DriveTrim detail: the ring gear and its teeth (y 0.020-0.052 m, radius 0.128-0.166 m around the hub axis).
            Gear = new bool[hub.Triangles];
            for (int t = 0; t < hub.Triangles; t++)
            {
                bool gear = true;
                for (int c = 0; c < 3; c++)
                {
                    int v = hub.Indices[3 * t + c];
                    double x = hub.Positions[3 * v], y = hub.Positions[3 * v + 1], z = hub.Positions[3 * v + 2], radius = Math.Sqrt(x * x + z * z);
                    gear &= y >= .0195 && y <= .0525 && radius >= .126 && radius <= .1665;
                }
                Gear[t] = gear;
            }
        }
    }
    internal static readonly StyleInfo T72 = new(CarouselModelData.T72), T80 = new(CarouselModelData.T80);
    internal static StyleInfo Info(bool verticalCharge) => verticalCharge ? T80 : T72;

    /// <summary>Continuous piecewise-linear map of one axis (extrapolated with the end slopes).</summary>
    internal sealed class Piecewise
    {
        private readonly double[] from, to;
        internal Piecewise(params (double From, double To)[] points)
        {
            var sorted = points.OrderBy(p => p.From).ToArray();
            from = sorted.Select(p => p.From).ToArray();
            to = sorted.Select(p => p.To).ToArray();
        }
        internal static Piecewise Scale(double factor) => new((0, 0), (1, factor));
        private int Segment(double x)
        {
            for (int i = 0; i < from.Length - 2; i++) if (x < from[i + 1]) return i;
            return from.Length - 2;
        }
        internal double Slope(double x) { int i = Segment(x); return (to[i + 1] - to[i]) / (from[i + 1] - from[i]); }
        internal double Map(double x) { int i = Segment(x); return to[i] + (x - from[i]) * (to[i + 1] - to[i]) / (from[i + 1] - from[i]); }
    }

    /// <summary>Everything Build derives from its inputs before it makes a mesh.</summary>
    internal sealed class Plan
    {
        internal StyleInfo Style = T72;
        internal bool Vertical;
        internal int Slots;
        internal double Radius, Depth, End, Floor, K, Sx, HubScale, Projectile, Charge, Calibre, CassetteDepth, SlotTop;
        // 0.2.30: lift direction (0 = +Z), the turn of the ring of slots that puts a slot under it, the load point (if known), and the lift placement.
        internal double LiftAngle = Math.PI, Rot, HeightFactor = 1, FootY;
        internal CarouselLiftTarget? Target;
        internal CarouselVisualVector LiftOrigin;
        internal Piecewise CassetteY = Piecewise.Scale(1), CassetteZ = Piecewise.Scale(1), ProjectileZ = Piecewise.Scale(1),
            ChargeY = Piecewise.Scale(1), ChargeZ = Piecewise.Scale(1);
    }

    /// <summary>Angle of a slot centre line (0 = +Z): the angles of the 0.2.28 model, which the native damage mesh also uses.</summary>
    public static double SlotAngle(int capacity, int slot) => slot * 2 * Math.PI / capacity;
    /// <summary>Slot angle of the turned ring (0.2.30): the 0.2.28 angle plus the rotation of the whole ring.</summary>
    public static double SlotAngle(int capacity, int slot, double rotation) => SlotAngle(capacity, slot) + rotation;

    /// <summary>Direction of the lift (0 = +Z, positive towards +X, the angle of the slot frame): that of the load point (x, z) in the carousel
    /// model frame; local -Z (pi) when the point is unknown, not finite or on the axis.</summary>
    public static double LiftDirection(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || x * x + z * z < 1e-6) return Math.PI;
        var angle = Math.Atan2(x, z);
        return angle <= -Math.PI + 1e-12 ? Math.PI : angle;
    }
    /// <summary>Turn of the whole ring so that one slot stands exactly in the lift direction: at most half a pitch either way.</summary>
    public static double RingRotation(int capacity, double liftAngle)
    {
        double pitch = 2 * Math.PI / capacity;
        return liftAngle - Math.Round(liftAngle / pitch) * pitch;
    }
    // ---- 0.2.31/0.2.32: the ring turns one slot per shot. n = capacity, s = stock (visible rounds), L = slot under the lift at a full stock
    // (the 0.2.30 lift slot), f = n - s rounds fired. Slots i with (i - L) mod n < f are empty. 0.2.32 ("Leeg, zoals echt"): the ring is turned
    // f - 1 slots, so the slot used last, (L + f - 1) mod n, stands EMPTY under the lift between two shots (the lift has taken its round, the empty
    // cassette dropped back); at f = 0 the ring is in its base position and the full slot L stands under the lift. Refilling turns the ring back.
    // Pure logic, no Unity.
    /// <summary>Rounds fired: n - s, with the stock limited to 0..n (0 for no slots).</summary>
    public static int RingFired(int capacity, int stock) => capacity <= 0 ? 0 : capacity - Math.Clamp(stock, 0, capacity);
    private static int Wrap(long value, int n) => (int)(((value % n) + n) % n);
    /// <summary>Slot that stands under the lift at a full stock for this load point (-Z without one).</summary>
    public static int LiftSlotFor(int capacity, CarouselLiftTarget? target)
        => capacity <= 0 ? 0 : LiftSlot(capacity, target is { } t ? LiftDirection(t.X, t.Z) : Math.PI);
    /// <summary>True when slot i has been fired empty: (i - L) mod n &lt; f.</summary>
    public static bool RingEmpty(int capacity, int liftSlot, int stock, int slot)
        => capacity > 0 && Wrap((long)slot - liftSlot, capacity) < RingFired(capacity, stock);
    /// <summary>The slot under the lift: (L + f - 1) mod n, the slot used last (empty), for f >= 1; slot L (full) at f = 0.</summary>
    public static int RingSlotUnderLift(int capacity, int liftSlot, int stock)
        => capacity <= 0 ? 0 : Wrap((long)liftSlot + Math.Max(0, RingFired(capacity, stock) - 1), capacity);
    /// <summary>Turn of the ring transform about Y (radians, Unity: positive turns +Z towards +X, the direction of rising slot angles) on top of the
    /// 0.2.30 base rotation: minus (f - 1) pitches, none at f = 0. With no stock left everything is empty; the ring then stands f - 1 = n - 1 slots on.</summary>
    public static double RingTurn(int capacity, int stock)
        => capacity <= 0 ? 0 : -Math.Max(0, RingFired(capacity, stock) - 1) * 2 * Math.PI / capacity;
    /// <summary>The full slots as at most two consecutive slot ranges (start, count): s slots from (L + f) mod n, wrapping at n.</summary>
    public static IReadOnlyList<(int Start, int Count)> RingVisibleRanges(int capacity, int liftSlot, int stock)
    {
        int s = capacity <= 0 ? 0 : Math.Clamp(stock, 0, capacity);
        if (s == 0) return Array.Empty<(int, int)>();
        int start = Wrap((long)liftSlot + RingFired(capacity, s), capacity);
        return start + s <= capacity ? new[] { (start, s) } : new[] { (start, capacity - start), (0, start + s - capacity) };
    }
    // 0.2.32: smooth turn. A stock that drops by exactly 1 (a shot or a loading) turns the ring from the old to the new angle in TurnSeconds;
    // any other change (more than 1 down, up = refill, Play reset, loading a save, the first display) jumps. Time based, so every call is idempotent.
    /// <summary>Config [Visuals] CarouselTurnSeconds; 0 = jump.</summary>
    public static double TurnSeconds { get; set; } = .5;
    public const double DefaultTurnSeconds = .5, MaxTurnSeconds = 5, TurnStallSeconds = 1;
    /// <summary>Config value to use: finite, 0..MaxTurnSeconds; anything else the default.</summary>
    public static double SanitizeTurnSeconds(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, MaxTurnSeconds) : DefaultTurnSeconds;
    /// <summary>True when the turn is animated: stock exactly one lower than before, both inside 0..n, and a turn time above 0.</summary>
    public static bool IsSingleShot(int capacity, int oldStock, int newStock, double seconds)
        => seconds > 0 && capacity > 0 && oldStock >= 1 && oldStock <= capacity && newStock == oldStock - 1;
    /// <summary>Ring angle (radians) at elapsed seconds into a turn from -> to (smoothstep). The end angle at once for a duration of 0 or less, at
    /// the end of the turn, or when the last tick is more than TurnStallSeconds ago (the safety net: a ring never stays half way).</summary>
    public static double TurnAngleAt(double from, double to, double elapsed, double duration, double sinceLastTick = 0)
    {
        if (!(duration > 0) || !(elapsed < duration) || !(sinceLastTick <= TurnStallSeconds) || !double.IsFinite(elapsed)) return to;
        double t = Math.Max(0, elapsed) / duration;
        return from + (to - from) * (t * t * (3 - 2 * t));
    }
    // ---- 0.2.33: the ring parts in Play. The game can hang the cassette/round objects somewhere else at the start of Play (moved or cloned
    // with the same sharedMesh) or bake them away; then the Ring transform turns nothing and every part is turned itself, about the Y axis of Root.
    /// <summary>Where the ring parts are: all under the Ring object, found elsewhere (moved or cloned by the game), or not found (baked into a native mesh).</summary>
    /// <summary>0.2.34: no ring parts at all (capacity 0, the model has no cassettes): None. Choice between the three paths: all `total` parts under the ring (and `underRing` = total) keeps the Ring.localRotation path; otherwise
    /// the parts found by mesh (`found` > 0) are turned one by one; nothing found means the game baked them.</summary>
    public static CarouselRingPartPath RingPartPath(int total, int underRing, int found)
        => total <= 0 ? CarouselRingPartPath.None : underRing == total ? CarouselRingPartPath.UnderRing : found > 0 ? CarouselRingPartPath.Moved : CarouselRingPartPath.Baked;
    /// <summary>A point (x, z) in the Root frame turned by theta about the Y axis of Root (Unity: +Z towards +X), as the Ring transform would.</summary>
    public static (double X, double Z) RotateAboutY(double x, double z, double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return (x * c + z * s, -x * s + z * c);
    }
    /// <summary>A load point is usable when finite and within 50 m of the carousel centre.</summary>
    public static bool ValidLiftTarget(CarouselLiftTarget? t)
        => t is { } v && double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z) && Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z) < 50;
    /// <summary>Lift target for a (re)build: the fresh one when valid, else the last valid one of the same basket when a cannon is assigned, else none.</summary>
    public static CarouselLiftTarget? ChooseLiftTarget(CarouselLiftTarget? fresh, CarouselLiftTarget? cached, bool cannonAssigned, out CarouselLiftTargetSource source)
    {
        if (ValidLiftTarget(fresh)) { source = CarouselLiftTargetSource.Fresh; return fresh; }
        if (cannonAssigned && ValidLiftTarget(cached)) { source = CarouselLiftTargetSource.Cached; return cached; }
        source = CarouselLiftTargetSource.None;
        return null;
    }
    /// <summary>The parts that turn with the ring: cassettes, projectiles and charges. The mechanism (floor ring, hub, lift) and the drive trim stay put.</summary>
    public static bool RotatesWithRing(string material) => material is "Cassette" or "Projectile" or "Charge";

    /// <summary>Index of the slot under the lift (the slot indexing itself never changes).</summary>
    public static int LiftSlot(int capacity, double liftAngle)
    {
        long s = (long)Math.Round(liftAngle / (2 * Math.PI / capacity));
        return (int)(((s % capacity) + capacity) % capacity);
    }
    /// <summary>Factor on the height of the lift (Y only, on top of the calibre scale k) that lets the ram end at the load point; rise = height of
    /// the load point above the foot, ramY = reference ram height of the part. 1 (default height x k) when the point is unknown or unusable;
    /// otherwise the factor limited to MinHeightFactor..MaxHeightFactor (0.2.34: before, a factor outside the limits fell back to 1).</summary>
    public static double HeightFactorFor(double rise, double k, double ramY)
    {
        if (!double.IsFinite(rise) || !(k > 0) || !(ramY > 0)) return 1;
        return Math.Clamp(rise / (ramY * k), MinHeightFactor, MaxHeightFactor);
    }

    public static IReadOnlyList<CarouselVisualMesh> Build(double diameter, double depth, int capacity,
        double calibre, double projectileLength, double chargeLength, bool verticalCharge, int visibleRounds = -1)
        => Build(diameter, depth, capacity, calibre, projectileLength, chargeLength, verticalCharge, out _, visibleRounds);

    // Same inputs as CarouselVisualGeometry.Build: metres except capacity; origin basket centre-bottom, Y up, Z forward.
    public static IReadOnlyList<CarouselVisualMesh> Build(double diameter, double depth, int capacity,
        double calibre, double projectileLength, double chargeLength, bool verticalCharge, out CarouselModelChoice choice, int visibleRounds = -1,
        CarouselLiftTarget? target = null)
    {
        string style = verticalCharge ? "T-80" : "T-72";
        IReadOnlyList<CarouselVisualMesh> Simple(string reason, out CarouselModelChoice simpleChoice)
        {
            // The 0.2.28 model, including its exceptions for inputs it rejects.
            var simple = CarouselVisualGeometry.Build(diameter, depth, capacity, calibre, projectileLength, chargeLength, verticalCharge, visibleRounds);
            simpleChoice = new(CarouselModelLevel.Simple, style, reason, Triangles(simple));
            return simple;
        }
        if (Model == CarouselVisualModel.Simple || !Valid(diameter, depth, capacity, calibre, projectileLength, chargeLength) || capacity == 0)
            return Simple("", out choice);
        var plan = Fit(diameter, depth, capacity, calibre, projectileLength, chargeLength, verticalCharge, out var misfit, target);
        if (plan == null) return Simple(misfit, out choice);
        if (visibleRounds < 0) visibleRounds = capacity;
        visibleRounds = Math.Clamp(visibleRounds, 0, capacity);
        var detailed = Detailed(plan, visibleRounds);
        int triangles = Triangles(detailed);
        if (triangles <= TriangleBudget) { choice = new(CarouselModelLevel.Detailed, style, "", triangles); return detailed; }
        var reduced = Reduced(plan, visibleRounds, out misfit);
        if (reduced == null) return Simple(misfit, out choice);
        int reducedTriangles = Triangles(reduced);
        if (reducedTriangles > TriangleBudget) return Simple($"{reducedTriangles} triangles even with simple cassettes", out choice);
        choice = new(CarouselModelLevel.Reduced, style, $"{triangles} triangles (more than {TriangleBudget})", reducedTriangles);
        return reduced;
    }

    public static int Triangles(IReadOnlyList<CarouselVisualMesh> meshes) => meshes.Sum(m => m.Triangles.Count) / 3;

    // The input checks of the 0.2.28 model; anything it rejects goes to it, so it throws exactly as before.
    private static bool Valid(double diameter, double depth, int capacity, double calibre, double projectileLength, double chargeLength)
    {
        foreach (var x in new[] { diameter, depth, calibre, projectileLength })
            if (!double.IsFinite(x) || x <= 0) return false;
        if (!double.IsFinite(chargeLength) || chargeLength < 0) return false;
        if (capacity < 0 || capacity > CarouselVisualGeometry.MaximumSlots) return false;
        return diameter / 2 > .08 && depth >= .06;
    }

    internal static Plan? Fit(double diameter, double depth, int capacity, double calibre, double projectileLength,
        double chargeLength, bool verticalCharge, out string misfit, CarouselLiftTarget? target = null)
    {
        misfit = "";
        var p = new Plan
        {
            Style = Info(verticalCharge), Vertical = verticalCharge, Slots = capacity, Radius = diameter / 2, Depth = depth,
            K = calibre / ReferenceCalibre, Projectile = projectileLength, Charge = chargeLength, Calibre = calibre,
            Floor = Math.Min(.055, depth * .12), Target = target,
        };
        p.LiftAngle = target is { } t ? LiftDirection(t.X, t.Z) : Math.PI;
        p.Rot = RingRotation(capacity, p.LiftAngle);
        double r = p.Radius, k = p.K;
        // Same outer edge and allocation rule as the 0.2.28 model.
        p.End = Math.Sqrt(Math.Max(0, (r - .04) * (r - .04) - Math.Pow((calibre * 1.28 + .012) / 2, 2)));
        var oldHub = Math.Min(r * .16, Math.Max(.09, r * .12));
        if (projectileLength > p.End - oldHub || (!verticalCharge && chargeLength > p.End - oldHub)) { misfit = "allocation"; return null; }
        // Charge shape for the cassette when there is no propellant: the reference charge.
        var shape = chargeLength > 0 ? chargeLength : ReferenceCharge * k;
        var nose = -ProjectileBase * k + projectileLength;
        double noseFace;
        if (!verticalCharge)
        {
            // T-72: outer head per calibre, upper trough to the charge tip, lower trough to the projectile nose.
            var chargeEnd = -ChargeBaseT72 * k + shape + ChargeTipGap * k;
            noseFace = Math.Max(nose + .001 * k, chargeEnd + .020 * k);
            p.CassetteZ = new((CassetteEnd, -(noseFace + .029 * k)), (NoseFace, -noseFace), (UpperTroughEnd, -chargeEnd), (HeadEnd, HeadEnd * k), (0, 0));
            p.CassetteY = Piecewise.Scale(k);
            p.ChargeY = Piecewise.Scale(k);
            p.ChargeZ = new((ChargeBaseT72 - ReferenceCharge, ChargeBaseT72 * k - Math.Max(chargeLength, 1e-6)), (ChargeBaseT72, ChargeBaseT72 * k), (0, 0));
        }
        else
        {
            // T-80: the cage of the upright charge keeps calibre proportions; the lying part stretches to the nose.
            noseFace = Math.Max(nose + .001 * k, -CageEnd * k + .020 * k);
            p.CassetteZ = new((CassetteEnd, -(noseFace + .029 * k)), (NoseFace, -noseFace), (CageEnd, CageEnd * k), (0, 0));
            p.CassetteY = new((0, 0), (ChargeFootT80, ChargeFootT80 * k), (ChargeTopT80, ChargeFootT80 * k + shape),
                (SpineTop, ChargeFootT80 * k + shape + (SpineTop - ChargeTopT80) * k));
            p.ChargeY = new((0, 0), (ChargeFootT80, ChargeFootT80 * k), (ChargeTopT80, ChargeFootT80 * k + Math.Max(chargeLength, 1e-6)));
            p.ChargeZ = Piecewise.Scale(k);
        }
        p.ProjectileZ = new((ProjectileBase - ReferenceProjectile, ProjectileBase * k - projectileLength), (ProjectileBase, ProjectileBase * k));
        p.CassetteDepth = noseFace + .029 * k;

        // Width: every part stays inside its own wedge (2 pi r / capacity) with a margin, at the inner and the outer end.
        double half = Math.PI / capacity, tan = Math.Tan(half), cos = Math.Cos(half);
        double Room(double zeta) => zeta * tan - WedgeMargin / cos;
        var cassette = p.Style.Data.Cassette;
        double sx = k;
        for (int i = 0; i < cassette.Vertices; i++)
        {
            double x = Math.Abs(cassette.Positions[3 * i]), zeta = p.End + p.CassetteZ.Map(cassette.Positions[3 * i + 2]);
            if (zeta <= 0) { misfit = "slots reach the basket centre"; return null; }
            if (x > 1e-9) sx = Math.Min(sx, Room(zeta) / x);
        }
        if (sx < MinimumCassetteScale * k) { misfit = $"slot wedge too narrow, cassette would be {sx / k:0%} wide"; return null; }
        p.Sx = sx;
        bool RoundsFit(CarouselPart part, Piecewise mz)
        {
            for (int i = 0; i < part.Vertices; i++)
            {
                double x = Math.Abs(part.Positions[3 * i]) * k, zeta = p.End + mz.Map(part.Positions[3 * i + 2]);
                if (zeta <= 0 || x > Room(zeta)) return false;
            }
            return true;
        }
        if (!RoundsFit(p.Style.Data.Projectile, p.ProjectileZ) || (chargeLength > 0 && !RoundsFit(p.Style.Data.Charge, p.ChargeZ)))
        { misfit = "rounds wider than their slot wedge"; return null; }

        // Radius and depth of the basket (the lift is outside both by design and is not part of this check).
        double top = 0, reach = 0;
        void Measure(CarouselPart part, Piecewise mx, Piecewise my, Piecewise mz)
        {
            for (int i = 0; i < part.Vertices; i++)
            {
                double x = mx.Map(part.Positions[3 * i]), y = my.Map(part.Positions[3 * i + 1]), zeta = p.End + mz.Map(part.Positions[3 * i + 2]);
                top = Math.Max(top, y + p.Floor);
                reach = Math.Max(reach, Math.Sqrt(x * x + zeta * zeta));
            }
        }
        Measure(cassette, Piecewise.Scale(sx), p.CassetteY, p.CassetteZ);
        Measure(p.Style.Data.Projectile, Piecewise.Scale(k), Piecewise.Scale(k), p.ProjectileZ);
        if (chargeLength > 0) Measure(p.Style.Data.Charge, Piecewise.Scale(k), p.ChargeY, p.ChargeZ);
        p.SlotTop = top;
        if (top > depth + 1e-9) { misfit = $"basket {depth:0.000} m deep, cassettes need {top:0.000} m"; return null; }
        if (reach > r + 1e-9) { misfit = $"cassettes reach {reach:0.000} m, basket radius {r:0.000} m"; return null; }

        // Hub: calibre scale, never wider than the free space inside the cassettes, never above the basket.
        var room = p.End - p.CassetteDepth - HubMargin;
        if (room < MinimumHubRoom) { misfit = "no room for the hub inside the cassettes"; return null; }
        p.HubScale = Math.Min(k, Math.Min(room / p.Style.HubRadius, (depth - p.Floor) / p.Style.HubHeight));
        if (p.HubScale <= 0) { misfit = "no room for the hub"; return null; }
        PlaceLift(p, p.SlotTop);
        return p;
    }

    private static List<CarouselVisualMesh> Detailed(Plan p, int visibleRounds)
    {
        var output = new List<CarouselVisualMesh>();
        var frame = new CarouselVisualMesh("mechanism", "Mechanism"); output.Add(frame);
        var trim = new CarouselVisualMesh("drive_trim", "DriveTrim"); output.Add(trim);
        FloorRings(frame, p.Radius, p.Depth);
        Hub(frame, trim, p, p.HubScale);
        var data = p.Style.Data;
        Piecewise kx = Piecewise.Scale(p.K), cassetteX = Piecewise.Scale(p.Sx);
        for (int slot = 0; slot < p.Slots; slot++)
        {
            var angle = SlotAngle(p.Slots, slot, p.Rot);
            var cassette = new CarouselVisualMesh($"cassette_{slot:00}", "Cassette"); output.Add(cassette);
            SlotPart(cassette, data.Cassette, cassetteX, p.CassetteY, p.CassetteZ, angle, p.End, p.Floor);
            if (slot >= visibleRounds) continue;
            var projectile = new CarouselVisualMesh($"projectile_{slot:00}", "Projectile", slot); output.Add(projectile);
            SlotPart(projectile, data.Projectile, kx, kx, p.ProjectileZ, angle, p.End, p.Floor);
            var charge = new CarouselVisualMesh($"charge_{slot:00}", "Charge", slot); output.Add(charge);
            if (p.Charge > 0) SlotPart(charge, data.Charge, kx, p.ChargeY, p.ChargeZ, angle, p.End, p.Floor);
        }
        if (Lift) { PlaceLift(p, p.SlotTop); LiftPart(frame, p); }
        return Combine(output);
    }

    // Above the triangle budget: the 0.2.28 cassettes and rounds with the detailed hub and lift.
    private static List<CarouselVisualMesh>? Reduced(Plan p, int visibleRounds, out string misfit)
    {
        misfit = "";
        double calibre = p.Calibre, end = p.End, projectileLength = p.Projectile, chargeLength = p.Charge;
        var railLength = Math.Max(projectileLength, p.Vertical ? projectileLength : chargeLength) + .012;
        var room = end - railLength - HubMargin;
        if (room < MinimumHubRoom) { misfit = "no room for the hub inside the simple cassettes"; return null; }
        var hubScale = Math.Min(p.K, Math.Min(room / p.Style.HubRadius, (p.Depth - p.Floor) / p.Style.HubHeight));
        var output = new List<CarouselVisualMesh>();
        var frame = new CarouselVisualMesh("mechanism", "Mechanism"); output.Add(frame);
        var trim = new CarouselVisualMesh("drive_trim", "DriveTrim"); output.Add(trim);
        FloorRings(frame, p.Radius, p.Depth);
        Hub(frame, trim, p, hubScale);
        for (int slot = 0; slot < p.Slots; slot++)
        {
            // Slot geometry of the 0.2.28 model (CarouselVisualGeometry.Build), at the turned slot angles.
            var angle = SlotAngle(p.Slots, slot, p.Rot);
            var radial = new CarouselVisualVector(Math.Sin(angle), 0, Math.Cos(angle));
            var tangent = new CarouselVisualVector(Math.Cos(angle), 0, -Math.Sin(angle));
            var width = calibre * 1.28 + .012;
            var center = radial * (end - railLength / 2);
            var cassette = new CarouselVisualMesh($"cassette_{slot:00}", "Cassette"); output.Add(cassette);
            foreach (var side in new[] { -1, 1 })
                Box(cassette, center + tangent * (side * width * .48) + Up * .09, tangent, Up, radial, .012, .04, railLength);
            Box(cassette, radial * (end - .012) + Up * .095, tangent, Up, radial, width, .035, .018);
            if (slot >= visibleRounds) continue;
            var projectile = new CarouselVisualMesh($"projectile_{slot:00}", "Projectile", slot); output.Add(projectile);
            var radius = calibre * .5;
            var py = .12 + radius;
            var inner = radial * (end - projectileLength) + Up * py;
            var outer = radial * end + Up * py;
            var nose = Math.Min(projectileLength * .24, calibre * 1.2);
            Cylinder(projectile, inner, inner + radial * nose, radius * .16, radius, 8);
            Cylinder(projectile, inner + radial * nose, outer, radius, radius, 8);
            var charge = new CarouselVisualMesh($"charge_{slot:00}", "Charge", slot); output.Add(charge);
            if (chargeLength == 0) continue;
            var cr = calibre * .62;
            if (p.Vertical)
            {
                var a = radial * (end - cr) + Up * (.12 + calibre);
                Cylinder(charge, a, a + Up * chargeLength, cr, cr * .92, 8);
            }
            else
            {
                var cy = .15 + calibre + cr;
                Cylinder(charge, radial * (end - chargeLength) + Up * cy, radial * end + Up * cy, cr, cr * .95, 8);
            }
        }
        if (output.Where(m => m.Material != "Mechanism").SelectMany(m => m.Vertices).Any(v => v.Y > p.Depth + 1e-9))
        { misfit = "simple cassettes deeper than the basket"; return null; }
        if (Lift)
        {
            // Foot on the top of the simple cassettes and rounds (the cassette, projectile and charge meshes).
            double top = output.Where(m => m.Material != "Mechanism" && m.Material != "DriveTrim").SelectMany(m => m.Vertices).Select(v => v.Y).DefaultIfEmpty(p.Floor).Max();
            PlaceLift(p, top);
            LiftPart(frame, p);
        }
        return Combine(output);
    }

    // Slot frame -> basket: x tangential, y up from the floor ring, z radial from the slot outer edge (end).
    // Normals use the inverse transpose of the local axis slopes.
    private static void SlotPart(CarouselVisualMesh mesh, CarouselPart part, Piecewise mx, Piecewise my, Piecewise mz, double angle, double end, double floor)
    {
        var radial = new CarouselVisualVector(Math.Sin(angle), 0, Math.Cos(angle));
        var tangent = new CarouselVisualVector(Math.Cos(angle), 0, -Math.Sin(angle));
        int offset = mesh.Vertices.Count;
        for (int i = 0; i < part.Vertices; i++)
        {
            double x = part.Positions[3 * i], y = part.Positions[3 * i + 1], z = part.Positions[3 * i + 2];
            mesh.Vertices.Add(tangent * mx.Map(x) + Up * (my.Map(y) + floor) + radial * (end + mz.Map(z)));
            var n = new CarouselVisualVector(part.Normals[3 * i] / mx.Slope(x), part.Normals[3 * i + 1] / my.Slope(y), part.Normals[3 * i + 2] / mz.Slope(z));
            n = n.Length > 1e-12 ? n.Unit : Up;
            mesh.Normals.Add(tangent * n.X + Up * n.Y + radial * n.Z);
        }
        foreach (var index in part.Indices) mesh.Triangles.Add(offset + index);
    }

    // 0.2.30: the lift stands over the outer end of the slot under it (the slot in the lift direction), on the rim: the foot plate ends on the
    // slot outer edge, its foot lies on the top of the cassettes, and its height is scaled so that the ram ends at the load point's height.
    private static void PlaceLift(Plan p, double slotTop)
    {
        double a = p.LiftAngle, k = p.K;
        p.FootY = slotTop;
        p.LiftOrigin = new CarouselVisualVector(Math.Sin(a), 0, Math.Cos(a)) * (p.End - LiftInset * k) + Up * slotTop;
        p.HeightFactor = p.Target is { } t && double.IsFinite(t.Y) ? HeightFactorFor(t.Y - slotTop, k, p.Style.LiftRamY) : 1;
    }
    internal static Piecewise LiftHeight(Plan p) => Piecewise.Scale(p.K * p.HeightFactor);

    // Lift at calibre scale (height scaled to the load point), ram (+Z of the part) towards the centre in the lift direction.
    private static void LiftPart(CarouselVisualMesh mesh, Plan p)
    {
        var part = p.Style.Data.Lift;
        double a = p.LiftAngle, k = p.K;
        var inward = new CarouselVisualVector(-Math.Sin(a), 0, -Math.Cos(a));
        var side = new CarouselVisualVector(-Math.Cos(a), 0, Math.Sin(a));
        var origin = p.LiftOrigin;
        var height = LiftHeight(p);
        int offset = mesh.Vertices.Count;
        for (int i = 0; i < part.Vertices; i++)
        {
            double py = part.Positions[3 * i + 1];
            mesh.Vertices.Add(origin + side * (part.Positions[3 * i] * k) + Up * height.Map(py) + inward * (part.Positions[3 * i + 2] * k));
            var n = new CarouselVisualVector(part.Normals[3 * i], part.Normals[3 * i + 1] * k / height.Slope(py), part.Normals[3 * i + 2]);
            n = n.Length > 1e-12 ? n.Unit : Up;
            mesh.Normals.Add(side * n.X + Up * n.Y + inward * n.Z);
        }
        foreach (var index in part.Indices) mesh.Triangles.Add(offset + index);
    }

    // Hub on the floor ring at the centre; the ring gear goes to the DriveTrim material.
    private static void Hub(CarouselVisualMesh frame, CarouselVisualMesh trim, Plan p, double scale)
    {
        var hub = p.Style.Data.Hub;
        var origin = new CarouselVisualVector(0, p.Floor, 0);
        var map = new Dictionary<(CarouselVisualMesh, int), int>();
        for (int t = 0; t < hub.Triangles; t++)
        {
            var target = p.Style.Gear[t] ? trim : frame;
            for (int c = 0; c < 3; c++)
            {
                int v = hub.Indices[3 * t + c];
                if (!map.TryGetValue((target, v), out var index))
                {
                    index = target.Vertices.Count;
                    target.Vertices.Add(origin + new CarouselVisualVector(hub.Positions[3 * v], hub.Positions[3 * v + 1], hub.Positions[3 * v + 2]) * scale);
                    target.Normals.Add(new CarouselVisualVector(hub.Normals[3 * v], hub.Normals[3 * v + 1], hub.Normals[3 * v + 2]).Unit);
                    map[(target, v)] = index;
                }
                target.Triangles.Add(index);
            }
        }
    }

    // The procedural floor ring and outer rim of the 0.2.28 model; they follow the basket diameter.
    private static void FloorRings(CarouselVisualMesh frame, double r, double depth)
    {
        var hubRadius = Math.Min(r * .16, Math.Max(.09, r * .12));
        Ring(frame, hubRadius, r - .025, .015, Math.Min(.055, depth * .12), 48);
        Ring(frame, r - .055, r - .025, .055, Math.Min(.13, depth * .3), 48);
    }

    // Same grouping as CarouselVisualGeometry: at most five meshes, rounds sorted by slot with prefix counts.
    private static List<CarouselVisualMesh> Combine(List<CarouselVisualMesh> source)
    {
        var result = new List<CarouselVisualMesh>();
        foreach (var group in source.GroupBy(m => m.Material))
        {
            var mesh = new CarouselVisualMesh(group.Key.ToLowerInvariant(), group.Key);
            var prefix = new List<int> { 0 };
            foreach (var part in group)
            {
                var offset = mesh.Vertices.Count;
                mesh.Vertices.AddRange(part.Vertices); mesh.Normals.AddRange(part.Normals);
                mesh.Triangles.AddRange(part.Triangles.Select(i => i + offset));
                if (part.Slot >= 0) prefix.Add(mesh.Triangles.Count);
            }
            if (prefix.Count > 1) mesh.SlotPrefixIndexCounts = prefix.ToArray();
            result.Add(mesh);
        }
        return result;
    }

    // Primitive helpers as in CarouselVisualGeometry (that file stays unchanged: the native damage mesh uses it).
    private static void Cylinder(CarouselVisualMesh m, CarouselVisualVector a, CarouselVisualVector b, double ra, double rb, int n)
    {
        var axis = (b - a).Unit;
        var u = CarouselVisualVector.Cross(Math.Abs(axis.Y) > .9 ? new(1, 0, 0) : Up, axis).Unit;
        var v = CarouselVisualVector.Cross(axis, u);
        for (var i = 0; i < n; i++)
        {
            var t = i * 2 * Math.PI / n; var q = (i + 1) * 2 * Math.PI / n;
            var d = u * Math.Cos(t) + v * Math.Sin(t); var e = u * Math.Cos(q) + v * Math.Sin(q);
            m.Quad(a + d * ra, a + e * ra, b + e * rb, b + d * rb);
            m.Triangle(a, a + e * ra, a + d * ra); m.Triangle(b, b + d * rb, b + e * rb);
        }
    }
    private static void Ring(CarouselVisualMesh m, double inner, double outer, double bottom, double top, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var t = i * 2 * Math.PI / n; var q = (i + 1) * 2 * Math.PI / n;
            CarouselVisualVector P(double radius, double y, double angle) => new(Math.Sin(angle) * radius, y, Math.Cos(angle) * radius);
            var a = P(inner, bottom, t); var b = P(outer, bottom, t); var c = P(outer, bottom, q); var d = P(inner, bottom, q);
            var aa = P(inner, top, t); var bb = P(outer, top, t); var cc = P(outer, top, q); var dd = P(inner, top, q);
            m.Quad(aa, bb, cc, dd); m.Quad(a, d, c, b); m.Quad(b, c, cc, bb); m.Quad(a, aa, dd, d);
        }
    }
    private static void Box(CarouselVisualMesh m, CarouselVisualVector center, CarouselVisualVector x, CarouselVisualVector y, CarouselVisualVector z, double sx, double sy, double sz)
    {
        CarouselVisualVector P(int a, int b, int c) => center + x * (a * sx / 2) + y * (b * sy / 2) + z * (c * sz / 2);
        m.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1));
        m.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        m.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        m.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1));
        m.Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        m.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
    }
}
