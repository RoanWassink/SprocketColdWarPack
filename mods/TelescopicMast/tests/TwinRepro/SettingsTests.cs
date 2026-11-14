using Sprocket.Vehicles;
using SprocketTelescopicMast.Io;
using UnityEngine;
using SprocketUiKit;
namespace SprocketTelescopicMast;
internal static partial class HingeRuntime
{
 internal static void RunSettingsTests()
 {
  int checks=0;void Check(bool b,string why){checks++;if(!b)throw new Exception("settings runtime: "+why);}
  foreach(string scenario in new[]{"merged","wired","offset","different-base","v1-output"})
  {
   Vehicles.Clear();Flows.Clear();FlowParts.Clear();Leaves.Clear();KnownExternalParts.Clear();Warnings.Clear();IoRegistry.ClearForTests();IoApiCore.ClearForTests();
   DesignerMode=true;Time.unscaledTime+=10;
   Check(scenario == "v1-output" ? IoApi.RegisterOutput("test.emitter","Emitter","testEmitter",(_,_)=>{},_=>null,(_,_)=>{}) : IoApi.RegisterManagedOutput("test.emitter","Emitter","testEmitter",(_,_)=>{}),"consumer output");
   var callbacks=new List<GameObject>();var warnings=new List<string>();IoApiCore.Log=warnings.Add;
   Check(IoApi.RegisterPartSettings("testEmitter","test.settings",Schema,go=>{Check(IoApi.GetPartData(go,"test.settings")!.Contains("7"),"persisted before callback");callbacks.Add(go);}),"consumer schema");
   var gateway=new Vehicle{Pointer=(IntPtr)8000};
   VehicleObject Basis(int id){var b=new VehicleObject{Id=id,Pointer=(IntPtr)id,Vehicle=gateway};b.VehicleTransform.VehicleObject=b;return b;}
   var basis=Basis(1);var other=scenario=="different-base"?Basis(2):basis;
   VehicleObject Part(int id,VehicleObject parent){var p=new VehicleObject{Id=id,Pointer=(IntPtr)id,GUID="emitter-guid",Vehicle=gateway};p.VehicleTransform.VehicleObject=p;p.VehicleTransform.Parent=parent.VehicleTransform;var c=new VehicleComponent{Id=id+100,ComponentID="testEmitter",VehicleObject=p};p.Components.Values.Add(c);gateway.ObjectReader.Items.Values.Add(p);RegisterFlowPart(p);return p;}
   var primary=Part(10,basis);var twin=Part(20,other);
   primary.VehicleTransform.MirrorVUID=(20,0);twin.VehicleTransform.MirrorVUID=(10,0);
   if(scenario=="offset"){primary.VehicleTransform.Position=new(-1,0,0);twin.VehicleTransform.Position=new(1,0,0);}
   PrepareFlows();var panel=PanelFlow(true,null)!;
   bool merged=scenario is "merged" or "wired" or "v1-output";
   Check(panel.Parts.Count==(merged?1:2),"designer output count");
   var flow=panel.Flow;flow.Nodes.Clear();flow.Wires.Clear();flow.AddNode(new("on","always"));
   var node=new FlowNode("primary","test.emitter");node.Set("partId",PartId(primary));flow.AddNode(node);flow.Connect("on","primary",0);
   if(scenario=="wired"){var n=new FlowNode("secondary","test.emitter");n.Set("partId",PartId(twin));flow.AddNode(n);flow.Connect("on","secondary",0);}
   panel.Save();panel=PanelFlow(true,null)!;
   var a=ExternalPartObject(primary)!;var b=ExternalPartObject(twin)!;
   Check(IoApi.GetPartData(a,"test.settings")!=null,"host defaults available without opening inspector");
   var binding=panel.Settings(node)!;Check(binding.Targets.Count==(scenario is "merged" or "v1-output" ?2:1),"twin settings targets");
   var displayed=panel;SettingsPanel.Build(new Container(),()=>displayed.Settings(node),()=>DesignerMode);
   var speed=Container.Controls["Speed"];
   Check(speed.Widget.Enabled(),"designer Kit field enabled");int prior=Plugin.Traces.Count(t=>t.StartsWith("[IO SETTINGS]"));
   ((Action<double>)speed.Set)(7);
   Check(callbacks.Count==(scenario is "merged" or "v1-output" ?2:1),"callbacks for each changed part");
   Check(Plugin.Traces.Count(t=>t.StartsWith("[IO SETTINGS]"))==prior+callbacks.Count,"detailed log only on change");
   ((Action<double>)speed.Set)(7);Check(Plugin.Traces.Count(t=>t.StartsWith("[IO SETTINGS]"))==prior+callbacks.Count,"no unchanged spam");
   double Value(GameObject go)=>PartSettings.Find("testEmitter")!.Values(go)["speed"]!.GetValue<double>();
   Check(Value(a)==7&&Value(b)==(scenario is "merged" or "v1-output" ?7:3),"independent/wired or merged settings");
   // Exact production outer Save and Load, with SerializationInfo/native objects doubled.
   VehicleObjectBlueprint SavePart(VehicleObject p){var bp=new VehicleObjectBlueprint();Save(p,bp);return bp;}
   var savedA=SavePart(primary);var savedB=SavePart(twin);
   Check(savedA.State!.Values.ContainsKey(PartData.StateKey),"existing ioPartDataV1 state key");
   File.WriteAllText("logs/settings-payload-0.9.9-"+scenario+".json",System.Text.Json.JsonSerializer.Serialize(new[]{savedA.State.Values,savedB.State!.Values}));
   PartData.ClearEntry(a);PartData.ClearEntry(b);Load(primary,savedA.State,"repro");Load(twin,savedB.State,"repro");PrepareFlows();
   Check(Value(a)==7&&Value(b)==(scenario is "merged" or "v1-output" ?7:3),"clean data reload from actual saved payload");
   var mirror=Part(30,basis);Load(mirror,savedA.State,"mirror-copy");PrepareFlows();
   Check(Value(ExternalPartObject(mirror)!)==7,"native blueprint copy carries settings");
   // Retain the same designer objects into Play and reload exactly their saved state.
   var play=new VehicleBehaviour(9000){VehicleInfo=gateway};gateway.Behaviour=play;primary.PhysicsEnabled=twin.PhysicsEnabled=true;DesignerMode=false;
   Load(primary,savedA.State,"Play");Load(twin,savedB.State,"Play");PrepareExternal();PrepareFlows();displayed=PanelFlow(false,null)!;
   Check(!speed.Widget.Enabled(),"retained field read only in Play");((Action<double>)speed.Set)(9);Check(Value(a)==7,"Play callback cannot edit");
   Check(displayed.Settings(node)!=null,"settings still visible in Play");
   Console.WriteLine("PASS settings "+scenario+": real panel, flow, outer Save/Load, mirrored payload and retained designer -> Play");
  }
  Console.WriteLine($"PASS {checks} settings production integration checks");
 }
 private const string Schema = """

 [{"key":"speed","label":"Speed","type":"float","unit":"m/s","min":0.5,"max":15,"step":0.1,"default":3},
 {"key":"count","label":"Count","type":"int","min":1,"max":20,"default":1},
 {"key":"enabled","label":"Enabled","type":"bool","default":false},
 {"key":"item","label":"Item","type":"enum","options":[{"value":"casing","label":"Casing"},{"value":"smoke","label":"Smoke"}],"default":"casing"},
 {"key":"interval","label":"Interval","type":"float","min":0.1,"max":5,"default":1,"showIf":{"key":"enabled","value":true}},
 {"key":"life","label":"Life","type":"float","min":1,"max":10,"default":2,"showIf":{"key":"item","value":"smoke"}}]
""";
}
