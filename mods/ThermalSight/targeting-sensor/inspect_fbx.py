import bpy, math, mathutils
from pathlib import Path

FBX = r"C:\Users\Roanj\Downloads\Meshy_AI_Military_Targeting_Sensor_20261127_generate.fbx"
OUT = Path(r"C:\Users\Roanj\Documents\Codex\2026-11-13\herpak\src\thermal-sight\targeting-sensor\inspect")
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
print(f"Objects: {len(bpy.context.scene.objects)}, Meshes: {len(meshes)}")
bpy.context.view_layer.update()

min_v = [1e9, 1e9, 1e9]
max_v = [-1e9, -1e9, -1e9]
total_verts = 0
total_polys = 0
for o in meshes:
    me = o.data
    total_verts += len(me.vertices)
    total_polys += len(me.polygons)
    mats = [m.name if m else "None" for m in me.materials]
    print(f"  Mesh '{o.name}': verts={len(me.vertices)}, polys={len(me.polygons)}, scale={tuple(round(x,4) for x in o.scale)}, rot={tuple(round(math.degrees(x),1) for x in o.rotation_euler)}, mats={mats}")
    for corner in o.bound_box:
        wc = o.matrix_world @ mathutils.Vector(corner)
        for i in range(3):
            min_v[i] = min(min_v[i], wc[i])
            max_v[i] = max(max_v[i], wc[i])

size = [max_v[i] - min_v[i] for i in range(3)]
center = [(min_v[i] + max_v[i]) / 2 for i in range(3)]
print(f"World bounds: X=[{min_v[0]:.4f}, {max_v[0]:.4f}] ({size[0]:.4f}), Y=[{min_v[1]:.4f}, {max_v[1]:.4f}] ({size[1]:.4f}), Z=[{min_v[2]:.4f}, {max_v[2]:.4f}] ({size[2]:.4f})")
print(f"Total verts={total_verts}, polys={total_polys}")

# Set up Workbench render with flat lighting/matcap so we can see geometry + textures clearly
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 800
scene.render.resolution_y = 600
scene.render.image_settings.file_format = 'PNG'

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

dist = max(size) * 2.2
views = [
    ("front_negY.png", (center[0], center[1] - dist, center[2] + dist*0.15)),
    ("back_posY.png",  (center[0], center[1] + dist, center[2] + dist*0.15)),
    ("right_posX.png", (center[0] + dist, center[1], center[2] + dist*0.15)),
    ("oblique.png",    (center[0] + dist*0.7, center[1] - dist*0.7, center[2] + dist*0.45)),
]
for name, pos in views:
    cam.location = pos
    direction = mathutils.Vector(center) - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(OUT / name)
    bpy.ops.render.render(write_still=True)
print("RENDERS DONE")
