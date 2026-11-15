# 单骨符号标定：thigh.L / shin.L 正负 40/60 度，侧视对比
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
rig = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 560
scene.render.resolution_y = 560

center = mathutils.Vector((0, 0.10, 0.14))
cam = bpy.data.objects.get("posecam_obj")
scene.camera = cam
cam.data.lens = 50
cam.location = center + mathutils.Vector((1.15, 0.0, 0.15))
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

pb = rig.pose.bones
for b in pb: b.rotation_mode = 'XYZ'

def reset():
    for b in pb:
        b.rotation_euler = (0, 0, 0); b.location = (0, 0, 0)

def one(bone, deg, tag):
    reset()
    pb[bone].rotation_euler = (math.radians(deg), 0, 0)
    bpy.context.view_layer.update()
    scene.render.filepath = os.path.join(out, "ax_%s_%s.png" % (bone.replace('.', ''), tag))
    bpy.ops.render.render(write_still=True)

one("thigh.L", 40, "p40")
one("thigh.L", -40, "m40")
one("shin.L", 60, "p60")
one("shin.L", -60, "m60")
reset()
print("AXIS_TEST_DONE")
