using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace SprocketFloppyParts;

internal static class MaterialLibrary
{
    private sealed class CustomFolderSettings
    {
        public string normalConvention { get; set; } = "OpenGL";
        public float normalStrength { get; set; } = 1.0f;
        public float smoothness { get; set; } = 0.2f;
        internal bool DirectX => string.Equals(normalConvention, "DirectX", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MaterialEntry(Material Material, bool HasMaskMap, bool TransparentGlass, bool HasCustomBaseMap);

    private static readonly Dictionary<(LookPreset Preset, string Folder), MaterialEntry> cache = new();
    private static readonly List<Object> ownedObjects = new();
    private static readonly HashSet<string> warnings = new(StringComparer.Ordinal);
    private static Texture2D? thermalRainbowTex;
    private static string texturesRoot = "";

    internal static int Generation { get; private set; } = 1;

    internal static void Initialize(string pluginDir)
    {
        texturesRoot = Path.Combine(pluginDir, "textures");
    }

    internal static Material? GetMaterial(LookPreset preset, string customFolder, out bool hasMaskMap)
    {
        hasMaskMap = false;
        if (preset == LookPreset.Native) return null;

        string folder = preset == LookPreset.Custom ? PlateLookConfig.SanitizeFolder(customFolder) : "";
        var key = (preset, folder);
        if (cache.TryGetValue(key, out var hit))
        {
            if (hit.Material != null)
            {
                hasMaskMap = hit.HasMaskMap;
                return hit.Material;
            }
            cache.Remove(key);
        }

        try
        {
            var entry = BuildMaterial(preset, folder);
            cache[key] = entry;
            hasMaskMap = entry.HasMaskMap;
            return entry.Material;
        }
        catch (Exception ex)
        {
            Warn($"mat:{preset}:{folder}", $"Failed to build {preset} material: {ex.Message}");
            return null;
        }
    }

    internal static void ReloadCustomTextures()
    {
        DestroyCachedMaterials();
        Generation++;
        if (Plugin.Detailed)
            Plugin.ModLog.LogInfo($"[Materials] Reloaded texture cache (generation {Generation}).");
    }

    internal static Material CreateInstanceMaterial(Material template) => new(template)
    {
        hideFlags = HideFlags.HideAndDontSave,
        name = $"{template.name} (Inst)"
    };

    internal static void ApplyMaterialProperties(
        Material mat,
        PlateLookConfig cfg,
        (float R, float G, float B, float Saturation)? tankPaint,
        bool hasMaskMap)
    {
        if (mat == null) return;

        if (cfg.Preset == LookPreset.Glass)
        {
            var (gr, gg, gb, ga) = ColorMath.ResolveTint(cfg, tankPaint);
            var glassColor = new Color(gr, gg, gb, ga);
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", glassColor);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", glassColor);

            bool isRainbow = cfg.ColorMode == ColorMode.Preset && cfg.ColorPreset == ColorPresetId.ThermalRainbowGlass;
            bool hasCustomGlassMap = cache.TryGetValue((LookPreset.Glass, ""), out var glassEntry) && glassEntry.HasCustomBaseMap;
            string mapProp = mat.HasProperty("_UnlitColorMap") ? "_UnlitColorMap" : "_BaseColorMap";
            try
            {
                if (isRainbow)
                {
                    var rainbowTex = GetOrCreateThermalRainbowTexture();
                    if (rainbowTex != null && mat.HasProperty(mapProp))
                    {
                        AssignTexture(mat, mapProp, rainbowTex);
                        if (mapProp == "_UnlitColorMap")
                            mat.EnableKeyword("_UNLIT_COLOR_MAP");
                    }
                }
                else if (!hasCustomGlassMap)
                {
                    if (mat.HasProperty(mapProp))
                        mat.SetTexture(mapProp, null);
                    if (mapProp == "_UnlitColorMap")
                        mat.DisableKeyword("_UNLIT_COLOR_MAP");
                }
                else if (glassEntry?.Material != null && glassEntry.Material.HasProperty(mapProp))
                {
                    var customTex = glassEntry.Material.GetTexture(mapProp);
                    mat.SetTexture(mapProp, customTex);
                    if (mapProp == "_UnlitColorMap")
                    {
                        if (customTex != null) mat.EnableKeyword("_UNLIT_COLOR_MAP");
                        else mat.DisableKeyword("_UNLIT_COLOR_MAP");
                    }
                }
            }
            catch (Exception ex)
            {
                Warn("glass-texture", ex.Message);
            }
            return;
        }

        var (r, g, b, a) = ColorMath.ResolveTint(cfg, tankPaint);
        mat.SetColor("_BaseColor", new Color(r, g, b, a));

        if (!hasMaskMap && mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", Math.Clamp(cfg.Smoothness, 0f, 1f));

        if (cfg.Preset == LookPreset.CamoNet && mat.HasProperty("_AlphaCutoff"))
            mat.SetFloat("_AlphaCutoff", Math.Clamp(cfg.Opacity, 0.05f, 1f));
    }

    private static Texture2D? GetOrCreateThermalRainbowTexture()
    {
        if (thermalRainbowTex != null) return thermalRainbowTex;
        thermalRainbowTex = CreateTexture(
            ProceduralTextureData.GenerateThermalRainbowRgba(),
            128,
            linear: false,
            "Floppy Thermal Rainbow");
        return thermalRainbowTex;
    }

    internal static void Clear()
    {
        DestroyCachedMaterials();
        warnings.Clear();
    }

    private static void DestroyCachedMaterials()
    {
        foreach (var entry in cache.Values)
        {
            if (entry.Material != null)
                Object.DestroyImmediate(entry.Material);
        }
        cache.Clear();

        for (int i = ownedObjects.Count - 1; i >= 0; i--)
        {
            if (ownedObjects[i] != null)
                Object.DestroyImmediate(ownedObjects[i]);
        }
        ownedObjects.Clear();
        thermalRainbowTex = null;
    }

    private static MaterialEntry BuildMaterial(LookPreset preset, string customFolder)
    {
        if (preset == LookPreset.Glass)
            return BuildGlassMaterial();

        var shader = Shader.Find("HDRP/Lit");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("HDRP/Lit shader unavailable.");

        var mat = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = $"FloppyParts {preset}" + (string.IsNullOrEmpty(customFolder) ? "" : $" ({customFolder})")
        };

        bool hasMaskMap = false;
        bool transparentGlass = false;

        try
        {
            void SetFloat(string prop, float value)
            {
                if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
            }

            SetFloat("_Metallic", 0f);
            SetFloat("_Smoothness", PlateLookConfig.DefaultsFor(preset).Smoothness);
            SetFloat("_CullMode", 0f);
            SetFloat("_CullModeForward", 0f);
            SetFloat("_TransparentCullMode", 0f);
            SetFloat("_DoubleSidedEnable", 1f);
            SetFloat("_DoubleSidedNormalMode", 1f);
            SetFloat("_UVBase", 0f);
            SetFloat("_NormalMapSpace", 0f);
            mat.SetVector("_DoubleSidedConstants", new Vector4(1f, 1f, -1f, 0f));
            mat.SetVector("_UVMappingMask", new Vector4(1f, 0f, 0f, 0f));
            mat.SetColor("_BaseColor", Color.white);

            string? folderPath = ResolveFolderPath(preset, customFolder);
            bool loadedFolder = folderPath != null && TryApplyFolderTextures(mat, folderPath, ref hasMaskMap);

            if (!loadedFolder && preset != LookPreset.Glass)
            {
                ProceduralTextureSet? proc = preset switch
                {
                    LookPreset.Canvas => ProceduralTextureData.GenerateCanvas(),
                    LookPreset.CamoNet => ProceduralTextureData.GenerateCamoNet(),
                    LookPreset.Rubber => ProceduralTextureData.GenerateRubber(),
                    LookPreset.Wood => ProceduralTextureData.GenerateWood(),
                    LookPreset.Custom => ProceduralTextureData.GenerateCanvas(),
                    _ => null
                };

                if (proc != null)
                {
                    var albedo = CreateTexture(proc.AlbedoRgba, proc.Size, linear: false, $"Floppy {preset} Albedo");
                    var normal = CreateTexture(proc.NormalRgba, proc.Size, linear: true, $"Floppy {preset} Normal");
                    AssignTexture(mat, "_BaseColorMap", albedo);
                    AssignTexture(mat, "_NormalMap", normal);
                    SetFloat("_NormalScale", 1f);
                }
            }

            if (preset == LookPreset.CamoNet)
            {
                HDMaterial.SetAlphaClipping(mat, true);
                HDMaterial.SetAlphaCutoff(mat, 0.45f);
                SetFloat("_CullMode", 0f);
                SetFloat("_DoubleSidedEnable", 1f);
                if (!HDMaterial.ValidateMaterial(mat))
                    throw new InvalidOperationException("HDRP CamoNet material validation failed.");
            }
            else
            {
                if (!HDMaterial.ValidateMaterial(mat))
                    throw new InvalidOperationException($"HDRP validation failed for {preset}.");
            }

            if (Plugin.Detailed)
                Plugin.ModLog.LogInfo($"[Materials] Built {preset}" +
                    (loadedFolder ? $" from {folderPath}" : " (procedural)") + ".");

            return new MaterialEntry(mat, hasMaskMap, transparentGlass, loadedFolder);
        }
        catch
        {
            Object.DestroyImmediate(mat);
            throw;
        }
    }

    private static MaterialEntry BuildGlassMaterial()
    {
        // HDRP/Unlit with SrcAlpha / OneMinusSrcAlpha gives true light transmission in Sprocket
        // across all sun angles, and responds 1:1 to _UnlitColor RGB and Opacity (0.01..1.00).
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("HDRP/Lit");
        if (shader == null || !shader.isSupported)
            throw new InvalidOperationException("HDRP/Unlit glass shader unavailable.");

        var mat = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "FloppyParts Glass"
        };

        try
        {
            void SetFloat(string prop, float value)
            {
                if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
            }

            var initColor = new Color(0.90f, 0.96f, 1.00f, 0.08f);
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", initColor);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", initColor);

            SetFloat("_SurfaceType", 1f);
            SetFloat("_BlendMode", 0f);
            SetFloat("_ZWrite", 0f);
            SetFloat("_TransparentZWrite", 0f);
            SetFloat("_SrcBlend", 5f);       // SrcAlpha
            SetFloat("_DstBlend", 10f);      // OneMinusSrcAlpha
            SetFloat("_AlphaSrcBlend", 1f);  // One
            SetFloat("_AlphaDstBlend", 10f); // OneMinusSrcAlpha
            SetFloat("_DoubleSidedEnable", 0f);
            SetFloat("_CullMode", 2f);
            SetFloat("_CullModeForward", 2f);
            SetFloat("_TransparentCullMode", 2f);
            SetFloat("_EnableFogOnTransparent", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;

            bool loadedFolder = false;
            string? folderPath = ResolveFolderPath(LookPreset.Glass, "");
            if (folderPath != null)
            {
                try
                {
                    var albedo = LoadPng(Path.Combine(folderPath, "albedo.png"), linear: false);
                    ownedObjects.Add(albedo);
                    string mapProp = mat.HasProperty("_UnlitColorMap") ? "_UnlitColorMap" : "_BaseColorMap";
                    if (mat.HasProperty(mapProp))
                    {
                        AssignTexture(mat, mapProp, albedo);
                        if (mapProp == "_UnlitColorMap")
                            mat.EnableKeyword("_UNLIT_COLOR_MAP");
                        loadedFolder = true;
                    }
                }
                catch (Exception ex)
                {
                    Warn("glass-custom-tex", ex.Message);
                }
            }

            if (Plugin.Detailed)
            {
                float surf = mat.HasProperty("_SurfaceType") ? mat.GetFloat("_SurfaceType") : -1f;
                float sb = mat.HasProperty("_SrcBlend") ? mat.GetFloat("_SrcBlend") : -1f;
                float db = mat.HasProperty("_DstBlend") ? mat.GetFloat("_DstBlend") : -1f;
                Plugin.ModLog.LogInfo(
                    $"[Materials] Built Glass ({shader.name}) [transparent]: surface={surf} src={sb} dst={db} queue={mat.renderQueue} kw={mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")}.");
            }

            return new MaterialEntry(mat, HasMaskMap: false, TransparentGlass: true, HasCustomBaseMap: loadedFolder);
        }
        catch
        {
            Object.DestroyImmediate(mat);
            throw;
        }
    }

    private static string? ResolveFolderPath(LookPreset preset, string customFolder)
    {
        if (string.IsNullOrEmpty(texturesRoot)) return null;
        string? sub = preset switch
        {
            LookPreset.Canvas => "canvas",
            LookPreset.CamoNet => "camonet",
            LookPreset.Rubber => "rubber",
            LookPreset.Wood => "wood",
            LookPreset.Glass => "glass",
            LookPreset.Custom => string.IsNullOrEmpty(customFolder) ? null : customFolder,
            _ => null
        };
        if (sub == null) return null;
        string full = Path.Combine(texturesRoot, sub);
        return File.Exists(Path.Combine(full, "albedo.png")) ? full : null;
    }

    private static bool TryApplyFolderTextures(Material mat, string folderPath, ref bool hasMaskMap)
    {
        var loaded = new List<Texture2D>();
        try
        {
            var settings = LoadFolderSettings(folderPath);
            var albedo = LoadPng(Path.Combine(folderPath, "albedo.png"), linear: false);
            loaded.Add(albedo);

            Texture2D? normal = null;
            string normalPath = Path.Combine(folderPath, "normal.png");
            if (File.Exists(normalPath))
            {
                normal = LoadPng(normalPath, linear: true);
                loaded.Add(normal);
                var pixels = normal.GetPixels32(0);
                var arr = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    p.g = CustomTextureRules.ConvertNormalGreen(p.g, settings.DirectX);
                    p.a = 255;
                    arr[i] = p;
                }
                normal.SetPixels32(new Il2CppStructArray<Color32>(arr), 0);
                normal.Apply(true, false);
            }

            Texture2D? rough = null;
            string roughPath = Path.Combine(folderPath, "roughness.png");
            if (File.Exists(roughPath))
            {
                rough = LoadPng(roughPath, linear: true);
                loaded.Add(rough);
            }

            Texture2D? ao = null;
            string aoPath = Path.Combine(folderPath, "ao.png");
            if (File.Exists(aoPath))
            {
                ao = LoadPng(aoPath, linear: true);
                loaded.Add(ao);
            }

            Texture2D? mask = null;
            if (rough != null || ao != null)
            {
                var reference = rough ?? ao!;
                int w = reference.width;
                int h = reference.height;
                var roughPixels = rough?.GetPixels32(0);
                var aoPixels = ao?.GetPixels32(0);
                var packed = new Color32[w * h];
                byte defaultRough = (byte)(255 - Math.Clamp((int)MathF.Round(settings.smoothness * 255f), 0, 255));

                static byte SampleRed(Il2CppStructArray<Color32> src, Texture2D tex, int x, int y, int w, int h) =>
                    src[(y * tex.height / h) * tex.width + (x * tex.width / w)].r;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        byte rVal = roughPixels == null ? defaultRough : SampleRed(roughPixels, rough!, x, y, w, h);
                        byte aoVal = aoPixels == null ? (byte)255 : SampleRed(aoPixels, ao!, x, y, w, h);
                        var m = CustomTextureRules.PackHdrpMask(rVal, aoVal);
                        packed[y * w + x] = new Color32(m.R, m.G, m.B, m.A);
                    }
                }

                mask = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "Floppy HDRP Mask",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 4
                };
                loaded.Add(mask);
                mask.SetPixels32(new Il2CppStructArray<Color32>(packed), 0);
                mask.Apply(true, false);
            }

            AssignTexture(mat, "_BaseColorMap", albedo);
            if (normal != null)
            {
                AssignTexture(mat, "_NormalMap", normal);
                if (mat.HasProperty("_NormalScale"))
                    mat.SetFloat("_NormalScale", Math.Clamp(settings.normalStrength, 0f, 8f));
            }
            if (mask != null)
            {
                AssignTexture(mat, "_MaskMap", mask);
                if (mat.HasProperty("_AORemapMin")) mat.SetFloat("_AORemapMin", 0f);
                if (mat.HasProperty("_AORemapMax")) mat.SetFloat("_AORemapMax", 1f);
                if (mat.HasProperty("_SmoothnessRemapMin")) mat.SetFloat("_SmoothnessRemapMin", 0f);
                if (mat.HasProperty("_SmoothnessRemapMax")) mat.SetFloat("_SmoothnessRemapMax", 1f);
                hasMaskMap = true;
            }

            foreach (var tex in loaded)
                ownedObjects.Add(tex);
            return true;
        }
        catch (Exception ex)
        {
            foreach (var tex in loaded)
            {
                if (tex != null) Object.DestroyImmediate(tex);
            }
            Warn($"custom-folder:{folderPath}", $"{folderPath}: {ex.Message}; using procedural fallback.");
            return false;
        }
    }

    private static CustomFolderSettings LoadFolderSettings(string folderPath)
    {
        string path = Path.Combine(folderPath, "settings.json");
        if (!File.Exists(path)) return new CustomFolderSettings();
        try
        {
            var parsed = JsonSerializer.Deserialize<CustomFolderSettings>(File.ReadAllText(path));
            return parsed ?? new CustomFolderSettings();
        }
        catch (Exception ex)
        {
            Warn($"settings:{folderPath}", $"Invalid settings.json in {folderPath}: {ex.Message}");
            return new CustomFolderSettings();
        }
    }

    private static Texture2D LoadPng(string path, bool linear)
    {
        var info = new FileInfo(path);
        if (info.Length > CustomTextureRules.MaxFileBytes)
            throw new InvalidDataException("PNG exceeds 32 MiB.");
        byte[] bytes = File.ReadAllBytes(path);
        var size = CustomTextureRules.ValidatePngHeader(bytes);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = Path.GetFileName(path),
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        try
        {
            if (!ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), false) ||
                tex.width != size.Width || tex.height != size.Height)
                throw new InvalidDataException("PNG decode failed.");
            tex.Apply(true, false);
            return tex;
        }
        catch
        {
            Object.DestroyImmediate(tex);
            throw;
        }
    }

    private static Texture2D CreateTexture(byte[] rgba, int size, bool linear, string name)
    {
        var colors = new Color32[size * size];
        for (int i = 0; i < colors.Length; i++)
        {
            int o = i * 4;
            colors[i] = new Color32(rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
        }

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        try
        {
            tex.SetPixels32(new Il2CppStructArray<Color32>(colors), 0);
            tex.Apply(true, false);
            ownedObjects.Add(tex);
            return tex;
        }
        catch
        {
            Object.DestroyImmediate(tex);
            throw;
        }
    }

    private static void AssignTexture(Material mat, string property, Texture2D tex)
    {
        mat.SetTexture(property, tex);
        mat.SetTextureScale(property, Vector2.one);
        mat.SetTextureOffset(property, Vector2.zero);
    }

    private static void Warn(string key, string message)
    {
        if (warnings.Add(key))
            Plugin.ModLog.LogWarning($"[Materials] {message}");
    }
}
