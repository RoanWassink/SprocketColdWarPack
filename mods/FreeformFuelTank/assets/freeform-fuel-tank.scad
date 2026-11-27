// Freeform Fuel Tank icon model for Sprocket (house style: steel grey #8E9296, iso 58,0,28)
// Resembles the vanilla Box Fuel Tank with subtle stamped ribs + filler cap, plus
// small #DC9E33 accent corner handles symbolizing freeform vertex editing.

$fn = 36;

module box_tank() {
    // Main tank body (1.0 x 0.5 x 1.0 proportions)
    color("#8E9296") {
        // Beveled box body via hull of 8 small spheres
        hull() {
            for (x = [-0.46, 0.46])
                for (y = [-0.46, 0.46])
                    for (z = [0.04, 0.46])
                        translate([x, y, z]) sphere(r = 0.04);
        }

        // Welded seam flange around middle
        translate([0, 0, 0.25])
            cube([1.04, 1.04, 0.025], center = true);

        // Reinforcement ribs on top
        for (x = [-0.26, 0.0, 0.26])
            translate([x, 0, 0.505])
                cube([0.07, 0.78, 0.022], center = true);

        // Filler neck & cap on top-right corner
        translate([0.30, -0.28, 0.50]) {
            cylinder(h = 0.07, r = 0.085);
            translate([0, 0, 0.07])
                cylinder(h = 0.035, r = 0.105);
        }
    }

    // Subtle freeform vertex accent nodes on visible front/top corners (<10% area, #DC9E33)
    color("#DC9E33") {
        for (p = [
            [-0.48, -0.48, 0.48],
            [ 0.48, -0.48, 0.48],
            [ 0.48,  0.48, 0.48],
            [-0.48,  0.48, 0.48],
            [ 0.48, -0.48, 0.02]
        ])
            translate(p) sphere(r = 0.055);
    }
}

box_tank();
