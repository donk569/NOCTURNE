"""v3 -- export the rigged + animated tabby as a Unity-ready FBX.

    blender --background out/cat_anim.blend --python export_cat.py

Conventions that must survive the trip to Unity:
  * bone names root / neck / head are contractual (CatBones)
  * one FBX "take" per NLA track, named exactly Cat_Idle / Cat_Walk / ...
  * real-world metres, paws at z=0, cat faces -Y in Blender
"""
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
TEXDST = os.path.join(OUT, "fbx_tex")
FBX = os.path.join(OUT, "Cat_All_v3.fbx")

SKIP = ("PV_", "Preview", "PreviewGround")


def log(*a):
    print("[EXPORT]", *a)
    sys.stdout.flush()


def objects():
    rig = bpy.data.objects.get("CatRig")
    assert rig is not None, "CatRig missing"
    meshes = [o for o in bpy.data.objects
              if o.type == "MESH" and not o.name.startswith(SKIP)]
    assert meshes, "no mesh"
    return rig, meshes


def report(rig, meshes):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    tris = 0
    for ob in meshes:
        me = ob.data
        tris += sum(max(0, len(p.vertices) - 2) for p in me.polygons)
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    log("MESHES", [(o.name, len(o.data.vertices), len(o.data.materials)) for o in meshes])
    log("TRIS", tris)
    log("BBOX", [round(v, 4) for v in lo], [round(v, 4) for v in hi])
    log("DIM", [round(hi[i] - lo[i], 4) for i in range(3)])
    log("BONES", len(rig.data.bones))
    missing = [b for b in ("root", "neck", "head") if b not in rig.data.bones]
    log("CONTRACT_BONES_MISSING", missing)
    log("TAIL", [b.name for b in rig.data.bones if b.name.startswith("tail")])
    ad = rig.animation_data
    log("NLA", [(t.name, [s.name for s in t.strips], int(t.strips[0].frame_start),
                 int(t.strips[0].frame_end)) for t in ad.nla_tracks] if ad else None)
    log("ACTIVE_ACTION", ad.action.name if ad and ad.action else None)
    # every vertex must be weighted, otherwise Unity deforms it to origin
    for ob in meshes:
        bad = [v.index for v in ob.data.vertices if not v.groups or
               sum(g.weight for g in v.groups) < 1e-4]
        log("UNWEIGHTED", ob.name, len(bad))
    for m in bpy.data.materials:
        if m.users:
            log("MAT", m.name, m.use_nodes)
    for im in bpy.data.images:
        if im.users and im.filepath:
            log("IMG", im.name, im.filepath, im.size[:])


def select(rig, meshes):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for ob in meshes:
        ob.hide_set(False)
        ob.hide_viewport = False
        ob.select_set(True)
    rig.hide_set(False)
    rig.hide_viewport = False
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig


def export(**over):
    kw = dict(
        filepath=FBX,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=False,
        use_custom_props=False,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="COPY",
        embed_textures=False,
        batch_mode="OFF",
    )
    kw.update(over)
    props = bpy.ops.export_scene.fbx.get_rna_type().properties.keys()
    drop = [k for k in kw if k not in props and k != "filepath"]
    for k in drop:
        log("DROP_UNSUPPORTED", k)
        kw.pop(k)
    dropped = set(drop)
    log("EXPORT_KW", sorted(k for k in kw if k != "filepath"))
    if "bake_anim_use_nla_strips" in dropped:
        log("WARNING nla strip export unsupported -> falling back to all actions")
        kw["bake_anim_use_all_actions"] = True
    if "path_mode" in kw:
        kw["path_mode"] = "COPY"
    bpy.ops.export_scene.fbx(**kw)
    log("WROTE", FBX, os.path.getsize(FBX))


def main():
    rig, meshes = objects()
    sc = bpy.context.scene
    sc.render.fps = 30
    sc.render.fps_base = 1.0
    sc.frame_start = 1
    sc.frame_end = 90
    sc.frame_set(1)
    rig.location = (0, 0, 0)
    rig.rotation_euler = (0, 0, 0)
    rig.scale = (1, 1, 1)
    if rig.animation_data:
        rig.animation_data.action = None
        for t in rig.animation_data.nla_tracks:
            t.mute = False
    report(rig, meshes)
    select(rig, meshes)
    export()
    written = sorted(f for f in os.listdir(OUT) if f.lower().endswith(".png"))
    log("PNG_IN_OUT", written)
    log("DONE")


main()
