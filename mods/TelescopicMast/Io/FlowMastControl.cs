namespace SprocketTelescopicMast.Io;

public sealed class FlowMastOutput : IOutputType
{
    public string Id => "mast";
    public string DisplayName => "Mast";
    public SignalKind SignalKind => SignalKind.Level;
    public ParamSchema Schema => ParamSchema.Empty;
    public IReadOnlyList<IoCommand> Commands { get; } = new[] { IoCommand.Follow, IoCommand.Toggle, IoCommand.Open, IoCommand.Close };
    public string Describe(IReadOnlyDictionary<string, string> values) => "Mast";
}

/// <summary>Last command wins. Re-reading an unchanged flow level is not a new command.</summary>
public sealed class FlowMastControl
{
    private bool initialized, last, fromFlow;
    public int Direction { get; private set; }
    public void Flow(bool raised)
    { if (!initialized || raised != last) { initialized = true; last = raised; fromFlow = true; Direction = raised ? 1 : -1; } }
    public void Manual(int direction) { if (direction != 0) { fromFlow = false; Direction = Math.Sign(direction); } }
    public void ReleaseManual() { fromFlow = false; Direction = 0; }
    public void Unbind() { initialized = false; if (fromFlow) { Direction = 0; fromFlow = false; } }
}
