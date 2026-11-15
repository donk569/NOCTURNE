# 无界面导出：读取当前 .blend，导出选中骨架+网格 FBX（含全部 Action）
import bpy, sys, os

argv = sys.argv
out_path = None
for i, a in enumerate(argv):
    if a == "--out":
        out_path = argv[i + 1]

rig = mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        rig = o
    elif o.type == 'MESH' and o.name != "GroundRef":
        mesh = o

print("EXPORT_OBJECTS", rig.name if rig else None, mesh.name if mesh else None)
print("ACTIONS", [a.name for a in bpy.data.actions])

# 把每个 Action 放进独立 NLA 轨道（Unity 按轨道导入为独立 clip）
if rig.animation_data is None:
    rig.animation_data_create()
ad = rig.animation_data
ad.action = None
for t in list(ad.nla_tracks):
    ad.nla_tracks.remove(t)
for act in bpy.data.actions:
    if act.name in ("Idle_Stare", "Alert", "Sleep", "Walk", "Jump"):
        tr = ad.nla_tracks.new()
        tr.name = act.name
        st = tr.strips.new(act.name, int(act.frame_start), act)
        st.name = act.name
        print("NLA", act.name, int(act.frame_start), int(act.frame_end))

# 取消所有选择，只选骨架+网格
for o in bpy.data.objects:
    o.select_set(False)
rig.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = rig

os.makedirs(os.path.dirname(out_path), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=out_path,
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
print("FBX_EXPORT_OK", out_path, os.path.getsize(out_path))
