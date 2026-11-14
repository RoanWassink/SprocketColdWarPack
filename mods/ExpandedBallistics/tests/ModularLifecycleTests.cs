using System;
using System.Collections.Generic;
using System.Linq;
using SprocketExpandedBallistics.Materials;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Tests;

// Pure regressions for the mirror/clone/save-load lifecycle policy. These do not
// prove native hook coverage or gameplay; they lock the queue/generation and
// finite-balance decisions that the native hooks call.
internal static class ModularLifecycleTests
{
    internal static void Run(Action<bool, string> check)
    {
        PerfDetailChecks.Run(check);
        var a = new IntPtr(101);
        var b = new IntPtr(202);
        var queue = new ModularRefreshQueue(8);
        var seen = new List<IntPtr>();
        check(queue.Enqueue(a), "exact transform owner is queued");
        check(!queue.Enqueue(a), "same exact owner is not duplicated while pending");
        queue.BeginDrain();
        while (queue.TryDequeue(out var owner))
        {
            seen.Add(owner);
            // A nested callback on the mirror peer may enqueue the first owner
            // again. That owner must not be processed twice in the same drain.
            if (owner == a) queue.Enqueue(b);
            else if (owner == b) queue.Enqueue(a);
        }
        queue.EndDrain();
        check(seen.SequenceEqual(new[] { a, b }),
            "A -> nested B(mirror) -> A processes both distinct owners exactly once");
        check(queue.Count == 0, "bounded drain empties its pending queue");
        check(queue.Enqueue(a), "a later real event can queue the same owner again after the drain");
        queue.Clear();

        var tiny = new ModularRefreshQueue(2);
        check(tiny.Enqueue(a) && tiny.Enqueue(b) && !tiny.Enqueue(new IntPtr(303)),
            "refresh queue bounds exact owners instead of growing without limit");
        tiny.Clear();

        check(!ModularRenderPolicy.IsResizeRelevant(1),
            "plain Translate transform events do not enqueue layout/mass work");
        check(!ModularRenderPolicy.IsResizeRelevant(2),
            "plain Rotate transform events do not enqueue layout/mass work");
        check(ModularRenderPolicy.IsResizeRelevant(4),
            "Scale transform events enqueue exact-owner reconstruction");
        check(ModularRenderPolicy.IsResizeRelevant(8) &&
              ModularRenderPolicy.IsResizeRelevant(16),
            "Parent/ParentTransform events enqueue exact-owner reconstruction");
        check(ModularRenderPolicy.IsResizeRelevant(1 | 4) &&
              ModularRenderPolicy.IsResizeRelevant(2 | 8),
            "combined transform flags remain relevant when Scale/Parent is present");
        check(!ModularRenderPolicy.IsResizeRelevant(32),
            "Manual-only transform flag does not trigger layout work");

        var wrapper = new IntPtr(11);
        var otherWrapper = new IntPtr(12);
        var geometry = ModularGeometryIdentity.Create(2, 3, 2f, -3f);
        var desired = new ModularDesiredRender(wrapper, geometry, "v1", "c1", 4, false);
        var successfulOutput = new ModularObservedRender(wrapper, true, true, true, 4, false);
        check(ModularRenderPolicy.ObservedMatches(desired, successfulOutput),
            "production output verification succeeds for a correctly installed wrapper/mesh/LOD");
        check(ModularRenderPolicy.NeedsRefresh(null, desired, successfulOutput),
            "null applied is still a dirty-cache reason to apply, not an output mismatch");
        check(!ModularRenderPolicy.NeedsRefresh(desired, desired, successfulOutput),
            "unchanged geometry, wrapper meshes, LOD and visibility need no refresh");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { Wrapper = otherWrapper }),
            "output verification rejects a recreated/wrong native wrapper");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { ModelPresent = false }),
            "output verification rejects a missing native model");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { VisualPresent = false }),
            "output verification rejects a missing visual mesh");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { CollisionPresent = false }),
            "output verification rejects a missing collision mesh");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { LodCount = 0 }),
            "output verification rejects a reset/default LOD generation");
        check(!ModularRenderPolicy.ObservedMatches(desired,
                successfulOutput with { Hidden = true }),
            "output verification rejects the wrong hidden/visible state");
        check(ModularRenderPolicy.NeedsRefresh(null, desired,
                successfulOutput),
            "a model with no committed applied generation refreshes even at correct output");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired,
                new ModularObservedRender(IntPtr.Zero, false, false, false, 0, false)),
            "a missing native model stays pending instead of being marked current");
        check(!ModularRenderPolicy.NeedsRefresh(desired, desired,
                new ModularObservedRender(wrapper, true, true, true, 4, false)),
            "the same exact owner clears once the native model and meshes are ready");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired,
                new ModularObservedRender(wrapper, true, false, true, 4, false)),
            "same wrapper with a missing desired visual mesh refreshes");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired,
                new ModularObservedRender(wrapper, true, true, false, 4, false)),
            "same wrapper with a missing collision mesh refreshes");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired,
                new ModularObservedRender(wrapper, true, true, true, 0, false)),
            "new native wrapper/LOD generation refreshes at identical scale/count");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired with { Wrapper = otherWrapper },
                new ModularObservedRender(otherWrapper, true, true, true, 4, false)),
            "a new native wrapper refreshes even when scale, layout and meshes are identical");
        check(ModularRenderPolicy.NeedsRefresh(desired, desired with { VisualKey = "v2" },
                new ModularObservedRender(wrapper, true, true, true, 4, false)),
            "new render generation identity refreshes at identical scale/count");
        var hidden = desired with { VisualKey = "hidden:c1", Hidden = true };
        check(ModularRenderPolicy.ObservedMatches(hidden,
                new ModularObservedRender(wrapper, true, false, true, 0, true)),
            "hidden output verification succeeds without requiring a rendered visual/LOD");
        check(!ModularRenderPolicy.ObservedMatches(hidden,
                new ModularObservedRender(wrapper, true, false, true, 0, false)),
            "hidden output verification rejects a visible native wrapper");
        check(!ModularRenderPolicy.NeedsRefresh(hidden, hidden,
                new ModularObservedRender(wrapper, true, true, true, 0, true)),
            "fully spent hidden generation stays current without requiring a rendered LOD");
        check(ModularRenderPolicy.NeedsRefresh(hidden, hidden with { Hidden = false },
                new ModularObservedRender(wrapper, true, true, true, 0, true)),
            "visibility change is a lifecycle refresh, not a stale early return");

        check(ModularRenderPolicy.NeedsNativeColliderRebuild(false, false, geometry, geometry) == false,
            "same wrapper, same collision and same geometry do not loop native collider rebuild");
        check(ModularRenderPolicy.NeedsNativeColliderRebuild(false, true, geometry, geometry),
            "a collision mesh that was missing/replaced before apply requests a rebuild");
        check(ModularRenderPolicy.NeedsNativeColliderRebuild(true, false, geometry, geometry),
            "a wrapper swap at identical geometry requests a rebuild");
        check(ModularRenderPolicy.NeedsNativeColliderRebuild(false, false, null, geometry),
            "first generated collision generation requests a native collider rebuild");
        check(ModularRenderPolicy.NeedsNativeColliderRebuild(false, false, geometry,
                ModularGeometryIdentity.Create(2, 4, 2f, -3f)),
            "whole-cell count change requests a native collider rebuild");

        check(Math.Abs(ModularMassPolicy.SafeArea(float.NaN, 2.5f) - 2.5f) < 1e-6,
            "transient invalid scale retains the last valid paid area");
        check(Math.Abs(ModularMassPolicy.SafeArea(0, 0) - 1f) < 1e-6,
            "no valid area ever falls back to the paid base, not zero");
        check(Math.Abs(ModularMassPolicy.SafeMass(3.3024f, 6f) - 19.8144f) < 1e-4,
            "finite paid-cell area scales mass");
        check(Math.Abs(ModularMassPolicy.SafeMass(3.3024f, float.NaN) - 3.3024f) < 1e-6,
            "invalid transient area keeps finite base mass instead of NaN");
        check(ModularMassPolicy.SafeMass(float.NaN, 2f) == 0f,
            "invalid configured base mass is rejected rather than propagated as NaN");
        check(Math.Abs(ModularMassPolicy.NativeMass(1.6125f, 20f, 20f) - 1.6125f) < 1e-6,
            "4x5 native base mass is divided by the native area so it is not charged twice");
        var expectedNative = (float)(1.6125 * 18.0 / 19.76358463035311);
        check(Math.Abs(ModularMassPolicy.NativeMass(1.6125f, 18f, 19.76358463035311f) - expectedNative) < 1e-5,
            "non-integer native scale is compensated back to the exact paid cell count");
        check(Math.Abs(ModularMassPolicy.NativeMass(1.6125f, 20f, 0f) - 32.25f) < 1e-5,
            "missing native area falls back to the paid mass instead of zero");
        check(ModularMassPolicy.NativeMass(float.NaN, 20f, 20f) == 0f,
            "invalid native base mass is rejected rather than propagated");
        var nativeStage = ModularMassPolicy.NativeMass(1.6125f, 20f, 20f);
        var nativeHousing = ModularMassPolicy.NativeMass(1.7187732f, 20f, 20f);
        check(Math.Abs((2 * nativeStage + nativeHousing) * 20f * 2f - 197.750928f) < 1e-3,
            "a mirrored 4x5 standard panel resolves to 197.8 kg, not 4 t");
        check(ModularMassPolicy.TryAssembly(float.NaN, 10f, 2f, out var assembly) &&
              float.IsFinite(assembly) && Math.Abs(assembly - 2f) < 1e-6,
            "non-finite native assembly base falls back to the finite material floor");
        check(ModularMassPolicy.TryAssembly(5f, 10f, 2f, out assembly) &&
              float.IsFinite(assembly) && Math.Abs(assembly - 10f) < 1e-6,
            "valid assembly base remains finite and paid-area scaled");
        check(!ModularMassPolicy.TryAssembly(5f, float.NaN, 2f, out _),
            "non-finite material cost is rejected, not swallowed as a NaN SetCost");

        var liveSpent = new HashSet<int> { 0, 1, 2, 3 };
        check(ModularCellGeometry.AllSpent(liveSpent, 2, 2),
            "a fully spent current 2x2 layout is detected exactly");
        var staleSpent = new HashSet<int> { 4, 5, 6, 7 };
        check(!ModularCellGeometry.AllSpent(staleSpent, 2, 2),
            "stale out-of-range spent coordinates from a larger layout do not hide a reconstructed layer");
        var partlySpent = new HashSet<int> { 0, 1, 2 };
        check(!ModularCellGeometry.AllSpent(partlySpent, 2, 2),
            "a partially spent current layout remains visible");
    }
}
