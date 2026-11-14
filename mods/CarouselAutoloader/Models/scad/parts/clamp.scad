// Carousel Autoloader 0.2.23 - bustle cross clamp (dwarsklem) saddle on unit calibre, z width 1.
// Mirror of the cradle: the upper cusps of a top-layer cell, flush with the top of the round,
// so a clamp row lies inside the rack bounds even when the rounds fill the full height.
h = 0.34; eps = 0.025;
fn = 24;      // 24 (default) or 8 (light level for very full racks, matches the 8-sided rounds)
linear_extrude(height = 1)
  difference() {
    translate([-0.5, 0.5 - h]) square([1, h]);
    translate([0, -eps]) circle(r = 0.5, $fn = fn);
  }
