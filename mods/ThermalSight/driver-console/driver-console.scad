// Driver Remote Vision Console (Sprocket Thermal Sight)
// Units: millimetres in OpenSCAD (scaled by 0.001 to metres during STL->OBJ conversion).
// OpenSCAD coords: X = width (-220..+220), Y = depth (front/screen faces -Y so iso camera 58,0,28 sees the screens!), Z = height (0..280).
// In Unity/Sprocket: X_u = X/1000, Y_u = Z/1000, Z_u = -Y/1000 (so screen faces +Z, upright on Y=0).

part = "all"; // "all" | "housing" | "glass" | "icon"
$fn = 24;

module rounded_box(w, d, h, r=4) {
    hull() {
        for (sx = [-1, 1], sy = [-1, 1])
            translate([sx*(w/2 - r), sy*(d/2 - r), 0])
                cylinder(h=h, r=r);
    }
}

module main_body_housing() {
    // Base mounting pedestal (sits flush at Z=0)
    translate([0, 18, 0])
        rounded_box(220, 120, 14, r=8);

    // Pedestal neck angled slightly back
    hull() {
        translate([0, 22, 12]) rounded_box(160, 82, 6, r=6);
        translate([0, 32, 66]) rounded_box(190, 86, 6, r=6);
    }

    // Main ruggedized electronics enclosure (rear block)
    difference() {
        hull() {
            // Bottom of main pod
            translate([0, 24, 62]) rounded_box(420, 125, 10, r=10);
            // Top of main pod
            translate([0, 34, 246]) rounded_box(420, 118, 10, r=10);
            // Top anti-glare hood protruding forward (-Y)
            translate([0, -24, 252]) rounded_box(410, 44, 10, r=8);
        }
        // Recessed screen bay on the front (-Y), tilted 8 deg back
        translate([0, -34, 174])
            rotate([8, 0, 0])
                cube([378, 46, 134], center=true);
        // Recessed lower instrument/switch strip
        translate([0, -36, 86])
            rotate([12, 0, 0])
                cube([352, 26, 26], center=true);
    }

    // Center & side screen dividers (triple-camera display bezels)
    for (x = [-78, 78]) {
        translate([x, -26, 174])
            rotate([8, 0, 0])
                cube([8, 24, 136], center=true);
    }

    // Side protective bumpers (centered vertically on the main pod: Z=82..232)
    for (sx = [-1, 1]) {
        translate([sx * 216, 20, 82])
            rounded_box(16, 102, 150, r=6);
        // Side mounting bolts
        for (bz = [106, 208])
            translate([sx * 225, 20, bz])
                rotate([0, 90, 0])
                    cylinder(h=6, r=6, center=true);
    }

    // Lower control console switches & rotary dials
    for (i = [-4 : 4]) {
        translate([i * 34, -36, 86])
            rotate([102, 0, 0])
                cylinder(h=10, r=5.5, center=true);
    }

    // Rear cooling fins & armored cable connector block
    for (i = [-4 : 4]) {
        translate([i * 36, 90, 160])
            cube([8, 16, 128], center=true);
    }
    translate([-130, 88, 96])
        rotate([90, 0, 0])
            cylinder(h=24, r=16, center=true);
    translate([130, 88, 96])
        rotate([90, 0, 0])
            cylinder(h=24, r=16, center=true);

    // Top mounting rail / visor brow lip
    translate([0, -32, 260])
        rounded_box(396, 26, 8, r=4);
}

module display_glass() {
    // Center wide driving screen + left & right peripheral camera screens
    translate([0, -22, 174])
        rotate([8, 0, 0]) {
            // Center display
            cube([148, 6, 124], center=true);
            // Left display
            translate([-132, 2, 0])
                rotate([0, 0, -5])
                    cube([100, 6, 124], center=true);
            // Right display
            translate([132, 2, 0])
                rotate([0, 0, 5])
                    cube([100, 6, 124], center=true);
        }
}

module icon_accent() {
    // Subtle amber driving horizon / status bar accent (<10% visible area, per part-icons.md)
    translate([0, -28, 120])
        rotate([8, 0, 0])
            cube([122, 4, 10], center=true);
}

if (part == "housing") {
    color("#6B7560") main_body_housing();
} else if (part == "glass") {
    color("#102428") display_glass();
} else if (part == "icon") {
    color("#8E9296") {
        main_body_housing();
        display_glass();
    }
    color("#DC9E33") icon_accent();
} else {
    color("#6B7560") main_body_housing();
    color("#102428") display_glass();
}
