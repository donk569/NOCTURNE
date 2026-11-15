# 诊断：坐姿猫骨骼位置 vs 网格真实轮廓
import bpy, mathutils, sys

path = r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend"
bpy.ops.wm.open_mainfile(filepath=path)

mesh = None
rig = None
for o in bpy.data.objects:
    if o.type == 'MESH' and mesh is None:
        mesh = o
    if o.type == 'ARMATURE':
        rig = o
print("MESH", mesh.name if mesh else None, "RIG", rig.name if rig else None)

# 世界坐标顶点
mw = mesh.matrix_world
vs = [mw @ v.co for v in mesh.data.vertices]
xs = [v.x for v in vs]; ys = [v.y for v in vs]; zs = [v.z for v in vs]
print("WORLD_BBOX x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

print("=== BONES (world head/tail) ===")
for b in rig.data.bones:
    h = rig.matrix_world @ b.head_local
    t = rig.matrix_world @ b.tail_local
    print("  %-10s head=(%.3f,%.3f,%.3f) tail=(%.3f,%.3f,%.3f) parent=%s" % (
        b.name, h.x, h.y, h.z, t.x, t.y, t.z, b.parent.name if b.parent else "-"))

# 逐层横截面：看每个高度的 X 分布（腿/身体）与 Y 分布
print("=== Z SLICES (x-range and y-range of verts near each z) ===")
z0, z1 = min(zs), max(zs)
for i in range(18):
    lo = z0 + (z1 - z0) * i / 18.0
    hi = z0 + (z1 - z0) * (i + 1) / 18.0
    sel = [v for v in vs if lo <= v.z < hi]
    if not sel:
        print("  z[%.3f,%.3f] empty" % (lo, hi)); continue
    sx = [v.x for v in sel]; sy = [v.y for v in sel]
    print("  z[%.3f,%.3f] n=%4d x[%.3f,%.3f] w=%.3f y[%.3f,%.3f] d=%.3f" % (
        lo, hi, len(sel), min(sx), max(sx), max(sx)-min(sx), min(sy), max(sy), max(sy)-min(sy)))

# 前爪/后爪定位：低处最前和最后的团块
print("=== PAW CLUSTERS (z<0.06) ===")
low = [v for v in vs if v.z < 0.06]
if low:
    # 按 y 排序，前 25% 视为前爪区，后 25% 后爪区
    lys = sorted(v.y for v in low)
    fy = lys[len(lys)//4]
    by = lys[len(lys)*3//4]
    front = [v for v in low if v.y <= fy]
    back = [v for v in low if v.y >= by]
    for nm, cl in (("front", front), ("back", back)):
        if cl:
            cx = sum(v.x for v in cl)/len(cl); cy = sum(v.y for v in cl)/len(cl)
            print("  %s n=%d cx=%.3f cy=%.3f x[%.3f,%.3f]" % (nm, len(cl), cx, cy, min(v.x for v in cl), max(v.x for v in cl)))
print("DIAG_DONE")
