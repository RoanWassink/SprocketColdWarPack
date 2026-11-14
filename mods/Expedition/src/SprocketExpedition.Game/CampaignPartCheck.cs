using System.Text.RegularExpressions;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PartImporting;

namespace SprocketExpedition.Game;

// 0.3.2 (2026-11-01): a design that uses a part from another mod (AGCP's APS
// launchers) fails native InstantiateStructured, spawns without crew and the
// native battle ends in an instant VICTORY. Reject such designs up front by
// checking every objects[].guid against the live native part database.
internal static class CampaignPartCheck
{
    private static readonly Regex PartGuid = new("\"guid\"\\s*:\\s*\"([0-9a-fA-F-]{36})\"", RegexOptions.Compiled);
    private static IntPtr database;
    private static HashSet<string>? known;

    // Null when the native database is not loaded yet; the check is then skipped.
    private static HashSet<string>? KnownGuids()
    {
        var db = IVehiclePartInfoSource.Instance?.TryCast<PartInfoSource>()?.partDatabase;
        if (db?.partDefinitions == null) return null;
        if (known != null && database == db.Pointer) return known;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in db.partDefinitions)
            if (!string.IsNullOrEmpty(part?.guid)) set.Add(part!.guid);
        if (set.Count == 0) return null;
        database = db.Pointer;
        return known = set;
    }

    internal static void ThrowIfUnknownParts(string path)
    {
        var guids = KnownGuids();
        if (guids == null)
        {
            Plugin.Logger.LogWarning("Expedition part check: native part database not ready; " + Path.GetFileName(path) + " not checked.");
            return;
        }
        var missing = PartGuid.Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value)
            .Where(g => !guids.Contains(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Uses {missing.Length} part(s) the game does not know (another mod?): {string.Join(" ", missing.Take(3))}");
    }
}
