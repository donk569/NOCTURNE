# -*- coding: utf-8 -*-
"""只重建 Cat_Walk（其余 7 段动画原样保留），然后重新导出 FBX。

为什么要重建：
- 实测原 Cat_Walk 后爪单次位移只有 0.051m，爪子自然速度 ≈0.077 m/s，
  而游戏里按 0.3~0.55 m/s 移动 → 脚底打滑 4~7 倍，视觉上像在冰面上滑行。
- 相位顺序实测是 LF→LH→RF→RH（前腿先落、随后同侧后腿），
  猫的正常行走是 LH→LF→RH→RF（后腿先落、随后同侧前腿）。差一个镜像。
- 原帧把跗关节(hock)目标压到 z=0.013，超出腿长可达范围（reach 0.1172），
  IK 被静默 clamp，导致后腿全程僵直、爪高不受控。

调参依据（probe_legs.py 实测）：
  thigh.L head z=0.1486, shin.L tail(hock) z=0.0346, HIND reach=0.1172
  upper_arm.L head z=0.1500, forearm.L tail(wrist) z=0.0421, FRONT reach=0.1089
把 root 降 0.022 让腿有弯曲余量，hock 回到 0.032（不再触发 clamp）。
步幅 0.10：dy=±0.05, dz=0.0946 → r=0.107 = reach 的 91%，仍有余量。
"""
import bpy, math, os, sys
from mathutils import Vector

sys.path.insert(0, r"D:/unity/NOCTURNE/Art/Cat/work")
import catik
from catik import ik2, wrap, DEG, RAD

BLEND = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
OUT_FBX = r"D:/unity/NOCTURNE/Art/Cat/export/Cat_All.fbx"

FPS = 30
GROUND = 0.013
END = 33                 # 保持 33 帧，.meta 里的 lastFrame 不用改
DUTY = 0.62              # 支撑相占比

ROOT_H = -0.022          # 整体压低，给腿留弯曲余量
BOB = 0.006              # 身体上下起伏（一个步幅 2 个周期）

HOCK_Z = 0.032           # 跗关节高度（原值 0.013 超出可达范围）
WRIST_Z = 0.042          # 腕关节高度
STRIDE_H = 0.100         # 后腿步幅
STRIDE_F = 0.085         # 前腿步幅
LIFT_H = 0.022           # 后爪抬脚高度
LIFT_F = 0.020

# 相位（落地时刻 = (1-phase)%1）：LH→LF→RH→RF，标准横向序列
PH_LH, PH_LF, PH_RH, PH_RF = 0.0, 0.75, 0.5, 0.25

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0

rig = None
mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        rig = o
    if o.type == 'MESH':
        mesh = o
if rig is None:
    raise RuntimeError("no armature")

pb = rig.pose.bones
for b in pb:
    b.rotation_mode = 'XYZ'

REST = catik.RestInfo(rig)
HOCK0 = (REST.bones["shin.L"]["tail"].y, REST.bones["shin.L"]["tail"].z)
WRIST0 = (REST.bones["forearm.L"]["tail"].y, REST.bones["forearm.L"]["tail"].z)
print("REST hock", HOCK0, "wrist", WRIST0)

TORSO = ["hips", "spine", "chest", "neck", "head", "ear.L", "ear.R",
         "tail.01", "tail.02", "tail.03"]
LEGS = ["upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R",
        "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R", "toe.L", "toe.R"]
ALL = TORSO + LEGS


def upd():
    bpy.context.view_layer.update()


def set_rot(name, rx=0.0, ry=0.0, rz=0.0):
    pb[name].rotation_euler = (RAD(rx), RAD(ry), RAD(rz))


def solve_hind(side, hock_y, hock_z, hips_rx):
    p0 = pb["thigh." + side].head.copy()
    p1 = Vector((0.0, hock_y, hock_z))
    a1, a2, _ = ik2(Vector((0, p0.y, p0.z)), p1,
                    REST.len("thigh." + side), REST.len("shin." + side),
                    prefer_forward=True)
    rx_th = wrap(DEG(a1) - REST.ang("thigh." + side) - hips_rx)
    set_rot("thigh." + side, rx_th)
    rx_sh = wrap(DEG(a2) - REST.ang("shin." + side) - hips_rx - rx_th)
    set_rot("shin." + side, rx_sh)
    acc = hips_rx + rx_th + rx_sh
    a_foot = DEG(math.atan2(GROUND - hock_z, -0.030))
    rx_ft = wrap(a_foot - REST.ang("foot." + side) - acc)
    set_rot("foot." + side, rx_ft)
    rx_toe = wrap(180.0 - REST.ang("toe." + side) - acc - rx_ft)
    set_rot("toe." + side, rx_toe)


def solve_front(side, wrist_y, wrist_z, chest_rx, dy=None):
    p0 = pb["upper_arm." + side].head.copy()
    if dy is not None:
        wy = p0.y + dy
    elif wrist_y is None:
        wy = p0.y
    else:
        wy = wrist_y
    p1 = Vector((0.0, wy, wrist_z))
    a1, a2, _ = ik2(Vector((0, p0.y, p0.z)), p1,
                    REST.len("upper_arm." + side), REST.len("forearm." + side),
                    prefer_forward=False)
    rx_ua = wrap(DEG(a1) - REST.ang("upper_arm." + side) - chest_rx)
    set_rot("upper_arm." + side, rx_ua)
    rx_fa = wrap(DEG(a2) - REST.ang("forearm." + side) - chest_rx - rx_ua)
    set_rot("forearm." + side, rx_fa)
    acc = chest_rx + rx_ua + rx_fa
    a_hand = DEG(math.atan2(GROUND - wrist_z, -0.026))
    rx_hd = wrap(a_hand - REST.ang("hand." + side) - acc)
    set_rot("hand." + side, rx_hd)


PEN = []
REACH = []


def _rx(v):
    if v is None:
        return 0.0
    if isinstance(v, (int, float)):
        return float(v)
    return float(v[0])


def compose(torso, root_h, hock_l, hock_r, wrist_l, wrist_r, tag=""):
    for b in pb:
        b.rotation_euler = (0.0, 0.0, 0.0)
        b.location = (0.0, 0.0, 0.0)

    for name in TORSO:
        v = torso.get(name)
        if v is None:
            continue
        if isinstance(v, (int, float)):
            v = (v, 0.0, 0.0)
        set_rot(name, v[0], v[1], v[2])

    hips_rx = _rx(torso.get("hips"))
    chest_rx = hips_rx + _rx(torso.get("spine")) + _rx(torso.get("chest"))

    pb["root"].location = (0.0, 0.0, root_h)
    upd()

    solve_hind("L", hock_l[0], hock_l[1], hips_rx)
    solve_hind("R", hock_r[0], hock_r[1], hips_rx)
    solve_front("L", wrist_l[0], wrist_l[1], chest_rx)
    solve_front("R", wrist_r[0], wrist_r[1], chest_rx)
    upd()

    # 可达性检查（防止 IK 被静默 clamp）
    for side, tgt in (("L", hock_l), ("R", hock_r)):
        hip = rig.matrix_world @ pb["thigh." + side].head
        d = math.hypot(tgt[0] - hip.y, tgt[1] - hip.z)
        if d > REST.len("thigh." + side) + REST.len("shin." + side) - 0.002:
            REACH.append("%s hind %s d=%.4f" % (tag, side, d))
    for side, tgt in (("L", wrist_l), ("R", wrist_r)):
        sh = rig.matrix_world @ pb["upper_arm." + side].head
        d = math.hypot(tgt[0] - sh.y, tgt[1] - sh.z)
        if d > REST.len("upper_arm." + side) + REST.len("forearm." + side) - 0.002:
            REACH.append("%s front %s d=%.4f" % (tag, side, d))

    z = min((rig.matrix_world @ pb["toe.L"].tail).z,
            (rig.matrix_world @ pb["toe.R"].tail).z,
            (rig.matrix_world @ pb["hand.L"].tail).z,
            (rig.matrix_world @ pb["hand.R"].tail).z)
    if z < -0.004:
        PEN.append("%s z=%.4f" % (tag, z))

    rots = {}
    for name in ALL:
        e = pb[name].rotation_euler
        rots[name] = (e.x, e.y, e.z)
    return rots, tuple(pb["root"].location)


def foot_curve(p, rest_y, stride, lift, base_z):
    """p∈[0,1)：<DUTY 支撑（脚向 +Y=后方扫），其余摆动（抬脚向前）"""
    p = p % 1.0
    if p < DUTY:
        u = p / DUTY
        return rest_y - stride * 0.5 + stride * u, base_z
    u = (p - DUTY) / (1.0 - DUTY)
    return rest_y + stride * 0.5 - stride * u, base_z + lift * math.sin(math.pi * u)


def iter_fcurves(act):
    out = []
    if hasattr(act, "layers"):
        for layer in act.layers:
            for strip in layer.strips:
                for bag in getattr(strip, "channelbags", []):
                    out.extend(bag.fcurves)
    else:
        out.extend(act.fcurves)
    return out


def build_walk():
    frames = []
    for i in range(17):
        t = i / 16.0
        f = 1 + i * 2
        # 腿
        hy_l, hz_l = foot_curve(t + PH_LH, HOCK0[0], STRIDE_H, LIFT_H, HOCK_Z)
        hy_r, hz_r = foot_curve(t + PH_RH, HOCK0[0], STRIDE_H, LIFT_H, HOCK_Z)
        wy_l, wz_l = foot_curve(t + PH_LF, WRIST0[0], STRIDE_F, LIFT_F, WRIST_Z)
        wy_r, wz_r = foot_curve(t + PH_RF, WRIST0[0], STRIDE_F, LIFT_F, WRIST_Z)

        # 身体：2 个起伏周期 + 侧摆 1 个周期
        bob = ROOT_H + BOB * math.sin(2 * math.pi * 2 * t + math.pi * 0.5)
        sway = 2.2 * math.sin(2 * math.pi * t)
        pitch2 = 0.9 * math.sin(2 * math.pi * 2 * t)

        torso = {
            "spine": (pitch2 * 0.6, 0, sway * 0.35),
            "chest": (0.5, 0, -sway * 0.5),
            "hips": (-pitch2 * 0.4, 0, sway * 0.25),
            # 头部反向补偿，尽量保持相对稳定（真猫走路会稳住头）
            "neck": (-pitch2 * 0.7, 0, -sway * 0.55),
            "head": (-pitch2 * 0.5, 0, -sway * 0.45),
            "tail.01": (8, 0, 8 * math.sin(2 * math.pi * t + 0.4)),
            "tail.02": (5, 0, 10 * math.sin(2 * math.pi * t + 0.8)),
            "tail.03": (2, 0, 12 * math.sin(2 * math.pi * t + 1.2)),
        }

        r, rl = compose(torso, bob, (hy_l, hz_l), (hy_r, hz_r),
                        (wy_l, wz_l), (wy_r, wz_r), tag="Walk f%d" % f)
        frames.append((f, r, rl))
    return frames


# ---- 只换 Cat_Walk
old = bpy.data.actions.get("Cat_Walk")
if old is not None:
    bpy.data.actions.remove(old)

frames = build_walk()

act = bpy.data.actions.new("Cat_Walk")
act.use_fake_user = True
rig.animation_data.action = act
act.use_frame_range = True
act.frame_start = 1
act.frame_end = END
for f, rots, rloc in frames:
    for bn, r in rots.items():
        pb[bn].rotation_euler = r
    pb["root"].location = rloc
    for bn in ALL:
        pb[bn].keyframe_insert("rotation_euler", frame=f)
    pb["root"].keyframe_insert("location", frame=f)
for fc in iter_fcurves(act):
    for kp in fc.keyframe_points:
        kp.interpolation = 'BEZIER'
        kp.handle_left_type = 'AUTO_CLAMPED'
        kp.handle_right_type = 'AUTO_CLAMPED'

# 把 NLA 轨道上的 strip 指回新 action
ad = rig.animation_data
fixed = 0
for tr in ad.nla_tracks:
    if tr.name != "Cat_Walk":
        continue
    for st in tr.strips:
        st.action = act
        st.name = "Cat_Walk"
        fixed += 1
print("NLA strip repointed:", fixed)

rig.animation_data.action = None
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=BLEND)

# ---- 导出
os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
for o in bpy.data.objects:
    o.select_set(False)
rig.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX,
    use_selection=True,
    bake_anim=True,
    bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=True,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    use_mesh_modifiers=True,
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
)
print("PENETRATION", PEN if PEN else "none")
print("REACH_OVERFLOW", REACH if REACH else "none")
print("WALK_OK", OUT_FBX, os.path.getsize(OUT_FBX))
print("NLA", [(t.name, len(t.strips)) for t in ad.nla_tracks])
