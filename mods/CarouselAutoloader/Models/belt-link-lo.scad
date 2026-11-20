// Low-poly M28-style clip, dimensions and pitch shared with belt-link.scad.
pitch=1.15; inner=0.49; outer=0.545;
// Five pairs of polygon vertices: open at +y, with exact extrema of the reference.
outer_pts=[[-.20,.506421],[-outer,0],[-.385373,-.385373],[0,-outer],[.385373,-.385373],[outer,0],[.20,.506421]];
inner_pts=[[-.18,.455],[-inner,0],[-.346482,-.346482],[0,-inner],[.346482,-.346482],[inner,0],[.18,.455]];
module sleeve(z,len,x=0){translate([x,0,z]) linear_extrude(height=len) polygon(concat(outer_pts,[for(i=[6:-1:0]) inner_pts[i]]));}
module belt_link_lo(){union(){sleeve(1.15,.30);sleeve(2.05,.30);sleeve(1.60,.30,pitch);translate([0,-.54,1.60]) cube([pitch,.05,.30]);translate([-.05,-.54,1.15]) cube([.10,.05,1.20]);}}
belt_link_lo();
