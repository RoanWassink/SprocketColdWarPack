namespace SprocketExpandedBallistics;

internal static class MaterialReactionDefaults
{
    // A user's sidecar always takes precedence, including an explicitly disabled
    // catalogue. Invalid custom content is reported by the shared snapshot parser.
    internal static string? Read(string configDirectory)
    {
        var path=Path.Combine(configDirectory,MaterialReactionCatalogue.FileName);
        if(File.Exists(path))return File.ReadAllText(path);
        using var stream=typeof(MaterialReactionDefaults).Assembly.GetManifestResourceStream("ExpandedBallistics.Materials.ReactionTemplates");
        if(stream==null)return null;
        using var reader=new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
