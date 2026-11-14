// Heavy Trunnion Mount - MOVING group (on the trunnions; tilts about X with elevation).
// Export: openscad.com -o heavy-trunnion-moving.stl heavy-trunnion-moving.scad
// Per material: -D m=1 (cast) | 2 (steel). Units mm, see heavy-trunnion-lib.scad.
include <heavy-trunnion-lib.scad>
m = 0;
group_moving(["all", "cast", "steel"][m]);
