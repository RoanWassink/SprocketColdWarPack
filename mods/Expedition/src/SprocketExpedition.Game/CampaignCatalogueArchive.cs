using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game;

internal sealed record ArchivedCampaignTank(string Id, string Name, string Hash, string Era, decimal Price, byte[] Bytes)
{
    public string? BlobHash { get; init; }
    [JsonIgnore] public string? SourcePath { get; init; }
    // Optional measured native mass. Absent on older archives and never inferred.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MassKg { get; init; }
    internal byte[] ReadBytes() => BlobHash == null ? Bytes : CampaignBlueprintBlob.Read(
        SourcePath ?? throw new InvalidDataException("Unbound blueprint blob."), BlobHash);
}
internal sealed record CampaignArchiveData(CatalogueDefinition Definition, decimal InitialBudget,
    string StarterId, IReadOnlyList<ArchivedCampaignTank> Tanks, string? AdoptedFromVersion = null, int? AdoptedAtRound = null);

// Local save content only. Never package or transmit this archive: it contains
// the actual enemy blueprint bytes, including designs from the user's faction.
internal static class CampaignCatalogueArchive
{
    private sealed record Envelope(int Schema, string Hash, string Payload);
    private const int MaxBytes = 96 * 1024 * 1024;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ArchivePath(string root, string version) => Path.Combine(root, "catalogues",
        Hash(Encoding.UTF8.GetBytes(version)) + ".json");

    internal static CampaignArchiveData? Read(string root, string version, string? libraryRoot = null)
    {
        var path = ArchivePath(root, version);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Saved catalogue archive is too large.");
        var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Saved catalogue archive is empty.");
        if (envelope.Schema is not (1 or 2) || envelope.Payload == null
            || Hash(Encoding.UTF8.GetBytes(envelope.Payload)) != envelope.Hash)
            throw new InvalidDataException("Saved catalogue archive failed its schema/hash check. Original files were preserved.");
        var data = JsonSerializer.Deserialize<CampaignArchiveData>(envelope.Payload)
            ?? throw new InvalidDataException("Saved catalogue payload is empty.");
        if (envelope.Schema == 2)
            data = data with { Tanks = data.Tanks.Select(t => t with { SourcePath = t.BlobHash == null ? null
                : CampaignBlueprintBlob.PathFor(libraryRoot ?? root, t.BlobHash) }).ToArray() };
        Validate(data);
        if (data.Definition.Version != version) throw new InvalidDataException("Saved catalogue version differs from the run.");
        return data;
    }

    internal static void Write(string root, CampaignArchiveData data, string? libraryRoot = null)
    {
        Validate(data);
        var existing = Read(root, data.Definition.Version, libraryRoot);
        if (existing != null)
        {
            if (!Equivalent(existing, data))
                throw new InvalidDataException("An immutable catalogue archive already exists with different content.");
            return;
        }
        // Schema 2 stores only a bounded manifest. A thousand large community
        // designs must never become a multi-GB JSON/base64 object in memory.
        data = data with { Tanks = data.Tanks.Select(t => t with
        {
            SourcePath = CampaignBlueprintBlob.Write(libraryRoot ?? root, t.Hash, t.ReadBytes()),
            BlobHash = t.Hash, Bytes = Array.Empty<byte>()
        }).ToArray() };
        var payload = JsonSerializer.Serialize(data);
        var path = ArchivePath(root, data.Definition.Version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(2, Hash(Encoding.UTF8.GetBytes(payload)), payload));
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Catalogue exceeds the local archive size limit.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path);
            _ = Read(root, data.Definition.Version, libraryRoot) ?? throw new InvalidDataException("Archive readback failed.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Recover only the exact version from a sibling archived slot, after
    // rechecking every persisted offer. No generation/adoption/substitution and
    // no checkpoint writes. A corrupt existing target remains an explicit error.
    internal static CampaignArchiveData? RecoverMissing(string root, RunSnapshot snapshot, string libraryRoot)
    {
        var present = Read(root, snapshot.CatalogueVersion, libraryRoot);
        if (present != null) return present;
        var archivedRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "archive");
        if (!Directory.Exists(archivedRoot)) return null;
        var fileName = Path.GetFileName(ArchivePath(root, snapshot.CatalogueVersion));
        var candidates = Directory.GetFiles(archivedRoot, fileName, SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            CampaignArchiveData? data;
            try
            {
                var sourceRoot = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
                data = Read(sourceRoot, snapshot.CatalogueVersion, libraryRoot);
                if (data == null || data.Definition.PriceUnitId != snapshot.CostUnitId
                    || !data.Definition.Eras.Select(e => e.Era).SequenceEqual(snapshot.EraSchedule)) continue;
                if (snapshot.Offers.Count > 0)
                {
                    var request = new OfferRequest(snapshot.RunId, snapshot.Seed, snapshot.Round, snapshot.BlockIndex,
                        snapshot.EraSchedule[snapshot.EraIndex], snapshot.OfferEconomy?.Budget ?? snapshot.AvailableBudget, snapshot.PendingSupplyPoints, snapshot.CatalogueVersion)
                    {
                        TestPool = snapshot.TestPool, TestPoolOffset = snapshot.TestPoolOffset,
                        RulesVersion = snapshot.Rewards?.Version ?? 1, OfferRoll = snapshot.Rewards?.OfferRoll ?? 0,
                        EnemyBudgetMultiplier = snapshot.OfferEconomy?.EnemyBudgetMultiplier
                            ?? (snapshot.Rewards?.Active.Any(i => i.Kind == RewardKind.ExperimentalContract) == true ? 1.2m : 1m),
                        EraProgressionEnabled = snapshot.OfferEconomy?.EraProgression ?? false,
                        GenerationPolicy = snapshot.GenerationPolicy
                            ?? (snapshot.Rewards?.Version >= 5 ? 5 : 0)
                    };
                    if (!new SeededEncounterCatalogue(data.Definition, new CatalogueOptions(ContentQualification.Candidate, false, true))
                        .RestoreOffers(request, snapshot.Offers).IsSuccess) continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or EncounterAvailabilityException or JsonException)
            { continue; }
            var target = ArchivePath(root, snapshot.CatalogueVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(path, temporary); File.Move(temporary, target); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Read(root, snapshot.CatalogueVersion, libraryRoot)
                ?? throw new InvalidDataException("Exact archived catalogue recovery failed readback.");
        }
        return null;
    }

    // Early builds did not archive content. Adopting today's catalogue is safe
    // only before any offers/attempt for this build phase have been committed.
    // Keep the run/checkpoint byte-for-byte; record the adopted source explicitly.
    internal static CampaignArchiveData AdoptLegacyBoundary(CampaignArchiveData current, RunSnapshot saved)
    {
        if (saved.Phase != RunPhase.Building || saved.Offers.Count != 0
            || saved.LockedPlan != null || saved.CurrentAttempt != null || saved.SelectedOfferId != null
            || saved.CostUnitId != current.Definition.PriceUnitId
            || !saved.EraSchedule.Select(e => e.NativeId).SequenceEqual(current.Definition.Eras.Select(e => e.Era.NativeId)))
            throw new InvalidDataException("The old catalogue archive is missing. Existing offers/combat cannot be replaced. Keep this save and restore its original content.");
        var adopted = current with
        {
            Definition = current.Definition with { Version = saved.CatalogueVersion },
            InitialBudget = saved.InitialBaseBudget.Value,
            AdoptedFromVersion = current.Definition.Version,
            AdoptedAtRound = saved.Round
        };
        Validate(adopted);
        return adopted;
    }

    private static void Validate(CampaignArchiveData data)
    {
        if (data.Definition == null || data.Tanks == null || data.Tanks.Count is < 1 or > 4096
            || data.InitialBudget <= 0m || data.Definition.IsFixture)
            throw new InvalidDataException("Invalid native catalogue archive metadata.");
        _ = new SeededEncounterCatalogue(data.Definition, new CatalogueOptions(ContentQualification.Candidate, false, true));
        var byId = data.Tanks.ToDictionary(t => t.Id, StringComparer.Ordinal);
        if (!byId.ContainsKey(data.StarterId)) throw new InvalidDataException("Archived starter is missing.");
        var enemies = data.Definition.Eras.SelectMany(e => e.Enemies)
            .Concat(data.Definition.TestPool?.Select(t => t.Design) ?? Array.Empty<EnemyDesignEntry>()).ToArray();
        if (enemies.Length != byId.Count) throw new InvalidDataException("Archived roster and catalogue differ.");
        foreach (var enemy in enemies)
        {
            if (!byId.TryGetValue(enemy.Blueprint.LocalId, out var tank) || !ValidBytes(tank)
                || tank.Hash != enemy.Blueprint.SnapshotHash || tank.Era != enemy.Blueprint.Era.NativeId
                || tank.Price != enemy.CheckedPrice.Value || tank.Price != enemy.ThreatScore)
                throw new InvalidDataException("An archived enemy failed its byte hash/era/price check.");
            if (enemy.MassKg is { } massKg && (massKg <= 0 || tank.MassKg != massKg))
                throw new InvalidDataException("An archived enemy failed its measured-mass check.");
        }
    }

    private static bool ValidBytes(ArchivedCampaignTank tank)
    {
        if (tank.BlobHash == null) return tank.Bytes != null && tank.Bytes.LongLength is >= 1 and <= CampaignBlueprintBlob.MaximumBytes
            && Hash(tank.Bytes) == tank.Hash;
        return tank.BlobHash == tank.Hash && tank.SourcePath != null && File.Exists(tank.SourcePath)
            && new FileInfo(tank.SourcePath).Length is >= 1 and <= CampaignBlueprintBlob.MaximumBytes;
    }
    private static bool Equivalent(CampaignArchiveData a, CampaignArchiveData b)
        => JsonSerializer.Serialize(a.Definition) == JsonSerializer.Serialize(b.Definition)
            && a.InitialBudget == b.InitialBudget && a.StarterId == b.StarterId
            && a.AdoptedFromVersion == b.AdoptedFromVersion && a.AdoptedAtRound == b.AdoptedAtRound
            && a.Tanks.Select(t => (t.Id,t.Name,t.Hash,t.Era,t.Price)).SequenceEqual(b.Tanks.Select(t => (t.Id,t.Name,t.Hash,t.Era,t.Price)));
}
