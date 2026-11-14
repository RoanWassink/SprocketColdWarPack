using System;
using System.Collections.Generic;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Materials;

// Bounded, exact-owner work queue for scale/parent/mirror callbacks. A native
// callback may re-enter while an owner is being refreshed; the outer drain keeps
// consuming until every distinct transform queued during that call has been
// handled once. A transform that re-queues itself from a nested callback is
// ignored for the rest of the same drain, which prevents unbounded
// RequestRebuild/scale loops without dropping the mirror peer.
internal sealed class ModularRefreshQueue
{
    private readonly Queue<IntPtr> pending = new();
    private readonly HashSet<IntPtr> queued = new();
    private readonly HashSet<IntPtr> processed = new();
    private readonly int capacity;
    private bool draining;

    internal ModularRefreshQueue(int capacity = 256)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    internal int Count => pending.Count;
    internal bool Draining => draining;

    internal void BeginDrain()
    {
        if (draining) return;
        processed.Clear();
        draining = true;
    }

    internal void EndDrain()
    {
        draining = false;
        processed.Clear();
    }

    // False means the owner was already handled in this drain, is already
    // pending, or the bounded queue is full. Full is logged by the caller once.
    internal bool Enqueue(IntPtr owner)
    {
        if (owner == IntPtr.Zero) return false;
        if (processed.Contains(owner) || queued.Contains(owner)) return false;
        if (pending.Count >= capacity) return false;
        queued.Add(owner);
        pending.Enqueue(owner);
        return true;
    }

    internal bool TryDequeue(out IntPtr owner)
    {
        while (pending.Count > 0)
        {
            owner = pending.Dequeue();
            queued.Remove(owner);
            if (processed.Add(owner)) return true;
        }
        owner = IntPtr.Zero;
        return false;
    }

    internal void Clear()
    {
        pending.Clear();
        queued.Clear();
        processed.Clear();
        draining = false;
    }
}

// Desired generated-render generation for one owned component. The keys are the
// actual mesh-cache identities; the geometry identity is the native
// scale/count identity used for collider invalidation. A native wrapper swap or
// a missing desired mesh is represented by the observed fields, not by silently
// trusting the previous spent/scale tuple.
internal readonly record struct ModularDesiredRender(
    IntPtr Wrapper,
    ModularGeometryIdentity Geometry,
    string VisualKey,
    string CollisionKey,
    int LodCount,
    bool Hidden)
{
    internal bool Valid => Wrapper != IntPtr.Zero && Geometry.Valid && !string.IsNullOrEmpty(VisualKey)
        && !string.IsNullOrEmpty(CollisionKey) && LodCount is >= 1 and <= 8;
}

internal readonly record struct ModularObservedRender(
    IntPtr Wrapper,
    bool ModelPresent,
    bool VisualPresent,
    bool CollisionPresent,
    int LodCount,
    bool Hidden);

internal static class ModularRenderPolicy
{
    // Structural verification of the installed native output only. This is the
    // production success check used immediately after ApplyModular installs a
    // generation. It deliberately has no applied-generation/dirty branch: a
    // null AppliedRender is a reason to apply, not a reason to reject the
    // output that was just installed.
    internal static bool ObservedMatches(ModularDesiredRender desired, ModularObservedRender observed)
    {
        if (!desired.Valid || !observed.ModelPresent) return false;
        if (observed.Wrapper != desired.Wrapper) return false;
        if (observed.Hidden != desired.Hidden) return false;
        if (!observed.CollisionPresent) return false;
        if (desired.Hidden) return true;
        return observed.VisualPresent && observed.LodCount == desired.LodCount;
    }

    // Dirty-cache policy: structural output must match AND the committed
    // applied generation must exactly equal this desired generation. Null
    // applied means "not committed yet", so a refresh is required.
    internal static bool NeedsRefresh(ModularDesiredRender? applied, ModularDesiredRender desired,
        ModularObservedRender observed)
    {
        if (!ObservedMatches(desired, observed)) return true;
        return applied is not { } old || old != desired;
    }

    // Explicit pre-apply facts. A wrapper swap or a collision mesh that was
    // missing/replaced before/while installing must request a native rebuild
    // even when the immutable geometry identity is unchanged.
    internal static bool NeedsNativeColliderRebuild(bool wrapperChanged, bool collisionChangedOrMissing,
        ModularGeometryIdentity? applied, ModularGeometryIdentity desired)
        => wrapperChanged || collisionChangedOrMissing
            || ModularColliderPolicy.NeedsNativeColliderRebuild(applied, desired);

    // TransformChangeType flags: Translate=1, Rotate=2, Scale=4, Parent=8,
    // ParentTransform=16, Manual=32. Only scale/reparent can change paid layout.
    internal static bool IsResizeRelevant(int changeFlags)
        => (changeFlags & (4 | 8 | 16)) != 0;
}

// Finite-safe balance helpers. Invalid transient scale never grants free area
// and never propagates NaN into native mass/cost. The last valid paid area is
// retained while a load/rebuild temporarily reports an invalid scale.
internal static class ModularMassPolicy
{
    internal static float SafeArea(float area, float lastValid)
    {
        if (float.IsFinite(area) && area > 0) return area;
        return float.IsFinite(lastValid) && lastValid > 0 ? lastValid : 1f;
    }

    internal static float SafeMass(float baseMass, float areaScale)
    {
        if (!float.IsFinite(baseMass) || baseMass <= 0) return 0f;
        if (!float.IsFinite(areaScale) || areaScale <= 0) areaScale = 1f;
        var mass = (double)baseMass * areaScale;
        return double.IsFinite(mass) && mass > 0 && mass <= MaterialBalance.MaxSupportedValue
            ? (float)mass : 0f;
    }

    // Native VehicleObjectModel.Build already multiplies the part mass by the
    // model's transform area. A modular whole-cell panel is authoritative over
    // its paid cell count, so set the native base mass to the paid mass divided
    // by the native area. Otherwise a 4x5 panel is charged twice (count * native
    // scale) and a mirrored pair can show tonnes instead of ~0.2 t.
    internal static float NativeMass(float baseMass, float paidAreaScale, float nativeAreaScale)
    {
        var paid = SafeMass(baseMass, paidAreaScale);
        if (!(paid > 0)) return 0f;
        if (!float.IsFinite(nativeAreaScale) || nativeAreaScale <= 0) return paid;
        var native = (double)paid / nativeAreaScale;
        return double.IsFinite(native) && native > 0 && native <= MaterialBalance.MaxSupportedValue
            ? (float)native : 0f;
    }

    internal static bool TryAssembly(float baseAssembly, float materialCost, float areaScale, out float assembly)
    {
        assembly = 0;
        if (!float.IsFinite(materialCost) || materialCost < 0) return false;
        if (!float.IsFinite(areaScale) || areaScale <= 0) areaScale = 1f;
        var floor = (double)materialCost * 0.2;
        var scaled = float.IsFinite(baseAssembly) && baseAssembly >= 0
            ? (double)baseAssembly * areaScale : floor;
        var value = Math.Max(scaled, floor);
        if (!double.IsFinite(value) || value < 0 || value > MaterialBalance.MaxSupportedValue) return false;
        assembly = (float)value;
        return float.IsFinite(assembly);
    }
}

// Allocation-free guard shared by production refresh and regressions. Pointer
// identity detects same-count native clone/wrapper/mesh replacement. Activation
// deliberately bypasses this cache because its finite ledger changed.
internal readonly record struct ModularFastState(float ScaleX,float ScaleY,float ScaleZ,
    IntPtr Wrapper,IntPtr Interaction,IntPtr Collision,int LodCount,bool Visible)
{
    internal bool Valid=>float.IsFinite(ScaleX)&&float.IsFinite(ScaleY)&&float.IsFinite(ScaleZ)
        &&ScaleX!=0&&ScaleZ!=0&&Wrapper!=IntPtr.Zero&&Interaction!=IntPtr.Zero
        &&Collision!=IntPtr.Zero&&LodCount is >=1 and <=8;
}
