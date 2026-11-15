# 精确测量站姿猫解剖关键点
import bpy, mathutils, sys

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_anim.blend")
mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o; break
mw = mesh.matrix_world
vs = [mw @ v.co for v in mesh.data.vertices]

def stats(pts, label):
    if not pts:
        print("  %-12s EMPTY" % label); return None
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    print("  %-12s n=%4d x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f] cen=(%.3f,%.3f,%.3f)" % (
        label, len(pts), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
        sum(xs)/len(xs), sum(ys)/len(ys), sum(zs)/len(zs)))
    return (sum(xs)/len(pts), sum(ys)/len(pts), sum(zs)/len(pts))

print("=== FEET (z<0.03, split x/y) ===")
feet = [p for p in vs if p.z < 0.03]
FL = [p for p in feet if p.y < 0 and p.x > 0]
FR = [p for p in feet if p.y < 0 and p.x < 0]
BL = [p for p in feet if p.y > 0 and p.x > 0]
BR = [p for p in feet if p.y > 0 and p.x < 0]
fFL = stats(FL, "front.L paw"); fFR = stats(FR, "front.R paw")
fBL = stats(BL, "back.L paw"); fBR = stats(BR, "back.R paw")

print("=== HEAD (z>0.24) ===")
head = [p for p in vs if p.z > 0.24]
stats(head, "head")
# 鼻子 = 最前点
nose = min(vs, key=lambda p: p.y)
print("  nose point=(%.3f,%.3f,%.3f)" % (nose.x, nose.y, nose.z))
# 耳尖 = 最高点
top = max(vs, key=lambda p: p.z)
print("  ear top=(%.3f,%.3f,%.3f)" % (top.x, top.y, top.z))

print("=== TORSO CENTROID PER Y SLICE (exclude legs: z>0.09) ===")
for i in range(20):
    y0 = -0.26 + 0.52 * i / 20.0
    y1 = y0 + 0.026
    sel = [p for p in vs if y0 <= p.y < y1 and p.z > 0.09]
    if not sel:
        print("  y[%.3f,%.3f] EMPTY" % (y0, y1)); continue
    zs = [p.z for p in sel]; xs = [p.x for p in sel]
    print("  y[%.3f,%.3f] n=%4d z[%.3f,%.3f] zc=%.3f w=%.3f" % (
        y0, y1, len(sel), min(zs), max(zs), sum(zs)/len(sel), max(xs)-min(xs)))

print("=== LEG COLUMNS (front region y in [-0.20,-0.09]) ===")
for z0 in [0.02, 0.05, 0.08, 0.11, 0.14, 0.17]:
    sel = [p for p in vs if z0 <= p.z < z0+0.025 and p.y < -0.05 and abs(p.x) > 0.012]
    if sel:
        ly = [p.y for p in sel]
        print("  z=%.2f n=%4d y[%.3f,%.3f] yc=%.3f" % (z0, len(sel), min(ly), max(ly), sum(ly)/len(ly)))
print("=== LEG COLUMNS (rear region y in [0.05,0.22]) ===")
for z0 in [0.02, 0.05, 0.08, 0.11, 0.14, 0.17]:
    sel = [p for p in vs if z0 <= p.z < z0+0.025 and p.y > 0.05 and abs(p.x) > 0.012]
    if sel:
        ly = [p.y for p in sel]
        print("  z=%.2f n=%4d y[%.3f,%.3f] yc=%.3f" % (z0, len(sel), min(ly), max(ly), sum(ly)/len(ly)))

print("=== TAIL (y>0.16) ===")
tailpts = [p for p in vs if p.y > 0.16]
stats(tailpts, "tail")
for i in range(6):
    y0 = 0.16 + 0.10 * i / 6.0
    sel = [p for p in vs if y0 <= p.y < y0 + 0.017]
    if sel:
        zs = [p.z for p in sel]
        print("  y[%.3f,%.3f] n=%4d z[%.3f,%.3f] zc=%.3f" % (y0, y0+0.017, len(sel), min(zs), max(zs), sum(zs)/len(sel)))
print("MEASURE_DONE")
