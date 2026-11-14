namespace SprocketExpandedBallistics;

internal static class EraDisplayNameTests
{
    internal static void Run(Action<bool,string> check)
    {
        check(EraDisplayName.Component("dupletPanelTurretStage2Cell")=="Duplet panel (turret) · layer 2","ERA stage cell uses the menu family name and layer");
        check(EraDisplayName.Component("dupletPanelTurretStage1Cell")=="Duplet panel (turret) · layer 1","ERA first stage gets layer 1");
        check(EraDisplayName.Component("dupletPanelTurretHousing")=="Duplet panel (turret)","ERA housing uses the family name without a layer");
        check(EraDisplayName.Component("reliktPanelHullCell")=="Relikt panel (hull) · layer 1","Relikt panel name and layer");
        check(EraDisplayName.Component("kontakt5Stage1Cell2")=="Kontakt-5","generated menu label is reused");
        check(EraDisplayName.Component("someCamelCaseId")=="Some camel case id","unknown ids fall back to camelCase words");
        check(EraDisplayName.Component("dupletPanelTurretStage2Cell")!=EraDisplayName.Component("dupletPanelHullStage2Cell"),"different ERA families keep different names");
        check(EraDisplayName.Material("dupletHullActive")=="Duplet hull active","material fallback is readable");
        check(EraDisplayName.Material("ebCassette_t80u")=="T-80U cellular cast armour","known cassette material label is retained");
        foreach(var guid in SprocketExpandedBallistics.Materials.EraMenuOrder.Ranks.Keys)
        {
            var preset=SprocketExpandedBallistics.Materials.EraPresetRegistry.Presets.FirstOrDefault(p=>p.PartGuid==guid);
            check(preset!=null,"every ranked ERA guid has a preset");
            if(preset==null)continue;
            var name=EraDisplayName.Component(preset.Cassette.Length>0?preset.Cassette:preset.Mount);
            check(name.Length>0&&name!=guid&&!name.Contains(guid,StringComparison.Ordinal),"ranked ERA menu entry has a readable name: "+guid);
            if(SprocketExpandedBallistics.Materials.EraGeneratedDefinitions.MenuLabels.TryGetValue(guid,out var menu))
                check(name==menu,"generated menu label is preferred: "+guid);
        }
    }
}
