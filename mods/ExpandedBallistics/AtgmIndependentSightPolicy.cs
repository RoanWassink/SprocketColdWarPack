namespace SprocketExpandedBallistics;
internal static class AtgmIndependentSightPolicy
{
    // 0.22.87: one or more healthy compatible dedicated launchers form the sight's
    // group (one tube is the 0.22.86 case). An ordinary cannon on the same sight,
    // another vehicle or no healthy launcher still refuses the sight.
    internal static bool Allows(bool player,int dedicatedMatches,bool ordinaryShares,bool validLauncherProfile)=>
        player&&dedicatedMatches>=1&&!ordinaryShares&&validLauncherProfile;
    // The group: the linked dedicated launchers of the sight's own vehicle that are
    // healthy and have a launchable ATGM profile, in VUID order. A broken or
    // unusable tube falls out; the others stay.
    internal static int[] Members(IReadOnlyList<AtgmLinkedLauncher> linked)=>
        Enumerable.Range(0,linked.Count).Where(i=>linked[i].SameVehicle&&linked[i].Healthy&&linked[i].Compatible)
            .OrderBy(i=>linked[i].Vuid).ThenBy(i=>i).ToArray();
    internal static bool InGroup(IReadOnlyList<IntPtr> group,IntPtr cannon)=>cannon!=IntPtr.Zero&&group.Contains(cannon);
    // A running sight session keeps going when tubes leave its group; any other
    // change (another tube, a joined tube, one tube replaced) binds anew, as 0.22.86.
    internal static bool Keeps(IReadOnlyList<IntPtr> before,IReadOnlyList<IntPtr> after)=>
        after.Count>0&&(before.Count==1?after.Count==1&&after[0]==before[0]:after.All(before.Contains));
    // The head may move when at least one tube of the group has a valid gunner.
    internal static bool HeadMoves(IReadOnlyList<(bool Assigned,bool Valid)> crews)=>crews.Any(c=>AtgmSightHead.CanMove(c.Assigned,c.Valid));
    // A sight-guided missile from one tube of a group keeps its guidance when another
    // tube of the same group fires: two missiles in the air, both on the one sight.
    // The same tube firing again, one tube, another sight or another mode: superseded as before.
    internal static bool GuidesTogether(IReadOnlyList<IntPtr> group,IntPtr olderTube,string? olderMode,IntPtr newerTube,string? newerMode)=>
        group.Count>=2&&olderTube!=newerTube&&InGroup(group,olderTube)&&InGroup(group,newerTube)&&olderMode=="sight"&&newerMode=="sight";
    internal static string Vuids(IEnumerable<int> vuids)=>string.Join(",",vuids);
}
internal readonly record struct AtgmLinkedLauncher(int Vuid,bool SameVehicle,bool Healthy,bool Compatible);
// The log lines of the sight. One tube: the 0.22.86 texts, character for character.
internal static class AtgmSightLog
{
    private const string Tag="[ATGM guidance sight] ";
    internal static string SkipAssignment(int sight,int matches,int healthy,bool ordinary)=>matches<=1
        ?$"{Tag}SKIP sight={sight}: requires exactly one healthy compatible dedicated launcher; launcherMatches={matches} ordinaryShared={ordinary}."
        :$"{Tag}SKIP sight={sight}: requires at least one healthy compatible dedicated launcher and no ordinary cannon on this sight; launcherMatches={matches} healthy={healthy} ordinaryShared={ordinary}.";
    // A dedicated launcher on the same mount that is not on this sight: the fix is to link it too.
    internal static string SkipSharedElevation(int sight,IReadOnlyList<int> unlinkedLaunchers)=>
        $"{Tag}SKIP sight={sight}: launcher shares elevation with another cannon; use a separate elevation mount or a fixed tube"+
        (unlinkedLaunchers.Count==0?"":$"; link every launcher on this mount to the sight (unlinked launcher {AtgmIndependentSightPolicy.Vuids(unlinkedLaunchers)})")+".";
    internal static string SharedTraverse(int sight)=>$"{Tag}Shared turret traverse retained for sight={sight}; native yaw commands remain available to other weapons.";
    internal static string Bind(IReadOnlyList<int> launchers,int sight)=>launchers.Count==1
        ?$"{Tag}BIND launcher={launchers[0]} sight={sight}; native ammo/reload retained; optical aim independent from tube."
        :$"{Tag}BIND launchers={AtgmIndependentSightPolicy.Vuids(launchers)} sight={sight} (group of {launchers.Count}); native ammo/reload retained; optical aim independent from tubes.";
    internal static string Group(int sight,IReadOnlyList<int> launchers,IReadOnlyList<int> left)=>
        $"{Tag}GROUP sight={sight} launchers={AtgmIndependentSightPolicy.Vuids(launchers)} (group of {launchers.Count}); launcher {AtgmIndependentSightPolicy.Vuids(left)} left the group.";
    internal static string NoGunner(IReadOnlyList<int> launchers)=>launchers.Count==1
        ?$"{Tag}No gunner on launcher {launchers[0]}: sight head locked"
        :$"{Tag}No gunner on launchers {AtgmIndependentSightPolicy.Vuids(launchers)}: sight head locked";
    internal static string TurretFollows(int sight)=>$"{Tag}Turret follows sight={sight}: shared turret yaw stays native; head elevates only (head yaw 0).";
}

internal readonly record struct AtgmAimingAxes(IntPtr Horizontal,IntPtr Vertical);
internal static class AtgmGuidanceAxisOwnership
{
    // A shared turret yaw remains under native control. Shared elevation cannot
    // simultaneously hold a fixed tube angle and preserve the other gun's laying.
    internal static bool SharedElevation(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)=>
        Shared(own.Vertical,others);
    internal static HashSet<IntPtr> ExclusiveAxes(AtgmAimingAxes own,IReadOnlyList<AtgmAimingAxes> others)
    {
        var result=new HashSet<IntPtr>();
        foreach(var axis in new[]{own.Horizontal,own.Vertical})
            if(axis!=IntPtr.Zero&&!Shared(axis,others))result.Add(axis);
        return result;
    }
    private static bool Shared(IntPtr axis,IReadOnlyList<AtgmAimingAxes> others)=>
        axis!=IntPtr.Zero&&others.Any(o=>o.Horizontal==axis||o.Vertical==axis);
    // 0.22.87 group: "outside" are the cannons that are not tubes of this sight. An
    // axis shared only by tubes of the group (one tilting block) is the group's own;
    // each axis is held or driven once. One tube gives the single-tube result.
    internal static bool GroupSharedElevation(IReadOnlyList<AtgmAimingAxes> group,IReadOnlyList<AtgmAimingAxes> outside)=>
        group.Any(g=>Shared(g.Vertical,outside));
    internal static HashSet<IntPtr> GroupExclusiveAxes(IReadOnlyList<AtgmAimingAxes> group,IReadOnlyList<AtgmAimingAxes> outside)
    {
        var result=new HashSet<IntPtr>();
        foreach(var own in group)result.UnionWith(ExclusiveAxes(own,outside));
        return result;
    }
    // The outside cannons that share an elevation axis with a tube of the group.
    internal static int[] ElevationConflicts(IReadOnlyList<AtgmAimingAxes> group,IReadOnlyList<AtgmAimingAxes> outside)=>
        Enumerable.Range(0,outside.Count).Where(i=>group.Any(g=>g.Vertical!=IntPtr.Zero&&(outside[i].Horizontal==g.Vertical||outside[i].Vertical==g.Vertical))).ToArray();
}

// 0.22.87 (user 15-11, "Toren volgt de sight"): a head sight on a turret that the
// game itself yaws toward the aim point (a shared native turret ring that also
// carries a tube of the group) leaves the yaw to the turret. The head elevates
// only (head yaw 0) and the native scope camera keeps its own world aim, so the
// turret is the one controller on that rotation. Without such a turret (a free
// sight, or the ring mode on an own unshared ring) nothing changes.
internal readonly record struct AtgmTurretRing(bool SightAxis,bool Pitch,bool Connected,bool LauncherYaw,bool Exclusive);
internal static class AtgmSharedTurretHead
{
    internal static bool Applies(bool headMode,IEnumerable<AtgmTurretRing> rings)=>
        headMode&&rings.Any(r=>r.SightAxis&&!r.Pitch&&r.Connected&&r.LauncherYaw&&!r.Exclusive);
    internal static AtgmHeadAngles Command(AtgmHeadAngles command,bool turret)=>turret?new(0,command.Pitch):command;
    internal static bool WritesCamera(bool turret)=>!turret;
}
