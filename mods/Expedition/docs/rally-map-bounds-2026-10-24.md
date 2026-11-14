# Rally targets and playable map bounds — 24 October 2026

User reports aimless driving, slow contact and a possible invisible shooter outside the map. The invisible-shooter explanation is unconfirmed. The observed log does prove repeated recovery waypoints near the same spawn, and the previous placement checked terrain/slope/obstacles without checking the playable WorldBounds rectangle.

Coordinator boundary change:
- Read Sprocket.Gameplay.WorldBounds in the active battle scene. Its centre/size/colliders signature was checked against installed interop using a local metadata reader. No game assembly is distributed.
- Accept a rectangle only when all selected authored friendly spawn markers fit it; transform world positions to local coordinates and retain a safety margin under scale/rotation. Missing/invalid native dimensions cannot validate newly generated positions.
- Relocated enemy markers need at least 32 m clearance inside the native rectangle. Native ground/slope/obstacle/spacing/cover checks still apply. The 350–500 m requested initial range is unchanged.
- Generated AI endpoints need at least 16 m clearance. These are endpoint checks; they do not prove a full native path remains inside, renderer visibility, or projectile attribution.
- No new map limits, terrain modifications or runtime tank teleports. Bounds cache is released with the scene spawn session. If native bounds cannot be identified, the custom spawn pass retains the authored native layout and the experimental team AI yields to native ordering rather than suppressing orders with no valid destination.

Managed AreaChecks exercise offset/negative coordinates, edge clearance, separate spawn/AI margins and invalid/unknown rectangle rejection. Tests use synthetic dimensions and do not establish the actual dimensions of Fields/Defence/Dunes. Scene-capture acceptance and rendering must be checked by the user.

Useful next-play log evidence: Expedition <map> native WorldBounds (local center/size/transform), front-arc APPLIED bounds status, team spawn rally targets, task/goal in recovery diagnostics. If the native bounds component is not present, the warning is explicit; do not silently treat terrain as a playable area.
