using System.Text.Json.Nodes;
using SprocketTelescopicMast;
using SprocketTelescopicMast.Io;
using SprocketUiKit;
using UnityEngine;
internal static class Tests099
{
 internal const string Schema = """
 [{"key":"speed","label":"Speed","type":"float","unit":"m/s","min":0.5,"max":15,"step":0.1,"default":3},
 {"key":"count","label":"Count","type":"int","min":1,"max":20,"default":1},
 {"key":"enabled","label":"Enabled","type":"bool","default":false},
 {"key":"item","label":"Item","type":"enum","options":[{"value":"casing","label":"Casing"},{"value":"smoke","label":"Smoke"}],"default":"casing"},
 {"key":"interval","label":"Interval","type":"float","min":0.1,"max":5,"default":1,"showIf":{"key":"enabled","value":true}},
 {"key":"life","label":"Life","type":"float","min":1,"max":10,"default":2,"showIf":{"key":"item","value":"smoke"}}]
 """;
 internal static int Run()
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception("0.9.9: "+why);}
  IoRegistry.ClearForTests();IoApiCore.ClearForTests();
  var warnings=new List<string>();IoApiCore.Log=warnings.Add;
  Check(IoApi.ApiVersion==1&&IoApi.ApiRevision==2,"revision");
  Check(IoApi.RegisterManagedOutput("test.emitter","Emitter","testEmitter",(_,_)=>{}),"fake managed consumer");
  void Bad(string component,string key,string json){int n=warnings.Count;Check(!IoApi.RegisterPartSettings(component,key,json),"reject invalid schema");Check(warnings.Count==n+1,"one reason warning");}
  Bad("missing","test.settings",Schema);Bad("testEmitter","io.settings",Schema);Bad("testEmitter","bad",Schema);
  Bad("testEmitter","test.settings",new string(' ',4097));Bad("testEmitter","test.settings", "{}");
  var array=JsonNode.Parse(Schema)!.AsArray();while(array.Count<17)array.Add(JsonNode.Parse(array[0]!.ToJsonString()));Bad("testEmitter","test.settings",array.ToJsonString());
  Bad("testEmitter","test.settings",Schema.Replace("\"float\"","\"text\""));
  array=JsonNode.Parse(Schema)!.AsArray();array.Add(JsonNode.Parse(array[0]!.ToJsonString()));Bad("testEmitter","test.settings",array.ToJsonString());
  foreach(var pair in new[]{("\"max\":15","\"max\":0"),("\"step\":0.1","\"step\":0"),("\"label\":\"Speed\"","\"label\":\""+new string('x',41)+"\""),("\"key\":\"speed\"","\"key\":\"UPPER\""),("\"key\":\"enabled\",\"value\":true","\"key\":\"speed\",\"value\":true")})
   Bad("testEmitter","test.settings",Schema.Replace(pair.Item1,pair.Item2));
  var go=new GameObject();IoApiCore.AttachPart(go,"testEmitter");int calls=0;int thread=Environment.CurrentManagedThreadId;
  Check(IoApi.RegisterPartSettings("testEmitter","test.settings",Schema,p=>{calls++;Check(Environment.CurrentManagedThreadId==thread,"main thread");Check(IoApi.GetPartData(p,"test.settings")!.Contains("speed"),"save before callback");throw new Exception("consumer failure");}),"settings facade");
  Bad("testEmitter","test.settings",Schema);
  Check(IoApiClient.Bind(typeof(IoApi))&&IoApiClient.ApiRevision==2,"reflection shim");
  Check(!IoApiClient.RegisterPartSettings("testEmitter","test.other",Schema),"shim bound and rejects duplicate");
  IoApi.SetPartData(go,"test.settings","{\"speed\":99,\"count\":2.6,\"item\":\"unknown\",\"future\":{\"v\":42}}");PartSettings.Ensure(go);
  var r=PartSettings.Find("testEmitter")!;var values=r.Values(go);
  Check(values["speed"]!.GetValue<double>()==15&&values["count"]!.GetValue<double>()==3,"clamp and round");
  Check(values["item"]!.GetValue<string>()=="casing"&&!values["enabled"]!.GetValue<bool>(),"enum fallback and defaults");
  Check(values["future"]!["v"]!.GetValue<int>()==42,"unknown preserved");
  Check(!r.Visible(r.Fields.Single(f=>f.Key=="interval"),values),"bool showIf hidden");
  Check(!r.Visible(r.Fields.Single(f=>f.Key=="life"),values),"enum showIf hidden");
  var flow=new FlowDocument();var node=new FlowNode("out","test.emitter");node.Set("partId","part1");flow.AddNode(node);
  HingeRuntime.Current=new(){Flow=flow,Parts=new(){new("part1","test.emitter","Emitter 1")},Settings=_=>new(){Registration=r,Targets=new[]{((object)go,"part1")}}};
  UiKit.Context=UiContexts.Designer;Time.unscaledTime+=10;HingePanel.Create(new());UiKit.Latest.Open();
  NodeGraph.Latest.Click!(new(){Kind=GraphHitKind.Node,NodeId="out"});UiKit.Poll();
  Check(UiKit.Labels.Contains("Settings")&&UiKit.Labels.Any(s=>s.Contains("outside flow undo")),"actual inspector section and undo hint");
  Check(UiKit.Numbers.ContainsKey("Speed")&&UiKit.Toggles.ContainsKey("Enabled")&&UiKit.Choices.ContainsKey("Item"),"all Kit field types");
  Check(UiKit.FieldEnabled["Speed"](),"designer editable");string graph=FlowJson.Write(flow);int warn=warnings.Count;
  UiKit.Numbers["Speed"](7);Check(calls==1&&warnings.Count==warn+1,"throw caught once");
  Check(r.Values(go)["speed"]!.GetValue<double>()==7,"value retained despite callback error");
  UiKit.Numbers["Speed"](7);Check(calls==1,"identical edit no callback");
  Check(FlowJson.Write(flow)==graph,"settings not in flow history");
  UiKit.Context=UiContexts.Game;Check(!UiKit.FieldEnabled["Speed"](),"Play read only on retained widgets");
  UiKit.Numbers["Speed"](9);Check(r.Values(go)["speed"]!.GetValue<double>()==7&&calls==1,"stale UI callback blocked in Play");
  HingePanel.Destroy();HingeRuntime.Current=null;
  // Both limits use the same PartData path; failed edits never call consumers.
  UiKit.Context=UiContexts.Designer;
  Check(!IoApi.SetPartData(go,"test.large",new string('x',4097)),"4 KB value limit");
  var normalized=r.Values(go);normalized["speed"]=-99;normalized["count"]=99;normalized["enabled"]=true;normalized["item"]="smoke";
  IoApi.SetPartData(go,"test.settings",normalized.ToJsonString());PartSettings.Ensure(go);normalized=r.Values(go);
  Check(normalized["speed"]!.GetValue<double>()==.5&&normalized["count"]!.GetValue<double>()==20,"lower float and upper int limits");
  Check(r.Visible(r.Fields.Single(f=>f.Key=="interval"),normalized)&&r.Visible(r.Fields.Single(f=>f.Key=="life"),normalized),"bool and enum showIf reveal");
  var full=new GameObject();IoApiCore.AttachPart(full,"testEmitter");PartSettings.Ensure(full);
  foreach(var key in new[]{"test.a","test.b","test.c"}) Check(IoApi.SetPartData(full,key,new string('x',4000)),"fill part within budget");
  Check(IoApi.SetPartData(full,"test.d",new string('x',4000)),"four legal values within part budget");
  Check(!IoApi.SetPartData(full,"test.e",new string('x',1000)),"16 KB part budget");
  IoApi.SetPartData(full,"test.d",null);
  // Twin preflight rejects all writes when only the second target would overflow.
  var huge=r.Values(full);huge["future"]=new string('x',3200);Check(IoApi.SetPartData(full,"test.settings",huge.ToJsonString()),"settings near part limit");
  int tail=0;while(PartData.CanSet(full,"test.tail",new string('x',tail+1)))tail++;
  Check(IoApi.SetPartData(full,"test.tail",new string('x',tail)),"fill to exact part budget");
  string original=IoApi.GetPartData(go,"test.settings")!;int beforeCalls=calls;
  Check(!PartSettings.Change(r,new[]{((object)go,"first"),((object)full,"second")},"speed",JsonValue.Create(15d),true,null),"full second twin rejects whole edit");
  var large=JsonValue.Create(new string('x',5000));
  Check(!PartData.CanSet(full,"test.settings",large!.ToJsonString()),"settings preflight 4 KB");
  Check(IoApi.GetPartData(go,"test.settings")==original&&calls==beforeCalls,"limit refusal leaves data and callbacks unchanged");
  File.WriteAllText("logs/settings-unit-0.9.9.txt",$"PASS {checks} validation, normalization, facade/shim, actual inspector, retained Play guards, callback isolation checks.\n");
  return checks;
 }
}
