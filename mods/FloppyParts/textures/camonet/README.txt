Camo Net texture override folder.

By default, Sprocket Floppy Parts generates a seamless procedural camo net with alpha cutouts (25-40% open holes).
To override it, place PNG files (64..4096 px, <= 32 MiB) here:
- albedo.png (required for override; alpha channel drives HDRP alpha clipping)
- normal.png (optional, tangent-space OpenGL Y+ unless settings.json specifies "DirectX")
- roughness.png (optional, grayscale)
- ao.png (optional, grayscale)
- settings.json (optional, e.g. {"normalConvention":"OpenGL","normalStrength":1.0,"smoothness":0.08})
