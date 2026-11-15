# -*- coding: utf-8 -*-
"""黑猫 8 段动画构建（30 FPS）。

关键教训：摆姿势阶段**不能挂 Action**，否则 view_layer.update() 会按当前帧重新求值、
把手动摆的姿势覆盖掉（第一版坐姿/行走全部变形就是这个原因）。
所以拆两步：
  1) compose() -> 摆姿势、跑 IK、把每帧骨骼角度录进内存（此时 action = None）
  2) commit()  -> 挂上 Action，逐帧写入关键帧

腿部用平面 IK 落到地面，保证不浮空不穿地。
坐标约定（axes2.py 实测）：所有骨骼局部 X ≈ 世界 +X，屈伸绕世界 X，角度可线性叠加。
"""
import bpy, math, sys
from mathutils import Vector

sys.path.insert(0, r"D:/unity/NOCTURNE/Art/Cat/work")
import catik
from catik import ik2, wrap, DEG, RAD

SRC = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_final.blend"
OUT = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
FPS = 30
GROUND = 0.013
PAW_UP = 0.040          # 腕/跗关节高度

bpy.ops.wm.open_mainfile(filepath=SRC)
scene = bpy.context.scene
scene.render.fps = FPS
scene.render.fps_base = 1.0

rig = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        rig = o
if rig is None:
    raise RuntimeError("no armature")
pb = rig.pose.bones
for b in pb:
    b.rotation_mode = 'XYZ'

if rig.animation_data:
    rig.animation_data_clear()
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
rig.animation_data_create()
rig.animation_data.action = None

REST = catik.RestInfo(rig)
HOCK0 = (REST.bones["shin.L"]["tail"].y, REST.bones["shin.L"]["tail"].z)
WRIST0 = (REST.bones["forearm.L"]["tail"].y, REST.bones["forearm.L"]["tail"].z)

TORSO = ["hips", "spine", "chest", "neck", "head", "ear.L", "ear.R",
         "tail.01", "tail.02", "tail.03"]
LEGS = ["upper_arm.L", "upper_arm.R", "forearm.L", "forearm.R", "hand.L", "hand.R",
        "thigh.L", "thigh.R", "shin.L", "shin.R", "foot.L", "foot.R", "toe.L", "toe.R"]
ALL = TORSO + LEGS


def upd():
    bpy.context.view_layer.update()


def set_rot(name, rx=0.0, ry=0.0, rz=0.0):
    pb[name].rotation_euler = (RAD(rx), RAD(ry), RAD(rz))


def _rx(torso, name):
    v = torso.get(name)
    if v is None:
        return 0.0
    return float(v) if isinstance(v, (int, float)) else float(v[0])


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
    """wrist_y=None 表示腕关节放在肩正下方；dy 给出时相对肩位偏移（前伸用）"""
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


def compose(torso, root_h=0.0, hock=None, wrist=None,
            hind_ov=None, front_ov=None, hock_r=None, wrist_r=None,
            front_dy=None, tag=""):
    """摆一帧并返回 (骨骼角度字典, root 位移)。不挂 Action，纯内存。"""
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

    hips_rx = _rx(torso, "hips")
    chest_rx = hips_rx + _rx(torso, "spine") + _rx(torso, "chest")
    pb["root"].location = (0.0, 0.0, root_h)
    upd()

    if hind_ov is not None:
        for bone, rx in hind_ov.items():
            set_rot(bone, rx)
    else:
        hy, hz = hock if hock is not None else HOCK0
        hy2, hz2 = hock_r if hock_r is not None else (hy, hz)
        solve_hind("L", hy, hz, hips_rx)
        solve_hind("R", hy2, hz2, hips_rx)

    if front_ov is not None:
        for bone, rx in front_ov.items():
            set_rot(bone, rx)
    else:
        if wrist is None:
            wy, wz = None, PAW_UP
        else:
            wy, wz = wrist
        if wrist_r is None:
            wy2, wz2 = wy, wz
        else:
            wy2, wz2 = wrist_r
        solve_front("L", wy, wz, chest_rx, front_dy)
        solve_front("R", wy2, wz2, chest_rx, front_dy)
    upd()

    z = min((rig.matrix_world @ pb["toe.L"].tail).z,
            (rig.matrix_world @ pb["toe.R"].tail).z,
            (rig.matrix_world @ pb["hand.L"].tail).z,
            (rig.matrix_world @ pb["hand.R"].tail).z)
    if z < -0.004:
        PEN.append("脚 %s z=%.4f" % (tag, z))
    hz = (rig.matrix_world @ pb["head"].tail).z
    if hz < 0.004:
        PEN.append("头 %s z=%.4f" % (tag, hz))

    rots = {}
    for name in ALL:
        e = pb[name].rotation_euler
        rots[name] = (e.x, e.y, e.z)
    root_loc = tuple(pb["root"].location)
    return rots, root_loc


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


LOG = []


def commit(name, end, frames):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = end
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
    rig.animation_data.action = None
    LOG.append("%-12s 1..%-4d keys=%d" % (name, end, len(frames)))
    return act


def blend(a, b, u):
    keys = set(a.keys()) | set(b.keys())
    out = {}
    for k in keys:
        va = a.get(k, 0.0)
        vb = b.get(k, 0.0)
        va = (va, 0.0, 0.0) if isinstance(va, (int, float)) else va
        vb = (vb, 0.0, 0.0) if isinstance(vb, (int, float)) else vb
        out[k] = (va[0] + (vb[0] - va[0]) * u,
                  va[1] + (vb[1] - va[1]) * u,
                  va[2] + (vb[2] - va[2]) * u)
    return out


# ===================================================================== Idle
def build_idle():
    seq = [
        (1,  {}, 0.0),
        (23, {"chest": -1.6, "spine": -0.8, "neck": -1.0, "head": (0, 0, 3),
              "ear.L": -2, "ear.R": -2,
              "tail.01": (0, 0, 9), "tail.02": (0, 0, 11), "tail.03": (0, 0, 13)}, 0.0012),
        (45, {"chest": 0.4, "spine": 0.2, "head": (0, 0, 0.5),
              "tail.01": (0, 0, 2), "tail.02": (0, 0, 3), "tail.03": (0, 0, 4)}, 0.0),
        (68, {"chest": 1.5, "spine": 0.7, "neck": 0.8, "head": (0, 0, -3),
              "ear.L": 1.5, "ear.R": 1.5,
              "tail.01": (0, 0, -9), "tail.02": (0, 0, -11), "tail.03": (0, 0, -13)}, -0.0010),
        (90, {}, 0.0),
    ]
    frames = []
    for f, torso, rh in seq:
        r, rl = compose(torso, rh, tag="Idle f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Idle", 90, frames)


# ===================================================================== Walk
def foot_curve(p, rest_y, stride, lift):
    p = p % 1.0
    if p < 0.62:
        u = p / 0.62
        return rest_y + stride * (u - 0.5), GROUND
    u = (p - 0.62) / 0.38
    return rest_y + stride * (0.5 - u), GROUND + lift * math.sin(math.pi * u)


def build_walk():
    END = 33
    LH, LF, RH, RF = 0.75, 0.0, 0.25, 0.5
    frames = []
    for i in range(17):
        t = i / 16.0
        f = 1 + i * 2
        rh = 0.0045 * math.sin(2 * math.pi * 2 * t)
        sway = 2.4 * math.sin(2 * math.pi * t)
        torso = {
            "spine": (0.8 * math.sin(2 * math.pi * 2 * t), 0, sway * 0.35),
            "chest": (0.6, 0, -sway * 0.5),
            "hips": (0, 0, sway * 0.25),
            "neck": (-0.6 * math.sin(2 * math.pi * 2 * t), 0, -sway * 0.4),
            "head": (0, 0, -sway * 0.6),
            "tail.01": (8, 0, 7 * math.sin(2 * math.pi * t + 0.4)),
            "tail.02": (5, 0, 9 * math.sin(2 * math.pi * t + 0.8)),
            "tail.03": (2, 0, 11 * math.sin(2 * math.pi * t + 1.2)),
        }
        hy_l, hz_l = foot_curve(t + LH, HOCK0[0], 0.060, 0.020)
        hy_r, hz_r = foot_curve(t + RH, HOCK0[0], 0.060, 0.020)
        wy_l, wz_l = foot_curve(t + LF, WRIST0[0], 0.048, 0.016)
        wy_r, wz_r = foot_curve(t + RF, WRIST0[0], 0.048, 0.016)
        r, rl = compose(torso, rh, hock=(hy_l, hz_l), hock_r=(hy_r, hz_r),
                        wrist=(wy_l, wz_l), wrist_r=(wy_r, wz_r), tag="Walk f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Walk", END, frames)


# ===================================================================== Sit
# 关键：坐姿要把胸口抬住（hips 负值让脊柱上翘），只让后半身坐下，
# 否则整只猫一起下沉，前腿只能折成 Z 形（第一版的错误）。
SIT_TORSO = {"hips": -13, "spine": -6, "chest": 0, "neck": 5, "head": 3,
             "tail.01": 26, "tail.02": 18, "tail.03": 10}


def build_sit():
    seq = [
        (1, {}, 0.0, HOCK0, WRIST0),
        (12, {"hips": -4, "spine": -2, "neck": 2, "tail.01": 8}, -0.020,
         (0.096, 0.030), (None, 0.044)),
        (26, blend(SIT_TORSO, {}, 0.45), -0.045, (0.100, 0.030), (None, 0.042)),
        (38, SIT_TORSO, -0.064, (0.104, 0.030), (None, 0.040)),
        (48, {"hips": -14, "spine": -6, "chest": 0, "neck": 6, "head": 3,
              "tail.01": 24, "tail.02": 17, "tail.03": 9}, -0.066,
         (0.104, 0.030), (None, 0.040)),
    ]
    frames = []
    for f, torso, rh, hk, wr in seq:
        r, rl = compose(torso, rh, hock=hk, wrist=wr, tag="Sit f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Sit", 48, frames)


# ===================================================================== Sleep
# 用「狮身人面」趴卧：前腿向前平伸贴地，后腿收在身体两侧，头枕在前爪上。
# 比蜷缩更容易靠 3 根脊柱骨做对，也更像真猫睡觉。
SLEEP_TORSO = {"hips": 0, "spine": 3, "chest": 5, "neck": 21, "head": (13, 0, 18),
               "ear.L": 5, "ear.R": 3,
               "tail.01": (8, 0, 30), "tail.02": (4, 0, 36), "tail.03": (0, 0, 40)}


def build_sleep():
    frames = []
    for f in (1, 30, 60, 90, 120):
        ph = (f - 1) / 120.0 * 2 * math.pi
        breath = 1.6 * math.sin(ph)
        torso = dict(SLEEP_TORSO)
        torso["chest"] = 5 + breath * 0.5
        torso["spine"] = 3 + breath * 0.28
        torso["neck"] = 21 - breath * 0.22
        rh = -0.086 + 0.0014 * math.sin(ph)
        r, rl = compose(torso, rh, hock=(0.104, 0.028), wrist=(None, 0.022),
                        front_dy=-0.078, tag="Sleep f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Sleep", 120, frames)


# ===================================================================== Eat
def build_eat():
    frames = []
    base = {"spine": 3, "chest": 8, "neck": 42, "head": 30, "ear.L": -3, "ear.R": -3,
            "tail.01": (4, 0, 4), "tail.02": (0, 0, 6), "tail.03": (0, 0, 8)}
    for i in range(31):
        f = 1 + i * 3
        ph = i / 30.0 * 2 * math.pi * 5.0
        torso = dict(base)
        torso["head"] = (30 + 3.6 * math.sin(ph), 0, 1.2 * math.sin(ph * 2.5))
        torso["neck"] = 42 + 1.2 * math.sin(ph)
        r, rl = compose(torso, -0.008, hock=HOCK0, wrist=WRIST0, tag="Eat f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Eat", 91, frames)


# ===================================================================== Drink
def build_drink():
    frames = []
    base = {"spine": 4, "chest": 9, "neck": 50, "head": 36, "ear.L": -4, "ear.R": -4,
            "tail.01": (2, 0, -3), "tail.02": (0, 0, -5), "tail.03": (0, 0, -7)}
    for i in range(31):
        f = 1 + i * 3
        ph = i / 30.0 * 2 * math.pi * 6.0
        torso = dict(base)
        torso["head"] = (36 + 2.6 * math.sin(ph), 0, 0.8 * math.sin(ph * 3.0))
        r, rl = compose(torso, -0.014, hock=HOCK0, wrist=WRIST0, tag="Drink f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Drink", 91, frames)


# ===================================================================== Alert
def build_alert():
    seq = [
        (1, {}, 0.0),
        (8, {"ear.L": -20, "ear.R": -20, "head": (0, 0, 10), "neck": (0, 0, 4),
             "tail.01": 8, "tail.02": 5}, 0.0015),
        (22, {"ear.L": -22, "ear.R": -22, "head": (-3, 0, 28), "neck": (0, 0, 9),
              "tail.01": 12, "tail.02": 8, "tail.03": 4}, 0.0015),
        (34, {"ear.L": -22, "ear.R": -21, "head": (-4, 0, 36), "neck": (0, 0, 11),
              "tail.01": 12, "tail.02": 8, "tail.03": 5}, 0.0012),
        (44, {"ear.L": -18, "ear.R": -24, "head": (-4, 0, 32), "neck": (0, 0, 10),
              "tail.01": 11, "tail.02": 7, "tail.03": 4}, 0.0012),
        (52, {"ear.L": -20, "ear.R": -20, "head": (-3, 0, 14), "neck": (0, 0, 4),
              "tail.01": 6, "tail.02": 4}, 0.0008),
        (60, {}, 0.0),
    ]
    frames = []
    for f, torso, rh in seq:
        r, rl = compose(torso, rh, tag="Alert f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Alert", 60, frames)


# ===================================================================== Pounce
def build_pounce():
    air_launch = {
        "thigh.L": 30, "thigh.R": 30, "shin.L": -16, "shin.R": -16,
        "foot.L": -12, "foot.R": -12, "toe.L": 8, "toe.R": 8,
        "upper_arm.L": -24, "upper_arm.R": -24, "forearm.L": 12, "forearm.R": 12,
        "hand.L": -4, "hand.R": -4,
    }
    air_apex = {
        "thigh.L": 44, "thigh.R": 44, "shin.L": 24, "shin.R": 24,
        "foot.L": 10, "foot.R": 10, "toe.L": 6, "toe.R": 6,
        "upper_arm.L": -54, "upper_arm.R": -54, "forearm.L": -20, "forearm.R": -20,
        "hand.L": -10, "hand.R": -10,
    }
    air_desc = {
        "thigh.L": 16, "thigh.R": 16, "shin.L": 28, "shin.R": 28,
        "foot.L": 2, "foot.R": 2,
        "upper_arm.L": -38, "upper_arm.R": -38, "forearm.L": -4, "forearm.R": -4,
        "hand.L": 8, "hand.R": 8,
    }
    hind_trail = {"thigh.L": 26, "thigh.R": 26, "shin.L": 36, "shin.R": 36,
                  "foot.L": 4, "foot.R": 4}

    seq = [
        (1, {}, 0.0, HOCK0, WRIST0, None, None),
        (8, {"hips": 6, "spine": 3, "chest": 2, "neck": 4, "head": 2,
             "tail.01": 30, "tail.02": 18, "tail.03": 8},
         -0.026, (0.098, 0.030), (None, 0.040), None, None),
        (15, {"hips": 10, "spine": 6, "chest": 2, "neck": 5, "head": 3,
              "ear.L": 8, "ear.R": 8,
              "tail.01": 44, "tail.02": 26, "tail.03": 12},
         -0.046, (0.110, 0.029), (None, 0.038), None, None),
        (17, {"hips": 7, "spine": 1, "chest": -1, "neck": 0, "head": -2,
              "ear.L": 4, "ear.R": 4,
              "tail.01": 34, "tail.02": 20, "tail.03": 9},
         -0.018, (0.108, 0.028), (None, 0.038), None, None),
        (19, {"hips": 4, "spine": -6, "chest": -4, "neck": -6, "head": -8,
              "tail.01": 20, "tail.02": 12},
         0.030, None, None, air_launch, air_launch),
        (26, {"hips": -6, "spine": -10, "chest": -6, "neck": -14, "head": -12,
              "tail.01": 10, "tail.02": 6},
         0.200, None, None, air_apex, air_apex),
        (33, {"hips": 2, "spine": -2, "chest": 0, "neck": 4, "head": 6,
              "tail.01": 6, "tail.02": 4},
         0.098, None, None, air_desc, air_desc),
        (39, {"hips": 6, "spine": 4, "chest": 2, "neck": 8, "head": 6},
         0.002, None, (None, 0.036), hind_trail, None),
        (45, {"hips": 10, "spine": 5, "chest": 2, "neck": 5, "head": 3},
         -0.042, (0.104, 0.029), (None, 0.038), None, None),
        (52, {"hips": 8, "spine": 4, "chest": 2, "neck": 4, "head": 2},
         -0.018, (0.096, 0.030), (None, 0.042), None, None),
        (60, {}, 0.0, HOCK0, WRIST0, None, None),
    ]
    frames = []
    for f, torso, rh, hk, wr, hov, fov in seq:
        r, rl = compose(torso, rh, hock=hk, wrist=wr, hind_ov=hov, front_ov=fov,
                        tag="Pounce f%d" % f)
        frames.append((f, r, rl))
    commit("Cat_Pounce", 60, frames)


build_idle()
build_walk()
build_sit()
build_sleep()
build_eat()
build_drink()
build_alert()
build_pounce()

# ---------------------------------------------------------------- NLA
rig.animation_data.action = None
ad = rig.animation_data
for t in list(ad.nla_tracks):
    ad.nla_tracks.remove(t)

ORDER = ["Cat_Idle", "Cat_Walk", "Cat_Sit", "Cat_Sleep",
         "Cat_Eat", "Cat_Drink", "Cat_Alert", "Cat_Pounce"]
NLA_ERR = []
for nm in ORDER:
    act = bpy.data.actions.get(nm)
    if act is None:
        NLA_ERR.append(nm + ": missing")
        continue
    try:
        tr = ad.nla_tracks.new()
        tr.name = nm
        st = tr.strips.new(nm, 1, act)
        st.name = nm
    except Exception as e:
        NLA_ERR.append("%s: %s" % (nm, e))

scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("ACTIONS", sorted(a.name for a in bpy.data.actions))
for l in LOG:
    print("  " + l)
print("NLA_ERR", NLA_ERR if NLA_ERR else "none")
print("PENETRATION", PEN if PEN else "none")
print("BUILD_OK", OUT)
