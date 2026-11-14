// Exact game geometry, in metres. Display in millimetres for convenient orbiting.
paint_mask = false;
module box_sight() {
    color(paint_mask ? [1,0.05,0.8] : [0.23,0.26,0.18]) import("stl/paint.stl", convexity=20);
    color(paint_mask ? [0.45,0.45,0.45] : [0.18,0.18,0.19]) import("stl/steel.stl", convexity=20);
    color(paint_mask ? [0.45,0.45,0.45] : [0.05,0.05,0.05]) import("stl/dark.stl", convexity=20);
    color(paint_mask ? [0.45,0.45,0.45] : [0.06,0.10,0.12]) import("stl/lens.stl", convexity=20);
}
rotate([90,0,0]) scale(1000) box_sight();
