using Sprocket.Vehicles;
using SprocketVisualEffects;
int checks=0;
void Check(bool b,string text){checks++;if(!b)throw new Exception(text);}
VehicleObjectModel Valid(){var v=new Gateway();var o=new VehicleObject{Vehicle=v};o.VehicleTransform=new VehicleTransform{VehicleObject=o};return new(){VehicleObject=o,Vehicle=v};}
var valid=Valid();
Check(LampEligibility.Check(valid)==0,"installed lamp accepted");
Check(LampEligibility.Check(null)<0,"missing model");
foreach(var flag in new[]{VehicleObjectFlags.ActivePlacement,VehicleObjectFlags.ActivePlacementChild,VehicleObjectFlags.NeverSave,VehicleObjectFlags.DontSaveWithVehicle}){
 var lamp=Valid();lamp.VehicleObject!.Flags|=flag;
 Check(LampEligibility.Check(lamp)<0,"reject own preview flag "+flag);
 lamp=Valid();var ancestor=new VehicleObject{Vehicle=lamp.Vehicle,Flags=flag};var root=new VehicleTransform{VehicleObject=ancestor};
 lamp.VehicleObject!.VehicleTransform!.Parent=new(){VehicleObject=new(){Vehicle=lamp.Vehicle},Parent=root};
 Check((LampEligibility.Check(lamp)<0)==(flag==VehicleObjectFlags.ActivePlacement || flag==VehicleObjectFlags.ActivePlacementChild),"ancestor placement vs serialization flag "+flag);
}
var wrong=Valid();wrong.ComponentID="other";Check(LampEligibility.Check(wrong)<0,"exact component");
wrong=Valid();wrong.VehicleObject!.GUID="other";Check(LampEligibility.Check(wrong)<0,"exact GUID");
wrong=Valid();wrong.IsInstalled=false;Check(LampEligibility.Check(wrong)==0,"plain model behaviour bit is not object placement");
wrong=Valid();wrong.VehicleObject!.Flags=0;Check(LampEligibility.Check(wrong)<0,"not installed object");
wrong=Valid();wrong.VehicleObject=null;Check(LampEligibility.Check(wrong)<0,"missing owner");
wrong=Valid();wrong.Vehicle=null;Check(LampEligibility.Check(wrong)<0,"missing vehicle");
wrong=Valid();wrong.VehicleObject!.Vehicle=null;Check(LampEligibility.Check(wrong)<0,"missing owner vehicle");
wrong=Valid();wrong.VehicleObject!.Released=true;Check(LampEligibility.Check(wrong)<0,"released owner");
wrong=Valid();wrong.Vehicle=new Gateway{Pointer=(IntPtr)99};Check(LampEligibility.Check(wrong)<0,"different owner vehicle");
wrong=Valid();wrong.VehicleObject!.VehicleTransform=null;Check(LampEligibility.Check(wrong)<0,"missing transform");
wrong=Valid();wrong.VehicleObject!.VehicleTransform!.Parent=wrong.VehicleObject.VehicleTransform;Check(LampEligibility.Check(wrong)<0,"cycle fails closed");
wrong=Valid();wrong.VehicleObject!.VehicleTransform!.Parent=new(){VehicleObject=new(){Vehicle=new Gateway{Pointer=(IntPtr)99}}};Check(LampEligibility.Check(wrong)<0,"foreign ancestor");
for(int form=0;form<3;form++){var lamp=Valid();lamp.VehicleObject!.GUID=LampSettings.Guids[form];Check(LampEligibility.Check(lamp)==form,"stable form "+form);}
valid.VehicleObject!.Flags|=VehicleObjectFlags.ActivePlacement;Check(LampEligibility.Check(valid)<0,"cached instance becomes ghost");
// Actual attachment shapes, including anonymous structural transform nodes.
foreach(var shape in new[]{"hull","turret","rotator","hinge","mirror","duplicate"}) {
 var lamp=Valid();lamp.IsInstalled=false;
 if(shape=="mirror")lamp.VehicleObject!.Flags|=(VehicleObjectFlags)32|(VehicleObjectFlags)2048;
 var parent=new VehicleObject{Vehicle=lamp.Vehicle,Flags=VehicleObjectFlags.DontSaveWithVehicle|VehicleObjectFlags.NeverSave};
 lamp.VehicleObject!.VehicleTransform!.Parent=new(){VehicleObject=parent,Parent=new(){VehicleObject=null}};
 Check(LampEligibility.Check(lamp)==0,"installed "+shape+" via structural parent");
 parent.Flags|=VehicleObjectFlags.ActivePlacement;
 Check(LampEligibility.Check(lamp)<0,"moving parent "+shape+" rejected");
 parent.Flags&=~VehicleObjectFlags.ActivePlacement;
 Check(LampEligibility.Check(lamp)==0,"reinstalled parent "+shape+" accepted");
}
Logger.Messages.Clear();UnityEngine.SceneManagement.SceneManager.Handle++;
var rejectedLamp=Valid();rejectedLamp.VehicleObject!.Flags=0;
for(int i=0;i<50;i++)LampEligibility.Check(rejectedLamp);
Check(Logger.Messages.Count==1,"one rejection per part pointer");
for(int i=0;i<50;i++){var x=Valid();x.VehicleObject!.Flags=0;LampEligibility.Check(x);}
Check(Logger.Messages.Count==20,"20 rejection lines per scene");
UnityEngine.SceneManagement.SceneManager.Handle++;LampEligibility.Check(rejectedLamp);
Check(Logger.Messages.Count==21,"new scene resets diagnostic budget");
// Fail the real shared entrance with a guard failure, even for an otherwise valid lamp.
var cached=typeof(LampEligibility).GetField("nativeReady",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
cached.SetValue(null,null);NativeGuard.Allow=false;Check(LampEligibility.Check(Valid())<0,"bad native guard fails closed");
Console.WriteLine($"PASS {checks} eligibility checks; production LampEligibility.cs compiled against fake native readers. No live lifecycle claim.");
namespace Sprocket.Vehicles {
 [Flags] public enum VehicleObjectFlags {Installed=0x80,ActivePlacement=0x200,ActivePlacementChild=0x40000,DontSaveWithVehicle=2,NeverSave=0x20000}
 public class Gateway {public IntPtr Pointer=(IntPtr)1;}
 public class VehicleObject {
  public static IntPtr NativeMethodInfoPtr_get_Flags_0,NativeMethodInfoPtr_get_Vehicle_0,NativeMethodInfoPtr_get_VehicleTransform_0;
  private static long next;public IntPtr Pointer=(IntPtr)Interlocked.Increment(ref next);public bool Released;public Gateway? Vehicle;public string GUID=LampSettings.Guids[0];public VehicleObjectFlags Flags=VehicleObjectFlags.Installed;public VehicleTransform? VehicleTransform;
 }
 public class VehicleTransform {public static IntPtr NativeMethodInfoPtr_get_Parent_0,NativeMethodInfoPtr_get_VehicleObject_0;public VehicleTransform? Parent;public VehicleObject? VehicleObject;}
 public class VehicleObjectModel {public IntPtr Pointer=(IntPtr)100;public string ComponentID="vfxLamp";public VehicleObject? VehicleObject;public Gateway? Vehicle;public bool IsInstalled=true;}
}
namespace SprocketVisualEffects {
 internal static class LampSettings {internal static string[] Guids={"7b696e26-98bd-4e6d-ad6a-9009814b0c01","7b696e26-98bd-4e6d-ad6a-9009814b0c02","7b696e26-98bd-4e6d-ad6a-9009814b0c03"};}
 internal static class NativeGuard {internal record struct Result(bool Ok,string Detail);internal static bool Allow=true;internal static Result Check(Type t,string f,string n,int p)=>new(Allow,"fake");}
 internal class Plugin {internal static Plugin Instance=new();internal Logger Log=new();}
 internal class Logger {internal static List<string> Messages=new();internal void LogInfo(string s){if(s.StartsWith("Lamp REJECT"))Messages.Add(s);}}
}

namespace UnityEngine.SceneManagement {public static class SceneManager {public static int Handle;public static Scene GetActiveScene()=>new();}public class Scene {public int handle=>SceneManager.Handle;}}
