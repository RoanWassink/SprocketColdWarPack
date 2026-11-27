"""Build Commander Panoramic Sight STLs from OpenSCAD, convert to metric-UV OBJ, and render previews."""
from pathlib import Path
import hashlib
import math
import struct
import subprocess

HERE = Path(__file__).resolve().parent
OPENSCAD = Path(r"C:\Program Files\OpenSCAD\openscad.com")
SCAD = HERE / "commander-sight.scad"
VIEW_SCAD = HERE / "view-commander-sight.scad"
STL_DIR = HERE / "stl"
RENDERS_DIR = HERE / "renders"
OBJ_OUT = HERE / "commander-sight.obj"
MESH_CHECK = HERE / "MESH-CHECK.txt"


def run(cmd):
    print("+", " ".join(str(c) for c in cmd))
    subprocess.run(cmd, check=True)


def export_stls():
    STL_DIR.mkdir(parents=True, exist_ok=True)
    for part in ("housing", "glass"):
        run([str(OPENSCAD), "-o", str(STL_DIR / f"{part}.stl"), "-D", f'part="{part}"', str(SCAD)])


def read_stl_triangles(path: Path):
    raw = path.read_bytes()
    if len(raw) >= 84:
        count = struct.unpack_from("<I", raw, 80)[0]
        if 84 + count * 50 == len(raw):
            tris = []
            offset = 84
            for _ in range(count):
                floats = struct.unpack_from("<12f", raw, offset)
                offset += 50
                tris.append((floats[3:6], floats[6:9], floats[9:12]))
            return tris
    tris, cur = [], []
    for line in raw.decode("utf-8", errors="replace").splitlines():
        s = line.strip()
        if s.startswith("vertex "):
            parts = s.split()
            cur.append((float(parts[1]), float(parts[2]), float(parts[3])))
            if len(cur) == 3:
                tris.append(tuple(cur))
                cur = []
    return tris


def scad_to_unity(v):
    return (v[0] * 0.001, v[2] * 0.001, -v[1] * 0.001)


def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def norm(v):
    l = math.sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2])
    return (0.0, 1.0, 0.0) if l < 1e-12 else (v[0]/l, v[1]/l, v[2]/l)


def metric_uv(p, n):
    ref = (0.0, 1.0, 0.0) if abs(n[1]) < 0.9 else (1.0, 0.0, 0.0)
    u_axis = norm(cross(ref, n))
    v_axis = norm(cross(n, u_axis))
    return (p[0]*u_axis[0] + p[1]*u_axis[1] + p[2]*u_axis[2], p[0]*v_axis[0] + p[1]*v_axis[1] + p[2]*v_axis[2])


def convert_to_obj():
    materials = [("Housing", STL_DIR / "housing.stl"), ("Glass", STL_DIR / "glass.stl")]
    positions, pos_map, uvs, normals, norm_map = [], {}, [], [], {}
    faces = {name: [] for name, _ in materials}

    def add_pos(p):
        key = (round(p[0], 6), round(p[1], 6), round(p[2], 6))
        idx = pos_map.get(key)
        if idx is None:
            idx = len(positions) + 1
            pos_map[key] = idx
            positions.append(key)
        return idx

    def add_norm(n):
        key = (round(n[0], 6), round(n[1], 6), round(n[2], 6))
        idx = norm_map.get(key)
        if idx is None:
            idx = len(normals) + 1
            norm_map[key] = idx
            normals.append(key)
        return idx

    for mat_name, stl_path in materials:
        for v0, v1, v2 in read_stl_triangles(stl_path):
            p0, p1, p2 = scad_to_unity(v0), scad_to_unity(v1), scad_to_unity(v2)
            n = norm(cross(sub(p1, p0), sub(p2, p0)))
            ni = add_norm(n)
            corners = []
            for p in (p0, p1, p2):
                pi = add_pos(p)
                uv = metric_uv(p, n)
                uvs.append((round(uv[0], 6), round(uv[1], 6)))
                corners.append((pi, len(uvs), ni))
            faces[mat_name].append(corners)

    fmt = lambda x: format(0.0 if x == 0 else x, ".6g")
    lines = ["# Commander Panoramic Sight; metres; +Z front (lenses); Y up; 1 UV/m metric UVs."]
    for x, y, z in positions: lines.append(f"v {fmt(x)} {fmt(y)} {fmt(z)}")
    for u, v in uvs: lines.append(f"vt {fmt(u)} {fmt(v)}")
    for nx, ny, nz in normals: lines.append(f"vn {fmt(nx)} {fmt(ny)} {fmt(nz)}")
    for mat_name, _ in materials:
        lines.append(f"usemtl {mat_name}")
        for tri in faces[mat_name]:
            lines.append("f " + " ".join(f"{pi}/{ti}/{ni}" for pi, ti, ni in tri))

    OBJ_OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    xs, ys, zs = [p[0] for p in positions], [p[1] for p in positions], [p[2] for p in positions]
    sha = hashlib.sha256(OBJ_OUT.read_bytes()).hexdigest().upper()
    total_tris = sum(len(v) for v in faces.values())
    check_text = (
        f"file: commander-sight.obj\n"
        f"sha256: {sha}\n"
        f"positions: {len(positions)}\n"
        f"vertices_expanded: {total_tris * 3}\n"
        f"triangles_total: {total_tris}\n"
        f"triangles_housing: {len(faces['Housing'])}\n"
        f"triangles_glass: {len(faces['Glass'])}\n"
        f"bounds_x: [{min(xs):.4f}, {max(xs):.4f}] ({max(xs)-min(xs):.4f} m)\n"
        f"bounds_y: [{min(ys):.4f}, {max(ys):.4f}] ({max(ys)-min(ys):.4f} m)\n"
        f"bounds_z: [{min(zs):.4f}, {max(zs):.4f}] ({max(zs)-min(zs):.4f} m)\n"
    )
    MESH_CHECK.write_text(check_text, encoding="utf-8")
    print(check_text)


def render_previews():
    RENDERS_DIR.mkdir(parents=True, exist_ok=True)
    views = [
        ("front-oblique.png", "0,0,165,68,0,28,900", VIEW_SCAD),
        ("rear-oblique.png", "0,0,165,68,0,208,900", VIEW_SCAD),
        ("side.png", "0,0,165,82,0,90,900", VIEW_SCAD),
        ("front.png", "0,0,165,82,0,0,850", VIEW_SCAD),
    ]
    for name, cam, scad in views:
        run([
            str(OPENSCAD), "-o", str(RENDERS_DIR / name),
            "--imgsize=1200,800", "--projection=p", "--colorscheme=Tomorrow Night",
            f"--camera={cam}", "--viewall", "--autocenter", str(scad),
        ])


if __name__ == "__main__":
    export_stls()
    convert_to_obj()
    render_previews()
