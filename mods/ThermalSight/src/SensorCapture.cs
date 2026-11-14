using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SprocketThermalSight;

internal sealed class SensorCapture : IDisposable
{
    private readonly ThermalProfile profile;
    private readonly RenderTexture scene, maskSmall;
    private readonly RTHandle uploadHandle;
    private readonly RTHandle sceneHandle, maskSmallHandle;
    private RTHandle? maskFullHandle;
    private readonly Texture2D upload;
    private RenderTexture? maskFull;
    private byte[]? sceneBytes, maskBytes;
    private readonly byte[] sceneBuffer, maskBuffer;
    private readonly SensorWorkspace processor;
    private readonly Il2CppStructArray<byte> uploadBytes;
    private readonly float[] illumination;
    private const int BeamWidth = 32, BeamHeight = 24;
    private readonly float[] beamGrid = new float[BeamWidth * BeamHeight];
    private readonly Il2CppStructArray<RaycastHit> rayHits = new(32);
    internal readonly HeatSpot[] HeatSpots = new HeatSpot[HeatTracker.MaxSpots];
    private readonly HeatField? heat;
    private int heatSpotCount; private bool heatEnabled;
    private string readbackFailure = "GPU request error or unexpected buffer size";
    private int outstanding;
    private bool error, disposed, shutdown;
    private uint sequence;
    private readonly object gate = new();
    internal bool HasImage { get; private set; }
    internal bool Pending { get { lock (gate) return outstanding > 0; } }
    internal bool AwaitingUpload { get { lock (gate) return sceneBytes != null || maskBytes != null || error; } }

    internal SensorCapture(ThermalProfile profile)
    {
        this.profile = profile;
        sceneBuffer = new byte[profile.Width * profile.Height * 4]; maskBuffer = new byte[sceneBuffer.Length];
        illumination = new float[profile.Width * profile.Height];
        processor = new SensorWorkspace(profile); uploadBytes = new Il2CppStructArray<byte>(sceneBuffer.Length);
        if (profile.SensorType == "thermal") heat = new HeatField(profile.Width, profile.Height);
        scene = Make(profile.Width, profile.Height, "Thermal scene");
        maskSmall = Make(profile.Width, profile.Height, "Thermal sensor mask");
        sceneHandle = RTHandles.Alloc(scene, false);
        maskSmallHandle = RTHandles.Alloc(maskSmall, false);
        upload = new Texture2D(profile.Width, profile.Height, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
        upload.filterMode = profile.SmoothPixels ? FilterMode.Bilinear : FilterMode.Point;
        uploadHandle = RTHandles.Alloc(upload);
    }
    // Spots belong to the capture issued right after this call (one capture in flight at a time).
    internal void SetHeatSpots(int count, HeatSettings settings)
    { heatSpotCount = Math.Clamp(count, 0, HeatSpots.Length); heatEnabled = settings.Enabled; if (heat != null) heat.HullContrast = settings.HullContrast; }
    private static RenderTexture Make(int width, int height, string name)
    {
        var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
        if (!rt.Create()) { UnityEngine.Object.Destroy(rt); throw new InvalidOperationException("Sensor texture creation failed."); }
        return rt;
    }
    internal void Capture(CustomPassContext ctx, List<Renderer> renderers, Material material, int pass)
    {
        var depth = ctx.cameraDepthBuffer;
        var source = ctx.cameraColorBuffer;
        if (depth?.rt == null || source?.rt == null) throw new InvalidOperationException("Camera buffers unavailable.");
        var depthLayout = Layout(depth);
        var colorLayout = Layout(source);
        var cmd = ctx.cmd;
        var scale = new Vector2(colorLayout.ScaleX, colorLayout.ScaleY);
        if (profile.SensorType == "thermal")
        {
            int width = depthLayout.SurfaceWidth, height = depthLayout.SurfaceHeight;
            if (maskFull == null || maskFull.width != width || maskFull.height != height)
            { maskFullHandle?.Release(); if (maskFull != null) Destroy(maskFull); maskFull = Make(width, height, "Thermal depth-tested vehicle mask"); maskFullHandle = RTHandles.Alloc(maskFull, false); }
            var viewport = new Rect(0, 0, depthLayout.ViewportWidth, depthLayout.ViewportHeight);
            // Equal depth uses the existing HDRP depth: no x-ray silhouettes, no changes to native materials.
            cmd.SetRenderTarget(new RenderTargetIdentifier(maskFull), depth.nameID);
            cmd.SetViewport(viewport);
            cmd.ClearRenderTarget(false, true, Color.clear);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                int count = 0;
                var skinned = r.TryCast<SkinnedMeshRenderer>();
                if (skinned != null && skinned.sharedMesh != null) count = skinned.sharedMesh.subMeshCount;
                else { var filter = r.GetComponent<MeshFilter>(); if (filter?.sharedMesh != null) count = filter.sharedMesh.subMeshCount; }
                for (int i = 0; i < count; i++) cmd.DrawRenderer(r, material, i, pass);
            }
            var maskScale = new Vector2(depthLayout.ScaleX, depthLayout.ScaleY);
            cmd.SetRenderTarget(maskSmallHandle.nameID);
            cmd.SetViewport(new Rect(0, 0, profile.Width, profile.Height));
            CopyTexture(cmd, maskFullHandle!, new Vector4(maskScale.x, maskScale.y, 0, 0), true);
        }
        cmd.SetRenderTarget(sceneHandle.nameID);
        cmd.SetViewport(new Rect(0, 0, profile.Width, profile.Height));
        CopyTexture(cmd, source, new Vector4(scale.x, scale.y, 0, 0), true);
        lock (gate)
        {
            sceneBytes = maskBytes = null; error = false; readbackFailure = "GPU request error or unexpected buffer size";
            outstanding = profile.SensorType == "thermal" ? 2 : 1;
            if (profile.SensorType != "thermal") maskBytes = maskBuffer;
        }
        cmd.RequestAsyncReadback(scene, 0, TextureFormat.RGBA32,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AsyncGPUReadbackRequest>>(new Action<AsyncGPUReadbackRequest>(r => Complete(r, false))));
        if (profile.SensorType == "thermal") cmd.RequestAsyncReadback(maskSmall, 0, TextureFormat.RGBA32,
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AsyncGPUReadbackRequest>>(new Action<AsyncGPUReadbackRequest>(r => Complete(r, true))));
        cmd.SetRenderTarget(source.nameID, depth.nameID);
        cmd.SetViewport(new Rect(0, 0, colorLayout.ViewportWidth, colorLayout.ViewportHeight));
        ReportLayout("Capture color", ctx.hdCamera, source, colorLayout);
        ReportLayout("Capture depth", ctx.hdCamera, depth, depthLayout);
    }
    internal bool PrepareIr(Camera camera, Sprocket.Vehicles.AttachedBehaviours.GunnerSight sight, GameObject? own)
    {
        Array.Clear(illumination, 0, illumination.Length);
        if (!IrSearchlight.TryPose(sight, out var origin, out var forward)) return false;
        // Sparse collider depth avoids an unverified depth-readback shader. The grid
        // uses the current camera projection and the lamp's independent world pose.
        for (int gy = 0; gy < BeamHeight; gy++) for (int gx = 0; gx < BeamWidth; gx++)
        {
            int cell = gy * BeamWidth + gx; beamGrid[cell] = 0;
            var ray = camera.ViewportPointToRay(new Vector3((gx + .5f) / BeamWidth, (gy + .5f) / BeamHeight, 0));
            int hits = Physics.RaycastNonAlloc(ray, rayHits, profile.BeamRangeM + Vector3.Distance(ray.origin, origin), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A saturated result is incomplete; fail dark instead of looking behind a surface.
            if (hits == rayHits.Length) continue;
            float nearest = float.PositiveInfinity; Vector3 point = Vector3.zero;
            for (int i = 0; i < hits; i++)
            {
                var hit = rayHits[i];
                if (hit.collider == null || (own != null && hit.collider.transform.IsChildOf(own.transform))) continue;
                if (hit.distance < nearest) { nearest = hit.distance; point = hit.point; }
            }
            if (!float.IsFinite(nearest)) continue;
            var offset = point - origin;
            float weight = IrBeamPolicy.Illumination(offset.x, offset.y, offset.z, forward.x, forward.y, forward.z, profile.BeamRangeM, profile.BeamAngleDeg);
            if (weight <= 0) continue;
            // Also test lamp-to-surface occlusion. The player's own hull is deliberately
            // excluded, matching the native rangefinder's self-hit contract.
            float distance = offset.magnitude;
            hits = Physics.RaycastNonAlloc(new Ray(origin, offset.normalized), rayHits, Math.Max(0, distance - .1f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool blocked = hits == rayHits.Length;
            for (int i = 0; i < hits && !blocked; i++)
            {
                var collider = rayHits[i].collider;
                if (collider != null && (own == null || !collider.transform.IsChildOf(own.transform))) blocked = true;
            }
            if (!blocked) beamGrid[cell] = weight;
        }
        for (int y = 0; y < profile.Height; y++) for (int x = 0; x < profile.Width; x++)
        {
            float sx = Math.Clamp((x + .5f) * BeamWidth / profile.Width - .5f, 0, BeamWidth - 1);
            float sy = Math.Clamp((y + .5f) * BeamHeight / profile.Height - .5f, 0, BeamHeight - 1);
            int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, BeamWidth - 1), y1 = Math.Min(y0 + 1, BeamHeight - 1);
            float tx = sx - x0, ty = sy - y0;
            float a = beamGrid[y0 * BeamWidth + x0] * (1 - tx) + beamGrid[y0 * BeamWidth + x1] * tx;
            float b = beamGrid[y1 * BeamWidth + x0] * (1 - tx) + beamGrid[y1 * BeamWidth + x1] * tx;
            illumination[y * profile.Width + x] = a * (1 - ty) + b * ty;
        }
        return true;
    }
    private void Complete(AsyncGPUReadbackRequest request, bool mask)
    {
        lock (gate)
        {
            try
            {
                if (request.hasError || request.layerDataSize != profile.Width * profile.Height * 4) error = true;
                else
                {
                    var bytes = mask ? maskBuffer : sceneBuffer;
                    Marshal.Copy(request.GetDataRaw(0), bytes, 0, bytes.Length);
                    if (mask) maskBytes = bytes; else sceneBytes = bytes;
                }
            }
            catch (Exception ex) { error = true; readbackFailure = ex.GetType().Name + ": " + ex.Message; }
            finally { outstanding--; }
            if (shutdown && outstanding == 0) Dispose();
        }
    }
    internal void UploadIfReady()
    {
        byte[]? a, b;
        lock (gate)
        {
            if (outstanding > 0) return;
            if (error) { error = false; throw new InvalidOperationException("GPU sensor readback failed: " + readbackFailure); }
            a = sceneBytes; b = maskBytes; sceneBytes = maskBytes = null;
        }
        if (a == null || b == null) return;
        HeatField? field = null;
        if (heat != null && heatEnabled)
        {
            heat.Clear();
            for (int i = 0; i < heatSpotCount; i++) heat.AddSpot(HeatSpots[i]);
            field = heat;
        }
        var bytes = processor.Process(a, b, ++sequence, illumination, field);
        bytes.AsSpan().CopyTo(uploadBytes.AsSpan());
        upload.LoadRawTextureData(uploadBytes);
        upload.Apply(false, false);
        HasImage = true;
        if (sequence == 1 && Runtime.DetailedLogging)
            Plugin.Instance.Log.LogInfo($"[Thermal] First sensor frame: sceneRGB={Stats(a)}, heatRGB={Stats(b)}, outputRGB={Stats(bytes)}; sensor={profile.Width}x{profile.Height}");
    }
    private static string Stats(byte[] bytes)
    {
        int min = 255, max = 0; long sum = 0;
        for (int i = 0; i < bytes.Length; i++)
        { if ((i & 3) == 3) continue; min = Math.Min(min, bytes[i]); max = Math.Max(max, bytes[i]); sum += bytes[i]; }
        return $"{min}..{max},mean={sum / (bytes.Length / 4 * 3.0):F1}";
    }
    internal RTHandle ScopeImage(CommandBuffer cmd, HDCamera camera, RTHandle source)
    {
        if (source.rt == null) throw new InvalidOperationException("Scope source texture unavailable.");
        // Keep the native handle, format and texture dimension. Upload is explicitly 2D;
        // HDRP's generic blitter can select an array sampler even for a 2D upload texture.
        cmd.SetRenderTarget(source.nameID);
        var layout = Layout(source);
        cmd.SetViewport(new Rect(0, 0, layout.ViewportWidth, layout.ViewportHeight));
        Blitter.BlitTexture2D(cmd, uploadHandle, new Vector4(1, 1, 0, 0), 0, profile.SmoothPixels);
        ReportLayout("Scope upload", camera, source, layout);
        if (!scopeReported)
        {
            scopeReported = true;
            Plugin.Instance.Log.LogInfo($"[Thermal] Native scope buffer retained: {source.rt.width}x{source.rt.height}, dimension={source.rt.dimension}, format={source.rt.graphicsFormat}; explicit 2D upload.");
        }
        return source;
    }
    private bool scopeReported;
    private static BufferLayout Layout(RTHandle handle)
    {
        var rt = handle.rt ?? throw new InvalidOperationException("Render texture missing.");
        var effective = new Vector2Int(rt.width, rt.height);
        var dynamic = DynamicResolutionHandler.instance;
        if (rt.useDynamicScale && dynamic.HardwareDynamicResIsEnabled())
            effective = dynamic.GetScaledSize(effective);
        var viewport = handle.useScaling ? handle.GetScaledSize(handle.rtHandleProperties.currentViewportSize) : effective;
        return BufferLayout.Resolve(effective.x, effective.y, viewport.x, viewport.y, handle.useScaling);
    }
    private readonly Dictionary<string, string> lastLayouts = new();
    private readonly Dictionary<string, float> layoutLogAt = new();
    private int layoutReports;
    private void ReportLayout(string stage, HDCamera camera, RTHandle handle, BufferLayout layout)
    {
        if (!Runtime.DetailedLogging || layoutReports >= 36) return;
        var properties = handle.rtHandleProperties;
        var rt = handle.rt!;
        var key = $"allocated={rt.width}x{rt.height}, effective={layout.SurfaceWidth}x{layout.SurfaceHeight}, viewport={layout.ViewportWidth}x{layout.ViewportHeight}, useScaling={handle.useScaling}, hwTexture={rt.useDynamicScale}, actual={camera.actualWidth}x{camera.actualHeight}";
        if (lastLayouts.TryGetValue(stage, out var previous) && key == previous) return;
        if (layoutLogAt.TryGetValue(stage, out var at) && Time.unscaledTime < at) return;
        lastLayouts[stage] = key; layoutLogAt[stage] = Time.unscaledTime + 2; layoutReports++;
        var dynamic = DynamicResolutionHandler.instance;
        Plugin.Instance.Log.LogInfo($"[Thermal] {stage}: {key}; handleViewport={properties.currentViewportSize}, handleTarget={properties.currentRenderTargetSize}, handleScale={properties.rtHandleScale}, uvScale={layout.ScaleX:F4},{layout.ScaleY:F4}, postProcess={camera.postProcessScreenSize}, final={camera.finalViewport}, hardwareDRS={dynamic.HardwareDynamicResIsEnabled()}, softwareDRS={dynamic.SoftwareDynamicResIsEnabled()}, dimension={rt.dimension}, format={rt.graphicsFormat}");
    }
    private static void CopyTexture(CommandBuffer cmd, RTHandle source, Vector4 scaleBias, bool bilinear)
    {
        var dimension = source.rt?.dimension ?? TextureDimension.Tex2D;
        if (dimension == TextureDimension.Tex2D)
            Blitter.BlitTexture2D(cmd, source, scaleBias, 0, bilinear);
        else
        {
            var material = Blitter.GetBlitMaterial(dimension, true);
            int shaderPass = material.FindPass(bilinear ? "Bilinear" : "Nearest");
            if (shaderPass < 0) throw new InvalidOperationException("Required HDRP texture-copy pass missing.");
            cmd.SetGlobalFloat("_BlitTexArraySlice", 0);
            Blitter.BlitTexture(cmd, source, scaleBias, material, shaderPass);
        }
    }
    internal void RetireForShutdown()
    { lock (gate) { shutdown = true; if (outstanding == 0) Dispose(); } }
    private static void Destroy(RenderTexture rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; uploadHandle.Release(); sceneHandle.Release(); maskSmallHandle.Release(); maskFullHandle?.Release();
        Destroy(scene); Destroy(maskSmall);
        if (maskFull != null) Destroy(maskFull);
        UnityEngine.Object.Destroy(upload);
    }
}





