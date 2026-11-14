# Sprocket Keybinds

A shared keybind menu for Sprocket mods, grouped by author or declared mod pack.

**v0.1.9 — development beta.** Configure mod controls inside the vanilla Settings menu, including modifier combinations. Expand author/mod groups, switch between author and pack grouping, and use Clear, Reset, Apply, Accept and Cancel. A compact information panel shows examples of free single keys. Personalized bindings survive updates.

## Requirements

- This build targets Sprocket **0.2.56.2 Experimental**, Windows x64, Unity 6000.3.21f1. It has been compiled against that installation's interop; native gameplay validation remains pending.
- A working **Sprocket Mod Loader / BepInEx 6 IL2CPP (6.0.0-be.788)** setup with its runtime and generated interop. Loader installation is separate. Stock BepInEx alone is not claimed equivalent to the tested Sprocket-specific setup.
- Quality of Life is not required or included. Other game versions have not been verified for this build.

## Install and update

1. Install a working Sprocket Mod Loader / BepInEx 6 IL2CPP setup, run Sprocket once, then close it. The loader is a separate prerequisite and is not included.
2. This source delivery has no published 0.1.9 archive. Build using [BUILD.md](BUILD.md); the resulting DLL is `bin/Release/net6.0/SprocketKeybinds.dll`.
3. In Steam, use Sprocket > Manage > Browse local files. After backing up the existing DLL, copy the new DLL to `BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll`.
4. Keep one copy of each plugin. Back up matching mod files and vehicle saves before updating. Never replace the whole BepInEx folder.
5. Preserve existing BepInEx/config files, customized thermal-models.json and sound overrides. Install required dependencies separately. Restart the game.

## Usage, controls and settings

Open Settings > Controls/keybinds > Mod keybinds > Open. Expand an author, then a mod. Clear unbinds a slot; Reset restores its factory default. Apply saves, Accept saves and closes, Cancel returns to the last applied bindings. Author/pack grouping changes display only. Storage: BepInEx/config/sprocket.keybinds.json; keep its .bak for recovery. Mods must opt into this API; it does not automatically add every installed mod's controls. [API integration](INTEGRATION.md) explains registration, dependencies and metadata for other modders.

## Mod keybinds panel in 0.1.9

The fixed Settings panel follows UI Kit STYLE.md 0.1.0: rounded olive window and shadow, compact title/footer, By author / By pack segments, nested section headers, action-count tags, styled native rebind/Reset rows and quiet Clear buttons. The native Mod keybinds / Open launcher stays unchanged. Close with the top-right cross (X if the font lacks the glyph). The footer shows Esc / cancel capture only during rebinding. Free single keys occupy a dynamically sized footer block in the same masked scroll view; unverified vanilla coverage has an icon and warning text.

Sizes and fonts scale in canvas units by `(Screen.height / 1080) / rootCanvas.scaleFactor`; resolution and canvas-scale changes trigger layout updates. Typography uses `TMP_Settings.defaultFontAsset`, falling back to the template action-label font. Local style helpers avoid a circular dependency: this API does not reference the UI Kit assembly. Rebinding, combo rules, grouping membership, Clear/Reset and Apply/Accept/Cancel retain their existing behavior. Compatible consumer dependency ranges remain `>=0.1.3 <0.2.0`.

This delivery is built and managed-tested, without installation or in-game validation. See [NOTES.md](NOTES.md) for limitations, test results and the game test plan.

## Troubleshooting, saves and rollback

If the mod is absent, check BepInEx/LogOutput.log for the mod name, missing dependencies, duplicate plugin versions or invalid configuration. When this mod requires Keybinds, missing/incompatible Keybinds causes the mod to be skipped; old direct-key CFG entries do not replace that requirement. Preserve a malformed file for inspection instead of overwriting all your settings. Restart after repairs.

Restore your backed-up mod files and settings together for rollback. Do not delete an entire shared folder. Custom parts/materials may be referenced by vehicle saves: return affected vehicles to stock parts/materials and save before uninstalling. Keep save backups; installed mods and release archives do not back up every vehicle automatically.

## Credits and support

Mod code is MIT licensed; native Sprocket meshes/icons are resolved from your installed game and are not bundled.

## Where to get the separate loader

Use [Hans21223's Sprocket Mod Loader](https://github.com/Hans21223/Sprocket-Mod-Loader) and follow its [manual installation guide](https://github.com/Hans21223/Sprocket-Mod-Loader/blob/main/package/MANUAL-INSTALL.md) or its documented manager installation. That upstream project targets the tested Sprocket version and supplies the Sprocket-specific patch. These mod downloads do not install the loader. Follow one upstream loader method and its update/backup instructions; the creator's supplied ModManager archive is not redistributed here.

## Combo bindings and free single keys in 0.1.8

During capture, hold Shift, Ctrl and/or Alt before pressing the main key. Either physical side works as a modifier. To bind a modifier by itself, press and release it without another key. Escape cancels capture. A more specific matching mod combo suppresses a less specific binding on the same main key. Adding a modifier after the main key is already held does not trigger a combo. Unrelated modifiers still work with ordinary bindings when no competing combo matches. Keyboard and mouse buttons remain supported.

The information panel shows a few free single keys, grouped by type. Keys used only in combos count as free for single use; combos themselves are not listed. The panel reads effective vanilla bindings and registered mod actions, updates after edits, and checks vanilla changes every half second. If it cannot verify vanilla bindings, it displays `vanilla onbekend` without advertising free keys. The list cannot discover controls implemented through another mod's direct keyboard polling. Free does not imply gameplay priority over vanilla.

Existing compatible consumers need no recompilation. Addons can optionally register combo defaults using strings such as `Shift+<Keyboard>/x`; integrations using this syntax require API `>=0.1.8 <0.2.0`. See [the integration guide](INTEGRATION.md) for the storage extension, conflict rules and API details. Keep the config and its backup on updates; older API versions do not support downgrading combo records. Live UI/input/scene validation is listed in [NOTES.md](NOTES.md).

<!-- sp-compat {"hamish.sprocket": ">=0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->

