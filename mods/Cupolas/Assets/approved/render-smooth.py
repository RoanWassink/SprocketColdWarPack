import bpy, math
from pathlib import Path
from mathutils import Vector
P=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.cycles.device='CPU'
scene.render.resolution_x=1200; scene.render.resolution_y=800; scene.render.resolution_percentage=100
scene.world.color=(0.15,0.15,0.15)
mat=bpy.data.materials.new('Steel grey'); mat.diffuse_color=(0.27,0.29,0.31,1); mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Base Color'].default_value=(0.27,0.29,0.31,1); bsdf.inputs['Metallic'].default_value=.35; bsdf.inputs['Roughness'].default_value=.46
for name in ('base','door'):
 verts=[]; normals=[]; faces=[]; corner_normals=[]
 for line in (P/f't90a_cupola_{name}.obj').read_text().splitlines():
  t=line.split()
  if not t: continue
  if t[0]=='v':
   x,y,z=map(float,t[1:4]); verts.append((x,-z,y))
  elif t[0]=='vn':
   x,y,z=map(float,t[1:4]); normals.append((x,-z,y))
  elif t[0]=='f':
   parts=[x.split('//') for x in t[1:]]; faces.append(tuple(int(q[0])-1 for q in parts)); corner_normals.extend(normals[int(q[1])-1] for q in parts)
 mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
 for poly in mesh.polygons: poly.use_smooth=True
 mesh.normals_split_custom_set(corner_normals)
 obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj); obj.data.materials.append(mat)
 if name=='door': obj.location=(0,.27,.24)
# Camera looks from the positive SCAD Y side (negative Blender Y).
def aim(obj,point): obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(1.05,-1.7,1.28)); cam=bpy.context.object; aim(cam,(0,-.04,.13)); cam.data.type='ORTHO'; cam.data.ortho_scale=1.15; scene.camera=cam
for loc,power,size in [((-.9,-1.4,2),100,1.6),((1.3,.5,1.6),55,1.5)]:
 bpy.ops.object.light_add(type='AREA',location=loc); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; aim(light,(0,0,.1))
scene.render.image_settings.file_format='PNG'; scene.render.filepath=str(P/'render'/'cupola-smooth.png')
bpy.ops.render.render(write_still=True)

