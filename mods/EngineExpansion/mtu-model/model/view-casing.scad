// Exact exported approval STL; change lod, spin with the mouse.
lod=0;show_bounds=true;
color([.48,.53,.44])scale([1000,1000,1000])import(str("parts/casing-lod",lod,".stl"),convexity=10);
module frame(lo,hi,t=2) {
 for(i=[0:2])for(u=[lo[(i+1)%3],hi[(i+1)%3]],v=[lo[(i+2)%3],hi[(i+2)%3]]) {
  p=[for(j=[0:2])j==i?lo[i]:(j==(i+1)%3?u:v)];
  translate(p-[t/2,t/2,t/2])cube([for(j=[0:2])j==i?hi[j]-lo[j]+t:t]);
 }
}
if(show_bounds)color([1,.85,0])frame([-500,0,45],[500,1000,1000]);
