namespace SprocketExpandedBallistics;
internal static class CombatReportPresentation
{
    internal static string Escape(string text)=>text.Replace("<","\u2039").Replace(">","\u203A");
    internal static string Colour(string colour,string text)=>"<color="+colour+">"+Escape(text)+"</color>";
    internal static (string Label,string Colour) Category(CombatReportEvent e)=>e.Kind switch
    {
        "plateResidual"=>("Armour passed","#79D8E8"),"componentResidual"=>("Component passed","#A5ADB5"),
        "blockMembers"=>("Grouped objects - membership only","#A5ADB5"),"nativeStop"=>("Stopped - native simulation","#F18E8E"),
        "materialStop"=>("Stopped - material response","#C8A3F3"),"eraReaction" or "eraResidual" or "lightEra" or "heavyEra"=>("ERA reaction","#F3C66A"),
        "materialReaction"=>("Material reaction","#C8A3F3"),"coreState"=>("Residual APFSDS core","#79D8E8"),
        "rodOrientation"=>("Rod yaw / bending","#C8A3F3"),"materialPending"=>("Material backing check","#C8A3F3"),
        "materialUnavailable"=>("Material response unavailable","#F3C66A"),"fragmentAbsorbed"=>("Fragment stopped","#C8A3F3"),
        "outcome" when e.Text.StartsWith("Stopped",StringComparison.OrdinalIgnoreCase)=>("Shot stopped","#F18E8E"),
        "outcome" when e.Text.StartsWith("Deflected",StringComparison.OrdinalIgnoreCase)=>("Shot deflected","#A5ADB5"),
        "interaction" when e.Text.Contains("destroyed",StringComparison.OrdinalIgnoreCase)||e.Text.Contains("killed",StringComparison.OrdinalIgnoreCase)=>("Damage","#F18E8E"),
        _=>("Interaction","#A5ADB5")
    };
}
