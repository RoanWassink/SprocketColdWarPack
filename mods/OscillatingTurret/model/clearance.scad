// Heavy Trunnion Mount - clearance test over the elevation range, with the pinion turning along.
// e = elevation (deg); the moving group turns e about X, the pinion turns -PIN_RATIO * e about its axis.
// pair = "static-moving": static group INTERSECT moving group. Left out by design: the journals
//        inside the bushes (r <= R_PIN + 0.6).
// pair = "pinion-moving": turned pinion INTERSECT moving group (the sector teeth must keep meshing).
// pair = "pinion-static": turned pinion INTERSECT static group. Left out by design: the shaft in its
//        bores (bracket and gearbox cover, r <= 18.6 around the pinion axis).
// An empty result ("top level object is empty") means no collision at this elevation.
include <heavy-trunnion-lib.scad>
e = 0;
pair = "static-moving";

module moving_at(e) difference() {
  rotate([e, 0, 0]) group_moving("all");
  for (s = [-1, 1]) translate([s > 0 ? X_BRG - 1 : -HALF - 1, 0, 0]) xcyl(R_PIN + 0.6, 0, HALF - X_BRG + 2);
}
module pinion_at(e) translate(P_PIN3) rotate([-PIN_RATIO * e, 0, 0]) pinion_unit();

if (pair == "static-moving") intersection() { group_static("all"); moving_at(e); }
if (pair == "pinion-moving") intersection() { pinion_at(e); rotate([e, 0, 0]) group_moving("all"); }
if (pair == "pinion-static") intersection() {
  difference() { group_static("all"); xcyl(18.6, -241, -149, 24, P_PIN); }
  pinion_at(e);
}
