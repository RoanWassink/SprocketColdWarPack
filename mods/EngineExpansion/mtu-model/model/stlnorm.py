"""Rewrite STL files in a canonical form, so the same geometry always gives the same bytes.

OpenSCAD 2021.01 writes the same triangles in a different order on each export, so its raw
STL hash is not reproducible. This keeps every triangle and its winding, starts each triangle
at its smallest vertex, sorts the triangles and writes ASCII STL with fixed number formatting.
(Same method as the approved trunnion model.)
Usage: py -I stlnorm.py <file.stl> [...]   (rewrites in place)
"""
import sys


def fmt(x):
    s = '%.4f' % x
    s = s.rstrip('0').rstrip('.')
    return '0' if s in ('-0', '') else s


def normalize(path):
    tris, verts = [], []
    for line in open(path, encoding='ascii'):
        t = line.split()
        if t and t[0] == 'vertex':
            verts.append(tuple(round(float(v), 4) + 0.0 for v in t[1:4]))
            if len(verts) == 3:
                i = verts.index(min(verts))
                tris.append(tuple(verts[i:] + verts[:i])); verts = []
    tris.sort()
    out = ['solid mtu_powerpack']
    for a, b, c in tris:
        u = [b[i] - a[i] for i in range(3)]; v = [c[i] - a[i] for i in range(3)]
        n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
        L = sum(x * x for x in n) ** 0.5 or 1.0
        out.append('facet normal ' + ' '.join(fmt(x / L) for x in n))
        out.append(' outer loop')
        out.extend('  vertex ' + ' '.join(fmt(x) for x in p) for p in (a, b, c))
        out.append(' endloop')
        out.append('endfacet')
    out.append('endsolid mtu_powerpack')
    with open(path, 'w', encoding='ascii', newline='\n') as f:
        f.write('\n'.join(out) + '\n')
    return len(tris)


if __name__ == '__main__':
    for p in sys.argv[1:]:
        print(p, normalize(p))
