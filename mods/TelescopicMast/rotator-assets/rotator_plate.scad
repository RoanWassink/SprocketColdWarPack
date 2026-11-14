$fn=144;
module rotator_plate() {
  union() {
    difference() {
      cylinder(d=360,h=20);
      // index notch through the rim at +X
      translate([166,-9,-1]) cube([20,18,22]);
    }
    translate([0,0,20]) cylinder(d=120,h=6);
    for (a=[22.5:45:337.5]) rotate([0,0,a]) translate([150,0,20]) cylinder(d=14,h=3,$fn=12);
    // raised pointer from the hub to the notch
    translate([0,0,20]) linear_extrude(height=6) polygon([[96,-16],[96,16],[162,0]]);
  }
}
rotator_plate();
