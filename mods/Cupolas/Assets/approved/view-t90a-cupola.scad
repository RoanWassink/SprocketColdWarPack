door_angle=0;
module bounds(lo,hi) {
 color("yellow") {
  for(x=[lo[0],hi[0]],y=[lo[1],hi[1]]) translate([x,y,lo[2]]) cylinder(h=hi[2]-lo[2],r=1,$fn=8);
  for(x=[lo[0],hi[0]],z=[lo[2],hi[2]]) translate([x,lo[1],z]) rotate([-90,0,0]) cylinder(h=hi[1]-lo[1],r=1,$fn=8);
  for(y=[lo[1],hi[1]],z=[lo[2],hi[2]]) translate([lo[0],y,z]) rotate([0,90,0]) cylinder(h=hi[0]-lo[0],r=1,$fn=8);
 }
}
color("#8E9296") import("t90a_cupola_base.stl");
bounds([-335,-330,-25],[335,440,245]);
translate([0,-270,240]) rotate([door_angle,0,0]) {
 color("#8E9296") import("t90a_cupola_door.stl");
 bounds([-265,-16,0],[265,535,89.2855072]);
}





