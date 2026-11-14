using System;
using System.Collections.Generic;
using System.Linq;
using Sprocket.Vehicles.Cannons;

namespace SprocketExpandedBallistics;

/// <summary>Public, read-only ammunition identity for HUD and sight integrations.</summary>
public sealed record BallisticsAmmoRound(string ProfileId, string Label, string Code, int Rounds, bool Chambered, bool Requested);

/// <summary>
/// Snapshot for one exact cannon. A carried mix is finite; <see cref="ChamberedConfirmed"/>
/// distinguishes the round actually in the gun from the next-load preference.
/// </summary>
public sealed record BallisticsAmmoState(
    bool HasCarriedMix, string? NextProfileId, string? NextLabel, string? NextCode,
    string? ChamberedProfileId, string? ChamberedLabel, string? ChamberedCode,
    bool ChamberedConfirmed, int TotalRounds, IReadOnlyList<BallisticsAmmoRound> Rounds,
    bool Frozen, string? FailureReason, double? ChamberedMuzzleVelocityMps = null);

/// <summary>
/// Supported integration surface. It is backed by the same selection/distribution
/// state as the Ballistics HUD, so consumers do not need a second ammunition catalogue.
/// </summary>
public static class BallisticsAmmoApi
{
    public static bool HasCarriedMix(Cannon cannon) => cannon != null && RuntimeAmmoLoadout.HasLoadout(cannon);

    public static bool TryGetState(Cannon cannon, out BallisticsAmmoState state)
    {
        state = null!;
        if (cannon == null || !RuntimeAmmoLoadout.TrySnapshot(cannon, out var snapshot)) return false;
        state = new BallisticsAmmoState(
            HasCarriedMix(cannon), snapshot.NextProfileId, snapshot.NextLabel, snapshot.NextCode,
            snapshot.ChamberedProfileId, snapshot.ChamberedLabel, snapshot.ChamberedCode,
            snapshot.ChamberedConfirmed, snapshot.TotalRounds,
            snapshot.Rounds.Select(r => new BallisticsAmmoRound(r.ProfileId, r.Label, r.Code, r.Count, r.Chambered, r.Requested)).ToArray(),
            snapshot.Frozen, snapshot.FailureReason, snapshot.ChamberedMuzzleVelocityMps);
        return true;
    }
}
