"""Deterministic creator OBJ -> game OBJ + exact per-surface STL previews. Run: py -I box-sight/convert_box_sight.py."""
from pathlib import Path
import hashlib
import json
import math
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'uv_tools'))
import add_uv  # noqa: E402  (0.4.1: metric 1 UV/m on the painted surface)

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
OUTPUT = ROOT / 'package/BepInEx/plugins/SprocketThermalSight/assets/box-sight.obj'
NAMES = ('paint', 'steel', 'dark', 'lens')
EXPECTED = (2700, 914, 748, 672)


def sub(a, b):
    return tuple(x-y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def triangulate(face, positions):
    points = [positions[c[0]] for c in face]
    # Newell normal; project onto its dominant plane, including concave n-gons.
    normal = [0., 0., 0.]
    for a, b in zip(points, points[1:]+points[:1]):
        for j in range(3):
            normal[j] += (a[(j+1)%3]-b[(j+1)%3])*(a[(j+2)%3]+b[(j+2)%3])
    axis = max(range(3), key=lambda i: abs(normal[i]))
    if abs(normal[axis]) < 1e-14:
        # Creator export contains collapsed bevels (coincident position tuples).
        # Retain their n-2 zero-area triangles without inventing any geometry.
        triangles = [(face[0],face[i],face[i+1]) for i in range(1,len(face)-1)]
        if any(dot(n,n) > 1e-24 for tri in triangles
               for n in [cross(sub(positions[tri[1][0]],positions[tri[0][0]]),sub(positions[tri[2][0]],positions[tri[0][0]]))]):
            raise ValueError('Face has no consistent normal')
        return triangles
    uv = [tuple(p[j] for j in range(3) if j != axis) for p in points]
    def orient(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    area = sum(a[0]*b[1]-b[0]*a[1] for a, b in zip(uv, uv[1:]+uv[:1]))
    sign = 1 if area > 0 else -1
    remaining = list(range(len(face)))
    triangles = []
    while len(remaining) > 3:
        for k, b in enumerate(remaining):
            a, c = remaining[k-1], remaining[(k+1)%len(remaining)]
            if sign*orient(uv[a], uv[b], uv[c]) <= 1e-14:
                continue
            def inside(p):
                return all(sign*orient(uv[x], uv[y], uv[p]) >= -1e-14 for x, y in ((a,b),(b,c),(c,a)))
            if any(inside(p) for p in remaining if p not in (a,b,c)):
                continue
            triangles.append((a,b,c))
            del remaining[k]
            break
        else:
            raise ValueError('Ear clipping failed (degenerate or self-intersecting face)')
    triangles.append(tuple(remaining))
    for tri in triangles:
        a,b,c = (points[i] for i in tri)
        if dot(cross(sub(b,a),sub(c,a)),normal) <= 0:
            raise ValueError('Triangle winding differs from face normal')
    return [tuple(face[i] for i in tri) for tri in triangles]


def convert():
    source = HERE / 'source/lrf down.obj'
    arrays = [[], [], []]
    surfaces = [[] for _ in NAMES]
    obj = material = None
    for number, line in enumerate(source.read_text(encoding='utf-8').splitlines(), 1):
        f = line.split()
        if not f or f[0].startswith('#'):
            continue
        if f[0] in ('v','vt','vn'):
            i = ('v','vt','vn').index(f[0])
            values = tuple(map(float, f[1:]))
            if not all(math.isfinite(x) for x in values):
                raise ValueError(f'Non-finite value at {number}')
            arrays[i].append(values)
        elif f[0] == 'o':
            obj = f[1]
        elif f[0] == 'usemtl':
            material = f[1]
        elif f[0] == 'f' and obj in ('housing','dark','lens'):
            surface = ('paint' if material == 'camo' else material) if obj == 'housing' else obj
            if surface not in NAMES:
                raise ValueError(f'Unknown surface at {number}: {obj}/{material}')
            corners = []
            for token in f[1:]:
                indices = token.split('/')
                if len(indices) != 3 or not all(indices):
                    raise ValueError(f'Missing v/vt/vn at {number}')
                corner = tuple(int(n)-1 if int(n)>0 else len(array)+int(n) for n,array in zip(indices,arrays))
                if any(n < 0 or n >= len(array) for n,array in zip(corner,arrays)):
                    raise ValueError(f'Index out of range at {number}')
                corners.append(corner)
            try:
                surfaces[NAMES.index(surface)].extend(triangulate(corners, arrays[0]))
            except ValueError as ex:
                raise ValueError(f'{source.name}:{number}: {ex}') from ex
    counts = tuple(map(len, surfaces))
    if counts != EXPECTED:
        raise ValueError(f'Triangle counts {counts} != {EXPECTED}')
    # Remove unused scene data from every attribute stream, then rotate 180 degrees about Y.
    remaps = [{old:new for new,old in enumerate(sorted({c[i] for group in surfaces for tri in group for c in tri}))} for i in range(3)]
    compact = [[array[old] for old in remap] for array,remap in zip(arrays,remaps)]
    for i in (0,2):
        compact[i] = [(-x,y,-z) for x,y,z in compact[i]]
    fmt = lambda n: format(0 if n == 0 else n, '.9g')
    lines = ['# Laser box sight; metres; +Z forward; creator geometry retained.']
    for label,array in zip(('v','vt','vn'),compact):
        lines.extend(label+' '+' '.join(map(fmt,p)) for p in array)
    for name,group in zip(NAMES,surfaces):
        lines.append('usemtl '+name)
        for tri in group:
            lines.append('f '+' '.join('/'.join(str(remaps[i][c[i]]+1) for i in range(3)) for c in tri))
    creator = ('\n'.join(lines)+'\n')
    # 0.4.1: the creator UVs are not metric (density 0..4 UV/m, collapsed islands); the painted
    # surface gets the shared metric UVs, steel/dark/lens keep their creator UVs. v/vn unchanged.
    payload, uv_rows = add_uv.convert('box-sight.obj', creator)
    add_uv.verify_file('box-sight.obj', creator.encode(), payload)
    uv_summary = add_uv.summary(uv_rows)
    if not add_uv.within_limits(uv_summary):
        raise ValueError('box-sight UV metrics out of limits')
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    OUTPUT.write_bytes(payload)
    stls = HERE / 'stl'
    stls.mkdir(exist_ok=True)
    for name,group in zip(NAMES,surfaces):
        lines = ['solid '+name]
        for tri in group:
            a,b,c = [compact[0][remaps[0][corner[0]]] for corner in tri]
            n = cross(sub(b,a),sub(c,a))
            length = math.sqrt(dot(n,n))
            n = tuple(x/length for x in n) if length else (0.,0.,0.)
            lines.extend(['facet normal '+' '.join(map(fmt,n)), 'outer loop'])
            lines.extend('vertex '+' '.join(map(fmt,p)) for p in (a,b,c))
            lines.extend(['endloop','endfacet'])
        lines.append('endsolid '+name)
        (stls/(name+'.stl')).write_text('\n'.join(lines)+'\n',encoding='ascii')
    low = [min(p[i] for p in compact[0]) for i in range(3)]
    high = [max(p[i] for p in compact[0]) for i in range(3)]
    degenerate = sum(dot(n,n) < 1e-24 for group in surfaces for tri in group
                     for a,b,c in [[arrays[0][corner[0]] for corner in tri]]
                     for n in [cross(sub(b,a),sub(c,a))])
    check = {'triangles':dict(zip(NAMES,counts)), 'totalTriangles':sum(counts),
             'sourceZeroAreaTrianglesRetained':degenerate,
             'positions':len(compact[0]), 'texcoords':sum(l.startswith(b'vt ') for l in payload.splitlines()),
             'normals':len(compact[2]), 'paintUv':uv_summary,
             'boundsMin':low,'boundsMax':high,'sha256':hashlib.sha256(payload).hexdigest(),
             'sourceSha256':hashlib.sha256(source.read_bytes()).hexdigest(),
             'converterSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    (HERE/'MESH-CHECK.txt').write_text(json.dumps(check,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(check,indent=2))


if __name__ == '__main__':
    convert()
