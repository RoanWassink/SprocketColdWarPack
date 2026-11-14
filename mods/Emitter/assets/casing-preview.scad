// Source models are imported unchanged. Units are millimetres.
charge="charge8.stl";
projectile="projectile8.stl";
module label(t,x,y,s=21) { color("#E8E9E5") translate([x,y,2]) linear_extrude(0.1) text(t,size=s,font="Arial:style=Bold"); }
module item(d,y,t,file,c) {
 color(c) translate([0,y,0]) rotate([0,90,0]) scale(d) import(file);
 label(t, -350,y-8,14);
}
color("#30332F") translate([-390,-495,-8]) cube([1200,1040,7]);
item(7.62,450,"CHARGE  7.62 x 46.33 mm",charge,"#CCA753");
item(30,300,"CHARGE  30 x 182.40 mm",charge,"#CCA753");
item(76,150,"CHARGE  76 x 462.08 mm",charge,"#CCA753");
item(105,0,"CHARGE  105 x 638.40 mm",charge,"#CCA753");
item(120,-150,"CHARGE  120 x 729.60 mm",charge,"#CCA753");
item(76,-300,"PROJECTILE  76 x 228 mm",projectile,"#8E9296");
color("white") translate([0,-440,1]) cube([100,5,1]);
label("100 mm", 25,-428,18);

