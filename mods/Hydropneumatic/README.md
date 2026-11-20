# Sprocket Hydropneumatic

Hydropneumatic adds a suspension part that you can adjust while you drive. You raise, lower and tilt the whole tank, or the front and rear on their own, with keys that you choose yourself.

Version 0.4.4. It needs [Keybinds](../Keybinds) for its keys.

## What it adds

- A suspension called "Hydropneumatic", in the suspension list next to Christie, torsion bar and HVSS. It becomes available for vehicles dated 1 January 1967 or later. In the last era, where every dated part is available, it is there too.
- Raising and lowering the whole tank while driving, and tilting it forward or backward. A neutral action brings the tank back to its normal stance, gradually.
- Optional separate control of front and rear. The same five actions then raise and lower the front and the rear on their own.
- Settings per tank in the designer: wheel travel, travel kept in reserve, how fast the height changes, spring firmness, firmness when compressed, and bounce damping.
- A price that fits a premium suspension: 185 kg of suspension mass per roadwheel, and a hydraulic unit that costs 1.20 per kg plus 160 for assembly. These are choices for the game and are not measurements of a real tank.

## How to use it

1. Open the tank designer and select the suspension. In the suspension list, pick "Hydropneumatic".
<!-- SCREENSHOT: hydro-suspension-list.png (the suspension list with Hydropneumatic selected) -->
2. In the suspension settings, set the wheel travel and the other values to taste. New profiles start with 100 mm of travel up and down, at the front and at the rear. The button "Reset HPS settings" puts all values back to their defaults.
<!-- SCREENSHOT: hydro-settings.png (the Hydropneumatic settings in the suspension panel) -->
3. If you want to control front and rear separately, switch on independent control in the same settings.
4. Open the game's Settings screen, go to Controls, and open the mod keybinds panel. Under the labels starting with "HPS /" set a key for each action. The mod sets no keys itself.
<!-- SCREENSHOT: hydro-keybinds.png (the HPS actions in the mod keybinds panel) -->
5. Drive the tank and use the keys. In normal mode they act on the whole tank, in independent mode on front and rear:

| Label | Normal mode | Independent mode |
|---|---|---|
| HPS / Raise | Raise whole tank | Raise front |
| HPS / Lower | Lower whole tank | Lower front |
| HPS / Tilt forward | Lean forward | Raise rear |
| HPS / Tilt back | Lean backward | Lower rear |
| HPS / Neutral | Return to neutral | Return to neutral |

If you switch away from the Hydropneumatic part and back again in the same editing session, the part gets its previous wheel layout back. The settings are saved inside the tank's blueprint. If you import a tank that is dated too early for the part, the tank keeps the part and its normal suspension, and one warning goes to the log.

## Settings

The settings file is `BepInEx\config\sprocket.hydropneumatic.cfg`. The game creates it at the first start. It has two entries: `General / Enabled` (default `true`) and `Diagnostics / VerboseLogging` (default `false`, switch it on only when you hunt for a problem). A `[Keys]` section may hold key values from older versions. They are copied once into the Keybinds menu.

The per-tank values (wheel travel 300 mm by default, 30 mm of travel in reserve, a height change speed of 40 mm/s, firmness, firmness when compressed, bounce damping) are in the designer, as described above.

## Known limits

- Earlier builds were tried in the game by the author. For 0.4.4, the date limit (part available from the Coldwar2 era, not in Coldwar1) is listed as not yet tested in the game.
- Rebinding, moving old key values into Keybinds, the reset button and both control modes (whole tank and independent) are listed as still needing in-game tests.
- The pack was renamed recently and has not been tested in the game again since.
- The mod was written for game version 0.2.56.2.

## Building

The project is a .NET project in this folder (`SprocketHydropneumatic.csproj`, net6.0). Build it with `dotnet build SprocketHydropneumatic.csproj -c Release -p:GameDir=<path to Sprocket folder>`. It also needs the Keybinds DLL from `$(GameDir)\BepInEx\plugins\SprocketKeybinds`. Tests run with `dotnet run --project tests\Hydro.Tests.csproj -c Release`.

More notes: [GUIDE.md](GUIDE.md), [REFERENCE-GUIDE.md](REFERENCE-GUIDE.md), [BALANCE.md](BALANCE.md) (in Dutch), [BUILD.md](BUILD.md) and [RELEASE-NOTES.md](RELEASE-NOTES.md). Licence: MIT ([LICENSE](LICENSE)). Plugin ID: `sprocket.hydropneumatic`.
