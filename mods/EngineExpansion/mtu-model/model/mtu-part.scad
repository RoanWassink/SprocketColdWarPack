// One exported game mesh per run: openscad.com -o parts/<part>-lod<lod>.stl -D part="pair" -D lod=0 mtu-part.scad
include <mtu-lib.scad>
part = "pair";

if (part == "start") start_segment();
if (part == "pair") pair_segment();
if (part == "output") output_segment();
if (part == "turbo") turbo_set(0);
if (part == "turbo-outboard") turbo_set(1);
if (part == "tra-input") tra_input();
if (part == "tra-shroud") tra_shroud();
if (part == "tra-output-l") tra_output_left();
if (part == "tra-output-r") tra_output_right();
if (part == "lon-input") lon_input();
if (part == "lon-shroud") lon_shroud();
if (part == "lon-output") lon_output();
if (part == "twin-bridge") twin_bridge();
