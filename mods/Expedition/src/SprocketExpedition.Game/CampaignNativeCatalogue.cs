using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Sprocket.TechTrees;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game;

// Local candidate content, built from native cards. Saved scalar design prices
// are not Custom Battle's resource vector. Nothing changes a source blueprint,
// native era definition or an enemy's original technology date.
internal sealed class CampaignNativeCatalogue
{
    internal const string CostUnit = "native.design.cost.v1";
    internal const string VersionPrefix = "native.cost.1.";
    // One era of the game's native list (VehicleClassifications.Eras).
    internal sealed record NativeEra(string Name, int StartKey, bool Playable);
    private static NativeEra[]? nativeEras;
    // Every native era in start-date order, read from the game (never a fixed
    // list). Includes non-playable eras: tanks are classified by this list and
    // EraSchedulePolicy later keeps only playable eras that have a usable tank.
    // Cached for the session; Reset() reads it again. A list read before the game
    // has loaded its eras (plugin start) is empty or lacks playable eras: it is
    // returned but never cached, so the next read sees the real list.
    internal static IReadOnlyList<NativeEra> NativeEraList
    {
        get
        {
            if (nativeEras != null) return nativeEras;
            var read = ReadNativeEras();
            if (read.Any(era => era.Playable)) nativeEras = read;
            return read;
        }
    }
    internal static IReadOnlyList<string> EraNames => NativeEraList.Select(era => era.Name).ToArray();
    internal static int StartKey(TechDate date) => date.Year * 10000 + date.Month * 100 + date.Day;
    // Position in the native era list; -1 for an unknown name.
    internal static int EraIndex(string? era)
    {
        var list = NativeEraList;
        for (var index = 0; index < list.Count; index++)
            if (string.Equals(list[index].Name, era, StringComparison.Ordinal)) return index;
        return -1;
    }
    private static NativeEra[] ReadNativeEras()
    {
        var result = new List<NativeEra>();
        // A lookup first lets the game initialise its classification data; the raw
        // field alone can still be empty at that point.
        try { _ = VehicleClassifications.GetEraByName("WWI"); } catch { }
        var native = VehicleClassifications.eras
            ?? throw new InvalidOperationException("The game has not loaded its era list yet.");
        for (var index = 0; index < native.Length; index++)
        {
            var era = native[index];
            if (era == null || string.IsNullOrWhiteSpace(era.Name)) continue;
            // Every name must resolve through the game's own lookup.
            var check = VehicleClassifications.GetEraByName(era.Name);
            if (check == null || check.Name != era.Name)
            {
                Plugin.Logger.LogWarning($"Expedition native era '{era.Name}' does not resolve by name; skipped.");
                continue;
            }
            if (result.Any(known => known.Name == era.Name)) continue;
            result.Add(new NativeEra(era.Name, StartKey(era.StartDate), era.Playable));
        }
        // Order on the start date; ties keep the native list order.
        return result.Select((era, position) => (era, position))
            .OrderBy(pair => pair.era.StartKey).ThenBy(pair => pair.position)
            .Select(pair => pair.era).ToArray();
    }
    private static CampaignNativeCatalogue? cached;
    // Metadata only, retained between runs in this process. Never retains a
    // native VehicleCard/TechDate wrapper or substitutes saved-slot content.
    private static readonly CampaignCardMetadataCache<Tank> communityCards = new();
    internal sealed record Tank(string Id, string Name, string Hash, string Era, decimal Price, byte[] Content,
        string? SourcePath = null, int? MassKg = null, bool DangerousOnly = false)
    {
        internal byte[] Bytes => SourcePath == null ? Content : CampaignBlueprintBlob.Read(SourcePath, Hash);
    }
    internal static string LibraryRoot => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "content-library");
    internal static string TestPoolDirectory => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "test-pool");
    internal static string TestPoolReportPath => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "stats", "test-pool-report.csv");
    internal static string TestPoolResultsPath => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "stats", "test-pool-results.csv");
    internal static string CommunityDirectory => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "community-blueprints");
    // Same content-addressed markers and blobs as CommunityDirectory, but every
    // accepted design gets MinimumRisk = Dangerous in the catalogue.
    internal static string DangerousCommunityDirectory => Path.Combine(Paths.ConfigPath, "SprocketExpedition", "community-blueprints-dangerous");
    private static readonly HashSet<string> BundledStationaryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "NTL AT Gun.blueprint", "Silent Border AT Gun.blueprint", "TaigaATGun.blueprint",
        "SandboxTarget150m.blueprint", "SandboxTarget300m.blueprint"
    };
    internal static bool IsBundledStationary(string path) =>
        BundledStationaryNames.Contains(Path.GetFileName(path));
    internal static bool TryValidateCommunityBlueprint(string path, out string hash)
    {
        hash = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        try
        {
            _ = CampaignBlueprintBlob.PathFor(LibraryRoot, hash);
            var file = new FileInfo(path);
            if (file.Length is < 1 or > CampaignBlueprintBlob.MaximumBytes) return false;
            _ = CampaignBlueprintBlob.Read(path, hash);
            return true;
        }
        catch
        {
            return false;
        }
    }
    private readonly Dictionary<string, Tank> tanks;
    internal CatalogueDefinition Definition { get; }
    internal IReadOnlyList<EraReference> Eras { get; }
    internal decimal InitialBudget { get; private set; }
    private bool starterMeasured;
    internal byte[] Starter { get; private set; }
    private string starterId;
    private readonly Dictionary<string, string> starterChoices = new(StringComparer.Ordinal);
    private int startingEraIndex;
    internal decimal StartingBudget => RunDirector.FiniteEraBudget(InitialBudget, startingEraIndex);
    internal static void Reset()
    {
        cached = null;
        testPoolPrepared = false; testPoolTanks.Clear();
        nativeEras = null;
        CampaignEraAvailability.Invalidate();
    }

    private CampaignNativeCatalogue(Dictionary<string, Tank> designs, decimal initialBudget, Tank starter,
        Dictionary<string, string> choices, IReadOnlyList<string> activeEras, IReadOnlyList<TestPoolDesign>? testPool = null)
    {
        var caps = DesignCapPolicy.Parse(designCaps?.Value ?? DesignCapPolicy.DefaultConfig,
            message => Plugin.Logger.LogWarning("Expedition design caps: " + message));
        int? Cap(Tank tank) => testPool == null && caps.TryGetValue(tank.Name, out var n) ? n : null;
        var regular = designs.Values.Where(t => !(testPool?.Any(p => p.Design.Blueprint.LocalId == t.Id) ?? false)).ToArray();
        Plugin.Logger.LogInfo("Expedition design caps: " + string.Join("; ", caps.Select(c =>
            $"'{c.Key}'={c.Value} ({regular.Count(t => t.Name == c.Key)})")));
        tanks = designs;
        starterChoices = choices;
        InitialBudget = initialBudget;
        Starter = starter.Bytes.ToArray();
        starterId = starter.Id;
        var maps = CampaignMapPool.CandidateNames.Select(name => CampaignMapPool.Read(MapDirectory, name)).ToArray();
        Eras = activeEras.Select(name => new EraReference(name)).ToArray();
        var identity = "schedule=" + string.Join(",", activeEras) + "\n";
        identity += string.Join("\n", designs.Values.OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => $"{t.Id}|{t.Era}|{t.Hash}|{t.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                + $"|{(int)CampaignFormationLabels.Role(t.Name, t.MassKg, t.Era)}|{CampaignFormationLabels.Family(t.Name, t.MassKg, t.Era)}"
                + $"|mass={t.MassKg?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}"
                + (t.DangerousOnly ? "|minimumRisk=Dangerous" : "")
                + (Cap(t) is { } cap ? "|maxPerBattle=" + cap.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")));
        foreach (var era in activeEras)
        {
            var native = VehicleClassifications.GetEraByName(era);
            identity += $"\n{native.Name}|{native.StartDate}|{native.Playable}";
        }
        identity += "\n" + string.Join("\n", maps.Select(m => m.Id + "|" + m.Hash));
        identity += "\nformation-doctrine/v1";
        if (testPool != null) identity += "\ntest-pool/v1\n" + string.Join("\n", testPool.Select(t => t.Name + "|" + t.Design.Blueprint.LocalId));
        var bossBlueprints = CampaignBossRoster.Resolve(designs.Values.Where(t =>
                !t.DangerousOnly
                && !(testPool?.Any(p => p.Design.Blueprint.LocalId == t.Id) ?? false)), activeEras, out var bossCandidates,
            message => Plugin.Logger.LogWarning("Expedition boss config: " + message),
            message => Plugin.Logger.LogInfo(message));
        if (bossBlueprints != null) identity += "\n" + string.Join("\n", bossBlueprints.OrderBy(x => x.Key).Select(x => x.Key + "|" + x.Value));
        if (bossCandidates != null) identity += "\nboss-candidates/v1\n"
            + string.Join("\n", bossCandidates.OrderBy(x => x.Key).Select(x => x.Key + "|" + string.Join("|", x.Value)));
        var version = VersionPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..16];
        Definition = new CatalogueDefinition(version, "Native era / saved-price candidate content", false, CostUnit,
            Eras.Select(era => new CatalogueEraContent(era,
                maps.Select(map => new MapEntry(era, map.Id, map.Hash,
                    new[] { "native.default" }, Array.Empty<ModRequirement>(), ContentQualification.Candidate)).ToArray(),
                designs.Values.Where(t => t.Era == era.NativeId && !(testPool?.Any(p => p.Design.Blueprint.LocalId == t.Id) ?? false)).OrderBy(t => t.Id, StringComparer.Ordinal)
                    .Select(t => new EnemyDesignEntry(new BlueprintReference(t.Id, t.Hash, era,
                        Array.Empty<ModRequirement>()), CampaignFormationLabels.Role(t.Name, t.MassKg, t.Era), t.Price,
                        new ScenarioValue(t.Price, CostUnit), ContentQualification.Candidate)
                        {
                            FormationFamily = CampaignFormationLabels.Family(t.Name, t.MassKg, t.Era),
                            MassKg = t.MassKg,
                            MinimumRisk = t.DangerousOnly ? EncounterRisk.Dangerous : null,
                            MaxPerBattle = Cap(t)
                        }).ToArray())).ToArray())
            { BossBlueprints = bossBlueprints, BossCandidates = bossCandidates, TestPool = testPool };
    }

    internal static string MapDirectory => Path.Combine(Paths.GameRootPath, "Sprocket_Data", "StreamingAssets", "Scenarios", "Configs");

    private CampaignNativeCatalogue(CampaignArchiveData archive)
    {
        Definition = archive.Definition;
        Eras = Definition.Eras.Select(e => e.Era).ToArray();
        InitialBudget = archive.InitialBudget;
        starterId = archive.StarterId;
        var dangerous = archive.Definition.Eras.SelectMany(era => era.Enemies)
            .Where(enemy => DangerousDesignPolicy.IsDangerousOnly(enemy))
            .Select(enemy => enemy.Blueprint.LocalId)
            .ToHashSet(StringComparer.Ordinal);
        tanks = archive.Tanks.ToDictionary(t => t.Id,
            t => new Tank(t.Id, t.Name, t.Hash, t.Era, t.Price, t.Bytes, t.SourcePath, t.MassKg,
                dangerous.Contains(t.Id)), StringComparer.Ordinal);
        Starter = tanks[starterId].Bytes.ToArray();
    }

    private CampaignArchiveData ArchiveData() => new(Definition, InitialBudget, starterId,
        tanks.Values.OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => new ArchivedCampaignTank(t.Id, t.Name, t.Hash, t.Era, t.Price, t.Content)
                { BlobHash = t.SourcePath == null ? null : t.Hash, SourcePath = t.SourcePath, MassKg = t.MassKg }).ToArray());

    // Slot selection can archive the same slot after initialisation. Re-pin
    // after that step, immediately before the first checkpoint is created.
    internal void PinTo(string root) => CampaignCatalogueArchive.Write(root, ArchiveData(), LibraryRoot);

    internal static CampaignNativeCatalogue ForSlot(RunStore store, RunLoadResult saved, bool newGame)
    {
        if (!newGame && saved.Snapshot is { } snapshot)
        {
            var prepared = CampaignResumePreparation.Read(store, ModAvailability.None);
            if (prepared.Error != null) throw new InvalidDataException(prepared.Error);
            var archive = CampaignCatalogueArchive.Read(store.RootDirectory, snapshot.CatalogueVersion, LibraryRoot);
            if (archive == null)
            {
                archive = CampaignCatalogueArchive.RecoverMissing(store.RootDirectory, snapshot, LibraryRoot);
                if (archive != null) Plugin.Logger.LogInfo("Expedition restored the exact missing catalogue from an archived slot; checkpoint and offers unchanged.");
            }
            if (archive == null)
            {
                var current = Get().ArchiveData();
                archive = current.Definition.Version == snapshot.CatalogueVersion ? current
                    : CampaignCatalogueArchive.AdoptLegacyBoundary(current, snapshot);
                CampaignCatalogueArchive.Write(store.RootDirectory, archive, LibraryRoot);
                Plugin.Logger.LogInfo($"Expedition catalogue pinned; slotRoot={store.RootDirectory}; version={archive.Definition.Version}; adoptedFrom={archive.AdoptedFromVersion ?? "none"}; round={archive.AdoptedAtRound?.ToString() ?? "none"}. Checkpoint/tank/progress preserved.");
            }
            // Native definitions still govern loading. A plugin removing an era
            // or tank part may remain incompatible; an archive cannot invent it.
            // An existing five-era run stays five eras; it is never extended.
            if (!EraSchedulePolicy.IsValidEraCount(archive.Definition.Eras.Count))
                throw new InvalidDataException("The saved expedition has an unsupported era schedule.");
            foreach (var era in archive.Definition.Eras)
                if (VehicleClassifications.GetEraByName(era.Era.NativeId)?.Playable != true)
                    throw new InvalidDataException($"Saved native era '{era.Era.NativeId}' is currently unavailable.");
            return new CampaignNativeCatalogue(archive);
        }
        var fresh = Get();
        // New Game has not selected its writable slot yet. Its era-specific
        // starter/budget must never be pinned over an existing run's manifest.
        // Both creation paths call PinTo only after ChooseNewGameSlot.
        return fresh;
    }

    internal static CampaignNativeCatalogue Get()
    {
        if (cached != null) return cached;
        foreach (var _ in BuildSteps()) { }
        return cached ?? throw new InvalidOperationException("Native catalogue preparation did not complete.");
    }

    internal static IEnumerator<bool> PrepareInFrames() => BuildSteps().GetEnumerator();

    private static IEnumerable<bool> BuildSteps()
    {
        if (cached != null) yield break;
        var eraNames = EraNames;
        if (eraNames.Count == 0)
            throw new InvalidOperationException("The game lists no native eras. Native definitions were preserved.");
        // The whole native list (names, starts, ends, playable flags) is part of
        // the cache identity, so a new or changed era invalidates cached cards.
        var eraSignature = string.Join("|", eraNames.Select(EraSignaturePart));
        communityCards.SetContext(eraSignature);
        var found = new Dictionary<string, Tank>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Sprocket", "Factions", CampaignFactionDesigns.FactionName, "Blueprints", "Vehicles");
        var stock = Path.Combine(Paths.GameRootPath, "Sprocket_Data", "StreamingAssets", "Blueprints", "Vehicles");
        foreach (var directory in new[] { custom, stock })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.GetFiles(directory, "*.blueprint").OrderBy(p => p, StringComparer.Ordinal))
            {
                yield return true;
                if (directory == custom && DesignCapPolicy.SkipFactionBlueprint(path))
                {
                    Plugin.Logger.LogInfo($"Expedition faction Autosave skipped: '{Path.GetFileName(path)}'.");
                    continue;
                }
                // These bundled entries are stationary guns or firing-range
                // targets, not the tank opponents requested for this mode.
                if (directory == stock && IsBundledStationary(path)) continue;
                try
                {
                    // One immutable local copy backs both the card and later spawn.
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024) continue;
                    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    var id = "native." + hash[..32];
                    if (found.ContainsKey(id)) continue;
                    var card = VehicleCard.LoadVehicleCard(path);
                    if (card == null || card.Cost <= 0) continue;
                    if (CampaignTankNamePolicy.IsStationaryGun(card.Name)) continue;
                    var era = VehicleClassifications.GetEra(card.CreationDate);
                    if (era == null || !eraNames.Contains(era.Name, StringComparer.Ordinal)) continue;
                    // The curated faction wins over the same named built-in
                    // model. Distinct model names keep their exact identities.
                    if (!names.Add(era.Name + "\n" + card.Name)) continue;
                    found.Add(id, new Tank(id, card.Name, hash, era.Name, card.Cost, bytes,
                        MassKg: MeasuredMass(card.Mass)));
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"Expedition candidate skipped '{Path.GetFileName(path)}': {ex.Message}");
                }
            }
        }
        // The first playable native era that has a tank defines the economy
        // baseline (WWI in the standard game).
        var firstEra = NativeEraList.FirstOrDefault(era => era.Playable
            && found.Values.Any(t => t.Era == era.Name))?.Name
            ?? throw new InvalidOperationException(
                "No native designs were found in any playable era. Add blueprints to the Expedition Spawns faction.");
        var wwi = found.Values.Where(t => t.Era == firstEra && !t.DangerousOnly).OrderBy(t => t.Price)
            .ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
        // The three cheapest native vehicle cards of that era define the recorded
        // starter baseline. With fewer candidates the smaller measured sample is explicit.
        var baseline = wwi.Take(3).Select(t => t.Price).OrderBy(p => p).ToArray();
        var median = baseline.Length % 2 == 0
            ? (baseline[baseline.Length / 2 - 1] + baseline[baseline.Length / 2]) / 2m
            : baseline[baseline.Length / 2];
        // Curated built-in starter; never use the last-loaded modern personal tank.
        var starter = wwi.FirstOrDefault(t => t.Name == "NTL Light")
            ?? wwi.FirstOrDefault(t => !t.Name.Contains("Gun", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No {firstEra} tank starter is available.");
        var choices = found.Values.Where(t => !t.DangerousOnly).GroupBy(t => t.Era).ToDictionary(g => g.Key,
            g => g.OrderBy(t => t.Price).ThenBy(t => t.Id, StringComparer.Ordinal).First().Id, StringComparer.Ordinal);
        choices[firstEra] = starter.Id;
        // Calculate B0/starter from the normal pool first. Personal era winners
        // add opponents without changing the next run's starting economy.
        foreach (var veteran in CampaignVeteranStore.ReadAll(CampaignVeteranUnlocks.DirectoryPath,
            reason => Plugin.Logger.LogWarning("Expedition veteran skipped: " + reason)))
        {
            yield return true;
            try
            {
                var id = "native." + veteran.Hash[..32];
                if (CampaignTankNamePolicy.IsStationaryGun(veteran.Name)) continue;
                if (!eraNames.Contains(veteran.Era, StringComparer.Ordinal)) continue;
                var unit = CustomBattleBlueprintResolver.CreateLockedUnit(veteran.Bytes, out var error)
                    ?? throw new InvalidDataException(error);
                if (!DateBelongsToEra(unit.CreationDate, veteran.Era))
                    throw new InvalidDataException("Veteran native era differs from its recorded victory era.");
                found[id] = new Tank(id, veteran.Name + " (Veteran)", veteran.Hash,
                    veteran.Era, veteran.Price, veteran.Bytes.ToArray());
            }
            catch (Exception ex) { Plugin.Logger.LogWarning("Expedition veteran native validation failed: " + ex.Message); }
        }
        // Imported community bytes are already immutable, content-addressed files.
        // Add them after starter pricing, retaining every distinct design (same
        // names can occur in different community packs). Native dates decide era.
        // A marker present in both folders is imported only from the dangerous one.
        var communityScan = DangerousBlueprintFolders.Resolve(
            MarkerNames(CommunityDirectory), MarkerNames(DangerousCommunityDirectory));
        var communityStats = new ImportStats();
        foreach (var step in ImportMarkers(communityScan.CommunityOnly, CommunityDirectory,
            found, eraNames, dangerous: false, communityStats))
            yield return step;
        Plugin.Logger.LogInfo($"Expedition community pool: accepted={communityStats.Accepted}; skipped={communityStats.Skipped}; reused native card metadata={communityStats.Reused}; native eras/costs retained; startup card validation only.");
        if (communityScan.Dangerous.Count > 0)
        {
            var dangerousStats = new ImportStats();
            foreach (var step in ImportMarkers(communityScan.Dangerous, DangerousCommunityDirectory,
                found, eraNames, dangerous: true, dangerousStats))
                yield return step;
            foreach (var duplicate in communityScan.Duplicates)
                Plugin.Logger.LogWarning($"Expedition dangerous-only blueprint '{duplicate}' also exists in community-blueprints; Dangerous wins and the normal copy was not imported.");
            Plugin.Logger.LogInfo($"Expedition dangerous-only pool: accepted={dangerousStats.Accepted}; skipped={dangerousStats.Skipped}; duplicates={communityScan.Duplicates.Count}; native eras/costs retained; startup card validation only.");
        }
        if (SpreadIntoLaterEras)
            foreach (var era in SpreadIntoEmptyLaterEras(found))
            {
                var choice = found.Values.Where(t => t.Era == era && !t.DangerousOnly)
                    .OrderBy(t => t.Price).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
                if (choice != null) choices[era] = choice.Id;
            }
        // Schedule: every playable native era with a usable tank, by start date.
        var schedule = EraSchedulePolicy.Resolve(NativeEraList
            .Select(era => new NativeEraInfo(era.Name, era.StartKey, era.Playable,
                found.Values.Any(tank => tank.Era == era.Name))).ToArray());
        Plugin.Logger.LogInfo("Expedition " + schedule.Reason);
        if (!schedule.IsUsable) throw new InvalidOperationException(schedule.Reason);
        // An era outside the schedule (not playable, or without a usable tank)
        // never contributes content to the new catalogue, even if a loose card was
        // readable during the scan.
        foreach (var id in found.Where(pair => !schedule.EraNames.Contains(pair.Value.Era, StringComparer.Ordinal))
            .Select(pair => pair.Key).ToArray())
            found.Remove(id);
        foreach (var era in choices.Keys.Where(era => !schedule.EraNames.Contains(era, StringComparer.Ordinal)).ToArray())
            choices.Remove(era);
        if (!schedule.EraNames.Contains(starter.Era, StringComparer.Ordinal))
            throw new InvalidOperationException($"The starter era '{starter.Era}' is not part of the era schedule.");
        IReadOnlyList<TestPoolDesign>? testPool = null;
        if (TestPoolEnabled)
        {
            foreach (var step in PrepareTestPool()) yield return step;
            testPool = testPoolTanks.Select(t => new TestPoolDesign(t.Name,
                new EnemyDesignEntry(new BlueprintReference(t.Id, t.Hash, new EraReference(t.Era), Array.Empty<ModRequirement>()),
                    CampaignFormationLabels.Role(t.Name, t.MassKg, t.Era), t.Price, new ScenarioValue(t.Price, CostUnit), ContentQualification.Candidate)
                    { MassKg = t.MassKg })).ToArray();
            if (testPool.Count < 3) throw new InvalidOperationException("Test pool needs at least three accepted distinct tanks. See stats/test-pool-report.csv.");
            foreach (var tank in testPoolTanks) found.Add(tank.Id, tank);
        }
        Plugin.Logger.LogInfo("Expedition dangerous-only designs: "
            + string.Join("; ", schedule.EraNames.Select(e =>
                $"{e}={found.Values.Count(t => t.Era == e && t.DangerousOnly)}")) + ".");
        CampaignEraAvailability.Publish(schedule);
        cached = new CampaignNativeCatalogue(found, median * 2m, starter, choices,
            schedule.EraNames, testPool);
        Plugin.Logger.LogInfo($"Expedition native candidate catalogue {cached.Definition.Version}; "
            + $"B0={cached.InitialBudget}; saved native scalar prices; starter sample={baseline.Length}; "
            + $"schedule={string.Join(",", schedule.EraNames)}; {schedule.Reason} "
            + string.Join("; ", schedule.EraNames.Select(e => $"{e}={found.Values.Count(t => t.Era == e)}"))
            + "; maps=" + string.Join(",", cached.Definition.Eras[0].Maps.Select(m => m.MapId)));
    }

    // Test aid (2026-10-31, user request): eras added later (e.g. Coldwar2/Coldwar3
    // from Expanded Ballistics) have no tanks yet. When on, each run of empty playable
    // eras after a populated era shares that era's tanks: sorted by price, cheapest
    // slice stays, pricier slices move to the later eras. Only the in-memory era label
    // changes; blueprint files and their dates stay untouched.
    private static BepInEx.Configuration.ConfigEntry<bool>? spreadIntoLaterEras;
    internal static bool SpreadIntoLaterEras => spreadIntoLaterEras?.Value ?? false;
    private static BepInEx.Configuration.ConfigEntry<string>? designCaps;
    private static BepInEx.Configuration.ConfigEntry<bool>? testPoolMode;
    internal static bool TestPoolEnabled => testPoolMode?.Value ?? false;
    private static readonly List<Tank> testPoolTanks = new();
    private static bool testPoolPrepared;
    internal static int TestPoolCount => testPoolTanks.Count;
    internal static IEnumerable<bool> PrepareTestPool()
    {
        if (!TestPoolEnabled || testPoolPrepared) yield break;
        Directory.CreateDirectory(TestPoolDirectory);
        var report = new List<CampaignTestPool.Entry>();
        testPoolTanks.Clear();
        foreach (var entry in CampaignTestPool.Scan(TestPoolDirectory, path =>
        {
            var card = VehicleCard.LoadVehicleCard(path)
                ?? throw new InvalidDataException("Native vehicle card could not be loaded.");
            CampaignPartCheck.ThrowIfUnknownParts(path);
            var era = VehicleClassifications.GetEra(card.CreationDate);
            return new CampaignTestPool.Card(card.Name, card.CreationDate.ToString(),
                era != null && EraNames.Contains(era.Name, StringComparer.Ordinal) ? era.Name : "",
                card.Cost, MeasuredMass(card.Mass));
        }))
        {
            report.Add(entry);
            if (entry.Accepted)
            {
                var card = entry.Card!;
                testPoolTanks.Add(new Tank("test." + entry.Hash![..32], card.Name, entry.Hash!, card.Era,
                    card.Price, entry.Bytes!, MassKg: card.MassKg));
            }
            else Plugin.Logger.LogWarning($"Expedition test pool: rejected {entry.File}: {entry.Reason}");
            yield return true;
        }
        try { CampaignTestPool.Report(TestPoolReportPath, report); }
        catch (Exception ex) { Plugin.Logger.LogWarning("Expedition test pool report: " + ex.Message); }
        testPoolPrepared = true;
        Plugin.Logger.LogInfo($"Expedition test pool: accepted={testPoolTanks.Count}; rejected={report.Count - testPoolTanks.Count}; native card + part GUID validation.");
    }
    internal static void Configure(BepInEx.Configuration.ConfigFile config)
    {
        designCaps = config.Bind("Campaign", "DesignCaps", DesignCapPolicy.DefaultConfig,
            "Exact native design name=maximum per battle; separate entries with semicolons. New catalogues only; test pool unchanged.");
        testPoolMode = config.Bind("Campaign", "TestPool", false,
            "New runs take enemies only from the test pool folder (for testing new designs). Records and veterans are not updated.");
        spreadIntoLaterEras = config.Bind("Campaign", "SpreadTanksIntoEmptyLaterEras", true,
            "Test aid: eras without tanks (e.g. Coldwar2/Coldwar3 from Expanded Ballistics) borrow the "
            + "pricier tanks of the nearest earlier era. Blueprint files are not changed. New runs only.");
    }

    private sealed class ImportStats
    {
        internal int Accepted;
        internal int Skipped;
        internal int Reused;
    }

    private static string[] MarkerNames(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.blueprint")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

    // One community marker. Dangerous markers may replace an already indexed
    // design with the same content id; normal markers keep the earlier source.
    private static IEnumerable<bool> ImportMarkers(IReadOnlyList<string> markers, string directory,
        Dictionary<string, Tank> found, IReadOnlyList<string> eraNames, bool dangerous, ImportStats stats)
    {
        foreach (var marker in markers)
        {
            yield return true;
            var path = Path.Combine(directory, marker + ".blueprint");
            try
            {
                var hash = marker;
                _ = CampaignBlueprintBlob.PathFor(LibraryRoot, hash);
                var id = "native." + hash[..32];
                if (found.ContainsKey(id) && !dangerous) continue;
                var file = new FileInfo(path);
                var length = file.Length;
                var modified = file.LastWriteTimeUtc.Ticks;
                if (length is < 1 or > CampaignBlueprintBlob.MaximumBytes)
                    throw new InvalidDataException("Blueprint size exceeds the supported limit.");
                if (found.Count >= 4096) throw new InvalidDataException("Community library exceeds 4096 designs.");
                if (communityCards.TryGet(path, length, modified, hash, out var previous))
                {
                    found[id] = dangerous ? previous with { DangerousOnly = true } : previous;
                    stats.Accepted++; stats.Reused++;
                    continue;
                }
                _ = CampaignBlueprintBlob.Read(path, hash);
                var card = VehicleCard.LoadVehicleCard(path);
                if (card == null || card.Cost <= 0 || string.IsNullOrWhiteSpace(card.Name))
                    throw new InvalidDataException("Native vehicle card has no valid name/cost.");
                if (CampaignTankNamePolicy.IsStationaryGun(card.Name)) { stats.Skipped++; continue; }
                var era = VehicleClassifications.GetEra(card.CreationDate);
                if (era == null || !eraNames.Contains(era.Name, StringComparer.Ordinal))
                    throw new InvalidDataException("Native creation date has no playable campaign era.");
                var tank = new Tank(id, card.Name, hash, era.Name, card.Cost, Array.Empty<byte>(), path,
                    MeasuredMass(card.Mass), dangerous);
                found[id] = tank;
                file.Refresh();
                if (file.Length == length && file.LastWriteTimeUtc.Ticks == modified)
                    communityCards.Store(path, length, modified, hash, tank);
                stats.Accepted++;
            }
            catch (Exception ex)
            {
                stats.Skipped++;
                Plugin.Logger.LogWarning($"Expedition {(dangerous ? "dangerous-only" : "community")} blueprint skipped '{Path.GetFileName(path)}': {ex.Message}");
            }
        }
    }

    private static List<string> SpreadIntoEmptyLaterEras(Dictionary<string, Tank> found)
    {
        var filled = new List<string>();
        var playable = NativeEraList.Where(era => era.Playable).Select(era => era.Name).ToArray();
        for (var index = 0; index < playable.Length; index++)
        {
            var source = playable[index];
            var empty = playable.Skip(index + 1).TakeWhile(era => !found.Values.Any(t => t.Era == era)).ToArray();
            if (empty.Length == 0) continue;
            var pool = found.Values.Where(t => t.Era == source).OrderBy(t => t.Price)
                .ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
            if (pool.Length == 0) continue;
            var slots = Math.Min(empty.Length + 1, pool.Length);
            for (var slot = 1; slot < slots; slot++)
            {
                var from = pool.Length * slot / slots;
                var to = pool.Length * (slot + 1) / slots;
                for (var position = from; position < to; position++)
                    found[pool[position].Id] = pool[position] with { Era = empty[slot - 1] };
                filled.Add(empty[slot - 1]);
            }
            Plugin.Logger.LogInfo($"Expedition test aid: {pool.Length} {source} tank(s) spread over "
                + string.Join(", ", new[] { source }.Concat(empty.Take(slots - 1))
                    .Select(era => $"{era}={found.Values.Count(t => t.Era == era)}"))
                + (slots - 1 < empty.Length ? $"; too few tanks for {string.Join(", ", empty.Skip(slots - 1))}" : "") + ".");
            index += slots - 1;
        }
        return filled;
    }

    internal Tank Find(string id, string era)
    {
        if (!tanks.TryGetValue(id, out var tank) || tank.Era != era)
            throw new InvalidOperationException($"The selected enemy '{id}' is unavailable in {era}. No other era was substituted.");
        return tank;
    }
    internal Tank FindAny(string id) => tanks.TryGetValue(id, out var tank) ? tank
        : throw new InvalidOperationException($"Pinned design '{id}' is missing.");
    internal string DescribeAny(string id)
    {
        var tank = FindAny(id);
        return $"{tank.Name} ({tank.Era}, {tank.Price:N0} each)";
    }

    internal void ConfigureStartingEra(int index)
    {
        if (index < 0 || index >= Eras.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var era = Eras[index].NativeId;
        var selected = starterChoices.TryGetValue(era, out var id) ? Find(id, era)
            : tanks.Values.Where(t => t.Era == era && !t.DangerousOnly)
                .OrderBy(t => t.Price).ThenBy(t => t.Id, StringComparer.Ordinal)
                .FirstOrDefault(t => !t.Name.EndsWith(" (Veteran)", StringComparison.Ordinal))
                ?? tanks.Values.Where(t => t.Era == era && !t.DangerousOnly)
                    .OrderBy(t => t.Price).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException($"No starter tank without a minimum risk is available in {era}.");
        startingEraIndex = index;
        starterId = selected.Id;
        Starter = selected.Bytes.ToArray();
        starterMeasured = false;
        EnsureStarterBudget(selected.Price);
    }

    private void EnsureStarterBudget(decimal price)
    {
        var multiplier = RunDirector.FiniteEraBudget(1m, startingEraIndex);
        // Round upward here so six-decimal block flooring cannot remove the
        // second starter. This is a base-budget quote, not invented tank cost.
        InitialBudget = Math.Max(InitialBudget, decimal.Ceiling(checked(price * 2m / multiplier) * 1_000_000m) / 1_000_000m);
    }

    internal void MeasureStarterPrice(decimal price)
    {
        if (starterMeasured || price <= 0m) return;
        starterMeasured = true;
        // Header prices are integer native snapshots; preserve the exact live
        // quote when rounding would otherwise remove the starting friendly copy.
        EnsureStarterBudget(price);
        Plugin.Logger.LogInfo($"Expedition {Eras[startingEraIndex].NativeId} starter live price={price}; initial fleet ceiling={StartingBudget}.");
    }

    internal string Describe(string id, string era)
    {
        var tank = Find(id, era);
        var prices = tanks.Values.Where(t => t.Era == era).Select(t => t.Price).Distinct().OrderBy(p => p).ToArray();
        var position = Array.IndexOf(prices, tank.Price);
        var rank = prices.Length == 1 ? 0.5m : (decimal)position / (prices.Length - 1);
        var tier = rank < 1m / 3m ? "low cost" : rank < 2m / 3m ? "mid cost" : "high cost";
        return $"{tank.Name} ({tier}, {tank.Price:N0} each)";
    }

    private static int? MeasuredMass(int mass) => mass > 0 ? mass : null;

    private static string EraSignaturePart(string name)
    {
        try
        {
            var era = VehicleClassifications.GetEraByName(name);
            return era == null
                ? name + ":missing"
                : name + ":" + era.StartDate + ":" + EndDate(name) + ":" + era.Playable;
        }
        catch
        {
            return name + ":missing";
        }
    }

    // Last day of an era, always inside that era: the day before the next era
    // starts. Only the last native era keeps the game's open-ended sentinel
    // (TechDate.MaxValue); it cannot reach into a later era because none exists.
    internal static TechDate EndDate(string era)
    {
        var index = VehicleClassifications.GetEraIndexByName(era);
        if (index < 0) throw new InvalidOperationException($"Unknown native era '{era}'.");
        var end = VehicleClassifications.GetEraEndDate(index);
        var list = VehicleClassifications.eras
            ?? throw new InvalidOperationException("The game has not loaded its era list yet.");
        if (index >= list.Length - 1) return end;
        if (VehicleClassifications.GetEraIndex(end) == index) return end;
        // The game's own end date is outside the era: use the day before the next era.
        var corrected = list[index + 1].StartDate.AddDays(-1);
        if (VehicleClassifications.GetEraIndex(corrected) != index)
            throw new InvalidOperationException($"Native era '{era}' has no valid end date before the next era starts.");
        return corrected;
    }

    internal static string? EraOf(TechDate date) => VehicleClassifications.GetEra(date)?.Name;

    // The open-ended sentinel Expedition wrote while an era was the last one.
    internal static bool IsOpenEndedDate(TechDate date) => date == TechDate.MaxValue;

    // True for a date Expedition itself writes into a run-owned design: the open
    // sentinel or the end date of any native era.
    internal static bool IsExpeditionDate(TechDate date)
    {
        if (IsOpenEndedDate(date)) return true;
        foreach (var era in NativeEraList)
            if (date == EndDate(era.Name)) return true;
        return false;
    }

    // A date belongs to an era when the game classifies it there. The open
    // sentinel saved while that era was the last one still belongs to it for
    // Expedition's own run-owned designs and veterans: the game now classifies
    // the sentinel in whatever era is last.
    internal static bool DateBelongsToEra(TechDate date, string era) =>
        EraOf(date) == era || IsOpenEndedDate(date);

    // Enemy designs may be older than the era they fight in (SpreadTanksIntoEmptyLaterEras
    // moves earlier tanks into later eras), never newer.
    internal static bool DateFitsEra(TechDate date, string era)
    {
        if (DateBelongsToEra(date, era)) return true;
        var dateIndex = EraIndex(EraOf(date));
        return dateIndex >= 0 && dateIndex <= EraIndex(era);
    }
}
