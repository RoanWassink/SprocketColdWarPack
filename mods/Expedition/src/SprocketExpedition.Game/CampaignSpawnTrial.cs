#if CAMPAIGN
using HarmonyLib;
using Sprocket;
using Sprocket.CustomBattles;
using Sprocket.Spawning;
using UnityEngine;

namespace SprocketExpedition.Game;

// Campaign-map spawn placement. Move scene-owned enemy markers before native SetupGame
// starts spawning. Never teleport assembled vehicles or edit terrain/scene assets.
internal static class CampaignSpawnTrial
{
    private static IntPtr configPointer;
    private static string layoutIdentity = string.Empty;
    private static string mapName = string.Empty;
    private static string? specialPattern;
    private static IReadOnlyDictionary<string, string>? roleNames;
    private static IReadOnlyDictionary<string, CampaignCasemateDetection.Decision>? ambusherDesigns;
    private static CampaignSpawnRange spawnRange = CampaignSpawnRange.Default;
    private static readonly List<(Transform Transform, Vector3 Position, Quaternion Rotation)> moved = new();
    private static readonly List<(Transform Transform, Transform Parent, int Index)> parked = new();
    private static readonly List<(Vector3 Position, string Name, string Role)> expectedSpawnSlots = new();
    private static GameObject? unusedMarkers;
    private static readonly List<(SpawnPoints Points, bool Random, int LastSpawnIndex)> originalSelections = new();
    internal static bool SpawnOrderVerified { get; private set; }
    internal static string Status { get; private set; } = "Native spawn layout";
    internal static void Prepare(BattleConfig config, string era, string planHash, string? pattern = null,
        IReadOnlyDictionary<string, string>? tacticalRoles = null,
        IReadOnlyDictionary<string, CampaignCasemateDetection.Decision>? ambushers = null)
    {
        Release();
        if (!CampaignMapPool.CandidateNames.Contains(config.MapName, StringComparer.Ordinal)) return;
        mapName = config.MapName;
        specialPattern = pattern;
        roleNames = tacticalRoles;
        ambusherDesigns = ambushers;
        var eraIndex = CampaignNativeCatalogue.EraIndex(era);
        spawnRange = eraIndex >= 0
            ? CampaignSpawnRange.ForEra(eraIndex)
            : CampaignSpawnRange.Default;
        if (eraIndex < 0)
            Plugin.Logger.LogWarning(
                $"Expedition spawn range: native era '{era}' is outside the native era list; "
                + "using the default range and retaining the explicit placement checks.");
        layoutIdentity = mapName + ".deployment-front-arc.v6|" + era + "|" + planHash;
        configPointer = config.Pointer;
        Status = mapName + ": planning front arc " + spawnRange.Label;
    }

    internal static void Apply(DeathmatchGameMode mode)
    {
        if (!CampaignNativeFlow.OwnsSession || configPointer == IntPtr.Zero
            || mode.gameObject.scene.name != mapName) return;
        var config = mode.context?.SetupContext?.SetupConfig?.TryCast<BattleConfig>();
        if (config?.Pointer != configPointer) return;
        try
        {
            Plugin.Logger.LogInfo($"Expedition {mapName} covered front-arc reached before SetupGame; range={spawnRange.Label}; nativePlayerTeamID={(byte)mode.playerTeamID}.");
            RestoreMarkers();
            if (mode.teams == null || mode.teams.Length != 2)
                throw new InvalidOperationException("two native teams are required");
            // The authored TeamDefinition player flag is authoritative. The
            // native mission role flags only identify which scene team carries
            // that definition; bit 1 is not assumed to mean player by itself.
            var playerDefinition = config.Teams.FirstOrDefault(t => (t.Flags & TeamDefinitionFlags.Player) != 0);
            var player = -1;
            if (playerDefinition != null)
            {
                var role = (playerDefinition.Flags & TeamDefinitionFlags.Attacker) != 0 ? 2
                    : (playerDefinition.Flags & TeamDefinitionFlags.Defender) != 0 ? 4 : 0;
                var matches = Enumerable.Range(0, mode.teams.Length).Where(i => mode.teams[i].mission != null
                    && role != 0 && (((int)mode.teams[i].mission.Flags) & role) != 0).ToArray();
                if (matches.Length == 1) player = matches[0];
            }
            if (player < 0)
                for (var i = 0; i < mode.teams.Length; i++)
                    if (mode.teams[i].mission != null && (((int)mode.teams[i].mission.Flags) & 1) != 0) player = i;
            if (player < 0 && (byte)mode.playerTeamID != 0)
                for (var i = 0; i < mode.teams.Length; i++)
                    if (mode.teams[i].spawner.TeamID.Equals(mode.playerTeamID)) player = i;
            if (player < 0)
                throw new InvalidOperationException("native player spawn team is unavailable before vehicle build; "
                    + string.Join("; ", Enumerable.Range(0, mode.teams.Length).Select(i =>
                        $"team{i}: nativeID={(byte)mode.teams[i].spawner.TeamID}, missionFlags="
                        + (mode.teams[i].mission == null ? "null" : ((int)mode.teams[i].mission.Flags).ToString()))));
            var nativeFriendly = Markers(mode.teams[player].spawnPoints);
            var enemy = Markers(mode.teams[1 - player].spawnPoints);
            var playerDefinitions = config.Teams.Where(t => (t.Flags & TeamDefinitionFlags.Player) != 0).ToArray();
            var enemyDefinitions = config.Teams.Where(t => (t.Flags & TeamDefinitionFlags.Player) == 0).ToArray();
            if (enemyDefinitions.Length != 1 || playerDefinitions.Length != 1)
                throw new InvalidOperationException("one player and one enemy definition are required");
            var friendlyCount = Count(playerDefinitions[0]);
            var enemyCount = Count(enemyDefinitions[0]);
            if (friendlyCount is < 1 or > 8 || friendlyCount > nativeFriendly.Length)
                throw new InvalidOperationException("friendly count exceeds supported native marker capacity");
            if (enemyCount is < 1 or > 12 || enemyCount > enemy.Length)
                throw new InvalidOperationException("enemy count exceeds supported native marker capacity");
            var friendlyIndices = CampaignSpawnCover.FriendlyIndices(nativeFriendly.Select(t => Horizontal(t.position)).ToArray(), friendlyCount);
            var friendly = friendlyIndices.Select(i => nativeFriendly[i]).ToArray();
            if (!CampaignBattleArea.Capture(mode, friendly.Select(marker => marker.position).ToArray()))
                throw new InvalidOperationException("native playable map bounds are unavailable");
            var requestedRange = spawnRange;
            spawnRange = CampaignBattleArea.FitSpawnRange(spawnRange);
            Plugin.Logger.LogInfo($"Expedition {mapName} formation range: requested={requestedRange.Label}; map-fitted={spawnRange.Label}; native bounds preserved.");
            if (!spawnRange.Valid)
                throw new InvalidOperationException(
                    $"authored map bounds cannot contain the requested {requestedRange.Label} formation; native layout retained");
            // Read the same native unit dictionary that the spawner enumerates.
            // Roles are matched by the actual UnitDefinition.Name, never by the
            // managed offer list index. A name absent from the pinned role map
            // stays neutral Center and is reported rather than guessed.
            var nativeUnits = new List<(string Name, int Count)>();
            foreach (var entry in enemyDefinitions[0].Units)
                nativeUnits.Add((entry.Key?.Name ?? string.Empty, entry.Value.Count));
            var deploymentRoles = CampaignGroupDoctrine.ExpandDeploymentRoles(
                nativeUnits, roleNames, out var matchedRoleNames, out var unmatchedNativeRoleNames,
                out var unusedRoleMappings, out var invalidRoleValues)
                .ToList();
            if (roleNames == null && (specialPattern ?? "CombinedArms") == "CombinedArms")
                Plugin.Logger.LogWarning(
                    $"Expedition {mapName} formation: no authored deployment roles; all hulls use Center lanes.");
            else if (unmatchedNativeRoleNames > 0)
                Plugin.Logger.LogWarning(
                    $"Expedition {mapName} formation: {unmatchedNativeRoleNames} native design name(s) have no configured role; "
                    + "those hulls keep neutral Center lanes and are reported instead of being inferred.");
            if (unusedRoleMappings > 0)
                Plugin.Logger.LogWarning(
                    $"Expedition {mapName} formation: {unusedRoleMappings} configured role name(s) do not occur in the native lineup; no slot was inferred for them.");
            if (invalidRoleValues > 0)
                Plugin.Logger.LogWarning(
                    $"Expedition {mapName} formation: {invalidRoleValues} configured role value(s) are invalid; those native hulls keep neutral Center lanes.");
            if (deploymentRoles.Count != enemyCount)
                throw new InvalidOperationException(
                    "native deployment roles do not match the enumerated UnitDefinition counts");
            Plugin.Logger.LogInfo($"Expedition {mapName} formation roles: matched={matchedRoleNames}; "
                + $"unmatchedNative={unmatchedNativeRoleNames}; unusedConfigured={unusedRoleMappings}; "
                + $"invalidValues={invalidRoleValues}; "
                + $"center={deploymentRoles.Count(role => role == CampaignGroupDoctrine.Center)}; "
                + $"flank={deploymentRoles.Count(role => role == CampaignGroupDoctrine.Flank)}; "
                + $"support={deploymentRoles.Count(role => role == CampaignGroupDoctrine.Support)}; native dictionary order retained.");
            var sightRays = 0;
            var terrainProbes = 0;
            var groundFailure = string.Empty;
            var coverFailure = string.Empty;
            bool SpendTerrainProbe() => ++terrainProbes <= 48000;
            var friendlyGround = friendly.Select(marker =>
            {
                if (!SpendTerrainProbe())
                    throw new InvalidOperationException("spawn terrain-probe limit reached");
                return Numeric(Surface(marker.position).point);
            }).ToArray();
            var friendlyCenter = Center(friendly);
            var originalFriendlyCenter = friendlyCenter;
            var enemyCenter = Center(enemy);
            var clearances = enemy.Take(enemyCount).Select(marker =>
                NativeClearance(marker, SpendTerrainProbe)).ToArray();
            Plugin.Logger.LogInfo($"Expedition {mapName} native marker clearance retained: {clearances.Min():N2}..{clearances.Max():N2} m; minimum 1 m; upright rotations on relocated markers.");
            var direction = enemyCenter - friendlyCenter;
            direction.y = 0f;
            var originalGap = direction.magnitude;
            if (originalGap < 1f) throw new InvalidOperationException("native group centers overlap");
            Plugin.Logger.LogInfo($"Expedition {mapName} layout request: {enemyCount}/{enemy.Length} enemy slots; {friendly.Length}/{nativeFriendly.Length} selected friendly anchors spanning x={friendly.Max(x => x.position.x) - friendly.Min(x => x.position.x):N1} m, z={friendly.Max(x => x.position.z) - friendly.Min(x => x.position.z):N1} m.");
            var originalAnchors = friendly.Select(marker =>
            {
                var heading = marker.forward;
                heading.y = 0f;
                if (heading.sqrMagnitude < 0.01f) heading = direction;
                return new SpawnArcAnchor(new System.Numerics.Vector2(marker.position.x, marker.position.z),
                    new System.Numerics.Vector2(heading.x, heading.z));
            }).ToArray();
            var anchors = originalAnchors;
            var proposed = new Vector3[enemyCount];
            var friendlyClearances = friendly.Select(marker =>
                NativeClearance(marker, SpendTerrainProbe)).ToArray();
            var proposedFriendly = new Vector3[friendlyCount];
            bool GroundClear(int index, System.Numerics.Vector2 point)
            {
                if (terrainProbes >= 48000)
                {
                    groundFailure = "spawn terrain-probe budget exhausted";
                    return false;
                }
                var ok = Ground(new Vector3(point.X, friendlyCenter.y, point.Y), clearances[index],
                    SpendTerrainProbe, out proposed[index], out var groundReason);
                if (!ok) groundFailure = groundReason;
                return ok;
            }
            bool Covered(int index, System.Numerics.Vector2 point)
            {
                if (sightRays >= 16384)
                {
                    coverFailure = "spawn cover sight-ray budget exhausted";
                    return false;
                }
                var ground = proposed[index] - Vector3.up * clearances[index];
                return CampaignSpawnCover.Occluded(friendlyGround, Numeric(ground), (from, to) =>
                {
                    if (++sightRays > 16384)
                    {
                        coverFailure = "spawn cover sight-ray budget exhausted";
                        return false;
                    }
                    var start = Native(from);
                    var delta = Native(to) - start;
                    var distance = delta.magnitude;
                    // Ignore the last/first four metres so local spawn clearance
                    // or a nearby shrub cannot be mistaken for useful separation.
                    return distance > 8f && Physics.Raycast(start + delta.normalized * 4f, delta.normalized,
                        out _, distance - 8f, ~0, QueryTriggerInteraction.Ignore);
                });
            }
            // GroundClear populates the exact candidate used by Covered. Every
            // full pass fills all proposed indices before native markers change.
            var arc = Array.Empty<System.Numerics.Vector2>();
            var withCover = false;
            var diagnostics = "no safe friendly deployment";
            var planned = false;
            var deployment = -1;
            var fittedRange = spawnRange;
            var rangeAttempts = spawnRange.Attempts().ToArray();
            foreach (var candidate in CampaignDeploymentPlanner.Candidates(originalAnchors, layoutIdentity))
            {
                deployment++;
                var clear = true;
                for (var i = 0; i < friendlyCount; i++)
                {
                    var point = candidate[i].Position;
                    if (!Ground(new Vector3(point.X, originalFriendlyCenter.y, point.Y),
                        friendlyClearances[i], SpendTerrainProbe, out proposedFriendly[i], out var friendlyReason))
                    { groundFailure = friendlyReason; clear = false; break; }
                }
                if (!clear) continue;
                friendlyCenter = proposedFriendly.Aggregate(Vector3.zero, (sum, p) => sum + p) / friendlyCount;
                friendlyGround = proposedFriendly.Select((p, i) => Numeric(p - Vector3.up * friendlyClearances[i])).ToArray();
                anchors = candidate;
                var identity = layoutIdentity + "|deployment=" + deployment;
                // Reserve a full final search for the original safe anchors.
                var openBudget = deployment < CampaignDeploymentPlanner.OriginalCandidate ? 256 : 4096;
                foreach (var attemptRange in rangeAttempts)
                {
                    planned = CampaignSpecialSpawnPlanner.TryPlan(anchors, enemyCount, identity,
                        specialPattern ?? "CombinedArms", GroundClear, Covered, out arc, out withCover,
                        out diagnostics, openBudget, attemptRange, deploymentRoles,
                        allowOpenFallback: false,
                        terrainSamplesPerPoint: CampaignSpawnSupport.Footprint.Length);
                    if (!planned) continue;
                    spawnRange = attemptRange;
                    break;
                }
                if (planned) break;
            }
            if (!planned)
                throw new InvalidOperationException(
                    $"no complete safe front-arc layout for {enemyCount} enemies; {diagnostics}; "
                    + $"lastGround={(string.IsNullOrEmpty(groundFailure) ? "none" : groundFailure)}; "
                    + $"lastCover={(string.IsNullOrEmpty(coverFailure) ? "none" : coverFailure)}");
            var expectedNames = new List<string>(enemyCount);
            foreach (var (name, count) in nativeUnits)
                for (var i = 0; i < count; i++) expectedNames.Add(name);
            if (expectedNames.Count != enemyCount)
                throw new InvalidOperationException(
                    "native lineup names do not match the enumerated enemy count");
            var ambusherFlags = new bool[enemyCount];
            var ambusherSources = new string[enemyCount];
            for (var i = 0; i < enemyCount; i++)
            {
                ambusherSources[i] = "none";
                if (ambusherDesigns == null || !ambusherDesigns.TryGetValue(expectedNames[i], out var decision)) continue;
                ambusherFlags[i] = decision.IsAmbusher;
                ambusherSources[i] = decision.SourceName;
            }
            var ambusherCount = ambusherFlags.Count(flag => flag);
            if (ambusherCount > 0)
            {
                // Reuse the already validated marker set. Permuting grounded
                // points adds no terrain probes and cannot break bounds, cover,
                // upright rotation or minimum spacing.
                var heights = Enumerable.Range(0, enemyCount).Select(i => proposed[i].y).ToArray();
                var order = CampaignAmbusherPlacement.OrderByHeight(heights, ambusherFlags);
                var originalProposed = (Vector3[])proposed.Clone();
                var originalArc = (System.Numerics.Vector2[])arc.Clone();
                for (var i = 0; i < enemyCount; i++)
                {
                    proposed[i] = originalProposed[order[i]];
                    arc[i] = originalArc[order[i]];
                }
                Plugin.Logger.LogInfo($"Expedition ambusher placement: {ambusherCount}/{enemyCount} design slots "
                    + "assigned the highest validated markers; terrainProbes unchanged; bounds/cover/spacing retained.");
            }
            expectedSpawnSlots.Clear();
            for (var i = 0; i < enemyCount; i++)
                expectedSpawnSlots.Add((proposed[i], expectedNames[i], deploymentRoles[i]));
            SpawnOrderVerified = false;
            ActivateMarkers(mode.teams[player].spawnPoints, nativeFriendly, friendlyIndices);
            ActivateMarkers(mode.teams[1 - player].spawnPoints, enemy, Enumerable.Range(0, enemyCount).ToArray());
            for (var i = 0; i < friendlyCount; i++)
            {
                moved.Add((friendly[i], friendly[i].position, friendly[i].rotation));
                friendly[i].position = proposedFriendly[i];
                var heading = anchors[i].Forward;
                friendly[i].rotation = Quaternion.LookRotation(new Vector3(heading.X, 0, heading.Y), Vector3.up);
            }
            for (var i = 0; i < enemyCount; i++)
            {
                moved.Add((enemy[i], enemy[i].position, enemy[i].rotation));
                enemy[i].position = proposed[i];
                // Every enemy marker faces the friendly deployment centre; an
                // ambusher therefore starts the battle already on its bearing.
                var forward = friendlyCenter - proposed[i];
                forward.y = 0f;
                enemy[i].rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                if (ambusherFlags[i])
                {
                    var toPlayer = friendlyCenter - proposed[i];
                    toPlayer.y = 0f;
                    var facing = toPlayer.sqrMagnitude > .01f
                        ? Vector3.Angle(enemy[i].forward, toPlayer) : 0f;
                    Plugin.Logger.LogInfo($"Expedition ambusher: enemy slot {i + 1}; "
                        + $"design='{expectedNames[i]}'; source={ambusherSources[i]}; "
                        + $"height={(proposed[i].y - friendlyCenter.y):N1} m above player spawn; "
                        + $"facing={facing:N0} deg to player.");
                }
            }
            Physics.SyncTransforms();
            var distances = arc.SelectMany(point => anchors.Select(anchor => System.Numerics.Vector2.Distance(point, anchor.Position))).ToArray();
            var placement = deployment < CampaignDeploymentPlanner.RotationCandidate ? "moved deployment"
                : deployment == CampaignDeploymentPlanner.RotationCandidate ? "rotation-only fallback" : "original-anchor fallback";
            var actualRange = distances.Min().ToString("N0") + "-"
                + distances.Max().ToString("N0") + " m";
            Status = mapName + ": " + placement + " requested " + requestedRange.Label
                + " · map " + fittedRange.Label + " · placed " + actualRange
                + " - bounds checked - " + (withCover ? "cover checked" : "open fallback (no complete cover layout)");
            Plugin.Logger.LogInfo($"Expedition {mapName} front-arc APPLIED: requested={requestedRange.Label}; "
                + $"mapFitted={fittedRange.Label}; formation={spawnRange.Label}; actualPlaced={actualRange}; "
                + $"original centers={originalGap:N1} m; all-marker range={distances.Min():N1}..{distances.Max():N1} m; "
                + $"{enemyCount}/{enemy.Length} active separated, grounded, upright enemy markers; cover={withCover}; "
                + $"sightRays={sightRays}; {diagnostics}.");
            Plugin.Logger.LogInfo($"Expedition deployment: candidate={deployment}; placement={placement}; friendly center {FormatPosition(originalFriendlyCenter)} -> {FormatPosition(friendlyCenter)}; displacement={Vector3.Distance(originalFriendlyCenter, friendlyCenter):N0} m; terrainProbes={terrainProbes}; native spawn markers only; retry identity fixed.");
        }
        catch (Exception ex)
        {
            RestoreMarkers();
            Status = mapName + ": spawn adjustment unavailable (native layout)";
            Plugin.Logger.LogWarning($"Expedition {mapName} spawn adjustment retained native layout: " + ex.Message);
        }
    }

    private static Transform[] Markers(SpawnLocator locator)
    {
        var points = locator?.TryCast<SpawnPoints>();
        if (points == null || points.randomOnConnectingLines || points.transform.childCount == 0
            || points.transform.childCount > 128 || points.MaxSpawn != points.transform.childCount)
            throw new InvalidOperationException("the native spawn locator is not a supported discrete marker layout");
        return Enumerable.Range(0, points.transform.childCount).Select(points.transform.GetChild).ToArray();
    }

    // Actual spawned position/design/role comparison against the planned marker
    // slot. This reads only the real spawned hulls; it never reads a hidden target
    // transform and never teleports a vehicle.
    internal static bool VerifySpawnedEnemies(IReadOnlyList<(Vector3 Position, string Name)> spawned)
    {
        var expected = expectedSpawnSlots.Select(slot => new SpawnPlanSlot(
            new System.Numerics.Vector2(slot.Position.x, slot.Position.z),
            slot.Name,
            slot.Role)).ToArray();
        var observed = spawned.Select(item => new SpawnObservation(
            new System.Numerics.Vector2(item.Position.x, item.Position.z),
            item.Name)).ToArray();
        if (!CampaignSpawnVerification.Verify(expected, observed, roleNames, out var error))
        {
            Plugin.Logger.LogWarning(
                $"Expedition spawn verification failed: {error}; role lanes will be neutral.");
            SpawnOrderVerified = false;
            return false;
        }
        SpawnOrderVerified = true;
        Plugin.Logger.LogInfo($"Expedition spawn verification: {expected.Length} actual hulls matched planned marker position/design/role order; roles remain active.");
        return true;
    }
    private static Vector3 Center(Transform[] markers)
    {
        var center = Vector3.zero;
        foreach (var marker in markers) center += marker.position;
        return center / markers.Length;
    }
    private static float NativeClearance(Transform marker, Func<bool> spendProbe)
    {
        if (!spendProbe()) throw new InvalidOperationException("spawn terrain-probe limit reached");
        var hit = Surface(marker.position);
        var clearance = marker.position.y - hit.point.y;
        if (!float.IsFinite(clearance) || clearance > 12f)
            throw new InvalidOperationException("original marker clearance is outside the supported trial range");
        return Math.Max(1f, clearance);
    }
    private static RaycastHit Surface(Vector3 position)
    {
        if (!Physics.Raycast(position + Vector3.up * 500f, Vector3.down, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore)
            || hit.collider?.TryCast<TerrainCollider>() == null)
            throw new InvalidOperationException("original spawn-marker terrain could not be measured");
        return hit;
    }
    private static System.Numerics.Vector2 Horizontal(Vector3 p) => new(p.x, p.z);
    private static string FormatPosition(Vector3 p) => $"({p.x:N0}, {p.z:N0})";
    private static System.Numerics.Vector3 Numeric(Vector3 p) => new(p.x, p.y, p.z);
    private static Vector3 Native(System.Numerics.Vector3 p) => new(p.X, p.Y, p.Z);
    private static int Count(TeamDefinition team)
    {
        var total = 0;
        foreach (var entry in team.Units ?? throw new InvalidOperationException("native lineup is unavailable"))
        {
            if (entry.Key == null || entry.Value == null || entry.Value.Count < 1)
                throw new InvalidOperationException("native lineup contains an invalid quantity");
            total = checked(total + entry.Value.Count);
        }
        return total;
    }
    private static void ActivateMarkers(SpawnLocator locator, Transform[] markers, int[] selected)
    {
        var points = locator.TryCast<SpawnPoints>()!;
        originalSelections.Add((points, points.random, points.lastSpawnIndex));
        points.random = false;
        if (selected.Length < markers.Length)
        {
            if (unusedMarkers == null)
            {
                unusedMarkers = new GameObject("Expedition unused spawn markers");
                unusedMarkers.SetActive(false);
            }
            var newlyParked = markers.Where((_, index) => !selected.Contains(index))
                .Select(marker => (Transform: marker, Parent: marker.parent, Index: marker.GetSiblingIndex())).ToArray();
            parked.AddRange(newlyParked);
            foreach (var saved in newlyParked) saved.Transform.SetParent(unusedMarkers.transform, true);
        }
        if (locator.MaxSpawn != selected.Length)
            throw new InvalidOperationException("native locator did not accept the encounter marker count");
        // NextSpawnIndex is (lastSpawnIndex + 1) % childCount and the getter does
        // not consume it. Point the field one before marker 0 so the native
        // sequential spawn pass begins at the first active marker.
        var activeCount = points.transform.childCount;
        if (activeCount != selected.Length || activeCount < 1)
            throw new InvalidOperationException("native locator active marker count is inconsistent");
        points.lastSpawnIndex = activeCount - 1;
        if (points.NextSpawnIndex != 0)
            throw new InvalidOperationException(
                $"native next spawn index is {points.NextSpawnIndex}; expected sequential marker 0");
    }
    private static bool Ground(Vector3 point, float clearance, Func<bool> spendProbe, out Vector3 grounded,
        out string failure)
    {
        grounded = default;
        failure = string.Empty;
        var samples = new SpawnGroundSample[CampaignSpawnSupport.Footprint.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            if (!spendProbe()) { failure = "spawn terrain-probe limit reached"; return false; }
            var offset = CampaignSpawnSupport.Footprint[i];
            var probe = point + new Vector3(offset.X, 0f, offset.Y);
            if (!Physics.Raycast(probe + Vector3.up * 500f, Vector3.down, out var hit, 1000f, ~0,
                    QueryTriggerInteraction.Ignore)
                || hit.collider?.TryCast<TerrainCollider>() == null)
            {
                samples[i] = new SpawnGroundSample(default, default, false);
                continue;
            }
            samples[i] = new SpawnGroundSample(Numeric(hit.point), Numeric(hit.normal), true);
        }

        if (!CampaignSpawnSupport.TryEvaluate(samples, out var center, out var reason))
        {
            failure = reason;
            return false;
        }
        var centerWorld = Native(center);
        if (!CampaignBattleArea.Contains(centerWorld, 32f))
        {
            failure = "spawn candidate is outside the playable bounds";
            return false;
        }
        var blockers = Physics.OverlapBox(centerWorld + Vector3.up * 2.5f, new Vector3(7f, 2f, 7f),
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var collider in blockers)
            if (collider != null && collider.TryCast<TerrainCollider>() == null)
            {
                failure = "spawn candidate collides with a non-terrain collider";
                return false;
            }
        grounded = centerWorld + Vector3.up * clearance;
        return true;
    }
    private static void RestoreMarkers()
    {
        foreach (var saved in moved)
            if (saved.Transform != null)
            {
                saved.Transform.position = saved.Position;
                saved.Transform.rotation = saved.Rotation;
            }
        moved.Clear();
        foreach (var selection in originalSelections)
            if (selection.Points != null)
            {
                selection.Points.random = selection.Random;
                selection.Points.lastSpawnIndex = selection.LastSpawnIndex;
            }
        originalSelections.Clear();
        foreach (var saved in parked.OrderBy(x => x.Index))
            if (saved.Transform != null && saved.Parent != null)
            {
                saved.Transform.SetParent(saved.Parent, true);
                saved.Transform.SetSiblingIndex(saved.Index);
            }
        parked.Clear();
        if (unusedMarkers != null) UnityEngine.Object.Destroy(unusedMarkers);
        unusedMarkers = null;
        expectedSpawnSlots.Clear();
        SpawnOrderVerified = false;
    }
    internal static void Release()
    {
        RestoreMarkers();
        CampaignBattleArea.Release();
        configPointer = IntPtr.Zero;
        layoutIdentity = string.Empty;
        mapName = string.Empty;
        specialPattern = null;
        roleNames = null;
        ambusherDesigns = null;
        spawnRange = CampaignSpawnRange.Default;
        Status = "Native spawn layout";
    }
}

[HarmonyPatch(typeof(DeathmatchGameMode), nameof(DeathmatchGameMode.SetupGame))]
internal static class CampaignSpawnTrialPatch
{
    static void Prefix(DeathmatchGameMode __instance) => CampaignSpawnTrial.Apply(__instance);
}
#endif
