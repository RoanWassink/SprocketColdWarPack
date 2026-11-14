// The pure test build links the real preset registry. BepInEx itself is not
// needed by the registry's pure descriptor, so an empty compatibility namespace
// keeps the production source unchanged.
namespace BepInEx { }

namespace SprocketExpandedBallistics.Materials
{
    internal sealed class TestLogSource
    {
        internal void LogWarning(string message) { }
    }

    internal static class MaterialFeature
    {
        internal static readonly TestLogSource ModLog = new();
        internal static SprocketExpandedBallistics.ArmourCatalogueSnapshot ArmourSnapshot =
            SprocketExpandedBallistics.ArmourCatalogueSnapshot.Empty;
    }
}
