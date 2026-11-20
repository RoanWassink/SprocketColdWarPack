# Sprocket AI Orders

AI Orders lets you give orders to your AI allies while a battle is running. A small menu on screen handles quick orders, and a tactical map lets you plan moves and attacks by clicking.

Version 0.5.4. It needs [Keybinds](../Keybinds) for its keys and [UI Kit](../UiKit) for its windows.

## What it adds

- A quick-orders menu in the bottom left of the screen. It takes no cursor, so you keep driving, aiming and firing while it is open.
- A tactical map with terrain relief, contour lines and the edge of the battle area. You are yellow, allies are blue with a number and a heading, and visible enemies are red with their distance. Enemies you saw last fade away after 90 seconds.
- Six orders: move, follow, halt, attack, retreat and release.
- Move orders with waypoints, and several tanks that spread into a line at the target.

## The orders

- Move: the tank drives to a point. If the AI stops on the way to fire, the order tries again.
- Follow: the ally follows you, in a column (one behind the other) or in a line (side by side). It uses the game's own formation system with you as the leader. If the formation does not take within about 5 seconds, the tank follows your waypoints instead.
- Halt: the tank stops. It leaves its formation first.
- Attack: the tank goes for a visible enemy.
- Retreat: the tank pulls back.
- Release: the tank goes back to the game's own AI.

A tank that has an order from you ignores the game's own orders and formation logic until you release it. Tanks without an order from you are not touched.

## How to use it

1. Open **Settings → Controls → Mod keybinds** and find the **AI Orders** group. Set keys for `Quick orders HUD`, `Tactical map`, `Menu choice 1`–`9`, and `Menu back / close`. The mod sets no keys itself.
<!-- SCREENSHOT: ai-orders-keybinds.png (the AI Orders actions in the mod keybinds panel) -->
2. In a battle, press the key for the quick-orders menu. The menu appears at the bottom left. Each line has a number, and you choose a line by pressing that number. The lines are: all allies on or off, choose allies, follow in a column, follow in a line, halt, attack, retreat, release to AI, and open the map. A status line shows the result of your last order or why it was refused.
<!-- SCREENSHOT: ai-orders-hud.png (the quick-orders menu at the bottom left in a battle) -->
3. To use the map, press the key for the tactical map. The list of allies is on the left. Click an ally to select it, and Shift-click to add or remove one. A click on empty ground clears the selection.
<!-- SCREENSHOT: ai-orders-map.png (the tactical map with allies, enemies and a planned route) -->
4. Right-click the ground to send the selected tanks there. Hold Shift while you right-click to add waypoints. Right-click a visible enemy to order an attack.
5. Use the mouse wheel to zoom and drag with the middle button to pan. Buttons on the map give the other orders (follow, halt, release, attack the nearest spotted enemy), and one centres the map on you or shows the whole map.
6. If an order is refused, for instance because you clicked outside the battle area or nothing is selected, a red cross appears at the click with a short text. A successful move shows a blue ring for a moment.

While the map is open, clicks do not fire your gun or turn the camera, and Esc closes the map before it opens the pause menu. Driving with the keyboard keeps working. A tank or missile switch releases all orders and closes the menu.

## Settings

The settings file is `BepInEx\config\sprocket.aiorders.cfg`. The game creates it at the first start.

- `Map` / `RedrawFps` (default 12, from 1 to 30): how often the map redraws. Lower it if the map stutters.
- `Diagnostics` / `DetailedLogging` (default `false`): extra lines in the log, for testing.

The keys are set in the mod keybinds panel, as in the steps above.

## Known limits

- The orders were checked in the game in one campaign-style battle only. There the map opened and a move order reached the tank. The game then froze when the first route was drawn. That was fixed in a later version.
- A later check of 0.5.2 in a battle found that the map opens and that clicks arrive in the right place. Only one of nine right-clicks became an order. The other clicks were refused without any message. Version 0.5.3 adds the red cross and the text so you can see why an order is refused.
- Not yet tested in the game: the quick-orders menu, the map redraw, halt, follow and attack, battles outside the campaign-style one, and 0.5.3 as a whole. The numbers in the menu are shared with some game actions (for example choosing ammunition), and that clash has not been checked either.
- The pack was renamed recently and has not been tested in the game again since.
- The mod only runs on the game version it was checked against (0.2.56.2). On another version it switches itself off and writes an error to the log. The game update of 18 November is not covered by 0.5.3, so the mod does not load there.

## Building

The project is a .NET project (`src\SprocketAIOrders.csproj`, solution `SprocketAIOrders.sln`, net6.0). Build it with `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. It uses the game's BepInEx and interop assemblies and the installed `SprocketUiKit.dll` and `SprocketKeybinds.dll`. The DLL is written to `build\`. `Verify.ps1` and `tests\` hold the offline checks. The plugin folder is `BepInEx\plugins\SprocketAIOrders\` and the mod ships only the DLL.

More notes: a full guide in Dutch ([README-NL.md](README-NL.md)), [CHANGELOG.md](CHANGELOG.md) and [TESTPLAN.md](TESTPLAN.md). Plugin ID: `sprocket.aiorders`.
