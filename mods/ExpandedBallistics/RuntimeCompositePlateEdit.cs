using Sprocket.Blueprints;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using Sprocket.Vehicles.VehicleMeshes;
using NVector=System.Numerics.Vector3;

namespace SprocketExpandedBallistics;

// Uses native registration, mesh references and operation history. No topology hook.
internal static class RuntimeCompositePlateEdit
{
    internal static bool TryApply(PlateStructureEditor editor,string materialId,double? depthMm,out string message)=>
        TryApply(editor,materialId,depthMm,ThicknessMode.Uniform,out message);
    internal static bool TryApply(PlateStructureEditor editor,string materialId,double? depthMm,ThicknessMode mode,out string message)
    {
        message="Select a Composite armour plate.";
        if(editor==null||!RuntimeCompositePlate.Is(editor.Component))return false;
        if(mode==ThicknessMode.FillToWall)
        {
            try { var prepared=RuntimeComposerFill.Prepare(editor,ComposerMeshAction.Fill) with{Material=materialId};return TryPrepared(editor,prepared,out message); }
            catch(Exception ex){message=ex.Message;return false;}
        }
        return Run(editor,materialId,depthMm,mode,true,"Set composite plate material and thickness","Composite plate material and thickness updated.",out message);
    }
    /// <summary>
    /// 0.22.14: "Convert to composite plate". The same single native step as Apply, without the composite gate: the plate gets a
    /// named mesh copy and the cassette, which is the identity that survives save and load. Undo leaves a normal plate.
    /// </summary>
    internal static bool TryConvert(PlateStructureEditor editor,string materialId,double? depthMm,out string message)=>
        TryConvert(editor,materialId,depthMm,ThicknessMode.Uniform,out message);
    internal static bool TryConvert(PlateStructureEditor editor,string materialId,double? depthMm,ThicknessMode mode,out string message)
    {
        message="Select an add-on plate.";
        if(mode==ThicknessMode.FillToWall){message="Convert the plate with Uniform first, then use Fill to wall.";return false;}
        if(editor==null||editor.Component==null)return false;
        if(Eligibility(editor.Component) is {} blocker){message=blocker;return false;}
        return Run(editor,materialId,depthMm,mode,false,"Convert to composite plate","Converted to a composite plate.",out message);
    }
    /// <summary>
    /// 0.22.82 (Apply "Solid block"): the recipe as material and the mark [Solid block] in the mesh name, in the one existing CreateOp step (Run): the mesh copy keeps every
    /// point and every corner thickness, so the shape stays as the user drew it. A plate that is not composite yet is converted in the same step (Undo leaves a normal plate).
    /// Refused, with nothing changed: a plate in layers (Back to box first), and a plate whose pieces are not all closed (the open edges are named).
    /// </summary>
    internal static bool TrySolidBlock(PlateStructureEditor editor,string materialId,bool convert,out string message)
    {
        message="Select a Composite armour plate.";
        if(editor==null||editor.Component==null)return false;
        var plate=editor.Component;var wasComposite=RuntimeCompositePlate.Is(plate);
        if(!wasComposite)
        {
            if(!convert)return false;
            if(Eligibility(plate) is {} cannot){message=SolidBlockApply.Label+": "+cannot;return false;}
        }
        SolidShapeResult shape;(int Faces,int MaxMm) thick;
        try
        {
            if(PlateMeshAccess.Read(plate,"solid block",SnapshotUse.Fresh)==null){message=PlateMeshAccess.Busy;return false;}
            if(CompositePlate.LayoutFromName(plate.Mesh.ToMeshBlueprint(false)?.Name)!=null){message=SolidBlockApply.Label+": "+SolidBlockApply.InLayers;return false;}
            var json=plate.Mesh.ToJson(true);shape=SolidShape.FromJson(json);thick=SolidShape.ThickFaces(json);
        }
        catch(Exception ex){message=SolidBlockApply.Label+": the plate could not be read ("+ex.Message+"). Nothing changed.";return false;}
        if(!shape.Closed){message=SolidBlockApply.Label+": "+shape.Refusal;Plugin.ModLog.LogInfo("[Solid block] Apply refused on plate #"+plate.VUID.Value+": "+shape.Refusal);return false;}
        var ok=Run(editor,materialId,null,ThicknessMode.SolidBlock,wasComposite,SolidBlockApply.Label,"",out message);
        if(!ok)return false;
        double? density=RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(materialId,out var model)==true&&model!=null?model.Passive.Density:null;
        message=SolidBlockApply.Label+": "+(wasComposite?"":"converted to a composite plate; ")+SolidBlockApply.Shape(shape.Pieces,shape.VolumeM3,density)+" "+SolidBlockApply.ColliderSummary+(thick.Faces>0?" "+SolidBlockApply.Thick(thick.Faces,thick.MaxMm):"");
        if(thick.Faces>0)Plugin.ModLog.LogWarning("[Solid block] plate #"+plate.VUID.Value+": "+SolidBlockApply.Thick(thick.Faces,thick.MaxMm));
        Plugin.ModLog.LogInfo(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Solid block] Applied to plate #{plate.VUID.Value} with {materialId}: {shape.Pieces} closed pieces, {shape.VolumeM3:0.######} m3 ({string.Join(" + ",shape.PieceVolumesM3.Select(v=>v.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)))}), {shape.ReversedFaces} faces turned for the volume; density {density?.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)??"unknown"} kg/m3."));
        return true;
    }
    /// <summary>Null when the plate can be converted; otherwise the reason (<see cref="CompositeConversion.Blocker"/>).</summary>
    internal static string? Eligibility(PlateStructure plate)
    {
        try
        {
            var hasMesh=plate.HasMesh;
            var released=hasMesh&&plate.Mesh.Released||!plate.blueprintRef.HasBlueprint;
            var transform=plate.VehicleTransform;
            return CompositeConversion.Blocker(plate.Applique,hasMesh,released,transform.IsMirrored||transform.Mirror!=null,RuntimeCompositePlate.Is(plate));
        }
        catch(Exception ex){return "Plate unreadable: "+ex.Message;}
    }
    /// <summary>Other live plates sharing this plate's mesh (Alt duplicates): the step unlinks them.</summary>
    internal static int LinkedCopies(PlateStructure component)
    {
        try
        {
            if(!component.HasMesh)return 0;
            int linked=0;var mesh=component.Mesh.Pointer;
            var items=component.Vehicle.ObjectReader.Items;
            var count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
            for(int i=0;i<count;i++)
            {
                var item=items[i];if(item==null||item.Released)continue;
                var owners=item.Components;
                var ownerCount=owners.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(int j=0;j<ownerCount;j++)
                    if(owners[j]?.TryCast<PlateStructure>() is {} plate&&plate.Pointer!=component.Pointer&&plate.HasMesh&&plate.Mesh.Pointer==mesh)linked++;
            }
            return linked;
        }
        catch(Exception){return 0;}
    }
    /// <summary>
    /// 0.22.14: the faces selected in the designer, read from the live editor mesh (a ToJson copy carries no selection).
    /// Null when nothing is selected: Apply then sets every face. 0.22.24: read through the snapshot gate; unreadable gives null for a display
    /// caller, and <paramref name="unreadable"/> true so an Apply refuses instead of silently treating every face.
    /// </summary>
    internal static bool[]? SelectedFaces(PlateStructureEditor editor,SnapshotUse use=SnapshotUse.Display)=>SelectedFaces(editor,use,out _);
    internal static bool[]? SelectedFaces(PlateStructureEditor editor,SnapshotUse use,out bool unreadable)
    {
        unreadable=false;
        try
        {
            var plate=editor.Component;if(plate==null||!plate.HasMesh)return null;
            var own=PlateMeshAccess.Read(plate,"selection",use);
            PlateStructureMesh? live=null;
            try{live=editor.mesh;}catch(Exception){}
            var snapshot=own;
            if(live!=null&&!live.Released&&live.Pointer!=plate.Mesh.Pointer)
            {
                var other=PlateMeshAccess.Read(live,"selection (editor mesh)",use);
                if(other==null){unreadable=true;return null;}
                if(own==null||other.FaceCount==own.FaceCount)snapshot=other;
            }
            if(snapshot==null){unreadable=true;return null;}
            return snapshot.SelectedFlags();
        }
        catch(Exception){unreadable=true;return null;}
    }
    internal static bool TryPrepared(PlateStructureEditor editor,PreparedComposerFill prepared,out string message)
    {
        if(!prepared.Plan.CanCommit){message=prepared.Plan.Label+": Review the corners without a wall and press Apply first.";return false;}
        if(!RuntimeComposerFill.Matches(editor,prepared)){message="The plate or designer context changed; preview the action again.";return false;}
        // 0.22.74: a plate that is not composite yet is converted by the same step (OldWasComposite false: the mesh gets the composite name, Undo leaves a normal plate).
        var wasComposite=RuntimeCompositePlate.Is(editor.Component);
        if(!wasComposite&&Eligibility(editor.Component) is {} cannot){message=prepared.Plan.Label+": "+cannot;return false;}
        CopyRefusal=null;
        bool retryable=prepared.Plan.CopyRetry!=null;
        var ok=Run(editor,prepared.Material,null,prepared.Mode,wasComposite,prepared.Plan.Label,"",out message,prepared,!retryable);
        if(ok||!retryable||CopyRefusal is not {} refusal)return ok;
        CopyRefusal=null;
        // 0.22.80: the game did not keep the planned layer faces. The dump gets the planned net and the copy of the game; then one more plan with the faces that differ
        // as wall (or left out) is applied. Only when that one is refused too, nothing changed.
        var different=LayerFaces.CopyDifferences(prepared.Plan.Json,refusal.Json);
        if(prepared.Plan.FollowWall is {} attempt)RuntimeComposerFill.ReportFollowWall(editor.Component,attempt with{PlannedJson=prepared.Plan.Json,GameJson=refusal.Json,CopyProblem=refusal.Problem,DifferentFaces=different.Select(i=>i+1).ToArray(),Summary="The game did not keep the planned faces ("+refusal.Problem+"); "+attempt.Summary},true);
        ComposerMeshPlan? second=null;
        try{second=prepared.Plan.CopyRetry!(refusal.Json);}catch(Exception ex){Plugin.ModLog.LogWarning("[Layer faces] Convert to layers: the second plan failed: "+ex.Message);}
        if(second==null){if(prepared.Plan.FollowWall is {} first)RuntimeComposerFill.ReportFollowWall(editor.Component,first,true);return false;}
        Plugin.ModLog.LogWarning($"[Layer faces] Convert to layers: the game did not keep {different.Length} planned faces; trying once more with their source faces as wall.");
        var again=prepared with{Plan=second};
        ok=Run(editor,again.Material,null,again.Mode,wasComposite,again.Plan.Label,"",out message,again);
        if(!ok)message+=" (second plan; the first: "+refusal.Problem+")";
        return ok;
    }
    /// <summary>0.22.80: the copy of the game and the reason, when the copy check of a prepared plan refused it (set in Change, read once by TryPrepared).</summary>
    private static (string Json,string Problem)? CopyRefusal;
    private static bool Run(PlateStructureEditor editor,string materialId,double? depthMm,ThicknessMode mode,bool oldWasComposite,string opName,string done,out string message,PreparedComposerFill? prepared=null,bool report=true)
    {
        Change? change=null;
        try
        {
            var component=editor.Component;
            if(!RuntimeCassetteComposer.CanApply(component,materialId,out message))return false;
            var creator=editor.Gateway.Editor.VehicleOperations;
            // 0.22.24: only a fresh, clean read of the plate mesh may drive a write; during an edit or a held mouse button the step is refused (nothing touched).
            var selected=SelectedFaces(editor,SnapshotUse.Fresh,out var unreadable);
            var before=PlateMeshAccess.Read(component,"apply",SnapshotUse.Fresh);
            if(unreadable||before==null){message=PlateMeshAccess.Busy;return false;}
            change=new Change(component,editor,materialId,depthMm,mode,oldWasComposite,selected,before,prepared);
            var pending=change;
            var callbacks=new VehicleOperationCallbacks
            {
                Execute=(Il2CppSystem.Func<IVehicleOperatorContext,VehicleOPExecutionResult>)(context=>pending.Execute()),
                Revert=(Il2CppSystem.Action<IVehicleOperatorContext>)(context=>pending.Revert()),
                Release=(Il2CppSystem.Action<IVehicleOperatorContext>)(context=>pending.Release())
            };
            creator.CreateOp(opName,ref callbacks,VehicleOperationFlags.Undo|VehicleOperationFlags.Redo|VehicleOperationFlags.Register,
                creator.GetNewGroupID(),VehicleOpExecutionMode.SynchronousCancelOngoing);
            if(!pending.Applied){message=pending.Error??"Composite material edit could not be applied.";pending.Release();return false;}
            // The operation ran inside CreateOp: name the mesh again and drop the cached identity.
            pending.Finish();
            if(pending.SizeNote!=null&&prepared?.Plan.FollowWall?.Warning!=true)Plugin.ModLog.LogInfo("[Composite plate] "+pending.LogPrefix+pending.SizeNote+".");
            message=prepared!=null?(prepared.Plan.Summary.StartsWith(prepared.Plan.Label+":",StringComparison.Ordinal)?prepared.Plan.Summary:prepared.Plan.Label+": "+prepared.Plan.Summary):done+(pending.SizeNote is {} note?$" {note}.":pending.SelectedFaces>0?$" ({pending.SelectedFaces} selected face{(pending.SelectedFaces==1?"":"s")})":"");
            return true;
        }
        catch(Exception ex)
        {
            if(change?.Applied==true){change.Finish();message="Composite edit applied; editor history reported: "+ex.Message;return true;}
            change?.Release();
            message="Could not apply cassette: "+ex.Message;
            return false;
        }
        finally
        {
            // 0.22.80: a refused first plan that gets a second one (TryPrepared) is reported there, with the copy of the game.
            if(prepared?.Plan.FollowWall is {} attempt&&(report||change?.Applied==true))RuntimeComposerFill.ReportFollowWall(editor.Component,attempt,change?.Applied!=true);
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
        // 0.22.14: material, thicknesses and the naming rule of both ends of the step (OldWasComposite false: a conversion).
        private readonly CompositeUndoSnapshot snapshot;
        private ThicknessMode mode;
        private readonly string? originalName;
        private readonly string? preparedName;
        private readonly bool designerOnly;
        private readonly string? actionLabel;
        internal readonly int SelectedFaces;
        internal readonly string? SizeNote;
        internal string LogPrefix=>actionLabel!=null?actionLabel+": ":mode==ThicknessMode.FillToWall?"Fill to wall: ":mode==ThicknessMode.ToBackPlane?"Thicken to back plane: ":"Composite mesh: ";
        internal bool Applied;
        internal string? Error;
        internal Change(PlateStructure component,PlateStructureEditor editor,string materialId,double? depthMm,ThicknessMode mode,bool oldWasComposite,bool[]? selected,PlateMeshSnapshot before,PreparedComposerFill? prepared=null)
        {
            originalName=component.Mesh.ToMeshBlueprint(false)?.Name;preparedName=prepared?.Plan.MeshName??(CompositePlate.ModeFromName(originalName)==mode?originalName:null);
            actionLabel=prepared?.Plan.Label;this.component=component;this.mode=mode;designerOnly=prepared!=null;applicationEditor=editor.Gateway.Editor;register=component.Vehicle.Meshes;
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
                var beforeEdit=component.Mesh.editMesh; // counts only; every face and loop is read from the snapshot
                // The selection lives on the live mesh only: it was read before the copy.
                SelectedFaces=CompositeThicknessPlan.SelectedCount(selected,before.FaceCount);
                var old=before.ThicknessList();
                // 0.22.53: a prepared composer action carries its own complete mesh; the depth-based planners do not run for it
                // (To back plane would otherwise demand a cassette depth for a plate that is only flipped or turned inward).
                CompositeThicknessPlan plan;string? sizeNote;var effectiveMode=mode;
                if(prepared!=null){plan=new(materialId,prepared.Plan.After.ThicknessList().ToArray());sizeNote=prepared.Plan.Summary;}
                else plan=ComputePlan(materialId,depthMm,mode,selected,old,before,out sizeNote,out effectiveMode);
                var expected=prepared?.Plan.After??before;
                var newTopology=prepared?.Plan.NewTopology==true;
                mode=effectiveMode;
                // 0.22.24: To back plane sets AlongFaceNormal itself on the faces that take part, in this same step. The copy gets the new modes,
                // the old mesh keeps its own: undo puts the old mesh (and with it the old directions) back.
                var modePlan=ThickenModePlan.Create(mode,before.Faces.Select(face=>face.Along).ToList(),selected);
                snapshot=new(component.armourTechID,materialId,oldWasComposite,old.ToArray(),plan,modePlan.OldAlong,prepared?.Plan.After.Faces.Select(f=>f.Along).ToArray()??modePlan.NewAlong);
                SizeNote=sizeNote==null?null:modePlan.Note is {} modeNote?sizeNote+"; "+modeNote:sizeNote;
                var json=component.Mesh.ToJson(true); // packs generation and rivet data, not just a shared ID.
                fresh=register.New<PlateStructureMesh>();
                fresh.FromJson(prepared?.Plan.Json??json);fresh.Rebuild();
                var after=fresh.editMesh;
                // Only the cheek preset replaces the topology; Flip, Thicken inward and Fill to wall keep every count.
                if(after==null||beforeEdit.VertexCount<=0||beforeEdit.FaceCount<=0||!newTopology&&(beforeEdit.VertexCount!=after.VertexCount||beforeEdit.EdgeCount!=after.EdgeCount||beforeEdit.FaceCount!=after.FaceCount||beforeEdit.LoopCount!=after.LoopCount))
                    throw new InvalidOperationException("Native mesh copy did not preserve topology.");
                var copy=PlateMeshAccess.ReadOwn(fresh,out var copyReason)??throw new InvalidOperationException("Native mesh copy unreadable: "+copyReason);
                // 0.22.57: only a plan with ribs (Cheek fill (shell)) sets CopyCheck; it compares the copy's saved helper points, rib edges and tm/te with the plan before anything is replaced.
                // 0.22.80: the copy the game gave back is kept for the dump and the one retry of Convert to layers (TryPrepared).
                if(prepared?.Plan.CopyCheck is {} copyCheck){var copyJson=fresh.ToJson(true);if(copyCheck(copyJson) is {} copyProblem){CopyRefusal=(copyJson,copyProblem);throw new InvalidOperationException(copyProblem);}}
                // Pin an explicitly authored saved-front packet to the real native copy, after
                // serializer/rebuild normalization. Inherited names on later edits keep their old
                // fingerprint so those edits cannot be silently restored away.
                if(prepared?.Plan.MeshName!=null&&LayerFaces.LoadFront(preparedName) is {} savedFront)
                    preparedName=LayerFaces.SaveFront(mode,savedFront.Json,savedFront.Front,fresh.ToJson(true));
                if(copy.FaceCount!=expected.FaceCount||copy.LoopCount!=expected.LoopCount)throw new InvalidOperationException("Native mesh copy did not preserve topology.");
                for(int f=0;f<expected.FaceCount;f++)
                {
                    var a=expected.Faces[f];var b=copy.Faces[f];
                    if(a.VertexCount!=b.VertexCount)throw new InvalidOperationException("Native mesh copy did not preserve topology.");
                    for(int j=0;j<a.VertexCount;j++)
                    {
                        if(!Finite(a.Corners[j])||!Finite(b.Corners[j])||(a.Corners[j]-b.Corners[j]).LengthSquared()>1e-10f)
                            throw new InvalidOperationException("Native mesh copy changed authored vertices.");
                        if(prepared!=null&&a.Along[j]!=b.Along[j])throw new InvalidOperationException("Native mesh copy changed thickness direction.");
                        if(a.Thickness[j]!=b.Thickness[j])throw new InvalidOperationException("Native mesh copy changed armour thickness.");
                    }
                }
                // A replaced (cheek preset) mesh has no generation data at all; it may only never share the old one.
                if(component.Mesh.generationBlueprintRef!=null&&(fresh.generationBlueprintRef==null&&!newTopology||fresh.generationBlueprintRef!=null&&fresh.generationBlueprintRef.Pointer==component.Mesh.generationBlueprintRef.Pointer))
                    throw new InvalidOperationException("Native mesh copy retained generation ownership.");
                if(fresh.editMesh.Pointer==beforeEdit.Pointer||fresh.ID==component.Mesh.ID)
                    throw new InvalidOperationException("Native mesh copy retained shared ownership.");
                if(depthMm.HasValue)
                {
                    // Only faces whose loops or directions change are written, so an unselected face keeps its per-corner thickness.
                    var writer=fresh.Cast<Sprocket.MeshEditing.IMeshWriter>();
                    for(int f=0;f<plan.Thicknesses.Length;f++)
                    {
                        var modeWrites=modePlan.Writes[f].Any(w=>w);
                        if(!plan.Changes(old,f)&&!modeWrites)continue;
                        var values=plan.Thicknesses[f];
                        writer.SetFaceThickness(f,values[0]); // marks the face dirty for the rebuild
                        // ToBackPlane varies per corner; the face writer only takes one value, so write each loop (and the direction).
                        if(modeWrites||values.Any(v=>v!=values[0]))
                        {
                            var loop=fresh.editMesh.GetFace(f).firstLoop;
                            for(int j=0;j<values.Length;j++)
                            {
                                loop.thickness=values[j];
                                if(modePlan.Writes[f][j])loop.thickenMode=Sprocket.PlateMesh.ThickenMode.AlongFaceNormal;
                                loop=loop.next;
                            }
                        }
                    }
                    fresh.Rebuild();
                    var written=PlateMeshAccess.ReadOwn(fresh,out var writtenReason)??throw new InvalidOperationException("Native face thickness write failed: "+writtenReason);
                    for(int f=0;f<plan.Thicknesses.Length;f++)
                        for(int j=0;j<plan.Thicknesses[f].Length;j++)
                        {
                            if(written.Faces[f].Thickness[j]!=plan.Thicknesses[f][j])throw new InvalidOperationException("Native face thickness write failed.");
                            if(written.Faces[f].Along[j]!=modePlan.NewAlong[f][j])throw new InvalidOperationException("Native thickness direction write failed.");
                        }
                    if(modePlan.Any)
                    {
                        // The saved mesh (blueprint JSON) must carry the new directions, or save and load would bring the old spikes back.
                        var problem=ThickenModeJson.Verify(fresh.ToJson(true),modePlan.NewAlong);
                        if(problem!=null)
                            throw new InvalidOperationException($"To back plane could not set Along face normal on {modePlan.FacesChanged} face{(modePlan.FacesChanged==1?"":"s")} ({problem}). Set their native thickness direction to Along face normal first.");
                    }
                }
                newMesh=new(fresh);
            }
            catch
            {
                oldMesh.Dispose();oldBlueprint?.Release();newBlueprint?.Release();
                if(fresh!=null&&fresh.UserCount==0)register.Release(fresh);
                throw;
            }
        }
        private static CompositeThicknessPlan ComputePlan(string materialId,double? depthMm,ThicknessMode mode,bool[]? selected,
            List<ushort[]> old,PlateMeshSnapshot before,out string? sizeNote,out ThicknessMode effectiveMode)
        {
            sizeNote=null;effectiveMode=mode;
            if(mode!=ThicknessMode.ToBackPlane)return CompositeThicknessPlan.Create(materialId,old,depthMm,selected);
            if(!depthMm.HasValue)throw new InvalidOperationException("Back-plane thickness needs a cassette depth.");
            var points=before.Faces.Select(face=>face.Corners).ToList();var normals=before.Faces.Select(face=>face.Normal).ToList();
            var plan=BackplaneThickness.CreateOrUniform(materialId,old,points,normals,selected,depthMm.Value,out var summary,out var flat,RuntimeCompositePlate.Limits);
            sizeNote=summary;
            if(flat)effectiveMode=ThicknessMode.Uniform; // 0.22.22: a flat plate silently becomes Uniform (one undo step, one info line)
            return plan;
        }
        private static bool Finite(NVector v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
        internal VehicleOPExecutionResult Execute()
        {
            try
            {
                if(released||designerOnly&&!RuntimeComposerFill.Designer||!Resolve())return VehicleOPExecutionResult.Cancelled;
                if(!RuntimeCassetteComposer.CanApply(component,snapshot.NewMaterial,out var blocker)){Error=blocker;return VehicleOPExecutionResult.Cancelled;}
                if(newId<0)
                {
                    component.blueprintRef.Copy();
                    newId=component.blueprintRef.BlueprintID;newBlueprint.Load(newId);
                    if(newId==oldId)throw new InvalidOperationException("Native blueprint copy did not acquire an independent ID.");
                }
                else component.blueprintRef.Load(newId);
                Apply(newMesh.Mesh,snapshot.NewMaterial,snapshot.NameOnExecute,mode);
                // 0.22.14: a conversion must leave a plate the mod recognises; otherwise the step is cancelled and restored.
                if(!snapshot.OldWasComposite&&!RuntimeCompositePlate.Is(component))
                    throw new InvalidOperationException("Conversion could not be stored: the plate is not recognised as a composite plate.");
                Applied=true;Error=null;
                return VehicleOPExecutionResult.Executed;
            }
            catch(Exception ex)
            {
                Error=ex.Message;
                try{component.blueprintRef.Load(oldId);Apply(oldMesh.Mesh,snapshot.OldMaterial,snapshot.NameOnRevert);}catch(Exception restore){Error+="; restore failed: "+restore.Message;}
                Applied=false;return VehicleOPExecutionResult.Cancelled;
            }
        }
        internal void Revert()
        {
            if(released||designerOnly&&!RuntimeComposerFill.Designer||!Resolve())return;
            if(!RuntimeCassetteComposer.CanApply(component,snapshot.OldMaterial,out var blocker))
            {Plugin.ModLog.LogWarning("Composite plate undo blocked: "+blocker);return;}
            try{component.blueprintRef.Load(oldId);Apply(oldMesh.Mesh,snapshot.OldMaterial,snapshot.NameOnRevert);Applied=false;}
            catch(Exception ex){Plugin.ModLog.LogWarning("Composite plate undo failed: "+ex.Message);}
        }
        private bool Resolve()
        {
            if(!reference.TryGet(out var item)||item==null||item.Released||item.Vehicle.Pointer!=vehiclePointer)return false;
            var current=item.GetBehaviour(componentId)?.TryCast<PlateStructure>();
            if(current==null)return false;
            component=current;return true;
        }
        private void Apply(PlateStructureMesh mesh,string material,bool name,ThicknessMode? thicknessMode=null)
        {
            var previous=component.HasMesh?component.Mesh.Pointer:IntPtr.Zero;
            component.Blueprint.armourTechID=material;component.SetArmourMaterial(material);
            component.Mesh=mesh; // native setter synchronizes saved BodyMeshVUID and subscriptions.
            component.blueprintRef.MarkModified();
            component.RequestMeshRefresh();component.RequestRebuild();
            // The ToJson copy has no name. Execute and redo name the new mesh again (the route that works for the native prefab);
            // undo names the old mesh only if it was a composite plate before (0.22.14: undoing a conversion leaves a normal plate).
            if(name&&!EnsureSavedName(mesh,thicknessMode))
                throw new InvalidOperationException("Composite plate thickness mode could not be stored on the native mesh.");
            if(previous!=IntPtr.Zero)RuntimeCompositePlate.Invalidate(previous);
            RuntimeCompositePlate.Invalidate(mesh.Pointer);
            try{component.Vehicle?.RaiseDirtyFlags(VehicleDirtyFlags.DamageModel);}catch(Exception){}
            // Undo may run after the original editor has been recreated. Refresh only currently active editors for this component, without
            // changing selection. 0.22.24: one frame later, never inside the native operation (CRASH-MAP.md D).
            var pointer=component.Pointer;var owner=applicationEditor;
            PlateMeshDeferred.NextFrame("editor refresh",()=>RefreshEditors(owner,pointer));
        }
        private bool EnsureSavedName(PlateStructureMesh mesh,ThicknessMode? thicknessMode)
        {
            var wanted=mesh.Pointer==oldMesh.Mesh.Pointer?originalName:preparedName;
            if(wanted==null||!wanted.Contains(CompositePlate.FrontMarker,StringComparison.Ordinal))return RuntimeCompositePlate.EnsureName(mesh,thicknessMode);
            if(mesh.ToMeshBlueprint(false)?.Name==wanted)return true;
            var blueprint=mesh.ToMeshBlueprint(false)??throw new InvalidOperationException("Mesh blueprint unavailable.");
            var beforeName=mesh.ToJson(true);
            blueprint.Name=wanted;mesh.LoadBlueprint(blueprint);mesh.Rebuild();
            if(LayerFaces.VerifyCopy(beforeName,mesh.ToJson(true)) is {} changed)throw new InvalidOperationException("Naming the saved front changed the mesh: "+changed);
            return mesh.ToMeshBlueprint(false)?.Name==wanted;
        }
        private static void RefreshEditors(IVehicleEditor owner,IntPtr componentPointer)
        {
            var editors=owner.SubEditors.ActiveEditors;
            var count=editors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.VehicleDesigner.DesignerEditor>>().Count;
            for(int i=0;i<count;i++)
            {
                var active=editors[i]?.TryCast<PlateStructureEditor>();
                if(active!=null&&active.IsValid()&&active.Component.Pointer==componentPointer)active.RequestEditorRefresh();
            }
        }
        /// <summary>True when every active editor of this plate targets the mesh the plate holds now (the native editor has caught up with the step).</summary>
        private bool EditorCaughtUp()
        {
            if(!reference.TryGet(out var item)||item==null||item.Released)return true;
            var current=item.GetBehaviour(componentId)?.TryCast<PlateStructure>();
            if(current==null||!current.HasMesh)return true;
            var editors=applicationEditor.SubEditors.ActiveEditors;
            var count=editors.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Sprocket.VehicleDesigner.DesignerEditor>>().Count;
            for(int i=0;i<count;i++)
            {
                var active=editors[i]?.TryCast<PlateStructureEditor>();
                if(active==null||!active.IsValid()||active.Component.Pointer!=current.Pointer)continue;
                var live=active.mesh;
                if(live!=null&&!live.Released&&live.Pointer!=current.Mesh.Pointer)return false;
            }
            return true;
        }
        /// <summary>After a successful operation: re-assert the name on whatever mesh the plate holds now and clear the cache.</summary>
        internal void Finish()
        {
            try
            {
                if(released||!Resolve())return;
                if(component.HasMesh&&Applied&&!EnsureSavedName(component.Mesh,mode))throw new InvalidOperationException("Saved front name was not retained.");
                RuntimeCompositePlate.Invalidate(component);
                try{component.Vehicle?.RaiseDirtyFlags(VehicleDirtyFlags.DamageModel);}catch(Exception){}
            }
            catch(Exception ex){Plugin.ModLog.LogWarning("Composite plate identity refresh failed: "+ex.Message);}
        }
        internal void Release()
        {
            if(released)return;released=true;
            // 0.22.24 (CRASH-MAP.md D): the leases on the old and the new mesh stay until the native editor targets the plate's current mesh
            // again, and no PurgeUnused: a native edit on a just-purged mesh touched freed memory.
            PlateMeshDeferred.WhenReady("mesh lease release",EditorCaughtUp,()=>{oldMesh.Dispose();newMesh.Dispose();oldBlueprint.Release();newBlueprint.Release();});
        }
    }
}
