using System.Numerics;
namespace SprocketExpandedBallistics;

// 0.22.12 acceptance: the cassette pays at its first entry face (front/back from
// the shot direction, not a later internal block), the report no longer merges
// plate blocks across a component, and a cassette row shows its own compact text.
internal static class Fix02212Tests
{
    internal static void Run(Action<bool,string> check)
    {
        
        Consolidation(check);
        CassetteText(check);
    }

    private static CombatLayerInfo Layer(string id,params CombatLayerPart[] parts)=>new(id,parts,parts.Length>1);
    private static CombatReportShot Shot(params CombatReportEvent[] events)=>new(1,"Outgoing","APDS (tungsten, late)",1265,events,0,529.94);

    private static void Consolidation(Action<bool,string> check)
    {
        var plate=new CombatLayerPart(276,"t84 turret oscilating","rha","Rolled Homogeneous Armour",100,12.4,true);
        var thinner=new CombatLayerPart(276,"t84 turret oscilating","rha","Rolled Homogeneous Armour",50,13.5,true);
        var cannon=new CombatLayerPart(330,"cannon",null,null,null,null,false);
        // Shot #2: 276 (100 mm), cannon (163 mm), 276 (50 mm). The cannon must
        // break the merge and stay visible as its own measured row.
        var isolated=Shot(
            new("layerStart","","Reached layer",414,null,"A",Layer("A",plate)),
            new("plateResidual","","Traversed t84 turret oscilating",414,331,"A"),
            new("layerStart","","Reached layer",331,null,"B",Layer("B",cannon)),
            new("componentResidual","","Traversed cannon",331,168,"B"),
            new("layerStart","","Reached layer",168,null,"C",Layer("C",thinner)),
            new("plateResidual","","Traversed t84 turret oscilating",168,125,"C"));
        check(CombatReportView.Build(new[]{isolated})[0].Layers.Length==3,"0.22.12: shot #2 pattern gives three rows");
        var rows=CombatReportView.Build(new[]{isolated})[0].Layers;
        check(rows[0].Thickness=="100 mm"&&rows[0].BeforeAfter=="414 → 331","0.22.12: the first plate wall stays 100 mm");
        check(rows[1].Name=="main gun"&&rows[1].BeforeAfter=="331 → 168","0.22.12: the cannon passage keeps its own row and 163 mm (0.22.41: named main gun)");
        check(rows[2].Thickness=="50 mm"&&rows[2].BeforeAfter=="168 → 125","0.22.12: the second wall stays 50 mm");

        // Two directly consecutive passes of one plate still merge, and the
        // thickness shows both walls instead of only the first.
        var adjacent=Shot(
            new("layerStart","","Reached layer",414,null,"A",Layer("A",plate)),
            new("plateResidual","","Traversed t84 turret oscilating",414,331,"A"),
            new("layerStart","","Reached layer",331,null,"B",Layer("B",thinner)),
            new("plateResidual","","Traversed t84 turret oscilating",331,281,"B"));
        var merged=CombatReportView.Build(new[]{adjacent})[0].Layers;
        check(merged.Length==1&&merged[0].Thickness=="100 + 50 mm"&&merged[0].BeforeAfter=="414 → 281","0.22.12: adjacent walls merge with '100 + 50 mm'");
    }

    private static void CassetteText(Action<bool,string> check)
    {
        var cassettePart=new CombatLayerPart(296,"Composite armour plate","ebCassette_nera","NERA - NERA cassette",133,36,true);
        var shot=Shot(
            new("layerStart","","Reached layer",467,null,"A",Layer("A",cassettePart)),
            new("materialReaction","","Cassette NERA cassette: APDS −9,4 % (NERA ×0.5); back, t = 99,6 mm, θ = 12,7°",467,423,"A",RelatedEntity:296));
        var view=CombatHitProjection.Build(CombatReportTruth.Normalize(shot)).Single();
        check(view.Reaction=="Cassette NERA: APDS −44 mm (×0.5), back 99,6 mm @12,7°","0.22.12: the cassette row uses its own compact text: "+view.Reaction);
        check(!view.Reaction.Contains("Backing confirmed",StringComparison.Ordinal),"0.22.12: the sandwich text is not used for a cassette");
        var history=new CombatReportHistory();history.Restore(new[]{shot},0);
        var html=CombatReportHtml.Render(history);
        check(html.Contains(System.Net.WebUtility.HtmlEncode("Composite NERA: APDS −44 mm (×0.5), back 99,6 mm @12,7°")),"0.22.12: the HTML report shows the same cassette text (0.22.41: Composite)");
        check(!html.Contains("Backing confirmed",StringComparison.Ordinal),"0.22.12: the HTML report does not show the sandwich text for a cassette");
    }
}
