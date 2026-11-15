"""Solve the leg fold for the "loaf" pose numerically, once, and print the
numbers so anim.py can hard-code them.

Grid + local refine on the lower-leg angle of each limb: we want every paw at
the same height as the belly so the cat actually rests on the floor instead of
dangling from its own feet.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402
import anim  # noqa: E402


def log(*a):
    print("[CAL]", *a)
    sys.stdout.flush()


def apply(rig, pose, loc):
    anim.clear_pose(rig)
    for name, rot in pose.items():
        pb = rig.pose.bones.get(name)
        if pb is None:
            continue
        pb.rotation_euler = anim.wrot(pb, rot[0], rot[1], rot[2])
    for name, v in (loc or {}).items():
        pb = rig.pose.bones.get(name)
        if pb is not None:
            pb.location = anim.wloc(pb, v)
    bpy.context.view_layer.update()


def paw_z(rig, bone):
    pb = rig.pose.bones[bone]
    return (rig.matrix_world @ pb.tail).z


def belly_z(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    z = min(v.co.z for v in me.vertices)
    ev.to_mesh_clear()
    return z


def solve(rig, joint, paw, pose, loc, target, rx0, ry=0.0, rz=0.0):
    """Coarse scan then refine; returns the world-axis rx that plants the paw."""
    best, best_err = rx0, 1e9
    for k in range(41):
        a = -3.0 + 6.0 * k / 40.0
        pose[joint] = (a, ry, rz)
        apply(rig, pose, loc)
        err = abs(paw_z(rig, paw) - target)
        if err < best_err:
            best, best_err = a, err
    step = 0.15
    for _ in range(14):
        moved = False
        for cand in (best - step, best + step):
            pose[joint] = (cand, ry, rz)
            apply(rig, pose, loc)
            err = abs(paw_z(rig, paw) - target)
            if err < best_err - 1e-5:
                best, best_err = cand, err
                moved = True
        if not moved:
            step *= 0.5
    return best, best_err


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "cat_rigged.blend"))
    rig = bpy.data.objects["CatRig"]
    mesh = bpy.data.objects["CatBody"]

    pose = anim.loaf_pose(0.0, 0.0, head_up=True)
    loc = {"hips": (0.0, -0.012, -0.100)}

    # 1. drop the body until the belly would touch if the legs vanished
    apply(rig, pose, loc)
    bz = belly_z(mesh)
    log("belly z with legs folded =", round(bz, 4))
    # scoot the whole body down so the belly sits on the floor
    loc["hips"] = (0.0, -0.012, -0.100 - bz)
    apply(rig, pose, loc)
    log("after drop, belly =", round(belly_z(mesh), 4))

    # 2. plant every paw at the ground
    target = 0.004
    out = {}
    for jn, pn, cur in (("forearm.L", "hand.L", pose["forearm.L"][0]),
                        ("forearm.R", "hand.R", pose["forearm.R"][0]),
                        ("shin.L", "toe.L", pose["shin.L"][0]),
                        ("shin.R", "toe.R", pose["shin.R"][0])):
        a, err = solve(rig, jn, pn, pose, loc, target, cur)
        out[jn] = a
        log("%-10s rx=%+.3f  err=%.4f  paw_z=%.4f" % (jn, a, err, paw_z(rig, pn)))

    # 3. final report
    apply(rig, pose, loc)
    log("final min_z", round(belly_z(mesh), 4))
    preview.setup_render(res=600, samples=14)
    preview.add_lights(power=1.0)
    preview.add_ground()
    preview.render_view("loaf", (0.55, 0.0, 0.16), (0, 0, 0.10),
                        os.path.join(OUT, "cal_loaf.png"), ortho=0.60)
    preview.render_view("loafq", (0.40, -0.34, 0.34), (0, -0.02, 0.09),
                        os.path.join(OUT, "cal_loaf_q.png"), focal=55.0)
    log("LOC", loc)
    log("SOLVED", {k: round(v, 3) for k, v in out.items()})


main()
