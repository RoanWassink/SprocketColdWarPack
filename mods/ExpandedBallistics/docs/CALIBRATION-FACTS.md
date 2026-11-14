# Additional measured local munitions metadata

Coordinator subsequently read numeric fields from the actual local GHPC serialized ammunition objects (same game hash), using read-only local metadata inspection. No game code, binary, prefab, image or audio is supplied here. These values are behavioral reference measurements, not declarations of real-world specifications. Existing custom Sprocket profiles must remain untouched.

| Reference | Speed m/s | Turn rad/s -> our deg/s | LOS lead m | Guidance delay s | Maximum range m |
|---|---:|---:|---:|---:|---:|
| I-TOW direct | 278 | 0.12 -> 6.87549354 | 30 | 0 | 3750 |
| Malyutka-M manual | 115 | 0.20 -> 11.45915590 | 30 | 0 | 3000 |
| Kobra hump | 375 | 0.10 -> 5.72957795 | 200 | 1 | 4000 |
| Konkurs direct (optional fourth example) | 208 | 0.12 -> 6.87549354 | 30 | 0 | 4000 |

Kobra: above-LOS height 4m, descend when remaining range <=700m, skip initial lockout for launch range <1000m, no raised cruise for launch range <1920m, initial forced elevation 0.5 degree. Guidance radio/transport current, not wire. MCLOS and direct TOW remain separate. No topattack here.

Raw reference spiral pitch input / body roll rates (degrees/s): Malyutka 10 /3060, Kobra15/2100, Konkurs30/2100, TOW0/0. These are NOT direct lateral displacement or yaw-angle amplitudes. Raw reference disturbance scalar pairs: TOW65/65 with timescale1.5; Malyutka8/8 timescale2; Kobra40/40 timescale2; Konkurs50/50 timescale2. Do not copy these numbers into fields with different units. A zero-mean smooth disturbance calibrated on actual trajectories is preferable to arbitrary exaggerated oscillations. Document which fields are independent gameplay approximations.

Desired new presets can use the measured speeds/turn limits/lead/hump figures, while preserving existing launch/warhead/technology mechanics. Initial ejection is not measured from these scalar fields. Exact visual/input parity still requires live comparison. Lifetime should support the stated range, but range enforcement is a separate policy; do not advertise a precise range/arming-distance gate if not actually implemented. The reference arming distances are TOW65m and others70m; warhead arming changes are outside this initial flight-only scope unless implemented and independently validated.

The coordinator will request a calibration delta if this file arrived after your first delivery. Complete the current implementation first, then read this when asked; do not invent host results.
