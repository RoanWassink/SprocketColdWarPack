using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SprocketExpedition.Game;

// Local content-addressed storage. Large community pools stay on disk; only a
// selected tank is read into memory. Exact bytes are checked before native load.
internal static class CampaignBlueprintBlob
{
    internal const long MaximumBytes = 64L * 1024 * 1024;
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static string PathFor(string root, string hash)
    {
        if (hash.Length != 64 || hash.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new InvalidDataException("Invalid blueprint blob identity.");
        return Path.Combine(root, "blueprint-blobs", hash + ".blueprint");
    }
    internal static byte[] Read(string path, string hash)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > MaximumBytes) throw new InvalidDataException("Pinned blueprint is missing or too large.");
        var bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != hash) throw new InvalidDataException("Pinned blueprint byte hash changed.");
        return bytes;
    }
    internal static string Write(string root, string hash, byte[] bytes)
    {
        if (bytes.LongLength is < 1 or > MaximumBytes || Hash(bytes) != hash) throw new InvalidDataException("Invalid pinned blueprint bytes.");
        var path = PathFor(root, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) { _ = Read(path, hash); return path; }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
    internal static byte[] SupportClone(byte[] bytes, string name)
    {
        var root = JsonNode.Parse(bytes) ?? throw new InvalidDataException("Support blueprint has no JSON document.");
        var header = root["header"]?.AsObject() ?? throw new InvalidDataException("Support blueprint has no native header.");
        header["name"] = "Expedition support " + Hash(bytes)[..8] + " | " + name;
        return System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
