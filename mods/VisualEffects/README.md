# Sprocket Visual Effects

Adds a visual overlay to cannon shots (muzzle blast, lit smoke puffs, ground dust, a short muzzle light) and three RGB tank lamp parts that glow and cast light.

- Version: 0.4.5
- Requires: BepInEx 6 IL2CPP (build 6.0.0-be.788); no other Sprocket mod
- Optional: Sprocket Telescopic Mast (`sprocket.telescopicmast`) for per-lamp settings and switching lamps from the I/O flow
- Plugin GUID: `sprocket.visualeffects`
- Game version tested: 0.2.56.2

## Features

- Volumetric muzzle blast for cannons of 50 mm and up (default), scaled by calibre, with the native flash, sound and damage left untouched.
- Smoke puffs that are lit by the scene, plus a ring of kicked-up ground dust (one raycast per shot).
- Short HDRP point-light flash at the muzzle (pool of 2, about 0.12 s, no shadows).
- Wind (speed and direction), smoke amount, size, lifetime and brightness are configurable.
- Falls back to a simple billboard overlay if the bundled volumetric shader cannot be loaded.
- Three lamp parts in Crew / Equipment: LAMP ROUND (stock headlight shape), LAMP RECT and LAMP MARKER (stock tail light shape, marker at scale 0.6).
- Lamps behave like stock lamps when placed, mirrored, moved, rotated and painted; the glow, halo and light are separate objects that follow the part.
- Right-click a lamp for a "Lamp" section: colour preset or #RRGGBB, brightness and range. Needs Telescopic Mast to store the values per lamp.
- Light budget and distance fade: at most 24 real lamp lights by default, further lamps keep the lens glow only.
- Two Harmony postfix patches only (muzzle flash setup and the part inspector GUI); both are skipped if the game build differs from the audited one.

## Settings and keys

- Config: `BepInEx\config\sprocket.visualeffects.cfg` (created by BepInEx on first start; a default file with the same name is shipped).
- `[Visuals]`: `Enabled = true`, `MinimumCalibreMm = 50`, `MaxDistance = 1500`, `MuzzleLight = true`, `GroundDust = true`, `WindSpeed = 2.5`, `WindDirection = 200`, `FallbackToBillboard = true`.
- `[Smoke]`: `SmokeAmount`, `SmokeSize`, `SmokeLifetime`, `SmokeBrightness`, `DustAmount` (all 1 by default; 0 for no smoke or dust).
- `[Lamps]`: `LampDefaultOn = false`, `MaxLamps = 24`, `MaxDistance = 250`, per-shape fallback colour, intensity and range, `LensBrightness`, `HaloStrength`.
- `[Diagnostics]`: `DetailedLogging` is `true` by default (verbose test trace); set it to `false` for normal play.
- Keys: none. The mod does not use Sprocket Keybinds.
- Shipped data: three part JSON files (`Sprocket_Data\StreamingAssets\Parts`), English localization XML files, muzzle atlases and `effect-manifest.json`, lamp icons and halo texture, in `BepInEx\plugins\SprocketVisualEffects\`.
- `sprocketvfx.bundle` (Unity AssetBundle with the volumetric shader and material) is not in this repository copy; it is part of the release and is installed next to the DLL.

## Status

- Muzzle overlay: last confirmed working in game at version 0.3.0; the smoke tuning of 0.3.1 and later is installed but was not reported back separately.
- Lamps: earlier lamp builds had placement, mirroring and paint problems in game. 0.4.5 (stock lamp shape, nothing in the part modified) is installed and not yet tested in game.
- The whole pack was just renamed and has not been re-tested in game since.

## Building

.NET project in this folder (`SprocketVisualEffects.csproj`, .NET SDK 8.0.425 via `global.json`); it builds against the game's BepInEx core and interop assemblies with `-p:GameDir=<path to Sprocket folder>`.
`Verify.ps1` builds and runs the offline checks, `Build-Package.ps1` stages the install layout. See [GUIDE.md](GUIDE.md) for lamp details and file layout and [ASSET-PROVENANCE.txt](ASSET-PROVENANCE.txt) for the assets.
