"""v3 -- bake a world-space normal map into the same atlas (needed for masks
such as inner ear / nose / eye rim)."""
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
RES = 2048


def log(*a):
    print("[NRM]", *a)
    sys.stdout.flush()


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "base_dec.blend"))
    ob = bpy.data.objects["CatBody"]

    img = bpy.data.images.new("NRM", RES, RES, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"

    mat = bpy.data.materials.new("BAKE_NRM")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mad = nt.nodes.new("ShaderNodeVectorMath")
    mad.operation = "MULTIPLY_ADD"
    mad.inputs[1].default_value = (0.5, 0.5, 0.5)
    mad.inputs[2].default_value = (0.5, 0.5, 0.5)
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(geo.outputs["Normal"], mad.inputs[0])
    nt.links.new(mad.outputs[0], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])

    ob.data.materials.clear()
    ob.data.materials.append(mat)

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.render.bake.margin = 12
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    nt.nodes.active = node
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.bake(type="EMIT", margin=12, use_clear=True)

    W, H = img.size
    buf = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    n = buf[:, :, :3] * 2.0 - 1.0
    np.save(os.path.join(OUT, "NRM.npy"), n.astype(np.float32))
    log("COVERAGE", round(float((buf[:, :, :3].sum(axis=2) > 1e-6).mean()), 4))
    log("DONE")


main()
