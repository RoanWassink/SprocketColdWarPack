$fn=24;
ring_d=660; upper_d=600; opening_d=500; ring_height=240; rim_height=25;
door_d=530; door_thickness=40; head_w=130; head_h=140; viewer_count=7; door_angle=0;
H=[0,-270,240];
module viewer() {
 union() {
  difference() {
   hull() {
    translate([-50,275,135]) cube([100,60,52]);
    translate([-50,275,202]) cube([100,48,1]);
    translate([-55,280,140]) cube([110,50,47]);
   }
   translate([-35,327,150]) cube([70,20,30]);
  }
  translate([-53,322,192]) cube([106,17,5]);
 }
}
module sight_head() {
 union() {
  difference() {
   polyhedron(points=[[-175,280,95],[-45,280,95],[-45,440,95],[-175,440,95],[-175,280,235],[-45,280,235],[-45,402.5,235],[-175,402.5,235]],
    faces=[[0,3,2,1],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]]);
   translate([-110,409.4,161.9]) rotate([-75,0,0]) cylinder(h=25,d=80);
  }
  // Raised collar intersects the sloping front and frames the recessed glass.
  translate([-110,418.8,164.5]) rotate([-75,0,0]) difference() {
   cylinder(h=4,d=98); translate([0,0,-1]) cylinder(h=6,d=80);
  }
  for(a=[0:60:300])
   translate([-110+57*cos(a),421-14.75*sin(a),165+55*sin(a)]) rotate([-75,0,0]) cylinder(h=5,d=14,$fn=6);
  hull() {
   translate([-180,385,232]) cube([140,31,9]);
   translate([-175,380,235]) cube([130,40,10]);
  }
 }
}
module lift_eye() {
 rotate([90,0,0]) rotate_extrude($fn=12) translate([24,0]) circle(r=4,$fn=4);
}
module base() {
 union() {
  // One revolution profile rounds the shoulder, the step and the top rim.
  rotate_extrude($fn=48) polygon(points=[
   [250,-25],[322,-25],[330,-17],[330,97],[322,105],
   [306,105],[306,109],[300,111],
   [300,207],[292,215],[275,215],
   [275,232],[267,240],[258,240],[250,232]
  ]);
  sight_head();
  for(i=[0:viewer_count-1]) {
   a=-135+i*270/(viewer_count-1);
   rotate([0,0,-a]) viewer();
  }
  translate([-65,-300,190]) cube([130,55,50]);
  for(s=[-1,1]) translate([s*225,-198,207]) lift_eye();
 }
}
module band(y) {
 // Rounded-end strap follows and intersects the crown.
 hull() {
  translate([-75,y,48]) cylinder(h=15,r=20,$fn=8);
  translate([75,y,48]) cylinder(h=15,r=20,$fn=8);
 }
 for(x=[-75,-45,-15,15,45,75]) translate([x,y,62.5]) cylinder(h=6,d=12,$fn=8);
}
module door() {
 union() {
  translate([0,270,0]) union() {
   rotate_extrude($fn=48) polygon(points=[[0,0],[263,0],[265,8],[265,32],[257,40],[180,50],[0,55]]);
   for(y=[-150,-70,70,150]) band(y);
   // Round bar forms a U with welded feet.
   for(x=[-45,45]) translate([x,0,48]) cylinder(h=33,d=20,$fn=8);
   translate([-45,0,80]) rotate([0,90,0]) cylinder(h=90,d=20,$fn=8);
  }
  translate([-35,-12,0]) cube([70,50,22]);
  // Two hinge eyes and axle, all overlapping the lip.
  for(x=[-28,18]) translate([x,-4,12]) rotate([0,90,0]) cylinder(h=10,d=24,$fn=8);
  translate([-36,-4,12]) rotate([0,90,0]) cylinder(h=72,d=10,$fn=8);
 }
}
module assembled(door_angle=door_angle) { base(); translate(H) rotate([door_angle,0,0]) door(); }
assembled(door_angle);



