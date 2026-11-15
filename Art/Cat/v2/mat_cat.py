"""Wire the baked textures into Principled materials and assign them."""
import os
import sys

import bpy

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

OUT = os.path.join(HERE, "out")


def load_img(path, non_color=False):
    name = os.path.basename(path)
    img = bpy.data.images.get(name)
    if img is None:
        img = bpy.data.images.load(path, check_existing=True)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    else:
        img.colorspace_settings.name = "sRGB"
    return img


def new_mat(name):
    m = bpy.data.materials.get(name)
    if m:
        bpy.data.materials.remove(m)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    # node names are localised in the Chinese UI -> look up by type
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (-260, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m, nt, bsdf


def set_in(bsdf, key, val):
    if key in bsdf.inputs:
        bsdf.inputs[key].default_value = val


def tex(nt, path, non_color=False):
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = load_img(path, non_color)
    n.location = (-760, 0)
    return n


def main():
    alb = os.path.join(OUT, "T_Cat_Fur_Albedo.png")
    rgh = os.path.join(OUT, "T_Cat_Fur_Roughness.png")
    nrm = os.path.join(OUT, "T_Cat_Fur_Normal.png")

    # ---- fur
    m, nt, b = new_mat("MAT_CatFur")
    t = tex(nt, alb)
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    tr = tex(nt, rgh, True)
    tr.location = (-760, -300)
    nt.links.new(tr.outputs["Color"], b.inputs["Roughness"])
    tn = tex(nt, nrm, True)
    tn.location = (-760, -600)
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.location = (-420, -600)
    nm.inputs["Strength"].default_value = 0.40
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    set_in(b, "Specular IOR Level", 0.28)
    try:
        b.inputs["Sheen Weight"].default_value = 0.22
        b.inputs["Sheen Roughness"].default_value = 0.35
    except Exception:
        pass

    fur = bpy.data.objects["CatFur"]
    fur.data.materials.clear()
    fur.data.materials.append(m)

    # ---- eyes
    me, nt, b = new_mat("MAT_CatEye")
    t = tex(nt, os.path.join(OUT, "T_Cat_Eye.png"))
    nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
    set_in(b, "Roughness", 0.12)
    set_in(b, "Specular IOR Level", 0.55)
    for nmn in ("L", "R"):
        ob = bpy.data.objects["Eye_" + nmn]
        ob.data.materials.clear()
        ob.data.materials.append(me)

    # ---- nose / paw leather
    m, nt, b = new_mat("MAT_CatLeather")
    set_in(b, "Base Color", (0.245, 0.105, 0.105, 1.0))
    set_in(b, "Roughness", 0.42)
    set_in(b, "Specular IOR Level", 0.4)
    bpy.data.objects["CatNose"].data.materials.clear()
    bpy.data.objects["CatNose"].data.materials.append(m)

    # ---- collar
    m, nt, b = new_mat("MAT_CatCollar")
    set_in(b, "Base Color", (0.115, 0.028, 0.034, 1.0))
    set_in(b, "Roughness", 0.52)
    bpy.data.objects["CatCollar"].data.materials.clear()
    bpy.data.objects["CatCollar"].data.materials.append(m)

    # ---- bell
    m, nt, b = new_mat("MAT_CatBell")
    set_in(b, "Base Color", (0.760, 0.600, 0.230, 1.0))
    set_in(b, "Metallic", 1.0)
    set_in(b, "Roughness", 0.28)
    for nmo in ("CatBell", "CatBellLoop"):
        bpy.data.objects[nmo].data.materials.clear()
        bpy.data.objects[nmo].data.materials.append(m)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cat_tex.blend"))
    print("MATERIALS")
    for ob in bpy.data.objects:
        if ob.type == "MESH" and not ob.name.startswith("PV_"):
            print("  %-12s %s" % (ob.name, [ms.name if ms else None for ms in ob.data.materials]))


main()
