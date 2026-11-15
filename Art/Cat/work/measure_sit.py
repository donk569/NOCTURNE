# 精确测量坐姿猫解剖关键点
import bpy, mathutils

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_sit_rig.blend")
mesh = None
for o in bpy.data.objects:
    if o.type == 'MESH':
        mesh = o; break
mw = mesh.matrix_world
vs = [mw @ v.co for v in mesh.data.vertices]

print("=== FRONT LEG COLUMN (x>0.008, y<-0.03) ===")
for z0 in [0.01, 0.04, 0.07, 0.10, 0.13, 0.16, 0.19]:
    sel = [p for p in vs if z0 <= p.z < z0+0.025 and p.y < -0.03 and p.x > 0.005]
    if sel:
        ly = [p.y for p in sel]; lx = [p.x for p in sel]
        print("  z=%.2f n=%4d y[%.3f,%.3f] yc=%.3f x[%.3f,%.3f] xc=%.3f" % (
            z0, len(sel), min(ly), max(ly), sum(ly)/len(ly), min(lx), max(lx), sum(lx)/len(lx)))

print("=== FRONT PAW (z<0.03, y<0) ===")
paw = [p for p in vs if p.z < 0.03 and p.y < 0 and abs(p.x) > 0.005]
if paw:
    xs = [p.x for p in paw]; ys = [p.y for p in paw]; zs = [p.z for p in paw]
    print("  n=%d x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (len(paw), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    L = [p for p in paw if p.x > 0]; R = [p for p in paw if p.x < 0]
    for nm, cl in (("L", L), ("R", R)):
        if cl:
            print("   %s cen=(%.3f,%.3f,%.3f) n=%d" % (nm, sum(p.x for p in cl)/len(cl), sum(p.y for p in cl)/len(cl), sum(p.z for p in cl)/len(cl), len(cl)))

print("=== HEAD (z>0.25) ===")
head = [p for p in vs if p.z > 0.25]
if head:
    print("  n=%d x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f] cen=(%.3f,%.3f,%.3f)" % (
        len(head), min(p.x for p in head), max(p.x for p in head), min(p.y for p in head), max(p.y for p in head),
        min(p.z for p in head), max(p.z for p in head), sum(p.x for p in head)/len(head), sum(p.y for p in head)/len(head), sum(p.z for p in head)/len(head)))
top = max(vs, key=lambda p: p.z)
nose = min(vs, key=lambda p: p.y)
print("  ear_top=(%.3f,%.3f,%.3f) nose=(%.3f,%.3f,%.3f)" % (top.x, top.y, top.z, nose.x, nose.y, nose.z))

print("=== REAR HAUNCH (y>0.02, z>0.02) ===")
for z0 in [0.02, 0.06, 0.10, 0.14, 0.18]:
    sel = [p for p in vs if z0 <= p.z < z0+0.03 and p.y > 0.02]
    if sel:
        print("  z=%.2f n=%4d y[%.3f,%.3f] yc=%.3f xmax=%.3f" % (
            z0, len(sel), min(p.y for p in sel), max(p.y for p in sel), sum(p.y for p in sel)/len(sel), max(abs(p.x) for p in sel)))

print("=== TAIL (y>0.09, z<0.09) ===")
tail = [p for p in vs if p.y > 0.09 and p.z < 0.09]
if tail:
    print("  n=%d x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f] cen=(%.3f,%.3f,%.3f)" % (
        len(tail), min(p.x for p in tail), max(p.x for p in tail), min(p.y for p in tail), max(p.y for p in tail),
        min(p.z for p in tail), max(p.z for p in tail), sum(p.x for p in tail)/len(tail), sum(p.y for p in tail)/len(tail), sum(p.z for p in tail)/len(tail)))
for y0 in [0.08, 0.10, 0.12, 0.14]:
    sel = [p for p in vs if y0 <= p.y < y0+0.02 and p.z < 0.10]
    if sel:
        print("  y=%.2f n=%4d x[%.3f,%.3f] xc=%.3f z[%.3f,%.3f]" % (
            y0, len(sel), min(p.x for p in sel), max(p.x for p in sel), sum(p.x for p in sel)/len(sel), min(p.z for p in sel), max(p.z for p in sel)))
print("MEASURE_DONE")
