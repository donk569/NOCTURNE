import bpy
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_sit_fit.blend")

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

# 坐姿猫动画：Idle_Stare / Alert / Sleep（含关键帧循环）
import bpy, math, os

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

def L(n, x=0, y=0, z=0):
    if n in pb: pb[n].location = (x, y, z)

def key_all(frame):
    for b in pb:
        b.keyframe_insert("rotation_euler", frame=frame)
        if b.name == "root":
            b.keyframe_insert("location", frame=frame)

# ---------- 1. Idle_Stare 96f ----------
clear_anim(); reset_pose()
frames = [1, 24, 48, 60, 72, 96]
for f in frames:
    reset_pose()
    t = (f - 1) / 95.0
    br = math.sin(t * math.pi * 2) * 1.6          # 呼吸
    yaw = math.sin(t * math.pi * 2) * 3.0          # 头微转
    if f == 60: yaw = 7.0                           # 中途偏头
    R("chest", br * 0.6)
    R("spine", br * 0.3)
    R("neck", -1.0 + br * 0.2, 0, yaw * 0.4)
    R("head", br * 0.3, 0, yaw)
    R("tail.01", -2, 0, yaw * 0.3)
    R("tail.02", 0, 0, yaw * 1.2)
    R("tail.03", 0, 0, math.sin(t * math.pi * 4) * 8)
    if f == 24: R("ear.L", -14)                    # 耳朵抖动
    if f == 48: R("ear.R", -10)
    key_all(f)
act_idle = rig.animation_data.action
act_idle.name = "Idle_Stare"
act_idle.use_fake_user = True

# ---------- 2. Alert 48f ----------
clear_anim(); reset_pose()
for f in [1, 8, 16, 32, 48]:
    reset_pose()
    if f == 1 or f == 48:
        pass
    else:
        R("neck", -13)
        R("head", -9)
        R("ear.L", -18); R("ear.R", -18)
        R("tail.01", -22, 0, 0)
        R("tail.02", -10, 0, 0)
        L("root", 0, 0, 0.012)
        R("chest", -3)
    key_all(f)
act_alert = rig.animation_data.action
act_alert.name = "Alert"
act_alert.use_fake_user = True

# ---------- 3. Sleep 120f ----------
clear_anim(); reset_pose()
for f in [1, 30, 60, 90, 120]:
    reset_pose()
    t = (f - 1) / 119.0
    br = math.sin(t * math.pi * 2) * 1.2
    R("neck", 16 + br * 0.4)
    R("head", 12 + br * 0.3)
    R("ear.L", 14); R("ear.R", 14)
    R("chest", 2 + br * 0.5)
    R("spine", 1.5)
    L("root", 0, 0, -0.012)
    R("tail.03", 0, 0, math.sin(t * math.pi * 2) * 4)
    key_all(f)
act_sleep = rig.animation_data.action
act_sleep.name = "Sleep"
act_sleep.use_fake_user = True

# 让动作帧范围正确
for a in (act_idle, act_alert, act_sleep):
    a.use_frame_range = True
act_idle.frame_start, act_idle.frame_end = 1, 96
act_alert.frame_start, act_alert.frame_end = 1, 48
act_sleep.frame_start, act_sleep.frame_end = 1, 120

clear_anim(); reset_pose()
bpy.context.scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_sit_final.blend")
print("SIT_ANIM_DONE", [a.name for a in bpy.data.actions])
