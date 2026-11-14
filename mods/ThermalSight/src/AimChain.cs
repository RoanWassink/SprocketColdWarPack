namespace SprocketThermalSight;

// Decides whether a player gun layer's control target belongs to the linked
// cannon's mount chain. Pointers are opaque node ids and the parent lookup is
// injected, so the rule stays testable without Unity types.
internal static class AimChain
{
    internal const int MaxDepth = 64;

    // Accept the cannon's own mechanism, or any transform on the same mount
    // path as the cannon or one of its mechanisms. The nearest Cannon above the
    // target is passed in, so another gun on the same cradle stays refused.
    internal static bool Controls(IntPtr targetBehaviour, IntPtr targetTransform,
        IntPtr cannonTransform, IntPtr horizontalBehaviour, IntPtr horizontalTransform,
        IntPtr verticalBehaviour, IntPtr verticalTransform, IntPtr targetCannon, IntPtr linkedCannon,
        Func<IntPtr, IntPtr> parent)
    {
        if (targetBehaviour == IntPtr.Zero) return false;
        // A target owned by another Cannon stays refused even when that cannon
        // hangs from the same cradle.
        if (targetCannon != IntPtr.Zero && targetCannon != linkedCannon) return false;
        if (targetBehaviour == horizontalBehaviour || targetBehaviour == verticalBehaviour) return true;
        if (targetTransform == IntPtr.Zero) return false;
        if (targetTransform == cannonTransform || targetTransform == horizontalTransform
            || targetTransform == verticalTransform) return true;
        return Mounted(targetTransform, cannonTransform, parent)
            || (horizontalTransform != IntPtr.Zero && Mounted(targetTransform, horizontalTransform, parent))
            || (verticalTransform != IntPtr.Zero && Mounted(targetTransform, verticalTransform, parent));
    }

    internal static bool Mounted(IntPtr a, IntPtr b, Func<IntPtr, IntPtr> parent)
        => a != IntPtr.Zero && b != IntPtr.Zero && (OnPath(a, b, parent) || OnPath(b, a, parent));

    internal static bool OnPath(IntPtr node, IntPtr ancestor, Func<IntPtr, IntPtr> parent)
    {
        for (int depth = 0; node != IntPtr.Zero && depth < MaxDepth; depth++)
        {
            if (node == ancestor) return true;
            var next = parent(node);
            if (next == node) return false;
            node = next;
        }
        return false;
    }
}
