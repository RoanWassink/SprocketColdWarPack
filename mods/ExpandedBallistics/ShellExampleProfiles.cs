using System.Text.Json.Nodes;
namespace SprocketExpandedBallistics;
internal static class ShellExampleProfiles
{
    internal static JsonArray Templates()
    {
        return JsonNode.Parse(ReleaseProfiles.Defaults())!["profiles"]!.AsArray();
    }
}
