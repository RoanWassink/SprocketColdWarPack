using System;
using System.Collections.Generic;

namespace SprocketExpedition.Game;

// Pure policy for the battle-stats observers: no Unity, BepInEx or native types,
// so the managed suites can test both the installation list and the byte guard.
//
// IL2CPP identical-code folding (ICF) makes methods with the same machine code
// share one native address. Patching such an address also detours every folded
// caller, with arguments that do not match the managed wrapper. A hook is
// therefore installed only after its native body has been read and classified as
// non-trivial. These helpers decide the plan and the classification; reading the
// bytes itself happens in BattleStatsNativeGuard.
internal static class BattleStatsHookPlan
{
    internal const string Launch = "launch";
    internal const string ArmourHit = "armour hit";
    internal const string ExplosiveHit = "explosive hit";
    internal const string KillEvent = "kill event";

    // Shrapnel is deliberately absent: its HitDamageModel is an empty method whose
    // native address is shared through ICF (the 0.3.8 designer crash).
    private static readonly string[] All =
    {
        Launch,
        ArmourHit,
        ExplosiveHit,
        KillEvent,
    };

    internal static IReadOnlyList<string> For(bool enabled) =>
        enabled ? Array.AsReadOnly(All) : Array.Empty<string>();
}

internal enum MethodBodyKind
{
    Normal,
    Trivial,
}

internal readonly record struct MethodBodyClassification(MethodBodyKind Kind, string Reason)
{
    internal bool IsTrivial => Kind == MethodBodyKind.Trivial;
}

internal static class BattleStatsHookGuard
{
    internal const int SampleBytes = 16;

    // A one-byte ret inside the first 8 bytes is enough for ICF to fold the method.
    internal const int RetScanBytes = 8;

    internal static MethodBodyClassification Classify(ReadOnlySpan<byte> code)
    {
        if (code.Length == 0) return Trivial("no readable bytes");

        if (code[0] == 0xC3) return Trivial("starts with ret");
        if (code[0] == 0xC2 && code.Length >= 3) return Trivial("starts with ret imm16");

        if (code.Length >= 3 && (code[0] == 0x33 || code[0] == 0x31)
            && code[1] == 0xC0 && code[2] == 0xC3)
            return Trivial("starts with xor eax,eax; ret");

        var scan = Math.Min(RetScanBytes, code.Length);
        for (var i = 1; i < scan; i++)
            if (code[i] == 0xC3) return Trivial($"ret at byte {i}");

        return new MethodBodyClassification(MethodBodyKind.Normal, "non-trivial prologue");
    }

    private static MethodBodyClassification Trivial(string reason) =>
        new(MethodBodyKind.Trivial, reason);
}
