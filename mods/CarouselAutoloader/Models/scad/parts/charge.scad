// Carousel Autoloader 0.2.24 - separate propellant charge of two-piece ammunition: a case without
// projectile (rim, extraction groove, body) closed by a combustible plug. Unit calibre (d = 1),
// rim at z = 0, plug at z = C0 + 0.08 (toward the gun). Zones (mirrored in AutoloaderModelGeometry):
//   A [0, 0.065]        rim (hulsrand) and groove (groef)  fixed
//   B [0.065, C0]       case body                          stretched to the true charge length
//   C [C0, C0+0.08]     plug                               fixed
C0 = 6;
fn = 12;   // 12 (fine) or 8 (medium/coarse)
rotate_extrude($fn = fn)
  polygon([[0,0],[0.5,0],[0.5,0.04],[0.44,0.065],[0.475,C0],[0.38,C0+0.05],[0,C0+0.08]]);
