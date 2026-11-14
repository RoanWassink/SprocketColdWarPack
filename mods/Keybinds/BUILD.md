# Building the source

Nothing is installed or published by the build. Developers need .NET SDK 8 and a Sprocket 0.2.56.2 Experimental loader installation (BepInEx 6 IL2CPP) with generated interop. The plugin targets net6.0. All game/loader references use Private=false. No game binaries or generated caches are distributed.

From this directory:

```powershell
dotnet build -c Release
dotnet run --project tests/Keybinds.Tests.csproj -c Release
dotnet build examples/KeybindDemo.csproj -c Release
```

The DLL is `bin/Release/net6.0/SprocketKeybinds.dll`. The build is deterministic and writes no pdb.

`GameDir` defaults to the standard Steam install. Point it at another folder with the same `BepInEx\core` and `BepInEx\interop` layout with `-p:GameDir=YOUR_GAME_DIRECTORY`; the build and the tests then never touch a real game folder. The example references the newly built project DLL.

The pure tests accept optional arguments `BASELINE_DLL NEW_DLL` to audit public member signatures and parameter defaults using metadata, without loading native wrappers. Pass an earlier build of this DLL as the baseline and the new build last:

```powershell
dotnet run --project tests/Keybinds.Tests.csproj -c Release -- PREVIOUS\SprocketKeybinds.dll bin\Release\net6.0\SprocketKeybinds.dll
```

The audit normalizes only the values of `Plugin.PluginVersion` and `Keybinds.PluginGuid` (their field signatures and flags are still compared); the new DLL is separately required to contain version `0.1.9` and GUID `sprocket.keybinds`. Every other public constant, member signature and optional default is compared exactly.

The environment variable `KEYBINDS_CONFIG` may point at a `sprocket.keybinds.json` file; the tests then copy it and check that every record is readable. The original is never written.

The style tests parse `tests/STYLE.md`, a snapshot of the UI Kit style guide (0.1.0), and compare the panel's colour tokens against it exactly.

Pure tests and interop compilation do not prove native gameplay; follow `NOTES.md` before installing or releasing.
