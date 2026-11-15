"""v5 -- rebuild Cat_Walk / Cat_Run with world-space paw IK, authored live in Blender.

Method (keyframe-coach / morph / blender-kiln):
  * plan footfall phases first, then block poses, then audit contacts+loop seam
  * in-place cycle: during stance the paw moves BACK relative to the body at exactly
    the speed the cat travels in Unity, so the paw is world-stationary (no slide)
  * damped-least-squares 2D IK solves shoulder/elbow/wrist each frame; the last
    bone is counter-rotated so the paw stays level (digitigrade, morph)
  * per-frame ground() closes the loop on the *deformed mesh* lowest point
  * two-bouncing-ball body, head counter-rotation, tail lag, folded swing leg
"""
import math
import os
import sys

import bpy

HERE = r"D:/unity/NOCTURNE/Art/Cat/v3"
sys.path.insert(0, HERE)
import anim  # noqa: E402

# 导出脚本会清理 QA_* 物体，anim.mesh_obj() 有可能缓存到已删除的 Object。
# 按名字重新取，避免 "StructRNA of type Object has been removed"。
def _mesh_obj():
    ob = bpy.data.objects.get("CatBody")
    if ob is None:
        ob = next(o for o in bpy.data.objects
                  if o.type == "MESH" and o.name.startswith("Cat"))
    return ob


anim.mesh_obj = _mesh_obj

# v6：只重 K Cat_Run。Cat_Walk 直接复用现有 action，逐帧数据保证不变。
REBAKE_WALK = False

RIG = bpy.data.objects["CatRig"]
BONES = RIG.data.bones
SC = bpy.context.scene
TAU = math.pi * 2.0
FPS = 30.0
REPORT = []


def log(*a):
    s = " ".join(str(x) for x in a)
    REPORT.append(s)
    print("[GAIT5] " + s)


# ---------------------------------------------------------------- geometry
def chain(tag):
    return list(anim.leg_bones(tag))


def segs(tag):
    out = []
    for n in chain(tag):
        d = BONES[n].tail_local - BONES[n].head_local
        out.append((d.y, d.z))
    return out


def rest_offset(tag):
    """foot tip (last bone tail) relative to first leg bone head, rest pose."""
    b0 = BONES[chain(tag)[0]]
    tip = BONES[chain(tag)[-1]].tail_local
    return (tip.y - b0.head_local.y, tip.z - b0.head_local.z)


def rest_foot(tag):
    """Absolute rest foot-tip position in armature space (the plant target)."""
    v = BONES[chain(tag)[-1]].tail_local
    return (v.y, v.z)


def r2(a, v):
    c, s = math.cos(a), math.sin(a)
    return (c * v[0] - s * v[1], s * v[0] + c * v[1])


def fk(H, B, sg, a):
    """foot tip = first-bone-head + all 4 segments, rotated by accumulated world-X."""
    A = B
    y, z = H
    for i in range(4):
        A += a[i]
        d = r2(A, sg[i])
        y += d[0]
        z += d[1]
    return (y, z)


def solve(H, B, sg, target, prior):
    """Damped least squares IK over all four leg segments in the Y-Z plane.

    The 4th (paw) bone is strongly regularised toward rest so it stays roughly
    level instead of being counter-rotated by 100+ degrees -- that flip was
    what drove the skinned paw through the floor. Regularising every frame
    toward the same analytic prior also makes the solution deterministic, so
    frame 0 and frame N are bit-identical (seamless loop)."""
    a = list(prior)
    lam, h = 0.004, 1e-4
    mu = (0.002, 0.002, 0.002, 0.035)
    lim = (0.60, 1.30, 1.50, 0.55)
    for _ in range(30):
        p = fk(H, B, sg, a)
        ex, ey = target[0] - p[0], target[1] - p[1]
        if ex * ex + ey * ey < 1e-11:
            break
        j0, j1 = [], []
        for k in range(4):
            ap = list(a)
            ap[k] += h
            pp = fk(H, B, sg, ap)
            j0.append((pp[0] - p[0]) / h)
            j1.append((pp[1] - p[1]) / h)
        m00 = sum(x * x for x in j0) + lam
        m01 = sum(j0[k] * j1[k] for k in range(4))
        m11 = sum(x * x for x in j1) + lam
        det = m00 * m11 - m01 * m01
        if abs(det) < 1e-13:
            break
        w0 = (m11 * ex - m01 * ey) / det
        w1 = (-m01 * ex + m00 * ey) / det
        for k in range(4):
            d = j0[k] * w0 + j1[k] * w1 - mu[k] * (a[k] - prior[k])
            a[k] += max(-0.20, min(0.20, d))
        for k in range(4):
            a[k] = max(-lim[k], min(lim[k], a[k]))
    return a


def body_x_rotation(pb):
    """World-X rotation already applied by torso bones (chest/hips) on this leg."""
    m = pb.matrix.to_3x3() @ pb.bone.matrix_local.to_3x3().inverted()
    return math.atan2(m[2][1], m[1][1])


def set_angles(pbs, a):
    for k, pb in enumerate(pbs):
        pb.rotation_euler = anim.wrot(pb, a[k], 0.0, 0.0)


def real_tip(rig, ch):
    v = rig.pose.bones[ch[-1]].tail
    return (v.y, v.z)


def solve_real(rig, ch, target, prior):
    """Closed-loop IK measured on the ACTUAL pose bones.

    The analytic FK model drifted ~2cm from Blender's real hierarchy (nested
    torso rotations, bone roll), which sank the skinned paws through the floor.
    Here the Jacobian is measured by perturbing each real pose bone and reading
    the real foot tip, so the paw lands on the target to sub-millimetre accuracy.
    """
    pbs = [rig.pose.bones[n] for n in ch]
    a = list(prior)
    h = 1e-3
    set_angles(pbs, a)
    lam = 0.0008
    mu = (0.0015, 0.0015, 0.0015, 0.022)
    lim = (0.70, 1.45, 1.60, 0.60)
    for _ in range(14):
        bpy.context.view_layer.update()
        p = real_tip(rig, ch)
        ex, ey = target[0] - p[0], target[1] - p[1]
        if ex * ex + ey * ey < 1e-10:
            break
        j0, j1 = [], []
        for k in range(4):
            orig = pbs[k].rotation_euler.copy()
            a[k] += h
            pbs[k].rotation_euler = anim.wrot(pbs[k], a[k], 0.0, 0.0)
            bpy.context.view_layer.update()
            pp = real_tip(rig, ch)
            a[k] -= h
            pbs[k].rotation_euler = orig
            j0.append((pp[0] - p[0]) / h)
            j1.append((pp[1] - p[1]) / h)
        m00 = sum(x * x for x in j0) + lam
        m01 = sum(j0[k] * j1[k] for k in range(4))
        m11 = sum(x * x for x in j1) + lam
        det = m00 * m11 - m01 * m01
        if abs(det) < 1e-14:
            break
        w0 = (m11 * ex - m01 * ey) / det
        w1 = (-m01 * ex + m00 * ey) / det
        for k in range(4):
            d = j0[k] * w0 + j1[k] * w1 - mu[k] * (a[k] - prior[k])
            a[k] += max(-0.25, min(0.25, d))
        for k in range(4):
            a[k] = max(-lim[k], min(lim[k], a[k]))
        set_angles(pbs, a)
    return a


# ---------------------------------------------------------------- gait tracks
def foot_track(ph, duty, stride, lift, reach, roll):
    if ph < duty:
        u = ph / duty
        dy = -stride * 0.5 + stride * u      # +Y = backwards relative to body
        dz = 0.0
        pitch = 0.0
        planted = True
    else:
        u = (ph - duty) / (1.0 - duty)
        k = anim.sstep(u)
        dy = stride * 0.5 - stride * k - reach * math.sin(math.pi * u)
        dz = lift * max(0.0, math.sin(math.pi * u)) ** 0.85
        pitch = roll * math.sin(math.pi * u)
        planted = False
    return dy, dz, pitch, planted


# ---------------------------------------------------------------- body poses
def walk_body(p):
    # two bouncing balls: hips ride hind feet (plant 0 / .5), chest rides front (.25 / .75)
    hip = math.cos(TAU * 2.0 * p)
    chest = math.cos(TAU * 2.0 * (p - 0.25))
    pose = {
        "hips": (0.016 * hip, 0.0, 0.018 * math.sin(TAU * p)),
        "spine": (0.010 * (chest - hip), 0.0, -0.022 * math.sin(TAU * p)),
        "spine2": (0.008 * (chest - hip), 0.0, -0.014 * math.sin(TAU * p)),
        "chest": (0.010 * chest, 0.0, 0.014 * math.sin(TAU * p)),
        "neck": (-0.026 * chest, 0.0, 0.024 * math.sin(TAU * p)),
        "head": (0.020 * chest, 0.0, -0.032 * math.sin(TAU * p)),
        "jaw": (0.0, 0.0, 0.0),
    }
    pose.update(anim.tail_pose(p - 0.10, sway=0.19, lift=0.10))
    pose.update(anim.ear_pose(fwd=0.02))
    return pose, {"hips": (0.0, 0.0, -0.003 + 0.003 * hip)}


def run_body(p):
    # v6 -- gallop 的辨识度来自「背部弓起 -> 拉平」的大幅伸缩，不是把脖子拉长。
    # v5 为避免狗样把整条脊柱幅度减半，结果连伸缩对比也一起丢了，看起来像
    # 原地高频倒腾腿。这里把 spine/spine2/chest/hips 的 flex 加回约 +70%，
    # neck/head 继续保持与脊柱齐平、只做很小的反向补偿 —— 头不抬、颈不伸，
    # 就不会回到 v4 那种狗的轮廓。
    flex = math.cos(TAU * (p - 0.10))
    bounce = math.sin(TAU * 2.0 * p)     # 一周期两次重心起伏（two bouncing balls）
    pose = {
        "hips": (-0.038 + 0.062 * flex, 0.0, 0.026 * math.sin(TAU * p)),
        "spine": (0.078 * flex, 0.0, -0.032 * math.sin(TAU * p)),
        "spine2": (0.088 * flex, 0.0, -0.026 * math.sin(TAU * p)),
        "chest": (-0.026 + 0.056 * flex, 0.0, 0.022 * math.sin(TAU * p)),
        "neck": (-0.022 - 0.024 * flex, 0.0, 0.020 * math.sin(TAU * p)),
        "head": (0.020 + 0.020 * flex, 0.0, -0.022 * math.sin(TAU * p)),
        "jaw": (0.0, 0.0, 0.0),
    }
    pose.update(anim.tail_pose(p - 0.16, sway=0.42, lift=0.52))
    pose.update(anim.ear_pose(fwd=0.14))
    return pose, {"hips": (0.0, 0.0, -0.010 + 0.006 * bounce)}


def run_air(p):
    """Two flight phases, strictly inside the gaps where NO paw is planted.

    With duty=0.30 and phases LH 0 / RH .06 / LF .45 / RF .51 the planted
    windows are [0,.36] (hind) and [.45,.81] (front). bump(t,c,w) covers
    [c-w,c+w], so the two waves must stay inside [.36,.45] and [.81,1.0].
    v5's wider waves leaked into the stance tails and lifted planted paws
    (audit RH stance zRange 0.0375m = visible hover).
    """
    return 0.055 * anim.bump(p, 0.405, 0.042) + 0.048 * anim.bump(p, 0.905, 0.090)


# ---------------------------------------------------------------- clip config
# Stride is bounded by leg reach: sqrt(L^2 - h^2) with L=0.195m, h~0.17m
# gives ~0.095m of forward/backward reach, so 0.13m total is safe and still a
# full cat step (body length 0.43m), not a shuffle.
# clipNaturalSpeed = stride / (duty * T); Unity's SyncAnimatorSpeed scales the
# clip by (realSpeed / clipNaturalSpeed) so paws still travel at the real speed.
WALK = dict(
    name="Cat_Walk", n=24, stride=0.130, duty=0.65,
    phase={"LH": 0.000, "LF": 0.250, "RH": 0.500, "RF": 0.755},
    lift=0.030, reach=0.012, roll=0.10, crouch=0.012,
    body=walk_body, air=lambda p: 0.0,
)
# v6 Run: speed-matched gallop.
#   stride = realSpeed * duty * T 是「支撑相脚掌不滑」的硬约束，不是可以随便加的
#   数值。v5 在 0.893 m/s 下被锁成 stride=0.100m —— 比 Walk 的 0.130m 还小，
#   所以腿只能在原地高频倒腾。v6 把实际速度提到 1.25 m/s，同步把 stride 提到
#   0.150m（终于大于 Walk），每周期前进 0.50m = 1.16 个身长。
#   reach 现在 > stride/2，摆动中期脚掌会真的「够」到落地点之前，形成前伸后蹬；
#   首尾 sin(pi*u)=0，落地位置和循环接缝都不受影响。
RUN = dict(
    name="Cat_Run", n=12, stride=0.150, duty=0.30,
    phase={"LH": 0.000, "RH": 0.060, "LF": 0.450, "RF": 0.510},
    lift=0.062, reach=0.100, roll=0.32, crouch=0.032,
    body=run_body, air=run_air,
)


def prior_for(tag, ph, duty, is_front):
    """Analytic start pose: elbows fold back, knees fold forward (digitigrade)."""
    sw = (ph / duty - 0.5) * 0.45 if ph < duty else -0.30 + 0.35 * anim.sstep((ph - duty) / (1 - duty))
    if is_front:
        return [sw * 0.55, 0.28 + 0.15 * abs(sw), -0.42 - 0.20 * abs(sw)]
    return [sw * 0.55, -0.34 - 0.18 * abs(sw), 0.52 + 0.22 * abs(sw)]


def bake(cfg, zlift=0.0):
    name, n = cfg["name"], cfg["n"]
    SC.frame_set(1)
    T = n / FPS
    stride = cfg["stride"]
    v_clip = stride / (cfg["duty"] * T)
    log("%s: frames=%d T=%.3fs duty=%.2f stride=%.4fm -> clipNaturalSpeed=%.3f m/s"
        % (name, n, T, cfg["duty"], stride, v_clip))

    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    ad = RIG.animation_data
    ad.action = act
    try:
        ad.action_slot = act.slots[0] if len(act.slots) else None
    except Exception:
        pass
    for t in ad.nla_tracks:
        t.mute = True

    tags = ["LF", "RF", "LH", "RH"]
    SG = {t: segs(t) for t in tags}
    FOOT = {t: rest_foot(t) for t in tags}
    CH = {t: chain(t) for t in tags}
    angle_log = {t: [] for t in tags}
    contact_log = {t: [] for t in tags}

    for f in range(n + 1):
        p = (f % n) / float(n)
        body_pose, body_loc = cfg["body"](p)
        air = cfg["air"](p)

        # Body height is authored; paws are NOT tied to the hips.
        # Sinking the hips creates knee/elbow bend, and IK absorbs the bob so
        # a planted paw stays at a fixed world Z (this is what kills foot hover).
        bl = dict(body_loc)
        h0 = bl.get("hips", (0.0, 0.0, 0.0))
        # zlift is added to BOTH the hips and the paw target, so the whole cat
        # rises rigidly: IK geometry is unchanged, solution is bit-identical,
        # and the skinned paw lands exactly on z=0 (calibration in main()).
        bl["hips"] = (h0[0], h0[1], h0[2] - cfg["crouch"] + air + zlift)
        anim.apply_pose(RIG, body_pose, bl)
        bpy.context.view_layer.update()

        for t in tags:
            ph = (p - cfg["phase"][t]) % 1.0
            dy, dz, pitch, planted = foot_track(
                ph, cfg["duty"], stride, cfg["lift"], cfg["reach"], cfg["roll"])
            y0, z0 = FOOT[t]
            # Absolute armature-space paw target. During flight both body and
            # paws rise together, so leg length never has to stretch.
            foot_y, foot_z = y0 + dy, z0 + dz + air + zlift
            is_front = t in ("LF", "RF")
            pr = prior_for(t, ph, cfg["duty"], is_front) + [pitch]
            a = solve_real(RIG, CH[t], (foot_y, foot_z), pr)
            if f < n:
                angle_log[t].append((a[0], a[1], a[2], a[3], planted))
                contact_log[t].append((foot_y, foot_z))

        anim.key_all(RIG, f + 1)

    for fc in anim._fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = n + 1
    log("%s baked keys=%d fcurves=%d" % (name, n + 1, len(anim._fcurves(act))))

    # ---- joint-angle + loop-seam audit
    for t in tags:
        aa = angle_log[t]
        if not aa:
            continue
        r0 = [max(x[k] for x in aa) - min(x[k] for x in aa) for k in range(4)]
        log("  %-3s angle ranges (deg) b0=%.1f b1=%.1f b2=%.1f paw=%.1f"
            % (t, *[math.degrees(x) for x in r0]))
    return act, angle_log, contact_log, cfg


# ---------------------------------------------------------------- calibration
def measure_min(act, n):
    """Global lowest deformed vertex across the whole clip.

    The skinned paw sits several cm below the paw bone's tail. One constant
    Z offset applied to every frame fixes penetration without changing the
    loop, the stance trajectory, or the joint solution."""
    ad = RIG.animation_data
    for tr in ad.nla_tracks:
        tr.mute = True
    ad.action = act
    try:
        ad.action_slot = act.slots[0]
    except Exception:
        pass
    ob = anim.mesh_obj()
    lo = 1e9
    for f in range(1, n + 2):
        SC.frame_set(f)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        lo = min(lo, min(v.co.z for v in me.vertices))
        ev.to_mesh_clear()
    return lo


# ---------------------------------------------------------------- audit via frame_set
def audit_clip(act, cfg, angle_log):
    ad = RIG.animation_data
    for t in ad.nla_tracks:
        t.mute = True
    ad.action = act
    try:
        ad.action_slot = act.slots[0]
    except Exception:
        pass
    n = cfg["n"]
    rows = {}
    for f in range(1, n + 2):
        SC.frame_set(f)
        bpy.context.view_layer.update()
        rows[f] = {}
        for t in ["LF", "RF", "LH", "RH"]:
            tip = RIG.pose.bones[chain(t)[-1]]
            v = RIG.matrix_world @ tip.tail
            rows[f][t] = (v.y, v.z)
    # stance slide: planted paw should move back at constant speed, Z constant
    for t in ["LF", "RF", "LH", "RH"]:
        ph_list = [((f - 1) / float(n) - cfg["phase"][t]) % 1.0 for f in range(1, n + 1)]
        planted = [f for f, ph in zip(range(1, n + 1), ph_list) if ph < cfg["duty"]]
        if len(planted) >= 2:
            ys = [rows[f][t][0] for f in planted]
            zs = [rows[f][t][1] for f in planted]
            steps = [ys[i + 1] - ys[i] for i in range(len(ys) - 1)]
            avg = sum(steps) / len(steps) if steps else 0.0
            dev = max(abs(s - avg) for s in steps) if steps else 0.0
            log("  audit %-3s stance frames=%d zRange=%.4fm stepAvg=%.4f stepDev=%.4f"
                % (t, len(planted), max(zs) - min(zs), avg, dev))
    # loop seam: frame 1 vs frame n+1
    seam = max(abs(rows[1][t][k] - rows[n + 1][t][k])
               for t in rows[1] for k in (0, 1))
    log("  audit LOOP SEAM paw pos delta = %.6fm (must be ~0)" % seam)

    # lowest deformed vertex per frame (floor penetration)
    ob = anim.mesh_obj()
    lows = []
    for f in range(1, n + 1):
        SC.frame_set(f)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        lows.append(min(v.co.z for v in me.vertices))
        ev.to_mesh_clear()
    log("  audit deformed minZ %.4f..%.4f (target 0, run may lift)"
        % (min(lows), max(lows)))
    return seam


# ---------------------------------------------------------------- main
def main():
    # weight / rig check first (requirement 1)
    ob = anim.mesh_obj()
    unweighted = sum(1 for v in ob.data.vertices
                     if not v.groups or sum(g.weight for g in v.groups) < 1e-4)
    maxgrp = max(len(v.groups) for v in ob.data.vertices)
    log("RIG CHECK bones=%d vertexGroups=%d verts=%d unweighted=%d maxInfluences=%d"
        % (len(BONES), len(ob.vertex_groups), len(ob.data.vertices), unweighted, maxgrp))
    for t in ["LF", "RF", "LH", "RH"]:
        L = sum(math.hypot(*d) for d in segs(t))
        log("  %-3s chain=%.4fm restOffset=(%.4f, %.4f)" % (t, L, *rest_offset(t)))

    # snapshot before overwriting
    bpy.ops.wm.save_as_mainfile(
        filepath=os.path.join(HERE, "out", "cat_anim_pre_v6.blend"), copy=True)

    # clear NLA so bake never mixes tracks
    ad = RIG.animation_data
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    ad.action = None

    made = {}
    for cfg in (WALK, RUN):
        if cfg is WALK and not REBAKE_WALK:
            keep = bpy.data.actions.get("Cat_Walk")
            if keep is None:
                raise RuntimeError("Cat_Walk missing; cannot keep it untouched")
            made["Cat_Walk"] = keep
            log("Cat_Walk KEPT unchanged (v6 re-keys Cat_Run only)")
            continue
        SC.frame_set(1)
        act, _, _, _ = bake(cfg, 0.0)
        lo = measure_min(act, cfg["n"])
        zlift = -lo + 0.0008
        log("%s calibration: first-pass minZ=%.4f -> constant paw lift %.4fm"
            % (cfg["name"], lo, zlift))
        SC.frame_set(1)
        act, al, cl, c = bake(cfg, zlift)
        made[cfg["name"]] = act
        audit_clip(act, cfg, al)

    # rebuild all 9 NLA tracks in the canonical order
    order = [c[0] for c in anim.CLIPS]
    for nm in order:
        act = made.get(nm) or bpy.data.actions.get(nm)
        if act is None:
            log("MISSING ACTION " + nm)
    pairs = [(nm, made.get(nm) or bpy.data.actions.get(nm)) for nm in order]
    pairs = [(nm, a) for nm, a in pairs if a is not None]
    anim.build_nla(RIG, pairs)
    ad.action = None
    anim.clear_pose(RIG)
    SC.frame_set(1)

    out = os.path.join(HERE, "out", "cat_anim.blend")
    bpy.ops.wm.save_as_mainfile(filepath=out)
    log("SAVED " + out)
    with open(os.path.join(HERE, "out", "gait_v5_report.txt"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(REPORT))
    return "\n".join(REPORT)


RESULT = main()
print(RESULT)
