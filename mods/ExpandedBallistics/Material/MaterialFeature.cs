using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using UnityEngine.Events;

namespace SprocketExpandedBallistics.Materials;

// Internal feature module. The unified Shell entrypoint is the sole BepInEx plugin.
internal static class MaterialFeature
{
    internal static ManualLogSource ModLog = null!;
    internal static ConfigEntry<bool> SectionOpen = null!;
    internal static bool Ready {get;private set;}
    private static bool started;
    private static Func<bool> shellReady=()=>false;
    private static Action refreshArmour=()=>{};
    private static Func<SprocketExpandedBallistics.ArmourCatalogueSnapshot> snapshot=
        ()=>new(null,null,null,null,null);
    private static readonly string[] Owners={"sprocket.expandedballistics.materials",
        "sprocket.expandedballistics.materials.relikt.spent","sprocket.expandedballistics.materials.relikt.visual",
        "sprocket.expandedballistics.materials.relikt.icon","sprocket.expandedballistics.materials.era.menu",
        "sprocket.expandedballistics.materials.era.parts-tab","sprocket.expandedballistics.materials.era.placement"};
    internal static bool ShellReady=>shellReady();
    internal static SprocketExpandedBallistics.ArmourCatalogueSnapshot ArmourSnapshot=>snapshot();
    internal static void RefreshArmour()=>refreshArmour();
    internal static void Initialize(ManualLogSource logger,Func<bool> ready,
        Action refresh,Func<SprocketExpandedBallistics.ArmourCatalogueSnapshot> current)
    {
        if(started)return;
        ModLog=logger;shellReady=ready;refreshArmour=refresh;snapshot=current;
        started=true;
        try
        {
            var config=new ConfigFile(Path.Combine(Paths.ConfigPath,"sprocket.expandedballistics.materials.cfg"),false);
            SectionOpen=config.Bind("UI","Armour material section open",true,
                "Whether the Armour material section in plate-structure panels starts open.");
            ResponseUi.Reload();MaterialDatabase.Reload();
            var harmony=new Harmony("sprocket.expandedballistics.materials");
            harmony.PatchAll(typeof(MaterialPickerPanel));
            harmony.PatchAll(typeof(RuntimeMaterialBalance));
            Optional("ERA registry",EraPresetRegistry.Start);
            Optional("ERA spent lifecycle",()=>{ReliktSpentVisuals.Start();new Harmony("sprocket.expandedballistics.materials.relikt.spent").PatchAll(typeof(ReliktSpentVisuals));});
            Optional("ERA visual",()=>new Harmony("sprocket.expandedballistics.materials.relikt.visual").PatchAll(typeof(ReliktPrototype)));
            Optional("ERA icon",()=>new Harmony("sprocket.expandedballistics.materials.relikt.icon").PatchAll(typeof(ReliktIcon)));
            Optional("ERA menu",()=>new Harmony("sprocket.expandedballistics.materials.era.menu").PatchAll(typeof(EraMenuPresentation)));
            Optional("Reactive armor tab",()=>new Harmony("sprocket.expandedballistics.materials.era.parts-tab").PatchAll(typeof(EraPartsTab)));
            Optional("ERA placement",()=>new Harmony("sprocket.expandedballistics.materials.era.placement").PatchAll(typeof(EraPlacementDefaults)));
            Ready=true;ModLog.LogInfo($"Material feature module loaded: {MaterialDatabase.Materials.Count} materials.");
        }
        catch(Exception ex){Unload();ModLog.LogError("Material feature module failed to load: "+ex);}
    }
    internal static void Unload()
    {
        Ready=false;ReliktSpentVisuals.Stop();
        foreach(var owner in Owners)new Harmony(owner).UnpatchSelf();
        started=false;shellReady=()=>false;refreshArmour=()=>{};snapshot=()=>new(null,null,null,null,null);
    }
    internal static void Optional(string feature,Action start)
    {try{start();}catch(Exception ex){ModLog.LogWarning(feature+" unavailable: "+ex.Message);}}
}

internal sealed class ArmourMaterial
{
    internal string Id { get; init; } = "";
    internal string Label { get; set; } = "";
    internal string SourceFile { get; init; } = "";
    internal float RhaFactor { get; init; }
    internal float Density { get; init; }
    internal float SpallFactor { get; init; }
    internal float CostMultiplier { get; init; }
    internal MaterialBalanceResult Balance { get; init; }
    internal string MenuFamily { get; init; } = "";
    internal string MenuContract { get; init; } = "";
    internal bool RegisteredMenuAlias { get; init; }
}

internal static class MaterialDatabase
{
    internal static readonly List<ArmourMaterial> Materials = new();
    internal static readonly HashSet<string> RejectedIds = new(StringComparer.Ordinal);
    internal static Il2CppSystem.Collections.Generic.List<string> Labels { get; private set; } = new();

    internal static void Reload()
    {
        Materials.Clear();
        RejectedIds.Clear();
        Labels = new Il2CppSystem.Collections.Generic.List<string>();

        var technologyDir = Path.Combine(
            Paths.GameRootPath,
            "Sprocket_Data",
            "StreamingAssets",
            "Technology");

        if (!Directory.Exists(technologyDir))
            throw new DirectoryNotFoundException($"Technology folder not found: {technologyDir}");

        foreach (var file in Directory.EnumerateFiles(
                     technologyDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc = TechnologyReader.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                if (!TechnologyReader.TryMaterial(root, out var typeElement, out var properties, out var rhaElement))
                    continue;

                var id = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                float Number(JsonElement e) =>
                    e.TryGetSingle(out var f) ? f : (float)e.GetDouble();

                var rha = Number(rhaElement);
                var density = properties.TryGetProperty("density", out var d) ? Number(d) : 0f;
                var spall = properties.TryGetProperty("spallFactor", out var s) ? Number(s) : 0f;
                var cost = properties.TryGetProperty("costMultiplier", out var c) ? Number(c) : 0f;
                var balance = MaterialBalance.Calculate(rha, density, cost);
                if (!balance.IsValid || !float.IsFinite(spall) || spall < 0)
                {
                    RejectedIds.Add(id);
                    MaterialFeature.ModLog.LogWarning($"[Material Balance] Rejected {id}: rhaFactor={rha}, density={density}. " +
                        (balance.IsValid ? "Invalid spall factor." : balance.ValidationError));
                    continue;
                }
                var report = $"[Material Balance] {id}: requestedCost={cost:0.000}, " +
                    $"minimumCost={balance.MinimumCostMultiplier:0.000}, effectiveCost={balance.EffectiveCostMultiplier:0.000}, " +
                    $"thicknessEfficiency={balance.ThicknessEfficiency:0.000}, weightEfficiency={balance.WeightEfficiency:0.000}";
                if (balance.WasAdjusted) if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo(report);
                else MaterialFeature.ModLog.LogDebug(report);

                Materials.Add(new ArmourMaterial
                {
                    Id = id,
                    Label = FriendlyName(Path.GetFileNameWithoutExtension(file)),
                    SourceFile = file,
                    RhaFactor = rha,
                    Density = density,
                    SpallFactor = spall,
                    CostMultiplier = cost,
                    Balance = balance,
                    MenuFamily = MenuFamily(id),
                    MenuContract = MenuContract(id,root),
                    RegisteredMenuAlias = EraGeneratedDefinitions.MaterialRoutes.ContainsKey(id)
                });
            }
            catch (Exception ex)
            {
                MaterialFeature.ModLog.LogWarning(
                    $"Ignoring Technology file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        MaterialEditorRefresh.ApplyLabels(Materials);
        if(MaterialFeature.ArmourSnapshot.Cassettes is {} cassetteModels)
            foreach(var material in Materials)if(cassetteModels.TryGetValue(material.Id,out var cassette))
                MaterialAvailability.CustomLabels[material.Id]=material.Label=cassette.Preset.Label;
        // One entry per technology id. Prefer vanilla/common materials first.
        var deduped = Materials
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        Materials.Clear();
        Materials.AddRange(deduped.Where(m => !RejectedIds.Contains(m.Id)));

        Materials.Sort((a, b) =>
        {
            static int Rank(string id) =>
                id == "rha" ? 0 :
                id == "sheetMetal" ? 1 :
                id == "sand" ? 2 : 3;

            var rank = Rank(a.Id).CompareTo(Rank(b.Id));
            return rank != 0
                ? rank
                : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var material in Materials)
        {
            var details = material.Density > 0
                ? $"{material.Label}  ({material.RhaFactor:0.###}x RHA, {material.Density:0} kg/m\u00B3)"
                : $"{material.Label}  ({material.RhaFactor:0.###}x RHA)";
            Labels.Add(details);
        }

        if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo(
            "Armour materials: " +
            string.Join(", ", Materials.Select(m => $"{m.Label} [{m.Id}]")));
    }

    private static string FriendlyName(string value)
    {
        value = value.Replace('_', ' ').Replace('-', ' ');
        value = Regex.Replace(value, "(?<=[a-z0-9])([A-Z])", " $1");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string MenuFamily(string id)
    {
        var response=ResponseUi.Find(id);
        return MaterialMenuPresentation.Family(id,response?.Kind??"","",FriendlyName(id));
    }
    private static string MenuContract(string id,JsonElement technology)
    {
        var response=ResponseUi.Find(id);
        string Serialized(object? value,params string[] identities)=>value==null?"none":
            MaterialMenuPresentation.Contract(JsonSerializer.SerializeToElement(value,value.GetType()),identities);
        object? rod=null;
        if(response!=null&&MaterialFeature.ArmourSnapshot.Experimental?.Recipes.TryGetValue(response.ResponseId,out var found)==true)rod=found;
        object? reaction=null;
        var reactions=MaterialFeature.ArmourSnapshot.MaterialReactions;
        if(reactions?.Bindings.TryGetValue(id,out var binding)==true)reaction=reactions.Recipes[binding.RecipeId];
        // Dates, native properties, finite-cell geometry, tandem/rod contracts and
        // steel eligibility all participate. A visually similar name is insufficient.
        return MaterialMenuPresentation.Contract(technology,"id","type")+"|"+
            Serialized(response,"ResponseId","CompatibleMaterialIds")+"|"+
            Serialized(rod,"ResponseId")+"|"+Serialized(reaction,"Id","Label")+"|"+
            (reactions?.SteelMaterialIds.Contains(id,StringComparer.Ordinal)==true)+"|"+
            Regex.Replace(id,@"^(experimental)|(?:Cassette|Active|Insert)$","",RegexOptions.IgnoreCase).ToLowerInvariant();
    }

    internal static string MenuLabel(ArmourMaterial material)
    {
        var original=MaterialAvailability.Label(material.Id,material.Label);
        var family=MaterialMenuPresentation.Family(material.Id,ResponseUi.Find(material.Id)?.Kind??"","",original);
        return MaterialMenuPresentation.Label(material.Id,original,family);
    }
    internal static List<ArmourMaterial> MenuItems(IEnumerable<ArmourMaterial> available,string selectedId,bool compositePlate=false,MaterialMenuVisibility.MenuGroup? group=null)
    {
        var bindings=MaterialFeature.ArmourSnapshot.Bindings?.Bindings;
        var source=available.Where(m=>MaterialMenuVisibility.Visible(m.Id,
            bindings?.Any(b=>b.MaterialId==m.Id)==true||
            EraGeneratedDefinitions.Presets.Any(p=>p.PassiveMaterialId==m.Id),compositePlate)&&(group==null||MaterialMenuVisibility.GroupOf(m.Id)==group)).ToList();
        var selected=MaterialMenuPresentation.Select(source.Select(m=>new MaterialMenuPresentation.Entry(m.Id,MenuLabel(m),m.MenuFamily,m.MenuContract,m.RegisteredMenuAlias)),selectedId);
        var ids=selected.Select(m=>m.Id).ToHashSet(StringComparer.Ordinal);
        return source.Where(m=>ids.Contains(m.Id)).OrderBy(m=>m.Id=="rha"?0:m.Id=="sheetMetal"?1:m.Id=="sand"?2:3)
            .ThenBy(MenuLabel,StringComparer.OrdinalIgnoreCase).ToList();
    }
}

internal static class Ui
{
    internal static UnityAction Callback(Action action) =>
        DelegateSupport.ConvertDelegate<UnityAction>(action)!;

    internal static UnityAction<int> IntCallback(Action<int> action) =>
        DelegateSupport.ConvertDelegate<UnityAction<int>>(action)!;

    internal static Il2CppSystem.Action<bool> BoolCallback(Action<bool> action) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(action)!;

    internal static void Guard(string feature, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MaterialFeature.ModLog.LogError($"{feature}: {ex}");
        }
    }
}

[HarmonyPatch]
internal static class MaterialPickerPanel
{
    private static bool loggedOnGuiHit;
    private static bool loggedNoUiDrawer;
    private sealed record PendingMassUpdate(
        PlateStructure Component, string MaterialId, float Density,
        float ArmourMass, float CachedMass, float VehicleMass);
    private static readonly Dictionary<IntPtr, PendingMassUpdate> pendingMassUpdates = new();
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlateStructureEditor), nameof(PlateStructureEditor.OnGUI))]
    private static void Draw(PlateStructureEditor __instance, IGUILayout layout) =>
        Ui.Guard("Armour material", () =>
        {
            if (MaterialDatabase.Materials.Count == 0)
                return;

            if (!loggedOnGuiHit)
            {
                loggedOnGuiHit = true;
                if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo("PlateStructureEditor.OnGUI hook reached.");
            }

            var component = __instance.Component;
            if (component == null)
                return;

            var ui = layout.TryCast<IGUIElementDrawer>();
            if (ui == null)
            {
                if (!loggedNoUiDrawer)
                {
                    loggedNoUiDrawer = true;
                    MaterialFeature.ModLog.LogWarning("PlateStructureEditor.OnGUI received an IGUILayout that cannot be cast to IGUIElementDrawer.");
                }
                return;
            }

            // QoL uses this same pattern: finish the game's current foldout,
            // then add a native Sprocket section to the inspector.
            layout.EndAllDropdowns();
            layout.BeginDropdown(
                "Armour material",
                MaterialFeature.SectionOpen.Value,
                Ui.BoolCallback(open => MaterialFeature.SectionOpen.Value = open));

            try
            {
                var currentId = ReadArmourTechId(component) ?? "rha";
                ResponseUi.DescribeAvailability(ui, component);
                var composite=SprocketExpandedBallistics.RuntimeCompositePlate.Is(component);
                var candidates = MaterialDatabase.Materials.Where(m =>
                    SprocketExpandedBallistics.CompositePlate.IsCassette(m.Id)||ResponseUi.EraAllowed(component, m.Id)).ToList();
                var visible = MaterialDatabase.MenuItems(candidates,currentId,composite);
                var tooltipMaterial = MaterialDatabase.Materials.FirstOrDefault(m => m.Id == currentId);
                var tooltipLabel = tooltipMaterial==null?currentId:MaterialDatabase.MenuLabel(tooltipMaterial);
                var labels = new Il2CppSystem.Collections.Generic.List<string>();
                var selectedIndex = visible.FindIndex(
                    m => string.Equals(m.Id, currentId, StringComparison.Ordinal));
                var hasPrompt=selectedIndex<0;
                if(hasPrompt){labels.Add(MaterialMenuVisibility.CurrentPrompt(currentId,tooltipLabel));selectedIndex=0;}
                foreach (var material in visible)
                {
                    var label=MaterialDatabase.MenuLabel(material) + " (" + ResponseUi.NativeDensityLabel(component, material.Id) + ")";
                    if(!SprocketExpandedBallistics.RuntimeCassetteComposer.CanApply(component,material.Id,out var reason))label+=" - "+reason;
                    labels.Add(label);
                }

                var materialTooltip = ResponseUi.Tooltip(component, currentId, tooltipLabel);
                ui.Dropdown(
                    "Material",
                    labels.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(),
                    selectedIndex,
                    Ui.IntCallback(index =>
                    {
                        Ui.Guard("Set armour material", () =>
                        {
                            var materialId = MaterialMenuVisibility.SelectedId(visible.Select(m=>m.Id).ToList(),index,hasPrompt);
                            if (materialId==null) return;
                            if (!ResponseUi.EraAllowed(component, materialId)) return;
                            if(!SprocketExpandedBallistics.RuntimeCassetteComposer.CanApply(component,materialId,out var blocker))throw new InvalidOperationException(blocker);
                            if(composite)
                            {
                                double? depth=null;
                                if(SprocketExpandedBallistics.CompositePlate.IsCassette(materialId))
                                {
                                    if(MaterialFeature.ArmourSnapshot.Cassettes is not {} models||!models.TryGetValue(materialId,out var cassette))throw new InvalidOperationException("Cassette preset unavailable.");
                                    depth=cassette.DepthMm;
                                }
                                if(!SprocketExpandedBallistics.RuntimeCompositePlateEdit.TryApply(__instance,materialId,depth,out var message))throw new InvalidOperationException(message);
                                __instance.RequestRedraw();return;
                            }
                            ApplyMaterial(component, MaterialDatabase.Materials.FindIndex(m => m.Id == materialId));

                            // Like QoL's inspector features: explicitly request a redraw,
                            // otherwise the old text can remain on screen.
                            __instance.RequestRedraw();
                        });
                    }),
                    materialTooltip);
                var editTip=new UITooltip("Material editor", "Create and edit materials, special behavior and protection settings. Opens the UI Kit editor; the JSON Editor stays available behind [Material editor] Use UI Kit = false.");
                ui.Button("Edit materials",Ui.Callback(()=>Ui.Guard("Edit materials",()=>MaterialEditorPanel.OpenOrFallback(__instance,()=>{MaterialEditorRefresh.Reload(component);__instance.RequestRedraw();}))),ref editTip);
                var reloadTip=new UITooltip("Reload armour materials","Reload armour materials for this vehicle. New cassette Technology files may require restarting Sprocket.");
                ui.Button("Reload armour materials",Ui.Callback(()=>Ui.Guard("Reload armour materials",()=>{MaterialEditorRefresh.Reload(component);__instance.RequestRedraw();})),ref reloadTip);
                // 0.22.14: "Open composer" and the composite plate status live here; the separate "Composite armour plate" block is gone.
                Ui.Guard("Composite armour plate",()=>SprocketExpandedBallistics.RuntimeCompositePlate.DrawInMaterialBlock(__instance,ui));
                ui.InfoField("Protection", materialTooltip, 1, UnityEngine.Color.white);
                if(hasPrompt)ui.InfoField("Current material: "+tooltipLabel+(MaterialMenuVisibility.ComposerOnly(currentId)?" — Assigned by the Cassette Composer":". Select a player material to replace it."),1);

                var current = tooltipMaterial;

                if (current?.Id == currentId)
                {
                    // The active native component recipe is authoritative, not an
                    // arbitrary duplicate file discovered for this technology ID.
                    var nativeRequest = ResponseUi.NativeRequestedCost(component, currentId);
                    var requested = ArmourResponses.RequestedPrice(nativeRequest ?? component.armourCostMultiplier,
                        ResponseUi.Find(currentId));
                    var active = MaterialBalance.Calculate(component.damageModelParameters.RhaFactor,
                        component.armourDensity, requested);
                    ui.InfoField($"{component.damageModelParameters.RhaFactor:0.###}x RHA | " +
                        $"{component.armourDensity:0} kg/m\u00B3", 2);
                    if (active.IsValid) ui.InfoField($"Weight efficiency: {active.WeightEfficiency:0.##}x | " +
                        $"Cost: {component.armourCostMultiplier:0.00}x", 2);
                    if (nativeRequest != null && active.IsValid && active.WasAdjusted)
                        ui.InfoField($"Requested: {requested:0.00}x | Balanced minimum applied", 2);
                }
                else ui.InfoField("This material is unavailable for the vehicle's current era.", 1);

                ResponseUi.Describe(ui, component);


            }
            finally
            {
                // Several native editors can share one inspector layout.
                // Never leave our foldout open for the next editor.
                layout.EndAllDropdowns();
            }
        });

    private static string? ReadArmourTechId(PlateStructure component) =>
        component.armourTechID;

    private static void ApplyMaterial(PlateStructure component, int index)
    {
        if (index < 0 || index >= MaterialDatabase.Materials.Count)
            return;

        var material = MaterialDatabase.Materials[index];
        var blueprint = component.Blueprint;
        if (blueprint == null)
            throw new InvalidOperationException("Plate structure has no blueprint.");

        var oldDensity = component.armourDensity;
        var oldArmourMass = component.GetMass(MassType.Armour);
        var oldCachedMass = component.CachedMass;
        var oldVehicleMass = component.Vehicle.Mass;

        // Build reads this blueprint field; changing only the runtime technology
        // would let the next vanilla build restore the previous material.
        blueprint.armourTechID = material.Id;
        component.SetArmourMaterial(material.Id);
        component.blueprintRef.MarkModified();
        pendingMassUpdates[component.Pointer] = new PendingMassUpdate(
            component, material.Id, component.armourDensity,
            oldArmourMass, oldCachedMass, oldVehicleMass);

        // Confirmed vanilla route: OnAssignedMeshModified calls RequestRebuild,
        // which marks this component and raises PartConfiguration on its gateway.
        // The game's scheduled build updates armour mass, cached mass, cost and COM.
        component.RequestRebuild();

        if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo(
            $"Armour material -> {material.Label} [{material.Id}] | " +
            $"density {oldDensity:0.###} -> {component.armourDensity:0.###} kg/m\u00B3 | " +
            $"before build: armour mass {oldArmourMass:0.###}, " +
            $"CachedMass {oldCachedMass:0.###}, Vehicle.Mass {oldVehicleMass:0.###} | " +
            "vanilla rebuild queued (PartConfiguration).");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Vehicle), nameof(Vehicle.Build))]
    private static void AfterVehicleBuild(Vehicle __instance) =>
        Ui.Guard("Verify armour mass after vanilla build", () =>
        {
            foreach (var item in pendingMassUpdates.ToArray())
            {
                var before = item.Value;
                var component = before.Component;
                if (component.Vehicle.Pointer != __instance.Pointer || component.RequiresRebuild)
                    continue;

                pendingMassUpdates.Remove(item.Key);
                var armourMass = component.GetMass(MassType.Armour);
                var cachedMass = component.CachedMass;
                if(SprocketExpandedBallistics.ReleaseDiagnostics.Enabled)MaterialFeature.ModLog.LogInfo(
                    $"After vanilla build [{component.armourTechID}] | " +
                    $"armour mass {before.ArmourMass:0.###} -> {armourMass:0.###} | " +
                    $"CachedMass {before.CachedMass:0.###} -> {cachedMass:0.###} | " +
                    $"Vehicle.Mass {before.VehicleMass:0.###} -> {__instance.Mass:0.###} | " +
                    $"effectiveCostMultiplier={component.armourCostMultiplier:0.000}, " +
                    $"armourMaterialCost={component.GetCost(MassType.Armour, CostType.Material):0.###}");

                if (component.armourTechID != before.MaterialId ||
                    Math.Abs(component.armourDensity - before.Density) > 0.01f ||
                    Math.Abs(cachedMass - component.GetMass(MassType.Everything)) > 0.01f)
                    MaterialFeature.ModLog.LogWarning("Vanilla build did not retain the selected material or synchronize CachedMass.");
            }
        });
}


















