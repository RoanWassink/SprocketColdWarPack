// EPP casing, approval asset only. NOT embedded in the DLL.
// Normalised frame: x +/-0.5 (width), y 0..1 (motor rear -> gearbox rear), z 0..1.
// Future code can interpolate engine/gearbox end widths and heights using y.
// Render coordinates are OpenSCAD x=side, y=longitudinal, z=up.
lod=0;
module casing_raw() {
 // Two continuous folded lower rails and the seam saddle, with chamfered outside faces.
 for(s=[-1,1]) {
  translate([s<0?-500:438,0,45]) cube([62,1000,92]);
  translate([s<0?-485:460,0,130]) cube([25,1000,46]);
  // Upper shoulders tie the motor rear into the transmission case.
  hull() {
   translate([s<0?-500:452,0,120]) cube([48,45,820]);
   translate([s<0?-465:417,300,120]) cube([48,45,635]);
  }
 }
 // Crown over the seam, with a bevel into the smaller gearbox. Open below for the machinery.
 hull() {
  translate([-474,0,928]) cube([948,45,72]);
  translate([-434,300,746]) cube([868,45,55]);
 }
 // Broad mounting crossmember joins both rails; no floating decorative pieces.
 translate([-455,875,45]) cube([910,125,47]);
 if(lod==0) {
  // Raised seams, six service cover fasteners and side rail bolt pads.
  for(y=[30,285]) translate([-430,y,805-y*.22]) rotate([0,90,0]) cylinder(r=8,h=860,$fn=12);
  for(s=[-1,1])for(y=[90,500,850]) {
   translate([s<0?-500:438,y,116]) cube([62,55,75]);
   translate([s*468,y+27,185]) cylinder(r=18,h=18,$fn=6);
  }
  for(x=[-340,0,340])for(y=[70,230])
   translate([x,y,972-y*.51]) cylinder(r=17,h=16,$fn=6);
 }
}
module casing() {scale([.001,.001,.001]) casing_raw();}
casing();
