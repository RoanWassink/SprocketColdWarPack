# Sprocket Gun Sights

Replaces the stock gunner-sight picture with period sights. Each placed sight gets a country and a historical variant with its own magnification steps and reticle, from the First World War to the Cold War.

- Version: 0.2.4
- Requires: no other plugin (runs on BepInEx 6.0.0-be.788, IL2CPP)
- Optional: Sprocket Expanded Ballistics (`sprocket.expandedballistics`), which supplies the loaded and next round shown in the sight
- Plugin GUID: `sprocket.gunsights`
- Game version tested: 0.2.56.2

## Features

- 33 sight variants for Germany, the Soviet Union, the United States and the United Kingdom, each with its own start date (for example TZF 12a, TSh-2-22, M83, EMES 15).
- Chosen per placed sight in the vehicle editor (right-click, "Gun sight"): first the country, then the variant. The panel lists the tanks it was used on, its optics, and whether the magnification is sourced or an estimate.
- The list only offers variants available at the vehicle's date. Too-early variants are shown in grey with their start date, and a saved choice is never overwritten. The default sight follows the vehicle date.
- Mouse-wheel steps between the optical powers of the sight (for example 2.5x and 5x on a dual-optic sight) instead of the stock smooth zoom; the zoom follows a reference field of view of 60 degrees as 1x.
- Period reticles drawn at their true angular size from the live field of view: black for etched wartime reticles, red for illuminated Cold War ones.
- Optional turret-following view: the scope picture is shown along the sight's real axis, so it follows the turret and gun while the mouse still commands the aim. The ATGM guidance sight keeps its free view.
- Hides the game's own scope reticle while a period sight is shown, and optionally shows the loaded and next round from Expanded Ballistics.
- The choice is saved with the vehicle (key `sprocketGunSightVariant`). A saved sight that is too early for the vehicle keeps its identity, the stock sight is used and one warning is logged.

## Settings and keys

Config file: `BepInEx\config\sprocket.gunsights.cfg`, created on first start. The mod has no key bindings; zoom steps use the mouse wheel.

| Section / key | Default | Meaning |
|---|---|---|
| `General / Enabled` | `true` | Enable the mod (restart to change). |
| `General / HideStockReticle` | `true` | Hide the game's own scope reticle while a period sight is shown. |
| `Optics / ReferenceFieldOfView` | `60` | Vertical field of view in degrees that counts as 1x (20 to 100). |
| `Optics / MinimumLinePixels` | `1.6` | Thinnest reticle stroke on screen (0.8 to 6). |
| `View / ScopeFollowsTurret` | `true` | Show the scope picture along the sight's real axis. |
| `View / ShowAmmunition` | `true` | Show loaded and next round from Expanded Ballistics. |

Shipped data: none. The mod is one DLL in `BepInEx\plugins\SprocketGunSights\`; the sight catalogue is compiled in.

## Status

No in-game test result of 0.2.4 is recorded. The date gate (a Coldwar2 vehicle should not list EMES 15 and the M1 GPS, but show them in grey with their start dates) is listed as not yet tested, and the author's notes of 2026-10-30 say that scope calibration, hiding of the stock overlay and save and reload were not yet confirmed in the game. The whole pack was just renamed and has not been re-tested in the game yet.

## Building

.NET project in `src\` (`SprocketGunSights.csproj`, net6.0); build with `dotnet build src\SprocketGunSights.csproj -c Release -p:GameDir=<path to Sprocket folder>`. No other plugin is needed to build; the Expanded Ballistics link is found at run time.

Release notes: [CHANGELOG.md](CHANGELOG.md). Tests: `dotnet run --project tests\Tests.csproj -c Release` (catalogue and reticle checks). `Audit-Hooks.ps1` checks that every patched game method exists exactly once in the game's interop assemblies.
