The approved Emitter arrow (assets/emitter_arrow.scad and .stl), the icon (assets/emitter-icon.png) and the contact sheet are the Emitter's own assets.
Arrow source: OpenSCAD, millimetres; the embedded arrays (ArrowMeshData.cs) convert mm to m. The icon remains the approved image unchanged.
Charge8 and Projectile8 (assets/charge8.stl, assets/projectile8.stl) and the embedded mesh data (CasingMeshData.cs) are the Carousel mod's casing and projectile shapes, copied unchanged.
Hashes and every triangle are compared by tools/generate-mesh.py. No runtime Carousel dependency exists.
Stock geometry, if already loaded, is copied at runtime only; no native binary or stock mesh file is redistributed.
The Mono.Cecil copy that BepInEx already ships (BepInEx/core) is used by the offline scanner only in checks, never in the delivered files.
