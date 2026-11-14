"""Heavy Trunnion Mount: STL checks (pure Python, no packages).

Usage: py -I stlcheck.py <model dir> [--write <report.txt>]
Checks per exported STL: triangle count, bounds (mm), volume, non-manifold edges,
connected parts; per group: union volume == sum of the material pieces; pins fit the
bushes (journal radius == bush bore, flush at +-357.5 mm); SHA256 of every STL.
Exit code 1 if a check fails.
"""
import hashlib, math, os, struct, sys
from collections import defaultdict

HALF, R_PIN = 357.5, 48.0


def read_stl(path):
    data = open(path, 'rb').read()
    tris = []
    if data[:5] == b'solid' and b'facet' in data[:400]:
        verts = []
        for line in data.decode('ascii', 'replace').splitlines():
            t = line.split()
            if t and t[0] == 'vertex':
                verts.append(tuple(float(v) for v in t[1:4]))
                if len(verts) == 3:
                    tris.append(tuple(verts)); verts = []
    else:
        n = struct.unpack('<I', data[80:84])[0]
        for i in range(n):
            f = struct.unpack('<12f', data[84 + 50 * i:84 + 50 * i + 48])
            tris.append((f[3:6], f[6:9], f[9:12]))
    return tris


def stats(tris):
    lo = [min(v[i] for t in tris for v in t) for i in range(3)]
    hi = [max(v[i] for t in tris for v in t) for i in range(3)]
    vol = 0.0
    for a, b, c in tris:
        vol += (a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0])) / 6
    key = lambda v: (round(v[0], 4), round(v[1], 4), round(v[2], 4))
    edges = defaultdict(int)
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for t in tris:
        k = [key(v) for v in t]
        for i in range(3):
            edges[tuple(sorted((k[i], k[(i + 1) % 3])))] += 1
        for i in (1, 2):
            ra, rb = find(k[0]), find(k[i])
            if ra != rb: parent[ra] = rb
    bad = sum(1 for n in edges.values() if n != 2)
    parts = len({find(k) for k in list(parent)})
    return lo, hi, vol, bad, parts


def radial_at(tris, xlo, xhi):
    """min / max distance from the X axis of the vertices with xlo <= x <= xhi."""
    r = [math.hypot(v[1], v[2]) for t in tris for v in t if xlo <= v[0] <= xhi]
    return (min(r), max(r)) if r else (None, None)


def main():
    root = sys.argv[1]
    out = sys.argv[sys.argv.index('--write') + 1] if '--write' in sys.argv else None
    lines, ok = [], True
    files = {
        'static': 'heavy-trunnion-static.stl', 'moving': 'heavy-trunnion-moving.stl',
        'static-cast': 'materials/static-cast.stl', 'static-steel': 'materials/static-steel.stl',
        'static-bronze': 'materials/static-bronze.stl',
        'moving-cast': 'materials/moving-cast.stl', 'moving-steel': 'materials/moving-steel.stl',
        'pinion': 'heavy-trunnion-pinion.stl', 'pinion-steel': 'materials/pinion-steel.stl'}
    res, tri = {}, {}
    lines.append(f'{"mesh":14} {"tris":>5} {"volume cm3":>11} {"nonmanifold":>11} {"parts":>5}  bounds min..max (mm)')
    for name, rel in files.items():
        t = read_stl(os.path.join(root, rel)); tri[name] = t
        lo, hi, vol, bad, parts = res[name] = stats(t)
        b = ' '.join(f'{a:.1f}..{c:.1f}' for a, c in zip(lo, hi))
        lines.append(f'{name:14} {len(t):5d} {vol / 1000:11.1f} {bad:11d} {parts:5d}  {b}')
    for g, mats in (('static', ['cast', 'steel', 'bronze']), ('moving', ['cast', 'steel']), ('pinion', ['steel'])):
        s = sum(res[f'{g}-{m}'][2] for m in mats); u = res[g][2]
        good = abs(s - u) <= 1e-3 * u
        ok &= good
        lines.append(f'CHECK {g}: union volume {u / 1000:.1f} cm3, sum of materials {s / 1000:.1f} cm3 -> {"OK" if good else "FAIL"}')
    # pins: moving journals end flush at +-HALF with radius R_PIN
    mlo, mhi = res['moving'][0], res['moving'][1]
    good = abs(mlo[0] + HALF) < 0.01 and abs(mhi[0] - HALF) < 0.01
    ok &= good
    lines.append(f'CHECK moving X width {mhi[0] - mlo[0]:.1f} mm (native 0.4765 m x 1.5 = 714.8 mm) -> {"OK" if good else "FAIL"}')
    for s in (-1, 1):
        lo_, hi_ = sorted((s * 252, s * HALF))
        pin = radial_at(tri['moving'], lo_, hi_)[1]
        bore = radial_at(tri['static-bronze'], lo_, hi_)[0]
        good = pin is not None and bore is not None and abs(pin - R_PIN) < 0.01 and abs(bore - R_PIN) < 0.01
        ok &= good
        lines.append(f'CHECK pin {"+" if s > 0 else "-"}X: journal radius {pin:.2f} mm, bush bore {bore:.2f} mm (no gap, no overlap) -> {"OK" if good else "FAIL"}')
    # the yellow bounds in the viewer must be the bounds of static + moving at elevation 0
    lo = [min(res['static'][0][i], res['moving'][0][i]) for i in range(3)]
    hi = [max(res['static'][1][i], res['moving'][1][i]) for i in range(3)]
    view = open(os.path.join(root, 'view-heavy-trunnion.scad'), encoding='utf-8').read()
    vb = view.split('B_MIN = [')[1].split(']')[0], view.split('B_MAX = [')[1].split(']')[0]
    vlo, vhi = ([float(v) for v in s.split(',')] for s in vb)
    good = all(abs(a - b) < 0.05 for a, b in zip(vlo + vhi, lo + hi))
    ok &= good
    lines.append(f'CHECK viewer bounds B_MIN {vlo} B_MAX {vhi} vs mesh {[round(v, 1) for v in lo]} {[round(v, 1) for v in hi]} -> {"OK" if good else "FAIL"}')
    # pinion: origin on its own axis (the teeth, R36 + module 6 = tip R42, are centred on y = z = 0)
    plo, phi = res['pinion'][0], res['pinion'][1]
    good = all(abs(plo[i] + phi[i]) < 1.0 for i in (1, 2)) and abs(phi[1] - 42) < 1.0
    ok &= good
    lines.append(f'CHECK pinion origin on its axis: y {plo[1]:.1f}..{phi[1]:.1f}, z {plo[2]:.1f}..{phi[2]:.1f} (tip R42) -> {"OK" if good else "FAIL"}')
    total = len(tri['static']) + len(tri['moving']) + len(tri['pinion'])
    good = total <= 5000
    ok &= good
    lines.append(f'CHECK triangles static {len(tri["static"])} + moving {len(tri["moving"])} + pinion {len(tri["pinion"])} = {total} (budget ~5000) -> {"OK" if good else "FAIL"}')
    mats = sum(len(tri[k]) for k in files if '-' in k)
    lines.append(f'INFO game triangles (per-material meshes in the DLL): {mats}')
    lines.append('SHA256')
    for name, rel in files.items():
        lines.append(f'  {hashlib.sha256(open(os.path.join(root, rel), "rb").read()).hexdigest()}  {rel}')
    lines.append('RESULT ' + ('PASS' if ok else 'FAIL'))
    text = '\n'.join(lines)
    print(text)
    if out:
        open(out, 'w', encoding='utf-8').write(text + '\n')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
