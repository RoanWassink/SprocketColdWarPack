// Controlled API doubles: these do NOT load Unity, IL2CPP or native game code.
// They exist only so the real mod source can be linked and exercised in a plain
// .NET process. They are not evidence about the real game's runtime behaviour.
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public class HarmonyPatch:Attribute { public HarmonyPatch(){} public HarmonyPatch(Type t,string m){} }
 public class HarmonyPrefix:Attribute {} public class HarmonyPostfix:Attribute {} public class HarmonyFinalizer:Attribute {}
}
namespace Il2CppInterop.Runtime.InteropTypes {
 public class Il2CppObjectBase { public IntPtr Pointer {get;set;} public T? TryCast<T>() where T:class => this as T; }
}
namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Type Of<T>()=>typeof(T); } }
namespace UnityEngine {
 public struct Quaternion { public static Quaternion identity=>default; }
 public class Transform {
  public Transform? parent; public Transform root=>parent?.root??this; public Quaternion rotation;
  public Dictionary<Type,Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase> Components=new();
  public Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? GetComponent(Type t)=>Components.GetValueOrDefault(t);
 }
}
namespace Sprocket {
 public class TechFrame { public bool Enabled=true; }
 public class Vehicle { public TechFrame Tech=new(); public IntPtr Pointer; }
 public class NativePose { public UnityEngine.Quaternion Rotation; }
 public class Root { public NativePose VehicleTransform=new(); }
 public class VehicleObject { public HashSet<string> Tags=new(); public bool HasTag(string value)=>Tags.Contains(value); public bool Released; }
 public record Id(int Value);
}
namespace Sprocket.Blueprints {
 public interface IBlueprintSerializer { void AddValue(string key,string value); }
 public interface IBlueprintDeserializer { string GetString(string key); }
 public class Blueprint:Il2CppSystem.Object {
  public string Name="";
  public void Save(IBlueprintSerializer serializer){}
  public void Load(IBlueprintDeserializer deserializer){}
  public void MarkModified(){}
  public static void Copy(Blueprint source,Blueprint target){}
  public Il2CppSystem.Object Clone()=>this;
 }
 public class BlueprintSlot<T> where T:class { public T? Blueprint; public bool HasBlueprint=>Blueprint!=null; }
}
namespace Sprocket.Vehicles {
 public class MotorBlueprint:Sprocket.Blueprints.Blueprint { public float Torque; public float Ratio; }
}
namespace Sprocket.Vehicles {
 public class VehicleComponent:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
  public Sprocket.Id VUID=new(0); public Sprocket.Vehicle Vehicle=new(); public Sprocket.Root VehicleRoot=new();
  public UnityEngine.Transform transform=new(); public Sprocket.VehicleObject VehicleObject=new();
  public void SaveData(Il2CppSystem.Runtime.Serialization.SerializationInfo info){}
  public void LoadData(Il2CppSystem.Runtime.Serialization.SerializationInfo info){}
  public void ReleaseInternal(){}
 }
}
namespace Sprocket.Vehicles.Turrets {
 public class TraverseMotor:Sprocket.Vehicles.VehicleComponent {
  public bool Connected=true;
  public Sprocket.Blueprints.BlueprintSlot<Sprocket.Vehicles.MotorBlueprint> MotorBlueprintSlot=new();
 }
 public class TurretRing:Sprocket.Vehicles.VehicleComponent { public TraverseMotor? motor; }
}
namespace Sprocket.Vehicles.Weapons {
 public class LayingDrive:Sprocket.Vehicles.VehicleComponent {}
 public class TurretBehaviour:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {
  public UnityEngine.Transform transform=new();
  public float MotorTorqueMultiplier {get;set;} public float MaxTraverseRate {get;set;}
  public float MaxBrakeAcceleration {get;set;}
  private float _acceleration;
  // Controlled fault injection for a partial-prefix failure.
  public bool ThrowOnAccelerationSet;
  public float MaxAcceleration {
   get=>_acceleration;
   set { if(ThrowOnAccelerationSet) throw new InvalidOperationException("controlled acceleration setter failure"); _acceleration=value; }
  }
  public void MoveToTarget(){}
 }
}
namespace Il2CppSystem {
 public class Object:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {}
}
namespace Il2CppSystem.Runtime.Serialization {
 public class SerializationInfo {
  // Real IL2CPP info is typed bool; object here lets a probe inject a malformed value.
  public Dictionary<string,object?> Values=new();
  public void AddValue(string k,bool v)=>Values[k]=v;
  public bool GetBoolean(string k){
   if(!Values.TryGetValue(k,out var v)||v is not bool b) throw new InvalidOperationException("missing or malformed serialized key");
   return b;
  }
 }
}
namespace Sprocket.Motors {
 public class TraverseMotorBlueprint:Sprocket.Vehicles.MotorBlueprint { public float MinAngle; public float MaxAngle; public bool Radial; }
}
namespace SprocketStabilization {
 internal class Entry<T>(T value) { public T Value=value; }
 internal class Logger { public void LogInfo(string s){} public void LogWarning(string s){} }
 internal static class Plugin {
  internal static Entry<bool> StabilizerEnabled=new(true); internal static Entry<bool> OverrideRateLimits=new(true); internal static Logger ModLog=new();
  internal static Entry<bool> RealisticLimits=new(true);
  internal static Entry<float> HorizontalMaxTraverseRate=new(24),ElevationMaxTraverseRate=new(6),ResponseTimeSeconds=new(0.75f);
  internal static Entry<float> BoostTorqueMultiplier=new(100),BoostMaxTraverseRate=new(360),BoostMaxAcceleration=new(3600),BoostMaxBrakeAcceleration=new(3600);
 }
 // Model only the bool returned by the real TryRead; the probes vary that input.
 internal static class StabilizationTechnology {
  internal static bool TryRead(Sprocket.TechFrame? frame,out object settings){settings=new();return frame?.Enabled==true;}
 }
}
