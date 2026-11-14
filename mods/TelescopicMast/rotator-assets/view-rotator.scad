use <rotator_base.scad>
use <rotator_plate.scad>
$fn=96;
rotator_base();
translate([0,0,60]) rotator_plate();
module bounds(d,h,z) {
  color("yellow") {
    for (zz=[z,z+h]) translate([0,0,zz]) difference() { cylinder(d=d+1,h=0.6); translate([0,0,-0.1]) cylinder(d=d-1,h=0.8); }
    for (a=[0:90:270]) rotate([0,0,a]) translate([d/2,0,z]) cylinder(d=1,h=h,$fn=8);
  }
}
bounds(400,60,0);
bounds(360,20,60);
