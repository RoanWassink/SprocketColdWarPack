"""Deterministic metre-scale box UVs; run with py -I uv_tools/add_uv.py."""
from pathlib import Path
import hashlib
import json
import math
import sys

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'package/BepInEx/plugins/SprocketThermalSight/assets'
NAMES = ['thermal-head.obj', 'thermal-head-vertical.obj', 'automatic-lrf.obj',
         'lrf-concept.obj', 'optical-rangefinder.obj']


def sha(data):
    return hashlib.sha256(data).hexdigest()


def geometry(text):
    positions, faces, order = [], [], []
    material = 'Paint'
    for line in text.splitlines():
        f = line.split()
        if not f:
            continue
        if f[0] == 'v':
            positions.append(tuple(map(float, f[1:])))
        elif f[0] == 'usemtl':
            material = f[1]
            order.append(material)
        elif f[0] == 'f':
            assert len(f) == 4
            faces.append((material, tuple(int(v.split('/')[0]) for v in f[1:])))
    return positions, faces, order


def convert(text):
    positions, _, _ = geometry(text)
    output, vt, vn = [], [], []
    for line in text.splitlines():
        f = line.split()
        if f and f[0] == 'f':
            ids = [int(v.split('/')[0]) for v in f[1:]]
            a, b, c = [positions[i-1] for i in ids]
            ab, ac = [[p-q for p, q in zip(v, a)] for v in (b, c)]
            n = (ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0])
            length = math.sqrt(sum(v*v for v in n))
            assert length > 1e-12, 'Degenerate source face'
            n = tuple(v/length for v in n)
            axis = max(range(3), key=lambda i: abs(n[i]))
            # Cyclic bases have cross(U,V) along the positive dominant axis.
            u, v = ((1, 2), (2, 0), (0, 1))[axis]
            sign = 1 if n[axis] > 0 else -1
            normal_id = len(vn)+1
            vn.append('vn ' + ' '.join(format(x, '.9g') for x in n))
            corners = []
            for index in ids:
                p = positions[index-1]
                vt.append(f'vt {p[u]*sign:.9g} {p[v]:.9g}')
                corners.append(f'{index}/{len(vt)}/{normal_id}')
            output.append('f ' + ' '.join(corners))
        elif not f or f[0] not in ('vt', 'vn'):
            output.append(line)
    # Attributes must precede the face that references them.
    first = next(i for i, line in enumerate(output) if line.startswith('f '))
    output[first:first] = vt + vn
    return ('\n'.join(output)+'\n').encode()


def main():
    check = '--check' in sys.argv
    records = {}
    for name in NAMES:
        source = (Path(__file__).parent/'original'/name).read_bytes()
        generated = convert(source.decode())
        if check:
            assert (ASSETS/name).read_bytes() == generated, name+' output differs from generator'
        else:
            (ASSETS/name).write_bytes(generated)
        assert geometry(source.decode()) == geometry(generated.decode()), name+' geometry/order changed'
        p, faces, order = geometry(generated.decode())
        bounds = [[min(v[i] for v in p) for i in range(3)], [max(v[i] for v in p) for i in range(3)]]
        records[name] = dict(source_sha256=sha(source), output_sha256=sha(generated), generator_sha256=sha(Path(__file__).read_bytes()),
                             positions=len(p), triangles=len(faces), bounds=bounds, material_order=order)
        print(name, len(p), len(faces), bounds, 'geometry/order unchanged; deterministic UV/normal output')
    manifest = Path(__file__).parent/'manifest.json'
    if check:
        assert json.loads(manifest.read_text()) == records
    else:
        manifest.write_text(json.dumps(records, indent=2)+'\n')


if __name__ == '__main__':
    main()
