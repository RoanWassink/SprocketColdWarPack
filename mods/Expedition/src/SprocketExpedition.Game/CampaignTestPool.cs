using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SprocketExpedition.Game;

// Pure filesystem/validation seam. Production supplies VehicleCard metadata;
// managed tests can exercise the same size, identity, rejection and report path.
internal static class CampaignTestPool
{
    internal sealed record Card(string Name, string Date, string Era, decimal Price, int? MassKg = null);
    internal sealed record Entry(string File, Card? Card, string? Hash, byte[]? Bytes, string? Reason)
    { internal bool Accepted => Reason == null; }

    internal static IEnumerable<Entry> Scan(string directory, Func<string, Card> readCard)
    {
        if (!Directory.Exists(directory)) yield break;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*.blueprint").OrderBy(p => p, StringComparer.Ordinal))
        {
            Entry entry;
            Card? card = null;
            try
            {
                var length = new FileInfo(path).Length;
                if (length is < 1 or > CampaignBlueprintBlob.MaximumBytes)
                    throw new InvalidDataException("Blueprint size must be 1..33554432 bytes.");
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                card = readCard(path);
                if (card.Price <= 0) throw new InvalidDataException("Native VehicleCard.Cost must be positive.");
                if (string.IsNullOrWhiteSpace(card.Name) || card.Name.Any(char.IsControl))
                    throw new InvalidDataException("Native vehicle card has no valid name.");
                if (string.IsNullOrWhiteSpace(card.Era) || string.IsNullOrWhiteSpace(card.Date))
                    throw new InvalidDataException("Native creation date has no campaign era/date.");
                // Do not apply the stationary-name filter to experimental designs.
                if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant())
                    throw new InvalidDataException("Blueprint changed while reading its native card.");
                if (!hashes.Add(hash)) throw new InvalidDataException("Duplicate blueprint content in test pool.");
                if (hashes.Count > 4096) throw new InvalidDataException("Test pool exceeds 4096 designs.");
                entry = new(Path.GetFileName(path), card, hash, bytes, null);
            }
            catch (Exception ex) { entry = new(Path.GetFileName(path), card, null, null, ex.Message); }
            yield return entry;
        }
    }

    internal static void Report(string path, IEnumerable<Entry> entries)
    {
        static string Csv(string? text) => "\"" + (text ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var rows = new StringBuilder("bestand,naam,datum,prijs,geaccepteerd,reden\n");
        foreach (var entry in entries)
            rows.AppendJoin(',', Csv(entry.File), Csv(entry.Card?.Name), Csv(entry.Card?.Date),
                entry.Card?.Price.ToString(CultureInfo.InvariantCulture) ?? "", entry.Accepted ? "1" : "0", Csv(entry.Reason)).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, rows.ToString(), new UTF8Encoding(false)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal const string ResultsHeaderV032 = "tijd,ronde,generatie,tank,uitslag,gespawnd";
    internal const string ResultsHeader = ResultsHeaderV032 + ",tank_gespawnd,contact,contact_gevecht";
    internal sealed record TankResult(string Tank, string Spawned, string Contact);

    // 0.3.2: one row per tank per finished test battle (append-only), so a
    // mixed battle of several test tanks still reports every design by name.
    // 0.3.4 appends tank_gespawnd/contact (ja/nee/onbekend) and contact_gevecht
    // (ja/nee). An old-header file gets only its header line replaced, atomically.
    // Never throws: the returned text is a warning for the caller to log.
    internal static string? AppendResults(string path, int round, long generation, IEnumerable<TankResult> tanks,
        string outcome, string spawned, bool battleContact)
    {
        static string Csv(string? text) => "\"" + (text ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var exists = File.Exists(path);
            if (exists && UpgradeResultsHeader(path) is { } problem) return problem;
            var rows = new StringBuilder(exists ? "" : ResultsHeader + "\n");
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            foreach (var tank in tanks)
                rows.AppendJoin(',', Csv(time), round.ToString(CultureInfo.InvariantCulture), generation.ToString(CultureInfo.InvariantCulture),
                    Csv(tank.Tank), Csv(outcome), Csv(spawned), Csv(tank.Spawned), Csv(tank.Contact), Csv(battleContact ? "ja" : "nee")).Append('\n');
            File.AppendAllText(path, rows.ToString(), new UTF8Encoding(false));
            return null;
        }
        catch (Exception ex) { return "Expedition test pool results not written: " + ex.Message; }
    }

    // Returns null when the file already has (or now has) the current header.
    // An unknown header is left untouched and reported.
    private static string? UpgradeResultsHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) { File.WriteAllText(path, ResultsHeader + "\n", new UTF8Encoding(false)); return null; }
        var end = Array.IndexOf(bytes, (byte)'\n');
        var first = Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end).TrimEnd('\r').TrimStart('﻿');
        if (first == ResultsHeader) return null;
        if (first != ResultsHeaderV032)
            return "Expedition test pool results: unknown header in " + Path.GetFileName(path) + "; file preserved, row not written.";
        var header = Encoding.UTF8.GetBytes(ResultsHeader + "\n");
        var rest = end < 0 ? Array.Empty<byte>() : bytes.AsSpan(end + 1).ToArray();
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(header); output.Write(rest); output.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return null;
    }

    // 0.3.4 per-tank evidence for test-pool-results.csv, keyed by native design
    // name (the name FindAny and the spawn check use). Contact = the tank got an
    // attack order from the team AI, lost crew health (a hit that reached the
    // crew) or was destroyed. No native per-hull hit event is used: a hit that
    // harms no crew is not seen. Inactive (no recording) outside the test pool.
    internal sealed class ContactLedger
    {
        private readonly Dictionary<int, string?> spawnedSlots = new();
        private readonly HashSet<string> claimed = new(StringComparer.Ordinal);
        private readonly HashSet<string> engaged = new(StringComparer.Ordinal);
        internal bool Active { get; private set; }

        internal void Begin(bool active)
        {
            Restart();
            Active = active;
        }

        // A new native battle of the same encounter (Retry): drop all per-slot
        // evidence so a name from the previous battle can never be reused.
        internal void Restart()
        {
            spawnedSlots.Clear(); claimed.Clear(); engaged.Clear();
        }

        // A native spawn slot of the enemy team; an unreadable name may be read later.
        internal void Spawned(int slot, string? name)
        {
            if (!Active) return;
            if (Known(name)) spawnedSlots[slot] = name;
            else if (!spawnedSlots.ContainsKey(slot)) spawnedSlots[slot] = null;
        }

        // The team AI owns this hull's orders, so a missing order means no target.
        internal void Claimed(string? name) { if (Active && Known(name)) claimed.Add(name!); }
        // Attack order, crew damage or destruction.
        internal void Contact(string? name) { if (Active && Known(name)) engaged.Add(name!); }

        internal TankResult For(string tank, bool victory)
        {
            var names = spawnedSlots.Values;
            var spawn = names.Contains(tank, StringComparer.Ordinal) ? "ja"
                : names.Count > 0 && names.All(n => n != null) ? "nee" : "onbekend";
            var contact = engaged.Contains(tank) || spawn == "ja" && victory ? "ja"
                : spawn == "nee" ? "nee"
                : spawn == "ja" && claimed.Contains(tank) ? "nee" : "onbekend";
            return new TankResult(tank, spawn, contact);
        }

        private static bool Known(string? name) => !string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    internal static readonly ContactLedger Ledger = new();
}