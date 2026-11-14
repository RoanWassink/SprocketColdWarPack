namespace SprocketTelescopicMast;

// Render attributes only: do not change vertices, indices, normals or collision meshes.
// Existing generators use separate vertices for each quad/triangle, so connected
// index islands are planar charts. Pack them into disjoint padded atlas cells.
internal static class PaintUv
{
    internal static (float U, float V)[] Create((float X, float Y, float Z)[] vertices, int[] triangles)
    {
        var parents = Enumerable.Range(0, vertices.Length).ToArray();
        int Root(int v) { while (parents[v] != v) { parents[v] = parents[parents[v]]; v = parents[v]; } return v; }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            parents[Root(triangles[i + 1])] = Root(triangles[i]);
            parents[Root(triangles[i + 2])] = Root(triangles[i]);
        }
        var charts = Enumerable.Range(0, vertices.Length).GroupBy(Root).Select(x => x.ToArray()).ToArray();
        int grid = (int)Math.Ceiling(Math.Sqrt(charts.Length));
        var uv = new (float U, float V)[vertices.Length];
        for (int chart = 0; chart < charts.Length; chart++)
        {
            var indices = charts[chart];
            var a = vertices[indices[0]]; var b = vertices[indices[1]]; var c = vertices[indices[2]];
            float nx = (b.Y-a.Y)*(c.Z-a.Z)-(b.Z-a.Z)*(c.Y-a.Y);
            float ny = (b.Z-a.Z)*(c.X-a.X)-(b.X-a.X)*(c.Z-a.Z);
            float nz = (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            int axis = Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz) ? 0 : Math.Abs(ny) >= Math.Abs(nz) ? 1 : 2;
            (float U, float V) Project(int i) => axis == 0 ? (vertices[i].Y, vertices[i].Z) : axis == 1 ? (vertices[i].X, vertices[i].Z) : (vertices[i].X, vertices[i].Y);
            float minU = indices.Min(i => Project(i).U), minV = indices.Min(i => Project(i).V);
            float spanU = indices.Max(i => Project(i).U) - minU, spanV = indices.Max(i => Project(i).V) - minV;
            foreach (int i in indices)
            {
                var p = Project(i);
                uv[i] = ((chart % grid + .02f + .96f * (p.U-minU)/Math.Max(spanU, 1e-8f))/grid,
                    (chart / grid + .02f + .96f * (p.V-minV)/Math.Max(spanV, 1e-8f))/grid);
            }
        }
        return uv;
    }
}
