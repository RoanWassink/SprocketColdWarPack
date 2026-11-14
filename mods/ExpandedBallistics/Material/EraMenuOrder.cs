namespace SprocketExpandedBallistics.Materials;

internal static class EraMenuOrder
{
    // Family, then base footprint ascending; equal footprints use depth/cell count.
    // Modular panels use their minimum 1x1 footprint. Exact remaining ties are fixed here.
    internal static readonly IReadOnlyDictionary<string, int> Ranks = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["62a4ab89-1e44-5885-857d-b482485df8f6"] = 100, // Kontakt-1
        ["06a8b44f-1ecd-5a28-b2d7-c5ce90d58b3c"] = 200, // Kontakt-5
        ["1ca6af2e-ad50-5d1f-a279-707967b2b8ad"] = 201, // Kontakt-5 turret S
        ["c81b2bc5-5d43-5224-a69e-e00f408b3380"] = 202, // Kontakt-5 turret L
        ["2228fbbd-e161-5a05-9319-4c94922abf61"] = 203, // Kontakt-5 skirt
        ["7344ca78-e479-55e7-b11f-c23fb17f38b4"] = 204, // Kontakt-5 glacis
        ["6b4e1dc7-9020-5443-90c2-0f52c80933f9"] = 300, // Relikt panel (turret)
        ["c8bc7cd9-1e9a-5a53-a613-306846df488a"] = 301, // Relikt panel (hull)
        ["dd04b5c1-ed08-5406-bec5-de39cfe21814"] = 302, // Relikt side box
        // Nizh and Duplet block (0.20.0): the Nizh family stays together, Nizh-LM follows the panels.
        ["d706e182-a7c5-5a2a-9231-681480af74cc"] = 400, // Nizh panel
        ["ea9df1d4-18f5-5c8f-a6d0-d2c20cd3bc91"] = 401, // Duplet panel (turret)
        ["3532720a-7150-51da-85a5-00d94530dd46"] = 402, // Duplet panel (hull)
        ["ac5d72a9-cbfc-5fa5-80da-061b75ea8c4c"] = 403, // Duplet side module
        ["0f0f3f44-3931-5d72-bd5c-5d2159b13dc6"] = 404, // Nizh-LM
        ["51733efc-4963-5f1a-a9a3-842484aee9e7"] = 600, // ARAT-I
        ["54e6f2dd-6ade-58ca-a7ee-f07ad79f583d"] = 601, // ARAT-II
        ["89a52df5-1f81-50ad-845f-69ed756ce1ad"] = 700, // BRAT
        ["ed56d261-d2b8-553a-b900-1d3040d8980b"] = 800, // ROMOR
        ["6adf6262-7687-57d0-bd11-ce1582ca783d"] = 900, // DYNA
        [SprocketExpandedBallistics.CompositeModule.PartGuid] = 1100, // Passive composite module
        ["fe6c499f-6487-583b-ae1b-39064f14e344"] = 1000, // VARMA (passive)
    };

    internal static IEnumerable<T> Sort<T>(IEnumerable<T> items, Func<T, string?> guid, Func<T, string?> name)
        => items.OrderBy(item => guid(item) is {} id && Ranks.TryGetValue(id, out var rank) ? rank : int.MaxValue)
            .ThenBy(name, StringComparer.Ordinal); // LINQ preserves input order for equal names.
}
