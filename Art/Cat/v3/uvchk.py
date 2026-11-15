import numpy as np
import bpy

bpy.ops.wm.open_mainfile(filepath=r"D:\unity\NOCTURNE\Art\Cat\v3\out\base_dec.blend")
ob = bpy.data.objects["CatBody"]
me = ob.data
uv = np.array([d.uv[:] for d in me.uv_layers.active.data], dtype=np.float32)
print("[UV] min", uv.min(0), "max", uv.max(0))
print("[UV] frac loops outside 0..1:", round(float(((uv < 0) | (uv > 1)).any(axis=1).mean()), 4))

bad = 0
tot = 0
for p in me.polygons:
    vs = [me.vertices[me.loops[li].vertex_index].co for li in p.loop_indices]
    a3 = 0.0
    for i in range(1, len(vs) - 1):
        a3 += (vs[i] - vs[0]).cross(vs[i + 1] - vs[0]).length * 0.5
    pts = [me.uv_layers.active.data[li].uv for li in p.loop_indices]
    au = 0.0
    for i in range(1, len(pts) - 1):
        au += abs((pts[i] - pts[0]).cross(pts[i + 1] - pts[0])) * 0.5
    tot += 1
    if a3 > 1e-9 and au / a3 < 0.004:
        bad += 1
print("[UV] tris with tiny uv/3d ratio:", bad, "of", tot)
print("[UV] texel density span:", round(float(au / a3), 4))
