namespace SprocketEngineExpansion.Core;

public readonly record struct Vec3(double X,double Y,double Z)
{
    public static Vec3 operator +(Vec3 a,Vec3 b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    public static Vec3 operator -(Vec3 a,Vec3 b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static Vec3 operator *(Vec3 a,double s)=>new(a.X*s,a.Y*s,a.Z*s);
    public static Vec3 Cross(Vec3 a,Vec3 b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    public static double Dot(Vec3 a,Vec3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static Vec3 UnitY=>new(0,1,0);
    public double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
    public Vec3 Normalized{get{var length=Length;return length>1e-12?this*(1/length):new(0,0,0);}}
}
public readonly record struct Quat(double X,double Y,double Z,double W)
{
    public static Quat AxisY(double degrees){double h=degrees*Math.PI/360;return new(0,Math.Sin(h),0,Math.Cos(h));}
    // Same convention as Unity's Quaternion * Vector3.
    public Vec3 Rotate(Vec3 v){var q=new Vec3(X,Y,Z);var t=Vec3.Cross(q,v)*2;return v+t*W+Vec3.Cross(q,t);}
    // Unity's Quaternion * Quaternion and Quaternion.Inverse (unit quaternions).
    public static Quat operator *(Quat a,Quat b)=>new(
        a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,
        a.W*b.Y+a.Y*b.W+a.Z*b.X-a.X*b.Z,
        a.W*b.Z+a.Z*b.W+a.X*b.Y-a.Y*b.X,
        a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z);
    public Quat Inverse=>new(-X,-Y,-Z,W);
}

/// <summary>
/// A Unity Transform reduced to position, rotation and lossy scale. Native
/// VehiclePowertrain.SetModuleTransform/ModuleTransformUpdate convert world to layout space with the
/// VehiclePowertrain's own Transform (InverseTransformPoint, Inverse(rotation) * rotation), so layout
/// space is this frame for that Transform.
/// </summary>
public readonly record struct Frame(Vec3 Position,Quat Rotation,Vec3 Scale)
{
    public static Frame Identity=>new(new Vec3(0,0,0),new Quat(0,0,0,1),new Vec3(1,1,1));
    public Vec3 TransformPoint(Vec3 local)=>Position+Rotation.Rotate(new Vec3(local.X*Scale.X,local.Y*Scale.Y,local.Z*Scale.Z));
    public Vec3 InverseTransformPoint(Vec3 world)
    {
        var p=Rotation.Inverse.Rotate(world-Position);
        return new(p.X/Scale.X,p.Y/Scale.Y,p.Z/Scale.Z);
    }
    /// <summary>A world box into this frame: centre, rotation and size.</summary>
    public OrientedBox ToLocal(OrientedBox world)=>new(
        InverseTransformPoint(world.Centre),
        new Vec3(world.Size.X/Scale.X,world.Size.Y/Scale.Y,world.Size.Z/Scale.Z),
        Rotation.Inverse*world.Rotation);
    public OrientedBox ToWorld(OrientedBox local)=>new(
        TransformPoint(local.Centre),
        new Vec3(local.Size.X*Scale.X,local.Size.Y*Scale.Y,local.Size.Z*Scale.Z),
        Rotation*local.Rotation);
}

public enum ModuleKind:byte { Engine=1, Transmission=2, Junction=4, Propulsion=8 }
public enum PowerpackStatus { Ready, NoDirectTransmission, ViaJunction, Conflict }
public sealed record LayoutModule(int Id,ModuleKind Kind,short Flags,int MirrorId=-1);
// One record per layout connection, resolved to the modules of its provider and receiver ports.
public sealed record LayoutConnection(int Id,int ProviderModule,int ReceiverModule,byte Flags);
public sealed record PowerpackPlan(PowerpackStatus Status,int TransmissionId=-1,int ConnectionId=-1,bool Marked=false,bool Partial=false);
/// <summary>
/// Twin transmission: engine output -> splitter junction -> two transmissions.
/// The plan names both modules and all three connections that the powerpack owns.
/// </summary>
public sealed record TwinPowerpackPlan(
    PowerpackStatus Status,
    int EngineId=-1,
    int JunctionId=-1,
    int LeftTransmissionId=-1,
    int RightTransmissionId=-1,
    int EngineJunctionConnectionId=-1,
    int LeftConnectionId=-1,
    int RightConnectionId=-1,
    bool Marked=false,
    bool Partial=false)
{
    public int[] ModuleIds=>new[]{LeftTransmissionId,RightTransmissionId};
    public int[] ConnectionIds=>new[]{EngineJunctionConnectionId,LeftConnectionId,RightConnectionId};
}
/// <summary>
/// Full transmission obstruction box. CentreX/HalfWidth are the local-X face the old placement used;
/// Centre/Size are all three axes of the native box collider, in module-local space.
/// </summary>
public readonly record struct TransmissionExtent(double CentreX,double HalfWidth,Vec3 Centre,Vec3 Size)
{
    public TransmissionExtent(double centreX,double halfWidth)
        :this(centreX,halfWidth,new Vec3(centreX,0,0),new Vec3(halfWidth*2,0,0)){}
    /// <summary>
    /// The extent of a collider box given in module-local space. An axis-aligned box keeps its centre
    /// and size exactly; a box turned against the module axes becomes its enclosing local AABB.
    /// </summary>
    public static TransmissionExtent FromModuleBox(OrientedBox local)
    {
        var half=local.Size*.5;
        var ax=local.AxisX;var ay=local.AxisY;var az=local.AxisZ;
        var size=new Vec3(
            2*(Math.Abs(ax.X)*half.X+Math.Abs(ay.X)*half.Y+Math.Abs(az.X)*half.Z),
            2*(Math.Abs(ax.Y)*half.X+Math.Abs(ay.Y)*half.Y+Math.Abs(az.Y)*half.Z),
            2*(Math.Abs(ax.Z)*half.X+Math.Abs(ay.Z)*half.Y+Math.Abs(az.Z)*half.Z));
        return new(local.Centre.X,size.X*.5,local.Centre,size);
    }
}

/// <summary>
/// Full oriented box: Size is the full extent on all three axes, Centre and Rotation are world space.
/// </summary>
public readonly record struct OrientedBox(Vec3 Centre,Vec3 Size,Quat Rotation)
{
    public Vec3 AxisX=>Rotation.Rotate(new Vec3(1,0,0));
    public Vec3 AxisY=>Rotation.Rotate(new Vec3(0,1,0));
    public Vec3 AxisZ=>Rotation.Rotate(new Vec3(0,0,1));
    public Vec3[] Corners()
    {
        var half=Size*.5;var corners=new Vec3[8];int i=0;
        foreach(double x in new[]{-half.X,half.X})
        foreach(double y in new[]{-half.Y,half.Y})
        foreach(double z in new[]{-half.Z,half.Z})
            corners[i++]=Centre+Rotation.Rotate(new Vec3(x,y,z));
        return corners;
    }
    public double MaxProjection(Vec3 direction)
    {
        double max=double.NegativeInfinity;
        foreach(var corner in Corners())max=Math.Max(max,Vec3.Dot(corner,direction));
        return max;
    }
    public double MinProjection(Vec3 direction)
    {
        double min=double.PositiveInfinity;
        foreach(var corner in Corners())min=Math.Min(min,Vec3.Dot(corner,direction));
        return min;
    }
    /// <summary>
    /// The native box-box narrow phase: 15 separating axes, no epsilon. The native compare is a
    /// strict "greater" (Sprocket.PrimitiveRaycast.BoxIntersection), so touching already overlaps.
    /// </summary>
    public bool Intersects(OrientedBox other)
    {
        var axes=new[]{AxisX,AxisY,AxisZ,other.AxisX,other.AxisY,other.AxisZ,
            Vec3.Cross(AxisX,other.AxisX),Vec3.Cross(AxisX,other.AxisY),Vec3.Cross(AxisX,other.AxisZ),
            Vec3.Cross(AxisY,other.AxisX),Vec3.Cross(AxisY,other.AxisY),Vec3.Cross(AxisY,other.AxisZ),
            Vec3.Cross(AxisZ,other.AxisX),Vec3.Cross(AxisZ,other.AxisY),Vec3.Cross(AxisZ,other.AxisZ)};
        foreach(var axis in axes)
        {
            var length=axis.Length;
            if(length<1e-12)continue;
            var direction=axis*(1/length);
            if(Math.Abs(Vec3.Dot(other.Centre-Centre,direction))>Radius(this,direction)+Radius(other,direction))
                return false;
        }
        return true;
    }
    private static double Radius(OrientedBox box,Vec3 direction)
    {
        var half=box.Size*.5;
        return Math.Abs(Vec3.Dot(box.AxisX,direction))*half.X
              +Math.Abs(Vec3.Dot(box.AxisY,direction))*half.Y
              +Math.Abs(Vec3.Dot(box.AxisZ,direction))*half.Z;
    }
}

/// <summary>
/// Widths measured during one VehiclePowertrain.BuildLayout. The cache is cleared at the start and
/// end of every build, so a changed transmission or a reused module pointer is never served an old
/// width. A guessed fallback is returned but never stored: GetModuleAutoPosition can run outside a
/// layout build, and pinning that guess would make it permanent.
/// </summary>
public sealed class TransmissionExtentCache
{
    private readonly Dictionary<IntPtr,TransmissionExtent> values=new();
    public int Count=>values.Count;
    public void BeginBuild()=>values.Clear();
    public bool TryGet(IntPtr module,out TransmissionExtent extent)=>values.TryGetValue(module,out extent);
    public TransmissionExtent Remember(IntPtr module,TransmissionExtent? measured,TransmissionExtent fallback)
    {
        if(values.TryGetValue(module,out var cached))return cached;
        if(measured is not {} extent)return fallback;
        values[module]=extent;
        return extent;
    }
}

/// <summary>
/// Native layout values used by the powerpack: the transmissions are auto-positioned on their
/// inputs (PowertrainModuleFlags.PositionBetweenInputs) and the engine/splitter connections are
/// Abstracted (PortConnectionFlags.Abstracted), as native already does for junction to propulsion.
/// </summary>
public static class PowerpackRules
{
    public const short PositionBetweenInputs=0x1C00;
    public const short AutoPosition=0x1C70;
    public const short SplitterBetweenOutputs=0x70;
    public const byte Abstracted=1;
    /// <summary>
    /// 1 mm. The native narrow phase has no epsilon and treats touching as overlap, so the smallest
    /// valid gap is any value above zero. 1 mm absorbs the float32 transform/position round trip.
    /// </summary>
    public const double ObstructionClearance=0.001;
    /// <summary>
    /// 0.5 m. A placement against the engine box that lands further than this from the 0.2.1
    /// output-plane placement is treated as a bad engine box (wrong space, stale bounds) and replaced.
    /// </summary>
    public const double PlacementSafetyNet=0.5;
    /// <summary>2 cm flush tuck into the engine rear when internal collision filtering is enabled.</summary>
    public const double DefaultMonoblockOverlap=0.02;
    /// <summary>Keep every Abstracted connection strictly inside native ValidateLayout's 1.0 m limit.</summary>
    public const double MaxAbstractedDistance=0.90;
    /// <summary>Half-gap on either side of the engine centreline between back-to-back twin boxes.</summary>
    public const double TwinCenterlineHalfGap=0.005;

    public static bool IsMarked(short moduleFlags,byte connectionFlags)=>
        (moduleFlags&PositionBetweenInputs)==PositionBetweenInputs&&(connectionFlags&Abstracted)!=0;

    /// <summary>Finds the transmission fed directly by the engine's output connection.</summary>
    public static PowerpackPlan Find(IReadOnlyList<LayoutModule> modules,IReadOnlyList<LayoutConnection> connections,int engineId)
    {
        var byId=modules.ToDictionary(m=>m.Id);
        var outputs=connections.Where(c=>c.ProviderModule==engineId).ToArray();
        if(outputs.Length!=1||!byId.TryGetValue(outputs[0].ReceiverModule,out var receiver))return new(PowerpackStatus.NoDirectTransmission);
        var connection=outputs[0];
        if(receiver.Kind==ModuleKind.Junction)return new(PowerpackStatus.ViaJunction);
        if(receiver.Kind!=ModuleKind.Transmission)return new(PowerpackStatus.NoDirectTransmission);
        if(IsMarked(receiver.Flags,connection.Flags))return new(PowerpackStatus.Ready,receiver.Id,connection.Id,true);
        // Output-auto flags on a transmission belong to splitter layouts and cannot be restored cleanly;
        // partial/stale powerpack bits (e.g. PositionBetweenInputs or Abstracted left by native drags) remain healable/restorable.
        if((receiver.Flags&SplitterBetweenOutputs)!=0)return new(PowerpackStatus.Conflict,receiver.Id,connection.Id);
        bool partial=(receiver.Flags&PositionBetweenInputs)!=0||(connection.Flags&Abstracted)!=0;
        return new(PowerpackStatus.Ready,receiver.Id,connection.Id,false,partial);
    }

    /// <summary>
    /// Recognises the native twin layout: one engine output into a splitter junction (0x70), and
    /// two distinct transmissions fed by that junction. The splitter's native 0x70 is not a
    /// conflict; only powerpack bits that cannot be restored exactly are.
    /// </summary>
    public static TwinPowerpackPlan FindTwin(IReadOnlyList<LayoutModule> modules,IReadOnlyList<LayoutConnection> connections,int engineId)
    {
        var byId=modules.ToDictionary(m=>m.Id);
        var engineOutputs=connections.Where(c=>c.ProviderModule==engineId).ToArray();
        if(engineOutputs.Length!=1||!byId.TryGetValue(engineOutputs[0].ReceiverModule,out var junction)||junction.Kind!=ModuleKind.Junction)
            return new(PowerpackStatus.NoDirectTransmission);
        var engineJunction=engineOutputs[0];
        var junctionInputs=connections.Where(c=>c.ReceiverModule==junction.Id).ToArray();
        if(junctionInputs.Length!=1||junctionInputs[0].Id!=engineJunction.Id||(junction.Flags&SplitterBetweenOutputs)!=SplitterBetweenOutputs)
            return new(PowerpackStatus.NoDirectTransmission);
        var outputs=connections.Where(c=>c.ProviderModule==junction.Id).ToArray();
        if(outputs.Length!=2)return new(PowerpackStatus.NoDirectTransmission);
        if(!byId.TryGetValue(outputs[0].ReceiverModule,out var left)||!byId.TryGetValue(outputs[1].ReceiverModule,out var right))
            return new(PowerpackStatus.NoDirectTransmission);
        if(left.Kind!=ModuleKind.Transmission||right.Kind!=ModuleKind.Transmission||left.Id==right.Id)
            return new(PowerpackStatus.NoDirectTransmission);
        // Each transmission must have exactly one input, from this junction.
        foreach(var (module,connection) in new[]{(left,outputs[0]),(right,outputs[1])})
        {
            var inputs=connections.Where(c=>c.ReceiverModule==module.Id).ToArray();
            if(inputs.Length!=1||inputs[0].Id!=connection.Id||connection.ProviderModule!=junction.Id)
                return new(PowerpackStatus.NoDirectTransmission);
        }
        // An input-auto splitter or output-auto transmission would fight powerpack placement.
        if((junction.Flags&PositionBetweenInputs)!=0||(left.Flags&SplitterBetweenOutputs)!=0||(right.Flags&SplitterBetweenOutputs)!=0)
            return new(PowerpackStatus.Conflict,engineId,junction.Id,left.Id,right.Id,engineJunction.Id,outputs[0].Id,outputs[1].Id);
        var allMarked=IsMarked(left.Flags,outputs[0].Flags)&&IsMarked(right.Flags,outputs[1].Flags)
            &&(engineJunction.Flags&Abstracted)!=0;
        if(allMarked)
            return new(PowerpackStatus.Ready,engineId,junction.Id,left.Id,right.Id,engineJunction.Id,outputs[0].Id,outputs[1].Id,true);
        var anyMarked=(left.Flags&PositionBetweenInputs)!=0||(right.Flags&PositionBetweenInputs)!=0
            ||(engineJunction.Flags&Abstracted)!=0||(outputs[0].Flags&Abstracted)!=0||(outputs[1].Flags&Abstracted)!=0;
        return new(PowerpackStatus.Ready,engineId,junction.Id,left.Id,right.Id,engineJunction.Id,outputs[0].Id,outputs[1].Id,false,anyMarked);
    }

    public static (short Module,byte Connection) Apply(short moduleFlags,byte connectionFlags)=>
        ((short)(moduleFlags|PositionBetweenInputs),(byte)(connectionFlags|Abstracted));
    public static (short Module,byte Connection) Restore(short moduleFlags,byte connectionFlags)=>
        ((short)(moduleFlags&~PositionBetweenInputs),(byte)(connectionFlags&~Abstracted));

    /// <summary>
    /// Native auto-positioning puts the module origin on the engine output port. The powerpack puts
    /// the transmission input port there instead, using the transmission's own current rotation.
    /// </summary>
    public static Vec3 FlushOrigin(Vec3 engineOutput,Quat transmissionRotation,Vec3 inputLocal)=>
        engineOutput-transmissionRotation.Rotate(inputLocal);

    /// <summary>
    /// Twin placement. The transmission keeps its current lateral coordinate and its rotation.
    /// Its motor-side face (the local X face that points back towards the engine) is put on the
    /// plane through the engine output, and its input port is put at the engine output height.
    /// The engine direction only positions the module; the splitter is not read, so there is no cycle.
    /// <paramref name="depth"/> moves that plane along the engine direction without changing the
    /// lateral coordinate or the input height (offsetting the origin along the direction alone would
    /// change X when the engine is yawed).
    /// </summary>
    public static (Vec3 Origin,Vec3 Input,Vec3 MotorFace) TwinOrigin(
        Vec3 engineOutput,Vec3 engineDirection,Vec3 currentOrigin,Quat rotation,Vec3 inputLocal,TransmissionExtent extent,
        double depth=0)
    {
        var direction=engineDirection.Normalized;
        // The layout's lateral coordinate is vehicle X: keeping it preserves the native mirror pair
        // and the editor's world-X mirror search. The engine direction only supplies the depth.
        var localX=rotation.Rotate(new Vec3(1,0,0));
        var rotatedInput=rotation.Rotate(inputLocal);
        var slope=Vec3.Dot(localX,direction);
        var localFace=Math.Abs(slope)<1e-9?extent.CentreX:extent.CentreX-Math.Sign(slope)*extent.HalfWidth;
        // Solve depth, world-X offset and height together: keep origin.X and the input height, and
        // put the motor face on the requested depth plane, even when the engine is yawed or pitched.
        // The engine direction only positions the module; the splitter is not read.
        var denominator=Math.Max(1e-9,1-direction.X*direction.X-direction.Y*direction.Y);
        var along=(depth-direction.X*(currentOrigin.X-engineOutput.X)+direction.Y*rotatedInput.Y-slope*localFace)/denominator;
        var lateralOffset=currentOrigin.X-engineOutput.X-direction.X*along;
        var height=-direction.Y*along-rotatedInput.Y;
        var origin=engineOutput+direction*along+new Vec3(lateralOffset,height,0);
        var input=origin+rotatedInput;
        var motorFace=origin+rotation.Rotate(new Vec3(localFace,0,0));
        return (origin,input,motorFace);
    }

    /// <summary>
    /// Places a twin transmission against the engine's real obstruction box. The full transmission
    /// box (centre and size on all three axes, rotated by the module rotation) is moved along the
    /// engine direction until its nearest corner is the engine box's furthest point plus
    /// <paramref name="clearance"/>. The lateral coordinate and the input height of TwinOrigin are
    /// kept, so both sides can be given the same depth without breaking the mirror pair.
    /// </summary>
    public static (Vec3 Origin,Vec3 Input,Vec3 MotorFace,double Clearance) TwinOriginAgainst(
        Vec3 engineOutput,Vec3 engineDirection,Vec3 currentOrigin,Quat rotation,Vec3 inputLocal,
        TransmissionExtent extent,OrientedBox engineBox,double clearance)
    {
        var direction=engineDirection.Normalized;
        var initial=TwinOrigin(engineOutput,direction,currentOrigin,rotation,inputLocal,extent);
        var initialBox=new OrientedBox(initial.Origin+rotation.Rotate(extent.Centre),extent.Size,rotation);
        var engineMax=engineBox.MaxProjection(direction);
        var depth=engineMax+clearance-initialBox.MinProjection(direction);
        var placed=TwinOrigin(engineOutput,direction,currentOrigin,rotation,inputLocal,extent,depth);
        var placedBox=new OrientedBox(placed.Origin+rotation.Rotate(extent.Centre),extent.Size,rotation);
        return (placed.Origin,placed.Input,placed.MotorFace,placedBox.MinProjection(direction)-engineMax);
    }

    /// <summary>
    /// TwinOriginAgainst with a safety net. Depth is the shift along the engine direction from the
    /// 0.2.1 placement (motor face on the output plane). When it exceeds
    /// <see cref="PlacementSafetyNet"/> the engine box cannot be right, so the 0.2.1 placement plus
    /// <paramref name="clearance"/> is used instead and FellBack is set.
    /// </summary>
    public static (Vec3 Origin,Vec3 Input,double Depth,bool FellBack) TwinOriginGuarded(
        Vec3 engineOutput,Vec3 engineDirection,Vec3 currentOrigin,Quat rotation,Vec3 inputLocal,
        TransmissionExtent extent,OrientedBox engineBox,double clearance)
    {
        var direction=engineDirection.Normalized;
        var simple=TwinOrigin(engineOutput,direction,currentOrigin,rotation,inputLocal,extent);
        var against=TwinOriginAgainst(engineOutput,direction,currentOrigin,rotation,inputLocal,extent,engineBox,clearance);
        var depth=Vec3.Dot(against.Origin-simple.Origin,direction);
        if(double.IsFinite(depth)&&Math.Abs(depth)<=PlacementSafetyNet)return (against.Origin,against.Input,depth,false);
        var fallback=TwinOrigin(engineOutput,direction,currentOrigin,rotation,inputLocal,extent,clearance);
        return (fallback.Origin,fallback.Input,depth,true);
    }

    /// <summary>
    /// After unticking, the three restored shafts must each be at least the native minimum.
    /// The splitter (0x70) settles on the midpoint of the two transmission inputs, so the lateral
    /// span is widened symmetrically when the two splitter shafts would be too short; if the
    /// engine-to-splitter shaft is too short, both transmissions move along it. The returned origins
    /// are absolute module positions for the two transmissions.
    /// </summary>
    public static (Vec3 Left,Vec3 Right) RestoredTwinPositions(
        Vec3 leftOrigin,Quat leftRotation,Vec3 leftInputLocal,
        Vec3 rightOrigin,Quat rightRotation,Vec3 rightInputLocal,
        Quat splitterRotation,Vec3 splitterInputLocal,
        Vec3 engineOutput,Vec3 engineDirection,double minimum)
    {
        var left=leftOrigin;var right=rightOrigin;
        var required=minimum+1e-4;
        var leftInput=left+leftRotation.Rotate(leftInputLocal);
        var rightInput=right+rightRotation.Rotate(rightInputLocal);
        var span=rightInput-leftInput;
        var centre=(leftInput+rightInput)*.5;
        var axis=span.Normalized;
        if(axis.Length<1e-9)axis=Vec3.Cross(Vec3.UnitY,engineDirection.Normalized).Normalized;
        if(span.Length<required*2)
        {
            var wantedLeft=centre-axis*required;var wantedRight=centre+axis*required;
            left+=wantedLeft-leftInput;right+=wantedRight-rightInput;
            leftInput=wantedLeft;rightInput=wantedRight;
            centre=(leftInput+rightInput)*.5;
        }
        var splitterInput=centre+splitterRotation.Rotate(splitterInputLocal);
        var engDir=engineDirection.Length>1e-9?engineDirection.Normalized:new Vec3(0,0,1);
        double along=Vec3.Dot(splitterInput-engineOutput,engDir);
        if(along<required)
        {
            var shift=engDir*(required-along);
            left+=shift;right+=shift;
        }
        return (left,right);
    }

    public static double Fraction(double value,string name)
    {
        if(!double.IsFinite(value)||value<0||value>1)throw new ArgumentOutOfRangeException(name,"Powerpack housing fraction must be within 0..1.");
        return value;
    }
    /// <summary>Housing added on top of the engine value; nothing when the powerpack is not effective.</summary>
    public static double WithHousing(double engineValue,double fraction,bool effective)=>
        effective?engineValue*(1+fraction):engineValue;

    public static Vec3 EngineRight(Quat engineRot,Vec3 engineDirection)=>EngineBasis(engineRot,engineDirection).Right;

    private static (Vec3 Right,Vec3 Up,Vec3 Fwd) EngineBasis(Quat engineRot,Vec3 engineDirection)
    {
        var fwd=engineDirection.Length>1e-9?engineDirection.Normalized:engineRot.Rotate(new Vec3(0,0,1)).Normalized;
        if(fwd.Length<=1e-9)fwd=new Vec3(0,0,1);
        var rawUp=engineRot.Rotate(new Vec3(0,1,0));
        var upOrtho=rawUp-fwd*Vec3.Dot(rawUp,fwd);
        if(upOrtho.Length<=1e-6)
        {
            var fallback=Math.Abs(fwd.Y)<.9?new Vec3(0,1,0):new Vec3(1,0,0);
            upOrtho=fallback-fwd*Vec3.Dot(fallback,fwd);
        }
        var up=upOrtho.Normalized;
        var right=Vec3.Cross(up,fwd).Normalized;
        return (right,up,fwd);
    }

    private static (double AnchorZ,double Depth,bool FellBack) GuardedAnchorZ(
        Vec3 engineOutput,Vec3 fwd,OrientedBox? engineBox)
    {
        double outputZ=Vec3.Dot(engineOutput,fwd);
        if(engineBox is not {} eb)return (outputZ,0,false);
        double boxMax=eb.MaxProjection(fwd);
        double depth=boxMax-outputZ;
        if(double.IsFinite(depth)&&Math.Abs(depth)<=PlacementSafetyNet)return (boxMax,depth,false);
        return (outputZ,depth,true);
    }

    public static int ChooseTwinSide(Vec3 engineRight,Vec3 thisTarget,Vec3 partnerTarget,
        short thisFlags,short partnerFlags,int thisId,int partnerId)
    {
        double proj=Vec3.Dot(thisTarget-partnerTarget,engineRight);
        if(Math.Abs(proj)>1e-4)return proj<0?-1:1;
        bool thisLeft=(thisFlags&2)!=0,thisRight=(thisFlags&4)!=0;
        bool partnerLeft=(partnerFlags&2)!=0,partnerRight=(partnerFlags&4)!=0;
        if(thisLeft&&!thisRight&&!(partnerLeft&&!partnerRight))return -1;
        if(thisRight&&!thisLeft&&!(partnerRight&&!partnerLeft))return 1;
        return thisId<partnerId?-1:1;
    }

    public static Quat TwinRotation(Quat engineRot,int side)=>engineRot;

    /// <summary>
    /// Engine-local 3D rigid placement for a single transmission. Matches the engine rotation, tracks
    /// X/Y/Z cleanly under any yaw/pitch (including 90 deg and 180 deg), and tucks into the engine
    /// rear by <paramref name="signedClearance"/> (negative = flush overlap when collision filter is on).
    /// </summary>
    public static (Vec3 Origin,Vec3 Input,double Depth,bool FellBack) MonoblockOrigin(
        Vec3 engineOutput,Quat engineRot,Vec3 engineDirection,Vec3 inputLocal,TransmissionExtent extent,
        OrientedBox? engineBox,double signedClearance)
    {
        var (_,_,fwd)=EngineBasis(engineRot,engineDirection);
        var transRot=engineRot;
        var (anchorZ,depth,fellBack)=GuardedAnchorZ(engineOutput,fwd,engineBox);
        double targetFaceZ=anchorZ+signedClearance;
        var rotInput=transRot.Rotate(inputLocal);
        var baseOrigin=engineOutput-rotInput;
        var baseBox=new OrientedBox(baseOrigin+transRot.Rotate(extent.Centre),extent.Size,transRot);
        double shiftZ=targetFaceZ-baseBox.MinProjection(fwd);
        var origin=baseOrigin+fwd*shiftZ;
        var input=origin+rotInput;
        var delta=input-engineOutput;
        if(delta.Length>MaxAbstractedDistance)
        {
            origin=baseOrigin+delta.Normalized*MaxAbstractedDistance;
            input=origin+rotInput;
        }
        return (origin,input,depth,fellBack);
    }

    /// <summary>
    /// Engine-local 3D rigid placement for a twin gearbox: mounts one transmission on each flank
    /// beside the engine in the longitudinal direction, with rear faces flush at the engine rear
    /// so driveshaft outputs exit cleanly at the back.
    /// </summary>
    public static (Vec3 Origin,Vec3 Input,double HalfSpread,double Depth,bool FellBack) TwinMonoblockOrigin(
        Vec3 engineOutput,Quat engineRot,Vec3 engineDirection,Quat transRot,Vec3 inputLocal,
        TransmissionExtent extent,int side,OrientedBox? engineBox,double signedClearance)
    {
        int s=side<0?-1:1;
        var (right,_,fwd)=EngineBasis(engineRot,engineDirection);
        var outward=right*s;
        var rotInput=transRot.Rotate(inputLocal);
        var (anchorZ,depth,fellBack)=GuardedAnchorZ(engineOutput,fwd,engineBox);
        double engineSideMax=engineBox is {} eb?eb.MaxProjection(outward):Vec3.Dot(engineOutput,outward)+0.35;
        double targetInnerSide=engineSideMax+signedClearance;
        var baseOrigin=engineOutput-rotInput;
        var baseBox=new OrientedBox(baseOrigin+transRot.Rotate(extent.Centre),extent.Size,transRot);
        double shiftOut=Math.Clamp(targetInnerSide-baseBox.MinProjection(outward),-MaxAbstractedDistance,MaxAbstractedDistance);
        double shiftZ=Math.Clamp(anchorZ-baseBox.MaxProjection(fwd),-MaxAbstractedDistance,MaxAbstractedDistance);
        var origin=baseOrigin+outward*shiftOut+fwd*shiftZ;
        var input=origin+rotInput;
        double halfSpread=Math.Abs(Vec3.Dot(origin-engineOutput,right));
        return (origin,input,halfSpread,depth,fellBack);
    }
}
