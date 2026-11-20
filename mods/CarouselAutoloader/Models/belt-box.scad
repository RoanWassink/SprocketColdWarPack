// Unit rack: x width, y height, z length; bottom centre at origin.
wall=0.02;
module belt_box(){
 union(){
  // Closed shell, inset long sides make room for shallow pressed ribs.
  difference(){translate([-.48,0,-.5]) cube([.96,.97,1]); translate([-.48+wall,wall,-.5+wall]) cube([.96-2*wall,.97-2*wall,1-2*wall]);}
  // Lid lip and its inset seam.
  translate([-.5,.965,-.5]) cube([1,.035,1]);
  for(s=[-1,1], y=[.22,.55]) translate([s*.49,y,0]) cube([.02,.065,.84],center=true);
  // Shallow front latch and identification plate.
  translate([-.5,.78,-.18]) cube([.025,.13,.16]);
  translate([-.5,.69,.08]) cube([.025,.13,.23]);
 }
}
belt_box();
