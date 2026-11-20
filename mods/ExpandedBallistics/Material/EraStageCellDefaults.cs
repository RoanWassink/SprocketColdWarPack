using SprocketExpandedBallistics.Era;

namespace SprocketExpandedBallistics.Materials;

// 0.22.96: built-in exact bindings for every cassette-classified preset row of EraGeneratedDefinitions that the
// shipped sprocket.expandedballistics.era.bindings.json does not carry (the stage cells of the physical ERA parts and
// the experimental cassettes/inserts). The route of each cell is the recipe its own native part declares: the
// durableMaterialID of the cell component in Sprocket_Data/StreamingAssets/Parts/<family>V1Part.json (e.g.
// kontakt1Stage1Cell1 -> kontakt1Active in kontakt1PhysicalV1Part.json), with the kind of that material in
// EraGeneratedDefinitions.MaterialRoutes. Same convention as 0.22.73 (EraWtVariants: recipe = the family *Active
// material) and the same routes as tests/physical-era-bindings.json. Like EraWtVariants this only fills gaps in the
// immutable in-memory catalogue: the config file in the game folder is user data, is never rewritten, and a binding
// the user config already holds always wins.
internal static class EraStageCellDefaults
{
    internal static IEnumerable<EraPartBinding> Bindings()
    {
        yield return new EraPartBinding("experimentalKontakt5Cassette","experimentalKontakt5Mount","experimentalKontakt5Cassette","experimentalKontakt5Cassette","heavyEra");
        yield return new EraPartBinding("experimentalReliktCassette","experimentalReliktMount","experimentalReliktCassette","experimentalReliktCassette","heavyEra");
        yield return new EraPartBinding("experimentalNizhCassette","experimentalNizhMount","experimentalNizhCassette","experimentalNizhCassette","heavyEra");
        yield return new EraPartBinding("experimentalNizh34Insert","experimentalNizh34Housing","experimentalNizh34Insert","experimentalNizh34Insert","heavyEra");
        yield return new EraPartBinding("experimentalNizh19Insert","experimentalNizh19Housing","experimentalNizh19Insert","experimentalNizh19Insert","heavyEra");
        yield return new EraPartBinding("experimentalDupletFrontCassette","experimentalDupletFrontMount","experimentalNizhCassette","experimentalNizhCassette","heavyEra");
        yield return new EraPartBinding("experimentalDupletRearCassette","experimentalDupletRearMount","experimentalNizhCassette","experimentalNizhCassette","heavyEra");
        yield return new EraPartBinding("kontakt1Stage1Cell1","kontakt1Housing","kontakt1Active","kontakt1Active","lightEra");
        yield return new EraPartBinding("kontakt5Stage1Cell1","kontakt5Housing","kontakt5Active","kontakt5Active","heavyEra");
        yield return new EraPartBinding("kontakt5Stage1Cell2","kontakt5Housing","kontakt5Active","kontakt5Active","heavyEra");
        yield return new EraPartBinding("reliktStage1Cell1","reliktHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktStage1Cell2","reliktHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurretStage1Cell1","reliktTurretHousing","reliktTurretActive","reliktTurretActive","heavyEra");
        yield return new EraPartBinding("reliktTurretStage1Cell2","reliktTurretHousing","reliktTurretActive","reliktTurretActive","heavyEra");
        yield return new EraPartBinding("nizhLegacyStage1Cell1","nizhLegacyHousing","nizhLegacyActive","nizhLegacyActive","heavyEra");
        yield return new EraPartBinding("dupletLegacySingleStage1Cell1","dupletLegacySingleHousing","dupletLegacySingleActive","dupletLegacySingleActive","heavyEra");
        yield return new EraPartBinding("dupletLegacyIntegratedStage1Cell1","dupletLegacyIntegratedHousing","dupletLegacyIntegratedActive","dupletLegacyIntegratedActive","heavyEra");
        yield return new EraPartBinding("dupletLegacyIntegratedStage2Cell1","dupletLegacyIntegratedHousing","dupletLegacyIntegratedActive","dupletLegacyIntegratedActive","heavyEra");
        yield return new EraPartBinding("dupletHullStage1Cell1","dupletHullHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletHullStage2Cell1","dupletHullHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletModularStage1Cell","dupletModularHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletModularStage2Cell","dupletModularHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletModularSlimStage1Cell","dupletModularSlimHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletModularSlimStage2Cell","dupletModularSlimHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("reliktPanelTurretCell","reliktPanelTurretHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktPanelHullCell","reliktPanelHullHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktSideBoxCell","reliktSideBoxHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("nizhPanelCell","nizhPanelHousing","nizh34Active","nizh34Active","heavyEra");
        yield return new EraPartBinding("dupletPanelTurretStage1Cell","dupletPanelTurretHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletPanelTurretStage2Cell","dupletPanelTurretHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletPanelHullStage1Cell","dupletPanelHullHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletPanelHullStage2Cell","dupletPanelHullHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletSideModuleStage1Cell","dupletSideModuleHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletSideModuleStage2Cell","dupletSideModuleHousing","dupletHullActive","dupletHullActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage1Cell1","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage1Cell2","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage1Cell3","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage2Cell1","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage2Cell2","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("dupletHullZonesStage2Cell3","dupletHullZonesHousing","dupletHullZonesActive","dupletHullZonesActive","heavyEra");
        yield return new EraPartBinding("nizh34Stage1Cell1","nizh34Housing","nizh34Active","nizh34Active","heavyEra");
        yield return new EraPartBinding("nizh19Stage1Cell1","nizh19Housing","nizh19Active","nizh19Active","heavyEra");
        yield return new EraPartBinding("nizh1MStage1Cell1","nizh1MHousing","nizh1MActive","nizh1MActive","heavyEra");
        yield return new EraPartBinding("duplet2MStage1Cell1","duplet2MHousing","duplet2MActive","duplet2MActive","heavyEra");
        yield return new EraPartBinding("duplet2MStage2Cell1","duplet2MHousing","duplet2MActive","duplet2MActive","heavyEra");
        yield return new EraPartBinding("duplet2MHullStage1Cell1","duplet2MHullHousing","duplet2MHullActive","duplet2MHullActive","heavyEra");
        yield return new EraPartBinding("duplet2MHullStage2Cell1","duplet2MHullHousing","duplet2MHullActive","duplet2MHullActive","heavyEra");
        yield return new EraPartBinding("nizhLmInsert","nizhLmHousing","nizhLmInsert","nizhLmInsert","lightEra");
        yield return new EraPartBinding("reliktCompactStage1Cell1","reliktCompactHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktCompactStage1Cell2","reliktCompactHousing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktHull22Stage1Cell1","reliktHull22Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktHull22Stage1Cell2","reliktHull22Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktHull22Stage1Cell3","reliktHull22Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktHull22Stage1Cell4","reliktHull22Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurretCompact12Stage1Cell1","reliktTurretCompact12Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurretCompact12Stage1Cell2","reliktTurretCompact12Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurretCompact11Stage1Cell1","reliktTurretCompact11Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurret2Plus1Stage1Cell1","reliktTurret2Plus1Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurret2Plus1Stage1Cell2","reliktTurret2Plus1Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("reliktTurret2Plus1Stage1Cell3","reliktTurret2Plus1Housing","reliktActive","reliktActive","heavyEra");
        yield return new EraPartBinding("aratIStage1Cell1","aratIHousing","aratIActive","aratIActive","lightEra");
        yield return new EraPartBinding("aratIIStage1Cell1","aratIIHousing","aratIIActive","aratIIActive","lightEra");
        yield return new EraPartBinding("dynaStage1Cell1","dynaHousing","dynaActive","dynaActive","heavyEra");
        yield return new EraPartBinding("romorAStage1Cell1","romorAHousing","romorAActive","romorAActive","lightEra");
        yield return new EraPartBinding("bratStage1Cell1","bratHousing","bratActive","bratActive","lightEra");
    }
    internal static EraPartBindingCatalogue With(EraPartBindingCatalogue catalogue)
    {
        var missing=Bindings().Where(b=>!catalogue.TryGetCassette(b.CassetteComponentId,out _)).ToList();
        return missing.Count==0?catalogue:new EraPartBindingCatalogue(catalogue.Bindings.Concat(missing).ToList());
    }
}
