// HingeMesh.CreateLeaf / HingeMath: knuckle r=.025, pin r=.02, width=.50; depth=.40, thickness=.02 m; 48 segments. Unity Y is vertical.
$fn=48; color("#8E9296") { translate([-.25,0,0]) rotate([0,90,0]) difference(){cylinder(r=.025,h=.5); translate([0,0,-.001]) cylinder(r=.02,h=.502);} translate([-.25,.015,-.025]) cube([.5,.385,.02]); }
