"""v3 stage A -- import the expert-pipeline base mesh (Hunyuan3D output) and
inspect / normalise it before we touch the rig.

    blender --background --python import_base.py
"""
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "work", "cat_stand.glb")
OUT = os.path.join(HERE, "out")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402


def log(*a):
    print("[BASE]", *a)
    sys.stdout.flush()


def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def main():
    wipe()
    os.makedirs(OUT, exist_ok=True)
    # NOTE: the "glb" the cloud pipeline saved is really Kaydara FBX bytes --
    # the download writes the same payload under every requested extension.
    with open(SRC, "rb") as f:
        magic = f.read(20)
    if magic.startswith(b"Kaydara FBX"):
        bpy.ops.import_scene.fbx(filepath=os.path.abspath(SRC))
    else:
        bpy.ops.import_scene.gltf(filepath=os.path.abspath(SRC))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    log("MESHES", [(o.name, len(o.data.vertices), len(o.data.polygons)) for o in meshes])
    for o in meshes:
        me = o.data
        log(" UV", o.name, [l.name for l in me.uv_layers])
        log(" MAT", o.name, [m.name if m else None for m in me.materials])
        lo = Vector((1e9,) * 3)
        hi = Vector((-1e9,) * 3)
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
        log(" BBOX", o.name, [round(v, 4) for v in lo], [round(v, 4) for v in hi],
            "dim", [round(hi[i] - lo[i], 4) for i in range(3)])
        log(" XFORM loc", [round(v, 4) for v in o.location],
            "rot", [round(v, 4) for v in o.rotation_euler],
            "scale", [round(v, 4) for v in o.scale])
    for m in bpy.data.materials:
        if m.use_nodes:
            imgs = [n.image.name for n in m.node_tree.nodes
                    if n.type == "TEX_IMAGE" and n.image]
            log(" MATIMG", m.name, imgs)
    for im in bpy.data.images:
        if im.name != "Render Result":
            log(" IMG", im.name, tuple(im.size), im.channels, im.filepath)
    log("DONE")


main()
