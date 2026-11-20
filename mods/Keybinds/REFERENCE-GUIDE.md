# Sprocket Keybinds 0.1.10

Shared configurable button bindings for Sprocket addons inside the vanilla Settings keybind menu. Open **Mod keybinds / Open**.

The panel has two collapsible levels: **author → mod → actions**. Existing HPS, Mast and Thermal appear beneath **Nero** without rebuilding those addons. Switch **By author / By pack** for explicitly declared pack membership. Standalone mods do not automatically belong to any pack. Other mods fall back to their author, or **Unknown** if none is declared. Scroll with the mouse wheel. Duplicate display names retain separate owners, with an owner-ID detail when needed. Long labels use ellipsis.

**Clear** removes one binding slot; the reset icon restores its registered factory default. **Apply** saves and establishes a new baseline. **Accept** saves and closes. **Cancel** restores the last applied settings. Panel Back, collapse and grouping switches do not save or cancel edits. Primary and optional secondary slots are independent.

## Compatibility and installation

Built against Sprocket 0.2.56.2 Experimental / Unity 6000.3.21f1, BepInEx 6 IL2CPP with local generated interop, .NET 6; SDK 8 builds the source. Other game/loader versions need verification. Use one shared BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll; consumers reference it with Private=false and must not ship private runtime copies.

Existing compiled consumers using >=0.1.3 <0.2.0 remain compatible. New optional metadata callers require **>=0.1.5 <0.2.0**. Existing exported signatures and input identities are retained. The API is still 0.x, not a stable 1.0 promise. A bare dependency string such as 0.1.3 means an exact version in this loader.

Close the game before replacing the API DLL and back up the DLL and configuration. Preserve BepInEx/config/sprocket.keybinds.json on updates. Uninstalling the API prevents hard-dependent addons from loading. The runtime does not download updates or install addons.

## Storage and recovery

The JSON stores overrides under stable owner/action identities, including explicitly unbound choices. Atomic writes retain the previous file as .bak. Settings for absent mods remain available when they return. Display author/mod/pack changes do not rename storage keys. Metadata is supplied at runtime by mods or a pack integration; no metadata file is introduced.

Legacy CFG import is optional and happens once, only when no API record exists. Register factory defaults before importing: Reset must restore the factory key, not an old personal key. Import cannot run during an edit transaction. Invalid/future-schema JSON fails initialization without overwriting the file. Close the game, preserve a copy, inspect the error and restore a known-good compatible backup; do not discard all owners' settings to repair one binding.

See [API integration](INTEGRATION.md). Concrete keyboard and mouse **buttons** are supported; Shift/Ctrl/Alt button combinations are supported; axes and arbitrary composites are outside this version. Conflict reports cover exact overlaps between mod actions, not all vanilla bindings. Polling is non-consuming; gameplay contexts remain the consumer's responsibility. Attribution and pack membership are declarations, not authenticated authorship.

Release and consumer builds, managed regression/settings tests, ABI and native RVA checks are recorded in logs/. Existing public signatures and installed consumer references are checked separately. Native hierarchy layout, scrolling, Clear/Reset, switching during edits, Apply/Cancel, restart, map transitions and press/hold gameplay still require live confirmation for this build.

## Mod settings API

0.1.10 adds `ModSettings`, `ModSetting`, `SettingOptions`, `SettingKind` and the immutable `ModSettingGroup`, `ModSettingMod`, `ModSettingSection` snapshot types. Settings users require `>=0.1.10 <0.2.0`; existing keybind consumers keep their ranges. See the ten-line example and full rules in [INTEGRATION.md](INTEGRATION.md#mod-settings-api).

Register individual ConfigEntryBase objects or the currently bound entries in one cfg section. Owner ID is the plugin GUID; repeated owner/ConfigDefinition registration returns the original setting. Describe is available without registration. Bool maps to Toggle; ranged int/long/float/double to Number; enums and acceptable lists to Choice; string to Text. Unsupported types and unranged numbers are rejected. Values, defaults, clamping, persistence and SettingChanged belong to the mod's own cfg, with no registry storage. Numeric lists are Choice. Invalid values never throw to callers.

All and Snapshot(byPack) are cached read-only lists replaced on registry or metadata change, followed by Changed. Grouping and ordering use the same metadata as keys; sections/settings retain registration order. Options override presentation, flag Advanced/RequiresRestart and provide a safe visibility predicate, evaluated only by IsVisible. Reset and ResetOwner restore bound defaults.

The UI Kit registers managed presenter callbacks with SetPresenter. HasPresenter/IsOpen/Open report or invoke it safely. Without it, Settings shows an inactive Mod settings / Needs UI Kit row. The Open mod settings keybind is unbound by default. Without Keybinds an optional guarded integration keeps its cfg but has no menu; hard dependencies cannot load. Keybinds itself registers Diagnostics.DetailedLogging (default false), read live for scene traces; registrations, warnings and errors always log. Capture/Reset failures return control to the native button. No new Harmony hooks or changes to Cancel/Esc handling are included.

FIX1 contract: `ModSetting` presentation, identity and kind/bounds members are getter-only properties; callers change only `Value` or call `Reset`. Author and ModName still follow registered metadata. Every `SettingOptions` member is a read/write property, preserving object initializers. These properties replace fields only in the unreleased 0.1.10 API; the 44 public ABI entries from 0.1.9 remain unchanged.

With a presenter, clicking **Mod settings / Open** requests opening after the player leaves Settings and changes the row to **Opens after Settings**. The next Attach/Refresh restores **Open**. Without a presenter it stays **Needs UI Kit** and disabled. Keybinds does not close Settings or suppress Cancel/Esc; the UI Kit defers display until Settings has closed. Optional logging initialization is guarded first; metadata, shortcut and settings registration share one separate try/catch, so their failure leaves the core keybind API available.
