namespace SprocketUiKit;

internal sealed class GraphPortGesture<T> where T : struct
{
    private Vec start;
    internal T Source { get; private set; }
    internal bool Active { get; private set; }
    internal bool Dragging { get; private set; }
    internal void Begin(Vec point, T source, bool port)
    { start = point; Source = source; Active = port; Dragging = false; }
    internal void Move(Vec point, float scale)
    { if (Active) Dragging |= GraphMath.Dragged(start, point, scale); }
    internal bool End() { bool drop = Active && Dragging; Cancel(); return drop; }
    internal void Cancel() { Active = Dragging = false; }
}
