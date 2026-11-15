"""Check the baked walk clip: how far does toe.L actually travel fore-aft?

    blender --background out/cat_anim.blend --python probe_walk.py
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


def use(name):
    act = bpy.data.actions[name]
    ad.action = act
    try:
        if len(act.slots):
            ad.action_slot = act.slots[0]
    except Exception as e:
        print("[WALK] slot set failed", e)
    return act


def sample(clip, n=24):
    act = use(clip)
    pb = rig.pose.bones["toe.L"]
    ys, zs = [], []
    f0, f1 = act.frame_start, act.frame_end
    for k in range(n):
        f = f0 + (f1 - f0) * k / float(n)
        bpy.context.scene.frame_set(int(round(f)))
        bpy.context.view_layer.update()
        m = rig.matrix_world @ pb.matrix
        ys.append(m.translation.y)
        zs.append(m.translation.z)
    print("[WALK] %-10s f=%d..%d toeL y %.4f..%.4f span=%.4f  z %.4f..%.4f" %
          (clip, f0, f1, min(ys), max(ys), max(ys) - min(ys), min(zs), max(zs)))


for c in ("Cat_Idle", "Cat_Walk", "Cat_Run"):
    sample(c)
