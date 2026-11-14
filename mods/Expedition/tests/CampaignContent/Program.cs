using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game;

if (args.Length == 3 && args[0] == "--verify-test-pool")
{
    try { TestPoolChecks.SuppliedBlueprints(args[1], args[2]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}

if (args.Length == 2 && args[0] == "--verify-curation")
{
    try { CuratedPoolChecks.Verify(args[1]); return 0; }
    catch (Exception ex) { Console.WriteLine("FAIL curated pool: " + ex.Message); return 1; }
}

if (args.Length == 2 && args[0] == "--verify-music")
{
    try { MusicChecks.VerifyAssets(args[1]); return 0; }
    catch (Exception ex) { Console.WriteLine("FAIL music assets: " + ex.Message); return 1; }
}
if (args.Length == 2 && args[0] == "--inspect-pool")
{
    using var inventory = JsonDocument.Parse(File.ReadAllText(args[1]));
    var names = inventory.RootElement.GetProperty("entries").EnumerateArray()
        .Where(e => e.GetProperty("date").ValueKind != JsonValueKind.Null)
        .Select(e => e.GetProperty("name").GetString()).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { VehicleBlueprints = names.Length,
        Filtered = names.Where(CampaignTankNamePolicy.IsStationaryGun).ToArray() },new JsonSerializerOptions { WriteIndented=true }));
    return 0;
}
if (args.Length == 2 && args[0] == "--verify-slots")
{
    try
    {
    var configRoot = args[1];
    var slots = Path.Combine(configRoot,"slots");
    foreach (var slot in Directory.GetDirectories(slots,"slot-*"))
    {
        var load = new SprocketExpedition.Core.Persistence.RunStore(slot).LoadActiveRun(SprocketExpedition.Core.Persistence.ModAvailability.None);
        if (load.Snapshot is not { } snapshot)
        { Console.WriteLine(Path.GetFileName(slot)+": "+load.Status); continue; }
        _ = RunDirector.Restore(snapshot);
        if (snapshot.CatalogueVersion.StartsWith("native.cost.1.",StringComparison.Ordinal))
        {
            var archive = CampaignCatalogueArchive.Read(slot,snapshot.CatalogueVersion,Path.Combine(configRoot,"content-library"));
            if (archive == null)
            {
                var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.CatalogueVersion))).ToLowerInvariant()+".json";
                var archived = Directory.GetFiles(Path.Combine(configRoot,"slots","archive"),expected,SearchOption.AllDirectories);
                if (archived.Length > 0)
                {
                    archive = CampaignCatalogueArchive.Read(Directory.GetParent(Path.GetDirectoryName(archived[0])!)!.FullName,
                        snapshot.CatalogueVersion,Path.Combine(configRoot,"content-library"));
                    Console.WriteLine(Path.GetFileName(slot)+": exact missing catalogue found in archived slot (read-only)");
                }
            }
            if (archive == null)
            {
                var boundary = snapshot.Phase == RunPhase.Building && snapshot.Offers.Count == 0 && snapshot.LockedPlan == null && snapshot.CurrentAttempt == null;
                Console.WriteLine(Path.GetFileName(slot)+": schema "+snapshot.SchemaVersion+", round "+snapshot.Round+", no old archive; safe legacy boundary="+boundary+" (read-only, no adoption performed)");
                continue;
            }
            if (snapshot.Offers.Count > 0)
            {
                var request = new OfferRequest(snapshot.RunId,snapshot.Seed,snapshot.Round,snapshot.BlockIndex,
                    snapshot.EraSchedule[snapshot.EraIndex],snapshot.OfferEconomy?.Budget??snapshot.AvailableBudget,snapshot.PendingSupplyPoints,snapshot.CatalogueVersion)
                    {RulesVersion=snapshot.Rewards?.Version??1,OfferRoll=snapshot.Rewards?.OfferRoll??0,
                        EnemyBudgetMultiplier=snapshot.OfferEconomy?.EnemyBudgetMultiplier??(snapshot.Rewards?.Active.Any(i=>i.Kind==RewardKind.ExperimentalContract)==true?1.2m:1m),
                        EraProgressionEnabled=snapshot.OfferEconomy?.EraProgression??false,
                        GenerationPolicy=snapshot.GenerationPolicy??(snapshot.Rewards?.Version>=5?5:0)};
                var check = new SeededEncounterCatalogue(archive.Definition,new CatalogueOptions(ContentQualification.Candidate,false,true))
                    .RestoreOffers(request,snapshot.Offers);
                if(!check.IsSuccess)throw new Exception(Path.GetFileName(slot)+": "+string.Join(";",check.Diagnostics));
            }
        }
        Console.WriteLine(Path.GetFileName(slot)+": schema "+snapshot.SchemaVersion+", round "+snapshot.Round+", pinned catalogue/offers verified (read-only)");
    }
    return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine("FAIL read-only slot check: " + ex.Message); return 1; }
}
var testBase = Path.Combine(AppContext.BaseDirectory, "test-temp");
Directory.CreateDirectory(testBase);
var root = Path.Combine(testBase, "expedition-content-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    BossFixtureChecks.Run(root);
    SpecialSpawnChecks.Run();
    SpawnSafetyChecks.Run();
    FormationLabelChecks.Run();
    DeploymentChecks.Run();
    EraRangeChecks.Run();
    GroupDoctrineChecks.Run();
    SpawnVerificationChecks.Run();
    SupportDepthChecks.Run();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action, string message)
    { try { action(); } catch (InvalidDataException) { return; } throw new Exception(message); }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    var names = new[] { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar" };
    var eras = names.Select(name => new EraReference(name)).ToArray();
    var tanks = eras.Select((era, index) =>
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic enemy " + index);
        var hash = Hash(bytes);
        return new ArchivedCampaignTank("native." + hash[..32], "Synthetic " + index, hash, era.NativeId, 10m + index, bytes)
            { MassKg = 42_000 + index };
    }).ToArray();
    const string unit = "native.design.cost.v1";
    var definition = new CatalogueDefinition("native.cost.1.synthetic", "Synthetic archive test", false, unit,
        eras.Select((era, index) => new CatalogueEraContent(era,
            CampaignMapPool.CandidateNames.Select(name => new MapEntry(era, name,
                Hash(Encoding.UTF8.GetBytes(name)), new[] { "native.default" }, Array.Empty<ModRequirement>(), ContentQualification.Candidate)).ToArray(),
            new[] { new EnemyDesignEntry(new BlueprintReference(tanks[index].Id, tanks[index].Hash, era, Array.Empty<ModRequirement>()),
                EnemyRole.Line, tanks[index].Price, new ScenarioValue(tanks[index].Price, unit), ContentQualification.Candidate)
                { FormationFamily = "Synthetic Family", MassKg = tanks[index].MassKg } })).ToArray());
    var data = new CampaignArchiveData(definition, 200m, tanks[0].Id, tanks);
    TestPoolChecks.Run(root, data);
    BattleStatsChecks.Run();
    BattleStatsHookChecks.Run();
    BlobChecks.Run(Path.Combine(root,"blob-checks"), definition);
    var recoveryRoot = Path.Combine(root,"recovery");
    var archivedSlot = Path.Combine(recoveryRoot,"slots","archive","slot-1-old");
    var liveSlot = Path.Combine(recoveryRoot,"slots","slot-1");
    CampaignCatalogueArchive.Write(archivedSlot,data,recoveryRoot);
    var recoveryDirector = new RunDirector();
    Check(recoveryDirector.NewRun(new RunConfiguration("exact-recovery",42,new ScenarioValue(200m,unit),eras,definition.Version)).IsAccepted,"recovery fixture");
    Check(CampaignCatalogueArchive.RecoverMissing(liveSlot,recoveryDirector.Snapshot!,recoveryRoot)?.Definition.Version==definition.Version,"exact missing catalogue recovered from sibling archive");
    var sourceManifest=Directory.GetFiles(Path.Combine(archivedSlot,"catalogues")).Single();
    var copiedManifest=Directory.GetFiles(Path.Combine(liveSlot,"catalogues")).Single();
    Check(File.ReadAllBytes(sourceManifest).SequenceEqual(File.ReadAllBytes(copiedManifest)),"recovery preserves exact manifest bytes");
    File.Move(copiedManifest,copiedManifest+".archived");
    CampaignCatalogueArchive.Write(liveSlot,data,recoveryRoot);
    Check(CampaignCatalogueArchive.Read(liveSlot,definition.Version,recoveryRoot)!=null,"catalogue re-pin after slot archival");
    CampaignCatalogueArchive.Write(root, data);
    CampaignCatalogueArchive.Write(root, data); // Exact duplicate is idempotent.
    var loaded = CampaignCatalogueArchive.Read(root, definition.Version)!;
    Check(loaded.Definition.Version == definition.Version, "pinned catalogue version survives archive round-trip");
    Check(loaded.Definition.Eras[0].Enemies[0].Role == EnemyRole.Line,
        "pinned enemy role survives archive round-trip without reclassification");
    Check(loaded.Tanks[0].ReadBytes().SequenceEqual(tanks[0].Bytes), "exact enemy bytes round-trip");
    Check(loaded.Definition.Eras[0].Enemies[0].FormationFamily == "Synthetic Family",
        "authored formation family survives the actual native catalogue archive path");
    Check(loaded.Definition.Eras[0].Enemies[0].MassKg == 42_000
        && loaded.Tanks[0].MassKg == 42_000,
        "measured mass survives the actual native catalogue archive path");
    Check(CampaignCatalogueArchive.Read(root, "missing") == null, "missing version is explicit");
    var request = new OfferRequest("synthetic-run", 42, 7, 2, eras[2], new ScenarioValue(200m, unit), 0, definition.Version);
    var options = new CatalogueOptions(ContentQualification.Candidate, false, true);
    var offers = new SeededEncounterCatalogue(definition, options).TryGenerateOffers(request).Offers;
    Check(offers.Count == 3 && offers.Select(o => o.MapId).Distinct().Count() == 3, "three maps appear once each in the offer set");
    Check(new SeededEncounterCatalogue(loaded.Definition, options).RestoreOffers(request, offers).IsSuccess,
        "exact offers restore from archived catalogue");
    var reclassifiedEras = definition.Eras.Select(era => era with
    {
        Enemies = era.Enemies.Select(enemy => enemy with
        {
            Role = EnemyRole.Recon,
            FormationFamily = "Reclassified family"
        }).ToArray()
    }).ToArray();
    var changed = data with
    {
        Definition = definition with
        {
            Version = "native.cost.1.changed",
            Label = "New live catalogue",
            Eras = reclassifiedEras
        }
    };
    CampaignCatalogueArchive.Write(root, changed);
    Check(new SeededEncounterCatalogue(CampaignCatalogueArchive.Read(root, definition.Version)!.Definition, options)
        .RestoreOffers(request, offers).IsSuccess,
        "a differently classified new live catalogue cannot replace the old archive or its offers");
    Reject(() => CampaignCatalogueArchive.Write(root, data with { Definition = definition with { Label = "conflicting bytes" } }),
        "immutable archive was overwritten");
    Reject(() => CampaignCatalogueArchive.Write(root, data with { Tanks = tanks.Select((tank, i) => i == 0
        ? tank with { Bytes = Encoding.UTF8.GetBytes("tampered") } : tank).ToArray() }), "bad enemy hash accepted");
    var director = new RunDirector();
    Check(director.NewRun(new RunConfiguration(Guid.NewGuid().ToString("N"), 42, new ScenarioValue(200m, unit), eras, "native.cost.1.old")).IsAccepted,
        "synthetic legacy run starts");
    var saved = director.Snapshot!;
    var adopted = CampaignCatalogueArchive.AdoptLegacyBoundary(data, saved);
    Check(adopted.Definition.Version == saved.CatalogueVersion && adopted.AdoptedFromVersion == definition.Version
        && adopted.AdoptedAtRound == saved.Round, "legacy safe-boundary adoption is explicit and preserves run identity");
    CampaignCatalogueArchive.Write(root, adopted);
    Check(CampaignCatalogueArchive.Read(root, saved.CatalogueVersion)!.AdoptedFromVersion == definition.Version,
        "recovery lineage persists");
    foreach (var unsafeSave in new[]
    {
        saved with { Phase = RunPhase.Fighting },
        saved with { Offers = offers },
        saved with { LockedPlan = offers[0] },
        saved with { SelectedOfferId = offers[0].OfferId },
        saved with { CurrentAttempt = new AttemptIdentity(saved.RunId, offers[0].OfferId, offers[0].PlanHash, 1) },
        saved with { CostUnitId = "another-unit" },
        saved with { EraSchedule = eras.Reverse().ToArray() }
    }) Reject(() => CampaignCatalogueArchive.AdoptLegacyBoundary(data, unsafeSave), "unsafe legacy adoption accepted");
    var archiveFile = Directory.GetFiles(Path.Combine(root, "catalogues"))[0];
    File.WriteAllText(archiveFile, File.ReadAllText(archiveFile).Replace("\"Schema\":2", "\"Schema\":99"));
    Reject(() => CampaignCatalogueArchive.Read(root, data.Definition.Version), "corrupt schema accepted");
    var maps = Path.Combine(root, "maps");
    Directory.CreateDirectory(maps);
    void MapConfig(string name, bool deathmatch, int secondCap) => File.WriteAllText(Path.Combine(maps, name + ".json"),
        JsonSerializer.Serialize(new { Identifier = name, Deathmatch = deathmatch, TeamCount = 2, TeamSpawnsSupported = new[] { 8, secondCap } }));
    foreach (var name in CampaignMapPool.CandidateNames) { MapConfig(name, true, 12); Check(CampaignMapPool.Read(maps, name).Id == name, "map ID passes through unchanged"); }
    MapConfig("Dunes", false, 12);
    Reject(() => CampaignMapPool.Read(maps, "Dunes"), "non-Deathmatch map accepted");
    MapConfig("Fields", true, 11);
    Reject(() => CampaignMapPool.Read(maps, "Fields"), "insufficient enemy spawn capacity accepted");
    Reject(() => CampaignMapPool.Read(maps, "../escape"), "unknown/path map accepted");
    var anchors = new[]
    {
        new SpawnArcAnchor(new System.Numerics.Vector2(-20, -25), System.Numerics.Vector2.UnitY),
        new SpawnArcAnchor(new System.Numerics.Vector2(20, 25), System.Numerics.Vector2.UnitY),
        new SpawnArcAnchor(new System.Numerics.Vector2(0, 0), System.Numerics.Vector2.UnitY)
    };
    for (var seed = 0; seed < 24; seed++)
    {
        Check(CampaignSpawnArcPlanner.TryPlan(anchors, 32, "encounter-" + seed, (_, _) => true, out var arc), "front arc can place 32 markers");
        Check(CampaignSpawnArcPlanner.TryPlan(anchors, 32, "encounter-" + seed, (_, _) => true, out var retryArc)
            && arc.SequenceEqual(retryArc), "same encounter retains its random layout");
        Check(arc.Any(p => p.X < 0) && arc.Any(p => p.X > 0), "random layout spans both sides of the front arc");
        for (var i = 0; i < arc.Length; i++)
        {
            foreach (var anchor in anchors)
            {
                var delta = arc[i] - anchor.Position;
                Check(delta.Length() >= 350f && delta.Length() <= 500f, "every possible player spawn sees the promised range");
                Check(System.Numerics.Vector2.Dot(delta, anchor.Forward) >= 0, "no enemy marker is behind any player spawn");
            }
            for (var j = 0; j < i; j++)
                Check(System.Numerics.Vector2.Distance(arc[i], arc[j]) >= 24f, "enemy markers cannot overlap");
        }
    }
    Check(CampaignSpawnArcPlanner.TryPlan(anchors, 32, "right-only", (_, point) => point.X > 0, out var rightArc)
        && rightArc.All(p => p.X > 0), "terrain vetoes are obeyed without changing distance bounds");
    Check(!CampaignSpawnArcPlanner.TryPlan(anchors, 32, "blocked", (_, _) => false, out var blockedArc)
        && blockedArc.Length == 0, "no partially valid layout is exposed when terrain blocks placement");
    CampaignSpawnArcPlanner.TryPlan(anchors, 32, "different-a", (_, _) => true, out var layoutA);
    CampaignSpawnArcPlanner.TryPlan(anchors, 32, "different-b", (_, _) => true, out var layoutB);
    Check(!layoutA.SequenceEqual(layoutB), "different encounters get different random positions");
    var gridAnchors = Enumerable.Range(0, 32).Select(index => new SpawnArcAnchor(
        new System.Numerics.Vector2((index % 8 - 3.5f) * 20f, (index / 8 - 1.5f) * 20f), System.Numerics.Vector2.UnitY)).ToArray();
    for (var seed = 0; seed < 16; seed++)
        Check(CampaignSpawnArcPlanner.TryPlan(gridAnchors, 32, "wide-native-grid-" + seed, (_, _) => true, out _),
            "a 32-point native-style friendly grid still admits a complete randomized enemy arc");
    var impossibleAnchors = new[]
    {
        new SpawnArcAnchor(new System.Numerics.Vector2(0, 0), System.Numerics.Vector2.UnitY),
        new SpawnArcAnchor(new System.Numerics.Vector2(2000, 0), System.Numerics.Vector2.UnitY)
    };
    var longTwoRowAnchors = Enumerable.Range(0, 32).Select(i => new SpawnArcAnchor(
        new System.Numerics.Vector2((i % 16 - 7.5f) * 25f, (i / 16 - 0.5f) * 50f),
        System.Numerics.Vector2.UnitY)).ToArray();
    for (var seed = 0; seed < 24; seed++)
        foreach (var enemies in new[] { 2, 12 })
            Check(CampaignSpawnArcPlanner.TryPlan(longTwoRowAnchors, enemies, "long-two-row-" + seed,
                (_, _) => true, out var requestedArc, out var report) && requestedArc.Length == enemies,
                "plan actual enemy count across a long native-style friendly grid: " + report);
    Check(!CampaignSpawnArcPlanner.TryPlan(impossibleAnchors, 32, "impossible", (_, _) => true, out _), "impossible anchor constraints terminate safely");
    Console.WriteLine("PASS archive integrity/immutability, exact offer restore after updates, guarded legacy adoption and per-offer map/capacity checks");
    Console.WriteLine("PASS random front-arc bounds, player-anchor coverage, spacing, terrain vetoes, deterministic retry and encounter variation");
    MusicChecks.Run();
    SpawnCoverChecks.Run();
    AreaChecks.Run();
    HuntChecks.Run();
    FrameWorkChecks.Run();
    CombatPolicyChecks.Run();
    FireProgressChecks.Run();
    AmbusherChecks.Run();
    BossTacticsChecks.Run();
    BossFormationChecks.Run();
    VeteranChecks.Run(definition, eras, Path.Combine(root, "veterans"));
    VeteranChecks.Run(definition, eras, Path.Combine(root, "veterans-adventure"), true);
    VeteranChecks.StartingEra(definition, eras);
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
finally { Directory.Delete(root, true); }
