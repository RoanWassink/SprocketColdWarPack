# Heavy Trunnion Mount: OpenSCAD model (2026-11-03)
Source: `heavy-trunnion-lib.scad` (all geometry), `heavy-trunnion-static.scad`, `heavy-trunnion-moving.scad`, `heavy-trunnion-pinion.scad` (0.3.0). Rebuild everything: `powershell -ExecutionPolicy Bypass -File build.ps1` (export, normalize, clearance, checks, PNGs, icon). Logs: `logs\stlcheck.txt`, `logs\clearance.txt`, `logs\determinism.txt`.
**Units: mm** (×0.001 for metres). Axes: X = trunnion axis, Y = forward (gun), Z = up; origin = centre of the trunnion axis. Unit model at `trunnionsScale` 1.5.

## Groups and parts
- **Static** (mantlet base, does not tilt), bounds X ±357.5, Y −317..125, Z −225..98:
  two bearing blocks (ring R88 × 107.5 wide at |x| 250..357.5, waisted pedestal, split line, 2 cap nuts, grease cup, foot flange with 4 bolts); bronze bushes (bore R48, OD 120, outer flange R68); bed plate 715 × 405 × 20 with front cut-out and 5 bolts; reduction gearbox (x −150..−40) with bolted cover; pinion (12 teeth, module 6, pitch R36, 34 wide, centre y −131 z −110) on a shaft with outboard bracket; elevation motor (Ø108 finned body, 9 axial fins, flange, fan cowl with grille, 2 feet, terminal box + cable gland); hand wheel (Ø92) on the back of the gearbox.
- **Moving** (on the trunnions, tilts about X), bounds X ±357.5, Y −138..74, Z −131..66:
  axle R55 (= native R55.3 at 1.5); journals (pins) R48 from |x| 230 to **357.5, flush with the bearing faces** (X width 715.0 = native 0.4765 m × 1.5); thrust collars R66; narrow saddle plate 450 × 117 × 23, top at z 56 (native trunnion top 55.3), 8 bolt heads; 3 clamp straps; sector gear (pitch R135, 8 teeth over −173..−107°, steel rim bolted to a cast web with 2 lightening holes and 3 ribs) on a hub at x −200..−150.
- **Pinion** (third mesh, 0.3.0; split out of the static group): 12 teeth, shaft and shaft-end nut, steel. Origin = its own axis (model y −131, z −110), axis parallel to X; in the game it turns −3.75 × the elevation (sector R135 / pinion R36), so the pitch circles roll without slip. The static pieces are cut by the pinion, so static + pinion at 0° is the old static group. Only change at 0°: the gearbox cover bolt at a = 0 lies inside the pinion tip circle and is now 1.5 mm high (x −157.5..−156, was 4 mm) so the turning teeth pass it; `zij.png` is pixel-identical, `34-boven-achter.png` differs in 62 px.
- Pins vs bearings: journal R48 = bush bore R48, same 20-gon, same length → no gap, no overlap (checked by `stlcheck.py`). Collars stop 1 mm short of the bearing faces.
- Clearance from −10° to +25° (the part's elevation limits) with the pinion turning along (`logs\clearance.txt`): static ↔ moving (5° steps), pinion ↔ moving sector (2.5° steps), pinion ↔ static (5° steps): no collision. Excluded by design: journals in the bushes and the pinion shaft in its bores (bracket, cover). A deliberately mistimed pinion (15°) does collide, so the test can fail.

## Materials (PNG colours → game)
- `cast` dark cast steel: bearing housings, pedestals, bed, gearbox, motor, saddle, straps, sector hub/web. → game material 1 (dark).
- `steel` machined steel: axle, journals, collars, sector rim + teeth, pinion + shaft, all bolts/nuts, grille, hand wheel. → game material 2 (metal).
- `bronze`: bearing bushes (static only). → metal material too (0.3.0: one submesh per material file, cast = dark, steel and bronze = metal).
- `materials\*.stl` = the groups split per material: bronze > steel > cast, disjoint, union volume = sum (checked). `materials\pinion-steel.stl` = the pinion group.

## Triangles ($fn 20 for round game parts, 16/12 for small, 6 for bolts)
| mesh | tris | | mesh | tris |
|---|---:|---|---|---:|
| heavy-trunnion-static.stl | 2782 | | static cast / steel / bronze | 2598 / 886 / 480 |
| heavy-trunnion-moving.stl | 1720 | | moving cast / steel | 964 / 876 |
| heavy-trunnion-pinion.stl | 340 | | pinion steel | 340 |
| **groups total** | **4842** | | per-material total (in the DLL) | 6144 (shared internal faces) |

## Axis conversion for the build (OpenSCAD → Unity)
`unity = (x, z, y) * 0.001` and **reverse the winding** (a,b,c → a,c,b): swapping Y/Z is a mirror. OpenSCAD +elevation = `rotate([+e,0,0])` = gun up; in Unity that is a negative angle about local X (the game drives the trunnion transform, the mesh just hangs under it). Static mesh → mantlet base transform; moving mesh → trunnion transform; pinion → child of the static mesh at Unity (0, −0.110, −0.131), turned −3.75 × the trunnion angle (same rule in both frames, the mirror flips both senses). Converter: `tests\tools\heavy_mesh_to_cs.py` → `src\HeavyTrunnionMeshData.cs`.
**Scale (resolved 0.3.0, disassembly of `Mantlet.Build`):** the trunnion transform gets only a local position (part scale × trunnionLocalPosition); part scale × `trunnionsScale` goes to the native trunnion *model* (`IVehicleModelComponent.set_ModelScale`). The transform does not carry the 1.5, so the mod scales both meshes by part scale × trunnionsScale / 1.5 (net of any inherited Unity scale): 1.0 at the default.

## SHA256 (canonical STL from `stlnorm.py`; a second export + normalize gives the same bytes)
- `heavy-trunnion-static.stl` 775c1c68e65928504d36c42654ba3fdb5a7957cb966fa3274ef169b5fcb290f2 (pinion cut out; was bd67b611…)
- `heavy-trunnion-moving.stl` 3679865af0a9af92e4d7c298b58e25ea381898eff07efaa5b8c49d5b978a104a (unchanged)
- `heavy-trunnion-pinion.stl` 5fcca19d1ad7ad7b5950c729d31aec0900b8e8411dc24b594cd4bdd8a54366b9 (= `materials\pinion-steel.stl`)
- Per-material hashes: `logs\stlcheck.txt`. Raw OpenSCAD 2021.01 STL is NOT byte-stable (random triangle order): always run `stlnorm.py` before hashing.
