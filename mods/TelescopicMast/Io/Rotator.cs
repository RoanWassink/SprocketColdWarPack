using System.Globalization;
namespace SprocketTelescopicMast.Io;

public readonly record struct RotatorCommand(bool Run, bool Reverse, bool Fast, bool Home);
public readonly record struct RotatorState(string PartId, string Name, double Angle);
public interface IRotatorSignals { IReadOnlyList<RotatorState> Rotators { get; } }

public sealed class RotatorOutput : IOutputType
{
    public string Id => "rotator";
    public string DisplayName => "Rotator";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[] {
        ParamDef.Float("slow", "Slow speed", 1, 360, 30, 1),
        ParamDef.Float("fast", "Fast speed", 1, 720, 90, 1),
        ParamDef.Enum("direction", "Direction", new[] { "CW", "CCW" }, "CW"),
        ParamDef.Int("target", "Target angle", 0, 359, 0)
    });
    public IReadOnlyList<IoCommand> Commands { get; } = new[] { IoCommand.Follow };
    public string Describe(IReadOnlyDictionary<string, string> values) => "Rotator";
}

public sealed class RotatorStateInput : IFeedbackInput
{
    public string Id => "rotator.state";
    public string DisplayName => "Rotator angle";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema { get; } = new(new[] {
        ParamDef.Choice("plate", "Plate", () => IoCatalog.Rotators, "none"),
        ParamDef.Int("from", "From angle", 0, 359, 0), ParamDef.Int("to", "To angle", 0, 359, 359)
    });
    public string Describe(IReadOnlyDictionary<string, string> v) => "Rotator angle in range";
    public InputSample Read(InputInstance instance, object? context)
    {
        if (context is not IRotatorSignals signals) return InputSample.Off;
        var plate = signals.Rotators.FirstOrDefault(r => r.PartId == instance.Get("plate"));
        return new InputSample(plate.PartId != null && RotatorMotion.InRange(plate.Angle,
            instance.GetInt("from", 0), instance.GetInt("to", 359)), 0);
    }
}

/// <summary>Transient motion only. Clockwise from above is positive about Sprocket local Y.</summary>
public sealed class RotatorMotion
{
    public const double RampSeconds = .3, MaxSpeed = 720;
    public double Angle { get; private set; }
    public double Velocity { get; private set; }
    public bool Moving => Velocity != 0;
    public RotatorCommand Command { get; private set; }
    private double requested = double.NaN, rampFrom, rampTo, rampTime;
    private double homeAcceleration;
    public static double Wrap(double a) => double.IsFinite(a) ? (a % 360 + 360) % 360 : 0;
    public static double Delta(double from, double to) => (Wrap(to) - Wrap(from) + 540) % 360 - 180;
    public static bool InRange(double angle, double from, double to)
    { angle = Wrap(angle); from = Wrap(from); to = Wrap(to); return from <= to ? angle >= from && angle <= to : angle >= from || angle <= to; }
    public void Reset() { Angle = Velocity = 0; requested = double.NaN; rampTime = 0; Command = default; }
    internal void Follow(RotatorMotion primary)
    {
        Angle=primary.Angle; Velocity=primary.Velocity; Command=primary.Command;
        requested=primary.requested; rampFrom=primary.rampFrom; rampTo=primary.rampTo; rampTime=primary.rampTime; homeAcceleration=primary.homeAcceleration;
    }
    public static double Speed(FlowNode? node, bool fast)
    { var v = node?.Number(fast ? "fast" : "slow", fast ? 90 : 30) ?? (fast ? 90 : 30); return Math.Clamp(double.IsFinite(v) ? v : 30, 1, fast ? MaxSpeed : 360); }
    public void Tick(FlowNode? node, RotatorCommand command, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        bool enteringHome = command.Home && !Command.Home;
        Command = command;
        double speed = Speed(node, command.Fast), target = Math.Clamp(node?.Number("target", 0) ?? 0, 0, 359);
        double desired = command.Run ? speed * ((node?.Get("direction", "CW") == "CCW") ^ command.Reverse ? -1 : 1) : 0;
        if (command.Home)
        {
            if (enteringHome) homeAcceleration = Math.Max(speed,Math.Abs(Velocity)) / RampSeconds;
            requested = double.NaN; // home chooses its own direction, regardless of reverse/run
        }
        else if (desired != requested)
        { requested = desired; rampFrom = Velocity; rampTo = desired; rampTime = 0; }
        // Integrate the ramp, including low frame rates, without a frame-sized jump or skipped game time.
        while (dt > 1e-10)
        {
            double h = Math.Min(dt, 1.0 / 120), before = Velocity, distance = Delta(Angle, target);
            if (command.Home)
            {
                double acceleration = Math.Max(homeAcceleration,speed / RampSeconds);
                if (Math.Abs(distance) < 1e-8 && Math.Abs(Velocity) < acceleration * h)
                { Angle = Wrap(target); Velocity = 0; dt -= h; continue; }
                double homeSpeed = Math.Sign(distance) * speed;
                if (Math.Sign(Velocity) == Math.Sign(distance) && distance * distance <= Math.Pow(Velocity * Velocity / (2 * acceleration) + Math.Abs(Velocity) * h, 2)) homeSpeed = 0;
                Velocity += Math.Clamp(homeSpeed - Velocity, -acceleration * h, acceleration * h);
            }
            else
            {
                rampTime = Math.Min(RampSeconds, rampTime + h);
                Velocity = rampFrom + (rampTo - rampFrom) * (rampTime / RampSeconds);
                if (rampTime >= RampSeconds - 1e-10) Velocity = rampTo;
            }
            Velocity = Math.Clamp(Velocity, -MaxSpeed, MaxSpeed);
            double step = (before + Velocity) * .5 * h;
            if (command.Home && Math.Sign(step) == Math.Sign(distance) && Math.Abs(step) >= Math.Abs(distance)
                && Math.Abs(Velocity) <= Math.Max(homeAcceleration,speed / RampSeconds) * h + 1e-8)
            { Angle = Wrap(target); Velocity = 0; }
            else Angle = Wrap(Angle + step);
            dt -= h;
        }
    }
    public string Footer(FlowNode node)
    {
        string angle = Angle.ToString("0", CultureInfo.InvariantCulture) + "\u00b0";
        if (Command.Home) return "Home " + node.Number("target").ToString("0", CultureInfo.InvariantCulture) + "\u00b0 \u00b7 " + (Velocity == 0 ? "holding" : angle);
        return Velocity == 0 ? "Stopped \u00b7 " + angle : "Spinning " + (Velocity > 0 ? "CW " : "CCW ") + Math.Abs(Velocity).ToString("0", CultureInfo.InvariantCulture) + "\u00b0/s \u00b7 " + angle;
    }
}

/// <summary>One vehicle COM refresh per 100 ms while moving, plus one at the final stopped pose.</summary>
public sealed class RotatorComGate
{
    private double next;
    private bool dirty;
    public bool Tick(double now, bool changed, bool moving, bool stopped = false)
    {
        dirty |= changed || stopped;
        if (!dirty || moving && !stopped && now + 1e-9 < next) return false;
        dirty = false; next = now + .1; return true;
    }
}
