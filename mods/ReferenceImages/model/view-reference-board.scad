// Preview uses the exact exported STLs. Metres, X width, Y depth, Z height.
// Set comparison to 0, 0.1 or 6 for the nominal part or scale comparison.
comparison = 0;
show_bounds = true;
module bounds(lo,hi,t=0.003) {
 for(i=[0:2],a=[0:1],b=[0:1]) {
  j=(i+1)%3;
  c=[for(n=[0:2]) n==i ? lo[n] : n==j ? (a ? hi[n] : lo[n]) : (b ? hi[n] : lo[n])];
  translate(c-[t,t,t]/2) cube([for(n=[0:2]) n==i ? hi[n]-lo[n]+t : t]);
 }
}
module board(s=1) {
 scale([s,s,s]) {
  color([0.19,0.22,0.22]) import("reference-board-frame.stl");
  color([0.72,0.75,0.71]) import("reference-board-image.stl");
  if(show_bounds) color([1,0.85,0]) bounds([-1.02,-0.004,-1.02],[1.02,0.004,1.02],0.002);
 }
}
if(comparison == 0) board();
else {
 // tank box grounded at z=0; board beside it with bottom also at z=0
 color([0.43,0.46,0.41]) translate([-3.5,-1.75,0]) cube([7,3.5,2.5]);
 translate([comparison > 1 ? 10 : 5.2,0,1.02*comparison]) board(comparison);
}

