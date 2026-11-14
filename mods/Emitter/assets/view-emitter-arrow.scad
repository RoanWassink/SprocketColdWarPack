use <emitter_arrow.scad>
arrow();
color("yellow") {
 for(x=[-30,30],y=[-30,30]) translate([x,y,0]) cylinder(h=120,r=0.35,$fn=8);
 for(z=[0,120],x=[-30,30]) translate([x,-30,z]) rotate([-90,0,0]) cylinder(h=60,r=0.35,$fn=8);
 for(z=[0,120],y=[-30,30]) translate([-30,y,z]) rotate([0,90,0]) cylinder(h=60,r=0.35,$fn=8);
}
