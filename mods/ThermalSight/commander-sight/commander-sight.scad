// External Remote Commander Panoramic Sight (CITV / PKP / 2S38 OES-OP style)
// Units: millimetres in OpenSCAD (scaled by 0.001 to metres during STL->OBJ conversion).
// OpenSCAD coords: X = width, Y = depth (front lenses face -Y so iso camera 58,0,28 sees the optics), Z = height (0..340).
// Unity/Sprocket: X_u = X/1000, Y_u = Z/1000, Z_u = -Y/1000 (+Z forward, upright on Y=0).

part = "all"; // "all" | "housing" | "glass" | "icon"
$fn = 32;

module rounded_box(w, d, h, r=6) {
    hull() {
        for (sx = [-1, 1], sy = [-1, 1])
            translate([sx*(w/2 - r), sy*(d/2 - r), 0])
                cylinder(h=h, r=r);
    }
}

module sight_housing() {
    // Base mounting flange ring (sits flush at Z=0)
    cylinder(h=16, r=112);
    // Flange bolts around perimeter
    for (a = [0 : 45 : 315])
        rotate([0, 0, a])
            translate([96, 0, 16])
                cylinder(h=7, r=6);

    // Armored azimuth turret column / neck
    translate([0, 0, 14])
        cylinder(h=66, r1=88, r2=82);
    // Azimuth ring collar
    translate([0, 0, 74])
        cylinder(h=14, r=92);

    // Main armored electro-optical sensor head (chamfered turret pod)
    difference() {
        hull() {
            // Lower turret head footprint
            translate([0, 6, 86]) rounded_box(248, 224, 14, r=18);
            // Mid wide armored body
            translate([0, 4, 130]) rounded_box(264, 236, 128, r=18);
            // Upper tapered armored roof
            translate([0, 10, 266]) rounded_box(236, 210, 34, r=16);
            // Front armored anti-glare brow overhang (-Y)
            translate([0, -102, 268]) rounded_box(228, 36, 22, r=10);
        }
        // Main wide thermal / TV optical aperture bay (left-center, facing -Y)
        translate([-36, -104, 186])
            cube([122, 54, 102], center=true);
        // Secondary circular daylight / LRF aperture bay (right side, facing -Y)
        translate([68, -104, 186])
            rotate([90, 0, 0])
                cylinder(h=56, r=36, center=true);
    }

    // Protective lens bezel frames around both apertures
    // Main rectangular armored hood bezel
    difference() {
        translate([-36, -116, 186]) cube([132, 16, 112], center=true);
        translate([-36, -118, 186]) cube([116, 26, 96], center=true);
    }
    // Secondary circular lens hood ring
    translate([68, -116, 186])
        rotate([90, 0, 0])
            difference() {
                cylinder(h=18, r=41, center=true);
                cylinder(h=26, r=33, center=true);
            }

    // Side armored trunnion caps & mounting bolts
    for (sx = [-1, 1]) {
        translate([sx * 134, 8, 186])
            rotate([0, 90, 0])
                cylinder(h=16, r=46, center=true);
        for (a = [0 : 90 : 270])
            translate([sx * 143, 8 + 32*cos(a), 186 + 32*sin(a)])
                rotate([0, 90, 0])
                    cylinder(h=6, r=5, center=true);
    }

    // Rear electronics heat-sink fins
    for (i = [-3 : 3])
        translate([i * 26, 124, 186])
            cube([8, 16, 116], center=true);

    // Top armored roof hatch & environmental/meteo sensor cap
    translate([0, 14, 298])
        rounded_box(168, 138, 14, r=12);
    translate([-52, 24, 310])
        cylinder(h=24, r1=18, r2=15);
    translate([56, 24, 310])
        cylinder(h=16, r=14);
}

module sight_glass() {
    // Main rectangular thermal/TV optical window
    translate([-36, -98, 186])
        cube([114, 8, 94], center=true);
    // Secondary circular LRF / daylight lens
    translate([68, -98, 186])
        rotate([90, 0, 0])
            cylinder(h=8, r=32, center=true);
}

if (part == "housing") {
    color("#6B7560") sight_housing();
} else if (part == "glass") {
    color("#102428") sight_glass();
} else {
    color("#6B7560") sight_housing();
    color("#102428") sight_glass();
}
