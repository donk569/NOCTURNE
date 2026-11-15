# 站姿猫：Walk 逐帧贴地补偿 + 导出 FBX（Walk/Jump 两个 Action）
import bpy, os, sys
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import ctx

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend")
try: ctx.ensure_window()
except Exception: pass

scene = bpy.context.scene
rig = mesh = None
for o in bpy.data.objects:
    if o.type == 'ARMATURE': rig = o
    if o.type == 'MESH': mesh = o

if rig.animation_data is None:
    rig.animation_data_create()

def min_z_eval():
    deps = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(deps)
    me = ev.to_mesh()
    mw = ev.matrix_world
    z = min((mw @ v.co).z for v in me.vertices)
    ev.to_mesh_clear()
    return z

# ---- Walk 贴地 ----
act = bpy.data.actions.get("Walk")
rig.animation_data.action = act
N = 32
offsets = {}
rig.location = (0, 0, 0)
for f in range(1, N + 2):
    scene.frame_set(f)
    rig.location.z = 0.0
    bpy.context.view_layer.update()
    offsets[f] = -min_z_eval()

for f in range(1, N + 2):
    scene.frame_set(f)
    rig.location.z = offsets[f]
    rig.keyframe_insert("location", frame=f)
print("WALK_GROUNDED", {k: round(v, 4) for k, v in list(offsets.items())[:5]})

# ---- Jump 落地帧贴地（只补偿落地后） ----
jact = bpy.data.actions.get("Jump")
rig.animation_data.action = jact
for f in (26, 31, 36, 40):
    scene.frame_set(f)
    rig.location.z = 0
    bpy.context.view_layer.update()
    z = min_z_eval()
    rig.location.z = -z
    rig.keyframe_insert("location", frame=f)

# 导出 FBX（两个 Action 都导）
rig.animation_data.action = bpy.data.actions.get("Walk")
scene.frame_set(1)
os.makedirs(r"D:/unity/NOCTURNE/Art/Cat/export", exist_ok=True)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); mesh.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=r"D:/unity/NOCTURNE/Art/Cat/export/Cat_Stand.fbx",
    use_selection=True,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    use_mesh_modifiers=True,
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
)
bpy.ops.wm.save_as_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend")
print("STAND_EXPORTED")
