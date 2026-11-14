// CylinderMesh.Create(false,true): 96 segments; radii .045 and .085; heights -1.23,.03,0,.07 m.
$fn=96; rotate([0,40,0]) color("#8E9296") { translate([0,0,-1.23]) cylinder(r=.045,h=1.26); cylinder(r=.085,h=.07); }

