// Unit envelope throat, inlet at y=0, outlet offset toward +z.
module outer(){hull(){translate([0,.08,0]) cube([.72,.16,.48],center=true); translate([0,.54,.20]) cube([.62,.18,.42],center=true);}}
module inner(){hull(){translate([0,.07,0]) cube([.59,.20,.35],center=true); translate([0,.55,.20]) cube([.49,.22,.29],center=true);}}
module chute(){difference(){union(){outer(); translate([0,.035,0]) cube([.9,.07,.66],center=true);} inner();}}
chute();
