// Heavy Trunnion Mount - shared geometry (OpenSCAD 2021.01).
// Units: mm. Axes: X = trunnion axis (left-right), Y = forward (gun direction), Z = up.
// Origin = centre of the trunnion axis. Unit model in the mantlet's local space at trunnionsScale 1.5.
// Two groups: STATIC (mantlet base, does not tilt) and MOVING (trunnions, tilts about X with elevation).
// Materials: "cast" (dark cast steel), "steel" (machined steel), "bronze" (bearing bushes).
// Each material piece is cut by the higher-priority materials (bronze > steel > cast), so the
// pieces of a group never overlap and their union is the group mesh.

HALF   = 357.5;  // native trunnion half width at trunnionsScale 1.5 (0.4765 m * 1.5 / 2)
R_AXLE = 55;     // native trunnion radius at 1.5 (0.0737 m * 1.5 / 2 = 55.3)
Z_SAD  = 56;     // top of the saddle plate: 1 mm above the axle, ~ the native trunnion top (55.3)
R_PIN  = 48;     // journal (pin) radius = bronze bush bore
X_BRG  = 250;    // inner face of the bearing blocks; journals run X_BRG..HALF
X_PED  = 343;    // outer face of the pedestal (set back, so the bearing ring stands proud)
R_BRG  = 88;     // outer radius of the bearing ring
Z_BED  = -205;   // top of the bed plate
Y_BED  = [-280, 125];
FN     = 20;     // round game parts
FN_M   = 16;
FN_S   = 12;

// Sector gear (moving) and pinion (static): module 6, sector pitch radius 135, pinion 12 teeth.
MOD    = 6;
R_SECT = 135;
R_PINI = 36;
A_MESH = -140;             // angle (from +Y towards +Z) of the pinion centre seen from the axis
X_GEAR = -175;             // centre plane of sector and pinion
W_GEAR = 34;               // face width
P_PIN  = (R_SECT + R_PINI) * [cos(A_MESH), sin(A_MESH)];   // pinion centre (y, z) = (-131, -110)
P_MOT  = [-205, -140];     // motor axis (y, z)
STEP   = MOD * 180 / R_SECT;  // angular pitch of the sector in degrees (pi*m/R) = 8
K0 = -4; K1 = 3;           // sector teeth k = K0..K1, centred at A_MESH + (k + 0.5) * STEP
A0 = A_MESH + K0 * STEP - 1; A1 = A_MESH + (K1 + 1) * STEP + 1;   // sector span (-173..-107)

// ---------- helpers ----------
module xcyl(r, x0, x1, fn = FN, yz = [0, 0]) translate([x0, yz[0], yz[1]]) rotate([0, 90, 0]) cylinder(r = r, h = x1 - x0, $fn = fn);
// 2D in (y, z) extruded along X from x0 to x1
module yz(x0, x1) translate([x0, 0, 0]) rotate([90, 0, 90]) linear_extrude(x1 - x0) children();
module arc2d(r0, r1, a0, a1, n = 16) polygon(concat(
  [for (i = [0:n]) r1 * [cos(a0 + (a1 - a0) * i / n), sin(a0 + (a1 - a0) * i / n)]],
  [for (i = [n:-1:0]) r0 * [cos(a0 + (a1 - a0) * i / n), sin(a0 + (a1 - a0) * i / n)]]));
// trapezoid tooth, radial along +u, pitch radius r; root width follows the root arc, so the
// 12-tooth pinion and the sector mesh without overlap at elevation 0 (with backlash)
module tooth2d(r) { p = PI * MOD; rr = r - 1.25 * MOD - 1; wr = 0.62 * p * rr / r; wt = 0.22 * p;
  polygon([[rr, -wr / 2], [r + MOD, -wt / 2], [r + MOD, wt / 2], [rr, wr / 2]]); }
module hexz(d, h, fn = 6) cylinder(d = d, h = h, $fn = fn);
module hexx(d, h, yz = [0, 0]) translate([0, yz[0], yz[1]]) rotate([0, 90, 0]) rotate([0, 0, 30]) cylinder(d = d, h = h, $fn = 6);
module chamfer_rect(x0, x1, y0, y1, c) polygon([[x0 + c, y0], [x1 - c, y0], [x1, y0 + c], [x1, y1 - c], [x1 - c, y1], [x0 + c, y1], [x0, y1 - c], [x0, y0 + c]]);

// =================== MOVING GROUP (tilts with elevation) ===================
module moving_steel() {
  // trunnion axle, thrust collars and the two journals (pins); journals end flush at +-HALF
  xcyl(R_AXLE, -236, 236);
  for (s = [-1, 1]) {
    translate([s > 0 ? 235 : -249, 0, 0]) xcyl(66, 0, 14);
    translate([s > 0 ? 230 : -HALF, 0, 0]) xcyl(R_PIN, 0, HALF - 230);
  }
  // sector gear rim with teeth (machined ring bolted to the cast web)
  yz(X_GEAR - W_GEAR / 2, X_GEAR + W_GEAR / 2) {
    arc2d(110, R_SECT - 1.25 * MOD, A0, A1, 10);
    for (k = [K0:K1]) rotate(A_MESH + (k + 0.5) * STEP) tooth2d(R_SECT);
  }
  // rim bolts (on the -X face of the rim)
  for (a = [A0 + 9, (A0 + A1) / 2, A1 - 9]) translate([X_GEAR - W_GEAR / 2 - 6, 0, 0]) hexx(12, 6, 118 * [cos(a), sin(a)]);
  // hub clamp bolts (+X face of the hub)
  for (a = [-140, -90, -40]) translate([-150, 0, 0]) hexx(16, 7, 63 * [cos(a), sin(a)]);
  // saddle bolts (8 bosses on top of the narrow saddle plate)
  for (x = [-185, -60, 60, 185], y = [-36, 42]) translate([x, y, Z_SAD]) hexz(18, 5);
}

module moving_cast() {
  // narrow saddle plate, its top level with the top of the native trunnion
  translate([0, 0, 33]) linear_extrude(Z_SAD - 33) chamfer_rect(-225, 225, -55, 62, 18);
  // clamp straps around the axle, under the saddle
  for (x = [-124, 100, 190]) intersection() {
    xcyl(64, x, x + 24, FN_M);
    translate([x - 1, -70, -70]) cube([26, 140, 70 + Z_SAD]);
  }
  // sector hub (cut flush with the saddle top)
  intersection() {
    xcyl(74, -200, -150);
    translate([-201, -80, -80]) cube([52, 160, 80 + Z_SAD]);
  }
  // sector web with lightening holes, and three radial ribs
  yz(X_GEAR - 9, X_GEAR + 9) difference() {
    arc2d(60, 112, A0, A1, 10);
    for (a = [A0 + 0.27 * (A1 - A0), A0 + 0.73 * (A1 - A0)]) translate(88 * [cos(a), sin(a)]) circle(r = 15, $fn = FN_S);
  }
  yz(X_GEAR - W_GEAR / 2, X_GEAR + W_GEAR / 2)
    for (a = [A0 + 2.5, (A0 + A1) / 2, A1 - 2.5]) rotate(a) translate([66, -7]) square([46, 14]);
}

// =================== STATIC GROUP (mantlet base) ===================
module pillow_block(s) {
  x0 = s > 0 ? X_BRG : -HALF; x1 = s > 0 ? HALF : -X_BRG;          // full width (bearing ring, flange)
  p0 = s > 0 ? X_BRG : -X_PED; p1 = s > 0 ? X_PED : -X_BRG;         // pedestal, set back on the outside
  difference() {
    union() {
      xcyl(R_BRG, x0, x1);
      // waisted cast pedestal (keeps the gear train visible from the side) with foot gussets
      yz(p0, p1) {
        hull() { circle(r = 70, $fn = FN_S); translate([-62, Z_BED + 18]) square([124, 4]); }
        for (m = [0, 1]) mirror([m, 0]) polygon([[58, Z_BED + 18], [112, Z_BED + 18], [60, Z_BED + 68]]);
      }
      // foot flange, cap-bolt bosses, outer rib
      translate([x0, -125, Z_BED]) cube([x1 - x0, 250, 18]);
      for (y = [-74, 74]) translate([(x0 + x1) / 2, y, -40]) cylinder(r = 14, h = 88, $fn = FN_M);
      translate([s > 0 ? p1 : x0, -12, Z_BED + 18]) cube([HALF - X_PED, 24, -Z_BED - 18 - 60]);
    }
    // bore for the bronze bush, with a recess for its outer flange
    xcyl(60, x0 - 1, x1 + 1);
    xcyl(68, s > 0 ? x1 - 7 : x0 - 1, s > 0 ? x1 + 1 : x0 + 7);
    // split line between cap and pedestal (front and back faces)
    for (m = [0, 1]) mirror([0, m, 0]) translate([x0 - 11, 82, -1.5]) cube([x1 - x0 + 22, 100, 3]);
  }
}

module static_cast() {
  for (s = [-1, 1]) pillow_block(s);
  // bed plate with a lightening cut-out at the front
  translate([0, 0, Z_BED - 20]) linear_extrude(20) difference() {
    chamfer_rect(-HALF, HALF, Y_BED[0], Y_BED[1], 40);
    chamfer_rect(-140, 215, 10, 92, 14);
  }
  // reduction gearbox around the pinion shaft and the motor shaft
  yz(-150, -40) hull() {
    translate(P_PIN) circle(r = 46, $fn = FN);
    translate(P_MOT) circle(r = 62, $fn = FN);
    translate([-262, Z_BED]) square([160, 2]);
  }
  // raised cover plate on the -X face
  yz(-156, -150) hull() { translate(P_PIN) circle(r = 36, $fn = FN_S); translate(P_MOT) circle(r = 50, $fn = FN_M); }
  // outboard bearing bracket for the pinion shaft
  yz(-232, -206) hull() { translate(P_PIN) circle(r = 26, $fn = FN_M); translate([-168, Z_BED]) square([76, 2]); }
  // elevation motor: finned body, flange, fan cowl
  yz(-34, 170) { translate(P_MOT) circle(r = 54, $fn = 16);
    for (a = [-30:30:210]) translate(P_MOT) rotate(a) translate([50, -3]) square([13, 6]); }
  xcyl(64, -40, -34, 16, P_MOT);
  xcyl(58, 170, 204, 16, P_MOT);
  // motor feet
  for (x = [10, 130]) translate([x, P_MOT[0] - 48, Z_BED]) cube([28, 96, 22]);
  // terminal box with cable gland
  translate([45, P_MOT[0] - 25, P_MOT[1] + 46]) cube([56, 46, 26]);
  translate([73, P_MOT[0] - 25, P_MOT[1] + 59]) rotate([90, 0, 0]) cylinder(r = 8, h = 18, $fn = FN_S);
  // hand wheel boss on the back of the gearbox
  translate([-95, P_MOT[0] - 50, P_MOT[1]]) rotate([90, 0, 0]) cylinder(r = 15, h = 28, $fn = FN_S);
}

// =================== PINION GROUP (third mesh, turns -R_SECT/R_PINI = -3.75 x elevation) ===================
// Same geometry as before the split, still at its place in the model (centre y -131, z -110).
// pinion_unit() moves its axis to the origin for the game mesh; the mod places and turns it.
P_PIN3 = [0, P_PIN[0], P_PIN[1]];
PIN_RATIO = R_SECT / R_PINI;     // 3.75
module pinion_steel() {
  // pinion with 12 teeth on its shaft, and the shaft end nut
  yz(X_GEAR - W_GEAR / 2, X_GEAR + W_GEAR / 2) translate(P_PIN) {
    circle(r = R_PINI - 1.25 * MOD, $fn = FN);
    for (k = [0:11]) rotate(A_MESH + 180 + k * 30) tooth2d(R_PINI);
  }
  xcyl(18, -240, -150, FN_S, P_PIN);
  translate([-246, 0, 0]) hexx(30, 8, P_PIN);
}
module pinion_unit() translate(-P_PIN3) pinion_steel();

module static_steel() {
  // cap nuts on the bearing blocks, grease cups
  for (s = [-1, 1], y = [-74, 74]) translate([s * (X_BRG + HALF) / 2, y, 48]) hexz(26, 12);
  for (s = [-1, 1]) translate([s * (X_BRG + HALF) / 2, 0, R_BRG - 4]) hexz(18, 14);
  // foot bolts: bearing blocks and bed plate
  for (s = [-1, 1], x = [268, 334], y = [-112, 112]) translate([s * x, y, Z_BED + 18]) hexz(20, 9);
  for (x = [-200, 0, 200]) translate([x, 108, Z_BED]) hexz(20, 9);
  for (x = [-330, 290]) translate([x, -262, Z_BED]) hexz(20, 9);
  // cover bolts on the gearbox; the pinion teeth end at x = -158. The bolt at a = 0 lies inside the
  // pinion's tip circle, so its head is 1.5 mm (x -157.5..-156) and the turning teeth pass it.
  for (a = [0:60:300]) translate([a == 0 ? -157.5 : -160, 0, 0]) hexx(13, a == 0 ? 1.5 : 4, P_MOT + 41 * [cos(a), sin(a)]);
  // motor flange bolts
  for (a = [0:90:270]) translate([-47, 0, 0]) hexx(13, 7, P_MOT + 58 * [cos(a + 45), sin(a + 45)]);
  // fan grille bars across the cowl end
  for (z = [-30, -10, 10, 30]) translate([204, P_MOT[0] - sqrt(52 * 52 - z * z), P_MOT[1] + z - 3]) cube([6, 2 * sqrt(52 * 52 - z * z), 6]);
  // hand wheel (manual elevation back-up): rim, three spokes, crank knob
  translate([-95, P_MOT[0] - 82, P_MOT[1]]) rotate([90, 0, 0]) {
    rotate_extrude($fn = 12) translate([46, 0]) circle(r = 6, $fn = 4);
    for (a = [90, 210, 330]) rotate([0, 0, a]) translate([0, -5, -4]) cube([44, 10, 8]);
    translate([0, 0, -8]) cylinder(r = 13, h = 16, $fn = 8);
    translate([46 * cos(30), 46 * sin(30), 0]) cylinder(r = 6, h = 30, $fn = 6);
  }
}

module static_bronze() {
  for (s = [-1, 1]) {
    x0 = s > 0 ? X_BRG : -HALF; x1 = s > 0 ? HALF : -X_BRG;
    difference() {
      union() { xcyl(60, x0, x1); if (s > 0) xcyl(68, x1 - 7, x1); else xcyl(68, x0, x0 + 7); }
      xcyl(R_PIN, x0 - 1, x1 + 1);
    }
  }
}

// ---------- group entry points ----------
// The pinion is cut out of every static piece, so static + pinion at 0 deg is the old static group.
module group_static(mat = "all") {
  if (mat == "all") difference() { union() { static_cast(); static_steel(); static_bronze(); } pinion_steel(); }
  else if (mat == "cast") difference() { static_cast(); static_steel(); static_bronze(); pinion_steel(); }
  else if (mat == "steel") difference() { static_steel(); static_bronze(); pinion_steel(); }
  else if (mat == "bronze") static_bronze();
}
// origin on the pinion axis (game mesh); "placed" = at its place in the model
module group_pinion(mat = "all") { if (mat == "placed") pinion_steel(); else pinion_unit(); }
module group_moving(mat = "all") {
  if (mat == "all") union() { moving_cast(); moving_steel(); }
  else if (mat == "cast") difference() { moving_cast(); moving_steel(); }
  else if (mat == "steel") moving_steel();
}
