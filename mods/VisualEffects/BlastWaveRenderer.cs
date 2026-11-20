using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
using NV=System.Numerics.Vector3;
namespace SprocketVisualEffects;

// 0.5.0 HDR flash coronas and supersonic shockwave rings in ONE dynamic mesh / ONE draw call.
// Vertex layout (must match BlastWave.shader):
//   vertices=world corner, uv=(-1..1,-1..1), uv2=(kind, param), colors=(r,g,b,alpha).
internal sealed class BlastWaveRenderer
{
    private readonly BlastWave[] pool;
    private int next, prevLive, lastStart, lastCount;
    private float lastSpawnTime = float.NegativeInfinity;
    private Vector3 lastSpawnPos;
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly Il2CppStructArray<Vector3> verts;
    private readonly Il2CppStructArray<Vector2> uv0, uv1;
    private readonly Il2CppStructArray<Color> cols;
    private bool disposed;
    internal int Capacity => pool.Length;
    internal int LastLive { get; private set; }

    internal BlastWaveRenderer(Transform parent, Material source, int capacity = 64)
    {
        var n = Math.Clamp(capacity, 16, 128);
        pool = new BlastWave[n];
        material = new Material(source) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3003 };
        root = new GameObject("Visual effects blast waves") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.SetParent(parent, false);
        mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        verts = new Il2CppStructArray<Vector3>(n * 4);
        uv0 = new Il2CppStructArray<Vector2>(n * 4);
        uv1 = new Il2CppStructArray<Vector2>(n * 4);
        cols = new Il2CppStructArray<Color>(n * 4);
        var tri = new Il2CppStructArray<int>(n * 6);
        for (var k = 0; k < n; k++)
        {
            uv0[k * 4] = new Vector2(-1, -1);
            uv0[k * 4 + 1] = new Vector2(1, -1);
            uv0[k * 4 + 2] = new Vector2(1, 1);
            uv0[k * 4 + 3] = new Vector2(-1, 1);
            int b = k * 4, o = k * 6;
            tri[o] = b; tri[o + 1] = b + 2; tri[o + 2] = b + 1;
            tri[o + 3] = b; tri[o + 4] = b + 3; tri[o + 5] = b + 2;
        }
        mesh.vertices = verts; mesh.uv = uv0; mesh.uv2 = uv1; mesh.colors = cols; mesh.triangles = tri;
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = root.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.allowOcclusionWhenDynamic = false;
        root.SetActive(false);
    }

    private void PutBatch(Span<BlastWave> batch, int count, Vector3 origin, float now)
    {
        if (count <= 0) return;
        lastStart = next;
        lastCount = count;
        lastSpawnPos = origin;
        lastSpawnTime = now;
        for (var i = 0; i < count; i++)
        {
            pool[next] = batch[i];
            next = (next + 1) % pool.Length;
        }
        root.SetActive(true);
    }

    internal void CancelNear(Vector3 pos, float now)
    {
        if (disposed || lastCount <= 0 || MathF.Abs(now - lastSpawnTime) > 0.10f) return;
        if ((pos - lastSpawnPos).sqrMagnitude > 0.90f * 0.90f) return;
        for (var i = 0; i < lastCount; i++)
            pool[(lastStart + i) % pool.Length].Life = 0f;
        lastCount = 0;
    }

    internal void EmitMuzzle(Vector3 muzzle, Vector3 forward, float scale, float fireIntensity, bool shockwave)
    {
        if (disposed) return;
        Span<BlastWave> tmp = stackalloc BlastWave[4];
        var now = Time.time;
        BlastWaveMath.SpawnMuzzleWaves(N(muzzle), N(forward), scale, now, fireIntensity, shockwave, tmp, out var n);
        PutBatch(tmp, n, muzzle, now);
    }

    internal void EmitImpact(ImpactKind kind, Vector3 pos, Vector3 normal, float scale, bool shockwaves)
    {
        if (disposed) return;
        Span<BlastWave> tmp = stackalloc BlastWave[4];
        var now = Time.time;
        var p = N(pos); var norm = N(normal);
        int n = 0;
        switch (kind)
        {
            case ImpactKind.Explosion:
                BlastWaveMath.SpawnExplosionWaves(p, norm, scale, now, shockwaves, tmp, out n);
                break;
            case ImpactKind.Era:
                BlastWaveMath.SpawnEraWaves(p, norm, scale, now, shockwaves, tmp, out n);
                break;
            case ImpactKind.Heat:
                BlastWaveMath.SpawnHeatWaves(p, norm, scale, now, shockwaves, tmp, out n);
                break;
            case ImpactKind.Hesh:
                BlastWaveMath.SpawnHeshWaves(p, norm, scale, now, shockwaves, tmp, out n);
                break;
            case ImpactKind.Aphe:
                BlastWaveMath.SpawnApheWaves(p, norm, scale, now, tmp, out n);
                break;
            case ImpactKind.Cookoff:
                BlastWaveMath.SpawnCookoffWaves(p, scale, now, shockwaves, tmp, out n);
                break;
            case ImpactKind.Ricochet:
            case ImpactKind.Penetration:
            case ImpactKind.NonPenetration:
                BlastWaveMath.SpawnArmourImpactFlash(p, norm, scale, now, kind == ImpactKind.Ricochet, tmp, out n);
                break;
        }
        PutBatch(tmp, n, pos, now);
    }

    internal void UpdateFrame(float now, Camera? camera)
    {
        if (disposed || !root.activeSelf) return;
        if (camera == null) { root.SetActive(false); return; }
        var cam = camera.transform.position;
        var camRight = camera.transform.right;
        var camUp = camera.transform.up;
        var live = 0;

        for (var i = 0; i < pool.Length; i++)
        {
            ref var w = ref pool[i];
            if (w.Life <= 0f) continue;
            var age = now - w.Born;
            if (!float.IsFinite(age) || age >= w.Life) { w.Life = 0f; continue; }
            if (age < 0f) continue;
            if (!BlastWaveMath.Evaluate(w, now, out var radius, out var alpha)) continue;

            var centre = U(w.Origin);
            Vector3 rx, ry;
            if (w.Kind == BlastWaveKind.FlashCorona)
            {
                rx = camRight * radius;
                ry = camUp * radius;
            }
            else
            {
                rx = U(w.AxisRight) * radius;
                ry = U(w.AxisUp) * radius;
            }

            var b = live * 4;
            verts[b] = centre - rx - ry;
            verts[b + 1] = centre + rx - ry;
            verts[b + 2] = centre + rx + ry;
            verts[b + 3] = centre - rx + ry;
            var u1 = new Vector2((float)w.Kind, w.Param);
            var col = new Color(w.R, w.G, w.B, alpha);
            uv1[b] = u1; uv1[b + 1] = u1; uv1[b + 2] = u1; uv1[b + 3] = u1;
            cols[b] = col; cols[b + 1] = col; cols[b + 2] = col; cols[b + 3] = col;
            live++;
        }

        LastLive = live;
        var clearEnd = Math.Max(live, prevLive);
        for (var k = live; k < clearEnd; k++)
        {
            var b = k * 4;
            verts[b] = cam; verts[b + 1] = cam; verts[b + 2] = cam; verts[b + 3] = cam;
            cols[b] = Color.clear; cols[b + 1] = Color.clear; cols[b + 2] = Color.clear; cols[b + 3] = Color.clear;
        }

        if (live == 0)
        {
            if (prevLive > 0)
            {
                mesh.vertices = verts;
                mesh.colors = cols;
                prevLive = 0;
            }
            root.SetActive(false);
            return;
        }

        mesh.vertices = verts;
        mesh.uv2 = uv1;
        mesh.colors = cols;
        mesh.bounds = new Bounds(cam, Vector3.one * 6000f);
        prevLive = live;
    }

    internal void Clear()
    {
        if (disposed) return;
        for (var i = 0; i < pool.Length; i++) pool[i].Life = 0f;
        for (var i = 0; i < cols.Length; i++) cols[i] = Color.clear;
        mesh.colors = cols;
        prevLive = 0;
        LastLive = 0;
        lastCount = 0;
        lastSpawnTime = float.NegativeInfinity;
        root.SetActive(false);
    }

    internal void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (root != null) Object.Destroy(root);
        if (mesh != null) Object.Destroy(mesh);
        if (material != null) Object.Destroy(material);
    }

    private static NV N(Vector3 v) => new(v.x, v.y, v.z);
    private static Vector3 U(NV v) => new(v.X, v.Y, v.Z);
}
