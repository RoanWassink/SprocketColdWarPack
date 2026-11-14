using SprocketExpedition.Game;

internal static class AmbusherChecks
{
    internal static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var directory = BlueprintDirectory();
        var cases = new (string Name, bool Casemate)[]
        {
            ("FV-90", true),
            ("Grille 17", true),
            ("Grille 21", true),
            ("Flakpanzer IV Wirbelwind", false),
            ("AMX-13-90", false)
        };
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (name, expected) in cases)
        {
            var file = Path.Combine(directory, name + ".blueprint");
            Check(File.Exists(file), "missing example blueprint: " + file);
            var content = File.ReadAllBytes(file);
            bytes[name] = content;
            var actual = CampaignCasemateDetection.IsCasemate(content);
            Check(actual == expected, $"casemate detection for '{name}' was {actual}, expected {expected}");
            var decision = CampaignCasemateDetection.Decide(name, content, null, null);
            Check(decision.IsAmbusher == expected && decision.Origin == CampaignCasemateDetection.Source.Detected,
                $"detected decision for '{name}' has the wrong role or source");
        }

        var none = Array.Empty<string>();
        Check(CampaignCasemateDetection.Decide("FV-90", bytes["FV-90"], none, none).IsAmbusher,
            "an unoverridden FV-90 is an ambusher");
        var forced = CampaignCasemateDetection.ParseNames(" Flakpanzer IV Wirbelwind ; AMX-13-90 ; ; ");
        Check(forced.Count == 2 && forced.Contains("Flakpanzer IV Wirbelwind") && forced.Contains("AMX-13-90"),
            "semicolon override names are trimmed and de-duplicated");
        var forcedDecision = CampaignCasemateDetection.Decide("Flakpanzer IV Wirbelwind",
            bytes["Flakpanzer IV Wirbelwind"], forced, none);
        Check(forcedDecision.IsAmbusher && forcedDecision.Origin == CampaignCasemateDetection.Source.Config,
            "AmbusherDesigns forces a non-casemate and reports config as its source");
        var denied = CampaignCasemateDetection.ParseNames("FV-90; Grille 21");
        var deniedDecision = CampaignCasemateDetection.Decide("FV-90", bytes["FV-90"], none, denied);
        Check(!deniedDecision.IsAmbusher && deniedDecision.Origin == CampaignCasemateDetection.Source.Config,
            "NotAmbusherDesigns excludes a detected casemate");
        Check(!CampaignCasemateDetection.Decide("FV-90", bytes["FV-90"], denied, denied).IsAmbusher,
            "the conservative exclude list wins when a name appears in both overrides");
        Check(CampaignCasemateDetection.Decide("Unreadable", null,
                CampaignCasemateDetection.ParseNames("Unreadable"), none).IsAmbusher,
            "a configured name does not need readable blueprint bytes");
        Check(!CampaignCasemateDetection.Decide("Unreadable", null, none, none).IsAmbusher,
            "unknown or unreadable designs are never guessed into the role");
        Check(!CampaignCasemateDetection.IsCasemate(System.Text.Encoding.UTF8.GetBytes("{not json")),
            "malformed blueprint bytes are not a casemate");

        // Non-casemates must keep the exact 0.3.2 profile formula, including
        // role selection, ranges, reaction variation and range independence.
        decimal[] costs = { 10m, 20m, 40m, 100m };
        foreach (var price in new decimal?[] { null, 0, 10, 45, 100 })
            for (var slot = 0; slot < 64; slot++)
                foreach (var friendly in new[] { false, true })
                {
                    var actual = CampaignCombatPolicy.Create("legacy-pin", slot, friendly, price, costs);
                    var legacy = LegacyProfile("legacy-pin", slot, friendly, price, costs);
                    Check(actual == legacy, "non-casemate profile changed for slot " + slot);
                }
        for (var slot = 0; slot < 64; slot++)
            foreach (var price in new decimal?[] { null, 10, 100 })
            {
                var profile = CampaignCombatPolicy.Create("ambusher-pin", slot, false, price, costs, ambusher: true);
                Check(profile.Role == CampaignCombatPolicy.Role.Ambusher && profile.ReactionSeconds == 0f,
                    "an ambusher always has role=Ambusher and the shared zero reaction");
            }
        var friendlyAmbusher = CampaignCombatPolicy.Create("ambusher-pin", 0, true, null, costs, ambusher: true);
        Check(friendlyAmbusher.Role != CampaignCombatPolicy.Role.Ambusher,
            "friendly AI can never receive the Ambusher role");

        var prioritized = CampaignAmbusherPlacement.OrderByHeight(
            new[] { 10f, 90f, 50f, 70f }, new[] { false, false, true, true });
        Check(prioritized.Length == 4, "height priority returns one source index per slot");
        var ambusherHeights = new[] { prioritized[2], prioritized[3] }.Select(index => new[] { 10f, 90f, 50f, 70f }[index]).ToArray();
        Check(ambusherHeights.OrderByDescending(value => value).SequenceEqual(new[] { 90f, 70f }),
            "ambusher slots receive the two highest validated markers");
        Check(CampaignAmbusherPlacement.OrderByHeight(new[] { 1f, 2f, 3f }, new[] { false, false, false })
            .SequenceEqual(new[] { 0, 1, 2 }), "placement without ambushers is byte-for-byte the old order");
        Check(CampaignAmbusherPlacement.OrderByHeight(new[] { 3f, 2f, 1f }, new[] { true, true, true })
            .SequenceEqual(new[] { 0, 1, 2 }), "an all-ambusher formation keeps its planned order");
        var first = CampaignAmbusherPlacement.OrderByHeight(new[] { 5f, 5f, 5f, 5f }, new[] { false, true, false, true });
        var second = CampaignAmbusherPlacement.OrderByHeight(new[] { 5f, 5f, 5f, 5f }, new[] { false, true, false, true });
        Check(first.SequenceEqual(second), "equal heights have a deterministic index tie-break");
        try
        {
            CampaignAmbusherPlacement.OrderByHeight(new[] { 1f }, Array.Empty<bool>());
            throw new Exception("mismatched placement inputs were accepted");
        }
        catch (ArgumentException) { }

        Check(CampaignAmbusherPlacement.HoldsPosition(true, false, 44.99f, 0f)
            && !CampaignAmbusherPlacement.HoldsPosition(true, false, 45f, 0f),
            "an ambusher may advance only at the 45 second boundary");
        Check(CampaignAmbusherPlacement.HoldsPosition(true, true, 500f, 0f),
            "a visible target keeps an ambusher in position regardless of elapsed time");
        Check(!CampaignAmbusherPlacement.HoldsPosition(false, false, 1f, 0f),
            "ordinary roles are not affected by the ambusher hold");
        Check(CampaignAmbusherPlacement.MayAdvanceAfterNoSight(45f, 0f)
            && !CampaignAmbusherPlacement.MayAdvanceAfterNoSight(44.9f, 0f)
            && !CampaignAmbusherPlacement.MayAdvanceAfterNoSight(float.NaN, 0f),
            "the no-sight gate rejects NaN and waits the full 45 s");
        Console.WriteLine("PASS casemate detection on 5 supplied blueprints, config overrides, fixed Ambusher profile, "
            + "height placement preference and the 45 s hold rule");
    }

    private static string BlueprintDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("SPROCKET_BLUEPRINT_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var root = Environment.GetEnvironmentVariable("SPROCKET_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = @"C:\Program Files (x86)\Steam\steamapps\common\Sprocket";
        var config = Path.Combine(root, "BepInEx", "config", "SprocketExpedition");
        // 0.3.6: the approved blueprints moved to test-pool-goedgekeurd on
        // 2026-11-02. Prefer test-pool when it still has every example, then the
        // approved folder, so the suite runs without an environment variable.
        var examples = new[]
        {
            "FV-90.blueprint", "Grille 17.blueprint", "Grille 21.blueprint",
            "Flakpanzer IV Wirbelwind.blueprint", "AMX-13-90.blueprint"
        };
        foreach (var folder in new[] { "test-pool", "test-pool-goedgekeurd" })
        {
            var directory = Path.Combine(config, folder);
            if (examples.All(name => File.Exists(Path.Combine(directory, name)))) return directory;
        }
        return Path.Combine(config, "test-pool-goedgekeurd");
    }

    // Exact copy of the pre-Ambusher 0.3.2 profile calculation, used as the
    // compatibility oracle for every non-casemate.
    private static CampaignCombatPolicy.Profile LegacyProfile(string encounter, int slot, bool friendly,
        decimal? price, IReadOnlyList<decimal> eraPrices, bool boss = false, int eraIndex = 0)
    {
        var rank = .5f;
        var prices = eraPrices.Where(p => p > 0).ToArray();
        if (price > 0 && prices.Length > 1)
        {
            var lower = prices.Count(p => p < price.Value);
            var equal = prices.Count(p => p == price.Value);
            rank = Math.Clamp((lower + Math.Max(0, equal - 1) * .5f) / (prices.Length - 1), 0, 1);
        }
        if (boss) rank = 1f;
        var hash = Hash(encounter, slot, friendly);
        var roll = hash % 100;
        var role = rank < .34f ? (roll < 60 ? CampaignCombatPolicy.Role.Assault : roll < 85 ? CampaignCombatPolicy.Role.Line : CampaignCombatPolicy.Role.FireSupport)
            : rank > .66f ? (roll < 60 ? CampaignCombatPolicy.Role.FireSupport : roll < 85 ? CampaignCombatPolicy.Role.Line : CampaignCombatPolicy.Role.Assault)
            : roll < 33 ? CampaignCombatPolicy.Role.Assault : roll < 75 ? CampaignCombatPolicy.Role.Line : CampaignCombatPolicy.Role.FireSupport;
        if (friendly) role = (slot % 3) switch { 0 => CampaignCombatPolicy.Role.Assault, 1 => CampaignCombatPolicy.Role.Line, _ => CampaignCombatPolicy.Role.FireSupport };
        var variation = ((hash >> 8) % 1001) / 1000f;
        var era = Math.Clamp(eraIndex, 0, 5);
        var range = role switch
        {
            CampaignCombatPolicy.Role.Assault => 100f + era * 70f + variation * (60f + era * 18f),
            CampaignCombatPolicy.Role.Line => 230f + era * 94f + variation * (70f + era * 26f),
            _ => 600f + era * 80f + variation * (100f + era * 20f)
        };
        return new CampaignCombatPolicy.Profile(role, range, range + 60f + era * 18f,
            0f); // 0.3.3: roles and ranges as before, reaction uniformly zero
    }

    private static uint Hash(string text, int slot, bool friendly)
    {
        var hash = 2166136261u;
        foreach (var c in text) hash = unchecked((hash ^ c) * 16777619u);
        hash = unchecked((hash ^ (uint)slot) * 16777619u);
        return unchecked((hash ^ (friendly ? 1u : 0u)) * 16777619u);
    }
}
