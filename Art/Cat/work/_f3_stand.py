# 骨架内缩拟合 v2：按"关节"优化（链共享关节，不会互相抵消）
import bpy, sys, mathutils
from mathutils.bvhtree import BVHTree
sys.path.insert(0, r"C:/Users/lenovo/.workbuddy/skills/blender-character-pipeline/scripts")
import rig_build, weights, ctx

MODE = "stand"
BLEND = "D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend"
OUT = "D:/unity/NOCTURNE/Art/Cat/work/cat_stand_fit.blend"

bpy.ops.wm.open_mainfile(filepath=BLEND)
try:
    ctx.ensure_window()
except Exception:
    pass

mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o; break
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
for m in list(mesh.modifiers):
    if m.type == 'ARMATURE':
        mesh.modifiers.remove(m)
mesh.vertex_groups.clear()
mesh.parent = None

if MODE == "stand":
    SPEC = [
        ("root",        (0.000,  0.000, 0.020), (0.000,  0.045, 0.020), None,          False),
        ("hips",        (0.000,  0.110, 0.150), (0.000,  0.050, 0.152), "root",        True),
        ("spine",       (0.000,  0.050, 0.152), (0.000, -0.025, 0.153), "hips",        True),
        ("chest",       (0.000, -0.025, 0.153), (0.000, -0.110, 0.156), "spine",       True),
        ("neck",        (0.000, -0.110, 0.156), (0.000, -0.168, 0.200), "chest",       True),
        ("head",        (0.000, -0.168, 0.200), (0.000, -0.232, 0.212), "neck",        True),
        ("ear.L",       ( 0.026, -0.205, 0.252), ( 0.038, -0.225, 0.270), "head",      True),
        ("ear.R",       (-0.026, -0.205, 0.252), (-0.038, -0.225, 0.270), "head",      True),
        ("shoulder.L",  ( 0.014, -0.080, 0.178), ( 0.024, -0.108, 0.150), "chest",     True),
        ("upper_arm.L", ( 0.024, -0.108, 0.150), ( 0.023, -0.116, 0.100), "shoulder.L", True),
        ("forearm.L",   ( 0.023, -0.116, 0.100), ( 0.022, -0.122, 0.042), "upper_arm.L", True),
        ("hand.L",      ( 0.022, -0.122, 0.042), ( 0.022, -0.136, 0.018), "forearm.L", True),
        ("shoulder.R",  (-0.014, -0.080, 0.178), (-0.024, -0.108, 0.150), "chest",     True),
        ("upper_arm.R", (-0.024, -0.108, 0.150), (-0.023, -0.116, 0.100), "shoulder.R", True),
        ("forearm.R",   (-0.023, -0.116, 0.100), (-0.022, -0.122, 0.042), "upper_arm.R", True),
        ("hand.R",      (-0.022, -0.122, 0.042), (-0.022, -0.136, 0.018), "forearm.R", True),
        ("thigh.L",     ( 0.032,  0.100, 0.148), ( 0.033,  0.074, 0.086), "hips",      True),
        ("shin.L",      ( 0.033,  0.074, 0.086), ( 0.032,  0.086, 0.032), "thigh.L",   True),
        ("foot.L",      ( 0.032,  0.086, 0.032), ( 0.031,  0.070, 0.014), "shin.L",    True),
        ("toe.L",       ( 0.031,  0.070, 0.014), ( 0.030,  0.056, 0.012), "foot.L",    True),
        ("thigh.R",     (-0.032,  0.100, 0.148), (-0.033,  0.074, 0.086), "hips",      True),
        ("shin.R",      (-0.033,  0.074, 0.086), (-0.032,  0.086, 0.032), "thigh.R",   True),
        ("foot.R",      (-0.032,  0.086, 0.032), (-0.031,  0.070, 0.014), "shin.R",    True),
        ("toe.R",       (-0.031,  0.070, 0.014), (-0.030,  0.056, 0.012), "foot.R",    True),
        ("tail.01",     (0.000,  0.150, 0.098), (0.000,  0.184, 0.076), "hips",        True),
        ("tail.02",     (0.000,  0.184, 0.076), (0.000,  0.218, 0.062), "tail.01",     True),
        ("tail.03",     (0.000,  0.218, 0.062), (0.000,  0.248, 0.062), "tail.02",     True),
    ]
    RIGNAME = "CatRig"
    GROUND = ["hand.L", "hand.R", "foot.L", "foot.R", "toe.L", "toe.R"]
else:
    SPEC = [
        ("root",        (0.000,  0.000, 0.020), (0.000,  0.040, 0.020), None,          False),
        ("hips",        (0.000,  0.096, 0.062), (0.000,  0.058, 0.098), "root",        True),
        ("spine",       (0.000,  0.058, 0.098), (0.000,  0.008, 0.146), "hips",        True),
        ("chest",       (0.000,  0.008, 0.146), (0.000, -0.046, 0.190), "spine",       True),
        ("neck",        (0.000, -0.046, 0.190), (0.000, -0.090, 0.232), "chest",       True),
        ("head",        (0.000, -0.090, 0.232), (0.000, -0.134, 0.284), "neck",        True),
        ("ear.L",       ( 0.028, -0.116, 0.288), ( 0.040, -0.136, 0.308), "head",      True),
        ("ear.R",       (-0.028, -0.116, 0.288), (-0.040, -0.136, 0.308), "head",      True),
        ("shoulder.L",  ( 0.016, -0.048, 0.170), ( 0.022, -0.062, 0.148), "chest",     True),
        ("upper_arm.L", ( 0.022, -0.062, 0.148), ( 0.021, -0.066, 0.098), "shoulder.L", True),
        ("forearm.L",   ( 0.021, -0.066, 0.098), ( 0.023, -0.062, 0.036), "upper_arm.L", True),
        ("hand.L",      ( 0.023, -0.062, 0.036), ( 0.026, -0.072, 0.016), "forearm.L", True),
        ("shoulder.R",  (-0.016, -0.048, 0.170), (-0.022, -0.062, 0.148), "chest",     True),
        ("upper_arm.R", (-0.022, -0.062, 0.148), (-0.021, -0.066, 0.098), "shoulder.R", True),
        ("forearm.R",   (-0.021, -0.066, 0.098), (-0.023, -0.062, 0.036), "upper_arm.R", True),
        ("hand.R",      (-0.023, -0.062, 0.036), (-0.026, -0.072, 0.016), "forearm.R", True),
        ("thigh.L",     ( 0.040,  0.088, 0.100), ( 0.048,  0.026, 0.064), "hips",      True),
        ("shin.L",      ( 0.048,  0.026, 0.064), ( 0.044,  0.092, 0.046), "thigh.L",   True),
        ("foot.L",      ( 0.044,  0.092, 0.046), ( 0.036,  0.064, 0.016), "shin.L",    True),
        ("thigh.R",     (-0.040,  0.088, 0.100), (-0.048,  0.026, 0.064), "hips",      True),
        ("shin.R",      (-0.048,  0.026, 0.064), (-0.044,  0.092, 0.046), "thigh.R",   True),
        ("foot.R",      (-0.044,  0.092, 0.046), (-0.036,  0.064, 0.016), "shin.R",    True),
        ("tail.01",     (0.000,  0.122, 0.056), (0.018,  0.146, 0.034), "hips",        True),
        ("tail.02",     (0.018,  0.146, 0.034), (0.040,  0.136, 0.026), "tail.01",     True),
        ("tail.03",     (0.040,  0.136, 0.026), (0.050,  0.110, 0.024), "tail.02",     True),
    ]
    RIGNAME = "CatSitRig"
    GROUND = ["hand.L", "hand.R", "foot.L", "foot.R"]

mw = mesh.matrix_world
verts = [mw @ v.co for v in mesh.data.vertices]
polys = [tuple(p.vertices) for p in mesh.data.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)

DIRS = [mathutils.Vector(d).normalized() for d in
        [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1),(1,1,1),(-1,-1,-1),(1,-1,1),(-1,1,-1)]]

def is_inside(p):
    odd = 0
    for d in DIRS:
        cnt = 0
        o = p + d * 1e-5
        for _ in range(120):
            hit = bvh.ray_cast(o, d, 4.0)
            if hit[0] is None:
                break
            cnt += 1
            o = hit[0] + d * 1e-4
        if cnt % 2 == 1:
            odd += 1
    return odd >= 6

# ---- 关节表 ----
PARENT = {n: (p, c) for n, h, t, p, d in [(s[0], s[1], s[2], s[3], s[4]) for s in SPEC] for c in [False]}
JOINT = {}     # joint_name -> Vector
BONE_J = {}    # bone -> (head_joint, tail_joint)
for name, h, t, parent, deform in SPEC:
    hv = mathutils.Vector(h); tv = mathutils.Vector(t)
    pconn = parent is not None and (JOINT.get(parent + "_t") is not None) and (JOINT[parent + "_t"] - hv).length < 1e-6
    hj = (parent + "_t") if pconn else (name + "_h")
    if hv.length == 0 and False:
        pass
    if hj not in JOINT:
        JOINT[hj] = hv.copy()
    tj = name + "_t"
    JOINT[tj] = tv.copy()
    BONE_J[name] = (hj, tj)

FREEZE = {"root_h", "root_t"}
MARGIN = 0.0085
GROUND_MARGIN = 0.0
SAMPLE = [i / 14.0 for i in range(0, 15)]

def needs_fix(p, margin):
    nearest = bvh.find_nearest(p, 4.0)
    if nearest[0] is None:
        return None
    loc, nrm, idx, dist = nearest
    if not is_inside(p):
        return loc - nrm * max(margin, 0.004)
    if dist < margin:
        return loc - nrm * margin
    return None

for it in range(30):
    acc = {k: mathutils.Vector((0, 0, 0)) for k in JOINT}
    wt = {k: 0.0 for k in JOINT}
    for name, (hj, tj) in BONE_J.items():
        margin = GROUND_MARGIN if name in GROUND else MARGIN
        h = JOINT[hj]; t = JOINT[tj]
        for s in SAMPLE:
            p = h.lerp(t, s)
            tgt = needs_fix(p, margin)
            if tgt is None:
                continue
            d = tgt - p
            if hj not in FREEZE:
                acc[hj] += d * (1.0 - s); wt[hj] += (1.0 - s)
            if tj not in FREEZE:
                acc[tj] += d * s; wt[tj] += s
    damp = max(0.35, 0.85 - it * 0.02)
    moved = 0.0
    for k in JOINT:
        if k in FREEZE or wt[k] < 1e-6:
            continue
        d = (acc[k] / wt[k]) * damp
        if d.length > 0.006:
            d = d.normalized() * 0.006
        JOINT[k] += d
        moved = max(moved, d.length)
    # 左右镜像
    for k in list(JOINT.keys()):
        if "_L_" in k:
            kr = k.replace("_L_", "_R_")
            if kr in JOINT:
                a, b = JOINT[k], JOINT[kr]
                ax = (abs(a.x) + abs(b.x)) * 0.5
                y = (a.y + b.y) * 0.5
                z = (a.z + b.z) * 0.5
                JOINT[k] = mathutils.Vector((ax, y, z))
                JOINT[kr] = mathutils.Vector((-ax, y, z))
    if it > 8 and moved < 0.0003:
        print("CONVERGED iter", it, "moved=%.5f" % moved)
        break
else:
    print("MAXITER moved=%.5f" % moved)

print("=== 拟合结果检查 ===")
bad = []
for name, (hj, tj) in BONE_J.items():
    h = JOINT[hj]; t = JOINT[tj]
    out = sum(1 for s in SAMPLE if not is_inside(h.lerp(t, s)))
    if out:
        print("  %-12s outside=%d/%d" % (name, out, len(SAMPLE)))
    if out > len(SAMPLE) * 0.2 and name != "root":
        bad.append(name)
print("STILL_BAD", bad)

new_bones = []
for name, h, t, parent, deform in SPEC:
    hj, tj = BONE_J[name]
    new_bones.append((name, tuple(JOINT[hj]), tuple(JOINT[tj]), parent, deform))
spec = {
    "bones": new_bones,
    "bone_head": {b[0]: b[1] for b in new_bones},
    "bone_tail": {b[0]: b[2] for b in new_bones},
    "key_bones": [b[0] for b in new_bones],
    "leg_chain": [b[0] for b in new_bones if any(k in b[0] for k in ("thigh", "shin", "foot", "toe"))],
    "arm_chain": [b[0] for b in new_bones if any(k in b[0] for k in ("upper_arm", "forearm", "hand"))],
    "tail": ["tail.01", "tail.02", "tail.03"],
}
rig = rig_build.build_armature(spec, name=RIGNAME)
rig_build.attach_mesh(mesh, rig)
stats = weights.auto_weights(mesh, rig, spec)
print("WEIGHTS", {k: (round(v, 3) if isinstance(v, float) else v) for k, v in stats.items() if k in ("method", "unweighted", "dead_bones")})
print("FINAL_BONES")
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    print("  %-12s (%.4f,%.4f,%.4f)->(%.4f,%.4f,%.4f)" % (b.name, h.x, h.y, h.z, t.x, t.y, t.z))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("FIT_SAVED", OUT)
