# Sprocket Emitter

A placeable, cosmetic emitter for the Sprocket designer. Wire it in the Telescopic Mast's I/O Flow and it throws spent casings (or projectile-shaped objects) out of the vehicle, for example when a hatch opens or a cannon fires.

Objects are visual only: no damage, no forces on the tank, no sound. The Emitter is inert without the Telescopic Mast.

- Version: 0.1.1
- Plugin GUID: `sprocket.emitter`
- Requires: no hard dependency, but it only works together with Sprocket Telescopic Mast (`sprocket.telescopicmast`, I/O API revision 1, mast 0.9.3 or later; 0.9.9 for per-part settings). The dependency is declared as soft, so the game still loads without the mast.
- Optional: Sprocket Telescopic Mast (see above)
- Game version tested: 0.2.56.2

## Features

- One part, "Emitter", in Crew / Stowage (part date 1930.01.01). It is shown as an arrow in the designer (scale 0.5 to 3) and hidden in Play. It cannot be mirrored; place independent copies.
- Appears as an "Eject / Stop" output in the Mast I/O Flow. A rising signal throws one batch; "Repeat while on" repeats at an interval until the signal falls.
- Per-part settings in the I/O Flow inspector: item (casing or projectile), caliber 5-155 mm, ejection speed 0.5-15 m/s, spread 0-30 deg, spin 0-720 deg/s, count 1-8, repeat, interval, lifetime and inherit vehicle velocity.
- Objects fly on a short raycast path, land on the tank or the ground, ride along with a moving collider, slide off slopes steeper than 35 degrees (configurable) and disappear after their lifetime.
- Since 0.1.1, casings and projectiles use the game's stock ammunition meshes, copied from an ammunition rack that is already loaded. If none is found, nothing is thrown and a warning is logged; an embedded fallback shape is available behind a config switch.
- Limits: at most 64 objects at once (configurable), a per-frame raycast budget, and a hard lifetime cap.
- No Harmony patches.

## Settings and keys

- Config file: `BepInEx\config\sprocket.emitter.cfg`, written on first start. Defaults: `Meshes.AllowEmbeddedFallback` false, `Limits.MaxObjects` 64, `Limits.MaxCastsPerFrame` 512, `Limits.MaxLifetime` 60 s, `Slope.MaxDegrees` 35, `Slope.Friction` 0.65, `Diagnostics.DetailedLogging` true (verbose log lines in this development build).
- The `Defaults` section (casing, 30 mm, 3 m/s, 8 deg, 180 deg/s, count 1, repeat off, 0.5 s, 10 s, inherit on) only applies with a Mast older than 0.9.9. With 0.9.9 or later, edit values per part in the Mast I/O Flow inspector.
- No keys of its own. Use the Mast's I/O Flow keys (default F8, see the Telescopic Mast README) to build the flow.
- Shipping data: part definition and English localization under `Sprocket_Data\StreamingAssets`, DLL and icon in `BepInEx\plugins\SprocketEmitter\`.

## Status

- 0.1.0 was tested in game (2026-11-13): the Mast coupling, per-part settings, throwing, landing, the first-frame baseline and cleanup worked. The casings used the embedded fallback shape, which was judged too ugly.
- 0.1.1 (stock casing meshes, fallback off) is installed but not yet tested in game. Saving, loading and copying a tank with an Emitter, performance, and the hidden arrow in the first Play frame are also untested.
- The part description still mentions fire and smoke, which are not built yet. The whole pack was renamed and has not been re-tested in game yet.

## Building

.NET project in this folder (`SprocketEmitter.csproj`, net6.0, warnings as errors). It builds against the game's interop assemblies: `dotnet build -c Release -p:GameDir=<path to Sprocket folder>`. `Verify.ps1` builds and runs the offline checks in `checks/`.

Docs: [GUIDE.md](GUIDE.md) (developer notes), [ASSET-PROVENANCE.md](ASSET-PROVENANCE.md). Licence: MIT (see the repository LICENSE). Author: Nero.
