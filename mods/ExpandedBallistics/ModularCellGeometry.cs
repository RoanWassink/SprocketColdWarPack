using System.Numerics;

namespace SprocketExpandedBallistics;

// Canonical dimensions for the new modular Duplet hull panel. These are
// gameplay/authoring estimates, not factory data. X is width, Z is length and
// Y is the native mounting/depth axis.
internal readonly record struct ModularCellSpec(
    double CellWidthM, double CellLengthM, double DepthM,
    double Stage1BottomM, double Stage1TopM,
    double Stage2BottomM, double Stage2TopM,
    double Stage1CoverBottomM, double Stage1CoverTopM,
    double Stage2CoverBottomM, double Stage2CoverTopM,
    double HousingWallM, double HousingBottomM, double CoverGapM,
    // Cosmetic-only divider recess. The physical Housing mesh keeps the full
    // depth; only HousingVisual lowers internal divider tops to show a trough.
    double DividerRecessM,
    // Opt-in Relikt styles; default zero preserves the original Duplet path.
    int ReliktStyle = 0, int FixedRows = 0,
    // Opt-in modular Nizh/Duplet panels (v0.20.0): 1 Nizh panel, 2 Duplet turret, 3 Duplet hull,
    // 4 Duplet side module. Zero keeps the original Duplet path.
    int DupletStyle = 0)
{
    // Slim v8 standard cell, matching the KhSChKV-34 external envelope
    // 125 x 250 x 36 mm (X width, Z length, Y depth). Both paid slabs stay
    // 12 mm: 0-4 pan, Stage1 4-16, Stage2 16-28, covers 28-32 and 32-36.
    // The 11 mm inter-cell cover seam (~5.5 mm per side) is unchanged; the
    // divider trough is 2 mm because the envelope is much shallower.
    internal static readonly ModularCellSpec DupletV1 = new(
        0.125, 0.250, 0.036,
        0.004, 0.016, 0.016, 0.028,
        0.028, 0.032, 0.032, 0.0360000,
        0.004, 0.004, 0.0055, 0.0020);

    // Slim v8 slim cell, matching the KhSChKV-19 external envelope
    // 125 x 250 x 26 mm. A 2 mm pan and 2 mm covers leave 20 mm for two
    // 10 mm paid slabs: 2-12 and 12-22, covers 22-24 and 24-26.
    internal static readonly ModularCellSpec DupletSlim = new(
        0.125, 0.250, 0.026,
        0.002, 0.012, 0.012, 0.022,
        0.022, 0.024, 0.024, 0.0260000,
        0.002, 0.002, 0.0055, 0.0010);

    internal static readonly ModularCellSpec ReliktTurret = Relikt(1, .150, .015, .006);
    internal static readonly ModularCellSpec ReliktHull = Relikt(2, .136, .008, .004);
    internal static readonly ModularCellSpec ReliktSide = Relikt(3, .136, .008, .003) with { FixedRows = 2 };
    private static ModularCellSpec Relikt(int style, double depth, double lid, double wall)
        => new(.270, .150, depth, .004, .017, .017, .030,
            depth-lid, depth, depth-lid, depth, wall, .004, .002, .002, style);

    // Layer stack (mm): pan | layer | [intermediate plate | layer] | front plate. Cell pitch 125 x 250 mm.
    // Nizh panel: 8 mm front plate + one 36 mm Nozh layer (nizh34Active window).
    // Duplet: two 12 mm layers (dupletHullActive window, one stage each); plates tuned to the kg/m2 targets.
    internal static readonly ModularCellSpec NizhPanel = Panel(1, .0025, 1, .036, 0, .008, .002);
    internal static readonly ModularCellSpec DupletTurret = Panel(2, .004, 2, .012, .012, .020, .003);
    internal static readonly ModularCellSpec DupletHull = Panel(3, .004, 2, .012, .010, .015, .0025);
    internal static readonly ModularCellSpec DupletSide = Panel(4, .004, 2, .012, .010, .010, .0025) with { FixedRows = 2 };
    private static ModularCellSpec Panel(int style, double pan, int layers, double layer, double inter, double front, double wall)
    {
        var l1t = pan + layer;
        if (layers == 1)
        {
            var depth1 = l1t + front;
            return new(.125, .250, depth1, pan, l1t, l1t, depth1, l1t, depth1, l1t, depth1, wall, pan, .004, .001, 0, 0, style);
        }
        var l2b = l1t + inter;
        var l2t = l2b + layer;
        var depth = l2t + front;
        return new(.125, .250, depth, pan, l1t, l2b, l2t, l1t, l2b, l2t, depth, wall, pan, .004, .001, 0, 0, style);
    }

    // Panels with procedural (non-slab) visuals: Relikt and the modular Nizh/Duplet panels.
    internal bool ProceduralPanel => ReliktStyle != 0 || DupletStyle != 0;

    internal static ModularCellSpec ForVariant(string? variant) => variant switch
    {
        "nizhPanel" => NizhPanel,
        "dupletTurret" => DupletTurret,
        "dupletHull" => DupletHull,
        "dupletSide" => DupletSide,
        "slim" => DupletSlim,
        "reliktTurret" => ReliktTurret,
        "reliktHull" => ReliktHull,
        "reliktSide" => ReliktSide,
        _ => DupletV1,
    };

    internal bool Valid => double.IsFinite(CellWidthM) && double.IsFinite(CellLengthM) && double.IsFinite(DepthM)
        && CellWidthM > 0 && CellLengthM > 0 && DepthM > 0
        && Stage1TopM > Stage1BottomM && Stage2TopM > Stage2BottomM
        && Stage1CoverTopM > Stage1CoverBottomM && Stage2CoverTopM > Stage2CoverBottomM
        && Stage1BottomM >= 0 && Stage2TopM <= DepthM && Stage2CoverTopM <= DepthM
        && HousingWallM > 0 && HousingBottomM > 0 && CoverGapM >= 0
        && DividerRecessM >= 0 && DividerRecessM < DepthM
        && CellWidthM > 2 * CoverGapM && CellLengthM > 2 * CoverGapM;
}

internal readonly record struct ModularCellLayout(int CountU, int CountV)
{
    internal int CellCount => CountU * CountV;
    internal int Index(int x, int y) => x + y * CountU;
    internal bool Contains(int x, int y) => x >= 0 && x < CountU && y >= 0 && y < CountV;
}

internal enum ModularPartKind
{
    Housing,
    HousingVisual,
    Stage1Collision,
    Stage2Collision,
    Stage1Visual,
    Stage2Visual,
}

internal sealed class ModularMeshData
{
    internal readonly Vector3[] Vertices;
    internal readonly int[] Triangles;
    internal readonly int[] TriangleCells;
    internal ModularMeshData(Vector3[] vertices, int[] triangles, int[] triangleCells)
    {
        Vertices = vertices; Triangles = triangles; TriangleCells = triangleCells;
    }
    internal int CellCount => TriangleCells.Length == 0 ? 0 : TriangleCells.Max() + 1;
}

// Pure, deterministic per-count geometry. Every supported layout is built from
// whole cells; no authored maximum lattice is stretched and no triangle-centroid
// guess is used for cover ownership. TriangleCells is an explicit ownership map:
// -1 = shared frame, otherwise cell index (x + y*CountU).
internal static class ModularCellGeometry
{
    // Bump when the canonical cell layout changes. Runtime mesh/binding caches
    // include this revision so a hot-reloaded generation cannot silently reuse
    // a mesh authored for a previous envelope.
    internal const int AuthoringRevision = 10;

    // A fully spent layer requires every current cell index to be present.
    // Retained ledger coordinates from a larger previous layout must not hide a
    // smaller reconstructed layer merely because their count is high enough.
    internal static bool AllSpent(IReadOnlySet<int>? spent,int countU,int countV)
        =>spent!=null&&countU>0&&countV>0&&spent.Count>=countU*countV
            &&Enumerable.Range(0,countU*countV).All(spent.Contains);

    internal static bool TryLayout(double scaleX, double scaleZ, ModularCellSpec spec,
        int maxCountU, int maxCountV, out ModularCellLayout layout)
    {
        layout = default;
        if (!spec.Valid || maxCountU < 1 || maxCountV < 1 || maxCountU > 64 || maxCountV > 64) return false;
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleZ) || scaleX == 0 || scaleZ == 0) return false;
        var u = EraCellPartition.CountForScale(Math.Abs(scaleX), maxCountU);
        var v = EraCellPartition.CountForScale(Math.Abs(scaleZ), maxCountV);
        if (spec.FixedRows > 0) v = spec.FixedRows;
        if (u < 1 || v < 1 || (long)u * v > 4096) return false;
        layout = new(u, v);
        return true;
    }

    internal static (double X0, double X1, double Z0, double Z1) CellBounds(
        ModularCellLayout layout, ModularCellSpec spec, int x, int y)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        var x0 = -width / 2 + x * spec.CellWidthM;
        var z0 = -length / 2 + y * spec.CellLengthM;
        return (x0, x0 + spec.CellWidthM, z0, z0 + spec.CellLengthM);
    }

    internal static ModularMeshData Build(ModularPartKind kind, ModularCellLayout layout,
        ModularCellSpec spec, IReadOnlySet<int>? spentCells = null,
        double compensateX = 1, double compensateZ = 1, bool mountingDetail = false, bool coarse = false)
    {
        if (!spec.Valid || layout.CountU < 1 || layout.CountV < 1) throw new InvalidDataException("Invalid modular cell layout.");
        if (!double.IsFinite(compensateX) || !double.IsFinite(compensateZ) || compensateX <= 0 || compensateZ <= 0)
            throw new InvalidDataException("Invalid modular scale compensation.");
        if (spec.ReliktStyle != 0)
            return ReliktModularGeometry.Build(kind, layout, spec, spentCells, compensateX, compensateZ, coarse);
        if (spec.DupletStyle != 0)
        {
            if (kind == ModularPartKind.Stage2Collision && DupletModularGeometry.SingleLayer(spec))
                throw new InvalidDataException("The Nizh panel has a single active layer.");
            if (kind is ModularPartKind.Housing or ModularPartKind.HousingVisual or ModularPartKind.Stage1Visual or ModularPartKind.Stage2Visual)
                return DupletModularGeometry.Build(kind, layout, spec, spentCells, compensateX, compensateZ, coarse);
            // Stage collisions: the shared flat slab builder below (closed plates, exact layer thickness).
        }
        var builder = new Builder(1 / compensateX, 1 / compensateZ);
        switch (kind)
        {
            case ModularPartKind.Housing:
                BuildHousing(builder, layout, spec, false);
                break;
            case ModularPartKind.HousingVisual:
                BuildHousing(builder, layout, spec, true);
                break;
            case ModularPartKind.Stage1Collision:
                BuildSlab(builder, layout, spec, false, false, null, false);
                break;
            case ModularPartKind.Stage2Collision:
                BuildSlab(builder, layout, spec, true, false, null, false);
                break;
            case ModularPartKind.Stage1Visual:
                BuildSlab(builder, layout, spec, false, true, spentCells ?? new HashSet<int>(), false);
                break;
            case ModularPartKind.Stage2Visual:
                BuildSlab(builder, layout, spec, true, true, spentCells ?? new HashSet<int>(), mountingDetail);
                break;
            default:
                throw new InvalidDataException("Unknown modular part kind.");
        }
        var data = builder.Build();
        if (data.Triangles.Count == 0 && !(kind is ModularPartKind.Stage1Visual or ModularPartKind.Stage2Visual && AllSpent(spentCells,layout.CountU,layout.CountV)) || data.Triangles.Count > 24000 || data.Vertices.Count > 8000 ||
            data.Vertices.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
            throw new InvalidDataException("Generated modular geometry is empty or outside the supported bound.");
        return new(data.Vertices.ToArray(), data.Triangles.ToArray(), data.TriangleCells.ToArray());
    }

    internal static double SignedVolume(ModularMeshData mesh)
    {
        double volume = 0;
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Triangles[i]];
            var b = mesh.Vertices[mesh.Triangles[i + 1]];
            var c = mesh.Vertices[mesh.Triangles[i + 2]];
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }
        return volume;
    }

    internal static double ShortestEdgeMm(ModularMeshData mesh)
    {
        var shortest = double.PositiveInfinity;
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
            for (var j = 0; j < 3; j++)
            {
                var a = mesh.Vertices[mesh.Triangles[i + j]];
                var b = mesh.Vertices[mesh.Triangles[i + (j + 1) % 3]];
                var length = (a - b).Length();
                if (length <= 1e-9 || !double.IsFinite(length)) return double.NaN;
                shortest = Math.Min(shortest, length);
            }
        return shortest * 1000;
    }

    internal static bool IsWatertightByPosition(ModularMeshData mesh)
    {
        var edges = new Dictionary<(long, long, long, long, long, long), int>();
        for (var i = 0; i < mesh.Triangles.Length; i += 3)
            for (var j = 0; j < 3; j++)
            {
                var a = Key(mesh.Vertices[mesh.Triangles[i + j]]);
                var b = Key(mesh.Vertices[mesh.Triangles[i + (j + 1) % 3]]);
                var edge = Compare(a, b) <= 0 ? (a.A, a.B, a.C, b.A, b.B, b.C) : (b.A, b.B, b.C, a.A, a.B, a.C);
                edges[edge] = edges.TryGetValue(edge, out var count) ? count + 1 : 1;
            }
        return edges.Count > 0 && edges.Values.All(count => count % 2 == 0);
    }

    // Expected paid volume is per-cell slab area * thickness * whole-cell count.
    internal static double ExpectedSlabVolume(ModularCellLayout layout, ModularCellSpec spec)
        => layout.CellCount * (spec.ReliktStyle != 0 ? ReliktModularGeometry.ActiveVolumeM3
            : spec.CellWidthM * spec.CellLengthM * (spec.Stage1TopM - spec.Stage1BottomM));

    internal static double ExpectedCoverVolume(ModularCellLayout layout, ModularCellSpec spec, bool outer)
    {
        var bottom = outer ? spec.Stage2CoverBottomM : spec.Stage1CoverBottomM;
        var top = outer ? spec.Stage2CoverTopM : spec.Stage1CoverTopM;
        var width = spec.CellWidthM - 2 * spec.CoverGapM;
        var length = spec.CellLengthM - 2 * spec.CoverGapM;
        return layout.CellCount * width * length * (top - bottom);
    }

    internal static double BaseHousingMass(ModularCellSpec spec, double densityKgM3 = 7850)
        => SignedVolume(Build(ModularPartKind.Housing, new(1, 1), spec with { FixedRows = 0 })) * densityKgM3;

    internal static double BaseStageMass(ModularCellSpec spec, double densityKgM3 = 4300)
        => spec.ReliktStyle != 0 ? 1.4 : ExpectedSlabVolume(new(1, 1), spec) * densityKgM3;

    private static void BuildHousing(Builder builder, ModularCellLayout layout, ModularCellSpec spec,
        bool recessInternalDividers)
    {
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        var x0 = -width / 2; var x1 = width / 2;
        var z0 = -length / 2; var z1 = length / 2;
        var w = spec.HousingWallM;
        // Cosmetic render shell: internal divider tops stop below the outer lid
        // so the wider cover gaps read as a real trough. Outer walls stay at the
        // unchanged envelope. The physical Housing collision mesh passes false.
        var dividerTop = recessInternalDividers ? spec.DepthM - spec.DividerRecessM : spec.DepthM;
        // Bottom pan.
        builder.Box(x0, x1, 0, spec.HousingBottomM, z0, z1, -1);
        // Outer walls, on top of the pan.
        builder.Box(x0, x0 + w, spec.HousingBottomM, spec.DepthM, z0, z1, -1);
        builder.Box(x1 - w, x1, spec.HousingBottomM, spec.DepthM, z0, z1, -1);
        builder.Box(x0 + w, x1 - w, spec.HousingBottomM, spec.DepthM, z0, z0 + w, -1);
        builder.Box(x0 + w, x1 - w, spec.HousingBottomM, spec.DepthM, z1 - w, z1, -1);
        // Whole-cell dividers. They are part of the single frame, not extra cells.
        for (var x = 1; x < layout.CountU; x++)
        {
            var cx = x0 + x * spec.CellWidthM;
            builder.Box(cx - w / 2, cx + w / 2, spec.HousingBottomM, dividerTop, z0 + w, z1 - w, -1);
        }
        for (var y = 1; y < layout.CountV; y++)
        {
            var cz = z0 + y * spec.CellLengthM;
            builder.Box(x0 + w, x1 - w, spec.HousingBottomM, dividerTop, cz - w / 2, cz + w / 2, -1);
        }
    }

    private static void BuildSlab(Builder builder, ModularCellLayout layout, ModularCellSpec spec,
        bool outer, bool withCovers, IReadOnlySet<int>? spent, bool mountingDetail)
    {
        var y0 = outer ? spec.Stage2BottomM : spec.Stage1BottomM;
        var y1 = outer ? spec.Stage2TopM : spec.Stage1TopM;
        var width = layout.CountU * spec.CellWidthM;
        var length = layout.CountV * spec.CellLengthM;
        var x0 = -width / 2; var x1 = width / 2;
        var z0 = -length / 2; var z1 = length / 2;
        for (var ix = 0; ix < layout.CountU; ix++)
            for (var iy = 0; iy < layout.CountV; iy++)
            {
                var cell = layout.Index(ix, iy);
                if (spent != null && spent.Contains(cell)) continue;
                var b = CellBounds(layout, spec, ix, iy);
                builder.Quad(b.X0, b.Z0, b.X1, b.Z1, y1, true, cell);   // top +Y
                builder.Quad(b.X0, b.Z0, b.X1, b.Z1, y0, false, cell);  // bottom -Y
            }
        // Only collision owns the closed slab perimeter. The shared render
        // housing already encloses these sides at exactly the same X/Z planes;
        // drawing the slab sides too causes coplanar z-fighting. Cell top/bottom
        // and spent cover ownership are unchanged, as is paid closed collision.
        if(!withCovers)
        {
        // Shared perimeter walls close the physical slab union. They carry no cell of
        // their own, so removing one cover cell never removes the outer frame.
        // Split each wall at cell boundaries so every shared edge is paired.
        for (var iy = 0; iy < layout.CountV; iy++)
        {
            var b = CellBounds(layout, spec, 0, iy);
            builder.WallX(x1, b.Z0, b.Z1, y0, y1, true, -1);
            builder.WallX(x0, b.Z0, b.Z1, y0, y1, false, -1);
        }
        for (var ix = 0; ix < layout.CountU; ix++)
        {
            var b = CellBounds(layout, spec, ix, 0);
            builder.WallZ(z1, b.X0, b.X1, y0, y1, true, -1);
            builder.WallZ(z0, b.X0, b.X1, y0, y1, false, -1);
        }
        }
        if (!withCovers) return;
        var gap = spec.CoverGapM;
        var cb = outer ? spec.Stage2CoverBottomM : spec.Stage1CoverBottomM;
        var ct = outer ? spec.Stage2CoverTopM : spec.Stage1CoverTopM;
        for (var ix = 0; ix < layout.CountU; ix++)
            for (var iy = 0; iy < layout.CountV; iy++)
            {
                var cell = layout.Index(ix, iy);
                if (spent != null && spent.Contains(cell)) continue;
                var b = CellBounds(layout, spec, ix, iy);
                if(mountingDetail)
                    builder.MountingCover(b.X0+gap,b.X1-gap,cb,ct,b.Z0+gap,b.Z1-gap,cell);
                else builder.Box(b.X0 + gap, b.X1 - gap, cb, ct, b.Z0 + gap, b.Z1 - gap, cell);
            }
    }

    private sealed class Builder
    {
        private readonly double inverseX, inverseZ;
        private readonly List<Vector3> vertices = new();
        private readonly Dictionary<(long, long, long), int> lookup = new();
        private readonly List<int> triangles = new();
        private readonly List<int> cells = new();

        internal Builder(double inverseX, double inverseZ)
        { this.inverseX = inverseX; this.inverseZ = inverseZ; }

        internal void Box(double x0, double x1, double y0, double y1, double z0, double z1, int cell)
        {
            Quad(x0, z0, x1, z1, y1, true, cell);
            Quad(x0, z0, x1, z1, y0, false, cell);
            QuadSide(x0, x1, y0, y1, z1, 2, cell);
            QuadSide(x0, x1, y0, y1, z0, 3, cell);
            QuadSide(z0, z1, y0, y1, x1, 0, cell);
            QuadSide(z0, z1, y0, y1, x0, 1, cell);
        }

        // Two blind octagonal countersunk mounting recesses. They stay fully
        // inside the cover envelope; this mesh is render-only and never changes
        // paid/collision armour. All triangles inherit their exact spent cell.
        internal void MountingCover(double x0,double x1,double y0,double y1,double z0,double z1,int cell)
        {
            Quad(x0,z0,x1,z1,y0,false,cell);
            for(var side=0;side<4;side++)BoxSide(x0,x1,y0,y1,z0,z1,side,cell);
            const double radius=.006;
            var band=radius*1.5;
            var first=z0+.016;var second=z1-.016;
            Quad(x0,z0,x1,first-band,y1,true,cell);
            Quad(x0,first+band,x1,second-band,y1,true,cell);
            Quad(x0,second+band,x1,z1,y1,true,cell);
            RecessBand(x0,x1,first,radius,y1,cell);
            RecessBand(x0,x1,second,radius,y1,cell);
        }
        private void RecessBand(double x0,double x1,double cz,double radius,double top,int cell)
        {
            var cx=(x0+x1)*.5;var lo=cz-radius*1.5;var hi=cz+radius*1.5;
            var outer=new (double X,double Y,double Z)[]{(x0,top,lo),(x0,top,cz),(x0,top,hi),(cx,top,hi),
                (x1,top,hi),(x1,top,cz),(x1,top,lo),(cx,top,lo)};
            var inner=new (double X,double Y,double Z)[8];
            var floor=new (double X,double Y,double Z)[8];
            for(var i=0;i<8;i++)
            {
                var angle=(-135-i*45)*Math.PI/180;
                inner[i]=(cx+radius*Math.Cos(angle),top,cz+radius*Math.Sin(angle));
                floor[i]=(cx+.0023*Math.Cos(angle),top-.0012,cz+.0023*Math.Sin(angle));
            }
            for(var i=0;i<8;i++)
            {
                var next=(i+1)%8;
                AddQuad(outer[i],outer[next],inner[next],inner[i],cell);
                AddQuad(inner[i],inner[next],floor[next],floor[i],cell);
                Triangle(Vertex((cx,top-.0012,cz)),Vertex(floor[i]),Vertex(floor[next]),cell);
            }
        }

        // Top/bottom XZ quad. top=true gives +Y winding.
        internal void Quad(double x0, double z0, double x1, double z1, double y, bool top, int cell)
        {
            if (top)
            {
                AddQuad((x0, y, z0), (x0, y, z1), (x1, y, z1), (x1, y, z0), cell);
            }
            else
            {
                AddQuad((x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1), cell);
            }
        }

        // Outer walls of a slab union. side 0=+X, 1=-X, 2=+Z, 3=-Z.
        internal void BoxSide(double x0, double x1, double y0, double y1, double z0, double z1, int side, int cell)
        {
            switch (side)
            {
                case 0: AddQuad((x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1), cell); break;
                case 1: AddQuad((x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0), cell); break;
                case 2: AddQuad((x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1), cell); break;
                case 3: AddQuad((x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0), cell); break;
            }
        }

        // Wall in a constant-X plane, spanning one cell edge in Z.
        internal void WallX(double x, double z0, double z1, double y0, double y1, bool positive, int cell)
        {
            if (positive) AddQuad((x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1), cell);
            else AddQuad((x, y0, z0), (x, y0, z1), (x, y1, z1), (x, y1, z0), cell);
        }

        // Wall in a constant-Z plane, spanning one cell edge in X.
        internal void WallZ(double z, double x0, double x1, double y0, double y1, bool positive, int cell)
        {
            if (positive) AddQuad((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z), cell);
            else AddQuad((x0, y0, z), (x0, y1, z), (x1, y1, z), (x1, y0, z), cell);
        }

        // Side wall in the plane of a constant X or Z coordinate.
        // side 0=+X, 1=-X, 2=+Z, 3=-Z; a0..a1 is the other horizontal axis.
        private void QuadSide(double a0, double a1, double y0, double y1, double fixedValue, int side, int cell)
        {
            if (side == 0) AddQuad((fixedValue, y0, a0), (fixedValue, y1, a0), (fixedValue, y1, a1), (fixedValue, y0, a1), cell);
            else if (side == 1) AddQuad((fixedValue, y0, a0), (fixedValue, y0, a1), (fixedValue, y1, a1), (fixedValue, y1, a0), cell);
            else if (side == 2) AddQuad((a0, y0, fixedValue), (a1, y0, fixedValue), (a1, y1, fixedValue), (a0, y1, fixedValue), cell);
            else AddQuad((a0, y0, fixedValue), (a0, y1, fixedValue), (a1, y1, fixedValue), (a1, y0, fixedValue), cell);
        }

        private void AddQuad((double X, double Y, double Z) a, (double X, double Y, double Z) b,
            (double X, double Y, double Z) c, (double X, double Y, double Z) d, int cell)
        {
            var ia = Vertex(a); var ib = Vertex(b); var ic = Vertex(c); var id = Vertex(d);
            Triangle(ia, ib, ic, cell);
            Triangle(ia, ic, id, cell);
        }

        private void Triangle(int a, int b, int c, int cell)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c); cells.Add(cell);
        }

        private int Vertex((double X, double Y, double Z) p)
        {
            var v = new Vector3((float)(p.X * inverseX), (float)p.Y, (float)(p.Z * inverseZ));
            var key = Key(v);
            if (lookup.TryGetValue(key, out var index)) return index;
            index = vertices.Count;
            vertices.Add(v);
            lookup[key] = index;
            return index;
        }

        internal (List<Vector3> Vertices, List<int> Triangles, List<int> TriangleCells) Build()
            => (vertices, triangles, cells);
    }

    private static (long A, long B, long C) Key(Vector3 v)
        => ((long)Math.Round(v.X * 1e7), (long)Math.Round(v.Y * 1e7), (long)Math.Round(v.Z * 1e7));

    private static int Compare((long A, long B, long C) a, (long A, long B, long C) b)
    {
        var c = a.A.CompareTo(b.A); if (c != 0) return c;
        c = a.B.CompareTo(b.B); if (c != 0) return c;
        return a.C.CompareTo(b.C);
    }
}
