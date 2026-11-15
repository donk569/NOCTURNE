# 坐姿参数试错：3 个变体，侧视图对比
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
os.makedirs(out, exist_ok=True)

rig = mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 640
scene.render.resolution_y = 640

center = mathutils.Vector((0, 0.10, 0.14))
cam = bpy.data.objects.get("posecam_obj")
scene.camera = cam
cam.data.lens = 50
cam.location = center + mathutils.Vector((1.15, -0.15, 0.22))
d = center - cam.location
cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()

pb = rig.pose.bones
for b in pb:
    b.rotation_mode = 'XYZ'

def reset():
    for b in pb:
        b.rotation_euler = (0, 0, 0); b.location = (0, 0, 0); b.scale = (1, 1, 1)

def rot(n, x=0, y=0, z=0):
    if n in pb: pb[n].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))

def sit(thigh, shin, foot, rootz, spine=-8, neck=10, head=-6):
    reset()
    pb["root"].location = (0, 0, rootz)
    for side in (".L", ".R"):
        rot("thigh" + side, thigh, 0, 0)
        rot("shin" + side, shin, 0, 0)
        rot("foot" + side, foot, 0, 0)
        rot("upper_arm" + side, 8, 0, 0)
        rot("forearm" + side, -4, 0, 0)
    rot("spine", spine, 0, 0)
    rot("chest", spine * 0.5, 0, 0)
    rot("neck", neck, 0, 0)
    rot("head", head, 0, 0)
    # 尾巴绕身前
    rot("tail.01", -25, 0, -30)
    rot("tail.02", -20, 0, -40)
    rot("tail.03", -10, 0, -40)
    bpy.context.view_layer.update()

variants = [
    ("sitA", -40, 80, -35, -0.09),
    ("sitB", -55, 100, -45, -0.11),
    ("sitC", -30, 60, -25, -0.07),
]
for name, th, sh, ft, rz in variants:
    sit(th, sh, ft, rz)
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

reset()
print("SIT_TEST_DONE")
