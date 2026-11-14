// CylinderMesh.Create(true,true): 96 segments; radii .070/.049, .085, .0775; heights 0,.07,1.18,1.23,1.25 m.
$fn=96; rotate([0,40,0]) color("#8E9296") { difference(){translate([0,0,.07]) cylinder(r=.07,h=1.16); translate([0,0,.069]) cylinder(r=.049,h=1.162);} cylinder(r=.085,h=.07); difference(){translate([0,0,1.18]) cylinder(r=.0775,h=.07); translate([0,0,1.179]) cylinder(r=.049,h=.072);} }

