using SprocketExpedition.Game;

// Pure checks for the 0.3.12 battle-stats hook policy: the ICF byte guard and the
// enabled/disabled installation list. No game or native runtime is required.
internal static class BattleStatsHookChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    internal static void Run()
    {
        Classification();
        InstallationList();
        Console.WriteLine("PASS battle stats hooks: ret/trivial byte guard and on/off installation list");
    }

    private static void Classification()
    {
        Check(BattleStatsHookGuard.Classify(new byte[] { 0xC3 }).IsTrivial,
            "C3 (ret) is trivial");
        Check(BattleStatsHookGuard.Classify(new byte[] { 0xC2, 0x08, 0x00 }).IsTrivial,
            "C2 08 00 (ret 8) is trivial");
        Check(BattleStatsHookGuard.Classify(new byte[] { 0x33, 0xC0, 0xC3 }).IsTrivial,
            "33 C0 C3 (xor eax,eax; ret) is trivial");
        Check(BattleStatsHookGuard.Classify(new byte[] { 0x31, 0xC0, 0xC3 }).IsTrivial,
            "31 C0 C3 (xor eax,eax; ret) is trivial");

        var retAtFive = new byte[16];
        retAtFive[0] = 0x48; retAtFive[1] = 0x83; retAtFive[2] = 0xEC;
        retAtFive[3] = 0x28; retAtFive[4] = 0x48; retAtFive[5] = 0xC3;
        Check(BattleStatsHookGuard.Classify(retAtFive).IsTrivial,
            "a ret at byte 5 is trivial");

        var prologue = new byte[]
        {
            0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83,
            0xEC, 0x20, 0x48, 0x8B, 0xD9, 0x48, 0x8B, 0xFA,
        };
        Check(!BattleStatsHookGuard.Classify(prologue).IsTrivial,
            "a normal 48 89 5C 24 08 prologue is not trivial");

        var lateRet = new byte[16];
        Array.Copy(prologue, lateRet, prologue.Length);
        lateRet[10] = 0xC3;
        Check(!BattleStatsHookGuard.Classify(lateRet).IsTrivial,
            "a ret after the first 8 bytes does not reject the hook");

        Check(BattleStatsHookGuard.Classify(ReadOnlySpan<byte>.Empty).IsTrivial,
            "an unreadable body is trivial");
    }

    private static void InstallationList()
    {
        Check(BattleStatsHookPlan.For(false).Count == 0,
            "BattleStatsHooks=false installs nothing");

        var enabled = BattleStatsHookPlan.For(true);
        Check(enabled.Count == 4, "exactly four battle-stats hooks are planned");
        Check(enabled.SequenceEqual(new[]
            {
                BattleStatsHookPlan.Launch, BattleStatsHookPlan.ArmourHit,
                BattleStatsHookPlan.ExplosiveHit, BattleStatsHookPlan.KillEvent,
            }), "launch, armour, explosive and kill hooks");
        Check(enabled.All(label => !label.Contains("shrapnel", StringComparison.OrdinalIgnoreCase)),
            "the shrapnel hook is gone");

        Check(BattleStatsHookPlan.For(false).Count == 0,
            "the disabled plan is stable on a second call");
    }
}
