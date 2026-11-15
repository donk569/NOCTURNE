"""Render a walk/run filmstrip from the side so the gait can actually be eyeballed.

    blender --background out/cat_anim.blend --python film.py -- Cat_Walk 8
"""
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
CLIP = argv[0] if argv else "Cat_Walk"
N = int(argv[1]) if len(argv) > 1 else 8
TAG = argv[2] if len(argv) > 2 else CLIP.lower()

rig = bpy.data.objects["CatRig"]
ad = rig.animation_data
ad.action = None
for t in ad.nla_tracks:
    t.mute = True
act = bpy.data.actions[CLIP]
ad.action = act
try:
    if len(act.slots):
        ad.action_slot = act.slots[0]
except Exception:
    pass

sc = bpy.context.scene
for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH"):
    try:
        sc.render.engine = eng
        break
    except Exception:
        continue
sc.render.resolution_x = 340
sc.render.resolution_y = 260
sc.render.resolution_percentage = 100
sc.render.film_transparent = False
sc.world.use_nodes = True
bg = sc.world.node_tree.nodes.get("Background")
if bg:
    bg.inputs[0].default_value = (0.09, 0.10, 0.12, 1.0)
    bg.inputs[1].default_value = 1.0

# ground plane so contact reads
bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0))
plane = bpy.context.object
m = bpy.data.materials.new("Ground")
m.use_nodes = True
bs = None
for nd in m.node_tree.nodes:
    if nd.type == "BSDF_PRINCIPLED":
        bs = nd
        break
if bs is not None:
    bs.inputs["Base Color"].default_value = (0.18, 0.19, 0.21, 1)
plane.data.materials.append(m)

cam_data = bpy.data.cameras.new("FilmCam")
cam = bpy.data.objects.new("FilmCam", cam_data)
sc.collection.objects.link(cam)
cam_data.lens = 55
cam.location = Vector((1.35, -0.02, 0.20))
target = Vector((0.0, -0.02, 0.15))
d = target - cam.location
cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
sc.camera = cam

for nm, loc, energy in (("Key", (2.0, -1.6, 2.2), 260), ("Fill", (-2.2, -1.2, 1.2), 90)):
    ld = bpy.data.lights.new(nm, "AREA")
    ld.energy = energy
    ld.size = 2.0
    lo = bpy.data.objects.new(nm, ld)
    sc.collection.objects.link(lo)
    lo.location = Vector(loc)
    dd = Vector((0, 0, 0.15)) - lo.location
    lo.rotation_euler = dd.to_track_quat("-Z", "Y").to_euler()

f0, f1 = int(act.frame_start), int(act.frame_end)
paths = []
for k in range(N):
    f = f0 + (f1 - f0) * k / float(N)
    sc.frame_set(int(round(f)))
    sc.render.filepath = os.path.join(OUT, "film_%s_%02d.png" % (TAG, k))
    bpy.ops.render.render(write_still=True)
    paths.append(sc.render.filepath)
    print("[FILM] frame %d -> %s" % (int(round(f)), sc.render.filepath))
print("[FILM] done", len(paths))
