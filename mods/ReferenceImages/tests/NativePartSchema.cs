using System.Text.Json;

// Native files allow trailing commas; installed copies of this mod are not schema evidence.
internal sealed class NativePartSchema
{
    private sealed record Example(string File, string RootVersion, string? Type, string Version, HashSet<string> Keys);
    private readonly List<Example> examples = new();
    private static string Version(JsonElement block) => block.GetProperty("v").GetString()!;
    private static HashSet<string> Keys(JsonElement block) => block.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

    public NativePartSchema(IEnumerable<string> paths, string ownFile)
    {
        foreach (string path in paths.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (Path.GetFileName(path).Equals(ownFile, StringComparison.OrdinalIgnoreCase)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            string rootVersion = Version(root), file = Path.GetFileName(path);
            examples.Add(new(file, rootVersion, null, rootVersion, Keys(root)));
            if (root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
                foreach (var component in components.EnumerateArray())
                    if (component.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object && info.TryGetProperty("v", out _))
                        examples.Add(new(file, rootVersion, component.GetProperty("type").GetString(), Version(info), Keys(info)));
        }
    }

    public List<string> Errors(JsonElement root)
    {
        var errors = new List<string>();
        string rootVersion = Version(root);
        Check(root, null, "root");
        int index = 0;
        foreach (var component in root.GetProperty("components").EnumerateArray())
        {
            Check(component.GetProperty("info"), component.GetProperty("type").GetString(), $"components[{index++}].info");
        }
        return errors;

        void Check(JsonElement block, string? type, string label)
        {
            string version = Version(block);
            var keys = Keys(block);
            var sameKind = examples.Where(x => x.Type == type).ToArray();
            var compatible = sameKind.Where(x => x.RootVersion == rootVersion && x.Version == version).ToArray();
            if (compatible.Any(x => x.Keys.SetEquals(keys))) return;
            var nearest = (compatible.Length > 0 ? compatible : sameKind)
                .OrderBy(x => x.Keys.Except(keys).Count() + keys.Except(x.Keys).Count())
                .ThenBy(x => x.File, StringComparer.Ordinal).FirstOrDefault();
            string detail = nearest == null ? "closest native example: none" :
                $"closest native example: {nearest.File} (root v{nearest.RootVersion}, info v{nearest.Version}); missing=[{string.Join(",", nearest.Keys.Except(keys).OrderBy(x => x, StringComparer.Ordinal))}]; extra=[{string.Join(",", keys.Except(nearest.Keys).OrderBy(x => x, StringComparer.Ordinal))}]";
            errors.Add($"{label}: no exact native key set for root v{rootVersion}, type {type ?? "root"}, block v{version}; {detail}");
        }
    }
}
