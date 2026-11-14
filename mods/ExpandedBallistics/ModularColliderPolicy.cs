namespace SprocketExpandedBallistics;

// The generated local mesh depends on the integer count and on the exact
// native scale compensation. A pure identity makes the "did the collision
// geometry really change?" decision testable without assuming that
// SetCollisionMesh alone rebuilds native damage colliders.
internal readonly record struct ModularGeometryIdentity(
    int CountU, int CountV, float ScaleX, float ScaleZ, int SpecVersion)
{
    internal static ModularGeometryIdentity Create(int countU, int countV, float scaleX, float scaleZ)
        => new(countU, countV, scaleX, scaleZ, 1);

    internal bool Valid => CountU is >= 1 and <= 64 && CountV is >= 1 and <= 64
        && float.IsFinite(ScaleX) && float.IsFinite(ScaleZ)
        && ScaleX != 0 && ScaleZ != 0 && SpecVersion > 0;
}

internal static class ModularColliderPolicy
{
    // True only when the generated collision geometry differs. Spent-cell
    // visual changes do not invalidate native collision, and an unchanged
    // scale/count must not request a rebuild.
    internal static bool NeedsNativeColliderRebuild(
        ModularGeometryIdentity previous, ModularGeometryIdentity current)
        => previous.Valid && current.Valid && previous != current;

    internal static bool NeedsNativeColliderRebuild(
        ModularGeometryIdentity? previous, ModularGeometryIdentity current)
        => current.Valid && (previous is not { } old || old != current);
}
