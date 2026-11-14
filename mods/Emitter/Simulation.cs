using System.Numerics;
namespace SprocketEmitter;

internal sealed class Signal
{
    internal bool Initialized,On;
    private float elapsed;
    private bool armed;
    internal void Reset(){Initialized=false;On=false;elapsed=0;armed=false;}
    // playing=false (designer, release): never records a baseline. Mast sends apply(false) on the
    // return to the designer; storing it would turn the next Play baseline into a rising edge.
    internal bool Apply(bool on,bool playing,bool canThrow) {
        if(!playing){Reset();return false;}
        if(!Initialized){Initialized=true;On=on;elapsed=0;armed=false;return false;}
        bool rise=on&&!On;On=on;if(rise||!on)elapsed=0;if(!on)armed=false;if(rise)armed=canThrow;return rise&&canThrow;
    }
    internal bool Apply(bool on,bool canThrow)=>Apply(on,true,canThrow);
    internal bool Tick(float dt,Settings s) {
        if(!Initialized||!On||!armed||!s.Continuous||dt<=0)return false;
        elapsed+=dt;if(elapsed<s.Interval)return false;
        elapsed%=s.Interval;return true; // at most one batch/frame; no paused or lag backlog
    }
}
internal readonly record struct SurfacePose(Vector3 Position,Quaternion Rotation) {
    internal Vector3 ToWorld(Vector3 p)=>Position+Vector3.Transform(p,Rotation);
    internal Vector3 ToLocal(Vector3 p)=>Vector3.Transform(p-Position,Quaternion.Inverse(Rotation));
}
internal readonly record struct Contact(long Surface,Vector3 Point,Vector3 Normal);
internal interface IWorld
{
    bool Cast(Vector3 from,Vector3 to,float radius,out Contact hit);
    bool Surface(long id,out SurfacePose pose);
    Vector3 ToLocal(long id,Vector3 point);
    Vector3 ToWorld(long id,Vector3 point);
}
internal sealed class Piece
{
    internal bool Active,Resting,Bounced;
    internal long Serial,Owner,Surface;
    internal float Age,Lifetime,Radius,Spin;
    internal Vector3 Position,Velocity,Axis,LocalPoint,LocalNormal;
    internal Quaternion Rotation=Quaternion.Identity,LocalRotation=Quaternion.Identity;
}
internal sealed class Simulation
{
    internal readonly Piece[] Pieces;
    internal float MaxSlope=35,Friction=.65f;
    internal Action<Piece,float>? Landed;
    private long serial;
    private int cursor;
    internal Simulation(int capacity){Pieces=new Piece[Math.Clamp(capacity,1,512)];for(int i=0;i<Pieces.Length;i++)Pieces[i]=new();}
    internal int ActiveCount {get{int n=0;foreach(var p in Pieces)if(p.Active)n++;return n;}}
    internal int Spawn(long owner,Vector3 position,Vector3 velocity,Quaternion rotation,Vector3 axis,float spin,float radius,float lifetime) {
        int slot=0;long oldest=long.MaxValue;
        for(int i=0;i<Pieces.Length;i++){if(!Pieces[i].Active){slot=i;break;}if(Pieces[i].Serial<oldest){oldest=Pieces[i].Serial;slot=i;}}
        var p=Pieces[slot];p.Active=true;p.Resting=p.Bounced=false;p.Surface=0;p.Serial=++serial;p.Owner=owner;p.Age=0;p.Lifetime=lifetime;p.Position=position;p.Velocity=velocity;p.Rotation=rotation;p.Axis=Vector3.Normalize(axis);p.Spin=spin;p.Radius=radius;return slot;
    }
    internal void Clear(){foreach(var p in Pieces)p.Active=false;}
    internal void RemoveOwner(long id){foreach(var p in Pieces)if(p.Owner==id)p.Active=false;}
    internal static float Slope(Vector3 normal)=>MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(normal),Vector3.UnitY),-1,1))*180/MathF.PI;
    internal static Vector3 Cone(float degrees,float u,float v) {
        float z=1-u*(1-MathF.Cos(degrees*MathF.PI/180)),r=MathF.Sqrt(Math.Max(0,1-z*z)),a=v*2*MathF.PI;
        return new(r*MathF.Cos(a),r*MathF.Sin(a),z);
    }
    internal int Tick(float dt,Vector3 gravity,IWorld world,int budget) {
        if(dt<=0||!float.IsFinite(dt))return 0;int casts=0;
        // Lifetimes follow scaled time even when casts are exhausted.
        foreach(var p in Pieces)if(p.Active){p.Age+=dt;if(p.Age>=p.Lifetime)p.Active=false;}
        int start=cursor;cursor=(cursor+1)%Pieces.Length;
        for(int k=0;k<Pieces.Length;k++) {
            var p=Pieces[(start+k)%Pieces.Length];if(!p.Active)continue;
            if(p.Surface!=0) {
                if(world.Surface(p.Surface,out var pose)) {
                    p.Position=world.ToWorld(p.Surface,p.LocalPoint);p.Rotation=Quaternion.Normalize(pose.Rotation*p.LocalRotation);
                    var normal=Vector3.Normalize(Vector3.Transform(p.LocalNormal,pose.Rotation));
                    if(Slope(normal)<=MaxSlope){p.Resting=true;continue;}
                    p.Resting=false; // Re-evaluate every frame when the supporting body tilts.
                    if(casts>=budget)continue;
                    float h=Math.Min(dt,.1f);
                    var tangent=gravity-normal*Vector3.Dot(gravity,normal);
                    p.Velocity=(p.Velocity+tangent*h)*Math.Max(0,1-Friction*h);
                    p.Velocity-=normal*Vector3.Dot(p.Velocity,normal);
                    var next=p.Position+p.Velocity*h;
                    casts++;
                    if(world.Cast(p.Position+normal*p.Radius,next-normal*(p.Radius*3+.02f),p.Radius,out var support))Attach(p,support,world,false);
                    else {p.Surface=0;p.Position=next;}
                    continue;
                }
                p.Surface=0;p.Resting=false;p.Velocity=Vector3.Zero;
            }
            float remaining=Math.Min(dt,.1f);int steps=0;
            while(remaining>0&&steps++<8&&casts<budget) {
                float h=Math.Min(remaining,1f/80);remaining-=h;
                var velocity=p.Velocity+gravity*h;var next=p.Position+velocity*h;
                casts++;
                if(world.Cast(p.Position,next,p.Radius,out var hit)) {
                    p.Velocity=velocity;
                    if(!p.Bounced&&Slope(hit.Normal)<=MaxSlope&&Vector3.Dot(velocity,hit.Normal)<-2) {
                        p.Bounced=true;p.Position=hit.Point+hit.Normal*(p.Radius+.001f);p.Velocity=Vector3.Reflect(velocity,hit.Normal)*.08f;break;
                    }
                    Attach(p,hit,world,true);break;
                }
                p.Position=next;p.Velocity=velocity;
                if(p.Spin!=0)p.Rotation=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(p.Axis,p.Spin*h*MathF.PI/180)*p.Rotation);
            }
        }
        return casts;
    }
    private void Attach(Piece p,Contact hit,IWorld world,bool notify) {
        p.Position=hit.Point+hit.Normal*(p.Radius+.001f);
        // Long axis lies in the plane, preserving its heading where possible.
        var forward=Vector3.Transform(Vector3.UnitZ,p.Rotation);forward-=hit.Normal*Vector3.Dot(forward,hit.Normal);
        if(forward.LengthSquared()<.001f)forward=Vector3.Cross(hit.Normal,Vector3.UnitX);
        if(forward.LengthSquared()<.001f)forward=Vector3.Cross(hit.Normal,Vector3.UnitZ);
        forward=Vector3.Normalize(forward);var right=Vector3.Normalize(Vector3.Cross(hit.Normal,forward));
        p.Rotation=Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X,right.Y,right.Z,0,hit.Normal.X,hit.Normal.Y,hit.Normal.Z,0,forward.X,forward.Y,forward.Z,0,0,0,0,1));
        float slope=Slope(hit.Normal);p.Resting=slope<=MaxSlope;
        if(p.Resting)p.Velocity=Vector3.Zero;
        p.Surface=hit.Surface;
        if(world.Surface(hit.Surface,out var pose)){p.LocalPoint=world.ToLocal(hit.Surface,p.Position);p.LocalNormal=Vector3.Transform(hit.Normal,Quaternion.Inverse(pose.Rotation));p.LocalRotation=Quaternion.Inverse(pose.Rotation)*p.Rotation;}
        else {p.Surface=0;p.Resting=false;}
        if(notify)Landed?.Invoke(p,slope);
    }
}
