"""Precise contact/loop audit (correct frame indexing) + Workbench side-view contact sheets."""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = r"D:/unity/NOCTURNE/Art/Cat/v3"
sys.path.insert(0, HERE)
import anim  # noqa: E402

RIG = bpy.data.objects["CatRig"]
SC = bpy.context.scene
AD = RIG.animation_data
OUT = os.path.join(HERE, "out")
QA = os.path.join(OUT, "qa_v5")
os.makedirs(QA, exist_ok=True)
FPS = 30.0
LINES = []


def log(*a):
    s = " ".join(str(x) for x in a)
    LINES.append(s)
    print("[QA] " + s)


def chain(t):
    return list(anim.leg_bones(t))


def set_clip(name):
    for tr in AD.nla_tracks:
        tr.mute = True
    act = bpy.data.actions.get(name)
    AD.action = act
    try:
        AD.action_slot = act.slots[0]
    except Exception:
        pass
    return act


def paw(t):
    return RIG.matrix_world @ RIG.pose.bones[chain(t)[-1]].tail


def lowest_mesh_z():
    ob = anim.mesh_obj()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    z = min(v.co.z for v in me.vertices)
    ev.to_mesh_clear()
    return z


def audit(name, cfg):
    set_clip(name)
    n = cfg["n"]
    T = n / FPS
    stride = cfg["stride"]
    rows = {}
    for f in range(n + 1):
        SC.frame_set(f + 1)          # bake wrote key at frame f+1
        bpy.context.view_layer.update()
        rows[f] = {t: (paw(t).y, paw(t).z) for t in ["LF", "RF", "LH", "RH"]}
        rows[f]["minZ"] = lowest_mesh_z()
    log("=== %s  n=%d T=%.2fs stride=%.4fm naturalSpeed=%.3f"
        % (name, n, T, stride, stride / (cfg["duty"] * T)))

    for t in ["LF", "RF", "LH", "RH"]:
        plant = []
        for f in range(n):
            ph = (f / float(n) - cfg["phase"][t]) % 1.0
            if ph < cfg["duty"]:
                plant.append(f)
        zs = [rows[f][t][1] for f in plant]
        steps = []
        for i in range(len(plant) - 1):
            if plant[i + 1] == plant[i] + 1:      # only consecutive frames, no wrap
                steps.append(rows[plant[i + 1]][t][0] - rows[plant[i]][t][0])
        theory = stride / (cfg["duty"] * n)
        avg = sum(steps) / len(steps) if steps else 0.0
        dev = max(abs(s - avg) for s in steps) if steps else 0.0
        log("  %-3s planted=%2d  pawZ span=%.4fm  step avg=%.4f (theory %.4f) dev=%.4f"
            % (t, len(plant), max(zs) - min(zs), avg, theory, dev))
        # swing lift
        swing = [rows[f][t][1] for f in range(n)
                 if ((f / float(n) - cfg["phase"][t]) % 1.0) >= cfg["duty"]]
        if swing:
            log("       swing pawZ %.4f..%.4f (lift %.4fm)" % (min(swing), max(swing), max(swing) - min(swing)))

    seam = max(abs(rows[0][t][k] - rows[n][t][k])
               for t in ["LF", "RF", "LH", "RH"] for k in (0, 1))
    zs = [rows[f]["minZ"] for f in range(n)]
    log("  LOOP SEAM %.7fm | deformed minZ %.4f..%.4f (penetration %.4f, max float %.4f)"
        % (seam, min(zs), max(zs), min(zs), max(zs)))
    return rows


def setup_render():
    sc = SC
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.display.shading.cavity_type = "BOTH"
    sc.display.shading.background_type = "VIEWPORT"
    sc.display.shading.background_color = (0.14, 0.15, 0.18)
    sc.render.resolution_x = 420
    sc.render.resolution_y = 320
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    for o in list(bpy.data.objects):
        if o.name.startswith("QA_"):
            bpy.data.objects.remove(o, do_unlink=True)
    cd = bpy.data.cameras.new("QA_CamData")
    cam = bpy.data.objects.new("QA_Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cd.type = "ORTHO"
    cd.ortho_scale = 0.60
    cam.location = (1.0, -0.035, 0.17)
    cam.rotation_euler = Vector((-1.0, 0.0, -0.03)).to_track_quat("-Z", "Y").to_euler()
    # a ground reference plane
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, -0.035, 0.0))
    pl = bpy.context.object
    pl.name = "QA_Ground"
    pl.scale = (0.30, 0.55, 1.0)
    pl.rotation_euler = (0, 0, 0)
    mat = bpy.data.materials.new("QA_GroundMat")
    mat.diffuse_color = (0.24, 0.26, 0.30, 1.0)
    pl.data.materials.append(mat)
    return cam


def render_frames(name, cfg, frames, prefix):
    set_clip(name)
    cam = SC.camera
    for i, f in enumerate(frames):
        SC.frame_set(f + 1)
        bpy.context.view_layer.update()
        SC.render.filepath = os.path.join(QA, "%s_%02d.png" % (prefix, i))
        bpy.ops.render.render(write_still=True)
    log("rendered %d frames for %s" % (len(frames), name))


WALK = dict(n=24, stride=0.130, duty=0.65, phase={"LH": 0.0, "LF": 0.25, "RH": 0.5, "RF": 0.755})
RUN = dict(n=12, stride=0.100, duty=0.28, phase={"LH": 0.0, "RH": 0.06, "LF": 0.45, "RF": 0.51})

audit("Cat_Walk", WALK)
audit("Cat_Run", RUN)
setup_render()
render_frames("Cat_Walk", WALK, [0, 3, 6, 9, 12, 15, 18, 21], "walk")
render_frames("Cat_Run", RUN, [0, 2, 3, 5, 6, 7, 9, 11], "run")

with open(os.path.join(OUT, "gait_v5_qa.txt"), "w", encoding="utf-8") as fh:
    fh.write("\n".join(LINES))
print("\n".join(LINES))
