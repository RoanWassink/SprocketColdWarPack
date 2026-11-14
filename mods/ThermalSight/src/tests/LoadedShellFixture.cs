namespace SprocketThermalSight.Tests;

// Managed doubles exercise the production reflection adapter without loading Unity.
public sealed record FakeProfile(string Id, string Behavior);
public sealed record FakeRound(string ProfileId);
// Pre-0.22.17 state shape: no chambered muzzle speed.
public record FakeAmmoState(string? ChamberedProfileId, bool ChamberedConfirmed, bool Frozen, string? NextProfileId,
    IReadOnlyList<FakeRound>? Rounds = null);
// EB 0.22.17+ state shape with the chambered round's muzzle speed.
public sealed record FakeSpeedState(string? ChamberedProfileId, bool ChamberedConfirmed, bool Frozen, string? NextProfileId,
    IReadOnlyList<FakeRound>? Rounds, double? ChamberedMuzzleVelocityMps)
    : FakeAmmoState(ChamberedProfileId, ChamberedConfirmed, Frozen, NextProfileId, Rounds);
public sealed class FakeCannon
{
    public bool Mix { get; set; }
    public FakeProfile? Initial { get; set; }
    public FakeAmmoState? State { get; set; }
}
internal static class FakeSelection
{
    internal static IReadOnlyList<FakeProfile> Profiles => new[] { new FakeProfile("ap", "ap"), new FakeProfile("heat", "heat"), new FakeProfile("missile", "atgm_gun") };
    internal static FakeProfile? CannonProfile(FakeCannon cannon) => cannon.Initial;
}
internal static class FakeAmmoApi
{
    public static bool HasCarriedMix(FakeCannon cannon) => cannon.Mix;
    public static bool TryGetState(FakeCannon cannon, out FakeAmmoState? state)
    { state = cannon.State; return state != null; }
}
