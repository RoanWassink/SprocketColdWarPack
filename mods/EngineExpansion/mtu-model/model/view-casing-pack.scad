// Approval mockup. Existing exact exported MTU/Renk meshes plus proposed casing STL.
// Intentional separate approval asset; no runtime integration or collision change.
N=12;d=3.97;twin=false;small=false;lod=0;show_bounds=true;
k=pow(d,1/3);n=N/2;rear=(n*60+245)*k*.9;
module part(name){import(str("parts/",name,"-lod",lod,".stl"),convexity=10);}
module engine(){scale([k,k*.9,k])translate([0,-n*60,0]){
 part("start");for(i=[0:n-1])translate([0,i*120,0])part("pair");
 translate([0,n*120,0]){part("output");part("turbo");}
}}
module renk(){
 part("tra-input");for(s=[-1,1]){
  for(i=[0:3])translate([s*(160+i*85),0,0])part("tra-shroud");
  translate([s*415,0,0])if(s<0)part("tra-output-l");else part("tra-output-r");
 }
}
module smallbox(){part("lon-input");for(i=[0:2])translate([0,i*85,0])part("lon-shroud");translate([0,170,0])part("lon-output");}
color([.73,.77,.78])engine();
color([.61,.65,.66])if(small)translate([0,rear+385,85])scale([.9,.9,.9])smallbox();
 else if(twin)for(s=[-1,1])translate([s*480,rear+390,60])scale([.65,.85,1.05])renk();
 else translate([0,rear+430,65])scale([1,.95,1.35])renk();
// Saddle covers the engine/gearbox seam. Rails make the whole power unit read as one block.
W=twin?1750:small?950:1200;L=small?650:1000;H=520*k;
color([.41,.47,.37])translate([0,rear-135,0])scale([W,L,H])import(str("parts/casing-lod",lod,".stl"),convexity=10);
// Retain a visible structural bridge between the Twin gearboxes (approval mockup).
if(twin)color([.41,.47,.37])translate([-300,rear+300,100])cube([600,360,360]);
module frame(lo,hi,t=2) {
 for(i=[0:2])for(u=[lo[(i+1)%3],hi[(i+1)%3]],v=[lo[(i+2)%3],hi[(i+2)%3]]) {
  p=[for(j=[0:2])j==i?lo[i]:(j==(i+1)%3?u:v)];
  translate(p-[t/2,t/2,t/2])cube([for(j=[0:2])j==i?hi[j]-lo[j]+t:t]);
 }
}
if(show_bounds)color([1,.85,0])frame([-W/2,rear-135,0],[W/2,rear-135+L,H]);
