import bpy, math, os
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
sc = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
if rig.animation_data is None: rig.animation_data_create()

me = bpy.data.meshes.new("g")
me.from_pydata([(-2,-2,0),(2,-2,0),(2,2,0),(-2,2,0)],[],[(0,1,2,3)]); me.update()
g = bpy.data.objects.new("g", me); sc.collection.objects.link(g)
mm = bpy.data.materials.new("gm"); mm.use_nodes=True
for n in mm.node_tree.nodes:
    if n.type=='BSDF_PRINCIPLED':
        n.inputs['Base Color'].default_value=(0.22,0.23,0.25,1); n.inputs['Roughness'].default_value=0.9
g.data.materials.append(mm)

def lamp(nm, loc, e, s=1.5):
    ld = bpy.data.lights.new(nm,'AREA'); ld.energy=e; ld.size=s
    o = bpy.data.objects.new(nm, ld); sc.collection.objects.link(o); o.location=loc
    o.rotation_mode='QUATERNION'
    o.rotation_quaternion=(Vector((0,0,0.12))-Vector(loc)).to_track_quat('-Z','Y')
lamp("k",(1.0,-1.0,1.0),150,1.6); lamp("f",(-1.1,-0.5,0.6),70,2.0); lamp("r",(0.2,1.3,0.7),60,2.0)

sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=820; sc.render.resolution_y=620
sc.view_settings.view_transform='Standard'
try: sc.eevee.taa_render_samples=32
except Exception: pass
cd=bpy.data.cameras.new("c"); cd.lens=70
cam=bpy.data.objects.new("c",cd); sc.collection.objects.link(cam); sc.camera=cam
cam.location=(1.45,0.02,0.20)
cam.rotation_mode='QUATERNION'
cam.rotation_quaternion=(Vector((0,0.0,0.15))-Vector(cam.location)).to_track_quat('-Z','Y')

for actn, f in (("Cat_Sit",48),("Cat_Sit",38),("Cat_Pounce",15),("Cat_Sleep",60),("Cat_Pounce",26)):
    act=bpy.data.actions.get(actn)
    rig.animation_data.action=act
    try: rig.animation_data.action_slot=act.slots[0]
    except Exception: pass
    sc.frame_set(f); bpy.context.view_layer.update()
    sc.render.filepath=r"D:/unity/NOCTURNE/Art/Cat/work/renders/big_%s_%d.png"%(actn,f)
    bpy.ops.render.render(write_still=True)
print("BIG_DONE")
