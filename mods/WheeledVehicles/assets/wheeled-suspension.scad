// Wheeled Suspension part icon (house style: #8E9296 steel grey, #DC9E33 steering accent)
// Follows .claude/skills/sprocket-bepinex/references/part-icons.md

$fn = 48;

steel = "#8E9296";
accent = "#DC9E33";

module tire_and_hub() {
    // Positioned slightly left-front and turned so both the off-road tyre AND the suspension/steering assembly read clearly.
    translate([-26, -12, 0])
    rotate([0, 0, -18]) {
        color(steel) {
            // Solid off-road tyre carcass with bevelled shoulders
            rotate([90, 0, 0]) {
                cylinder(h = 26, r = 42, center = true);
                translate([0, 0, 13]) cylinder(h = 4, r1 = 42, r2 = 38);
                translate([0, 0, -17]) cylinder(h = 4, r1 = 38, r2 = 42);
            }
            // Off-road tread blocks
            for (a = [0 : 20 : 340]) {
                rotate([0, a, 0])
                    translate([0, 0, 41.5])
                        cube([7.5, 25, 4.5], center = true);
            }
            // Recessed rim lip & solid hub
            rotate([90, 0, 0]) {
                translate([0, 0, 16]) cylinder(h = 3.5, r = 24, center = true);
                translate([0, 0, 18.5]) cylinder(h = 4.5, r = 11, center = true);
            }
            // 8 wheel lug bolts
            for (a = [0 : 45 : 315]) {
                rotate([0, a, 0])
                    translate([0, -18.5, 17])
                        rotate([90, 0, 0])
                            cylinder(h = 3, r = 2.2, center = true);
            }
        }
    }
}

module wishbone_and_strut() {
    // Exposed double-wishbone & coilover strut to the right/rear of the wheel
    color(steel) {
        // Chassis mounting block
        translate([34, 18, 6]) cube([16, 26, 52], center = true);
        // Solid bridging web between wheel knuckle and chassis mount (upper & lower wishbones)
        hull() {
            translate([30, 18, 20]) cylinder(h = 7, r = 6, center = true);
            translate([-8, 4, 16]) cylinder(h = 7, r = 6, center = true);
            translate([16, 8, 18]) cylinder(h = 7, r = 7, center = true);
        }
        hull() {
            translate([30, 18, -14]) cylinder(h = 8, r = 6.5, center = true);
            translate([-8, 4, -14]) cylinder(h = 8, r = 6.5, center = true);
            translate([16, 8, -14]) cylinder(h = 8, r = 7, center = true);
        }
        // Central backing web to prevent any enclosed background hole between arms
        translate([12, 12, 2]) rotate([0, 0, 20]) cube([38, 10, 32], center = true);

        // Coilover spring / damper strut angled prominently across the top
        translate([12, 4, 16]) rotate([0, 32, 15]) {
            cylinder(h = 54, r = 6, center = true);
            for (z = [-14 : 6.5 : 14]) {
                translate([0, 0, z]) cylinder(h = 3, r = 10.5, center = true);
            }
        }
    }

    // Functional steering knuckle & tie-rod accent (#DC9E33, ~7% visible area)
    color(accent) {
        translate([-2, -6, 4]) cylinder(h = 34, r = 4.5, center = true);
        translate([-2, -6, 21]) sphere(r = 5.5);
        translate([14, -10, -8]) rotate([0, 90, 15]) cylinder(h = 32, r = 3.5, center = true);
    }
}

union() {
    tire_and_hub();
    wishbone_and_strut();
}
