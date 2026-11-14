using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;

namespace RunTests;

internal sealed class CheckFailedException : Exception
{
    public CheckFailedException(string message) : base(message)
    {
    }
}

internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new CheckFailedException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new CheckFailedException(
                $"{message}: expected <{expected}>, actual <{actual}>");
        }
    }

    public static void NotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
            throw new CheckFailedException($"{message}: value was null");
    }

    public static void Accepted(RunActionResult result, string message)
    {
        if (!result.IsAccepted)
            throw new CheckFailedException($"{message}: {result.Status}: {result.Reason}");
    }

    public static void Rejected(RunActionResult result, string message)
    {
        if (result.Status != RunActionStatus.Rejected)
        {
            throw new CheckFailedException(
                $"{message}: expected Rejected, actual {result.Status}: {result.Reason}");
        }
    }

    public static void Ignored(RunActionResult result, string message)
    {
        if (result.Status is not (RunActionStatus.IgnoredStale or
            RunActionStatus.IgnoredDuplicate))
        {
            throw new CheckFailedException(
                $"{message}: expected ignored, actual {result.Status}: {result.Reason}");
        }
    }

    public static void SamePlan(EncounterPlan expected, EncounterPlan actual, string message)
    {
        Equal(expected.OfferId, actual.OfferId, message + " offer id");
        Equal(expected.PlanHash, actual.PlanHash, message + " plan hash");
        Equal(expected.Round, actual.Round, message + " round");
        Equal(expected.MapId, actual.MapId, message + " map");
        Equal(expected.SpawnLayoutId, actual.SpawnLayoutId, message + " spawn");
        Equal(expected.Risk, actual.Risk, message + " risk");
        Equal(expected.SupplyReward, actual.SupplyReward, message + " supply");
        Equal(expected.CatalogueVersion, actual.CatalogueVersion, message + " catalogue");
        NotNull(actual.Era, message + " era");
        NotNull(expected.Era, message + " expected era");
        Equal(expected.Era!.NativeId, actual.Era!.NativeId, message + " era id");

        Equal(expected.Enemies.Count, actual.Enemies.Count, message + " enemy count");
        for (var i = 0; i < expected.Enemies.Count; i++)
        {
            Equal(expected.Enemies[i].BlueprintId, actual.Enemies[i].BlueprintId,
                message + " enemy blueprint " + i);
            Equal(expected.Enemies[i].Count, actual.Enemies[i].Count,
                message + " enemy count " + i);
        }

        Equal(expected.RequiredMods.Count, actual.RequiredMods.Count,
            message + " required mods");
        for (var i = 0; i < expected.RequiredMods.Count; i++)
        {
            Equal(expected.RequiredMods[i].Id, actual.RequiredMods[i].Id,
                message + " mod id " + i);
            Equal(expected.RequiredMods[i].Version, actual.RequiredMods[i].Version,
                message + " mod version " + i);
        }
    }

    public static void SameAttempt(AttemptIdentity expected, AttemptIdentity actual,
        string message)
    {
        Equal(expected.RunId, actual.RunId, message + " run id");
        Equal(expected.EncounterId, actual.EncounterId, message + " encounter id");
        Equal(expected.PlanHash, actual.PlanHash, message + " plan hash");
        Equal(expected.Generation, actual.Generation, message + " generation");
    }

    public static void SameStrings(IReadOnlyList<string> expected,
        IReadOnlyList<string> actual, string message)
    {
        Equal(expected.Count, actual.Count, message + " count");
        for (var i = 0; i < expected.Count; i++)
            Equal(expected[i], actual[i], message + " item " + i);
    }

    public static void Throws<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            if (exception is T)
                return;
            throw new CheckFailedException(
                $"{message}: expected {typeof(T).Name}, got {exception.GetType().Name}");
        }

        throw new CheckFailedException($"{message}: expected {typeof(T).Name}, nothing thrown");
    }
}
