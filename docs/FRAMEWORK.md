# Sprocket Mod Framework

Two shared plugins for Sprocket mods: **Sprocket Keybinds 0.1.10** is the core,
providing configurable keys, Shift/Ctrl/Alt combinations and the **Mod settings**
registry. **Sprocket UI Kit 0.1.13** is the UI layer: consistent panels and the
shared Mod settings panel. License: MIT. Author: Nero.

## For players

1. Install the **Sprocket Mod Loader** as described in the [pack installation guide](../README.md).
   It supplies official BepInEx 6 IL2CPP build `6.0.0-be.788` with a compatibility
   patch for the black-screen issue in current Sprocket builds. Use its Releases
   page and Easy install through the Mod Manager, or its documented manual route.
   Only the BepInEx part is needed, not MLLoader. Start the game once to generate
   the BepInEx folders; the first start takes a while.
2. With the game closed, place `SprocketKeybinds/SprocketKeybinds.dll` and
   `SprocketUiKit/SprocketUiKit.dll` (plus `SprocketUiKit.xml`) under `BepInEx\plugins`.
3. Open Settings > Controls > **Mod keybinds** to rebind registered actions.
   For global options, choose Settings > Controls > **Mod settings** > **Open**, then leave
   Settings: the panel appears after Settings closes. **Open mod settings** is
   unbound by default; bind it for direct access. Preserve configs when updating.

## For modders

Use the complete [Framework Example](examples/FrameworkExample/) as a starting point.
The C# blocks below are literal excerpts from its compiled source.

### 1. Reference and declare dependencies

Target `net6.0` with BepInEx 6 IL2CPP. Reference the two shared DLLs, BepInEx core
and generated interop assemblies with `Private=false`; never bundle these dependencies.
The example project exposes `GameDir`, `KeybindsDll` and `UiKitDll` properties.

```csharp
[BepInPlugin(GUID, "Framework Example", "1.0.0")]
[BepInDependency(Keybinds.PluginGuid, ">=0.1.10 <0.2.0")]
[BepInDependency(UiKit.PluginGuid, ">=0.1.13 <0.2.0")]
```

### 2. Identify the owner and register a key

Use your plugin GUID as the owner ID for metadata, keys, settings and panels.
Register on Unity's main thread in `Load`, after dependencies load. Metadata
groups the mod under its author; keep owner/action IDs stable across releases.

```csharp
            Keybinds.RegisterModMetadata(GUID, "Nero", "Framework Example");
```

```csharp
            var toggle = Keybinds.RegisterButton(GUID, "Framework Example", "toggle-panel",
                "Toggle example panel", "");
```

An empty default leaves the action unbound. Players choose their own key or combo.

### 3. Bind configuration, then opt in to the registry

```csharp
            var showGreeting = Config.Bind("Example", "Show greeting", true,
                "Show the greeting in the example panel.");
            var panelOpacity = Config.Bind("Example", "Panel opacity", 0.9f,
                new ConfigDescription("Opacity of the example panel content.",
                    new AcceptableValueRange<float>(0.2f, 1f)));
            var detailedLogging = Config.Bind("Diagnostics", "DetailedLogging", false,
                "Verbose trace lines. Warnings and errors are always logged.");
```

```csharp
            var greetingSetting = ModSettings.Register(GUID, showGreeting,
                new SettingOptions { Label = "Show greeting", Group = "Example" });
            var opacitySetting = ModSettings.Register(GUID, panelOpacity,
                new SettingOptions { Label = "Panel opacity", Step = 0.05f });
            ModSettings.RegisterSection(GUID, Config, "Diagnostics");
```

`RegisterSection` includes supported entries present at call time. `Register`
returns null when refused; duplicate owner/definition registration returns the existing setting.

| Config type/constraint | UI kind |
|---|---|
| `bool` | Toggle |
| `int`, `long`, `float`, `double` with `AcceptableValueRange<T>` | Number |
| `enum` | Choice using all enum names |
| Other `AcceptableValueList<T>` | Choice (including numeric lists) |
| `string` without a list | Text |
| Unranged numbers without a list, `KeyboardShortcut`, other types | Refused |

Enum subset lists cannot be constructed with this BepInEx's `IEquatable<T>` constraint.
`SettingOptions` provides `Label`, `Description`, `Group`, `RequiresRestart`,
`Advanced`, `Step` and `VisibleWhen`. Defaults use the cfg key, description and
section; integer steps default to 1, float/double steps to null. Mark cached
values `RequiresRestart`; `Advanced` hides them until Show advanced is selected.
`VisibleWhen` controls visibility (a throwing predicate defaults to visible);
the shared panel evaluates visibility on view refreshes, not every frame.

Follow value changes through `ConfigFile.SettingChanged`, filtering by entry:

```csharp
            Config.SettingChanged += (_, e) =>
            {
                try
                {
                    if (e.ChangedSetting == showGreeting || e.ChangedSetting == panelOpacity)
                    {
                        if (detailedLogging.Value) Log.LogInfo("Example setting changed.");
                    }
                }
                catch (Exception ex) { Log.LogWarning("Setting callback: " + ex.Message); }
            };
```

`ModSettings.Changed` signals registry/metadata changes, not value edits.

### 4. Present the same entries in your own panel

Inside guarded setup, use the public `Container.Setting(ModSetting)` API:

```csharp
                var panel = UiKit.CreatePanel(GUID, "main", "Framework Example", null, 600, 340);
                UiKit.BindToggle(panel, toggle);
                var content = panel.Body;
                content.Alpha(() => panelOpacity.Value);
                content.Label("Hello from the Sprocket Mod Framework!")
                    .VisibleWhen(() => showGreeting.Value);
                if (greetingSetting != null) content.Setting(greetingSetting);
                if (opacitySetting != null) content.Setting(opacitySetting);
```

## Rules and compatibility

Register **global settings only**. Registration is opt-in. Values stay in each
mod's own `.cfg`; nothing is added to saves or vehicle blueprints. The registry
does not store values or provide Apply/Cancel. UI edits update the original entries.
The Mod settings API never throws to callers: unsupported entries are refused and
invalid writes ignored with a warning. Keybind registration and Kit panel creation
can throw; guard setup as in the example so `Load` returns safely.
Without UI Kit, registration still works and the native row reads **Needs UI Kit**.
Without Keybinds, a hard-dependent mod cannot load. For optional integration use a
soft dependency, check loader plugin presence, and call a separate guarded helper
containing all API types (keep them out of unguarded `Load`); cfg use still works.
Within **0.1.x**, the API only grows. Breaking changes require **0.2.0**;
consumers' `<0.2.0` dependency limit protects them from incompatible upgrades.

## Further reading

[Keybinds README](../mods/Keybinds/README.md) · [Keybinds integration](../mods/Keybinds/INTEGRATION.md) · [Keybinds API](../mods/Keybinds/API.md)

[UI Kit README](../mods/UiKit/README.md) · [UI Kit guide](../mods/UiKit/GUIDE.md) · [Example source](examples/FrameworkExample/)
