using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Transmissions;
using Sprocket.Vehicles.Powertrains.Layouts;
using Sprocket.Powertrains.Engines;
using SprocketEngineExpansion.Core;
using UnityEngine;
namespace SprocketEngineExpansion.Native;

/// <summary>M2+: per-model definition immediately before Generate; outputs before the native Built event.</summary>
[HarmonyPatch]
internal static class PowerpackModel
{
    internal static PowerpackModelSettings Settings=new();
    internal static bool Installed;
    internal static bool Enabled=>Installed&&Settings.Enabled;
    private sealed class State
    {
        internal readonly ModelDefinitionLease<EngineModelDefinition> Definition;
        internal EngineBlueprint Blueprint;
        internal CombustionEngine? Engine;
        internal string? Report;
        internal State(EngineModelDefinition definition,EngineBlueprint blueprint){Definition=new(definition);Blueprint=blueprint;}
    }
    private static readonly Dictionary<IntPtr,State> models=new();
    internal static bool Effective(CombustionEngine engine)
    {
        EngineDateRegistry.Remember(engine.Blueprint,engine);
        return EngineDateRegistry.PowerpackEffective(engine.Blueprint);
    }
    internal static bool UsesEngine(CombustionEngine engine)=>Enabled&&Effective(engine)&&
        engine.Blueprint.EngineLayout==EngineLayout.V&&Plans.EffectiveKind(engine.Blueprint)!=EngineKind.GasTurbine;

    [HarmonyPrefix,HarmonyPatch(typeof(CombustionEngineModel),nameof(CombustionEngineModel.Generate))]
    private static void BeforeGenerate(CombustionEngineModel __instance,EngineModelBlueprint __0,IEngineModelAssetLoader __1)
    {
        try
        {
            if(__1.TryCast<CombustionEngine>() is not {} engine)return;
            bool active=UsesEngine(engine);
            if(!models.TryGetValue(__instance.Pointer,out var state))
            {if(!active)return;models.Add(__instance.Pointer,state=new(__instance.definition,engine.Blueprint));}
            state.Blueprint=engine.Blueprint;state.Engine=engine;
            bool changed=state.Definition.Select(active,Settings,Clone);
            // A native layout change constructs a NEW model: this prefix sees that new object's definition too.
            __instance.definition=state.Definition.Current;
            if(changed)__instance.builtMeshHash=~__0.GetMeshRebuildHashCode();
            if(!active&&state.Report!=null)
            {if(Plugin.Detailed)Plugin.ModLog.LogInfo("POWERPACK model off");state.Report=null;}
        }
        catch(Exception ex){Plugin.Warn("powerpack model definition",ex);throw;}
    }
    internal static EngineModelDefinition Clone(EngineModelDefinition native,PowerpackModelSettings s)=>new()
    {
        Layout=native.Layout,materialGuid=native.materialGuid,startMeshGuid=native.startMeshGuid,
        cylinderSegmentMeshGuid=native.cylinderSegmentMeshGuid,outputMeshGuid=native.outputMeshGuid,
        baseCylinderCount=0,cylindersPerSegment=2,maxCylinderCount=native.maxCylinderCount,cylinderDisplacement=1,
        startSegmentLength=(float)s.Start,cylinderSegmentLength=(float)s.Pitch,outputSegmentLength=(float)s.Output,
        outputSideOffset=0,outputForwardOffset=(float)s.Output,outputHeightOffset=(float)s.Crank,
        centreOfMassHeight=(float)s.CentreOfMass
    };

    [HarmonyPostfix,HarmonyPatch(typeof(CombustionEngineModel),nameof(CombustionEngineModel.BuildMeshes))]
    private static void Built(CombustionEngineModel __instance)
    {
        if(!models.TryGetValue(__instance.Pointer,out var state)||!state.Definition.Active)return;
        try
        {
            var instances=__instance.meshInstances;int n=instances.Length-2;
            var factors=Matrix4x4.Scale(new Vector3((float)Settings.SizeFactor,(float)(Settings.SizeFactor*Settings.HeightFactor),(float)(Settings.SizeFactor*Settings.LengthFactor)));
            var pieces=new List<(string Part,Matrix4x4 Matrix)>();
            Matrix4x4 Matrix(int i)=>__instance.transformMatrix*instances[i].transform*factors;
            pieces.Add(("start",Matrix(instances.Length-1)));
            for(int i=0;i<n;i++)pieces.Add(("pair",Matrix(i)));
            pieces.Add(("output",Matrix(instances.Length-2)));
            pieces.Add((Settings.TurboLayout==PowerpackTurboLayout.Compact?"turbo":"turbo-outboard",Matrix(instances.Length-2)));
            var lods=__instance.outputLODs;
            if(lods==null||lods.Length!=2)
            {
                if(lods!=null)foreach(var old in lods)if(old!=null)UnityEngine.Object.Destroy(old);
                lods=new Il2CppReferenceArray<Mesh>(new[]{new Mesh(),new Mesh()});__instance.outputLODs=lods;
            }
            void Combine(Mesh target,string suffix)
            {
                var batch=new Il2CppStructArray<CombineInstance>(pieces.Count);
                for(int i=0;i<pieces.Count;i++)batch[i]=new CombineInstance {mesh=PowerpackMeshes.Get(pieces[i].Part+suffix),transform=pieces[i].Matrix};
                target.Clear();target.CombineMeshes(batch,true,true);target.RecalculateBounds();
            }
            Combine(lods[0],"-lod0");Combine(lods[1],"-lod1");
            Combine(__instance.outputShadow,"-lod1");Combine(__instance.outputCollisionMesh,"-hull");
            int tris=pieces.Sum(p=>PowerpackMeshes.Triangles(p.Part+"-lod0"));
            var bounds=__instance.outputCollisionMesh.bounds.size;
            var d=state.Blueprint.CylinderVolume;string report=FormattableString.Invariant($"POWERPACK model on n={n} d={d:0.####} k={__instance.scale:0.####} LxBxH={bounds.z:0.###}x{bounds.x:0.###}x{bounds.y:0.###} tris={tris}");
            // Existing mesh hook: refresh the render-only casing/bridge after the model meshes are
            // committed. This is a direct visual rebuild, not a native layout dirty flag: no extra
            // autoposition/history step and no rebuild loop. The slot retires the old child first.
            if(state.Engine?.PowertrainNode?.TryCast<PowertrainComponent>()?.layout?.TryCast<VehiclePowertrain>() is {} powertrain)
                PowerpackBridge.Refresh(powertrain);
            if(Plugin.Detailed&&state.Report!=report)Plugin.ModLog.LogInfo(report);state.Report=report;
        }
        catch(Exception ex){Plugin.Warn("powerpack model meshes",ex);throw;}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(CombustionEngineModel),nameof(CombustionEngineModel.Release))]
    private static void Release(CombustionEngineModel __instance)=>models.Remove(__instance.Pointer);

    internal static IEnumerable<CombustionEngine> Engines(VehiclePowertrain powertrain)
    {
        var items=powertrain.Vehicle?.ObjectReader?.Items;if(items==null)yield break;
        int count=items.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleObject>>().Count;
        for(int i=0;i<count;i++)
        {
            var components=items[i].Components;
            int n=components.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponent>>().Count;
            for(int j=0;j<n;j++)if(components[j].TryCast<CombustionEngine>() is {} engine&&
                engine.PowertrainNode?.TryCast<PowertrainComponent>()?.layout?.Pointer==powertrain.Pointer)yield return engine;
        }
    }
    internal static bool TransmissionActive(TransmissionBlock block)
    {
        if(!Enabled)return false;
        var node=block.powertrainNode?.TryCast<PowertrainComponent>();
        if(node?.layout?.TryCast<VehiclePowertrain>() is not {} powertrain)return false;
        foreach(var engine in Engines(powertrain))
        {
            if(!Effective(engine))continue;
            var context=PowerpackLayout.Resolve(engine);
            if(context?.Status!=PowerpackStatus.Ready||!context.Marked)continue;
            if(context.ModuleIds.Contains(node.powertrainModuleID))return true;
        }
        return false;
    }
    private static readonly Dictionary<IntPtr,(string Key,bool Active)> engineStates=new();
    [HarmonyPostfix,HarmonyPatch(typeof(CombustionEngine),nameof(CombustionEngine.Build))]
    private static void EngineRebuilt(CombustionEngine __instance)
    {
        try
        {
            if(!Enabled)return;
            var context=PowerpackLayout.Resolve(__instance);if(context==null)return;
            bool active=Effective(__instance);
            string key=$"{active}:{__instance.Blueprint.EngineLayout}:{Plans.EffectiveKind(__instance.Blueprint)}:{Settings}:{context.Plan}:{context.Twin}";
            bool remembered=engineStates.TryGetValue(__instance.Pointer,out var previous);
            if(remembered&&previous.Key==key)return;
            engineStates[__instance.Pointer]=(key,active);
            // An ordinary native engine (or Model.Enabled=false) must not gain rebuild/dirty side effects.
            // Only an active pack or the transition away from one needs the transmission refresh.
            if(!active&&(!remembered||!previous.Active))return;
            var ids=context.ModuleIds;
            foreach(int id in ids)
            {
                var module=context.Powertrain.Layout?.GetModule(id);
                if(module!=null)PowerpackLayout.FindTransmission(context.Powertrain,module)?.RequestRebuild();
            }
            context.Powertrain.RaiseDirtyFlags(VehiclePowertrainLayoutDirtyFlags.Geometry);
            PowerpackBridge.Clear(context.Powertrain.Pointer);
        }
        catch(Exception ex){Plugin.Warn("powerpack model rebuild",ex);}
    }
    [HarmonyPostfix,HarmonyPatch(typeof(CombustionEngine),nameof(CombustionEngine.Release))]
    private static void EngineReleased(CombustionEngine __instance)
    {engineStates.Remove(__instance.Pointer);var context=PowerpackLayout.Resolve(__instance);if(context!=null)PowerpackBridge.Clear(context.Powertrain.Pointer);}
}
