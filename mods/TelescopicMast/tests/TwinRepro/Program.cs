using SprocketTelescopicMast;
HingeRuntime.RunTwinTests();
HingeRuntime.RunSettingsTests();
namespace SprocketTelescopicMast
{
 using Sprocket.Vehicles;using SprocketTelescopicMast.Io;using UnityEngine;
 using Bag=Il2CppSystem.Runtime.Serialization.SerializationInfo;
 internal static partial class HingeRuntime
 {
  private static int checks;
  private static readonly List<string> report=new();
  private static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  internal static void RunTwinTests()
  {
   foreach(var type in new[]{"mast","rotator","hinge","mount"})
    foreach(var scenario in new[]{"mutual","fallback","unreadable","legacy-wired","separate-bases","outside-1mm","inside-1mm","different-guid","wrong-mirror","missing-vuid","unwired-node","release-primary","initial-off"}) Case(type,scenario);
   File.WriteAllLines("logs/twin-cases-0.9.8.txt",report);File.WriteAllLines("logs/twin-traces-0.9.8.txt",Plugin.Traces);
   Console.WriteLine($"PASS {checks} production twin grouping/panel/binding/world-pose/save/lifecycle checks across {report.Count} cases");
  }
  private static void Case(string type,string scenario)
  {
   Vehicles.Clear();Flows.Clear();FlowParts.Clear();Leaves.Clear();KnownExternalParts.Clear();Warnings.Clear();MastRuntime.States.Clear();RotatorRuntime.ClearForTest();IoRegistry.ClearForTests();IoApiCore.ClearForTests();
   DesignerMode=true;Time.unscaledTime+=10;Plugin.Detailed=true;
   var gateway=new Vehicle{Pointer=(IntPtr)12345};
   string baseTag=type=="mast"?MastRuntime.BaseTag:type=="rotator"?RotatorRuntime.BaseTag:BaseTag;
   var basisRotation=Quaternion.AngleAxis(23,Vector3.forward)*Quaternion.AngleAxis(38,Vector3.up);
   VehicleObject Base(int id,Vector3 at){var o=new VehicleObject{Id=id,Pointer=(IntPtr)id,GUID="base",Vehicle=gateway,Tag=baseTag};o.VehicleTransform.VehicleObject=o;o.VehicleTransform.Position=at;o.VehicleTransform.Rotation=basisRotation;return o;}
   var basis=Base(100,new(2,1,-3));var other=scenario=="separate-bases"?Base(200,new(-2,1,-3)):basis;
   VehicleObject Part(int id,int vuid,VehicleObject parent,bool mirror){
    var o=new VehicleObject{Id=id,Pointer=(IntPtr)id,GUID=type+"-definition",SavedVuid=vuid,Vehicle=gateway};
    o.Tag=type=="mast"?MastRuntime.PartTag:type=="rotator"?RotatorRuntime.PlateTag:"";
    var t=o.VehicleTransform;t.VehicleObject=o;t.Parent=parent.VehicleTransform;t.Position=parent.VehicleTransform.Position;
    if(type=="mast")t.Position+=basisRotation*new Vector3(0,Travel.InsertPosition(0,1),0);
    t.Rotation=basisRotation*(mirror?Quaternion.AngleAxis(180,Vector3.up):Quaternion.identity);t.LocalRotation=mirror?Quaternion.AngleAxis(180,Vector3.up):Quaternion.identity;
    if(type=="mast")MastRuntime.States[o.Id]=new(){Object=o};
    else if(type=="rotator")RotatorRuntime.RegisterForTest(o);
    else Leaves[o.Id]=new Leaf(o,type=="mount");
    gateway.ObjectReader.Items.Values.Add(o);RegisterFlowPart(o);return o;
   }
   // Register the larger VUID first, with a smaller instance ID: VUID must win.
   var secondary=Part(10,200,other,true);var primary=Part(50,100,basis,false);
   if(scenario=="missing-vuid"){primary.SavedVuid=secondary.SavedVuid=0;(primary,secondary)=(secondary,primary);}
   if(scenario=="different-guid")secondary.GUID+="-other";
   if(scenario is "inside-1mm" or "initial-off")secondary.VehicleTransform.Position+=basisRotation*new Vector3(.0005f,0,0);
   if(scenario=="outside-1mm")secondary.VehicleTransform.Position+=basisRotation*new Vector3(.002f,0,0);
   if(scenario is not ("fallback" or "missing-vuid" or "unreadable")){
    primary.VehicleTransform.MirrorVUID=(secondary.VUID.Value,0);secondary.VehicleTransform.MirrorVUID=(primary.VUID.Value,0);
   }
   if(scenario=="unreadable")primary.VehicleTransform.MirrorUnreadable=secondary.VehicleTransform.MirrorUnreadable=true;
   if(scenario=="wrong-mirror")secondary.VehicleTransform.MirrorVUID=(9999,0);
   bool twin=scenario is not ("separate-bases" or "outside-1mm" or "different-guid" or "wrong-mirror");
   PrepareFlows();var panel=PanelFlow(true,null)!;
   Check(gateway.Behaviour==null&&!primary.PhysicsEnabled&&!secondary.PhysicsEnabled,"same real designer objects");
   Check(panel.Parts.Count==(twin?1:2),type+" "+scenario+" designer output count");
   if(twin)Check(panel.Parts.Single().Id==PartId(primary),"lowest VUID or stable fallback primary");
   var target=FlowTemplates.Target(type=="mast"?FlowTemplates.MastFired:type=="rotator"?FlowTemplates.Spin:FlowTemplates.Reload,panel.Flow,null,panel.Parts);
   Check(target!=null&&(!twin||target.Id==PartId(primary)),"templates see primary only");
   var flow=panel.Flow;flow.Nodes.Clear();flow.Wires.Clear();
   flow.Nodes.Add(new("on","always"));flow.Nodes.Add(new("off","not"));flow.Wires.Add(new("on","off",0));
   string outputType=type=="mount"?"hinge":type;
   FlowNode Output(string id,VehicleObject o,string source){var n=new FlowNode(id,outputType);n.Set("partId",PartId(o));n.Set("openAngle","70");n.Set("speed","90");n.Set("slow","45");flow.Nodes.Add(n);if(source!="")flow.Wires.Add(new(source,id,0));return n;}
   var node=Output("primary",primary,scenario=="initial-off"?"off":"on");FlowNode? legacy=null;
   if(scenario is "legacy-wired" or "unwired-node")legacy=Output("secondary",secondary,scenario=="legacy-wired"?"off":"");
   flow.Edited();panel.Save();
   string original=FlowJson.Write(flow);var states=new[]{primary,secondary}.Select(o=>{var b=new Bag();SaveFlow(o,b);return b;}).ToArray();
   Check(states[0].Values[FlowDocument.PartKey]!=states[1].Values[FlowDocument.PartKey],"twin identities retained independently");
   if(legacy!=null){Check(panel.Exists(legacy.PartId),"legacy twin remains a valid stored output");Check(panel.TwinHint(legacy)=="twin of "+panel.Parts.Single().Title+", can be removed","legacy node hint");}
   int logs=Plugin.Traces.Count(t=>t.StartsWith("[FLOW] twin"));for(int i=0;i<4;i++)PrepareFlows();
   Check(Plugin.Traces.Count(t=>t.StartsWith("[FLOW] twin"))==logs,"unchanged twin relation never spams");
   Check(FlowJson.Write(flow)==original,"grouping never changes graph payload");
   // The same native-double objects gain their Play behaviour. Reload hooks also run.
   var play=new VehicleBehaviour(500){VehicleInfo=gateway};gateway.Behaviour=play;primary.PhysicsEnabled=secondary.PhysicsEnabled=true;DesignerMode=false;
   LoadFlow(primary,states[0]);LoadFlow(secondary,states[1]);PrepareExternal();PrepareFlows();
   panel=PanelFlow(false,null)!;Check(panel.Parts.Count==(twin?1:2),"retained Play output count");
   var io=Vehicles[play.Pointer];io.Leaves.AddRange(Leaves.Values);
   var initial=primary.VehicleTransform.Rotation;
   for(int i=0;i<70;i++){
    Time.frameCount++;Time.unscaledTime+=Time.deltaTime;Time.time=Time.unscaledTime;TickVehicle(io);PrepareFlows();
    if(twin&&scenario!="legacy-wired"){
     Check(Vector3.Distance(primary.VehicleTransform.Position,secondary.VehicleTransform.Position)<.00001f,"same world position under mirrored transform");
     if(type!="mast")Check(Quaternion.Angle(primary.VehicleTransform.Rotation,secondary.VehicleTransform.Rotation)<.06f,"same world rotation, never opposite spin/open");
    }
   }
   if(scenario=="initial-off")Check(!panel.Engine.Target("primary"),"initial off command is preserved while twin seats align");
   else if(type=="mast")Check(MastRuntime.States[primary.Id].Height>.99f,"actual production mast Move completes stroke");
   else if(type=="rotator")Check(RotatorRuntime.Angle(primary)>20,"actual production turntable moved");
   else Check(Leaves[primary.Id].Angle>60,"actual production hinge/mount opened");
   if(scenario=="legacy-wired"){
    Check(TwinLeader(secondary)==null,"wired twin keeps independent binding");
    if(type=="mast")Check(MastRuntime.States[secondary.Id].Height==0,"legacy secondary mast stays down");
    else if(type=="rotator")Check(RotatorRuntime.Angle(secondary)==0,"legacy secondary turntable stays stopped");
    else Check(Leaves[secondary.Id].Angle==0,"legacy secondary hinge stays closed");
    Check(panel.Engine.Target("primary")&&!panel.Engine.Target("secondary"),"both existing wired nodes keep their own targets");
   }
   Check(FlowJson.Write(panel.Flow)==original,"Play preserves existing nodes/wires and params");
   if(scenario=="release-primary"){
    primary.Released=true;PrepareFlows();panel=PanelFlow(false,null)!;
    Check(panel.Parts.Count==1&&panel.Parts.Single().Id==PartId(secondary),"released primary removed and remaining physical part offered");
    Check(TwinLeader(secondary)==null,"no stale twin relation after primary release");
   }
   report.Add($"PASS {type} {scenario}: outputs={(twin?1:2)}, native mirror/seat detection, real bindings and world poses, saved state intact");
   Console.WriteLine(report.Last());
  }
 }
}
