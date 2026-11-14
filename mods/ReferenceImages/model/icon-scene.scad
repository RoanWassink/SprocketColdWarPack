// Icon scene; board geometry is imported from production STLs.
color([0.19,0.22,0.22]) import("reference-board-frame.stl");
color([0.68,0.72,0.66]) import("reference-board-image.stl");
// Front is y=-0.004. A simple tank graphic on the image plane.
color([0.19,0.24,0.19]) translate([0,-0.0045,-0.25]) rotate([90,0,0]) linear_extrude(0.001) union() {
 polygon([[-0.78,-0.18],[0.73,-0.18],[0.63,0.08],[-0.62,0.08]]);
 polygon([[-0.40,0.09],[-0.27,0.36],[0.30,0.36],[0.42,0.09]]);
 translate([0.15,0.28]) square([0.64,0.055]);
 translate([0,-0.22]) scale([1,0.32]) circle(r=0.69,$fn=40);
}
