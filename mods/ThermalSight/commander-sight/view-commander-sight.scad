// Interactive OpenSCAD viewer for Commander Panoramic Sight with thin yellow bounding-box lines.
module bounds_box(x0, x1, y0, y1, z0, z1, t=1.5) {
    color([1.0, 0.9, 0.1, 0.9]) {
        for (y = [y0, y1], z = [z0, z1])
            translate([(x0+x1)/2, y, z]) cube([x1-x0, t, t], center=true);
        for (x = [x0, x1], z = [z0, z1])
            translate([x, (y0+y1)/2, z]) cube([t, y1-y0, t], center=true);
        for (x = [x0, x1], y = [y0, y1])
            translate([x, y, (z0+z1)/2]) cube([t, t, z1-z0], center=true);
    }
}

color("#6B7560") import("stl/housing.stl");
color("#102428") import("stl/glass.stl");
bounds_box(-146, 146, -125, 132, 0, 334);
