import bpy
import math
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r"D:\unity\NOCTURNE\Art\Cat\v3\out\cat_anim.blend")
rig = bpy.data.objects["CatRig"]
ad = rig.animation_data
for t in ad.nla_tracks:
    t.mute = True
ad.action = bpy.data.actions["Cat_Walk"]
try:
    ad.action_slot = ad.action.slots[0]
except Exception:
    pass

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.render.resolution_x = 480
sc.render.resolution_y = 360
sc.render.resolution_percentage = 100
sc.render.image_settings.file_format = "PNG"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "SINGLE"
sc.display.shading.single_color = (0.35, 0.35, 0.38)
sc.display.shading.show_shadows = True

cam_data = bpy.data.cameras.new("QACam")
cam = bpy.data.objects.new("QACam", cam_data)
bpy.context.collection.objects.link(cam)
cam_data.lens = 50
sc.camera = cam

# 猫面向 -Y，侧面从 +X 看过去
def aim(pos, target):
    cam.location = Vector(pos)
    d = Vector(target) - Vector(pos)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


out = r"D:\unity\NOCTURNE\.workbuddy\walkqa"
frames = [1, 4, 7, 10, 13, 16, 19, 22]
for f in frames:
    aim((0.95, -0.27, 0.22), (0.0, -0.27, 0.15))
    sc.frame_set(f)
    sc.render.filepath = out + "\\bw_%02d.png" % f
    bpy.ops.render.render(write_still=True)
    print("RENDER", f)

# 前脚/后脚的世界 Z 轨迹，确认四足都在动
import collections
rig_z = collections.defaultdict(list)
for f in range(1, 25):
    sc.frame_set(f)
    bpy.context.view_layer.update()
    for n in ("hand.L", "hand.R", "toe.L", "toe.R"):
        pb = rig.pose.bones.get(n)
        if pb:
            rig_z[n].append(round((rig.matrix_world @ pb.tail).z, 4))
for n, zs in rig_z.items():
    print("PAWZ", n, "min", min(zs), "max", max(zs), "span", round(max(zs) - min(zs), 4))
