"""v3 stage B -- normalise the base mesh into cat-world coordinates.

Target contract (same one v2 used, so the Unity side does not change):
  * metres, paws on z = 0
  * cat faces -Y, +X is the cat's LEFT
  * height ~0.30 m, nose-to-tail-root ~0.50 m
"""
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "work", "cat_stand.fbx")
OUT = os.path.join(HERE, "out")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402

TARGET_H = 0.305          # top of the head, tail tip may go a bit higher
FACING = os.environ.get("CAT_FACING", "-Y")   # rotY applied after measuring


def log(*a):
    print("[PREP]", *a)
    sys.stdout.flush()


def bbox(ob):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for c in ob.bound_box:
        w = ob.matrix_world @ Vector(c)
        for i in range(3):
            lo[i] = min(lo[i], w[i])
            hi[i] = max(hi[i], w[i])
    return lo, hi


def world_verts(ob):
    mw = ob.matrix_world
    return [mw @ v.co for v in ob.data.vertices]


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(SRC))
    ob = [o for o in bpy.data.objects if o.type == "MESH"][0]
    ob.name = "CatBody"

    # 1. bake the object rotation into the mesh
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    lo, hi = bbox(ob)
    log("RAW dim", [round(hi[i] - lo[i], 4) for i in range(3)])

    # 2. uniform scale so total height == TARGET_H
    s = TARGET_H / (hi.z - lo.z)
    ob.matrix_world = Matrix.Scale(s, 4) @ ob.matrix_world
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lo, hi = bbox(ob)
    log("SCALED", round(s, 4), "dim", [round(hi[i] - lo[i], 4) for i in range(3)])

    # 3. centre in X, ground in Z, park the body mass around y = 0
    verts = world_verts(ob)
    cx = (lo.x + hi.x) * 0.5
    cy = (lo.y + hi.y) * 0.5
    ob.location.x -= cx
    ob.location.y -= cy
    ob.location.z -= lo.z
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    lo, hi = bbox(ob)
    log("GROUNDED dim", [round(hi[i] - lo[i], 4) for i in range(3)],
        "lo", [round(v, 4) for v in lo], "hi", [round(v, 4) for v in hi])

    # 4. which end is the head? highest point on the top 55% of the body tells us
    #    (ears / muzzle sit high, the tail base is lower)
    zs = [(v.co.z, v.co.y) for v in ob.data.vertices]
    top = sorted(zs, reverse=True)[:400]
    ytop = sum(y for _, y in top) / len(top)
    log("TOP400 mean y", round(ytop, 4), "-> head at", "LOW y" if ytop < 0 else "HIGH y")

    preview.setup_render(res=760, samples=16)
    preview.add_lights(power=1.0)
    preview.add_ground()
    tag = "b0"
    preview.four_views(OUT, tag, target=Vector((0, 0, 0.15)), ortho=0.62)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "base_raw.blend"))
    log("DONE")


main()
