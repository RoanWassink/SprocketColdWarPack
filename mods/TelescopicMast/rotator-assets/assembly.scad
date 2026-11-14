use <rotator_base.scad>
use <rotator_plate.scad>
rotator_base();
translate([0,0,60]) rotator_plate();
