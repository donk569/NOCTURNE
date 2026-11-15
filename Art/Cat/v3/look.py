"""v3 -- build the tabby material on the decimated base and render QA views.

    blender --background --python look.py
"""
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
NRM_SRC = os.path.join(HERE, "out", "raw_tex", "texture_pbr_20250901_normal.png")
sys.path.insert(0, os.path.join(HERE, "..", "v2"))
import preview  # noqa: E402

TAG = os.environ.get("CAT_TAG", "c1")


def log(*a):
    print("[LOOK]", *a)
    sys.stdout.flush()


def img(path, srgb):
    name = os.path.basename(path)
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    im = bpy.data.images.load(path, check_existing=False)
    im.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    return im


def build_material(ob):
    mat = bpy.data.materials.get("MAT_Tabby") or bpy.data.materials.new("MAT_Tabby")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    alb = nt.nodes.new("ShaderNodeTexImage")
    if os.environ.get("CAT_RAW"):
        alb.image = img(os.path.join(OUT, "raw_tex", "texture_pbr_20250901.png"), True)
    else:
        alb.image = img(os.path.join(OUT, "T_Cat_Coat_Albedo.png"), True)
    nt.links.new(alb.outputs["Color"], bsdf.inputs["Base Color"])

    rgh = nt.nodes.new("ShaderNodeTexImage")
    rgh.image = img(os.path.join(OUT, "T_Cat_Coat_Rough.png"), False)
    nt.links.new(rgh.outputs["Color"], bsdf.inputs["Roughness"])

    if os.path.exists(NRM_SRC):
        nt_n = nt.nodes.new("ShaderNodeTexImage")
        nt_n.image = img(NRM_SRC, False)
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 0.62
        nt.links.new(nt_n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

    bsdf.inputs["Metallic"].default_value = 0.0
    try:
        bsdf.inputs["Specular IOR Level"].default_value = 0.42
    except Exception:
        pass
    try:
        bsdf.inputs["Sheen Weight"].default_value = 0.18
        bsdf.inputs["Sheen Roughness"].default_value = 0.5
    except Exception:
        pass

    ob.data.materials.clear()
    ob.data.materials.append(mat)
    log("MAT", mat.name, [n.image.name for n in nt.nodes if n.type == "TEX_IMAGE"])
    return mat


def main():
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "base_dec.blend"))
    ob = bpy.data.objects["CatBody"]
    mat = build_material(ob)
    if os.environ.get("CAT_EMIT"):
        # flat albedo view: isolates the texture from shading so pattern errors
        # cannot hide behind highlights
        nt = mat.node_tree
        out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
        em = nt.nodes.new("ShaderNodeEmission")
        alb = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
        nt.links.new(alb.outputs["Color"], em.inputs["Color"])
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    preview.setup_render(res=760, samples=24)
    preview.add_lights(power=1.0)
    preview.add_ground()
    preview.four_views(OUT, TAG, target=Vector((0, 0, 0.15)), ortho=0.62)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_look.blend"))
    log("DONE")


main()
