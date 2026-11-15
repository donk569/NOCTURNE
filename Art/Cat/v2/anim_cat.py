"""Author the nine animation clips for the cat.

Poses are specified in WORLD axes (rx = pitch, ry = roll, rz = yaw) and converted
into each bone's local space, so bone roll never has to be reasoned about.
Everything is in-place: `root` never gets a location key.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

try:
    P = PARAMS
except NameError:
    P = {}

OUT = os.path.join(HERE, "out")
FPS = 30
TAU = math.pi * 2.0


def rig_obj():
    return bpy.data.objects["CatRig"]


# ---------------------------------------------------------------- pose maths
def _basis(pb):
    return pb.bone.matrix_local.to_3x3()


def wrot(pb, rx=0.0, ry=0.0, rz=0.0):
    M = _basis(pb)
    R = Matrix.Rotation(rz, 3, "Z") @ Matrix.Rotation(ry, 3, "Y") @ Matrix.Rotation(rx, 3, "X")
    return (M.inverted() @ R @ M).to_euler("XYZ")


def wloc(pb, v):
    M = _basis(pb)
    return M.inverted() @ Vector(v)


def clear_pose(rig):
    for pb in rig.pose.bones:
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)


def apply_pose(rig, pose, loc=None):
    clear_pose(rig)
    for name, rot in pose.items():
        pb = rig.pose.bones.get(name)
        if pb is None:
            continue
        if len(rot) == 3:
            pb.rotation_euler = wrot(pb, rot[0], rot[1], rot[2])
        else:
            pb.rotation_euler = wrot(pb, rot[0], rot[1], rot[2], )
    for name, v in (loc or {}).items():
        pb = rig.pose.bones.get(name)
        if pb is not None:
            pb.location = wloc(pb, v)


def key_all(rig, frame):
    for pb in rig.pose.bones:
        pb.keyframe_insert("rotation_euler", frame=frame)
        pb.keyframe_insert("location", frame=frame)


# ---------------------------------------------------------------- helpers
def lerp(a, b, t):
    return a + (b - a) * t


def sstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def bump(t, c=0.5, w=0.35):
    d = abs(t - c) / w
    return 0.0 if d > 1.0 else (math.cos(d * math.pi) + 1.0) * 0.5


LEGS = ["LF", "RF", "LH", "RH"]


def leg_bones(tag):
    if tag in ("LF", "RF"):
        s = tag[1]
        return ("shoulder." + s, "upper_arm." + s, "forearm." + s, "hand." + s)
    s = tag[1]
    return ("thigh." + s, "shin." + s, "foot." + s, "toe." + s)


PHASE = {"LH": 0.00, "LF": 0.25, "RH": 0.50, "RF": 0.75}


def gait_pose(p, amp=0.40, knee=0.55, duty=0.62, lift=1.0):
    """One frame of a lateral-sequence walk / trot as world-axis rotations."""
    pose = {}
    for tag in LEGS:
        ph = (p + PHASE[tag]) % 1.0
        b0, b1, b2, b3 = leg_bones(tag)
        if ph < duty:
            u = ph / duty
            swing = lerp(-amp, amp, u)          # -x = forward
            knee_a = knee * 0.22 * math.sin(math.pi * u)
        else:
            u = (ph - duty) / (1.0 - duty)
            swing = lerp(amp, -amp, sstep(u))
            knee_a = knee * math.sin(math.pi * u) ** 1.2 * lift
        # a little more flex right after lift-off
        pose[b0] = (-swing, 0.0, 0.0)
        if tag in ("LF", "RF"):
            pose[b1] = (swing * 0.35 - knee_a * 0.45, 0.0, 0.0)
            pose[b2] = (-knee_a * 1.25, 0.0, 0.0)
            pose[b3] = (knee_a * 0.85 + swing * 0.25, 0.0, 0.0)
        else:
            pose[b1] = (swing * 0.30 + knee_a * 0.55, 0.0, 0.0)
            pose[b2] = (-knee_a * 1.10, 0.0, 0.0)
            pose[b3] = (knee_a * 0.75 - swing * 0.20, 0.0, 0.0)
    return pose


def tail_pose(p, sway=0.16, lift=0.0, curl=0.0, phase=0.0):
    pose = {}
    for i in range(5):
        k = i / 4.0
        a = sway * (0.55 + 0.65 * k) * math.sin(TAU * (p - 0.10 * i) + phase)
        pose["tail.%02d" % (i + 1)] = (-lift * (1.0 - 0.5 * k) + curl * k, 0.0, a)
    return pose


def ear_pose(fwd=0.0, twitch=0.0):
    return {"ear.L": (-fwd, 0.0, twitch), "ear.R": (-fwd, 0.0, -twitch)}


# ---------------------------------------------------------------- clips
def clip_idle(n=60):
    frames = []
    for f in range(n + 1):
        p = f / float(n)
        breath = math.sin(TAU * p)
        pose = {}
        pose["hips"] = (0.010 * breath, 0.0, 0.0)
        pose["spine"] = (-0.020 * breath, 0.0, 0.004 * math.sin(TAU * p + 0.6))
        pose["spine2"] = (-0.015 * breath, 0.0, 0.0)
        pose["chest"] = (0.020 * breath, 0.0, 0.0)
        pose["neck"] = (-0.030 * breath, 0.0, 0.02 * math.sin(TAU * p * 0.5))
        pose["head"] = (0.015 * breath, 0.0, 0.05 * math.sin(TAU * p * 0.5 + 0.8))
        pose["jaw"] = (0.0, 0.0, 0.0)
        pose.update(tail_pose(p, sway=0.14, lift=-0.05))
        pose.update(ear_pose(fwd=0.03, twitch=0.03 * math.sin(TAU * p * 2.0)))
        for tag in LEGS:
            b0, b1, b2, b3 = leg_bones(tag)
            pose[b0] = (0.0, 0.0, 0.0)
            pose[b1] = (0.0, 0.0, 0.0)
            pose[b2] = (0.0, 0.0, 0.0)
            pose[b3] = (0.0, 0.0, 0.0)
        loc = {"hips": (0.0, 0.0, 0.0016 * breath)}
        frames.append((f, pose, loc))
    return frames


def clip_walk(n=24, amp=0.34, knee=0.60):
    frames = []
    for f in range(n + 1):
        p = (f % n) / float(n)
        pose = gait_pose(p, amp=amp, knee=knee, duty=0.62)
        bob = math.sin(TAU * 2.0 * p)
        pose["hips"] = (0.02 * bob, 0.03 * math.sin(TAU * p), 0.02 * math.sin(TAU * p))
        pose["spine"] = (0.02 * bob, 0.0, -0.03 * math.sin(TAU * p))
        pose["spine2"] = (-0.015 * bob, 0.0, -0.02 * math.sin(TAU * p))
        pose["chest"] = (0.0, 0.0, 0.02 * math.sin(TAU * p))
        pose["neck"] = (-0.03 * bob, 0.0, 0.03 * math.sin(TAU * p))
        pose["head"] = (0.02 * bob, 0.0, -0.05 * math.sin(TAU * p))
        pose.update(tail_pose(p, sway=0.20, lift=0.10))
        pose.update(ear_pose(fwd=0.02))
        loc = {"hips": (0.0, 0.0, 0.004 * bob)}
        frames.append((f, pose, loc))
    return frames


def clip_run(n=14, amp=0.62, knee=1.05):
    frames = []
    for f in range(n + 1):
        p = (f % n) / float(n)
        pose = gait_pose(p, amp=amp, knee=knee, duty=0.36, lift=1.35)
        bob = math.sin(TAU * 2.0 * p)
        pose["hips"] = (-0.10 + 0.06 * bob, 0.05 * math.sin(TAU * p), 0.05 * math.sin(TAU * p))
        pose["spine"] = (0.06 * bob, 0.0, -0.05 * math.sin(TAU * p))
        pose["spine2"] = (0.05 + 0.05 * bob, 0.0, -0.04 * math.sin(TAU * p))
        pose["chest"] = (-0.04, 0.0, 0.03 * math.sin(TAU * p))
        pose["neck"] = (-0.16 - 0.05 * bob, 0.0, 0.03 * math.sin(TAU * p))
        pose["head"] = (0.05 + 0.03 * bob, 0.0, -0.05 * math.sin(TAU * p))
        pose.update(tail_pose(p, sway=0.26, lift=0.45))
        pose.update(ear_pose(fwd=0.10))
        loc = {"hips": (0.0, 0.0, -0.012 + 0.014 * bob)}
        frames.append((f, pose, loc))
    return frames


def sit_pose(breath=0.0, t=0.0):
    pose = {}
    pose["hips"] = (0.34 + 0.01 * breath, 0.0, 0.0)
    pose["spine"] = (-0.16 - 0.015 * breath, 0.0, 0.0)
    pose["spine2"] = (-0.10 - 0.01 * breath, 0.0, 0.0)
    pose["chest"] = (-0.06 + 0.015 * breath, 0.0, 0.0)
    pose["neck"] = (-0.10 + 0.02 * breath, 0.0, 0.03 * math.sin(TAU * t * 0.4))
    pose["head"] = (-0.02, 0.0, 0.06 * math.sin(TAU * t * 0.4 + 0.7))
    for s in ("L", "R"):
        pose["thigh." + s] = (-0.95, 0.0, 0.06 if s == "L" else -0.06)
        pose["shin." + s] = (1.65, 0.0, 0.0)
        pose["foot." + s] = (-0.72, 0.0, 0.0)
        pose["toe." + s] = (0.30, 0.0, 0.0)
        pose["shoulder." + s] = (0.06, 0.0, 0.0)
        pose["upper_arm." + s] = (-0.04, 0.0, 0.0)
        pose["forearm." + s] = (0.02, 0.0, 0.0)
        pose["hand." + s] = (0.0, 0.0, 0.0)
    pose.update(tail_pose(t, sway=0.10, curl=0.18, lift=-0.20))
    pose.update(ear_pose(fwd=0.04, twitch=0.02 * math.sin(TAU * t * 1.5)))
    return pose


def clip_sit(n=70):
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        breath = math.sin(TAU * t)
        pose = sit_pose(breath, t)
        loc = {"hips": (0.0, -0.030, -0.058 + 0.002 * breath)}
        frames.append((f, pose, loc))
    return frames


def sleep_pose(breath=0.0, t=0.0):
    pose = {}
    pose["hips"] = (0.16 + 0.012 * breath, 0.0, 0.0)
    pose["spine"] = (-0.05 - 0.012 * breath, 0.0, 0.0)
    pose["spine2"] = (-0.02, 0.0, 0.0)
    pose["chest"] = (0.02 + 0.014 * breath, 0.0, 0.0)
    pose["neck"] = (0.34 + 0.02 * breath, 0.0, 0.06)
    pose["head"] = (0.20, 0.0, 0.30)
    for s in ("L", "R"):
        sgn = 1.0 if s == "L" else -1.0
        pose["thigh." + s] = (-1.15, 0.0, 0.10 * sgn)
        pose["shin." + s] = (1.95, 0.0, 0.0)
        pose["foot." + s] = (-0.85, 0.0, 0.0)
        pose["toe." + s] = (0.35, 0.0, 0.0)
        pose["shoulder." + s] = (0.10, 0.0, 0.0)
        pose["upper_arm." + s] = (0.95, 0.0, -0.10 * sgn)
        pose["forearm." + s] = (-1.75, 0.0, 0.0)
        pose["hand." + s] = (0.55, 0.0, 0.0)
    pose.update(tail_pose(t, sway=0.05, curl=0.30, lift=-0.35))
    pose.update(ear_pose(fwd=-0.05, twitch=0.0))
    return pose


def clip_sleep(n=100):
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        breath = math.sin(TAU * t)
        pose = sleep_pose(breath, t)
        loc = {"hips": (0.0, -0.045, -0.108 + 0.0025 * breath)}
        frames.append((f, pose, loc))
    return frames


def eat_pose(chew=0.0, t=0.0, down=0.72):
    pose = {}
    pose["hips"] = (0.02, 0.0, 0.0)
    pose["spine"] = (-0.02, 0.0, 0.0)
    pose["spine2"] = (0.04, 0.0, 0.0)
    pose["chest"] = (0.06, 0.0, 0.0)
    pose["neck"] = (down, 0.0, 0.0)
    pose["head"] = (0.30, 0.0, 0.0)
    pose["jaw"] = (0.12 + 0.10 * chew, 0.0, 0.0)
    for tag in LEGS:
        b0, b1, b2, b3 = leg_bones(tag)
        if tag in ("LF", "RF"):
            pose[b0] = (-0.16, 0.0, 0.0)
            pose[b1] = (0.10, 0.0, 0.0)
            pose[b2] = (0.02, 0.0, 0.0)
            pose[b3] = (0.0, 0.0, 0.0)
        else:
            pose[b0] = (0.10, 0.0, 0.0)
            pose[b1] = (-0.06, 0.0, 0.0)
            pose[b2] = (0.04, 0.0, 0.0)
            pose[b3] = (0.0, 0.0, 0.0)
    pose.update(tail_pose(t, sway=0.10, lift=0.10))
    pose.update(ear_pose(fwd=0.05))
    return pose


def clip_eat(n=54, down=0.72):
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        chew = math.sin(TAU * t * 3.0)
        pose = eat_pose(chew, t, down=down)
        loc = {"hips": (0.0, 0.0, -0.006)}
        frames.append((f, pose, loc))
    return frames


def clip_drink(n=54):
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        pose = eat_pose(0.35 * math.sin(TAU * t * 4.0), t, down=0.96)
        pose["jaw"] = (0.20 + 0.12 * math.sin(TAU * t * 5.0), 0.0, 0.0)
        pose["head"] = (0.44, 0.0, 0.0)
        loc = {"hips": (0.0, 0.0, -0.008)}
        frames.append((f, pose, loc))
    return frames


def clip_alert(n=40):
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        breath = math.sin(TAU * t)
        pose = {}
        pose["hips"] = (-0.03, 0.0, 0.0)
        pose["spine"] = (0.02, 0.0, 0.0)
        pose["spine2"] = (0.03, 0.0, 0.0)
        pose["chest"] = (-0.04 + 0.01 * breath, 0.0, 0.0)
        pose["neck"] = (-0.26, 0.0, 0.05 * math.sin(TAU * t))
        pose["head"] = (-0.10, 0.0, 0.10 * math.sin(TAU * t + 1.0))
        for tag in LEGS:
            b0, b1, b2, b3 = leg_bones(tag)
            pose[b0] = (-0.06 if tag in ("LF", "RF") else 0.06, 0.0, 0.0)
            pose[b1] = (0.0, 0.0, 0.0)
            pose[b2] = (0.10, 0.0, 0.0)
            pose[b3] = (-0.05, 0.0, 0.0)
        pose.update(tail_pose(t, sway=0.14, lift=0.75))
        pose.update(ear_pose(fwd=0.20, twitch=0.05 * math.sin(TAU * t * 3.0)))
        loc = {"hips": (0.0, 0.0, -0.006 + 0.002 * breath)}
        frames.append((f, pose, loc))
    return frames


def clip_pounce(n=30):
    """crouch -> launch -> airborne -> land, one-shot."""
    static = {
        "crouch": (0.30, -0.12, -0.30, -0.55, 0.95, -0.60),
    }
    frames = []
    for f in range(n + 1):
        t = f / float(n)
        pose = {}
        if t < 0.30:
            u = sstep(t / 0.30)
            crouch = u
            air = 0.0
            land = 0.0
        elif t < 0.52:
            u = sstep((t - 0.30) / 0.22)
            crouch = 1.0 - u
            air = u
            land = 0.0
        elif t < 0.72:
            air = 1.0
            crouch = 0.0
            land = 0.0
        else:
            u = sstep((t - 0.72) / 0.28)
            air = 1.0 - u
            land = u
            crouch = 0.0
        pose["hips"] = (0.34 * crouch - 0.20 * air + 0.10 * land, 0.0, 0.0)
        pose["spine"] = (-0.18 * crouch - 0.14 * air + 0.06 * land, 0.0, 0.0)
        pose["spine2"] = (-0.10 * crouch - 0.10 * air, 0.0, 0.0)
        pose["chest"] = (-0.08 * crouch - 0.06 * air, 0.0, 0.0)
        pose["neck"] = (-0.30 - 0.16 * air + 0.24 * land, 0.0, 0.05 * air)
        pose["head"] = (-0.06 - 0.10 * air + 0.18 * land, 0.0, 0.0)
        for tag in LEGS:
            b0, b1, b2, b3 = leg_bones(tag)
            if tag in ("LF", "RF"):
                pose[b0] = (-0.30 * crouch - 0.85 * air + 0.30 * land, 0.0, 0.0)
                pose[b1] = (0.55 * crouch - 0.55 * air + 0.30 * land, 0.0, 0.0)
                pose[b2] = (0.95 * crouch + 0.35 * air - 0.20 * land, 0.0, 0.0)
                pose[b3] = (-0.55 * crouch + 0.25 * air, 0.0, 0.0)
            else:
                pose[b0] = (0.45 * crouch - 0.70 * air + 0.25 * land, 0.0, 0.0)
                pose[b1] = (0.85 * crouch - 0.30 * air + 0.30 * land, 0.0, 0.0)
                pose[b2] = (-0.70 * crouch + 0.30 * air - 0.15 * land, 0.0, 0.0)
                pose[b3] = (0.35 * crouch, 0.0, 0.0)
        pose.update(tail_pose(t, sway=0.18, lift=0.30 + 0.45 * air))
        pose.update(ear_pose(fwd=0.12))
        z = -0.045 * crouch + 0.075 * air - 0.030 * land
        loc = {"hips": (0.0, 0.02 * air, z)}
        frames.append((f, pose, loc))
    return frames


CLIPS = [
    ("Cat_Idle", clip_idle, True),
    ("Cat_Walk", clip_walk, True),
    ("Cat_Run", clip_run, True),
    ("Cat_Sit", clip_sit, True),
    ("Cat_Sleep", clip_sleep, True),
    ("Cat_Eat", clip_eat, True),
    ("Cat_Drink", clip_drink, True),
    ("Cat_Alert", clip_alert, True),
    ("Cat_Pounce", clip_pounce, False),
]


# ---------------------------------------------------------------- baking
def bake_action(rig, name, frames):
    if rig.animation_data is None:
        rig.animation_data_create()
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    rig.animation_data.action = act
    try:
        rig.animation_data.action_slot = act.slots[0] if len(act.slots) else None
    except Exception:
        pass
    for (f, pose, loc) in frames:
        apply_pose(rig, pose, loc)
        key_all(rig, f + 1)
    for fc in _fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = frames[-1][0] + 1
    return act


def _fcurves(act):
    """Blender 5 layered actions: walk layers/strips/channelbags."""
    out = []
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    out.extend(bag.fcurves)
    except Exception:
        pass
    if not out:
        try:
            out = list(act.fcurves)
        except Exception:
            out = []
    return out


def build_nla(rig, actions):
    ad = rig.animation_data
    ad.action = None
    for tr in list(ad.nla_tracks):
        ad.nla_tracks.remove(tr)
    for name, act in actions:
        tr = ad.nla_tracks.new()
        tr.name = name
        st = tr.strips.new(name, 1, act)
        st.name = name
        st.action_frame_start = act.frame_start
        st.action_frame_end = act.frame_end


def main():
    rig = rig_obj()
    bpy.context.scene.render.fps = FPS
    actions = []
    for (name, fn, loop) in CLIPS:
        frames = fn()
        act = bake_action(rig, name, frames)
        actions.append((name, act))
        print("CLIP %-12s frames=%d fcurves=%d" % (name, len(frames) - 1, len(_fcurves(act))))
    build_nla(rig, actions)
    rig.animation_data.action = None
    clear_pose(rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_anim.blend"))
    print("SAVED anim")


main()
