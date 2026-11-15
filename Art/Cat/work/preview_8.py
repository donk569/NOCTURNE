# -*- coding: utf-8 -*-
"""渲染 8 段动画的关键帧预览（侧视 + 3/4 视角）"""
import bpy, math, os, sys
from mathutils import Vector

BLEND = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
OUT = r"D:/unity/NOCTURNE/Art/Cat/work/renders/anim8"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]

if rig.animation_data is None:
    rig.animation_data_create()

# 地面
if not bpy.data.objects.get("__ground"):
    me = bpy.data.meshes.new("__gmesh")
    me.from_pydata([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)], [], [(0, 1, 2, 3)])
    me.update()
    g = bpy.data.objects.new("__ground", me)
    scene.collection.objects.link(g)
    m = bpy.data.materials.new("__gmat")
    m.use_nodes = True
    for n in m.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            n.inputs['Base Color'].default_value = (0.16, 0.17, 0.19, 1)
            n.inputs['Roughness'].default_value = 0.95
    g.data.materials.append(m)

# 灯
def lamp(name, loc, energy, size=1.2):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = energy
    ld.size = size
    o = bpy.data.objects.new(name, ld)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = (Vector((0, 0, 0.14)) - Vector(loc)).to_track_quat('-Z', 'Y')

if not any(o.type == 'LIGHT' for o in bpy.data.objects):
    lamp("__k", (0.9, -0.9, 0.9), 120, 1.5)
    lamp("__f", (-1.0, -0.4, 0.5), 50, 2.0)
    lamp("__r", (0.1, 1.2, 0.6), 40, 2.0)

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 460
scene.render.resolution_y = 400
scene.render.film_transparent = False
try:
    scene.eevee.taa_render_samples = 24
except Exception:
    pass
scene.view_settings.view_transform = 'Standard'

cd = bpy.data.cameras.new("__cam")
cd.lens = 62
cam = bpy.data.objects.new("__cam", cd)
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(path, loc, look, lens=62):
    cam.location = loc
    cam.data.lens = lens
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (Vector(look) - Vector(loc)).to_track_quat('-Z', 'Y')
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


SIDE = ((1.20, 0.02, 0.19), (0.0, 0.02, 0.14))
Q34 = ((1.05, -1.05, 0.42), (0.0, 0.0, 0.19))

SHOTS = {
    "Cat_Idle":   [1, 23, 45, 68],
    "Cat_Walk":   [1, 5, 9, 13, 17, 25],
    "Cat_Sit":    [1, 12, 26, 38, 48],
    "Cat_Sleep":  [1, 30, 60, 90],
    "Cat_Eat":    [1, 20, 40, 60],
    "Cat_Drink":  [1, 20, 40, 60],
    "Cat_Alert":  [1, 8, 22, 34, 52],
    "Cat_Pounce": [1, 10, 15, 19, 24, 28, 33, 39, 45, 52],
}
VIEW = {"Cat_Pounce": "q34", "Cat_Sit": "q34", "Cat_Sleep": "q34", "Cat_Alert": "q34"}

for act_name, frames in SHOTS.items():
    act = bpy.data.actions.get(act_name)
    if act is None:
        print("MISSING", act_name)
        continue
    rig.animation_data.action = act
    try:
        rig.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    v = VIEW.get(act_name, "side")
    loc, look = Q34 if v == "q34" else SIDE
    for f in frames:
        scene.frame_set(f)
        bpy.context.view_layer.update()
        shoot(os.path.join(OUT, "%s_%03d.png" % (act_name, f)), loc, look)

print("PREVIEW_DONE")
