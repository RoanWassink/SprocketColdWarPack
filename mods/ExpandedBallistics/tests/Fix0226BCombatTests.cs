using System.Text.Json;
namespace SprocketExpandedBallistics;

/// <summary>0.22.6B items 3 to 5: target vehicle in the impact head, readable damage and TANK DESTROYED, the doubled plate in the layer table.</summary>
internal static class Fix0226BCombatTests
{
    private static CombatReportEvent Interaction(string text,int? entity=null)=>new("interaction","",text,RelatedEntity:entity);
    private static readonly CombatLayerPart Plate27=new(27,"Plate #27","rha","Steel (RHA)",85,62,true);
    private static CombatLayerInfo Layer(string id,params CombatLayerPart[] parts)=>new(id,parts,parts.Length>1);

    /// <summary>The screenshot: plate 27 twice (550 → 397, then 397 → unknown), then the engine (63 → 0).</summary>
    private static CombatReportShot Screenshot(string? target=null,int? crewTotal=null,int? crewAlive=null,params CombatReportEvent[] extra)
    {
        var engine=new CombatLayerPart(41,"Engine",null,null,null,null,false);
        var events=new List<CombatReportEvent>
        {
            new("layerStart","","Reached layer",550,null,"A",Layer("A",Plate27)),
            new("plateResidual","","Traversed Plate #27",550,397,"A"),
            new("layerStart","","Reached layer",397,null,"B",Layer("B",Plate27)),
            new("plateResidual","","Traversed Plate #27",397,null,"B"),
            new("layerStart","","Reached layer",63,null,"C",Layer("C",engine)),
            new("nativeStop","","Native stop in Engine; no outgoing original core recorded.",63,0,"C")
        };
        events.AddRange(extra);
        return new CombatReportShot(7,"Outgoing","APFSDS 120 mm",1500,events,0,550,target,crewTotal,crewAlive);
    }

    internal static void Run(Action<bool,string> check)
    {
        // ---- item 5: the same plate registered twice becomes one layer, and an exit is never left "?" when the next layer shows it ----
        var shot=Screenshot("T-72A");
        var views=CombatHitProjection.Build(CombatReportTruth.Normalize(shot));
        check(views.Length==2,"fix0226b: plate 27 reached twice in a row is one layer, plus the engine ("+views.Length+" layers)");
        var impact=CombatReportView.Build(new[]{shot})[0];
        check(impact.Layers.Length==2,"fix0226b: the layer table has two rows, not three");
        var plateRow=impact.Layers[0];
        check(plateRow.Name=="Plate #27"&&plateRow.Thickness=="85 mm"&&plateRow.Angle=="62°"&&plateRow.BeforeAfter=="550 → 63","fix0226b: merged plate row reads 550 → 63 ("+plateRow.BeforeAfter+")");
        check(impact.Layers[1].Name=="Engine"&&impact.Layers[1].BeforeAfter=="63 → 0 (stopped)"&&impact.Layers.All(r=>!r.BeforeAfter.Contains('?')),"fix0226b: the engine row stays 63 → 0 and no row shows ?");
        check(impact.Layers[0].Index=="01"&&impact.Layers[1].Index=="02","fix0226b: rows are renumbered after the merge");
        check(HitBlockBuilder.Build(shot).Breakdown.Count(l=>l.Contains("Plate #27",StringComparison.Ordinal))==1,"fix0226b: the hit panel breakdown lists the plate once");
        check(views[0].Details.Count(e=>e.Kind=="plateResidual")==2,"fix0226b: both residual events stay in the merged layer's details");

        // the second pass has a measured exit: that exit wins
        var known=shot with{Events=shot.Events.Select(e=>e.HitId=="B"&&e.Kind=="plateResidual"?e with{AfterMm=120}:e).ToArray()};
        check(CombatReportView.Build(new[]{known})[0].Layers[0].BeforeAfter=="550 → 120","fix0226b: a measured exit of the second pass is used as the merged exit");
        // no next layer to borrow from: "?" only when really unknown; an earlier measured exit is the last resort
        var lastOnly=new CombatReportShot(1,"Incoming","HEAT",700,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",400,null,"A",Layer("A",Plate27)),
            new CombatReportEvent("plateResidual","","Traversed Plate #27",400,null,"A")
        },0,400);
        check(CombatReportView.Build(new[]{lastOnly})[0].Layers.Single().BeforeAfter=="400 → ?","fix0226b: an exit nothing measured stays ?");
        var lastKnown=new CombatReportShot(1,"Incoming","HEAT",700,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",400,null,"A",Layer("A",Plate27)),new CombatReportEvent("plateResidual","","Traversed Plate #27",400,300,"A"),
            new CombatReportEvent("layerStart","","Reached layer",300,null,"B",Layer("B",Plate27)),new CombatReportEvent("plateResidual","","Traversed Plate #27",300,null,"B")
        },0,400);
        check(CombatReportView.Build(new[]{lastKnown})[0].Layers.Single().BeforeAfter=="400 → 300","fix0226b: with nothing later, the earlier measured exit is kept");
        // a different plate in between, or another stage, is never merged
        var other=new CombatLayerPart(5,"Plate #5","rha","Steel (RHA)",30,0,true);
        var interleaved=new CombatReportShot(2,"Incoming","APFSDS",1500,new[]
        {
            new CombatReportEvent("layerStart","","Reached layer",500,null,"A",Layer("A",Plate27)),new CombatReportEvent("plateResidual","","Traversed Plate #27",500,400,"A"),
            new CombatReportEvent("layerStart","","Reached layer",400,null,"B",Layer("B",other)),new CombatReportEvent("plateResidual","","Traversed Plate #5",400,null,"B"),
            new CombatReportEvent("layerStart","","Reached layer",300,null,"C",Layer("C",Plate27)),new CombatReportEvent("plateResidual","","Traversed Plate #27",300,250,"C")
        },0,500);
        var rows=CombatReportView.Build(new[]{interleaved})[0].Layers;
        check(rows.Length==3&&rows[1].BeforeAfter=="400 → 300"&&rows[2].BeforeAfter=="300 → 250","fix0226b: non-consecutive plates stay separate and a missing exit comes from the next entry");
        var staged=new CombatReportShot(3,"Incoming","Tandem",700,new[]
        {
            new CombatReportEvent("layerStart","Precursor","Reached layer",300,null,"P",Layer("P",Plate27)),new CombatReportEvent("plateResidual","Precursor","Traversed",300,100,"P"),
            new CombatReportEvent("layerStart","","Reached layer",600,null,"M",Layer("M",Plate27)),new CombatReportEvent("plateResidual","","Traversed",600,450,"M")
        },0,600);
        check(CombatReportView.Build(new[]{staged})[0].Layers.Length==2,"fix0226b: precursor and main charge hits of one plate are not merged");

        // ---- item 3: the blueprint name of the target in the impact head ----
        check(impact.Target=="T-72A","fix0226b: impact head carries the target blueprint name");
        check(CombatReportView.Build(new[]{Screenshot(null)})[0].Target=="","fix0226b: unknown target is empty, not a placeholder");
        check(CombatReportView.Build(new[]{Screenshot("<b>Tank</b> "+new string('x',80))})[0].Target.Length<=48&&!CombatReportView.Build(new[]{Screenshot("<b>Tank</b>")})[0].Target.Contains('<'),"fix0226b: target name is escaped and fitted");
        var json=JsonSerializer.Serialize(new[]{Screenshot("T-72A",4,0)});
        var back=JsonSerializer.Deserialize<CombatReportShot[]>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        check(back[0].Target=="T-72A"&&back[0].CrewTotal==4&&back[0].CrewAlive==0&&back[0].Events.Count==shot.Events.Count,"fix0226b: target and crew survive the saved JSON");
        var oldJson="{\"number\":4,\"direction\":\"Incoming\",\"profile\":\"HE\",\"speedMps\":300,\"events\":[],\"omittedEvents\":0,\"initialPenetrationMm\":50}";
        var old=JsonSerializer.Deserialize<CombatReportShot>(oldJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        check(old.Target==null&&old.CrewTotal==null&&old.CrewAlive==null&&!CombatDamageSummary.IsDestroyed(old),"fix0226b: a saved report from before 0.22.6B still loads");
        var clean=CombatReportTruth.Normalize(Screenshot("  T-72A\u0007  ",3,9));
        check(clean.Target=="T-72A"&&clean.CrewTotal==3&&clean.CrewAlive==null,"fix0226b: normalize cleans the name and drops inconsistent crew counts");

        // ---- item 4: readable damage ----
        var doubled=CombatDamageSummary.From(new[]
        {
            Interaction("Crew member Crew member #12 killed",12),Interaction("Crew member Crew member #13 killed",13),Interaction("Crew member Crew member #14 killed",14),
            Interaction("Crew member Crew member #15 wounded",15),Interaction("Engine damaged",90)
        });
        check(doubled.CrewKilled==3&&doubled.CrewWounded==1&&doubled.KilledRoles.Count==0,"fix0226b: anonymous crew are counted, not named");
        check(doubled.Text()=="3 crew killed, 1 wounded · engine damaged","fix0226b: doubled 'Crew member Crew member' becomes a count ("+doubled.Text()+")");
        check(!doubled.Text().Contains("Crew member",StringComparison.Ordinal),"fix0226b: no 'Crew member' repeated in the damage text");
        var roles=CombatDamageSummary.From(new[]
        {
            Interaction("Crew member Gunner killed",1),Interaction("Crew member Loader killed",2),Interaction("Crew member Driver killed",3),Interaction("Crew member Commander wounded",4),
            Interaction("Engine destroyed",5),Interaction("Gun damaged",6),Interaction("fuelTank damaged",7)
        });
        check(roles.Text()=="3 crew killed (gunner, loader, driver), 1 wounded (commander) · engine destroyed, gun damaged, fuel tank damaged","fix0226b: roles are named when the seats have names ("+roles.Text()+")");
        var mixed=CombatDamageSummary.From(new[]{Interaction("Crew member Gunner killed",1),Interaction("Crew member #9 killed",9)});
        check(mixed.Text()=="2 crew killed (gunner, 1 unnamed)","fix0226b: named and unnamed crew mix ("+mixed.Text()+")");
        var upgrade=CombatDamageSummary.From(new[]{Interaction("Crew member Loader wounded",2),Interaction("Crew member Loader killed",2),Interaction("Engine damaged",5),Interaction("Engine destroyed",5)});
        check(upgrade.CrewKilled==1&&upgrade.CrewWounded==0&&upgrade.PartsDestroyed.Count==1&&upgrade.PartsDamaged.Count==0,"fix0226b: wounded then killed counts once; damaged then destroyed shows destroyed");
        var twoLoaders=CombatDamageSummary.From(new[]{Interaction("Crew member Loader wounded",2),Interaction("Crew member Loader killed",3)});
        check(twoLoaders.CrewKilled==1&&twoLoaders.CrewWounded==1,"fix0226b: two seats with the same name stay two people when the events carry their ids");
        var many=CombatDamageSummary.From(Enumerable.Range(1,9).Select(i=>Interaction($"Part{i} destroyed",i)));
        check(many.PartsText(6).EndsWith("+3 more",StringComparison.Ordinal),"fix0226b: long part lists are summarised");
        check(!CombatDamageSummary.From(new[]{Interaction("Something unrelated"),new CombatReportEvent("plateResidual","","Traversed")}).Any,"fix0226b: other events are not damage");
        var longRoles=new[]{"Gunner","Loader","Driver","Commander","Radio operator","Bow gunner"}.Select((r,i)=>Interaction($"Crew member {r} killed",i+1)).ToArray();
        var tall=CombatReportView.Build(new[]{Screenshot("T-72A",6,0,longRoles)})[0];
        check(tall.SummaryLine.Length>60&&!tall.SummaryLine.Contains('…')&&tall.SummaryLine.StartsWith("Crew 6/6 killed (gunner, loader, driver, commander, radio operator, bow gunner)",StringComparison.Ordinal),"fix0226b: the damage summary line is never cut off ("+tall.SummaryLine+")");
        check(tall.Layers.All(l=>l.Note.Length==0)&&!tall.SummaryLine.Contains("Crew member",StringComparison.Ordinal),"fix0226b: the damage of the last layer is only in the summary line, not repeated in the layer table (0.22.7)");

        // ---- item 4: long text becomes lines, because the installed kit 0.1.2 does not wrap a label made inside a template ----
        check(CombatReportView.Lines("a b c",20).SequenceEqual(new[]{"a b c"})&&CombatReportView.Lines("",20).Length==0,"fix0226b: short text is one line, empty text none");
        var sentence=string.Join(" ",Enumerable.Range(1,40).Select(i=>"word"+i));
        var wrapped=CombatReportView.Lines(sentence,50);
        check(wrapped.Length>1&&wrapped.All(l=>l.Length<=50)&&string.Join(" ",wrapped)==sentence,"fix0226b: lines are at most 50 characters and rejoin to the original ("+wrapped.Length+" lines)");
        check(CombatReportView.Lines(new string('x',130),50).SequenceEqual(new[]{new string('x',50),new string('x',50),new string('x',30)}),"fix0226b: a word longer than a line is cut, not lost");
        var capped=CombatReportView.Lines(string.Join(" ",Enumerable.Repeat("lorem ipsum",300)),40);
        check(capped.Length==CombatReportView.MaxLines&&capped[^1].EndsWith("…",StringComparison.Ordinal)&&capped.All(l=>l.Length<=40),"fix0226b: line count is capped and the cut is marked");
        check(tall.SummaryLines.Length>=1&&tall.SummaryLines[0].StartsWith("Crew 6/6 killed",StringComparison.Ordinal)&&tall.SummaryLines.All(l=>l.Length<=CombatReportView.LineColumns)&&string.Join(" ",tall.SummaryLines)==tall.SummaryLine,"fix0226b: the summary line is delivered as lines that rejoin to the whole text");
        check(CombatReportView.Build(new[]{Screenshot("T-72A",4,4)})[0].SummaryLines.Length==0,"fix0226b: no damage, no lines");

        // ---- item 4: TANK DESTROYED ----
        check(tall.Destroyed,"fix0226b: all crew dead marks the tank destroyed");
        check(!CombatReportView.Build(new[]{Screenshot("T-72A",4,1,longRoles.Take(3).ToArray())})[0].Destroyed,"fix0226b: a survivor means not destroyed");
        check(!CombatReportView.Build(new[]{Screenshot("T-72A",null,null,longRoles)})[0].Destroyed,"fix0226b: unknown crew totals never claim a kill");
        check(CombatDamageSummary.IsDestroyed(Screenshot(null,null,null,Interaction("Vehicle destroyed")))&&CombatDamageSummary.IsDestroyed(Screenshot(null,null,null,new CombatReportEvent("vehicleDestroyed","","Native kill"))),"fix0226b: a native vehicle-destroyed event also marks the kill");
        check(!CombatReportView.Build(new[]{Screenshot("T-72A",4,4)})[0].Destroyed&&CombatReportView.Build(new[]{Screenshot("T-72A",4,4)})[0].SummaryLine=="","fix0226b: an undamaged target has no damage line and no label");
        check(CombatReportView.Build(new[]{Screenshot("T-72A",5,2,Interaction("Crew member Gunner killed",1))})[0].SummaryLine=="Crew 1/5 killed (gunner)","fix0226b: the crew total is part of the phrase, with no dangling crew-alive tail (0.22.7)");
        Console.WriteLine("0.22.6B combat report checks PASS");
    }
}
