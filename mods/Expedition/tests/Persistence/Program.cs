namespace SprocketExpedition.Core.Persistence.Tests;

internal static class Program
{
    public static int Main()
    {
        RunStoreTests.Register();
        RecoveryTests.Register();
        BattleLogTests.Register();
        V7SpecialPersistenceTests.Register();
        return TestRunner.RunAll();
    }
}
