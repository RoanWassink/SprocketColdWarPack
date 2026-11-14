using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SprocketExpandedBallistics.Materials;

// Native material register/painter owns paint changes and material lifetime.
// No fixed-colour shader, shared vehicle mutation, or extra rendering hooks.
internal static class EraSurface
{
    private sealed record Surface(IntPtr Vehicle,IntPtr Transform,VehicleMaterial Material,VehicleMaterial? Dark);
    private static readonly Dictionary<IntPtr,Surface> Surfaces=new();
    private static VehicleMaterialConfiguration? factory,darkFactory;
    private static int appliedLogs;
    internal static void Apply(VehicleObjectModel model,bool cassette)
    {
        var native=model.Model;var vehicle=model.Vehicle;var transform=model.VehicleTransform;
        if(native==null||vehicle==null||transform==null)return;
        if(!Surfaces.TryGetValue(model.Pointer,out var current)||current.Vehicle!=vehicle.Pointer||current.Transform!=transform.Pointer)
        {
            if(Surfaces.Count>=4096)throw new InvalidOperationException("ERA paint cache limit reached; native fallback retained.");
            if(factory==null)
            {
                factory=ScriptableObject.CreateInstance<VehicleMaterialConfiguration>();
                factory.hideFlags=HideFlags.HideAndDontSave;
                factory.flags=VehicleMaterialFlags.None;factory.slot=VehicleMaterialSlot.Exterior;
                // Native source clones force CustomShader, which bypasses normal paint.
                factory.materialSource=null;
                factory.baseColourMap=NeutralMap("ERA base",Color.white,false);
                factory.armMap=NeutralMap("ERA surface",new Color(1f,.5f,0f,1f),true);
                factory.normalMap=NeutralMap("ERA normal",new Color(.5f,.5f,1f,1f),true);
                // VehicleLit: paint weight = DPM.b * (1-saturate(3*(DPM.g-condition))).
                // Blue enables paint; red/green zero adds no dirt or wear mask.
                factory.dpmMap=NeutralMap("ERA paint mask",new Color(0f,0f,1f,1f),true);
            }
            var material=vehicle.Materials.NewMaterial(factory.Cast<IVehicleMaterialFactory>(),transform);
            if(material==null)throw new InvalidOperationException("Native ERA paint material unavailable.");
            material.Flags=VehicleMaterialFlags.None;material.PaintSlot=VehicleMaterialSlot.Exterior;
            VehicleMaterial? dark=null;
            if(model.ComponentID==SprocketExpandedBallistics.CompositeModule.ComponentId)
            {
                if(darkFactory==null)
                {
                    darkFactory=ScriptableObject.CreateInstance<VehicleMaterialConfiguration>();
                    darkFactory.hideFlags=HideFlags.HideAndDontSave;darkFactory.flags=VehicleMaterialFlags.None;
                    darkFactory.slot=VehicleMaterialSlot.Exterior;darkFactory.materialSource=null;
                    darkFactory.baseColourMap=NeutralMap("Composite dark",new Color(.025f,.025f,.025f,1),false);
                    darkFactory.armMap=NeutralMap("Composite rubber",new Color(1,.8f,0,1),true);
                    darkFactory.normalMap=NeutralMap("Composite dark normal",new Color(.5f,.5f,1,1),true);
                    darkFactory.dpmMap=NeutralMap("Composite no paint",new Color(0,0,0,1),true);
                }
                dark=vehicle.Materials.NewMaterial(darkFactory.Cast<IVehicleMaterialFactory>(),transform);
                if(dark==null)throw new InvalidOperationException("Composite dark material unavailable");
            }
            current=new(vehicle.Pointer,transform.Pointer,material,dark);Surfaces[model.Pointer]=current;
            if(appliedLogs++<6)if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo($"[ERA vehicle paint] component={model.ComponentID} vuid={model.VUID.Value} shader={material.Material?.shader?.name} slot={material.PaintSlot} flags={material.Flags}; native linked exterior material");
        }
        var materials=native.Materials;
        if(materials==null||materials.Length!=(current.Dark==null?1:2)||materials[0]?.Pointer!=current.Material.Pointer||(current.Dark!=null&&materials[1]?.Pointer!=current.Dark.Pointer))
        {
            native.SetMaterials(new Il2CppReferenceArray<VehicleMaterial>(current.Dark==null?new[]{current.Material}:new[]{current.Material,current.Dark}));
            vehicle.Materials.MarkMaterialModified(current.Material);
            vehicle.MaterialProcessor.Process(current.Material,transform);
            if(current.Dark!=null){vehicle.Materials.MarkMaterialModified(current.Dark);vehicle.MaterialProcessor.Process(current.Dark,transform);}
        }
    }
    private static Texture2D NeutralMap(string name,Color colour,bool linear)
    {
        var texture=new Texture2D(1,1,TextureFormat.RGBA32,false,linear)
        {name=name,hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Point};
        texture.SetPixel(0,0,colour);
        texture.Apply(false,true);
        return texture;
    }
    internal static void Forget(VehicleObjectModel model)=>Surfaces.Remove(model.Pointer);
}
