namespace SprocketExpandedBallistics;

internal static class RodResponseDefaults
{
    internal static string Read(string configDirectory)
    {
        var path=Path.Combine(configDirectory,"sprocket.expandedballistics.experimental.json");
        if(File.Exists(path))return File.ReadAllText(path);
        using var stream=typeof(RodResponseDefaults).Assembly.GetManifestResourceStream("ExpandedBallistics.Materials.ExperimentalTemplates")
            ??throw new InvalidOperationException("Missing ERA rod-response defaults.");
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
