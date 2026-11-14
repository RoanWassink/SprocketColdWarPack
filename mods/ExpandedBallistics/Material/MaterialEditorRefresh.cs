using BepInEx;
using System.Text.Json.Nodes;
using Sprocket.TechTrees;
using Sprocket.Vehicles.PlateStructures;
namespace SprocketExpandedBallistics.Materials;
internal static class MaterialEditorRefresh
{
    internal static void ApplyLabels(IEnumerable<ArmourMaterial> materials)
    {
        MaterialAvailability.CustomLabels.Clear();
        var path=Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.materials.editor.json");if(!File.Exists(path))return;
        var labels=MaterialEditorDraft.ReadLabels(File.ReadAllText(path));
        foreach(var pair in labels)MaterialAvailability.CustomLabels[pair.Key]=pair.Value!.GetValue<string>();
        foreach(var m in materials)if(labels[m.Id] is {} label)m.Label=label.GetValue<string>();
    }
    internal static void Reload(PlateStructure owner)
    {
        // Reject invalid armour files before mutating the live native technology frame.
        MaterialFeature.RefreshArmour();
        var loaded=TechTreeLoader.LoadTechnologies(TechTreeLoader.TechDirectory);
        // 0.22.14: later frames (Play, the designer after Play) are built from this list too, not from the startup list.
        // Published before the dated selection below, so that call already sees the same list.
        SprocketExpandedBallistics.RuntimeLiveTechnology.Publish(loaded);
        // The file set contains several dated versions of types such as cannon.
        // Use native selection before creating a frame; feeding every version
        // directly to TechFrame produces duplicate keys.
        var loaderAtDate=new TechTreeLoader { technologies=loaded };
        var refreshed=loaderAtDate.GetTechFrameAtDate(owner.Vehicle.Tech.Date).TryCast<TechFrame>()
            ??throw new InvalidOperationException("Native dated technology selection returned an unsupported frame.");
        var frame=owner.Vehicle.Tech.TryCast<TechFrame>()??throw new InvalidOperationException("Native technology frame type is unsupported.");
        frame.techLookup=refreshed.techLookup;
        // UI reads the same already-validated authoritative snapshot.
        ResponseUi.Reload();MaterialDatabase.Reload();
        var items=owner.Vehicle.ObjectReader.Items;
        int Count<T>(Il2CppSystem.Collections.Generic.IReadOnlyList<T> list)=>list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<T>>().Count;
        for(int i=0;i<Count(items);i++)
        {
            var parts=items[i].Components;for(int j=0;j<Count(parts);j++)if(parts[j].TryCast<PlateStructure>() is {} plate)
            {
                if(owner.Vehicle.Tech.TryGetTech(plate.Blueprint.armourTechID,out _))plate.SetArmourMaterial(plate.Blueprint.armourTechID);
                plate.RequestRebuild();
            }
        }
        if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo("[Material editor] Native technology frame, passive materials and armour response consumer refreshed.");
        SprocketExpandedBallistics.RuntimeCassettePlateVisual.InvalidateCatalogue();
        SprocketExpandedBallistics.CassetteComposerPanel.MaterialsReloaded();
    }
}




