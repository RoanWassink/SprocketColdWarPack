Expanded Ballistics 0.22.46 - custom wood textures
Place your own files here; no example PNG is installed. Restart Sprocket to rescan.
Required: albedo.png. Optional: normal.png, roughness.png, ao.png.
Seamlessly tileable, square 1024 x 1024 or 2048 x 2048 PNG; maximum 32 MiB per map.
Photograph/generate straight from above with flat, diffuse light: no perspective,
shadows, highlights, baked directional lighting, borders, text, fittings or seams.
Albedo: sRGB base colour (RGBA for glass dirt/scratches; alpha 0 = clear).
Normal: linear RGB tangent-space, OpenGL Y+ by default; DirectX Y- supported.
Use normalConvention: "DirectX" to invert green/Y; keep X and Z unchanged.
Roughness: linear grayscale, white = rough, black = smooth.
AO: linear grayscale, white = unoccluded. Missing optional maps are allowed.
Data-map dimensions may differ; HDRP mask resamples AO to roughness size.
Any present broken map or missing albedo selects the original native look,
with one warning per material. useCustomTexture off disables custom loading.
Config: BepInEx/config/sprocket.expandedballistics.material-looks.json, material section wood.
Keys: useCustomTexture (auto/on/off; auto and on both require a valid albedo),
tileSizeM (0.01..100 metres), normalStrength (0..8), tint ([R,G,B,A], each 0..1),
smoothness (0..1, used without roughness), normalConvention (OpenGL/DirectX).
Default tile: wood/glass 1 m; rubber/fabric 0.5 m. Glass remains transparent;
its alpha map controls the dirt/scratch overlay, multiplied by tint alpha.
Optional maps cannot enable a custom look without albedo. No hot reload/watchers.
The installer and Restore never write, overwrite or delete user PNGs.
