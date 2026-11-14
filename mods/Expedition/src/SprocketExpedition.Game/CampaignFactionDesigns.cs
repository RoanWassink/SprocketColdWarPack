using Sprocket.CustomBattles;
using Sprocket.Vehicles.Serialization;

namespace SprocketExpedition.Game;

// Reads native vehicle designs from the player's "Expedition Spawns" faction.
// Read-only: the mod never creates or edits faction files. Designs are converted
// through the same native card/UnitSelectionLoader path as the locked player design.
internal static class CampaignFactionDesigns
{
    internal const string FactionName = "Expedition Spawns";

    internal static bool TryLoad(out List<UnitDefinition> units, out string reason)
    {
        units = new List<UnitDefinition>();
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var factionsRoot = Path.Combine(documents, "My Games", "Sprocket", "Factions");
            if (!Directory.Exists(factionsRoot))
            {
                reason = "The Sprocket Factions directory does not exist.";
                return false;
            }

            var directories = Directory.GetDirectories(factionsRoot);
            var currentFaction = ReadCurrentFaction();
            var preferred = new[] { FactionName, currentFaction, "Nederland", "Default" };
            string? selectedName = null;
            string? factionDirectory = null;
            foreach (var candidate in preferred)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                factionDirectory = directories.FirstOrDefault(directory =>
                    Path.GetFileName(directory).Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (factionDirectory != null)
                {
                    selectedName = Path.GetFileName(factionDirectory);
                    break;
                }
            }

            if (factionDirectory == null)
            {
                reason = $"No faction named '{FactionName}' or a usable current/default faction was found.";
                return false;
            }

            var vehicles = Path.Combine(factionDirectory, "Blueprints", "Vehicles");
            if (!Directory.Exists(vehicles))
            {
                reason = $"Faction '{selectedName}' has no Blueprints/Vehicles directory.";
                return false;
            }

            foreach (var path in Directory.GetFiles(vehicles, "*.blueprint")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var card = VehicleCard.LoadVehicleCard(path);
                    if (card == null) continue;
                    var unit = UnitSelectionLoader.NewUnit(card);
                    if (unit != null) units.Add(unit);
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning(
                        $"Expedition faction designs: could not load '{Path.GetFileName(path)}'. " + ex.Message);
                }
            }

            if (units.Count == 0)
            {
                reason = $"Faction '{FactionName}' contains no loadable .blueprint vehicle designs.";
                return false;
            }

            var fallback = string.Equals(selectedName, FactionName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" (fallback; create '{FactionName}' to override)";
            reason = $"{units.Count} design(s) loaded from faction '{selectedName}'{fallback}.";
            return true;
        }
        catch (Exception ex)
        {
            reason = "Faction design scan failed: " + ex.Message;
            return false;
        }
    }

    private static string? ReadCurrentFaction()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "HD", "Sprocket", "CurrentFaction");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
