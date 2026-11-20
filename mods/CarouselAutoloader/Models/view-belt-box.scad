use <belt-box.scad>
color("#8E9296") belt_box();
color("yellow") for(y=[0,1],z=[-.5,.5]) translate([-.5,y,z]) rotate([0,90,0]) cylinder(h=1,r=.002,$fn=8);
color("yellow") for(x=[-.5,.5],z=[-.5,.5]) translate([x,0,z]) rotate([-90,0,0]) cylinder(h=1,r=.002,$fn=8);
color("yellow") for(x=[-.5,.5],y=[0,1]) translate([x,y,-.5]) cylinder(h=1,r=.002,$fn=8);
