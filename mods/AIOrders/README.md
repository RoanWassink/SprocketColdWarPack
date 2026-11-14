# Sprocket AI Orders

Lets you give orders to your AI allies in battle: a small number-key menu on F9 and a tactical map on M with move, follow, halt, attack, retreat and release orders.

- Version: 0.5.2
- Requires: Sprocket UI Kit (`sprocket.uikit`, >=0.1.2 <0.2.0) and Sprocket Keybinds (`sprocket.keybinds`, >=0.1.7 <0.2.0)
- Optional: Sprocket Expedition (`sprocket.expedition`, soft dependency); in Expedition battles the orders are handed to Expedition, so Expedition releases those tanks from its own AI
- Plugin GUID: `sprocket.aiorders`
- Game version tested: 0.2.56.2

## Features

- F9 opens a small, non-modal quick-orders HUD at the bottom left: no cursor, and driving, aiming, firing and the camera keep working. The number keys are only read while the HUD is visible.
- Menu: all allies on/off, choose allies, Follow column, Follow line, Halt, Attack, Retreat, Release to AI, open the map; a status line shows the result of the last order or why it was refused.
- M opens a tactical map with terrain relief, contour lines and the battle boundary; you in yellow, allies in blue with number and heading, visible enemies in red with distance, last-seen enemies fading after 90 seconds.
- On the map: click or Shift+click to select, right-click ground to move (Shift for waypoints; several tanks spread into a line), right-click a visible enemy to attack, wheel zoom, middle-drag pan.
- Follow uses the game's own formation system with you as leader and falls back to waypoint following if the formation does not take within about 5 seconds.
- Halt, Move and Attack first take a tank out of its formation; Move retries the target if the AI stops to fire; Release to AI hands a tank back to the game's AI.
- Three Harmony prefix patches: `CommanderAI.Order` and `CommanderAI.UpdateFormationTasks` skip the game's own order and formation logic for tanks you have ordered, `VehicleOrderConstruction.SelectConstructAndApply` only observes native player commands.
- Safety gate: only runs on the exact `GameAssembly.dll` build it was checked against (game 0.2.56.2); on another build it disables itself with an error in the log.

## Settings and keys

- Config: `BepInEx\config\sprocket.aiorders.cfg` (created by BepInEx on first start). `[Map] RedrawFps = 12` (1 to 30) and `[Diagnostics] DetailedLogging = true` (verbose trace; set to `false` for normal play).
- Keys (defaults): `F9` Quick orders HUD, `M` Tactical map, `1` to `9` Menu choice, `0` Menu back / close. Rebind them under "AI Orders" in the game's Settings > Controls/keybinds > Mod keybinds, which Sprocket Keybinds provides.
- On the map the number keys select tanks 1 to 9 (Shift adds) and 0 toggles all or none.
- Shipped files: `BepInEx\plugins\SprocketAIOrders\SprocketAIOrders.dll` only; no part, technology or localization files.

## Status

- In game (0.5.0, Expedition battle): the map opens and a move order reaches the tank (`Move: 1 unit(s)`). The game then froze at the first drawn route; that was fixed in 0.5.1.
- 0.5.2 is installed but not yet tested in game: the F9 HUD, the map redraw, halt, follow, attack, Expedition and Custom Battle still need checking. The number keys are shared with game actions (for example ammo selection), which is also unchecked.
- The whole pack was just renamed and has not been re-tested in game since.

## Building

.NET project (`src\SprocketAIOrders.csproj`, solution `SprocketAIOrders.sln` in this folder, net6.0); build with `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. It references the game's BepInEx and interop assemblies and the installed `SprocketUiKit.dll` and `SprocketKeybinds.dll`; the DLL is written to `build\`.
`Verify.ps1` and `tests\` hold the offline checks. Full usage guide in Dutch: [README-NL.md](README-NL.md); see also [CHANGELOG.md](CHANGELOG.md) and [TESTPLAN.md](TESTPLAN.md).
