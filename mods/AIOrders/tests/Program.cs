using SprocketAIOrders;
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
static bool Near(float a, float b, float eps = .01f) => MathF.Abs(a - b) < eps;

Check(!OrderPolicy.FollowNeedsMove(15, false) && OrderPolicy.FollowNeedsMove(15, true), "Follow hysteresis prevents repeated start/stop at the same distance");
Check(!OrderPolicy.FollowNeedsMove(float.NaN, true), "Invalid follow distance does not dispatch movement");
Check(!OrderPolicy.RefreshMove(.5f, 100) && !OrderPolicy.RefreshMove(10, 2) && OrderPolicy.RefreshMove(1, 8), "Moving leader updates are rate- and distance-limited");
foreach (var threat in new[] { (0f, 10f), (10f, 0f), (-10f, -20f) })
{
    var goal = OrderPolicy.Retreat(0, 0, threat.Item1, threat.Item2);
    Check(Near(MathF.Sqrt(goal.X * goal.X + goal.Z * goal.Z), 150), "Retreat distance is 150 m");
    Check(goal.X * threat.Item1 + goal.Z * threat.Item2 < 0, "Retreat goes away from threat");
}
bool refused = false;
try { OrderPolicy.Retreat(1, 1, 1, 1); } catch (InvalidOperationException) { refused = true; }
Check(refused, "Coincident threat cannot generate a poisoned waypoint");
Check(!OrderPolicy.RetryEndedMove(3.9f, false) && OrderPolicy.RetryEndedMove(4f, false) && !OrderPolicy.RetryEndedMove(30f, true), "Ended native movement is retried after settling, active path is not");
var turn = OrderPolicy.BlendHeading(0, 1, 0, -1);
Check(turn.Z < -.99f && MathF.Abs(turn.X) < .001f, "Formation direction can reverse through a complete U-turn");
turn = OrderPolicy.BlendHeading(0, 1, 0, 0);
Check(turn.X == 0 && turn.Z == 1, "Missing leader direction preserves previous valid heading");

// Fallback follow slots (leader at origin heading north, standing still).
var c0 = OrderPolicy.FollowSlot(0, 0, 0, 1, 0, 0, 0, true);
var c1 = OrderPolicy.FollowSlot(0, 0, 0, 1, 0, 0, 1, true);
Check(Near(c0.X, 0) && c0.Z < -20 && c1.Z < c0.Z - 20, "Column slots trail behind the leader one after another");
var l0 = OrderPolicy.FollowSlot(0, 0, 0, 1, 0, 0, 0, false);
var l2 = OrderPolicy.FollowSlot(0, 0, 0, 1, 0, 0, 2, false);
Check(l0.X < 0 && l2.X > 0 && Near(l0.Z, l2.Z), "Line slots spread left and right on the same row");
var moving = OrderPolicy.FollowSlot(0, 0, 0, 1, 0, 10, 0, true);
Check(moving.Z > c0.Z + 5, "Slot leads ahead along leader velocity so followers do not lag");

// Group move spread.
var spread = OrderPolicy.SpreadLine(100, 100, 100, 0, 3);
Check(spread.Length == 3 && Near(spread[1].X, 100) && Near(spread[1].Z, 100), "Middle unit of an odd group goes to the clicked point");
Check(Near(spread[0].Z, 100) && Near(spread[2].Z, 100) && Near(MathF.Abs(spread[2].X - spread[0].X), 50), "Line abreast is perpendicular to the approach, 25 m apart");
Check(OrderPolicy.SpreadLine(5, 5, 5, 5, 2).All(p => float.IsFinite(p.X) && float.IsFinite(p.Z)), "Zero-length approach still produces finite goals");
Check(OrderPolicy.SpreadLine(0, 0, 0, 0, 0).Length == 0, "Empty selection produces no goals");

// Quick menu.
var menu = new NumberMenu();
Check(menu.Press(1, 0) == "all", "Root toggles all allies");
Check(menu.Press(3, 0) == "FollowColumn" && menu.Press(4, 0) == "FollowLine", "Follow column/line quick orders");
Check(menu.Press(5, 0) == "Halt" && menu.Press(8, 0) == "Release" && menu.Press(9, 0) == "map", "Halt, release and map entries");
Check(menu.Press(2, 0) == "units" && menu.Screen == "units", "Open ally submenu");
Check(menu.Press(7, 16) == "unit:6", "Seventh row maps to seventh snapshot entry");
menu.Press(9, 16);
Check(menu.Press(1, 16) == "unit:7", "Next page maps to eighth entry");
menu.Press(9, 16);
Check(menu.Press(3, 16) == null, "Empty final-page slot issues no action");
menu.Press(0, 16);
Check(menu.Screen == "root" && menu.Page == 0, "Back resets page and returns to root");
Check(menu.Press(6, 0) == "targets" && menu.TargetOrder == "Attack", "Attack enters target submenu");
Check(menu.Press(1, 0) == null, "No invisible or absent target can be picked from empty list");
menu.Press(0, 0);
menu.Press(7, 0);
Check(menu.TargetOrder == "Retreat" && menu.Press(1, 1) == "target:0", "Retreat target preserves chosen order");
menu.Press(0, 0);
Check(menu.Press(0, 0) == "close", "Back from root closes menu");

// All root choices and submenu paths use the same pure dispatcher as Kit buttons and digits.
var rootActions = new[] { "close", "all", "units", "FollowColumn", "FollowLine", "Halt", "targets", "targets", "Release", "map" };
for (var digit = 0; digit <= 9; digit++)
{
    var byKey = new NumberMenu(); var byButton = new NumberMenu();
    var keyAction = byKey.Press(digit, 15); var buttonAction = byButton.Press(digit, 15);
    Check(keyAction == rootActions[digit] && buttonAction == keyAction && byButton.Screen == byKey.Screen,
        $"Root digit/button {digit} resolves expected action/submenu");
}
menu.Reset();
Check(menu.Press(-1, 10) == null && menu.Press(10, 10) == null && menu.Screen == "root", "Invalid digits cannot change navigation");
menu.Press(2, 15); menu.Press(8, 15);
Check(menu.Page == 2 && menu.Press(1, 15) == "unit:14", "Previous page wraps to final ally snapshot page");
menu.Press(9, 15);
Check(menu.Page == 0 && menu.Press(0, 15) == null && menu.Screen == "root", "Next wraps; zero backs without issuing order");
menu.Press(6, 9); menu.Press(9, 9);
Check(menu.Press(2, 9) == "target:8" && menu.TargetOrder == "Attack", "Paged attack chooses correct target");
menu.Press(0, 9);
Check(menu.Press(0, 9) == "close", "Zero from target submenu then root backs and closes");

// F9 non-modal HUD: visibility, the digit gate and the map hand-off.
var nav = new NumberMenu();
var quick = new QuickMenu(nav);
Check(!quick.Visible && !quick.HudVisible && !quick.AcceptsDigits, "Quick HUD starts hidden and leaves digits to the game");
quick.Toggle();
Check(quick.Visible && quick.HudVisible && quick.AcceptsDigits, "F9 shows the quick HUD and claims the digits");
quick.Toggle();
Check(!quick.Visible && !quick.AcceptsDigits, "F9 hides the quick HUD and returns the digits");
quick.Toggle();
Check(quick.Press(0, 0) == "close" && !quick.Visible && nav.Screen == "root", "0 in root closes the HUD and resets the menu");
quick.Toggle();
Check(quick.Press(2, 0) == "units" && quick.Visible, "Opening the ally submenu keeps the HUD visible");
Check(quick.Press(0, 0) == null && quick.Visible && nav.Screen == "root", "0 in a submenu only backs out and keeps the HUD");
quick.Press(3, 0);
quick.Complete();
Check(!quick.Visible && nav.Screen == "root", "A completed order hides the HUD and resets the menu");
quick.Toggle();
nav.Press(2, 0);
quick.Complete();
Check(!quick.Visible, "An order completed from a submenu also hides the HUD");
quick.Toggle();
quick.MapOpened();
Check(!quick.Visible && !quick.HudVisible && quick.AcceptsDigits && quick.MapOpen, "Opening the map hides the HUD and the map keeps the digits");
Check(quick.Press(1, 0) == null, "HUD digits do not run while the map owns them");
quick.Toggle();
Check(!quick.Visible, "F9 does not open the HUD over the map");
quick.MapClosed();
Check(!quick.AcceptsDigits, "Closing the map returns the digits to the game");
quick.Toggle();
Check(quick.Press(9, 0) == "map" && !quick.Visible, "Root 9 hands off to the map and hides the HUD");
quick.MapOpened();
quick.MapClosed();
quick.Toggle();
Check(quick.Visible && quick.AcceptsDigits, "F9 shows the HUD again after the map closes");

Check(MapPolicy.DashCount(100, 8) == 6, "Dashed route: 100 m at 8 m dashes gives 6 dashes");
Check(MapPolicy.DashCount(10, 8) == 0, "Short route is drawn solid");
Check(MapPolicy.DashCount(1e9f, 1e-3f) == MapPolicy.MaxDashes, "Huge route is capped");
Check(MapPolicy.DashCount(float.PositiveInfinity, 8) == 0 && MapPolicy.DashCount(100, float.NaN) == 0 && MapPolicy.DashCount(100, 0) == 0, "Broken lengths never loop");

MapTests.Run();
if (args.Length == 2) AssemblyChecks.Run(args[0], args[1]);
