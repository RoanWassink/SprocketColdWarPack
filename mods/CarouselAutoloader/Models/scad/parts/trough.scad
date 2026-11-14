// Carousel Autoloader 0.2.23 - feed trough (laadgoot) profile on unit calibre, extruded along z over 1.
// The round axis is at (0,0). Outer width 1.1 d, a groove of radius 0.52 d, a floor of 0.12 d and a
// bottom flange; the walls end at half height so the round stays visible in the trough.
linear_extrude(height = 1)
  difference() {
    polygon([[-0.55,-0.64],[0.55,-0.64],[0.55,-0.10],[0.50,-0.04],[-0.50,-0.04],[-0.55,-0.10]]);
    circle(r = 0.52, $fn = 24);
  }
