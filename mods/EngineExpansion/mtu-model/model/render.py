"""Render the PNGs of the exported meshes (shots/) through the viewer files the user opens.

Usage: py -I render.py <model dir>
Packs: MB 873 size + transverse, MB 873 size + Twin, MT 883 size, V8 4.07 L; views 34, zij, voor,
boven (+ 34-achter); a close-up of the pair segment (LOD0 and LOD1).
"""
import os
import subprocess
import sys

OPENSCAD = r"C:\Program Files\OpenSCAD\openscad.com"
root = sys.argv[1]
PACKS = [
    ("mb873-tra", 12, 3.97, "tra", 0),
    ("mb873-twin", 12, 3.97, "twin", 0),
    ("mt883-tra", 12, 2.28, "tra", 0),
    ("v8-407-tra", 8, 4.07, "tra", 0),
]
VIEWS = [
    ("34", "-1.25,-1.0,0.85", "p"),
    ("34-achter", "-1.1,1.3,0.9", "p"),
    ("zij", "-1,0,0", "o"),
    ("voor", "0,-1,0", "o"),
    ("boven", "0,-0.0001,1", "o"),
]


def scad(args, log):
    p = subprocess.run([OPENSCAD] + args, cwd=root, capture_output=True, text=True)
    log.write(">>> " + " ".join(args) + "\n" + p.stdout + p.stderr + "\n")
    if p.returncode != 0:
        raise SystemExit("openscad failed: " + " ".join(args))


os.makedirs(os.path.join(root, "shots"), exist_ok=True)
with open(os.path.join(root, "logs", "render.log"), "w", encoding="utf-8") as log:
    for name, N, d, trans, turbo in PACKS:
        for view, eye, proj in VIEWS:
            scad(["-o", f"shots/{name}-{view}.png", "--imgsize=1200,800", "--colorscheme=Tomorrow",
                  f"--camera={eye},0,0,0", "--viewall", "--autocenter", f"--projection={proj}",
                  "-D", f"N={N}", "-D", f"d={d}", "-D", f'trans="{trans}"', "-D", f"turbo={turbo}",
                  "view-mtu-pack.scad"], log)
    # variant: MB 873-style outboard turbos (cfg TurboLayout = Outboard)
    scad(["-o", "shots/mb873-outboard-34.png", "--imgsize=1200,800", "--colorscheme=Tomorrow",
          "--camera=-1.25,-1.0,0.85,0,0,0", "--viewall", "--autocenter", "--projection=p",
          "-D", "N=12", "-D", "d=3.97", "-D", 'trans="tra"', "-D", "turbo=1", "view-mtu-pack.scad"], log)
    # today's EE 0.2.3 Clutch-braking placement (input port on the engine output) for comparison
    scad(["-o", "shots/mb873-tra-zij-poort.png", "--imgsize=1200,800", "--colorscheme=Tomorrow",
          "--camera=-1,0,0,0,0,0", "--viewall", "--autocenter", "--projection=o",
          "-D", "N=12", "-D", "d=3.97", "-D", 'trans="tra"', "-D", 'place="port"', "view-mtu-pack.scad"], log)
    for lod in (0, 1):
        scad(["-o", f"shots/pair-closeup-lod{lod}.png", "--imgsize=1200,800", "--colorscheme=Tomorrow",
              "--camera=-1.3,-0.9,0.8,0,0,0", "--viewall", "--autocenter", "--projection=p",
              "-D", f"lod={lod}", "view-pair.scad"], log)
    scad(["-o", "shots/pair-closeup-voor.png", "--imgsize=1200,800", "--colorscheme=Tomorrow",
          "--camera=0,-1,0,0,0,0", "--viewall", "--autocenter", "--projection=o", "view-pair.scad"], log)
print("rendered")
