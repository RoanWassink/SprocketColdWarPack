// Heavy Trunnion Mount - PINION group (third mesh; static gear train, turns about its own axis).
// Export: openscad.com -o heavy-trunnion-pinion.stl heavy-trunnion-pinion.scad
// Origin = pinion axis (model y -131, z -110 mm), axis parallel to X. Material: steel only.
// In the game the mod turns it by -3.75 x the elevation angle (sector R135 / pinion R36).
include <heavy-trunnion-lib.scad>
m = 0;
group_pinion(["all", "placed"][m]);
