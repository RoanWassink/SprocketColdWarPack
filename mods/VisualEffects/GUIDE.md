# Sprocket Visual Effects 0.4.5

Author: Nero. Plugin GUID `sprocket.visualeffects`, config `BepInEx\config\sprocket.visualeffects.cfg`.

Adds a cannon-shot overlay (volumetric muzzle blast, lit smoke puffs, ground dust, short muzzle light) and three RGB tank
lamp parts, LAMP ROUND, LAMP RECT and LAMP MARKER (Crew / Equipment), with RGB lens glow, a soft halo and an HDRP light.

## Lamps (0.4.5, route B "stock lamp shape")

- The lamps are normal static parts with the stock model: LAMP ROUND = stock headlight, LAMP RECT = stock tail light,
  LAMP MARKER = stock tail light at a default scale of 0.6. Same mesh/material GUIDs, placementMode (forwards / backwards),
  dynamic:false, mirroring/flipping and scale limits as the stock lamps, so placing, mirroring, moving, rotating, paint and
  the paint atlas work exactly like a stock lamp.
- Visual Effects does not modify the part at all (no MarkDynamic, mesh, shadow/interaction mesh or material swap). The glow
  is an own emissive disc on the stock lens, plus the halo and the real light; these are own objects that are not under the
  vehicle and follow the part's native model transform every frame, including the mirrored twin (negative scale). An off
  or IR lamp shows the plain stock lens.
- Right-click "Lamp" section in the part inspector (colour preset, #RRGGBB, brightness, range; the mirrored twin follows).
  Settings are stored per lamp through the Telescopic Mast (storage key `visualeffects.lamp`); defaults (rect 80 cd,
  marker 20 cd) come from the config shape settings.
- Display-referred lens colour (red stays red), light budget, distance LOD and fade, eligibility/ghost rules.

## Dependencies

BepInEx 6 IL2CPP. Telescopic Mast (`sprocket.telescopicmast`) is an optional soft dependency, found by plugin GUID and
reflection only (type `SprocketTelescopicMast.Io.IoApi`): IO-API v1 revision 1 enables per-lamp settings, managed flow
persistence and icons. No Keybinds or UI Kit dependency.

## Harmony patches

Exactly two: the muzzle observer (postfix on `MuzzleFlashEffect.Setup`) and a postfix on `VehicleObjectEditor.OnGUI`
(unique RVA 0x1F92E50, hash/signature/RVA guarded, own Harmony owner `sprocket.visualeffects.inspector`). Both are
postfix-only and are skipped when the game build differs from the audited one.

## Files

Installed under `BepInEx\plugins\SprocketVisualEffects\`: `SprocketVisualEffects.dll`, `sprocketvfx.bundle` (Unity AssetBundle with
the volumetric shader/material), `assets\` (muzzle atlases and `effect-manifest.json`), `lamp-assets\` (halo texture and the
three part icons). Part JSON goes to `Sprocket_Data\StreamingAssets\Parts`, localization to
`Sprocket_Data\StreamingAssets\Localization\en-UK\Parts`, the default config to `BepInEx\config`. Provenance of the assets:
`ASSET-PROVENANCE.txt`.

## Build and check

```powershell
# build and run every check once; writes nothing into the game folder
.\Verify.ps1 -GamePath <Sprocket folder, read only> -GameRef <folder with BepInEx\core and BepInEx\interop> -Out <scratch folder>
# stage the files in game-folder layout into an empty folder
.\Build-Package.ps1 -Out <empty folder> -GameDir <folder with BepInEx\core and BepInEx\interop>
```

Needs the .NET SDK 8.0.425 (`global.json`). Offline checks cover: part JSON = stock lamp, no native part setter in source
or DLL, lens position vs the stock mesh, lens transform with negative scale (mirrored twin), the Mast link by GUID and
type name with a fake facade (revision 0, 1, partial, throwing), settings, budget/LOD/fade, effect math, and the static
interop scan of the built DLL.
