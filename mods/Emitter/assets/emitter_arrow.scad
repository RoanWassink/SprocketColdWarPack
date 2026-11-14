// Dimensions in millimetres; +Z is the ejection direction.
foot_size=60; corner_radius=6; foot_thickness=6;
shaft_diameter=14; shaft_top=70; head_diameter=36; height=120;
$fn=24;
module foot() { linear_extrude(height=foot_thickness) offset(r=corner_radius) square([foot_size-2*corner_radius,foot_size-2*corner_radius],center=true); }
module shaft() { translate([0,0,foot_thickness]) cylinder(d=shaft_diameter,h=shaft_top-foot_thickness); }
module head() { translate([0,0,shaft_top]) cylinder(d1=head_diameter,d2=0,h=height-shaft_top); }
module arrow() { union() { foot(); shaft(); head(); } }
if (is_undef(colored) || !colored) arrow();
else { color("#8E9296") { foot(); shaft(); } color("#DC9E33") head(); }
