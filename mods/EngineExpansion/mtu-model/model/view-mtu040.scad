// Exact STL engine assembly as M2+ places it. Open in OpenSCAD, change N/d/lod/collision/cfg.
N=12; d=3.97; SizeFactor=1; LengthFactor=.9; HeightFactor=1; TurboLayout="Compact";
lod=0; collision=false; show_bounds=true;
k=pow(d,1/3); n=N/2;
module frame(lo,hi,t=2) {
 for(i=[0:2]) for(u=[lo[(i+1)%3],hi[(i+1)%3]],v=[lo[(i+2)%3],hi[(i+2)%3]]) {
  p=[for(j=[0:2]) j==i?lo[i]:(j==(i+1)%3?u:v)];
  translate(p-[t/2,t/2,t/2]) cube([for(j=[0:2]) j==i?hi[j]-lo[j]+t:t]);
 }
}

module part(name) {import(str("parts/",name,collision?"-hull.stl":str("-lod",lod,".stl")),convexity=10);}
color([.76,.79,.81]) scale([k*SizeFactor,k*SizeFactor*LengthFactor,k*SizeFactor*HeightFactor]) translate([0,-n*60,0]) {
 part("start");for(i=[0:n-1])translate([0,i*120,0])part("pair");
 translate([0,n*120,0]){part("output");part(TurboLayout=="Compact"?"turbo":"turbo-outboard");}
}
if(show_bounds) color([1,.85,0]) scale([k*SizeFactor,k*SizeFactor*LengthFactor,k*SizeFactor*HeightFactor])
 frame([TurboLayout=="Compact"?-350:-625,-n*60-160,0],[TurboLayout=="Compact"?350:625,n*60+245,620]);
