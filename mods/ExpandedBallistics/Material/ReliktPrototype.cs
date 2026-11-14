using System.Globalization;
using HarmonyLib;
using Sprocket.PlayerControl;
using Sprocket.Vehicles;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SprocketExpandedBallistics;

namespace SprocketExpandedBallistics.Materials;

// Measurement/visual candidate only. No ERA consumption or impact mutation.
[HarmonyPatch]
internal static class ReliktPrototype
{
    internal const string CassetteId = "reliktCassette";
    internal const string MountId = "reliktMount";
    private sealed class MeshRecord
    {
        internal readonly Mesh Mesh;
        internal readonly bool Owned;
        internal MeshRecord(Mesh mesh,bool owned){Mesh=mesh;Owned=owned;}
    }
    private static readonly Dictionary<string,MeshRecord> Meshes = new(StringComparer.Ordinal);
    private static readonly HashSet<Mesh> AllGeneratedMeshes = new();
    private static readonly ModularMeshOwnership MeshOwnership = new();
    private static readonly Dictionary<IntPtr,ModularGeometryIdentity> ModularScaleSignatures = new();
    private static readonly Dictionary<IntPtr,ModularDesiredRender> AppliedRender = new();
    private static readonly Dictionary<IntPtr,ModularFastState> FastStates = new();
    private static readonly Dictionary<IntPtr,int> OwnedTransforms = new();
    private static readonly Dictionary<IntPtr,IntPtr> OwnerByModel = new();
    // Registration happens on model lifecycle boundaries, not on arbitrary tank
    // movement. Ordinary suspension/turret transforms never enumerate components.
    private static void RegisterOwner(VehicleObjectModel model)
    {
        if(model.VehicleTransform is not {} transform)return;
        if(OwnerByModel.TryGetValue(model.Pointer,out var old)&&old==transform.Pointer)return;
        UnregisterOwner(model.Pointer);
        OwnerByModel[model.Pointer]=transform.Pointer;
        OwnedTransforms[transform.Pointer]=OwnedTransforms.TryGetValue(transform.Pointer,out var users)?users+1:1;
    }
    private static void UnregisterOwner(IntPtr model)
    {
        if(!OwnerByModel.Remove(model,out var owner))return;
        if(OwnedTransforms.TryGetValue(owner,out var users)&&users>1)OwnedTransforms[owner]=users-1;
        else OwnedTransforms.Remove(owner);
    }
    private static ModularFastState? CurrentFastState(VehicleObjectModel model)
    {
        if(model.Model is not {} native)return null;
        var scale=model.GetModelScale();
        return new(scale.x,scale.y,scale.z,native.Pointer,
            native.InteractionMesh?.Pointer??IntPtr.Zero,native.CollisionMesh?.Pointer??IntPtr.Zero,
            native.LODCount,model.visible);
    }
    private static readonly Dictionary<IntPtr,float> LastValidArea = new();
    private static readonly Dictionary<IntPtr,float> baseAssembly = new();
    private static readonly ModularRefreshQueue RefreshQueue = new(256);
    private static readonly Dictionary<IntPtr,VehicleTransform> RefreshTransforms = new();
    private static readonly Dictionary<IntPtr,DeferredModel> DeferredModels = new();
    private static readonly Dictionary<IntPtr,VehicleObjectModel> PendingColliderModels = new();
    private static readonly Dictionary<IntPtr,string> PendingColliderKeys = new();
    private sealed class DeferredModel
    {
        internal readonly VehicleObjectModel Model;
        internal int Attempts;
        internal int Ticks;
        internal int SettleTicks;
        internal bool ImpactDeferred;
        internal bool NeedsBalance;
        internal DeferredModel(VehicleObjectModel model){Model=model;}
    }
    [ThreadStatic] private static bool drainingRefresh;
    [ThreadStatic] private static int colliderRequestDepth;
    private static int identityLogs;
    private static int geometryLogs;
    private static int retireLogs;
    private static int lodLogs;
    private static int colliderLogs;
    private static int refreshLogs;
    private static int balanceLogs;
    private static int mainThreadId;
    private const int DeferredMaxAttempts=180;
    private const int DeferredOwnerCap=64;
    private const int DeferredSettleTicks=24;
    private static readonly HashSet<IntPtr> InstalledGeometry = new();
    [ThreadStatic] private static int loadDepth;
    private readonly struct LoadScope : IDisposable
    {
        internal LoadScope(bool enter){if(enter)loadDepth++;}
        public void Dispose(){loadDepth--;}
    }
    private static bool LiveImpact=>RuntimeSpall.Impact?.LiveImpact==true;
    private static bool CanBuildGeometry=>ModularGeometryGate.CanWrite(RuntimeDamageFeed.IsEditor,LiveImpact,loadDepth>0);
    private static bool Own(VehicleObjectModel model) => model != null &&
        EraPresetRegistry.Visual(model.ComponentID,out _,out _);

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void Building(VehicleObjectModel __instance)
    { if (!LiveImpact&&Own(__instance)) __instance.staticRenderer = false; }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static void Built(VehicleObjectModel __instance)
    {
        if (!Own(__instance)) return;
        if(LiveImpact){ScheduleDeferred(__instance,balance:true);return;}
        using var load=new LoadScope(true);
        RegisterOwner(__instance);
        FastStates.Remove(__instance.Pointer);
        InitializeBalance(__instance);
        if(__instance.VehicleTransform is {} builtTransform)EnqueueTransform(builtTransform,"built");
    }

    private static void InitializeBalance(VehicleObjectModel __instance)
    {
        if(!CanBuildGeometry)return;
        try
        {
            // Native definition.mass is the sole mass owner. SetCost replaces its
            // resource entry; it does not add a second visual/mechanism mass.
            EraPresetRegistry.Visual(__instance.ComponentID,out var preset,out bool cassette);
            float baseMass=BaseMass(__instance,preset,cassette);
            var requestedArea=ResizableAreaScale(__instance,preset);
            var lastArea=LastValidArea.TryGetValue(__instance.Pointer,out var previousArea)?previousArea:1f;
            var areaScale=ModularMassPolicy.SafeArea(requestedArea,lastArea);
            if(float.IsFinite(requestedArea)&&requestedArea>0)LastValidArea[__instance.Pointer]=requestedArea;
            else if(balanceLogs++<12)
                MaterialFeature.ModLog.LogWarning($"[Relikt balance] transient invalid area for {__instance.ComponentID} vuid={__instance.VUID.Value} area={requestedArea:0.####}; retaining finite paid area {areaScale:0.####}, no NaN mass/cost.");
            float paidMass=ModularMassPolicy.SafeMass(baseMass,areaScale);
            float nativeArea=NativeAreaScale(__instance,preset);
            float nativeMass=ModularMassPolicy.NativeMass(baseMass,areaScale,nativeArea);
            if(!(paidMass>0)||!(nativeMass>0))
            {
                if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] non-finite base mass for {__instance.ComponentID}; native definition retained.");
                return;
            }
            __instance.mass=nativeMass;
            __instance.associatedComponent?.SetMass(nativeMass,MassType.Armour);
            if(balanceLogs++<24)
                MaterialFeature.ModLog.LogWarning($"[Relikt mass] build {__instance.ComponentID} paidArea={areaScale:0.####} nativeArea={nativeArea:0.####} paidKg={paidMass:0.####} nativeKg={nativeMass:0.####} modelKg={__instance.mass:0.####} componentKg={(__instance.associatedComponent?.GetMass(MassType.Armour) ?? 0f):0.####}");
            float rha=cassette ? .55f : 1f, density=cassette ? 4000f : 7850f;
            float spall=cassette ? .25f : __instance.damageColliderParameters.SpallFactor;
            float price=cassette ? 10.5f : 2f;
            if(cassette || preset.PassiveMaterialId!=null)
            {
                string? materialId=preset.PassiveMaterialId;
                if(cassette&&__instance.ComponentID!=CompositeModule.ComponentId)
                {
                    if(!EraPresetRegistry.Cassette(__instance.ComponentID,out var binding))
                        throw new InvalidOperationException("Exact cassette binding is unavailable; passive native definition retained.");
                    materialId=binding.MaterialId;
                }
                var material=MaterialDatabase.Materials.FirstOrDefault(m=>m.Id==materialId);
                if(material==null)throw new InvalidOperationException("Native Relikt material is missing; protection adapter must remain unavailable.");
                rha=material.RhaFactor;density=material.Density;spall=material.SpallFactor;price=material.Balance.EffectiveCostMultiplier;
            }
            // Constructor order differs from field presentation: RHA, spall, density, priority.
            if(__instance.ComponentID==CompositeModule.ComponentId)
            {
                var effective=CompositeModuleEraGeometry.Effective(CompositeModule.Current);
                rha=(float)effective.Factor(false);density=(float)(effective.ArealMass/(CompositeModule.DepthMm*.001));
                spall=(float)CompositeModule.Current.SpallFactor;price=(float)CompositeModule.Current.CostMultiplier;
            }
            __instance.damageColliderParameters=new Sprocket.Vehicles.Colliders.DamageModelParameters(rha,spall,density,__instance.damageColliderParameters.Priority);
            var balance=MaterialBalance.Calculate(rha,density,price);
            if(!balance.IsValid)
            {
                if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] invalid material balance for {__instance.ComponentID}: {balance.ValidationError}");
                return;
            }
            if(!MaterialBalance.TryMaterialCost(paidMass,balance.EffectiveCostMultiplier,Sprocket.Constants.CostPerKgOfArmour,out float cost))
            {
                if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] finite mass still exceeded cost range for {__instance.ComponentID} mass={paidMass:0.####} multiplier={balance.EffectiveCostMultiplier:0.####}.");
                return;
            }
            if(!baseAssembly.TryGetValue(__instance.Pointer,out var baseAssemblyCost))
            {
                baseAssemblyCost=__instance.GetCost(MassType.Armour,CostType.Assembly);
                if(!float.IsFinite(baseAssemblyCost)||baseAssemblyCost<0)
                {
                    if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] native assembly cost was not finite for {__instance.ComponentID} ({baseAssemblyCost}); using finite material floor.");
                    baseAssemblyCost=0;
                }
                baseAssembly[__instance.Pointer]=baseAssemblyCost;
            }
            if(!ModularMassPolicy.TryAssembly(baseAssemblyCost,cost,areaScale,out float assembly))
            {
                if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] assembly cost is outside the supported finite range for {__instance.ComponentID}; SetCost skipped.");
                return;
            }
            __instance.SetCost(cost,MassType.Armour,CostType.Material);
            __instance.SetCost(assembly,MassType.Armour,CostType.Assembly);
        }
        catch(Exception ex){MaterialFeature.ModLog.LogWarning("Relikt native balance: "+ex.Message);}
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.OnModelLoaded))]
    private static void Loaded(VehicleObjectModel __instance)
    {
        if (!Own(__instance)) return;
        if(LiveImpact){ScheduleDeferred(__instance);return;}
        using var load=new LoadScope(true);
        RegisterOwner(__instance);
        FastStates.Remove(__instance.Pointer);
        try
        {
            var transform=__instance.VehicleTransform;
            if(transform!=null)
            {
                // Non-resizable ERA keeps its direct model-loaded reconstruction.
                // Resizable parts are reconstructed from the native saved scale by
                // the exact transform/mirror queue and its bounded settle window.
                if(!EraPresetRegistry.Visual(__instance.ComponentID,out var loadedPreset,out _)||!loadedPreset.Resizable)
                    Apply(__instance);
                EnqueueTransform(transform,"model-loaded");
            }
            else Apply(__instance);
            DrainRefresh();
            ScheduleSettle(transform);
        }
        catch (Exception ex) { MaterialFeature.ModLog.LogWarning("Relikt prototype model: " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.BuildDamageModelColliders))]
    private static void BuildingDamage(VehicleObjectModel __instance)
    {
        if (!Own(__instance)) return;
        if(LiveImpact){ScheduleDeferred(__instance);return;}
        using var load=new LoadScope(true);
        RegisterOwner(__instance);
        FastStates.Remove(__instance.Pointer);
        try
        {
            if(__instance.VehicleTransform is {} damageTransform)EnqueueTransform(damageTransform,"damage-build");
            Apply(__instance);
            DrainRefresh();
            // 0.22.7: only with diagnostics (the geometry is read from disk), and never for procedural modular panels (WholeCells), which have no -cell-collision.obj.
            if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled&&EraPresetRegistry.Visual(__instance.ComponentID,out var geometryPreset,out bool cassette)&&cassette&&!geometryPreset.WholeCells&&geometryLogs++<24)
            {
                var actual=__instance.Model?.CollisionMesh;
                var expected=Geometry(__instance.ComponentID,true);
                MaterialFeature.ModLog.LogInfo($"[Relikt geometry] vuid={__instance.VUID.Value} collisionMatch={actual==expected} vertices={actual?.vertexCount} bounds={actual?.bounds.size} expectedBounds={expected.bounds.size} modelScale={__instance.GetModelScale()} modelPosition={__instance.ModelPosition} modelRotation={__instance.ModelRotation}; native geometry only, no declared thickness override");
            }
            if (identityLogs++ < 24)
                if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo($"[Relikt identity] component={__instance.ComponentID} vuid={__instance.VUID.Value} generated={__instance.generateDamageColliders}; native own VUID is the damage identity");
        }
        catch (Exception ex) { MaterialFeature.ModLog.LogWarning("Relikt prototype damage identity: " + ex.Message); }
    }

    // Native construction always uses intact geometry and never reads the expenditure ledger.
    // 0.22.52 applies spent cuts only to private individual render copies in EraSpentRender.
    private static Mesh Geometry(string id, bool collision=false, string? visualOverride=null)
    {
        if(!EraPresetRegistry.Visual(id,out var preset,out bool cassette))throw new InvalidDataException("Unknown placed ERA visual component.");
        string materialKey=EraPresetRegistry.Cassette(id,out var binding) ? binding.MaterialId+":"+binding.ResponseId : "passive";
        string cacheKey=id+":"+materialKey+(collision ? ":collision" : ":visual");
        if(!collision&&visualOverride!=null)cacheKey+=":"+visualOverride;
        if (Meshes.TryGetValue(cacheKey, out var cached)) return cached.Mesh;
        string asset=collision ? (cassette?preset.Collision:preset.MountCollision??preset.Visual)
            : !cassette&&preset.MountVisual!=null?preset.MountVisual:preset.Visual;
        if(!collision&&visualOverride!=null)asset=visualOverride;
        string path = Path.Combine(Path.GetDirectoryName(typeof(MaterialFeature).Assembly.Location)!, "era-assets",asset);
        int paintedVertices=0;
        var module=id==CompositeModule.ComponentId&&!collision;
        var objLines=File.ReadAllLines(path);
        var expanded=EraObjGeometry.Read(objLines,module?"housing":id==CompositeModule.ComponentId?"mount":cassette ? "cassette" : "mount",
            (x,y,z)=>new Vector3(x,y,z),v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z));
        int[]? renderCells=null;
        if(module)
        {
            int housingTriangles=expanded.Count/3;
            List<Vector3> Group(string name)=>EraObjGeometry.Read(objLines,name,(x,y,z)=>new Vector3(x,y,z),v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z));
            var cover=Group("cover");
            expanded.AddRange(cover);paintedVertices=expanded.Count;expanded.AddRange(Group("dark"));
            renderCells=Enumerable.Repeat(-1,expanded.Count/3).ToArray();
            for(int t=housingTriangles;t<paintedVertices/3;t++)
            {var a=expanded[3*t];var b=expanded[3*t+1];var c=expanded[3*t+2];
                if(CompositeModuleEraGeometry.TryCell((a.x+b.x+c.x)/3,(a.z+b.z+c.z)/3,out int x,out int y))renderCells[t]=x+y*2;}
        }
        var mesh=new Mesh { name="Original Relikt prototype "+id,hideFlags=HideFlags.HideAndDontSave };
        mesh.vertices=expanded.ToArray();mesh.triangles=Enumerable.Range(0,expanded.Count).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
        if(module){mesh.subMeshCount=2;mesh.SetTriangles(Enumerable.Range(0,paintedVertices).ToArray(),0);mesh.SetTriangles(Enumerable.Range(paintedVertices,expanded.Count-paintedVertices).ToArray(),1);}
        // VehicleLit expects a valid tangent frame, including with flat normal maps.
        // Expanded triangles already have hard face normals; use a matching planar
        // UV frame without changing geometry, collision indices or native placement.
        var normals=mesh.normals;
        var uv=new Vector2[expanded.Count];var tangents=new Vector4[expanded.Count];
        for(int i=0;i<expanded.Count;i+=3)
        {
            var normal=normals[i];
            var axis=Math.Abs(normal.y)<.9f ? Vector3.up : Vector3.forward;
            var tangent=Vector3.Cross(axis,normal).normalized;
            var bitangent=Vector3.Cross(normal,tangent);
            for(int j=i;j<i+3;j++)
            {
                uv[j]=new Vector2(Vector3.Dot(expanded[j],tangent),Vector3.Dot(expanded[j],bitangent));
                tangents[j]=new Vector4(tangent.x,tangent.y,tangent.z,1f);
            }
        }
        mesh.uv=uv;mesh.tangents=tangents;
        if(cassette&&!collision)EraSpentRender.Register(mesh,renderCells);
        // 0.22.70: the intact housing render mesh (state 0) is registered as the only source EraSpentRender may rebind.
        else if(!collision&&visualOverride!=null)EraSpentRender.RegisterHousing(mesh,id);
        AllGeneratedMeshes.Add(mesh);
        Meshes.Add(cacheKey,new MeshRecord(mesh,false));
        TrimMeshCache();
        return mesh;
    }

    [ThreadStatic] private static HashSet<IntPtr>? applyingModels;

    // Re-entrant native callbacks are queued for the outer refresh drain instead
    // of being dropped. This keeps the exact model that changed, not merely the
    // transform that started the first call, and prevents A -> B -> A recursion.
    private static bool Apply(VehicleObjectModel model)
    {
        if(!CanBuildGeometry)return false;
        if(model==null)return false;
        applyingModels??=new HashSet<IntPtr>();
        var pointer=model.Pointer;
        if(!applyingModels.Add(pointer))
        {
            if(model.VehicleTransform is {} reentrant)EnqueueTransform(reentrant,"reentrant-apply");
            return false;
        }
        try
        {
            EraSpentRender.BeforeGeometry(model);
            var ready=ApplyCore(model);
            if(ready){InstalledGeometry.Add(pointer);EraSpentRender.Track(model);}
            return ready;
        }
        finally
        {
            applyingModels.Remove(pointer);
            // If a native callback queued another exact owner while this call was
            // outside a refresh drain, consume it before returning to the game.
            if(!drainingRefresh)DrainRefresh();
        }
    }

    private static bool ApplyCore(VehicleObjectModel model)
    {
        if(model.Model==null){ScheduleDeferred(model);return false;}
        if(EraPresetRegistry.Visual(model.ComponentID,out var modularPreset,out var modularCassette)&&modularPreset.WholeCells)
            return ApplyModular(model,modularPreset,modularCassette);
        return ApplyLegacy(model);
    }

    private static bool ApplyLegacy(VehicleObjectModel model)
    {
        var native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        // 0.22.51: always the intact generation; the ledger is never read here (see Geometry).
        var mesh=Geometry(model.ComponentID,visualOverride:IntactHousingVisual(model));model.MarkDynamic();
        // Single-mesh SetMesh creates LOD0 only. Native distance selection hides
        // that renderer at LOD1+, without falling back to its last mesh.
        // SetLOD indexes the native LOD name array during renderer registration.
        int lodCount=RenderLodCount();
        native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        if(native.InteractionMesh!=mesh||native.LODCount!=lodCount)
        {
            native.SetMesh(new Il2CppReferenceArray<Mesh>(Enumerable.Repeat(mesh,lodCount).ToArray()));
            native.SetShadowProxy(mesh);native.SetInteractionMesh(mesh);
            if(lodLogs++<16)if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo($"[ERA render LOD] component={model.ComponentID} vuid={model.VUID.Value} nativeNames={lodCount} modelLODs={native.LODCount} localBounds={native.LocalBounds}; low-poly mesh at each native level; intact generation, never rebuilt by an activation");
        }
        EraPresetRegistry.Visual(model.ComponentID,out _,out bool cassette);
        var collision=cassette||EraPresetRegistry.Visual(model.ComponentID,out var preset,out _)&&preset.MountCollision!=null ? Geometry(model.ComponentID,true) : mesh;
        native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        if(native.CollisionMesh!=collision)native.SetCollisionMesh(collision);
        // Native per-vehicle exterior paint; renderer updates retain the linked wrapper.
        EraSurface.Apply(model,cassette);
        return true;
    }

    // Whole-cell modular path: build real geometry for the current integer
    // count. Local vertices are inverse-scaled so native X/Z scale changes only
    // the requested count, never the world size of an individual cell.
    // 0.22.51: every generation is the intact one (no spent cut, never hidden); see Geometry.
    private static bool ApplyModular(VehicleObjectModel model,EraVisualPreset preset,bool cassette)
    {
        if(!PlacedEraCells.TryLayout(model,preset,out var layout))
        {
            if(geometryLogs++<24)
            {
                var rejected=model.GetModelScale();
                MaterialFeature.ModLog.LogWarning($"Modular ERA layout rejected for {model.ComponentID} vuid={model.VUID.Value} scale=({rejected.x:0.####},{rejected.y:0.####},{rejected.z:0.####}); previous valid geometry retained, refresh remains pending.");
            }
            ScheduleDeferred(model);
            return false;
        }
        var scale=model.GetModelScale();
        if(!float.IsFinite(scale.x)||!float.IsFinite(scale.y)||!float.IsFinite(scale.z)||scale.x==0||scale.z==0)
        {
            ScheduleDeferred(model);
            return false;
        }
        var identity=ModularGeometryIdentity.Create(layout.CountU,layout.CountV,scale.x,scale.z);
        if(!identity.Valid){ScheduleDeferred(model);return false;}
        var outer=preset.StageIndex==2;
        var collisionKind=cassette?(outer?ModularPartKind.Stage2Collision:ModularPartKind.Stage1Collision):ModularPartKind.Housing;
        var collisionKey=ModularMeshKey(model.ComponentID,collisionKind,layout,scale);
        var visualKind=cassette?(outer?ModularPartKind.Stage2Visual:ModularPartKind.Stage1Visual):ModularPartKind.HousingVisual;
        var visualKey=ModularMeshKey(model.ComponentID,visualKind,layout,scale);
        var collisionMesh=ModularMesh(model.ComponentID,preset,collisionKind,layout,scale,out _);
        var visualMesh=ModularMesh(model.ComponentID,preset,visualKind,layout,scale,out _);
        var farMesh=ModularMesh(model.ComponentID,preset,visualKind,layout,scale,out var farKey,coarse:true);

        var native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        var desired=new ModularDesiredRender(native.Pointer,identity,visualKey,collisionKey,RenderLodCount(),false);
        // Damage-build/visibility callbacks may re-enter an already installed
        // generation. Do not MarkDynamic or rewrite native renderer state again.
        var installed=AppliedRender.TryGetValue(model.Pointer,out var installedState)?installedState:(ModularDesiredRender?)null;
        if(!ModularRenderPolicy.NeedsRefresh(installed,desired,Observe(model,desired,visualMesh,collisionMesh)))return true;

        // Install collision on the pre-swap wrapper as well: if MarkDynamic
        // triggers a native damage-collider build, that build must already see
        // the new collision geometry. Then re-read the wrapper after
        // MarkDynamic; the game may replace it or reset it to the default block.
        var collisionChangedOrMissing=native.CollisionMesh!=collisionMesh;
        if(collisionChangedOrMissing)native.SetCollisionMesh(collisionMesh);
        model.MarkDynamic();
        native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        // Collision is installed first on the final wrapper: if a native callback
        // triggers a damage-collider build from the visual swap, the new geometry
        // is already visible to that build.
        if(native.CollisionMesh!=collisionMesh)
        {
            native.SetCollisionMesh(collisionMesh);
            collisionChangedOrMissing=true;
        }
        native=model.Model;if(native==null){ScheduleDeferred(model);return false;}
        desired=desired with { Wrapper = native.Pointer };
        var lodCount=desired.LodCount;
        if(native.InteractionMesh!=visualMesh||native.LODCount!=lodCount)
        {
            native.SetMesh(new Il2CppReferenceArray<Mesh>(Enumerable.Range(0,lodCount).Select(i=>i==0?visualMesh!:farMesh!).ToArray()));
            native.SetShadowProxy(farMesh!);native.SetInteractionMesh(visualMesh!);
            if(lodLogs++<16&&SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)
                MaterialFeature.ModLog.LogInfo($"[Modular ERA LOD] component={model.ComponentID} cells={layout.CountU}x{layout.CountV} nativeNames={lodCount} modelLODs={native.LODCount} localBounds={native.LocalBounds}; mounting recesses on LOD0; plain covers on farther LODs and shadows");
        }
        else
        {
            native.SetShadowProxy(farMesh!);native.SetInteractionMesh(visualMesh!);
        }
        // 0.22.51: no SetVisible here. Generations are never hidden, so the native visibility flag is left alone.
        // Commit only after both wrappers point at the new generation. Old
        // visual-key generations can be released immediately; old collision
        // generations stay referenced until the native damage-collider build
        // postfix confirms the swap.
        CommitOwner(model.Pointer,new[]{visualKey,collisionKey,farKey});
        ReleaseRetiredVisuals(model.Pointer);
        if(!ObserveMatches(model,desired,visualMesh,collisionMesh))
        {
            ScheduleDeferred(model);
            return false;
        }
        RecordApplied(model,desired,collisionChangedOrMissing);
        EraSurface.Apply(model,cassette);
        return true;
    }

    private static int RenderLodCount()
    {
        var lodCount=VehicleRendererGroup.LODNames?.Length??0;
        if(lodCount<1||lodCount>8)throw new InvalidDataException("Unsupported native ERA render LOD count.");
        return lodCount;
    }

    private static ModularObservedRender Observe(VehicleObjectModel model,ModularDesiredRender desired,
        Mesh? visual,Mesh? collision)
    {
        var native=model.Model;
        if(native==null)return new(IntPtr.Zero,false,false,false,0,false);
        var visualPresent=desired.Hidden||visual!=null&&native.InteractionMesh==visual;
        var collisionPresent=collision!=null&&native.CollisionMesh==collision;
        return new(native.Pointer,true,visualPresent,collisionPresent,native.LODCount,model.visible==false);
    }

    // Production wrapper: verify the output that was just installed, not the
    // dirty applied cache. A null applied generation must not make this fail.
    private static bool ObserveMatches(VehicleObjectModel model,ModularDesiredRender desired,
        Mesh? visual,Mesh? collision)
        =>ModularRenderPolicy.ObservedMatches(desired,Observe(model,desired,visual,collision));

    private static void RecordApplied(VehicleObjectModel model,ModularDesiredRender desired,
        bool collisionChangedOrMissing)
    {
        var previous=ModularScaleSignatures.TryGetValue(model.Pointer,out var old)?old:(ModularGeometryIdentity?)null;
        var wrapperChanged=AppliedRender.TryGetValue(model.Pointer,out var previousRender)
            &&previousRender.Wrapper!=desired.Wrapper;
        AppliedRender[model.Pointer]=desired;
        // Collider success is confirmed later by DamageCollidersBuilt. Request a
        // rebuild when the wrapper changed, when this apply had to create/replace
        // the collision mesh, or when the generated geometry identity changed.
        // A same-generation reapply is a no-op, avoiding MarkDynamic/rebuild loops.
        if(ModularRenderPolicy.NeedsNativeColliderRebuild(wrapperChanged,collisionChangedOrMissing,previous,desired.Geometry)
            &&model.associatedComponent!=null)
            QueueColliderRebuild(model,desired.Geometry.ToString()+":"+desired.CollisionKey);
        else
            ModularScaleSignatures[model.Pointer]=desired.Geometry;
    }

    private static bool EnsureWholeCell(VehicleObjectModel model,EraVisualPreset preset,bool cassette)
    {
        if(!PlacedEraCells.TryLayout(model,preset,out var layout)){ScheduleDeferred(model);return false;}
        var scale=model.GetModelScale();
        if(!float.IsFinite(scale.x)||!float.IsFinite(scale.y)||!float.IsFinite(scale.z)||scale.x==0||scale.z==0)
        {ScheduleDeferred(model);return false;}
        var identity=ModularGeometryIdentity.Create(layout.CountU,layout.CountV,scale.x,scale.z);
        if(!identity.Valid){ScheduleDeferred(model);return false;}
        var outer=preset.StageIndex==2;
        var collisionKind=cassette?(outer?ModularPartKind.Stage2Collision:ModularPartKind.Stage1Collision):ModularPartKind.Housing;
        var collisionKey=ModularMeshKey(model.ComponentID,collisionKind,layout,scale);
        var visualKind=cassette?(outer?ModularPartKind.Stage2Visual:ModularPartKind.Stage1Visual):ModularPartKind.HousingVisual;
        var visualKey=ModularMeshKey(model.ComponentID,visualKind,layout,scale);
        var nativeForIdentity=model.Model;
        if(nativeForIdentity==null){ScheduleDeferred(model);return false;}
        var desired=new ModularDesiredRender(nativeForIdentity.Pointer,identity,visualKey,collisionKey,RenderLodCount(),false);
        Mesh? visual=null,collision=null;
        if(Meshes.TryGetValue(visualKey,out var visualRecord))visual=visualRecord.Mesh;
        if(Meshes.TryGetValue(collisionKey,out var collisionRecord))collision=collisionRecord.Mesh;
        var observed=Observe(model,desired,visual,collision);
        var applied=AppliedRender.TryGetValue(model.Pointer,out var previousRender)?previousRender:(ModularDesiredRender?)null;
        if(!ModularRenderPolicy.NeedsRefresh(applied,desired,observed))
        {
            // Render is current, but keep collider invalidation retryable until
            // the native build boundary confirmed this exact generation.
            if(!ModularScaleSignatures.TryGetValue(model.Pointer,out var confirmed)||confirmed!=identity)
            {
                if(model.associatedComponent==null)ModularScaleSignatures[model.Pointer]=identity;
                else QueueColliderRebuild(model,identity.ToString()+":"+collisionKey);
            }
            return true;
        }
        // Route through Apply so a native wrapper/collider callback cannot
        // re-enter this exact model; the reentrant target is queued.
        return Apply(model);
    }

    private static bool EnsureLegacy(VehicleObjectModel model,EraVisualPreset preset)
    {
        var scale=model.GetModelScale();
        if(!PlacedEraCells.ValidScale(scale)||!PlacedEraCells.TryLayout(model,preset,out var layout))
        {ScheduleDeferred(model);return false;}
        var identity=ModularGeometryIdentity.Create(layout.CountU,layout.CountV,scale.x,scale.z);
        if(!identity.Valid){ScheduleDeferred(model);return false;}
        var previous=ModularScaleSignatures.TryGetValue(model.Pointer,out var old)?old:(ModularGeometryIdentity?)null;
        if(!Apply(model))return false;
        ModularScaleSignatures[model.Pointer]=identity;
        if(ModularColliderPolicy.NeedsNativeColliderRebuild(previous,identity))
            QueueColliderRebuild(model,identity.ToString());
        return true;
    }

    private static bool RefreshModel(VehicleObjectModel model)
    {
        if(!CanBuildGeometry)return false;
        if(!Own(model))return false;
        RegisterOwner(model);
        // Test the actual wrapper and installed meshes, not scale alone: native
        // clone/load can replace either while retaining identical counts.
        var current=CurrentFastState(model);
        if(current is {} state&&FastStates.TryGetValue(model.Pointer,out var lastFast)
            &&lastFast==state&&state.Valid)return true;
        if(model.Model==null){ScheduleDeferred(model);return false;}
        if(!EraPresetRegistry.Visual(model.ComponentID,out var preset,out bool cassette))return false;
        if(preset.Resizable)
        {
            var requested=ResizableAreaScale(model,preset);
            var last=LastValidArea.TryGetValue(model.Pointer,out var remembered)?remembered:1f;
            var area=ModularMassPolicy.SafeArea(requested,last);
            if(float.IsFinite(requested)&&requested>0)LastValidArea[model.Pointer]=requested;
            var baseMass=BaseMass(model,preset,cassette);
            var paidMass=ModularMassPolicy.SafeMass(baseMass,area);
            var nativeArea=NativeAreaScale(model,preset);
            var nativeMass=ModularMassPolicy.NativeMass(baseMass,area,nativeArea);
            if(nativeMass>0){model.mass=nativeMass;model.associatedComponent?.SetMass(nativeMass,MassType.Armour);}
            else if(balanceLogs++<12)MaterialFeature.ModLog.LogWarning($"[Relikt balance] scale refresh retained native mass for {model.ComponentID}; finite paid area unavailable.");
            if(balanceLogs++<24)
                MaterialFeature.ModLog.LogWarning($"[Relikt mass] refresh {model.ComponentID} paidArea={area:0.####} nativeArea={nativeArea:0.####} paidKg={paidMass:0.####} nativeKg={nativeMass:0.####} componentKg={(model.associatedComponent?.GetMass(MassType.Armour) ?? 0f):0.####}");
            if(model.ComponentID==CompositeModule.ComponentId)
            {
                var readyModule=Apply(model);
                if(readyModule&&CurrentFastState(model) is {} moduleFast)FastStates[model.Pointer]=moduleFast;
                return readyModule;
            }
            var ready=preset.WholeCells?EnsureWholeCell(model,preset,cassette):EnsureLegacy(model,preset);
            if(ready&&CurrentFastState(model) is {} applied)FastStates[model.Pointer]=applied;
            return ready;
        }
        // Non-resizable owned ERA still needs model-loaded/build reconstruction;
        // the queue is event-driven, so this is not a per-frame scan.
        return Apply(model);
    }

    private static void EnqueueTransform(VehicleTransform? transform,string reason)
    {
        if(!CanBuildGeometry||transform==null||!OwnedTransforms.ContainsKey(transform.Pointer))return;
        AddTransform(transform);
        try{if(transform.Mirror is {} mirror&&mirror.Pointer!=transform.Pointer)AddTransform(mirror);}
        catch(Exception ex){if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning($"Mirror ERA transform unavailable ({reason}): {ex.Message}");}
    }

    private static void AddTransform(VehicleTransform transform)
    {
        if(!CanBuildGeometry||transform==null||!OwnedTransforms.ContainsKey(transform.Pointer))return;
        if(RefreshQueue.Enqueue(transform.Pointer))RefreshTransforms[transform.Pointer]=transform;
        else if(refreshLogs++<12&&RefreshQueue.Count>=256)
            MaterialFeature.ModLog.LogWarning("Modular ERA refresh queue cap reached; exact-owner event dropped rather than scanning the vehicle.");
    }

    private static void ProcessTransform(VehicleTransform transform)
    {
        if(transform==null)return;
        try
        {
            if(transform.Mirror is {} mirror&&mirror.Pointer!=transform.Pointer)AddTransform(mirror);
            var owner=transform.VehicleObject;
            var components=owner?.Components;
            if(components==null)return;
            var count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(var i=0;i<count;i++)
            {
                if(components[i].TryCast<VehicleObjectModel>() is not {} model||!Own(model))continue;
                if(!EraPresetRegistry.Visual(model.ComponentID,out var preset,out _)||!preset.Resizable)continue;
                try{RefreshModel(model);}
                catch(Exception ex){if(identityLogs++<16)MaterialFeature.ModLog.LogWarning($"Resizable ERA refresh for {model.ComponentID}: {ex.Message}");ScheduleDeferred(model);}
            }
        }
        catch(Exception ex){if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning("Resizable ERA transform refresh: "+ex.Message);}
    }

    private static void DrainRefresh()
    {
        if(!CanBuildGeometry||drainingRefresh)return;
        drainingRefresh=true;
        try
        {
            RefreshQueue.BeginDrain();
            var iterations=0;
            while(iterations++<512&&RefreshQueue.TryDequeue(out var pointer))
            {
                if(!RefreshTransforms.Remove(pointer,out var transform)||transform==null)continue;
                ProcessTransform(transform);
            }
        }
        finally
        {
            RefreshQueue.EndDrain();
            drainingRefresh=false;
        }
    }

    private static void ScheduleDeferred(VehicleObjectModel model,bool settle=false,bool balance=false)
    {
        if(model==null||!Own(model))return;
        var pointer=model.Pointer;
        // A damage callback on an installed vehicle must leave no work to drain
        // after the impact. A first load interrupted by impact must be retryable.
        if(LiveImpact&&!ModularGeometryGate.DeferImpact(InstalledGeometry.Contains(pointer)))return;
        if(DeferredModels.TryGetValue(pointer,out var existing))
        {
            if(LiveImpact)existing.ImpactDeferred=true;
            existing.NeedsBalance|=balance;
            if(settle)existing.SettleTicks=Math.Max(existing.SettleTicks,DeferredSettleTicks);
            return;
        }
        if(DeferredModels.Count>=DeferredOwnerCap)
        {
            if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning("Modular ERA deferred model cap reached; exact owner remains unmarked and will retry on the next native event.");
            return;
        }
        DeferredModels[pointer]=new DeferredModel(model){SettleTicks=settle?DeferredSettleTicks:0,ImpactDeferred=LiveImpact,NeedsBalance=balance};
    }

    private static void ScheduleSettle(VehicleTransform? transform)
    {
        if(!CanBuildGeometry||transform==null)return;
        VehicleTransform? mirror=null;
        try{mirror=transform.Mirror;}catch { /* optional mirror */ }
        foreach(var target in new[]{transform,mirror})
        {
            if(target==null||!OwnedTransforms.ContainsKey(target.Pointer))continue;
            try
            {
                var owner=target.VehicleObject;
                var components=owner?.Components;
                if(components==null)continue;
                var count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
                for(var i=0;i<count;i++)
                    if(components[i].TryCast<VehicleObjectModel>() is {} model&&Own(model)
                        &&EraPresetRegistry.Visual(model.ComponentID,out var preset,out _)&&preset.Resizable)
                        ScheduleDeferred(model,settle:true);
            }
            catch { /* Optional deferred settling must not break the native callback. */ }
        }
    }

    internal static void TickDeferred()
    {
        if(LiveImpact)return;
        // Only load/build work can enter these queues in Play. Impact callbacks
        // can add only a missing first generation; they cannot dirty an owner.
        using var load=new LoadScope(true);
        if(RefreshQueue.Count>0)DrainRefresh();
        if(DeferredModels.Count>0)
        {
            foreach(var entry in DeferredModels.ToArray())
            {
                var model=entry.Value.Model;
                try
                {
                    if(entry.Value.NeedsBalance&&Own(model)&&model.Model!=null)
                    {InitializeBalance(model);entry.Value.NeedsBalance=false;}
                    if(ModularGeometryGate.DropDeferred(RuntimeDamageFeed.IsEditor,entry.Value.ImpactDeferred,InstalledGeometry.Contains(entry.Key)))
                    {DeferredModels.Remove(entry.Key);continue;}
                    entry.Value.Ticks++;
                    if(!Own(model)||model.Model==null)
                    {
                        if(++entry.Value.Attempts>=DeferredMaxAttempts)
                        {
                            DeferredModels.Remove(entry.Key);
                            if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning($"Modular ERA deferred model {entry.Key} released before becoming ready; not marked current.");
                        }
                        continue;
                    }
                    var current=RefreshModel(model);
                    // Load/duplicate settle for a small bounded window so a scale
                    // restored by a later native frame is not masked by an early
                    // 1x1 generation. The exact owner set is tiny, never a vehicle scan.
                    if(entry.Value.Ticks<entry.Value.SettleTicks)continue;
                    if(current)DeferredModels.Remove(entry.Key);
                    else if(++entry.Value.Attempts>=DeferredMaxAttempts)
                    {
                        DeferredModels.Remove(entry.Key);
                        if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning($"Modular ERA deferred model {entry.Key} remained not-ready after {DeferredMaxAttempts} bounded ticks; not marked current.");
                    }
                }
                catch(Exception ex)
                {
                    DeferredModels.Remove(entry.Key);
                    if(refreshLogs++<12)MaterialFeature.ModLog.LogWarning($"Modular ERA deferred model skipped after error: {ex.Message}");
                }
            }
        }
        DrainColliderRequests();
    }

    private static void QueueColliderRebuild(VehicleObjectModel model,string key)
    {
        if(!CanBuildGeometry||model==null||model.associatedComponent==null)return;
        PendingColliderModels[model.Pointer]=model;
        PendingColliderKeys[model.Pointer]=key;
    }

    private static void DrainColliderRequests()
    {
        if(!CanBuildGeometry||PendingColliderModels.Count==0)return;
        foreach(var entry in PendingColliderModels.ToArray())
        {
            PendingColliderModels.Remove(entry.Key);
            var key=PendingColliderKeys.TryGetValue(entry.Key,out var pending)?pending:"";
            PendingColliderKeys.Remove(entry.Key);
            if(!Own(entry.Value)||entry.Value.Model==null)continue;
            RequestNativeColliderRebuild(entry.Value,key);
        }
    }

    [HarmonyPostfix,HarmonyPatch(typeof(Player),nameof(Player.GameLateUpdate))]
    private static void DeferredTick(Player __instance)
    {
        var thread=Environment.CurrentManagedThreadId;
        if(mainThreadId==0)mainThreadId=thread;
        if(thread!=mainThreadId)return;
        TickDeferred();
        EraSpentRender.Tick();
    }

    // 0.22.51: the intact generation only (identical to the former key without a ":cut:" suffix).
    private static string ModularMeshKey(string component,ModularPartKind kind,EraCellLayout layout,Vector3 scale)
    {
        var cellLayout=new ModularCellLayout(layout.CountU,layout.CountV);
        return "modular:v"+ModularCellGeometry.AuthoringRevision+":"+component+":"+kind+":"+cellLayout.CountU+"x"+cellLayout.CountV+":"+
            scale.x.ToString("R",CultureInfo.InvariantCulture)+":"+scale.z.ToString("R",CultureInfo.InvariantCulture);
    }

    private static Mesh ModularMesh(string component,EraVisualPreset preset,ModularPartKind kind,
        EraCellLayout layout,Vector3 scale,out string cacheKey,bool coarse=false)
    {
        cacheKey=ModularMeshKey(component,kind,layout,scale);
        if(coarse&&(kind==ModularPartKind.Stage2Visual||kind==ModularPartKind.Stage1Visual&&ModularCellSpec.ForVariant(preset.ModularVariant).ProceduralPanel))cacheKey+=":far";
        if(Meshes.TryGetValue(cacheKey,out var cached))return cached.Mesh;
        var cellLayout=new ModularCellLayout(layout.CountU,layout.CountV);
        var spec=ModularCellSpec.ForVariant(preset.ModularVariant);
        var data=ModularCellGeometry.Build(kind,cellLayout,spec,null,
            Math.Abs(scale.x),Math.Abs(scale.z),mountingDetail:!coarse&&kind==ModularPartKind.Stage2Visual,coarse:coarse);
        var flat=kind is ModularPartKind.Stage1Visual or ModularPartKind.Stage2Visual or ModularPartKind.Housing or ModularPartKind.HousingVisual;
        var mesh=ToUnityMesh(data,"Duplet modular "+kind+" "+component,flat);
        if(kind is ModularPartKind.Stage1Visual or ModularPartKind.Stage2Visual)EraSpentRender.Register(mesh,data.TriangleCells);
        // Do not trim here: the visual and collision meshes of one generation
        // are created before CommitOwner takes their references. Trimming at
        // this point could destroy the uncommitted first mesh.
        AllGeneratedMeshes.Add(mesh);
        Meshes.Add(cacheKey,new MeshRecord(mesh,true));
        return mesh;
    }

    private static void CommitOwner(IntPtr owner,string[] keys)
    {
        // Collision generations remain referenced until DamageCollidersBuilt,
        // not merely until RequestRebuild. The higher bounded cap covers a drag
        // sequence without releasing a mesh a native collider may still use.
        MeshOwnership.Commit(owner,keys,256,key=>{
            // 0.22.7: its own counter, Info, and only with diagnostics (a long drag sequence is expected to reach the cap).
            if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled&&retireLogs++<4)MaterialFeature.ModLog.LogInfo("Modular mesh retirement threshold reached; retained until native rebuild confirmation: "+key);
        });
        TrimMeshCache();
    }

    private static void ReleaseRetiredVisuals(IntPtr owner)
    {
        MeshOwnership.ReleaseRetired(owner,IsVisualKey);
        TrimMeshCache();
    }

    // The physical Housing collision mesh must stay retired until the native
    // collider-build boundary. HousingVisual is render/shadow/interaction only
    // and can be released immediately with the other visual generations.
    private static bool IsVisualKey(string key)
        =>key.Contains(":Stage1Visual",StringComparison.Ordinal)||key.Contains(":Stage2Visual",StringComparison.Ordinal)
            ||key.Contains(":HousingVisual",StringComparison.Ordinal);

    private static void ReleaseRetired(IntPtr owner)
    {
        MeshOwnership.ReleaseRetired(owner);
        TrimMeshCache();
    }

    private static void ReleaseOwner(IntPtr owner)
    {
        MeshOwnership.ReleaseOwner(owner);
        TrimMeshCache();
    }

    private static void TrimMeshCache()
    {
        const int Limit=192;
        if(Meshes.Count<Limit)return;
        foreach(var entry in Meshes.ToArray())
        {
            if(Meshes.Count<Limit)break;
            if(MeshOwnership.Users(entry.Key)>0)continue;
            if(entry.Value.Owned)
            {
                EraSpentRender.ForgetMesh(entry.Value.Mesh);
                if(entry.Value.Mesh)UnityEngine.Object.Destroy(entry.Value.Mesh);
                AllGeneratedMeshes.Remove(entry.Value.Mesh);
            }
            // Legacy unowned entries are dropped from lookup but stay in
            // AllGeneratedMeshes so teardown can still destroy them exactly once.
            Meshes.Remove(entry.Key);
        }
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.BuildDamageModelColliders))]
    private static void DamageCollidersBuilt(VehicleObjectModel __instance)
    {
        if(!Own(__instance))return;
        if(LiveImpact){ScheduleDeferred(__instance);return;}
        using var load=new LoadScope(true);
        RegisterOwner(__instance);
        FastStates.Remove(__instance.Pointer);
        try
        {
            // The native build is the success boundary for collider ownership
            // and for the retained collision-generation signature.
            if(AppliedRender.TryGetValue(__instance.Pointer,out var applied))
                ModularScaleSignatures[__instance.Pointer]=applied.Geometry;
            ReleaseRetired(__instance.Pointer);
            if(__instance.VehicleTransform is {} builtTransform)EnqueueTransform(builtTransform,"collider-built");
        }
        catch(Exception ex){if(geometryLogs++<24)MaterialFeature.ModLog.LogWarning("Modular retired mesh release: "+ex.Message);}
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.Release))]
    private static void Releasing(VehicleObjectModel __instance)
    {
        if(__instance==null)return;
        try{ForgetModel(__instance.Pointer);ReleaseOwner(__instance.Pointer);}
        catch { /* Release must proceed even if an optional mesh is already gone. */ }
    }

    private static Mesh ToUnityMesh(ModularMeshData data,string name,bool flat)
    {
        var source=new UnityEngine.Vector3[data.Vertices.Length];
        for(var i=0;i<source.Length;i++)
        {
            var v=data.Vertices[i];
            source[i]=new UnityEngine.Vector3(v.X,v.Y,v.Z);
        }
        var mesh=new Mesh{name=name,hideFlags=HideFlags.HideAndDontSave};
        if(!flat)
        {
            mesh.vertices=source;
            mesh.triangles=data.Triangles;
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            return mesh;
        }
        var vertices=new List<Vector3>(data.Triangles.Length);
        var normals=new List<Vector3>(data.Triangles.Length);
        var uv=new List<Vector2>(data.Triangles.Length);
        var tangents=new List<Vector4>(data.Triangles.Length);
        for(var i=0;i<data.Triangles.Length;i+=3)
        {
            var a=source[data.Triangles[i]];var b=source[data.Triangles[i+1]];var c=source[data.Triangles[i+2]];
            var normal=Vector3.Cross(b-a,c-a);
            if(normal.sqrMagnitude<=1e-20f)throw new InvalidDataException("Degenerate modular triangle.");
            normal.Normalize();
            var axis=Math.Abs(normal.y)<.9f?Vector3.up:Vector3.forward;
            var tangent=Vector3.Cross(axis,normal).normalized;
            var bitangent=Vector3.Cross(normal,tangent);
            for(var j=0;j<3;j++)
            {
                var p=source[data.Triangles[i+j]];
                vertices.Add(p);normals.Add(normal);
                uv.Add(new Vector2(Vector3.Dot(p,tangent),Vector3.Dot(p,bitangent)));
                tangents.Add(new Vector4(tangent.x,tangent.y,tangent.z,1f));
            }
        }
        if(vertices.Count is <3 or >24000)throw new InvalidDataException("Modular visual vertex bound exceeded.");
        mesh.vertices=vertices.ToArray();
        mesh.triangles=Enumerable.Range(0,vertices.Count).ToArray();
        mesh.normals=normals.ToArray();mesh.uv=uv.ToArray();mesh.tangents=tangents.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float BaseMass(VehicleObjectModel model,EraVisualPreset preset,bool cassette)
        =>model.ComponentID==CompositeModule.ComponentId?(float)CompositeModuleEraGeometry.Mass(CompositeModule.Current):cassette?preset.CassetteMass:preset.MountMass;
    private static float ResizableAreaScale(VehicleObjectModel model,EraVisualPreset preset)
    {
        if(model.ComponentID==CompositeModule.ComponentId)
        {
            var s=model.GetModelScale();return PlacedEraCells.ValidScale(s)?(float)Math.Abs((double)s.x*s.z):float.NaN;
        }
        if(!preset.Resizable||preset.CellPitchM<=0&&!preset.WholeCells)return 1f;
        var scale=model.GetModelScale();
        if(preset.WholeCells)
        {
            if(!PlacedEraCells.TryModularCounts(scale,preset,out var countU,out var countV))
            {
                if(identityLogs<16)MaterialFeature.ModLog.LogWarning("Modular ERA scale rejected; paid mass retained at the last valid count.");
                return float.NaN;
            }
            // Base preset mass is exactly one paid cell. No depth or continuous
            // scale factor can grant uncovered area.
            return countU*countV;
        }
        var area=EraCellPartition.MassScale(scale.x,scale.z,preset.MaximumAreaScale);
        if(!float.IsFinite((float)area)||area<=0)
        {
            if(identityLogs<16)MaterialFeature.ModLog.LogWarning("Resizable ERA scale rejected; mass retained at the paid base.");
            return 1f;
        }
        return (float)area;
    }

    private static float NativeAreaScale(VehicleObjectModel model,EraVisualPreset preset)
    {
        if(!preset.Resizable)return 1f;
        var scale=model.GetModelScale();
        var area=Math.Abs((double)scale.x*scale.z);
        return double.IsFinite(area)&&area>0?(float)area:1f;
    }

    internal static void RefreshScale(VehicleTransform? transform)
    {
        if(transform==null)return;
        EnqueueTransform(transform,"scale/parent/mirror");
        DrainRefresh();
    }

    // Narrow verified path: make the component's collider state dirty and ask
    // the native component lifecycle to rebuild it. SetCollisionMesh alone does
    // not prove that already-built native damage colliders were replaced.
    private static void RequestNativeColliderRebuild(VehicleObjectModel model,string key)
    {
        if(!CanBuildGeometry)return;
        if(model==null)return;
        var component=model.associatedComponent;
        if(component==null)return;
        if(colliderRequestDepth>0)
        {
            QueueColliderRebuild(model,key);
            return;
        }
        colliderRequestDepth++;
        try
        {
            component.MarkCollidersDirty();
            component.RequestRebuild();
            if(colliderLogs++<24&&SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)
                MaterialFeature.ModLog.LogInfo($"[Modular collider] component={model.ComponentID} cells/mesh changed; MarkCollidersDirty+RequestRebuild issued after geometry swap");
        }
        catch(Exception ex){if(identityLogs<16)MaterialFeature.ModLog.LogWarning("Modular collider rebuild request: "+ex.Message);}
        finally{colliderRequestDepth--;}
    }

    internal static void ClearMeshes()
    {
        try
        {
            // Enumerate the complete generated set, including legacy cache
            // evictions that were intentionally left alive for live renderers.
            foreach(var mesh in AllGeneratedMeshes)if(mesh)UnityEngine.Object.Destroy(mesh);
        }
        catch { }
        AllGeneratedMeshes.Clear();MeshOwnership.Clear();ModularScaleSignatures.Clear();Meshes.Clear();
        AppliedRender.Clear();LastValidArea.Clear();baseAssembly.Clear();
        FastStates.Clear();OwnedTransforms.Clear();OwnerByModel.Clear();InstalledGeometry.Clear();
        RefreshQueue.Clear();RefreshTransforms.Clear();DeferredModels.Clear();
        PendingColliderModels.Clear();PendingColliderKeys.Clear();
    }

    private static void ForgetModel(IntPtr pointer)
    {
        InstalledGeometry.Remove(pointer);
        ModularScaleSignatures.Remove(pointer);AppliedRender.Remove(pointer);
        FastStates.Remove(pointer);UnregisterOwner(pointer);
        LastValidArea.Remove(pointer);baseAssembly.Remove(pointer);
        DeferredModels.Remove(pointer);PendingColliderModels.Remove(pointer);
        PendingColliderKeys.Remove(pointer);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.ReclampScale))]
    private static void ScaleReclamped(VehicleTransform __instance)=>RefreshScale(__instance);

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),"set_Scale")]
    private static void ScaleSet(VehicleTransform __instance)=>RefreshScale(__instance);

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),"set_ModelScale")]
    private static void ModelScaleSet(VehicleObjectModel __instance)
    {if(!Own(__instance))return;RegisterOwner(__instance);RefreshScale(__instance.VehicleTransform);}

    // Native transform lifecycle boundaries. CopyFrom/OnDuplicate/SetMirrors and
    // load are the places a mirrored or saved scale is restored without passing
    // through set_Scale on every component. Enqueue both exact transforms.
    // TransformChangeType is a flags enum: Translate=1, Rotate=2, Scale=4,
    // Parent=8, ParentTransform=16, Manual=32. Translation/rotation alone cannot
    // change paid cell count or DAG layout, so only scale/parent events enqueue.
    private static bool ResizeRelevant(TransformChangeType change)
        =>ModularRenderPolicy.IsResizeRelevant((int)change);

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.OnTransformChanged))]
    private static void TransformChanged(VehicleTransform __instance,TransformChangeType __0)
    {
        if(ResizeRelevant(__0))RefreshScale(__instance);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.OnParentTransformChanged))]
    private static void ParentTransformChanged(VehicleTransform __instance,TransformChangeType __0)
    {
        if(ResizeRelevant(__0))RefreshScale(__instance);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.OnChildTransformChanged))]
    private static void ChildTransformChanged(VehicleTransform __instance,TransformChangeType __0,VehicleTransform __1)
    {
        if(!ResizeRelevant(__0))return;
        EnqueueTransform(__1,"child-transform");
        EnqueueTransform(__instance,"child-transform-parent");
        DrainRefresh();
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.CopyFrom))]
    private static void TransformCopied(VehicleTransform __instance,VehicleTransform __0)
    {
        EnqueueTransform(__instance,"copy-from");
        EnqueueTransform(__0,"copy-source");
        DrainRefresh();
        ScheduleSettle(__instance);ScheduleSettle(__0);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.OnDuplicate))]
    private static void TransformDuplicated(VehicleTransform __instance,VehicleTransform __0)
    {
        EnqueueTransform(__instance,"duplicate-origin");
        EnqueueTransform(__0,"duplicate-copy");
        DrainRefresh();
        ScheduleSettle(__instance);ScheduleSettle(__0);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.SetMirrors))]
    private static void MirrorsSet(VehicleTransform __0,VehicleTransform __1)
    {
        EnqueueTransform(__0,"mirror-a");
        EnqueueTransform(__1,"mirror-b");
        DrainRefresh();
        ScheduleSettle(__0);ScheduleSettle(__1);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleTransform),nameof(VehicleTransform.LoadState))]
    private static void TransformLoaded(VehicleTransform __instance)
    {
        EnqueueTransform(__instance,"transform-load");
        DrainRefresh();
        ScheduleSettle(__instance);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleComponent),nameof(VehicleComponent.LoadData))]
    private static void ComponentDataLoaded(VehicleComponent __instance)
    {
        RefreshScale(__instance?.VehicleTransform);
        ScheduleSettle(__instance?.VehicleTransform);
    }

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObjectModel),nameof(VehicleObjectModel.ForceRebuild))]
    private static void ModelForceRebuilt(VehicleObjectModel __instance)
    {if(!CanBuildGeometry||!Own(__instance))return;RegisterOwner(__instance);FastStates.Remove(__instance.Pointer);RefreshScale(__instance.VehicleTransform);}

    [HarmonyPostfix,HarmonyPatch(typeof(VehicleObject),nameof(VehicleObject.Duplicate))]
    private static void ObjectDuplicated(VehicleObject __result)
    {
        if(__result?.VehicleTransform is {} transform)
        {
            RefreshScale(transform);
            ScheduleSettle(transform);
        }
    }

    private static VehicleObjectModel? Sibling(VehicleObjectModel model,string id)
    {
        var owner=model.VehicleObject;
        var root=model.VehicleRoot;
        if(owner==null||root==null)return null;
        var components=model.VehicleObject?.Components;
        if(components==null)return null;
        int count=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
        VehicleObjectModel? match=null;
        for(int i=0;i<count;i++)
        {
            if(components[i].TryCast<VehicleObjectModel>() is not {} sibling||sibling.ComponentID!=id)continue;
            if(match!=null||sibling.VehicleObject?.Pointer!=owner.Pointer||sibling.VehicleRoot?.Pointer!=root.Pointer)return null;
            match=sibling;
        }
        return match;
    }
    // 0.22.51: the intact housing render (state 0 of the damage series), selected exactly as before for zero spent
    // units. The ledger is not read: native construction always builds the intact look. Missing, duplicate or
    // foreign siblings still fail closed to the preset's own visual. 0.22.70: the damaged look after an activation
    // is a render-only binding in EraSpentRender, never a construction of this method.
    private static string? IntactHousingVisual(VehicleObjectModel model)
    {
        if(!EraPresetRegistry.Visual(model.ComponentID,out var preset,out bool cassette)||cassette
            ||preset.HousingDamageVisuals is not {} visuals||preset.HousingActiveIds is not {} ids
            ||ids.Length==0||visuals.Length!=ids.Length+1)return null;
        foreach(var id in ids)
            if(Sibling(model,id) is not {} insert||insert.VehicleRoot==null)return null;
        return visuals[0];
    }
}
