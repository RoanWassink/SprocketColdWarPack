$fn=144;
module rotator_base() {
  union() {
    difference() {
      union() {
        cylinder(d=400,h=57);
        translate([0,0,57]) cylinder(d1=400,d2=394,h=3);
      }
      translate([0,0,58]) difference() { cylinder(d=366,h=3); cylinder(d=358,h=3); }
      for (a=[0:30:330]) rotate([0,0,a]) translate([185,0,56]) cylinder(d=16,h=5,$fn=8);
    }
    for (a=[0:30:330]) rotate([0,0,a]) translate([185,0,56]) cylinder(d=14,h=4,$fn=8);
  }
}
rotator_base();


