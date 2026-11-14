using SprocketTelescopicMast.Io;
namespace UnityEngine { public class GameObject { } public static class Time { public static float unscaledTime; }
    public class Object { public static T[] FindObjectsOfType<T>() => Array.Empty<T>(); } }
namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Il2CppSystem.Type Of<T>() => new(); } }
namespace Il2CppSystem { public class Type { } }
namespace Il2CppSystem.Runtime.Serialization
{
    public class SerializationInfo
    {
        public readonly List<string> m_members = new();
        public readonly Dictionary<string,string> Values = new();
        public int MemberCount => m_members.Count;
        public string GetString(string key) => Values[key];
        public void UpdateValue(string key, string value, Il2CppSystem.Type type) => Values[key] = value;
        public void AddValue(string key, string value, Il2CppSystem.Type type) { m_members.Add(key); Values.Add(key,value); }
        public Enumerator GetEnumerator() => new(this);
        public class Enumerator
        {
            private readonly SerializationInfo info; private int at = -1;
            public Enumerator(SerializationInfo info) { this.info = info; }
            public bool MoveNext() => ++at < info.MemberCount;
            public string Name => info.m_members[at];
        }
    }
}
namespace Il2CppSystem.Collections.Generic { public interface IReadOnlyCollection<T> { int Count { get; } } }
namespace Sprocket.Vehicles.Control { public enum ControlType { Player, AI } }
namespace Sprocket.Vehicles
{
    public class Native
    {
        public bool WasCollected; public IntPtr Pointer;
        public T? TryCast<T>() where T : class => this as T;
        public T Cast<T>() => (T)(object)this;
    }
    public class NativeList<T> : Native, Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>
    {
        public readonly List<T> Values = new(); public int Count => Values.Count;
        public T this[int i] => Values[i];
    }
    public class IVehicleObjectReader : Native { public NativeList<VehicleObject> Items = new(); }
    public class Reader : IVehicleObjectReader
    {
        // Behaviour GetAll searches components; VehicleObject is not a component.
        public T[] GetAll<T>() => Items.Values.SelectMany(o => o.Components.Values).OfType<T>().ToArray();
    }
    public class Vehicle : Native { public Native? Behaviour; public Reader ObjectReader = new(); public UnityEngine.GameObject gameObject = new(); }
    public class VehicleBehaviour : Native
    {
        public Vehicle VehicleInfo = new(); public UnityEngine.GameObject gameObject = new();
        public Control.ControlType ControlType = Control.ControlType.Player;
        public VehicleBehaviour(int id) { Pointer = (IntPtr)id; VehicleInfo.Pointer = (IntPtr)(id + 10000); VehicleInfo.Behaviour = this; }
    }
    public class VehicleObject : Native
    {
        public VehicleTransform VehicleTransform=new(); public string GUID=""; public bool Released, PhysicsEnabled; public int Id; public string Name = "Round lamp";
        public (int Value, int unused) VUID => (Id, 0);
        public Vehicle? Vehicle; public NativeList<VehicleComponent> Components = new();
        public int GetInstanceID() => Id; public bool HasTag(string tag) => false;
    }
    public class VehicleComponent : Native
    {
        public VehicleObject VehicleObject = null!; public string ComponentID = ""; public int Id;
        public UnityEngine.GameObject gameObject = new(); public Vehicle? Vehicle => VehicleObject.Vehicle;
        public int GetInstanceID() => Id;
    }
}
namespace SprocketTelescopicMast
{
    using Sprocket.Vehicles;
    using Sprocket.Vehicles.Control;
    internal sealed class FakeLog { internal void LogWarning(string message) => Plugin.Traces.Add(message); }
    internal sealed class Plugin
    {
        internal static bool Detailed = true; internal static Plugin Instance = new(); internal FakeLog Log = new();
        internal static List<string> Traces = new(); internal static void Trace(string message) => Traces.Add(message);
    }
    internal static class HingePanel { internal static bool IsOpen = true; }
    internal static class HingeFire { internal static void Discard(IntPtr p) { } }
    internal static class HingeCannons
    { internal static ParamOption[] Options(HashSet<IntPtr> roots, Dictionary<string,int> groups, Dictionary<string,int> masks) => Array.Empty<ParamOption>(); }
    internal static class MastRuntime
    {
        internal const string Piston="piston", Sleeve="sleeve", BaseModel="base", PartModel="part", Tag="mast", BaseTag="base", PartTag="part";
        internal static bool IsFlowOutput(VehicleObject o) => false;
        internal static void FlowCommand(VehicleObject o, bool b) { } internal static void UnbindFlow(VehicleObject o) { }
        internal static void TickFlow(IntPtr p) { } internal static double FlowPosition(VehicleObject o) => 0;
    }
    internal static class RotatorRuntime
    {
        internal const string BaseTag="rotatorBase", BaseModel="rbase", PlateModel="plate";
        internal static bool IsPart(VehicleObject o) => false; internal static bool Has(VehicleObject o) => false;
        internal static bool IsPlate(VehicleObject o) => false; internal static void Unbind(IntPtr p) { }
        internal static void Bind(VehicleObject o, FlowNode n, FlowEngine e) { } internal static double Angle(VehicleObject o) => 0;
    }
    public class VehicleIoContext : IVehicleSignals, IExternalVehicleContext
    {
        public bool IsPlayer => true; public bool Braking { get; set; } public object? VehicleToken { get; set; }
        public IReadOnlyList<CannonShot> Fired => Array.Empty<CannonShot>(); public IReadOnlyList<CannonState> Cannons => Array.Empty<CannonState>();
        public IReadOnlyList<HingeState> Hinges => Array.Empty<HingeState>(); public IReadOnlyList<MastState> Masts => Array.Empty<MastState>();
        public int KeyPresses(int action) => 0; public bool KeyHeld(int action) => false;
    }
    internal static partial class HingeRuntime
    {
        internal const string BaseTag="hingeBase", LeafModel="leaf", MountModel="mount";
        private static readonly Dictionary<int, NativeFlowPart> FlowParts = new();
        private static readonly Dictionary<IntPtr, FlowSession> Flows = new();
        private static readonly Dictionary<IntPtr, VehicleIo> Vehicles = new();
        private static readonly Dictionary<int, Leaf> Leaves = new();
        private static readonly HashSet<string> Warnings = new();
        internal static int ConfigRevision; private static int ExtTraces;
        internal sealed class Leaf
        {
            internal bool IsMount; internal VehicleObject Object = null!; internal bool Alive => true; internal int Vuid; internal float Angle;
            internal HingeConfig Config = new(); internal FlowNode? FlowNode; internal string PartId = "";
            internal string DisplayName(int n) => "Hinge";
        }
        private static string PartNameOf(VehicleComponent c) => c.VehicleObject.Name;
        internal static void RefreshVuid(Leaf l) { } internal static bool IsMoving(VehicleObject o) => false;
        private static void RefreshMembership() { }
        private static void DiscoverVehicles()
        { foreach (var v in UnityEngine.Object.FindObjectsOfType<VehicleBehaviour>()) Vehicles.TryAdd(v.Pointer, new VehicleIo { Vehicle = v }); }
        private const string StateKey = "hingeIoV1";
        private static bool InControl() => !DesignerMode; private static bool InDesigner() => DesignerMode;
        internal static bool DesignerMode = true;
        private static void WarnOnce(string key, string message) { if (Warnings.Add(key)) Plugin.Traces.Add(message); }
        private static void Warn(string message, Exception ex) => Plugin.Traces.Add(message + ex.Message);
        internal static VehicleIo? IoFor(IntPtr p) => Vehicles.GetValueOrDefault(p);
    }
}
