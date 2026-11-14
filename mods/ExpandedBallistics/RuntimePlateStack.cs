using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using UnityEngine;
namespace SprocketExpandedBallistics;

internal static class RuntimePlateStack
{
    internal static IReadOnlyList<PlateStructure> GetSelection(PlateStructureEditor editor)
    {
        var selected=new List<PlateStructure>();
        if(editor==null||editor.Component==null)return selected;
        var items=editor.Gateway.Editor.SelectionReader.Items;
        var count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        for(int i=0;i<count;i++)
        {
            var plate=items[i]?.TryCast<PlateStructure>();
            if(plate!=null)selected.Add(plate);
            else throw new InvalidOperationException("Select only addon plates for stacking.");
        }
        var reference=selected.FindIndex(p=>p.Pointer==editor.Component.Pointer);
        if(reference<0)throw new InvalidOperationException("The reference plate must be selected.");
        return selected;
    }
    private static bool TryPrepare(PlateStructureEditor editor,double gapMm,bool reverseOrder,out IReadOnlyList<PlateStructure> plates,out PlateStackGeometry.Plan? plan,out string reason,IReadOnlyList<PlateStructure>? ordered=null,bool reverseDirection=false)
    {
        plates=GetSelection(editor);plan=null;reason="Select at least two flat addon plates.";
        if(plates.Count<2)return false;
        var currentSelection=plates;
        if(ordered!=null)
        {
            if(ordered.Count!=plates.Count||ordered.Select(p=>p.Pointer).Distinct().Count()!=plates.Count||ordered.Any(p=>!currentSelection.Any(current=>current.Pointer==p.Pointer))){reason="Selection changed. Choose the stack order again.";return false;}
            plates=ordered.ToArray();
        }
        if(reverseOrder)plates=new[]{plates[0]}.Concat(plates.Skip(1).Reverse()).ToArray();
        var layers=new List<PlateStackGeometry.Layer>();var vehicle=editor.Component.Vehicle.Pointer;
        foreach(var p in plates)
        {
            if(p.Vehicle==null||p.Vehicle.Pointer!=vehicle||p.VehicleObject.Released||!p.Applique||!p.HasMesh||p.Mesh.Released){reason="Select addon plates belonging to the same vehicle.";return false;}
            var meshSnapshot=PlateMeshAccess.Read(p,"plate stack",SnapshotUse.Fresh);
            if(meshSnapshot==null){reason=PlateMeshAccess.Busy;return false;}
            var transform=p.VehicleTransform;
            if((transform.LocalToWorldMatrix.MultiplyPoint3x4(Vector3.zero)-transform.Position).sqrMagnitude>1e-8f)
            {reason="A plate's native coordinate frame is unavailable.";return false;}
            if(transform.IsMirrored||transform.Mirror!=null){reason="Remove mirrored pairing before stacking these plates.";return false;}
            var faces=new List<System.Numerics.Vector3[]>();
            foreach(var face in meshSnapshot.Faces)
            {
                var firstThickness=face.Thickness[0];
                var points=new System.Numerics.Vector3[face.VertexCount];
                for(int j=0;j<points.Length;j++)
                {
                    if(face.Thickness[j]!=firstThickness){reason="Wedge plates with varying face thickness cannot be stacked.";return false;}
                    points[j]=N(transform.LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(face.Corners[j].X,face.Corners[j].Y,face.Corners[j].Z)));
                }
                faces.Add(points);
            }
            if(!PlateStackGeometry.TrySurface(faces,out var surface,out var centre,out reason))return false;
            var built=p.Mesh.manifoldMesh?.vertices;
            if(built==null||built.Length<4){reason="The native physical plate mesh is not ready.";return false;}
            var solid=new System.Numerics.Vector3[built.Length];
            for(int j=0;j<solid.Length;j++)solid[j]=N(transform.LocalToWorldMatrix.MultiplyPoint3x4(built[j]));
            layers.Add(new(surface,solid,centre));
        }
        return PlateStackGeometry.TryPlan(layers,gapMm,out plan,out reason,reverseDirection);
    }
    private static bool IsAncestor(VehicleTransform a,VehicleTransform b)
    {for(int i=0;b.Parent!=null&&i<128;i++){b=b.Parent;if(b.Pointer==a.Pointer)return true;}return false;}
    private static System.Numerics.Vector3 N(Vector3 v)=>new(v.x,v.y,v.z);
    private static Quaternion Q(System.Numerics.Quaternion q)=>new(q.X,q.Y,q.Z,q.W);
    private static Vector3 U(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
    internal static bool TryBuildStack(PlateStructureEditor editor,double gapMm,bool reverseOrder,out string message)
    =>TryBuildStackCore(editor,null,gapMm,reverseOrder,out message);
    internal static bool TryBuildStack(PlateStructureEditor editor,IReadOnlyList<PlateStructure> ordered,double gapMm,out string message)
        =>TryBuildStackCore(editor,ordered,gapMm,false,out message);
    internal static bool TryBuildStack(PlateStructureEditor editor,IReadOnlyList<PlateStructure> ordered,double gapMm,bool reverseDirection,out string message)
        =>TryBuildStackCore(editor,ordered,gapMm,false,out message,reverseDirection);
    private static bool TryBuildStackCore(PlateStructureEditor editor,IReadOnlyList<PlateStructure>? ordered,double gapMm,bool reverseOrder,out string message,bool reverseDirection=false)
    {
        Change? change=null;
        try
        {
            if(!TryPrepare(editor,gapMm,reverseOrder,out var plates,out var plan,out message,ordered,reverseDirection))return false;
            change=new Change(plates,plan!,editor);
            var pending=change;var creator=editor.Gateway.Editor.VehicleOperations;
            var callbacks=new VehicleOperationCallbacks
            {
                Execute=(Il2CppSystem.Func<IVehicleOperatorContext,VehicleOPExecutionResult>)(_=>pending.Execute()),
                Revert=(Il2CppSystem.Action<IVehicleOperatorContext>)(_=>pending.Revert()),
                Release=(Il2CppSystem.Action<IVehicleOperatorContext>)(_=>pending.Release())
            };
            creator.CreateOp("Stack addon plates",ref callbacks,VehicleOperationFlags.Undo|VehicleOperationFlags.Redo|VehicleOperationFlags.Register,creator.GetNewGroupID(),VehicleOpExecutionMode.SynchronousCancelOngoing);
            if(!change.Applied){message=change.Error??"Plate stack was cancelled.";return false;}
            message=$"Stacked {plates.Count} plates with {gapMm:0.###} mm clear gaps. Undo restores their positions and rotations.";
            if(plan!.FootprintsDiffer)message+=" Footprints differ; check edge coverage.";
            return true;
        }
        catch(Exception ex){message="Could not stack plates: "+ex.Message;return change?.Applied==true;}
    }
    private sealed class Change
    {
        private sealed record Entry(ISoftVehicleObject Reference,string ComponentId,IntPtr Vehicle,IntPtr Parent,Vector3 Before,Quaternion BeforeRotation,Vector3 Target,Quaternion TargetRotation,Vector3[] LocalVertices,Vector3[] ExpectedVertices);
        private Vector3[] afterPositions=Array.Empty<Vector3>();private Quaternion[] afterRotations=Array.Empty<Quaternion>();private bool captured;private readonly int[] applyOrder;
        private readonly Entry[] entries;private readonly IVehicleEditor editor;private bool released;
        internal bool Applied;internal string? Error;
        internal Change(IReadOnlyList<PlateStructure> plates,PlateStackGeometry.Plan plan,PlateStructureEditor owner)
        {
            editor=owner.Gateway.Editor;
            var parents=plates.Select(p=>{var current=p.VehicleTransform.Parent;for(int depth=0;current!=null&&depth<128;depth++,current=current.Parent){for(int j=0;j<plates.Count;j++)if(plates[j].VehicleTransform.Pointer==current.Pointer)return j;}return -1;}).ToArray();
            applyOrder=PlateStackGeometry.ParentFirst(parents);
            entries=plates.Select((p,i)=>
            {
                var t=p.VehicleTransform;var delta=Q(plan.Rotations![i]);var centre=U(plan.Centres![i]);var offset=U(plan.Translations[i]);
                var target=centre+delta*(t.Position-centre)+offset;var rotation=delta*t.Rotation;
                var local=p.Mesh.manifoldMesh.vertices.ToArray();var expected=local.Select(v=>centre+delta*(t.LocalToWorldMatrix.MultiplyPoint3x4(v)-centre)+offset).ToArray();
                return new Entry(p.VehicleObject.GetReference(),p.ComponentID,p.Vehicle.Pointer,t.Parent?.Pointer??IntPtr.Zero,t.LocalPosition,t.LocalRotation,target,rotation,local,expected);
            }).ToArray();
        }
        private PlateStructure[] Resolve()
        {
            if(released)throw new InvalidOperationException("Stack history was released.");
            return entries.Select(e=>
            {if(!e.Reference.TryGet(out var obj)||obj==null||obj.Released||obj.Vehicle==null||obj.Vehicle.Pointer!=e.Vehicle)throw new InvalidOperationException("A stacked plate is no longer available.");var plate=obj.GetBehaviour(e.ComponentId)?.TryCast<PlateStructure>()??throw new InvalidOperationException("A stacked plate changed type.");if((plate.VehicleTransform.Parent?.Pointer??IntPtr.Zero)!=e.Parent)throw new InvalidOperationException("A stacked plate changed attachment; undo was withheld.");return plate;}).ToArray();
        }
        internal VehicleOPExecutionResult Execute()
        {
            try{Apply(true);Applied=true;return VehicleOPExecutionResult.Executed;}
            catch(Exception ex){Error=ex.Message;try{Apply(false);}catch(Exception restore){Error+="; rollback failed: "+restore.Message;}Applied=false;return VehicleOPExecutionResult.Cancelled;}
        }
        private void Apply(bool forward)
        {
            var plates=Resolve();
            foreach(int i in applyOrder)
            {
                var t=plates[i].VehicleTransform;var e=entries[i];
                if(forward&&!captured&&t.Parent==null){t.SetPositionAndRotation(e.Target,e.TargetRotation);continue;}
                var position=!forward?e.Before:captured?afterPositions[i]:t.Parent==null?e.Target:t.Parent.WorldToLocalSpace(e.Target);
                var rotation=!forward?e.BeforeRotation:captured?afterRotations[i]:t.Parent==null?e.TargetRotation:Quaternion.Inverse(t.Parent.Rotation)*e.TargetRotation;
                t.SetLocalPositionAndRotation(position,rotation);
            }
            if(forward&&!captured)
            {
                for(int i=0;i<plates.Length;i++)for(int j=0;j<entries[i].LocalVertices.Length;j++)
                    if((plates[i].VehicleTransform.LocalToWorldMatrix.MultiplyPoint3x4(entries[i].LocalVertices[j])-entries[i].ExpectedVertices[j]).sqrMagnitude>1e-8f)
                        throw new InvalidOperationException("This scaled attachment cannot align without changing the plate shape. Original transforms restored.");
                afterPositions=plates.Select(p=>p.VehicleTransform.LocalPosition).ToArray();afterRotations=plates.Select(p=>p.VehicleTransform.LocalRotation).ToArray();captured=true;
            }
            var editors=editor.SubEditors.ActiveEditors;var count=editors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.VehicleDesigner.DesignerEditor>>().Count;
            for(int i=0;i<count;i++)
            {var active=editors[i]?.TryCast<PlateStructureEditor>();if(active!=null&&active.IsValid()&&plates.Any(p=>p.Pointer==active.Component.Pointer))active.RequestEditorRefresh();}
        }
        internal void Revert(){try{Apply(false);Applied=false;}catch(Exception ex){Plugin.ModLog.LogWarning("Plate stack undo failed: "+ex.Message);}}
        internal void Release(){released=true;}
    }
}

