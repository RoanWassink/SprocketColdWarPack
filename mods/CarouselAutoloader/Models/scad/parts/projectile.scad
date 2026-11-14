// Carousel Autoloader 0.2.24 - separate projectile of two-piece ammunition, unit calibre (d = 1).
// Flat base at z = 0, nose at z = P0 = 3 (toward the gun). Zones (mirrored in AutoloaderModelGeometry):
//   A [0, 0.05]        base and driving band (geleideband)   fixed
//   B [0.05, P0-0.8]   projectile body                        stretched to the true projectile length
//   C [P0-0.8, P0]     ogive (neus/ogief) and tip             fixed
// The band has radius 0.5 with vertices on the x and y axes: bounding box = packer diameter.
P0 = 3;
fn = 12;   // 12 (fine) or 8 (medium/coarse)
rotate_extrude($fn = fn)
  polygon([[0,0],[0.46,0],[0.5,0.05],[0.455,P0-0.8],[0.30,P0-0.3],[0,P0]]);
