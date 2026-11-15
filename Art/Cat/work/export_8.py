# -*- coding: utf-8 -*-
"""导出含 8 段动画的 FBX（直接使用 build_8.py 建好的 NLA 轨道）"""
import bpy, os, sys

BLEND = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend"
OUT = r"D:/unity/NOCTURNE/Art/Cat/export/Cat_All.fbx"

bpy.ops.wm.open_mainfile(filepath=BLEND)
rig = None
mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        rig = o
    if o.type == 'MESH':
        mesh = o
if rig is None or mesh is None:
    raise RuntimeError("missing rig/mesh")

ad = rig.animation_data
ad.action = None
print("NLA_TRACKS", [(t.name, len(t.strips)) for t in ad.nla_tracks])
print("ACTIONS", sorted(a.name for a in bpy.data.actions))

for o in bpy.data.objects:
    o.select_set(False)
rig.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = rig

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=OUT,
    use_selection=True,
    bake_anim=True,
    bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=True,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    use_mesh_modifiers=True,
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
)
print("FBX_EXPORT_OK", OUT, os.path.getsize(OUT))
