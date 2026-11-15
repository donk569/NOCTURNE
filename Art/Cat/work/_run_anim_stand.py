# 站姿猫动画：Walk（原地行走循环）/ Jump（原地跳）
import bpy, math, os, sys
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import ctx

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_fit.blend")
try:
    ctx.ensure_window()
except Exception:
    pass

# --- root 骨朝向校正为 -Y（与动画局部轴约定一致，root 不参与蒙皮）---
_rig = None
for _o in bpy.data.objects:
    if _o.type == 'ARMATURE':
        _rig = _o
if _rig:
    bpy.context.view_layer.objects.active = _rig
    _rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    _eb = _rig.data.edit_bones.get("root")
    if _eb:
        _eb.head = (0.0, 0.0, 0.020)
        _eb.tail = (0.0, -0.055, 0.020)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("ROOT_FIXED", tuple(_rig.data.bones["root"].head_local), tuple(_rig.data.bones["root"].tail_local))

# --- 清空旧动作（拟合文件里带着旧骨架时期生成的动画）---
if _rig:
    if _rig.animation_data:
        _rig.animation_data_clear()
    for _a in list(bpy.data.actions):
        bpy.data.actions.remove(_a)
    print("ACTIONS_PURGED")

rig = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
pb = rig.pose.bones
for b in pb:
    b.rotation_mode = 'XYZ'

def clear_anim():
    if rig.animation_data:
        rig.animation_data_clear()

def reset_pose():
    for b in pb:
        b.rotation_euler = (0, 0, 0); b.location = (0, 0, 0); b.scale = (1, 1, 1)

def R(n, x=0, y=0, z=0):
    if n in pb: pb[n].rotation_euler = (math.radians(x), math.radians(y), math.radians(z))

def key(frame, loc_bones=("root",)):
    for b in pb:
        b.keyframe_insert("rotation_euler", frame=frame)
        if b.name in loc_bones:
            b.keyframe_insert("location", frame=frame)

def make(name, frames):
    act = rig.animation_data.action
    act.name = name
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start, act.frame_end = 1, frames

# ---------------- Walk 32f ----------------
clear_anim(); reset_pose()
N = 32
for i in range(0, N + 1, 2):
    f = i + 1
    reset_pose()
    ph = i / float(N) * 2 * math.pi
    # 四腿相位：LF 0 / RH 0.25 / RF 0.5 / LH 0.75
    legs = [
        ("upper_arm.L", "forearm.L", 0.00,  1),
        ("thigh.R",     "shin.R",    0.25, -1),
        ("upper_arm.R", "forearm.R", 0.50,  1),
        ("thigh.L",     "shin.L",    0.75, -1),
    ]
    for up, lo, offset, sgn in legs:
        a = math.sin(ph + offset * 2 * math.pi)
        # 前摆/后摆 ±11 度（小步幅）
        R(up, -a * 11.0 * sgn)
        # 摆动相抬膝
        swing = max(0.0, math.cos(ph + offset * 2 * math.pi))
        R(lo, swing * 16.0 * sgn)
    bob = math.sin(ph * 2) * 0.006
    pb["root"].location = (0, 0, bob)
    R("spine", math.sin(ph * 2) * 1.2)
    R("chest", math.sin(ph * 2 + 0.6) * 1.0)
    R("neck", -3 + math.sin(ph * 2) * 1.0)
    R("head", 2, 0, math.sin(ph) * 3.0)
    R("tail.01", -12, 0, math.sin(ph) * 10)
    R("tail.02", -4, 0, math.sin(ph + 0.8) * 14)
    R("tail.03", 0, 0, math.sin(ph + 1.6) * 12)
    key(f)
make("Walk", N + 1)

# ---------------- Jump 40f ----------------
clear_anim(); reset_pose()
keys = [1, 8, 13, 18, 26, 31, 36, 40]
for f in keys:
    reset_pose()
    if f == 1:
        pass
    elif f == 8:   # 蓄力下蹲
        pb["root"].location = (0, 0, -0.055)
        for s in (".L", ".R"):
            R("upper_arm" + s, -18); R("forearm" + s, 28)
            R("thigh" + s, -26); R("shin" + s, 40)
        R("spine", -6); R("neck", 6)
    elif f == 13:  # 起跳
        pb["root"].location = (0, 0, 0.085)
        for s in (".L", ".R"):
            R("upper_arm" + s, 16); R("forearm" + s, -6)
            R("thigh" + s, 18); R("shin" + s, -10)
        R("spine", 4); R("neck", -8)
    elif f == 18:  # 空中（收腿）
        pb["root"].location = (0, 0, 0.075)
        for s in (".L", ".R"):
            R("upper_arm" + s, -22); R("forearm" + s, 34)
            R("thigh" + s, -30); R("shin" + s, 46)
        R("tail.01", -30); R("tail.02", -16)
    elif f == 26:  # 落地缓冲
        pb["root"].location = (0, 0, -0.045)
        for s in (".L", ".R"):
            R("upper_arm" + s, -14); R("forearm" + s, 24)
            R("thigh" + s, -22); R("shin" + s, 34)
        R("spine", -4)
    elif f == 31:  # 起身
        pb["root"].location = (0, 0, -0.012)
        for s in (".L", ".R"):
            R("upper_arm" + s, -4); R("forearm" + s, 6)
            R("thigh" + s, -6); R("shin" + s, 8)
    key(f)
make("Jump", 40)

clear_anim(); reset_pose()
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend")
print("STAND_ANIM_DONE", [a.name for a in bpy.data.actions])
