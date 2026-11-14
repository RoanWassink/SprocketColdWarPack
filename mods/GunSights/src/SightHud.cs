using Sprocket.WeaponFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SprocketGunSights;

/// <summary>Per-frame driver for the reticle and gunner indications.</summary>
public sealed class SightHud : MonoBehaviour
{
    public SightHud(IntPtr pointer) : base(pointer) { }

    private static GameObject? root;
    private static RawImage? reticle;
    private static TextMeshProUGUI? text;
    private static Texture2D? texture;
    private static (string Id, float Scale, int Height) baked;
    private static int warnings;

    public void LateUpdate()
    {
        try
        {
            if (!Plugin.Enabled || !ScopeRuntime.Scoped) { if (root != null && root.activeSelf) root.SetActive(false); return; }
            if (root == null && !Build()) return;
            root!.SetActive(true);
            var variant = ScopeRuntime.ActiveVariant!;
            var sight = ScopeRuntime.ActiveSight!;
            var view = Plugin.ScopeCamera();
            if (view == null) return;

            // True angular size: pixels per mil from the live vertical field of view.
            var perMil = Screen.height * .5f / (1000f * MathF.Tan(view.fieldOfView * MathF.PI / 360f));
            if (baked.Id != variant.Reticle || baked.Height != Screen.height || Math.Abs(perMil - baked.Scale) > baked.Scale * .03f)
                Bake(variant.Reticle, perMil);
            reticle!.color = variant.Ink switch
            {
                ReticleInk.Red => new Color(1f, .16f, .1f, .92f),
                ReticleInk.Amber => new Color(1f, .7f, .15f, .92f),
                _ => new Color(.03f, .03f, .03f, .92f),
            };
            text!.text = Indications(variant, sight);
        }
        catch (Exception ex)
        {
            if (warnings++ < 3) Plugin.Log.LogWarning("[Gun sights] HUD: " + ex.Message);
        }
    }

    private static void Bake(string id, float perMil)
    {
        var design = Reticles.Get(id);
        var mask = ReticleRaster.Render(design, perMil, Plugin.MinimumLinePixels, out var size);
        if (texture == null || texture.width != size)
        {
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        }
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, mask[i]);
        texture.SetPixels32(pixels); texture.Apply(false);
        reticle!.texture = texture;
        reticle.rectTransform.sizeDelta = new Vector2(size, size) * (2f * design.Extent * perMil / size);
        baked = (id, perMil, Screen.height);
    }

    private static string Indications(SightVariant variant, Sprocket.Vehicles.AttachedBehaviours.GunnerSight sight)
    {
        var lines = new List<string>();
        var powers = variant.Magnifications;
        var power = ScopeRuntime.ActiveMagnification.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
        lines.Add(powers.Length > 1
            ? $"{variant.Name}  {power}   [{string.Join(" | ", powers.Select(p => p.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))}]"
            : $"{variant.Name}  {power}");
        if (ScopeRuntime.ActiveScope is { } scope) lines.Add($"Range  {scope.GunConverganceDistance:0} m");
        var gun = ScopeRuntime.LinkedCannon(sight);
        if (gun?.Behaviour?.TryCast<WeaponBehaviour>() is { } weapon)
        {
            string status = !weapon.Functioning ? "INOP"
                : weapon.LoadState.ToString() == "Loaded" ? "READY"
                : $"LOADING {weapon.LoadFraction * 100f:0}%";
            lines.Add("Gun  " + status);
        }
        if (Plugin.ShowAmmunition) lines.AddRange(AmmoBridge.Lines(gun));
        if (ScopeRuntime.Bearing(sight, gun) is { } bearing) lines.Add($"Turret  {bearing:000}°");
        return string.Join("\n", lines);
    }

    private static bool Build()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null) return false;
        root = new GameObject("Period gun sight") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;

        var reticleObject = new GameObject("Reticle", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        reticleObject.transform.SetParent(root.transform, false);
        reticle = reticleObject.AddComponent<RawImage>();
        reticle.raycastTarget = false;
        var r = reticle.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = Vector2.zero;

        var textObject = new GameObject("Indications", Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
        textObject.transform.SetParent(root.transform, false);
        text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = Math.Max(14f, Screen.height / 60f);
        text.color = new Color(.85f, .9f, .8f, .9f);
        text.raycastTarget = false; text.richText = false; text.enableWordWrapping = false;
        var t = text.rectTransform;
        t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 0f);
        t.anchoredPosition = new Vector2(Screen.height * .04f, Screen.height * .06f);
        t.sizeDelta = new Vector2(Screen.width * .4f, Screen.height * .2f);
        return true;
    }

    internal static void Clear()
    {
        if (root != null) Object.Destroy(root);
        if (texture != null) Object.Destroy(texture);
        root = null; reticle = null; text = null; texture = null; baked = default;
    }
}
