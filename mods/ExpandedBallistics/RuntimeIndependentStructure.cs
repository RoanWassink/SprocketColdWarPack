using Sprocket.Blueprints;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using Sprocket.Vehicles.VehicleMeshes;

namespace SprocketExpandedBallistics;

// Uses native registration, mesh references and operation history. No topology hook.
internal static class RuntimeIndependentStructure
{
    internal static bool CanMakeIndependent(PlateStructure component)
    {
        try{return component!=null&&component.Applique&&component.HasMesh&&!component.Mesh.Released&&component.blueprintRef.HasBlueprint&&HasOtherOwner(component);}
        catch{return false;}
    }
    private static bool HasOtherOwner(PlateStructure component)
    {
        var items=component.Vehicle.ObjectReader.Items;
        var count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<count;i++)
        {
            var item=items[i];
            if(item==null||item.Released)continue;
            var owners=item.Components;
            var ownerCount=owners.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<ownerCount;j++)
            {
                var owner=owners[j];
                var plate=owner.TryCast<PlateStructure>();
                if(plate!=null&&plate.Pointer!=component.Pointer&&plate.HasMesh&&plate.Mesh.Pointer==component.Mesh.Pointer)return true;
            }
        }
        return false;
    }
    internal static bool TryMakeIndependent(PlateStructureEditor editor,out string message)
    {
        message="Select a shared addon structure.";
        if(editor==null||!CanMakeIndependent(editor.Component))return false;
        Change? change=null;
        try
        {
            var component=editor.Component;
            var creator=editor.Gateway.Editor.VehicleOperations;
            change=new Change(component,editor);
            var pending=change;
            var callbacks=new VehicleOperationCallbacks
            {
                Execute=(Il2CppSystem.Func<IVehicleOperatorContext,VehicleOPExecutionResult>)(context=>pending.Execute()),
                Revert=(Il2CppSystem.Action<IVehicleOperatorContext>)(context=>pending.Revert()),
                Release=(Il2CppSystem.Action<IVehicleOperatorContext>)(context=>pending.Release())
            };
            creator.CreateOp("Make structure independent",ref callbacks,VehicleOperationFlags.Undo|VehicleOperationFlags.Redo|VehicleOperationFlags.Register,
                creator.GetNewGroupID(),VehicleOpExecutionMode.SynchronousCancelOngoing);
            if(!pending.Applied){message=pending.Error??"Structure independence could not be applied.";pending.Release();return false;}
            message="Structure is independent. Geometry changes now affect only this addon.";
            return true;
        }
        catch(Exception ex)
        {
            if(change?.Applied==true){message="Structure is independent; editor history reported: "+ex.Message;return true;}
            change?.Release();
            message="Could not make structure independent: "+ex.Message;
            return false;
        }
    }
    private sealed class Change
    {
        private PlateStructure component;
        private readonly ISoftVehicleObject reference;
        private readonly string componentId;
        private readonly IntPtr vehiclePointer;
        private readonly IVehicleEditor applicationEditor;
        private readonly IVehicleMeshRegister register;
        private readonly MeshReference<PlateStructureMesh> oldMesh;
        private readonly MeshReference<PlateStructureMesh> newMesh;
        private readonly BlueprintSlot<Sprocket.PlateStructures.Blueprints.StructureBlueprint> oldBlueprint;
        private readonly BlueprintSlot<Sprocket.PlateStructures.Blueprints.StructureBlueprint> newBlueprint;
        private readonly int oldId;
        private int newId=-1;
        private bool released;
        internal bool Applied;
        internal string? Error;
        internal Change(PlateStructure component,PlateStructureEditor editor)
        {
            this.component=component;applicationEditor=editor.Gateway.Editor;register=component.Vehicle.Meshes;
            reference=component.VehicleObject.GetReference();componentId=component.ComponentID;vehiclePointer=component.Vehicle.Pointer;
            // Native references hold both meshes while undo history can revisit them.
            oldMesh=new(component.Mesh);
            oldId=component.blueprintRef.BlueprintID;
            PlateStructureMesh? fresh=null;
            try
            {
                oldBlueprint=new(component.blueprintRef.register,component.blueprintRef.reader);
                newBlueprint=new(component.blueprintRef.register,component.blueprintRef.reader);
                oldBlueprint.Load(oldId);
                var json=component.Mesh.ToJson(true); // packs generation and rivet data, not just a shared ID.
                fresh=register.New<PlateStructureMesh>();
                fresh.FromJson(json);fresh.Rebuild();
                var before=component.Mesh.editMesh;var after=fresh.editMesh;
                if(after==null||before.VertexCount<=0||before.FaceCount<=0||before.VertexCount!=after.VertexCount||before.EdgeCount!=after.EdgeCount||before.FaceCount!=after.FaceCount||before.LoopCount!=after.LoopCount)
                    throw new InvalidOperationException("Native mesh copy did not preserve topology.");
                for(int i=0;i<before.VertexCount;i++)
                    if(!Finite(before.GetVertex(i).position)||!Finite(after.GetVertex(i).position)||(before.GetVertex(i).position-after.GetVertex(i).position).sqrMagnitude>1e-10f)
                        throw new InvalidOperationException("Native mesh copy changed authored vertices.");
                for(int i=0;i<before.LoopCount;i++)
                    if(before.GetLoop(i).thickness!=after.GetLoop(i).thickness)
                        throw new InvalidOperationException("Native mesh copy changed armour thickness.");
                if(component.Mesh.generationBlueprintRef!=null&&(fresh.generationBlueprintRef==null||fresh.generationBlueprintRef.Pointer==component.Mesh.generationBlueprintRef.Pointer))
                    throw new InvalidOperationException("Native mesh copy retained generation ownership.");
                if(fresh.editMesh.Pointer==component.Mesh.editMesh.Pointer||fresh.ID==component.Mesh.ID)
                    throw new InvalidOperationException("Native mesh copy retained shared ownership.");
                newMesh=new(fresh);
            }
            catch
            {
                oldMesh.Dispose();oldBlueprint?.Release();newBlueprint?.Release();
                if(fresh!=null&&fresh.UserCount==0)register.Release(fresh);
                throw;
            }
        }
        private static bool Finite(UnityEngine.Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        internal VehicleOPExecutionResult Execute()
        {
            try
            {
                if(released||!Resolve())return VehicleOPExecutionResult.Cancelled;
                if(newId<0)
                {
                    component.blueprintRef.Copy();
                    newId=component.blueprintRef.BlueprintID;newBlueprint.Load(newId);
                    if(newId==oldId)throw new InvalidOperationException("Native blueprint copy did not acquire an independent ID.");
                }
                else component.blueprintRef.Load(newId);
                Apply(newMesh.Mesh);Applied=true;Error=null;
                return VehicleOPExecutionResult.Executed;
            }
            catch(Exception ex)
            {
                Error=ex.Message;
                try{component.blueprintRef.Load(oldId);Apply(oldMesh.Mesh);}catch(Exception restore){Error+="; restore failed: "+restore.Message;}
                Applied=false;return VehicleOPExecutionResult.Cancelled;
            }
        }
        internal void Revert()
        {
            if(released||!Resolve())return;
            try{component.blueprintRef.Load(oldId);Apply(oldMesh.Mesh);Applied=false;}
            catch(Exception ex){Plugin.ModLog.LogWarning("Independent structure undo failed: "+ex.Message);}
        }
        private bool Resolve()
        {
            if(!reference.TryGet(out var item)||item==null||item.Released||item.Vehicle.Pointer!=vehiclePointer)return false;
            var current=item.GetBehaviour(componentId)?.TryCast<PlateStructure>();
            if(current==null)return false;
            component=current;return true;
        }
        private void Apply(PlateStructureMesh mesh)
        {
            component.Mesh=mesh; // native setter synchronizes saved BodyMeshVUID and subscriptions.
            component.blueprintRef.MarkModified();
            component.RequestMeshRefresh();
            // Undo may run after the original editor has been recreated. Refresh only
            // currently active editors for this component, without changing selection.
            var editors=applicationEditor.SubEditors.ActiveEditors;
            var count=editors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.VehicleDesigner.DesignerEditor>>().Count;
            for(int i=0;i<count;i++)
            {
                var active=editors[i]?.TryCast<PlateStructureEditor>();
                if(active!=null&&active.IsValid()&&active.Component.Pointer==component.Pointer)active.RequestEditorRefresh();
            }
        }
        internal void Release()
        {
            if(released)return;released=true;
            oldMesh.Dispose();newMesh.Dispose();oldBlueprint.Release();newBlueprint.Release();
            register.TryCast<VehicleMeshRegister>()?.PurgeUnused();
        }
    }
}
