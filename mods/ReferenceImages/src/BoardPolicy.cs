using System.Numerics;

namespace SprocketReferenceImages;

internal static class BoardPolicy
{
    internal const string PartGuid = "a22720e6-c10c-47d7-96a0-00a03b9d8b20";
    internal const string PartName = "referenceImage", ModelId = "referenceImageModel";
    internal const string NativeDate = "0000.01.01";
    internal const float PictureGap = .003f, HalfDepth = .004f;
    // Native scale is geometry scale; X is depth, Y height, Z picture width.
    internal static float PictureDepth(float sx) => HalfDepth + PictureGap / Math.Abs(sx);
    internal static Vector3 SidePlane(Vector3 v) => new(v.Z,v.Y,-v.X);
    internal static readonly float[] PictureVertices = {
        1,-1,1, 1,-1,-1, 1,1,-1, 1,-1,1, 1,1,-1, 1,1,1,
        -1,-1,1, -1,1,-1, -1,-1,-1, -1,-1,1, -1,1,1, -1,1,-1
    };
    internal static bool SafeName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 240 && name == Path.GetFileName(name) && !name.Contains("..") &&
        !name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c)) &&
        !name.EndsWith(' ') && !name.EndsWith('.') &&
        (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) &&
        !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name.Split('.')[0].ToUpperInvariant());

    internal static void Dimensions(int w, int h, int gpuMax)
    {
        int limit = Math.Min(8192, gpuMax);
        if (w < 1 || h < 1 || w > limit || h > limit || (long)w * h > 32_000_000)
            throw new InvalidDataException($"Image exceeds {limit} px per side or 32 Mpx ({w} x {h}).");
    }

    // Arguments are width and height scale (native Z/Y for the side plane).
    internal static (float Width, float Height, float X, float Y, float ExactHeight) Fit(float sx, float sy, float aspect)
    {
        float w = 2 * Math.Abs(sx), h = 2 * Math.Abs(sy);
        if (!float.IsFinite(w + h + aspect) || w <= 0 || h <= 0 || aspect <= 0) throw new InvalidDataException("Invalid board dimensions.");
        float iw = Math.Min(w, h * aspect), ih = iw / aspect;
        return (iw, ih, iw / w, ih / h, Math.Abs(sx) / aspect);
    }
}

internal readonly record struct Pose(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    internal bool Near(Pose other)
    {
        static bool V(Vector3 a, Vector3 b) => Math.Abs(a.X-b.X)<=1e-4f && Math.Abs(a.Y-b.Y)<=1e-4f && Math.Abs(a.Z-b.Z)<=1e-4f;
        static bool Q(Quaternion a, Quaternion b) => Math.Abs(a.X-b.X)<=1e-4f && Math.Abs(a.Y-b.Y)<=1e-4f && Math.Abs(a.Z-b.Z)<=1e-4f && Math.Abs(a.W-b.W)<=1e-4f;
        return V(Position,other.Position) && V(Scale,other.Scale) && (Q(Rotation,other.Rotation) || Q(Rotation,new Quaternion(-other.Rotation.X,-other.Rotation.Y,-other.Rotation.Z,-other.Rotation.W)));
    }
}

// Every assignment has its own entry, even if two boards occupy identical poses.
// Entries are updated by reference, never deduplicated by pose or file name.
internal sealed class SessionMemory
{
    internal sealed class Item { internal Pose Pose; internal string File = ""; internal bool Mirror; }
    private readonly Dictionary<long, List<Item>> vehicles = new();
    internal Item Remember(long vehicle, Item? previous, Pose pose, string file, bool mirror)
    {
        if (!BoardPolicy.SafeName(file)) throw new InvalidDataException("Unsafe image file name.");
        if (!vehicles.TryGetValue(vehicle, out var items)) vehicles.Add(vehicle, items = new());
        var item = previous ?? new Item();
        if (!items.Contains(item)) items.Add(item);
        item.Pose = pose; item.File = file; item.Mirror = mirror;
        return item;
    }
    internal Item? Match(long vehicle, Pose pose, out int count)
    {
        count = 0; Item? found = null;
        if (vehicles.TryGetValue(vehicle, out var items))
            foreach (var item in items) if (item.Pose.Near(pose)) { count++; found = item; }
        return count == 1 ? found : null;
    }
}
