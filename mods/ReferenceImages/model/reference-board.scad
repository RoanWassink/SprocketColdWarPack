// Metres; X horizontal, Z up, Y depth; origin at centre.
// Select exported part with -D part=0 (frame) or -D part=1 (image).
part = 0;
module frame() difference() {
  cube([2.04,0.008,2.04],center=true);
  cube([2,0.01,2],center=true);
}
module image_surface() cube([2,0.008,2],center=true);
if (part == 0) frame();
if (part == 1) image_surface();


