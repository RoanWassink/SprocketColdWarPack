"""MTU powerpack: STL checks (pure Python, no packages).

Usage: py -I stlcheck.py <model dir> [--write <report.txt>]
Per exported STL: triangles (budget), bounds (mm) against the part frame / native box,
non-manifold edges, SHA256. Seam checks at the joint planes: pair y=0 face == pair y=SEG face,
start and output joint faces lie inside the pair face (no gap, nothing sticking out).
Exit code 1 if a check fails.
"""
import hashlib, os, struct, sys
from collections import defaultdict

SEG, START, OUT, W2, H = 120.0, 160.0, 245.0, 350.0, 620.0
# part: (budget LOD0, bounds lo, bounds hi); bounds = engine frame or the native collider/mesh box (mm)
PARTS = {
    'start': (2500, (-W2, -START, 0), (W2, 0, H)),
    'pair': (1500, (-W2, 0, 0), (W2, SEG, H)),
    'output': (2500, (-W2, 0, 0), (W2, OUT, H)),
    'turbo': (2500, (-W2, -2 * SEG, 0), (W2, OUT, H)),
    'turbo-outboard': (2500, (-622.5, -2 * SEG, 0), (622.5, OUT, H)),
    'tra-input': (1500, (-150, -556, 0), (150, 290, 411.2)),
    'tra-shroud': (1500, (-42.5, -292.5, 0), (42.5, 292.5, 402)),
    'tra-output-l': (1500, (-256, -292.9, -2.2), (37, 292.9, 409.6)),
    'tra-output-r': (1500, (-37, -292.9, -2.2), (256, 292.9, 409.6)),
    'lon-input': (1500, (-231, -401, 0), (265.4, 11.3, 568.1)),
    'lon-shroud': (1500, (-200, 0, 55), (200, 85, 550)),
    'lon-output': (1500, (-280.3, 16.7, 20.3), (280.6, 214.6, 572.7)),
    'twin-bridge': (1500, (-500, 0, 0), (500, 1000, 1000)),
}
ENGINE = ('start', 'pair', 'output', 'turbo', 'turbo-outboard')


def read_stl(path):
    tris, verts = [], []
    for line in open(path, encoding='ascii'):
        t = line.split()
        if t and t[0] == 'vertex':
            verts.append(tuple(float(v) for v in t[1:4]))
            if len(verts) == 3:
                tris.append(tuple(verts)); verts = []
    return tris


def stats(tris):
    lo = [min(v[i] for t in tris for v in t) for i in range(3)]
    hi = [max(v[i] for t in tris for v in t) for i in range(3)]
    edges = defaultdict(int)
    for t in tris:
        for i in range(3):
            edges[tuple(sorted((t[i], t[(i + 1) % 3])))] += 1
    bad = sum(1 for n in edges.values() if n != 2)
    return lo, hi, bad


def plane_face(tris, y):
    """triangles lying in the plane y (projected to x, z) and their total area"""
    face = [tuple((v[0], v[2]) for v in t) for t in tris if all(abs(v[1] - y) < 1e-3 for v in t)]
    area = sum(abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])) / 2 for a, b, c in face)
    return face, area


def inside(p, face, eps=0.05):
    for a, b, c in face:
        d1 = (p[0] - b[0]) * (a[1] - b[1]) - (a[0] - b[0]) * (p[1] - b[1])
        d2 = (p[0] - c[0]) * (b[1] - c[1]) - (b[0] - c[0]) * (p[1] - c[1])
        d3 = (p[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (p[1] - a[1])
        area2 = abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])) or 1.0
        tol = eps * area2 ** 0.5
        if (d1 >= -tol and d2 >= -tol and d3 >= -tol) or (d1 <= tol and d2 <= tol and d3 <= tol):
            return True
    return False


def main():
    root = sys.argv[1]
    out = sys.argv[sys.argv.index('--write') + 1] if '--write' in sys.argv else None
    lines, ok, tri, res = [], True, {}, {}
    lines.append(f'{"mesh":22} {"tris":>5} {"nonmanifold":>11}  bounds min..max (mm)  budget/frame')
    for part, (budget, blo, bhi) in PARTS.items():
        for lod in ((0, 1) if part in ENGINE else (0,)):
            name = f'{part}-lod{lod}'
            path = os.path.join(root, 'parts', name + '.stl')
            t = tri[name] = read_stl(path)
            lo, hi, bad = res[name] = stats(t)
            inb = all(lo[i] >= blo[i] - 0.6 and hi[i] <= bhi[i] + 0.6 for i in range(3))
            good = inb and bad == 0 and (lod == 1 or len(t) <= budget)
            ok &= good
            b = ' '.join(f'{a:.1f}..{c:.1f}' for a, c in zip(lo, hi))
            lines.append(f'{name:22} {len(t):5d} {bad:11d}  {b}  {"<=" + str(budget) if lod == 0 else "lod1"} {"in frame" if inb else "OUT OF FRAME"} -> {"OK" if good else "FAIL"}')
    # seams
    for lod in (0, 1):
        p0, a0 = plane_face(tri[f'pair-lod{lod}'], 0.0)
        p1, a1 = plane_face(tri[f'pair-lod{lod}'], SEG)
        good = abs(a0 - a1) <= 1e-3 * a0 and a0 > 0
        ok &= good
        lines.append(f'CHECK seam pair-lod{lod}: face y=0 {a0:.0f} mm2, face y={SEG:.0f} {a1:.0f} mm2 -> {"OK" if good else "FAIL"}')
        for other in ('start', 'output'):
            f, a = plane_face(tri[f'{other}-lod{lod}'], 0.0)
            pts = {v for t in f for v in t}
            miss = [p for p in pts if not inside(p, p0)]
            good = a > 0 and not miss and a <= a0 * 1.001
            ok &= good
            lines.append(f'CHECK seam {other}-lod{lod} at y=0: face {a:.0f} mm2 inside pair face {a0:.0f} mm2, {len(miss)} vertices outside -> {"OK" if good else "FAIL"}')
    # flange face of the output part: at y = OUT, centred on the crank axis
    f, a = plane_face(tri['output-lod0'], OUT)
    xs = [v[0] for t in f for v in t]; zs = [v[1] for t in f for v in t]
    good = f and abs(max(xs) + min(xs)) < 1 and min(zs) >= -0.01
    ok &= bool(good)
    lines.append(f'CHECK output flange at y={OUT:.0f}: area {a:.0f} mm2, x {min(xs):.1f}..{max(xs):.1f}, z {min(zs):.1f}..{max(zs):.1f} -> {"OK" if good else "FAIL"}')
    lines.append('SHA256 (canonical STL from stlnorm.py)')
    for name in tri:
        p = os.path.join(root, 'parts', name + '.stl')
        lines.append(f'  {hashlib.sha256(open(p, "rb").read()).hexdigest()}  parts/{name}.stl')
    lines.append('RESULT ' + ('PASS' if ok else 'FAIL'))
    text = '\n'.join(lines)
    print(text)
    if out:
        open(out, 'w', encoding='utf-8').write(text + '\n')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
