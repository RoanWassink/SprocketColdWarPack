using System.Text.Json;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;
using SprocketExpedition.Core.Run;
using SprocketExpedition.Game;

internal static class CuratedPoolChecks
{
    internal static void Verify(string path)
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(path));var root=doc.RootElement;
        var definition=root.GetProperty("Definition").Deserialize<CatalogueDefinition>()
            ?? throw new InvalidDataException("Missing native catalogue definition.");
        var rows=root.GetProperty("Selected").EnumerateArray().ToArray();
        var selected=rows.Select(r=>r.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
        if(selected.Count!=rows.Length)throw new InvalidDataException("Duplicate selected identities.");
        var filtered=rows.Where(r=>CampaignTankNamePolicy.IsStationaryGun(r.GetProperty("Name").GetString())).ToArray();
        if(filtered.Length!=0)throw new InvalidDataException("Native name filter rejects selected design: "+filtered[0].GetProperty("Name").GetString());
        definition=definition with { Version="native.cost.1.curated-preflight",Eras=definition.Eras.Select(e=>e with {
            Enemies=rows.Where(r=>r.GetProperty("Era").GetString()==e.Era.NativeId).Select(r=>
                new EnemyDesignEntry(new BlueprintReference(r.GetProperty("Id").GetString()!,r.GetProperty("Hash").GetString()!,e.Era,Array.Empty<ModRequirement>()),
                    EnemyRole.Line,r.GetProperty("Price").GetDecimal(),new ScenarioValue(r.GetProperty("Price").GetDecimal(),definition.PriceUnitId),ContentQualification.Candidate)).ToArray()
        }).ToArray() };
        var options=new CatalogueOptions(ContentQualification.Candidate,false,true);
        var initial=root.GetProperty("InitialBaseBudget").GetDecimal();var tested=0;
        for(var seed=0;seed<64;seed++)for(var round=1;round<=24;round++)
        {
            var block=(round-1)/4;var request=new OfferRequest("curated-preview-"+seed,seed,round,block,definition.Eras[block].Era,
                new ScenarioValue(RunDirector.FiniteEraBudget(initial,block),definition.PriceUnitId),0,definition.Version){RulesVersion=3};
            var catalogue=new SeededEncounterCatalogue(definition,options);var result=catalogue.TryGenerateOffers(request);
            if(!result.IsSuccess)throw new InvalidDataException($"Round {round}/seed {seed}: "+string.Join(";",result.Diagnostics));
            var plans=result.Offers!;
            if(plans.Count!=(round%4==0?1:3))throw new InvalidDataException("Wrong offer count.");
            if(plans.Any(p=>p.Enemies.Any(e=>!selected.Contains(e.BlueprintId))))throw new InvalidDataException("Offer escaped selected pool.");
            if(round%4==0)
            {
                var boss=plans.Single().Extras!.BossBlueprintId;
                var next=definition.Eras[Math.Min(block+1,5)].Enemies;
                if(next.Single(e=>e.Blueprint.LocalId==boss).CheckedPrice.Value!=next.Max(e=>e.CheckedPrice.Value))
                    throw new InvalidDataException("Boss no longer follows max-cost policy.");
            }
            var restored=new SeededEncounterCatalogue(definition,options).RestoreOffers(request,plans);
            if(!restored.IsSuccess)throw new InvalidDataException("Pinned offer restore failed: "+string.Join(";",restored.Diagnostics));
            tested++;
        }
        Console.WriteLine($"PASS {rows.Length} selected designs; {tested} real-cost round/seed requests, exact restores and curated next-era boss policy. Native spawning remains untested.");
    }
}
