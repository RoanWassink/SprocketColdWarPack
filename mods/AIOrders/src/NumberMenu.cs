namespace SprocketAIOrders;

// Pure navigation state; engine references stay in OrdersHost snapshots.
public sealed class NumberMenu
{
    public string Screen { get; private set; } = "root";
    public string TargetOrder { get; private set; } = "Attack";
    public int Page { get; private set; }
    public void Reset() { Screen = "root"; Page = 0; }
    public string? Press(int key, int itemCount)
    {
        if (key < 0 || key > 9) return null;
        if (key == 0)
        { if (Screen == "root") return "close"; Reset(); return null; }
        if (Screen == "root")
        {
            switch (key)
            {
                case 1: return "all";
                case 2: Screen = "units"; Page = 0; return "units";
                case 3: return "FollowColumn";
                case 4: return "FollowLine";
                case 5: return "Halt";
                case 6: case 7:
                    TargetOrder = key == 6 ? "Attack" : "Retreat";
                    Screen = "targets"; Page = 0; return "targets";
                case 8: return "Release";
                case 9: return "map";
                default: return null;
            }
        }
        var pages = Math.Max(1, (itemCount + 6) / 7);
        if (key == 8) { Page = (Page + pages - 1) % pages; return null; }
        if (key == 9) { Page = (Page + 1) % pages; return null; }
        var index = Page * 7 + key - 1;
        return index < itemCount ? (Screen == "units" ? "unit:" : "target:") + index : null;
    }
}
