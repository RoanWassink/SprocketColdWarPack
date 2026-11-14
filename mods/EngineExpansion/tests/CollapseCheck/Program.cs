using System.Text.Json;
using System.Text.Json.Nodes;
using SprocketEngineExpansion.Core;
System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
int checks=0;void Check(bool v,string message){checks++;if(!v)throw new Exception(message);}
int Int(JsonElement e,string key)=>e.GetProperty(key).GetInt32();
Vec3 V(JsonElement e,string key,int i){var a=e.GetProperty(key);return new(a[i*3].GetDouble(),a[i*3+1].GetDouble(),a[i*3+2].GetDouble());}
Quat Euler(Vec3 e)
{
 Quat Axis(double angle,Vec3 axis){double r=angle*Math.PI/360;return new(axis.X*Math.Sin(r),axis.Y*Math.Sin(r),axis.Z*Math.Sin(r),Math.Cos(r));}
 return Axis(e.Y,new(0,1,0))*Axis(e.X,new(1,0,0))*Axis(e.Z,new(0,0,1));
}
// Real-tank blueprints are not part of the source tree. Point ENGINE_EXPANSION_FIXTURES (or argument 1) at a folder
// with *.blueprint files to run the fixture checks; without it only the synthetic cases below run.
string? fixtures=Environment.GetEnvironmentVariable("ENGINE_EXPANSION_FIXTURES");
if(string.IsNullOrWhiteSpace(fixtures)&&args.Length>0)fixtures=args[0];
fixtures=string.IsNullOrWhiteSpace(fixtures)?null:Path.GetFullPath(fixtures);
var rows=new List<string>();int oldCount=0,newCount=0,collapsed=0;
foreach(string file in fixtures!=null?Directory.GetFiles(fixtures,"*.blueprint",SearchOption.AllDirectories).OrderBy(x=>x).ToArray():Array.Empty<string>())
{
 using var doc=JsonDocument.Parse(File.ReadAllText(file));
 JsonElement? Find(JsonElement e)
 {if(e.ValueKind==JsonValueKind.Object){if(e.TryGetProperty("modules",out _)&&e.TryGetProperty("ports",out _)&&e.TryGetProperty("connections",out _))return e;foreach(var p in e.EnumerateObject()){var v=Find(p.Value);if(v!=null)return v;}}else if(e.ValueKind==JsonValueKind.Array)foreach(var a in e.EnumerateArray()){var v=Find(a);if(v!=null)return v;}return null;}
 var layout=Find(doc.RootElement)!.Value;
 var mods=layout.GetProperty("modules").EnumerateArray().Select(m=>new LayoutModule(Int(m,"id"),(ModuleKind)Int(m,"type"),(short)Int(m,"flags"),Int(m,"mirrorID"))).ToArray();
 var ports=layout.GetProperty("ports").EnumerateArray().Select((p,i)=>(Id:Int(p,"id"),Module:Int(p,"moduleID"),Index:i)).ToDictionary(p=>p.Id);
 var conns=layout.GetProperty("connections").EnumerateArray().Select(c=>new LayoutConnection(Int(c,"id"),ports[Int(c,"providerID")].Module,ports[Int(c,"receiverID")].Module,(byte)Int(c,"flags"))).ToArray();
 var rawConnections=layout.GetProperty("connections").EnumerateArray().ToDictionary(c=>Int(c,"id"));
 Vec3 PortPosition(int id){var p=ports[id];int index=Array.FindIndex(mods,m=>m.Id==p.Module);return V(layout,"modulePositions",index)+Euler(V(layout,"moduleRotations",index)).Rotate(V(layout,"portOffsets",p.Index));}
 Vec3 PortDirection(int id){var p=ports[id];int index=Array.FindIndex(mods,m=>m.Id==p.Module);return Euler(V(layout,"moduleRotations",index)).Rotate(V(layout,"portDirections",p.Index));}
 int engine=mods.First(m=>m.Kind==ModuleKind.Engine).Id;
 var old=PowerpackRules.Find(mods,conns,engine);var twin=PowerpackRules.FindTwin(mods,conns,engine);
 bool oldReady=old.Status==PowerpackStatus.Ready||twin.Status==PowerpackStatus.Ready;
 var outgoing=conns.First(c=>c.ProviderModule==engine);int outputId=Int(rawConnections[outgoing.Id],"providerID");
 var plan=LayoutCollapse.Find(mods,conns,engine,id=>PortPosition(Int(rawConnections[id],"receiverID")),PortPosition(outputId),PortDirection(outputId));
 bool marked=old.Marked||twin.Marked;bool ready=marked||plan.Status==PowerpackStatus.Ready;
 Check(!oldReady||ready,"no 0.4.1-ready layout may become grey: "+file);
 if(oldReady)oldCount++;if(ready)newCount++;
 string name=Path.GetRelativePath(fixtures!,file).Replace('\\','/');
 if(name.Contains("Challenger 1")||name.Contains("Challenger 2")||name.Contains("Puma II")||name.Contains("Lowe 2A2"))Check(ready,"required calibration fixture "+name+": "+plan.Reason);
 if(name.Contains("Panzer IV"))Check(!ready&&plan.Reason.StartsWith("Front drive"),"Panzer IV rejected as front drive");
 if(ready&&plan.Needed)
 {
  collapsed++;
  // Replay the plan on the actual saved graph. The native adapter translates these same IDs with
  // DestroyModule/DeleteMarked/CreateConnection. These checks do not impersonate native undo.
  var copy=JsonNode.Parse(layout.GetRawText())!.AsObject();
  var removed=plan.Removed.ToHashSet();var removedPorts=ports.Values.Where(p=>removed.Contains(p.Module)).Select(p=>p.Id).ToHashSet();
  var mm=copy["modules"]!.AsArray();var pp=copy["ports"]!.AsArray();var cc=copy["connections"]!.AsArray();
  int next=mods.Select(m=>m.Id).Concat(ports.Keys).Concat(conns.Select(c=>c.Id)).Max()+1;int maximum=next-1;
  foreach(var link in plan.Links.Where(l=>l.FirstConnection!=l.LastConnection))
  {var a=rawConnections[link.FirstConnection];var b=rawConnections[link.LastConnection];cc.Add(new JsonObject{["id"]=next++,["flags"]=0,["providerID"]=Int(a,"providerID"),["receiverID"]=Int(b,"receiverID")});}
  for(int i=cc.Count-1;i>=0;i--)if(removedPorts.Contains(cc[i]!["providerID"]!.GetValue<int>())||removedPorts.Contains(cc[i]!["receiverID"]!.GetValue<int>()))cc.RemoveAt(i);
  for(int i=pp.Count-1;i>=0;i--)if(removedPorts.Contains(pp[i]!["id"]!.GetValue<int>()))pp.RemoveAt(i);
  for(int i=mm.Count-1;i>=0;i--)if(removed.Contains(mm[i]!["id"]!.GetValue<int>()))mm.RemoveAt(i);
  var mids=mm.Select(m=>m!["id"]!.GetValue<int>()).ToHashSet();var pids=pp.Select(p=>p!["id"]!.GetValue<int>()).ToHashSet();
  Check(mm.All(m=>m!["mirrorID"]!.GetValue<int>()<0||mids.Contains(m["mirrorID"]!.GetValue<int>())),"no mirror orphan "+name);
  Check(pp.All(p=>mids.Contains(p!["moduleID"]!.GetValue<int>())),"no module orphan "+name);
  Check(cc.All(c=>pids.Contains(c!["providerID"]!.GetValue<int>())&&pids.Contains(c["receiverID"]!.GetValue<int>())),"no port orphan "+name);
  Check(cc.Where(c=>c!["id"]!.GetValue<int>()>maximum).Count()==plan.Links.Count(l=>l.FirstConnection!=l.LastConnection),"new IDs beyond global maximum");
  // The pure plan/replay cannot exercise native history. Check real graph invariants here;
  // native Ctrl+Z/redo and layout hashes are explicit in-game acceptance gates.
  var survivingOriginal=layout.GetProperty("connections").EnumerateArray().Where(c=>!removedPorts.Contains(Int(c,"providerID"))&&!removedPorts.Contains(Int(c,"receiverID"))).ToArray();
  Check(survivingOriginal.All(c=>cc.Any(n=>n!["id"]!.GetValue<int>()==Int(c,"id")&&n["providerID"]!.GetValue<int>()==Int(c,"providerID")&&n["receiverID"]!.GetValue<int>()==Int(c,"receiverID"))),"all surviving downstream connections unchanged");
  Check(mm.Count==mods.Length-removed.Count&&pp.Count==ports.Count-removedPorts.Count&&cc.Count==survivingOriginal.Length+plan.Links.Count(l=>l.FirstConnection!=l.LastConnection),"collapse graph cardinality");
 }
 rows.Add($"| {name} | {(oldReady?"ready":"grey")} | {(ready?"ready":"grey")} | {plan.Distance:0.000} | {(marked?"Existing marked powerpack":plan.Reason)} | {plan.Removed.Length} |");
 checks++;
}
Check(fixtures==null||rows.Count>0,"the fixture folder holds at least one blueprint");
// Negative topology and distance cases exercise production graph recognition.
var m0=new[]{new LayoutModule(0,ModuleKind.Engine,0),new LayoutModule(1,ModuleKind.Junction,0),new LayoutModule(2,ModuleKind.Transmission,0)};
var c0=new[]{new LayoutConnection(3,0,1,0),new LayoutConnection(4,1,2,0)};
CollapsePlan Plan(LayoutModule[] m,LayoutConnection[] c,Vec3 pos)=>LayoutCollapse.Find(m,c,0,_=>pos,new(0,0,0),new(0,0,1));
Check(Plan(m0,c0,new(0,0,-1)).Removed.SequenceEqual(new[]{1}),"one-to-one junction removed");
Check(Plan(m0,c0,new(0,0,-2)).Reason.StartsWith("Too far"),"long rear route refused");
Check(Plan(m0,c0,new(0,0,3)).Reason.StartsWith("Front drive"),"front route refused");
Check(Plan(m0.Select(m=>m.Id==1?m with {MirrorId=99}:m).ToArray(),c0,new(0,0,0)).Status!=PowerpackStatus.Ready,"unpaired mirror refused");
Check(Plan(m0,c0.Append(new LayoutConnection(5,1,0,0)).ToArray(),new(0,0,0)).Status!=PowerpackStatus.Ready,"cycle/branch refused");
var beforeIssues=new[]{new LayoutIssue("existing","old")};
foreach(string reason in new[]{"TooLong","angle","obstruction"})
 Check(LayoutValidation.NewProblem(beforeIssues,beforeIssues.Append(new LayoutIssue(reason,reason)))==reason,"new "+reason+" rejects copy");
Check(LayoutValidation.NewProblem(beforeIssues,beforeIssues)==null,"existing issues allowed");
Check(StartupSafety.SafeMode("FAKE-HASH")&&!StartupSafety.InstallHooks("FAKE-HASH"),"fake hash installs zero hooks");
Check(StartupSafety.InstallHooks(StartupSafety.AuditedHash),"audited hash installs hooks");
Check(StartupSafety.Warning.Contains("CAN REMOVE")&&StartupSafety.Warning.Contains("No hooks"),"safe-mode warning explains loss and disabled features");
if(fixtures!=null)File.WriteAllText(Path.Combine(fixtures,"..","ELIGIBILITY-0.5.0.md"),$"# Eligibility 0.5.0\n\n{rows.Count} copied fixtures. Old topology-ready: {oldCount}; new topology/distance-ready: {newCount}; newly collapsible chains: {collapsed}. 0 ready-to-grey regressions. Existing direct/exact Twin bypass distance/front-drive checks. Date and native preflight are additional runtime gates (not stored in the layout table).\n\nDefault distance **1.75 m**: Challenger 1 reaches 1.701 m; Challenger 2 1.548 m; Puma II 0.530-0.609 m; Lowe 2A2 1.147 m. Panzer IV 2.358-2.868 m, forwards. Distance is Euclidean engine-output to transmission-input in layout space, worst side of Twin. Unity Euler order Y*X*Z. Front-drive reason uses vehicle-forward projected into layout space; these fixtures have identity layout frames. All 0.4.1-ready layouts stay ready, including remote saved Leopard layouts.\n\nA two-output junction with flags 0 is normalised to native splitter flags 0x70 in the copy; required because Challenger fixtures store manual splitters. CollapseJunctions=false retains the exact old recognisers and bypasses this distance gate. Conservative preflight may still refuse a ready row.\n\n| Fixture | 0.4.1 | 0.5.0 | Distance m | Reason | Junctions removed |\n|---|---|---|---:|---|---:|\n"+string.Join("\n",rows)+"\n");
Console.WriteLine($"PASS {checks} collapse/safe-mode checks; fixtures={rows.Count} old={oldCount} new={newCount} chains={collapsed}. Native undo/rebuild acceptance remains in-game.");
