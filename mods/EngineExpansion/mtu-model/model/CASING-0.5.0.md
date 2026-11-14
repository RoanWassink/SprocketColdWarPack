# EPP casing approval asset (not integrated)

`casing.scad` exports `parts/casing-lod0.stl` and `parts/casing-lod1.stl`. Unit frame x=-0.5..0.5, y=0..1 (motor rear to gearbox rear), z=0..1 (geometry starts at 0.045). Units are normalised, not millimetres; the existing engine assets are millimetres. Future integration scales/interpolates the unit casing to engine and gearbox cross sections. LOD0 adds fastener pads and cover ribs; LOD1 keeps the same structural envelope. Separate watertight shells are intentional where fasteners sit inside the folded rail/hood envelope.

`view-casing.scad` imports the exact STL, displays thin yellow bounds, and supports lod=0/1. `view-casing-pack.scad` shows the existing exported MTU/Renk meshes with the casing; these are approval mockups, not claims about current native transforms. The Twin preview includes a bridge. Neither preview nor casing is a DLL resource or obstruction collider.

Reproduce: `py -I -B mtu-model/model/build-casing.py`. Manifest, bounds, triangle counts and SHA256: `logs/casing-manifest.json`. OpenSCAD output: `logs/casing-build.txt`. The build script validates watertight edges and unit bounds before rendering 1200x800 PNGs. OpenSCAD 2021.01 can reorder facets when it re-exports the same CSG, so the **stored `parts/casing-lod0/1.stl` are the canonical embedded assets**; `embed_meshes.py --check` is the authority for the DLL payload. Re-running the SCAD build is a geometry/visual reproduction, not a byte-identical rewrite of the canonical STLs.

The integration is now built: `CasingFit` stretches this unit frame from the engine rear cross-section to the gearbox box. The exact V12/Renk check render is `shots/casing-fit-v12-renk.png`, reproduced from the embedded payload by `preview-casing-fit.py` and `view-casing-fit.scad`; preview positions and the 520*k shoulder in `view-casing-pack.scad` are approval mockups only.

PNG files: `shots/casing-unit.png`, `shots/casing-v12-renk.png`, `shots/casing-twin.png`, `shots/casing-v8-small.png`. All were visually inspected; rails and hood are present, the motor and gearbox remain readable. No icons were added, so the part-icon contact-sheet rule is not applicable.
