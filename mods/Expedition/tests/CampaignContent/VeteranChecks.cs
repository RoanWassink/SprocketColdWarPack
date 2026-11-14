using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game;

internal static class VeteranChecks
{
    internal static void StartingEra(CatalogueDefinition definition, IReadOnlyList<EraReference> eras)
    {
        var director=new RunDirector();
        if(!director.NewRun(new RunConfiguration("late-start",1,new ScenarioValue(200m,definition.PriceUnitId),eras,definition.Version)
            {AdventureRules=true,StartingEraIndex=4}).IsAccepted) throw new Exception("late-start veteran setup failed");
        if(CampaignVeteranStore.Eligible(director.Snapshot!)) throw new Exception("skipped eras cannot grant veterans");
        var catalogue=new SeededEncounterCatalogue(definition,new CatalogueOptions(ContentQualification.Candidate,false,true));
        var bytes=Encoding.UTF8.GetBytes("synthetic late-start veteran");
        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        for(var win=1;win<=8;win++)
        {
            var s=director.Snapshot!;
            var quote=new DesignQuote(new BlueprintReference("player",hash,eras[s.EraIndex],Array.Empty<ModRequirement>()),
                new ScenarioValue(20m,definition.PriceUnitId),true,true,null);
            var confirmed=s.Phase==RunPhase.Building?director.ConfirmDesign(quote):director.ConfirmEncounterDesign(quote);
            if(!confirmed.IsAccepted)throw new Exception(confirmed.Reason);
            if(director.Snapshot!.Offers.Count==0)director.SetOffers(catalogue.GenerateOffers(director.CreateOfferRequest()));
            director.SelectEncounter(director.Snapshot!.Offers.Single(p=>p.Risk==EncounterRisk.Standard).OfferId);
            director.BeginEncounter();var request=director.CreateEncounterRequest();director.ReportCombatStarted(request.Attempt);
            var result=director.AcceptResult(new EncounterResult(request.Attempt,request.Plan,EncounterOutcome.Victory,null));
            if(!result.IsAccepted)throw new Exception(result.Reason);
            s=director.Snapshot!;
            if(win%4==0)
            {
                if(!CampaignVeteranStore.Eligible(s))throw new Exception("actual completed era must unlock after a late start");
                var expected=win==4?4:5;
                var entry=CampaignVeteranStore.Create(s,bytes,"Synthetic",eras[expected].NativeId);
                if(entry.CompletedRound!=(expected+1)*4||entry.Era!=eras[expected].NativeId)
                    throw new Exception("late-start veteran must record the actual canonical era/round");
            }
            else if(CampaignVeteranStore.Eligible(s))throw new Exception("ordinary late-start victory cannot unlock veteran");
            if(s.Rewards!.Choices.Count>0)director.SkipRewardChoice();
            while(director.Snapshot!.Rewards!.Inventory.Count>3)director.DiscardReward(director.Snapshot.Rewards.Inventory[0].Id);
        }
        Console.WriteLine("PASS late-era starts unlock only genuinely completed eras, with canonical veteran round identity");
    }

    internal static void Run(CatalogueDefinition definition, IReadOnlyList<EraReference> eras, string root, bool adventure = false)
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
        static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("invalid veteran must be rejected"); }
        var battles = adventure ? 4 : 3;
        var finite = battles * 6;
        var director = new RunDirector();
        Check(director.NewRun(new RunConfiguration("veteran-test", 1234,
            new ScenarioValue(200m, definition.PriceUnitId), eras, definition.Version) { AdventureRules = adventure }).IsAccepted, "new veteran test run");
        var catalogue = new SeededEncounterCatalogue(definition, new CatalogueOptions(ContentQualification.Candidate, false, true));
        var expected = new List<CampaignVeteran>();
        RunSnapshot? firstBoundary = null;
        for (var round = 1; round <= finite; round++)
        {
            var before = director.Snapshot!;
            var bytes = Encoding.UTF8.GetBytes("synthetic winning tank revision for round " + round);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var quote = new DesignQuote(new BlueprintReference("player.locked", hash, eras[before.EraIndex], Array.Empty<ModRequirement>()),
                new ScenarioValue(20m + round, definition.PriceUnitId), true, true, null);
            Check((before.Phase == RunPhase.Building ? director.ConfirmDesign(quote) : director.ConfirmEncounterDesign(quote)).IsAccepted,
                "capture latest tank version before each battle");
            if (director.Snapshot!.Offers.Count == 0)
                Check(director.SetOffers(catalogue.GenerateOffers(director.CreateOfferRequest())).IsAccepted, "offers");
            Check(director.SelectEncounter(director.Snapshot.Offers[1].OfferId).IsAccepted, "selected offer");
            Check(director.BeginEncounter().IsAccepted, "begin");
            var attempt = director.Snapshot!.CurrentAttempt!;
            Check(director.ReportCombatStarted(attempt).IsAccepted, "started");
            Check(!CampaignVeteranStore.Eligible(director.Snapshot!), "fighting has no persisted victory unlock");
            var plan = director.Snapshot!.LockedPlan!;
            Check(director.AcceptResult(new EncounterResult(attempt, plan, EncounterOutcome.Victory, "win")).IsAccepted, "victory");
            var saved = director.Snapshot!;
            if (saved.Rewards?.Choices.Count > 0) Check(director.SkipRewardChoice().IsAccepted, "settle reward");
            while (director.Snapshot!.Rewards?.Inventory.Count > 3) Check(director.DiscardReward(director.Snapshot.Rewards.Inventory[0].Id).IsAccepted, "clear synthetic inventory");
            if (round % battles != 0) { Check(!CampaignVeteranStore.Eligible(saved), "ordinary win does not unlock a veteran"); continue; }
            Check(CampaignVeteranStore.Eligible(saved), "all six finite era completions unlock a veteran");
            var entry = CampaignVeteranStore.Create(saved, bytes, "Synthetic tank " + round, quote.Blueprint.Era.NativeId);
            Check(entry.Hash == hash && entry.Bytes.SequenceEqual(bytes) && entry.Price == quote.Cost.Value
                && entry.Era == eras[round / battles - 1].NativeId, "exact final-battle bytes, measured price and completed era retained");
            Check(CampaignVeteranStore.Write(root, entry), "first immutable veteran write");
            var archivePath = Path.Combine(root, entry.Hash + ".json");
            var original = File.ReadAllBytes(archivePath);
            Check(!CampaignVeteranStore.Write(root, entry) && original.SequenceEqual(File.ReadAllBytes(archivePath)), "duplicate boundary is byte-for-byte idempotent");
            Reject(() => CampaignVeteranStore.Create(saved, Encoding.UTF8.GetBytes("later edited tank"), entry.Name, entry.Era));
            Reject(() => CampaignVeteranStore.Create(saved, bytes, entry.Name, "wrong era"));
            Reject(() => CampaignVeteranStore.Create(saved with { LastOutcome = EncounterOutcome.FriendlyForceDestroyed }, bytes, entry.Name, entry.Era));
            Reject(() => CampaignVeteranStore.Write(root, entry with { Price = entry.Price + 1 }));
            expected.Add(entry);
            firstBoundary ??= saved;
        }
        var loaded = CampaignVeteranStore.ReadAll(root);
        Check(loaded.Count == 6 && expected.All(e => loaded.Any(r => r.Hash == e.Hash && r.Bytes.SequenceEqual(e.Bytes))), "library survives reload across all six native eras");
        Check(!CampaignVeteranStore.Eligible(firstBoundary! with { Victories = finite + 3, Round = finite + 4 }), "endless block is not a new finite-era completion");
        Check(!CampaignVeteranStore.Eligible(firstBoundary! with { LastOutcome = EncounterOutcome.TechnicalFailure }), "technical failure cannot promote a tank");
        Check(!CampaignVeteranStore.Eligible(firstBoundary! with { TestPool = true }), "test run cannot promote an otherwise eligible veteran");
        Check(!CampaignVeteranStore.Eligible(firstBoundary! with { ProcessedAttempts = Array.Empty<AttemptIdentity>() }), "uncommitted result cannot promote a tank");
        File.WriteAllText(Path.Combine(root, "orphan.tmp"), "unfinished write");
        var broken = Path.Combine(root, loaded[0].Hash + ".json");
        var envelope = JsonNode.Parse(File.ReadAllText(broken))!;
        envelope["Payload"] = envelope["Payload"]!.GetValue<string>() + " ";
        File.WriteAllText(broken, envelope.ToJsonString());
        var warnings = new List<string>();
        Check(CampaignVeteranStore.ReadAll(root, warnings.Add).Count == 5 && warnings.Count == 1 && File.Exists(broken),
            "corrupt record is preserved/skipped without blocking other veterans; incomplete temp ignored");
        Console.WriteLine("PASS six-era winning-version unlocks, exact price/era/bytes, persistent deduplication and corruption isolation");
    }
}

