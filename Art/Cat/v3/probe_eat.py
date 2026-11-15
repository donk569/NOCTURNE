"""Find eat-pose angles that put the muzzle down at bowl height.

    blender --background out/cat_rigged.blend --python probe_eat.py
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anim  # noqa: E402

rig = anim.rig_obj()
if rig.animation_data:
    rig.animation_data.action = None
mesh = anim.mesh_obj()


def eat(chew, t, down, head_pitch, front_a, front_b, hips_drop):
    pose = anim.eat_pose(chew, t, down=down)
    pose["head"] = (head_pitch, 0.0, 0.0)
    for tag in ("LF", "RF"):
        b0, b1, b2, b3 = anim.leg_bones(tag)
        pose[b0] = (front_a, 0.0, 0.0)
        pose[b1] = (front_b, 0.0, 0.0)
    anim.apply_pose(rig, pose, {"hips": (0.0, 0.0, hips_drop)})
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    zmin = min(v.co.z for v in me.vertices)
    # 猫朝向 -Y：鼻子 = 最靠前的顶点
    nose = min(me.vertices, key=lambda v: v.co.y)
    ev.to_mesh_clear()
    return nose.co.z, zmin, nose.co.y


print("[EAT] down head_p front_a front_b hipdz | nose_z  body_min_z  nose_y")
for down in (0.52, 0.78, 1.00, 1.20):
    for head_p in (0.30, 0.10, -0.10):
        for fa, fb in ((-0.16, 0.10), (-0.45, 0.35), (-0.70, 0.60)):
            nz, bz, ny = eat(0.0, 0.0, down, head_p, fa, fb, -0.02)
            print("[EAT] %.2f %.2f %.2f %.2f -0.02 | %.4f %.4f %.4f" % (down, head_p, fa, fb, nz, bz, ny))
