"""Reusable preview renderer for the cat work (EEVEE, 3-point light, multi view)."""
import math
import os

import bpy
from mathutils import Vector


def _clear_helpers():
    for ob in list(bpy.data.objects):
        if ob.name.startswith("PV_"):
            bpy.data.objects.remove(ob, do_unlink=True)


def setup_render(res=760, samples=24, engine="BLENDER_EEVEE"):
    sc = bpy.context.scene
    try:
        sc.render.engine = engine
    except TypeError:
        sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    try:
        sc.eevee.taa_render_samples = samples
    except Exception:
        pass
    avail = [e.identifier for e in sc.view_settings.bl_rna.properties["view_transform"].enum_items]
    for want in ("AgX", "Filmic", "Standard"):
        if want in avail:
            sc.view_settings.view_transform = want
            break
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.0

    world = sc.world or bpy.data.worlds.new("W")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg is None:
        for n in world.node_tree.nodes:
            if n.type == "BACKGROUND":
                bg = n
    if bg:
        bg.inputs[0].default_value = (0.045, 0.05, 0.062, 1.0)
        bg.inputs[1].default_value = 0.55


def add_lights(target=Vector((0, 0, 0.16)), power=1.0):
    specs = [
        ("PV_Key", (1.5, -1.7, 1.5), 62.0, 1.6),
        ("PV_Fill", (-1.7, -1.1, 0.9), 22.0, 2.0),
        ("PV_Rim", (0.2, 1.9, 1.4), 40.0, 1.4),
        ("PV_Top", (0.0, 0.0, 2.0), 16.0, 1.2),
    ]
    for name, loc, pw, size in specs:
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = pw * power
        ld.size = size
        ob = bpy.data.objects.new(name, ld)
        bpy.context.scene.collection.objects.link(ob)
        ob.location = loc
        d = (target - Vector(loc))
        ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def add_ground(z=0.0, size=6.0):
    name = "PV_Ground"
    if name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    me = bpy.data.meshes.new(name)
    s = size
    me.from_pydata([(-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z)], [], [(0, 1, 2, 3)])
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    mat = bpy.data.materials.get("PV_GroundMat") or bpy.data.materials.new("PV_GroundMat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.16, 0.15, 0.15, 1)
        bsdf.inputs["Roughness"].default_value = 0.9
    ob.data.materials.append(mat)
    return ob


def clay_material(color=(0.46, 0.44, 0.42, 1.0), rough=0.62):
    mat = bpy.data.materials.get("PV_Clay")
    if mat is None:
        mat = bpy.data.materials.new("PV_Clay")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Roughness"].default_value = rough
        try:
            bsdf.inputs["Specular IOR Level"].default_value = 0.3
        except Exception:
            pass
    return mat


def apply_clay():
    mat = clay_material()
    for ob in bpy.data.objects:
        if ob.type != "MESH" or ob.name.startswith("PV_"):
            continue
        ob.data.materials.clear()
        ob.data.materials.append(mat)


def render_view(name, loc, look_at, path, ortho=None, focal=52.0, res=None):
    cam_name = "PV_Cam"
    cam = bpy.data.objects.get(cam_name)
    if cam is None:
        cd = bpy.data.cameras.new(cam_name)
        cam = bpy.data.objects.new(cam_name, cd)
        bpy.context.scene.collection.objects.link(cam)
    cam.data.lens = focal
    cam.location = loc
    d = Vector(look_at) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    if ortho:
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = ortho
    else:
        cam.data.type = "PERSP"
    bpy.context.scene.camera = cam
    if res:
        bpy.context.scene.render.resolution_x = res
        bpy.context.scene.render.resolution_y = res
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def four_views(outdir, tag, target=Vector((0.0, 0.06, 0.15)), ortho=0.72):
    os.makedirs(outdir, exist_ok=True)
    paths = []
    d = 1.6
    views = [
        ("front", (target.x, target.y - d, target.z + 0.02), ortho),
        ("side", (target.x + d, target.y, target.z + 0.02), ortho),
        ("back", (target.x, target.y + d, target.z + 0.02), ortho),
        ("q34", (target.x + d * 0.80, target.y - d * 0.72, target.z + d * 0.30), None),
    ]
    for nm, loc, ort in views:
        p = os.path.join(outdir, "%s_%s.png" % (tag, nm))
        render_view(nm, loc, target, p, ortho=ort)
        paths.append(p)
    # face closeup
    p = os.path.join(outdir, "%s_face.png" % tag)
    render_view("face", (0.16, -0.52, 0.30), (0.0, -0.20, 0.222), p, focal=70.0)
    paths.append(p)
    return paths
