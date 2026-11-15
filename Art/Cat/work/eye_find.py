import bpy, mathutils, math
from mathutils.bvhtree import BVHTree

bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
rig  = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
if rig.animation_data: rig.animation_data.action = None
for b in rig.pose.bones:
    b.rotation_euler=(0,0,0); b.location=(0,0,0)
bpy.context.view_layer.update()

me = mesh.data
mw = mesh.matrix_world
verts = [mw @ v.co for v in me.vertices]
polys = [tuple(p.vertices) for p in me.polygons]
bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False, epsilon=0.0)

D = mathutils.Vector((0,1,0))       # 从前方（-Y 侧）向 +Y 打
print("从正前方扫描面部表面点：")
for z in (0.230, 0.240, 0.248, 0.256, 0.264):
    row = []
    for x in (0.000, 0.012, 0.020, 0.026, 0.032):
        o = mathutils.Vector((x, -1.0, z))
        hit = bvh.ray_cast(o, D, 2.0)
        if hit[0] is None:
            row.append("x%.3f:--" % x)
        else:
            p, n = hit[0], hit[1]
            row.append("x%.3f:y%.3f" % (x, p.y))
    print("  z=%.3f  %s" % (z, "  ".join(row)))

# 头部包围盒（头部区域）
head_pts = [v for v in verts if v.y < -0.19 and v.z > 0.20]
if head_pts:
    xs=[p.x for p in head_pts]; ys=[p.y for p in head_pts]; zs=[p.z for p in head_pts]
    print("HEAD_BBOX x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (min(xs),max(xs),min(ys),max(ys),min(zs),max(zs)))
