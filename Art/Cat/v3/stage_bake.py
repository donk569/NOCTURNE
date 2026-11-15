"""v3 stage C -- decimate to a game budget and bake an object-space position map.

Why a position map: the AI base mesh ships a 4096^2 atlas we cannot author into.
Baking object coordinates into that atlas once through Cycles gives us a cheap,
exact UV -> 3D oracle, so the coat can be painted in 3D space with numpy
(stripes that wrap the body correctly, no UV seam artefacts) instead of being
drawn in UV space.

Outputs
-------
out/POS.npy        (H, W, 3) object-space xyz per texel, linear
out/POS_meta.json  bbox used for the normalisation + eye anchors
out/base_dec.blend
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
RES = 2048
TARGET_TRIS = 60000


def log(*a):
    print("[BAKE]", *a)
    sys.stdout.flush()


def load(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    ob = bpy.data.objects["CatBody"]
    return ob


def old_albedo():
    img = bpy.data.images.get("texture_pbr_20250901.png")
    if img is None:
        for i in bpy.data.images:
            if i.name.lower().endswith(".png"):
                img = i
                break
    return img


def sample_eyes(ob):
    """Find the two iris centres by looking for amber texels on the old albedo."""
    img = old_albedo()
    W, H = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    uvl = ob.data.uv_layers.active.data
    hits = []
    for poly in ob.data.polygons:
        for li in poly.loop_indices:
            v = ob.data.vertices[ob.data.loops[li].vertex_index]
            uv = uvl[li].uv
            x = min(W - 1, max(0, int(uv[0] * W)))
            y = min(H - 1, max(0, int(uv[1] * H)))
            c = px[y, x]
            # amber: strong red, mid green, weak blue, reasonably bright
            if c[0] > 0.16 and c[1] > 0.055 and c[2] < c[0] * 0.55 and c[0] > c[2] * 2.2:
                hits.append(tuple(v.co))
    log("AMBER_TEXELS", len(hits))
    if not hits:
        return []
    pts = np.array(hits, dtype=np.float32)
    # two clusters by x sign around the head
    eyes = []
    for sign in (1, -1):
        sel = pts[(pts[:, 0] * sign) > 0.004]
        if len(sel) < 8:
            continue
        # keep the densest blob: median centre, then trim 1.5x median radius
        c = np.median(sel, axis=0)
        d = np.linalg.norm(sel - c, axis=1)
        r = np.median(d) * 2.2 + 0.002
        sel = sel[d < r]
        c = sel.mean(axis=0)
        rad = float(np.percentile(np.linalg.norm(sel - c, axis=1), 92))
        eyes.append((c.tolist(), rad, len(sel)))
    return eyes


def decimate(ob, target_tris):
    tris = sum(max(0, len(p.vertices) - 2) for p in ob.data.polygons)
    ratio = min(1.0, float(target_tris) / tris)
    bpy.context.view_layer.objects.active = ob
    mod = ob.modifiers.new("Dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    tris2 = sum(max(0, len(p.vertices) - 2) for p in ob.data.polygons)
    log("DECIMATE", tris, "->", tris2, "ratio", round(ratio, 4))


def obj_bbox(ob):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for v in ob.data.vertices:
        for i in range(3):
            lo[i] = min(lo[i], v.co[i])
            hi[i] = max(hi[i], v.co[i])
    return lo, hi


def bake_pos(ob, lo, hi):
    size = Vector((hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
    img = bpy.data.images.new("POS", RES, RES, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"

    mat = bpy.data.materials.new("BAKE_POS")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sub = nt.nodes.new("ShaderNodeVectorMath")
    sub.operation = "SUBTRACT"
    sub.inputs[1].default_value = tuple(lo)
    div = nt.nodes.new("ShaderNodeVectorMath")
    div.operation = "DIVIDE"
    div.inputs[1].default_value = (size.x, size.y, size.z)
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(tc.outputs["Object"], sub.inputs[0])
    nt.links.new(sub.outputs[0], div.inputs[0])
    nt.links.new(div.outputs[0], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])

    ob.data.materials.clear()
    ob.data.materials.append(mat)

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.cycles.use_denoising = False
    sc.render.bake.margin = 12
    sc.render.bake.use_clear = True
    img_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    img_node.image = img
    mat.node_tree.nodes.active = img_node
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.bake(type="EMIT", margin=12, use_clear=True)
    log("BAKED", img.size[:])
    return img


def main():
    ob = load(os.path.join(OUT, "base_raw.blend"))
    eyes = sample_eyes(ob)
    log("EYES", eyes)

    decimate(ob, TARGET_TRIS)
    lo, hi = obj_bbox(ob)
    log("BBOX", [round(v, 4) for v in lo], [round(v, 4) for v in hi])
    img = bake_pos(ob, lo, hi)

    W, H = img.size
    buf = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    pos = buf[:, :, :3]
    cov = (pos.sum(axis=2) > 1e-6).mean()
    log("COVERAGE", round(float(cov), 4))
    # de-normalise back to object space
    size = np.array([hi.x - lo.x, hi.y - lo.y, hi.z - lo.z], dtype=np.float32)
    xyz = pos * size + np.array([lo.x, lo.y, lo.z], dtype=np.float32)
    np.save(os.path.join(OUT, "POS.npy"), xyz)

    meta = {
        "res": RES,
        "lo": list(map(float, lo)),
        "hi": list(map(float, hi)),
        "coverage": float(cov),
        "eyes": [{"c": e[0], "r": e[1], "n": e[2]} for e in eyes],
    }
    with open(os.path.join(OUT, "POS_meta.json"), "w") as f:
        json.dump(meta, f, indent=1)
    log("META", json.dumps(meta["eyes"]))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "base_dec.blend"))
    log("DONE")


main()
