Rubber texture override folder.

By default, Sprocket Floppy Parts generates a seamless procedural vulcanized rubber grain texture.
To override it, place PNG files (64..4096 px, <= 32 MiB) here:
- albedo.png (required for override)
- normal.png (optional, tangent-space OpenGL Y+ unless settings.json specifies "DirectX")
- roughness.png (optional, grayscale)
- ao.png (optional, grayscale)
- settings.json (optional, e.g. {"normalConvention":"OpenGL","normalStrength":1.0,"smoothness":0.14})
