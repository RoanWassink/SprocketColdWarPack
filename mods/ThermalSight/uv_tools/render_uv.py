"""Checkerboard (0.1 m) renders of the UVs; py -I uv_tools/render_uv.py.

Pure-Python orthographic rasteriser (no numpy/PIL). For every model one PNG with two views:
oblique front (left) and oblique rear (right). Painted surfaces show a 0.1 m checker with faint
0.025 m lines from UV0; fixed surfaces (glass, dark) are plain blue-grey. Writes
renders-0.4.1/<model>-0.4.1.png from the package and <model>-0.3.7.png from obj-0.4.0.
"""
from pathlib import Path
import math
import struct
import sys
import zlib

sys.path.insert(0, str(Path(__file__).parent))
import add_uv as A  # noqa: E402

OUT = Path(__file__).parent/'renders-0.4.1'
OLD = Path(__file__).parent/'obj-0.4.0'
W, H = 720, 620
VIEWS = [(1.0, 0.75, 1.3), (-1.0, 0.75, -1.3)]   # camera directions: front (+Z) oblique, rear oblique
LIGHT = A.unit((0.4, 0.9, 0.5))
CELL = 0.1


def png(path, width, height, rows):
    raw = b''.join(b'\x00'+bytes(r) for r in rows)
    def chunk(tag, data):
        return struct.pack('>I', len(data))+tag+data+struct.pack('>I', zlib.crc32(tag+data) & 0xffffffff)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 9))+chunk(b'IEND', b''))


def colour(u, v, painted):
    if not painted:
        return (95, 110, 130)
    c = (math.floor(u/CELL)+math.floor(v/CELL)) & 1
    base = (230, 205, 120) if c else (70, 95, 60)
    fu, fv = (u/0.025) % 1.0, (v/0.025) % 1.0
    if min(fu, 1-fu) < 0.06 or min(fv, 1-fv) < 0.06:
        base = tuple(int(x*0.8) for x in base)
    return base


def render(path, name, out):
    pos, tex, nrm, faces = A.parse(path.read_text())
    tris = []
    for material, corners in faces:
        p = [pos[c[0]] for c in corners]
        uv = [tex[c[1]] for c in corners]
        n = A.unit(A.cross(A.sub(p[1], p[0]), A.sub(p[2], p[0])))
        tris.append((p, uv, n, material in A.PAINT))
    image = [[(245, 245, 245)]*(2*W) for _ in range(H)]
    for view, eye in enumerate(VIEWS):
        f = A.unit(A.mul(eye, -1))
        r = A.unit(A.cross((0.0, 1.0, 0.0), f))
        up = A.cross(f, r)
        pts = [(A.dot(q, r), A.dot(q, up)) for t in tris for q in t[0]]
        x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
        y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
        scale = 0.9*min(W/(x1-x0), H/(y1-y0))
        ox, oy = view*W+W/2-scale*(x0+x1)/2, H/2+scale*(y0+y1)/2
        depth = [[math.inf]*W for _ in range(H)]
        for p, uv, n, painted in tris:
            s = [(ox+scale*A.dot(q, r), oy-scale*A.dot(q, up), A.dot(q, f)) for q in p]
            area = (s[1][0]-s[0][0])*(s[2][1]-s[0][1])-(s[2][0]-s[0][0])*(s[1][1]-s[0][1])
            if abs(area) < 1e-9:
                continue
            light = 0.35+0.65*abs(A.dot(n, LIGHT))
            bx0 = max(int(math.floor(min(q[0] for q in s))), view*W)
            bx1 = min(int(math.ceil(max(q[0] for q in s))), view*W+W-1)
            by0 = max(int(math.floor(min(q[1] for q in s))), 0)
            by1 = min(int(math.ceil(max(q[1] for q in s))), H-1)
            for y in range(by0, by1+1):
                py = y+0.5
                for x in range(bx0, bx1+1):
                    px = x+0.5
                    w0 = ((s[1][0]-px)*(s[2][1]-py)-(s[2][0]-px)*(s[1][1]-py))/area
                    w1 = ((s[2][0]-px)*(s[0][1]-py)-(s[0][0]-px)*(s[2][1]-py))/area
                    w2 = 1-w0-w1
                    if w0 < -1e-9 or w1 < -1e-9 or w2 < -1e-9:
                        continue
                    d = w0*s[0][2]+w1*s[1][2]+w2*s[2][2]
                    lx = x-view*W
                    if d >= depth[y][lx]:
                        continue
                    depth[y][lx] = d
                    u = w0*uv[0][0]+w1*uv[1][0]+w2*uv[2][0]
                    v = w0*uv[0][1]+w1*uv[1][1]+w2*uv[2][1]
                    c = colour(u, v, painted)
                    image[y][x] = (int(c[0]*light), int(c[1]*light), int(c[2]*light))
    for y in range(H):     # divider
        image[y][W] = (0, 0, 0)
    png(out, 2*W, H, [[ch for px in row for ch in px] for row in image])


def main():
    OUT.mkdir(exist_ok=True)
    names = sys.argv[1:] or A.BLOCKOUTS+A.AUTHORED+['box-sight.obj']
    for name in names:
        stem = name[:-4]
        render(A.ASSETS/name, name, OUT/f'{stem}-0.4.1.png')
        render(OLD/name, name, OUT/f'{stem}-0.3.7.png')
        print('rendered', stem)


if __name__ == '__main__':
    main()
