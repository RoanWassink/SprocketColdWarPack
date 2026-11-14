# Sprocket Expanded Ballistics

<!-- sp-compat {"hamish.sprocket": ">=0.2.56.1", "bepinex.bepinex": "6.0.0-be.788"} -->

Version 0.22.72. Author: Nero. MIT licence (see `LICENSE`).

Design and select ammunition, simulate armour penetration, choose armour materials, compose layered armour cassettes and fit reactive armour. One BepInEx 6 IL2CPP plugin with reusable feature modules and one shared ERA catalogue.

- **Shells**: custom shell profiles (APFSDS long and short rod, APHE, HE, HEAT, tandem HEAT, HESH) and guided ATGMs (SACLOS, MCLOS, top attack, gun-launched). APFSDS uses the physical penetrator length, density and speed to limit penetration; long projectiles also need room in the autoloader, so a longer penetrator is not a free upgrade. Shells are dated by the vehicle's date and the technologies it has.
- **Armour materials and ERA**: material editor in the plate-structure panels, placeable reactive armour (Relikt, Kontakt-1, Kontakt-5, Nizh, Duplet and others) in the **Reactive armor** tab, the cassette composer for layered passive and reactive packages, a composite side module, and extra materials (wood, rubber, fabric, glass).
- **Reports**: a read-only combat report with the newest ten observed impacts, a forensic section drawing per impact, and an optional on-screen damage feed. Session history is also saved as local HTML and JSON after Play.
- Tandem HEAT is available in the armour simulator: its precursor and main charge share temporary ERA consumption for that sample, without consuming the vehicle's real ERA.

## Requirements

Sprocket 0.2.56.x with BepInEx 6 IL2CPP (verified 6.0.0-be.788). Required plugins: Sprocket JSON Editor (0.1.x, 0.1.5 or later) and Sprocket UI Kit (0.1.x, 0.1.7 or later). The optional shared Sprocket Keybinds plugin adds the configurable key bindings (combat report, ammunition cycling, ATGM guidance and others). No game assemblies, generated interop or loader are included.

## Installation

Close Sprocket first. The package mirrors the game folder: copy `BepInEx` and `Sprocket_Data` into the folder that contains `Sprocket.exe` and merge. Everything the plugin loads at run time sits in one plugin folder:

```
BepInEx/plugins/SprocketExpandedBallistics/
    SprocketExpandedBallistics.dll
    README.md
    assets/          icons and textures of the ATGM launcher, ammunition box and guidance sight
    era-assets/      meshes and icons of the placeable ERA packages
    material-looks/  drop-in folders for your own wood, rubber, fabric and glass textures (see the README.txt in each)
```

Keep one copy of the DLL only. Back up your vehicle saves and your settings before updating.

## Configuration

Settings live in `BepInEx/config` and are created on first start:

- `sprocket.expandedballistics.cfg` and `sprocket.expandedballistics.materials.cfg`: plugin settings (BepInEx format).
- `sprocket.expandedballistics.shells.json`, `.modules.json`, `.modules.import.json`, `.spall.json`: shell profiles, the module authoring file and spall balance. See `CUSTOM-SHELLS.md`.
- `sprocket.expandedballistics.responses.json`: threat eligibility, passive fingerprints, angle curves, retention and cumulative limits. See `ARMOUR-RESPONSES.md`.
- `sprocket.expandedballistics.experimental.json`: versioned dynamic rod-family recipes and susceptibility settings.
- `sprocket.expandedballistics.material.reactions.json`: extensible material recipes, threat tags and exact native passive bindings. User content overrides the embedded defaults; per-interaction limits do not cap the complete armour array.
- `sprocket.expandedballistics.era.bindings.json`: physical part and cassette bindings, loaded once per session (restart after changes).
- `sprocket.expandedballistics.cassettes.json` and `sprocket.expandedballistics.cassettes-share/`: cassette presets, including your own, and the share folder.
- `sprocket.expandedballistics.composite-module.json`: the composite side module.
- `sprocket.expandedballistics.materials.editor.json`: labels written by the material editor.
- `sprocket.expandedballistics.material-looks.json`: optional per-material texture settings.
- `sprocket.expandedballistics.audio/`: optional WAV overrides for the ATGM sounds (they take priority over the embedded sounds).
- `sprocket.expandedballistics.combat-report/` and `sprocket.expandedballistics.layer-overlay/`: written reports and layer-overlay dumps.

Do not overwrite your custom catalogues with the files in `examples/`. Shared ERA edits are validated before reload; an invalid edit keeps the last valid settings. Shots already in flight keep the settings captured at launch; new shots use successfully reloaded values.

## Using it

Shell controls are in the cannon panel and the shared JSON editor. Material controls are in the plate-structure panels. Placeable ERA is in **Reactive armor**, beside Structural. Configure **Sprocket Expanded Ballistics** actions in the shared Keybinds settings (the factory binding of the combat report is unbound).

To create tandem HEAT, choose **HEAT impact** in the shell's Effect group, set **Precursor penetration** above zero and set **Main-charge penetration** independently. Both values are millimetres at the **Reference calibre**. A precursor value of zero gives ordinary single-charge HEAT. Loader space applies to HEAT and tandem HEAT too: a length of 10 calibres in a 125 mm gun is a 1,250 mm projectile before its charge.

The ordinary Relikt, Kontakt-5, Nizh and Duplet materials keep their 60-80 mm declared-face normal-thickness eligibility and paid passive properties. One material cell is one responding stage, even for the Duplet label. Multiple physical layers need multiple reached structures. Spent cells do not react again; the next Edit/Play cycle rearms them. A tandem precursor may consume cells before the main charge, so no guaranteed tandem defeat is claimed.

Custom curves and calibrations are never silently replaced. The numerical protection is a gameplay approximation, not certified manufacturer performance.

## Logging and performance

Detailed developer logging is off by default. Keep `Diagnostics / Detailed logging` disabled for normal play. Warnings and errors remain visible in the BepInEx log.

## Troubleshooting

If a custom shell fails validation, repair the reported fields and reload through the editor. An invalid APFSDS design is withheld rather than silently replaced by vanilla ammunition. If a shell does not fit, reduce its projectile or charge dimensions or increase the loader's usable space. Back up custom profiles before editing them outside the game.

For a missing simulator list, confirm that the cannon has available profiles for the vehicle's date and technologies. Repeat samples in the simulator are previews and do not spend the vehicle's installed ERA.

## More documents

`REFERENCE-GUIDE.md` (shell and ATGM reference), `CUSTOM-SHELLS.md`, `ARMOUR-RESPONSES.md`, `ATGM.md`, `docs/` (armour sandbox, spaced armour, shell usage, ATGM flight, native API facts, ICF audit) and `RELEASE-NOTES.md`. Building from source: `BUILD.md`.
