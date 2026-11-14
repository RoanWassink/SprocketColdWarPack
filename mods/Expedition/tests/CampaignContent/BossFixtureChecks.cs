using System.Security.Cryptography;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Persistence;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game;

internal static class BossFixtureChecks
{
    internal static void Run(string temporary)
    {
        // The two saved campaign slots are not part of the source tree (they hold saved tank blueprints
        // that other mods wrote their settings into). Point EXPEDITION_FIXTURES at a folder with slot-1 and slot-2.
        var fixtures = Environment.GetEnvironmentVariable("EXPEDITION_FIXTURES");
        if (string.IsNullOrWhiteSpace(fixtures) || !Directory.Exists(fixtures))
        {
            Console.WriteLine("SKIP saved-slot fixtures: set EXPEDITION_FIXTURES to a folder with slot-1 and slot-2");
            return;
        }
        for (var i=1; i<=2; i++)
        {
            var source = Path.Combine(fixtures,"slot-"+i);
            var copy = Path.Combine(temporary,"fixture-slot-"+i);
            foreach (var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
            {
                var target = Path.Combine(copy,Path.GetRelativePath(source,file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target);
            }
            var loaded = new RunStore(copy).LoadActiveRun(ModAvailability.None);
            var snapshot = loaded.Snapshot ?? throw new Exception("fixture slot "+i+": "+loaded.Status);
            _ = RunDirector.Restore(snapshot);
            var archive = CampaignCatalogueArchive.Read(copy,snapshot.CatalogueVersion,
                Environment.GetEnvironmentVariable("EXPEDITION_FIXTURE_LIBRARY")
                ?? Path.Combine(fixtures,"content-library"))
                ?? throw new Exception("fixture archive missing");
            if (archive.Definition.BossCandidates != null) throw new Exception("fixture unexpectedly has new candidates");
            if (snapshot.Offers.Count > 0)
            {
                var request = new OfferRequest(snapshot.RunId,snapshot.Seed,snapshot.Round,snapshot.BlockIndex,
                    snapshot.EraSchedule[snapshot.EraIndex],snapshot.OfferEconomy?.Budget??snapshot.AvailableBudget,
                    snapshot.PendingSupplyPoints,snapshot.CatalogueVersion) {
                    RulesVersion=snapshot.Rewards?.Version??1, OfferRoll=snapshot.Rewards?.OfferRoll??0,
                    EnemyBudgetMultiplier=snapshot.OfferEconomy?.EnemyBudgetMultiplier??1m,
                    EraProgressionEnabled=snapshot.OfferEconomy?.EraProgression??false,
                    GenerationPolicy=snapshot.GenerationPolicy??0, TestPool=snapshot.TestPool, TestPoolOffset=snapshot.TestPoolOffset };
                var result = new SeededEncounterCatalogue(archive.Definition,new CatalogueOptions(ContentQualification.Candidate,false,true))
                    .RestoreOffers(request,snapshot.Offers);
                if (!result.IsSuccess) throw new Exception("fixture offers: "+string.Join(";",result.Diagnostics));
            }
            Console.WriteLine($"PASS fixture slot {i}: schema {snapshot.SchemaVersion}, round {snapshot.Round}, archived catalogue and {snapshot.Offers.Count} offers loaded");
        }
    }
}
