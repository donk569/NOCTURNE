import bpy, math
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
rig  = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
me = mesh.data
uvl = me.uv_layers.active.data
W = 4096.0
EYES = [(1015.0, 3773.0, 72.0, 37.0), (1465.0, 3855.0, 68.0, 42.0)]
head = rig.data.bones["head"]
print("head rest head=", tuple(round(v,3) for v in head.head_local), " tail=", tuple(round(v,3) for v in head.tail_local))

# 顶点 -> 世界坐标（取每个 loop 的 uv 与对应顶点）
hits = {0: [], 1: []}
alluv = [(uvl[i].uv.x*W, uvl[i].uv.y*W, me.loops[i].vertex_index) for i in range(len(me.loops))]
for i,(cx,cy,rx,ry) in enumerate(EYES):
    for u,v,vi in alluv:
        if ((u-cx)/rx)**2 + ((v-cy)/ry)**2 <= 1.0:
            hits[i].append(vi)
for i in (0,1):
    idxs = list(set(hits[i]))
    if not idxs:
        print("eye", i, "NO VERTICES"); continue
    pts = [mesh.matrix_world @ me.vertices[k].co for k in idxs]
    xs=[p.x for p in pts]; ys=[p.y for p in pts]; zs=[p.z for p in pts]
    print("eye %d: verts=%d  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]  centroid=(%.3f,%.3f,%.3f)" % (
        i, len(idxs), min(xs),max(xs), min(ys),max(ys), min(zs),max(zs),
        sum(xs)/len(xs), sum(ys)/len(ys), sum(zs)/len(zs)))
