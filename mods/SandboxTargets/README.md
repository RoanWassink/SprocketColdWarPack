# Sprocket Sandbox Targets

Adds extra target tanks to the stock flat-map sandbox (`Sandbox (Low performance)`): by default copies of your own tank at 150, 250, 350 and 500 m, and you can choose a different blueprint per target. The stock copy stays in place.

- Version: 0.3.2
- Requires: BepInEx 6 IL2CPP (build 6.0.0-be.788); no other Sprocket mod
- Optional: none
- Plugin GUID: `sprocket.sandboxtargets`
- Game version tested: 0.2.56.2

## Features

- Four extra targets by default: 150 m left, 250 m right, 350 m left and 500 m right of your start position (lateral offsets -15, +15, -30 and +30 m), hull fronts turned towards you.
- The targets are spawned by the game's own flat-map spawn event, so the game builds, registers, activates and resets them. The mod gives no drive, aim or fire orders.
- Each target can use another saved blueprint (`"Default/PZ4"` for faction/name, or just `"PZ4"` to search the active faction). A missing blueprint gives a warning and a copy of your own tank.
- Per target: `distance` (m between tank centres), `lateral` offset, `facing` (`"towards"`, `"side"` or degrees clockwise).
- Up to 64 targets; the config is re-read at the start of every sandbox or Play session.
- Invalid JSON or values give a warning and the defaults; the config file itself is never overwritten.
- Three Harmony patches on parameterless native methods: a postfix on `StageVehicleSpawnEvent.GetSpawnInstances` and `StageReset`, and a prefix on `MissionScenarioGameState.Exit`.
- Safety gate: the mod only runs on the exact `GameAssembly.dll` build it was checked against (SHA-256 `3E580828...C17B`, game 0.2.56.2). On another build it disables itself and logs a warning.

## Settings and keys

- Config: `BepInEx\config\sprocket.sandboxtargets.json` (JSON, not a `.cfg`). The mod creates it with the defaults on first start and never overwrites an existing file; an example is in `config\sprocket.sandboxtargets.example.json`.
- Fields: `enabled` (true), `targets` (list), and per target `distance` (0 < d <= 10000), `lateral` (negative = left; `abs(lateral)` must be smaller than `distance`), `blueprint` (null = copy of your tank) and `facing` (`"towards"`).
- Blueprint files are looked up in `Documents\My Games\Sprocket\Factions\<faction>\Blueprints\Vehicles\<name>.blueprint`.
- There is no `.cfg` entry and no `DetailedLogging` switch; log lines use the prefix `[SandboxTargets]`, and are currently in Dutch.
- Keys: none.
- Shipped files: only `BepInEx\plugins\SprocketSandboxTargets\SprocketSandboxTargets.dll`; no part or localization files.

## Status

- 0.3.2 is installed and loads cleanly. It has not yet been tested in game: the flat-map sandbox has not been started since the install.
- Still to check: that custom-blueprint targets are different tanks, that hit effects and decals work on all targets, and that targets are removed after a reset or when leaving the map.
- The whole pack was just renamed and has not been re-tested in game since.

## Building

.NET project in `src\` (`SprocketSandboxTargets.csproj`, net6.0); it builds against the game's BepInEx core and interop assemblies. The property is `GameRoot`, not `GameDir`: `.\Build-SandboxTargets.ps1 -GameRoot <path to Sprocket folder>` or `dotnet build src\SprocketSandboxTargets.csproj -c Release -p:GameRoot=<path to Sprocket folder>`.
`tests\Check-SandboxTargets.ps1` and `tests\Check-Interop.ps1` are the offline checks. Dutch documentation: [README-NL.md](README-NL.md) (usage, config details, in-game test list); hook research: [SANDBOX-MAP.md](SANDBOX-MAP.md).
