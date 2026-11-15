import bpy, json
bpy.ops.wm.open_mainfile(filepath=r"D:/unity/NOCTURNE/Art/Cat/work/cat_stand_8.blend")
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]
me = mesh.data
uvl = me.uv_layers.active.data
mw = mesh.matrix_world
print("uv layers:", [l.name for l in me.uv_layers])

# 头部区域：y < -0.18 且 z > 0.19
polys = []
for p in me.polygons:
    c = mw @ p.center
    if c.y < -0.18 and c.z > 0.19:
        uvs = [(uvl[li].uv.x, uvl[li].uv.y) for li in p.loop_indices]
        polys.append(uvs)
print("head polys:", len(polys))
json.dump(polys, open(r"D:/unity/NOCTURNE/Art/Cat/work/head_uv.json","w"))
