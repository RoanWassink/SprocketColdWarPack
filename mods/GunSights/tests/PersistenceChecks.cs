using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketThermalSight;

namespace SprocketGunSights;

// 0.3.0: per-sight choices in the object state (sprocketGunSightsV1) next to Thermal Sight's keys.
// Thermal's current codec files are linked read-only from the sibling source tree.
internal static class PersistenceChecks
{
    // Thermal Sight ConsolePersistence.Capture, on a dictionary (same filter/append as its own tests).
    private static List<KeyValuePair<string, JsonNode?>> ThermalCapture(IEnumerable<KeyValuePair<string, JsonNode?>> state,
        IEnumerable<ConsoleLinkEntry> links, IEnumerable<ThermalProfileEntry> profiles)
    {
        var next = state.Where(e => !ObjectStateRows.Owned(e.Key)).Select(e => new KeyValuePair<string, JsonNode?>(e.Key, e.Value?.DeepClone())).ToList();
        foreach (var env in ObjectStateRows.Envelopes(links, profiles)) next.Add(new(env.Key, JsonValue.Create(env.Value)));
        return next;
    }
    private static List<KeyValuePair<string, JsonNode?>> OurCapture(IEnumerable<KeyValuePair<string, JsonNode?>> state, IEnumerable<SightStateEntry> rows) =>
        SightStateData.Capture(state.Select(e => new KeyValuePair<string, JsonNode?>(e.Key, e.Value?.DeepClone())), rows, v => (JsonNode?)JsonValue.Create(v));
    private static string? Str(List<KeyValuePair<string, JsonNode?>> s, string key) =>
        s.Where(e => e.Key == key).Select(e => e.Value!.GetValue<string>()).SingleOrDefault();

    internal static void Run(Action<bool, string> check, Action<Action, string> reject, string fixtureDir)
    {
        // ------------------------------------------------------------ codec
        var a = new SightStateEntry(357, "us_m70f", false);
        var b = new SightStateEntry(363, null, true);
        var c = new SightStateEntry(400, "de_tzf12a", null);
        var json = SightStateData.Encode(new[] { a, b, c });
        check(json == "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"us_m70f\",\"FollowGun\":false},{\"Sight\":363,\"Variant\":null,\"FollowGun\":true},{\"Sight\":400,\"Variant\":\"de_tzf12a\",\"FollowGun\":null}]}", "wire format " + json);
        check(SightStateData.Decode(json).SequenceEqual(new[] { a, b, c }), "codec roundtrip");
        check(SightStateData.Decode(SightStateData.Encode(Array.Empty<SightStateEntry>())).Length == 0, "empty envelope decodes");
        var unknown = new SightStateEntry(401, "removed_from_catalogue", true);
        check(SightStateData.Decode(SightStateData.Encode(new[] { unknown })).Single() == unknown, "unknown variant id kept by the codec");
        foreach (var bad in new[]{
            "{\"Version\":2,\"Sights\":[]}", "{\"Version\":0,\"Sights\":[]}", "{\"Sights\":[]}", "{\"Version\":1}",
            "{\"Version\":1,\"Sights\":null}", "{\"Version\":\"1\",\"Sights\":[]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"x\"}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Variant\":\"x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":-1,\"Variant\":\"x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":\"357\",\"Variant\":\"x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357.5,\"Variant\":\"x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":7,\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"x\",\"FollowGun\":1}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"x\",\"FollowGun\":\"true\"}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":null,\"FollowGun\":null}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\" x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"a\\nb\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"x\",\"FollowGun\":true,\"extra\":1}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Sight\":358,\"Variant\":\"x\",\"FollowGun\":true}]}",
            "{\"Version\":1,\"Sights\":[{\"Sight\":357,\"Variant\":\"a\",\"FollowGun\":true},{\"Sight\":357,\"Variant\":\"b\",\"FollowGun\":false}]}",
            "{\"Version\":1,\"Sights\":[],\"Profiles\":[]}", "{\"Version\":1,\"Sights\":[", "not json", "null", "[]", "" })
            reject(() => SightStateData.Decode(bad), "reject broken envelope " + bad);
        reject(() => SightStateData.Decode(null!), "reject null envelope");
        reject(() => SightStateData.Decode(new string(' ', SightStateData.MaxBytes + 1)), "bounded envelope bytes");
        reject(() => SightStateData.Encode(new[] { a, a with { Variant = "us_m71d" } }), "duplicate sight VUID refused on save");
        reject(() => SightStateData.Encode(new[] { new SightStateEntry(1, null, null) }), "row without a choice refused on save");
        reject(() => SightStateData.Encode(Enumerable.Range(0, 65).Select(i => new SightStateEntry(i, null, true))), "bounded rows");
        var max = Enumerable.Range(0, 64).Select(i => new SightStateEntry(i, "v" + i, i % 2 == 0)).ToArray();
        check(SightStateData.Decode(SightStateData.Encode(max)).SequenceEqual(max), "64 rows valid");
        check(SightStateData.Owned(SightStateData.Key) && !SightStateData.Owned(ConsoleLinkData.Key) && !SightStateData.Owned(ThermalProfileData.Key) &&
              !SightStateData.Owned(ConsoleLinkData.LegacyKey) && !SightStateData.Owned("sprocketGunSightVariant"), "Gun Sights owns only its own key");
        check(!ObjectStateRows.Owned(SightStateData.Key), "Thermal does not own sprocketGunSightsV1");

        // ------------------------------------------------------------ remap
        var map = new SightVuidMap(); map.Add(357, 357);
        check(SightStateData.Remap(new[] { a }, map.Values).Single() == a, "identity load keeps row");
        map = new(); map.Add(357, 1357); map.Add(363, 1363);
        check(SightStateData.Remap(new[] { a, b }, map.Values).SequenceEqual(new[] { a with { Sight = 1357 }, b with { Sight = 1363 } }), "duplicate remaps sight VUIDs");
        map = new(); map.Add(999, 1999);
        check(SightStateData.Remap(new[] { a }, map.Values).Length == 0, "unmapped sight never keeps raw VUID");
        map = new(); map.Add(357, 1357);
        check(SightStateData.Remap(new[] { a, a with { FollowGun = true } }, map.Values).Length == 0, "same sight in two envelopes dropped");
        map = new(); map.Add(357, 1000); map.Add(363, 1000);
        check(SightStateData.Remap(new[] { a, b }, map.Values).Length == 0, "colliding new VUID poisons both rows");
        map = new(); map.Add(357, 1357); map.Add(357, 2357);
        check(SightStateData.Remap(new[] { a }, map.Values).Length == 0, "old VUID mapped twice is poisoned");
        check(SightStateData.ForOwner(new[] { a }, new HashSet<int> { 381 }).Length == 0, "foreign owner row skipped");
        check(SightStateData.ForOwner(new[] { a, b }, new HashSet<int> { 357 }).Single() == a, "owner filter keeps own sight");
        for (int offset = 1; offset <= 20; offset++)
        {
            map = new(); map.Add(363, 363 + offset * 1000); map.Add(357, 357 + offset * 1000);
            var r = SightStateData.Remap(new[] { a, b }, map.Values);
            check(r.Length == 2 && r[0].Sight == 357 + offset * 1000 && r[1].Sight == 363 + offset * 1000 && r[0].FollowGun == false && r[1].FollowGun == true, "independent remap " + offset);
        }
        map = new(); map.Add(357, 357); map.Add(401, 401);
        var plan = SightStateData.Plan(new[] { a, unknown }, map.Values, id => SightCatalogue.Find(id) != null);
        check(plan.Length == 2 && plan[0] == new SightRestore(357, "us_m70f", false, null), "known variant restored");
        check(plan[1] == new SightRestore(401, null, true, "removed_from_catalogue"), "unknown variant not applied, follow flag still applied");
        check(SightCatalogue.Find("us_m70f") != null && SightCatalogue.Find("de_tzf12a") != null, "test variant ids exist in the catalogue");

        // ------------------------------------------------------------ decision rule
        check(!SightViewPolicy.Static(null, true, false), "default (true): follows gun, native");
        check(SightViewPolicy.Static(null, false, false), "default (false): stays static");
        check(!SightViewPolicy.Static(true, false, false), "explicit on wins over default false");
        check(SightViewPolicy.Static(false, true, false), "explicit off wins over default true");
        foreach (bool? saved in new bool?[] { null, true, false })
            foreach (var def in new[] { true, false })
                check(!SightViewPolicy.Static(saved, def, true), $"ATGM guidance always native (saved={saved}, default={def})");

        // ------------------------------------------------------------ coexistence with Thermal keys, both postfix orders
        var link = new ConsoleLinkEntry(363, 1246);
        var profile = new ThermalProfileEntry(363, "tiGen2");
        var state = new List<KeyValuePair<string, JsonNode?>> { new("thermalSight", 363), new("model", 364), new("other.mod", JsonValue.Create("keep")),
            new(SightStateData.Key, JsonValue.Create("stale")), new(ConsoleLinkData.Key, JsonValue.Create("stale")) };
        var ours = new[] { b with { Variant = "us_m70f" } };
        var thermalFirst = OurCapture(ThermalCapture(state, new[] { link }, new[] { profile }), ours);
        var oursFirst = ThermalCapture(OurCapture(state, ours), new[] { link }, new[] { profile });
        foreach (var (name, s) in new[] { ("Thermal then Gun Sights", thermalFirst), ("Gun Sights then Thermal", oursFirst) })
        {
            check(s.Select(e => e.Key).Distinct().Count() == s.Count, name + ": no duplicate keys");
            check(ConsoleLinkData.Decode(Str(s, ConsoleLinkData.Key)!).Single() == link, name + ": FCS link kept");
            check(ThermalProfileData.Decode(Str(s, ThermalProfileData.Key)!).Single() == profile, name + ": Thermal profile kept");
            check(SightStateData.Decode(Str(s, SightStateData.Key)!).SequenceEqual(ours), name + ": Gun Sights row kept");
            check(s.Single(e => e.Key == "thermalSight").Value!.GetValue<int>() == 363 && Str(s, "other.mod") == "keep", name + ": foreign keys kept");
        }
        // Clearing our choices removes only our key; Thermal clearing removes only theirs.
        var cleared = OurCapture(thermalFirst, Array.Empty<SightStateEntry>());
        check(Str(cleared, SightStateData.Key) == null && Str(cleared, ConsoleLinkData.Key) != null && Str(cleared, ThermalProfileData.Key) != null, "no Gun Sights choice: only our key removed");
        var thermalCleared = ThermalCapture(oursFirst, Array.Empty<ConsoleLinkEntry>(), Array.Empty<ThermalProfileEntry>());
        check(Str(thermalCleared, SightStateData.Key) != null && Str(thermalCleared, ConsoleLinkData.Key) == null, "Thermal unlink keeps our key");
        var thermalRead = ConsoleLinkData.Read(oursFirst.Select(e => e.Key), k => Str(oursFirst, k)!, k => 0);
        check(thermalRead.Canonical && thermalRead.Links.Single() == link, "Thermal reader ignores our key");

        // ------------------------------------------------------------ fixtures
        Fixture(check, Path.Combine(fixtureDir, "Bradley M3A3.blueprint"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Sprocket", "Factions", "Nederland", "Blueprints", "vehicles", "Bradley M3A3.blueprint"), injectThermal: true);
        Fixture(check, Path.Combine(fixtureDir, "Leopard 2a7.blueprint"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Sprocket", "Factions", "Nederland", "Blueprints", "vehicles", "Leopard 2a7.blueprint"), injectThermal: false);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // Every object that owns a sight component ("sight" or "thermalSight") gets a Gun Sights row; the Thermal sight
    // also gets Thermal envelopes when the file has none (Bradley: thermal part but no Thermal keys saved yet).
    private static void Fixture(Action<bool, string> check, string copy, string source, bool injectThermal)
    {
        var name = Path.GetFileNameWithoutExtension(copy);
        check(File.Exists(copy), name + ": fixture copy present in tmp-test");
        if (!File.Exists(copy)) return;
        var sourceHash = File.Exists(source) ? Hash(source) : null;
        var copyHash = Hash(copy);
        string original = File.ReadAllText(copy);
        var root = JsonNode.Parse(original)!.AsObject();
        var objects = root["objects"]!.AsArray();
        var touched = new Dictionary<int, (int Sight, SightStateEntry Row)>();
        bool hadThermalKeys = original.Contains(ConsoleLinkData.Key) || original.Contains(ThermalProfileData.Key);
        check(!original.Contains(SightStateData.Key), name + ": fixture has no Gun Sights envelope yet");
        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i]!.AsObject();
            string? comp = obj.ContainsKey("thermalSight") ? "thermalSight" : obj.ContainsKey("sight") ? "sight" : null;
            if (comp == null) continue;
            int sight = obj[comp]!.GetValue<int>();
            bool thermal = comp == "thermalSight";
            var row = new SightStateEntry(sight, thermal ? null : "us_m70f", thermal ? true : false);
            var before = obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value)).ToList();
            var links = obj.ContainsKey(ConsoleLinkData.Key) ? ConsoleLinkData.Decode(obj[ConsoleLinkData.Key]!.GetValue<string>()) : Array.Empty<ConsoleLinkEntry>();
            var profiles = obj.ContainsKey(ThermalProfileData.Key) ? ThermalProfileData.Decode(obj[ThermalProfileData.Key]!.GetValue<string>()) : Array.Empty<ThermalProfileEntry>();
            if (thermal && injectThermal && links.Length == 0 && profiles.Length == 0)
            { links = new[] { new ConsoleLinkEntry(sight, 1246) }; profiles = new[] { new ThermalProfileEntry(sight, "tiGen2") }; }
            // Alternate the postfix order per object; both orders are checked again on read-back.
            var captured = touched.Count % 2 == 0
                ? OurCapture(ThermalCapture(before, links, profiles), new[] { row })
                : ThermalCapture(OurCapture(before, new[] { row }), links, profiles);
            var replaced = new JsonObject(); foreach (var p in captured) replaced[p.Key] = p.Value;
            objects[i] = replaced;
            touched[i] = (sight, row);
            if (links.Length > 0) check(Str(captured, ConsoleLinkData.Key) != null, name + $": FCS link written for object {i}");
        }
        check(touched.Count >= (injectThermal ? 2 : 1), name + $": sight objects found ({touched.Count})");
        string written = Path.Combine(Path.GetDirectoryName(copy)!, name + ".gunsights-0.3.0.blueprint");
        File.WriteAllText(written, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var reread = JsonNode.Parse(File.ReadAllText(written))!.AsObject();
        var origRoot = JsonNode.Parse(original)!;
        var originalObjects = origRoot["objects"]!.AsArray();
        for (int i = 0; i < originalObjects.Count; i++)
        {
            var robj = reread["objects"]![i]!.AsObject();
            if (!touched.TryGetValue(i, out var t)) { check(JsonNode.DeepEquals(originalObjects[i], robj), name + $": untouched object {i} unchanged"); continue; }
            var rows = SightStateData.Decode(robj[SightStateData.Key]!.GetValue<string>());
            check(rows.Single() == t.Row, name + $": Gun Sights row reads back on object {i}");
            foreach (var p in originalObjects[i]!.AsObject().Where(p => !ObjectStateRows.Owned(p.Key)))
                check(JsonNode.DeepEquals(robj[p.Key], p.Value), name + $": key '{p.Key}' unchanged on object {i}");
            foreach (var key in new[] { ConsoleLinkData.Key, ThermalProfileData.Key })
                if (originalObjects[i]!.AsObject().ContainsKey(key))
                    check(robj[key]!.GetValue<string>() == originalObjects[i]![key]!.GetValue<string>(), name + $": real Thermal key {key} preserved on object {i}");
            if (robj.ContainsKey(ThermalProfileData.Key)) ThermalProfileData.Decode(robj[ThermalProfileData.Key]!.GetValue<string>());
            // Duplicate of that object: old sight VUID -> new, row follows (and the Thermal rows independently).
            var map = new SightVuidMap(); map.Add(robj["vuid"]!.GetValue<int>(), 9000 + i); map.Add(t.Sight, 9500 + i);
            var restored = SightStateData.Plan(SightStateData.ForOwner(rows, new HashSet<int> { t.Sight }), map.Values, id => SightCatalogue.Find(id) != null);
            check(restored.Single() == new SightRestore(9500 + i, t.Row.Variant, t.Row.FollowGun, null), name + $": duplicate remaps row on object {i}");
        }
        check(JsonNode.DeepEquals(origRoot["header"], reread["header"]), name + ": header unchanged");
        check(objects.Count == originalObjects.Count, name + ": object count unchanged");
        if (hadThermalKeys) foreach (var key in new[] { ConsoleLinkData.Key, ThermalProfileData.Key }) if (original.Contains(key)) check(File.ReadAllText(written).Contains(key), name + $": real Thermal key {key} still in written file");
        check(Hash(copy) == copyHash, name + ": fixture copy not modified");
        check(sourceHash == null || Hash(source) == sourceHash, name + ": source blueprint not modified");
    }
}
