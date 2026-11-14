// Carousel Autoloader 0.2.23 - bustle cradle (goot) on unit calibre, extruded along z over length 1.
// One cradle per round cell: the material lies only in the two lower cusps of the cell
// [-0.5,0.5] x [-0.5,-0.5+h], outside the round, so cradles of touching rounds and of the
// layer below never intersect a true-size round. The circle is lifted by eps, which leaves
// a thin floor under the round (eps intrudes only into the tangent point of the layer below).
h = 0.30; eps = 0.025;
fn = 24;      // 24 (default) or 8 (light level for very full racks, matches the 8-sided rounds)
linear_extrude(height = 1)
  difference() {
    translate([-0.5, -0.5]) square([1, h]);
    translate([0, eps]) circle(r = 0.5, $fn = fn);
  }
