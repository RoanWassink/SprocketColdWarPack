using System;
using System.Collections.Generic;

namespace SprocketOscillatingTurret;

/// <summary>
/// Pure ownership matching for the runtime registry.
///
/// A native <c>TurretBehaviour</c> instance can be replaced or re-enabled during
/// the design-to-combat transition. The replacement still drives the same exact
/// native transform as the owned motor, so that transform pointer is the safe
/// identity to recover ownership with.
///
/// A match is accepted only when it is unique. If two owned entries claim the
/// same transform (for example after pointer reuse or stale registry data), the
/// result is ambiguous and the caller must leave the behaviour to the native
/// game rather than converting an unrelated turret.
/// </summary>
public static class OwnerResolution
{
    public const int NoMatch = -1;

    public static int FindUniqueTransformMatch(
        IntPtr behaviourTransform,
        IReadOnlyList<IntPtr> ownedTransforms)
    {
        if (behaviourTransform == IntPtr.Zero ||
            ownedTransforms == null ||
            ownedTransforms.Count == 0)
        {
            return NoMatch;
        }

        int match = NoMatch;
        for (int i = 0; i < ownedTransforms.Count; i++)
        {
            if (ownedTransforms[i] != behaviourTransform)
            {
                continue;
            }

            if (match != NoMatch)
            {
                // The same transform was recorded for more than one owned motor.
                return NoMatch;
            }

            match = i;
        }

        return match;
    }
}
