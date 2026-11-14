using System.Globalization;
using System.Text.Json.Nodes;
using SprocketJsonEditor;
namespace SprocketExpandedBallistics;
internal sealed class CombatReportSession:EditorSession
{
    private readonly CombatReportShot[] shots;
    private readonly CombatHitView[][] hits;
    private readonly JsonArray items;
    private readonly CombatReportHistory export=new();
    internal CombatReportSession(CombatReportHistory history)
    {
        export.Restore(history.Shots,history.Omitted);
        shots=history.Shots.TakeLast(10).Reverse().Select(CombatReportTruth.Normalize).ToArray();
        hits=shots.Select(CombatHitProjection.Build).ToArray();
        items=new JsonArray(shots.Select(s=>(JsonNode)new JsonObject{["label"]=$"#{s.Number} {s.Direction} - {s.Profile}"}).ToArray());
    }
    public override bool ReadOnly=>true;
    public override bool ReadOnlyRichText=>true;
    public override string ReadOnlySaveLabel=>"Save combat report";
    public override string Title=>"Combat report - last 10 impacts";
    public override string HelpText=>"Material, thickness, angle and penetration per reached layer. Expand Details for diagnostics. Save combat report exports this session to local HTML and JSON.";
    public override JsonArray Items=>items;
    public override JsonArray Templates=>new();
    public override IEnumerable<string> Fields(int index)
    {
        yield return "summary";
        for(int i=0;i<hits[index].Length;i++)
        {
            for(int j=0;j<(hits[index][i].Layer?.Parts.Count??0);j++)yield return $"hit.{i}.part.{j}";
            var events=Diagnostics(hits[index][i]);for(int j=0;j<events.Length;j++)yield return $"hit.{i}.event.{j}";
            if(hits[index][i].Layer?.Combined==true)yield return $"hit.{i}.note";
        }
    }
    private static CombatReportEvent[] Diagnostics(CombatHitView hit)=>hit.Details.Where(e=>e.Kind!="blockMembers"&&e.Kind!="materialPending").ToArray();
    public override IEnumerable<string> Groups(int index)=>Enumerable.Range(0,hits[index].Length).Select(i=>$"Details - {i+1:00}");
    public override string FieldGroup(int index,string field)=>field=="summary"?"":$"Details - {int.Parse(field.Split('.')[1])+1:00}";
    public override string? GroupSummary(int index,string group)
    {
        int i=int.Parse(group.Split('-')[1].Trim(),CultureInfo.InvariantCulture)-1;var hit=hits[index][i];
        return CombatReportPresentation.Colour(CombatHitSummaryModel.Category(hit).Colour,hit.Summary(i+1));
    }
    private static string N(double? n)=>n is {} v?v.ToString("0.#",CultureInfo.InvariantCulture):"-";
    public override JsonNode Value(int index,string field)
    {
        var shot=shots[index];
        if(field=="summary")return JsonValue.Create(CombatReportPresentation.Escape($"{shot.Direction} | {shot.Profile}\nImpact speed: {N(shot.SpeedMps)} m/s | Initial penetration: {N(shot.InitialPenetrationMm)} mm\n{hits[index].Length} reached layers"+(shot.OmittedEvents>0?$" | {shot.OmittedEvents} omitted diagnostics":"")))!;
        var tokens=field.Split('.');var hit=hits[index][int.Parse(tokens[1],CultureInfo.InvariantCulture)];string text;
        if(tokens[2]=="part")
        {var p=hit.Layer!.Parts[int.Parse(tokens[3],CultureInfo.InvariantCulture)];text=$"{p.Name}: {p.MaterialLabel??"component"}; {N(p.ThicknessMm)} mm; {N(p.AngleDegrees)}\u00B0";}
        else if(tokens[2]=="note")text="Native aggregate: entry/exit apply to the entire block. Individual member penetration was not measured.";
        else
        {var e=Diagnostics(hit)[int.Parse(tokens[3],CultureInfo.InvariantCulture)];text=e.Text+(e.BeforeMm!=null||e.AfterMm!=null?$" [Pen: {N(e.BeforeMm)} -> {N(e.AfterMm)} mm]":"");}
        return JsonValue.Create(CombatReportPresentation.Escape(text))!;
    }
    public override string Caption(int index,string field)=>field=="summary"?"IMPACT SUMMARY":"Hit details";
    public override void Set(int index,string field,string value)=>throw new InvalidOperationException("Combat reports are read-only.");
    public override int Duplicate(JsonObject template)=>throw new InvalidOperationException("Combat reports are read-only.");
    public override void Save()=>CombatReportStore.Save(export);
}
