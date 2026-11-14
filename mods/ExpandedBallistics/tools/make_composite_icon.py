"""Draws the 256x256 isometric icon for the Composite armour plate.

Native cube-icon style: a light plate on a transparent background, seen from above/front, with thin
coloured layer stripes on the visible side. The stripe colours are the CassettePalette families:
steel (0x8e9296), ceramic (0xe8e1d0), glass/composite (0xa9b347) and the accent (0xdc9e33).

usage: python make_composite_icon.py [output.png]
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
SS = 4
STEEL = (0x8e, 0x92, 0x96)
CERAMIC = (0xe8, 0xe1, 0xd0)
COMPOSITE = (0xa9, 0xb3, 0x47)
ACCENT = (0xdc, 0x9e, 0x33)
SEPARATOR = (0x14, 0x15, 0x0f)
TOP = (0xea, 0xe8, 0xe0)
TOP_EDGE = (0xf7, 0xf5, 0xee)
SIDE = (0xcf, 0xcd, 0xc5)
SIDE_DARK = (0xb9, 0xb7, 0xaf)
OUTLINE = (0x4b, 0x4c, 0x47)
SHADOW = (0, 0, 0, 70)

# 1 x 1 m plate, 0.14 m thick, viewed from the front-right above.
X0, X1 = -0.5, 0.5
Z0, Z1 = -0.5, 0.5
Y0, Y1 = 0.0, 0.20
EYE = np.array([1.15, 1.10, 1.35], dtype=float)
EYE /= np.linalg.norm(EYE)
RIGHT = np.cross([0.0, 1.0, 0.0], EYE)
RIGHT /= np.linalg.norm(RIGHT)
UP = np.cross(EYE, RIGHT)
UP /= np.linalg.norm(UP)


def project(points, scale, centre, offset):
    points = np.asarray(points, dtype=float)
    x = (points - centre) @ RIGHT
    y = (points - centre) @ UP
    return [(offset[0] + x[i] * scale, offset[1] - y[i] * scale) for i in range(len(points))]


def face(points, colour, outline=None, width=0):
    draw.polygon(points, fill=colour)
    if outline is not None:
        draw.line(points + [points[0]], fill=outline, width=width, joint="curve")


def band(x0, x1, y0, y1, z, colour, scale, centre, offset):
    face(project([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], scale, centre, offset), colour)


# Fit the projected plate into a 236 px box with a little room for the shadow.
corners = np.array([(x, y, z) for x in (X0, X1) for y in (Y0, Y1) for z in (Z0, Z1)])
centre = (corners.min(axis=0) + corners.max(axis=0)) / 2
projected = project(corners, 1.0, centre, (0, 0))
xs, ys = [p[0] for p in projected], [p[1] for p in projected]
scale = (SIZE * SS * 0.82) / max(max(xs) - min(xs), max(ys) - min(ys))
offset = (SIZE * SS / 2 - 4 * SS, SIZE * SS / 2 + 2 * SS)

image = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)

# Soft shadow under the plate.
shadow = Image.new("RGBA", image.size, (0, 0, 0, 0))
shadow_draw = ImageDraw.Draw(shadow)
bottom = project([(X0, Y0, Z0), (X1, Y0, Z0), (X1, Y0, Z1), (X0, Y0, Z1)], scale, centre,
                 (offset[0] + 5 * SS, offset[1] + 9 * SS))
shadow_draw.polygon(bottom, fill=SHADOW)
shadow = shadow.filter(ImageFilter.GaussianBlur(6 * SS))
image.alpha_composite(shadow)
draw = ImageDraw.Draw(image)

# Visible right side (x = X1) is the deepest face; keep it plain and slightly darker.
face(project([(X1, Y0, Z0), (X1, Y0, Z1), (X1, Y1, Z1), (X1, Y1, Z0)], scale, centre, offset),
     SIDE_DARK, OUTLINE, 2 * SS)

# Visible front side (z = Z1): thin stacked layers, bottom to top.
face(project([(X0, Y0, Z1), (X1, Y0, Z1), (X1, Y1, Z1), (X0, Y1, Z1)], scale, centre, offset),
     SIDE, OUTLINE, 2 * SS)
layers = [
    (0.000, 0.045, STEEL),
    (0.045, 0.060, SEPARATOR),
    (0.060, 0.100, CERAMIC),
    (0.100, 0.115, SEPARATOR),
    (0.115, 0.160, COMPOSITE),
    (0.160, 0.172, SEPARATOR),
    (0.172, 0.195, ACCENT),
    (0.195, 0.200, SEPARATOR),
]
for low, high, colour in layers:
    band(X0, X1, low, high, Z1, colour, scale, centre, offset)

# Top face: bright light grey with a thin lit front edge.
top = project([(X0, Y1, Z0), (X1, Y1, Z0), (X1, Y1, Z1), (X0, Y1, Z1)], scale, centre, offset)
face(top, TOP, OUTLINE, 2 * SS)
edge = project([(X0, Y1, Z1), (X1, Y1, Z1)], scale, centre, offset)
draw.line(edge, fill=TOP_EDGE, width=3 * SS)

image = image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / \
    "Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets/compositeArmourPlate-icon.png"
out.parent.mkdir(parents=True, exist_ok=True)
image.save(out)
print(out)
