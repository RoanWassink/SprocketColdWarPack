use <box-sight.scad>
paint_mask=false;
// Yellow lines: exact bounds; cyan: objective; orange: virtual eyepiece (no physical ocular).
lo=[-0.186,0,-0.345458]; hi=[0.186,0.273,0.468883];
module line(a,b,r=0.0006) { hull() { translate(a) sphere(r,$fn=8); translate(b) sphere(r,$fn=8); } }
rotate([90,0,0]) scale(1000) {
    box_sight();
    color("yellow") {
        for(y=[lo.y,hi.y],z=[lo.z,hi.z]) line([lo.x,y,z],[hi.x,y,z]);
        for(x=[lo.x,hi.x],z=[lo.z,hi.z]) line([x,lo.y,z],[x,hi.y,z]);
        for(x=[lo.x,hi.x],y=[lo.y,hi.y]) line([x,y,lo.z],[x,y,hi.z]);
    }
    // Leaders keep the virtual optical points identifiable through the closed housing.
    color("cyan") {
        translate([0.085,0.139,0.263]) sphere(0.004,$fn=20);
        line([0.085,0.139,0.263],[0.23,0.31,0.263],0.001);
        translate([0.23,0.31,0.263]) sphere(0.005,$fn=20);
    }
    color("orange") {
        translate([0.085,0.139,-0.255]) sphere(0.004,$fn=20);
        line([0.085,0.139,-0.255],[0.23,0.31,-0.255],0.001);
        translate([0.23,0.31,-0.255]) sphere(0.005,$fn=20);
    }
}
