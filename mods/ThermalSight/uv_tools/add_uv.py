"""Deterministic metric (1 UV per metre) UVs for painted Thermal Sight meshes (0.4.1).

Run with   py -I uv_tools/add_uv.py            (writes package OBJs + manifest)
           py -I uv_tools/add_uv.py --check    (verifies bytes, geometry, hashes and UV metrics)

Method (replaces the 0.3.7 per-triangle box projection):
  * painted triangles are grouped into flat faces: edge-connected triangles whose normal is
    within ANGLE_DEG of the seed normal;
  * smooth chains of faces (dihedral < CHAIN_DEG) are, in this order: projected onto their mean
    plane when all normals are within NEAR_FLAT_DEG (shallow caps); unrolled as a cylinder when the
    normals are perpendicular to one axis and the vertices lie on one circle (exact prism
    unrolling: u = cumulative polygon width, v = position along the axis); or developed as a cone
    when the facet planes meet in one apex (polar: slant distance, cumulative apex angle).
    Coaxial stacks are split into bands first. Every development is validated against LIMITS;
  * every other face is projected into its own plane with an orthonormal basis built from its
    normal and a fixed world 'up' (Y; X for near-horizontal faces). UV = world position in that
    basis, so the offset follows the world position and seams continue where possible;
  * no per-triangle or per-face normalisation; positions, normals, counts, bounds and usemtl
    order stay identical. Fixed (glass/dark) surfaces keep their previous UVs.
"""
from pathlib import Path
import hashlib
import json
import math
import sys

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'package/BepInEx/plugins/SprocketThermalSight/assets'
ORIGINAL = Path(__file__).parent / 'original'
# Files generated from bare blockouts (no vt/vn): flat face normals, one vt per corner.
BLOCKOUTS = ['thermal-head.obj', 'thermal-head-vertical.obj', 'automatic-lrf.obj',
             'lrf-concept.obj', 'optical-rangefinder.obj']
# Author meshes: keep v, vn and fixed-surface vt; replace only the painted (housing) vt.
AUTHORED = ['fcs-console.obj', 't72-style-sight.obj', 'ir-searchlight.obj']
# box-sight.obj is converted by box-sight/convert_box_sight.py, which calls convert() below.
PAINT = {'Paint', 'PaintLight', 'Mount', 'Tube', 'Housing', 'housing', 'paint'}

ANGLE_DEG = 2.0          # flat-face grouping threshold (adjustable)
CHAIN_DEG = 50.0         # max dihedral between faces of one smooth chain (cylinder/cone candidate)
AXIS_TOL = math.sin(math.radians(4.0))   # normals must be this perpendicular to the axis
CIRCLE_TOL = 0.04        # max |r - R| / R for cylinder vertices
MIN_SPAN_DEG = 30.0      # minimum angular span of a cylinder
BAND_GAP_DEG = 2.0       # coaxial stacks: split bands where the normal-to-axis angle jumps by more
NEAR_FLAT_DEG = 10.0    # smooth chains within this angle of their mean normal: one plane projection
DENSITY = 1.0            # UV units per metre (native plates: UVSettings.Default)
LIMITS = dict(plane_area=0.02, plane_stretch=1.05, cyl_area=0.03, cyl_stretch=1.12)


def sha(data):
    return hashlib.sha256(data).hexdigest()


# ---------------------------------------------------------------- vector helpers
def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def mul(a, s): return (a[0]*s, a[1]*s, a[2]*s)
def dot(a, b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def norm(a): return math.sqrt(dot(a, a))


def unit(a):
    n = norm(a)
    return (a[0]/n, a[1]/n, a[2]/n) if n > 1e-15 else (0.0, 0.0, 0.0)


# ---------------------------------------------------------------- OBJ parsing
def parse(text):
    """Returns positions, texcoords, normals and faces (material, [(v, vt, vn)] zero-based/None)."""
    pos, tex, nrm, faces = [], [], [], []
    material = 'Paint'
    # Files without usemtl (IR searchlight) name their surfaces with o/g, like its runtime reader.
    named_by_object = not any(l.startswith('usemtl ') for l in text.splitlines())
    for line in text.splitlines():
        f = line.split()
        if not f:
            continue
        if f[0] == 'v':
            pos.append(tuple(map(float, f[1:4])))
        elif f[0] == 'vt':
            tex.append(tuple(map(float, f[1:3])))
        elif f[0] == 'vn':
            nrm.append(tuple(map(float, f[1:4])))
        elif f[0] == 'usemtl' or (named_by_object and f[0] in ('o', 'g') and len(f) == 2):
            material = f[1]
        elif f[0] == 'f':
            assert len(f) == 4, 'OBJ must be triangulated'
            corners = []
            for token in f[1:]:
                p = token.split('/') + ['', '']
                corners.append(tuple(int(x)-1 if x else None for x in p[:3]))
            faces.append((material, corners))
    return pos, tex, nrm, faces


def geometry(text):
    """Geometry identity: positions, face position indices, materials, normals actually used."""
    pos, _, _, faces = parse(text)
    order = []
    for line in text.splitlines():
        f = line.split()
        if f and f[0] == 'usemtl':
            order.append(f[1])
    return pos, [(m, tuple(c[0] for c in cs)) for m, cs in faces], order


# ---------------------------------------------------------------- UV generation
def triangle_frames(pos, tris, hints):
    """Per triangle: oriented unit normal and area. hints: per-triangle stored normal or None."""
    normals, areas = [], []
    for t, h in zip(tris, hints):
        a, b, c = (pos[i] for i in t)
        n = cross(sub(b, a), sub(c, a))
        area = norm(n) / 2
        n = unit(n)
        if h is not None and dot(n, h) < 0:
            n = mul(n, -1)       # authored winding disagrees with the stored normal: follow the normal
        if area < 1e-12:
            n = unit(h) if h is not None and norm(h) > 0 else (0.0, 0.0, 1.0)
        normals.append(n)
        areas.append(area)
    return normals, areas


def edges_of(pos, tris):
    key = lambda i: tuple(round(x, 6) for x in pos[i])
    owners = {}
    for ti, t in enumerate(tris):
        for k in range(3):
            e = tuple(sorted((key(t[k]), key(t[(k+1) % 3]))))
            owners.setdefault(e, []).append(ti)
    adj = [set() for _ in tris]
    for ts in owners.values():
        for x in ts:
            for y in ts:
                if x != y:
                    adj[x].add(y)
    return [sorted(a) for a in adj]


def flat_groups(normals, areas, adj, angle_deg):
    limit = math.cos(math.radians(angle_deg))
    group = [-1]*len(normals)
    groups = []
    for seed in range(len(normals)):
        if group[seed] >= 0:
            continue
        gid = len(groups)
        members, queue = [seed], [seed]
        group[seed] = gid
        while queue:
            t = queue.pop()
            for nb in adj[t]:
                if group[nb] < 0 and (dot(normals[nb], normals[seed]) >= limit or areas[nb] < 1e-12):
                    group[nb] = gid
                    members.append(nb)
                    queue.append(nb)
        groups.append(sorted(members))
    return group, groups


def group_normal(members, normals, areas):
    s = (0.0, 0.0, 0.0)
    for t in members:
        s = add(s, mul(normals[t], max(areas[t], 1e-12)))
    return unit(s)


def plane_basis(n):
    """Orthonormal (U, V) with cross(U, V) = n; V follows world up (Y) where possible."""
    if abs(n[1]) < 0.95:
        v = unit(sub((0.0, 1.0, 0.0), mul(n, n[1])))
        u = cross(v, n)
    else:
        u = unit(sub((1.0, 0.0, 0.0), mul(n, n[0])))
        v = cross(n, u)
    return u, v


def smallest_eigenvector(m):
    """Symmetric 3x3: eigenvector of the smallest eigenvalue (Jacobi rotations, deterministic)."""
    a = [row[:] for row in m]
    vecs = [[1.0 if i == j else 0.0 for j in range(3)] for i in range(3)]
    for _ in range(50):
        p, q = max(((0, 1), (0, 2), (1, 2)), key=lambda pq: abs(a[pq[0]][pq[1]]))
        if abs(a[p][q]) < 1e-18:
            break
        theta = (a[q][q]-a[p][p]) / (2*a[p][q])
        t = (1 if theta >= 0 else -1) / (abs(theta)+math.sqrt(theta*theta+1))
        c = 1/math.sqrt(t*t+1)
        s = t*c
        for k in range(3):
            akp, akq = a[k][p], a[k][q]
            a[k][p], a[k][q] = c*akp-s*akq, s*akp+c*akq
        for k in range(3):
            apk, aqk = a[p][k], a[q][k]
            a[p][k], a[q][k] = c*apk-s*aqk, s*apk+c*aqk
        for k in range(3):
            vkp, vkq = vecs[k][p], vecs[k][q]
            vecs[k][p], vecs[k][q] = c*vkp-s*vkq, s*vkp+c*vkq
    i = min(range(3), key=lambda j: a[j][j])
    v = unit((vecs[0][i], vecs[1][i], vecs[2][i]))
    # Deterministic sign: largest component positive.
    k = max(range(3), key=lambda j: abs(v[j]))
    return v if v[k] > 0 else mul(v, -1)


def fit_cylinder(tris_idx, tris, pos, normals, areas):
    """Returns cylinder dict or None."""
    m = [[0.0]*3 for _ in range(3)]
    for t in tris_idx:
        n, w = normals[t], max(areas[t], 1e-12)
        for i in range(3):
            for j in range(3):
                m[i][j] += w*n[i]*n[j]
    axis = smallest_eigenvector(m)
    if any(abs(dot(normals[t], axis)) > AXIS_TOL for t in tris_idx if areas[t] >= 1e-12):
        return None
    ref = (1.0, 0.0, 0.0) if abs(axis[1]) > 0.9 else (0.0, 1.0, 0.0)
    e1 = unit(sub(ref, mul(axis, dot(ref, axis))))
    e2 = cross(axis, e1)
    verts = sorted({i for t in tris_idx for i in tris[t]})
    pts = [(dot(pos[i], e1), dot(pos[i], e2)) for i in verts]
    # Kasa circle fit: x^2+y^2 + D x + E y + F = 0.
    s = [[0.0]*3 for _ in range(3)]
    r = [0.0]*3
    for x, y in pts:
        row = (x, y, 1.0)
        z = -(x*x+y*y)
        for i in range(3):
            r[i] += row[i]*z
            for j in range(3):
                s[i][j] += row[i]*row[j]
    sol = solve3(s, r)
    if sol is None:
        return None
    cx, cy = -sol[0]/2, -sol[1]/2
    rad2 = cx*cx+cy*cy-sol[2]
    if rad2 <= 0:
        return None
    radius = math.sqrt(rad2)
    if radius > 2.0 or any(abs(math.hypot(x-cx, y-cy)-radius) > CIRCLE_TOL*radius for x, y in pts):
        return None
    angles = sorted(math.atan2(dot(normals[t], e2), dot(normals[t], e1)) for t in tris_idx if areas[t] >= 1e-12)
    if not angles:
        return None
    gaps = [(angles[(k+1) % len(angles)]-angles[k]) % (2*math.pi) for k in range(len(angles))]
    if len(angles) == 1:
        gaps = [2*math.pi]
    gap_i = max(range(len(gaps)), key=lambda k: gaps[k])
    span = 2*math.pi-gaps[gap_i]
    if span < math.radians(MIN_SPAN_DEG):
        return None
    return dict(axis=axis, e1=e1, e2=e2, cx=cx, cy=cy, radius=radius, verts=verts, pts=pts, span=span,
                full=gaps[gap_i] < math.radians(100))


def solve3(a, b):
    m = [a[i][:]+[b[i]] for i in range(3)]
    for c in range(3):
        p = max(range(c, 3), key=lambda r: abs(m[r][c]))
        if abs(m[p][c]) < 1e-30:
            return None
        m[c], m[p] = m[p], m[c]
        for r in range(3):
            if r != c:
                f = m[r][c]/m[c][c]
                m[r] = [x-f*y for x, y in zip(m[r], m[c])]
    return [m[i][3]/m[i][i] for i in range(3)]


def cylinder_uvs(cyl, tri_ids, tris, pos, normals):
    """Exact prism unrolling. Returns {tri: [uv, uv, uv]}."""
    axis, e1, e2, cx, cy = cyl['axis'], cyl['e1'], cyl['e2'], cyl['cx'], cyl['cy']
    flat = dict(zip(cyl['verts'], cyl['pts']))
    angle = {i: math.atan2(y-cy, x-cx) % (2*math.pi) for i, (x, y) in flat.items()}
    # Start of the unrolled range: the vertex after the largest angular gap
    # (partial cylinders then have no seam; full ones get exactly one).
    order = sorted(flat, key=lambda i: (angle[i], i))
    gaps = [((angle[order[(k+1) % len(order)]]-angle[order[k]]) % (2*math.pi), k) for k in range(len(order))]
    _, k = max(gaps, key=lambda g: (round(g[0], 6), -g[1]))
    start = angle[order[(k+1) % len(order)]]
    rel = {}
    for i in flat:
        a = (angle[i]-start) % (2*math.pi)
        rel[i] = a-2*math.pi if a > 2*math.pi-1e-6 else a
    # Cluster equal angles; u = cumulative distance between the mean 2D points of the clusters,
    # which is the exact unrolled width of a prism.
    clusters = []
    for i in sorted(flat, key=lambda i: (rel[i], i)):
        if clusters and rel[i]-clusters[-1][0] < 1e-6:
            clusters[-1][1].append(i)
        else:
            clusters.append([rel[i], [i]])
    u_vertex, u, prev = {}, 0.0, None
    for _, members in clusters:
        p = (sum(flat[i][0] for i in members)/len(members), sum(flat[i][1] for i in members)/len(members))
        if prev is not None:
            u += math.hypot(p[0]-prev[0], p[1]-prev[1])
        for i in members:
            u_vertex[i] = u
        prev = p
    first_members = clusters[0][1]
    p0 = (sum(flat[i][0] for i in first_members)/len(first_members), sum(flat[i][1] for i in first_members)/len(first_members))
    total = u+math.hypot(p0[0]-prev[0], p0[1]-prev[1])   # polygon circumference, used across the seam
    out = {}
    for t in tri_ids:
        r = [rel[i] for i in tris[t]]
        us = [u_vertex[i] for i in tris[t]]
        if max(r)-min(r) > math.pi:      # straddles the seam: continue past the last cluster
            us = [uu+total if rr < math.pi else uu for uu, rr in zip(us, r)]
        vs = [dot(pos[i], axis) for i in tris[t]]
        # Orientation: cross(dU, dV) must follow the triangle normal (outer or inner wall).
        centre = mul(add(add(pos[tris[t][0]], pos[tris[t][1]]), pos[tris[t][2]]), 1/3)
        cxy = (dot(centre, e1)-cx, dot(centre, e2)-cy)
        radial = unit(add(mul(e1, cxy[0]), mul(e2, cxy[1])))
        sign = 1.0 if dot(normals[t], radial) >= 0 else -1.0
        out[t] = [(sign*uu*DENSITY, vv*DENSITY) for uu, vv in zip(us, vs)]
    return out


def covariance_axis(tri_ids, normals, areas, centred):
    w = [max(areas[t], 1e-12) for t in tri_ids]
    total = sum(w)
    mean = (0.0, 0.0, 0.0)
    if centred:
        for t, wt in zip(tri_ids, w):
            mean = add(mean, mul(normals[t], wt/total))
    m = [[0.0]*3 for _ in range(3)]
    for t, wt in zip(tri_ids, w):
        n = sub(normals[t], mean)
        for i in range(3):
            for j in range(3):
                m[i][j] += wt*n[i]*n[j]
    return smallest_eigenvector(m), mean


def fit_cone(tri_ids, tris, pos, normals, areas):
    """Cone (or faceted pyramid frustum) whose facet planes meet in one apex; None otherwise."""
    axis, mean = covariance_axis(tri_ids, normals, areas, True)
    live = [t for t in tri_ids if areas[t] >= 1e-12]
    if not live:
        return None
    c = sum(dot(normals[t], axis)*areas[t] for t in live)/sum(areas[t] for t in live)
    if abs(c) < AXIS_TOL or abs(c) > math.cos(math.radians(10)):
        return None
    if any(abs(dot(normals[t], axis)-c) > AXIS_TOL for t in live):
        return None
    m = [[0.0]*3 for _ in range(3)]
    r = [0.0]*3
    for t in live:
        n, w = normals[t], areas[t]
        d = dot(n, pos[tris[t][0]])
        for i in range(3):
            r[i] += w*n[i]*d
            for j in range(3):
                m[i][j] += w*n[i]*n[j]
    apex = solve3(m, r)
    if apex is None:
        return None
    apex = tuple(apex)
    verts = sorted({i for t in tri_ids for i in tris[t]})
    slant = [norm(sub(pos[i], apex)) for i in verts]
    if min(slant) < 1e-9 or max(slant) > 5.0:
        return None
    tol = 1e-4+0.05*max(slant)   # loose: the development itself is validated by region_ok()
    if any(abs(dot(normals[t], sub(pos[i], apex))) > tol for t in live for i in tris[t]):
        return None
    ref = (1.0, 0.0, 0.0) if abs(axis[1]) > 0.9 else (0.0, 1.0, 0.0)
    e1 = unit(sub(ref, mul(axis, dot(ref, axis))))
    e2 = cross(axis, e1)
    angle = {i: math.atan2(dot(sub(pos[i], apex), e2), dot(sub(pos[i], apex), e1)) % (2*math.pi) for i in verts}
    ang = sorted(angle.values())
    gaps = [(ang[(k+1) % len(ang)]-ang[k]) % (2*math.pi) for k in range(len(ang))]
    if len(ang) < 2 or 2*math.pi-max(gaps) < math.radians(MIN_SPAN_DEG):
        return None
    return dict(apex=apex, axis=axis, verts=verts, angle=angle, span=2*math.pi-max(gaps),
                half_angle=math.degrees(math.asin(min(1.0, abs(c)))))


def cone_uvs(cone, tri_ids, tris, pos, normals):
    """Exact development of a faceted cone: polar (slant distance, cumulative apex angle)."""
    apex, angle = cone['apex'], cone['angle']
    order = sorted(angle, key=lambda i: (angle[i], i))
    gaps = [((angle[order[(k+1) % len(order)]]-angle[order[k]]) % (2*math.pi), k) for k in range(len(order))]
    _, k = max(gaps, key=lambda g: (round(g[0], 6), -g[1]))
    start = angle[order[(k+1) % len(order)]]
    rel = {}
    for i in angle:
        a = (angle[i]-start) % (2*math.pi)
        rel[i] = a-2*math.pi if a > 2*math.pi-1e-6 else a
    clusters = []
    for i in sorted(angle, key=lambda i: (rel[i], i)):
        if clusters and rel[i]-clusters[-1][0] < 1e-6:
            clusters[-1][1].append(i)
        else:
            clusters.append([rel[i], [i]])

    def direction(members):
        s = (0.0, 0.0, 0.0)
        for i in members:
            s = add(s, unit(sub(pos[i], apex)))
        return unit(s)

    phi_vertex, phi, prev = {}, 0.0, None
    for _, members in clusters:
        d = direction(members)
        if prev is not None:
            phi += math.acos(max(-1.0, min(1.0, dot(prev, d))))
        for i in members:
            phi_vertex[i] = phi
        prev = d
    total = phi+math.acos(max(-1.0, min(1.0, dot(prev, direction(clusters[0][1])))))
    out = {}
    for t in tri_ids:
        r = [rel[i] for i in tris[t]]
        ph = [phi_vertex[i] for i in tris[t]]
        if max(r)-min(r) > math.pi:
            ph = [x+total if rr < math.pi else x for x, rr in zip(ph, r)]
        out[t] = [(norm(sub(pos[i], apex))*math.cos(x)*DENSITY, norm(sub(pos[i], apex))*math.sin(x)*DENSITY)
                  for i, x in zip(tris[t], ph)]
    # Handedness: mirror the whole development if most of its area is mirrored against the normals.
    score = 0.0
    for t in tri_ids:
        a, b, c = (pos[i] for i in tris[t])
        g = cross(sub(b, a), sub(c, a))
        q = out[t]
        det = (q[1][0]-q[0][0])*(q[2][1]-q[0][1])-(q[2][0]-q[0][0])*(q[1][1]-q[0][1])
        score += det*(1 if dot(g, normals[t]) >= 0 else -1)
    if score < 0:
        out = {t: [(u, -v) for u, v in q] for t, q in out.items()}
    return out


def region_ok(tri_ids, tris, pos, uv):
    a3 = auv = 0.0
    for t in tri_ids:
        m = tri_metric([pos[i] for i in tris[t]], uv[t])
        a3 += m[0]
        auv += m[1]
        if m[0] >= 1e-8 and m[2] >= LIMITS['cyl_stretch']:
            return False
    return a3 > 0 and abs(auv/a3-1) <= LIMITS['cyl_area']


def develop(tri_ids, tris, pos, normals, areas):
    """Near-flat projection, cylinder or cone development of one smooth chain, validated; None if nothing fits."""
    mean = group_normal(tri_ids, normals, areas)
    live = [t for t in tri_ids if areas[t] >= 1e-12]
    if live and min(dot(normals[t], mean) for t in live) >= math.cos(math.radians(NEAR_FLAT_DEG)):
        # Shallow domes/cones (e.g. a rear cap): one projection onto the mean plane, distortion
        # at most 1/cos(NEAR_FLAT_DEG) and without seams between the facets.
        u, v = plane_basis(mean)
        uv = {t: [(dot(pos[i], u)*DENSITY, dot(pos[i], v)*DENSITY) for i in tris[t]] for t in tri_ids}
        a3 = sum(tri_metric([pos[i] for i in tris[t]], uv[t])[0] for t in tri_ids)
        auv = sum(tri_metric([pos[i] for i in tris[t]], uv[t])[1] for t in tri_ids)
        worst = max(tri_metric([pos[i] for i in tris[t]], uv[t])[2] for t in live)
        if a3 > 0 and abs(auv/a3-1) <= LIMITS['plane_area'] and worst < LIMITS['plane_stretch']:
            return 'plane', uv, {}
    cyl = fit_cylinder(tri_ids, tris, pos, normals, areas)
    if cyl is not None:
        uv = cylinder_uvs(cyl, tri_ids, tris, pos, normals)
        if region_ok(tri_ids, tris, pos, uv):
            return 'cylinder', uv, dict(radius=cyl['radius'], span_deg=math.degrees(cyl['span']))
    cone = fit_cone(tri_ids, tris, pos, normals, areas)
    if cone is not None:
        uv = cone_uvs(cone, tri_ids, tris, pos, normals)
        if region_ok(tri_ids, tris, pos, uv):
            return 'cone', uv, dict(half_angle_deg=cone['half_angle'], span_deg=math.degrees(cone['span']))
    return None


def metric_uvs(pos, tris, hints):
    """Returns per-triangle corner UVs plus the group list [(kind, members)]."""
    normals, areas = triangle_frames(pos, tris, hints)
    adj = edges_of(pos, tris)
    group, groups = flat_groups(normals, areas, adj, ANGLE_DEG)
    gnormal = [group_normal(g, normals, areas) for g in groups]
    # Chains of faces with small dihedral angles are cylinder candidates.
    chain_limit = math.cos(math.radians(CHAIN_DEG))
    gadj = [set() for _ in groups]
    for t, nbs in enumerate(adj):
        for nb in nbs:
            if group[t] != group[nb] and dot(gnormal[group[t]], gnormal[group[nb]]) >= chain_limit:
                gadj[group[t]].add(group[nb])
    chain = [-1]*len(groups)
    chains = []
    for g in range(len(groups)):
        if chain[g] >= 0:
            continue
        members, queue = [g], [g]
        chain[g] = len(chains)
        while queue:
            x = queue.pop()
            for y in sorted(gadj[x]):
                if chain[y] < 0:
                    chain[y] = chain[g]
                    members.append(y)
                    queue.append(y)
        chains.append(sorted(members))
    uvs = [None]*len(tris)
    kinds = []
    def components(gs):
        gs, seen, out = set(gs), set(), []
        for g in sorted(gs):
            if g in seen:
                continue
            comp, queue = [g], [g]
            seen.add(g)
            while queue:
                x = queue.pop()
                for y in sorted(gadj[x]):
                    if y in gs and y not in seen:
                        seen.add(y)
                        comp.append(y)
                        queue.append(y)
            out.append(sorted(comp))
        return out

    for members in chains:
        if len(members) < 2:
            continue
        tri_ids = sorted(t for g in members for t in groups[g])
        found = develop(tri_ids, tris, pos, normals, areas)
        if found is not None:
            parts = [(tri_ids, found)]
        else:
            # Coaxial stacks (cylinder + bevel cone + cylinder): split the chain by the angle between
            # each face normal and the common axis, then develop every connected band separately.
            axis, _ = covariance_axis(tri_ids, normals, areas, True)
            # Bands: faces sorted by their angle to the axis, split where consecutive angles differ
            # by more than BAND_GAP_DEG.
            tilt = {g: math.degrees(math.acos(max(-1.0, min(1.0, dot(gnormal[g], axis))))) for g in members}
            bands, key, last = {}, 0, None
            for g in sorted(members, key=lambda g: (tilt[g], g)):
                if last is not None and tilt[g]-last > BAND_GAP_DEG:
                    key += 1
                bands.setdefault(key, []).append(g)
                last = tilt[g]
            parts = []
            for key in sorted(bands):
                for comp in components(bands[key]):
                    if len(comp) < 2:
                        continue
                    sub_ids = sorted(t for g in comp for t in groups[g])
                    got = develop(sub_ids, tris, pos, normals, areas)
                    if got is not None:
                        parts.append((sub_ids, got))
        for ids, (kind, uv, info) in parts:
            for t, q in uv.items():
                uvs[t] = q
            kinds.append((kind, ids, info))
    for g, members in enumerate(groups):
        rest = [t for t in members if uvs[t] is None]
        if not rest:
            continue
        u, v = plane_basis(gnormal[g])
        for t in rest:
            uvs[t] = [(dot(pos[i], u)*DENSITY, dot(pos[i], v)*DENSITY) for i in tris[t]]
        kinds.append(('plane', rest, {}))
    return uvs, kinds


# ---------------------------------------------------------------- metrics
def tri_metric(p, uv):
    """(area3d, area_uv, stretch) for one triangle; stretch = sigma_max / sigma_min."""
    a, b, c = p
    e1, e2 = sub(b, a), sub(c, a)
    area3 = norm(cross(e1, e2))/2
    if area3 < 1e-10:
        return area3, 0.0, 1.0
    x = unit(e1)
    y = unit(sub(e2, mul(x, dot(e2, x))))
    p1, p2 = (dot(e1, x), 0.0), (dot(e2, x), dot(e2, y))
    q1 = (uv[1][0]-uv[0][0], uv[1][1]-uv[0][1])
    q2 = (uv[2][0]-uv[0][0], uv[2][1]-uv[0][1])
    det = p1[0]*p2[1]-p2[0]*p1[1]
    # Jacobian J with J @ p = q.
    j00 = (q1[0]*p2[1]-q2[0]*p1[1])/det
    j01 = (q2[0]*p1[0]-q1[0]*p2[0])/det
    j10 = (q1[1]*p2[1]-q2[1]*p1[1])/det
    j11 = (q2[1]*p1[0]-q1[1]*p2[0])/det
    areauv = abs(q1[0]*q2[1]-q1[1]*q2[0])/2
    s = j00*j00+j01*j01+j10*j10+j11*j11
    d = abs(j00*j11-j01*j10)
    disc = math.sqrt(max(s*s/4-d*d, 0.0))
    smax, smin = math.sqrt(s/2+disc), math.sqrt(max(s/2-disc, 0.0))
    return area3, areauv, (smax/smin if smin > 1e-12 else float('inf'))


def measure(pos, tris, uvs, kinds):
    """Per group metrics; returns summary rows."""
    rows = []
    for kind, members, info in kinds:
        a3 = auv = 0.0
        worst = 1.0
        for t in members:
            m = tri_metric([pos[i] for i in tris[t]], uvs[t])
            a3 += m[0]
            auv += m[1]
            if m[0] >= 1e-8:
                worst = max(worst, m[2])
        if a3 < 1e-8:
            continue
        rows.append(dict(kind=kind, triangles=len(members), area=a3, ratio=auv/a3, stretch=worst, **info))
    return rows


def summary(rows):
    out = {}
    for kind in ('plane', 'cylinder', 'cone'):
        r = [x for x in rows if x['kind'] == kind]
        if not r:
            continue
        out[kind] = dict(groups=len(r), triangles=sum(x['triangles'] for x in r),
                         ratio_min=min(x['ratio'] for x in r), ratio_max=max(x['ratio'] for x in r),
                         stretch_max=max(x['stretch'] for x in r))
    return out


def within_limits(s):
    ok = True
    if 'plane' in s:
        ok &= abs(s['plane']['ratio_min']-1) <= LIMITS['plane_area'] and abs(s['plane']['ratio_max']-1) <= LIMITS['plane_area']
        ok &= s['plane']['stretch_max'] < LIMITS['plane_stretch']
    for kind in ('cylinder', 'cone'):
        if kind in s:
            ok &= abs(s[kind]['ratio_min']-1) <= LIMITS['cyl_area'] and abs(s[kind]['ratio_max']-1) <= LIMITS['cyl_area']
            ok &= s[kind]['stretch_max'] < LIMITS['cyl_stretch']
    return ok


# ---------------------------------------------------------------- file conversion
def fmt(x):
    s = format(x, '.9g')
    return '0' if s == '-0' else s


def old_box_uv(a, b, c):
    """0.3.7 box projection, kept only for fixed (unpainted) blockout surfaces."""
    ab, ac = sub(b, a), sub(c, a)
    n = unit(cross(ab, ac))
    axis = max(range(3), key=lambda i: abs(n[i]))
    u, v = ((1, 2), (2, 0), (0, 1))[axis]
    sign = 1 if n[axis] > 0 else -1
    return n, [(p[u]*sign, p[v]) for p in (a, b, c)]


def painted_set(text):
    pos, tex, nrm, faces = parse(text)
    idx = [k for k, (m, _) in enumerate(faces) if m in PAINT]
    tris = [tuple(faces[k][1][j][0] for j in range(3)) for k in idx]
    hints = []
    for k in idx:
        cs = faces[k][1]
        if all(c[2] is not None for c in cs) and nrm:
            hints.append(unit(add(add(nrm[cs[0][2]], nrm[cs[1][2]]), nrm[cs[2][2]])))
        else:
            hints.append(None)
    return pos, tex, nrm, faces, idx, tris, hints


def convert(name, text):
    pos, tex, nrm, faces, idx, tris, hints = painted_set(text)
    uvs, kinds = metric_uvs(pos, tris, hints)
    new_uv = dict(zip(idx, uvs))
    lines = text.splitlines()
    out = []
    if name in BLOCKOUTS:
        vt, vn = [], []
        k = 0
        for line in lines:
            f = line.split()
            if f and f[0] == 'f':
                ids = [int(t.split('/')[0]) for t in f[1:]]
                a, b, c = (pos[i-1] for i in ids)
                n, box = old_box_uv(a, b, c)
                uv = new_uv.get(k, box)
                vn.append('vn ' + ' '.join(fmt(x) for x in n))
                corners = []
                for index, (u, v) in zip(ids, uv):
                    vt.append(f'vt {fmt(u)} {fmt(v)}')
                    corners.append(f'{index}/{len(vt)}/{len(vn)}')
                out.append('f ' + ' '.join(corners))
                k += 1
            elif not f or f[0] not in ('vt', 'vn'):
                out.append(line)
        first = next(i for i, line in enumerate(out) if line.startswith('f '))
        out[first:first] = vt + vn
    else:
        # Compact vt list, one per corner in face order: Housing gets metric UVs, other surfaces
        # keep their authored values. v and vn lines and indices are untouched.
        vt = []
        k = 0
        face_lines = []
        for line in lines:
            f = line.split()
            if f and f[0] == 'f':
                material, corners = faces[k]
                tokens = []
                for j, (vi, ti, ni) in enumerate(corners):
                    u, v = new_uv[k][j] if k in new_uv else tex[ti]
                    vt.append(f'vt {fmt(u)} {fmt(v)}')
                    tokens.append(f'{vi+1}/{len(vt)}/{ni+1}')
                face_lines.append('f ' + ' '.join(tokens))
                k += 1
        k = 0
        inserted = False
        for line in lines:
            f = line.split()
            if f and f[0] == 'vt':
                if not inserted:
                    out.extend(vt)
                    inserted = True
            elif f and f[0] == 'f':
                assert inserted, 'vt block must precede faces'
                out.append(face_lines[k])
                k += 1
            else:
                out.append(line)
    data = ('\n'.join(out)+'\n').encode()
    return data, measure(pos, tris, uvs, kinds)


def verify_file(name, source, generated):
    s_pos, s_faces, s_order = geometry(source.decode())
    g_pos, g_faces, g_order = geometry(generated.decode())
    assert (s_pos, s_faces, s_order) == (g_pos, g_faces, g_order), name+' geometry/order changed'
    pos, tex, nrm, faces = parse(generated.decode())
    assert all(c[1] is not None and c[2] is not None for _, cs in faces for c in cs), name+' face without vt/vn'
    assert all(math.isfinite(x) for t in tex for x in t), name+' non-finite UV'
    assert all(abs(x) <= 100 for t in tex for x in t), name+' UV beyond reader range'
    if name not in BLOCKOUTS:
        _, s_tex, s_nrm, s_f = parse(source.decode())
        assert nrm == s_nrm, name+' normals changed'
        for (m, a), (_, b) in zip(s_f, faces):
            assert [c[2] for c in a] == [c[2] for c in b], name+' normal indices changed'
            if m not in PAINT:
                assert [s_tex[c[1]] for c in a] == [tex[c[1]] for c in b], name+' fixed-surface UV changed'
    return pos, faces


def main():
    check = '--check' in sys.argv
    records, report = {}, []
    generator = sha(Path(__file__).read_bytes())
    failed = []
    for name in BLOCKOUTS+AUTHORED:
        source = (ORIGINAL/name).read_bytes()
        generated, rows = convert(name, source.decode())
        if check:
            assert (ASSETS/name).read_bytes() == generated, name+' output differs from generator'
        else:
            (ASSETS/name).write_bytes(generated)
        pos, faces = verify_file(name, source, generated)
        after = summary(rows)
        if not within_limits(after):
            failed.append(name)
        used = sorted({c[0] for _, cs in faces for c in cs})
        p = [pos[i] for i in used]
        bounds = [[min(v[i] for v in p) for i in range(3)], [max(v[i] for v in p) for i in range(3)]]
        order = [l.split()[1] for l in generated.decode().splitlines() if l.startswith('usemtl ')]
        record = dict(source_sha256=sha(source), output_sha256=sha(generated), generator_sha256=generator,
                      positions=len(pos), triangles=len(faces), bounds=bounds, material_order=order)
        records[name] = record
        report.append((name, after, rows))
        print(name, len(pos), len(faces), 'geometry/order/normals unchanged;',
              json.dumps(after, sort_keys=True), 'OK' if name not in failed else 'OUT OF LIMITS')
    blockouts = {k: records[k] for k in BLOCKOUTS}
    authored = {k: records[k] for k in AUTHORED}
    manifest = Path(__file__).parent/'manifest.json'
    manifest2 = Path(__file__).parent/'manifest-housing.json'
    if check:
        assert json.loads(manifest.read_text()) == blockouts, 'manifest differs'
        assert json.loads(manifest2.read_text()) == authored, 'housing manifest differs'
    else:
        manifest.write_text(json.dumps(blockouts, indent=2)+'\n')
        manifest2.write_text(json.dumps(authored, indent=2)+'\n')
    assert not failed, 'UV metrics out of limits: '+', '.join(failed)
    print('limits', json.dumps(LIMITS, sort_keys=True), 'angle_deg', ANGLE_DEG, 'density', DENSITY)


if __name__ == '__main__':
    main()
