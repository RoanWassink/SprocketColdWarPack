namespace SprocketExpandedBallistics;

/// <summary>
/// native-data/Technology plus the stock-value copies in tests/TestData/Technology (RolledHomogeneousArmour, SheetMetal:
/// test data only, not shipped), merged into one scratch folder so tests can read the whole technology set.
/// </summary>
internal static class TestTechnologyDirectory
{
    internal static string Merged(string sourceRoot)
    {
        var target = Path.Combine(Fix02228Tests.OutputRoot(), "technology-merged");
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        foreach (var dir in new[] { Path.Combine(sourceRoot, "native-data", "Technology"), Path.Combine(sourceRoot, "tests", "TestData", "Technology") })
            if (Directory.Exists(dir))
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        return target;
    }
}
