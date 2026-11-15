# 姿势测试：静止 / 转头抬尾 / 下蹲 / 拱背炸毛
import bpy, os, math, mathutils

scene = bpy.context.scene
out = r"D:/unity/NOCTURNE/Art/Cat/work/renders"
os.makedirs(out, exist_ok=True)

rig = None; mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o
print("RIG", rig.name if rig else None, "MESH", mesh.name if mesh else None)

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 640
scene.render.resolution_y = 640

bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
zs = [v.z for v in bb]
center = mathutils.Vector((0, 0.10, 0.14))

# 确保有相机与灯
if not any(o.type == 'LIGHT' for o in bpy.data.objects):
    l = bpy.data.lights.new("L", 'AREA'); l.energy = 30; l.size = 2
    lo = bpy.data.objects.new("L", l); scene.collection.objects.link(lo)
    lo.location = (0.8, -1.2, 1.0); lo.rotation_euler = (math.radians(60), 0, math.radians(35))
cam_data = bpy.data.cameras.get("posecam") or bpy.data.cameras.new("posecam")
cam = bpy.data.objects.get("posecam_obj")
if cam is None:
    cam = bpy.data.objects.new("posecam_obj", cam_data); scene.collection.objects.link(cam)
scene.camera = cam

def look(off, tgt=None):
    cam.location = center + mathutils.Vector(off)
    t = center if tgt is None else mathutils.Vector(tgt)
    d = t - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = 50

def shot(name):
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)

pb = rig.pose.bones
for b in pb:
    b.rotation_mode = 'XYZ'

def reset():
    for b in pb:
        b.rotation_euler = (0, 0, 0)
        b.location = (0, 0, 0)
        b.scale = (1, 1, 1)

def rot(name, x=0, y=0, z=0):
    if name in pb:
        pb[name].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))

# 1 静止
reset()
bpy.context.view_layer.update()
look((-1.05, -1.05, 0.42))
shot("pose_idle")

# 2 转头看镜头 + 尾巴上举（凝视）
reset()
rot("neck", 0, 0, 25)
rot("head", -10, 0, 35)
rot("tail.01", -45, 0, 0)
rot("tail.02", -35, 0, 0)
rot("tail.03", -25, 0, 0)
bpy.context.view_layer.update()
look((-0.80, -1.25, 0.26))
shot("pose_stare")

# 3 下蹲（跳跃蓄力）
reset()
for legF in ("upper_arm.L", "upper_arm.R"):
    rot(legF, 25, 0, 0)
for legH in ("thigh.L", "thigh.R"):
    rot(legH, -35, 0, 0)
for sh in ("shin.L", "shin.R"):
    rot(sh, 55, 0, 0)
pb["root"].location = (0, 0, -0.05)
rot("spine", -8, 0, 0)
bpy.context.view_layer.update()
look((-0.95, -1.10, 0.34))
shot("pose_crouch")

# 4 拱背炸毛（恐怖姿态）
reset()
rot("spine", -18, 0, 0)
rot("chest", -14, 0, 0)
rot("neck", 22, 0, 0)
rot("head", 10, 0, 0)
rot("tail.01", -60, 0, 0)
rot("tail.02", -40, 0, 0)
for legF in ("upper_arm.L", "upper_arm.R"):
    rot(legF, -12, 0, 0)
bpy.context.view_layer.update()
look((-1.00, -1.00, 0.30))
shot("pose_arch")

reset()
print("POSE_TEST_DONE")
