// Import the actual final meshes, including the optimized lid.
color("#8E9296") rotate([0,0,180]) {
 import("t90a_cupola_base.stl");
 translate([0,-270,240]) import("t90a_cupola_door.stl");
}
