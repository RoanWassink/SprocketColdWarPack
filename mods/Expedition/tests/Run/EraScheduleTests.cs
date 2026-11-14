using SprocketExpedition.Core;
using SprocketExpedition.Core.Run;

namespace RunTests;

internal static class EraScheduleTests
{
    private static NativeEraInfo Era(string name, int start, bool playable = true, bool tank = true) =>
        new(name, start, playable, tank);

    // The standard game: six native eras.
    private static NativeEraInfo[] Standard() => new[]
    {
        Era("WWI", 19140101), Era("Interwar", 19200101), Era("Earlywar", 19380101),
        Era("Midwar", 19420101), Era("Latewar", 19440101), Era("Coldwar", 19450903)
    };

    // Expanded Ballistics adds Coldwar2 (1960) and Coldwar3 (1975).
    private static NativeEraInfo[] Eight() => Standard().Concat(new[]
    {
        Era("Coldwar2", 19600101), Era("Coldwar3", 19750101)
    }).ToArray();

    public static void ScheduleIsEveryPlayableEraWithATankInStartOrder()
    {
        var six = EraSchedulePolicy.Resolve(Standard());
        Check.True(six.IsUsable, "six-era schedule is usable");
        Check.SameStrings(new[] { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar" },
            six.EraNames, "six native eras give six scheduled eras");
        Check.Equal(0, six.Skipped.Count, "nothing skipped when every era qualifies");

        var eight = EraSchedulePolicy.Resolve(Eight());
        Check.SameStrings(new[]
            { "WWI", "Interwar", "Earlywar", "Midwar", "Latewar", "Coldwar", "Coldwar2", "Coldwar3" },
            eight.EraNames, "eight native eras give eight scheduled eras");
        Check.Equal("Coldwar3", eight.EraNames[^1], "the last scheduled era is the latest native era");
        Check.True(eight.Reason.Contains("8 era", StringComparison.Ordinal), "reason states the count");

        // Order follows the start date, not the order the game lists the eras in.
        var shuffled = Eight().Reverse().ToArray();
        Check.SameStrings(eight.EraNames, EraSchedulePolicy.Resolve(shuffled).EraNames,
            "scheduling sorts on start date");

        // Equal start dates keep the native list order.
        var tie = EraSchedulePolicy.Resolve(new[] { Era("B", 19500101), Era("A", 19500101), Era("C", 19400101) });
        Check.SameStrings(new[] { "C", "B", "A" }, tie.EraNames, "ties keep list order");
    }

    public static void EraWithoutATankOrNotPlayableIsSkipped()
    {
        var noTank = Eight();
        noTank[6] = Era("Coldwar2", 19600101, tank: false);
        var decision = EraSchedulePolicy.Resolve(noTank);
        Check.Equal(7, decision.EraNames.Count, "an era without a usable tank is skipped");
        Check.False(decision.EraNames.Contains("Coldwar2"), "Coldwar2 is not scheduled without tanks");
        Check.Equal("Coldwar3", decision.EraNames[^1], "later era still scheduled");
        Check.True(decision.Skipped.Single().Contains("Coldwar2 (no usable non-stationary tank)",
            StringComparison.Ordinal), "skip reason names the era and cause");
        Check.True(decision.Reason.Contains("Skipped", StringComparison.Ordinal), "reason lists skipped eras");

        var notPlayable = Eight();
        notPlayable[7] = Era("Coldwar3", 19750101, playable: false);
        var second = EraSchedulePolicy.Resolve(notPlayable);
        Check.Equal(7, second.EraNames.Count, "a non-playable era is skipped");
        Check.Equal("Coldwar2", second.EraNames[^1], "last era is the last playable one");
        Check.True(second.Skipped.Single().Contains("(not playable)", StringComparison.Ordinal),
            "non-playable reason");

        // The standard game with a missing final era keeps working with five eras.
        var five = Standard();
        five[5] = Era("Coldwar", 19450903, tank: false);
        Check.Equal(5, EraSchedulePolicy.Resolve(five).EraNames.Count, "five eras when the last has no tank");
    }

    public static void TooFewEraSchedulesAreReportedNotThrown()
    {
        var one = EraSchedulePolicy.Resolve(new[] { Era("WWI", 19140101), Era("Interwar", 19200101, playable: false) });
        Check.False(one.IsUsable, "one era is not enough");
        Check.True(one.Reason.Contains("unusable", StringComparison.Ordinal), "reason says the schedule is unusable");
        Check.False(EraSchedulePolicy.Resolve(Array.Empty<NativeEraInfo>()).IsUsable, "an empty list is unusable");
        Check.Equal(2, EraSchedulePolicy.Resolve(new[] { Era("A", 1), Era("B", 2) }).EraNames.Count,
            "the minimum of two eras is accepted");

        var duplicate = false;
        try { EraSchedulePolicy.Resolve(new[] { Era("A", 1), Era("A", 2) }); }
        catch (ArgumentException) { duplicate = true; }
        Check.True(duplicate, "a duplicate era name is rejected");
        var blank = false;
        try { EraSchedulePolicy.Resolve(new[] { Era(" ", 1) }); }
        catch (ArgumentException) { blank = true; }
        Check.True(blank, "a blank era name is rejected");
    }

    public static void EraCountAndFamilyRules()
    {
        Check.True(EraSchedulePolicy.IsValidEraCount(2), "two is valid");
        Check.True(EraSchedulePolicy.IsValidEraCount(5), "five is valid (existing runs)");
        Check.True(EraSchedulePolicy.IsValidEraCount(6), "six is valid (existing runs)");
        Check.True(EraSchedulePolicy.IsValidEraCount(8), "eight is valid");
        Check.False(EraSchedulePolicy.IsValidEraCount(1), "one is invalid");
        Check.False(EraSchedulePolicy.IsValidEraCount(EraSchedulePolicy.MaximumEraCount + 1), "above the sanity bound is invalid");
        Check.False(EraSchedulePolicy.IsValidStartingEra(5, 5), "five-era index five is out of range");
        Check.True(EraSchedulePolicy.IsValidStartingEra(4, 5), "five-era Latewar index is valid");
        Check.True(EraSchedulePolicy.IsValidStartingEra(7, 8), "eight-era last index is valid");
        Check.False(EraSchedulePolicy.IsValidStartingEra(8, 8), "eight-era index eight is out of range");

        Check.Equal("Coldwar", EraSchedulePolicy.FamilyName("Coldwar2"), "family drops a trailing number");
        Check.Equal("Coldwar", EraSchedulePolicy.FamilyName("Coldwar"), "family of a plain name");
        Check.Equal("WWI", EraSchedulePolicy.FamilyName("WWI"), "family keeps letters");
        Check.Equal("2", EraSchedulePolicy.FamilyName("2"), "an all-digit name is kept");

        // Budget multipliers: the authored table is unchanged, later eras continue it.
        Check.Equal(160m, RunDirector.FiniteEraBudget(100m, 1), "index one keeps the authored value");
        Check.Equal(800m, RunDirector.FiniteEraBudget(100m, 5), "index five keeps the authored value");
        Check.Equal(1000m, RunDirector.FiniteEraBudget(100m, 6), "index six continues the table");
        Check.Equal(1250m, RunDirector.FiniteEraBudget(100m, 7), "index seven continues the table");
    }

    public static void FiveEraRunCompletesAtLatewarAndContinuesEndless()
    {
        var eras = RunFixture.Eras().Take(5).ToArray();
        var director = RunFixture.NewRun(baseBudget: 100m, eras: eras);
        Check.Equal(5, RunFixture.Snap(director).EraSchedule.Count, "five-era schedule accepted");

        for (var victory = 0; victory < 15; victory++) RunFixture.PlayVictory(director);
        var awaiting = RunFixture.Snap(director);
        Check.Equal(RunPhase.AwaitingEndlessChoice, awaiting.Phase, "fifteen victories finish five eras");
        Check.Equal(15, awaiting.Victories, "finite victory total");
        Check.Equal(4, awaiting.BlockIndex, "final included block");
        Check.Equal(4, awaiting.EraIndex, "final included era index");
        Check.Equal("native-era-4", awaiting.EraSchedule[awaiting.EraIndex].NativeId,
            "final era is the fifth included era");
        Check.Equal(16, awaiting.Round, "next round is endless round sixteen");
        Check.Equal(500m, awaiting.BlockBaseBudget.Value, "fifth historical multiplier retained");
        Check.Equal(30m, awaiting.SupplyBonus, "final block supply bonus");
        Check.Equal(530m, awaiting.AvailableBudget.Value, "final block available budget");
        Check.Equal(6, awaiting.PendingSupplyPoints, "final block supply remains pending");

        Check.Accepted(director.ContinueEndless(), "continue five-era endless");
        var endless = RunFixture.Snap(director);
        Check.Equal(5, endless.BlockIndex, "first endless block follows five finite blocks");
        Check.Equal(4, endless.EraIndex, "endless remains in Latewar");
        Check.Equal(575m, endless.BlockBaseBudget.Value, "first endless base grows from Latewar");
        Check.Equal(34.5m, endless.SupplyBonus, "first endless bonus uses the new base");
        Check.Equal(609.5m, endless.AvailableBudget.Value, "first endless available budget");
        Check.Equal(16, endless.Round, "first endless round");
    }

    public static void EightEraRunCompletesAtTheLastEraAndKeepsItsSchedule()
    {
        var eras = Enumerable.Range(0, 8).Select(i => new EraReference("native-era-" + i)).ToArray();
        var director = RunFixture.NewRun(baseBudget: 100m, eras: eras);
        Check.Equal(8, RunFixture.Snap(director).EraSchedule.Count, "eight-era schedule accepted");
        for (var victory = 0; victory < 24; victory++) RunFixture.PlayVictory(director);
        var awaiting = RunFixture.Snap(director);
        Check.Equal(RunPhase.AwaitingEndlessChoice, awaiting.Phase, "twenty-four victories finish eight eras");
        Check.Equal(24, awaiting.Victories, "eight-era finite victory total");
        Check.Equal(7, awaiting.BlockIndex, "final block of eight");
        Check.Equal("native-era-7", awaiting.EraSchedule[awaiting.EraIndex].NativeId, "final era is the eighth");
        Check.Equal(1250m, awaiting.BlockBaseBudget.Value, "eighth block continues the multiplier table");
        Check.Equal(25, awaiting.Round, "next round is endless round twenty-five");
        Check.Accepted(director.ContinueEndless(), "continue eight-era endless");
        Check.Equal(8, RunFixture.Snap(director).EraSchedule.Count, "the run keeps its own pinned schedule");

        // Starting in the seventh era of eight leaves two eras (index 6, 7).
        var late = RunFixture.NewRun(baseBudget: 100m, eras: eras, startingEraIndex: 6);
        var start = RunFixture.Snap(late);
        Check.Equal(1000m, start.BlockBaseBudget.Value, "seventh-era start budget");
        Check.Equal(19, start.Round, "seventh-era start round");
        for (var victory = 0; victory < 6; victory++) RunFixture.PlayVictory(late);
        Check.Equal(RunPhase.AwaitingEndlessChoice, RunFixture.Snap(late).Phase, "late start completes at era eight");
    }

    public static void FiveEraRunCanStartInLatewarButNotPastTheSchedule()
    {
        var eras = RunFixture.Eras().Take(5).ToArray();
        var latewar = RunFixture.NewRun(baseBudget: 100m, eras: eras, startingEraIndex: 4);
        var start = RunFixture.Snap(latewar);
        Check.Equal(4, start.BlockIndex, "Latewar start block");
        Check.Equal(4, start.EraIndex, "Latewar start era");
        Check.Equal(13, start.Round, "five-era Latewar starts at round thirteen");
        Check.Equal(500m, start.BlockBaseBudget.Value, "Latewar start uses fifth multiplier");
        Check.Equal(0, start.Victories, "skipped eras give no victories");
        for (var victory = 0; victory < 3; victory++) RunFixture.PlayVictory(latewar);
        Check.Equal(RunPhase.AwaitingEndlessChoice, RunFixture.Snap(latewar).Phase,
            "starting in Latewar completes at the real final era");

        Check.Rejected(new RunDirector().NewRun(
                RunFixture.Config(eras: eras, startingEraIndex: 5)),
            "a five-era run cannot start past its schedule");
        Check.Rejected(new RunDirector().NewRun(
                RunFixture.Config(startingEraIndex: 6)),
            "a six-era start past the schedule is rejected");
    }
}
