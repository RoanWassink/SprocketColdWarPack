"""Blender --background --python this_script. Renders the shipped OBJ, no geometry edits."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix
r=Path(__file__).resolve().parents[1]; out=r/'bin'/'renders';out.mkdir(parents=True,exist_ok=True)
obj=r/'Material/files/BepInEx/plugins/SprocketExpandedBallistics/era-assets/compositeSideModule.obj'
for o in list(bpy.data.objects):bpy.data.objects.remove(o,do_unlink=True)
vertices=[];faces={};group=''
for line in obj.read_text().splitlines():
 p=line.split()
 if not p:continue
 if p[0]=='v':vertices.append(tuple(map(float,p[1:4])))
 if p[0]=='g':group=p[1];faces[group]=[]
 if p[0]=='f':faces[group].append(tuple(int(x)-1 for x in p[1:]))
for name,ff in faces.items():
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],ff);mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
 mat=bpy.data.materials.new(name);mat.diffuse_color=(.025,.028,.022,1) if name=='dark' else (.23,.28,.12,1) if name=='housing' else (.32,.36,.18,1)
 bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=mat.diffuse_color;bs.inputs['Roughness'].default_value=.7
 if name!='dark':
  tex=mat.node_tree.nodes.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=5;tex.inputs['Detail'].default_value=0;tex.inputs['Roughness'].default_value=.3
  ramp=mat.node_tree.nodes.new('ShaderNodeValToRGB');ramp.color_ramp.interpolation='CONSTANT';ramp.color_ramp.elements[0].position=.36;ramp.color_ramp.elements[0].color=(.065,.08,.04,1);ramp.color_ramp.elements[1].position=.58;ramp.color_ramp.elements[1].color=(.30,.35,.16,1)
  ramp.color_ramp.elements.new(.48).color=(.16,.19,.075,1);mat.node_tree.links.new(tex.outputs['Fac'],ramp.inputs[0]);mat.node_tree.links.new(ramp.outputs[0],bs.inputs['Base Color'])
 o.data.materials.append(mat)
 o.rotation_euler=Matrix(((0,1,0),(0,0,-1),(-1,0,0))).to_euler()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48
scene.world.color=(.25,.25,.25);scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
for name,loc,power,size in [('key',(3,4,2),650,4),('fill',(-1,1,3),300,3),('rim',(1,-3,-2),450,3)]:
 data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
 o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((.15,0,0))-o.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',camera);scene.collection.objects.link(cam);scene.camera=cam;camera.type='ORTHO';camera.ortho_scale=1.65
def render(name,location,res,transparent=False):
 cam.location=location;direction=(Vector((.15,0,0))-cam.location).normalized()
 if name.endswith('-top.png'):
  right=Vector((0,0,1));up=Vector((1,0,0));back=Vector((0,1,0))
 else:
  right=direction.cross(Vector((0,0,1))).normalized();up=right.cross(direction);back=-direction
 cam.rotation_euler=Matrix((right,up,back)).transposed().to_euler()
 scene.render.resolution_x=res[0];scene.render.resolution_y=res[1];scene.render.resolution_percentage=100
 scene.render.film_transparent=transparent;scene.render.filepath=str(out/name);bpy.ops.render.render(write_still=True)

camera.ortho_scale=1.45
render('compositeSideModule-front-three-quarter.png',(2,-1.7,1.25),(1200,900))
camera.ortho_scale=1.4
render('compositeSideModule-icon-raw.png',(2,-1.7,1.25),(512,512),True)
# A vertical armour wall is the world Y/Z plane. Module local +Y is world +X.
def material(name,color):
 m=bpy.data.materials.new(name);m.diffuse_color=color;return m
bpy.ops.mesh.primitive_cube_add(size=1,location=(-.025,0,0));wall=bpy.context.object;wall.name='vertical side wall';wall.scale=(.05,1.4,1.1);wall.data.materials.append(material('wall',(.16,.19,.21,1)))
yellow=material('local +Y arrow',(1,.65,.015,1))
bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=.009,depth=.42,location=(.57,-.63,0),rotation=(0,math.pi/2,0));bpy.context.object.data.materials.append(yellow)
bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=.033,radius2=0,depth=.10,location=(.83,-.63,0),rotation=(0,math.pi/2,0));bpy.context.object.data.materials.append(yellow)
camera.ortho_scale=1.95
render('compositeSideModule-vertical-wall.png',(2,-2,1.5),(1400,1000))
