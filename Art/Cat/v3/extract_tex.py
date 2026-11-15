"""v3 -- the AI base FBX carries its PBR maps embedded. Extract them to disk so
the normal map (fur grain + eye curvature) can be reused by the new material."""
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "work", "cat_stand.fbx")
DST = os.path.join(HERE, "out", "raw_tex")


def log(*a):
    print("[TEX]", *a)
    sys.stdout.flush()


def main():
    os.makedirs(DST, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(SRC))
    for im in bpy.data.images:
        if im.name == "Render Result" or im.size[0] == 0:
            continue
        im.file_format = "PNG"
        path = os.path.join(DST, os.path.basename(im.name))
        im.filepath_raw = path
        try:
            im.save()
            log("SAVED", path, tuple(im.size), im.channels)
        except Exception as e:
            log("FAIL", im.name, e)
    log("DONE")


main()
