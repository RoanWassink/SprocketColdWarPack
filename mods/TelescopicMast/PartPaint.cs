using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AssetManagement;
using UnityEngine;

namespace SprocketTelescopicMast;

// Called only after the existing native Build, never from its prefix.
internal static class PartPaint
{
    private static bool warned;
    private static VehicleMaterialConfiguration? factory;
    private static readonly List<UnityEngine.Object> assets = new();
    private static readonly PartPaintCache<VehicleMaterial> paints = new(
        paint => { if (paint.Material != null) UnityEngine.Object.Destroy(paint.Material); }, Warn);

    private static void Warn(Exception ex)
    {
        if (warned) return;
        warned = true;
        try { Plugin.Instance.Log.LogWarning($"[Mast/Hinge] Exterior paint: {ex.Message}; previous material retained."); }
        catch { } // Visual fallback must never escape to the game, even during teardown.
    }
    private static Texture2D Neutral(string name, Color colour, bool linear)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear)
        { name = name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        assets.Add(texture);
        texture.SetPixel(0, 0, colour); texture.Apply(false, true);
        return texture;
    }
    private static VehicleMaterialConfiguration Factory()
    {
        if (factory != null) return factory;
        var config = ScriptableObject.CreateInstance<VehicleMaterialConfiguration>();
        assets.Add(config); config.hideFlags = HideFlags.HideAndDontSave;
        config.flags = VehicleMaterialFlags.None; config.slot = VehicleMaterialSlot.Exterior;
        // Cloning a source forces CustomShader and bypasses native paint.
        config.materialSource = null;
        config.baseColourMap = Neutral("Mast/hinge base", Color.white, false);
        config.armMap = Neutral("Mast/hinge surface", new Color(1, .5f, 0, 1), true);
        config.normalMap = Neutral("Mast/hinge normal", new Color(.5f, .5f, 1, 1), true);
        config.dpmMap = Neutral("Mast/hinge paint mask", new Color(0, 0, 1, 1), true);
        return factory = config;
    }
    internal static void Apply(VehicleObjectModel component)
    {
        try
        {
            // Mount is intentionally excluded, including in the designer.
            if (!(HingeRuntime.Own(component) && component.ComponentID != HingeRuntime.MountModel) && !MastRuntime.Own(component) && !RotatorRuntime.Own(component)) return;
            var model = component.Model; var vehicle = component.Vehicle; var transform = component.VehicleTransform;
            if (model == null || vehicle == null || transform == null || component.VehicleObject == null) return;
            var controller = vehicle.Materials; var processor = vehicle.MaterialProcessor;
            if (controller == null || processor == null) throw new InvalidOperationException("Native paint context unavailable.");
            paints.Prepare(component.Pointer, component.VehicleObject.Pointer, vehicle.Pointer, transform.Pointer,
                () => controller.NewMaterial(Factory().Cast<IVehicleMaterialFactory>(), transform)
                    ?? throw new InvalidOperationException("Native exterior material unavailable."),
                paint =>
                {
                    paint.Flags = VehicleMaterialFlags.None; paint.PaintSlot = VehicleMaterialSlot.Exterior;
                    var previous = model.Materials;
                    if (previous == null || previous.Length == 0)
                        throw new InvalidOperationException("No existing material to preserve.");
                    if (previous[0]?.Pointer == paint.Pointer) return;
                    var replacement = new Il2CppReferenceArray<VehicleMaterial>(previous.Length);
                    for (int i = 0; i < previous.Length; i++) replacement[i] = previous[i];
                    replacement[0] = paint;
                    try
                    {
                        model.SetMaterials(replacement);
                        controller.MarkMaterialModified(paint);
                        processor.Process(paint, transform);
                    }
                    catch
                    {
                        // Native SetMaterials is the same proven API used for the forward assignment.
                        model.SetMaterials(previous);
                        throw;
                    }
                });
        }
        catch (Exception ex) { Warn(ex); }
    }
    internal static void Release(VehicleObject obj)
    {
        try { if (obj != null) paints.Release(obj.Pointer); }
        catch (Exception ex) { Warn(ex); }
    }
    internal static void Cleanup()
    {
        paints.Clear();
        foreach (var asset in assets)
            try { if (asset != null) UnityEngine.Object.Destroy(asset); } catch (Exception ex) { Warn(ex); }
        assets.Clear(); factory = null;
    }
}
