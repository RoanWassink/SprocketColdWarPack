use <belt-link-lo.scad>
color("#8E9296") belt_link_lo();
lo=[-.545,-.545,1.15]; hi=[1.695,.506421,2.35]; r=.002;
color("yellow") {
 for(y=[lo[1],hi[1]],z=[lo[2],hi[2]]) translate([lo[0],y,z]) rotate([0,90,0]) cylinder(h=hi[0]-lo[0],r=r,$fn=8);
 for(x=[lo[0],hi[0]],z=[lo[2],hi[2]]) translate([x,lo[1],z]) rotate([-90,0,0]) cylinder(h=hi[1]-lo[1],r=r,$fn=8);
 for(x=[lo[0],hi[0]],y=[lo[1],hi[1]]) translate([x,y,lo[2]) cylinder(h=hi[2]-lo[2],r=r,$fn=8);
}
