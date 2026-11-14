namespace SprocketTelescopicMast.Io;

/// <summary>
/// Undo/redo for one vehicle flow while the panel is open: whole-flow JSON snapshots (lossless,
/// so unknown blocks and fields return byte for byte), at most <see cref="Limit"/> steps each way.
/// </summary>
public sealed class FlowHistory
{
    public const int Limit = 50;
    private readonly LinkedList<string> undo = new(), redo = new();
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    internal void Record(string before) { undo.AddLast(before); Trim(undo); redo.Clear(); }
    internal string? Undo(string current) => Swap(undo, redo, current);
    internal string? Redo(string current) => Swap(redo, undo, current);
    public void Clear() { undo.Clear(); redo.Clear(); }
    private static string? Swap(LinkedList<string> from, LinkedList<string> to, string current)
    {
        if (from.Count == 0) return null;
        string state = from.Last!.Value; from.RemoveLast();
        to.AddLast(current); Trim(to); return state;
    }
    private static void Trim(LinkedList<string> list) { while (list.Count > Limit) list.RemoveFirst(); }
}
