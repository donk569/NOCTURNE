"""Deformation QA: put the rig into a few hard poses and render them opaque."""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402


def log(*a):
    print("[POSE]", *a)
    sys.stdout.flush()


def wrot(rig, name, rx=0.0, ry=0.0, rz=0.0):
    """Rotate a bone using WORLD axes (rx pitch, ry roll, rz yaw)."""
    pb = rig.pose.bones.get(name)
    if pb is None:
        return
    M = pb.bone.matrix_local.to_3x3()
    R = Matrix.Rotation(rz, 3, "Z") @ Matrix.Rotation(ry, 3, "Y") @ Matrix.Rotation(rx, 3, "X")
    pb.rotation_euler = (M.inverted() @ R @ M).to_euler("XYZ")


def clear(rig):
    for pb in rig.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)


POSES = {
    "walk": {
        "forearm.L": (0.55,), "hand.L": (-0.5,), "upper_arm.L": (-0.3,),
        "forearm.R": (-0.45,), "hand.R": (0.35,), "upper_arm.R": (0.25,),
        "thigh.L": (-0.4,), "shin.L": (0.5,), "foot.L": (-0.4,),
        "thigh.R": (0.35,), "shin.R": (-0.3,), "foot.R": (0.2,),
        "neck": (0.06,), "head": (-0.05,),
        "tail.01": (-0.55,), "tail.02": (-0.35,), "tail.03": (-0.2, 0.25),
    },
    "tuck": {
        "thigh.L": (1.1,), "shin.L": (-1.5,), "foot.L": (0.7,),
        "thigh.R": (1.1,), "shin.R": (-1.5,), "foot.R": (0.7,),
        "forearm.L": (-1.0,), "forearm.R": (-1.0,),
        "neck": (-0.35,), "head": (0.3,),
        "tail.01": (0.5, 0.6), "tail.02": (0.3, 0.5), "tail.03": (0.2, 0.4),
    },
    "stretch": {
        "forearm.L": (-0.9,), "forearm.R": (-0.9,), "upper_arm.L": (0.5,),
        "upper_arm.R": (0.5,), "neck": (0.5,), "head": (-0.4,),
        "tail.01": (-0.9,), "tail.02": (-0.5,), "tail.03": (-0.4,),
    },
}


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "cat_rigged.blend"))
    rig = bpy.data.objects["CatRig"]
    mesh = bpy.data.objects["CatBody"]
    preview.setup_render(res=700, samples=16)
    preview.add_lights(power=1.0)
    preview.add_ground()

    for name, pose in POSES.items():
        clear(rig)
        for bone, vals in pose.items():
            vals = list(vals) + [0.0] * (3 - len(vals))
            wrot(rig, bone, *vals)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        me = mesh.evaluated_get(dg).to_mesh()
        zmin = min(v.co.z for v in me.vertices)
        mesh.evaluated_get(dg).to_mesh_clear()
        log(name, "min_z", round(zmin, 4))
        preview.render_view(name, (0.55, 0.0, 0.16), (0, 0, 0.15),
                            os.path.join(OUT, "pose_%s.png" % name), ortho=0.70)
    clear(rig)
    log("DONE")


main()
