# Sprocket Keybinds 0.1.9 — delivery notes

Prepared source and DLL only; nothing installed, published or changed in the game directory. The UI/dependency boundaries below take precedence over general panel/dependency recommendations; no Kit or input changes were added.

## Restyling

- `PanelStyle.cs`: exact named Color tokens from UI Kit STYLE.md 0.1.0 (Color is a Unity struct, so these are `static readonly`, not C# `const`); the only extra hex token is the brief's Clear hover #3a1f17. Local supersampled cached 9-slice fills and 1px outlines at radius 4/6, radius 3 key cap, and a 0/18/48, alpha .55 shadow. Runtime-owned sprites/textures use HideAndDontSave and are destroyed with panel cleanup. The shadow and window fill are ordered siblings beneath the chrome, so the shadow cannot darken the window content.
- The window remains inset 18 reference px within `keymappingGUI`, with `LayoutElement.ignoreLayout`. Title bar 44, footer 32, body padding 14, row gap 4. Caps headings use TMP spacing 6 (title) / 10 (sections/block title). Font is TMP_Settings.defaultFontAsset, fallback template.ActionLabel.font. Missing cross/triangle/warning glyphs fall back to X, +/- and !.
- By author / By pack are adjacent 34px segments using the same stop-capture/toggle/layout handler. The cross retains Back's stop-capture/hide callback. Parent headers are 36px transparent sections with indicator and N MODS. Mod headers are 36px, or 40px with owner-id for duplicate display names, inset 12px; expanded rows have selected fill/border and an indicator. The 17px action tag counts distinct action IDs, including actions with secondary slots only once.
- Rebind clones: enumerate every descendant `UnityEngine.UI.Image`, including inactive children. An Image used by a descendant Selectable as `targetGraphic` gets a white base (to prevent double tint) and ColorBlock normal #2b2924, highlighted #3a372e, pressed/selected #3a3220, disabled #1f1e18. Other Images get #24231c. Every image uses the local radius-4 sprite; original raycast flags are preserved. Every descendant TMP_Text, including inactive labels, gets the chosen font, fixed 14px reference size, rich text off; the exact `RebindActionUI.BindingText` gets Value #c9bc9c, all other TMPs Text #fff0cc. Root row is 36px high, inset 24px, with 72px reserved at the right for Clear plus its gap. Descendant anchored positions/size deltas scale from captured template values, preventing cumulative scale drift. No component removal, event replacement, listener registration, label-value rewrite or input change occurs in the style helper. Plugin.cs changes only the release constant and one `PanelStyle.StyleRow(clone)` call; layout reapplies visual styles after resolution changes.
- Clear is 28px high, font 12, horizontal padding 10; transparent fill and Border outline normally, danger fill/border/ink when hovered/selected/pressed. Normal uGUI ColorBlock transitions drive button fills. Border/ink follow the existing Selectable pointer/selection state; no new hit tests, pointer callbacks, Input System reads or cursor/input blockade. All new clicks remain parameterless ConvertDelegate<UnityAction> through Plugin.Safe.
- Free single keys are a footer block at the end of the same ScrollRect + RectMask2D content. Title, four group-name/key label pairs and combo note are separate plain TMP labels. Wrapped key/note rows reserve preferred text heights dynamically. Unknown vanilla coverage hides examples and shows a warning row plus icon. Footer help retains the specified text; Esc key cap and cancel-capture text appear only while rebinding.
- Canvas-unit factor is `(Screen.height / 1080f) / rootCanvas.scaleFactor`, so multiplying by the canvas scale gives precisely Screen.height/1080 screen pixels per reference px. Geometry, font sizes, native-child offsets, scrolling and nine-slice borders use that factor. TMP characterSpacing stays in its native em/100 units because font-size scaling already scales it. TickLayout checks width, screen width/height and canvas scale; text-height changes trigger relayout.
- Optional font, sprite, row-child and chrome styling is guarded. A styling failure logs at most one warning per plugin lifetime. Failed fills use a plain Image; failed outlines/shadow stay transparent to avoid obscuring controls. Header/free-block chrome errors cannot interrupt the remaining binding layout. Native launcher code remains byte-for-byte identical as text; the input, store and metadata source files are unchanged by the restyle.

## Validation and deviations

All 181 pure checks pass (178 without the ABI audit); the plugin and demo Release builds report zero warnings and zero errors. The tests check storage/combo behavior, the public ABI baseline, exact parsed STYLE.md tokens (snapshot in `tests/STYLE.md`), absence of the Kit name/dependency in panel/helper/project source, and the native delegate scan including PanelStyle.cs.

The ABI helper normalizes ONLY the literal values of Plugin.PluginVersion and Keybinds.PluginGuid, retaining their names, types and flags. All other public constants/signatures/optional defaults remain exact. Separate metadata checks require the newly built DLL's version constant to be 0.1.9 and its plugin GUID to be sprocket.keybinds.

No design changes beyond the documented fallbacks and the language-required readonly Color fields.

## Unverified without a live test

No game was launched and no DLL was installed. Managed checks and interop compilation do not prove native rendering, actual canvas scaling at 1080p/another resolution, inherited row geometry, glyph support, shadow/ring appearance, hover/selection transitions, readability at narrow widths, scrolling, live capture or map/pause lifecycle. Grouping, plain/combo capture, Clear/Reset and transactions use unchanged logic, but their live operation with the restyled clones still needs confirmation. Graceful sprite/font failure handling is source-reviewed, not fault-injected inside IL2CPP.

## Game test plan (12 lines)
1. Open Settings > Controls/keybinds > native Mod keybinds / Open; verify the launcher remains native.
2. At 1920x1080 inspect window, title/subtitle, shadow, fonts, row heights, padding, borders and footer.
3. Switch to another resolution while open; inspect reference scaling, wrapping, scrolling and clipping.
4. Switch By author / By pack; verify grouping and pending edits survive.
5. Expand/collapse parents and mods; verify indicators, N MODS, action tags and duplicate-name owner IDs.
6. Rebind a plain key; verify binding text, capture prompt, Esc hint and Esc cancellation.
7. Rebind a Shift/Ctrl/Alt combo and a modifier alone; verify display and existing gameplay semantics.
8. Hover/click Clear; verify danger feedback, unbound display, updated free keys and explicit unknown-vanilla warning when applicable.
9. Reset a slot; verify its real factory default returns and the free-key block updates.
10. Apply, make another edit, then Cancel; reopen and verify the applied edit remains and the later edit rolls back.
11. Accept an edit, reopen, close with the cross/X, then restart; verify persistence and unchanged close behavior.
12. Load a map and repeat opening/rebinding from pause Settings; verify menu lifecycle and gameplay input after closing.

