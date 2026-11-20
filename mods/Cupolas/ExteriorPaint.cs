using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;

namespace SprocketCupolas;

internal static class ExteriorPaint
{
    private sealed record Paint(IntPtr Vehicle, IntPtr Transform, VehicleMaterial Material);
    private static VehicleMaterialConfiguration? paintFactory;
    private static Texture2D? paintBase, paintArm, paintNormal, paintDpm;
    private static readonly Dictionary<IntPtr, Paint> paints = new();
    private static readonly List<VehicleMaterial> createdPaints = new();
    private static readonly HashSet<string> logged = new();

    private static Texture2D Neutral(string name, Color colour, bool linear)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear)
        { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        texture.SetPixel(0, 0, colour); texture.Apply(false, true); return texture;
    }

    private static void EnsurePaintFactory()
    {
        if (paintFactory != null) return;
        paintBase = Neutral("Cupola exterior base", Color.white, false);
        paintArm = Neutral("Cupola exterior arm", new Color(1f, .5f, 0f, 1f), true);
        paintNormal = Neutral("Cupola exterior normal", new Color(.5f, .5f, 1f, 1f), true);
        paintDpm = Neutral("Cupola exterior paint mask", new Color(0f, 0f, 1f, 1f), true);
        paintFactory = ScriptableObject.CreateInstance<VehicleMaterialConfiguration>();
        paintFactory.hideFlags = HideFlags.HideAndDontSave;
        paintFactory.flags = VehicleMaterialFlags.None;
        paintFactory.slot = VehicleMaterialSlot.Exterior;
        paintFactory.materialSource = null;
        paintFactory.baseColourMap = paintBase;
        paintFactory.armMap = paintArm;
        paintFactory.normalMap = paintNormal;
        paintFactory.dpmMap = paintDpm;
    }

    internal static VehicleMaterial? Get(VehicleComponent model, VehicleMaterial? fallback)
    {
        var vehicle = model.Vehicle; var transform = model.VehicleTransform;
        var controller = vehicle?.Materials;
        if (vehicle == null || transform == null || controller == null) return fallback;
        if (paints.TryGetValue(model.Pointer, out var known) && known.Vehicle == vehicle.Pointer
            && known.Transform == transform.Pointer && known.Material.Material != null) return known.Material;
        if (paints.Count >= 4096) paints.Clear();
        EnsurePaintFactory();
        var material = controller.NewMaterial(paintFactory!.Cast<IVehicleMaterialFactory>(), transform);
        if (material == null) return fallback;
        material.Flags = VehicleMaterialFlags.None;
        material.PaintSlot = VehicleMaterialSlot.Exterior;
        paints[model.Pointer] = new(vehicle.Pointer, transform.Pointer, material);
        createdPaints.Add(material);
        if (logged.Add(model.ComponentID))
            Plugin.Instance.Log.LogInfo($"[Cupola] Exterior paint linked; component={model.ComponentID}; shader={material.Material?.shader?.name}");
        return material;
    }

    internal static void Process(VehicleComponent model, VehicleMaterial paint)
    {
        var vehicle = model.Vehicle; var transform = model.VehicleTransform;
        if (vehicle == null || transform == null || vehicle.Materials == null) return;
        vehicle.Materials.MarkMaterialModified(paint);
        vehicle.MaterialProcessor?.Process(paint, transform);
    }

    internal static void Cleanup()
    {
        foreach (var paint in createdPaints) if (paint.Material != null) UnityEngine.Object.Destroy(paint.Material);
        createdPaints.Clear();
        paints.Clear();
        if (paintFactory != null) UnityEngine.Object.Destroy(paintFactory); paintFactory = null;
        if (paintBase != null) UnityEngine.Object.Destroy(paintBase); paintBase = null;
        if (paintArm != null) UnityEngine.Object.Destroy(paintArm); paintArm = null;
        if (paintNormal != null) UnityEngine.Object.Destroy(paintNormal); paintNormal = null;
        if (paintDpm != null) UnityEngine.Object.Destroy(paintDpm); paintDpm = null;
        logged.Clear();
    }
}
