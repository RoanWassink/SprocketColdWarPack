namespace SprocketTelescopicMast.Io;

// Built-in input types. Each is one class plus IoRegistry.RegisterInput: they read the per-vehicle
// IVehicleSignals snapshot and nothing else, so the engine, panel and storage need no change for them.

/// <summary>Shared "cannon" + "fireGroup" parameters for every cannon input.</summary>
public static class CannonParams
{
    public const int AnyGroup = 0;
    public static ParamDef FireGroup() => ParamDef.Int("fireGroup", "Fire group", AnyGroup, FireGroups.Max, AnyGroup);
    public static IReadOnlyList<ParamOption> Groups { get; } = new[] { new ParamOption("0", "Any group") }
        .Concat(Enumerable.Range(1, FireGroups.Max).Select(g => new ParamOption(g.ToString(), "Group " + g))).ToArray();
    public static ParamDef Cannon() => ParamDef.Choice("cannon", "Cannon", () => IoCatalog.Cannons, PartSelector.Any);

    /// <summary>"group 1", "Cannon 2 - 75 mm (group 1)" or "cannon #12 not found: any in group 1".</summary>
    public static string Describe(IReadOnlyDictionary<string, string> v)
    {
        string group = v.GetValueOrDefault("fireGroup", "0");
        string selector = v.GetValueOrDefault("cannon", PartSelector.Any);
        string fallback = group == "0" ? "any cannon" : $"any in group {group}";
        if (!PartSelector.TryParse(selector, out int vuid, out _)) return fallback;
        foreach (var o in IoCatalog.Cannons) if (o.Value == selector) return o.Label;
        return $"cannon #{vuid} not found: {fallback}";
    }

    /// <summary>Vehicle-scoped source and fallback, suitable for a graph footer.</summary>
    public static string Footer(string type, IReadOnlyDictionary<string, string> values)
    {
        string group = values.GetValueOrDefault("fireGroup", "0");
        string selector = values.GetValueOrDefault("cannon", PartSelector.Any);
        string name = type switch
        {
            "cannon.fired" => "Fired", "cannon.reloaded" => "Reloaded", "cannon.loaded" => "Ready",
            _ => values.GetValueOrDefault("mode", "loading") == "notReady" ? "Not ready" : "Reloading"
        };
        bool specific = PartSelector.TryParse(selector, out int vuid, out _);
        var chosen = specific ? IoCatalog.Cannons.FirstOrDefault(o => PartSelector.TryParse(o.Value, out int id, out _) && id == vuid) : default;
        string source;
        if (specific && !string.IsNullOrEmpty(chosen.Value)) source = chosen.Label;
        else
        {
            var matches = IoCatalog.Cannons.Where(o => o.Value != PartSelector.Any &&
                IoCatalog.CannonFireMasks.TryGetValue(o.Value, out int mask) && int.TryParse(group, out int g) && (g == AnyGroup || FireGroups.Contains(mask, g))).ToArray();
            source = matches.Length == 1 ? matches[0].Label : matches.Length > 1 ? "Any of " + matches.Length + " cannons" : "Any cannon";
            if (specific) source = "Missing cannon #" + vuid + "; fallback: " + source;
        }
        return name + " - " + source + (group == "0" ? " - any group" : " - group " + group);
    }

    /// <summary>True for a cannon selected by this instance (specific VUID, or fallback to the fire group).</summary>
    public static Func<int, int, bool> Matcher(InputInstance instance, IVehicleSignals signals)
    {
        int group = instance.GetInt("fireGroup", AnyGroup);
        int? vuid = PartSelector.ResolveCannon(instance.Get("cannon"), signals.Cannons, out _);
        return vuid is int v ? (cannonVuid, _) => cannonVuid == v : (_, mask) => group == AnyGroup || FireGroups.Contains(mask, group);
    }
}

public sealed class CannonFiredInput : IInputType
{
    public string Id => "cannon.fired";
    public string DisplayName => "Cannon fired";
    public SignalKind SignalKind => SignalKind.Event;
    public ParamSchema Schema { get; } = new(new[] { CannonParams.FireGroup(), CannonParams.Cannon() });
    public string Describe(IReadOnlyDictionary<string, string> v) => $"Cannon fired ({CannonParams.Describe(v)})";
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s) return InputSample.Off;
        var match = CannonParams.Matcher(instance, s);
        int n = 0;
        foreach (var shot in s.Fired) if (match(shot.Vuid, shot.FireGroupMask)) n++;
        return new InputSample(false, n);
    }
}

public sealed class CannonReloadingInput : IInputType
{
    public string Id => "cannon.reloading";
    public string DisplayName => "Cannon reloading";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[]
    {
        CannonParams.FireGroup(),
        ParamDef.Enum("mode", "Mode", new[] { "loading", "notReady" }, "loading"),
        CannonParams.Cannon()
    });
    public string Describe(IReadOnlyDictionary<string, string> v) =>
        (v.GetValueOrDefault("mode", "loading") == "notReady" ? "Cannon not ready" : "Cannon reloading") + $" ({CannonParams.Describe(v)})";
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s) return InputSample.Off;
        var match = CannonParams.Matcher(instance, s);
        bool notReady = instance.Get("mode") == "notReady";
        foreach (var c in s.Cannons)
            if (match(c.Vuid, c.FireGroupMask) && (notReady ? c.NotReady : c.Loading)) return new InputSample(true, 0);
        return InputSample.Off;
    }
}

/// <summary>Event: a selected cannon finished its reload (Loading -> Loaded). "Close at the end of the reload cycle".</summary>
public sealed class CannonReloadedInput : IInputType
{
    public string Id => "cannon.reloaded";
    public string DisplayName => "Cannon reloaded";
    public SignalKind SignalKind => SignalKind.Event;
    public ParamSchema Schema { get; } = new(new[] { CannonParams.FireGroup(), CannonParams.Cannon() });
    public string Describe(IReadOnlyDictionary<string, string> v) => $"Cannon reloaded ({CannonParams.Describe(v)})";
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s) return InputSample.Off;
        var match = CannonParams.Matcher(instance, s);
        int n = 0;
        foreach (var c in s.Cannons) if (match(c.Vuid, c.FireGroupMask)) n += Math.Max(0, c.ReloadedEvents);
        return new InputSample(false, n);
    }
}

/// <summary>Level: a selected cannon is loaded and ready to fire.</summary>
public sealed class CannonLoadedInput : IInputType
{
    public string Id => "cannon.loaded";
    public string DisplayName => "Cannon loaded";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[] { CannonParams.FireGroup(), CannonParams.Cannon() });
    public string Describe(IReadOnlyDictionary<string, string> v) => $"Cannon loaded ({CannonParams.Describe(v)})";
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s) return InputSample.Off;
        var match = CannonParams.Matcher(instance, s);
        foreach (var c in s.Cannons) if (match(c.Vuid, c.FireGroupMask) && c.Loaded) return new InputSample(true, 0);
        return InputSample.Off;
    }
}

/// <summary>Level (+ rising-edge events): another hinge has reached open or closed. Enables chains.</summary>
public sealed class HingeStateInput : IFeedbackInput
{
    public const string NoHinge = "none";
    public string Id => "hinge.state";
    public string DisplayName => "Hinge state";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[]
    {
        ParamDef.Choice("hinge", "Hinge", () => IoCatalog.Hinges, NoHinge),
        ParamDef.Enum("state", "State", new[] { "open", "closed" }, "open")
    });
    public string Describe(IReadOnlyDictionary<string, string> v)
    {
        string selector = v.GetValueOrDefault("hinge", NoHinge);
        string state = v.GetValueOrDefault("state", "open") == "closed" ? "is closed" : "is open";
        if (!PartSelector.TryParse(selector, out _, out var name)) return $"Hinge (pick one) {state}";
        foreach (var o in IoCatalog.Hinges) if (o.Value == selector) return $"Hinge {o.Label} {state}";
        return $"Hinge '{name}' {state} (not found)";
    }
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s) return InputSample.Off;
        int index = PartSelector.ResolveHinge(instance.Get("hinge"), s.Hinges);
        if (index < 0) return InputSample.Off;
        var h = s.Hinges[index];
        return new InputSample(instance.Get("state") == "closed" ? h.ClosedReached : h.OpenReached, 0);
    }
}

/// <summary>Level: telescopic masts of this vehicle fully extended, fully retracted, or raised at all.</summary>
public sealed class MastStateInput : IInputType
{
    public const float Tolerance = .001f;
    public string Id => "mast.state";
    public string DisplayName => "Mast state";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[] { ParamDef.Enum("state", "State", new[] { "extended", "retracted", "raised" }, "extended") });
    public string Describe(IReadOnlyDictionary<string, string> v) => v.GetValueOrDefault("state", "extended") switch
    {
        "retracted" => "Mast fully retracted",
        "raised" => "Mast raised (not retracted)",
        _ => "Mast fully extended"
    };
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IVehicleSignals s || s.Masts.Count == 0) return InputSample.Off;
        bool on = instance.Get("state") switch
        {
            "retracted" => s.Masts.All(m => m.Fraction <= Tolerance),
            "raised" => s.Masts.Any(m => m.Fraction > Tolerance),
            _ => s.Masts.Any(m => m.Fraction >= 1 - Tolerance)
        };
        return new InputSample(on, 0);
    }
}

/// <summary>Level: the vehicle's own brake state (not one-sided steering).</summary>
public sealed class VehicleBrakingInput : IInputType
{
    public string Id => "vehicle.braking";
    public string DisplayName => "Vehicle braking";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema => ParamSchema.Empty;
    public string Describe(IReadOnlyDictionary<string, string> v) => "Vehicle braking";
    public InputSample Read(InputInstance instance, object? context) =>
        context is IVehicleSignals s ? new InputSample(s.Braking, 0) : InputSample.Off;
}

public sealed class KeyInput : IInputType
{
    public const int Actions = 4;
    public string Id => "key";
    public string DisplayName => "Key (I/O Flow / Action)";
    public SignalKind SignalKind => SignalKind.Event;
    public ParamSchema Schema { get; } = new(new[] { ParamDef.Int("action", "Action", 1, Actions, 1) });
    public string Describe(IReadOnlyDictionary<string, string> v) => $"Key I/O Flow / Action {v.GetValueOrDefault("action", "1")}";
    public InputSample Read(InputInstance instance, object? context)
    {
        // Keys belong to the player's own vehicle only; AI vehicles never see them.
        if (context is not IVehicleSignals { IsPlayer: true } s) return InputSample.Off;
        int action = instance.GetInt("action", 1);
        return new InputSample(s.KeyHeld(action), s.KeyPresses(action));
    }
}
