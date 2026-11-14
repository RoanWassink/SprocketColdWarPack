door_angle=0;
module part() {
 color("#8E9296") {
  import("t90a_cupola_base.stl");
  translate([0,-270,240]) rotate([door_angle,0,0]) import("t90a_cupola_door.stl");
 }
}
part();
