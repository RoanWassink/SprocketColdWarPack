using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Object=UnityEngine.Object;
using NV=System.Numerics.Vector3;
namespace SprocketVisualEffects;

// 0.5.0 HDR velocity-stretched spark layer in ONE dynamic mesh / ONE draw call.
// Vertex layout (must match ImpactSpark.shader):
//   vertices=world corner, uv=(u -1..1 width, v 0..1 head->tail),
//   uv2=(brightnessMul, hotCore01), colors=(tint rgb, alpha).
internal sealed class SparkRenderer
{
    private readonly Spark[] pool;
    private int next, prevLive;
    private readonly GameObject root;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly Il2CppStructArray<Vector3> verts;
    private readonly Il2CppStructArray<Vector2> uv0, uv1;
    private readonly Il2CppStructArray<Color> cols;
    private readonly System.Random rng = new(5678);
    private float lastTime;
    private bool hasLastTime, disposed;
    internal int Capacity => pool.Length;
    internal int LastLive { get; private set; }
    internal SparkTuning Tuning { get; set; } = SparkTuning.Default;

    internal SparkRenderer(Transform parent, Material source, int capacity)
    {
        var n = EffectMath.SparkCapacity(capacity);
        pool = new Spark[n];
        material = new Material(source) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3002 };
        root = new GameObject("Visual effects sparks") { hideFlags = HideFlags.HideAndDontSave };
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
            uv0[k * 4] = new Vector2(-1, 0);
            uv0[k * 4 + 1] = new Vector2(1, 0);
            uv0[k * 4 + 2] = new Vector2(1, 1);
            uv0[k * 4 + 3] = new Vector2(-1, 1);
            int b = k * 4, o = k * 6;
            tri[o] = b; tri[o + 1] = b + 1; tri[o + 2] = b + 2;
            tri[o + 3] = b; tri[o + 4] = b + 2; tri[o + 5] = b + 3;
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

    private void PutBatch(Span<Spark> batch, int count)
    {
        if (count <= 0) return;
        for (var i = 0; i < count; i++)
        {
            pool[next] = batch[i];
            next = (next + 1) % pool.Length;
        }
        root.SetActive(true);
    }

    internal int EmitMuzzle(Vector3 muzzle, Vector3 forward, float scale, float groundY, float lod)
    {
        if (disposed || Tuning.Intensity <= 0f || lod <= 0.25f) return 0;
        Span<Spark> tmp = stackalloc Spark[64];
        var t = new SparkTuning(Tuning.Intensity * lod, Tuning.Scale);
        SparkMath.SpawnMuzzleSparks(rng, N(muzzle), N(forward), scale, groundY, t, tmp, out var n);
        PutBatch(tmp, n);
        return n;
    }

    internal int EmitImpact(ImpactKind kind, Vector3 pos, Vector3 normal, Vector3 inVelocity, float scale, float groundY, float lod)
    {
        if (disposed || Tuning.Intensity <= 0f || lod <= 0.1f) return 0;
        Span<Spark> tmp = stackalloc Spark[200];
        var t = new SparkTuning(Tuning.Intensity * lod, Tuning.Scale);
        var p = N(pos); var norm = N(normal); var vel = N(inVelocity);
        int n;
        switch (kind)
        {
            case ImpactKind.Ricochet:
                SparkMath.SpawnRicochetSparks(rng, p, norm, vel, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Penetration:
                SparkMath.SpawnPenetrationSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.NonPenetration:
                SparkMath.SpawnNonPenetrationSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Explosion:
                SparkMath.SpawnExplosionSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Era:
                SparkMath.SpawnEraSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Heat:
                SparkMath.SpawnHeatSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Hesh:
                SparkMath.SpawnHeshSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Aphe:
                SparkMath.SpawnApheSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
            case ImpactKind.Cookoff:
                SparkMath.SpawnCookoffSparks(rng, p, scale, groundY, t, tmp, out n);
                break;
            default:
                SparkMath.SpawnGroundSparks(rng, p, norm, scale, groundY, t, tmp, out n);
                break;
        }
        PutBatch(tmp, n);
        return n;
    }

    internal void UpdateFrame(float now, Camera? camera)
    {
        if (disposed || !root.activeSelf) return;
        if (camera == null) { root.SetActive(false); return; }
        if (!hasLastTime) { hasLastTime = true; lastTime = now; }
        var dt = float.IsFinite(now) ? Math.Clamp(now - lastTime, 0f, 0.05f) : 0f;
        lastTime = now;

        var cam = camera.transform.position;
        var camN = N(cam);
        var live = 0;

        for (var i = 0; i < pool.Length; i++)
        {
            if (pool[i].Life <= 0f) continue;
            if (dt > 0f)
            {
                SparkMath.Step(ref pool[i], dt);
                if (pool[i].Life <= 0f) continue;
            }
            SparkMath.ComputeQuad(pool[i], camN, out var q0, out var q1, out var q2, out var q3,
                out var alpha, out var br, out var hot, out var cr, out var cg, out var cb);
            if (alpha <= 1e-4f) continue;
            var b = live * 4;
            verts[b] = U(q0); verts[b + 1] = U(q1); verts[b + 2] = U(q2); verts[b + 3] = U(q3);
            var u1 = new Vector2(br, hot);
            var col = new Color(cr, cg, cb, alpha);
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
            hasLastTime = false;
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
        hasLastTime = false;
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
