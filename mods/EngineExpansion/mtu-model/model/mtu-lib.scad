// MTU powerpack: all part geometry (OpenSCAD 2021.01).
// Units mm. Engine parts at the unit size k = 1 (1 L per cylinder); the game scales them by
// k = d^(1/3) (native generator, cloned definition with cylinderDisplacement 1.0).
// Axes: x = side (Unity x), y = along the crankshaft, output at +y (Unity z), z = up (Unity y).
// Engine origin: crank-axis line at x = 0, z = 0 is the bottom of the sump; the start part is
// y -START..0, a cylinder-pair segment y 0..SEG, the output part y 0..OUT (flange face at y = OUT).
// Transmission parts use the native frames of the Sprocket meshes (MTU-MAP.md 1.5), in mm.
// lod = 0 detailed, 1 low (LOD1, shadow and collision). Colours are only for the PNGs.

SEG = 120;      // cylinder-pair pitch (MAATREGELS: 0.120 k)
START = 160;    // damper end
OUT = 245;      // flywheel housing + flange; START + OUT = 405 (0.405 k)
CRANK = 220;    // crank axis height (0.22 k)
HALF_V = 45;    // MTU V90
lod = 0;

function fn(n) = lod == 0 ? n : max(6, round(n / 2));
U = [sin(HALF_V), cos(HALF_V)];          // right bank axis (x, z)
P = [cos(HALF_V), -sin(HALF_V)];         // right bank side normal, outward-down
C = [0, CRANK];

// ---------- 2D cross-section helpers (x, z) ----------
function bp(t, w) = C + t * U + w * P;   // point on the right bank
module slab(t0, t1, w) {                 // right-bank slab between axis stations t0..t1
  polygon([bp(t0, w), bp(t1, w), bp(t1, -w), bp(t0, -w)]);
}
module both() { children(); mirror([1, 0]) children(); }

module sump2d() {
  polygon([[-140, 0], [140, 0], [152, 12], [152, 52], [162, 52], [162, 62], [-162, 62], [-162, 52], [-152, 52], [-152, 12]]);
}
module crankcase2d() {
  hull() { translate([-172, 56]) square([344, 70]); both() slab(40, 72, 94); }
}
module block2d() { both() slab(60, 202, 92); }
module head2d() { both() slab(200, 284, 101); }
module cover2d() {
  both() offset(r = 14, $fn = fn(16)) offset(delta = -14) slab(284, 322, 74);
}
module section2d() { sump2d(); crankcase2d(); block2d(); }   // the continuous part; heads are per cylinder
module heads(y0, y1) {                   // individual cylinder heads and valve covers (12 / 20 mm gaps at joints)
  along_y(y0 + 6, y1 - 6) head2d();
  along_y(y0 + 10, y1 - 10) cover2d();
}
module mounts(y0) {                      // engine mounting brackets on the crankcase skirt
  for (s = [-1, 1]) translate([s > 0 ? 160 : -236, y0, 64]) cube([76, 26, 56]);
}
module bulkhead2d() { offset(delta = 7) { crankcase2d(); block2d(); } }

// ---------- 3D helpers ----------
module along_y(y0, y1) { translate([0, y1, 0]) rotate([90, 0, 0]) linear_extrude(y1 - y0) children(); }
module cyl_y(x, z, y0, y1, r, n = 16) { translate([x, y0, z]) rotate([-90, 0, 0]) cylinder(r = r, h = y1 - y0, $fn = fn(n)); }
module cone_y(x, z, y0, y1, r0, r1, n = 16) { translate([x, y0, z]) rotate([-90, 0, 0]) cylinder(r1 = r0, r2 = r1, h = y1 - y0, $fn = fn(n)); }
module cyl_x(y, z, x0, x1, r, n = 16) { translate([x0, y, z]) rotate([0, 90, 0]) cylinder(r = r, h = x1 - x0, $fn = fn(n)); }
module cone_x(y, z, x0, x1, r0, r1, n = 16) { translate([x0, y, z]) rotate([0, 90, 0]) cylinder(r1 = r0, r2 = r1, h = x1 - x0, $fn = fn(n)); }
module rod(a, b, r, n = 10) {            // capsule-free rod between two points (hull of two discs)
  d = b - a; L = norm(d);
  translate(a) rotate([0, acos(d[2] / L), atan2(d[1], d[0])]) cylinder(r = r, h = L, $fn = fn(n));
}
module on_bank(t, side = 1) {            // place children on the bank axis, local z = bank axis
  q = bp(t, 0);
  translate([side * q[0], 0, q[1]]) rotate([0, side * HALF_V, 0]) children();
}
module on_side(t, w, side = 1) {         // place children on the bank side face, local z = outward normal
  q = bp(t, w);
  translate([side * q[0], 0, q[1]]) rotate([0, side * (90 + HALF_V), 0]) children();
}

// manifold and charge-air pipe positions
EXH = [300, 300]; EXH_R = 33;
AIR = [0, 470]; AIR_R = 42;

// ---------- cylinder-pair segment: y 0..SEG ----------
module pair_segment() {
  along_y(0, SEG) section2d();
  heads(0, SEG);
  if (lod == 0) {
    along_y(0, 5) bulkhead2d();
    along_y(SEG - 5, SEG) bulkhead2d();
  }
  for (s = [-1, 1]) {
    // exhaust manifold with slip-joint rings at both ends (whole rings at every joint)
    cyl_y(s * EXH[0], EXH[1], 0, SEG, EXH_R, 16);
    if (lod == 0) { cyl_y(s * EXH[0], EXH[1], 0, 6, EXH_R + 6, 16); cyl_y(s * EXH[0], EXH[1], SEG - 6, SEG, EXH_R + 6, 16); }
    // exhaust stub from the head side face to the manifold
    translate([0, SEG / 2, 0]) on_side(242, 101, s) cylinder(r = 22, h = lod == 0 ? 42 : 40, $fn = fn(10));
    // injector / rocker boss on the valve cover, with its hold-down nut
    if (lod == 0) translate([0, SEG / 2, 0]) on_bank(318, s) {
      cylinder(r = 21, h = 28, $fn = fn(12));
      translate([0, 0, 28]) cylinder(r = 13, h = 9, $fn = 6);
    }
    // intake runner from the charge-air pipe to the head
    cyl_x(SEG / 2, 458, s > 0 ? 20 : -112, s > 0 ? 112 : -20, 21, 10);
  }
  // charge-air pipe in the V, with joint flanges
  cyl_y(AIR[0], AIR[1], 0, SEG, AIR_R, 16);
  if (lod == 0) { cyl_y(AIR[0], AIR[1], 0, 6, AIR_R + 6, 16); cyl_y(AIR[0], AIR[1], SEG - 6, SEG, AIR_R + 6, 16); }
  // injection pump element in the V (one per pair)
  translate([-36, 12, 328]) cube([72, SEG - 24, 72]);
  if (lod == 0) translate([0, SEG / 2, 398]) cylinder(r = 16, h = 14, $fn = fn(8));
}

// ---------- start segment (damper end): y -START..0 ----------
module start_segment() {
  along_y(-40, 0) section2d();
  along_y(-40, -6) head2d();
  along_y(-40, -10) cover2d();
  mounts(-40);
  // gear-train cover following the block outline
  along_y(-70, -40) offset(delta = -6) hull() { crankcase2d(); block2d(); head2d(); }
  for (s = [-1, 1]) {
    cyl_y(s * EXH[0], EXH[1], -40, 0, EXH_R, 16);
    cone_y(s * EXH[0], EXH[1], -62, -40, 18, EXH_R, 16);       // manifold end cap
    // water pump on each bank, outlet elbow up
    cyl_y(s * 150, 360, -102, -70, 50, 16);
    if (lod == 0) translate([s * 150, -86, 360]) cylinder(r = 20, h = 78, $fn = fn(10));
  }
  cyl_y(AIR[0], AIR[1], -40, 0, AIR_R, 16);
  cone_y(AIR[0], AIR[1], -58, -40, 20, AIR_R, 16);
  // alternator in the V
  cyl_y(0, 436, -118, -70, 52, 16);
  if (lod == 0) cyl_y(0, 436, -128, -118, 38, 16);
  // torsional vibration damper on the crank axis: inertia ring, disc, hub, nut
  cyl_y(0, CRANK, -112, -70, 150, 24);
  cyl_y(0, CRANK, -132, -112, 138, 24);
  cyl_y(0, CRANK, -150, -132, 56, 16);
  if (lod == 0) cyl_y(0, CRANK, -160, -150, 32, 6);
}

// ---------- output segment (flywheel end): y 0..OUT ----------
BELL0 = 272; BELL1 = 244; FLANGE_R = 262;
module floor_cut() { intersection() { children(); translate([-2000, -2000, 0]) cube([4000, 4000, 4000]); } }
module output_segment() {
  along_y(0, 40) section2d();
  along_y(6, 40) head2d();
  along_y(10, 40) cover2d();
  mounts(8);
  along_y(40, 56) offset(delta = 6) { crankcase2d(); block2d(); }  // rear end plate
  for (s = [-1, 1]) {
    cyl_y(s * EXH[0], EXH[1], 0, 40, EXH_R, 16);
    cone_y(s * EXH[0], EXH[1], 40, 60, EXH_R, 18, 16);
  }
  cyl_y(AIR[0], AIR[1], 0, 40, AIR_R, 16);
  cone_y(AIR[0], AIR[1], 40, 56, AIR_R, 20, 16);
  floor_cut() {
    // flywheel housing (bell) with radial ribs, flange to the transmission
    cone_y(0, CRANK, 56, OUT - 30, BELL0, BELL1, 24);
    cyl_y(0, CRANK, OUT - 30, OUT, FLANGE_R, 24);
    if (lod == 0) {
      // radial ribs: 6 mm proud near the wide end of the bell, 34 mm proud at the flange
      for (a = [22.5 : 45 : 360]) translate([0, 0, CRANK]) rotate([0, a, 0])
        translate([-8, 60, 0]) cube([16, OUT - 30 - 60, BELL0 + 6]);
      for (a = [15 : 30 : 360]) translate([sin(a) * 252, OUT - 42, CRANK + cos(a) * 252])
        rotate([-90, 0, 0]) cylinder(r = 11, h = 12, $fn = 6);
    }
  }
  // starter motor (left) with solenoid, oil filter (right) on a bracket
  cyl_y(-250, 112, 62, 212, 50, 16);
  if (lod == 0) cyl_y(-250, 170, 110, 196, 24, 10);
  translate([236, 40, 88]) cylinder(r = 38, h = 112, $fn = fn(16));
  translate([168, 28, 120]) cube([50, 24, 50]);
}

// ---------- turbo set: same frame as the output part (y 0 = end of the last pair) ----------
// outboard = 0: compact (EPP, inside B = 0.70 k); 1: MB 873 style, turbos outside the block.
module turbo(c) {                        // axis along y, turbine at -y, compressor at +y
  cyl_y(c[0], c[2], c[1] - 50, c[1], 68, 20);           // turbine housing
  if (lod == 0) cone_y(c[0], c[2], c[1] - 58, c[1] - 50, 52, 68, 20);
  cyl_y(c[0], c[2], c[1], c[1] + 20, 32, 12);            // bearing housing
  cyl_y(c[0], c[2], c[1] + 20, c[1] + 64, 60, 20);       // compressor housing
  cone_y(c[0], c[2], c[1] + 64, c[1] + 92, 36, 30, 12);  // compressor inlet
}
module turbo_set(outboard = 0) {
  T = outboard == 0 ? [205, 10, 548] : [540, 20, 372];
  for (s = [-1, 1]) {
    c = [s * T[0], T[1], T[2]];
    turbo(c);
    // turbine feed from the manifold end
    rod([s * EXH[0], 34, EXH[1]], [c[0] - s * (outboard == 0 ? 20 : 60), c[1] - 25, c[2] - (outboard == 0 ? 50 : 0)], 26, 12);
    // exhaust outlet, insulated pipe leaving sideways
    if (outboard == 0) cyl_x(c[1] - 25, c[2] + 12, s > 0 ? c[0] : 228 * s - 104, s > 0 ? 332 : -c[0], 30, 12);
    else translate([c[0], c[1] - 25, c[2]]) cylinder(r = 34, h = 150, $fn = fn(12));
    // compressor outlet to the charge-air cooler
    rod([c[0], c[1] + 42, c[2] + (outboard == 0 ? 0 : 40)], [s * 80, -10, 560], 25, 12);
  }
  // charge-air cooler in the V with fins, neck down to the charge-air pipe
  translate([-95, -170, 522]) cube([190, 190, 74]);
  translate([-40, -150, 500]) cube([80, 150, 24]);
  if (lod == 0) for (y = [-160 : 28 : 10]) translate([-95, y, 596]) cube([190, 6, 18]);
}

// ---------- transmission: transverse (Clutch braking), native frame ----------
// shroud x +-42.5, y +-292.5, z 0..402; output shaft line y 94, z 200 (outputOffset)
module tra_profile() {
  intersection() {
    union() {                                                  // low case + two gear drums on top
      offset(r = 18, $fn = fn(12)) offset(delta = -18) translate([-262, 14]) square([524, 300]);
      translate([94, 214]) circle(r = 170, $fn = fn(24));      // output / brake shaft drum
      translate([-148, 222]) circle(r = 142, $fn = fn(24));    // input / countershaft drum
      translate([-280, 0]) square([560, 16]);                  // sump lip
    }
    translate([-292.5, 0]) square([585, 402]);
  }
}
module along_x(x0, x1) { translate([x0, 0, 0]) rotate([90, 0, 90]) linear_extrude(x1 - x0) children(); }
module tra_shroud() {
  along_x(-38.5, 38.5) offset(delta = -5) tra_profile();
  along_x(-42.5, -38.5) tra_profile();
  along_x(38.5, 42.5) tra_profile();
}
module tra_input() {                                         // centre case + adapter to the engine flange
  along_x(-150, 150) intersection() { offset(delta = 4) tra_profile(); translate([-292.5, 0]) square([585, 402]); }
  if (lod == 0) translate([-118, -200, 398]) cube([236, 400, 13]);        // bevel-case cover
  cone_y(0, 200, -536, -292, 128, 150, 24);
  cyl_y(0, 200, -556, -536, 150, 24);
  if (lod == 0) {
    for (a = [0 : 45 : 359]) translate([sin(a) * 138, -536, 200 + cos(a) * 138]) rotate([-90, 0, 0]) cylinder(r = 9, h = 8, $fn = 6);
    for (a = [45 : 90 : 359]) translate([0, 0, 200]) rotate([0, a, 0]) translate([-5, -520, 0]) cube([10, 230, 150]);
  }
}
module tra_output_left() {                                   // at the first shroud: end cover, brake, flange
  along_x(-62, -42.5) offset(delta = 2) tra_profile();
  cyl_x(94, 200, -200, -62, 158, 24);
  if (lod == 0) for (x = [-170, -120]) cyl_x(94, 200, x, x + 7, 164, 24);
  cyl_x(94, 200, -236, -200, 124, 24);
  cyl_x(94, 200, -256, -236, 56, 16);
  if (lod == 0) for (a = [0 : 60 : 359]) translate([-244, 94 + sin(a) * 96, 200 + cos(a) * 96]) rotate([0, 90, 0]) cylinder(r = 10, h = 8, $fn = 6);
  translate([-112, -262, 250]) cube([50, 170, 136]);         // steering pump block
}
module tra_output_right() { mirror([1, 0, 0]) tra_output_left(); }

// ---------- transmission: longitudinal (Twin), native frame ----------
// shroud x +-200, y 0..85, z 55..550; input port (0, -400, 164), output port (0, 162.5, 351)
module lon_profile() {
  union() {                                                    // wide lower case, output drum, narrow top
    offset(r = 20, $fn = fn(12)) offset(delta = -20) translate([-184, 60]) square([368, 250]);
    translate([0, 351]) circle(r = 170, $fn = fn(24));
    offset(r = 20, $fn = fn(12)) offset(delta = -20) translate([-130, 300]) square([260, 245]);
    translate([-200, 150]) square([400, 26]);                  // strake
  }
}
module lon_shroud() {
  along_y(4, 81) lon_profile();
  along_y(0, 4) offset(delta = 0) translate([-200, 55]) square([400, 495]);
  along_y(81, 85) translate([-200, 55]) square([400, 495]);
}
module lon_input() {
  along_y(-180, 11) intersection() { offset(delta = 6) lon_profile(); translate([-206, 49]) square([412, 507]); }
  cone_y(0, 164, -381, -180, 118, 150, 24);
  cyl_y(0, 164, -401, -381, 140, 24);
  if (lod == 0) for (a = [0 : 45 : 359]) translate([sin(a) * 128, -381, 164 + cos(a) * 128]) rotate([-90, 0, 0]) cylinder(r = 9, h = 8, $fn = 6);
  translate([206, -150, 250]) cube([56, 110, 150]);           // hydraulic pump (native input reaches +x 265)
}
module lon_output() {
  along_y(17, 200) intersection() { offset(delta = 6) lon_profile(); translate([-206, 49]) square([412, 507]); }
  along_y(200, 215) offset(delta = -30) translate([-184, 60]) square([368, 485]);
  cyl_x(162.5, 351, -280, 280, 50, 20);                       // transverse output shaft tube
  if (lod == 0) for (s = [-1, 1]) cyl_x(162.5, 351, s > 0 ? 262 : -280, s > 0 ? 280 : -262, 52, 20);
}

// ---------- twin bridge: unit block x -500..500, y 0..1000, z 0..1000 (code scales it) ----------
module twin_bridge() {
  hull() {
    translate([-500, 60, 60]) cube([1000, 880, 700]);
    cyl_x(500, 760, -500, 500, 240, 24);
  }
  if (lod == 0) {
    for (x = [-330, 0, 330]) translate([x - 15, 30, 30]) cube([30, 940, 760]);
    translate([0, 260, 0]) cylinder(r = 150, h = 1000, $fn = fn(20));   // bevel cover over the splitter
  }
}
