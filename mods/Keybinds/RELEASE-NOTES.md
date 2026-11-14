# Sprocket Keybinds 0.1.9 - development beta

- Restyle the existing Settings panel to UI Kit STYLE.md 0.1.0 with local cached rounded fill/outline/shadow sprites; no new assembly dependency or circular dependency.
- Add a 44px title bar, author/pack segmented control, cross close button, nested section/list headers with indicators and counts, quiet Clear controls and a 32px status footer with capture-only Esc key cap.
- Restyle cloned native Images and TMP labels without removing components or changing their callbacks; retain native rebind/Reset, capture, Apply/Accept/Cancel, input, storage and public API signatures.
- Put grouped free single keys and the icon-bearing vanilla-unknown warning in a dynamically measured footer block inside the existing ScrollRect/RectMask2D.
- Scale reference dimensions/fonts by screen height divided by 1080 and root-canvas scale; update on resolution or canvas-scale changes. Use the TMP default font with the native action-label font as fallback.
- Release and consumer builds pass with zero warnings; pure checks cover the public ABI baseline, parsed guide tokens, no Kit dependency and safe IL2CPP delegates. Only the release-version and plugin-GUID constant values are normalized by the ABI audit; they are separately checked as 0.1.9 and `sprocket.keybinds`.

No installation or publication. Appearance, glyphs, transitions and runtime menu behavior await the live game plan in `NOTES.md`.

# Sprocket Keybinds 0.1.8 - development beta

<!-- sp-compat {"hamish.sprocket": ">=0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->

- Bind actions to Shift/Ctrl/Alt combinations through Mod keybinds. Either modifier side works; press and release a modifier alone to bind it independently. Escape cancels capture.
- The most specific matching mod binding wins for a main key. A plain binding still works with modifiers when no competing combo matches. Modifiers must precede the main key.
- See examples of free single keys in the scrolling panel. Effective vanilla bindings and registered mod actions are included. Combo-only keys remain available for single use. Unavailable vanilla coverage is explicitly shown as `vanilla onbekend`.
- Existing consumer signatures and dependency ranges remain compatible. Optional combo factory defaults use `Shift+<Keyboard>/x` and require API 0.1.8 or later, below 0.2.0.
- Preserve existing plain, unbound, factory-reset and absent-owner config records. Combo metadata is additive; Apply/Accept, Cancel/abandon, Clear, Reset and action-identity migration retain their transaction behavior. Older versions do not support combo-config downgrade.

Built against Sprocket 0.2.56.2 Experimental / Unity 6000.3.21f1 / BepInEx 6 IL2CPP 6.0.0-be.788 interop. Managed build, pure tests, public API metadata comparison and consumer build are checked locally. Native input ordering, UI layout and map/pause lifecycle still require live validation; see `NOTES.md`. No installation or public release was performed.

Close Sprocket before replacing the shared API DLL. Back up the DLL and preserve `BepInEx/config/sprocket.keybinds.json` and its backup.

