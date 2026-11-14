namespace SprocketExpandedBallistics.Materials;

internal enum MaterialAvailabilityStatus { Available, UnknownVehicleContext, MissingTechnologyFrame, NotInTechnologyFrame }

internal static class MaterialAvailability
{
    // Calendar validation only. Feature availability is decided from the actual
    // owning vehicle by SprocketExpandedBallistics.VehicleEraAvailability.
    internal static bool HasValidCalendarDate(int year, int month, int day)
        => SprocketExpandedBallistics.VehicleEraAvailability.TryCalendarDate(year, month, day, out _);

    internal static bool TryCalendarDate(int year, int month, int day, out DateTime date)
        => SprocketExpandedBallistics.VehicleEraAvailability.TryCalendarDate(year, month, day, out date);

    internal static MaterialAvailabilityStatus Evaluate(bool hasKnownVehicleContext, bool hasTechnologyFrame, bool materialInFrame)
    {
        if (!hasKnownVehicleContext) return MaterialAvailabilityStatus.UnknownVehicleContext;
        if (!hasTechnologyFrame) return MaterialAvailabilityStatus.MissingTechnologyFrame;
        return materialInFrame ? MaterialAvailabilityStatus.Available : MaterialAvailabilityStatus.NotInTechnologyFrame;
    }

    internal static readonly Dictionary<string,string> CustomLabels=new(StringComparer.Ordinal);
    internal static string Label(string id, string fallback) => CustomLabels.TryGetValue(id,out var custom) ? custom : id switch
    {
        "ebWood" => "Wood",
        "ebRubber" => "Rubber",
        "ebFabric" => "Fabric (canvas)",
        "ebGlass" => "Glass (soda-lime)",
        "experimentalNizh34Insert" => "Nizh KhSChKV-34 active insert (36 mm; no housing)",
        "experimentalNizh19Insert" => "Nizh KhSChKV-19 active insert (26 mm; no housing)",
        "cwepHeavyEraCassette" => "Heavy ERA cassette (Kontakt-5-inspired; HEAT / APFSDS)",
        "cwepLightEraCassette" => "Light ERA cassette (HEAT; one use per cell)",
        "cwepNeraCassette" => "NERA sandwich cassette (legacy)",
        "cwepGlassTextolite" => "Glass / textolite laminate",
        "cwepPassiveComposite" => "Passive composite cassette (legacy)",
        "perforatedHhs" => "Perforated high-hardness steel",
        "quartzFiller" => "Quartz / sand filler",
        "boronCarbide" => "Boron carbide ceramic",
        "duMesh" => "Depleted uranium mesh",
        "aluminaCeramic" => "Alumina ceramic (corundum)",
        "siliconCarbide" => "Silicon carbide ceramic",
        "ebCassette_t64k" => "T-64 combination K",
        "ebCassette_burlington" => "Chieftain / Challenger 1 Burlington",
        "ebCassette_leopard2a4" => "Leopard 2A4",
        "ebCassette_t72b" => "T-72B reflecting plates",
        "ebCassette_t80u" => "T-80U cellular cast armour",
        "ebCassette_m1a1ha" => "M1A1HA",
        "ebCassette_t72m1" => "T-72M1 quartz cavity",
        "ebCassette_nera" => "NERA cassette",
        "ebCassette_passive" => "Passive composite",
        "ebCassette_t72a_hull" => "T-72A glacis",
        "ebCassette_t72b_hull" => "T-72B glacis",
        "ebCassette_t80u_hull" => "T-80U glacis",
        "ebCassette_leopard2a4_hull" => "Leopard 2A4 hull",
        "ebCassette_m1a1_hull" => "M1A1 hull",
        "ebCassette_challenger1_hull" => "Challenger 1 hull",
        "ebCassette_t64a" => "T-64A turret (corundum balls)",
        "ebCassette_t64b" => "T-64B turret (corundum balls)",
        "ebCassette_t64a_hull" => "T-64A glacis",
        "ebCassette_t90" => "T-90 turret (reflecting plates)",
        "ebCassette_t90_hull" => "T-90 glacis",
        "ebCassette_t90a" => "T-90A welded turret",
        "ebCassette_t90m" => "T-90M turret (base array)",
        "ebCassette_oplot" => "Oplot (T-84) turret",
        "ebCassette_oplot_hull" => "Oplot (T-84) glacis",
        "ebCassette_oplotm" => "Oplot-M turret (base array)",
        "ebCassette_leopard2a5_wedge" => "Leopard 2A5 / 2A6 turret wedge (add-on)",
        "ebCassette_leopard2a7_wedge" => "Leopard 2A7 turret wedge (add-on)",
        "ebCassette_leclerc" => "Leclerc turret",
        "ebCassette_merkava4" => "Merkava 4 turret (modular spaced)",
        "ebCassette_challenger2" => "Challenger 2 turret (Dorchester)",
        "ebCassette_gen_corundum_light" => "Light corundum block (generic)",
        "ebCassette_gen_ti_sic_block" => "Titanium / SiC block (generic)",
        "ebCassette_gen_heavy_wedge" => "Heavy spaced wedge (generic)",
        "ebCassette_gen_sic_block" => "SiC modular block (generic)",
        "ebCassette_gen_du_array" => "DU-mesh heavy array (generic)",
        _ => fallback
    };
}

