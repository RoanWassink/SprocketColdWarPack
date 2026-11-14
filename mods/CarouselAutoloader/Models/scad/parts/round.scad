// Carousel Autoloader 0.2.23 - complete round on unit calibre (d = 1).
// Base (case rim) at z = 0, nose at z = C0 + P0. The axis is the gun axis (+z = nose = toward the gun).
// Canonical case length C0 = 6 and projectile length P0 = 3. The C# geometry stretches only
// the two plain zones (case body and projectile body) to the true case/projectile lengths, so the
// rim, groove, driving band and ogive keep their calibre proportions. Zone limits are mirrored in
// AutoloaderModelGeometry.RoundZones; a test checks the embedded data against this export.
//   A [0, 0.065]            rim (hulsrand) + extraction groove (groef)      fixed
//   B [0.065, C0]           case body                                       stretched to the charge length
//   C [C0, C0 + 0.05]       driving band (geleideband)                      fixed
//   D [C0 + 0.05, C0+P0-0.8] projectile body                                stretched to the projectile length
//   E [C0+P0-0.8, C0+P0]    ogive (neus/ogief) and tip                      fixed
// The widest rings (rim, band) have radius 0.5 exactly, with a vertex on the x and y axes, so the
// bounding box of every round is exactly the packer diameter.
C0 = 6; P0 = 3;
fn = 12;      // 12 (fine) or 8 (medium); -D fn=8 for the medium level
coarse = 0;   // 1: six-point profile for very full racks
fine_profile = [[0,0],[0.5,0],[0.5,0.04],[0.44,0.065],[0.475,C0],[0.5,C0+0.05],
                [0.455,C0+P0-0.8],[0.30,C0+P0-0.3],[0,C0+P0]];
coarse_profile = [[0,0],[0.5,0],[0.46,0.065],[0.475,C0],[0.47,C0+P0-0.8],[0,C0+P0]];
rotate_extrude($fn = fn) polygon(coarse == 1 ? coarse_profile : fine_profile);
