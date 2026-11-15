"""Tune the lying-down pose interactively: one pose in, one render out."""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402
import anim  # noqa: E402

TAG = os.environ.get("LY_TAG", "ly")


def log(*a):
    print("[LY]", *a)
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


def zmin(mesh):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    z = min(v.co.z for v in me.vertices)
    ev.to_mesh_clear()
    return z


def pose_rest(head_up=True, leg_out=0.55):
    """Lying flat: front legs stretched forward on the floor, hind legs folded."""
    p = {}
    p["hips"] = (0.14, 0.0, 0.0)
    p["spine"] = (-0.05, 0.0, 0.0)
    p["spine2"] = (-0.02, 0.0, 0.0)
    p["chest"] = (0.02, 0.0, 0.0)
    if head_up:
        p["neck"] = (-0.34, 0.0, 0.0)
        p["head"] = (0.12, 0.0, 0.0)
    else:
        p["neck"] = (0.42, 0.0, 0.0)
        p["head"] = (0.24, 0.0, 0.10)
    for s, sgn in (("L", 1.0), ("R", -1.0)):
        # front leg: swing down-forward so the forearm lies along the floor
        p["shoulder." + s] = (0.10, 0.0, 0.0)
        p["upper_arm." + s] = (-1.30, 0.0, -leg_out * sgn)
        p["forearm." + s] = (0.22, 0.0, 0.0)
        p["hand." + s] = (0.10, 0.0, 0.0)
        # hind leg: fold, knee out to the side so it does not sink into the belly
        p["thigh." + s] = (-0.75, 0.0, 0.55 * sgn)
        p["shin." + s] = (1.85, 0.0, 0.0)
        p["foot." + s] = (-0.95, 0.0, 0.0)
        p["toe." + s] = (0.30, 0.0, 0.0)
    return p


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "cat_rigged.blend"))
    rig = bpy.data.objects["CatRig"]
    mesh = bpy.data.objects["CatBody"]
    preview.setup_render(res=600, samples=14)
    preview.add_lights(power=1.0)
    preview.add_ground()

    for head_up in (True, False):
        pose = pose_rest(head_up)
        loc = {"hips": (0.0, -0.010, 0.0)}
        apply(rig, pose, loc)
        # drop until the lowest point touches the floor
        for _ in range(3):
            z = zmin(mesh)
            loc["hips"] = (loc["hips"][0], loc["hips"][1], loc["hips"][2] - z)
            apply(rig, pose, loc)
        z = zmin(mesh)
        pose["tail.01"] = (0.25 if head_up else 0.10, 0.0, 0.55)
        pose["tail.02"] = (0.10, 0.0, 0.55)
        pose["tail.03"] = (0.0, 0.0, 0.5)
        pose["tail.04"] = (0.0, 0.0, 0.45)
        pose["tail.05"] = (0.0, 0.0, 0.4)
        apply(rig, pose, loc)
        tag = "up" if head_up else "dn"
        log(tag, "min_z", round(zmin(mesh), 4), "hips_dz", round(loc["hips"][2], 4))
        preview.render_view("s", (0.52, 0.0, 0.14), (0, 0, 0.08),
                            os.path.join(OUT, "%s_%s_side.png" % (TAG, tag)), ortho=0.62)
        preview.render_view("q", (0.42, -0.36, 0.30), (0, -0.03, 0.07),
                            os.path.join(OUT, "%s_%s_q.png" % (TAG, tag)), focal=52.0)
    log("DONE")


main()
