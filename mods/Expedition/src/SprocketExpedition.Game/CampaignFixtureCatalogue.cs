using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace SprocketExpedition.Game;

// Labelled first-playtest content. It is deliberately a fixture: it uses local
// synthetic map/enemy ids, never claims native qualification, and exists only to
// exercise the controller/run/offer loop against the real native encounter host.
// Six distinct era references are required by the frozen run engine; only era 0
// is expected in the first encounter, but every block has valid fixture content.
internal static class CampaignFixtureCatalogue
{
    internal const string UnitId = "fixture.scenario.unit.v1";
    internal const string Version = "fixture.firstplay.1";
    internal static readonly EraReference[] Eras = Enumerable.Range(0, 6)
        .Select(i => new EraReference("fixture.era.firstplay." + i))
        .ToArray();
    internal static readonly EraReference Era = Eras[0];
    internal static readonly ModRequirement[] NoMods = Array.Empty<ModRequirement>();

    internal static CatalogueDefinition Create()
    {
        var contents = Eras
            .Select((era, index) => new CatalogueEraContent(
                era,
                BuildMaps(era, index),
                BuildEnemies(era, index)))
            .ToArray();

        return new CatalogueDefinition(
            Version,
            CatalogueDefinition.FixtureLabel,
            true,
            UnitId,
            contents);
    }

    internal static IReadOnlyList<EraReference> EraSchedule()
        => Eras.ToArray();

    private static MapEntry[] BuildMaps(EraReference era, int eraIndex)
    {
        var names = new[] { "a", "b", "c" };
        return names
            .Select((name, index) => new MapEntry(
                era,
                $"fixture.map.{eraIndex}.{name}",
                Hash(eraIndex * 10 + index),
                new[] { $"fixture.layout.{eraIndex}.{name}.1" },
                NoMods,
                ContentQualification.Fixture))
            .ToArray();
    }

    private static EnemyDesignEntry[] BuildEnemies(EraReference era, int eraIndex)
    {
        var specs = new[]
        {
            ("line", EnemyRole.Line, 20m, 10m),
            ("support", EnemyRole.Support, 30m, 12m),
            ("heavy", EnemyRole.Heavy, 45m, 18m),
            ("recon", EnemyRole.Recon, 60m, 24m)
        };

        return specs
            .Select((spec, index) => new EnemyDesignEntry(
                new BlueprintReference(
                    $"fixture.enemy.{eraIndex}.{spec.Item1}",
                    Hash(eraIndex * 10 + 3 + index),
                    era,
                    NoMods),
                spec.Item2,
                spec.Item3,
                new ScenarioValue(spec.Item4, UnitId),
                ContentQualification.Fixture))
            .ToArray();
    }

    private static string Hash(int seed)
    {
        const string hex = "0123456789abcdef";
        var first = hex[(seed / 16) % 16];
        var second = hex[seed % 16];
        return new string(first, 32) + new string(second, 32);
    }
}
