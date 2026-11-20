using System.Text.Json.Nodes;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using NVector=System.Numerics.Vector3;
using UVector=UnityEngine.Vector3;
using UMatrix=UnityEngine.Matrix4x4;
namespace SprocketExpandedBallistics;

internal sealed record PreparedComposerFill(IntPtr Plate,IntPtr Mesh,string BeforeJson,string Material,ThicknessMode Mode,ComposerMeshPlan Plan,string Inspection,double ReserveMm=50);

// Called only from designer buttons, never from Update or Play. Reuses the existing mesh access gates.
internal static class RuntimeComposerFill
{
    internal static bool Designer=>RuntimeCassettePlateVisual.ComposerDesignerActive;
    internal static void ReportFollowWall(PlateStructure plate,FollowWallAttempt? attempt,bool refused,bool applying=true)
    {
        if(attempt==null||!FollowWallDump.ShouldWrite(Designer,applying,refused,attempt.Warning))return;
        var summary=refused?"Convert to layers refused: "+attempt.Summary:attempt.Summary;
        Plugin.ModLog.LogWarning("[Follow wall] Plate #"+plate.VUID.Value+": "+summary);
        FollowWallDump.TryWrite(Path.Combine(BepInEx.Paths.ConfigPath,"sprocket.expandedballistics.layer-overlay"),plate.VUID.Value,attempt,m=>Plugin.ModLog.LogWarning(m));
    }

    internal static PreparedComposerFill Prepare(PlateStructureEditor editor,ComposerMeshAction action,double? offsetMm=null,CheekShellRequest? shell=null,string? newMaterial=null,bool convert=false,bool diagnosticApply=false,double? layerDepthMm=null)
    {
        if(!Designer||editor==null||!editor.IsValid())throw new InvalidOperationException("Composer mesh actions are available in the designer only.");
        var plate=editor.Component;
        if(!RuntimeCompositePlate.Is(plate))
        {
            // 0.22.74: with the option chosen first, Apply converts an add-on plate in the same step (TryPrepared); only a plate that cannot be converted refuses, with the reason.
            if(!convert)throw new InvalidOperationException("Select a Composite armour plate.");
            if(RuntimeCompositePlateEdit.Eligibility(plate) is {} cannot)throw new InvalidOperationException(cannot);
        }
        if(plate.VehicleTransform.IsMirrored||plate.VehicleTransform.Mirror!=null)throw new InvalidOperationException("Remove mirrored pairing before changing face direction.");
        var selected=RuntimeCompositePlateEdit.SelectedFaces(editor,SnapshotUse.Fresh,out var unreadable);
        var before=PlateMeshAccess.Read(plate,"composer fill",SnapshotUse.Fresh);
        if(unreadable||before==null)throw new InvalidOperationException(PlateMeshAccess.Busy);
        var json=plate.Mesh.ToJson(true);var beforeJson=json;var settings=RuntimeCompositePlate.FillLimits;
        var meshName=RuntimeLayerFaces.MeshName(plate,true);
        var savedFront=RuntimeLayerFaces.ReadSavedFront(plate,meshName);
        var oldMode=CompositePlate.ModeFromName(meshName);
        if(action==ComposerMeshAction.Flip)
        {
            var flip=ComposerFill.Plan(json,before,selected,action,NVector.Zero,Array.Empty<FillTriangle>(),settings);
            return new(plate.Pointer,plate.Mesh.Pointer,json,plate.armourTechID,oldMode,flip,flip.Summary);
        }
        var transform=plate.VehicleTransform.LocalToWorldMatrix;
        // Native t is measured in local millimetres. Refuse scaled frames instead of fitting wrong distances.
        var sx=transform.MultiplyVector(UVector.right).magnitude;var sy=transform.MultiplyVector(UVector.up).magnitude;var sz=transform.MultiplyVector(UVector.forward).magnitude;
        if(Math.Abs(sx-1)>.001||Math.Abs(sy-1)>.001||Math.Abs(sz-1)>.001)throw new InvalidOperationException(action==ComposerMeshAction.LayerConvert?"Apply the plate scale before Convert to layers (local millimetres must match metres).":"Apply the plate scale before filling a cavity (local millimetres must match metres).");
        if(action==ComposerMeshAction.BackToBox)
        {
            // 0.22.71: a plate in layers back to its outer faces (front, sides, back where the box needs it); the mesh gets its plain name again.
            if(CompositePlate.LayoutFromName(meshName) is not {} oldKind)throw new InvalidOperationException("Back to box: this plate is not in layers.");
            if(RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(plate.armourTechID,out var oldRecipe)!=true||oldRecipe==null)throw new InvalidOperationException("Back to box: the plate's cassette is not loaded, so its layers cannot be told apart. Nothing changed. "+LayerFaces.BackToBoxAdvice);
            var box=savedFront!=null?LayerFaces.RestoreFront(meshName!,json):LayerFaces.ToBox(json,LayerLayout.Build(oldRecipe.Preset,oldKind,RuntimeLayerFaces.MaterialLabel),RuntimeLayerFaces.Forward(plate),LayerFaces.BoxCheck(json,oldRecipe,id=>{var d=RuntimeLayerFaces.Density(id);return double.IsFinite(d)?d:oldRecipe.Passive.Density;},RuntimeLayerFaces.Forward(plate),RuntimeCompositePlate.LayerWallMm,RuntimeLayerFaces.Centre(plate)));
            var boxPlan=new ComposerMeshPlan(box.Json,ComposerFill.Read(box.Json,true),0,0,0,box.Summary,true){Action=ComposerMeshAction.BackToBox,Total=box.Points,MaximumMm=short.MaxValue,MeshName=savedFront is {} restored?LayerFaces.SaveFront(ThicknessMode.Uniform,box.Json,restored.Front):null};
            return new(plate.Pointer,plate.Mesh.Pointer,json,plate.armourTechID,ThicknessMode.Uniform,boxPlan,box.Summary);
        }
        if(action==ComposerMeshAction.LayerConvert)
        {
            // 0.22.60: the whole plate is split by its own recipe; the material id stays the recipe, the mesh name records the layout. No host is needed.
            var recipeId=newMaterial??plate.armourTechID;
            if(RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(recipeId,out var layerRecipe)!=true||layerRecipe==null)throw new InvalidOperationException("Apply a cassette to the plate first: Convert to layers splits the plate by the cassette's layers.");
            // 0.22.71: a plate that is already in layers goes back to its box first (with the recipe it was made with) and is then split again by this recipe: one step for Undo.
            string rebuilt="";
            if(CompositePlate.LayoutFromName(meshName) is {} oldKind)
            {
                if(RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(plate.armourTechID,out var oldRecipe)!=true||oldRecipe==null)throw new InvalidOperationException("Convert to layers: the cassette the plate was made with is not loaded, so its layers cannot be told apart. Nothing changed. "+LayerFaces.BackToBoxAdvice);
                var back=savedFront!=null?LayerFaces.RestoreFront(meshName!,json):LayerFaces.ToBox(json,LayerLayout.Build(oldRecipe.Preset,oldKind,RuntimeLayerFaces.MaterialLabel),RuntimeLayerFaces.Forward(plate),LayerFaces.BoxCheck(json,oldRecipe,id=>{var d=RuntimeLayerFaces.Density(id);return double.IsFinite(d)?d:oldRecipe.Passive.Density;},RuntimeLayerFaces.Forward(plate),RuntimeCompositePlate.LayerWallMm,RuntimeLayerFaces.Centre(plate)));
                json=back.Json;selected=savedFront?.Front;rebuilt="This plate is already in layers: it is rebuilt. It goes back to its box ("+back.FrontFaces+" front faces, "+back.SideWalls+" side walls"+(back.BackFaces>0?", "+back.BackFaces+" back faces":"")+") and is then split again by the cassette's layers.\n";
            }
            // 0.22.61: a kneaded box is layered from the front of the vehicle to the back; a loose plate or a filling as 0.22.60.
            LayerConversion layers;
            // 0.22.65: a message of the planner is plain words; anything unexpected (a plate that cannot be read) is shown as one sentence and the technical text goes to the game log.
            if(selected==null&&savedFront is {} stored&&stored.Front.Length==ComposerFill.Read(json,true).FaceCount)selected=stored.Front;
            try
            {
            try{layers=LayerFaces.Convert(json,layerRecipe,RuntimeLayerFaces.MaterialLabel,id=>{var d=RuntimeLayerFaces.Density(id);return double.IsFinite(d)?d:layerRecipe.Passive.Density;},RuntimeLayerFaces.Forward(plate),RuntimeCompositePlate.LayerWallMm,RuntimeLayerFaces.Centre(plate),selected,layerDepthMm);}
            catch(InvalidOperationException){throw;}
            catch(Exception unexpected){Plugin.ModLog.LogWarning("[Layer faces] Convert to layers could not read the plate: "+unexpected);throw new InvalidOperationException("Convert to layers could not read this plate. Nothing changed.");}
            }
            catch(InvalidOperationException ex)
            {
                if(selected?.Any(x=>x)==true)
                {
                    var outcomes=ex is FollowWallFailure failure?failure.Faces:Enumerable.Range(0,selected.Length).Where(i=>selected[i]).Select(i=>new FollowWallFaceOutcome(i+1,false,ex.Message)).ToArray();
                    ReportFollowWall(plate,new(json,selected.ToArray(),layerRecipe.Preset,outcomes,"Convert to layers: "+ex.Message,true),true,diagnosticApply);
                }
                throw;
            }
            if(layers.SelectedWalls>0)
            {
                var reasons=$"[Layer faces] {layers.SelectedWalls} selected faces kept as steel wall: {layers.SelectedWallReasons}";
                if(layers.Plan.FollowWall?.Warning==true)Plugin.ModLog.LogWarning(reasons);else Plugin.ModLog.LogInfo(reasons);
            }
            if(layers.Box is {SideWallsDropped:>0} dropped)Plugin.ModLog.LogWarning($"[Layer faces] Convert to layers: {dropped.SideWallsDropped} side walls could not be kept: {layers.DropReason}");
            if(!RuntimeCassetteComposer.CanApply(plate,recipeId,out var blocked))throw new InvalidOperationException(blocked);
            return new(plate.Pointer,plate.Mesh.Pointer,beforeJson,recipeId,layers.Mode,layers.Plan,rebuilt+layers.Inspection);
        }
        var structures=Plates(plate).Where(p=>!p.Applique&&p.Pointer!=plate.Pointer&&p.HasMesh).ToArray();
        var ancestors=new List<IntPtr>();for(var t=plate.VehicleTransform.Parent;t!=null&&ancestors.Count<128;t=t.Parent)ancestors.Add(t.Pointer);
        int Rank(PlateStructure p)
        {
            int own=ancestors.IndexOf(p.VehicleTransform.Pointer),parent=ancestors.IndexOf(p.VehicleTransform.Parent?.Pointer??IntPtr.Zero);
            return own>=0?own*2:parent>=0?parent*2+1:1000;
        }
        var host=structures.OrderBy(Rank).ThenBy(p=>(p.VehicleTransform.Position-plate.VehicleTransform.Position).sqrMagnitude).FirstOrDefault();
        if(host==null||Rank(host)>=1000)throw new InvalidOperationException("Attach the composite plate to its turret structure or ring first.");
        var surface=PlateOverlaySnapshotReader.Read(host,"composer host wall")??throw new InvalidOperationException("Turret wall mesh is not ready; release the mouse and retry.");
        var hostToPlate=transform.inverse*host.VehicleTransform.LocalToWorldMatrix;
        if(action is ComposerMeshAction.ConformCheek or ComposerMeshAction.CheekShell)
        {
            var hx=hostToPlate.MultiplyVector(UVector.right);var hy=hostToPlate.MultiplyVector(UVector.up);var hz=hostToPlate.MultiplyVector(UVector.forward);
            if(Math.Abs(hx.magnitude-1)>.001||Math.Abs(hy.magnitude-1)>.001||Math.Abs(hz.magnitude-1)>.001||UVector.Dot(UVector.Cross(hx,hy),hz)<.999f)
                throw new InvalidOperationException("Apply the turret scale and remove mirrored pairing before "+ComposerFill.Label(action)+".");
        }
        var walls=new List<FillTriangle>();var hostWalls=new List<FillTriangle>();var innerWalls=new List<FillTriangle>();var wallFaces=new List<ShellWallFace>();
        // The turret can contain several native structure sections. Include structural
        // descendants of the host (e.g. its cast front), but never an applique or the hull.
        bool IsSection(PlateStructure p)
        {
            if(p.Pointer==host.Pointer)return true;
            for(var t=p.VehicleTransform.Parent; t!=null;t=t.Parent)if(t.Pointer==host.VehicleTransform.Pointer)return true;
            return false;
        }
        foreach(var section in structures.Where(IsSection))
        {
        var sectionSurface=section.Pointer==host.Pointer?surface:PlateOverlaySnapshotReader.Read(section,"composer wall section")??throw new InvalidOperationException("A turret wall section is not ready.");
        var sectionToHost=host.VehicleTransform.LocalToWorldMatrix.inverse*section.VehicleTransform.LocalToWorldMatrix;
        if(action==ComposerMeshAction.CheekShell)
        {
            // 0.22.57: the drawn faces of the turret (not the tessellation), with their own corners and wall thickness, in the host frame.
            var authored=PlateMeshAccess.Read(section,"composer host faces",SnapshotUse.Fresh)??throw new InvalidOperationException("Turret wall mesh is not ready; release the mouse and retry.");
            bool mirrored=sectionSurface.Mirror.GetDeterminant()<0;
            foreach(var face in authored.Faces)
            {
                var corners=face.Corners.Select(c=>Map(sectionToHost,System.Numerics.Vector3.Transform(c,sectionSurface.Mirror))).ToArray();var wall=face.Thickness.ToArray();
                if(mirrored){Array.Reverse(corners);Array.Reverse(wall);}
                wallFaces.Add(new(corners,wall,face.HoleLoops));
            }
        }
        foreach(var part in new[]{sectionSurface.Front,sectionSurface.Back,sectionSurface.Sides})
        for(int j=0;j+2<part.Triangles.Length;j+=3)
        {
            NVector Point(int k)=>Map(sectionToHost,System.Numerics.Vector3.Transform(part.Vertices[part.Triangles[j+k]],sectionSurface.Mirror));
            var a=Point(0);var b=Point(1);var c=Point(2);
            if(action==ComposerMeshAction.CheekShell)
            {
                // 0.22.57: the shell's inside-ray counts only walls the rib runs into, so every triangle is oriented with its stored free-space normal.
                var normal=System.Numerics.Vector3.TransformNormal(part.Normals[part.Triangles[j]],sectionSurface.Mirror);
                var un=sectionToHost.MultiplyVector(new(normal.X,normal.Y,normal.Z));
                if(NVector.Dot(NVector.Cross(b-a,c-a),new(un.x,un.y,un.z))<0)(b,c)=(c,b);
            }
            hostWalls.Add(new(a,b,c));
            if(action==ComposerMeshAction.ConformCheek&&ReferenceEquals(part,sectionSurface.Back))
            {
                // The tessellator's stored normals decide the side, including mirrored sections.
                var normal=System.Numerics.Vector3.TransformNormal(part.Normals[part.Triangles[j]],sectionSurface.Mirror);
                var un=sectionToHost.MultiplyVector(new(normal.X,normal.Y,normal.Z));
                if(NVector.Dot(NVector.Cross(b-a,c-a),new(un.x,un.y,un.z))<0)(b,c)=(c,b);
                innerWalls.Add(new(a,b,c));
            }
            walls.Add(new(Map(hostToPlate,a),Map(hostToPlate,b),Map(hostToPlate,c)));
        }
        }
        var reference=Map(hostToPlate,NVector.Zero);ComposerMeshPlan plan;
        if(action==ComposerMeshAction.CheekShell)
        {
            // Planned in the host frame; the planner maps every point to the plate frame itself before it writes the JSON (te is never a double).
            var bounds=FillBounds.Of("host",hostWalls.SelectMany(t=>new[]{t.A,t.B,t.C}));
            var gunBounds=Obstacles(plate,plate.transform.worldToLocalMatrix,out _,p=>Map(hostToPlate.inverse,p)).Where(b=>b.Label=="gun").ToArray();
            float centre=gunBounds.Length>0?((FillBounds.Of("gun",gunBounds.SelectMany(b=>new[]{b.Min,b.Max})) is {} g)?(g.Min.X+g.Max.X)*.5f:0):0;
            // 0.22.59: this button has its own thickness maximum and takes the rear limit from the window; the config and the other buttons keep CheekLimits.
            var limits=CheekShellFill.ShellLimits(RuntimeCompositePlate.CheekLimits,RuntimeCompositePlate.CheekShellMaximumMm,shell?.RearLimitMm);
            plan=CheekShellFill.Plan(wallFaces,hostWalls,bounds,shell??new(Math.Min(450,limits.MaximumMm),Math.Min(200,limits.MaximumMm)),settings,limits,ComposerFill.GunHalfGap(gunBounds),gunBounds.Length==0,centre,p=>Map(hostToPlate,p)).Plan;
        }
        else if(action is ComposerMeshAction.CheekPreset or ComposerMeshAction.ConformCheek)
        {
            var bounds=FillBounds.Of("host",hostWalls.SelectMany(t=>new[]{t.A,t.B,t.C}));
            // Size the opening in the plate frame, exactly as the overlap check maps renderer bounds.
            var gunBounds=Obstacles(plate,plate.transform.worldToLocalMatrix,out _).Where(b=>b.Label=="gun").ToArray();
            if(action==ComposerMeshAction.ConformCheek)
                gunBounds=Obstacles(plate,plate.transform.worldToLocalMatrix,out _,p=>Map(hostToPlate.inverse,p)).Where(b=>b.Label=="gun").ToArray();
            float centre=0;
            if(gunBounds.Length>0)
            {
                var gunCentre=FillBounds.Of("gun",gunBounds.SelectMany(b=>new[]{b.Min,b.Max}));
                centre=action==ComposerMeshAction.ConformCheek?((gunCentre.Min+gunCentre.Max)*.5f).X:Map(hostToPlate.inverse,(gunCentre.Min+gunCentre.Max)*.5f).X;
            }
            var cheek=action==ComposerMeshAction.ConformCheek
                ?ConformCheekFill.Plan(innerWalls,offsetMm??RuntimeCompositePlate.CheekLimits.MaximumMm,settings,RuntimeCompositePlate.CheekLimits,ComposerFill.GunHalfGap(gunBounds),gunBounds.Length==0,centre,hostWalls).Plan
                :ComposerFill.Cheeks(hostWalls,bounds,settings,RuntimeCompositePlate.CheekLimits,ComposerFill.GunHalfGap(gunBounds),gunBounds.Length==0,centre);
            var mapped=JsonNode.Parse(cheek.Json)!;var v=mapped["vertices"]!.AsArray();
            for(int i=0;i<v.Count;i+=3)
            {
                var p=Map(hostToPlate,new(v[i]!.GetValue<float>(),v[i+1]!.GetValue<float>(),v[i+2]!.GetValue<float>()));
                v[i]=p.X;v[i+1]=p.Y;v[i+2]=p.Z;
            }
            var payload=mapped.ToJsonString();plan=cheek with{Json=payload,After=ComposerFill.Read(payload)};
        }
        else plan=ComposerFill.Plan(json,before,selected,action,reference,walls,settings);
        var material=convert&&newMaterial!=null?newMaterial:plate.armourTechID; // 0.22.74: a plate being converted has no recipe yet; the chosen one is used for the checks
        if(action==ComposerMeshAction.CheekPreset)material="ebCassette_t80u";
        if(!RuntimeCassetteComposer.CanApply(plate,material,out var why))throw new InvalidOperationException(why);
        string inspection=action==ComposerMeshAction.Fill?plan.FillInspection:plan.Summary;
        if(action is ComposerMeshAction.CheekPreset or ComposerMeshAction.ConformCheek or ComposerMeshAction.CheekShell)
        {
            if(RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(material,out var recipe)!=true)throw new InvalidOperationException(action==ComposerMeshAction.CheekShell?"The plate's cassette material is unavailable; apply a cassette to the plate first.":"T-80U cassette is unavailable.");
            // Renderer bounds are Unity world space; map them with the plate's Unity transform (as the plate visual does), not the vehicle frame.
            var obstacles=Obstacles(plate,plate.transform.worldToLocalMatrix,out var missing);
            var stats=plan.Inspect(recipe!.Passive.Density,obstacles);
            inspection=plan.Summary+$"\nWeight {stats.MassKg:0.##} kg, volume {stats.VolumeM3:0.000} m\u00b3.\n"+
                (stats.Warnings.Length>0?"May touch: "+string.Join(", ",stats.Warnings)+". Check before Play.":"No contact found with the available equipment shapes. Check before Play.")+"\n"+
                (missing>0?"Some equipment could not be checked. Check before Play.\n":"");
        }
        return new(plate.Pointer,plate.Mesh.Pointer,json,material,action==ComposerMeshAction.Inward?oldMode:ThicknessMode.FillToWall,plan,inspection,settings.ReserveMm);
    }
    internal static bool Matches(PlateStructureEditor editor,PreparedComposerFill prepared)
    {
        try
        {
            return Designer&&editor!=null&&prepared!=null&&editor.IsValid()&&editor.Component!=null&&
                editor.Component.Pointer==prepared.Plate&&editor.Component.HasMesh&&
                editor.Component.Mesh.Pointer==prepared.Mesh&&editor.Component.Mesh.ToJson(true)==prepared.BeforeJson;
        }
        catch(Exception){return false;}
    }
    private static IEnumerable<PlateStructure> Plates(PlateStructure source)
    {
        foreach(var component in Components(source))if(component.TryCast<PlateStructure>() is {} p)yield return p;
    }
    private static IEnumerable<VehicleComponent> Components(PlateStructure source)
    {
        var items=source.Vehicle.ObjectReader.Items;int n=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<n;i++)
        {
            var item=items[i];if(item==null||item.Released)continue;var list=item.Components;
            int count=list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<count;j++)if(list[j] is {} component)yield return component;
        }
    }
    private static List<FillBounds> Obstacles(PlateStructure plate,UMatrix worldToPlate,out int missing,Func<NVector,NVector>? map=null)
    {
        missing=0;var result=new List<FillBounds>();
        foreach(var c in Components(plate))
        {
            string type;
            try
            {
                type=c.GetIl2CppType().Name;var id=type+" "+c.ComponentID;
                if(!ComposerFill.IsObstacleType(id))continue;
            }
            catch(Exception){missing++;continue;}
            var points=new List<NVector>();
            try
            {
                foreach(var renderer in c.GetComponentsInChildren<UnityEngine.Renderer>(true))
                {
                    if(renderer==null)continue;var b=renderer.bounds;
                    points.AddRange(FillBounds.RendererCorners(new(b.center.x,b.center.y,b.center.z),new(b.extents.x,b.extents.y,b.extents.z),p=>map?.Invoke(Map(worldToPlate,p))??Map(worldToPlate,p)));
                }
            }
            catch(Exception){missing++;continue;}
            if(points.Count==0){missing++;continue;}
            string label=type.Contains("Cannon",StringComparison.OrdinalIgnoreCase)?"gun":type.Contains("crew",StringComparison.OrdinalIgnoreCase)?"crew seat":type.Contains("carousel",StringComparison.OrdinalIgnoreCase)||type.Contains("carrousel",StringComparison.OrdinalIgnoreCase)?"carousel":"ammunition";
            if(c.TryCast<Sprocket.Vehicles.CrewSystems.CrewSeat>() is {} seat)
            {
                var name=seat.SeatBlueprint?.Name;
                if(!string.IsNullOrWhiteSpace(name)&&!CombatDamageSummary.IsAnonymous(name)&&!name.Any(char.IsDigit))label=name.ToLowerInvariant()+" seat";
            }
            result.Add(FillBounds.Of(label,points));
        }
        return result;
    }
    private static NVector Map(UMatrix m,NVector p){var v=m.MultiplyPoint3x4(new(p.X,p.Y,p.Z));return new(v.x,v.y,v.z);}
}

/// <summary>
/// 0.22.60 "lagen als vlakken": the prepared recipes (per loaded cassette model and layout), the per-mesh table of a plate in layers, the native
/// face of a hit, and the composer line. No hook; the hit runs in NativeMaterialReactions.ReachedLayered (RuntimeGroupedProtection.cs).
/// </summary>
internal static class RuntimeLayerFaces
{
    // ---------------- recipes ----------------
    private sealed class Prepared{internal LayeredRecipe? PerLayer,PerBlock;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CassetteModel,Prepared> recipes=new();
    private static IReadOnlyDictionary<string,CassetteMaterial>? technology;
    /// <summary>The native rhaFactor of a layer material, from the same Technology files the cassette models are built from (CassetteDefaults).</summary>
    private static double RhaFactor(string id)
    {
        technology??=CassetteTechnology.ReadDirectory(Path.Combine(BepInEx.Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology"));
        return technology.TryGetValue(id,out var m)?m.RhaFactor:double.NaN;
    }
    internal static string MaterialLabel(string id)
    {
        try
        {
            var known=SprocketExpandedBallistics.Materials.MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==id);
            return known==null?LayerLayout.DefaultLabel(id):CombatReportLanguage.Material(SprocketExpandedBallistics.Materials.MaterialDatabase.MenuLabel(known),id);
        }
        catch(Exception){return LayerLayout.DefaultLabel(id);}
    }
    internal static LayeredRecipe Recipe(CassetteModel model,LayerLayoutKind kind)
    {
        var prepared=recipes.GetOrCreateValue(model);
        return kind==LayerLayoutKind.PerLayer
            ?prepared.PerLayer??=LayeredRecipe.Build(model,LayerLayout.Build(model.Preset,kind,MaterialLabel),RhaFactor)
            :prepared.PerBlock??=LayeredRecipe.Build(model,LayerLayout.Build(model.Preset,kind,MaterialLabel),RhaFactor);
    }
    internal static void MaterialsReloaded(){technology=null;}
    /// <summary>0.22.61: the native density of a layer material (same Technology files); NaN when unknown.</summary>
    internal static double Density(string id)
    {
        technology??=CassetteTechnology.ReadDirectory(Path.Combine(BepInEx.Paths.GameRootPath,"Sprocket_Data/StreamingAssets/Technology"));
        return technology.TryGetValue(id,out var m)?m.Density:double.NaN;
    }

    // ---------------- 0.22.61: the weight of a plate in layers ----------------
    // From the existing PlateStructure.Build postfix (RuntimeCassettePlateVisual.Built): the plate gets the density of its real faces (each layer at the
    // density of its own layers, side walls as steel), and its armour mass and cost follow from the native armour volume, exactly as the existing
    // missing-recipe fallback (RuntimeCassetteComposer.AfterBuild) writes armourDensity, mass and cost. In the designer the mesh is read through the
    // snapshot gate; while it is being edited the weighing waits (PlateMeshDeferred) until the gate is quiet. Every other plate is not touched.
    private static readonly HashSet<IntPtr> weighing=new();
    internal static void Weigh(PlateStructure plate)
    {
        try
        {
            NativeMaterialReactions.SolidMarkBuilt(plate); // 0.22.79: the [Solid block] mark is noted here, at build (never during a hit); it never throws and changes nothing below
            if(plate==null||!plate.HasMesh||!CompositePlate.IsCassette(plate.armourTechID))return;
            if(SolidMarks.Answer(plate.Pointer.ToInt64(),out _,out _) is var solid&&solid!=SolidMarkAnswer.Unmarked&&WeighSolid(plate,solid==SolidMarkAnswer.Marked))return; // 0.22.82 solid
            if(CompositePlate.LayoutFromName(MeshName(plate)) is not {} kind)return;
            if(TryWeigh(plate,kind))return;
            var pointer=plate.Pointer;if(!weighing.Add(pointer))return;
            PlateMeshDeferred.WhenReady("layer weight",()=>!PlateMeshAccess.Gate.Busy,()=>{weighing.Remove(pointer);try{if(plate!=null&&plate.Pointer==pointer&&plate.HasMesh)TryWeigh(plate,kind);}catch(Exception ex){Warn(ex);}},10f);
        }
        catch(Exception ex){Warn(ex);}
    }
    private static int weightWarnings;
    private static void Warn(Exception ex){if(weightWarnings++<4)Plugin.ModLog.LogWarning("[Layer faces] Weight of a plate in layers unavailable: "+ex.Message);}
    private static bool TryWeigh(PlateStructure plate,LayerLayoutKind kind)
    {
        var snap=RuntimeArmourResponses.Snapshot;
        if(snap.Cassettes?.TryGetValue(plate.armourTechID,out var model)!=true||model==null)return true;
        var designer=RuntimeComposerFill.Designer;
        if(designer&&PlateMeshAccess.Gate.Busy)return false;
        var snapshot=designer?PlateMeshAccess.Read(plate,"layer weight",SnapshotUse.Fresh):PlateMeshAccess.ReadUngated(plate.Mesh,out _);
        if(snapshot==null)return false;
        var json=plate.Mesh.ToJson(true);var mesh=LayerMesh.Read(json);if(mesh.Polygons.Length!=snapshot.Faces.Length)return true;
        var recipe=Recipe(model,kind);var ranks=LayerHitIndex.RanksOf(snapshot,json);
        var weight=LayerWeight.Of(mesh,ranks,recipe.Layout,model.Preset,id=>{var d=Density(id);return double.IsFinite(d)?d:model.Passive.Density;},model.Passive.Density);
        if(!double.IsFinite(weight.Density)||weight.Density<=0)return true;
        var density=(float)weight.Density;
        // 0.22.62: the native armour volume was seen at exactly twice the volume of the faces on plate #280 after an edit (0.43 against 0.215 m3, while the first build gave 0.215), so a stale or doubled native figure
        // must not decide the weight: when it differs from the faces by more than 2 %, the volume of the faces is used (the same fields are written).
        var volume=plate.armourVolume;var own=(float)weight.VolumeM3;
        if(float.IsFinite(own)&&own>0&&!(Math.Abs(volume-own)<=.02f*own)){if(weightWarnings++<4)Plugin.ModLog.LogWarning(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Layer faces] plate #{plate.VUID.Value}: native armour volume {volume:0.####} m3 differs from the {own:0.####} m3 of its faces; the faces are used for the weight."));volume=own;}
        var mass=volume*density;
        if(!float.IsFinite(mass)||mass<0)return true;
        plate.armourDensity=density;
        plate.SetMass(mass,Sprocket.Vehicles.MassType.Armour);plate.cachedMass=plate.GetMass(Sprocket.Vehicles.MassType.Everything);plate.RecalculateCenterOfMass();
        if(SprocketExpandedBallistics.Materials.MaterialBalance.TryMaterialCost(mass,plate.armourCostMultiplier,Sprocket.Constants.CostPerKgOfArmour,out var cost))
            plate.SetCost(cost,Sprocket.Vehicles.MassType.Armour,Sprocket.Vehicles.CostType.Material);
        plate.Vehicle.RaiseDirtyFlags(Sprocket.Vehicles.VehicleDirtyFlags.DamageModel);
        if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Layer faces] plate #{plate.VUID.Value}: density {model.Passive.Density:0.#} -> {density:0.#} kg/m3 ({weight.VolumeM3:0.####} m3 of faces), mass {mass:0.#} kg from the armour volume {volume:0.####} m3."));
        return true;
    }
    // ---- 0.22.82 solid weight (begin) ----
    // A plate marked [Solid block] weighs its closed volume x the density of its recipe over the nominal depth (air included), from the same Build postfix and after
    // every Build: only SetMass (with cachedMass, as Build itself), the centre of mass and the cost are written; armourDensity keeps the value of the game (the hit
    // reads its density elsewhere, and the mod's own UI compares armourDensity). The mark comes from SolidMarks (noted by SolidMarkBuilt just before); when it is still
    // unknown in the designer (gate busy at Build), one deferred check reads the name once the gate is free. A marked plate that is not closed keeps the game's weight.
    private static readonly HashSet<IntPtr> solidWeighing=new();
    private static readonly Dictionary<IntPtr,float> solidLogged=new();
    private static int solidWarnings,solidLines;
    /// <summary>True when the plate is handled here (marked: weighed now or later); false for an unknown mark, which goes on as before after a deferred check is queued.</summary>
    private static bool WeighSolid(PlateStructure plate,bool marked)
    {
        if(!marked)
        {
            if(!RuntimeComposerFill.Designer)return false;
            var waiting=plate.Pointer;if(!solidWeighing.Add(waiting))return false;
            PlateMeshDeferred.WhenReady("solid weight check",()=>!PlateMeshAccess.Gate.Busy,()=>{solidWeighing.Remove(waiting);try{if(plate!=null&&plate.Pointer==waiting&&plate.HasMesh&&CompositePlate.IsCassette(plate.armourTechID)&&CompositePlate.IsSolidBlock(MeshName(plate,true)))TryWeighSolid(plate);}catch(Exception ex){SolidWarn(ex);}},10f);
            return false;
        }
        if(TryWeighSolid(plate))return true;
        var pointer=plate.Pointer;if(!solidWeighing.Add(pointer))return true;
        PlateMeshDeferred.WhenReady("solid weight",()=>!PlateMeshAccess.Gate.Busy,()=>{solidWeighing.Remove(pointer);try{if(plate!=null&&plate.Pointer==pointer&&plate.HasMesh)TryWeighSolid(plate);}catch(Exception ex){SolidWarn(ex);}},10f);
        return true;
    }
    private static void SolidWarn(Exception ex){if(solidWarnings++<4)Plugin.ModLog.LogWarning("[Solid block] Weight of a solid block unavailable; the plate keeps the weight of the game: "+ex.Message);}
    /// <summary>False: the mesh cannot be read now (try again when the gate is free).</summary>
    private static bool TryWeighSolid(PlateStructure plate)
    {
        try
        {
            var snap=RuntimeArmourResponses.Snapshot;
            if(snap.Cassettes?.TryGetValue(plate.armourTechID,out var model)!=true||model==null)return true;
            var designer=RuntimeComposerFill.Designer;
            if(designer&&PlateMeshAccess.Gate.Busy)return false;
            var snapshot=designer?PlateMeshAccess.Read(plate,"solid weight",SnapshotUse.Fresh):PlateMeshAccess.ReadUngated(plate.Mesh,out _);
            if(snapshot==null)return false;
            var shape=SolidShape.FromJson(plate.Mesh.ToJson(true));
            if(!shape.Closed)
            {
                if(solidWarnings++<4)Plugin.ModLog.LogWarning("[Solid block] plate #"+plate.VUID.Value+" is marked but not closed; it keeps the weight of the game (its skins). "+shape.Refusal);
                return true;
            }
            var mass=(float)SolidBlockApply.MassKg(shape.VolumeM3,model.Passive.Density);
            if(!float.IsFinite(mass)||mass<=0)return true;
            plate.SetMass(mass,Sprocket.Vehicles.MassType.Armour);plate.cachedMass=plate.GetMass(Sprocket.Vehicles.MassType.Everything);plate.RecalculateCenterOfMass();
            if(SprocketExpandedBallistics.Materials.MaterialBalance.TryMaterialCost(mass,plate.armourCostMultiplier,Sprocket.Constants.CostPerKgOfArmour,out var cost))
                plate.SetCost(cost,Sprocket.Vehicles.MassType.Armour,Sprocket.Vehicles.CostType.Material);
            // one line per plate and mass, at most 32 per session: the evidence of the weight in the game log
            if(solidLines<32&&(!solidLogged.TryGetValue(plate.Pointer,out var last)||last!=mass))
            {
                solidLines++;if(solidLogged.Count>256)solidLogged.Clear();solidLogged[plate.Pointer]=mass;
                Plugin.ModLog.LogInfo(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Solid block] weight: plate #{plate.VUID.Value}: {shape.Pieces} closed pieces, {shape.VolumeM3:0.######} m3 x {model.Passive.Density:0.#} kg/m3 = {mass:0.#} kg ({(designer?"designer":"outside the designer")}); armourDensity stays {plate.armourDensity:0.#} kg/m3."));
            }
            return true;
        }
        catch(Exception ex){SolidWarn(ex);return true;}
    }
    // ---- 0.22.82 solid weight (end) ----

    // ---------------- 0.22.61: the overlay of a plate in layers ----------------
    /// <summary>The native overlay copy re-mapped per face (LayerOverlay); null for every plate that is not in layers (its overlay stays as it was).</summary>
    internal static OverlayCopy? Overlay(PlateStructure plate,OverlayCopy data)
    {
        try
        {
            if(SolidOverlay(plate,data) is {} solid)return solid; // 0.22.83 solid overlay
            if(CompositePlate.LayoutFromName(MeshName(plate)) is not {} kind)return null;
            if(RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(plate.armourTechID,out var model)!=true||model==null)return null;
            var snapshot=PlateMeshAccess.Read(plate,"layer overlay",SnapshotUse.Fresh);if(snapshot==null)return null;
            var json=plate.Mesh.ToJson(true);var mesh=LayerMesh.Read(json) with{FollowWall=plate.Mesh.ToMeshBlueprint(false)?.Name?.Contains(CompositePlate.FrontMarker,StringComparison.Ordinal)==true};if(mesh.Polygons.Length!=snapshot.Faces.Length)return null;
            var ranks=LayerHitIndex.RanksOf(snapshot,json);var layout=Recipe(model,kind).Layout;bool painted=RuntimeCassettePlateVisual.LayerBoxPainted;
            var report=painted?new LayerOverlay.OverlayReport():null;
            var result=painted?LayerOverlay.RemapPainted(data,mesh,ranks,layout,report):LayerOverlay.Remap(data,mesh,ranks,layout);
            if(RuntimeCassettePlateVisual.LayerOverlayDump)WriteDump(plate,json,mesh,ranks,layout,data,report,painted?"painted":"layers",plate.armourTechID);
            return result;
        }
        catch(Exception ex){if(weightWarnings++<4)Plugin.ModLog.LogWarning("[Layer faces] Layer overlay unavailable; the whole recipe is shown: "+ex.Message);return null;}
    }
    // ---- 0.22.83 solid overlay (begin) ----
    // A plate marked [Solid block] shows itself in the layer view as one dense block in the colour of its recipe (the band of the thickest solid material), shaded by the
    // native normals; the triangles off the drawn surface (the inside of the skin, and a skin outside a face that winds the other way) get zero area (SolidBlockOverlay). Null for every plate without the mark (it goes on
    // exactly as before), and null when the mesh cannot be read now (the overlay is retried as for any plate) or on an error (the whole recipe is shown, as before).
    // 0.22.84: the layers of the recipe as bands from front to back along the front of the vehicle (SolidBlockOverlay.ApplyBands, d per point, once per rebuild: this
    // method only runs when the overlay signature changed); when the bands cannot be made, the one colour of 0.22.83 and the reason in the one log line of the plate.
    private static readonly Dictionary<IntPtr,string> solidOverlayLogged=new();
    private static int solidOverlayWarnings;
    private static OverlayCopy? SolidOverlay(PlateStructure plate,OverlayCopy data)
    {
        if(!CompositePlate.IsSolidBlock(MeshName(plate)))return null;
        try
        {
            CassetteModel? model=null;RuntimeArmourResponses.Snapshot.Cassettes?.TryGetValue(plate.armourTechID,out model);
            // the view is cached on its signature (which holds the mark), so a mesh that cannot be read now still gets the colour; only the inside test needs the mesh
            var json=PlateMeshAccess.Read(plate,"solid overlay",SnapshotUse.Fresh)!=null?plate.Mesh.ToJson(true):null;
            var report=new SolidBlockOverlay.Report();
            NVector? forward=null;try{forward=Forward(plate);}catch(Exception){} // no front direction: the one colour of 0.22.83
            var result=SolidBlockOverlay.ApplyBands(data,json,model?.Preset,forward,report);
            var state=string.Create(System.Globalization.CultureInfo.InvariantCulture,$"{report.Hidden}|{report.Bands}|{report.BandRefusal}");
            if(solidOverlayLogged.Count<32&&(!solidOverlayLogged.TryGetValue(plate.Pointer,out var logged)||logged!=state))
            {
                solidOverlayLogged[plate.Pointer]=state;
                var look=report.Bands?string.Create(System.Globalization.CultureInfo.InvariantCulture,$"the recipe as bands from front to back ({report.BandPoints} points, {report.BandNeighbour} from the nearest point, {report.BandPieces} piece(s), {report.BandMs:0.#} ms)")
                    :string.Create(System.Globalization.CultureInfo.InvariantCulture,$"in the colour of {report.Material} (depth {report.Depth:0.###}; no bands: {report.BandRefusal})");
                Plugin.ModLog.LogInfo(string.Create(System.Globalization.CultureInfo.InvariantCulture,$"[Solid block] overlay: plate #{plate.VUID.Value}: {report.Triangles} triangles {look}, {report.Hidden} off the drawn surface not drawn ({report.Inside} inside the shape, {report.Outside} outside){(report.Closed?".":"; every triangle drawn: "+report.Refusal)}"));
            }
            return result;
        }
        catch(Exception ex){if(solidOverlayWarnings++<4)Plugin.ModLog.LogWarning("[Solid block] Solid overlay unavailable; the whole recipe is shown: "+ex.Message);return null;}
    }
    // ---- 0.22.83 solid overlay (end) ----
    /// <summary>0.22.68: one file per plate in layers, overwritten each time the overlay is built; a failure to write is a line in the log and nothing else.</summary>
    private static void WriteDump(PlateStructure plate,string json,LayerMesh mesh,IReadOnlyList<int> ranks,LayerLayout layout,OverlayCopy data,LayerOverlay.OverlayReport? report,string look,string recipe)
    {
        try
        {
            var text=LayerOverlayDump.Build(json,mesh,ranks,layout,data,report,look,plate.VUID.Value,recipe);
            var folder=Path.Combine(BepInEx.Paths.ConfigPath,"sprocket.expandedballistics.layer-overlay");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"plate-"+plate.VUID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+".json"),text);
        }
        catch(Exception ex){Plugin.ModLog.LogWarning("[Layer faces] Overlay dump not written: "+ex.Message);}
    }
    /// <summary>0.22.63: the plate is a box (something lies behind its front faces); Convert to composite plate then leaves the shape alone. A read that fails or is not clean counts as not a box (the old behaviour).</summary>
    internal static bool IsBoxPlate(PlateStructure plate)
    {
        try
        {
            if(plate==null||!plate.HasMesh)return false;
            if(PlateMeshAccess.Read(plate,"box check",SnapshotUse.Fresh)==null)return false;
            return LayerFaces.IsBox(plate.Mesh.ToJson(true),Forward(plate),Centre(plate));
        }
        catch(Exception){return false;}
    }
    /// <summary>0.22.64: the centre of the vehicle (its origin) in the frame of the plate mesh.</summary>
    internal static NVector Centre(PlateStructure plate)
    {
        var c=plate.VehicleTransform.LocalToWorldMatrix.inverse.MultiplyPoint3x4(UVector.zero);
        return new NVector(c.x,c.y,c.z);
    }
    /// <summary>The designer's forward axis (the front of the vehicle) in the frame of the plate mesh.</summary>
    internal static NVector Forward(PlateStructure plate)
    {
        var f=plate.VehicleTransform.LocalToWorldMatrix.inverse.MultiplyVector(UVector.forward);
        return NVector.Normalize(new NVector(f.x,f.y,f.z));
    }

    // ---------------- the per-mesh table ----------------
    // Keyed like the hit-geometry cache of NativeCassetteGeometry (mesh pointer and content signature; that file stays byte-exact): built once per
    // mesh; between builds a lookup compares the face and loop counts, and at most once a second the content signature (a full snapshot read).
    // RuntimeCompositePlate's mesh events (FromJson, LoadBlueprint, RequestMeshRefresh) drop an entry at once. A plate not in layers is remembered.
    private sealed class Entry{internal string Material="";internal LayerHitIndex? Index;internal int Faces,Loops;internal float Checked;}
    private static readonly Dictionary<IntPtr,Entry> tables=new();
    private static int warnings;
    internal static void Invalidate(IntPtr mesh){tables.Remove(mesh);kinds.Remove(mesh);}
    internal static void Clear(){tables.Clear();kinds.Clear();last=null;names.Clear();}
    private static readonly Dictionary<IntPtr,(int Faces,int Loops,string Material,LayerLayoutKind? Kind,float Checked)> kinds=new();
    /// <summary>
    /// 0.22.69 (uniform hit): is this plate a plate in layers, and with which layout. Only the name of the mesh is read (the mark that Convert to layers leaves); no face table, no ranks, no snapshot of the faces.
    /// Cached per mesh while its face and loop counts and its material stay the same.
    /// </summary>
    internal static bool TryKind(PlateStructure plate,out LayerLayoutKind kind)
    {
        kind=default;
        try
        {
            if(!plate.HasMesh)return false;
            var mesh=plate.Mesh;var pointer=mesh.Pointer;var edit=mesh.editMesh;if(edit==null)return false;
            int faces=edit.FaceCount,loops=edit.LoopCount;var material=plate.armourTechID??"";var now=UnityEngine.Time.unscaledTime;
            if(kinds.TryGetValue(pointer,out var known)&&known.Faces==faces&&known.Loops==loops&&known.Material==material&&(known.Kind==null||now-known.Checked<1f))
            {if(known.Kind is {} cached){kind=cached;return true;}return false;}
            var found=CompositePlate.LayoutFromName(mesh.ToMeshBlueprint(false)?.Name);
            if(kinds.Count>256)kinds.Clear();
            kinds[pointer]=(faces,loops,material,found,now);
            if(found is {} layout){kind=layout;return true;}
            return false;
        }
        catch(Exception error)
        {
            if(warnings++<4)Plugin.ModLog.LogWarning("[Layer faces] Plate layout unavailable: "+error.Message);
            return false;
        }
    }
    internal static bool TryTable(PlateStructure plate,CassetteModel model,out LayerHitIndex index)
    {
        index=null!;
        try
        {
            if(!plate.HasMesh)return false;
            var mesh=plate.Mesh;var pointer=mesh.Pointer;var edit=mesh.editMesh;if(edit==null)return false;
            int faces=edit.FaceCount,loops=edit.LoopCount;var material=plate.armourTechID??"";var now=UnityEngine.Time.unscaledTime;
            tables.TryGetValue(pointer,out var entry);
            if(entry!=null&&entry.Faces==faces&&entry.Loops==loops&&entry.Material==material&&(entry.Index==null||now-entry.Checked<1f))
            {index=entry.Index!;return entry.Index!=null;}
            var kind=CompositePlate.LayoutFromName(mesh.ToMeshBlueprint(false)?.Name);
            if(kind is not {} layout){tables[pointer]=new(){Faces=faces,Loops=loops,Material=material,Checked=now};return false;}
            // Designer (armour simulator): the gated snapshot; Play: the plate is not being edited.
            var designer=RuntimeComposerFill.Designer;
            var snapshot=designer?PlateMeshAccess.Read(plate,"layer hit faces",SnapshotUse.Display):PlateMeshAccess.ReadUngated(mesh,out _);
            if(snapshot==null)return false;
            if(entry?.Index is {} kept&&kept.Signature==snapshot.Signature&&kept.Kind==layout&&entry.Material==material)
            {entry.Checked=now;entry.Faces=faces;entry.Loops=loops;index=kept;return true;}
            string? json=null;try{if(!designer||!PlateMeshAccess.Gate.Busy)json=mesh.ToJson(true);}catch(Exception){}
            var built=LayerHitIndex.Build(snapshot,json,layout,Recipe(model,layout).Layout.Groups.Length);
            tables[pointer]=new(){Material=material,Index=built,Faces=faces,Loops=loops,Checked=now};
            if(ReleaseDiagnostics.Enabled)Plugin.ModLog.LogInfo($"[Layer faces] table for mesh 0x{pointer.ToInt64():x}: {built.Ranks.Length} faces, {built.Assigned} assigned, {built.Groups} layers ({layout}).");
            index=built;return true;
        }
        catch(Exception error)
        {
            if(warnings++<4)Plugin.ModLog.LogWarning("[Layer faces] Plate layer table unavailable: "+error.Message);
            return false;
        }
    }
    /// <summary>
    /// The face of one native hit from the table: the hit triangle's first vertex selects the front (1) or back (2) marker range, the vertex offset
    /// inside it selects the face by binary search, and all three vertices must lie in that face (the rule of DeclaredFacePolicy.ResolveSamples).
    /// No array is copied: the interop accessors read the native arrays in place.
    /// </summary>
    internal static bool TryFace(Sprocket.DamageModelling.PenetrationSimulation sim,Sprocket.DamageModelling.StructureIntersection hit,LayerHitIndex index,out int face,out bool back)
    {
        face=-1;back=false;
        if(hit.structureIndex>=sim.meshes.Length)return false;
        var mesh=sim.meshes[hit.structureIndex];var triangles=mesh.triangles;var markers=mesh.meshMarkers;var types=mesh.meshMarkerTypes;
        if(triangles==null||markers==null||types==null||markers.Length!=types.Length)return false;
        var offset=hit.triangleIndex;
        if(offset<0||offset%3!=0||offset>triangles.Length-3)return false;
        int a=triangles[offset],b=triangles[offset+1],c=triangles[offset+2];
        uint start=0;
        for(int range=0;range<markers.Length;range++)
        {
            var end=markers[range];
            if(end<=start)return false;
            if(a>=start&&a<end)
            {
                if(types[range] is not (1 or 2)||b<start||b>=end||c<start||c>=end)return false;
                face=index.Face((int)start,(int)(end-start),a,b,c);back=types[range]==2;
                return face>=0;
            }
            start=end;
        }
        return false;
    }

    // ---------------- the composer and inspector line ----------------
    private static (string Signature,string Material,string? Name,string Text)? last;
    // The inspector draws every frame and the composer reads 4x/s: the mesh name is read at most once a second per mesh.
    private static readonly Dictionary<IntPtr,(string? Name,float Until)> names=new();
    internal static string? MeshName(PlateStructure plate,bool fresh=false)
    {
        try
        {
            if(!plate.HasMesh)return null;
            var mesh=plate.Mesh;var pointer=mesh.Pointer;var now=UnityEngine.Time.unscaledTime;
            if(names.TryGetValue(pointer,out var known)&&!fresh&&now<known.Until)return known.Name;
            if(PlateMeshAccess.Gate.Busy)return names.TryGetValue(pointer,out known)?known.Name:null;
            if(names.Count>256)names.Clear();
            var name=mesh.ToMeshBlueprint(false)?.Name;names[pointer]=(name,now+1f);return name;
        }
        catch(Exception){return null;}
    }
    private static readonly Dictionary<string,((string Json,bool[] Front,string Fingerprint)? Value,bool Broken)> fronts=new(StringComparer.Ordinal);
    internal static (string Json,bool[] Front,string Fingerprint)? ReadSavedFront(PlateStructure plate,string? name=null)
    {
        name??=MeshName(plate);if(name==null)return null;
        if(!fronts.TryGetValue(name,out var known))
        {
            var value=LayerFaces.ReadFront(name,out var broken);known=(value,broken);if(fronts.Count>256)fronts.Clear();fronts[name]=known;
        }
        if(known.Broken&&FrontWarningOnce.First(plate.Pointer.ToString()))Plugin.ModLog.LogWarning($"[Layer faces] Plate 0x{plate.Pointer.ToInt64():x}: saved front is unreadable; using legacy layered-mesh recovery.");
        return known.Value;
    }
    /// <summary>The composer and inspector lines of a plate in layers ("Layers: N faces assigned, M not assigned." and the estimate); null for every other plate. Cached on the snapshot.</summary>
    internal static string? Line(PlateStructure plate,PlateMeshSnapshot snapshot)
    {
        var name=MeshName(plate);
        if(CompositePlate.LayoutFromName(name) is not {} kind)return null;
        var material=plate.armourTechID??"";
        if(last is {} kept&&kept.Signature==snapshot.Signature&&kept.Material==material&&kept.Name==name)return kept.Text;
        string text;
        var snap=RuntimeArmourResponses.Snapshot;
        if(snap.Cassettes?.TryGetValue(material,out var model)!=true||model==null)text="Layers: the plate's cassette is not loaded; its layers cannot be assigned.";
        else
        {
            var recipe=Recipe(model,kind);
            // The saved JSON carries the rib of every corner (the line of sight of a face); never read while the mesh is being edited.
            string? json=null;try{if(!PlateMeshAccess.Gate.Busy)json=plate.Mesh.ToJson(true);}catch(Exception){}
            var ranks=LayerHitIndex.RanksOf(snapshot,json);
            text=LayerRanks.Line(ranks,recipe.Layout.Groups.Length);
            if(snap.Protection?.TryGetValue(material,out var table)==true&&table!=null)text+="\n"+EstimateLine(recipe,table);
        }
        last=(snapshot.Signature,material,name,text);
        return text;
    }
    /// <summary>The composer estimate of a plate in layers: the same calculation as the hit, on a flat plate at the recipe depth.</summary>
    internal static string EstimateLine(LayeredRecipe recipe,ProtectionFactorTable table)
    {
        string Row(ProtectionThreat threat)=>string.Join("/",new[]{0d,30,60}.Select(a=>LayerHit.Estimate(recipe,table,threat,a).ToString("0",System.Globalization.CultureInfo.InvariantCulture)));
        return $"As layers (the hit calculation, flat plate at {recipe.Layout.DepthMm.ToString("0",System.Globalization.CultureInfo.InvariantCulture)} mm, 0/30/60°): long rod {Row(ProtectionThreat.LongRod)} mm, jet {Row(ProtectionThreat.Jet)} mm RHA-eq. The table above is the recipe as one block.";
    }
}
