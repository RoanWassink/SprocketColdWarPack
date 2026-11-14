// Heavy Trunnion Mount - STATIC group (mantlet base; does not tilt).
// Export: openscad.com -o heavy-trunnion-static.stl heavy-trunnion-static.scad
// Per material: -D m=1 (cast) | 2 (steel) | 3 (bronze). Units mm, see heavy-trunnion-lib.scad.
include <heavy-trunnion-lib.scad>
m = 0;
group_static(["all", "cast", "steel", "bronze"][m]);
