// Approved native frame + backing, rotated XY -> YZ in Unity.
// Display axes: (Unity x, -Unity z, Unity y). Source assets are read-only.
scale_factor=1;
image_aspect=1.49812734;
depth_scale=scale_factor;
width_scale=scale_factor;
height_scale=scale_factor;
gap=0.003;
halfdepth=0.004;
fit_width=min(2*width_scale,2*height_scale*image_aspect);
fit_height=fit_width/image_aspect;
module board(){rotate([0,0,90]){
 import("../model/reference-board-frame.stl");
 import("../model/reference-board-image.stl");
}}
color([0.12,0.13,0.12]) scale([depth_scale,width_scale,height_scale])board();
// Exactly the two picture planes; exposed backing supplies letterboxing.
for(s=[-1,1])color([0.75,0.8,0.72])
 polyhedron(points=[[s*(halfdepth*depth_scale+gap),-fit_width/2,-fit_height/2],
 [s*(halfdepth*depth_scale+gap),fit_width/2,-fit_height/2],
 [s*(halfdepth*depth_scale+gap),fit_width/2,fit_height/2],
 [s*(halfdepth*depth_scale+gap),-fit_width/2,fit_height/2]],faces=s==1?[[0,1,2],[0,2,3]]:[[0,2,1],[0,3,2]]);
show_bounds=true;
if(show_bounds)color("yellow"){
 for(y=[-1,1])translate([0,y*1.02*width_scale,0])cube([.0004,max(.0004,width_scale*.0005),2.04*height_scale],center=true);
 for(z=[-1,1])translate([0,0,z*1.02*height_scale])cube([.0004,2.04*width_scale,max(.0004,height_scale*.0005)],center=true);
}
