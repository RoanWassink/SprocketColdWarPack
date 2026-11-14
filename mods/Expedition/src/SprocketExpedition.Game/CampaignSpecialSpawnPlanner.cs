using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SprocketExpedition.Game;

// Whole-formation placement, all-or-nothing before native spawning. Every hull
// retains normal front/range/spacing, terrain, cover and playable-area checks.
internal static class CampaignSpecialSpawnPlanner
{
    internal static bool TryPlan(IReadOnlyList<SpawnArcAnchor> anchors, int count, string identity, string pattern,
        Func<int, Vector2, bool> ground, Func<int, Vector2, bool> covered,
        out Vector2[] positions, out bool withCover, out string diagnostics, int maximumOpenTerrainProbes = 4096,
        CampaignSpawnRange? range = null, IReadOnlyList<string>? deploymentRoles = null,
        bool allowOpenFallback = true, int terrainSamplesPerPoint = 1)
    {
        positions = Array.Empty<Vector2>(); withCover = false; diagnostics = "invalid special formation";
        var distances = range ?? CampaignSpawnRange.Default;
        if (!distances.Valid) return false;
        if (anchors.Count == 0 || count is < 1 or > 12 || pattern is not ("Swarm" or "Line" or "Split" or "CombinedArms" or "Ambush" or "Defensive")
            || anchors.Any(a => !Finite(a.Position) || !Finite(a.Forward) || a.Forward.LengthSquared() < .01f)) return false;
        // Ambush is anchored to the player's initial spawn marker, not to the
        // friendly centroid or an averaged heading. Every other pattern keeps
        // the group-centre geometry. The caller supplies friendly anchors in
        // native marker order, so anchors[0] is that player reference.
        var isAmbush = pattern == "Ambush";
        var playerAnchor = anchors[0];
        var center = isAmbush
            ? playerAnchor.Position
            : anchors.Aggregate(Vector2.Zero, (sum, a) => sum + a.Position) / anchors.Count;
        Vector2 heading;
        if (isAmbush)
        {
            heading = Vector2.Normalize(playerAnchor.Forward);
        }
        else
        {
            heading = anchors.Aggregate(Vector2.Zero,
                (sum, a) => sum + Vector2.Normalize(a.Forward));
            if (heading.LengthSquared() < .01f) return false;
            heading = Vector2.Normalize(heading);
        }
        uint random = BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "|" + pattern)));
        if (random == 0) random = 0x6d2b79f5;
        float Next() { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (float)(random / 4294967296d); }
        var probes = 0;
        var passCount = allowOpenFallback ? 2 : 1;
        var samplesPerPoint = terrainSamplesPerPoint > 0 ? terrainSamplesPerPoint : 1;
        for (var pass = 0; pass < passCount; pass++)
        {
            var startProbes = probes;
            var budget = pass == 0 && allowOpenFallback ? 512 : maximumOpenTerrainProbes;
            var budgetLimit = budget * samplesPerPoint;
            for (var attempt = 0; attempt < 384 && probes - startProbes < budgetLimit; attempt++)
            {
                var yaw = isAmbush ? 0f : (Next() - .5f) * (pattern == "Split" ? .20f : .80f);
                var rightOfHeading = new Vector2(heading.Y, -heading.X);
                var forward = heading * MathF.Cos(yaw) + rightOfHeading * MathF.Sin(yaw);
                var right = new Vector2(forward.Y, -forward.X);
                var radius = distances.Minimum + (distances.Maximum - distances.Minimum) * (.35f + Next() * .25f);
                var points = new Vector2[count];
                IReadOnlyList<string> roles = deploymentRoles != null && deploymentRoles.Count == count
                    ? deploymentRoles : Enumerable.Repeat("Center", count).ToArray();
                var offsets = pattern == "CombinedArms" ? CampaignGroupDoctrine.Layout(
                    roles, attempt < 192 ? 1f : .65f) : Array.Empty<Vector2>();
                var ambush = isAmbush
                    ? CampaignGroupDoctrine.AmbushOffsets(roles, radius)
                    : Array.Empty<Vector2>();
                for (var i = 0; i < count; i++)
                {
                    if (pattern == "CombinedArms")
                        points[i] = center + forward * (radius - offsets[i].Y) + right * offsets[i].X;
                    else if (pattern == "Ambush")
                        points[i] = center + forward * ambush[i].Y + right * ambush[i].X;
                    else if (pattern == "Defensive")
                    {
                        // Compact two-row defensive box in front of the group
                        // centre: the boss and its close escort hold together.
                        var columns = Math.Min(3, count);
                        var rows = (count + columns - 1) / columns;
                        points[i] = center
                            + forward * (radius + (i / columns - (rows - 1) * .5f) * 26f)
                            + right * (i % columns - (columns - 1) * .5f) * 26f;
                    }
                    else if (pattern == "Swarm")
                    {
                        var columns = Math.Min(4, count); var rows = (count + columns - 1) / columns;
                        points[i] = center + forward * (radius + (i / columns - (rows - 1) * .5f) * 24f)
                            + right * (i % columns - (columns - 1) * .5f) * 24f;
                    }
                    else if (pattern == "Line")
                        points[i] = center + forward * radius + right * (i - (count - 1) * .5f) * 60f;
                    else
                    {
                        var side = i % 2 == 0 ? -1f : 1f; var local = i / 2;
                        var groupForward = forward * MathF.Cos(.65f) + right * (side * MathF.Sin(.65f));
                        var groupRight = new Vector2(groupForward.Y, -groupForward.X);
                        points[i] = center + groupForward * (radius + local / 2 * 24f) + groupRight * (local % 2 - .5f) * 24f;
                    }
                }
                if (points.Any(point => !Finite(point) || anchors.Any(a =>
                {
                    var delta = point - a.Position; var distance = delta.LengthSquared();
                    return distance < distances.Minimum * distances.Minimum
                        || distance > distances.Maximum * distances.Maximum
                        || (!isAmbush && Vector2.Dot(delta, a.Forward) < 0f);
                }))) continue;
                // Exact 3/9 o'clock lies on the player's side plane and can be
                // a rounding hair behind it. Ambush therefore uses a player-
                // relative side/front arc with a small scale-aware tolerance;
                // extra friendly anchors still enforce distance to every hull.
                if (isAmbush && points.Any(point =>
                {
                    var delta = point - playerAnchor.Position;
                    var tolerance = MathF.Max(.05f, delta.Length() * 1e-4f);
                    return Vector2.Dot(delta, heading) < -tolerance;
                })) continue;
                if (points.Where((point, i) => points.Take(i).Any(p => Vector2.DistanceSquared(point, p) < 24f * 24f - .01f)).Any()) continue;
                var valid = true;
                for (var i = 0; i < count; i++)
                {
                    probes += samplesPerPoint;
                    if (probes - startProbes > budgetLimit || !ground(i, points[i]) || (pass == 0 && !covered(i, points[i]))) { valid = false; break; }
                }
                if (!valid) continue;
                positions = points; withCover = pass == 0;
                diagnostics = pattern + ": complete formation; range=" + distances.Label
                    + "; terrainProbes=" + probes + (withCover ? "; covered" : "; open fallback");
                return true;
            }
        }
        diagnostics = pattern + ": no complete safe formation; range=" + distances.Label
            + "; terrainProbes=" + probes + (allowOpenFallback ? string.Empty : "; covered-only");
        return false;
    }
    private static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
