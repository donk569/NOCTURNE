# 按实测解剖坐标重建骨架（站姿 / 坐姿），重新蒙皮并保存
import bpy, sys, mathutils
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import rig_build, weights, ctx

MODE = "__MODE__"   # stand | sit
BLEND = "__BLEND__"
OUT = "__OUT__"

bpy.ops.wm.open_mainfile(filepath=BLEND)
try:
    ctx.ensure_window()
except Exception:
    pass

mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o; break
if mesh is None:
    raise RuntimeError("no mesh")

# --- 1. 拆掉旧骨架 ---
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
for m in list(mesh.modifiers):
    if m.type == 'ARMATURE':
        mesh.modifiers.remove(m)
mesh.vertex_groups.clear()
mesh.parent = None

# --- 2. 新骨架坐标（全部来自网格实测） ---
if MODE == "stand":
    bones = [
        ("root",        (0.000,  0.000, 0.000), (0.000,  0.045, 0.000), None,          False),
        ("hips",        (0.000,  0.115, 0.150), (0.000,  0.050, 0.152), "root",        True),
        ("spine",       (0.000,  0.050, 0.152), (0.000, -0.025, 0.153), "hips",        True),
        ("chest",       (0.000, -0.025, 0.153), (0.000, -0.110, 0.156), "spine",       True),
        ("neck",        (0.000, -0.110, 0.156), (0.000, -0.165, 0.205), "chest",       True),
        ("head",        (0.000, -0.165, 0.205), (0.000, -0.238, 0.222), "neck",        True),
        ("ear.L",       ( 0.026, -0.200, 0.256), ( 0.041, -0.226, 0.281), "head",      True),
        ("ear.R",       (-0.026, -0.200, 0.256), (-0.041, -0.226, 0.281), "head",      True),
        ("shoulder.L",  ( 0.014, -0.080, 0.180), ( 0.026, -0.110, 0.150), "chest",     True),
        ("upper_arm.L", ( 0.026, -0.110, 0.150), ( 0.024, -0.120, 0.100), "shoulder.L", True),
        ("forearm.L",   ( 0.024, -0.120, 0.100), ( 0.022, -0.130, 0.040), "upper_arm.L", True),
        ("hand.L",      ( 0.022, -0.130, 0.040), ( 0.022, -0.146, 0.014), "forearm.L", True),
        ("shoulder.R",  (-0.014, -0.080, 0.180), (-0.026, -0.110, 0.150), "chest",     True),
        ("upper_arm.R", (-0.026, -0.110, 0.150), (-0.024, -0.120, 0.100), "shoulder.R", True),
        ("forearm.R",   (-0.024, -0.120, 0.100), (-0.022, -0.130, 0.040), "upper_arm.R", True),
        ("hand.R",      (-0.022, -0.130, 0.040), (-0.022, -0.146, 0.014), "forearm.R", True),
        ("thigh.L",     ( 0.030,  0.108, 0.150), ( 0.033,  0.082, 0.088), "hips",      True),
        ("shin.L",      ( 0.033,  0.082, 0.088), ( 0.032,  0.090, 0.032), "thigh.L",   True),
        ("foot.L",      ( 0.032,  0.090, 0.032), ( 0.030,  0.066, 0.012), "shin.L",    True),
        ("toe.L",       ( 0.030,  0.066, 0.012), ( 0.030,  0.046, 0.008), "foot.L",    True),
        ("thigh.R",     (-0.030,  0.108, 0.150), (-0.033,  0.082, 0.088), "hips",      True),
        ("shin.R",      (-0.033,  0.082, 0.088), (-0.032,  0.090, 0.032), "thigh.R",   True),
        ("foot.R",      (-0.032,  0.090, 0.032), (-0.030,  0.066, 0.012), "shin.R",    True),
        ("toe.R",       (-0.030,  0.066, 0.012), (-0.030,  0.046, 0.008), "foot.R",    True),
        ("tail.01",     (0.000,  0.150, 0.105), (0.000,  0.185, 0.082), "hips",        True),
        ("tail.02",     (0.000,  0.185, 0.082), (0.000,  0.220, 0.062), "tail.01",     True),
        ("tail.03",     (0.000,  0.220, 0.062), (0.000,  0.252, 0.058), "tail.02",     True),
    ]
    legs = ["thigh.L", "shin.L", "foot.L", "toe.L", "thigh.R", "shin.R", "foot.R", "toe.R"]
    arms = ["upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R"]
    rig_name = "CatRig"
else:
    bones = [
        ("root",        (0.000,  0.000, 0.000), (0.000,  0.040, 0.000), None,          False),
        ("hips",        (0.000,  0.098, 0.058), (0.000,  0.055, 0.100), "root",        True),
        ("spine",       (0.000,  0.055, 0.100), (0.000,  0.005, 0.150), "hips",        True),
        ("chest",       (0.000,  0.005, 0.150), (0.000, -0.050, 0.196), "spine",       True),
        ("neck",        (0.000, -0.050, 0.196), (0.000, -0.092, 0.238), "chest",       True),
        ("head",        (0.000, -0.092, 0.238), (0.000, -0.140, 0.292), "neck",        True),
        ("ear.L",       ( 0.030, -0.118, 0.292), ( 0.043, -0.141, 0.320), "head",      True),
        ("ear.R",       (-0.030, -0.118, 0.292), (-0.043, -0.141, 0.320), "head",      True),
        ("shoulder.L",  ( 0.016, -0.048, 0.172), ( 0.024, -0.066, 0.150), "chest",     True),
        ("upper_arm.L", ( 0.024, -0.066, 0.150), ( 0.022, -0.068, 0.098), "shoulder.L", True),
        ("forearm.L",   ( 0.022, -0.068, 0.098), ( 0.024, -0.062, 0.034), "upper_arm.L", True),
        ("hand.L",      ( 0.024, -0.062, 0.034), ( 0.026, -0.078, 0.012), "forearm.L", True),
        ("shoulder.R",  (-0.016, -0.048, 0.172), (-0.024, -0.066, 0.150), "chest",     True),
        ("upper_arm.R", (-0.024, -0.066, 0.150), (-0.022, -0.068, 0.098), "shoulder.R", True),
        ("forearm.R",   (-0.022, -0.068, 0.098), (-0.024, -0.062, 0.034), "upper_arm.R", True),
        ("hand.R",      (-0.024, -0.062, 0.034), (-0.026, -0.078, 0.012), "forearm.R", True),
        ("thigh.L",     ( 0.040,  0.092, 0.100), ( 0.050,  0.016, 0.062), "hips",      True),
        ("shin.L",      ( 0.050,  0.016, 0.062), ( 0.046,  0.100, 0.042), "thigh.L",   True),
        ("foot.L",      ( 0.046,  0.100, 0.042), ( 0.036,  0.062, 0.012), "shin.L",    True),
        ("thigh.R",     (-0.040,  0.092, 0.100), (-0.050,  0.016, 0.062), "hips",      True),
        ("shin.R",      (-0.050,  0.016, 0.062), (-0.046,  0.100, 0.042), "thigh.R",   True),
        ("foot.R",      (-0.046,  0.100, 0.042), (-0.036,  0.062, 0.012), "shin.R",    True),
        ("tail.01",     (0.000,  0.128, 0.056), (0.016,  0.152, 0.030), "hips",        True),
        ("tail.02",     (0.016,  0.152, 0.030), (0.040,  0.138, 0.022), "tail.01",     True),
        ("tail.03",     (0.040,  0.138, 0.022), (0.050,  0.108, 0.020), "tail.02",     True),
    ]
    legs = ["thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R"]
    arms = ["upper_arm.L", "forearm.L", "hand.L", "upper_arm.R", "forearm.R", "hand.R"]
    rig_name = "CatSitRig"

spec = {
    "bones": bones,
    "bone_head": {b[0]: b[1] for b in bones},
    "bone_tail": {b[0]: b[2] for b in bones},
    "key_bones": [b[0] for b in bones],
    "leg_chain": legs,
    "arm_chain": arms,
    "tail": ["tail.01", "tail.02", "tail.03"],
}
rig = rig_build.build_armature(spec, name=rig_name)
rig_build.attach_mesh(mesh, rig)
stats = weights.auto_weights(mesh, rig, spec)
print("WEIGHTS", {k: (round(v, 3) if isinstance(v, float) else v) for k, v in stats.items() if k in ("method", "unweighted", "dead_bones")})

# 骨骼是否都落在网格内部（最近表面距离检查）
import mathutils
mw = mesh.matrix_world
verts = [mw @ v.co for v in mesh.data.vertices]
dg = bpy.context.evaluated_depsgraph_get()
outside = []
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    mid = (h + t) * 0.5
    d = min((mid - v).length for v in verts)
    if d > 0.035:
        outside.append((b.name, round(d, 3)))
print("BONE_OUTSIDE", outside)

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("RIG_REBUILT", MODE, "bones=", len(rig.data.bones), "->", OUT)
