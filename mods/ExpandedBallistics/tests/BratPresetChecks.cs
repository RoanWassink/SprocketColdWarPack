using System.Text.Json.Nodes;
using SprocketExpandedBallistics.Materials;

internal static class BratPresetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var preset = EraGeneratedDefinitions.Presets.Single(p => p.Cassette == "bratStage1Cell1");
        check(preset.Mount == "bratHousing" && preset.CassetteMass > 0 && preset.MountMass > 0,
            "BRAT has the required housing and positive component masses");
        check(Math.Abs(preset.CassetteMass - 1.977230769) < 1e-6 &&
              Math.Abs(preset.MountMass - 3.0049172) < 1e-5,
            "BRAT masses follow approved active and passive collider volumes");
        var files = new[] { preset.Visual, preset.Collision, preset.Icon, preset.SpentVisual!, preset.MountCollision! }
            .Concat(preset.HousingDamageVisuals!).Distinct().ToArray();
        check(files.Length == 6 && files.All(f => File.Exists(Path.Combine(AppContext.BaseDirectory, "BratAssets", f))),
            "all six BRAT preset assets exist");
        check(preset.HousingActiveIds!.SequenceEqual(new[] { "bratStage1Cell1" }) &&
              preset.HousingDamageVisuals!.SequenceEqual(new[] { "brat-housing-state-0.obj", "brat-housing-state-1.obj" }),
            "BRAT uses the single-cell intact/spent housing lifecycle");
        check(preset.PartGuid == "89a52df5-1f81-50ad-845f-69ed756ce1ad" &&
              EraGeneratedDefinitions.Presets.Count(p => p.PartGuid == preset.PartGuid) == 1,
            "BRAT has a stable unique part GUID");
        var route = EraGeneratedDefinitions.MaterialRoutes["bratActive"];
        check(route.ResponseId == "bratActive" && route.Kind == "lightEra" && !route.NeutralKinetic,
            "BRAT routes to the light ERA response");
        var catalogue = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "brat-response-catalogue.json"));
        check(SprocketExpandedBallistics.ArmourResponses.Parse(catalogue).Responses.Any(r => r.ResponseId == "bratActive" && r.Kind == "lightEra"),
            "production catalogue parses the BRAT light ERA response");
        var responses = JsonNode.Parse(catalogue)!["responses"]!.AsArray();
        var brat = responses.Single(r => (string?)r!["responseId"] == "bratActive")!;
        check((string?)brat["historicalDate"] == "1995.01.01" && (string?)brat["minimumDate"] == "1995.01.01" && (string?)brat["status"] == "candidate",
            "BRAT has the requested date and candidate status");
        var basis = responses.Single(r => (string?)r!["responseId"] == "aratIActive")!.DeepClone();
        basis["responseId"] = "bratActive";
        basis["compatibleMaterialIds"] = new JsonArray("bratActive");
        basis["historicalDate"] = "1995.01.01";
        basis["minimumDate"] = "1995.01.01";
        check(JsonNode.DeepEquals(brat, basis), "BRAT response preserves all ARAT-I balance values");
        Console.WriteLine("BRAT preset PASS: 9 checks.");
    }
}
