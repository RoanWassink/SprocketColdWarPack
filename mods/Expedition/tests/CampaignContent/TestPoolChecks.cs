using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Game;

internal static class TestPoolChecks
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    internal static void Run(string root, CampaignArchiveData normal)
    {
        var directory = Path.Combine(root, "test-blueprints"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "valid.blueprint"), "fixture");
        File.WriteAllText(Path.Combine(directory, "empty.blueprint"), "");
        File.WriteAllText(Path.Combine(directory, "bad-card.blueprint"), "bad");
        File.WriteAllText(Path.Combine(directory, "bad-name.blueprint"), "no name");
        File.WriteAllText(Path.Combine(directory, "bad-date.blueprint"), "no era");
        File.WriteAllText(Path.Combine(directory, "zero.blueprint"), "zero cost");
        File.WriteAllText(Path.Combine(directory, "duplicate.blueprint"), "fixture");
        File.WriteAllText(Path.Combine(directory, "huge.blueprint"), "");
        using (var huge = File.OpenWrite(Path.Combine(directory, "huge.blueprint"))) huge.SetLength(CampaignBlueprintBlob.MaximumBytes + 1L);
        var entries = CampaignTestPool.Scan(directory, path =>
        {
            var file = Path.GetFileName(path);
            if (file == "bad-card.blueprint") throw new InvalidDataException("native card failed");
            return new CampaignTestPool.Card(file == "bad-name.blueprint" ? "" : "AT Gun",
                "2049.12.31", file == "bad-date.blueprint" ? "" : "future.era", file == "zero.blueprint" ? 0 : 100m);
        }).ToArray();
        Check(entries.Count(e => e.Accepted) == 1 && entries.Count(e => !e.Accepted) == 7, "size/card/name/date/cost/duplicate rejection");
        Check(entries.All(e => e.Accepted || !string.IsNullOrEmpty(e.Reason)), "every rejection has reason");
        Check(entries.Single(e => e.Accepted).Card!.Name == "AT Gun", "stationary-name filter bypassed");
        var report = Path.Combine(root, "test-pool-report.csv"); CampaignTestPool.Report(report, entries);
        Check(File.ReadAllLines(report).Length == 9, "all accepted and rejected files reported");
        var accepted = entries.Single(e => e.Accepted);
        var tank = new ArchivedCampaignTank("test." + accepted.Hash![..32], "AT Gun", accepted.Hash!, "future.era", 100m, accepted.Bytes!);
        var design = new EnemyDesignEntry(new BlueprintReference(tank.Id, tank.Hash, new EraReference(tank.Era), Array.Empty<ModRequirement>()),
            EnemyRole.AntiTank, tank.Price, new ScenarioValue(tank.Price, normal.Definition.PriceUnitId), ContentQualification.Candidate);
        var pinned = normal with { Definition = normal.Definition with { Version = "native.testpool.fixture",
            TestPool = new[] { new TestPoolDesign(tank.Name, design) } }, Tanks = normal.Tanks.Append(tank).ToArray() };
        var archiveRoot = Path.Combine(root, "pinned-test-pool");
        CampaignCatalogueArchive.Write(archiveRoot, pinned);
        File.Delete(Path.Combine(directory, accepted.File));
        var restored = CampaignCatalogueArchive.Read(archiveRoot, pinned.Definition.Version)!;
        Check(restored.Definition.TestPool!.Single().Design.Blueprint.Era.NativeId == "future.era", "future native era retained outside normal schedule");
        Check(restored.Tanks.Single(t => t.Id == tank.Id).ReadBytes().SequenceEqual(accepted.Bytes!), "pinned test bytes survive deleted source");
        Console.WriteLine("PASS test pool: 8 scanner cases, complete report, stationary name allowed, future-era archive and deleted-source restore");
        Results(Path.Combine(root, "results"));
        Ledger();
    }

    // 0.3.4 test-pool-results.csv: new columns at the end, header-only upgrade.
    static void Results(string directory)
    {
        Directory.CreateDirectory(directory);
        var rows = new[] { new CampaignTestPool.TankResult("Tank, A", "ja", "ja"), new CampaignTestPool.TankResult("Tank B", "ja", "nee") };
        var fresh = Path.Combine(directory, "fresh.csv");
        Check(CampaignTestPool.AppendResults(fresh, 24, 3, rows, "Victory", "4/4", true) == null, "fresh write succeeds");
        var lines = File.ReadAllLines(fresh);
        Check(lines[0] == "tijd,ronde,generatie,tank,uitslag,gespawnd,tank_gespawnd,contact,contact_gevecht", "new header keeps old columns first");
        Check(lines.Length == 3 && lines[1].EndsWith(",24,3,\"Tank, A\",\"Victory\",\"4/4\",\"ja\",\"ja\",\"ja\"", StringComparison.Ordinal)
            && lines[2].EndsWith(",\"Tank B\",\"Victory\",\"4/4\",\"ja\",\"nee\",\"ja\"", StringComparison.Ordinal), "per-tank cells appended in order");

        var old = Path.Combine(directory, "old.csv");
        const string oldRows = "\"2026-11-01 18:00:00\",21,1,\"Old, tank\",\"FriendlyForceDestroyed\",\"4/4\"\r\n\"2026-11-01 18:05:00\",21,2,\"Other\",\"Retreat\",\"3/4\"\n";
        File.WriteAllText(old, CampaignTestPool.ResultsHeaderV032 + "\n" + oldRows, new UTF8Encoding(false));
        Check(CampaignTestPool.AppendResults(old, 21, 3, rows.Take(1), "FriendlyForceDestroyed", "4/4", true) == null, "old file upgrade succeeds");
        var upgraded = File.ReadAllText(old);
        Check(upgraded.StartsWith(CampaignTestPool.ResultsHeader + "\n" + oldRows, StringComparison.Ordinal), "only the header line replaced; old rows byte-identical");
        Check(upgraded.Split('\n').Length == 5 && !Directory.GetFiles(directory, "*.tmp-*").Any(), "one row appended, no temporary file left");
        Check(CampaignTestPool.AppendResults(old, 21, 4, rows.Take(1), "Victory", "4/4", true) == null
            && File.ReadAllText(old).StartsWith(CampaignTestPool.ResultsHeader + "\n" + oldRows, StringComparison.Ordinal), "current header is not upgraded twice");

        var unknown = Path.Combine(directory, "unknown.csv");
        File.WriteAllText(unknown, "a,b\n1,2\n");
        Check(CampaignTestPool.AppendResults(unknown, 1, 1, rows, "Victory", "4/4", true) is { } warning && warning.Contains("unknown header")
            && File.ReadAllText(unknown) == "a,b\n1,2\n", "unknown header preserved with a warning");
        Check(CampaignTestPool.AppendResults(directory, 1, 1, rows, "Victory", "4/4", true) is { }, "unwritable path returns a warning instead of throwing");
        Console.WriteLine("PASS test pool results CSV: 3 new trailing columns, atomic header-only upgrade, unknown header and IO failure warn");
    }

    static void Ledger()
    {
        var ledger = new CampaignTestPool.ContactLedger();
        ledger.Begin(false); ledger.Spawned(0, "A"); ledger.Contact("A");
        Check(ledger.For("A", false) == new CampaignTestPool.TankResult("A", "onbekend", "onbekend"), "inactive ledger records nothing");
        ledger.Begin(true);
        ledger.Spawned(0, "A"); ledger.Spawned(1, "B"); ledger.Claimed("A"); ledger.Claimed("B"); ledger.Contact("A");
        Check(ledger.For("A", false) == new CampaignTestPool.TankResult("A", "ja", "ja"), "attack order, crew damage or death is contact");
        Check(ledger.For("B", false) == new CampaignTestPool.TankResult("B", "ja", "nee"), "AI-owned tank without order has no contact");
        Check(ledger.For("C", false) == new CampaignTestPool.TankResult("C", "nee", "nee"), "tank never spawned has no contact");
        Check(ledger.For("B", true).Contact == "ja", "victory destroyed every spawned enemy");
        ledger.Spawned(2, "D"); ledger.Spawned(3, null);
        Check(ledger.For("D", false) == new CampaignTestPool.TankResult("D", "ja", "onbekend"), "tank without team-AI ownership is unknown");
        Check(ledger.For("C", false).Spawned == "onbekend", "an unreadable spawn name makes a missing tank unknown");
        ledger.Spawned(3, "C");
        Check(ledger.For("C", false).Spawned == "ja", "a later readable name resolves the slot");
        ledger.Begin(true);
        Check(ledger.For("A", false).Spawned == "onbekend", "new battle starts empty");

        // 0.3.6 (0.3.4 fix): a Retry reuses spawn indices for different tanks.
        // Neither the old name nor an unknown may survive that restart.
        ledger.Spawned(0, "Old A"); ledger.Spawned(1, "Old B");
        ledger.Restart();
        ledger.Spawned(0, "New A"); ledger.Spawned(1, "New B");
        Check(ledger.For("New A", false).Spawned == "ja" && ledger.For("New B", false).Spawned == "ja",
            "retry records the new tanks at the same spawn slots");
        Check(ledger.For("Old A", false).Spawned == "nee", "a previous battle's name cannot leak into the retry");
        Console.WriteLine("PASS test pool contact ledger: spawned/contact ja-nee-onbekend per tank, clean retry slots");
    }

    internal static void SuppliedBlueprints(string directory, string reportPath)
    {
        // Only parses saved v2 header metadata. Does not call native VehicleCard,
        // classify eras, load parts, spawn, or prove combat playability.
        var entries = CampaignTestPool.Scan(directory, path =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var h = doc.RootElement.GetProperty("header");
            var date = h.GetProperty("creationDate").GetString()!;
            if (!DateTime.TryParseExact(date, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidDataException("Invalid saved creationDate.");
            return new CampaignTestPool.Card(h.GetProperty("name").GetString()!, date,
                "managed-header-only", h.GetProperty("cost").GetDecimal(), h.GetProperty("mass").GetInt32());
        }).ToArray();
        CampaignTestPool.Report(reportPath, entries);
        Check(entries.Length > 0 && entries.All(e => e.Accepted), "supplied blueprints must pass managed header checks; see report");
        Console.WriteLine($"PASS {entries.Length}/{entries.Length} supplied blueprints: managed header/size/hash/name/date/cost only; native validation NOT run. Report: {reportPath}");
    }
}
