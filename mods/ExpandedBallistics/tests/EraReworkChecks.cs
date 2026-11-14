using System.Numerics;
using SprocketExpandedBallistics.Materials;

internal static class EraReworkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool ok, string message) { checks++; check(ok, message); }
        List<Vector3> Load(IEnumerable<string> lines, string group = "mount", Func<Vector3, Vector3, Vector3, bool>? skip = null)
            => EraObjGeometry.Read(lines, group, (x, y, z) => new Vector3(x, y, z),
                v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), skip);
        void Invalid(IEnumerable<string> lines, string message)
        {
            bool rejected = false;
            try { Load(lines); } catch (InvalidDataException) { rejected = true; }
            Verify(rejected, message);
        }
        string[] Triangles(int count) => new[] { "v 0 0 0", "v 1 0 0", "v 0 1 0", "g mount" }
            .Concat(Enumerable.Repeat("f 1 2 3", count)).ToArray();

        var brat = Load(File.ReadLines(Path.Combine(AppContext.BaseDirectory, "BratAssets", "brat-housing-state-0.obj")));
        Verify(brat.Count == 1904 * 3, "production OBJ loader accepts the actual 1904-triangle BRAT housing");
        Verify(Load(Triangles(1904)).Count == 5712, "loader accepts a synthetic 1904-triangle group");
        Verify(EraObjGeometry.MaximumExpandedVertices == 20_000, "loader maximum is exactly 20000 expanded vertices");
        Verify(Load(Triangles(6666)).Count == 19998, "loader accepts the largest complete triangle group below 20000");
        Invalid(Triangles(6667), "loader rejects 20001 expanded vertices despite only three source positions");
        Invalid(Triangles(0), "empty geometry remains invalid");
        Invalid(Triangles(1).Append("f 1 2 4"), "out-of-range vertex remains invalid");
        Invalid(Triangles(1).Append("f 0 2 3"), "nonpositive vertex remains invalid");
        Invalid(Triangles(1).Append("f 1 2 3 1"), "nontriangular face remains invalid");
        Invalid(Triangles(1).Select(l => l == "v 0 0 0" ? "v NaN 0 0" : l), "nonfinite expanded geometry remains invalid");
        Verify(Load(Triangles(1).Concat(new[] { "g cassette" }).Concat(Enumerable.Repeat("f 1 2 3", 6667))).Count == 3,
            "OBJ cap applies only to the selected group");
        int seen = 0;
        Verify(Load(Triangles(6667), skip: (_, _, _) => ++seen > 6666).Count == 19998,
            "spent-cell filtering still precedes the expanded geometry limit");

        // Independent expected family/variant order, including every visible GUID.
        var families = new[]
        {
            new[] { "62a4ab89-1e44-5885-857d-b482485df8f6" },
            // 0.22.73: the four War Thunder size variants follow the existing Kontakt-5 (small to large footprint).
            new[] { "06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c", "1ca6af2e-ad50-5d1f-a279-707967b2b8ad", "c81b2bc5-5d43-5224-a69e-e00f408b3380", "2228fbbd-e161-5a05-9319-4c94922abf61", "7344ca78-e479-55e7-b11f-c23fb17f38b4" },
            new[] { "6b4e1dc7-9020-5443-90c2-0f52c80933f9", "c8bc7cd9-1e9a-5a53-a613-306846df488a",
                "dd04b5c1-ed08-5406-bec5-de39cfe21814" },
            // 0.20.0: Nizh panel, the three Duplet parts, then Nizh-LM (the old Duplet and Nizh types are hidden).
            new[] { "d706e182-a7c5-5a2a-9231-681480af74cc", "ea9df1d4-18f5-5c8f-a6d0-d2c20cd3bc91",
                "3532720a-7150-51da-85a5-00d94530dd46", "ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c",
                "0f0f3f44-3931-5d72-bd5c-5d2159b13dc6" },
            new[] { "51733efc-4963-5f1a-a9a3-842484aee9e7", "54e6f2dd-6ade-58ca-a7ee-f07ad79f583d" },
            new[] { "89a52df5-1f81-50ad-845f-69ed756ce1ad" },
            new[] { "ed56d261-d2b8-553a-b900-1d3040d8980b" },
            new[] { "6adf6262-7687-57d0-bd11-ce1582ca783d" },
            new[] { "fe6c499f-6487-583b-ae1b-39064f14e344" },
            new[] { SprocketExpandedBallistics.CompositeModule.PartGuid },
        };
        var expected = families.SelectMany(f => f).ToArray();
        var visible = EraPresetRegistry.Presets.Select(p => p.PartGuid).Distinct(StringComparer.Ordinal)
            .Where(g => !EraGeneratedDefinitions.LegacyPartGuids.Contains(g)).ToArray();
        Verify(visible.Length == 21 && visible.ToHashSet(StringComparer.Ordinal).SetEquals(expected),
            "all 21 visible GUIDs match the family catalogue after the modular Relikt, Nizh and Duplet replacements");
        Verify(EraMenuOrder.Ranks.Count == 21 && EraMenuOrder.Ranks.Keys.ToHashSet().SetEquals(visible) &&
            EraMenuOrder.Ranks.Values.Distinct().Count() == 21, "rank table covers exactly the visible GUIDs with unique ranks");
        var random = new Random(184);
        for (int run = 0; run < 10; run++)
            Verify(EraMenuOrder.Sort(visible.OrderBy(_ => random.Next()), g => g, g => EraGeneratedDefinitions.MenuLabels.GetValueOrDefault(g, g))
                .SequenceEqual(expected), "every visible family and size sorts correctly from shuffled input " + run);
        var unknown = new[] { (Id: "u-z", Name: "Z", Index: 0), (Id: "u-a", Name: "A", Index: 1),
            (Id: "u-b", Name: "A", Index: 2), (Id: expected[0], Name: "Z", Index: 3) };
        Verify(EraMenuOrder.Sort(unknown, p => p.Id, p => p.Name).Select(p => p.Index).SequenceEqual(new[] { 3, 1, 2, 0 }),
            "unknown GUIDs follow known families, sort by ordinal name, and retain equal-name input order");

        const string hidden = "203b6295-460f-5757-b32a-a368d026a889";
        Verify(EraGeneratedDefinitions.LegacyPartGuids.Contains(hidden) && !visible.Contains(hidden),
            "duplicate Relikt turret 1x2 is hidden through the existing legacy filter");
        var aliases = EraGeneratedDefinitions.Presets.Where(p => p.PartGuid == hidden).ToArray();
        Verify(aliases.Length == 2, "both legacy Relikt turret 1x2 presets are retained");
        for (int cell = 1; cell <= 2; cell++)
        {
            var alias = aliases.Single(p => p.Cassette == $"reliktTurretCompact12Stage1Cell{cell}");
            var canonical = EraGeneratedDefinitions.Presets.Single(p => p.Cassette == $"reliktCompactStage1Cell{cell}");
            Verify(alias.Mount == "reliktTurretCompact12Housing" && alias.PartGuid == hidden &&
                alias.HousingActiveIds!.SequenceEqual(new[] { "reliktTurretCompact12Stage1Cell1", "reliktTurretCompact12Stage1Cell2" }),
                "legacy cassette, mount, GUID and housing active identities remain intact for cell " + cell);
            Verify(alias.Visual == canonical.Visual && alias.Collision == canonical.Collision && alias.Icon == canonical.Icon &&
                alias.SpentVisual == canonical.SpentVisual && alias.MountCollision == canonical.MountCollision &&
                alias.MountVisual == canonical.MountVisual && alias.HousingDamageVisuals!.SequenceEqual(canonical.HousingDamageVisuals!),
                "all legacy Relikt asset references match canonical compact for cell " + cell);
            Verify(alias.CassetteMass == canonical.CassetteMass && alias.MountMass == canonical.MountMass,
                "legacy Relikt component masses equal canonical compact for cell " + cell);
        }
        var labels = new[] { "Relikt 1x1", "Relikt 1x2", "Relikt 2+1", "Relikt 2x2" };
        var oldRelikt = new[] { "ad460401-467a-548f-97d3-875666eaf742", "de46580b-dc12-5004-855a-ce85694cef85",
            "ade97085-9eb6-5b71-a12a-38dea4d7351f", "5a88679a-ee60-5a12-93b3-5acbe780854b" };
        Verify(oldRelikt.Select(g => EraGeneratedDefinitions.MenuLabels[g]).SequenceEqual(labels) &&
            oldRelikt.All(g => EraGeneratedDefinitions.LegacyPartGuids.Contains(g)),
            "the four fixed Relikt types keep their names but are hidden through the legacy filter");
        Verify(families[2].All(g => !EraGeneratedDefinitions.MenuLabels.ContainsKey(g)),
            "the modular Relikt panels take their menu names from localization, not from a label override");
        Console.WriteLine($"ERA rework PASS: {checks} checks.");
    }
}
