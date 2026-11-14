using System;
using System.Collections.Generic;
using System.Linq;

namespace SprocketExpandedBallistics.Tests;

/// <summary>
/// Synthetic checks for the merged inspector selection rule and the stable global
/// damage-feed action metadata. No game, Unity object or Keybinds instance is
/// required; invoke <see cref="RunAll"/> from the coordinator's test runner.
/// </summary>
internal static class AmmoInspectorPolicyTests
{
    internal static void RunAll()
    {
        StartingTypeUsesCarriedSavedChoice();
        StartingTypeFallsBackToFirstNonEmptyRow();
        EmptyMixHasNoStartingType();
        ActiveRowsKeepUserOrder();
        InspectorVisibilityCoversIndependentGates();
        DamageFeedActionsRemainStable();
    }

    private static void InspectorVisibilityCoversIndependentGates()
    {
        // shellEnabled, ammoEnabled, ammoReady, showSection, shellControls, ammoControls
        VisibilityCase(false, false, false, false, false, false);
        VisibilityCase(false, false, true, false, false, false);
        VisibilityCase(false, true, false, false, false, false);
        VisibilityCase(false, true, true, true, false, true);
        VisibilityCase(true, false, false, true, true, false);
        VisibilityCase(true, false, true, true, true, false);
        VisibilityCase(true, true, false, true, true, false);
        VisibilityCase(true, true, true, true, true, true);
    }

    private static void VisibilityCase(bool shellEnabled, bool ammoEnabled, bool ammoReady,
        bool showSection, bool shellControls, bool ammoControls)
    {
        var actual = AmmoInspectorPolicy.Visibility(shellEnabled, ammoEnabled, ammoReady);
        var name = $"shell={shellEnabled}, ammo={ammoEnabled}, ready={ammoReady}";
        Equal(showSection, actual.ShowSection, name + " show section");
        Equal(shellControls, actual.ShellControls, name + " shell controls");
        Equal(ammoControls, actual.AmmoControls, name + " ammo controls");
    }

    private static void StartingTypeUsesCarriedSavedChoice()
    {
        var entries = new[]
        {
            new AmmoLoadoutEntry("ap", 8),
            new AmmoLoadoutEntry("aphe", 0),
            new AmmoLoadoutEntry("heat", 4)
        };
        Equal("heat", AmmoInspectorPolicy.StartingProfile(entries, "heat"), "stored carried type");
        Equal("ap", AmmoInspectorPolicy.StartingProfile(entries, "aphe"), "stored zero row is ignored");
        Equal("ap", AmmoInspectorPolicy.StartingProfile(entries, null), "no stored type");
    }

    private static void StartingTypeFallsBackToFirstNonEmptyRow()
    {
        var entries = new[]
        {
            new AmmoLoadoutEntry("ap", 0),
            new AmmoLoadoutEntry("heat", 3)
        };
        Equal("heat", AmmoInspectorPolicy.StartingProfile(entries, "ap"), "fallback skips zero row");
        Equal(0, AmmoInspectorPolicy.StartingIndex(AmmoInspectorPolicy.Active(entries), "heat"), "active index");
    }

    private static void EmptyMixHasNoStartingType()
    {
        var entries = new[] { new AmmoLoadoutEntry("ap", 0) };
        Equal<string?>(null, AmmoInspectorPolicy.StartingProfile(entries, "ap"), "empty mix");
        Equal(-1, AmmoInspectorPolicy.StartingIndex(AmmoInspectorPolicy.Active(entries), "ap"), "empty active index");
        False(AmmoInspectorPolicy.HasActiveMix(entries), "zero rows are not an active mix");
    }

    private static void ActiveRowsKeepUserOrder()
    {
        var entries = new[]
        {
            new AmmoLoadoutEntry("b", 2),
            new AmmoLoadoutEntry("a", 0),
            new AmmoLoadoutEntry("c", 5)
        };
        var active = AmmoInspectorPolicy.Active(entries);
        Equal(2, active.Count, "active count");
        Equal("b", active[0].ProfileId, "first active id");
        Equal("c", active[1].ProfileId, "second active id");
        True(AmmoInspectorPolicy.HasActiveMix(entries), "non-empty mix");
    }

    private static void DamageFeedActionsRemainStable()
    {
        var ids = DamageFeedKeyPolicy.Actions;
        Equal(2, ids.Length, "global feed action count");
        Equal("damage-feed-toggle", ids[0], "toggle action id");
        Equal("damage-feed-cycle-corner", ids[1], "corner action id");
        Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(), "action ids are unique");
        Equal(ids.Length, DamageFeedKeyPolicy.Labels.Length, "one label per action");
        True(ids.All(id => !string.IsNullOrWhiteSpace(id)), "action ids are non-empty");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name + ": expected true");
    }

    private static void False(bool value, string name) => True(!value, name);

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected '{expected}', got '{actual}'");
    }
}
