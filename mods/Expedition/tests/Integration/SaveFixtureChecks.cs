using System.Text.Json;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Core.Integration.Tests;

// 0.3.4: two real 0.3.3 test-pool saves must load, validate and continue in
// place. The fixtures live outside the repository (personal designs); they are
// copied into the test folder first and never written in place.
internal static class SaveFixtureChecks
{
    internal static async Task Run(string root)
    {
        var fixtures = Locate();
        if (fixtures == null)
        {
            Console.WriteLine("SKIP 0.3.3 save fixtures: no fixtures/slot-1 directory above the test binary");
            return;
        }

        var slot1 = Copy(Path.Combine(fixtures, "slot-1"), Path.Combine(root, "slot-1"));
        AssertDefinitionRoundTrip(slot1);
        var archive1 = HashFile(Directory.GetFiles(Path.Combine(slot1, "catalogues"), "*.json").Single());
        var (c1, p1) = Resume(slot1);
        await c1.ResumeAsync();
        Assert.Equal(archive1, HashFile(Directory.GetFiles(Path.Combine(slot1, "catalogues"), "*.json").Single()),
            "slot 1 catalogue archive stays byte-identical");
        Assert.False(p1.Has<ErrorView>() || p1.Has<BlockedView>(), "slot 1 resumes: " + p1.Describe());
        var s1 = c1.Snapshot!;
        Assert.True(s1.RunId == "119abf6ac7024b449c32072e9748d389" && s1.Round == 24 && s1.Victories == 3
            && s1.TestPoolOffset == 12 && s1.Phase == RunPhase.ChoosingEncounter, "slot 1 continues in its own round and cursor");
        var names = Names(slot1, s1.Offers.Single());
        Assert.Equal("HGS 105-13-1|HGS 105-2-1 Dunkleosteus|HGS 105-3-1|HGS 105-4-1", names, "slot 1 next battle");
        var pinned = JsonSerializer.Serialize(s1.Offers);
        var (again, pAgain) = Resume(slot1);
        await again.ResumeAsync();
        Assert.False(pAgain.Has<ErrorView>() || pAgain.Has<BlockedView>(), "slot 1 pinned offers stay valid: " + pAgain.Describe());
        Assert.Equal(pinned, JsonSerializer.Serialize(again.Snapshot!.Offers), "slot 1 pinned offers restore exactly");

        var slot2 = Copy(Path.Combine(fixtures, "slot-2"), Path.Combine(root, "slot-2"));
        AssertDefinitionRoundTrip(slot2);
        var archive2 = HashFile(Directory.GetFiles(Path.Combine(slot2, "catalogues"), "*.json").Single());
        var saved = new RunStore(slot2).LoadActiveRun(ModAvailability.None).Snapshot!;
        var (c2, p2) = Resume(slot2);
        await c2.ResumeAsync();
        Assert.Equal(archive2, HashFile(Directory.GetFiles(Path.Combine(slot2, "catalogues"), "*.json").Single()),
            "slot 2 catalogue archive stays byte-identical");
        Assert.False(p2.Has<ErrorView>() || p2.Has<BlockedView>(), "slot 2 resumes: " + p2.Describe());
        var s2 = c2.Snapshot!;
        Assert.True(s2.RunId == "53f6482c05b3434194d9e672161d257e" && s2.TestPoolOffset == 0 && s2.Defeats == 2,
            "slot 2 keeps cursor 0 (no migration)");
        Assert.Equal(JsonSerializer.Serialize(saved.Offers), JsonSerializer.Serialize(s2.Offers), "slot 2 keeps its pinned offers");
        Assert.Equal(saved.SelectedOfferId, s2.SelectedOfferId, "slot 2 keeps its selection");
        var first = Names(slot2, s2.Offers[0]);
        var adapter = new FakeAdapter(s2.CostUnitId) { Outcome = EncounterOutcome.FriendlyForceDestroyed };
        var (c3, p3) = Resume(slot2, adapter);
        await c3.ResumeAsync();
        await c3.BeginEncounterAsync();
        var lost = c3.Snapshot!;
        Assert.False(p3.Has<ErrorView>() || p3.Has<BlockedView>(), "slot 2 loss continues: " + p3.Describe());
        Assert.True(lost.TestPoolOffset == 4 && lost.Defeats == 3 && lost.Round == 21 && lost.Offers.Count == 3, "slot 2 loss advances from 0 to 4");
        var next = Names(slot2, lost.Offers[0]);
        Assert.False(next.Split('|').Intersect(first.Split('|')).Any(), "slot 2 next choice shows four other tanks");
        Console.WriteLine($"PASS 0.3.3 save fixtures: slot 1 round 24 offset 12 -> {names}; slot 2 offset 0 ({first}) -> loss -> offset 4 ({next})");
    }

    private static (ExpeditionController, RecordingPresenter) Resume(string slot, FakeAdapter? adapter = null)
    {
        var definition = Catalogue(slot);
        var presenter = new RecordingPresenter();
        return (new ExpeditionController(
            new SeededEncounterCatalogue(definition, new CatalogueOptions(ContentQualification.Candidate, false, true)),
            new RunStore(slot), adapter ?? new FakeAdapter(definition.PriceUnitId), presenter,
            () => ModAvailability.None, () => Guid.NewGuid().ToString("N"), () => 1), presenter);
    }

    // The pinned archive payload holds the catalogue definition; its blob library
    // is not part of the fixture, so only the definition is read here.
    private static CatalogueDefinition Catalogue(string slot)
    {
        var file = Directory.GetFiles(Path.Combine(slot, "catalogues"), "*.json").Single();
        using var envelope = JsonDocument.Parse(File.ReadAllBytes(file));
        using var payload = JsonDocument.Parse(envelope.RootElement.GetProperty("Payload").GetString()!);
        return JsonSerializer.Deserialize<CatalogueDefinition>(payload.RootElement.GetProperty("Definition").GetRawText())!;
    }

    private static string Names(string slot, EncounterPlan plan)
    {
        var pool = Catalogue(slot).TestPool!;
        return string.Join("|", plan.Enemies.Select(e => pool.Single(t => t.Design.Blueprint.LocalId == e.BlueprintId).Name));
    }

    // 0.3.7: an archive written before MinimumRisk existed must keep its exact
    // catalogue bytes after read and write. The fixture envelope itself stays
    // untouched; only the embedded definition is parsed and serialized again.
    private static void AssertDefinitionRoundTrip(string slot)
    {
        var file = Directory.GetFiles(Path.Combine(slot, "catalogues"), "*.json").Single();
        using var envelope = JsonDocument.Parse(File.ReadAllBytes(file));
        using var payload = JsonDocument.Parse(envelope.RootElement.GetProperty("Payload").GetString()!);
        var raw = payload.RootElement.GetProperty("Definition").GetRawText();
        Assert.False(raw.Contains("MinimumRisk", StringComparison.Ordinal),
            Path.GetFileName(slot) + " archive predates MinimumRisk");
        var parsed = JsonSerializer.Deserialize<CatalogueDefinition>(raw)!;
        Assert.Equal(raw, JsonSerializer.Serialize(parsed),
            Path.GetFileName(slot) + " archive definition stays byte-identical");
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static string? Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures");
            if (File.Exists(Path.Combine(candidate, "slot-1", "active-run.json"))) return candidate;
        }
        return null;
    }

    private static string Copy(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return target;
    }
}
