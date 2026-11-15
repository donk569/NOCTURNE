# 骨架自动内缩拟合：确保每根骨都落在网格内部 + 重新蒙皮
import bpy, sys, mathutils, json
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

# 拆旧骨架
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
for m in list(mesh.modifiers):
    if m.type == 'ARMATURE':
        mesh.modifiers.remove(m)
mesh.vertex_groups.clear()
mesh.parent = None

# ---------- 初始骨架坐标（来自实测，待自动校正） ----------
if MODE == "stand":
    SPEC = [
        ("root",        (0.000,  0.000, 0.030), (0.000,  0.045, 0.030), None,          False),
        ("hips",        (0.000,  0.112, 0.150), (0.000,  0.050, 0.152), "root",        True),
        ("spine",       (0.000,  0.050, 0.152), (0.000, -0.025, 0.153), "hips",        True),
        ("chest",       (0.000, -0.025, 0.153), (0.000, -0.110, 0.156), "spine",       True),
        ("neck",        (0.000, -0.110, 0.156), (0.000, -0.168, 0.200), "chest",       True),
        ("head",        (0.000, -0.168, 0.200), (0.000, -0.232, 0.212), "neck",        True),
        ("ear.L",       ( 0.026, -0.205, 0.252), ( 0.038, -0.225, 0.272), "head",      True),
        ("ear.R",       (-0.026, -0.205, 0.252), (-0.038, -0.225, 0.272), "head",      True),
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
else:
    SPEC = [
        ("root",        (0.000,  0.000, 0.030), (0.000,  0.040, 0.030), None,          False),
        ("hips",        (0.000,  0.096, 0.062), (0.000,  0.058, 0.098), "root",        True),
        ("spine",       (0.000,  0.058, 0.098), (0.000,  0.008, 0.146), "hips",        True),
        ("chest",       (0.000,  0.008, 0.146), (0.000, -0.046, 0.190), "spine",       True),
        ("neck",        (0.000, -0.046, 0.190), (0.000, -0.090, 0.232), "chest",       True),
        ("head",        (0.000, -0.090, 0.232), (0.000, -0.134, 0.284), "neck",        True),
        ("ear.L",       ( 0.028, -0.116, 0.288), ( 0.040, -0.136, 0.310), "head",      True),
        ("ear.R",       (-0.028, -0.116, 0.288), (-0.040, -0.136, 0.310), "head",      True),
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

# ---------- BVH ----------
mw = mesh.matrix_world
verts = [mw @ v.co for v in mesh.data.vertices]
polys = [tuple(p.vertices) for p in mesh.data.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)

DIRS = [mathutils.Vector(d).normalized() for d in
        [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1),(1,1,1),(-1,-1,-1),(1,-1,1),(-1,1,-1)]]

def is_inside(p):
    votes = 0
    for d in DIRS:
        cnt = 0
        o = p + d * 1e-5
        for _ in range(80):
            hit = bvh.ray_cast(o, d, 4.0)
            if hit[0] is None:
                break
            cnt += 1
            o = hit[0] + d * 1e-4
        if cnt % 2 == 1:
            votes += 1
    return votes >= len(DIRS) * 0.6

MARGIN = 0.007          # 目标：距表面至少 7mm
SAMPLE = [i / 8.0 for i in range(1, 8)]   # 0.125 .. 0.875

# 关节表：bone -> [head_vec, tail_vec]
J = {}
CONNECT = {}
for name, h, t, parent, deform in SPEC:
    J[name] = [mathutils.Vector(h), mathutils.Vector(t)]
    CONNECT[name] = (parent, parent is not None and
                     (mathutils.Vector(J[parent][1]) - mathutils.Vector(h)).length < 1e-6)

FREEZE = set(["root"])

def fix_point(p):
    """若 p 在体外或离表面太近，返回到体内的目标点；否则 None"""
    nearest = bvh.find_nearest(p, 4.0)
    if nearest[0] is None:
        return None
    loc, nrm, idx, dist = nearest
    if not is_inside(p):
        return loc - nrm * MARGIN
    if dist < MARGIN:
        return loc - nrm * MARGIN
    return None

for it in range(14):
    delta = {k: mathutils.Vector((0, 0, 0)) for k in J}
    wt = {k: 0.0 for k in J}
    for name in J:
        if name in FREEZE:
            continue
        h, t = J[name]
        for s in SAMPLE:
            p = h.lerp(t, s)
            tgt = fix_point(p)
            if tgt is None:
                continue
            d = tgt - p
            delta[name] += d
            wt[name] += 1.0
    moved = 0.0
    for name in J:
        if wt[name] == 0:
            continue
        d = delta[name] / wt[name]
        # 阻尼 + 限幅
        d *= 0.55
        if d.length > 0.02:
            d = d.normalized() * 0.02
        J[name][0] += d
        J[name][1] += d
        moved = max(moved, d.length)
    # 链式连接：子骨 head = 父骨 tail
    for name, h, t, parent, deform in SPEC:
        if parent and CONNECT[name]:
            J[name][0] = J[parent][1].copy()
    # 左右对称
    for name in list(J.keys()):
        if name.endswith(".L"):
            r = name[:-2] + ".R"
            if r in J:
                for k in (0, 1):
                    xl, xr = J[name][k].x, J[r][k].x
                    ax = (abs(xl) + abs(xr)) * 0.5
                    y = (J[name][k].y + J[r][k].y) * 0.5
                    z = (J[name][k].z + J[r][k].z) * 0.5
                    J[name][k] = mathutils.Vector((ax, y, z))
                    J[r][k] = mathutils.Vector((-ax, y, z))
    if moved < 0.0004:
        print("CONVERGED at iter", it)
        break

# 报告
print("=== 拟合后穿模检查 ===")
bad = []
for name in J:
    if name in FREEZE:
        continue
    h, t = J[name]
    out = 0
    for s in SAMPLE:
        if not is_inside(h.lerp(t, s)):
            out += 1
    if out > 0:
        print("  %-12s outside=%d/%d" % (name, out, len(SAMPLE)))
        if out > len(SAMPLE) * 0.2:
            bad.append(name)
print("STILL_BAD", bad)

# ---------- 重建骨架 ----------
new_bones = []
for name, h, t, parent, deform in SPEC:
    new_bones.append((name, tuple(J[name][0]), tuple(J[name][1]), parent, deform))
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
    print("  %-12s (%.4f,%.4f,%.4f) -> (%.4f,%.4f,%.4f)" % (b.name, h.x, h.y, h.z, t.x, t.y, t.z))

bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("FIT_SAVED", OUT)
