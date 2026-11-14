using System.Text.Json;
using System.Xml;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Tests;

internal static class SightIntegrationTests
{
    internal static void Run(Action<bool, string> check)
    {
        // Expedition picker policy: only the exact vanilla gunner-sight card is
        // hidden, and only on an affirmative authoritative session signal.
        check(SightMenuPolicy.ShouldHide(true) && !SightMenuPolicy.ShouldHide(false) && !SightMenuPolicy.ShouldHide(null), "picker hides only on affirmative Expedition ownership");
        check(SightMenuPolicy.IsVanillaGuid("f90f373f-4edb-4563-a162-6eee6a5d77dd") && SightMenuPolicy.IsVanillaGuid(SightMenuPolicy.VanillaGunnerSightGuid.ToUpperInvariant()), "exact vanilla GUID matches case-insensitively");
        check(!SightMenuPolicy.IsVanillaGuid(SightCatalog.Ww1Guid) && !SightMenuPolicy.IsVanillaGuid(SightCatalog.Ww2Guid) && !SightMenuPolicy.IsVanillaGuid(null), "WW and missing GUIDs are not filtered");
        check(SightMenuPolicy.IsVanillaMarker(SightMenuPolicy.VanillaMarker + SightMenuPolicy.VanillaGunnerSightGuid) && !SightMenuPolicy.IsVanillaMarker("other"), "only the exact vanilla marker is filtered");
        check(SightCatalog.TryResolveIdentifier("WW1 sight", out var id1) && id1.Key == "ww1" && SightCatalog.TryResolveIdentifier("ww2Sight", out var id2) && id2.Key == "ww2", "icon lookup accepts localized names and part names");
        check(SightCatalog.TryResolveIdentifier(SightCatalog.Ww1Guid, out var id3) && id3.Key == "ww1" && !SightCatalog.TryResolveIdentifier("not-a-sight", out _), "icon lookup is exact for GUIDs and rejects unknown keys");
        check(SightMenuPolicy.RemapActive(1, new[] { false, false, false }) == 1, "active group index is unchanged when nothing is removed");
        check(SightMenuPolicy.RemapActive(2, new[] { false, true, false }) == 1, "active group index shifts after an earlier removal");
        check(SightMenuPolicy.RemapActive(1, new[] { false, true, false }) == 1, "removed active group selects the next remaining group");
        check(SightMenuPolicy.RemapActive(0, new[] { true }) == 0, "empty filtered panel keeps a safe active index");

        // Closed-source bridge discovery: absence is transient; only a present but
        // incompatible shape may disable the version-locked adapter.
        check(BridgeInstallPolicy.Decide(false, false, false) == BridgeInstallAction.Wait, "missing DynamicSight waits and retries");
        check(BridgeInstallPolicy.Decide(true, false, false) == BridgeInstallAction.Disable, "present incompatible shape disables the adapter");
        check(BridgeInstallPolicy.Decide(true, true, false) == BridgeInstallAction.Install, "complete compatible shape installs");
        check(BridgeInstallPolicy.Decide(false, false, true) == BridgeInstallAction.Install, "installed bridge stays installed");

        // Stable part identities and representative, sourced fixed magnification.
        check(SightCatalog.Ww1.Guid != SightCatalog.Ww2.Guid, "WW1 and WW2 sight GUIDs are distinct");
        check(SightCatalog.Ww1.DisplayName == "WW1 sight" && SightCatalog.Ww2.DisplayName == "WW2 sight", "exact required part names");
        check(SightCatalog.Ww1.Magnification == 2f && SightCatalog.Ww2.Magnification == 5f, "fixed historical magnification values");
        check(SightCatalog.Ww1.Guid != "f90f373f-4edb-4563-a162-6eee6a5d77dd", "vanilla sight GUID is not reused");
        check(SightCatalog.TryResolve(SightCatalog.Ww1Guid, out var w1) && w1.Key == "ww1", "WW1 GUID resolves");
        check(SightCatalog.TryResolve(SightCatalog.Ww2Guid.ToUpperInvariant(), out var w2) && w2.Key == "ww2", "WW2 GUID resolves case-insensitively");
        check(!SightCatalog.TryResolve(null, out _) && !SightCatalog.TryResolve("", out _) && !SightCatalog.TryResolve("f90f373f-4edb-4563-a162-6eee6a5d77dd", out _), "vanilla/missing/unknown sights do not get WW zoom");
        check(SightCatalog.Ww1.Date == "1917.05.01" && SightCatalog.Ww2.Date == "1943.11.01", "era dates are finite and ordered");
        check(SightCatalog.Ww1.MassKg > 0 && SightCatalog.Ww2.MassKg > SightCatalog.Ww1.MassKg, "WW2 optic carries the larger mass allowance");

        // Angle-correct FOV mapping, not reference-FOV division.
        const float reference = 60f;
        var one = SightCatalog.VerticalFieldOfView(reference, 1f);
        var two = SightCatalog.VerticalFieldOfView(reference, 2f);
        var five = SightCatalog.VerticalFieldOfView(reference, 5f);
        check(Math.Abs(one - reference) < .001f, "1x keeps the reference vertical FOV");
        check(two < one && five < two && five > .01f, "FOV falls monotonically with magnification");
        check(Math.Abs(two - reference / 2f) > 1f, "linear FOV division would be wrong");
        var ratio = MathF.Tan(two * MathF.PI / 360f) * 2f;
        check(Math.Abs(ratio - MathF.Tan(reference * MathF.PI / 360f)) < 1e-5f, "2x preserves the tangent angle ratio");
        var ratio5 = MathF.Tan(five * MathF.PI / 360f) * 5f;
        check(Math.Abs(ratio5 - MathF.Tan(reference * MathF.PI / 360f)) < 1e-5f, "5x preserves the tangent angle ratio");
        check(SightCatalog.SafeVerticalFieldOfView(float.NaN, float.NaN) > 0f, "invalid zoom input falls back safely");
        check(Throws(() => SightCatalog.VerticalFieldOfView(0f, 2f)) && Throws(() => SightCatalog.VerticalFieldOfView(60f, 0f)), "invalid FOV/magnification is rejected");

        // Additive native part definitions and localization files.
        var dir = Path.Combine(AppContext.BaseDirectory, "SightParts");
        using (var ww1 = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "ww1SightPart.json"))))
        using (var ww2 = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "ww2SightPart.json"))))
        {
            var a = ww1.RootElement;
            var b = ww2.RootElement;
            check(a.GetProperty("v").GetString() == "0.2" && b.GetProperty("v").GetString() == "0.2", "part JSON uses the supported top-level v0.2 schema");
            check(a.TryGetProperty("order", out var orderA) && orderA.GetInt32() == 0 && b.TryGetProperty("order", out var orderB) && orderB.GetInt32() == 0, "v0.2 parts include the native-required order member");
            var topLevel = new[] { "v", "guid", "name", "order", "tags", "components", "transform", "date" };
            check(a.EnumerateObject().Select(p => p.Name).OrderBy(n => n).SequenceEqual(topLevel.OrderBy(n => n)), "WW1 top-level schema matches working v0.2 custom parts");
            check(b.EnumerateObject().Select(p => p.Name).OrderBy(n => n).SequenceEqual(topLevel.OrderBy(n => n)), "WW2 top-level schema matches working v0.2 custom parts");
            check(a.GetProperty("guid").GetString() == SightCatalog.Ww1Guid && b.GetProperty("guid").GetString() == SightCatalog.Ww2Guid, "part JSON carries the stable catalog GUIDs");
            check(a.GetProperty("name").GetString() == "ww1Sight" && b.GetProperty("name").GetString() == "ww2Sight", "part JSON names are stable");
            check(a.GetProperty("date").GetString() == SightCatalog.Ww1.Date && b.GetProperty("date").GetString() == SightCatalog.Ww2.Date, "part JSON dates match the catalog");
            check(a.GetProperty("guid").GetString() != "f90f373f-4edb-4563-a162-6eee6a5d77dd", "additive part never mutates the vanilla identity");
            var sightA = a.GetProperty("components")[0];
            var sightB = b.GetProperty("components")[0];
            check(sightA.GetProperty("type").GetString() == "sight" && sightB.GetProperty("type").GetString() == "sight", "both parts keep the native sight component");
            var lensA = sightA.GetProperty("info").GetProperty("objectiveLensPosition");
            var lensB = sightB.GetProperty("info").GetProperty("objectiveLensPosition");
            check(lensA.GetArrayLength() == 3 && lensB.GetArrayLength() == 3 && lensA[2].GetDouble() == .0409 && lensB[2].GetDouble() == .0409, "vanilla lens geometry cloned");
            check(a.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).SequenceEqual(new[] { "gunner", "scope", "cannon" }), "vanilla sight tags preserved");
            check(a.GetProperty("components")[1].GetProperty("info").GetProperty("mass").GetDouble() == 4, "WW1 mass is explicit");
            check(b.GetProperty("components")[1].GetProperty("info").GetProperty("mass").GetDouble() == 12, "WW2 mass is explicit");
            check(Guid.TryParse(a.GetProperty("guid").GetString(), out _) && Guid.TryParse(b.GetProperty("guid").GetString(), out _), "part GUIDs serialize as UUIDs");
        }
        check(XmlName(Path.Combine(dir, "ww1Sight.xml")) == "WW1 sight" && XmlName(Path.Combine(dir, "ww2Sight.xml")) == "WW2 sight", "localization names match exactly");

        // A carried mix always has a real next request; absent/unlisted saved selection falls back.
        var carried = AmmoLoadoutRules.Parse("heat=6;hesh=3");
        check(AmmoSnapshotFormat.NextSelection(carried, null) == "heat", "missing saved selection falls back to the first carried type");
        check(AmmoSnapshotFormat.NextSelection(carried, "vanilla") == "heat", "unlisted vanilla selection falls back to the carried mix");
        check(AmmoSnapshotFormat.NextSelection(carried, "hesh") == "hesh", "valid carried selection is retained");
        check(AmmoSnapshotFormat.NextSelection(AmmoLoadoutRules.Parse(""), "heat") == null, "empty mix has no next selection");

        // Canonical HUD identity: selected next load and chambered round stay distinct.
        var rounds = new[]
        {
            new AmmoSnapshotRound("apfsds", "APFSDS long rod", "APFSDS", 9, true, false),
            new AmmoSnapshotRound("heat", "HEAT-FS", "HEAT", 6, false, true)
        };
        var mixed = new AmmoSnapshot("heat", "HEAT-FS", "HEAT", "apfsds", "APFSDS long rod", "APFSDS", true, 15, rounds, false, null);
        var lines = AmmoSnapshotFormat.Lines(mixed);
        check(lines[0].Contains("HEAT-FS", StringComparison.Ordinal) && lines[1].Contains("APFSDS long rod", StringComparison.Ordinal), "HUD separates next load from the round in the gun");
        check(lines[3].StartsWith(">", StringComparison.Ordinal) && lines[3].Contains("HEAT-FS", StringComparison.Ordinal), "selected next type is marked");
        check(!lines[3].Contains("(includes round in gun)", StringComparison.Ordinal) && lines[2].Contains("(includes round in gun)", StringComparison.Ordinal), "only the chambered type claims the gun");
        check(mixed.TotalRounds == 15 && mixed.Rounds.Sum(r => r.Count) == 15, "total counts each carried round once");
        var emptyGun = new AmmoSnapshot("heat", "HEAT-FS", "HEAT", null, null, null, false, 6, new[] { rounds[1] with { Chambered = false, Requested = true } }, false, null);
        check(AmmoSnapshotFormat.Lines(emptyGun)[1].Contains(AmmoSnapshotFormat.EmptyGun, StringComparison.Ordinal), "empty/loading gun is never labeled chambered");
        check(!AmmoSnapshotFormat.Lines(emptyGun)[2].Contains("(includes round in gun)", StringComparison.Ordinal), "empty/loading HUD has no false chambered marker");
        var missing = new AmmoSnapshot(null, null, null, null, null, null, false, 0, Array.Empty<AmmoSnapshotRound>(), false, null);
        check(AmmoSnapshotFormat.Lines(missing).Count == 2 && !AmmoSnapshotFormat.Lines(missing)[1].Contains("APFSDS", StringComparison.Ordinal), "missing ammunition has no stale identity");
        var frozen = new AmmoSnapshot(null, null, null, null, null, null, false, 0, Array.Empty<AmmoSnapshotRound>(), true, "storage already belongs to another cannon");
        check(AmmoSnapshotFormat.Lines(frozen)[0].Contains("unavailable", StringComparison.Ordinal), "frozen mix reports the real failure");
        check(AmmoSnapshotFormat.CodeFor("apfsds", null) == "APFSDS" && AmmoSnapshotFormat.CodeFor("", "HEAT-FS") == "HEAT" && AmmoSnapshotFormat.CodeFor(null, null) == "?", "canonical round type codes");

        // Two cannons/vehicles must not share selection state.
        var other = new AmmoSnapshot("apfsds", "APFSDS long rod", "APFSDS", "heat", "HEAT-FS", "HEAT", true, 4,
            new[] { new AmmoSnapshotRound("heat", "HEAT-FS", "HEAT", 1, true, false), new AmmoSnapshotRound("apfsds", "APFSDS long rod", "APFSDS", 3, false, true) }, false, null);
        check(mixed.NextProfileId == "heat" && other.NextProfileId == "apfsds" && mixed.ChamberedProfileId == "apfsds" && other.ChamberedProfileId == "heat", "separate cannon snapshots keep independent identities");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (ArgumentOutOfRangeException) { return true; }
    }

    private static string? XmlName(string path)
    {
        var document = new XmlDocument();
        document.Load(path);
        return document.DocumentElement?.SelectSingleNode("name")?.InnerText;
    }
}
