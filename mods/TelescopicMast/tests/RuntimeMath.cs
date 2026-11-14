// Native boundary doubles; math uses System.Numerics rather than reimplementing mod motion.
namespace UnityEngine
{
 public struct Vector3
 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  internal System.Numerics.Vector3 N=>new(x,y,z);internal static Vector3 From(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
  public static Vector3 zero=>new();public static Vector3 one=>new(1,1,1);public static Vector3 up=>new(0,1,0);public static Vector3 right=>new(1,0,0);public static Vector3 forward=>new(0,0,1);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>From(a.N+b.N);public static Vector3 operator -(Vector3 a,Vector3 b)=>From(a.N-b.N);public static Vector3 operator *(Vector3 a,float s)=>From(a.N*s);
  public static float Distance(Vector3 a,Vector3 b)=>System.Numerics.Vector3.Distance(a.N,b.N);
  public override string ToString()=>N.ToString();public string ToString(string f)=>ToString();
 }
 public struct Quaternion
 {
  public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
  internal System.Numerics.Quaternion N=>new(x,y,z,w);internal static Quaternion From(System.Numerics.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
  public static Quaternion identity=>new(0,0,0,1);public static Quaternion Inverse(Quaternion q)=>From(System.Numerics.Quaternion.Inverse(q.N));
  public Vector3 eulerAngles=>Vector3.zero; // Diagnostic formatting only; assertions compare quaternions.
  public static Quaternion AngleAxis(float degrees,Vector3 axis)=>From(System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.Normalize(axis.N),degrees*MathF.PI/180));
  public static Quaternion operator *(Quaternion a,Quaternion b)=>From(a.N*b.N);public static Vector3 operator *(Quaternion q,Vector3 p)=>Vector3.From(System.Numerics.Vector3.Transform(p.N,q.N));
  public static float Angle(Quaternion a,Quaternion b){double dot=(double)a.x*b.x+(double)a.y*b.y+(double)a.z*b.z+(double)a.w*b.w;double aa=(double)a.x*a.x+(double)a.y*a.y+(double)a.z*a.z+(double)a.w*a.w;double bb=(double)b.x*b.x+(double)b.y*b.y+(double)b.z*b.z+(double)b.w*b.w;return (float)(2*Math.Acos(Math.Clamp(Math.Abs(dot)/Math.Sqrt(aa*bb),0,1))*180/Math.PI);}
 }
}
namespace Sprocket.Vehicles
{
 public class VehicleTransform : Native
 {
  public VehicleObject? VehicleObject;public VehicleTransform? Parent,PlaceTarget;public object? Base;
  public UnityEngine.Vector3 Position,LocalPosition,Scale=UnityEngine.Vector3.one;
  public UnityEngine.Quaternion Rotation=UnityEngine.Quaternion.identity,LocalRotation=UnityEngine.Quaternion.identity;
  private (int Value,int unused) mirror=(-1,0);public bool MirrorUnreadable;
  public (int Value,int unused) MirrorVUID {get=>MirrorUnreadable?throw new InvalidOperationException("native mirror unavailable"):mirror;set=>mirror=value;}public int Notifications;
  public int GetInstanceID()=>VehicleObject?.GetInstanceID()??Pointer.ToInt32();public void NotifyTransformChanged()=>Notifications++;
 }
}
