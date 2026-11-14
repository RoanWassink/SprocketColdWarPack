// Heavy Trunnion Mount - viewer. Double-click to open in OpenSCAD (F5 = preview).
// Shows exactly the exported game meshes (materials\*.stl = the three group STLs split per material;
// the pinion is the third mesh and turns along with the elevation).
// Axes: X = trunnion axis, Y = forward (gun), Z = up. Units mm, origin = centre of the trunnion axis.
// Yellow lines = bounds of the part at elevation 0 (static + moving group).
// show_native = true: yellow lines = native trunnions at trunnionsScale 1.5 (on the axis) and the
// native laying drive at scale 1 (placed beside the part, at axis height; its in-game position differs).
elevation = 0;          // degrees about X; + = gun up. Try 20 and -10.
show_native = false;
show_bounds = true;
labels = false;         // text labels for the native boxes (front view)
line = 3;               // thickness of the yellow lines (mm)

CAST = [96, 101, 97] / 255; STEEL = [176, 181, 186] / 255; BRONZE = [190, 128, 52] / 255;
B_MIN = [-357.5, -317, -225]; B_MAX = [357.5, 125, 98];   // checked by stlcheck.py

P_PIN3 = [0, -131.0, -110.0];   // pinion axis (y, z); pinion turns -3.75 x elevation, as in the game
color(CAST) import("materials/static-cast.stl");
color(STEEL) import("materials/static-steel.stl");
color(BRONZE) import("materials/static-bronze.stl");
color(STEEL) translate(P_PIN3) rotate([-3.75 * elevation, 0, 0]) import("materials/pinion-steel.stl");
rotate([elevation, 0, 0]) {
  color(CAST) import("materials/moving-cast.stl");
  color(STEEL) import("materials/moving-steel.stl");
}

// 12 thin edges of the box lo..hi
module frame(lo, hi, t = line)
  for (i = [0:2], a = [0, 1], b = [0, 1]) {
    j = (i + 1) % 3;
    c = [for (n = [0:2]) n == i ? lo[n] : n == j ? (a ? hi[n] : lo[n]) : (b ? hi[n] : lo[n])];
    translate(c - [t, t, t] / 2) cube([for (n = [0:2]) n == i ? hi[n] - lo[n] + t : t]);
  }
color([1, 0.85, 0]) {
  if (show_bounds && !show_native) frame(B_MIN, B_MAX);
  if (show_native) {
    frame([-357.4, -55.2, -55.3], [357.4, 55.2, 55.3]);            // native trunnions x1.5
    frame([420, -100, -100], [684.8, 100, 100]);                    // native laying drive x1
    if (labels) {
      translate([0, 0, 135]) rotate([90, 0, 180]) linear_extrude(1) text("native trunnions x1.5", size = 22, halign = "center");
      translate([552.4, 0, 125]) rotate([90, 0, 180]) linear_extrude(1) text("native laying drive", size = 22, halign = "center");
    }
  }
}
