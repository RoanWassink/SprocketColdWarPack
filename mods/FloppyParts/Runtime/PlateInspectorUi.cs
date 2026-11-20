using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.UI;
using Sprocket.Vehicles.PlateStructures;
using Sprocket.Vehicles.PlateStructures.Design;
using UnityEngine;
using UnityEngine.Events;

namespace SprocketFloppyParts;

[HarmonyPatch]
internal static class PlateInspectorUi
{
    private static readonly LookPreset[] LookPresets =
    {
        LookPreset.Native,
        LookPreset.Canvas,
        LookPreset.CamoNet,
        LookPreset.Rubber,
        LookPreset.Wood,
        LookPreset.Glass,
        LookPreset.Custom
    };

    private static readonly string[] LookLabels =
    {
        "Native Armour",
        "Canvas",
        "Camo Net",
        "Rubber",
        "Wood",
        "Glass",
        "Custom Texture"
    };

    private static readonly ColorMode[] ColorModes =
    {
        ColorMode.FollowTankPaint,
        ColorMode.Preset,
        ColorMode.CustomHex
    };

    private static readonly string[] ColorModeLabels =
    {
        "Follow Tank Paint",
        "Preset",
        "Custom Hex"
    };

    private static readonly ColorPresetId[] ColorPresets =
    {
        ColorPresetId.Natural,
        ColorPresetId.OliveDrab,
        ColorPresetId.RussianGreen,
        ColorPresetId.Khaki,
        ColorPresetId.DesertSand,
        ColorPresetId.WinterWhite,
        ColorPresetId.FieldGrey,
        ColorPresetId.Charcoal,
        ColorPresetId.RubberBlack,
        ColorPresetId.ClearGlass,
        ColorPresetId.TintedGlass
    };

    private static readonly string[] ColorPresetLabels =
    {
        "Natural",
        "Olive Drab",
        "Russian Green",
        "Khaki",
        "Desert Sand",
        "Winter White",
        "Field Grey",
        "Charcoal",
        "Rubber Black",
        "Clear Glass",
        "Tinted Glass"
    };

    private static readonly MotionProfile[] MotionProfiles =
    {
        MotionProfile.Static,
        MotionProfile.Cloth,
        MotionProfile.Rubber,
        MotionProfile.Hinged
    };

    private static readonly string[] MotionLabels =
    {
        "Static",
        "Cloth",
        "Rubber",
        "Hinged"
    };

    private static readonly PinMode[] PinModes =
    {
        PinMode.Auto,
        PinMode.TopEdge,
        PinMode.BottomEdge,
        PinMode.Border,
        PinMode.Corners,
        PinMode.TopAndBottom,
        PinMode.LeftAndRight
    };

    private static readonly string[] PinModeLabels =
    {
        "Auto",
        "Top Edge",
        "Bottom Edge",
        "Border",
        "Corners",
        "Top & Bottom",
        "Left & Right"
    };

    private static Il2CppSystem.Collections.Generic.List<string>? lookList;
    private static Il2CppSystem.Collections.Generic.List<string>? modeList;
    private static Il2CppSystem.Collections.Generic.List<string>? presetList;
    private static Il2CppSystem.Collections.Generic.List<string>? motionList;
    private static Il2CppSystem.Collections.Generic.List<string>? pinModeList;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlateStructureEditor), nameof(PlateStructureEditor.OnGUI))]
    private static void Draw(PlateStructureEditor __instance, IGUILayout layout)
    {
        try
        {
            var component = __instance.Component;
            if (component == null) return;

            var ui = layout.TryCast<IGUIElementDrawer>();
            if (ui == null) return;

            layout.EndAllDropdowns();
            layout.BeginDropdown(
                "Floppy Parts",
                Plugin.SectionOpen.Value,
                BoolCallback(open => Plugin.SectionOpen.Value = open));

            try
            {
                if (Plugin.IsExpandedBallisticsLoaded() && IsEbMaterialLook(component.armourTechID))
                {
                    ui.InfoField(
                        $"Expanded Ballistics look ({component.armourTechID}) active; Floppy Parts overrides visuals when non-Native.",
                        2,
                        new Color(1f, 0.85f, 0.40f, 1f));
                }

                void UpdateField(Action<PlateLookConfig> mutate, bool geometryChanged, bool redrawUi)
                {
                    var next = PlateConfigStore.Get(component).Clone();
                    mutate(next);
                    CommitChange(__instance, component, next, geometryChanged, redrawUi);
                }

                var cfg = PlateConfigStore.Get(component);
                int lookIdx = Math.Max(0, Array.IndexOf(LookPresets, cfg.Preset));

                ui.Dropdown(
                    "Look",
                    ToIl2CppList(ref lookList, LookLabels),
                    lookIdx,
                    IntCallback(idx =>
                    {
                        if (idx < 0 || idx >= LookPresets.Length) return;
                        var cur = PlateConfigStore.Get(component);
                        var nextPreset = LookPresets[idx];
                        if (nextPreset == cur.Preset) return;
                        var next = PlateLookConfig.DefaultsFor(nextPreset);
                        if (nextPreset == LookPreset.Custom && !string.IsNullOrEmpty(cur.CustomFolder))
                            next.CustomFolder = cur.CustomFolder;
                        // Preserve existing motion settings when switching surface look.
                        next.Motion = cur.Motion;
                        next.PinMode = cur.PinMode;
                        next.Stiffness = cur.Stiffness;
                        next.Damping = cur.Damping;
                        next.WindScale = cur.WindScale;
                        next.PinRadiusCm = cur.PinRadiusCm;
                        next.MaxSwingDeg = cur.MaxSwingDeg;
                        CommitChange(__instance, component, next, geometryChanged: true, redrawUi: true);
                    }),
                    "Visual surface look for this plate (visual overlay only; armour protection stays native).");

                if (cfg.Preset != LookPreset.Native)
                {
                    int modeIdx = Math.Max(0, Array.IndexOf(ColorModes, cfg.ColorMode));
                    ui.Dropdown(
                        "Color mode",
                        ToIl2CppList(ref modeList, ColorModeLabels),
                        modeIdx,
                        IntCallback(idx =>
                        {
                            if (idx < 0 || idx >= ColorModes.Length) return;
                            UpdateField(c => c.ColorMode = ColorModes[idx], geometryChanged: false, redrawUi: true);
                        }),
                        "Choose whether tint follows the tank paint, a colour preset, or a custom hex code.");

                    if (cfg.ColorMode == ColorMode.Preset)
                    {
                        int presetIdx = Math.Max(0, Array.IndexOf(ColorPresets, cfg.ColorPreset));
                        ui.Dropdown(
                            "Color preset",
                            ToIl2CppList(ref presetList, ColorPresetLabels),
                            presetIdx,
                            IntCallback(idx =>
                            {
                                if (idx < 0 || idx >= ColorPresets.Length) return;
                                UpdateField(c => c.ColorPreset = ColorPresets[idx], geometryChanged: false, redrawUi: true);
                            }),
                            "Preset dye or material tint.");
                    }
                    else if (cfg.ColorMode == ColorMode.CustomHex)
                    {
                        ui.InputField(
                            "Hex color",
                            cfg.CustomHex,
                            StringCallback(value =>
                                UpdateField(c => c.CustomHex = value, geometryChanged: false, redrawUi: true)));

                        bool validHex = ColorMath.TryParseHex(cfg.CustomHex, out float r, out float g, out float b);
                        var swatch = validHex ? new Color(r, g, b, 1f) : new Color(0.85f, 0.25f, 0.25f, 1f);
                        ui.InfoField(validHex ? $"Tint {cfg.CustomHex}" : "Invalid hex (expected #RRGGBB)", 1, swatch);
                    }

                    ui.Slider(
                        "Brightness",
                        value: cfg.Brightness,
                        min: 0.25f,
                        max: 2.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.Brightness = v, geometryChanged: false, redrawUi: false)));

                    ui.Slider(
                        "Tile scale (m)",
                        value: cfg.TileSizeM,
                        min: 0.05f,
                        max: 5.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.TileSizeM = v, geometryChanged: true, redrawUi: false)));

                    ui.Slider(
                        "UV rotation (deg)",
                        value: cfg.UvRotationDeg,
                        min: 0f,
                        max: 360f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.UvRotationDeg = v, geometryChanged: true, redrawUi: false)));

                    ui.Slider(
                        "Smoothness",
                        value: cfg.Smoothness,
                        min: 0f,
                        max: 1.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.Smoothness = v, geometryChanged: false, redrawUi: false)));

                    string opacityLabel = cfg.Preset == LookPreset.CamoNet ? "Cutout" : "Opacity";
                    ui.Slider(
                        opacityLabel,
                        value: cfg.Opacity,
                        min: 0.05f,
                        max: 1.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.Opacity = v, geometryChanged: false, redrawUi: false)));

                    if (cfg.Preset == LookPreset.Custom)
                    {
                        ui.InputField(
                            "Custom folder",
                            cfg.CustomFolder,
                            StringCallback(value =>
                                UpdateField(c => c.CustomFolder = value, geometryChanged: false, redrawUi: true)));
                    }
                }

                // Motion controls (available even when Look == Native Armour).
                int motionIdx = Math.Max(0, Array.IndexOf(MotionProfiles, cfg.Motion));
                ui.Dropdown(
                    "Motion",
                    ToIl2CppList(ref motionList, MotionLabels),
                    motionIdx,
                    IntCallback(idx =>
                    {
                        if (idx < 0 || idx >= MotionProfiles.Length) return;
                        var cur = PlateConfigStore.Get(component);
                        var nextMotion = MotionProfiles[idx];
                        if (nextMotion == cur.Motion) return;
                        var next = cur.Clone();
                        next.ApplyMotionDefaults(nextMotion);
                        CommitChange(__instance, component, next, geometryChanged: true, redrawUi: true);
                    }),
                    "Play-mode physical simulation profile (Cloth, Rubber soft-body, or Hinged skirt).");

                if (cfg.Motion != MotionProfile.Static)
                {
                    int pinIdx = Math.Max(0, Array.IndexOf(PinModes, cfg.PinMode));
                    ui.Dropdown(
                        "Pin mode",
                        ToIl2CppList(ref pinModeList, PinModeLabels),
                        pinIdx,
                        IntCallback(idx =>
                        {
                            if (idx < 0 || idx >= PinModes.Length) return;
                            UpdateField(c => c.PinMode = PinModes[idx], geometryChanged: true, redrawUi: true);
                        }),
                        "How vertices are anchored to rigid vehicle parts or plate edges.");

                    ui.Slider(
                        "Stiffness",
                        value: cfg.Stiffness,
                        min: 0.05f,
                        max: 1.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.Stiffness = v, geometryChanged: false, redrawUi: false)));

                    ui.Slider(
                        "Damping",
                        value: cfg.Damping,
                        min: 0.02f,
                        max: 0.95f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.Damping = v, geometryChanged: false, redrawUi: false)));

                    ui.Slider(
                        "Wind / Flutter",
                        value: cfg.WindScale,
                        min: 0.0f,
                        max: 2.0f,
                        onValueChange: FloatCallback(v =>
                            UpdateField(c => c.WindScale = v, geometryChanged: false, redrawUi: false)));

                    if (cfg.PinMode == PinMode.Auto)
                    {
                        ui.Slider(
                            "Pin radius (cm)",
                            value: cfg.PinRadiusCm,
                            min: 1.0f,
                            max: 15.0f,
                            onValueChange: FloatCallback(v =>
                                UpdateField(c => c.PinRadiusCm = v, geometryChanged: true, redrawUi: false)));
                    }

                    if (cfg.Motion == MotionProfile.Hinged)
                    {
                        ui.Slider(
                            "Max swing (deg)",
                            value: cfg.MaxSwingDeg,
                            min: 5.0f,
                            max: 75.0f,
                            onValueChange: FloatCallback(v =>
                                UpdateField(c => c.MaxSwingDeg = v, geometryChanged: false, redrawUi: false)));
                    }

                    ui.ToggleField(
                        "Pin preview",
                        FloppyMotionController.PinPreviewEnabled,
                        BoolCallback(show =>
                        {
                            FloppyMotionController.PinPreviewEnabled = show;
                            __instance.RequestRedraw();
                        }),
                        "Highlight pinned vertices (red) vs free vertices (green) in the Designer.");

                    ui.InfoField(FloppyMotionController.Describe(component), 2);
                }

                if (!cfg.IsDefault)
                {
                    var resetTip = new UITooltip("Reset to Native", "Restore native Sprocket plate appearance and static rigidity.");
                    ui.Button(
                        "Reset to Native",
                        VoidCallback(() =>
                        {
                            CommitChange(
                                __instance,
                                component,
                                PlateLookConfig.DefaultsFor(LookPreset.Native),
                                geometryChanged: true,
                                redrawUi: true);
                        }),
                        ref resetTip);

                    if (cfg.Preset != LookPreset.Native)
                    {
                        var reloadTip = new UITooltip("Reload textures", "Reload custom PNG textures from the plugin textures folder.");
                        ui.Button(
                            "Reload textures",
                            VoidCallback(() =>
                            {
                                MaterialLibrary.ReloadCustomTextures();
                                FloppyOverlayRuntime.RefreshMaterials();
                                __instance.RequestRedraw();
                            }),
                            ref reloadTip);
                    }
                }
            }
            finally
            {
                layout.EndAllDropdowns();
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[Inspector] OnGUI failed: {ex.Message}");
        }
    }

    private static void CommitChange(
        PlateStructureEditor editor,
        PlateStructure component,
        PlateLookConfig next,
        bool geometryChanged,
        bool redrawUi)
    {
        next.Sanitize();
        if (next.IsDefault)
            PlateConfigStore.Remove(component);
        else
            PlateConfigStore.Set(component, next);

        component.blueprintRef?.MarkModified();
        FloppyOverlayRuntime.NotifyConfigChanged(component, geometryChanged);
        if (redrawUi)
            editor.RequestRedraw();
    }

    private static bool IsEbMaterialLook(string? id) =>
        id is "ebWood" or "ebRubber" or "ebFabric" or "ebGlass";

    private static Il2CppSystem.Collections.Generic.IReadOnlyList<string> ToIl2CppList(
        ref Il2CppSystem.Collections.Generic.List<string>? cache,
        string[] items)
    {
        if (cache == null)
        {
            cache = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (string item in items)
                cache.Add(item);
        }
        return cache.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>();
    }

    private static UnityAction VoidCallback(Action action) =>
        DelegateSupport.ConvertDelegate<UnityAction>(action)!;

    private static UnityAction<int> IntCallback(Action<int> action) =>
        DelegateSupport.ConvertDelegate<UnityAction<int>>(action)!;

    private static UnityAction<string> StringCallback(Action<string> action) =>
        DelegateSupport.ConvertDelegate<UnityAction<string>>(action)!;

    private static Il2CppSystem.Action<bool> BoolCallback(Action<bool> action) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(action)!;

    private static Il2CppSystem.Action<float> FloatCallback(Action<float> action) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<float>>(action)!;
}
