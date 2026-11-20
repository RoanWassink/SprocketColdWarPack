# Building 0.1.10 (FIX1)

Nothing is installed or published. Use .NET SDK 8 and the reference-only BepInEx 6 IL2CPP layout for Sprocket 0.2.56.2 Experimental. Plugin and consumer target net6.0; all game references are Private=false. Build only against the copied gameref, never the real game installation.

From this directory:

```powershell
$refs = Join-Path $env:USERPROFILE 'Documents\Codex\2026-11-13\herpak\gameref'
$baseline = Join-Path $env:USERPROFILE 'Documents\Codex\2026-11-13\herpak\dist\keybinds\BepInEx\plugins\SprocketKeybinds\SprocketKeybinds.dll'
dotnet build -c Release -p:GameDir=$refs
dotnet run --project tests/Keybinds.Tests.csproj -c Release -p:GameDir=$refs -- $baseline bin/Release/net6.0/SprocketKeybinds.dll
dotnet build examples/KeybindDemo.csproj -c Release -p:GameDir=$refs
```

DLL: `bin/Release/net6.0/SprocketKeybinds.dll`. The deterministic build emits no pdb. Verify the new DLL's version **0.1.10** and GUID **sprocket.keybinds** with the metadata-only tests above; they do not load Unity wrappers from the plugin. The baseline must be 0.1.9, GUID sprocket.keybinds, SHA256 prefix `1EC9E2D611D0`, with exactly 44 exported ABI entries. Only the release constant value changes; all other old signatures, constants, flags and optional defaults remain exact. The legacy GUID normalization in ApiSurface is constrained by separately asserting both baseline and current GUID. Additions are listed in `logs/public-api-added.txt`.

The reference BepInEx AcceptableValueList<T> requires IEquatable<T>, so an enum-list fixture cannot be instantiated; that subset test is explicitly SKIP. Normal enum and list tests still execute.

Tests link the managed registry to real BepInEx config types. They use temporary cfg files, verify disk reload, all kind rules, conversions, failure isolation, duplicate registration, reset, cache identity, author/pack grouping and registration order. Additional checks enforce protected-source hashes, all 10 Harmony targets, safe parameterless IL2CPP delegates (including ModSettings.cs) and unchanged Cancel behavior. The metadata bridge in the pure harness substitutes only Keybinds.Metadata, without instantiating the Unity plugin.

`SPROCKET_NATIVE_METHODS` overrides the native method dump; default is the workspace's `2026-10-04/dez/work/ammo-switch-research/all-native-methods.json`. Missing dump produces an explicit SKIP. A present dump must contain each target exactly once with an unshared, nonzero RVA. The target table is written to `logs/native-targets.md`.

Optional `KEYBINDS_CONFIG` copies and checks an existing bindings JSON without writing the original. Style tests compare PanelStyle tokens to `tests/STYLE.md`. Game checks remain in NOTES.md.

Validation logs: `logs/build.log`, `logs/tests.log`, `logs/consumer-build.log`, and the brief's required name-scan log in logs/. The release-name checker scans the workspace (excluding bin, obj, .git and logs) plus the new DLL explicitly. Logs are excluded because their mandated filename and tool header contain the prohibited personal token; no source/DLL exceptions are added. Generated build caches stay excluded as the supplied checker defaults specify. Long build paths in logs are replaced with location labels.

FIX1 validation checks that every Load call to RegisterButton, RegisterModMetadata, ModSettings.Register and Config.Bind is inside a lexical try block. The scanner ignores comments/strings, handles nested blocks and tests rejection in catch/finally or after an earlier try. It also checks all ModSetting presentation members have no setters and all SettingOptions members have public get/set accessors. The consumer demonstrates both an options initializer and reading a ModSetting property.

The existing unbound default is an empty string: ButtonBinding.Parse returns an empty path with no modifiers, ValidatePath skips control lookup for it, and ModKeybind serializes that empty path in its Button action bindings and enables the asset. Source/pure checks establish this; no native action is instantiated by the managed harness.

The current diffstat and patch are relative to the previous 0.1.10 delivery, captured under obj/fix1-baseline before edits. Tests still compare the DLL's ABI to the specified 0.1.9 baseline, and logs/public-api-added.txt is regenerated for the property API.
