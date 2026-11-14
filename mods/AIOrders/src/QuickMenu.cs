namespace SprocketAIOrders;

// Pure F9 quick-HUD state; kept free of Unity types so the tests can cover
// visibility, the digit gate and the map hand-off without a game instance.
public sealed class QuickMenu
{
    public NumberMenu Menu { get; }
    public bool Visible { get; private set; }
    public bool MapOpen { get; private set; }
    public bool HudVisible => Visible && !MapOpen;
    // Digits belong to AI Orders while the HUD is up; on the map the existing
    // map selection keeps them, and otherwise the game keeps them.
    public bool AcceptsDigits => Visible || MapOpen;

    public QuickMenu(NumberMenu? menu = null) => Menu = menu ?? new NumberMenu();

    // F9 toggles the HUD. The map owns the digits, so it never opens over the map.
    public void Toggle()
    {
        if (MapOpen) return;
        Visible = !Visible;
        Menu.Reset();
    }

    public void Close()
    {
        Visible = false;
        Menu.Reset();
    }

    // The M panel opened: hide the HUD and remember that the map owns the digits.
    public void MapOpened()
    {
        MapOpen = true;
        Close();
    }

    public void MapClosed() => MapOpen = false;

    // A successful order finishes the menu and hides the HUD; a refused order leaves it up.
    public void Complete()
    {
        if (Visible) Close();
    }

    // One digit while the HUD is visible. 0 from root and 9 (map) close it here;
    // the other orders close it from Complete() once the host has issued them.
    public string? Press(int key, int itemCount)
    {
        if (!Visible || MapOpen) return null;
        var action = Menu.Press(key, itemCount);
        if (action is "close" or "map") Close();
        return action;
    }
}
