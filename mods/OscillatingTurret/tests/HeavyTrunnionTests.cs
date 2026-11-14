using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SprocketOscillatingTurret;

// Heavy Trunnion Mount 0.3.0 checks: mesh data vs STL, part JSONs, date, config floors, tag scope,
// MaxSpeed, pinion maths, version/author and the embedded icon.
internal static class HeavyTrunnionTests
{
    private static string TestsDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    private static string ProjectDir => Path.GetFullPath(Path.Combine(TestsDir(), ".."));
    private static string ModelDir => Path.Combine(ProjectDir, "model");
    private static string SrcDir => Path.Combine(ProjectDir, "src");

    private static string GameDir => Environment.GetEnvironmentVariable("SPROCKET_DIR") ??
        @"C:\Program Files (x86)\Steam\steamapps\common\Sprocket";

    internal static void Run(Action<bool, string> check, string? builtDll)
    {
        MeshData(check);
        PartJson(check);
        RingPart(check);
        Localization(check);
        Config(check);
        TagScope(check);
        MaxSpeed(check);
        Pinion(check);
        VersionAndAuthor(check);
        Icon(check, builtDll);
    }

    // ---------- mesh data in the DLL == normalized STLs ----------
    private static void MeshData(Action<bool, string> check)
    {
        var expected = new (string File, string Sha, int Tris)[]
        {
            ("static-cast", HeavyTrunnionMeshData.StaticCastSha256, HeavyTrunnionMeshData.StaticCastTriangles),
            ("static-steel", HeavyTrunnionMeshData.StaticSteelSha256, HeavyTrunnionMeshData.StaticSteelTriangles),
            ("static-bronze", HeavyTrunnionMeshData.StaticBronzeSha256, HeavyTrunnionMeshData.StaticBronzeTriangles),
            ("moving-cast", HeavyTrunnionMeshData.MovingCastSha256, HeavyTrunnionMeshData.MovingCastTriangles),
            ("moving-steel", HeavyTrunnionMeshData.MovingSteelSha256, HeavyTrunnionMeshData.MovingSteelTriangles),
            ("pinion-steel", HeavyTrunnionMeshData.PinionSteelSha256, HeavyTrunnionMeshData.PinionSteelTriangles),
        };
        var stl = new Dictionary<string, List<double[]>>();
        foreach (var (file, sha, tris) in expected)
        {
            string path = Path.Combine(ModelDir, "materials", file + ".stl");
            check(File.Exists(path), "STL exists: " + file);
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            check(actual == sha, $"embedded mesh data matches the current STL hash: {file} ({actual} vs {sha})");
            var triangles = ReadStl(path);
            stl[file] = triangles;
            check(triangles.Count == tris, $"{file}: {triangles.Count} STL triangles == embedded {tris}");
        }

        MeshMatches(check, "Static", HeavyTrunnionMeshData.StaticVertices, HeavyTrunnionMeshData.StaticNormals,
            HeavyTrunnionMeshData.StaticTriangles, HeavyTrunnionMeshData.StaticMaterials, stl);
        MeshMatches(check, "Moving", HeavyTrunnionMeshData.MovingVertices, HeavyTrunnionMeshData.MovingNormals,
            HeavyTrunnionMeshData.MovingTriangles, HeavyTrunnionMeshData.MovingMaterials, stl);
        MeshMatches(check, "Pinion", HeavyTrunnionMeshData.PinionVertices, HeavyTrunnionMeshData.PinionNormals,
            HeavyTrunnionMeshData.PinionTriangles, HeavyTrunnionMeshData.PinionMaterials, stl);

        // MODEL.md lists the hashes of the current export.
        string modelMd = File.ReadAllText(Path.Combine(ModelDir, "MODEL.md"));
        foreach (var name in new[] { "heavy-trunnion-static.stl", "heavy-trunnion-moving.stl", "heavy-trunnion-pinion.stl" })
        {
            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(ModelDir, name)))).ToLowerInvariant();
            check(modelMd.Contains(sha), "MODEL.md lists the current hash of " + name);
        }
    }

    // Every embedded triangle, read back in OpenSCAD space, is exactly one STL triangle (as a multiset).
    private static void MeshMatches(Action<bool, string> check, string name, float[] vertices, float[] normals,
        int[][] submeshes, string[] materials, Dictionary<string, List<double[]>> stl)
    {
        check(vertices.Length % 3 == 0 && normals.Length == vertices.Length, name + ": vertex and normal arrays line up");
        check(submeshes.Length == materials.Length, name + ": one submesh per material file");
        int count = vertices.Length / 3;
        for (int i = 0; i < count; i++)
        {
            float l = MathF.Sqrt(normals[3 * i] * normals[3 * i] + normals[3 * i + 1] * normals[3 * i + 1] + normals[3 * i + 2] * normals[3 * i + 2]);
            if (MathF.Abs(l - 1f) > 2e-3f) { check(false, $"{name}: normal {i} is unit length ({l})"); return; }
        }

        for (int s = 0; s < submeshes.Length; s++)
        {
            var idx = submeshes[s];
            check(idx.Length % 3 == 0 && idx.All(i => i >= 0 && i < count), $"{name}/{materials[s]}: indices in range");
            // STL side: the converter's own rule, mm -> m rounded to 6 decimals, as float; axes stay OpenSCAD.
            var want = stl[materials[s]].Select(t => Key(t.Select(Quantize).ToArray())).ToList();
            var got = new List<string>(idx.Length / 3);
            for (int t = 0; t < idx.Length; t += 3)
            {
                // Unity (x, y, z) -> OpenSCAD order (x, z, y); Unity winding a, c, b -> STL a, b, c.
                float[] P(int v) => new[] { vertices[3 * v], vertices[3 * v + 2], vertices[3 * v + 1] };
                got.Add(Key(P(idx[t]).Concat(P(idx[t + 2])).Concat(P(idx[t + 1])).ToArray()));
            }

            want.Sort(StringComparer.Ordinal);
            got.Sort(StringComparer.Ordinal);
            check(want.SequenceEqual(got), $"{name}/{materials[s]}: {got.Count} embedded triangles equal the STL triangles (positions and winding)");
        }
    }

    private static float Quantize(double mm) =>
        float.Parse((mm * 0.001).ToString("F6", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture) + 0f;

    // Rotation-invariant key of a triangle (exact floats; start at the smallest vertex).
    private static string Key(float[] t)
    {
        var p = new[] { (t[0] + 0f, t[1] + 0f, t[2] + 0f), (t[3] + 0f, t[4] + 0f, t[5] + 0f), (t[6] + 0f, t[7] + 0f, t[8] + 0f) };
        int start = 0;
        for (int i = 1; i < 3; i++) if (p[i].CompareTo(p[start]) < 0) start = i;
        var sb = new StringBuilder();
        for (int i = 0; i < 3; i++)
        {
            var v = p[(start + i) % 3];
            sb.Append(FormattableString.Invariant($"{v.Item1:R},{v.Item2:R},{v.Item3:R};"));
        }

        return sb.ToString();
    }

    private static List<double[]> ReadStl(string path)
    {
        var result = new List<double[]>();
        var current = new List<double>(9);
        foreach (string line in File.ReadLines(path))
        {
            var t = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 4 && t[0] == "vertex")
            {
                current.AddRange(t.Skip(1).Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)));
                if (current.Count == 9) { result.Add(current.ToArray()); current.Clear(); }
            }
        }

        return result;
    }

    // ---------- part JSONs, GUIDs, date ----------
    private static void PartJson(Action<bool, string> check)
    {
        string mantletPath = Path.Combine(SrcDir, "parts", "heavyTrunnionMantletPart.json");
        string drivePath = Path.Combine(SrcDir, "parts", "heavyTrunnionDrivePart.json");
        using var mantlet = JsonDocument.Parse(File.ReadAllText(mantletPath)); // strict: no trailing commas
        using var drive = JsonDocument.Parse(File.ReadAllText(drivePath));
        var m = mantlet.RootElement;
        var d = drive.RootElement;

        check(m.GetProperty("guid").GetString() == HeavyTrunnionPolicy.MantletGuid, "mantlet GUID");
        check(d.GetProperty("guid").GetString() == HeavyTrunnionPolicy.DriveGuid, "drive GUID");
        check(HeavyTrunnionPolicy.MantletGuid != HeavyTrunnionPolicy.DriveGuid, "the two GUIDs differ");
        check(m.GetProperty("name").GetString() == HeavyTrunnionPolicy.MantletName, "mantlet name");
        check(Tags(m).SequenceEqual(new[] { "mantlet" }), "mantlet tags are exactly [\"mantlet\"] (no azimuthMantlet)");
        check(Tags(d).SequenceEqual(new[] { HeavyTrunnionPolicy.DriveTag }), "drive tags are exactly [\"heavyTrunnionDrive\"]");

        var info = m.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "mantlet").GetProperty("info");
        check(info.GetProperty("trunnionPartGuid").GetString() == HeavyTrunnionPolicy.NativeTrunnionsGuid, "mantlet uses the native trunnions");
        check(info.GetProperty("layingDrivePartGuid").GetString() == HeavyTrunnionPolicy.DriveGuid, "mantlet uses the heavy drive");
        check(info.GetProperty("trunnionsScale").GetSingle() == HeavyTrunnionPolicy.ModelTrunnionsScale, "trunnionsScale 1.5");
        check(info.GetProperty("maxAzimuth").GetSingle() == 0f && info.GetProperty("minAzimuth").GetSingle() == 0f, "azimuth 0");
        check(info.GetProperty("maxElevation").GetSingle() == 25f && info.GetProperty("minElevation").GetSingle() == -10f,
            "elevation limits +25/-10 like the native Panther and circular mantlets");
        check(d.GetProperty("components").EnumerateArray().Any(c => c.GetProperty("type").GetString() == "layingDrive"), "drive has a layingDrive component");

        // Date: exactly 1952.01.01, a date and never an era name.
        foreach (var (root, what) in new[] { (m, "mantlet"), (d, "drive") })
        {
            string date = root.GetProperty("date").GetString() ?? "";
            check(Regex.IsMatch(date, @"^\d{4}\.\d{2}\.\d{2}$"), what + " date is a plain date: " + date);
            check(date == HeavyTrunnionPolicy.AvailableFrom.ToString("yyyy.MM.dd"), what + " date is 1952.01.01");
        }

        // GUIDs unique against every part file in the game.
        string parts = Path.Combine(GameDir, "Sprocket_Data", "StreamingAssets", "Parts");
        check(Directory.Exists(parts), "game Parts folder readable: " + parts);
        var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heavyTrunnionMantletPart.json", "heavyTrunnionDrivePart.json" };
        int files = 0;
        var guids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lenient = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        foreach (string file in Directory.GetFiles(parts, "*.json"))
        {
            if (ours.Contains(Path.GetFileName(file))) continue;
            files++;
            string text = File.ReadAllText(file);
            check(!text.Contains(HeavyTrunnionPolicy.MantletGuid, StringComparison.OrdinalIgnoreCase) &&
                  !text.Contains(HeavyTrunnionPolicy.DriveGuid, StringComparison.OrdinalIgnoreCase),
                "GUIDs not used anywhere in " + Path.GetFileName(file));
            try
            {
                using var doc = JsonDocument.Parse(text, lenient);
                if (doc.RootElement.TryGetProperty("guid", out var g) && g.GetString() is { } s) guids[s] = file;
            }
            catch (JsonException) { }
        }

        check(files > 300, $"scanned {files} game part files");
        check(!guids.ContainsKey(HeavyTrunnionPolicy.MantletGuid) && !guids.ContainsKey(HeavyTrunnionPolicy.DriveGuid), "GUIDs unique in the game Parts folder");
    }

    // ---------- the oscillating ring part (data\Parts, delivered next to the DLL) ----------
    private static void RingPart(Action<bool, string> check)
    {
        string dataDir = Path.Combine(ProjectDir, "data");
        using var ring = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "Parts", "oscillatingElevationDrivePart.json")));
        var r = ring.RootElement;
        string pivot = File.ReadAllText(Path.Combine(SrcDir, "PivotHousingVisuals.cs"));
        string plugin = File.ReadAllText(Path.Combine(SrcDir, "Plugin.cs"));
        string guid = r.GetProperty("guid").GetString() ?? "";
        check(guid.Length > 0 && pivot.Contains("PartGuid=\"" + guid + "\""), "ring part GUID is the one the pivot housing hooks look for");
        check(r.GetProperty("name").GetString() == "oscillatingElevationDrive", "ring part name oscillatingElevationDrive");
        check(Tags(r).SequenceEqual(new[] { "oscillatingElevationDrive" }) && plugin.Contains("ElevationTag = \"oscillatingElevationDrive\";"),
            "ring part tag is the ElevationTag the plugin and Stabilization look for");
        check(r.GetProperty("date").GetString() == OscillatingDatePolicy.MinimumDate.ToString("yyyy.MM.dd"), "ring part date equals OscillatingDatePolicy.MinimumDate (1952.01.01)");
        byte[] bytes = File.ReadAllBytes(Path.Combine(dataDir, "Localization", "en-UK", "Parts", "oscillatingElevationDrive.xml"));
        check(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "ring localization is UTF-16 LE with BOM");
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        check(xml.SelectSingleNode("/Part/name")?.InnerText == "Oscillating elevation drive", "ring localization name");
    }

    private static string[] Tags(JsonElement root) => root.GetProperty("tags").EnumerateArray().Select(t => t.GetString() ?? "").ToArray();

    private static void Localization(Action<bool, string> check)
    {
        string path = Path.Combine(SrcDir, "localization", HeavyTrunnionPolicy.MantletName + ".xml");
        byte[] bytes = File.ReadAllBytes(path);
        check(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "localization is UTF-16 LE with BOM like native blankMantlet.xml");
        string text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(text);
        check(xml.DocumentElement?.Name == "Part", "root element <Part>");
        check(xml.SelectSingleNode("/Part/name")?.InnerText == "Heavy Trunnion Mount", "name Heavy Trunnion Mount");
        check((xml.SelectSingleNode("/Part/description")?.InnerText.Length ?? 0) > 20, "has a description");
    }

    // ---------- config floors ----------
    private static void Config(Action<bool, string> check)
    {
        var d = HeavyTrunnionPolicy.Defaults;
        check(d == new HeavyTrunnionPolicy.Settings(2f, 250f, 2f, 1.5f), "defaults 2.0 / 250 kg / 2.0 / 1.5");
        var low = HeavyTrunnionPolicy.Sanitize(2f, -100f, 0.5f, 1.5f);
        check(low.ExtraMassKg == 0f, "ExtraMassKg floor 0 (never lighter than native)");
        check(low.CostFactor == 1f, "CostFactor floor 1.0 (never cheaper than native)");
        var high = HeavyTrunnionPolicy.Sanitize(40f, 50000f, 30f, 9f);
        check(high == new HeavyTrunnionPolicy.Settings(40f, 50000f, 30f, 9f), "no upper limits");
        var bad = HeavyTrunnionPolicy.Sanitize(float.NaN, float.PositiveInfinity, float.NaN, -1f);
        check(bad == d, "non-finite or non-positive values fall back to the defaults");
        check(HeavyTrunnionPolicy.Mass(low) >= 0f && HeavyTrunnionPolicy.Cost(low) >= 0f, "floors give mass and cost >= native (0)");
        check(MathF.Abs(HeavyTrunnionPolicy.Cost(d) - 250f * 0.8099862f * 2f) < 0.01f, "default cost 250 kg x native 0.81/kg x 2");
        check(HeavyTrunnionPolicy.MaxDistance(d) == 1.5f, "MaxDistanceFromTrunnions 1 m x 1.5");
    }

    // ---------- tag scope: native parts notice nothing ----------
    private static void TagScope(Action<bool, string> check)
    {
        static Func<string, bool> Has(params string[] tags) => t => tags.Contains(t);
        check(!HeavyTrunnionPolicy.IsHeavyDrive(Has()), "native laying drive (no tags) is out of scope");
        check(!HeavyTrunnionPolicy.IsHeavyDrive(Has("oscillatingElevationDrive")), "oscillating ring is out of scope");
        check(!HeavyTrunnionPolicy.IsHeavyDrive(Has("mantlet", "azimuthMantlet")), "native mantlet tags are out of scope");
        check(HeavyTrunnionPolicy.IsHeavyDrive(Has("heavyTrunnionDrive")), "heavy drive is in scope");
        check(!HeavyTrunnionPolicy.IsHeavyMantlet("0c396ede-f39b-46ac-9d7e-e37c22b2c89e"), "mantlet with the native drive is out of scope");
        check(!HeavyTrunnionPolicy.IsHeavyMantlet(null) && !HeavyTrunnionPolicy.IsHeavyMantlet(""), "mantlet without a drive is out of scope");
        check(HeavyTrunnionPolicy.IsHeavyMantlet(HeavyTrunnionPolicy.DriveGuid.ToUpperInvariant()), "heavy mantlet is in scope");

        // Source: every gameplay hook starts with its scope guard; the model hooks only touch models it hid.
        string src = File.ReadAllText(Path.Combine(SrcDir, "HeavyTrunnion.cs"));
        foreach (var (hook, guard) in new[]
                 {
                     ("EnableBehaviourPrefix", "if (!IsHeavyDrive(__instance)) return;"),
                     ("EnableBehaviourPostfix", "if (!IsHeavyDrive(__instance)) return;"),
                     ("DriveBuildPostfix", "if (!IsHeavyDrive(__instance)) return;"),
                     ("ValidateDistancePostfix", "if (!IsHeavyDrive(__instance)) return;"),
                     ("MantletBuildPostfix", "if (!IsHeavyMantlet(__instance)) return;"),
                     ("KeepHiddenPrefix", "HiddenModels.Contains(__instance.Pointer)"),
                     ("ModelBuildPostfix", "HiddenModels.Contains(__instance.Pointer)"),
                     ("CreateCardPostfix", "HeavyTrunnionPolicy.MantletGuid"),
                 })
        {
            string body = Body(src, hook);
            check(body.Length > 0, "hook found: " + hook);
            string first = FirstStatement(body);
            check(first.Contains(guard), $"{hook} starts with its scope guard ({guard})");
        }

        check(Regex.Matches(src, @"ApplyMassCost\(").Count == 3, "ApplyMassCost is defined once and called only from the two guarded build hooks");
        check(!src.Contains("PluginInfo.ElevationTag") && !src.Contains("TurretRing") && !src.Contains("TurretBehaviour"),
            "Heavy Trunnion code never touches the oscillating ring path");
    }

    private static string Body(string src, string method)
    {
        var match = Regex.Match(src, @"private static \w+ " + method + @"\([^)]*\)\s*\{");
        if (!match.Success) return "";
        int depth = 0, start = match.Index + match.Length - 1;
        for (int i = start; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start + 1, i - start - 1);
        }

        return "";
    }

    private static string FirstStatement(string body)
    {
        var lines = body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "{" && l != "try" && !l.StartsWith("//"));
        string code = string.Join(" ", lines);
        int end = code.IndexOf(';');
        return end < 0 ? code : code.Substring(0, end + 1);
    }

    // ---------- stronger, not faster ----------
    private static void MaxSpeed(Action<bool, string> check)
    {
        var method = typeof(HeavyTrunnionPolicy).GetMethod("MaxSpeed", BindingFlags.Static | BindingFlags.NonPublic)!;
        check(method.GetParameters().Length == 1 && method.GetParameters()[0].Name == "ratio", "MaxSpeed depends on the ratio only");
        foreach (float ratio in new[] { 1f, 50f, 179.200256f, 305.7f })
        {
            foreach (float factor in new[] { 1f, 2f, 4f, 40f })
            {
                var s = HeavyTrunnionPolicy.Sanitize(factor, 250f, 2f, 1.5f);
                float native = HeavyTrunnionPolicy.MotorTorque(ratio, 1f);
                float heavy = HeavyTrunnionPolicy.MotorTorque(ratio, HeavyTrunnionPolicy.ScaledTorqueMultiplier(1f, s));
                check(MathF.Abs(heavy - native * factor) <= 1e-3f * heavy, $"torque x{factor} at ratio {ratio}");
                check(HeavyTrunnionPolicy.MaxSpeed(ratio) == 180f / ratio, $"MaxSpeed stays 180/ratio at ratio {ratio}, factor {factor}");
            }
        }

        // King Tiger blueprint: ratio 179.200256, torqueMultiplier 1.0.
        check(MathF.Abs(HeavyTrunnionPolicy.MotorTorque(179.200256f, 1f) - 22400.03f) < 0.1f, "native torque formula Ratio x 125 x multiplier");
        string src = string.Join("\n", File.ReadAllLines(Path.Combine(SrcDir, "HeavyTrunnion.cs"))
            .Where(l => !l.TrimStart().StartsWith("//")));
        check(!Regex.IsMatch(src, @"MaxSpeed\s*=[^=]") && !Regex.IsMatch(src, @"elevationInfo\s*=[^=]") && !Regex.IsMatch(src, @"\.Ratio\s*=[^=]"),
            "hook source never writes MaxSpeed, elevationInfo or Ratio");
        check(Regex.Matches(src, @"axis\.TorqueMultiplier\s*=[^=]").Count == 2, "TorqueMultiplier is only scaled in the prefix and restored in the postfix");
    }

    // ---------- pinion follows the sector ----------
    private static void Pinion(Action<bool, string> check)
    {
        foreach (float e in new[] { -10f, 0f, 7.5f, 20f })
        {
            var (x, w) = HeavyTrunnionPolicy.AboutX(e);
            check(MathF.Abs(HeavyTrunnionPolicy.AngleAboutX(x, w) - e) < 1e-3f, $"angle about X round-trips at {e}");
            check(MathF.Abs(HeavyTrunnionPolicy.PinionAngle(e) + 3.75f * e) < 1e-4f, $"pinion turns -3.75 x {e}");
            // Rolling contact: arc length on the sector pitch circle == arc on the pinion pitch circle.
            check(MathF.Abs(135f * e + 36f * HeavyTrunnionPolicy.PinionAngle(e)) < 1e-2f, $"pitch circles roll without slip at {e}");
        }

        check(!HeavyTrunnionPolicy.AngleChanged(5f, 5.005f), "no pinion write below 0.01 degree");
        check(HeavyTrunnionPolicy.AngleChanged(5f, 5.02f) && HeavyTrunnionPolicy.AngleChanged(float.NaN, 0f), "pinion write on change or first frame");
        string visuals = File.ReadAllText(Path.Combine(SrcDir, "HeavyTrunnionVisuals.cs"));
        string frame = Body(visuals.Replace("internal static void Frame()", "private static void Frame()"), "Frame");
        check(frame.Contains("if (!HeavyTrunnionPolicy.AngleChanged(v.LastAngle, angle)) continue;"), "Frame skips unchanged angles");
        check(!Regex.IsMatch(frame, @"\bnew\s+(?!NQuaternion|Quaternion)[A-Z]|\.ToArray\(|\.Select\(|\+\s*\""|\$\"""), "Frame allocates nothing (no arrays, LINQ or strings)");
    }

    // ---------- version, author ----------
    private static void VersionAndAuthor(Action<bool, string> check)
    {
        string plugin = File.ReadAllText(Path.Combine(SrcDir, "Plugin.cs"));
        string csproj = File.ReadAllText(Path.Combine(SrcDir, "SprocketOscillatingTurret.csproj"));
        string changelog = File.ReadAllText(Path.Combine(ProjectDir, "CHANGELOG.md"));
        check(plugin.Contains("public const string Version = \"0.3.1\";"), "plugin version 0.3.1");
        check(plugin.Contains("public const string Guid = \"sprocket.oscillatingturret\";"), "plugin GUID is sprocket.oscillatingturret (Expanded Ballistics looks for exactly this id)");
        check(File.ReadAllText(Path.Combine(SrcDir, "HeavyTrunnion.cs")).Contains("internal const string Owner = \"sprocket.oscillatingturret.heavy\";"), "Harmony id of the Heavy Trunnion patches is sprocket.oscillatingturret.heavy");
        check(File.ReadAllText(Path.Combine(SrcDir, "PivotHousingVisuals.cs")).Contains("internal const string Owner=\"sprocket.oscillatingturret.visuals\";"), "Harmony id of the pivot housing patches is sprocket.oscillatingturret.visuals");
        check(csproj.Contains("<Version>0.3.1</Version>") && csproj.Contains("<Authors>Nero</Authors>"), "csproj version 0.3.1, author Nero");
        check(csproj.Contains("<AssemblyName>SprocketOscillatingTurret</AssemblyName>") && csproj.Contains("<RootNamespace>SprocketOscillatingTurret</RootNamespace>"),
            "csproj assembly and root namespace are SprocketOscillatingTurret");
        check(csproj.Contains("<Company>Nero</Company>") && csproj.Contains("<Copyright>Nero</Copyright>") && csproj.Contains("<Product>Sprocket Oscillating Turret</Product>"),
            "csproj company and copyright Nero, product Sprocket Oscillating Turret");
        check(csproj.Contains("<DebugType>none</DebugType>") && csproj.Contains("<Deterministic>true</Deterministic>") && csproj.Contains("<PathMap>"),
            "csproj builds without pdb, deterministic and with a path map (no absolute paths in the DLL)");
        check(changelog.Contains("## 0.3.1"), "CHANGELOG has 0.3.1");
        // Personal names and old mod names in any file are caught by the name-check script of the release tooling, not by a test here (its pattern list cannot sit in a source file).
    }

    // ---------- the picker icon is embedded in the built DLL ----------
    private static void Icon(Action<bool, string> check, string? builtDll)
    {
        string png = Path.Combine(ModelDir, "heavy-trunnion-icon.png");
        if (builtDll == null || !File.Exists(builtDll))
        {
            check(false, "built DLL given for the icon check: " + builtDll);
            return;
        }

        var assembly = Assembly.Load(File.ReadAllBytes(builtDll));
        using var stream = assembly.GetManifestResourceStream("heavy-trunnion-icon.png");
        check(stream != null, "icon embedded as heavy-trunnion-icon.png");
        using var copy = new MemoryStream();
        stream!.CopyTo(copy);
        check(copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(png)), "embedded icon == model/heavy-trunnion-icon.png");
        check(assembly.GetName().Version?.ToString(3) == "0.3.1", "assembly version 0.3.1");
        check(assembly.GetName().Name == "SprocketOscillatingTurret", "assembly name SprocketOscillatingTurret");
    }
}
