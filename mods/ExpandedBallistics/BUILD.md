# Building the source

Users do not need to compile; the release DLL is the pinned, tested build.

## Plugin

Needs the .NET SDK 8 (the plugin targets net6.0) and a Sprocket 0.2.56.x installation that has run BepInEx 6 IL2CPP once, so that the generated interop exists. The plugin also references the built DLLs of three other mods (Sprocket JSON Editor, Sprocket Keybinds and Sprocket UI Kit); they are read from the game folder:

```
BepInEx/plugins/SprocketJsonEditor/SprocketJsonEditor.dll
BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll
BepInEx/plugins/SprocketUiKit/SprocketUiKit.dll
```

Build against a game folder or a reference copy of it (BepInEx `core`, `interop`, `unity-libs` and the three plugin folders above):

```
dotnet build SprocketExpandedBallistics.csproj -c Release -p:GameDir=PATH_TO_GAME_OR_REFERENCE_COPY
```

The default `GameDir` in `SprocketMod.props` is the usual Steam folder. All game and loader references are `Private=false`; no game binaries or generated caches are distributed. The build is deterministic and writes no pdb.

Copy `SprocketExpandedBallistics.dll` into `BepInEx/plugins/SprocketExpandedBallistics/`, next to `era-assets/`, `material-looks/` and `assets/` (the plugin finds these folders relative to its own DLL).

## Tests

```
dotnet build tests/ExpandedBallistics.Tests.csproj -c Release
dotnet tests/bin/Release/net8.0/ExpandedBallistics.Tests.dll
```

The pure tests compile the model sources directly and do not need the game. They look for the project folder (`SprocketExpandedBallistics.csproj`) above the test output, read the recorded sessions and blueprints in `tests/fixtures`, and write their evidence tables to `bin/test-output` (or to the folder in `EB_TEST_OUT`). Two suites reach outside this folder: the UI Kit chart test reads the sibling `ui-kit` source folder, and the plugin-wiring test reads the built plugin DLL from `bin/Release/net6.0`, so build the plugin once first.

The ICF audit of the Harmony targets needs two dumps of the game's native methods (they come from the game's own metadata and are not distributed). Set `EB_NATIVE_METHODS` and `EB_NATIVE_METHODS_ALL` to the two JSON files to run it; without them that single check is skipped. Individual suites run with flags such as `--fix02251-only` or `--feat02272-only` (see `tests/Program.cs`).

Pure tests do not prove native gameplay; in-game checks are listed in `docs/ARMOUR-SANDBOX.md` and `RELEASE-NOTES.md`.
