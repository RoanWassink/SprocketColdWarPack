using System.Reflection;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.WeaponFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PlayerController = Sprocket.Gameplay.VehicleControl.VehicleController;

namespace SprocketThermalSight;

// 0.5.0: one HUD per night-sight type while a night/thermal profile is active in the scope image.
// 0.5.1: drawn with uGUI (own Canvas, one RawImage with the CPU-rasterised lines, TextMeshProUGUI texts),
// as in Gun Sights; IMGUI drawing (GUI.DrawTexture/GUIStyle) is stripped from this IL2CPP build.
// Every value is read from native state or this mod's own state; nothing is written.
internal static class NightHud
{
    private static ConfigEntry<bool> enabled = null!, drawReticle = null!;
    private static readonly HudData data = new();
    private static bool visible, failed, releasePending, shownThisFrame;
    private static int collectedFrame = -10, shownFrame = -10;
    private static readonly HashSet<HudStyle> logged = new();
    private static IntPtr cannonSight; private static Cannon? cannon; private static float cannonAt;
    // Repaint the canvas texture only when the content changed, at most ~20 times a second.
    private const float RepaintInterval = .05f;
    private static int lastSignature; private static float nextPaint; private static bool painted;
    // True when the canvas HUD showed the COOLING line this frame; otherwise Runtime draws it with GUI.Label.
    internal static bool CoolingShown => shownFrame == Time.frameCount && shownThisFrame && data.Cooling > 0;

    internal static void Configure(ConfigFile config)
    {
        enabled = config.Bind("Hud", "Enabled", true, "Draw the night-sight HUD (one layout per sensor type) while a night or thermal profile is active.");
        drawReticle = config.Bind("Hud", "DrawReticle", true, "Draw the HUD's own aiming marks. Turn off if the native or Gun Sights reticle already shows over the thermal image.");
    }
    // Not shown this frame (paused, image not ready): the canvas is hidden in Sync, kept for reuse.
    internal static void Hide() => visible = false;
    // Thermal switched off, sight changed or vehicle left: Canvas and texture are destroyed in the next Sync
    // (LateUpdate), never inside a native hook or render callback.
    internal static void Release() { visible = false; releasePending = true; painted = false; }
    internal static void Clear() { visible = false; releasePending = false; painted = false; cannon = null; cannonSight = IntPtr.Zero; logged.Clear(); HudCanvas.Release(); }

    // LateUpdate, while the sensor is active: gather a read-only snapshot for the canvas (Sync).
    // 0.5.4: palette is the polarity actually shown (white-hot/black-hot switch), so the WH/BH text follows it.
    internal static void Collect(ThermalProfile profile, GunnerSight? sight, Camera? camera, PlayerController? player, float gain, bool irLamp, float cooling, string palette)
    {
        visible = false;
        if (failed || !enabled.Value || sight == null) return;
        collectedFrame = Time.frameCount;
        var d = data;
        d.Style = HudMath.StyleFor(profile.ComponentId, profile.SensorType, profile.Width);
        if (d.Style == HudStyle.None) return;
        d.ProfileId = profile.ComponentId; d.Palette = palette; d.Gain = gain; d.IrLamp = irLamp; d.Cooling = cooling;
        d.Heading = d.Turret = d.Elevation = d.Magnification = null; d.Narrow = d.Loaded = null;
        d.Range = null; d.RangeStatus = RangeStatus.None; d.Echoes = 0; d.TargetCentred = false; d.Ammo = null; d.Rounds.Clear();
        Transform? optic = null, hull = null;
        try
        {
            optic = sight.SightParent;
            hull = sight.Vehicle?.Behaviour?.TryCast<VehicleBehaviour>()?.transform;
            if (optic != null) { var f = optic.forward; d.Heading = HudMath.Heading(f.x, f.z); }
            if (optic != null && hull != null)
            {
                Vector3 f = optic.forward, h = hull.forward, u = hull.up;
                d.Turret = HudMath.RelativeBearing(f.x, f.y, f.z, h.x, h.y, h.z, u.x, u.y, u.z);
            }
        }
        catch (Exception ex) { Runtime.Warn("HUD heading unavailable: " + ex.Message, "hud-heading"); }
        try
        {
            var weapon = sight.LinkedWeapon;
            if (weapon != null && hull != null)
            { Vector3 f = weapon.forward, u = hull.up; d.Elevation = HudMath.Elevation(f.x, f.y, f.z, u.x, u.y, u.z); }
        }
        catch (Exception ex) { Runtime.Warn("HUD elevation unavailable: " + ex.Message, "hud-elevation"); }
        ScopeController? scope = null;
        try
        {
            if (camera != null) d.Magnification = HudMath.Magnification(camera.fieldOfView);
            scope = player?.ScopeControl?.TryCast<ScopeController>();
            var zoom = scope?.controller;
            if (zoom != null) d.Narrow = HudMath.Narrow(zoom.AppliedFocalLength, zoom.minZoom, zoom.maxZoom);
        }
        catch (Exception ex) { Runtime.Warn("HUD zoom unavailable: " + ex.Message, "hud-zoom"); }
        try
        {
            var last = Rangefinder.Last;
            if (last.Vehicle != IntPtr.Zero && last.Vehicle == sight.Vehicle?.Pointer)
            {
                d.Range = last.Range; d.RangeStatus = last.Status; d.Echoes = last.Echoes;
                if (last.Status == RangeStatus.Ok && last.Range != null && last.Body && scope != null && optic != null)
                {
                    var line = scope.ScopeAimPoint - optic.TransformPoint(sight.ObjectiveLensLocalPosition);
                    d.TargetCentred = line.sqrMagnitude > 1e-6f && Vector3.Angle(line, last.SightDirection) <= .6f;
                }
            }
        }
        catch (Exception ex) { Runtime.Warn("HUD range unavailable: " + ex.Message, "hud-range"); }
        try
        {
            if (cannonSight != sight.Pointer || Time.unscaledTime >= cannonAt)
            { cannonSight = sight.Pointer; cannonAt = Time.unscaledTime + 1; cannon = RangefinderBallistics.LinkedCannon(sight); }
            var weapon = cannon?.Behaviour?.TryCast<WeaponBehaviour>();
            if (weapon != null) d.Loaded = weapon.Functioning && weapon.LoadState.ToString() == "Loaded";
            if (cannon != null && AmmoReader.Read(cannon, d.Rounds) is { } chambered) d.Ammo = chambered;
        }
        catch (Exception ex) { Runtime.Warn("HUD gun state unavailable: " + ex.Message, "hud-gun"); }
        visible = true;
        if (logged.Add(d.Style))
            Plugin.Instance.Log.LogInfo($"[Thermal HUD] {d.Style} for {d.ProfileId}: heading={(d.Heading != null ? "yes" : "no")}, turret={(d.Turret != null ? "yes" : "no")}, elevation={(d.Elevation != null ? "yes" : "no")}, zoom={(d.Magnification is { } m ? m.ToString("0.0") + "x" : "no")}, fov={(d.Narrow is { } n ? (n ? "N" : "W") : "fixed")}, range={HudMath.FormatRange(d.Range)} ({d.RangeStatus}), loaded={(d.Loaded?.ToString() ?? "n/a")}, ammo={(d.Ammo ?? (AmmoReader.Available ? AmmoReader.Status : "no EB"))}, ammoTypes={d.Rounds.Count}, reticle={drawReticle.Value}.");
    }

    // ThermalDriver.LateUpdate, after Runtime.Tick: show, repaint, hide or release the canvas.
    internal static void Sync()
    {
        shownThisFrame = false;
        try
        {
            bool show = visible && !failed && enabled.Value && Application.isFocused && collectedFrame == Time.frameCount;
            if (!show)
            {
                if (releasePending) { HudCanvas.Release(); releasePending = false; painted = false; }
                else HudCanvas.Hide();
                return;
            }
            releasePending = false;
            int signature = data.Signature(drawReticle.Value);
            bool repaint = !painted || (signature != lastSignature && Time.unscaledTime >= nextPaint);
            if (!HudCanvas.Show(data, drawReticle.Value, repaint, out bool didPaint)) { painted = false; return; }
            if (didPaint) { painted = true; lastSignature = signature; nextPaint = Time.unscaledTime + RepaintInterval; }
            shownThisFrame = true; shownFrame = Time.frameCount;
        }
        catch (Exception ex)
        {
            failed = true; visible = false; painted = false;
            try { HudCanvas.Release(); } catch { }
            Plugin.Instance.Log.LogWarning("[Thermal HUD] Disabled for this session; thermal image retained: " + ex.Message);
        }
    }
}

// The uGUI side: one ScreenSpaceOverlay Canvas above Gun Sights (sorting 70 > 60), one RawImage with the
// rasterised lines, four black side panels for the round eyepiece, and a pool of TextMeshProUGUI texts.
internal static class HudCanvas
{
    private const int SortingOrder = 70, MaxTexts = 48;
    private static GameObject? root;
    private static RawImage? image;
    private static readonly RawImage?[] sides = new RawImage?[4];
    private static readonly List<TextMeshProUGUI> texts = new();
    private static TMP_FontAsset? font;
    private static Texture2D? texture;
    private static Il2CppStructArray<byte>? upload;
    private static HudRaster? raster;
    private static (int W, int H) screen;
    private static bool fontMissingLogged;

    internal static bool Visible => root != null && root.activeSelf;

    // False when the canvas cannot be shown yet (no TMP font); exceptions go to NightHud's safety net.
    internal static bool Show(HudData d, bool reticle, bool repaint, out bool painted)
    {
        painted = false;
        var layout = HudLayout.For(Screen.width, Screen.height);
        if (layout.Scale <= 0) { Hide(); return false; }
        if (root == null && !Build()) return false;
        float k = HudPainter.RasterScale(layout.Scale);
        if (raster == null || Math.Abs(raster.Scale - k) > 1e-4f || texture == null)
        {
            raster = new HudRaster(k);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(raster.Width, raster.Height, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            upload = new Il2CppStructArray<byte>(raster.Pixels.Length);
            image!.texture = texture;
            repaint = true;
        }
        if (screen != (Screen.width, Screen.height)) { screen = (Screen.width, Screen.height); repaint = true; }
        if (repaint)
        {
            HudPainter.Paint(raster, d, reticle);
            Upload(raster);
            Place(layout, raster);
            painted = true;
        }
        if (!root!.activeSelf) root.SetActive(true);
        return true;
    }

    // Texture rows run bottom-up; the raster runs top-down.
    private static void Upload(HudRaster r)
    {
        int row = r.Width * 4;
        var target = upload!.AsSpan();
        for (int y = 0; y < r.Height; y++)
            r.Pixels.AsSpan(y * row, row).CopyTo(target.Slice((r.Height - 1 - y) * row, row));
        texture!.LoadRawTextureData(upload);
        texture.Apply(false, false);
    }

    private static void Place(HudLayout layout, HudRaster r)
    {
        float w = Screen.width, h = Screen.height, s = layout.Scale;
        var rect = image!.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(HudLayout.DesignWidth * s, HudLayout.DesignHeight * s);
        // Eyepiece: black outside the design frame (one pixel overlap against seams).
        float left = layout.OriginX, bottom = layout.OriginY, frameW = HudLayout.DesignWidth * s, frameH = HudLayout.DesignHeight * s;
        Side(0, r.Eyepiece && left > 0, 0, 0, left + 1, h);
        Side(1, r.Eyepiece && left > 0, left + frameW - 1, 0, w - left - frameW + 1, h);
        Side(2, r.Eyepiece && bottom > 0, 0, 0, w, bottom + 1);
        Side(3, r.Eyepiece && bottom > 0, 0, bottom + frameH - 1, w, h - bottom - frameH + 1);
        int used = 0;
        foreach (var t in r.Texts)
        {
            if (used >= MaxTexts) break;
            var label = Label(used++);
            int px = Math.Max(9, (int)MathF.Round(t.Size * s));
            label.text = t.Text;
            label.fontSize = px;
            label.fontStyle = t.Bold ? FontStyles.Bold : FontStyles.Normal;
            label.color = new Color(t.Color.R, t.Color.G, t.Color.B, t.Color.A);
            label.alignment = t.Anchor switch { HudAnchor.Center => TextAlignmentOptions.Bottom, HudAnchor.Right => TextAlignmentOptions.BottomRight, _ => TextAlignmentOptions.BottomLeft };
            var lr = label.rectTransform;
            float pivot = t.Anchor switch { HudAnchor.Center => .5f, HudAnchor.Right => 1f, _ => 0f };
            lr.anchorMin = lr.anchorMax = Vector2.zero;
            lr.pivot = new Vector2(pivot, 0);
            lr.sizeDelta = new Vector2(Math.Max(400, px * (t.Text.Length + 2)), px * 1.6f);
            // The baseline sits about a quarter em above the bottom of a bottom-aligned TMP box.
            lr.anchoredPosition = new Vector2(layout.X(t.X), h - layout.Y(t.Y) - px * .25f);
            if (!label.enabled) label.enabled = true;
        }
        for (int i = used; i < texts.Count; i++) if (texts[i].enabled) texts[i].enabled = false;
    }

    private static void Side(int i, bool on, float x, float y, float w, float h)
    {
        var side = sides[i]!;
        side.enabled = on && w > 0 && h > 0;
        if (!side.enabled) return;
        var rect = side.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(w, h);
    }

    private static TextMeshProUGUI Label(int index)
    {
        while (texts.Count <= index)
        {
            var go = new GameObject("HUD text", Il2CppType.Of<RectTransform>());
            go.transform.SetParent(root!.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font; text.raycastTarget = false; text.richText = false; text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            texts.Add(text);
        }
        return texts[index];
    }

    private static bool Build()
    {
        font = TMP_Settings.defaultFontAsset;
        if (font == null)
        {
            if (!fontMissingLogged) { fontMissingLogged = true; Plugin.Instance.Log.LogInfo("[Thermal HUD] Waiting for the TextMeshPro default font; COOLING shown with GUI.Label meanwhile."); }
            return false;
        }
        root = new GameObject("Thermal sight HUD") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(root);
        root.SetActive(false);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        for (int i = 0; i < sides.Length; i++) sides[i] = Panel("Eyepiece side", new Color(0, 0, 0, 1));
        image = Panel("HUD lines", Color.white);
        Plugin.Instance.Log.LogInfo($"[Thermal HUD] Canvas ready (ScreenSpaceOverlay, sorting {SortingOrder}, RawImage + TextMeshPro).");
        return true;
    }

    private static RawImage Panel(string name, Color colour)
    {
        var go = new GameObject(name, Il2CppType.Of<RectTransform>());
        go.transform.SetParent(root!.transform, false);
        var panel = go.AddComponent<RawImage>();
        panel.raycastTarget = false; panel.color = colour;
        return panel;
    }

    internal static void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }

    internal static void Release()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        root = null; image = null; texture = null; upload = null; raster = null; font = null; screen = default;
        Array.Clear(sides, 0, sides.Length); texts.Clear();
    }
}

// Optional read-only link to Expanded Ballistics (reflection, no assembly reference):
// chambered round and the remaining count per type.
internal static class AmmoReader
{
    private static MethodInfo? tryGetState;
    private static float searchAt;
    private static int warnings;
    private static string lastStatus = "";
    private static int statusLogs;
    internal static string Status { get; private set; } = "no EB";
    internal static bool Available => tryGetState != null;
    internal static string? Read(Cannon gun, List<(string Code, int Count, bool Chambered)> rounds)
    {
        rounds.Clear();
        if (tryGetState == null)
        {
            if (Time.unscaledTime < searchAt) { Status = "no EB"; return null; }
            searchAt = Time.unscaledTime + 5;
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("SprocketExpandedBallistics.BallisticsAmmoApi", false)).FirstOrDefault(t => t != null);
            tryGetState = type?.GetMethod("TryGetState", BindingFlags.Public | BindingFlags.Static);
            if (tryGetState == null) { Status = "no EB"; return null; }
            Plugin.Instance.Log.LogInfo("[Thermal HUD] Expanded Ballistics ammunition API found; ammunition shown read-only.");
        }
        try
        {
            object?[] args = { gun, null };
            bool ok = tryGetState.Invoke(null, args) is true;
            string? chambered = AmmoSnapshotReader.Read(ok ? args[1] : null, rounds, out string status);
            Status = status;
            string key = $"{gun.Pointer}:{status}:{rounds.Count}";
            if (key != lastStatus && Runtime.DetailedLogging && statusLogs < 40)
            {
                lastStatus = key; statusLogs++;
                Plugin.Instance.Log.LogInfo($"[Thermal HUD] Ammunition: {status}, types={rounds.Count}; missing data shown as ----.");
            }
            return chambered;
        }
        catch (Exception ex)
        {
            if (warnings++ < 3) Plugin.Instance.Log.LogWarning("[Thermal HUD] Ammunition link: " + ex.Message);
            Status = "read-error"; rounds.Clear(); return null;
        }
    }
}
