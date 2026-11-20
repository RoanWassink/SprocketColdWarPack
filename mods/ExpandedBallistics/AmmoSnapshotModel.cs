using System;
using System.Collections.Generic;
using System.Linq;

namespace SprocketExpandedBallistics;

/// <summary>One carried shell type in the canonical HUD/API snapshot.</summary>
internal sealed record AmmoSnapshotRound(string ProfileId, string Label, string Code, int Count, bool Chambered, bool Requested);

/// <summary>
/// Immutable ammunition identity for one live cannon. <see cref="ChamberedConfirmed"/>
/// is false while the gun is empty or loading; <see cref="ChamberedProfileId"/> is
/// then null and no HUD row may claim to be the round in the gun.
/// </summary>
internal sealed record AmmoSnapshot(
    string? NextProfileId, string? NextLabel, string? NextCode,
    string? ChamberedProfileId, string? ChamberedLabel, string? ChamberedCode,
    bool ChamberedConfirmed, int TotalRounds, IReadOnlyList<AmmoSnapshotRound> Rounds,
    bool Frozen, string? FailureReason, double? ChamberedMuzzleVelocityMps = null);

/// <summary>0.22.17 point 9: the muzzle velocity of the shell that is really in the chamber (not Cannon.muzzleVelocity, which follows the design shell).</summary>
internal static class AmmoMuzzle
{
    /// <summary>Same value the launch uses: vanilla keeps the blueprint velocity, a powered round its cruise speed, any other profile its balanced velocity.</summary>
    internal static double? Velocity(ShellProfile? profile, double calibreMm, double blueprintVelocity, ushort penetratorConstant)
    {
        if (!double.IsFinite(blueprintVelocity) || blueprintVelocity <= 0) return null;
        if (profile == null) return blueprintVelocity;
        try
        {
            if (ShellBalance.Powered(profile) && profile.Atgm is { } flight) return flight.FlightSpeed;
            var v = ShellBalance.Calculate(calibreMm, blueprintVelocity, penetratorConstant, profile).Velocity;
            return double.IsFinite(v) && v > 0 ? v : null;
        }
        catch (Exception) { return null; }
    }
}

internal static class AmmoSnapshotFormat
{
    internal const string EmptyGun = "empty / loading not yet confirmed";

    /// <summary>
    /// A carried mix always has a real next request. An absent or unlisted saved
    /// selection falls back to the first carried type, matching the native chooser.
    /// </summary>
    internal static string? NextSelection(IReadOnlyList<AmmoLoadoutEntry> entries, string? selected)
    {
        if (entries.Count == 0) return null;
        if (!string.IsNullOrEmpty(selected) && entries.Any(e => e.ProfileId == selected)) return selected;
        return entries[0].ProfileId;
    }

    internal static string CodeFor(string? behavior, string? label)
    {
        var mode = (behavior ?? "").ToLowerInvariant();
        var known = mode switch
        {
            "apfsds" => "APFSDS", "heat" => "HEAT", "hesh" => "HESH", "he" => "HE",
            "aphe" => "APHE", "atgm" => "ATGM", "atgm_gun" => "GLATGM",
            "apds" => "APDS", "apcr" => "APCR", "ap" => "AP", _ => null
        };
        if (known != null) return known;
        var upper = (label ?? "").ToUpperInvariant();
        foreach (var candidate in new[] { "APFSDS", "APDS", "APCR", "APHE", "HEAT", "HESH", "ATGM", "HE", "AP" })
            if (upper.Contains(candidate, StringComparison.Ordinal)) return candidate;
        return string.IsNullOrWhiteSpace(behavior) ? "?" : behavior!.ToUpperInvariant();
    }

    /// <summary>Exact text used by the existing battle HUD; kept pure for regression checks.</summary>
    internal static IReadOnlyList<string> Lines(AmmoSnapshot snapshot)
    {
        if (snapshot.Frozen)
            return new[] { "Ammunition mix unavailable: " + (snapshot.FailureReason ?? "Invalid loadout."), "Return to Edit to fix fit/loadout." };
        var lines = new List<string>
        {
            $"Ammunition | next load: {snapshot.NextLabel ?? "none"}",
            $"In gun: {(snapshot.ChamberedConfirmed ? snapshot.ChamberedLabel : EmptyGun)}"
        };
        for (var i = 0; i < snapshot.Rounds.Count; i++)
        {
            var round = snapshot.Rounds[i];
            lines.Add($"{(round.Requested ? ">" : " ")} {i + 1}  {round.Label}  {round.Count}{(round.Chambered ? "  (includes round in gun)" : string.Empty)}");
        }
        return lines;
    }
}

/// <summary>Gun state for the ammunition HUD (from the native load task).</summary>
internal enum AmmoLoadState { Unknown, Empty, Loading, Loaded }
/// <summary>One carried type on the ammunition HUD.</summary>
internal sealed record AmmoHudRow(string Marker, string Label, string Count, bool Chambered);
/// <summary>
/// What the Kit ammunition HUD shows (0.22.3). Built at most ten times a second; <see cref="Key"/>
/// changes only when a shown value changes, so the HUD never formats or lays out per frame.
/// </summary>
internal sealed record AmmoHudView(string Name, string Code, string Rounds, string State, string StateColour,
    IReadOnlyList<AmmoHudRow> Rows, string Notice, string Key);

internal static class AmmoHudFormat
{
    internal const string Loaded = "#8fd99c", Loading = "#f3cf86", Empty = "#ffcbb3", Unknown = "#b8ad94";
    private static (string Text, string Colour) Gun(AmmoLoadState state) => state switch
    {
        AmmoLoadState.Loaded => ("LOADED", Loaded),
        AmmoLoadState.Loading => ("LOADING", Loading),
        AmmoLoadState.Empty => ("EMPTY", Empty),
        _ => ("", Unknown)
    };
    private static string Rounds(int? rounds) => rounds is { } r ? "\u00d7 " + r.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    private static AmmoHudView Make(string name, string code, string rounds, AmmoLoadState state, IReadOnlyList<AmmoHudRow> rows, string notice)
    {
        var (text, colour) = Gun(state);
        var key = string.Join("|", new[] { name, code, rounds, text, notice }.Concat(rows.Select(r => r.Marker + r.Label + r.Count + r.Chambered)));
        return new AmmoHudView(name, code, rounds, text, colour, rows, notice, key);
    }
    /// <summary>A cannon without a carried mix: the selected (native or profile) shell and the native rounds left.</summary>
    internal static AmmoHudView Native(string name, string code, int? rounds, AmmoLoadState state, string notice = "") =>
        Make(name, code, Rounds(rounds), state, Array.Empty<AmmoHudRow>(), notice);
    /// <summary>A cannon with a carried mix: the round in the gun (else the next load) and one row per type.</summary>
    internal static AmmoHudView Mix(AmmoSnapshot snapshot, AmmoLoadState state, string notice = "")
    {
        if (snapshot.Frozen)
            return Make("Mix unavailable", "MIX", "", state, Array.Empty<AmmoHudRow>(),
                (snapshot.FailureReason ?? "Invalid loadout.") + " Return to Edit to fix the loadout.");
        var name = snapshot.ChamberedConfirmed ? snapshot.ChamberedLabel ?? "?" : snapshot.NextLabel ?? "none";
        var code = snapshot.ChamberedConfirmed ? snapshot.ChamberedCode ?? "?" : snapshot.NextCode ?? "?";
        var rows = snapshot.Rounds.Select((r, i) => new AmmoHudRow(r.Requested ? ">" : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            r.Label, r.Chambered ? $"{Math.Max(0, r.Count - 1)} +1 gun" : r.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Chambered)).ToArray();
        return Make(name, code, Rounds(snapshot.TotalRounds), state, rows, notice);
    }
}
