# Public API contract — 0.1.8

Namespace SprocketKeybinds; stable plugin GUID sprocket.keybinds. Reference the shared DLL with Private=false. All registration, metadata and polling occur on Unity's main thread after initialization. Existing exported signatures and optional defaults are retained.

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

RegisterModMetadata(string ownerId, string author, string displayName) works before or after action registration. Resolution: explicit metadata; known HPS/Mast/Thermal defaults (Nero and short mod names); original ModName with Unknown author. No author inference from filenames/GUID prefixes, whitelist or pack dependency. Identical repeats are idempotent; conflicting calls for one owner throw. Labels are trimmed, 1–128 printable characters; control characters and angle-bracket markup are rejected. Panel text disables TMP rich text and ellipsizes long labels. Fallback ModName controls are rendered as spaces.

RegisterPackMembership(string ownerId, string packId, string packDisplayName) declares **one primary pack** for an owner. A pack integration can register existing owners without rebuilding consumer DLLs. The installer determines membership; the API renders it. Pack IDs follow owner-ID syntax. Repeated identical calls are idempotent; conflicting membership or inconsistent labels for the same pack ID throw. No automatic Cold War membership and no metadata descriptor file.

By author: author parent → owner/mod → binding rows. By pack: declared pack parent → author/mod → bindings; standalone owners fall back to authors. Parent keys use author: plus declared author, or pack: plus stable pack ID. Children use owner IDs, so identical names never merge editors/settings. Sorting is ordinal group name/key, author, mod, then owner ID. Late metadata retains child collapse state. Grouping modes retain their parent collapse sets; switching stops an active capture and lays out rows without committing/cancelling pending overrides. Metadata does not write the configuration.

Tooltips should direct players to Settings → keybinds → Mod keybinds and their action labels. Do not promise a fixed active key. Documentation can list factory defaults.

## Packaging facts

Separate shared module SprocketKeybinds.dll 0.1.8; existing dependency ranges retained, metadata callers require >=0.1.5. Actual config BepInEx/config/sprocket.keybinds.json, schema Version 1. Preserve it and legacy CFGs on upgrades. See GUIDE.md for supported runtime, recovery and dependent-mod uninstall effects. This change implements no installer, manifest schema, automatic updater or attribution authentication.

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
