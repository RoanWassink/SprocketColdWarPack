namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.11: a shot that did internal damage never reads STOPPED. One shared rule (<see cref="HitResultRule"/>) feeds the hit panel,
/// the Kit combat report and the HTML export, which all take the result word from <see cref="HitBlockBuilder.Build"/>.
/// </summary>
internal static class HitResultRuleTests
{
    private const string Cyan="#79D8E8",Red="#F18E8E",Grey="#A5ADB5",Purple="#C8A3F3";

    private static CombatLayerInfo Layer(string id,int entity,string name,double thickness,double angle)=>new(id,new[]{new CombatLayerPart(entity,name,null,null,thickness,angle,true)},false);
    private static CombatReportEvent Hit(string text,int entity)=>new("interaction","",text,RelatedEntity:entity);
    private static CombatReportShot Shot(int number,params CombatReportEvent[] events)=>new(number,"Incoming","APFSDS 105 mm M735",700,events,0,318,"T-64",4,4);
    /// <summary>The armour part of a shot that stopped in plate #2 (native stop, nothing measured behind it).</summary>
    private static CombatReportEvent[] Stop(params CombatReportEvent[] after)=>new[]
    {
        new CombatReportEvent("layerStart","","Reached layer",318,null,"b",Layer("b",2,"Plate #2",120,0)),
        new CombatReportEvent("nativeStop","","Native stop in Plate #2",318,null,"b")
    }.Concat(after).ToArray();
    private static string E(string text)=>System.Net.WebUtility.HtmlEncode(text);
    private static string Html(CombatReportShot shot)
    {
        var history=new CombatReportHistory();history.Restore(new[]{shot},0);
        return CombatReportHtml.Render(history);
    }

    internal static void Run(Action<bool,string> check)
    {
        // ---- stop with internal damage: PENETRATED (hit panel, Kit report, HTML) ----
        var engine=Shot(31,Stop(Hit("Crew member Gunner killed",1),Hit("Engine destroyed",5)));
        var block=HitBlockBuilder.Build(engine);
        check(block.Result==HitBlockBuilder.StoppedSpall,"hit result: a stop that wrecked the engine and killed the gunner reads PENETRATED ("+block.Result+")");
        check(block.Colour==Red,"hit result: the colour is the penetration colour, not the stop red ("+block.Colour+")");
        check(block.Values=="318 → 0 mm"&&block.Detail.Contains("stopped in Plate #2"),"hit result: the measured numbers and the stop position stay factual ("+block.Values+" | "+block.Detail+")");
        check(block.DamageShort.StartsWith("→ ",StringComparison.Ordinal),"hit result: the damage still shows on the hit panel");
        var view=CombatReportView.Build(new[]{engine})[0];
        check(view.Block.Result==HitBlockBuilder.StoppedSpall&&view.Head.EndsWith(" · STOPPED · spall damage",StringComparison.Ordinal),"hit result: the Kit combat report header says PENETRATED ("+view.Head+")");
        var html=Html(engine);
        check(html.Contains(E(" · STOPPED · spall damage")+"</h2>",StringComparison.Ordinal)&&!html.Contains("PENETRATED",StringComparison.Ordinal),"hit result: the HTML export says PENETRATED and never STOPPED");
        check(HitBlockBuilder.Build(Shot(32,Stop(Hit("Gun damaged",6)))).Result==HitBlockBuilder.StoppedSpall&&HitBlockBuilder.Build(Shot(33,Stop(Hit("fuelTank damaged",7)))).Result==HitBlockBuilder.StoppedSpall,"hit result: any damaged component is enough, crew or not");
        check(HitBlockBuilder.Build(Shot(34,Stop(Hit("Crew member Loader wounded",2)))).Result==HitBlockBuilder.StoppedSpall,"hit result: a wounded crew member is enough");

        // ---- armour-only stop: unchanged ----
        var plain=Shot(35,Stop());
        block=HitBlockBuilder.Build(plain);
        check(block.Result==HitBlockBuilder.Stopped&&block.Colour==Red&&block.Values=="318 → 0 mm","hit result: a stop in armour with no damage stays STOPPED, red ("+block.Result+" "+block.Colour+")");
        check(CombatReportView.Build(new[]{plain})[0].Head.EndsWith(" · STOPPED",StringComparison.Ordinal)&&Html(plain).Contains(E(" · STOPPED")+"</h2>",StringComparison.Ordinal),"hit result: Kit report and HTML keep STOPPED for an armour-only stop");
        check(HitBlockBuilder.Build(Shot(36,Stop(Hit("Something unrelated",9)))).Result==HitBlockBuilder.Stopped,"hit result: an interaction that is not damage does not change the result");
        check(!HitResultRule.InternalDamage(Stop())&&!HitResultRule.InternalDamage(Array.Empty<CombatReportEvent>())&&HitResultRule.InternalDamage(Stop(Hit("Engine damaged",5))),"hit result: InternalDamage reads only the damage events");

        // ---- destroyed tank: PENETRATED word with the TANK DESTROYED label, never STOPPED ----
        var killed=Shot(38,Stop(Hit("Crew member Crew member #1 killed",1),Hit("Crew member Crew member #2 killed",2),Hit("Crew member Crew member #3 killed",3),Hit("Crew member Crew member #4 killed",4)))with{CrewAlive=0};
        var dead=CombatReportView.Build(new[]{killed})[0];
        check(dead.Destroyed&&dead.Block.Result==HitBlockBuilder.StoppedSpall&&dead.Head.EndsWith(" · STOPPED · spall damage · TANK DESTROYED",StringComparison.Ordinal),"hit result: a destroyed tank closes the header with TANK DESTROYED ("+dead.Head+")");
        check(Html(killed).Contains("<strong class='kill'>TANK DESTROYED</strong>",StringComparison.Ordinal),"hit result: the HTML heading shows TANK DESTROYED and no STOPPED");
        var noEvents=Shot(39,Stop())with{CrewAlive=0};
        check(CombatDamageSummary.IsDestroyed(noEvents)&&HitBlockBuilder.Build(noEvents).Result==HitBlockBuilder.StoppedSpall,"hit result: destroyed by the crew counts alone still never reads STOPPED");
        var native=Shot(40,Stop(new CombatReportEvent("vehicleDestroyed","","Native kill")));
        check(HitBlockBuilder.Build(native).Result==HitBlockBuilder.StoppedSpall&&HitResultRule.Resolve(HitBlockBuilder.Stopped,Array.Empty<CombatReportEvent>(),true)==HitBlockBuilder.StoppedSpall,"hit result: a native vehicle-destroyed event counts as internal damage");

        // ---- ricochet: unchanged ----
        var glance=new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",190,null,"d",Layer("d",5,"Plate #5",100,68)),
            new CombatReportEvent("outcome","","Deflected at Plate #5",HitId:"d")
        };
        block=HitBlockBuilder.Build(Shot(41,glance));
        check(block.Result==HitBlockBuilder.Ricochet&&block.Colour==Grey,"hit result: a ricochet stays RICOCHET, grey");
        var glanceHurt=Shot(42,glance.Append(Hit("Crew member Loader wounded",2)).ToArray())with{CrewAlive=0};
        check(HitBlockBuilder.Build(glanceHurt).Result==HitBlockBuilder.Ricochet&&HitResultRule.Resolve(HitBlockBuilder.Ricochet,glanceHurt.Events,true)==HitBlockBuilder.Ricochet,"hit result: the rule never turns a ricochet into a penetration");
        check(HitResultRule.Resolve(HitBlockBuilder.Penetrated,Array.Empty<CombatReportEvent>())==HitBlockBuilder.Penetrated&&HitResultRule.Resolve(HitBlockBuilder.NoArmour,Array.Empty<CombatReportEvent>())==HitBlockBuilder.NoArmour&&HitResultRule.Resolve(HitBlockBuilder.Unknown,Array.Empty<CombatReportEvent>())==HitBlockBuilder.Unknown,"hit result: only STOPPED is ever replaced");

        // ---- the colour follows PENETRATED, except where ERA or a material reaction already outranks the stop ----
        var material=Shot(43,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",400,null,"c",Layer("c",2,"Plate #2",120,0)),
            new CombatReportEvent("materialReaction","","textolite: backing confirmed",300,184,"c"),
            new CombatReportEvent("plateResidual","","Traversed",400,0,"c"),
            new CombatReportEvent("nativeStop","","Native stop",0,null,"c"),
            Hit("Engine damaged",5)
        });
        block=HitBlockBuilder.Build(material);
        check(block.Result==HitBlockBuilder.StoppedSpall&&block.Colour==Purple,"hit result: a material reaction keeps its colour on a promoted stop ("+block.Colour+")");
        check(HitResultRule.PenetratedColour(Stop())==Cyan&&HitResultRule.PenetratedColour(Array.Empty<CombatReportEvent>())==Cyan,"hit result: with nothing outranking it, a promoted stop is cyan");
        Console.WriteLine("0.22.11 hit result checks PASS");
    }
}
