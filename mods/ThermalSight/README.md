# Sprocket Thermal Sight

Night vision and ranging for gunner sights in Sprocket: thermal, image-intensifier and active-infrared sensor profiles, a per-vehicle heat model, an IR searchlight, a laser box sight and laser rangefinders.

- Version: 0.4.0
- Requires: Sprocket Keybinds (`sprocket.keybinds`, version >=0.1.6 <0.2.0)
- Optional: Sprocket Expanded Ballistics (assembly `SprocketExpandedBallistics`), which tells the rangefinder which round is loaded
- Plugin GUID: `sprocket.thermalsight`
- Game version tested: 0.2.56.2

## Features

- Sensor profiles from `thermal-models.json` (format version 2), each with its own start date: Active IR night sight (1950), image intensifier Gen 1 (1962) and Gen 2 (1970), thermal Gen 1 (1977), Gen 1 late (1983) and Gen 2 (1992), plus the thermal sight models 1 to 3 and Mk3 white-hot and black-hot (1977). Right-click an installed sight to pick its profile; profiles that are too early for the vehicle are labelled with the date they work from.
- Per-vehicle heat model (0.4.0): engine and exhaust, barrel after firing, and burning or cooling wrecks, read from the game at a low polling rate; `Heat / Enabled = false` gives the uniform look of 0.3.x.
- IR searchlight part (from 1950.01.01) for the Active IR profile; without one the sight shows green noise and an on-screen hint that a searchlight is needed.
- Laser box sight (from 1945.09.03): a gunner sight with a built-in manual laser. The TPD-K1 style sight and the laser rangefinder part range the same way.
- Rangefinders: manual laser, automatic laser (sets the gun convergence from any active sight) and an optical coincidence rangefinder (from 1943.10.19). Range readings can correct the aim for bullet drop, using the loaded round when Expanded Ballistics is present.
- Sight console part (remote controls for linked exterior sights), thermal sight heads, wall-mounted head, and decorative glass and window parts.
- Sight housings take the vehicle's exterior camo paint; lenses, glass and screens stay fixed.
- Extra mass and assembly cost per thermal model (for example model 2: 22 kg, cost 650).

## Settings and keys

Config file: `BepInEx\config\sprocket.thermalsight.cfg`, created on first start.

| Section / key | Default | Meaning |
|---|---|---|
| `General / Enabled` | `true` | Thermal only on recognized thermal sight parts. |
| `Rangefinder / Enabled` | `true` | Manual ranging when a laser rangefinder device is fitted. |
| `Rangefinder / MaximumDistanceMetres`, `MinimumDistanceMetres` | `4000`, `50` | Measurable distance window. |
| `Rangefinder / CooldownSeconds`, `DisplaySeconds` | `3`, `5` | Time between readings, and how long one stays visible. |
| `Heat / Enabled`, `Heat / InvertSpotY` | `true`, `false` | Heat model on or off; flip hot spots vertically if they look mirrored. |
| `Diagnostics / DetailedLogging` | `true` | Detailed sensor and heat log lines. |

The `Heat` section has many more tuning values (ambient temperature, warm-up and cool-down times, polling rate, range). Keys are bound in the game's Settings keybind menu (Sprocket Keybinds), group "Thermal": Toggle (default N), Reload profiles (default F8) and Measure range (unbound by default). The `[Keys]` entries in the cfg are legacy values imported once.

Shipped data: sensor profiles in `BepInEx\plugins\SprocketThermalSight\thermal-models.json`, models and icons in its `assets` folder, 18 part definitions and 16 English localization files in `Sprocket_Data\StreamingAssets`.

## Status

Version 0.4.0 was installed on 2026-11-13. Its heat model, the camo on all models (0.3.7), the laser box sight (0.3.6) and the rangefinder fixes (0.3.3 to 0.3.5) are listed as not yet tested in the game. Seen working so far: range measurement (log of 2026-11-02) and camo on the IR searchlight (2026-11-04). Reported on 2026-11-14: streaked, stretched camo textures on the laser rangefinder and the sight console (UV mapping from 0.3.7); a fix is in preparation. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in `src\` (`SprocketThermalSight.csproj`, net6.0); build with `dotnet build src\SprocketThermalSight.csproj -c Release -p:GameDir=<path to Sprocket folder>`, and `-p:KeybindsDll=<path>` if Sprocket Keybinds is elsewhere than `$(GameDir)\BepInEx\plugins\SprocketKeybinds`.

Docs: [GUIDE.md](GUIDE.md) (Dutch), [TEST-GUIDE-NL.md](TEST-GUIDE-NL.md), [HEAT-MAP.md](HEAT-MAP.md), [CHANGELOG.md](CHANGELOG.md). Tests: `src\tests\Sensor.Tests.csproj` and `ConsoleLink.Tests.csproj`.
