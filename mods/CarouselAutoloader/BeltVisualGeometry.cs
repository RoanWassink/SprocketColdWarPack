using System.Numerics;

namespace SprocketCarouselAutoloader;

public enum BeltDetail { Links, RoundsOnly, RibbedBlock }

// Packer units are integer mm, exactly as for the native complete-round rack.
public readonly record struct BeltLayout(int Columns, int Rows, int Layers, double Diameter, double Length)
{
    public double PitchX { get; init; } = 0;
    public double PitchY { get; init; } = 0;
    public double PitchZ { get; init; } = 0;
    public double X0 { get; init; } = 0;
    public double Y0 { get; init; } = 0;
    public double Z0 { get; init; } = 0;
    public double Width => Columns * Diameter + Math.Max(0, Columns - 1) * (PitchX - Diameter);
    public double Depth => Rows * Length + Math.Max(0, Rows - 1) * (PitchZ - Length);
    public int PerLayer => Columns * Rows;
    public int Capacity => PerLayer * Layers;
    public static BeltLayout Of(Spec s)
    {
        int d = (int)Math.Ceiling(s.Caliber), len = (int)Math.Ceiling(s.Projectile + s.Charge);
        if (d <= 0 || len <= 0 || !double.IsFinite(s.X + s.Y + s.Z) || s.X <= 0 || s.Y <= 0 || s.Z <= 0)
            throw new ArgumentException("Invalid Belt geometry");
        var grid = RackGrid.Of(s.X, s.Y, s.Z, d, len, s.Spacing, s.MinPadding, s.MaxPadding);
        return new(grid.Columns, grid.Rows, grid.Layers, d / 1000d, len / 1000d)
        {
            PitchX = grid.PitchX, PitchY = grid.PitchY, PitchZ = grid.PitchZ,
            X0 = -s.X / 2 + s.MinPadding.X, Y0 = -s.Y / 2 + s.MinPadding.Y, Z0 = -s.Z / 2 + s.MinPadding.Z
        };
    }
    public BeltStock Stock(int stock)
    {
        stock = Math.Clamp(stock, 0, Capacity);
        if (stock == 0 || PerLayer == 0) return default;
        int full = stock / PerLayer, rest = stock % PerLayer;
        // An exact multiple exposes the last full layer, rather than an empty next layer.
        int under = rest == 0 ? full - 1 : full;
        int top = rest == 0 ? PerLayer : rest;
        return new(stock, full, rest, top, under, under * PitchY, under * PitchY + Diameter / 2,
            top / Columns, top % Columns);
    }
}

public readonly record struct BeltStock(int Stored, int FullLayers, int Remainder, int TopCount,
    int UnderLayers, double FillHeight, double TopCentre, int FullRows, int PartialColumns);

public sealed class BeltVisualModel
{
    public LoaderModel Model { get; init; } = null!;
    public BeltLayout Layout { get; init; }
    public BeltDetail Detail { get; init; }
    public bool RaisedChute { get; init; }
    // A normalized block reused for the lower fill and the fallback top blocks.
    public MeshPart Fill { get; init; } = null!;
    public MeshPart? Ribs { get; init; }
    public int RibStride { get; init; } = 1;
    public int MaximumTriangles => Model.Triangles + Fill.Triangles * (Detail == BeltDetail.RibbedBlock ? 3 : 1) +
        (Ribs?.Triangles ?? 0) * 2;
}

public static class BeltVisualGeometry
{
    public const int FillMaterial = 5;
    public static BeltDetail SelectDetail(BeltLayout g, int budget = AutoloaderVisualPolicy.TriangleBudget)
    {
        long fixedTriangles = BeltMeshData.Box.Triangles + BeltMeshData.Chute.Triangles + 12;
        long rounds = (long)g.PerLayer * AutoloaderMeshData.Round12.Triangles;
        long links = (long)Math.Max(0, g.Columns - 1) * g.Rows * BeltMeshData.Link.Triangles;
        return fixedTriangles + rounds + links <= budget ? BeltDetail.Links :
            fixedTriangles + rounds <= budget ? BeltDetail.RoundsOnly : BeltDetail.RibbedBlock;
    }

    public static BeltVisualModel Create(Spec s)
    {
        var g = BeltLayout.Of(s);
        if (g.Capacity <= 0 || s.Capacity <= 0 || s.Capacity > g.Capacity) throw new ArgumentException("Belt capacity does not fit");
        var detail = SelectDetail(g);
        var m = new LoaderModel { Spec = s, Slots = s.Capacity, LayoutSlots = g.Capacity, Columns = g.Columns,
            Rows = g.Rows, Layers = g.Layers, DiameterM = g.Diameter, LengthM = g.Length, Detail = detail.ToString() };
        MeshPart Add(string name, PartRole role) { var p = new MeshPart { Name = name, Role = role }; m.Meshes.Add(p); return p; }
        // Imported box is bottom-centred. Every axis follows the actual rack bounds.
        AutoloaderModelGeometry.Place(Add("Belt box", PartRole.Frame), BeltMeshData.Box,
            v => new((float)(v.X * s.X), (float)(v.Y * s.Y - s.Y / 2), (float)(v.Z * s.Z)), false, _ => AutoloaderModelGeometry.Steel);
        double d = g.Diameter;
        double chuteW = Math.Min(2.8 * d, s.X / .9), chuteH = 2.8 * d, chuteL = 3.4 * d;
        double free = s.Z / 2 - (g.Z0 + g.Depth);
        // Actual STL z extent is -.33 .. +.41. The throat must not cover packed rounds.
        bool raised = free < .74 * chuteL;
        double halfRange = Math.Max(0, s.X / 2 - .45 * chuteW);
        double cx = double.IsFinite(s.LoadX) ? Math.Clamp(s.LoadX, -halfRange, halfRange) : Math.Clamp(s.X / 2 - 1.5 * d, -halfRange, halfRange);
        double cy = raised || !double.IsFinite(s.LoadY) ? s.Y / 2 : Math.Clamp(s.LoadY, -s.Y / 2, s.Y / 2);
        double cz = s.Z / 2 - .41 * chuteL;
        AutoloaderModelGeometry.Place(Add("Belt chute", PartRole.Arm), BeltMeshData.Chute,
            v => new((float)(cx + v.X * chuteW), (float)(cy + v.Y * chuteH), (float)(cz + v.Z * chuteL)), false, _ => AutoloaderModelGeometry.Steel);
        var fill = new MeshPart { Name = "Belt fill", Role = PartRole.Frame };
        AutoloaderModelGeometry.Box(fill, 0, 0, 0, 1, 1, 1, FillMaterial);
        MeshPart? ribs = null;
        int stride = 1;
        if (detail != BeltDetail.RibbedBlock)
        {
            for (int row = 0; row < g.Rows; row++) for (int col = 0; col < g.Columns; col++)
            {
                int slot = row * g.Columns + col;
                double x = g.X0 + col * g.PitchX + d / 2, y = g.Y0 + d / 2, z = g.Z0 + row * g.PitchZ;
                AutoloaderModelGeometry.Round(m, AutoloaderMeshData.Round12, slot, x, y, z, d, s.Charge / 1000, g.Length);
                if (detail != BeltDetail.Links || col == g.Columns - 1) continue;
                var link = Add("Belt link " + slot, PartRole.Round); link.Slot = slot + 1;
                // Map the approved link across the actual native spacing, retaining its STL.
                AutoloaderModelGeometry.Place(link, BeltMeshData.Link,
                    v => new((float)(x + v.X * g.PitchX / 1.15), (float)(y - v.Y * d), (float)(z + v.Z * d)), true, _ => AutoloaderModelGeometry.Steel);
            }
        }
        else
        {
            // Two scalable blocks represent full rows + the partial row. Two rib strips sit on
            // those blocks. No per-round geometry, even for very small calibres in large boxes.
            // Normally every column has a 12-triangle ridge on its exact round pitch. Extreme
            // grids retain every nth ridge so the fallback itself is also strictly bounded.
            int available = AutoloaderVisualPolicy.TriangleBudget - m.Triangles - 36;
            stride = Math.Max(1, (int)Math.Ceiling(g.Columns * 24d / available));
            ribs = new MeshPart { Name = "Belt top ribs", Role = PartRole.Round };
            for (int col = 0; col < g.Columns; col += stride)
                AutoloaderModelGeometry.Box(ribs, col * g.PitchX + .42 * d, 0, 0, col * g.PitchX + .58 * d, .08 * d, 1, AutoloaderModelGeometry.Brass);
        }
        return new() { Model = m, Layout = g, Detail = detail, RaisedChute = raised, Fill = fill, Ribs = ribs, RibStride = stride };
    }
}
