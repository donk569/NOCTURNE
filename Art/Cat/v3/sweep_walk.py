"""Sweep walk parameters and report how far each paw travels fore-aft.

    blender --background out/cat_rigged.blend --python sweep_walk.py
"""
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anim  # noqa: E402

rig = anim.rig_obj()
if rig.animation_data:
    rig.animation_data.action = None


def gait(p, amp, knee, duty, comp):
    pose = {}
    for tag in anim.LEGS:
        ph = (p + anim.PHASE[tag]) % 1.0
        b0, b1, b2, b3 = anim.leg_bones(tag)
        if ph < duty:
            u = ph / duty
            swing = anim.lerp(-amp, amp, u)
            knee_a = knee * 0.22 * math.sin(math.pi * u)
        else:
            u = (ph - duty) / (1.0 - duty)
            swing = anim.lerp(amp, -amp, anim.sstep(u))
            knee_a = knee * math.sin(math.pi * u) ** 1.2
        pose[b0] = (-swing, 0.0, 0.0)
        if tag in ("LF", "RF"):
            pose[b1] = (swing * 0.35 * comp - knee_a * 0.45, 0.0, 0.0)
            pose[b2] = (-knee_a * 1.25, 0.0, 0.0)
            pose[b3] = (knee_a * 0.85 + swing * 0.25 * comp, 0.0, 0.0)
        else:
            pose[b1] = (swing * 0.30 * comp + knee_a * 0.55, 0.0, 0.0)
            pose[b2] = (-knee_a * 1.10, 0.0, 0.0)
            pose[b3] = (knee_a * 0.75 - swing * 0.20 * comp, 0.0, 0.0)
    return pose


def span(amp, knee, duty, comp, n=24):
    ys = {"hand.L": [], "hand.R": [], "toe.L": [], "toe.R": []}
    zs = []
    for k in range(n):
        p = k / float(n)
        anim.apply_pose(rig, gait(p, amp, knee, duty, comp))
        bpy.context.view_layer.update()
        for nm in ys:
            pb = rig.pose.bones[nm]
            m = rig.matrix_world @ pb.matrix
            ys[nm].append(m.translation.y)
            if nm == "hand.L":
                zs.append(m.translation.z)
    out = {}
    for nm, v in ys.items():
        out[nm] = max(v) - min(v)
    return out, min(zs), max(zs)


print("[SWEEP] amp  comp  duty  | frontspan hindspan  footZ")
for amp in (0.40, 0.60, 0.80, 1.00, 1.20):
    for comp in (1.0, 0.5, 0.0):
        s, z0, z1 = span(amp, 0.60, 0.62, comp)
        print("[SWEEP] %.2f %.2f 0.62 | %.4f %.4f | %.3f..%.3f" %
              (amp, comp, max(s["hand.L"], s["hand.R"]), max(s["toe.L"], s["toe.R"]), z0, z1))
