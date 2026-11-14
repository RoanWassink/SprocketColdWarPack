// MTU powerpack viewer: open in OpenSCAD and change the variables below (F5 = preview).
// It imports exactly the exported game meshes (parts\*.stl) and places them the way the game
// will: native CombustionEngineModel.Generate with the cloned powerpack definition (scale k,
// start at 0, pair i at i*SEG, output at n*SEG, block centred), the transmission input against
// the engine flange with 1 mm gap (EE 0.2.3), native TransmissionBlockModel segment layout.
// Units mm, axes: x side, y along the crankshaft (output and transmission at +y), z up.
// Yellow thin lines: native V engine RequiredSpace bounds for the same N and d.
// Blue thin lines: the real engine from MTU-MATEN.md (when N and d match a known engine).

N = 12;            // cylinders (even, V layout)
d = 3.97;          // litres per cylinder
trans = "tra";     // "tra" = Clutch braking (one transverse box), "twin" = splitter + two boxes, "none"
turbo = 0;         // 0 = compact (EPP), 1 = outboard (MB 873), -1 = no turbo set
place = "box";     // "box" = gearbox body against the engine flange (advice for 0.4; Twin already in 0.2.3)
                   // "port" = input port on the engine output port (Clutch braking in EE 0.2.3)
lod = 0;           // 0 = LOD0, 1 = LOD1 (also shadow and collision)
show_native = true;
show_real = true;
show_engine = true;
TS = 0.97;         // assumed native transmission scale (T about 3000 Nm, x cbrt 0.5 of EE); the game computes it
TC = 6;            // transverse gear-shroud segments (assumed)
LC = 5;            // longitudinal gear-shroud segments per box (assumed)
line = 6;          // bounds line thickness

SEG = 120; OUT = 245; CRANK = 220;
k = pow(d, 1 / 3);
n = N / 2;
s_nat = pow(d / 1.85, 1 / 3);
yF = k * (n * SEG / 2 + OUT);        // engine flange plane
zC = k * CRANK;                      // crank axis = engine output port height

// real engines (MTU-MATEN.md): [N, d, [L, B, H] low, [L, B, H] high]
REAL = [
  [12, 3.97, [1703, 1060, 1030], [1801, 1971, 1100]],   // MB 873 Ka-501 (MND / Defence24; B 1060 block, 1971 with turbos)
  [12, 2.27, [1488, 972, 742], [1488, 972, 742]],       // MT 883 Ka-500 (datasheet)
  [12, 2.28, [1488, 972, 742], [1488, 972, 742]],       // MT 883 at the brief's 2.28 L
  [10, 3.74, [1450, 0, 0], [1500, 0, 0]]                // MB 838 (length estimated only)
];

C_ALU = [0.80, 0.82, 0.84]; C_STEEL = [0.50, 0.53, 0.56]; C_TRANS = [0.64, 0.66, 0.60];

module mesh(name, c) {
  eng = (name == "start" || name == "pair" || name == "output" || name == "turbo" || name == "turbo-outboard");
  color(c) import(str("parts/", name, "-lod", eng ? lod : 0, ".stl"), convexity = 6);
}

module frame(lo, hi, t) {
  for (i = [0 : 2]) {
    a = (i + 1) % 3; b = (i + 2) % 3;
    for (u = [lo[a], hi[a]], v = [lo[b], hi[b]]) {
      q0 = [for (j = [0 : 2]) j == i ? lo[i] : (j == a ? u : v)];
      translate(q0 - [t / 2, t / 2, t / 2]) cube([i == 0 ? hi[0] - lo[0] + t : t, i == 1 ? hi[1] - lo[1] + t : t, i == 2 ? hi[2] - lo[2] + t : t]);
    }
  }
}

module engine() {
  scale(k) translate([0, -n * SEG / 2, 0]) {
    mesh("start", C_ALU);
    for (i = [0 : n - 1]) translate([0, i * SEG, 0]) mesh("pair", C_ALU);
    translate([0, n * SEG, 0]) {
      mesh("output", C_ALU);
      if (turbo == 0) mesh("turbo", C_STEEL);
      if (turbo == 1) mesh("turbo-outboard", C_STEEL);
    }
  }
}

module transverse() {
  L = 85 * TS;
  // box: the shroud depth (+-292.5 TS) is the box front; port: native inputOffset z -0.556
  translate([0, yF + 1 + (place == "box" ? 292.5 : 556) * TS, zC - 200 * TS]) {
    scale(TS) mesh("tra-input", C_TRANS);
    for (i = [0 : TC - 1]) translate([-0.5 * L * TC + i * L + 0.5 * L, 0, 0]) scale(TS) mesh("tra-shroud", C_TRANS);
    translate([-0.5 * L * TC + 0.5 * L, 0, 0]) scale(TS) mesh("tra-output-l", C_TRANS);
    translate([-0.5 * L * TC + (TC - 1) * L + 0.5 * L, 0, 0]) scale(TS) mesh("tra-output-r", C_TRANS);
  }
}

XT = 321 * k;                        // box centres: x +-0.47 m at 12 x 3.13 L in the user's 0.2.3 log
module twin() {
  L = 85 * TS;
  // box: front of the gearbox body 0.18 m before the module origin (user's 0.2.3 log: origin z -1.63,
  // engine output -1.81); port: native inputOffset z -0.4
  y0 = yF + 1 + (place == "box" ? 180 : 400) * TS; z0 = zC - 164 * TS;
  for (s = [-1, 1]) translate([s * XT, y0, z0]) mirror([s < 0 ? 1 : 0, 0, 0]) {
    scale(TS) mesh("lon-input", C_TRANS);
    for (i = [0 : LC - 1]) translate([0, i * L, 0]) scale(TS) mesh("lon-shroud", C_TRANS);
    translate([0, (LC - 1) * L, 0]) scale(TS) mesh("lon-output", C_TRANS);
  }
  // bridge between the two boxes, scaled by the code to the gap, the box length and height
  gap = 2 * (XT - 200 * TS);
  ya = yF + 1; yb = y0 + (LC - 1) * L + 215 * TS;
  translate([0, ya, z0 + 49 * TS]) scale([gap / 1000, (yb - ya) / 1000, 507 * TS / 1000]) mesh("twin-bridge", C_TRANS);
}

if (show_engine) engine();
if (trans == "tra") transverse();
if (trans == "twin") twin();

if (show_native) color([1, 0.83, 0]) {
  frame([-416.1 * s_nat, -(0.5 * n * 140 + 278.1) * s_nat, 0], [424.6 * s_nat, (0.5 * n * 140 + 122.6) * s_nat, 995.5 * s_nat], line);
}
if (show_real) color([0.18, 0.42, 0.87]) for (r = REAL) if (r[0] == N && abs(r[1] - d) < 0.02) {
  y0 = -k * (n * SEG / 2 + 160);    // real box starts at the damper end of our model
  for (b = [r[2], r[3]]) if (b[1] > 0) frame([-b[1] / 2, y0, 0], [b[1] / 2, y0 + b[0], b[2]], line);
  else for (yy = [y0 + b[0]]) translate([-30 * k, yy - line / 2, 0]) cube([60 * k, line, 60 * k]);   // length mark only
}
