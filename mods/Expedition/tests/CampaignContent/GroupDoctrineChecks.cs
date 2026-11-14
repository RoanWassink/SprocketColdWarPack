using System.Numerics;
using SprocketExpedition.Game;

internal static class GroupDoctrineChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }

        var roles = new[] { "Center", "Center", "Center", "Flank", "Flank", "Support" };
        var layout = CampaignGroupDoctrine.Layout(roles, .65f);
        for (var i = 0; i < layout.Length; i++)
            for (var j = 0; j < i; j++)
                Check(Vector2.Distance(layout[i], layout[j]) >= 24f,
                    "compressed combined-arms offsets must retain native hull spacing");
        Check(layout[5].Y < layout[0].Y,
            "support offset must be negative so it sits farther from the player than the centre");
        Check(Math.Abs(layout[3].X) >= 96f && Math.Abs(layout[4].X) >= 96f,
            "flankers stay on separate side lanes");
        foreach (var compression in new[] { 1f, .65f })
        {
            var allCentres = CampaignGroupDoctrine.Layout(
                Enumerable.Repeat("Center", 12).ToArray(), compression);
            Check(allCentres.Select(point => point.X).Distinct().Count() == 12,
                "all twelve centre offsets must be unique at normal and compressed sizes");
            for (var i = 0; i < allCentres.Length; i++)
                for (var j = 0; j < i; j++)
                    Check(Vector2.Distance(allCentres[i], allCentres[j]) >= 24f,
                        "twelve centre tanks retain spacing at compression " + compression);
        }

        var contact = new Vector2(0, 500);
        var origin = Vector2.Zero;
        var centre = CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Center", -1, 0);
        var leftFlank = CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Flank", -1, 2);
        var rightFlank = CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Flank", 1, 3);
        var support = CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Support", -1, 4);
        Check(centre.Y > support.Y, "centre approaches closer than the support depth");
        Check(leftFlank.X < -100f && rightFlank.X > 100f,
            "flankers use stable left/right lanes instead of one shared charge point");
        Check(CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Unknown", 1, 1)
            == CampaignGroupDoctrine.ApproachWaypoint(contact, origin, "Center", 1, 1),
            "unknown roles remain neutral centre lanes");
        Check(CampaignGroupDoctrine.DefaultRole(true, 2, false) == "Flank"
            && CampaignGroupDoctrine.DefaultRole(false, 2, false) == "Center"
            && CampaignGroupDoctrine.DefaultRole(true, 1, true) == "Support",
            "friendly fallback lanes add one flank without inferring an enemy light class");

        var oddMoved = false;
        var evenMoved = false;
        for (var tick = 0; tick <= 80; tick++)
        {
            var now = tick * .25f;
            oddMoved |= CampaignGroupDoctrine.EngagementMoveAllowed(
                "Center", 1, 3, 0, coverAvailable: true, now, contactSince: 0f, contactDistance: 420f);
            evenMoved |= CampaignGroupDoctrine.EngagementMoveAllowed(
                "Center", 2, 3, 0, coverAvailable: true, now, contactSince: 0f, contactDistance: 420f);
        }
        Check(oddMoved && evenMoved,
            "both odd and even groups move across repeated 250 ms clear sightings");
        Check(!CampaignGroupDoctrine.EngagementMoveAllowed(
                "Center", 0, 3, 0, coverAvailable: true, now: 2f, contactSince: 0f, contactDistance: 420f),
            "the lead hull covers instead of charging with the first group");
        Check(!CampaignGroupDoctrine.EngagementMoveAllowed(
                "Center", 1, 3, 0, coverAvailable: false, now: 12f, contactSince: 0f, contactDistance: 420f),
            "no currently stationary cover means no clear-contact movement leg");
        Check(!CampaignGroupDoctrine.EngagementMoveAllowed(
                "Support", 1, 3, 0, coverAvailable: true, now: 12f, contactSince: 0f, contactDistance: 420f),
            "support holds depth while the centre bounds forward");
        Check(!CampaignGroupDoctrine.EngagementMoveAllowed(
                "Center", 1, 3, 0, coverAvailable: true, now: 12f, contactSince: 0f, contactDistance: 250f),
            "a close clear contact settles to fire rather than driving through it");
        Check(CampaignGroupDoctrine.EngagementNoCoverLeadAllowed("Center", 0, 3, 0, 420f)
            && !CampaignGroupDoctrine.EngagementNoCoverLeadAllowed("Center", 1, 3, 0, 420f)
            && !CampaignGroupDoctrine.EngagementNoCoverLeadAllowed("Center", 0, 3, 0, 250f),
            "only a mobile lead may break a no-cover clear-contact stall, and it settles when close");
        Check(CampaignGroupDoctrine.IsCoverSlot("Center", 0, 3, 0)
            && CampaignGroupDoctrine.IsCoverSlot("Support", 2, 3, 0)
            && !CampaignGroupDoctrine.IsCoverSlot("Flank", 1, 3, 0),
            "lead and support hulls are the deliberate covering subset");
        Check(CampaignGroupDoctrine.KeepEngagementLeg(2f, 6f, 0f, 150f, 120f, false)
            && CampaignGroupDoctrine.KeepEngagementLeg(1f, 6f, 0f, 150f, 149f, false)
            && !CampaignGroupDoctrine.KeepEngagementLeg(6f, 6f, 0f, 150f, 120f, false)
            && !CampaignGroupDoctrine.KeepEngagementLeg(2f, 6f, 0f, 150f, 120f, true)
            && !CampaignGroupDoctrine.KeepEngagementLeg(2f, 6f, 0f, 150f, 10f, false),
            "a clear-contact leg survives for its bounded duration/progress, then settles to fire");
        Check(CampaignGroupDoctrine.IsStationarySpeed(0f)
            && CampaignGroupDoctrine.IsStationarySpeed(1.5f)
            && !CampaignGroupDoctrine.IsStationarySpeed(1.6f)
            && !CampaignGroupDoctrine.IsStationarySpeed(float.NaN),
            "cover requires a finite near-stationary hull, not only an attack flag");
        Check(CampaignGroupDoctrine.SelectLeadSlot(new[]
            {
                (Slot: 0, Mobile: false, Usable: true),
                (Slot: 1, Mobile: true, Usable: true),
                (Slot: 2, Mobile: true, Usable: false)
            }, fallback: 0) == 1,
            "the lowest immobile/unsuitable slot cannot block the mobile lead from repositioning");
        Check(CampaignGroupDoctrine.SelectLeadSlot(Array.Empty<(int, bool, bool)>(), 3) == 3,
            "a force with no mobile hull retains its native fallback lead");

        var nativeOrder = new List<(string Name, int Count)>
        {
            ("Leopard", 3), ("Light", 2)
        };
        var rolesByName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Leopard"] = "Center", ["Light"] = "Flank"
        };
        var expanded = CampaignGroupDoctrine.ExpandDeploymentRoles(
            nativeOrder, rolesByName, out var matched, out var unmatched);
        Check(matched == 2 && unmatched == 0, "every actual native design name is matched");
        Check(expanded.Count(role => role == "Center") == 3
            && expanded.Count(role => role == "Flank") == 2,
            "counts expand in native dictionary order by design name");
        Check(expanded.Take(3).All(role => role == "Center")
            && expanded.Skip(3).All(role => role == "Flank"),
            "each native dictionary entry contributes its own design role, not a managed list index");
        var reversed = CampaignGroupDoctrine.ExpandDeploymentRoles(
            nativeOrder.AsEnumerable().Reverse().ToList(), rolesByName, out _, out _);
        Check(reversed.Count(role => role == "Center") == 3
            && reversed.Count(role => role == "Flank") == 2
            && reversed.Take(2).All(role => role == "Flank")
            && reversed.Skip(2).All(role => role == "Center"),
            "reversing the native dictionary follows the native order instead of a fixed slot table");
        var unknown = CampaignGroupDoctrine.ExpandDeploymentRoles(nativeOrder,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Leopard"] = "Scout" },
            out var invalidMatched, out var missing, out var unused, out var invalid);
        Check(unknown.All(role => role == "Center") && missing == 1
            && invalidMatched == 0 && unused == 0 && invalid == 1,
            "unknown native role values fail safe and are counted separately from genuinely unmatched native names");
        var absent = CampaignGroupDoctrine.ExpandDeploymentRoles(nativeOrder, null,
            out var absentMatched, out var absentUnmatched, out var absentUnused, out var absentInvalid);
        Check(absent.All(role => role == "Center") && absentMatched == 0
            && absentUnmatched == 2 && absentUnused == 0 && absentInvalid == 0,
            "with no role map every native design name is reported unmatched, not silently resolved");
        var ghost = CampaignGroupDoctrine.ExpandDeploymentRoles(nativeOrder,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Leopard"] = "Center", ["Ghost"] = "Flank"
            },
            out var ghostMatched, out var ghostUnmatched, out var ghostUnused, out var ghostInvalid);
        Check(ghost.SequenceEqual(new[] { "Center", "Center", "Center", "Center", "Center" })
            && ghostMatched == 1 && ghostUnmatched == 1 && ghostUnused == 1 && ghostInvalid == 0,
            "a configured mapping for an absent native design is reported unused and never claims a light slot");

        Check(CampaignGroupDoctrine.CanStartLeg("Center", 0, 4, 0,
                contactVisible: false, coverAvailable: false, now: 30, contactAt: 0,
                contactDistance: 400),
            "no visible contact: the group may advance toward the frozen last-known position");
        Check(CampaignGroupDoctrine.CanStartLeg("Support", 4, 1, 4,
                contactVisible: true, coverAvailable: false, now: 30, contactAt: 0,
                contactDistance: 300),
            "a lone survivor never waits for cover that cannot exist");
        Check(CampaignGroupDoctrine.CanStartLeg("Center", 0, 4, 0,
                contactVisible: true, coverAvailable: false, now: 30, contactAt: 0,
                contactDistance: 400)
            && !CampaignGroupDoctrine.CanStartLeg("Center", 1, 4, 0,
                contactVisible: true, coverAvailable: false, now: 30, contactAt: 0,
                contactDistance: 400),
            "without any covering hull exactly one lead hull may reposition");
        Check(CampaignGroupDoctrine.CanStartLeg("Center", 0, 4, 0,
                contactVisible: true, coverAvailable: true, now: 0, contactAt: 0,
                contactDistance: 400)
            && !CampaignGroupDoctrine.CanStartLeg("Center", 1, 4, 0,
                contactVisible: true, coverAvailable: true, now: 0, contactAt: 0,
                contactDistance: 400)
            && CampaignGroupDoctrine.CanStartLeg("Center", 1, 4, 0,
                contactVisible: true, coverAvailable: true, now: 10, contactAt: 0,
                contactDistance: 400),
            "covering hulls let the centre advance in alternating short bounds");
        Check(!CampaignGroupDoctrine.CanStartLeg("Support", 4, 4, 0,
                contactVisible: true, coverAvailable: true, now: 10, contactAt: 0,
                contactDistance: 250)
            && CampaignGroupDoctrine.CanStartLeg("Support", 4, 4, 0,
                contactVisible: true, coverAvailable: true, now: 10, contactAt: 0,
                contactDistance: 500),
            "support holds depth near a covered contact but closes when still far away");
        Check(!CampaignGroupDoctrine.CanStartLeg("Flank", 3, 4, 0,
                contactVisible: true, coverAvailable: true, now: 0, contactAt: 0,
                contactDistance: 140),
            "flankers settle to shoot once already inside their side-lane range");
        Check(CampaignGroupDoctrine.CanStartLeg("Support", 4, 4, 0,
                contactVisible: true, coverAvailable: false, now: 0, contactAt: 0,
                contactDistance: 300, currentlyMoving: true),
            "an already valid movement leg is never aborted by the cover gate");
        Check(CampaignGroupDoctrine.CanStartLeg("Center", 0, 4, 0,
                contactVisible: true, coverAvailable: true, now: float.NaN, contactAt: 0,
                contactDistance: 300),
            "invalid timing cannot freeze native movement");
        Console.WriteLine("PASS combined-arms layout/compression, role-by-name expansion, side lanes and bounded cover/advance phases");
    }
}
