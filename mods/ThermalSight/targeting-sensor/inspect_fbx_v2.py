import bpy, bmesh, math, mathutils
from pathlib import Path

FBX = r"C:\Users\Roanj\Downloads\Meshy_AI_Military_Targeting_Sensor_20261127_generate (1).fbx"
OUT = Path(r"C:\Users\Roanj\Documents\Codex\2026-11-13\herpak\src\thermal-sight\targeting-sensor\inspect-v2")
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.context.view_layer.update()
min_v, max_v = [1e9]*3, [-1e9]*3
for o in meshes:
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    open_edges = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    bm.free()
    print(f"Mesh '{o.name}': verts={len(me.vertices)}, polys={len(me.polygons)}, open_edges={open_edges}")
    for c in o.bound_box:
        wc = o.matrix_world @ mathutils.Vector(c)
        for i in range(3):
            min_v[i] = min(min_v[i], wc[i]); max_v[i] = max(max_v[i], wc[i])

size = [max_v[i] - min_v[i] for i in range(3)]
center = [(min_v[i] + max_v[i]) / 2 for i in range(3)]
print(f"Bounds: X=[{min_v[0]:.4f},{max_v[0]:.4f}] ({size[0]:.4f}), Y=[{min_v[1]:.4f},{max_v[1]:.4f}] ({size[1]:.4f}), Z=[{min_v[2]:.4f},{max_v[2]:.4f}] ({size[2]:.4f})")

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'SINGLE'
scene.render.resolution_x = 800; scene.render.resolution_y = 600
scene.render.image_settings.file_format = 'PNG'
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam); scene.camera = cam

dist = max(size) * 2.15
for name, pos in [
    ("front_negY.png", (center[0], center[1] - dist, center[2] + dist*0.12)),
    ("oblique.png",    (center[0] + dist*0.65, center[1] - dist*0.7, center[2] + dist*0.42)),
    ("top.png",        (center[0], center[1] - dist*0.35, center[2] + dist*1.1)),
]:
    cam.location = pos
    cam.rotation_euler = (mathutils.Vector(center) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(OUT / name)
    bpy.ops.render.render(write_still=True)
print("INSPECT V2 DONE")
