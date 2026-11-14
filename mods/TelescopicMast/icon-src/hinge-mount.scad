// HingeMesh.CreateMount / HingeMath: width=.10, depth=.10, thickness=.01, pin radius=.01 m; 24 segments. Unity Y is vertical.
$fn=24; color("#8E9296") { translate([-.05,0,0]) rotate([0,90,0]) cylinder(r=.01,h=.10); translate([-.05,.01,-.01]) cube([.10,.09,.01]); }
