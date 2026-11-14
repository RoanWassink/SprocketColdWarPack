using BepInEx;
using SprocketExpandedBallistics.Era;

namespace SprocketExpandedBallistics.Materials;

internal sealed record EraVisualPreset(string Cassette, string Mount, string Visual,
    string Collision, string Icon, string PartGuid, float CassetteMass, float MountMass,
    string? SpentVisual = null, string? MountCollision = null, float DefaultPlacementRotation = 90f,
    string[]? HousingActiveIds = null, string[]? HousingDamageVisuals = null,
    string? PassiveMaterialId = null,
    // Optional finite-area partition for a resizable/resizable-capable cassette.
    // Zero pitch means the historical one-component/one-cell behaviour.
    double CellPitchM = 0, int MaxCells = 0, bool Resizable = false,
    double MaximumAreaScale = 0, string SurfaceAxes = "xz", string? MountVisual = null,
    // Canonical base surface bounds in unscaled component-local metres. When
    // set, hit and render triangles use this exact paid footprint instead of
    // whichever collision mesh happens to be loaded.
    double CellMinU = 0, double CellMaxU = 0, double CellMinV = 0, double CellMaxV = 0,
    // Whole-cell modular panels: native X/Z scale selects an integer cell count
    // and the generated local geometry is inverse-scaled so world cell size and
    // the outer footprint stay snapped to whole cells.
    bool WholeCells = false, double CellWidthM = 0, double CellLengthM = 0,
    int MaxCountU = 0, int MaxCountV = 0,
    // 1 = inner/back paid stage, 2 = outer paid stage. Housing ignores it.
    int StageIndex = 0,
    // Selects the canonical modular geometry spec: null/"standard" = DupletV1,
    // "slim" = DupletSlim. Non-modular presets leave this null.
    string? ModularVariant = null, int FixedCountV = 0);

internal static class EraPresetRegistry
{
    internal static readonly EraVisualPreset[] Presets = EraGeneratedDefinitions.Presets.Prepend(
        new EraVisualPreset(SprocketExpandedBallistics.CompositeModule.ComponentId,"compositeSideModuleBacking","compositeSideModule.obj","compositeSideModule-collision.obj",
            "compositeSideModule-icon.png",SprocketExpandedBallistics.CompositeModule.PartGuid,(float)SprocketExpandedBallistics.CompositeModuleEraGeometry.Mass(SprocketExpandedBallistics.CompositeModule.Current),(float)SprocketExpandedBallistics.CompositeModuleEraGeometry.Mass(SprocketExpandedBallistics.CompositeModule.Current),
            MountCollision:"compositeSideModule-collision.obj",DefaultPlacementRotation:0,PassiveMaterialId:SprocketExpandedBallistics.CompositeModule.MaterialId,Resizable:true,CellPitchM:.3,MaxCells:6)).ToArray();
    private static EraPartBindingCatalogue catalogue = EraPartBindings.Parse("{\"schemaVersion\":1,\"matchMode\":\"componentId\",\"bindings\":[]}");
    internal static void Start()
    {
        // One immutable session snapshot; Save/refresh cannot reclassify spent keys.
        try { catalogue=MaterialFeature.ArmourSnapshot.Bindings
            ??throw new InvalidOperationException("Shared immutable ERA bindings are unavailable."); }
        catch(Exception ex){MaterialFeature.ModLog.LogWarning("Placed ERA bindings unavailable; active classification disabled: "+ex.Message);}
    }
    internal static bool Visual(string? component,out EraVisualPreset preset,out bool cassette)
    {
        if(string.IsNullOrEmpty(component)){preset=null!;cassette=false;return false;}
        foreach(var item in Presets)
        {
            if(component==item.Cassette){preset=item;cassette=true;return true;}
            if(component==item.Mount){preset=item;cassette=false;return true;}
        }
        preset=null!;cassette=false;return false;
    }
    internal static bool Cassette(string? component,out EraPartBinding binding)
        => catalogue.TryGetCassette(component,out binding!);
    internal static bool Mount(string? component,out EraPartBinding binding)
        => catalogue.TryGetMount(component,out binding!);
}

