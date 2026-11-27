import bpy, bmesh, math, mathutils, hashlib
from pathlib import Path

FBX = r"C:\Users\Roanj\Downloads\Meshy_AI_Military_Targeting_Sensor_20261127_generate.fbx"
OUT_DIR = Path(r"C:\Users\Roanj\Documents\Codex\2026-11-13\herpak\src\thermal-sight\targeting-sensor")
OBJ_OUT = OUT_DIR / "oes-op-sight.obj"
MESH_CHECK = OUT_DIR / "MESH-CHECK.txt"
RENDERS = OUT_DIR / "renders"
RENDERS.mkdir(parents=True, exist_ok=True)

TARGET_HEIGHT = 0.52  # metres
DECIMATE_RATIO = 0.015  # ~11k crisp triangles

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
assert len(meshes) == 1
obj = meshes[0]
obj.name = "OES_OP"

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

verts = [v.co for v in obj.data.vertices]
min_x, max_x = min(v.x for v in verts), max(v.x for v in verts)
min_y, max_y = min(v.y for v in verts), max(v.y for v in verts)
min_z, max_z = min(v.z for v in verts), max(v.z for v in verts)
cx, cy = (min_x + max_x) / 2.0, (min_y + max_y) / 2.0
scale = TARGET_HEIGHT / (max_z - min_z)

for v in obj.data.vertices:
    v.co.x = (v.co.x - cx) * scale
    v.co.y = (v.co.y - cy) * scale
    v.co.z = (v.co.z - min_z) * scale
obj.data.update()

dec = obj.modifiers.new("Decimate", 'DECIMATE')
dec.decimate_type = 'COLLAPSE'
dec.ratio = DECIMATE_RATIO
dec.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=dec.name)

bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bmesh.ops.triangulate(bm, faces=bm.faces)
for e in bm.edges:
    e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0.0) > math.radians(35.0))
bm.to_mesh(obj.data)
bm.free()
obj.data.update()

for p in obj.data.polygons:
    p.use_smooth = True
wn = obj.modifiers.new("WeightedNormal", 'WEIGHTED_NORMAL')
wn.keep_sharp = True
wn.weight = 100
bpy.ops.object.modifier_apply(modifier=wn.name)

bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.cube_project(cube_size=1.0)
bpy.ops.object.mode_set(mode='OBJECT')

# Measure the exact Y depth of the front armor face vs the recessed optical pocket floors!
# Front armor face is around Z in [0.30, 0.50], X in [-0.14, 0.14], normal.y < -0.8
front_ys = []
for p in obj.data.polygons:
    c = p.center
    if -0.14 <= c.x <= 0.14 and 0.30 <= c.z <= 0.50 and c.y < -0.05 and p.normal.y < -0.8:
        front_ys.append(c.y)
front_ys.sort()
# Median front armor plate Y (front-most flat face is around 20th percentile)
armor_y = front_ys[int(len(front_ys) * 0.25)]
print(f"Front face Y stats: min={front_ys[0]:.4f}, p25(armor)={armor_y:.4f}, p75={front_ys[int(len(front_ys)*0.75)]:.4f}, max={front_ys[-1]:.4f}")

# Classify exact recessed optical pocket floor faces as Glass (submesh 1)!
# Right side (X >= -0.025): front armor sits at armor_y; the 3 recessed windows sit >= 5 mm behind armor_y.
# Left side (X < -0.025): the left cheek is stepped back slightly, so we only take faces inside the large circular lens pocket!
left_ys = sorted(p.center.y for p in obj.data.polygons if -0.12 <= p.center.x <= -0.03 and 0.31 <= p.center.z <= 0.39 and p.center.y < -0.05 and p.normal.y < -0.8)
left_armor_y = left_ys[len(left_ys) // 2] if left_ys else armor_y
print(f"Left cheek armor_y={left_armor_y:.4f}, right armor_y={armor_y:.4f}")

def is_glass_face(p):
    c = p.center
    if p.normal.y > -0.75 or c.y >= -0.04:
        return False
    if c.x >= -0.025:
        return c.x <= 0.138 and 0.305 <= c.z <= 0.495 and c.y > armor_y + 0.0055
    else:
        # Large circular thermal lens on the left side (center around X=-0.072, Z=0.436, radius ~0.040)
        dx = c.x - (-0.072)
        dz = c.z - 0.436
        return (dx * dx + dz * dz) <= (0.040 * 0.040) and c.y > left_armor_y + 0.0025

# Assign preview materials so we can inspect the exact split in Blender renders
mat_h = bpy.data.materials.new("PreviewHousing")
mat_h.diffuse_color = (0.42, 0.46, 0.38, 1.0)
mat_g = bpy.data.materials.new("PreviewGlass")
mat_g.diffuse_color = (0.04, 0.09, 0.12, 1.0)
obj.data.materials.clear()
obj.data.materials.append(mat_h)
obj.data.materials.append(mat_g)

glass_poly_indices = set()
for p in obj.data.polygons:
    if is_glass_face(p):
        p.material_index = 1
        glass_poly_indices.add(p.index)
    else:
        p.material_index = 0
print(f"Split faces: Housing={len(obj.data.polygons)-len(glass_poly_indices)}, Glass={len(glass_poly_indices)}")

# Export Unity-space OBJ (Housing + Glass) with exact outward winding (p0, p1, p2) under (x, z, -y)
def to_unity(v): return (v.x, v.z, -v.y)
def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def norm(v):
    l = math.sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2])
    return (0.0, 1.0, 0.0) if l < 1e-12 else (v[0]/l, v[1]/l, v[2]/l)

positions, pos_map = [], {}
uvs, normals, norm_map = [], [], {}
faces = {"Housing": [], "Glass": []}

def add_pos(p):
    key = (round(p[0], 6), round(p[1], 6), round(p[2], 6))
    idx = pos_map.get(key)
    if idx is None:
        idx = len(positions) + 1
        pos_map[key] = idx
        positions.append(key)
    return idx

def add_norm(n):
    key = (round(n[0], 5), round(n[1], 5), round(n[2], 5))
    idx = norm_map.get(key)
    if idx is None:
        idx = len(normals) + 1
        norm_map[key] = idx
        normals.append(key)
    return idx

me = obj.data
me.calc_loop_triangles()
uv_layer = me.uv_layers.active.data if me.uv_layers.active else None
for lt in me.loop_triangles:
    mat_name = "Glass" if lt.polygon_index in glass_poly_indices else "Housing"
    p0, p1, p2 = (to_unity(me.vertices[vi].co) for vi in lt.vertices)
    fn = norm(cross(sub(p1, p0), sub(p2, p0)))
    corners = []
    for k, (vi, li) in enumerate(zip(lt.vertices, lt.loops)):
        p = (p0, p1, p2)[k]
        pi = add_pos(p)
        uv = uv_layer[li].uv if uv_layer is not None else (0.0, 0.0)
        uvs.append((round(uv[0], 5), round(uv[1], 5)))
        ti = len(uvs)
        ln = to_unity(me.loops[li].normal) if hasattr(me.loops[li], 'normal') else fn
        if ln[0]*fn[0] + ln[1]*fn[1] + ln[2]*fn[2] <= 0:
            ln = fn
        ni = add_norm(norm(ln))
        corners.append((pi, ti, ni))
    faces[mat_name].append(corners)

fmt = lambda x: format(0.0 if x == 0 else x, ".6g")
lines = ["# OES-OP Targeting Sensor Sight; metres; +Z front (lenses); Y up; 1 UV/m Cube Projection UVs; exact recessed glass."]
for x, y, z in positions: lines.append(f"v {fmt(x)} {fmt(y)} {fmt(z)}")
for u, v in uvs: lines.append(f"vt {fmt(u)} {fmt(v)}")
for nx, ny, nz in normals: lines.append(f"vn {fmt(nx)} {fmt(ny)} {fmt(nz)}")
for mat_name in ("Housing", "Glass"):
    lines.append(f"usemtl {mat_name}")
    for tri in faces[mat_name]:
        lines.append("f " + " ".join(f"{pi}/{ti}/{ni}" for pi, ti, ni in tri))

OBJ_OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")

signed_vol, neg_count = 0.0, 0
for mat_name in ("Housing", "Glass"):
    for tri in faces[mat_name]:
        a, b, c = positions[tri[0][0]-1], positions[tri[1][0]-1], positions[tri[2][0]-1]
        cr = cross(sub(b, a), sub(c, a))
        signed_vol += (a[0]*(b[1]*c[2]-b[2]*c[1]) + a[1]*(b[2]*c[0]-b[0]*c[2]) + a[2]*(b[0]*c[1]-b[1]*c[0])) / 6.0
        n = normals[tri[0][2]-1]
        if cr[0]*n[0] + cr[1]*n[1] + cr[2]*n[2] < 0:
            neg_count += 1
assert signed_vol > 0 and neg_count == 0, f"Winding check failed: vol={signed_vol}, neg={neg_count}"

xs, ys, zs = [p[0] for p in positions], [p[1] for p in positions], [p[2] for p in positions]
sha = hashlib.sha256(OBJ_OUT.read_bytes()).hexdigest().upper()
total_tris = len(faces["Housing"]) + len(faces["Glass"])
check_text = (
    f"file: oes-op-sight.obj\n"
    f"sha256: {sha}\n"
    f"positions: {len(positions)}\n"
    f"vertices_expanded: {total_tris * 3}\n"
    f"triangles_total: {total_tris}\n"
    f"triangles_housing: {len(faces['Housing'])}\n"
    f"triangles_glass: {len(faces['Glass'])}\n"
    f"signed_volume: {signed_vol:+.6f}\n"
    f"inverted_normals: {neg_count}\n"
    f"bounds_x: [{min(xs):.4f}, {max(xs):.4f}] ({max(xs)-min(xs):.4f} m)\n"
    f"bounds_y: [{min(ys):.4f}, {max(ys):.4f}] ({max(ys)-min(ys):.4f} m)\n"
    f"bounds_z: [{min(zs):.4f}, {max(zs):.4f}] ({max(zs)-min(zs):.4f} m)\n"
)
MESH_CHECK.write_text(check_text, encoding="utf-8")
print(check_text)

# Render clean front & front-oblique previews + icon-raw
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

center = mathutils.Vector((0.0, 0.0, TARGET_HEIGHT * 0.5))
dist = TARGET_HEIGHT * 2.15
scene.render.resolution_x = 1000
scene.render.resolution_y = 750
for name, pos in [
    ("front-oblique.png", (dist*0.55, -dist*0.75, TARGET_HEIGHT*0.5 + dist*0.42)),
    ("front.png", (0.0, -dist, TARGET_HEIGHT*0.52)),
]:
    cam.location = pos
    cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(RENDERS / name)
    bpy.ops.render.render(write_still=True)
print("RECESSED GLASS BUILD COMPLETE")
