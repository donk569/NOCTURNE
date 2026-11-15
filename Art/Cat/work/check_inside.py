# 定量穿模检测：每根骨采样若干点，射线法判断是否在网格内部
import bpy, mathutils
from mathutils.bvhtree import BVHTree

MODE = "__MODE__"
BLEND = "__BLEND__"

bpy.ops.wm.open_mainfile(filepath=BLEND)
mesh = rig = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o
    if o.type == 'ARMATURE':
        rig = o

mw = mesh.matrix_world
verts = [mw @ v.co for v in mesh.data.vertices]
polys = [tuple(p.vertices) for p in mesh.data.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)

DIRS = [mathutils.Vector((1, 0, 0)), mathutils.Vector((-1, 0, 0)),
        mathutils.Vector((0, 1, 0)), mathutils.Vector((0, -1, 0)),
        mathutils.Vector((0, 0, 1)), mathutils.Vector((0, 0, -1)),
        mathutils.Vector((1, 1, 1)).normalized(), mathutils.Vector((-1, -1, -1)).normalized()]

def inside(p):
    votes = 0
    for d in DIRS:
        cnt = 0
        o = p + d * 1e-5
        for _ in range(64):
            hit = bvh.ray_cast(o, d, 4.0)
            if hit[0] is None:
                break
            cnt += 1
            o = hit[0] + d * 1e-4
        if cnt % 2 == 1:
            votes += 1
    return votes >= len(DIRS) * 0.6

print("=== 每根骨穿模比例（采样 11 点，含两端） ===")
bad = []
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    n = 11
    out = 0
    outs = []
    for i in range(n):
        p = h.lerp(t, i / (n - 1.0))
        if not inside(p):
            out += 1
            outs.append(i)
    frac = out / float(n)
    flag = "  <<< 穿模" if frac > 0.15 else ""
    print("  %-12s outside=%2d/%2d (%.0f%%)%s" % (b.name, out, n, frac * 100, flag))
    if frac > 0.15:
        bad.append(b.name)
print("BAD_BONES", bad)
print("CHECK_DONE")
