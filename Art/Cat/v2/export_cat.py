"""Export the rigged + animated cat as a Unity-ready FBX.

Run headless:
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

try:
    HERE = SCRIPTS_DIR
except NameError:
    HERE = os.path.dirname(os.path.abspath(__file__))

OUT = os.path.join(HERE, "out")
FBX = os.path.join(OUT, "Cat_All_v2.fbx")


def log(*a):
    print("[EXPORT]", *a)
    sys.stdout.flush()


def objects():
    rig = bpy.data.objects.get("CatRig")
    assert rig is not None, "CatRig missing"
    skip = ("PV_", "Preview", "PreviewGround")
    meshes = [o for o in bpy.data.objects
              if o.type == "MESH" and not o.name.startswith(skip)]
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
    log("MESHES", [(o.name, len(o.data.vertices)) for o in meshes])
    log("TRIS", tris)
    log("BBOX", [round(v, 4) for v in lo], [round(v, 4) for v in hi])
    log("DIM", [round(hi[i] - lo[i], 4) for i in range(3)])
    log("BONES", len(rig.data.bones))
    missing = [b for b in ("root", "neck", "head") if b not in rig.data.bones]
    log("CONTRACT_BONES_MISSING", missing)
    log("TAIL", [b.name for b in rig.data.bones if b.name.startswith("tail")])
    ad = rig.animation_data
    log("NLA", [(t.name, [s.name for s in t.strips]) for t in ad.nla_tracks] if ad else None)
    log("ACTIVE_ACTION", ad.action.name if ad and ad.action else None)


def select(rig, meshes):
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for ob in meshes:
        ob.hide_set(False)
        ob.select_set(True)
    rig.hide_set(False)
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
    dropped = set(k for k in drop)
    log("EXPORT_KW", sorted(k for k in kw if k != "filepath"))
    if "bake_anim_use_nla_strips" in dropped:
        log("WARNING nla strip export unsupported -> falling back to all actions")
        kw["bake_anim_use_all_actions"] = True
    bpy.ops.export_scene.fbx(**kw)
    log("WROTE", FBX, os.path.getsize(FBX))


def main():
    rig, meshes = objects()
    bpy.context.scene.render.fps = 30
    bpy.context.scene.render.fps_base = 1.0
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 90
    rig.location = (0, 0, 0)
    rig.rotation_euler = (0, 0, 0)
    rig.scale = (1, 1, 1)
    report(rig, meshes)
    select(rig, meshes)
    export()
    log("DONE")


main()
