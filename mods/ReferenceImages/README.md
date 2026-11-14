# Sprocket Reference Images

Adds a "Reference Image" board part to the vehicle designer. You pick a PNG or JPG, or paste one from the clipboard, and it is shown on the board as a reference while you build, for example a drawing or a photo of the vehicle.

- Version: 0.1.2
- Requires: Sprocket UI Kit (`sprocket.uikit`, >=0.1.3 <0.2.0) and Sprocket Keybinds (`sprocket.keybinds`, >=0.1.3 <0.2.0)
- Optional: none
- Plugin GUID: `sprocket.referenceimages`
- Game version tested: 0.2.56.2

## Features

- A new part, "Reference Image" (decorations, crew / equipment), with its own icon. The part definition has zero mass and is not damageable.
- Placed, moved, rotated and scaled with the native designer tools; the board stands upright and can be placed in the air. Scale limits are 0.1 to 6.
- The board is 2 x 2 m at scale 1, shown on both sides; the picture keeps its aspect ratio inside the board and is letterboxed.
- Pictures come from `Documents\My Games\Sprocket\Reference Images` (PNG and JPG; the folder is created on first start) or from the clipboard (PNG, DIBV5 and DIB).
- A designer-only panel built with UI Kit: image list, Open folder, Refresh, Paste and a per-board Mirror toggle.
- Pasted images are saved as PNG files in the same folder.
- Image limits: 8192 px per side (less if the GPU maximum is lower) and 32 megapixels.
- One Harmony postfix only (the part card icon, `PartDefinitionCardFactory.CreateCard`); no save or load hooks.
- Image assignments are kept in memory for the current game session only. After reopening a vehicle the board may show "NO IMAGE" again, by design.

## Settings and keys

- Config: `BepInEx\config\sprocket.referenceimages.cfg` (created by BepInEx on first start). One entry: `[Diagnostics] DetailedLogging = true` (verbose log lines; warnings and errors are always logged).
- Keys, both unbound by default: `Reference Images / Panel` (open the panel for the selected board) and `Reference Images / Paste` (paste the clipboard image onto the selected board). Bind them in the game's Settings > Controls/keybinds > Mod keybinds, which Sprocket Keybinds provides.
- Select exactly one board before opening the panel; the panel only opens in the designer.
- Shipped files: `BepInEx\plugins\SprocketReferenceImages\SprocketReferenceImages.dll`, one part JSON (`Sprocket_Data\StreamingAssets\Parts`) and one English localization XML (`Sprocket_Data\StreamingAssets\Localization\en-UK\Parts`).

## Status

- In game (0.1.1): the part appears in the list, and pasting a PNG works without exceptions.
- 0.1.2 (upright placement, live scaling, rebuilt colliders, 3 mm picture quads) is installed but not yet tested in game; selection, collision, scaling up to 6, mirror and the session memory are still to be checked.
- The whole pack was just renamed and has not been re-tested in game since.

## Building

.NET project in this folder (`SprocketReferenceImages.csproj`, net6.0); it builds against the game's BepInEx and interop assemblies with `-p:GameDir=<path to Sprocket folder>`, and also references the built Sprocket UI Kit and Sprocket Keybinds DLLs in `BepInEx\plugins\` of that folder.
The two board meshes (`model\reference-board-frame.stl`, `model\reference-board-image.stl`) are embedded at build time but are not in this repository copy; regenerate them from `model\reference-board.scad` with OpenSCAD 2021.01 (see `model\MODEL.md`, `model\verify-and-render.ps1`). Offline checks are in `tests\` and `tools\`; see [CHANGELOG.md](CHANGELOG.md).
