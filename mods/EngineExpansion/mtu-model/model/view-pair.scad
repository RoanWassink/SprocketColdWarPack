// Close-up of the cylinder-pair segment: three exported pair meshes in a row (seams visible),
// the middle one highlighted, with the segment frame (x +-350, y 0..120, z 0..620) as thin yellow lines.
lod = 0;
SEG = 120;
for (i = [-1 : 1]) translate([0, i * SEG, 0])
  color(i == 0 ? [0.80, 0.82, 0.84] : [0.62, 0.64, 0.66]) import(str("parts/pair-lod", lod, ".stl"), convexity = 6);
color([1, 0.83, 0]) for (x = [-350, 350], z = [0, 620]) translate([x - 1.5, 0, z - 1.5]) cube([3, SEG, 3]);
color([1, 0.83, 0]) for (y = [0, SEG], z = [0, 620]) translate([-350, y - 1.5, z - 1.5]) cube([700, 3, 3]);
color([1, 0.83, 0]) for (x = [-350, 350], y = [0, SEG]) translate([x - 1.5, y - 1.5, 0]) cube([3, 3, 620]);
