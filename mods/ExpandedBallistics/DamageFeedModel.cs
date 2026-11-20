namespace SprocketExpandedBallistics;
internal enum PlateOutcome {Unknown,Penetrated,Stopped,Deflected}
internal static class DamageFeedTruth
{
    internal static PlateOutcome Outcome(bool penetrated,bool embedded,bool deflected,int continuations,bool error=false)
    {if(error)return PlateOutcome.Unknown;if(embedded)return continuations==0?PlateOutcome.Stopped:PlateOutcome.Unknown;if(deflected)return PlateOutcome.Deflected;if(penetrated&&continuations>0)return PlateOutcome.Penetrated;return PlateOutcome.Unknown;}
    internal static double? Remaining(IReadOnlyList<double> continuationBudgets)=>continuationBudgets.Count==1&&double.IsFinite(continuationBudgets[0])&&continuationBudgets[0]>=0?continuationBudgets[0]:null;
    internal static string Label(string? text,string fallback)
    {var clean=new string((text??"").Where(c=>!char.IsControl(c)&&c!='<'&&c!='>').ToArray()).Trim();return clean.Length==0?fallback:clean.Length>48?clean[..48]:clean;}
    // The editor changes the runtime mesh name; the structure blueprint is a separate template.
    internal static string StructureLabel(string? instanceName,string? templateName,string fallback)
    {var instance=Label(instanceName,"");return instance.Length>0?instance:Label(templateName,fallback);}
    internal static bool DirectDamage(bool shot,bool nativeDamageCall,double delta,bool targetRegister=true)=>shot&&nativeDamageCall&&targetRegister&&double.IsFinite(delta)&&delta<0;
    internal static bool Died(bool? previouslyAlive,bool alive,double delta)=>previouslyAlive==true&&!alive&&double.IsFinite(delta)&&delta<0;
}
internal sealed record ComponentDamage(string Text,bool Fatal)
{
    internal ComponentDamage Merge(ComponentDamage next)=>Fatal&&!next.Fatal?this:next;
}
internal sealed class DamageFeedBuffer
{
    internal sealed record Row(double Time,string Text);
    private readonly List<Row> rows=new();
    internal const int MaximumRows=12;
    internal long Revision {get;private set;}
    internal IReadOnlyList<Row> Visible(double now)
    {if(!double.IsFinite(now)){Clear();return rows;}for(int i=rows.Count-1;i>=0;i--)if(now-rows[i].Time>=8||now<rows[i].Time){rows.RemoveAt(i);Revision++;}return rows;}
    internal void Add(double now,IEnumerable<string> lines)
    {if(!double.IsFinite(now))return;foreach(var line in lines.Take(MaximumRows))rows.Add(new(now,line));if(rows.Count>MaximumRows)rows.RemoveRange(0,rows.Count-MaximumRows);Revision++;}
    internal void AddHit(double now,CombatHitSummary hit)
    {if(!double.IsFinite(now))return;rows.Add(new(now,hit.RichText));if(rows.Count>6)rows.RemoveRange(0,rows.Count-6);Revision++;}
    internal void Clear(){if(rows.Count==0)return;rows.Clear();Revision++;}
}

internal sealed class DamageFeedActivations
{
    private sealed record Activation(string Label,string Text);
    private readonly Dictionary<string,Activation> rows=new();
    internal IEnumerable<string> Rows=>rows.Values.Select(r=>r.Text);
    internal bool Record(string key,bool live,bool first,string kind,string label)
    {
        if(!live||!first||kind is not ("lightEra" or "heavyEra")||rows.Count>=4||rows.ContainsKey(key))return false;
        rows.Add(key,new(label,"ERA activated: "+label));return true;
    }
    internal void Budget(string key,double before,double after)
    {
        if(rows.TryGetValue(key,out var row)&&double.IsFinite(before)&&double.IsFinite(after)&&before>=0&&after>=0)
            rows[key]=row with {Text=$"ERA activated: {row.Label} - {before:0} -> {after:0} mm"};
    }
    internal static IEnumerable<string> Compose(IEnumerable<string> activations,IEnumerable<string> health,IEnumerable<string> layers)=>activations.Take(4).Concat(health.Take(8)).Concat(layers).Take(12);
}

internal sealed class DamageFeedTimeline
{
 private sealed record Event(string Key,string Stage,string Text,bool Terminal);
 private readonly List<Event> events=new();
 private readonly HashSet<string> seen=new();
 private int omitted;
 internal void Add(string key,string stage,string text,bool terminal=false)
 {
  key=stage+":"+key;
  if(!seen.Add(key))return;
  if(events.Count>=256)
  {
   var pinned=events.Where(e=>e.Terminal).GroupBy(e=>e.Stage).Select(g=>g.Last().Key).ToHashSet();
   var discard=events.FindIndex(e=>!pinned.Contains(e.Key));
   seen.Remove(events[discard].Key);events.RemoveAt(discard);omitted++;
  }
  events.Add(new(key,stage,text,terminal));
 }
 internal void Era(string key,string stage,bool live,bool first,string kind,string label)
 {if(live&&first&&kind is ("lightEra" or "heavyEra"))Add("era:"+key,stage,"ERA activated: "+label);}
 internal void EraBudget(string key,string stage,double before,double after)
 {
  if(!double.IsFinite(before)||!double.IsFinite(after)||before<0||after<0)return;
  var i=events.FindIndex(e=>e.Key==stage+":era:"+key);
  if(i>=0)events[i]=events[i] with {Text=events[i].Text+$" - ERA only {before:0} -> {after:0} mm (before plate)"};
 }
 internal IReadOnlyList<string> All=>events.Select(e=>(e.Stage.Length>0?e.Stage+": ":"")+e.Text).ToArray();
 internal IReadOnlyList<string> Visible
 {
  get
  {
   if(events.Count<=12&&omitted==0)return All;
   // Keep the latest native terminal of each charge, then fill remaining space
   // with recent events. Sort by original event position, never by category.
   var selected=events.Select((e,i)=>(e,i)).Where(x=>x.e.Terminal)
    .GroupBy(x=>x.e.Stage).Select(g=>g.Last().i).TakeLast(11).ToHashSet();
   for(int i=events.Count-1;i>=0&&selected.Count<11;i--)selected.Add(i);
   var rows=selected.OrderBy(i=>i).Select(i=>events[i]).Select(e=>(e.Stage.Length>0?e.Stage+": ":"")+e.Text);
   return new[]{"... "+(omitted+events.Count-selected.Count)+" events omitted"}.Concat(rows).ToArray();
  }
 }
}
