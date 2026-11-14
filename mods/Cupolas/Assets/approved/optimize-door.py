import bpy,math
from pathlib import Path
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
root=Path(__file__).resolve().parent
bpy.ops.wm.stl_import(filepath=str(root/'t90a_cupola_door-raw.stl'))
obj=bpy.context.selected_objects[0]; bpy.context.view_layer.objects.active=obj
g=obj.vertex_groups.new(name='details')
for v in obj.data.vertices:
 x,y,z=v.co; r=math.hypot(x,y-270)
 if r<195: g.add([v.index],1.0,'REPLACE')
mod=obj.modifiers.new('Collapse','DECIMATE'); mod.ratio=.655; mod.vertex_group='details'; mod.vertex_group_factor=1; mod.use_collapse_triangulate=True
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.ops.wm.stl_export(filepath=str(root/'t90a_cupola_door.stl'),export_selected_objects=True,ascii_format=False)



