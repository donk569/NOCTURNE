"""Walk-cycle QA: paw heights, fore-aft travel, body bob, head stability.

    blender --background out/cat_anim.blend --python qa_walk.py
"""
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
rig = bpy.data.objects["CatRig"]
ad = rig.animation_data
ad.action = None
for t in ad.nla_tracks:
    t.mute = True

PAWS = ["hand.L", "hand.R", "toe.L", "toe.R"]


def use(name):
    act = bpy.data.actions[name]
    ad.action = act
    try:
        if len(act.slots):
            ad.action_slot = act.slots[0]
    except Exception:
        pass
    return act


def sample(clip, n):
    act = use(clip)
    f0, f1 = int(act.frame_start), int(act.frame_end)
    rows = []
    for k in range(n):
        f = f0 + (f1 - f0) * k / float(n)
        bpy.context.scene.frame_set(int(round(f)))
        bpy.context.view_layer.update()
        r = {"f": int(round(f))}
        for p in PAWS:
            m = rig.matrix_world @ rig.pose.bones[p].matrix
            r[p] = (m.translation.y, m.translation.z)
        r["head"] = (rig.matrix_world @ rig.pose.bones["head"].matrix).translation.z
        r["hips"] = (rig.matrix_world @ rig.pose.bones["hips"].matrix).translation.z
        rows.append(r)
    print("[QA] %s frames %d..%d" % (clip, f0, f1))
    print("[QA]  f   handL_y  z  | handR_y  z  | toeL_y  z   | toeR_y  z   | head_z hips_z")
    for r in rows:
        print("[QA] %2d  %7.4f %.3f | %7.4f %.3f | %7.4f %.3f | %7.4f %.3f | %.4f %.4f" % (
            r["f"],
            r["hand.L"][0], r["hand.L"][1],
            r["hand.R"][0], r["hand.R"][1],
            r["toe.L"][0], r["toe.L"][1],
            r["toe.R"][0], r["toe.R"][1],
            r["head"], r["hips"]))
    for p in PAWS:
        ys = [r[p][0] for r in rows]
        zs = [r[p][1] for r in rows]
        print("[QA] %-7s fore-aft span=%.4f  lift span=%.4f  minz=%.4f" %
              (p, max(ys) - min(ys), max(zs) - min(zs), min(zs)))
    hz = [r["head"] for r in rows]
    hp = [r["hips"] for r in rows]
    print("[QA] head z span=%.4f   hips z span=%.4f" % (max(hz) - min(hz), max(hp) - min(hp)))


sample("Cat_Walk", 24)
sample("Cat_Run", 14)
