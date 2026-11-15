# 校验 is_inside 判定可靠性
import bpy, mathutils
from mathutils.bvhtree import BVHTree

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend")
mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o; break
mw = mesh.matrix_world
verts = [mw @ v.co for v in mesh.data.vertices]
polys = [tuple(p.vertices) for p in mesh.data.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)

DIRS = [mathutils.Vector(d).normalized() for d in
        [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1),(1,1,1),(-1,-1,-1),(1,-1,1),(-1,1,-1)]]

def counts(p):
    res = []
    for d in DIRS:
        cnt = 0
        o = p + d * 1e-5
        for _ in range(120):
            hit = bvh.ray_cast(o, d, 4.0)
            if hit[0] is None:
                break
            cnt += 1
            o = hit[0] + d * 1e-4
        res.append(cnt)
    return res

tests = [
    ("身体中心", mathutils.Vector((0, 0.00, 0.15)), True),
    ("身体中心2", mathutils.Vector((0, -0.05, 0.14)), True),
    ("头顶内部", mathutils.Vector((0, -0.20, 0.25)), True),
    ("体外上方", mathutils.Vector((0, 0.00, 0.50)), False),
    ("体外侧面", mathutils.Vector((0.30, 0.00, 0.15)), False),
    ("尾巴中段", mathutils.Vector((0, 0.210, 0.058)), True),
    ("腿内部", mathutils.Vector((0.022, -0.122, 0.06)), True),
]
for label, p, expect in tests:
    c = counts(p)
    odd = sum(1 for x in c if x % 2 == 1)
    print("%-10s expect=%-5s odd=%d/%d counts=%s" % (label, expect, odd, len(c), c))
print("VALIDATE_DONE")
