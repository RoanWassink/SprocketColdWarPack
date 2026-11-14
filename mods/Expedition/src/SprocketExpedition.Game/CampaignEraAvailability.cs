#if CAMPAIGN
using System.Diagnostics;
using BepInEx;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using SprocketExpedition.Core.Run;

namespace SprocketExpedition.Game;

// Lightweight, cooperative New Game discovery. The schedule is every playable
// native era that has at least one usable non-stationary tank, ordered by native
// start date (EraSchedulePolicy). This is only a menu availability hint; the full
// native catalogue makes the same decision again from the loaded tanks before a
// run can start.
internal static class CampaignEraAvailability
{
    // Time one Pump call may spend before it yields to the next frame.
    private const long PumpBudgetTicks = 5 * TimeSpan.TicksPerMillisecond / 1;
    private static EraScheduleDecision? decision;
    private static IEnumerator<bool>? work;
    private static IReadOnlyList<CampaignNativeCatalogue.NativeEra> eras = Array.Empty<CampaignNativeCatalogue.NativeEra>();
    private static readonly HashSet<string> erasWithTank = new(StringComparer.Ordinal);

    internal static bool Ready => decision != null;
    // Also true while waiting for the native era list, so the menu keeps pumping.
    internal static bool IsChecking => work != null || (decision == null && waitingLogged);
    private static bool waitingLogged;

    // The checked schedule. While the check runs (or when it found no usable
    // schedule) this is the playable native era list as a provisional hint; the
    // menu never starts a run before Ready and the catalogue decides again.
    internal static IReadOnlyList<string> ScheduleEraNames
    {
        get
        {
            if (decision is { IsUsable: true }) return decision.EraNames;
            try
            {
                return CampaignNativeCatalogue.NativeEraList.Where(era => era.Playable)
                    .Select(era => era.Name).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    internal static string Status => decision?.Reason
        ?? (work == null ? "Era availability has not been checked yet." : "Checking available eras and tanks...");

    internal static void EnsureStarted()
    {
        if (decision != null || work != null) return;
        try
        {
            eras = CampaignNativeCatalogue.NativeEraList;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Expedition era list could not be read: " + ex.Message);
            eras = Array.Empty<CampaignNativeCatalogue.NativeEra>();
        }
        erasWithTank.Clear();
        if (!eras.Any(era => era.Playable))
        {
            // The game has not loaded its eras yet (first New Game after start):
            // stay undecided so the next Pump reads the list again.
            if (!waitingLogged) Plugin.Logger.LogInfo("Expedition era check: native era list not loaded yet; retrying.");
            waitingLogged = true;
            return;
        }
        waitingLogged = false;
        work = DiscoverTanks().GetEnumerator();
    }

    internal static bool Pump(int minimumSteps = 2)
    {
        EnsureStarted();
        if (work == null) return false;
        try
        {
            var started = Stopwatch.GetTimestamp();
            var steps = 0;
            while (steps < minimumSteps
                || (Stopwatch.GetTimestamp() - started) * TimeSpan.TicksPerSecond / Stopwatch.Frequency < PumpBudgetTicks)
            {
                if (!work.MoveNext())
                {
                    work.Dispose();
                    work = null;
                    Finish();
                    return true;
                }
                steps++;
            }
            return false;
        }
        catch (Exception ex)
        {
            // A directory/veteran discovery failure must complete once with what
            // was found instead of retrying every frame behind a gated START.
            work?.Dispose();
            work = null;
            Plugin.Logger.LogWarning("Expedition era check stopped: " + ex.Message);
            Finish();
            return true;
        }
    }

    internal static void Publish(EraScheduleDecision schedule)
    {
        work?.Dispose();
        work = null;
        decision = schedule;
    }

    // Catalogue reset marks a session/content boundary. The next New Game menu
    // checks again instead of trusting a pre-session hint.
    internal static void Invalidate()
    {
        work?.Dispose();
        work = null;
        decision = null;
        eras = Array.Empty<CampaignNativeCatalogue.NativeEra>();
        erasWithTank.Clear();
    }

    private static void Finish()
    {
        // Same test aid as the catalogue: empty later eras take tanks from the
        // nearest earlier era (a menu hint only; the catalogue decides with counts).
        if (CampaignNativeCatalogue.SpreadIntoLaterEras)
        {
            var populated = false;
            foreach (var era in eras.Where(era => era.Playable))
            {
                if (erasWithTank.Contains(era.Name)) populated = true;
                else if (populated) erasWithTank.Add(era.Name);
            }
        }
        decision = EraSchedulePolicy.Resolve(eras.Select(era => new NativeEraInfo(
            era.Name, era.StartKey, era.Playable, erasWithTank.Contains(era.Name))).ToArray());
        Plugin.Logger.LogInfo("Expedition era availability: " + decision.Reason);
    }

    private static bool AllPlayableErasHaveTank() =>
        eras.Where(era => era.Playable).All(era => erasWithTank.Contains(era.Name));

    private static IEnumerable<bool> DiscoverTanks()
    {
        foreach (var step in CampaignNativeCatalogue.PrepareTestPool()) yield return step;
        var playable = new HashSet<string>(eras.Where(era => era.Playable).Select(era => era.Name),
            StringComparer.Ordinal);
        foreach (var (path, community) in CandidateVehicleFiles())
        {
            yield return true;
            if (CampaignNativeCatalogue.IsBundledStationary(path)) continue;
            try
            {
                var length = new FileInfo(path).Length;
                if (community)
                {
                    if (!CampaignNativeCatalogue.TryValidateCommunityBlueprint(path, out _)) continue;
                    if (length is < 1 or > CampaignBlueprintBlob.MaximumBytes) continue;
                }
                else if (length is < 1 or > 32L * 1024 * 1024) continue;
                var card = VehicleCard.LoadVehicleCard(path);
                if (card != null && card.Cost > 0 && !string.IsNullOrWhiteSpace(card.Name)
                    && !CampaignTankNamePolicy.IsStationaryGun(card.Name)
                    && VehicleClassifications.GetEra(card.CreationDate)?.Name is { } era
                    && playable.Contains(era))
                    erasWithTank.Add(era);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"Expedition era check skipped '{Path.GetFileName(path)}': {ex.Message}");
            }
            if (AllPlayableErasHaveTank()) yield break;
        }

        IReadOnlyList<CampaignVeteran> veterans;
        try
        {
            veterans = CampaignVeteranStore.ReadAll(CampaignVeteranUnlocks.DirectoryPath,
                reason => Plugin.Logger.LogWarning("Expedition era check skipped veteran: " + reason));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Expedition era check veteran read stopped: " + ex.Message);
            veterans = Array.Empty<CampaignVeteran>();
        }
        foreach (var veteran in veterans)
        {
            yield return true;
            if (!playable.Contains(veteran.Era) || erasWithTank.Contains(veteran.Era)
                || !IsUsableVeteran(veteran)) continue;
            erasWithTank.Add(veteran.Era);
            if (AllPlayableErasHaveTank()) yield break;
        }
    }

    private static bool IsUsableVeteran(CampaignVeteran veteran)
    {
        if (CampaignTankNamePolicy.IsStationaryGun(veteran.Name)) return false;
        try
        {
            var unit = CustomBattleBlueprintResolver.CreateLockedUnit(veteran.Bytes, out _);
            return unit != null
                && CampaignNativeCatalogue.DateBelongsToEra(unit.CreationDate, veteran.Era);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<(string Path, bool Community)> CandidateVehicleFiles()
    {
        var custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Sprocket", "Factions", CampaignFactionDesigns.FactionName,
            "Blueprints", "Vehicles");
        var stock = Path.Combine(Paths.GameRootPath, "Sprocket_Data", "StreamingAssets",
            "Blueprints", "Vehicles");
        foreach (var path in Enumerate(custom, community: false).Concat(
            Enumerate(stock, community: false)).Concat(
            Enumerate(CampaignNativeCatalogue.CommunityDirectory, community: true)).Concat(
            Enumerate(CampaignNativeCatalogue.DangerousCommunityDirectory, community: true)))
            yield return path;
    }

    private static IEnumerable<(string Path, bool Community)> Enumerate(
        string directory, bool community)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.blueprint")
            .OrderBy(p => p, StringComparer.Ordinal))
            yield return (path, community);
    }
}
#endif
