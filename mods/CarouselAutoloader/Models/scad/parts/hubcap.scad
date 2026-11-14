// Carousel Autoloader 0.2.23 - drum end-plate hub (naaf) on unit radius; z from 0 (inside) to 1 (outer face).
// The hub disc fills the plate centre up to z = 0.7 (flush with the holed plate), six bolt heads and the
// axle nut stand proud up to z = 1. The C# geometry scales x/y by the hub radius and z by the plate thickness.
cylinder(r = 1, h = 0.7, $fn = 24);
for (i = [0:5]) rotate([0, 0, 30 + 60 * i]) translate([0.68, 0, 0.7]) cylinder(r = 0.1, h = 0.3, $fn = 6);
translate([0, 0, 0.7]) cylinder(r = 0.36, h = 0.3, $fn = 6);
