"""UV density/stretch report before (0.4.0 = 0.3.7 UVs) and after (0.4.1); py -I uv_tools/uv_metrics.py.

Groups (flat faces, cylinders, cones) are those of the 0.4.1 generator; 'before' measures the old
UVs on exactly the same groups. Only painted surfaces are measured.
"""
from pathlib import Path
import math
import sys

sys.path.insert(0, str(Path(__file__).parent))
import add_uv as A  # noqa: E402

OLD = Path(__file__).parent/'obj-0.4.0'
NEW = A.ASSETS
NAMES = A.BLOCKOUTS+A.AUTHORED+['box-sight.obj']


def stats(path):
    pos, tex, nrm, faces, idx, tris, hints = A.painted_set(path.read_text())
    _, kinds = A.metric_uvs(pos, tris, hints)
    uvs = [[tex[c[1]] for c in faces[k][1]] for k in idx]
    rows = A.measure(pos, tris, uvs, kinds)
    # Per triangle: density (UV per metre) and stretch, area weighted.
    tri = []
    for t, uv in enumerate(uvs):
        m = A.tri_metric([pos[i] for i in tris[t]], uv)
        if m[0] > 1e-10:
            tri.append((m[0], math.sqrt(m[1]/m[0]), m[2]))
    total = sum(x[0] for x in tri)

    def pct(key, q):
        acc = 0.0
        for x in sorted(tri, key=key):
            acc += x[0]
            if acc >= q*total:
                return key(x)
        return key(tri[-1])

    out = dict(d10=pct(lambda x: x[1], .1), d50=pct(lambda x: x[1], .5), d90=pct(lambda x: x[1], .9),
               stretched=sum(x[0] for x in tri if x[2] >= 1.05)/total,
               off=sum(x[0] for x in tri if abs(x[1]-1) > 0.01)/total, kinds={})
    for kind in ('plane', 'cylinder', 'cone'):
        r = [x for x in rows if x['kind'] == kind]
        if r:
            out['kinds'][kind] = (len(r), min(x['ratio'] for x in r), max(x['ratio'] for x in r), max(x['stretch'] for x in r))
    return out


def line(tag, s):
    parts = [f"{tag}: UV/m p10/p50/p90 {s['d10']:.2f}/{s['d50']:.2f}/{s['d90']:.2f}; "
             f"oppervlak met stretch>=1,05 {100*s['stretched']:.1f}%; dichtheid buiten 1,00+-1% {100*s['off']:.1f}%"]
    for kind, (n, lo, hi, st) in s['kinds'].items():
        parts.append(f"{kind} n={n} UV/3D {lo:.3f}..{hi:.3f} stretch max {st:.3f}")
    return '; '.join(parts)


def main():
    lines = [f"Drempels: vlak {A.ANGLE_DEG} graden, cilinder/kegel-keten {A.CHAIN_DEG} graden, as-tolerantie 4 graden, "
             f"cirkel {A.CIRCLE_TOL*100:.0f}%, dichtheid {A.DENSITY} UV/m; grenzen {A.LIMITS}", '']
    for name in NAMES:
        lines.append(name)
        lines.append('  ' + line('voor (0.4.0)', stats(OLD/name)))
        lines.append('  ' + line('na   (0.4.1)', stats(NEW/name)))
    text = '\n'.join(lines)+'\n'
    (Path(__file__).parent/'UV-METRICS-0.4.1.txt').write_text(text, encoding='utf-8')
    print(text)


if __name__ == '__main__':
    main()
