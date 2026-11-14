using System.Globalization;
using System.Text;
using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

// Static content check for an authored encounter catalogue.
// Usage:
//   dotnet run --project tools/catalogue-check/CatalogueCheck.csproj -- \
//       content/example.catalogue.json [--seeds N] [--csv path]
//
// The bundled example catalogue is a TEST FIXTURE and is not playable content.
// Budgets used for the sweep are synthetic fixture values, not native costs.

if (args.Length == 0)
{
    Console.Error.WriteLine(
        "usage: CatalogueCheck <catalogue.json> [--seeds N] [--csv path]");
    return 2;
}

string cataloguePath = args[0];
int seedCount = 32;
string? csvPath = null;

for (int index = 1; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--seeds" when index + 1 < args.Length:
            if (!int.TryParse(
                args[index + 1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out seedCount)
                || seedCount < 1
                || seedCount > 10000)
            {
                Console.Error.WriteLine("--seeds must be an integer between 1 and 10000");
                return 2;
            }

            index++;
            break;
        case "--csv" when index + 1 < args.Length:
            csvPath = args[index + 1];
            index++;
            break;
        default:
            Console.Error.WriteLine($"unknown argument '{args[index]}'");
            return 2;
    }
}

if (!File.Exists(cataloguePath))
{
    Console.Error.WriteLine($"catalogue file not found: {cataloguePath}");
    return 2;
}

CatalogueDefinition definition;
try
{
    definition = CatalogueJson.Load(File.ReadAllText(cataloguePath));
}
catch (CatalogueJsonException exception)
{
    Console.Error.WriteLine($"catalogue JSON rejected: {exception.Message}");
    return 1;
}

Console.WriteLine($"catalogue: {definition.Version}");
Console.WriteLine($"label:     {definition.Label}");
Console.WriteLine($"fixture:   {definition.IsFixture}");
Console.WriteLine($"unit:      {definition.PriceUnitId}");

if (definition.IsFixture)
{
    Console.WriteLine(
        "status:    TEST FIXTURE — NOT PLAYABLE; fixture content cannot satisfy release qualification");
}

var fixtureValidation = CatalogueValidator.Validate(
    definition, CatalogueOptions.Fixture);
PrintValidation("structural/fixture validation", fixtureValidation);

var releaseValidation = CatalogueValidator.Validate(
    definition, CatalogueOptions.Release);
PrintValidation("release validation", releaseValidation);

if (!fixtureValidation.IsValid)
{
    return 1;
}

if (!definition.IsFixture && !releaseValidation.IsValid)
{
    Console.Error.WriteLine(
        "real catalogue is not release-qualified; no sweep performed");
    return 1;
}

var options = releaseValidation.IsValid
    ? CatalogueOptions.Release
    : CatalogueOptions.Fixture;

SeededEncounterCatalogue catalogue;
try
{
    catalogue = new SeededEncounterCatalogue(definition, options);
}
catch (EncounterAvailabilityException exception)
{
    Console.Error.WriteLine(
        $"catalogue unavailable: {exception.Status}: {string.Join("; ", exception.Diagnostics)}");
    return 1;
}

Console.WriteLine();
foreach (var eraContent in definition.Eras)
{
    string eraId = eraContent.Era.NativeId;
    int maps = eraContent.Maps.Count(map =>
        map.Qualification >= options.RequiredQualification);
    int enemies = eraContent.Enemies.Count(enemy =>
        enemy.Qualification >= options.RequiredQualification);
    Console.WriteLine($"era {eraId}: {maps} maps, {enemies} enemy designs");
}

Console.WriteLine();
Console.WriteLine(
    "synthetic deterministic sweep (fixture budgets; NOT native costs or playable balance)");

var stats = new SortedDictionary<
    (int Round, EncounterRisk Risk),
    (int MinCount, int MaxCount, decimal MinThreat, decimal MaxThreat, int Samples)>();

int failures = 0;
string? firstFailure = null;

for (int eraIndex = 0; eraIndex < definition.Eras.Count; eraIndex++)
{
    var eraContent = definition.Eras[eraIndex];
    string eraId = eraContent.Era.NativeId;

    for (int round = 1; round <= EncounterRules.FiniteRounds; round++)
    {
        // Synthetic monotone fixture budget; never presented as a native price.
        decimal budget = 100m + round * 20m;
        int block = EncounterRules.BlockForRound(round);

        for (int seed = 1; seed <= seedCount; seed++)
        {
            var request = new OfferRequest(
                $"tool-{eraIndex}-{seed}",
                seed,
                round,
                block,
                new EraReference(eraId),
                new ScenarioValue(budget, definition.PriceUnitId),
                0,
                definition.Version);

            var result = catalogue.TryGenerateOffers(request);
            if (!result.IsSuccess)
            {
                failures++;
                firstFailure ??= $"{eraId} round {round} seed {seed}: {result.Status}: {string.Join("; ", result.Diagnostics)}";
                continue;
            }

            foreach (var plan in result.Offers)
            {
                decimal threat = EncounterOfferValidator.ComputeFormationThreat(
                    eraContent, plan.Enemies);
                int count = plan.Enemies.Sum(enemy => enemy.Count);
                var key = (round, plan.Risk);
                if (stats.TryGetValue(key, out var existing))
                {
                    stats[key] = (
                        Math.Min(existing.MinCount, count),
                        Math.Max(existing.MaxCount, count),
                        Math.Min(existing.MinThreat, threat),
                        Math.Max(existing.MaxThreat, threat),
                        existing.Samples + 1);
                }
                else
                {
                    stats[key] = (count, count, threat, threat, 1);
                }
            }
        }
    }
}

var csv = new StringBuilder();
csv.AppendLine("round,risk,min_count,max_count,min_threat,max_threat,samples");

for (int round = 1; round <= EncounterRules.FiniteRounds; round++)
{
    foreach (var profile in EncounterRules.All)
    {
        if (!stats.TryGetValue((round, profile.Risk), out var range))
        {
            continue;
        }

        Console.WriteLine(
            $"round {round,2} {profile.Risk,-9} count {range.MinCount,2}-{range.MaxCount,2} threat {range.MinThreat,4}-{range.MaxThreat,4} samples {range.Samples}");

        csv.Append(round.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(profile.Risk.ToString()).Append(',')
            .Append(range.MinCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(range.MaxCount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(range.MinThreat.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(range.MaxThreat.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(range.Samples.ToString(CultureInfo.InvariantCulture))
            .AppendLine();
    }
}

if (csvPath is not null)
{
    File.WriteAllText(csvPath, csv.ToString());
    Console.WriteLine($"wrote summary CSV: {csvPath}");
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} generation failure(s); first: {firstFailure}");
    return 1;
}

Console.WriteLine("sweep completed without generation failures");

static void PrintValidation(string title, CatalogueValidationResult result)
{
    if (result.IsValid)
    {
        Console.WriteLine($"{title}: ok");
        return;
    }

    Console.WriteLine($"{title}: {result.Errors.Count} error(s)");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  - {error}");
    }
}

return 0;
