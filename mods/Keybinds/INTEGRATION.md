# Public API contract â€” 0.1.8

Namespace SprocketKeybinds; stable plugin GUID sprocket.keybinds. Reference the shared DLL with Private=false. Keybind registration, metadata and polling occur on Unity's main thread after initialization. Existing exported signatures and optional defaults are retained.

## Minimal external example

```csharp
using SprocketKeybinds;
// Attribute on your BasePlugin class:
[BepInDependency(Keybinds.PluginGuid, ">=0.1.5 <0.2.0")]
// In Load, after the dependency is ready:
Keybinds.RegisterModMetadata("example.sight", "Example Author", "Sight");
var toggle = Keybinds.RegisterButton("example.sight", "Sight", "toggle",
    "Toggle sight", "<Keyboard>/n", "<Mouse>/middleButton");
// In existing Unity Update, using your own gameplay context:
if (hasValidSight && controlsThisVehicle && !paused && toggle.WasPressedThisFrame)
    ToggleSight();
```

The attribute and Load/Update statements belong in their respective class/method locations. Metadata is optional. New metadata methods require 0.1.5; old consumers can retain >=0.1.3 <0.2.0.

RegisterButton(string modGuid, string modName, string actionId, string label, string defaultPath, string? secondaryPath=null) returns ModKeybind. IDs use ASCII letters, digits, dot, underscore or hyphen. Duplicate owner/action registration throws. Keep IDs stable across releases. Deterministic input GUIDs and storage keys are independent of metadata. This hierarchy preserves consumer action labels.

ModKeybind exposes Id, ModName, Label, IsHeld, WasPressedThisFrame, WasReleasedThisFrame, GetBindingDisplayString(index=0) and Unbind(index=0). Slot 0 is primary, slot 1 optional secondary. Unbind applies an empty override with menu Apply/Cancel semantics; outside a menu edit session it saves immediately. Native Reset restores factory defaults. Reads are non-consuming and suppressed during actual settings/rebinding, focus loss and selected input fields. Preserve vehicle/part/mode/pause checks in consumers. Assets survive scene transitions. Unregister only on genuine owner removal, not transient UI destruction.

Keybinds.Ready, IsConfiguring, TryImportLegacyBinding(handle,path), GetConflicts() and Unregister(owner,action) remain available. Legacy import is primary-only, once and outside edit transactions; existing API choices, including unbound/reset, always win. GetConflicts reports equal normalized mod bindings: plain and combo bindings differ, while left/right modifier prefixes are equivalent. Contexts can make an exact conflict intentional. The free-key panel separately reads vanilla bindings.

## Optional metadata

RegisterModMetadata(string ownerId, string author, string displayName) works before or after action registration. Resolution: explicit metadata; known HPS/Mast/Thermal defaults (Nero and short mod names); original ModName with Unknown author. No author inference from filenames/GUID prefixes, whitelist or pack dependency. Identical repeats are idempotent; conflicting calls for one owner throw. Labels are trimmed, 1â€“128 printable characters; control characters and angle-bracket markup are rejected. Panel text disables TMP rich text and ellipsizes long labels. Fallback ModName controls are rendered as spaces.

RegisterPackMembership(string ownerId, string packId, string packDisplayName) declares **one primary pack** for an owner. A pack integration can register existing owners without rebuilding consumer DLLs. The installer determines membership; the API renders it. Pack IDs follow owner-ID syntax. Repeated identical calls are idempotent; conflicting membership or inconsistent labels for the same pack ID throw. No automatic Cold War membership and no metadata descriptor file.

By author: author parent â†’ owner/mod â†’ binding rows. By pack: declared pack parent â†’ author/mod â†’ bindings; standalone owners fall back to authors. Parent keys use author: plus declared author, or pack: plus stable pack ID. Children use owner IDs, so identical names never merge editors/settings. Sorting is ordinal group name/key, author, mod, then owner ID. Late metadata retains child collapse state. Grouping modes retain their parent collapse sets; switching stops an active capture and lays out rows without committing/cancelling pending overrides. Metadata does not write the configuration.

Tooltips should direct players to Settings â†’ keybinds â†’ Mod keybinds and their action labels. Do not promise a fixed active key. Documentation can list factory defaults.

## Packaging facts

Separate shared module SprocketKeybinds.dll 0.1.10; existing dependency ranges retained, metadata callers require >=0.1.5. Actual config BepInEx/config/sprocket.keybinds.json, schema Version 1. Preserve it and legacy CFGs on upgrades. See GUIDE.md for supported runtime, recovery and dependent-mod uninstall effects. This change implements no installer, manifest schema, automatic updater or attribution authentication.

## Migrating action identities (0.1.6)

Require `>=0.1.6 <0.2.0` for `TryMigrateBindingIdentity(oldOwner, oldAction, newOwner, newAction)`. Call on the main thread after API initialization, outside menu edits, before either action is registered. Register the new action's real factory defaults afterwards.

The helper copies both saved binding overrides, including paths, interactions and processors, and remaps action/binding IDs. Existing target records always win, even reset or unbound records. Absent source or existing target returns false. Old records remain available for rollback. Each call persists independently and atomically; malformed data or write failure throws without a partial target record. Do not catch errors and silently discard secondary bindings.

## Combo bindings (0.1.8)

All existing signatures and polling properties remain compatible. Existing consumers with `>=0.1.3 <0.2.0` or `>=0.1.5 <0.2.0` can use player-configured combos without recompiling. An integration that supplies a combo factory default must require `>=0.1.8 <0.2.0`.

The optional default string format is `Shift+<Keyboard>/x` or `Ctrl+Alt+<Keyboard>/f`. Use `+` between modifiers and finish with one ordinary concrete keyboard/mouse button path. Order and case of modifiers are ignored; Shift/Ctrl/Alt each accept either physical side. Prefix aliases LeftShift/RightShift, LeftCtrl/RightCtrl and LeftAlt/RightAlt normalize to the same modifier. The canonical order is Shift, Ctrl, Alt. A physical modifier alone, such as `<Keyboard>/leftShift`, remains a plain binding. Empty string means unbound. Modifier keys cannot be the main key of a combo.

```csharp
var action = Keybinds.RegisterButton("example.sight", "Sight", "adjust", "Adjust sight",
    "Ctrl+Alt+<Keyboard>/f");
// Existing polling code works unchanged; the displayed binding is Ctrl+Alt+F.
if (controlsThisVehicle && action.WasPressedThisFrame) AdjustSight();
```

At the main-key press, the matching registered binding with the most required modifiers wins for that main key. Equal-specificity bindings may both fire and are advisory conflicts when their normalized paths are equal. Thus Shift+X suppresses X; Ctrl+Shift+X suppresses both when present. Extra unrelated modifiers are allowed. A plain binding with no matching competing combo still works while modifiers are held. Press modifiers before the main key; adding modifiers to an already-held key never starts a combo. Releasing a required modifier releases the combo; a fresh main-key press is required to rearm it. Reads remain non-consuming. This priority applies among registered mod actions, not vanilla gameplay actions.

Capture observes native button events through a pass-through input action. Hold modifiers then press a main key; press and release a modifier alone to bind that physical modifier. Escape cancels. Keyboard and mouse buttons are supported. Apply advances the cancellation baseline; Cancel/abandon rolls back unapplied captures. Reset restores each slot's registered factory default, including combo defaults.

Storage keeps the Version 1 outer document and the original opaque per-action override JSON. An edited combo slot stores its ordinary main-key override in `bindings`, plus an additive `sprocketCombos` object mapping that binding's deterministic GUID to its combo string. The extension is removed before calling Unity's override loader. Combo factory defaults need no extension until overridden. Plain, unbound, reset and absent-owner records retain their original strings unless changed. Identity migration remaps combo GUIDs along with native slots. Downgrading combo configuration to older API versions is unsupported.

The panel shows examples of free single keys in letters/digits/function/other groups, using vanilla target assets (including disabled maps) with their `SaveID` overrides from the menu's `RebindMap`, live effective vanilla working UI paths, enabled native actions and registered mod bindings. Working UI paths take precedence over saved overrides, including pending Clear/Reset/rebind changes. Modifier-composite parts and mod combo-only main keys do not reserve a single key. It updates after edits and checks vanilla changes every 0.5 seconds. Missing assets, unresolved keyboard paths, ambiguous working copies or read errors show `vanilla onbekend` and suppress free-key claims. Supplemental enabled assets are scanned conservatively across their maps. Bindings created only by another mod's direct keyboard polling cannot be discovered. Native coverage and layout still require the live checklist in `NOTES.md`.

## Mod settings API

Available in 0.1.10; settings consumers require `>=0.1.10 <0.2.0` and reference the shared DLL with `Private=false`. Call from the mod's Load method. Registration is also available before Keybinds.Load; it does not depend on `Keybinds.Ready`.

```csharp
const string owner = "example.sight";
var enabled = Config.Bind("General", "Enabled", true, "Enable sight.");
var limit = Config.Bind("General", "Limit", 5,
    new ConfigDescription("Maximum level.",
        new AcceptableValueRange<int>(1, 10)));
ModSettings.Register(owner, enabled, new SettingOptions { Label = "Sight enabled" });
ModSettings.Register(owner, limit);
Config.Bind("Diagnostics", "DetailedLogging", false,
    "Verbose trace lines for testing. Warnings and errors are always logged.");
ModSettings.RegisterSection(owner, Config, "Diagnostics");
```

Use `BepInEx.Configuration` and `SprocketKeybinds`. A bool becomes Toggle. An int, long, float or double with `AcceptableValueRange<T>` becomes Number; integer steps default to 1, floating steps to null, and `SettingOptions.Step` overrides either. Numbers without a range are rejected unless they have an acceptable list. Enums become Choice using their names, restricted to any `AcceptableValueList<TEnum>`; other acceptable lists also become Choice. Strings become Text. Other types, including KeyboardShortcut, are rejected with one warning per cause.

`Value` reads/writes the original entry's `BoxedValue`: BepInEx clamps, raises `SettingChanged` and, when `SaveOnConfigSet` is enabled, saves immediately. The value remains in the mod's own cfg. The registry has no file or Apply/Cancel transaction. Numeric inputs convert to the entry type (integer midpoints round away from zero); choices accept names/value strings or their original type. Invalid inputs are ignored with one warning. `Reset()` uses the bound default; `ResetOwner(owner)` resets that owner's registered entries.

The reference BepInEx build constrains `AcceptableValueList<T>` to `IEquatable<T>`, which C# enum types do not implement. Ordinary enum choices work; an actual enum-list subset cannot be instantiated with these references. The tests explicitly record that limitation as SKIP, while covering string and numeric lists.

`SettingOptions` supplies Label, Description, Group, RequiresRestart, Advanced, Step and VisibleWhen. Defaults use the cfg key, description and section. A consumer that caches its cfg value should set RequiresRestart. Only `ModSetting.IsVisible` evaluates VisibleWhen; exceptions mean visible and warn once. `RegisterSection` captures supported entries already present in that section; entries added later require another registration. Registering the same owner/ConfigDefinition returns the existing setting. `Describe(entry, options)` applies the same type rules without registering. Invalid registrations do not throw.

`All` and `Snapshot(byPack: false)` are cached read-only collections. Snapshot groups author (or explicit pack), mod, cfg section, setting, using the keybind metadata and ordinal ordering. Sections/settings preserve registration order. Register/unregister or new metadata replaces both snapshots and All before `Changed`; value changes use the entry's `SettingChanged` instead. Changed handlers are isolated from each other. `Unregister(owner, definition)` removes an entry without touching its cfg.


FIX1 contract: `ModSetting` presentation, identity and kind/bounds members are getter-only properties; callers change only `Value` or call `Reset`. Author and ModName still follow registered metadata. Every `SettingOptions` member is a read/write property, preserving object initializers. These properties replace fields only in the unreleased 0.1.10 API; the 44 public ABI entries from 0.1.9 remain unchanged.

With a presenter, clicking **Mod settings / Open** requests opening after the player leaves Settings and changes the row to **Opens after Settings**. The next Attach/Refresh restores **Open**. Without a presenter it stays **Needs UI Kit** and disabled. Keybinds does not close Settings or suppress Cancel/Esc; the UI Kit defers display until Settings has closed. Optional logging initialization is guarded first; metadata, shortcut and settings registration share one separate try/catch, so their failure leaves the core keybind API available.

The UI Kit supplies the presenter through `SetPresenter(open, isOpen)` using managed delegates. `HasPresenter`, `IsOpen` and `Open()` expose it safely; `SetPresenter(null, null)` clears it, and replacing it warns once. Without UI Kit the register still works, the native row reads **Needs UI Kit** and cannot be clicked, and Open logs one informational line. Keybinds itself provides the unbound **Open mod settings** action. No UI Kit reference or reflection is required.

Without Keybinds, an optional integration must guard a separate API helper with the loader's plugin-presence check and catch integration errors; its cfg continues working without a menu. A hard dependency prevents a plugin from loading when Keybinds is absent. Do not call missing API types from an unguarded Load method.
