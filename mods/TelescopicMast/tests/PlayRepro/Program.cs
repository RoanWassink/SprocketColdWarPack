using SprocketTelescopicMast;
HingeRuntime.RunPlayTests(args.Contains("--baseline"));
namespace SprocketTelescopicMast
{
 using Sprocket.Vehicles;
 using Sprocket.Vehicles.Cannons;
 using Sprocket.Vehicles.Weapons.Cannons;
 using SprocketTelescopicMast.Io;
 using UnityEngine;
 using Bag=Il2CppSystem.Runtime.Serialization.SerializationInfo;
 internal static partial class HingeRuntime
 {
  static int checks;
  static void Check(bool ok,string why) { checks++; if(!ok) throw new Exception(why); }
  internal static void RunPlayTests(bool baseline)
  {
   foreach(bool repeat in new[]{false,true}) foreach(bool hooks in new[]{false,true}) foreach(int group in new[]{0,1,4}) foreach(bool specific in new[]{false,true})
    Case(baseline,repeat,hooks,group,specific);
   File.WriteAllLines("logs/play-traces-"+(baseline?"baseline":"0.9.7")+".txt",Plugin.Traces);
   Console.WriteLine($"PASS {checks} real production flow/cannon/mast checks");
  }
  static void Case(bool baseline,bool repeat,bool hooks,int group,bool specific)
  {
   Vehicles.Clear(); Flows.Clear(); FlowParts.Clear(); Leaves.Clear(); KnownExternalParts.Clear(); MastRuntime.States.Clear();
   IoRegistry.ClearForTests();IoApiCore.ClearForTests(); IoRegistry.RegisterInput(new CannonFiredInput());IoRegistry.RegisterInput(new VehicleBrakingInput());
   var calls=new List<(GameObject Part,bool On)>();
   IoApi.RegisterManagedOutput("visualeffects.lamp","Lamp","vfxLamp",(p,on)=>calls.Add((p,on)));
   Time.unscaledTime+=10; Time.time=Time.unscaledTime; DesignerMode=true;
   var gateway=new Vehicle {Pointer=(IntPtr)10001};
   VehicleObject Part(Vehicle root,int id,string tag,string component="") {
    var p=new VehicleObject {Id=id,Pointer=(IntPtr)id,Vehicle=root,Tag=tag};
    if(component!="") p.Components.Values.Add(new VehicleComponent{Id=id+100,ComponentID=component,VehicleObject=p});
    root.ObjectReader.Items.Values.Add(p); RegisterFlowPart(p);
    if(tag==MastRuntime.PartTag) MastRuntime.States[id]=new(){Object=p};
    return p;
   }
   var parts=new[]{Part(gateway,1,MastRuntime.BaseTag),Part(gateway,2,MastRuntime.PartTag),Part(gateway,3,MastRuntime.PartTag),Part(gateway,4,"","vfxLamp")};
   parts[1].VehicleTransform.Base=parts[0];parts[2].VehicleTransform.Base=parts[0];
   PrepareFlows();var panel=PanelFlow(true,null)!;
   var flow=panel.Flow;flow.Nodes.Clear();flow.Wires.Clear();
   var fired=new FlowNode("fire","cannon.fired");fired.Set("fireGroup",group.ToString());flow.Nodes.Add(fired);
   foreach(var p in parts.Skip(1).Take(2)) { var node=new FlowNode("mast"+p.Id,"mast");node.Set("partId",PartId(p));flow.Nodes.Add(node);flow.Wires.Add(new("fire",node.Id,0)); }
   var brake=new FlowNode("brake","vehicle.braking");flow.Nodes.Add(brake);
   var lamp=new FlowNode("lamp","visualeffects.lamp");lamp.Set("partId",PartId(parts[3]));flow.Nodes.Add(lamp);flow.Wires.Add(new("brake","lamp",0));flow.Edited();panel.Save();
   var saved=parts.Select(p=>{var b=new Bag();SaveFlow(p,b);return b;}).ToArray();
   var gun=new Cannon{Id=275,Pointer=(IntPtr)(90000+checks),Vehicle=gateway};gun.Behaviour=new(){mount=gun};gun.Behaviour.projectileFactory.Pointer=(IntPtr)80000;
   HingeCannons.Track(gun);HingeCannons.Options(new HashSet<IntPtr>{gateway.Pointer});
   Check(gateway.Behaviour==null&&parts.All(p=>!p.PhysicsEnabled),"real disabled designer");
   if(repeat) {
    // Earlier sandbox ride, as in the supplied log before the rev-8 graph was edited.
    var old=new VehicleBehaviour(500){VehicleInfo=gateway};gateway.Behaviour=old;
    foreach(var p in parts)p.PhysicsEnabled=true;DesignerMode=false;PrepareExternal();PrepareFlows();
    var prior=Vehicles[old.Pointer];for(int i=0;i<100;i++){Time.frameCount++;Time.unscaledTime+=Time.deltaTime;Time.time=Time.unscaledTime;TickVehicle(prior);}
    old.WasCollected=true;gateway.Behaviour=null;foreach(var p in parts)p.PhysicsEnabled=false;DesignerMode=true;
    PrepareExternal();PrepareFlows();
   }
   var player=new VehicleBehaviour(501){VehicleInfo=gateway};gateway.Behaviour=player;
   foreach(var p in parts)p.PhysicsEnabled=true;DesignerMode=false;
   if(hooks) {HingeCannons.Track(gun);for(int i=0;i<parts.Length;i++)LoadFlow(parts[i],saved[i]);}
   // Fresh target copy has identical saved ioPartIds, but a distinct gateway and behaviour.
   var copy=new VehicleBehaviour(601);copy.ControlType=Sprocket.Vehicles.Control.ControlType.AI;
   var cp=new[]{Part(copy.VehicleInfo,11,MastRuntime.BaseTag),Part(copy.VehicleInfo,12,MastRuntime.PartTag),Part(copy.VehicleInfo,13,MastRuntime.PartTag),Part(copy.VehicleInfo,14,"","vfxLamp")};
   cp[1].VehicleTransform.Base=cp[0];cp[2].VehicleTransform.Base=cp[0];
   for(int i=0;i<cp.Length;i++){cp[i].PhysicsEnabled=true;LoadFlow(cp[i],saved[i]);}
   PrepareExternal();PrepareFlows();
   var pi=Vehicles[player.Pointer];var ci=Vehicles[copy.Pointer];
   if(specific) { fired.Set("cannon",PartSelector.ForVuid(275)); panel.Save(); }
   Check(SessionFor(pi)!=SessionFor(ci),"separate sessions despite identical ioPartIds");
   void Tick(VehicleIo io,double now) {Time.frameCount++;Time.unscaledTime+=Time.deltaTime;Time.time=Time.unscaledTime;TickVehicle(io);}
   // Native resolution is deliberately throttled to 2 seconds. Let spawn settle before firing.
   for(int i=0;i<30;i++){Tick(pi,.1);Tick(ci,.1);}
   Check(pi.Context.Cannons.Count==1,"retained cannon registry resolves current player without Build rerun");
   player.Braking=true;
   int receipts=Plugin.Traces.Count(t=>t.StartsWith("[FLOW] event"));
   HingeFire.BeginFire(gun.Behaviour);gun.Behaviour.projectileFactory.nextID++;HingeFire.EndFire(gun.Behaviour);
   Tick(pi,.2);Tick(ci,.2);
   Check(pi.Context.Fired.Count==1&&ci.Context.Fired.Count==0,"fire event reaches only player context even when old engine stalls");
   bool expected=group!=1||specific;bool stalled=baseline&&repeat;
   Check(MastRuntime.States[2].Control.Direction==(expected&&!stalled?1:-1),"player cannon mask 8 actual mast direction");
   Check(MastRuntime.States[3].Control.Direction==(expected&&!stalled?1:-1),"second Separate insert bound");
   Check(MastRuntime.States[12].Control.Direction==-1&&MastRuntime.States[13].Height==0,"copy receives no player shot");
   Check(calls.Any(c=>c.Part==parts[3].Components[0].gameObject&&c.On)==!stalled,"retained Brake lamp apply(true)");
   if(expected&&!stalled) Check(MastRuntime.States[2].Height>0&&MastRuntime.States[3].Height>0,"both inserts actually step upward");
   Tick(pi,.3); Check(MastRuntime.States[2].Control.Direction==-1,"direct event remains one tick; saved semantics unchanged");
   if(!baseline) {
    Check(Plugin.Traces.Count(t=>t.StartsWith("[FLOW] event"))==receipts+1,"one receipt per shot, none for idle/copy ticks");
    Check(Plugin.Traces.Last(t=>t.StartsWith("[FLOW] event")).Contains("matched="+(expected?1:0)+" blocks"),"actual production selector match count");
    Check(Plugin.Traces.Any(t=>t.Contains($"event cannon.fired group=4 session={player.Pointer} matched=")),"event receipt diagnostic");
    var edit=new FlowEditor(flow);edit.SelectNode("mast2");
    Check(FlowPresentation.MomentaryToLevel(flow,flow.Wires.First(w=>w.To=="mast2")),"mast direct event warning");
    Check(FlowPresentation.Summary(fired,new FlowEngine()).Contains("one tick"),"visible event footer hint");
    Check(edit.Template(FlowTemplates.MastFired,new[]{new FlowPart(PartId(parts[1]),"mast","Mast 1")}),"production Memory template");panel.Save();
    Time.time+=1;HingeFire.BeginFire(gun.Behaviour);gun.Behaviour.projectileFactory.nextID++;HingeFire.EndFire(gun.Behaviour);
    for(int i=0;i<70;i++)Tick(pi,.4+i*.1);
    Check(MastRuntime.States[2].Height>.99f,"template visibly completes full metre stroke after one shot");
    Check(MastRuntime.States[12].Height==0,"copy remains retracted during Memory run");
    receipts=Plugin.Traces.Count(t=>t.StartsWith("[FLOW] event"));
    Plugin.Detailed=false;Time.time+=1;HingeFire.BeginFire(gun.Behaviour);gun.Behaviour.projectileFactory.nextID++;HingeFire.EndFire(gun.Behaviour);Tick(pi,9);
    Check(Plugin.Traces.Count(t=>t.StartsWith("[FLOW] event"))==receipts,"DetailedLogging false suppresses event diagnostics");Plugin.Detailed=true;
   }
   HingeCannons.Forget(gun);
   Console.WriteLine($"PASS baseline={baseline} priorPlay={repeat} hooksAgain={hooks} group={group} specific={specific} playerStalled={stalled} copyTicked=True");
  }
 }
}

