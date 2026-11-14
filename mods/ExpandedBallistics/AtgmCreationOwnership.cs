namespace SprocketExpandedBallistics;

// Runtime identities only. Scope is a synchronous native factory invocation on
// one vehicle. A partially failing factory may register parts before returning.
internal readonly record struct AtgmCreatedObject(IntPtr Object,IntPtr Parent);
internal static class AtgmCreationOwnership
{
    internal static HashSet<IntPtr> NewRoots(IReadOnlySet<IntPtr> before,IEnumerable<AtgmCreatedObject> after)
    {
        var added=after.Where(p=>p.Object!=IntPtr.Zero&&!before.Contains(p.Object)).GroupBy(p=>p.Object).Select(g=>g.First()).ToArray();
        var ids=added.Select(p=>p.Object).ToHashSet();
        return added.Where(p=>!ids.Contains(p.Parent)).Select(p=>p.Object).ToHashSet();
    }
}
