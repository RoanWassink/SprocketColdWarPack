using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game;

internal sealed record CampaignVeteran(string Hash, string Name, string Era, decimal Price,
    string CostUnit, byte[] Bytes, string RunId, int CompletedRound, AttemptIdentity Attempt);

// Local personal content only. Never package this directory or send its contents
// to a model. One immutable file per exact winning blueprint; no save-schema change.
internal static class CampaignVeteranStore
{
    // Legacy finite milestone: three victories per era. Adventure eligibility
    // derives its finite total from the run's own pinned era schedule.
    private const int BattlesPerEra = 3;
    private const string NativeCostUnit = "native.design.cost.v1";
    private const int MaximumFileBytes = 48 * 1024 * 1024;
    private sealed record Envelope(int Schema, string Hash, string Payload);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static bool Eligible(RunSnapshot snapshot)
    {
        var battlesPerEra = snapshot.Rewards == null ? 3 : 4;
        var finiteBattles = battlesPerEra * snapshot.EraSchedule.Count;
        if (snapshot.TestPool || snapshot.LastOutcome != EncounterOutcome.Victory || snapshot.Defeats != 0
            || snapshot.Phase is not (RunPhase.Building or RunPhase.AwaitingEndlessChoice or RunPhase.Finished)
            || snapshot.Victories < battlesPerEra || snapshot.Victories > finiteBattles
            || snapshot.Victories % battlesPerEra != 0
            || snapshot.Round != snapshot.Victories + snapshot.StartingEraIndex * battlesPerEra + 1
            || snapshot.CurrentAttempt != null || snapshot.LockedPlan != null
            || snapshot.ConfirmedDesign == null || snapshot.CostUnitId != NativeCostUnit) return false;
        var index = snapshot.StartingEraIndex + snapshot.Victories / battlesPerEra - 1;
        return index < snapshot.EraSchedule.Count
            && snapshot.ConfirmedDesign.Blueprint.Era == snapshot.EraSchedule[index]
            && snapshot.ProcessedAttempts.Any(a => a.RunId == snapshot.RunId && a.Generation == snapshot.GenerationCounter
                && snapshot.ConsumedOfferIds.Contains(a.EncounterId));
    }

    internal static CampaignVeteran Create(RunSnapshot snapshot, byte[] bytes, string name, string nativeEra)
    {
        if (!Eligible(snapshot)) throw new InvalidDataException("No persisted finite-era victory boundary exists.");
        var design = snapshot.ConfirmedDesign!;
        var hash = Hash(bytes);
        if (!hash.Equals(design.Blueprint.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            || nativeEra != design.Blueprint.Era.NativeId || design.Cost.UnitId != NativeCostUnit)
            throw new InvalidDataException("Winning blueprint hash, native era or quote differs from the saved design.");
        var attempt = snapshot.ProcessedAttempts.Single(a => a.Generation == snapshot.GenerationCounter);
        var entry = new CampaignVeteran(hash, name, nativeEra, design.Cost.Value, design.Cost.UnitId,
            bytes.ToArray(), snapshot.RunId, snapshot.Round - 1, attempt);
        Validate(entry);
        return entry;
    }

    internal static bool Write(string root, CampaignVeteran entry)
    {
        Validate(entry);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, entry.Hash + ".json");
        if (File.Exists(path))
        {
            var previous = Read(path);
            if (previous.Hash != entry.Hash || previous.Era != entry.Era || previous.Price != entry.Price)
                throw new InvalidDataException("An immutable veteran already exists with different metadata.");
            return false;
        }
        var payload = JsonSerializer.Serialize(entry);
        var encoded = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, Hash(Encoding.UTF8.GetBytes(payload)), payload));
        if (encoded.Length > MaximumFileBytes) throw new InvalidDataException("Veteran archive is too large.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(encoded); stream.Flush(true); }
            File.Move(temporary, path);
            _ = Read(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }

    internal static IReadOnlyList<CampaignVeteran> ReadAll(string root, Action<string>? warning = null)
    {
        var entries = new List<CampaignVeteran>();
        if (!Directory.Exists(root)) return entries;
        foreach (var file in Directory.EnumerateFiles(root, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            try { entries.Add(Read(file)); }
            catch (Exception ex) { warning?.Invoke(Path.GetFileName(file) + ": " + ex.Message); }
        }
        return entries;
    }

    private static CampaignVeteran Read(string path)
    {
        if (new FileInfo(path).Length > MaximumFileBytes) throw new InvalidDataException("Veteran archive is too large.");
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Empty veteran archive.");
        if (envelope.Schema != 1 || envelope.Payload == null
            || Hash(Encoding.UTF8.GetBytes(envelope.Payload)) != envelope.Hash)
            throw new InvalidDataException("Veteran archive integrity check failed.");
        var entry = JsonSerializer.Deserialize<CampaignVeteran>(envelope.Payload)
            ?? throw new InvalidDataException("Empty veteran payload.");
        Validate(entry);
        if (Path.GetFileName(path) != entry.Hash + ".json") throw new InvalidDataException("Veteran identity differs from its file.");
        return entry;
    }

    private static void Validate(CampaignVeteran entry)
    {
        if (entry.Bytes == null || entry.Bytes.Length is < 1 or > 32 * 1024 * 1024
            || Hash(entry.Bytes) != entry.Hash || string.IsNullOrWhiteSpace(entry.Name)
            || string.IsNullOrWhiteSpace(entry.Era) || entry.Price <= 0m || entry.CostUnit != NativeCostUnit
            || string.IsNullOrWhiteSpace(entry.RunId) || entry.CompletedRound < BattlesPerEra
            || entry.CompletedRound > 24 || (entry.CompletedRound % 3 != 0 && entry.CompletedRound % 4 != 0)
            || entry.Attempt == null || entry.Attempt.RunId != entry.RunId || entry.Attempt.Generation < 1)
            throw new InvalidDataException("Invalid winning-tank metadata or blueprint bytes.");
    }
}

