using SprocketTelescopicMast;

internal static class CamoTests
{
    internal static int Run()
    {
        int checks = 0, created = 0, disposed = 0, warnings = 0, bound = 0;
        void Check(bool condition) { checks++; if (!condition) throw new Exception("Camo lifecycle/UV regression"); }
        var cache = new PartPaintCache<object>(_ => disposed++, _ => warnings++);
        object Create() { created++; return new object(); }
        object? first = null;
        void Prepare() => cache.Prepare(1, 10, 20, 30, Create, material => { bound++; first ??= material; Check(ReferenceEquals(first, material)); });
        Prepare(); Prepare(); // Same policy called by post-build preparation.
        Check(created == 1 && disposed == 0 && bound == 2);
        for (int i = 0; i < 100; i++) Prepare();
        Check(created == 1); // Rebuilds rebind the retained material, never allocate another.
        cache.Prepare(2, 10, 20, 31, Create, _ => { });
        Check(created == 2);
        cache.Release(99); Check(disposed == 0);
        cache.Release(10); Check(disposed == 2);
        cache.Release(10); Check(disposed == 2);
        first = null; Prepare(); Check(created == 3);
        cache.Prepare(1, 10, 21, 30, Create, _ => { }); // Model pointer reused with another context.
        Check(created == 4 && disposed == 3);
        cache.Prepare(1, 10, 21, 32, Create, _ => { });
        Check(created == 5 && disposed == 4);
        int attempts = 0;
        for (int i = 0; i < 2; i++)
            cache.Prepare(3, 11, 20, 33, () => { attempts++; throw new Exception("create failed"); }, _ => throw new Exception("unreachable"));
        Check(attempts == 1 && warnings == 1);
        for (int i = 0; i < 2; i++)
            cache.Prepare(4, 12, 20, 34, Create, _ => { attempts++; throw new Exception("bind failed"); });
        Check(attempts == 2 && warnings == 2 && created == 6);
        cache.Release(12); Check(disposed == 5); // Failed binding still owns its allocation.
        cache.Clear(); Check(disposed == 6);
        cache.Clear(); Check(disposed == 6);
        var throwingDispose = new PartPaintCache<object>(_ => throw new Exception("destroy failed"), _ => warnings++);
        throwingDispose.Prepare(1, 1, 1, 1, Create, _ => { });
        throwingDispose.Clear(); Check(warnings == 3); // Cleanup isolates native exceptions too.

        // Three independent charts in XY/XZ/YZ planes, including a shared-index quad.
        var vertices = new (float X, float Y, float Z)[] {
            (0,0,0), (2,0,0), (2,1,0), (0,1,0),
            (0,0,0), (0,0,2), (2,0,2),
            (0,0,0), (0,2,0), (0,2,2) };
        int[] triangles = {0,1,2,0,2,3,4,5,6,7,8,9};
        var uv = PaintUv.Create(vertices, triangles);
        Check(uv.Length == vertices.Length);
        Check(uv.All(p => float.IsFinite(p.U) && float.IsFinite(p.V) && p.U > 0 && p.U < 1 && p.V > 0 && p.V < 1));
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = uv[triangles[i]]; var b = uv[triangles[i+1]]; var c = uv[triangles[i+2]];
            Check(Math.Abs((b.U-a.U)*(c.V-a.V)-(b.V-a.V)*(c.U-a.U)) > 0.001f);
        }
        Check(uv.Take(4).Max(p => p.U) < uv.Skip(4).Take(3).Min(p => p.U));
        Check(uv.Take(7).Max(p => p.V) < uv.Skip(7).Min(p => p.V));
        Check(uv.SequenceEqual(PaintUv.Create(vertices, triangles)));
        foreach (var (mesh, count, indices) in new[] {
            (HingeMesh.CreateBase(), 552, 684), (HingeMesh.CreateLeaf(), 792, 1188),
            (CylinderMesh.Create(true), 2880, 3456), (CylinderMesh.Create(false), 1920, 2304),
            (CylinderMesh.Create(true, true), 4032, 5760), (CylinderMesh.Create(false, true), 1920, 2304) })
        {
            Check(mesh.vertices.Length == count && mesh.triangles.Length == indices && mesh.normals.Length == count);
            Check(mesh.uv.Length == count);
            Check(mesh.uv.All(p => float.IsFinite(p.x) && float.IsFinite(p.y)));
            for (int i = 0; i < mesh.triangles.Length; i += 3)
            {
                var a = mesh.uv[mesh.triangles[i]]; var b = mesh.uv[mesh.triangles[i+1]]; var c = mesh.uv[mesh.triangles[i+2]];
                Check(Math.Abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)) > 1e-10f);
            }
        }
        var mount = HingeMesh.CreateMount();
        Check(mount.vertices.Length == 264 && mount.triangles.Length == 324 && mount.uv.Length == 0);
        return checks;
    }
}
