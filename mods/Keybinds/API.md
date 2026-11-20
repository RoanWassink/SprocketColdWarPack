# Sprocket Keybinds 0.1.10: Mod settings API

Documentation correction 2026-11-15, DLL unchanged: the event note on `ModSetting.Entry` now points to `ConfigFile.SettingChanged`.

**DLL:** `SprocketKeybinds.dll`, SHA256 `F817B6A0C399E1F69643BFE99B31279EAA6C23BB4294B21BAD7291365EA47CBD`.
- Plugin GUID: `sprocket.keybinds`.
- Namespace: `SprocketKeybinds`.
- The source of truth is each mod's own BepInEx `ConfigEntry`. The registry stores nothing.
- No member throws to the caller.

```csharp
namespace SprocketKeybinds;

public enum SettingKind { Toggle, Number, Choice, Text }

public sealed class SettingOptions
{
    public string? Label { get; set; }          // default: cfg key
    public string? Description { get; set; }    // default: cfg description
    public string? Group { get; set; }          // default: cfg section
    public bool RequiresRestart { get; set; }
    public bool Advanced { get; set; }
    public float? Step { get; set; }            // Number only; default 1 for integral types, null for float/double
    public Func<bool>? VisibleWhen { get; set; }
}

public sealed class ModSetting
{
    public string OwnerId { get; }  public string Author { get; }  public string ModName { get; }
    public string Group { get; }    public string Key { get; }     public string Label { get; }
    public string Description { get; }
    public SettingKind Kind { get; }
    public double Min { get; }      public double Max { get; }     public float? Step { get; }   // Number
    public IReadOnlyList<string> Choices { get; }                                             // Choice
    public bool RequiresRestart { get; }  public bool Advanced { get; }
    public ConfigEntryBase Entry { get; }       // value changes: ConfigFile.SettingChanged, filtered on e.ChangedSetting == Entry
                                                // (ConfigEntryBase has no event of its own; typed ConfigEntry<T>.SettingChanged also works)
    public bool IsVisible { get; }              // VisibleWhen in try/catch; a throw = visible + one warning
    public object Value { get; set; }           // writes Entry.BoxedValue; Number converts any number (integral rounds),
                                                // Choice accepts the name/value string or the typed value; invalid = ignored + one warning
    public object DefaultValue { get; }
    public void Reset();
}

public sealed class ModSettingGroup   { public string Key { get; } public string Name { get; } public bool IsPack { get; } public IReadOnlyList<ModSettingMod> Mods { get; } }
public sealed class ModSettingMod     { public string OwnerId { get; } public string Name { get; } public string Author { get; } public IReadOnlyList<ModSettingSection> Sections { get; } }
public sealed class ModSettingSection { public string Name { get; } public IReadOnlyList<ModSetting> Settings { get; } }

public static class ModSettings
{
    public static ModSetting? Register(string ownerId, ConfigEntryBase entry, SettingOptions? options = null); // null = refused (one warning); duplicate = existing
    public static int RegisterSection(string ownerId, ConfigFile file, string section);                         // entries existing at call time; returns count
    public static bool Unregister(string ownerId, ConfigDefinition definition);
    public static ModSetting? Describe(ConfigEntryBase entry, SettingOptions? options = null);                  // pure; same type rules; not registered
    public static IReadOnlyList<ModSetting> All { get; }                       // cached, immutable; replaced only before Changed
    public static IReadOnlyList<ModSettingGroup> Snapshot(bool byPack = false); // cached per mode; same order as the keybind panel (ModMetadata.Order)
    public static event Action? Changed;                                       // registry changed (register/unregister/metadata); handlers in try/catch
    public static void SetPresenter(Action? open, Func<bool>? isOpen);         // (null, null) clears; a second presenter replaces with one warning
    public static bool HasPresenter { get; }
    public static bool IsOpen { get; }                                         // isOpen() in try/catch; false without presenter
    public static void Open();                                                 // no presenter: one info line; open() in try/catch
    public static void ResetOwner(string ownerId);
}
```

## Type rules (`Describe`)
- `bool` → Toggle.
- `int`, `long`, `float` or `double` with `AcceptableValueRange<T>` → Number. A number without a range is refused.
- An `enum` → Choice, with all its names. A subset list for an enum cannot exist in this BepInEx, because `AcceptableValueList<T>` needs `IEquatable<T>`.
- Any other `AcceptableValueList<T>` → Choice.
- `string` → Text.
- Everything else (including `KeyboardShortcut`) is refused.

## Settings row (0.1.10)
- In vanilla Settings, a native row "Mod settings" sits next to "Mod keybinds".
  - Without a presenter it shows "Needs UI Kit" and cannot be clicked.
  - With a presenter it shows "Open". A click calls `ModSettings.Open()`, and the value then reads "Opens after Settings".
- The panel never opens on top of Settings; the presenter shows it once `Keybinds.IsConfiguring` is false. There are no new hooks.
- Keybinds registers its own action `sprocket.keybinds/open-mod-settings` ("Open mod settings"), unbound by default. It also registers its own setting `Diagnostics › DetailedLogging` (label "Detailed logging", in `sprocket.keybinds.cfg`).

## Consumer dependency
`[BepInDependency(Keybinds.PluginGuid, ">=0.1.10 <0.2.0")]`, or soft with a guarded helper. See `INTEGRATION.md` next to the Keybinds source (`mods/Keybinds/INTEGRATION.md` in the pack repository).
