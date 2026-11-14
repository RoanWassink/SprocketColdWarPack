using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SprocketExpedition.Game;

// Native Custom Battle metadata, not scenario objectives. Scene/spawn acceptance
// remains a separate live check. These maps support both teams at our 8/12 caps.
internal static class CampaignMapPool
{
    internal static readonly string[] CandidateNames = { "Defence", "Fields", "Dunes" };
    internal sealed record Map(string Id, string Hash);

    internal static Map Read(string directory, string id)
    {
        if (!CandidateNames.Contains(id, StringComparer.Ordinal))
            throw new InvalidDataException($"Map '{id}' is outside the checked Expedition candidate pool.");
        var bytes = File.ReadAllBytes(Path.Combine(directory, id + ".json"));
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        var spawns = root.GetProperty("TeamSpawnsSupported").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (root.GetProperty("Identifier").GetString() != id || !root.GetProperty("Deathmatch").GetBoolean()
            || root.GetProperty("TeamCount").GetInt32() < 2 || spawns.Length < 2 || spawns[0] < 8 || spawns[1] < 12)
            throw new InvalidDataException($"Map '{id}' does not support the campaign's native Custom Battle teams/spawn caps.");
        var payload = bytes.Concat(Encoding.UTF8.GetBytes("\nnative.default-spawns.v1")).ToArray();
        return new Map(id, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    }
}
