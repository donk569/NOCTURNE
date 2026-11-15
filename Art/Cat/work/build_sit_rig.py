# 坐姿猫：导入 → 减面 → 定制上半身骨架（无腿骨）→ 蒙皮
import bpy, sys, math, mathutils
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import rig_build, weights

# 清场
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

# 导入 FBX
bpy.ops.import_scene.fbx(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_raw.fbx")
mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
if mesh is None:
    raise RuntimeError("no mesh")
# 合并多网格（如果有）
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
if len(meshes) > 1:
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes: m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    mesh = bpy.context.active_object

# 归一化：身高 0.32，脚底 z=0，居中
bpy.context.view_layer.update()
bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
zs = [v.z for v in bb]
h = max(zs) - min(zs)
scale = 0.32 / h
mesh.scale = (scale, scale, scale)
bpy.ops.object.transform_apply(scale=True)
bpy.context.view_layer.update()
bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
minz = min(v.z for v in bb)
cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
mesh.location.x -= cx
mesh.location.y -= cy
mesh.location.z -= minz
bpy.ops.object.transform_apply(location=True)

# 减面 30k
tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
mod = mesh.modifiers.new("Dec", 'DECIMATE')
mod.decimate_type = 'COLLAPSE'
mod.ratio = min(1.0, 30000.0 / max(1, tris))
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.modifier_apply(modifier=mod.name)
print("TRIS", sum(len(p.vertices) - 2 for p in mesh.data.polygons))

bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
print("BBOX", [round(v, 3) for v in (min(v.x for v in bb), max(v.x for v in bb), min(v.y for v in bb), max(v.y for v in bb), min(v.z for v in bb), max(v.z for v in bb))])

# 定制坐姿骨架（面朝 -Y）
bones = [
    ("root",    (0.0,  0.050, 0.000), (0.0, -0.050, 0.000), None,      False),
    ("hips",    (0.0,  0.095, 0.085), (0.0,  0.040, 0.120), "root",    True),
    ("spine",   (0.0,  0.040, 0.120), (0.0, -0.020, 0.170), "hips",    True),
    ("chest",   (0.0, -0.020, 0.170), (0.0, -0.060, 0.220), "spine",   True),
    ("neck",    (0.0, -0.060, 0.220), (0.0, -0.090, 0.262), "chest",   True),
    ("head",    (0.0, -0.090, 0.262), (0.0, -0.120, 0.305), "neck",    True),
    ("ear.L",   (0.034, -0.088, 0.300), (0.056, -0.072, 0.344), "head", True),
    ("ear.R",  (-0.034, -0.088, 0.300), (-0.056, -0.072, 0.344), "head", True),
    ("tail.01", (0.0,  0.130, 0.058), (0.070, 0.120, 0.038), "hips",    True),
    ("tail.02", (0.070, 0.120, 0.038), (0.078, 0.020, 0.032), "tail.01", True),
    ("tail.03", (0.078, 0.020, 0.032), (0.048, -0.090, 0.030), "tail.02", True),
]
spec = {
    "bones": bones,
    "bone_head": {b[0]: b[1] for b in bones},
    "bone_tail": {b[0]: b[2] for b in bones},
    "key_bones": [b[0] for b in bones],
    "leg_chain": [],
    "arm_chain": [],
    "tail": ["tail.01", "tail.02", "tail.03"],
}
rig = rig_build.build_armature(spec, name="CatSitRig")
rig_build.attach_mesh(mesh, rig)
stats = weights.auto_weights(mesh, rig, spec)
print("WEIGHTS", {k: (round(v, 3) if isinstance(v, float) else v) for k, v in stats.items() if k in ("method", "unweighted", "dead_bones")})

bpy.ops.wm.save_as_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_sit_rig.blend")
print("SIT_RIG_SAVED")
