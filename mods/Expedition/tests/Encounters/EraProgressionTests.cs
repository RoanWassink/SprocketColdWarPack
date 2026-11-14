using SprocketExpedition.Core;
using SprocketExpedition.Core.Encounters;

namespace EncountersTests;

// Dynamic era lists: eras after Coldwar (for example Coldwar2 and Coldwar3) keep
// scaling, and pinned values of existing runs stay valid.
public static class EraProgressionTests
{
    public static void ProgressionKeepsGrowingPastTheSixthEra()
    {
        TestAssert.Equal(1m, DifficultyScaling.ProgressionFactor(0), "first era has no progression");
        TestAssert.Equal(1.25m, DifficultyScaling.ProgressionFactor(5), "Coldwar keeps its authored +25%");
        TestAssert.Equal(1.30m, DifficultyScaling.ProgressionFactor(6), "Coldwar2 is stronger than Coldwar");
        TestAssert.Equal(1.35m, DifficultyScaling.ProgressionFactor(7), "Coldwar3 is stronger than Coldwar2");
        TestAssert.True(DifficultyScaling.ProgressionFactor(7) < DifficultyScaling.ProgressionFactor(8),
            "the progression continues");
        TestAssert.Equal(DifficultyScaling.ProgressionFactor(DifficultyScaling.MaximumEraIndex),
            DifficultyScaling.ProgressionFactor(99), "the progression is bounded");
    }

    public static void WorstCasePersistedMultiplierStaysAcceptedForEveryEra()
    {
        for (var era = 0; era <= DifficultyScaling.MaximumEraIndex; era++)
            TestAssert.True(DifficultyScaling.TryEffectiveMultiplier(2.4m, true, era,
                EncounterRisk.Dangerous, out _), $"era index {era} worst case is accepted");
        // The pre-change worst case (index 5) is unchanged.
        TestAssert.True(DifficultyScaling.TryEffectiveMultiplier(2.4m, true, 5, EncounterRisk.Dangerous,
            out var legacy) && legacy == 2.4m * 1.25m * 1.35m, "index five arithmetic is unchanged");
    }

    public static void LaterErasOfAFamilyInheritMassLimits()
    {
        TestAssert.Equal(EnemyDesignClassifier.ThresholdFor("Coldwar"),
            EnemyDesignClassifier.ThresholdFor("Coldwar2"), "Coldwar2 uses the Coldwar mass limits");
        TestAssert.Equal(EnemyDesignClassifier.ThresholdFor("Coldwar"),
            EnemyDesignClassifier.ThresholdFor("Coldwar3"), "Coldwar3 uses the Coldwar mass limits");
        TestAssert.Equal(EnemyDesignClassifier.ThresholdFor(null),
            EnemyDesignClassifier.ThresholdFor("Unknown"), "an unrelated era keeps the default limits");
        TestAssert.Equal(EnemyDesignClassifier.Role("Fictional", 50_000d, "Coldwar3"),
            EnemyDesignClassifier.Role("Fictional", 50_000d, "Coldwar"), "classification matches the family");
    }
}
