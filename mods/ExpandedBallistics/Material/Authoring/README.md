# Relikt directional active geometry

Run generate-relikt-active.py after general ERA regeneration, then test-relikt-active.py. Supply --output and --report; --mirror-canonical updates only the canonical Relikt overrides. The scripts use immutable local authored baselines and never access installed game files.

Each paid cell contains one closed inclined 13 mm slab. relikt-active-layouts.json declares riseAxis, riseSign and angleDegrees per model, with an explicit third-cell override for the narrow 2+1 package. Volume, case footprint and cover details are preserved. These angles are an authored game proxy, not verified factory internals.

Orientation follows each active cell's 250 mm length: local +X for eleven cells, local +Z for the transverse lower cell in the 2+1 package. This is the cell length, not the outer housing length. Mount using the part's local gizmo; reversing the declared axis reverses the preferred direction. All cells use a 25 degree authored inclination so the original plate fits inside its original case. At grazing angles the opposite or perpendicular direction may miss the charge. The housing alone never grants activation. No universal directional coverage is promised. Exact factory angle and handedness have not been verified.

Generation and tests assert inclination along the longer cell dimension. Tests verify intended centre rays at 0/60/70/80/85 degrees, negative opposite/perpendicular grazing rays, watertight winding, original paid volume and 13 mm shortest-edge depth. Native traversal and spent/reset visuals remain live checks. Nonuniform player scaling may change shortest-edge calibration.
